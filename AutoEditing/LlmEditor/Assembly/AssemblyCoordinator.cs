using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.LlmEditor.Automation;
using AutoEditing.LlmEditor.Iteration;
using AutoEditing.LlmEditor.Planning;
using AutoEditing.LlmEditor.Workbench;
using AutoEditing.LlmEditor.Sessions;
using Core.Domain.Planning;
using System.Security.Cryptography;
using System.Text;

namespace AutoEditing.LlmEditor.Assembly;

internal sealed class AssemblyCoordinator
{
	private const int MaximumAutomaticProposalAttempts = 3;

	private sealed record ValidatedProposal(
		ClipStepDecision Decision,
		EditPlanDocument Plan,
		int ProposalRevision,
		bool WasRepaired);

	private readonly IIterativeEditPlanner planner;
	private readonly IProgressiveAssemblyPlanner? progressivePlanner;
	private readonly IVegasAutomationClient automation;
	private readonly WorkbenchSessionPublisher publisher;
	private readonly AssemblyActionStore actions;
	private readonly AssemblyActionExecutionStore actionExecutions;
	private readonly ICheckpointPreviewPipeline? checkpointPreviews;
	private readonly ISectionMilestoneRenderer? sectionMilestones;
	private readonly string sessionId;
	private int totalClips;
	private long stateRevision;
	private List<string> allClipPaths = new();

	public AssemblyCoordinator(
		IIterativeEditPlanner planner,
		IVegasAutomationClient automation,
		WorkbenchSessionPublisher publisher,
		string sessionId)
	{
		this.planner = planner;
		this.automation = automation;
		this.publisher = publisher;
		this.sessionId = sessionId;
		actions = new AssemblyActionStore(publisher.SessionRoot);
		actionExecutions = new AssemblyActionExecutionStore(publisher.SessionRoot);
	}

	public AssemblyCoordinator(
		IProgressiveAssemblyPlanner planner,
		IVegasAutomationClient automation,
		WorkbenchSessionPublisher publisher,
		string sessionId,
		ICheckpointPreviewPipeline? checkpointPreviews = null,
		ISectionMilestoneRenderer? sectionMilestones = null)
	{
		this.planner = null!;
		progressivePlanner = planner ??
			throw new ArgumentNullException(nameof(planner));
		this.automation = automation;
		this.publisher = publisher;
		this.sessionId = sessionId;
		this.checkpointPreviews = checkpointPreviews;
		this.sectionMilestones = sectionMilestones;
		actions = new AssemblyActionStore(publisher.SessionRoot);
		actionExecutions = new AssemblyActionExecutionStore(publisher.SessionRoot);
	}

