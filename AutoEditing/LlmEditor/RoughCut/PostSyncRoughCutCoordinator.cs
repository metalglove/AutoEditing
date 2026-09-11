using System.Security.Cryptography;
using System.Text;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.LlmEditor.Assembly;
using AutoEditing.LlmEditor.Automation;
using AutoEditing.LlmEditor.Workbench;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.RoughCut;

internal sealed class RoughCutWorkflowResult
{
	public required EditPlanDocument AcceptedPlan { get; init; }
	public required RoughCutAuditReport Report { get; init; }
	public required AcceptedRoughCutMilestone Milestone { get; init; }
	public required CandidateMaterializationBaseline AcceptedBaseline { get; init; }
}

internal sealed class PostSyncRoughCutCoordinator
{
	private static readonly TimeSpan ActionPollInterval =
		TimeSpan.FromMilliseconds(250);

	private readonly string sessionId;
	private readonly string sessionRoot;
	private readonly IVegasAutomationClient automation;
	private readonly WorkbenchSessionPublisher publisher;
	private readonly RoughCutFullRenderService fullRender;
	private readonly RoughCutEvidenceCaptureService evidenceCapture;
	private readonly RoughCutAuditService audit;
	private readonly RoughCutAuditArtifactStore reports;
	private readonly RoughCutWorkflowRecoveryStore recovery;
	private readonly AssemblyArtifactStore assemblyArtifacts;
	private readonly AssemblyActionStore actions;

	public PostSyncRoughCutCoordinator(
		string sessionId,
		string sessionRoot,
		IVegasAutomationClient automation,
		WorkbenchSessionPublisher publisher,
		RoughCutAuditService audit)
	{
		this.sessionId = string.IsNullOrWhiteSpace(sessionId)
			? throw new ArgumentException("A session ID is required.", nameof(sessionId))
			: sessionId;
		this.sessionRoot = Path.GetFullPath(
			string.IsNullOrWhiteSpace(sessionRoot)
				? throw new ArgumentException("A session root is required.", nameof(sessionRoot))
				: sessionRoot);
		this.automation = automation ?? throw new ArgumentNullException(nameof(automation));
		this.publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
		this.audit = audit ?? throw new ArgumentNullException(nameof(audit));
		fullRender = new RoughCutFullRenderService(
			sessionId,
			this.sessionRoot,
			new VegasRoughCutChunkRenderer(automation));
		evidenceCapture = new RoughCutEvidenceCaptureService(
			this.sessionRoot, automation);
		reports = new RoughCutAuditArtifactStore(this.sessionRoot);
		recovery = new RoughCutWorkflowRecoveryStore(this.sessionRoot);
		assemblyArtifacts = new AssemblyArtifactStore(this.sessionRoot);
		actions = new AssemblyActionStore(this.sessionRoot);
	}

	public async Task<RoughCutWorkflowResult> RunAsync(
		EditPlanningRequest request,
		AssemblySketch sketch,
		EditPlanDocument acceptedPlan,
		CandidateWorkspaceId workspace,
		CancellationToken cancellationToken,
		AssemblyRuntimeLease? runtimeLease = null)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(sketch);
		ArgumentNullException.ThrowIfNull(acceptedPlan);
		workspace?.Validate();
		if (workspace == null)
			throw new ArgumentNullException(nameof(workspace));
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		ProgressiveAssemblyContractValidator.Validate(sketch);
		EditPlanDocumentValidator.ValidateAndNormalize(acceptedPlan);
		using AssemblyRuntimeLease? ownedLease = runtimeLease == null
			? AssemblyRuntimeLease.Acquire(sessionRoot, sessionId)
			: null;
		runtimeLease ??= ownedLease;