	public async Task<EditPlanDocument> RunProgressiveAsync(
		EditPlanningRequest request,
		AssemblySketch sketch,
		CancellationToken cancellationToken,
		AssemblyRuntimeLease? runtimeLease = null)
	{
		using AssemblyRuntimeLease? ownedLease = runtimeLease == null
			? AssemblyRuntimeLease.Acquire(publisher.SessionRoot, sessionId)
			: null;
		if (progressivePlanner == null)
			throw new InvalidOperationException(
				"This assembly coordinator was not configured for progressive planning.");
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		ProgressiveAssemblyContractValidator.Validate(sketch);
		AssemblyArtifactStore artifacts = new(publisher.SessionRoot);
		AssemblySessionDescriptor? existing = artifacts.ReadSessionDescriptor();
		if (existing == null)
			artifacts.InitializeSession(sessionId, request);
		else if (!string.Equals(
			existing.RequestSha256,
			AssemblyArtifactStore.RequestSha256(request),
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The progressive request differs from the persisted session request.");
		stateRevision = actions.ReadState()?.StateRevision ?? 0;
		artifacts.SaveSketch(sketch, 1);
		totalClips = request.Clips.Count;
		allClipPaths = sketch.ClipOrder
			.OrderBy(item => item.Order)
			.Select(item => item.Clip.MediaPath)
			.ToList();
		return await ContinueProgressiveAsync(
			request,
			sketch,
			artifacts,
			startCheckpoint: 1,
			acceptedPlan: null,
			resumeProposal: null,
			reuseMaterializedWorkspace: false,
			resumeReconciliationConflict: false,
			cancellationToken);
	}

	public async Task<EditPlanDocument> ResumeProgressiveAsync(
		AssemblyResumePlan resume,
		CancellationToken cancellationToken,
		AssemblyRuntimeLease? runtimeLease = null)
	{
		using AssemblyRuntimeLease? ownedLease = runtimeLease == null
			? AssemblyRuntimeLease.Acquire(publisher.SessionRoot, sessionId)
			: null;
		ArgumentNullException.ThrowIfNull(resume);
		if (progressivePlanner == null)
			throw new InvalidOperationException(
				"This assembly coordinator was not configured for progressive planning.");
		if (resume.Summary.Disposition != AssemblyRecoveryDisposition.ReadyToResume)
			throw new InvalidOperationException(
				"Only a validated ready-to-resume assembly may be resumed.");
		EditPlanningRequestValidator.ValidateAndNormalize(resume.Request);
		AssemblySketch sketch = resume.Sketch ??
			await progressivePlanner.CreateSketchAsync(
				resume.Request, cancellationToken);
		ProgressiveAssemblyContractValidator.Validate(sketch);
		if (resume.Sketch == null)
			new AssemblyArtifactStore(publisher.SessionRoot).SaveSketch(sketch, 1);
		totalClips = resume.Request.Clips.Count;
		allClipPaths = sketch.ClipOrder
			.OrderBy(item => item.Order)
			.Select(item => item.Clip.MediaPath)
			.ToList();
		stateRevision = resume.State.StateRevision;
		publisher.TransitionTo(
			EditSessionState.NeedsRecovery,
			"Validated persisted assembly artifacts for recovery.");
		PublishRecoveryState(
			resume.Summary.Checkpoint,
			resume.AcceptedPlan,
			resume.State.Workspace,
			"Recovering the persisted progressive assembly checkpoint.");
		if (resume.Summary.FinalizeAcceptedPlan && resume.AcceptedPlan != null)
		{
			PublishState(
				AssemblyPhase.SyncPassComplete,
				totalClips,
				resume.AcceptedPlan,
				resume.State.Workspace,
				"Synchronization pass complete. Recovered the final accepted checkpoint.");
			publisher.TransitionTo(
				EditSessionState.Rendering,
				"Recovered synchronization pass; preparing full rough-cut review.");
			return resume.AcceptedPlan;
		}
		return await ContinueProgressiveAsync(
			resume.Request,
			sketch,
			new AssemblyArtifactStore(publisher.SessionRoot),
			resume.Summary.Checkpoint,
			resume.AcceptedPlan,
			resume.CurrentProposal,
			resume.Summary.ReuseMaterializedWorkspace,
			resume.State.Phase == AssemblyPhase.ReconciliationConflict,
			cancellationToken);
	}

	private async Task<EditPlanDocument> ContinueProgressiveAsync(
		EditPlanningRequest request,
		AssemblySketch sketch,
		AssemblyArtifactStore artifacts,
		int startCheckpoint,
		EditPlanDocument? acceptedPlan,
		ClipStepDecision? resumeProposal,
		bool reuseMaterializedWorkspace,
		bool resumeReconciliationConflict,
		CancellationToken cancellationToken)
	{
		IProgressiveAssemblyPlanner activePlanner = progressivePlanner ??
			throw new InvalidOperationException(
				"This assembly coordinator was not configured for progressive planning.");
		AssemblySteeringDirectiveStore steering =
			new(publisher.SessionRoot);
		if (startCheckpoint > 1 && resumeProposal == null)
		{
			int supersededCheckpoint = startCheckpoint - 1;
			await CleanupAsync(
				Workspace(supersededCheckpoint),
				supersededCheckpoint,
				cancellationToken,
				"recovery-post-accept-" +
					supersededCheckpoint.ToString(
						System.Globalization.CultureInfo.InvariantCulture));
		}
		for (int checkpoint = startCheckpoint; checkpoint <= totalClips; checkpoint++)
		{
			ClipStepDecision decision;
			int proposalRevision;
			ProgressiveAssemblyPlanningContext context =
				ProgressiveAssemblyContextFactory.Create(
					request,
					sketch,
					acceptedPlan,
					checkpoint,
					scopedInstruction: steering.DescribeApplicable(
						sessionId,
						sketch,
						checkpoint));
			bool reuseThisWorkspace =
				checkpoint == startCheckpoint &&
				reuseMaterializedWorkspace &&
				resumeProposal != null;
			if (checkpoint == startCheckpoint && resumeProposal != null)
			{
				decision = resumeProposal;
				proposalRevision = artifacts.GetLatestProposalRevision(checkpoint);
			}
			else
			{
				PublishPlanningState(checkpoint, acceptedPlan, sketch);
				decision = await activePlanner.PlanClipAsync(
					request, context, cancellationToken);
				proposalRevision = 1;
				artifacts.SaveProposal(decision, proposalRevision);
			}
			ValidatedProposal validated = await ValidateOrRepairProposalAsync(
				activePlanner,
				request,
				context,
				acceptedPlan,
				artifacts,
				checkpoint,
				decision,
				proposalRevision,
				cancellationToken);
			decision = validated.Decision;
			proposalRevision = validated.ProposalRevision;
			reuseThisWorkspace = reuseThisWorkspace && !validated.WasRepaired;
			EditPlanDocument current = validated.Plan;
			EditPlanDocument synchronizationPlan = SynchronizationPlan(current);
			CandidateWorkspaceId workspace = Workspace(checkpoint);
			if (!reuseThisWorkspace)
			{
				if (checkpoint == startCheckpoint && resumeProposal != null)
					await CleanupAsync(workspace, checkpoint, cancellationToken);
				await MaterializeAsync(
					request,
					synchronizationPlan,
					workspace,
					checkpoint,
					cancellationToken,
					(checkpoint == startCheckpoint && resumeProposal != null)
						? RecoveryMaterializationScope(checkpoint, stateRevision)
						: $"proposal-{proposalRevision:D4}");
			}

			while (true)
			{
				AssemblyAction action;
				CandidateTimelineSnapshot actual;
				EditPlanDocument? reconciledEvidence = null;
				ProgressiveConflictOutcome? conflictOutcome = null;
				if (checkpoint == startCheckpoint &&
					resumeReconciliationConflict)
				{
					resumeReconciliationConflict = false;
					actual = await SnapshotAsync(
						workspace, checkpoint, cancellationToken);
					EditPlanDocument resumedEvidence = Clone(synchronizationPlan);
					try
					{
						TimelineAdjustmentDelta resumedDelta =
							ReconcileMaterializedTimeline(
							resumedEvidence, actual, checkpoint);
						PersistAdjustment(checkpoint, resumedDelta);
						synchronizationPlan = resumedEvidence;
						current = resumedEvidence;
						continue;
					}
					catch (InvalidOperationException exception)
					{
						conflictOutcome = await ResolveProgressiveConflictAsync(
							request,
							sketch,
							artifacts,
							checkpoint,
							synchronizationPlan,
							actual,
							workspace,
							exception,
							cancellationToken);
					}
					if (!conflictOutcome.Accepted)
					{
						synchronizationPlan = conflictOutcome.Plan;
						current = conflictOutcome.Plan;
						continue;
					}
					reconciledEvidence = conflictOutcome.Plan;
					actual = conflictOutcome.Snapshot;
					action = conflictOutcome.Action;
				}
				else
				{
					publisher.TransitionTo(EditSessionState.Reviewing,
						$"Clip {checkpoint} is ready for review.");
					publisher.TransitionTo(EditSessionState.AwaitingUser,
						$"Awaiting review of clip {checkpoint}.");
					PublishState(
						AssemblyPhase.AwaitingHumanReview,
						checkpoint,
						synchronizationPlan,
						workspace,
						$"Review clip {checkpoint} of {totalClips} in VEGAS.");
					action = await WaitForReviewActionAsync(
						checkpoint,
						synchronizationPlan,
						workspace,
						cancellationToken,
						proposalRevision,
						journalProgressiveAction: true);
					actual = await ReadOrCaptureActionTimelineEvidenceAsync(
						action,
						workspace,
						checkpoint,
						cancellationToken);
				}
				if (action.Kind is
					AssemblyActionKind.CompareCurrentTimeline or
					AssemblyActionKind.RenderCheckpointPreview or
					AssemblyActionKind.ReviseCurrentClip or
					AssemblyActionKind.AcceptTimelineAndContinue or
					AssemblyActionKind.FinishSyncPass)
				{
					reconciledEvidence = Clone(synchronizationPlan);
					try
					{
						TimelineAdjustmentDelta adjustment =
							ReconcileMaterializedTimeline(
								reconciledEvidence, actual, checkpoint);
						PersistAdjustment(checkpoint, adjustment);
					}
					catch (InvalidOperationException exception)
					{
						conflictOutcome = await ResolveProgressiveConflictAsync(
							request,
							sketch,
							artifacts,
							checkpoint,
							synchronizationPlan,
							actual,
							workspace,
							exception,
							cancellationToken);
						if (!conflictOutcome.Accepted)
						{
							synchronizationPlan = conflictOutcome.Plan;
							current = conflictOutcome.Plan;
							continue;
						}
						reconciledEvidence = conflictOutcome.Plan;
						actual = conflictOutcome.Snapshot;
						action = conflictOutcome.Action;
					}
				}

				if (action.Kind == AssemblyActionKind.CompareCurrentTimeline)
				{
					actionExecutions.Complete(
						action,
						"timeline-compared",
						$"assembly/checkpoints/{checkpoint:D4}/adjustment-delta.json",
						CurrentAdjustmentSha256(checkpoint));
					PublishState(
						AssemblyPhase.ReconcilingTimeline,
						checkpoint,
						synchronizationPlan,
						workspace,
						$"Compared checkpoint {checkpoint} with VEGAS. Refreshing review evidence.");
					continue;
				}

				if (action.Kind == AssemblyActionKind.RenderCheckpointPreview)
				{
					if (checkpointPreviews == null)
						throw new InvalidOperationException(
							"Checkpoint preview rendering is not configured.");
					EditPlanDocument previewEvidence = reconciledEvidence ??
						throw new InvalidOperationException(
							"Checkpoint preview evidence was not reconciled.");
					TimelineAdjustmentDelta previewAdjustment =
						ReadPersistedAdjustment(checkpoint);
					AssemblyActionExecutionStart started =
						actionExecutions.Read(action.ActionId) ??
						throw new InvalidDataException(
							"The checkpoint preview action journal is missing.");
					CheckpointPreviewArtifact? existingPreview =
						new CheckpointPreviewArtifactStore(publisher.SessionRoot)
							.ReadCurrent(checkpoint);
					if (existingPreview != null &&
						existingPreview.Attempt > started.PreviewAttempt)
					{
						actionExecutions.Complete(
							action,
							existingPreview.Status is
								CheckpointPreviewStatus.Completed or
								CheckpointPreviewStatus.ReviewFailed
								? "checkpoint-preview-recovered"
								: "checkpoint-preview-interrupted",
							$"assembly/checkpoints/{checkpoint:D4}/previews/" +
								$"{existingPreview.Attempt:D4}/preview-artifact.json");
						continue;
					}
					PublishState(
						AssemblyPhase.RenderingCheckpoint,
						checkpoint,
						synchronizationPlan,
						workspace,
						$"Rendering checkpoint {checkpoint} preview and collecting observations.");
					publisher.TransitionTo(
						EditSessionState.Rendering,
						$"Rendering checkpoint {checkpoint} preview.");
					CheckpointPreviewArtifact completedPreview =
						await checkpointPreviews.RenderAndReviewAsync(
						checkpoint,
						sketch,
						decision,
						previewEvidence,
						actual,
						previewAdjustment,
						cancellationToken);
					if (completedPreview == null ||
						completedPreview.Checkpoint != checkpoint ||
						completedPreview.Attempt <= started.PreviewAttempt)
						throw new InvalidDataException(
							"The checkpoint preview pipeline returned an invalid " +
							"or stale artifact.");
					actionExecutions.Complete(
						action,
						"checkpoint-preview-completed",
						$"assembly/checkpoints/{checkpoint:D4}/previews/" +
							$"{completedPreview.Attempt:D4}/preview-artifact.json");
					continue;
				}

				if (action.Kind == AssemblyActionKind.ResetCurrentClip)
				{
					publisher.TransitionTo(
						EditSessionState.Revising,
						"Restoring the current AI proposal.");
					await CleanupAsync(
						workspace,
						checkpoint,
						cancellationToken,
						"reset-" + action.ActionId);
					await MaterializeAsync(
						request,
						synchronizationPlan,
						workspace,
						checkpoint,
						cancellationToken,
						"reset-" + action.ActionId);
					actionExecutions.Complete(action, "current-clip-reset");
					continue;
				}

				if (action.Kind == AssemblyActionKind.ReviseCurrentClip)
				{
					if (action.SteeringScope !=
						AssemblySteeringScope.CurrentClip)
						throw new InvalidOperationException(
							"Revise applies only to the current clip. Select the " +
							"Current clip steering scope.");
					publisher.TransitionTo(
						EditSessionState.Revising,
						"Revising the current clip from VEGAS evidence.");
					EditPlanDocument adjustedEvidence = reconciledEvidence ??
						throw new InvalidOperationException(
							"Revision evidence was not reconciled.");
					TimelineAdjustmentDelta adjustment =
						ReadPersistedAdjustment(checkpoint);
					ProgressiveAssemblyPlanningContext revisionContext =
						ProgressiveAssemblyContextFactory.Create(
							request,
							sketch,
							acceptedPlan,
							checkpoint,
							adjustment,
							action.Instruction);
					AssemblyActionExecutionStart started =
						actionExecutions.Read(action.ActionId) ??
						throw new InvalidDataException(
							"The revision action journal is missing.");
					ClipStepDecision revised;
					bool recoveredProposal =
						proposalRevision > started.ProposalRevision;
					if (recoveredProposal)
					{
						revised = artifacts.ReadCurrentProposal(checkpoint) ??
							throw new InvalidDataException(
								"The durable revised proposal is missing.");
					}
					else
					{
						revised = await activePlanner.ReviseClipAsync(
							request,
							revisionContext,
							decision,
							cancellationToken);
					}
					EditPlanDocument revisedPlan = new ClipStepDecisionCompiler()
						.Append(request, acceptedPlan, revised)
						.CombinedPlan;
					EditPlanDocumentValidator.ValidateAndNormalize(revisedPlan);
					decision = revised;
					current = revisedPlan;
					synchronizationPlan = SynchronizationPlan(current);
					if (!recoveredProposal)
					{
						proposalRevision++;
						artifacts.SaveProposal(decision, proposalRevision);
					}
					await CleanupAsync(
						workspace,
						checkpoint,
						cancellationToken,
						"revise-" + action.ActionId);
					await MaterializeAsync(
						request,
						synchronizationPlan,
						workspace,
						checkpoint,
						cancellationToken,
						"revise-" + action.ActionId);
					actionExecutions.Complete(
						action,
						recoveredProposal
							? "revision-recovered"
							: "revision-completed",
						$"assembly/checkpoints/{checkpoint:D4}/proposals/" +
							$"{proposalRevision:D4}.json");
					continue;
				}

				if (action.Kind != AssemblyActionKind.AcceptTimelineAndContinue &&
					action.Kind != AssemblyActionKind.FinishSyncPass &&
					action.Kind != AssemblyActionKind.ExcludeReconciliationClip &&
					action.Kind != AssemblyActionKind.AdoptReconciliationEvent)
				{
					actionExecutions.Complete(action, "unsupported-progressive-action");
					throw new InvalidOperationException(
						"Unsupported progressive assembly action " + action.Kind + ".");
				}

				PublishState(
					AssemblyPhase.ReconcilingTimeline,
					checkpoint,
					synchronizationPlan,
					workspace,
					"Validating and accepting the current VEGAS timeline.");
				EditPlanDocument reconciled = reconciledEvidence ??
					throw new InvalidOperationException(
						"Accepted timeline evidence was not reconciled.");
				EditPlanDocumentValidator.ValidateAndNormalize(reconciled);
				acceptedPlan = reconciled;
				int acceptedCheckpoint = acceptedPlan.Montage.Placements.Count;
				bool completesSynchronization =
					action.Kind == AssemblyActionKind.FinishSyncPass ||
					acceptedCheckpoint == totalClips;
				steering.SaveAcceptedDirection(
					action,
					sketch,
					acceptedCheckpoint);
				if (sectionMilestones != null &&
					TryCompletedSection(
						sketch,
						acceptedCheckpoint,
						out string completedSectionId))
				{
					(TimeSpan sectionStart, TimeSpan sectionEnd) =
						SectionRange(
							sketch,
							acceptedPlan,
							completedSectionId,
							acceptedCheckpoint);
					publisher.TransitionTo(
						EditSessionState.Rendering,
						"Rendering completed song section " +
							completedSectionId + ".");
					await sectionMilestones.RenderAsync(
						completedSectionId,
						acceptedCheckpoint,
						actual.Workspace ??
							throw new InvalidDataException(
								"The accepted timeline has no candidate workspace."),
						sectionStart,
						sectionEnd,
						PlanSha256(acceptedPlan),
						cancellationToken);
					if (!completesSynchronization)
						publisher.TransitionTo(
							EditSessionState.Reviewing,
							"Completed song section render; publishing the accepted " +
							"checkpoint before planning the next clip.");
				}
				artifacts.SaveAcceptedPlan(acceptedCheckpoint, acceptedPlan);
				if (conflictOutcome?.Resolution != null)
				{
					AssemblyReconciliationConflictService conflictArtifacts =
						new(publisher.SessionRoot);
					conflictArtifacts.CompleteResolution(
						conflictOutcome.Resolution);
					conflictArtifacts.CompleteActionIntent(action);
				}
				await PublishCheckpointAsync(
					acceptedCheckpoint,
					acceptedPlan,
					actual,
					action,
					cancellationToken);
				actionExecutions.Complete(
					action,
					action.Kind == AssemblyActionKind.FinishSyncPass
						? "sync-pass-finished"
						: "timeline-accepted",
					$"assembly/checkpoints/{acceptedCheckpoint:D4}/accepted-plan.json");
				if (completesSynchronization)
				{
					List<string> unusedClips = allClipPaths
						.Where(path => !acceptedPlan.Montage.Placements.Any(
							placement => string.Equals(
								placement.Clip.FilePath,
								path,
								StringComparison.OrdinalIgnoreCase)))
						.ToList();
					string completionStatus = unusedClips.Count == 0
						? "Synchronization pass complete. The accepted assembly remains in VEGAS."
						: "Synchronization pass finished early with " +
							unusedClips.Count + " selected clip(s) unused: " +
							string.Join(
								", ",
								unusedClips.Select(Path.GetFileName)) +
							". The rough-cut audit will report uncovered musical structure.";
					PublishState(
						AssemblyPhase.SyncPassComplete,
						acceptedCheckpoint,
						acceptedPlan,
						workspace,
						completionStatus);
					publisher.TransitionTo(
						EditSessionState.Rendering,
						unusedClips.Count == 0
							? "Synchronization pass complete; preparing full rough-cut review."
							: "Early synchronization completion accepted; preparing a " +
								"rough-cut review that includes unused-media and song-coverage diagnostics.");
					return acceptedPlan;
				}

				await CleanupAsync(
					workspace,
					checkpoint,
					cancellationToken,
					"post-accept-" + action.ActionId);
				checkpoint = acceptedCheckpoint;
				break;
			}
		}
		throw new InvalidOperationException("Progressive assembly ended unexpectedly.");
	}

	public async Task<EditPlanDocument> RunAsync(
		EditPlanningRequest request,
		EditPlanDocument sketch,
		CancellationToken cancellationToken)
	{
		EditPlanDocument candidate = sketch;
		new AtomicFileWriter().WriteText(
			new SessionPathResolver(publisher.SessionRoot).Resolve("assembly/sketch.json"),
			EditPlanDocumentSerializer.SerializePlan(sketch));
		int total = candidate.Montage.Placements.Count;
		totalClips = total;
		allClipPaths = candidate.Montage.Placements
			.OrderBy(item => item.TimelineStartSeconds)
			.Select(item => item.Clip.FilePath)
			.ToList();
		for (int checkpoint = 1; checkpoint <= total; checkpoint++)
		{
			CandidateWorkspaceId workspace = Workspace(checkpoint);
			EditPlanDocument prefix = AssemblyPlanSlices.Prefix(candidate, checkpoint);
			await MaterializeAsync(request, prefix, workspace, checkpoint, cancellationToken);
			while (true)
			{
				publisher.TransitionTo(EditSessionState.Reviewing,
					$"Clip {checkpoint} is ready for review.");
				publisher.TransitionTo(EditSessionState.AwaitingUser,
					$"Awaiting review of clip {checkpoint}.");
				PublishState(AssemblyPhase.AwaitingHumanReview, checkpoint, prefix, workspace,
					$"Review clip {checkpoint} of {total} in VEGAS.");
				AssemblyAction action = await WaitForReviewActionAsync(
					checkpoint, prefix, workspace, cancellationToken);
				CandidateTimelineSnapshot actual = await SnapshotAsync(workspace, checkpoint, cancellationToken);
				if (action.Kind == AssemblyActionKind.CompareCurrentTimeline)
				{
					EditPlanDocument comparison = Clone(prefix);
					TimelineAdjustmentDelta adjustment =
						ReconcileMaterializedTimeline(comparison, actual, checkpoint);
					PersistAdjustment(checkpoint, adjustment);
					PublishState(
						AssemblyPhase.ReconcilingTimeline,
						checkpoint,
						prefix,
						workspace,
						$"Compared checkpoint {checkpoint} with VEGAS. Refreshing review evidence.");
					continue;
				}
				if (action.Kind == AssemblyActionKind.ResetCurrentClip)
				{
					publisher.TransitionTo(EditSessionState.Revising, "Resetting current clip.");
					await CleanupAsync(workspace, checkpoint, cancellationToken);
					await MaterializeAsync(request, prefix, workspace, checkpoint, cancellationToken);
					continue;
				}
				if (action.Kind == AssemblyActionKind.ReviseCurrentClip)
				{
					publisher.TransitionTo(EditSessionState.Revising, "Revising current clip.");
					TimelineAdjustmentDelta adjustment =
						ReconcileMaterializedTimeline(prefix, actual, checkpoint);
					PersistAdjustment(checkpoint, adjustment);
					EditPlanDocument revisionEvidence = Clone(candidate);
					MergePrefix(revisionEvidence, prefix);
					EditIterationFeedback feedback = new()
					{
						IsAccepted = false,
						Summary = string.IsNullOrWhiteSpace(action.Instruction)
							? "Revise the current clip using the human-adjusted timeline as evidence."
							: action.Instruction,
						SteeringInstructions = string.IsNullOrWhiteSpace(action.Instruction)
							? Array.Empty<string>()
							: new[] { action.Instruction },
						TimelineAdjustment = adjustment
					};
					EditPlanDocument revised = await planner.RevisePlanAsync(
						request, revisionEvidence, feedback, checkpoint, cancellationToken);
					PreserveAcceptedPrefix(candidate, revised, checkpoint - 1);
					EditPlanDocumentValidator.ValidateAndNormalize(revised);
					candidate = revised;
					prefix = AssemblyPlanSlices.Prefix(candidate, checkpoint);
					await CleanupAsync(workspace, checkpoint, cancellationToken);
					await MaterializeAsync(request, prefix, workspace, checkpoint, cancellationToken);
					continue;
				}
				if (action.Kind == AssemblyActionKind.AcceptTimelineAndContinue ||
					action.Kind == AssemblyActionKind.FinishSyncPass)
				{
					PublishState(AssemblyPhase.ReconcilingTimeline, checkpoint, prefix, workspace,
						"Adopting the current VEGAS timeline as authoritative.");
					TimelineAdjustmentDelta adjustment =
						ReconcileMaterializedTimeline(prefix, actual, checkpoint);
					PersistAdjustment(checkpoint, adjustment);
					MergePrefix(candidate, prefix);
					EditPlanDocumentValidator.ValidateAndNormalize(candidate);
					await PublishCheckpointAsync(checkpoint, candidate, actual, action, cancellationToken);
					if (action.Kind == AssemblyActionKind.FinishSyncPass ||
						checkpoint == total)
					{
						List<string> unusedClips = allClipPaths
							.Where(path => !prefix.Montage.Placements.Any(
								placement => string.Equals(
									placement.Clip.FilePath,
									path,
									StringComparison.OrdinalIgnoreCase)))
							.ToList();
						PublishState(AssemblyPhase.SyncPassComplete, checkpoint, prefix, workspace,
							unusedClips.Count == 0
								? "Synchronization pass complete. The assembly remains on the VEGAS timeline."
								: "Synchronization pass finished early with " +
									unusedClips.Count + " selected clip(s) unused: " +
									string.Join(", ", unusedClips.Select(Path.GetFileName)) +
									". The rough-cut audit will report uncovered musical structure.");
						publisher.TransitionTo(EditSessionState.Rendering,
							unusedClips.Count == 0
								? "Synchronization pass complete; preparing full rough-cut review."
								: "Early synchronization completion accepted; preparing a " +
									"rough-cut review that includes unused-media and song-coverage diagnostics.");
						return action.Kind == AssemblyActionKind.FinishSyncPass
							? prefix
							: candidate;
					}
					await CleanupAsync(workspace, checkpoint, cancellationToken);
					PublishState(AssemblyPhase.PlanningClip, checkpoint + 1, prefix,
						Workspace(checkpoint + 1),
						"Planning the next clip from the accepted VEGAS timeline.");
					publisher.TransitionTo(EditSessionState.Revising,
						"Planning next clip from accepted timeline.");
					EditPlanDocument next = await planner.RevisePlanAsync(
						request,
						candidate,
						new EditIterationFeedback
						{
							IsAccepted = false,
							Summary =
								"The human accepted checkpoint " + checkpoint +
								". Preserve accepted placements exactly and reconsider the next " +
								"clip using the remaining song regions, anchors, and clips.",
							SteeringInstructions = string.IsNullOrWhiteSpace(action.Instruction)
								? Array.Empty<string>()
								: new[] { action.Instruction }
						},
						checkpoint,
						cancellationToken);
					PreserveAcceptedPrefix(candidate, next, checkpoint);
					EditPlanDocumentValidator.ValidateAndNormalize(next);
					candidate = next;
					break;
				}
			}
		}
		throw new InvalidOperationException("Assembly ended unexpectedly.");
	}

	private async Task<ValidatedProposal> ValidateOrRepairProposalAsync(
		IProgressiveAssemblyPlanner activePlanner,
		EditPlanningRequest request,
		ProgressiveAssemblyPlanningContext originalContext,
		EditPlanDocument? acceptedPlan,
		AssemblyArtifactStore artifacts,
		int checkpoint,
		ClipStepDecision initialDecision,
		int initialProposalRevision,
		CancellationToken cancellationToken)
	{
		ClipStepDecision decision = initialDecision;
		int proposalRevision = initialProposalRevision;
		for (int attempt = 1;
			attempt <= MaximumAutomaticProposalAttempts;
			attempt++)
		{
			try
			{
				EditPlanDocument plan = new ClipStepDecisionCompiler()
					.Append(request, acceptedPlan, decision)
					.CombinedPlan;
				EditPlanDocumentValidator.ValidateAndNormalize(plan);
				return new ValidatedProposal(
					decision,
					plan,
					proposalRevision,
					attempt > 1);
			}
			catch (Exception exception) when (
				exception is InvalidOperationException ||
				exception is InvalidDataException ||
				exception is ArgumentException)
			{
				artifacts.SaveProposalRejection(
					checkpoint,
					proposalRevision,
					attempt,
					MaximumAutomaticProposalAttempts,
					exception.Message);
				if (attempt == MaximumAutomaticProposalAttempts)
					throw new InvalidOperationException(
						"AI proposal remained invalid after " +
						MaximumAutomaticProposalAttempts +
						" attempts. Last deterministic diagnostic: " +
						exception.Message,
						exception);

				int nextAttempt = attempt + 1;
				PublishProposalRepairState(
					checkpoint,
					acceptedPlan,
					decision,
					nextAttempt,
					exception.Message);
				publisher.TransitionTo(
					publisher.State == EditSessionState.NeedsRecovery
						? EditSessionState.Planning
						: publisher.State,
					"Repairing rejected clip proposal; attempt " +
						nextAttempt + " of " +
						MaximumAutomaticProposalAttempts + ".");
				ProgressiveAssemblyPlanningContext repairContext =
					ProgressiveAssemblyContextFactory.Create(
						request,
						originalContext.Sketch,
						acceptedPlan,
						checkpoint,
						scopedInstruction:
							BuildAutomaticRepairInstruction(
								originalContext.ScopedInstruction,
								exception.Message,
								nextAttempt));
				decision = await activePlanner.ReviseClipAsync(
					request,
					repairContext,
					decision,
					cancellationToken);
				proposalRevision++;
				artifacts.SaveProposal(decision, proposalRevision);
			}
		}
		throw new InvalidOperationException(
			"Automatic proposal repair ended unexpectedly.");
	}

	private void PublishProposalRepairState(
		int checkpoint,
		EditPlanDocument? acceptedPlan,
		ClipStepDecision rejectedDecision,
		int attempt,
		string diagnostic)
	{
		HashSet<string> placed = new(
			acceptedPlan?.Montage.Placements.Select(item => item.Clip.FilePath) ??
				Enumerable.Empty<string>(),
			StringComparer.OrdinalIgnoreCase);
		stateRevision++;
		actions.PublishState(new AssemblySessionState
		{
			SessionId = sessionId,
			Phase = AssemblyPhase.RepairingProposal,
			Checkpoint = checkpoint,
			StateRevision = stateRevision,
			TotalClips = totalClips,
			CurrentClipPath = rejectedDecision.Clip.MediaPath,
			RemainingClipPaths = allClipPaths
				.Where(path => !placed.Contains(path))
				.ToList(),
			Status = "Repairing rejected proposal; attempt " + attempt +
				" of " + MaximumAutomaticProposalAttempts + ". " + diagnostic,
			Workspace = Workspace(checkpoint)
		});
	}

	private static string BuildAutomaticRepairInstruction(
		string? originalInstruction,
		string diagnostic,
		int attempt)
	{
		string repair =
			"AUTOMATIC PRE-MATERIALIZATION REPAIR, attempt " + attempt +
			" of " + MaximumAutomaticProposalAttempts + ". The previous proposal " +
			"was rejected by deterministic validation and was not applied to VEGAS. " +
			"Diagnostic: " + diagnostic + " Recompute the current clip proposal. " +
			"At constant speed, every primary and additional sync must imply the same " +
			"timeline start: musicEventTime - " +
			"(killSourceConfirmationTime - sourceWindowStart) / constantSpeed. " +
			"Remove an incompatible additional sync rather than inventing a time, event, " +
			"kill, or speed. Preserve the accepted prefix and all other hard constraints.";
		return string.IsNullOrWhiteSpace(originalInstruction)
			? repair
			: originalInstruction.Trim() + "\n\n" + repair;
	}

	private async Task MaterializeAsync(
		EditPlanningRequest request,
		EditPlanDocument prefix,
		CandidateWorkspaceId workspace,
		int checkpoint,
		CancellationToken cancellationToken,
		string? operationScope = null)
	{
		string planSha256 = PlanSha256(prefix);
		string scope = SafeOperationScope(
			operationScope ?? "initial-" + planSha256[..16]);
		string operationKey =
			$"assembly-{checkpoint:D4}-{scope}-{planSha256[..16]}";
		PublishState(AssemblyPhase.MaterializingClip, checkpoint, prefix, workspace,
			"Materializing synchronized clip " + checkpoint + ".");
		publisher.TransitionTo(EditSessionState.Validating,
			$"Validating assembly checkpoint {checkpoint}.");
		PreflightCandidateResult preflight =
			await automation.ExecuteAsync<PreflightCandidateRequest, PreflightCandidateResult>(
				VegasOperations.PreflightCandidate,
				new PreflightCandidateRequest { Workspace = workspace, Plan = prefix, SongPath = request.SongPath },
				operationKey + "-preflight",
				cancellationToken: cancellationToken);
		AssemblyArtifactStore sessionArtifacts = new(publisher.SessionRoot);
		if (sessionArtifacts.ReadSessionDescriptor() != null &&
			automation.LastHost != null)
			sessionArtifacts.CaptureProjectIdentity(automation.LastHost);
		if (!preflight.IsReady)
			throw new InvalidOperationException(
				"VEGAS assembly preflight failed: " +
				string.Join("; ", preflight.Errors.Select(item => item.Message)));
		publisher.TransitionTo(EditSessionState.Materializing,
			$"Materializing assembly checkpoint {checkpoint}.");
		await automation.ExecuteAsync<MaterializeCandidateRequest, MaterializeCandidateResult>(
			VegasOperations.MaterializeCandidate,
			new MaterializeCandidateRequest
			{
				Workspace = workspace,
				Plan = prefix,
				SongPath = request.SongPath,
				IncludeSong = true,
				IncludeSfx = true,
				ApplyEffects = false
			},
			operationKey + "-materialize",
			cancellationToken: cancellationToken);
		CandidateTimelineSnapshot live = await SnapshotAsync(
			workspace,
			checkpoint,
			cancellationToken);
		CandidateMaterializationBaseline baseline =
			sessionArtifacts.SaveMaterializedBaseline(
				checkpoint,
				planSha256,
				live);
		EditPlanDocument verified = Clone(prefix);
		AssemblyTimelineReconciler.Apply(
			verified,
			live,
			checkpoint,
			baseline);
		EditPlanDocumentValidator.ValidateAndNormalize(verified);
	}

	private async Task<ProgressiveConflictOutcome> ResolveProgressiveConflictAsync(
		EditPlanningRequest request,
		AssemblySketch sketch,
		AssemblyArtifactStore artifacts,
		int checkpoint,
		EditPlanDocument proposal,
		CandidateTimelineSnapshot snapshot,
		CandidateWorkspaceId workspace,
		InvalidOperationException failure,
		CancellationToken cancellationToken)
	{
		AssemblyReconciliationConflictService conflicts =
			new(publisher.SessionRoot);
		AssemblyReconciliationResolution? pending =
			conflicts.ReadCurrentResolution(checkpoint);
		ReconciliationActionIntent? pendingAction =
			conflicts.ReadActionIntent(checkpoint);
		string? persistedConflictId =
			pending?.ConflictId ?? pendingAction?.ConflictId;
		AssemblyReconciliationConflict conflict = persistedConflictId == null
			? conflicts.Create(
				sessionId,
				checkpoint,
				request,
				proposal,
				snapshot,
				failure)
			: conflicts.ReadConflict(checkpoint, persistedConflictId);
		AssemblyAction? replayAction = pending == null
			? pendingAction?.Action
			: ActionFromResolution(pending);
		PublishState(
			AssemblyPhase.ReconciliationConflict,
			checkpoint,
			proposal,
			workspace,
			"Timeline conflict requires an explicit resolution. No live event was adopted.");
		while (true)
		{
			bool replaying = replayAction != null;
			AssemblyAction action = replayAction ??
				await WaitForConflictActionAsync(
					checkpoint, conflict, conflicts, cancellationToken);
			replayAction = null;
			if (action.Kind == AssemblyActionKind.DeferReconciliation ||
				action.Kind == AssemblyActionKind.PauseSession)
			{
				conflicts.SaveResolution(
					conflict,
					AssemblyReconciliationResolutionKind.DeferAndPause,
					"",
					action.Instruction);
				conflicts.CompleteActionIntent(action);
				publisher.TransitionTo(
					EditSessionState.Paused,
					"Synchronization conflict deferred by the editor.");
				PublishState(
					AssemblyPhase.Paused,
					checkpoint,
					proposal,
					workspace,
					"Paused with the unresolved synchronization conflict intact.");
				while (true)
				{
					AssemblyAction pausedAction = await WaitForActionAsync(
						checkpoint,
						cancellationToken,
						proposal,
						artifacts.GetLatestProposalRevision(checkpoint),
						journalProgressiveAction: true);
					if (pausedAction.Kind == AssemblyActionKind.AbandonSession)
					{
						await CleanupAsync(
							workspace,
							checkpoint,
							cancellationToken,
							"conflict-abandon-" + pausedAction.ActionId);
						PublishState(
							AssemblyPhase.Abandoned,
							checkpoint,
							proposal,
							workspace,
							"Assembly abandoned. Candidate-owned tracks were removed.");
						publisher.TransitionTo(
							EditSessionState.Cancelled,
							"Assembly abandoned by the editor.");
						actionExecutions.Complete(
							pausedAction,
							"deferred-conflict-abandoned");
						throw new AssemblySessionAbandonedException(sessionId);
					}
					if (pausedAction.Kind != AssemblyActionKind.ResumeSession)
						throw new InvalidOperationException(
							"Only resume or abandon is valid while a conflict is deferred.");
					publisher.TransitionTo(
						EditSessionState.AwaitingUser,
						"Resumed the deferred synchronization conflict.");
					PublishState(
						AssemblyPhase.ReconciliationConflict,
						checkpoint,
						proposal,
						workspace,
						"Choose how to resolve the persisted synchronization conflict.");
					actionExecutions.Complete(
						pausedAction,
						"deferred-conflict-resumed");
					break;
				}
				continue;
			}
			if (action.Kind == AssemblyActionKind.AbandonSession)
			{
				conflicts.CompleteActionIntent(action);
				await CleanupAsync(workspace, checkpoint, cancellationToken);
				PublishState(
					AssemblyPhase.Abandoned,
					checkpoint,
					proposal,
					workspace,
					"Assembly abandoned. Candidate-owned tracks were removed.");
				publisher.TransitionTo(
					EditSessionState.Cancelled,
					"Assembly abandoned by the editor.");
				throw new AssemblySessionAbandonedException(sessionId);
			}
			if (action.Kind == AssemblyActionKind.RestoreReconciliationProposal)
			{
				AssemblyReconciliationResolution resolution = pending ??
					conflicts.SaveResolution(
						conflict,
						AssemblyReconciliationResolutionKind.RestoreExactProposal,
						"",
						action.Instruction);
				await CleanupAsync(
					workspace,
					checkpoint,
					cancellationToken,
					"conflict-" + resolution.ResolutionId);
				await MaterializeAsync(
					request,
					proposal,
					workspace,
					checkpoint,
					cancellationToken,
					"conflict-" + resolution.ResolutionId);
				CandidateTimelineSnapshot restored = await SnapshotAsync(
					workspace, checkpoint, cancellationToken);
				EditPlanDocument verified = Clone(proposal);
				TimelineAdjustmentDelta restoredDelta =
					ReconcileMaterializedTimeline(
						verified, restored, checkpoint);
				PersistAdjustment(checkpoint, restoredDelta);
				conflicts.SaveVerifiedOutcome(
					resolution, verified, restored);
				conflicts.CompleteResolution(resolution);
				conflicts.CompleteActionIntent(action);
				return new ProgressiveConflictOutcome(
					verified, restored, action, accepted: false, resolution);
			}
			if (action.Kind == AssemblyActionKind.ExcludeReconciliationClip)
			{
				if (!replaying)
				{
					CandidateTimelineSnapshot fresh = await SnapshotAsync(
						workspace, checkpoint, cancellationToken);
					AssemblyReconciliationConflict refreshed = conflicts.Create(
						sessionId,
						checkpoint,
						request,
						proposal,
						fresh,
						failure);
					if (!string.Equals(
						refreshed.ConflictId,
						conflict.ConflictId,
						StringComparison.Ordinal))
					{
						conflict = refreshed;
						snapshot = fresh;
						PublishState(
							AssemblyPhase.ReconciliationConflict,
							checkpoint,
							proposal,
							workspace,
							"The live timeline changed. Review the refreshed conflict before resolving it.");
						conflicts.CompleteActionIntent(action);
						continue;
					}
				}
				AssemblyReconciliationResolution resolution = pending ??
					conflicts.SaveResolution(
						conflict,
						AssemblyReconciliationResolutionKind.ExcludeCurrentClip,
						"",
						action.Instruction);
				string excludedPath = proposal.Montage.Placements.Last().Clip.FilePath;
				EditPlanDocument resolved = Clone(proposal);
				resolved.Montage.Placements.RemoveAt(
					resolved.Montage.Placements.Count - 1);
				resolved.Montage.SyncAssignments =
					resolved.Montage.SyncAssignments.Where(item =>
						!string.Equals(
							item.ClipPath,
							excludedPath,
							StringComparison.OrdinalIgnoreCase)).ToList();
				EditPlanDocumentValidator.ValidateAndNormalize(resolved);
				bool sketchChanged =
					AssemblyReconciliationConflictService.ExcludeCurrentFromSketch(
					sketch, checkpoint, excludedPath);
				if (sketchChanged)
					artifacts.SaveSketch(
						sketch, artifacts.GetLatestSketchRevision() + 1);
				totalClips = sketch.ClipOrder.Count;
				allClipPaths = sketch.ClipOrder
					.OrderBy(item => item.Order)
					.Select(item => item.Clip.MediaPath)
					.ToList();
				await CleanupAsync(
					workspace,
					checkpoint,
					cancellationToken,
					"conflict-" + resolution.ResolutionId);
				await MaterializeAsync(
					request,
					resolved,
					workspace,
					checkpoint,
					cancellationToken,
					"conflict-" + resolution.ResolutionId);
				CandidateTimelineSnapshot rebuilt = await SnapshotAsync(
					workspace, checkpoint, cancellationToken);
				TimelineAdjustmentDelta rebuiltDelta =
					ReconcileMaterializedTimeline(
						resolved, rebuilt, checkpoint);
				PersistAdjustment(checkpoint, rebuiltDelta);
				conflicts.SaveVerifiedOutcome(
					resolution, resolved, rebuilt);
				return new ProgressiveConflictOutcome(
					resolved, rebuilt, action, accepted: true, resolution);
			}
			if (action.Kind == AssemblyActionKind.AdoptReconciliationEvent)
			{
				AssemblyReconciliationConflict refreshed = conflict;
				CandidateEventSnapshot live;
				if (!replaying)
				{
					CandidateTimelineSnapshot fresh = await SnapshotAsync(
						workspace, checkpoint, cancellationToken);
					refreshed = conflicts.Create(
						sessionId,
						checkpoint,
						request,
						proposal,
						fresh,
						failure);
					if (!string.Equals(
						refreshed.ConflictId,
						conflict.ConflictId,
						StringComparison.Ordinal))
					{
						conflict = refreshed;
						snapshot = fresh;
						PublishState(
							AssemblyPhase.ReconciliationConflict,
							checkpoint,
							proposal,
							workspace,
							"The live timeline changed. Re-select a candidate from the refreshed conflict.");
						conflicts.CompleteActionIntent(action);
						continue;
					}
					AssemblyReconciliationEventCandidate freshCandidate =
						conflicts.RequireCandidate(refreshed, action.TargetId);
					live = conflicts.RequireLiveCandidate(fresh, freshCandidate);
				}
				else
				{
					AssemblyReconciliationEventCandidate persistedCandidate =
						conflicts.RequireCandidate(conflict, action.TargetId);
					live = EventFromCandidate(persistedCandidate);
				}
				AssemblyReconciliationEventCandidate candidate =
					conflicts.RequireCandidate(refreshed, action.TargetId);
				bool asCurrent = candidate.CanAdoptAsCurrent;
				if (!asCurrent && !candidate.CanAdoptAsAdditional)
					throw new InvalidOperationException(
						"The selected event is not a supported deterministic adoption.");
				AssemblyReconciliationResolution resolution = pending ??
					conflicts.SaveResolution(
						conflict,
						AssemblyReconciliationResolutionKind.AdoptKnownRemainingEvent,
						candidate.CandidateId,
						action.Instruction);
				EditPlanDocument resolved =
					AssemblyReconciliationConflictService.Adopt(
						request, proposal, live, asCurrent);
				bool sketchChanged =
					AssemblyReconciliationConflictService.ReorderSketchForAdoption(
					sketch, checkpoint, candidate.MediaPath, asCurrent);
				if (sketchChanged)
					artifacts.SaveSketch(
						sketch, artifacts.GetLatestSketchRevision() + 1);
				allClipPaths = sketch.ClipOrder
					.OrderBy(item => item.Order)
					.Select(item => item.Clip.MediaPath)
					.ToList();
				await CleanupAsync(
					workspace,
					checkpoint,
					cancellationToken,
					"conflict-" + resolution.ResolutionId);
				await MaterializeAsync(
					request,
					resolved,
					workspace,
					checkpoint,
					cancellationToken,
					"conflict-" + resolution.ResolutionId);
				CandidateTimelineSnapshot rebuilt = await SnapshotAsync(
					workspace, checkpoint, cancellationToken);
				TimelineAdjustmentDelta rebuiltDelta =
					ReconcileMaterializedTimeline(
						resolved, rebuilt, checkpoint);
				PersistAdjustment(checkpoint, rebuiltDelta);
				conflicts.SaveVerifiedOutcome(
					resolution, resolved, rebuilt);
				return new ProgressiveConflictOutcome(
					resolved, rebuilt, action, accepted: true, resolution);
			}
			throw new InvalidOperationException(
				"Action " + action.Kind +
				" cannot resolve a synchronization conflict.");
		}
	}

	private Task<CandidateTimelineSnapshot> SnapshotAsync(
		CandidateWorkspaceId workspace,
		int checkpoint,
		CancellationToken cancellationToken) =>
		automation.ExecuteAsync<GetCandidateSnapshotRequest, CandidateTimelineSnapshot>(
			VegasOperations.GetCandidateSnapshot,
			new GetCandidateSnapshotRequest { Workspace = workspace },
			$"assembly-{checkpoint:D4}-snapshot-{Guid.NewGuid():N}",
			cancellationToken: cancellationToken);

	private TimelineAdjustmentDelta ReconcileMaterializedTimeline(
		EditPlanDocument plan,
		CandidateTimelineSnapshot snapshot,
		int checkpoint)
	{
		CandidateMaterializationBaseline baseline =
			new AssemblyArtifactStore(publisher.SessionRoot)
				.ReadMaterializedBaseline(checkpoint)
			?? throw new InvalidDataException(
				"The exact post-materialization candidate baseline is missing. " +
				"Reset the current proposal before adopting live timeline changes.");
		return AssemblyTimelineReconciler.Apply(
			plan,
			snapshot,
			checkpoint,
			baseline);
	}

	private async Task<CandidateTimelineSnapshot>
		ReadOrCaptureActionTimelineEvidenceAsync(
			AssemblyAction action,
			CandidateWorkspaceId workspace,
			int checkpoint,
			CancellationToken cancellationToken)
	{
		AssemblyActionExecutionStart? started =
			actionExecutions.Read(action.ActionId);
		if (started == null || !RequiresManualTimelineEvidence(action.Kind))
			return await SnapshotAsync(
				workspace, checkpoint, cancellationToken);
		AssemblyActionTimelineEvidence? persisted =
			actionExecutions.ReadTimelineEvidence(action.ActionId);
		if (persisted != null)
			return persisted.Snapshot;
		CandidateTimelineSnapshot live = await SnapshotAsync(
			workspace, checkpoint, cancellationToken);
		return actionExecutions.SaveTimelineEvidence(action, live).Snapshot;
	}

	private async Task CleanupAsync(
		CandidateWorkspaceId workspace,
		int checkpoint,
		CancellationToken cancellationToken,
		string? operationScope = null)
	{
		await automation.ExecuteAsync<CleanupCandidateRequest, CleanupCandidateResult>(
			VegasOperations.CleanupCandidate,
			new CleanupCandidateRequest { Workspace = workspace },
			operationScope == null
				? $"assembly-{checkpoint:D4}-cleanup-{Guid.NewGuid():N}"
				: $"assembly-{checkpoint:D4}-" +
					SafeOperationScope(operationScope) + "-cleanup",
			cancellationToken: cancellationToken);
	}

	private async Task<AssemblyAction> WaitForActionAsync(
		int checkpoint,
		CancellationToken cancellationToken,
		EditPlanDocument? plan = null,
		int proposalRevision = 0,
		bool journalProgressiveAction = false)
	{
		if (journalProgressiveAction)
		{
			AssemblySessionState current = actions.ReadState() ??
				throw new InvalidDataException(
					"The progressive review state is missing.");
			AssemblyActionExecutionStart? pending =
				actionExecutions.ReadPendingForRecovery(sessionId);
			if (pending != null)
			{
				if (pending.Action.Checkpoint != checkpoint)
					throw new InvalidDataException(
						"A pending progressive action targets another checkpoint.");
				if (pending.Action.ExpectedStateRevision == current.StateRevision &&
					pending.Phase == current.Phase)
				{
					actions.RecordRecoveredDisposition(pending.Action);
					return pending.Action;
				}
				if (CanReplayAdvancedProgressiveAction(pending, current))
				{
					actions.RecordRecoveredDisposition(pending.Action);
					return pending.Action;
				}
				if (TryCompleteReflectedLifecycleAction(pending, current))
					return await WaitForActionAsync(
						checkpoint,
						cancellationToken,
						plan,
						proposalRevision,
						journalProgressiveAction);
				throw new InvalidDataException(
					"A pending progressive action no longer matches the current " +
					"assembly state and could not be reconciled safely.");
			}
		}
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			AssemblyAction? action;
			if (journalProgressiveAction)
			{
				AssemblySessionState state = actions.ReadState() ??
					throw new InvalidDataException(
						"The progressive review state is missing.");
				string planSha256 = PlanSha256(plan ??
					throw new ArgumentNullException(nameof(plan)));
				int previewAttempt =
					new CheckpointPreviewArtifactStore(publisher.SessionRoot)
						.NextAttempt(checkpoint) - 1;
				string adjustmentSha256 = CurrentAdjustmentSha256(checkpoint);
				Action<AssemblyAction> begin = candidate =>
					actionExecutions.Begin(
						candidate,
						state.Phase,
						planSha256,
						proposalRevision,
						previewAttempt,
						adjustmentSha256);
				action = actions.TryRecoverClaimed(
					checkpoint,
					sessionId,
					stateRevision,
					begin);
				action ??= actions.TryConsume(
					checkpoint, sessionId, stateRevision, begin);
			}
			else
			{
				action = actions.TryConsume(
					checkpoint, sessionId, stateRevision);
			}
			if (action != null)
				return action;
			await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
		}
	}

	private async Task<AssemblyAction> WaitForConflictActionAsync(
		int checkpoint,
		AssemblyReconciliationConflict conflict,
		AssemblyReconciliationConflictService conflicts,
		CancellationToken cancellationToken)
	{
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Action<AssemblyAction> persistIntent =
				action => conflicts.SaveActionIntent(conflict, action);
			AssemblyAction? action = actions.TryRecoverClaimed(
				checkpoint,
				sessionId,
				stateRevision,
				persistIntent);
			action ??= actions.TryConsume(
				checkpoint,
				sessionId,
				stateRevision,
				persistIntent);
			if (action != null) return action;
			await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
		}
	}