		RoughCutAuditReport? current = reports.ReadCurrentReport();
		AssemblySessionState? persistedState = actions.ReadState();
		string acceptedHash = PlanSha256(acceptedPlan);
		RoughCutWorkflowContext? context = recovery.ReadContext();
		if (context != null &&
			(!string.Equals(context.SessionId, sessionId, StringComparison.Ordinal) ||
			 !string.Equals(
				 context.PlanSha256, acceptedHash, StringComparison.OrdinalIgnoreCase)))
		{
			CompletePersistedCorrectionAdoption(
				context,
				current,
				acceptedHash);
			// A correction produces a new accepted-plan hash. Its new render/audit
			// supersedes the prior review context rather than resuming it.
			context = null;
		}
		if (persistedState?.Phase == AssemblyPhase.Abandoned)
			await ReconcileAbandonedAsync(
				persistedState,
				workspace,
				acceptedHash,
				current?.ReportId ?? context?.ReportId ?? "",
				cancellationToken);
		if (current != null &&
			string.Equals(
				current.PlanSha256, acceptedHash, StringComparison.OrdinalIgnoreCase))
		{
			AcceptedRoughCutMilestone? accepted =
				reports.ReadAcceptedMilestone(current.ReportId);
			if (accepted != null)
			{
				CandidateMaterializationBaseline acceptedBaseline =
					assemblyArtifacts.ReadRoughCutBaseline(
						"accepted",
						acceptedPlan.Montage.Placements.Count)
					?? throw new InvalidDataException(
						"The accepted rough cut has no exact timeline baseline. " +
						"Reopen and accept the rough cut before polishing.");
				RoughCutOperationStart? pendingAcceptance =
					recovery.ReadPending(
						sessionId,
						acceptedHash,
						current.ReportId);
				if (pendingAcceptance?.Action.Kind ==
					AssemblyActionKind.AcceptRoughCut)
					recovery.Complete(
						pendingAcceptance.Action,
						"rough-cut-accepted-recovered");
				PublishState(
					AssemblyPhase.RoughCutAccepted,
					workspace,
					"Recovered the durable accepted rough-cut milestone.",
					acceptedHash,
					current.ReportId);
				if (publisher.State != EditSessionState.Polishing)
					publisher.TransitionTo(
						EditSessionState.Polishing,
						"Recovered the accepted synchronization-only rough cut.");
				return new RoughCutWorkflowResult
				{
					AcceptedPlan = acceptedPlan,
					Report = current,
					Milestone = accepted,
					AcceptedBaseline = acceptedBaseline
				};
			}
		}
		bool currentEvidenceValid = current != null &&
			string.Equals(
				current.PlanSha256, acceptedHash, StringComparison.OrdinalIgnoreCase) &&
			reports.IsFullRenderEvidenceValid(current);
		if (current != null &&
			string.Equals(
				current.PlanSha256, acceptedHash, StringComparison.OrdinalIgnoreCase) &&
			currentEvidenceValid &&
			(persistedState?.Phase is
				AssemblyPhase.RoughCutReview or
				AssemblyPhase.RoughCutCorrection ||
				persistedState?.Phase == AssemblyPhase.RoughCutAuditing ||
			 context?.Phase == AssemblyPhase.Paused))
		{
			AssemblySessionState reviewState = persistedState ??
				throw new InvalidDataException(
					"The rough-cut report has no durable assembly state.");
			if (context?.Phase == AssemblyPhase.Paused)
			{
				if (publisher.State != EditSessionState.Paused)
					publisher.TransitionTo(
						EditSessionState.Paused,
						"Recovered a paused rough-cut review.");
				reviewState = await ResumePausedAsync(
					reviewState,
					context,
					current,
					acceptedHash,
					cancellationToken);
			}
			else if (publisher.State != EditSessionState.AwaitingUser)
			{
				publisher.TransitionTo(
					EditSessionState.AwaitingUser,
					"Resumed durable rough-cut review.");
			}
			return await ReviewAsync(
				request,
				sketch,
				acceptedPlan,
				workspace,
				current,
				persistedState,
				cancellationToken,
				runtimeLease);
		}