	private async Task<AssemblyAction> WaitForReviewActionAsync(
		int checkpoint,
		EditPlanDocument prefix,
		CandidateWorkspaceId workspace,
		CancellationToken cancellationToken,
		int proposalRevision = 0,
		bool journalProgressiveAction = false)
	{
		while (true)
		{
			AssemblyAction action = await WaitForActionAsync(
				checkpoint,
				cancellationToken,
				prefix,
				proposalRevision,
				journalProgressiveAction);
			if (action.Kind == AssemblyActionKind.PauseSession)
			{
				publisher.TransitionTo(
					EditSessionState.Paused,
					"Assembly paused by the editor.");
				PublishState(
					AssemblyPhase.Paused,
					checkpoint,
					prefix,
					workspace,
					"Paused. The current candidate remains in VEGAS.");
				if (journalProgressiveAction)
					actionExecutions.Complete(action, "progressive-review-paused");
				continue;
			}
			if (action.Kind == AssemblyActionKind.ResumeSession)
			{
				if (publisher.State != EditSessionState.Paused)
					throw new InvalidOperationException(
						"The assembly session can only resume from its paused state.");
				publisher.TransitionTo(
					EditSessionState.AwaitingUser,
					"Assembly resumed by the editor.");
				PublishState(
					AssemblyPhase.AwaitingHumanReview,
					checkpoint,
					prefix,
					workspace,
					$"Review clip {checkpoint} of {totalClips} in VEGAS.");
				if (journalProgressiveAction)
					actionExecutions.Complete(action, "progressive-review-resumed");
				continue;
			}
			if (action.Kind == AssemblyActionKind.AbandonSession)
			{
				await CleanupAsync(
					workspace,
					checkpoint,
					cancellationToken,
					journalProgressiveAction
						? "abandon-" + action.ActionId
						: null);
				PublishState(
					AssemblyPhase.Abandoned,
					checkpoint,
					prefix,
					workspace,
					"Assembly abandoned. Candidate-owned tracks were removed.");
				publisher.TransitionTo(
					EditSessionState.Cancelled,
					"Assembly abandoned by the editor.");
				if (journalProgressiveAction)
					actionExecutions.Complete(action, "progressive-assembly-abandoned");
				throw new AssemblySessionAbandonedException(sessionId);
			}
			return action;
		}
	}

	private void PublishState(
		AssemblyPhase phase,
		int checkpoint,
		EditPlanDocument prefix,
		CandidateWorkspaceId workspace,
		string status)
	{
		HashSet<string> placed = new(
			prefix.Montage.Placements.Select(item => item.Clip.FilePath),
			StringComparer.OrdinalIgnoreCase);
		stateRevision++;
		actions.PublishState(new AssemblySessionState
		{
			SessionId = sessionId,
			Phase = phase,
			Checkpoint = checkpoint,
			StateRevision = stateRevision,
			TotalClips = totalClips,
			CurrentClipPath = prefix.Montage.Placements
				.OrderBy(item => item.TimelineStartSeconds).Last().Clip.FilePath,
			RemainingClipPaths = allClipPaths.Where(path => !placed.Contains(path)).ToList(),
			Status = status,
			Workspace = workspace
		});
	}

	private void PublishPlanningState(
		int checkpoint,
		EditPlanDocument? acceptedPlan,
		AssemblySketch sketch)
	{
		HashSet<string> accepted = new(
			acceptedPlan?.Montage.Placements.Select(item => item.Clip.FilePath) ??
				Enumerable.Empty<string>(),
			StringComparer.OrdinalIgnoreCase);
		List<string> remaining = sketch.ClipOrder
			.OrderBy(item => item.Order)
			.Select(item => item.Clip.MediaPath)
			.Where(path => !accepted.Contains(path))
			.ToList();
		stateRevision++;
		actions.PublishState(new AssemblySessionState
		{
			SessionId = sessionId,
			Phase = AssemblyPhase.PlanningClip,
			Checkpoint = checkpoint,
			StateRevision = stateRevision,
			TotalClips = totalClips,
			CurrentClipPath = remaining.FirstOrDefault() ?? "",
			RemainingClipPaths = remaining,
			Status = $"Planning synchronized clip {checkpoint} of {totalClips}.",
			Workspace = Workspace(checkpoint)
		});
		if (acceptedPlan != null)
		{
		publisher.TransitionTo(
			publisher.State == EditSessionState.Planning
				? EditSessionState.Planning
				: EditSessionState.Revising,
			$"Planning progressive assembly clip {checkpoint}.");
		}
	}