		return await RenderAuditAndReviewAsync(
			request,
			sketch,
			acceptedPlan,
			workspace,
			cancellationToken,
			runtimeLease);
	}

	private async Task<RoughCutWorkflowResult> RenderAuditAndReviewAsync(
		EditPlanningRequest request,
		AssemblySketch sketch,
		EditPlanDocument acceptedPlan,
		CandidateWorkspaceId workspace,
		CancellationToken cancellationToken,
		AssemblyRuntimeLease? runtimeLease)
	{
		if (publisher.State != EditSessionState.Rendering)
			publisher.TransitionTo(
				EditSessionState.Rendering,
				"Rendering the complete synchronization-only rough cut.");
		AssemblySessionState state = PublishState(
			AssemblyPhase.RoughCutRendering,
			workspace,
			"Rendering the complete synchronized montage in resumable chunks.",
			PlanSha256(acceptedPlan));

		CandidateTimelineSnapshot timeline =
			await automation.ExecuteAsync<
				GetCandidateSnapshotRequest,
				CandidateTimelineSnapshot>(
				VegasOperations.GetCandidateSnapshot,
				new GetCandidateSnapshotRequest { Workspace = workspace },
				"rough-cut-snapshot-" + PlanSha256(acceptedPlan)[..16],
				cancellationToken: cancellationToken);
		string planSha256 = PlanSha256(acceptedPlan);
		EnsureTimelineExactlyMatchesAcceptedPlan(acceptedPlan, timeline);
		assemblyArtifacts.SaveRoughCutBaseline(
			"review",
			acceptedPlan.Montage.Placements.Count,
			planSha256,
			timeline);
		(RoughCutRenderManifest _, RoughCutEvidenceReference renderEvidence) =
			await fullRender.RenderAsync(
				timeline, planSha256, cancellationToken);
		IReadOnlyList<RoughCutEvidenceReference> visualEvidence =
			await evidenceCapture.CaptureAsync(
				timeline,
				acceptedPlan,
				request,
				planSha256,
				cancellationToken);

		state = PublishState(
			AssemblyPhase.RoughCutAuditing,
			workspace,
			"Auditing the full rough cut for pacing, continuity, repetition, gaps, and musical coverage.",
			planSha256);
		publisher.TransitionTo(
			EditSessionState.Reviewing,
			"Running deterministic and multimodal rough-cut review.");

		RoughCutAuditReport report = await audit.RunAsync(
			sessionRoot,
			new RoughCutAuditInput
			{
				SessionId = sessionId,
				PlanningRequest = request,
				AcceptedSyncPlan = acceptedPlan,
				Sketch = sketch,
				Timeline = timeline,
				Evidence = new[] { renderEvidence }
					.Concat(visualEvidence)
					.ToArray()
			},
			cancellationToken);
		reports.SaveReport(report);
		publisher.TransitionTo(
			EditSessionState.AwaitingUser,
			"Rough-cut audit complete; correction decisions require human review.");
		state = PublishState(
			AssemblyPhase.RoughCutReview,
			workspace,
			ReviewStatus(report),
			planSha256,
			report.ReportId);
		return await ReviewAsync(
			request,
			sketch,
			acceptedPlan,
			workspace,
			report,
			state,
			cancellationToken,
			runtimeLease);
	}

	private async Task<RoughCutWorkflowResult> ReviewAsync(
		EditPlanningRequest request,
		AssemblySketch sketch,
		EditPlanDocument acceptedPlan,
		CandidateWorkspaceId workspace,
		RoughCutAuditReport report,
		AssemblySessionState state,
		CancellationToken cancellationToken,
		AssemblyRuntimeLease? runtimeLease)
	{
		string planSha256 = PlanSha256(acceptedPlan);
		while (true)
		{
			AssemblyAction action = await NextActionAsync(
				state,
				report,
				planSha256,
				cancellationToken);
			if (action.Kind == AssemblyActionKind.PauseSession)
			{
				state = await PauseAsync(
					state,
					report,
					planSha256,
					action,
					cancellationToken);
				continue;
			}
			if (action.Kind == AssemblyActionKind.AbandonSession)
			{
				await CandidateAbandonCleanup.CleanupAsync(
					automation,
					workspace,
					action.ActionId,
					cancellationToken);
				state = PublishState(
					AssemblyPhase.Abandoned,
					workspace,
					"Rough-cut review abandoned; durable artifacts were retained.",
					planSha256,
					report.ReportId);
				recovery.Complete(action, "abandoned");
				publisher.TransitionTo(
					EditSessionState.Cancelled,
					"Rough-cut review was explicitly abandoned.");
				throw new AssemblySessionAbandonedException(
					"AI editing session was abandoned during rough-cut review.");
			}

			if (action.Kind == AssemblyActionKind.RejectRoughCutCorrection)
			{
				if (!TryGetCorrection(report, action.TargetId, out _))
				{
					state = PublishState(
						state.Phase,
						workspace,
						"Correction rejection ignored: select a current correction.",
						planSha256,
						report.ReportId);
					recovery.Complete(action, "ignored-invalid-correction");
					continue;
				}
				reports.SaveCorrectionDecision(Decision(
					report,
					action.TargetId,
					RoughCutCorrectionDisposition.Rejected,
					action.Instruction,
					action.CreatedUtc));
				state = PublishState(
					AssemblyPhase.RoughCutReview,
					workspace,
					ReviewStatus(report),
					planSha256,
					report.ReportId);
				recovery.Complete(action, "correction-rejected");
				continue;
			}

			if (action.Kind == AssemblyActionKind.ApproveRoughCutCorrection)
			{
				if (!TryGetCorrection(report, action.TargetId, out _))
				{
					state = PublishState(
						state.Phase,
						workspace,
						"Correction approval ignored: select a current correction.",
						planSha256,
						report.ReportId);
					recovery.Complete(action, "ignored-invalid-correction");
					continue;
				}
				reports.SaveCorrectionDecision(Decision(
					report,
					action.TargetId,
					RoughCutCorrectionDisposition.ApprovedForReopen,
					action.Instruction,
					action.CreatedUtc));
				RoughCutCheckpointReopenRequest reopen =
					reports.CreateApprovedReopenRequest(
						report.ReportId, action.TargetId);
				state = PublishState(
					AssemblyPhase.RoughCutCorrection,
					workspace,
					"Adjust only checkpoint(s) " +
					string.Join(", ", reopen.TargetCheckpoints) +
					" in VEGAS, then apply the approved correction. " +
					reopen.ScopedInstruction,
					planSha256,
					report.ReportId,
					action.TargetId);
				recovery.Complete(action, "correction-approved");
				continue;
			}

			if (action.Kind == AssemblyActionKind.ApplyRoughCutCorrection)
			{
				RoughCutWorkflowContext? correctionContext =
					recovery.ReadContext();
				if (state.Phase != AssemblyPhase.RoughCutCorrection ||
					!string.Equals(
						correctionContext?.ActiveCorrectionId,
						action.TargetId,
						StringComparison.Ordinal) ||
					!TryGetCorrection(report, action.TargetId, out
						RoughCutCorrectionProposal? correction))
				{
					state = PublishState(
						state.Phase,
						workspace,
						"Correction application ignored: approve a current correction first.",
						planSha256,
						report.ReportId);
					recovery.Complete(action, "ignored-no-active-correction");
					continue;
				}
				RoughCutCorrectionDecision? approval =
					reports.ReadLatestCorrectionDecisions(report.ReportId)
						.SingleOrDefault(item =>
							string.Equals(
								item.CorrectionId,
								action.TargetId,
								StringComparison.Ordinal) &&
							item.Disposition ==
								RoughCutCorrectionDisposition.ApprovedForReopen);
				if (approval == null)
				{
					state = PublishState(
						state.Phase,
						workspace,
						"Correction application ignored: its approval is no longer current.",
						planSha256,
						report.ReportId,
						action.TargetId);
					recovery.Complete(action, "ignored-no-current-approval");
					continue;
				}

				try
				{
					CandidateTimelineSnapshot actual =
						await automation.ExecuteAsync<
							GetCandidateSnapshotRequest,
							CandidateTimelineSnapshot>(
							VegasOperations.GetCandidateSnapshot,
							new GetCandidateSnapshotRequest { Workspace = workspace },
							"rough-cut-correction-" + report.ReportId + "-" +
								action.TargetId + "-" + state.StateRevision,
							cancellationToken: cancellationToken);
					EditPlanDocument corrected = Clone(acceptedPlan);
					TimelineAdjustmentDelta delta =
						AssemblyTimelineReconciler.Apply(
							corrected, actual, corrected.Montage.Placements.Count);
					ValidateCorrectionScope(
						acceptedPlan, correction!, delta);
					assemblyArtifacts.SaveAcceptedPlan(
						corrected.Montage.Placements.Count, corrected);
					reports.SaveCorrectionDecision(Decision(
						report,
						action.TargetId,
						RoughCutCorrectionDisposition.Applied,
						action.Instruction,
						action.CreatedUtc));
					recovery.Complete(action, "correction-applied");
					return await RenderAuditAndReviewAsync(
						request,
						sketch,
						corrected,
						workspace,
						cancellationToken,
						runtimeLease);
				}
				catch (Exception exception) when (
					exception is InvalidOperationException or
						InvalidDataException or ArgumentException)
				{
					state = PublishState(
						AssemblyPhase.RoughCutCorrection,
						workspace,
						"VEGAS correction was not adopted: " +
						exception.GetBaseException().Message +
						" Adjust only the approved checkpoint scope and try again.",
						planSha256,
						report.ReportId,
						action.TargetId);
					recovery.Complete(action, "correction-not-adopted");
					continue;
				}
			}

			if (action.Kind == AssemblyActionKind.AcceptRoughCut)
			{
				if (!reports.IsFullRenderEvidenceValid(report))
				{
					if (publisher.State != EditSessionState.Rendering)
						publisher.TransitionTo(
							EditSessionState.Rendering,
							"Repairing invalidated rough-cut render evidence before acceptance.");
					PublishState(
						AssemblyPhase.RoughCutRendering,
						workspace,
						"Rough-cut acceptance is withheld while missing or corrupt render evidence is rebuilt.",
						planSha256);
					recovery.Complete(
						action,
						"acceptance-triggered-render-repair");
					return await RenderAuditAndReviewAsync(
						request,
						sketch,
						acceptedPlan,
						workspace,
						cancellationToken,
						runtimeLease);
				}
				IReadOnlyList<RoughCutCorrectionDecision> decisions =
					reports.ReadLatestCorrectionDecisions(report.ReportId);
				HashSet<string> terminal = decisions
					.Where(item =>
						item.Disposition is RoughCutCorrectionDisposition.Applied or
							RoughCutCorrectionDisposition.Rejected)
					.Select(item => item.CorrectionId)
					.ToHashSet(StringComparer.Ordinal);
				if (report.Corrections.Any(item =>
					!terminal.Contains(item.CorrectionId)))
				{
					state = PublishState(
						AssemblyPhase.RoughCutReview,
						workspace,
						"Resolve every proposed correction by applying or rejecting it before accepting the rough cut.",
						planSha256,
						report.ReportId);
					recovery.Complete(action, "acceptance-blocked-unresolved");
					continue;
				}
				CandidateTimelineSnapshot acceptedSnapshot =
					await automation.ExecuteAsync<
						GetCandidateSnapshotRequest,
						CandidateTimelineSnapshot>(
						VegasOperations.GetCandidateSnapshot,
						new GetCandidateSnapshotRequest { Workspace = workspace },
						"rough-cut-accept-snapshot-" + report.ReportId,
						cancellationToken: cancellationToken);
				CandidateMaterializationBaseline reviewBaseline =
					assemblyArtifacts.ReadRoughCutBaseline(
						"review",
						acceptedPlan.Montage.Placements.Count)
					?? throw new InvalidDataException(
						"The rough-cut review baseline is missing.");
				EditPlanDocument acceptanceCheck = Clone(acceptedPlan);
				TimelineAdjustmentDelta acceptanceDelta =
					AssemblyTimelineReconciler.Apply(
						acceptanceCheck,
						acceptedSnapshot,
						acceptedPlan.Montage.Placements.Count,
						reviewBaseline);
				if (acceptanceDelta.Changes.Count > 0)
					throw new InvalidOperationException(
						"The VEGAS timeline changed after the reviewed rough-cut " +
						"render. Render and review the updated rough cut before accepting.");
				CandidateMaterializationBaseline acceptedBaseline =
					assemblyArtifacts.SaveRoughCutBaseline(
						"accepted",
						acceptedPlan.Montage.Placements.Count,
						planSha256,
						acceptedSnapshot);
				AcceptedRoughCutMilestone milestone =
					reports.AcceptMilestone(report.ReportId, "workbench-user");
				state = PublishState(
					AssemblyPhase.RoughCutAccepted,
					workspace,
					"Rough cut accepted. Effects and audio remain separate downstream passes.",
					planSha256,
					report.ReportId);
				recovery.Complete(action, "rough-cut-accepted");
				publisher.TransitionTo(
					EditSessionState.Polishing,
					"The synchronization-only rough cut was accepted; effects and audio remain separate approval-gated passes.");
				return new RoughCutWorkflowResult
				{
					AcceptedPlan = acceptedPlan,
					Report = report,
					Milestone = milestone,
					AcceptedBaseline = acceptedBaseline
				};
			}

			state = PublishState(
				state.Phase,
				workspace,
				"That action is not available during rough-cut review.",
				planSha256,
				report.ReportId);
			recovery.Complete(action, "ignored-unavailable");
		}
	}

	private async Task<AssemblySessionState> PauseAsync(
		AssemblySessionState previous,
		RoughCutAuditReport report,
		string planSha256,
		AssemblyAction pauseAction,
		CancellationToken cancellationToken)
	{
		publisher.TransitionTo(
			EditSessionState.Paused,
			"Rough-cut review paused at a durable boundary.");
		AssemblySessionState paused = PublishState(
			AssemblyPhase.Paused,
			previous.Workspace,
			"Rough-cut review paused. Resume or abandon the session.",
			planSha256,
			report.ReportId,
			recovery.ReadContext()?.ActiveCorrectionId ?? "",
			previous.Phase);
		recovery.Complete(pauseAction, "paused");
		return await ResumePausedAsync(
			paused,
			recovery.ReadContext() ??
				throw new InvalidDataException(
					"The paused rough-cut context was not persisted."),
			report,
			planSha256,
			cancellationToken);
	}

	private async Task<AssemblySessionState> ResumePausedAsync(
		AssemblySessionState paused,
		RoughCutWorkflowContext context,
		RoughCutAuditReport report,
		string planSha256,
		CancellationToken cancellationToken)
	{
		AssemblyPhase resumePhase = context.PhaseBeforePause;
		RoughCutOperationStart? pendingPause = recovery.ReadPending(
			sessionId, planSha256, report.ReportId);
		if (pendingPause?.Action.Kind == AssemblyActionKind.PauseSession)
			recovery.Complete(
				pendingPause.Action,
				"paused-recovered");
		while (true)
		{
			AssemblyAction action = await NextActionAsync(
				paused,
				report,
				planSha256,
				cancellationToken);
			if (action.Kind == AssemblyActionKind.ResumeSession)
			{
				publisher.TransitionTo(
					EditSessionState.AwaitingUser,
					"Rough-cut review resumed.");
				AssemblySessionState resumed = PublishState(
					resumePhase,
					paused.Workspace,
					"Rough-cut review resumed at its durable " +
						resumePhase + " subphase.",
					planSha256,
					report.ReportId,
					context.ActiveCorrectionId);
				recovery.Complete(action, "resumed");
				return resumed;
			}
			if (action.Kind == AssemblyActionKind.AbandonSession)
			{
				await CandidateAbandonCleanup.CleanupAsync(
					automation,
					paused.Workspace,
					action.ActionId,
					cancellationToken);
				PublishState(
					AssemblyPhase.Abandoned,
					paused.Workspace,
					"Session abandoned while paused.",
					planSha256,
					report.ReportId);
				recovery.Complete(action, "abandoned");
				publisher.TransitionTo(
					EditSessionState.Cancelled,
					"Rough-cut review was abandoned while paused.");
				throw new AssemblySessionAbandonedException(
					"AI editing session was abandoned while rough-cut review was paused.");
			}
			paused = PublishState(
				AssemblyPhase.Paused,
				paused.Workspace,
				"Only Resume or Abandon is available while paused.",
				planSha256,
				report.ReportId,
				context.ActiveCorrectionId,
				resumePhase);
			recovery.Complete(action, "ignored-while-paused");
		}
	}

	private async Task ReconcileAbandonedAsync(
		AssemblySessionState abandoned,
		CandidateWorkspaceId workspace,
		string planSha256,
		string reportId,
		CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(reportId))
		{
			RoughCutOperationStart? pending = recovery.ReadPending(
				sessionId,
				planSha256,
				reportId);
			if (pending != null)
			{
				if (pending.Action.Kind != AssemblyActionKind.AbandonSession)
					throw new InvalidDataException(
						"An abandoned rough-cut state has a non-abandon operation pending.");
				await CandidateAbandonCleanup.CleanupAsync(
					automation,
					workspace,
					pending.Action.ActionId,
					cancellationToken);
				recovery.Complete(
					pending.Action,
					"abandoned-recovered");
			}
		}
		if (publisher.State != EditSessionState.Cancelled)
			publisher.TransitionTo(
				EditSessionState.Cancelled,
				"Recovered a reflected rough-cut abandon action.");
		throw new AssemblySessionAbandonedException(
			"AI editing session was already abandoned during rough-cut review.");
	}

	private async Task<AssemblyAction> NextActionAsync(
		AssemblySessionState state,
		RoughCutAuditReport report,
		string planSha256,
		CancellationToken cancellationToken)
	{
		RoughCutOperationStart? pending = recovery.ReadPending(
			sessionId, planSha256, report.ReportId);
		if (pending != null) return pending.Action;
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Action<AssemblyAction> begin = candidate => recovery.Begin(
				candidate,
				state.Phase,
				planSha256,
				report.ReportId);
			AssemblyAction? action = actions.TryRecoverClaimed(
				state.Checkpoint,
				sessionId,
				state.StateRevision,
				begin);
			action ??= actions.TryConsume(
				state.Checkpoint,
				sessionId,
				state.StateRevision,
				begin);
			if (action != null) return action;
			await Task.Delay(ActionPollInterval, cancellationToken);
		}
	}

	private AssemblySessionState PublishState(
		AssemblyPhase phase,
		CandidateWorkspaceId workspace,
		string status,
		string planSha256,
		string reportId = "",
		string activeCorrectionId = "",
		AssemblyPhase phaseBeforePause = default)
	{
		AssemblySessionState? prior = actions.ReadState();
		int total = prior?.TotalClips ??
			assemblyArtifacts.ReadSessionDescriptor()?.TotalClips ?? 0;
		if (total < 1)
			throw new InvalidDataException(
				"Rough-cut review requires the assembly clip count.");
		AssemblySessionState state = new()
		{
			SessionId = sessionId,
			Phase = phase,
			Checkpoint = total,
			StateRevision = (prior?.StateRevision ?? 0) + 1,
			TotalClips = total,
			CurrentClipPath = "",
			RemainingClipPaths = Array.Empty<string>(),
			Status = status,
			Workspace = workspace
		};
		actions.PublishState(state);
		if (phase is
			AssemblyPhase.RoughCutRendering or
			AssemblyPhase.RoughCutAuditing or
			AssemblyPhase.RoughCutReview or
			AssemblyPhase.RoughCutCorrection or
			AssemblyPhase.RoughCutAccepted or
			AssemblyPhase.Paused)
		{
			recovery.SaveContext(new RoughCutWorkflowContext
			{
				SessionId = sessionId,
				PlanSha256 = planSha256,
				ReportId = reportId,
				Phase = phase,
				PhaseBeforePause = phase == AssemblyPhase.Paused
					? phaseBeforePause
					: default,
				ActiveCorrectionId = activeCorrectionId
			});
		}
		return state;
	}

	private string ReviewStatus(RoughCutAuditReport report)
	{
		IReadOnlyList<RoughCutCorrectionDecision> decisions =
			reports.ReadLatestCorrectionDecisions(report.ReportId);
		int terminal = decisions.Count(item =>
			item.Disposition is RoughCutCorrectionDisposition.Applied or
				RoughCutCorrectionDisposition.Rejected);
		return report.Summary + " Review " + report.Findings.Count +
			" finding(s) and resolve " + (report.Corrections.Count - terminal) +
			" of " + report.Corrections.Count +
			" remaining correction proposal(s).";
	}

	private void CompletePersistedCorrectionAdoption(
		RoughCutWorkflowContext priorContext,
		RoughCutAuditReport? priorReport,
		string newPlanSha256)
	{
		if (priorReport == null ||
			!string.Equals(
				priorReport.ReportId,
				priorContext.ReportId,
				StringComparison.Ordinal))
			return;
		RoughCutOperationStart? pending = recovery.ReadPending(
			sessionId,
			priorContext.PlanSha256,
			priorReport.ReportId);
		if (pending?.Action.Kind != AssemblyActionKind.ApplyRoughCutCorrection)
			return;
		if (string.Equals(
			priorContext.PlanSha256,
			newPlanSha256,
			StringComparison.OrdinalIgnoreCase))
			return;
		RoughCutCorrectionDecision? latest =
			reports.ReadLatestCorrectionDecisions(priorReport.ReportId)
				.SingleOrDefault(item =>
					string.Equals(
						item.CorrectionId,
						pending.Action.TargetId,
						StringComparison.Ordinal));
		if (latest?.Disposition == RoughCutCorrectionDisposition.ApprovedForReopen)
			reports.SaveCorrectionDecision(Decision(
				priorReport,
				pending.Action.TargetId,
				RoughCutCorrectionDisposition.Applied,
				pending.Action.Instruction,
				pending.Action.CreatedUtc));
		recovery.Complete(
			pending.Action,
			"correction-adoption-recovered");
	}

	private RoughCutCorrectionDecision Decision(
		RoughCutAuditReport report,
		string correctionId,
		RoughCutCorrectionDisposition disposition,
		string note,
		DateTimeOffset decidedUtc) =>
		new()
		{
			SessionId = sessionId,
			ReportId = report.ReportId,
			CorrectionId = correctionId,
			Disposition = disposition,
			Note = note ?? "",
			DecidedUtc = decidedUtc
		};

	private static bool TryGetCorrection(
		RoughCutAuditReport report,
		string correctionId,
		out RoughCutCorrectionProposal? correction)
	{
		correction = report.Corrections.SingleOrDefault(item =>
			string.Equals(
				item.CorrectionId, correctionId, StringComparison.Ordinal));
		return correction != null;
	}

	internal static void ValidateCorrectionScope(
		EditPlanDocument original,
		RoughCutCorrectionProposal correction,
		TimelineAdjustmentDelta delta)
	{
		if (delta.Changes.Count == 0)
			throw new InvalidOperationException(
				"The approved correction produced no measurable VEGAS timeline change.");
		List<Core.Domain.Editing.ClipPlacement> ordered =
			original.Montage.Placements
				.OrderBy(item => item.TimelineStartSeconds)
				.ToList();
		HashSet<string> allowedPaths = correction.TargetCheckpoints
			.Select(checkpoint => ordered[checkpoint - 1].Clip.FilePath)
			.Select(Path.GetFullPath)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		string[] outsideScope = delta.Changes
			.Select(change => Path.GetFullPath(change.ClipPath))
			.Where(path => !allowedPaths.Contains(path))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (outsideScope.Length > 0)
			throw new InvalidOperationException(
				"The VEGAS correction changed checkpoint(s) outside the approved scope: " +
				string.Join(", ", outsideScope.Select(Path.GetFileName)) + ".");
	}

	internal static void EnsureTimelineExactlyMatchesAcceptedPlan(
		EditPlanDocument acceptedPlan,
		CandidateTimelineSnapshot timeline)
	{
		EditPlanDocument comparison = Clone(acceptedPlan);
		TimelineAdjustmentDelta delta = AssemblyTimelineReconciler.Apply(
			comparison,
			timeline,
			comparison.Montage.Placements.Count);
		if (delta.UnsupportedChanges.Count > 0 || delta.Changes.Count > 0)
		{
			string changed = string.Join(
				", ",
				delta.Changes
					.Select(item =>
						Path.GetFileName(item.ClipPath) + " " + item.Kind)
					.Distinct(StringComparer.Ordinal));
			throw new InvalidOperationException(
				"The final VEGAS synchronization timeline no longer exactly matches " +
				"the accepted plan, so it cannot be rendered under that plan hash. " +
				"Reconcile and accept the timeline again. Changes: " +
				(string.IsNullOrWhiteSpace(changed) ? "unsupported timeline changes" : changed) +
				".");
		}
	}

	private static EditPlanDocument Clone(EditPlanDocument source) =>
		EditPlanDocumentSerializer.DeserializePlan(
			EditPlanDocumentSerializer.SerializePlan(source));

	private static string PlanSha256(EditPlanDocument plan) =>
		Convert.ToHexString(SHA256.HashData(
			new UTF8Encoding(false).GetBytes(
				EditPlanDocumentSerializer.SerializePlan(plan))))
			.ToLowerInvariant();
}