	private void PublishRecoveryState(
		int checkpoint,
		EditPlanDocument? acceptedPlan,
		CandidateWorkspaceId workspace,
		string status)
	{
		HashSet<string> placed = new(
			acceptedPlan?.Montage.Placements.Select(item => item.Clip.FilePath) ??
				Enumerable.Empty<string>(),
			StringComparer.OrdinalIgnoreCase);
		stateRevision++;
		actions.PublishState(new AssemblySessionState
		{
			SessionId = sessionId,
			Phase = AssemblyPhase.Recovering,
			Checkpoint = checkpoint,
			StateRevision = stateRevision,
			TotalClips = totalClips,
			CurrentClipPath = allClipPaths.Skip(Math.Max(0, checkpoint - 1))
				.FirstOrDefault() ?? "",
			RemainingClipPaths = allClipPaths
				.Where(path => !placed.Contains(path))
				.ToList(),
			Status = status,
			Workspace = workspace
		});
	}

	private void PersistAdjustment(int checkpoint, TimelineAdjustmentDelta adjustment)
	{
		new AtomicFileWriter().WriteText(
			new SessionPathResolver(publisher.SessionRoot).Resolve(
				$"assembly/checkpoints/{checkpoint:D4}/adjustment-delta.json"),
			ContractSerializer.Serialize(adjustment));
	}

	private TimelineAdjustmentDelta ReadPersistedAdjustment(int checkpoint)
	{
		string path = new SessionPathResolver(publisher.SessionRoot).Resolve(
			$"assembly/checkpoints/{checkpoint:D4}/adjustment-delta.json");
		if (!File.Exists(path))
			throw new InvalidOperationException(
				"The reconciled timeline adjustment evidence is missing.");
		return ContractSerializer.Deserialize<TimelineAdjustmentDelta>(
			File.ReadAllText(path));
	}

	private string CurrentAdjustmentSha256(int checkpoint)
	{
		string path = new SessionPathResolver(publisher.SessionRoot).Resolve(
			$"assembly/checkpoints/{checkpoint:D4}/adjustment-delta.json");
		return File.Exists(path)
			? new SessionArtifactHasher().ComputeSha256(path)
			: "";
	}

	private static string PlanSha256(EditPlanDocument plan) =>
		Convert.ToHexString(SHA256.HashData(
			new UTF8Encoding(false).GetBytes(
				EditPlanDocumentSerializer.SerializePlan(plan))))
			.ToLowerInvariant();

	private static bool TryCompletedSection(
		AssemblySketch sketch,
		int acceptedCheckpoint,
		out string sectionId)
	{
		AssemblyClipIntent current = sketch.ClipOrder.SingleOrDefault(
			item => item.Order == acceptedCheckpoint)
			?? throw new InvalidDataException(
				"The accepted checkpoint is absent from the semantic clip order.");
		sectionId = current.SectionId;
		AssemblyClipIntent? next = sketch.ClipOrder.SingleOrDefault(
			item => item.Order == acceptedCheckpoint + 1);
		return next == null ||
			!string.Equals(
				next.SectionId,
				sectionId,
				StringComparison.Ordinal);
	}

	private static (TimeSpan Start, TimeSpan End) SectionRange(
		AssemblySketch sketch,
		EditPlanDocument acceptedPlan,
		string sectionId,
		int acceptedCheckpoint)
	{
		List<int> orders = sketch.ClipOrder
			.Where(item =>
				item.Order <= acceptedCheckpoint &&
				string.Equals(
					item.SectionId,
					sectionId,
					StringComparison.Ordinal))
			.Select(item => item.Order)
			.OrderBy(value => value)
			.ToList();
		if (orders.Count == 0 ||
			orders.Any(order =>
				order < 1 ||
				order > acceptedPlan.Montage.Placements.Count))
			throw new InvalidDataException(
				"The completed song section cannot be mapped to accepted placements.");
		List<Core.Domain.Editing.ClipPlacement> placements =
			orders.Select(order =>
				acceptedPlan.Montage.Placements[order - 1])
			.ToList();
		return (
			TimeSpan.FromSeconds(
				placements.Min(item => item.TimelineStartSeconds)),
			TimeSpan.FromSeconds(
				placements.Max(item => item.TimelineEndSeconds)));
	}

	private string RecoveryMaterializationScope(
		int checkpoint,
		long recoveryStateRevision)
	{
		string relative =
			$"assembly/checkpoints/{checkpoint:D4}/recovery/" +
			$"materialization-{recoveryStateRevision:D19}.scope.txt";
		string path = new SessionPathResolver(publisher.SessionRoot).Resolve(relative);
		if (File.Exists(path))
			return SafeOperationScope(File.ReadAllText(path).Trim());
		string scope =
			$"recovery-{recoveryStateRevision:D19}-{Guid.NewGuid():N}";
		new AtomicFileWriter().WriteText(path, scope + Environment.NewLine);
		return scope;
	}

	private static string SafeOperationScope(string value)
	{
		if (string.IsNullOrWhiteSpace(value) ||
			value.Length > 160 ||
			value.Any(character =>
				!char.IsLetterOrDigit(character) &&
				character is not ('-' or '_' or '.')))
			throw new InvalidDataException(
				"An assembly automation operation scope is unsafe.");
		return value;
	}

	private static bool CanReplayAdvancedProgressiveAction(
		AssemblyActionExecutionStart pending,
		AssemblySessionState current)
	{
		if (pending.Action.Checkpoint != current.Checkpoint ||
			pending.Action.ExpectedStateRevision >= current.StateRevision)
			return false;
		return pending.Action.Kind switch
		{
			AssemblyActionKind.ReviseCurrentClip or
			AssemblyActionKind.ResetCurrentClip or
			AssemblyActionKind.CompareCurrentTimeline or
			AssemblyActionKind.RenderCheckpointPreview or
			AssemblyActionKind.AcceptTimelineAndContinue or
			AssemblyActionKind.FinishSyncPass =>
				current.Phase is AssemblyPhase.Recovering or
					AssemblyPhase.PlanningClip or
					AssemblyPhase.MaterializingClip or
					AssemblyPhase.ReconcilingTimeline or
					AssemblyPhase.RenderingCheckpoint or
					AssemblyPhase.AwaitingHumanReview,
			_ => false
		};
	}

	internal static bool RequiresManualTimelineEvidence(
		AssemblyActionKind kind) =>
		kind is AssemblyActionKind.CompareCurrentTimeline or
			AssemblyActionKind.RenderCheckpointPreview or
			AssemblyActionKind.ReviseCurrentClip or
			AssemblyActionKind.AcceptTimelineAndContinue or
			AssemblyActionKind.FinishSyncPass;

	private bool TryCompleteReflectedLifecycleAction(
		AssemblyActionExecutionStart pending,
		AssemblySessionState current)
	{
		string? outcome = pending.Action.Kind switch
		{
			AssemblyActionKind.PauseSession
				when current.Phase == AssemblyPhase.Paused =>
				"progressive-review-paused",
			AssemblyActionKind.ResumeSession
				when pending.Phase == AssemblyPhase.Paused &&
					current.Phase != AssemblyPhase.Paused =>
				"progressive-review-resumed",
			AssemblyActionKind.AbandonSession
				when current.Phase == AssemblyPhase.Abandoned =>
				"progressive-assembly-abandoned",
			_ => null
		};
		if (outcome == null) return false;
		actionExecutions.Complete(pending.Action, outcome);
		return true;
	}

	private static AssemblyAction ActionFromResolution(
		AssemblyReconciliationResolution resolution) => new()
	{
		ActionId = "replay-" + resolution.ResolutionId,
		SessionId = resolution.SessionId,
		Checkpoint = resolution.Checkpoint,
		Kind = resolution.Kind switch
		{
			AssemblyReconciliationResolutionKind.RestoreExactProposal =>
				AssemblyActionKind.RestoreReconciliationProposal,
			AssemblyReconciliationResolutionKind.ExcludeCurrentClip =>
				AssemblyActionKind.ExcludeReconciliationClip,
			AssemblyReconciliationResolutionKind.AdoptKnownRemainingEvent =>
				AssemblyActionKind.AdoptReconciliationEvent,
			_ => throw new InvalidOperationException(
				"Only a pending mutating conflict resolution may be replayed.")
		},
		TargetId = resolution.TargetCandidateId,
		Instruction = resolution.Instruction,
		CreatedUtc = resolution.ResolvedUtc
	};

	private static CandidateEventSnapshot EventFromCandidate(
		AssemblyReconciliationEventCandidate candidate) => new()
	{
		PlacementId = candidate.PlacementId,
		MediaPath = candidate.MediaPath,
		TimelineStart = TimeSpan.FromSeconds(candidate.TimelineStartSeconds),
		TimelineDuration = TimeSpan.FromSeconds(candidate.TimelineDurationSeconds),
		SourceOffset = TimeSpan.FromSeconds(candidate.SourceOffsetSeconds),
		Velocity = new List<CandidateVelocityPoint>
		{
			new()
			{
				Offset = TimeSpan.Zero,
				Velocity = candidate.ConstantSpeed
			}
		}
	};

	private static EditPlanDocument Clone(EditPlanDocument source) =>
		EditPlanDocumentSerializer.DeserializePlan(
			EditPlanDocumentSerializer.SerializePlan(source));

	private static EditPlanDocument SynchronizationPlan(EditPlanDocument source) =>
		AssemblyPlanSlices.Prefix(source, source.Montage.Placements.Count);

	private async Task PublishCheckpointAsync(
		int checkpoint,
		EditPlanDocument candidate,
		CandidateTimelineSnapshot timeline,
		AssemblyAction action,
		CancellationToken cancellationToken)
	{
		publisher.PublishTimeline(checkpoint, timeline);
		await publisher.OnSnapshotAsync(new EditIterationSnapshot
		{
			Iteration = checkpoint,
			Phase = "assembly-accepted",
			Candidate = candidate,
			Summary = action.Kind == AssemblyActionKind.FinishSyncPass
				? "Synchronization pass completed from the current VEGAS timeline."
				: "Human-adjusted timeline accepted; ready for the next clip."
		}, cancellationToken);
	}

	private CandidateWorkspaceId Workspace(int checkpoint) => new()
	{
		SessionId = sessionId,
		Iteration = checkpoint,
		Nonce = "assembly"
	};

	private static void PreserveAcceptedPrefix(
		EditPlanDocument current,
		EditPlanDocument revised,
		int acceptedCount)
	{
		Dictionary<string, Core.Domain.Editing.ClipPlacement> accepted = current.Montage.Placements
			.OrderBy(item => item.TimelineStartSeconds)
			.Take(acceptedCount)
			.ToDictionary(item => item.Clip.FilePath, StringComparer.OrdinalIgnoreCase);
		for (int index = 0; index < revised.Montage.Placements.Count; index++)
		{
			string path = revised.Montage.Placements[index].Clip.FilePath;
			if (accepted.TryGetValue(path, out Core.Domain.Editing.ClipPlacement? placement))
				revised.Montage.Placements[index] = placement;
		}
		HashSet<string> acceptedPaths = new(accepted.Keys, StringComparer.OrdinalIgnoreCase);
		revised.Montage.SyncAssignments = revised.Montage.SyncAssignments
			.Where(item => !acceptedPaths.Contains(item.ClipPath))
			.Concat(current.Montage.SyncAssignments
				.Where(item => acceptedPaths.Contains(item.ClipPath)))
			.ToList();
	}

	private static void MergePrefix(EditPlanDocument target, EditPlanDocument prefix)
	{
		Dictionary<string, Core.Domain.Editing.ClipPlacement> accepted =
			prefix.Montage.Placements.ToDictionary(
				item => item.Clip.FilePath, StringComparer.OrdinalIgnoreCase);
		for (int index = 0; index < target.Montage.Placements.Count; index++)
		{
			string path = target.Montage.Placements[index].Clip.FilePath;
			if (accepted.TryGetValue(path, out Core.Domain.Editing.ClipPlacement? placement))
				target.Montage.Placements[index] = placement;
		}
		target.Montage.SyncAssignments = target.Montage.SyncAssignments
			.Where(item => !accepted.ContainsKey(item.ClipPath))
			.Concat(prefix.Montage.SyncAssignments)
			.ToList();
	}
}

internal sealed class ProgressiveConflictOutcome
{
	public ProgressiveConflictOutcome(
		EditPlanDocument plan,
		CandidateTimelineSnapshot snapshot,
		AssemblyAction action,
		bool accepted,
		AssemblyReconciliationResolution? resolution = null)
	{
		Plan = plan;
		Snapshot = snapshot;
		Action = action;
		Accepted = accepted;
		Resolution = resolution;
	}

	public EditPlanDocument Plan { get; }
	public CandidateTimelineSnapshot Snapshot { get; }
	public AssemblyAction Action { get; }
	public bool Accepted { get; }
	public AssemblyReconciliationResolution? Resolution { get; }
}

internal sealed class AssemblySessionAbandonedException : OperationCanceledException
{
	public AssemblySessionAbandonedException(string sessionId)
		: base("Assembly session '" + sessionId + "' was abandoned by the editor.")
	{
	}
}
