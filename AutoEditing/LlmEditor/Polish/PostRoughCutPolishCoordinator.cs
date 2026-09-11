using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.LlmEditor.Assembly;
using AutoEditing.LlmEditor.Automation;
using AutoEditing.LlmEditor.Workbench;
using Core.Domain.Planning;
using System.Security.Cryptography;
using System.Text;

namespace AutoEditing.LlmEditor.Polish;

internal sealed class PolishWorkflowResult
{
	public required AcceptedPolishPass Effects { get; init; }
	public required AcceptedPolishPass Audio { get; init; }
	public required EffectsPassPlan EffectsPlan { get; init; }
	public required string EffectsPlanSha256 { get; init; }
	public required CandidateMaterializationBaseline FinalBaseline { get; init; }
}

internal sealed class PostRoughCutPolishCoordinator
{
	private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
	private readonly string sessionId;
	private readonly string sessionRoot;
	private readonly WorkbenchSessionPublisher publisher;
	private readonly IVegasAutomationClient automation;
	private readonly PolishPassWorkflow workflow;
	private readonly PolishPassArtifactStore artifacts;
	private readonly AssemblyActionStore actions;
	private readonly AssemblyActionExecutionStore actionExecutions;
	private CandidateMaterializationBaseline? acceptedRoughCutBaseline;

	public PostRoughCutPolishCoordinator(
		string sessionId,
		string sessionRoot,
		IVegasAutomationClient automation,
		WorkbenchSessionPublisher publisher)
	{
		this.sessionId = string.IsNullOrWhiteSpace(sessionId)
			? throw new ArgumentException("A session ID is required.", nameof(sessionId))
			: sessionId;
		this.sessionRoot = Path.GetFullPath(
			string.IsNullOrWhiteSpace(sessionRoot)
				? throw new ArgumentException(
					"A session root is required.",
					nameof(sessionRoot))
				: sessionRoot);
		this.automation = automation ?? throw new ArgumentNullException(nameof(automation));
		this.publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
		workflow = new PolishPassWorkflow(
			sessionId,
			sessionRoot,
			automation,
			new VegasPolishPreviewRenderer(automation));
		artifacts = new PolishPassArtifactStore(sessionRoot);
		actions = new AssemblyActionStore(sessionRoot);
		actionExecutions = new AssemblyActionExecutionStore(sessionRoot);
	}

	public async Task<PolishWorkflowResult> RunAsync(
		AcceptedRoughCutMilestone roughCut,
		EditPlanningRequest request,
		EditPlanDocument acceptedRoughCut,
		CandidateMaterializationBaseline roughCutBaseline,
		CandidateWorkspaceId workspace,
		CancellationToken cancellationToken,
		AssemblyRuntimeLease? runtimeLease = null)
	{
		ArgumentNullException.ThrowIfNull(roughCut);
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(acceptedRoughCut);
		ArgumentNullException.ThrowIfNull(roughCutBaseline);
		acceptedRoughCutBaseline = roughCutBaseline;
		workspace?.Validate();
		if (workspace == null) throw new ArgumentNullException(nameof(workspace));
		if (!string.Equals(roughCut.SessionId, sessionId, StringComparison.Ordinal))
			throw new InvalidDataException("The rough-cut milestone belongs to another session.");
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		EditPlanDocumentValidator.ValidateAndNormalize(acceptedRoughCut);
		using AssemblyRuntimeLease? ownedLease = runtimeLease == null
			? AssemblyRuntimeLease.Acquire(sessionRoot, sessionId)
			: null;
		runtimeLease ??= ownedLease;
		await ResumePersistedPauseAsync(
			workspace,
			cancellationToken,
			runtimeLease);
		await RequireRoughCutLayoutAsync(
			acceptedRoughCut,
			workspace,
			cancellationToken);
		EnsurePolishing("Starting separate effects and audio approval passes.");

		(EffectsPassPlan effectsPlan, string effectsHash, AcceptedPolishPass effects) =
			await RunEffectsAsync(
				roughCut.PlanSha256,
				request,
				acceptedRoughCut,
				workspace,
				cancellationToken,
				runtimeLease);
		SaveAcceptedPolishBaseline(
			"effects",
			PolishPassKind.Effects,
			acceptedRoughCut);
		AcceptedPolishPass audio = await RunAudioAsync(
			roughCut.PlanSha256,
			effectsPlan,
			effectsHash,
			request,
			acceptedRoughCut,
			workspace,
			cancellationToken,
			runtimeLease);
		CandidateMaterializationBaseline finalBaseline =
			SaveAcceptedPolishBaseline(
				"audio",
				PolishPassKind.Audio,
				acceptedRoughCut);
		EnsurePolishing("Effects and audio passes accepted.");
		PublishState(
			AssemblyPhase.PolishAccepted,
			workspace,
			"Effects and audio passes are accepted. Review the complete result before final promotion.");
		publisher.TransitionTo(
			EditSessionState.FinalReview,
			"All post-sync polish passes are accepted and ready for final review.");
		PublishState(
			AssemblyPhase.FinalReview,
			workspace,
			"Review the completed candidate in VEGAS, then explicitly finalize the montage.");
		return new PolishWorkflowResult
		{
			Effects = effects,
			Audio = audio,
			EffectsPlan = effectsPlan,
			EffectsPlanSha256 = effectsHash,
			FinalBaseline = finalBaseline
		};
	}

	private async Task<(EffectsPassPlan Plan, string Hash, AcceptedPolishPass Accepted)>
		RunEffectsAsync(
			string roughCutHash,
			EditPlanningRequest request,
			EditPlanDocument acceptedRoughCut,
			CandidateWorkspaceId workspace,
			CancellationToken cancellationToken,
			AssemblyRuntimeLease? runtimeLease)
	{
		while (true)
		{
			PolishPassStateRecord? state =
				artifacts.ReadLatestState(PolishPassKind.Effects);
			if (state == null)
			{
				(EffectsPassPlan _, string _) = workflow.CreateEffectsPlan(
					1, roughCutHash, acceptedRoughCut);
				continue;
			}
			CompleteRecoveredPolishAction(
				state,
				PolishPassKind.Effects);
			EffectsPassPlan plan = artifacts.ReadEffectsPlan(state.PlanRevision) ??
				throw new InvalidDataException("The current effects plan is missing.");
			if (state.Status == PolishPlanStatus.Rejected)
			{
				if (artifacts.ReadRejected(
					PolishPassKind.Effects, state.PlanSha256) != null)
					await EnsureBaselineRestoredAsync(
						PolishPassKind.Effects,
						state.PlanSha256,
						roughCutHash,
						"",
						request,
						acceptedRoughCut,
						null,
						workspace,
						cancellationToken);
				(EffectsPassPlan skipped, string hash) = workflow.CreateEffectsPlan(
					state.PlanRevision + 1,
					roughCutHash,
					acceptedRoughCut,
					new PolishRendererCapabilities { ScreenPump = false });
				workflow.Decide(
					skipped,
					hash,
					PolishApprovalDisposition.Approve,
					"workbench-user",
					"Explicit evidence-backed effects skip.");
				continue;
			}
			if (state.Status == PolishPlanStatus.AwaitingApproval)
			{
				AssemblySessionState review = AwaitReview(
					AssemblyPhase.EffectsPlanReview,
					workspace,
					$"Review effects plan revision {state.PlanRevision}: " +
					$"{plan.Actions.Count} executable native screen-pump action(s).");
				AssemblyAction action = await WaitAsync(
					review, cancellationToken, runtimeLease, state);
				if (await HandleLifecycleAsync(
					action, review, state, cancellationToken, runtimeLease))
					continue;
				if (action.Kind == AssemblyActionKind.ApproveEffectsPlan)
				{
					workflow.Decide(
						plan, state.PlanSha256, PolishApprovalDisposition.Approve,
						"workbench-user", action.Instruction);
					actionExecutions.Complete(action, "effects-plan-approved");
				}
				else if (action.Kind == AssemblyActionKind.SkipEffectsPlan)
				{
					workflow.Decide(
						plan, state.PlanSha256, PolishApprovalDisposition.Reject,
						"workbench-user", action.Instruction);
					actionExecutions.Complete(action, "effects-plan-skipped");
				}
				else
				{
					PublishState(
						AssemblyPhase.EffectsPlanReview,
						workspace,
						"Choose Approve effects plan or Skip effects pass.");
					actionExecutions.Complete(action, "unsupported-effects-plan-action");
				}
				continue;
			}
			if (state.Status is PolishPlanStatus.Approved or
				PolishPlanStatus.Materializing or PolishPlanStatus.Failed)
			{
				PolishPassMaterialization? persisted =
					artifacts.ReadMaterialization(
						PolishPassKind.Effects, state.PlanRevision);
				if (state.Status == PolishPlanStatus.Failed &&
					persisted != null &&
					persisted.FullyApplied)
				{
					await RenderPreviewAsync(
						PolishPassKind.Effects,
						state,
						persisted.Snapshot,
						workspace,
						cancellationToken);
				}
				else
				{
					EnsurePolishing("Applying the exact approved effects plan.");
					PublishState(
						AssemblyPhase.EffectsMaterializing,
						workspace,
						"Applying approved native VEGAS effects.");
					PolishPassMaterialization applied =
						await workflow.ApplyEffectsAsync(
							plan,
							state.PlanSha256,
							workspace,
							acceptedRoughCut,
							cancellationToken);
					if (!applied.FullyApplied)
						throw new InvalidOperationException(
							"The effects pass was only partially applied and cannot continue.");
				}
				continue;
			}
			if (state.Status == PolishPlanStatus.Applied)
			{
				PolishPassMaterialization applied =
					artifacts.ReadMaterialization(
						PolishPassKind.Effects, state.PlanRevision) ??
					throw new InvalidDataException(
						"The applied effects snapshot is missing.");
				await RenderPreviewAsync(
					PolishPassKind.Effects,
					state,
					applied.Snapshot,
					workspace,
					cancellationToken);
				continue;
			}
			if (state.Status == PolishPlanStatus.PreviewReady)
			{
				AssemblySessionState review = AwaitReview(
					AssemblyPhase.EffectsPreviewReview,
					workspace,
					"Review the complete rendered effects preview. Accept it or skip the effects pass and restore the rough-cut baseline.");
				AssemblyAction action = await WaitAsync(
					review, cancellationToken, runtimeLease, state);
				if (await HandleLifecycleAsync(
					action, review, state, cancellationToken, runtimeLease))
					continue;
				if (action.Kind == AssemblyActionKind.AcceptEffectsPreview)
				{
					PolishPassMaterialization materialization =
						artifacts.ReadMaterialization(
							PolishPassKind.Effects,
							state.PlanRevision) ??
						throw new InvalidDataException(
							"The effects materialization snapshot is missing.");
					await RequireExactLiveSnapshotAsync(
						materialization.Snapshot,
						workspace,
						"effects preview",
						cancellationToken);
					AcceptedPolishPass accepted = workflow.Accept(
						PolishPassKind.Effects,
						state.PlanRevision,
						state.PlanSha256,
						"workbench-user");
					actionExecutions.Complete(action, "effects-preview-accepted");
					EnsurePolishing("Effects pass accepted.");
					return (plan, state.PlanSha256, accepted);
				}
				if (action.Kind == AssemblyActionKind.SkipEffectsPreview)
				{
					workflow.RejectPreview(
						PolishPassKind.Effects,
						state.PlanRevision,
						state.PlanSha256,
						"workbench-user",
						action.Instruction);
					await EnsureBaselineRestoredAsync(
						PolishPassKind.Effects,
						state.PlanSha256,
						roughCutHash,
						"",
						request,
						acceptedRoughCut,
						null,
						workspace,
						cancellationToken);
					actionExecutions.Complete(action, "effects-preview-skipped");
					EnsurePolishing("Effects preview rejected; rough-cut baseline restored.");
					continue;
				}
				PublishState(
					AssemblyPhase.EffectsPreviewReview,
					workspace,
					"Choose Accept effects preview or Skip effects pass.");
				actionExecutions.Complete(action, "unsupported-effects-preview-action");
				continue;
			}
			if (state.Status == PolishPlanStatus.Accepted)
			{
				if (artifacts.ReadLatestState(PolishPassKind.Audio) == null)
				{
					PolishPassMaterialization materialization =
						artifacts.ReadMaterialization(
							PolishPassKind.Effects,
							state.PlanRevision) ??
						throw new InvalidDataException(
							"The accepted effects materialization is missing.");
					await RequireExactLiveSnapshotAsync(
						materialization.Snapshot,
						workspace,
						"accepted effects pass",
						cancellationToken);
				}
				AcceptedPolishPass accepted = artifacts.ReadAccepted(
					PolishPassKind.Effects, state.PlanSha256) ??
					throw new InvalidDataException(
						"The accepted effects milestone is missing.");
				return (plan, state.PlanSha256, accepted);
			}
			throw new InvalidOperationException(
				"Effects pass cannot recover from state " + state.Status + ".");
		}
	}

	private async Task<AcceptedPolishPass> RunAudioAsync(
		string roughCutHash,
		EffectsPassPlan effectsPlan,
		string effectsHash,
		EditPlanningRequest request,
		EditPlanDocument acceptedRoughCut,
		CandidateWorkspaceId workspace,
		CancellationToken cancellationToken,
		AssemblyRuntimeLease? runtimeLease)
	{
		while (true)
		{
			PolishPassStateRecord? state =
				artifacts.ReadLatestState(PolishPassKind.Audio);
			if (state == null)
			{
				(AudioPassPlan _, string _) = workflow.CreateAudioPlan(
					1,
					roughCutHash,
					effectsHash,
					request.SongPath,
					acceptedRoughCut);
				continue;
			}
			CompleteRecoveredPolishAction(
				state,
				PolishPassKind.Audio);
			AudioPassPlan plan = artifacts.ReadAudioPlan(state.PlanRevision) ??
				throw new InvalidDataException("The current audio plan is missing.");
			if (state.Status == PolishPlanStatus.Rejected)
			{
				if (artifacts.ReadRejected(
					PolishPassKind.Audio, state.PlanSha256) != null)
					await EnsureBaselineRestoredAsync(
						PolishPassKind.Audio,
						state.PlanSha256,
						roughCutHash,
						effectsHash,
						request,
						acceptedRoughCut,
						effectsPlan,
						workspace,
						cancellationToken);
				(AudioPassPlan skipped, string hash) = workflow.CreateAudioPlan(
					state.PlanRevision + 1,
					roughCutHash,
					effectsHash,
					request.SongPath,
					acceptedRoughCut,
					new PolishRendererCapabilities
					{
						SongTrack = false,
						ReviewedGunHitSfx = false
					});
				workflow.Decide(
					skipped,
					hash,
					PolishApprovalDisposition.Approve,
					"workbench-user",
					"Explicit evidence-backed audio/SFX skip.");
				continue;
			}
			if (state.Status == PolishPlanStatus.AwaitingApproval)
			{
				AssemblySessionState review = AwaitReview(
					AssemblyPhase.AudioPlanReview,
					workspace,
					$"Review audio plan revision {state.PlanRevision}: " +
					$"{plan.Sfx.Count} reviewed hit-SFX action(s), " +
					(plan.Song == null ? "no song action." : "one song action."));
				AssemblyAction action = await WaitAsync(
					review, cancellationToken, runtimeLease, state);
				if (await HandleLifecycleAsync(
					action, review, state, cancellationToken, runtimeLease))
					continue;
				if (action.Kind == AssemblyActionKind.ApproveAudioPlan)
				{
					workflow.Decide(
						plan, state.PlanSha256, PolishApprovalDisposition.Approve,
						"workbench-user", action.Instruction);
					actionExecutions.Complete(action, "audio-plan-approved");
				}
				else if (action.Kind == AssemblyActionKind.SkipAudioPlan)
				{
					workflow.Decide(
						plan, state.PlanSha256, PolishApprovalDisposition.Reject,
						"workbench-user", action.Instruction);
					actionExecutions.Complete(action, "audio-plan-skipped");
				}
				else
				{
					PublishState(
						AssemblyPhase.AudioPlanReview,
						workspace,
						"Choose Approve audio plan or Skip audio/SFX pass.");
					actionExecutions.Complete(action, "unsupported-audio-plan-action");
				}
				continue;
			}
			if (state.Status is PolishPlanStatus.Approved or
				PolishPlanStatus.Materializing or PolishPlanStatus.Failed)
			{
				if (state.Status == PolishPlanStatus.Approved)
				{
					PolishPassStateRecord acceptedEffectsState =
						artifacts.ReadLatestState(PolishPassKind.Effects) ??
						throw new InvalidDataException(
							"The audio pass has no accepted effects state.");
					if (acceptedEffectsState.Status != PolishPlanStatus.Accepted ||
						!string.Equals(
							acceptedEffectsState.PlanSha256,
							effectsHash,
							StringComparison.OrdinalIgnoreCase))
						throw new InvalidDataException(
							"The audio pass is not bound to the accepted effects state.");
					PolishPassMaterialization effectsMaterialization =
						artifacts.ReadMaterialization(
							PolishPassKind.Effects,
							acceptedEffectsState.PlanRevision) ??
						throw new InvalidDataException(
							"The accepted effects snapshot is missing.");
					await RequireExactLiveSnapshotAsync(
						effectsMaterialization.Snapshot,
						workspace,
						"accepted effects baseline",
						cancellationToken);
				}
				PolishPassMaterialization? persisted =
					artifacts.ReadMaterialization(
						PolishPassKind.Audio, state.PlanRevision);
				if (state.Status == PolishPlanStatus.Failed &&
					persisted != null &&
					persisted.FullyApplied)
				{
					await RenderPreviewAsync(
						PolishPassKind.Audio,
						state,
						persisted.Snapshot,
						workspace,
						cancellationToken);
				}
				else
				{
					EnsurePolishing("Applying the exact approved audio plan.");
					PublishState(
						AssemblyPhase.AudioMaterializing,
						workspace,
						"Applying approved song and reviewed hit-SFX actions.");
					PolishPassMaterialization applied =
						await workflow.ApplyAudioAsync(
							plan,
							state.PlanSha256,
							workspace,
							acceptedRoughCut,
							cancellationToken);
					if (!applied.FullyApplied)
						throw new InvalidOperationException(
							"The audio pass was only partially applied and cannot continue.");
				}
				continue;
			}
			if (state.Status == PolishPlanStatus.Applied)
			{
				PolishPassMaterialization applied =
					artifacts.ReadMaterialization(
						PolishPassKind.Audio, state.PlanRevision) ??
					throw new InvalidDataException(
						"The applied audio snapshot is missing.");
				await RenderPreviewAsync(
					PolishPassKind.Audio,
					state,
					applied.Snapshot,
					workspace,
					cancellationToken);
				continue;
			}
			if (state.Status == PolishPlanStatus.PreviewReady)
			{
				AssemblySessionState review = AwaitReview(
					AssemblyPhase.AudioPreviewReview,
					workspace,
					"Review the complete rendered audio/SFX preview. Accept it or skip audio/SFX and restore the accepted effects baseline.");
				AssemblyAction action = await WaitAsync(
					review, cancellationToken, runtimeLease, state);
				if (await HandleLifecycleAsync(
					action, review, state, cancellationToken, runtimeLease))
					continue;
				if (action.Kind == AssemblyActionKind.AcceptAudioPreview)
				{
					PolishPassMaterialization materialization =
						artifacts.ReadMaterialization(
							PolishPassKind.Audio,
							state.PlanRevision) ??
						throw new InvalidDataException(
							"The audio materialization snapshot is missing.");
					await RequireExactLiveSnapshotAsync(
						materialization.Snapshot,
						workspace,
						"audio preview",
						cancellationToken);
					AcceptedPolishPass accepted = workflow.Accept(
						PolishPassKind.Audio,
						state.PlanRevision,
						state.PlanSha256,
						"workbench-user");
					actionExecutions.Complete(action, "audio-preview-accepted");
					EnsurePolishing("Audio/SFX pass accepted.");
					return accepted;
				}
				if (action.Kind == AssemblyActionKind.SkipAudioPreview)
				{
					workflow.RejectPreview(
						PolishPassKind.Audio,
						state.PlanRevision,
						state.PlanSha256,
						"workbench-user",
						action.Instruction);
					await EnsureBaselineRestoredAsync(
						PolishPassKind.Audio,
						state.PlanSha256,
						roughCutHash,
						effectsHash,
						request,
						acceptedRoughCut,
						effectsPlan,
						workspace,
						cancellationToken);
					actionExecutions.Complete(action, "audio-preview-skipped");
					EnsurePolishing(
						"Audio preview rejected; accepted effects baseline restored.");
					continue;
				}
				PublishState(
					AssemblyPhase.AudioPreviewReview,
					workspace,
					"Choose Accept audio preview or Skip audio/SFX pass.");
				actionExecutions.Complete(action, "unsupported-audio-preview-action");
				continue;
			}
			if (state.Status == PolishPlanStatus.Accepted)
			{
				PolishPassMaterialization materialization =
					artifacts.ReadMaterialization(
						PolishPassKind.Audio,
						state.PlanRevision) ??
					throw new InvalidDataException(
						"The accepted audio materialization is missing.");
				await RequireExactLiveSnapshotAsync(
					materialization.Snapshot,
					workspace,
					"accepted audio pass",
					cancellationToken);
				return artifacts.ReadAccepted(
						PolishPassKind.Audio, state.PlanSha256) ??
					throw new InvalidDataException(
						"The accepted audio milestone is missing.");
			}
			throw new InvalidOperationException(
				"Audio pass cannot recover from state " + state.Status + ".");
		}
	}

	private async Task RenderPreviewAsync(
		PolishPassKind pass,
		PolishPassStateRecord state,
		CandidateTimelineSnapshot timeline,
		CandidateWorkspaceId workspace,
		CancellationToken cancellationToken)
	{
		if (!string.Equals(
			timeline.Workspace?.ToString(), workspace.ToString(),
			StringComparison.Ordinal))
			throw new InvalidDataException(
				"The polish materialization snapshot targets another workspace.");
		if (publisher.State != EditSessionState.Rendering)
			publisher.TransitionTo(
				EditSessionState.Rendering,
				"Rendering the complete post-pass preview.");
		await workflow.RenderPreviewAsync(
			pass,
			state.PlanRevision,
			state.PlanSha256,
			state.PlanId,
			timeline,
			cancellationToken);
		publisher.TransitionTo(
			EditSessionState.Reviewing,
			"Post-pass preview rendered.");
	}

	private async Task EnsureBaselineRestoredAsync(
		PolishPassKind rejectedPass,
		string rejectedPlanSha256,
		string roughCutSha256,
		string acceptedEffectsSha256,
		EditPlanningRequest request,
		EditPlanDocument acceptedRoughCut,
		EffectsPassPlan? effectsPlan,
		CandidateWorkspaceId workspace,
		CancellationToken cancellationToken)
	{
		PolishBaselineRestoration? completed =
			artifacts.ReadRestorationReceipt(rejectedPass, rejectedPlanSha256);
		if (completed != null)
		{
			ValidateRestorationBinding(
				completed,
				rejectedPass,
				rejectedPlanSha256,
				roughCutSha256,
				acceptedEffectsSha256,
				workspace);
			CandidateTimelineSnapshot live = await ReadLiveSnapshotAsync(
				workspace,
				"polish-restoration-" +
					rejectedPlanSha256[..16],
				cancellationToken);
			if (!string.Equals(
				SnapshotHash(live),
				completed.RestoredSnapshotSha256,
				StringComparison.OrdinalIgnoreCase))
				RequireRecovery(
					"The candidate diverged after the recorded polish baseline " +
					"restoration. Restore and verify the baseline again.");
			return;
		}
		PolishBaselineRestoration intent =
			artifacts.ReadRestorationIntent(rejectedPass, rejectedPlanSha256) ??
			new PolishBaselineRestoration
			{
				SessionId = sessionId,
				RejectedPass = rejectedPass,
				RejectedPlanSha256 = rejectedPlanSha256,
				BaselineRoughCutSha256 = roughCutSha256,
				AcceptedEffectsSha256 = acceptedEffectsSha256,
				AttemptId = "restore-" + Guid.NewGuid().ToString("N"),
				Workspace = workspace,
				CreatedUtc = DateTimeOffset.UtcNow
			};
		ValidateRestorationBinding(
			intent,
			rejectedPass,
			rejectedPlanSha256,
			roughCutSha256,
			acceptedEffectsSha256,
			workspace);
		artifacts.SaveRestorationIntent(intent);

		await automation.ExecuteAsync<CleanupCandidateRequest, CleanupCandidateResult>(
			VegasOperations.CleanupCandidate,
			new CleanupCandidateRequest { Workspace = workspace },
			intent.AttemptId + "-cleanup",
			cancellationToken: cancellationToken);
		MaterializeCandidateResult restored =
			await automation.ExecuteAsync<
				MaterializeCandidateRequest,
				MaterializeCandidateResult>(
				VegasOperations.MaterializeCandidate,
				new MaterializeCandidateRequest
				{
					Workspace = workspace,
					Plan = acceptedRoughCut,
					SongPath = request.SongPath
				},
				intent.AttemptId + "-rough-cut",
				cancellationToken: cancellationToken);
		CandidateTimelineSnapshot snapshot = restored?.Snapshot ??
			throw new InvalidOperationException(
				"VEGAS did not restore the accepted rough-cut workspace.");
		if (rejectedPass == PolishPassKind.Audio)
		{
			if (effectsPlan == null)
				throw new InvalidOperationException(
					"Audio baseline restoration requires the accepted effects plan.");
			ApplyCandidateEffectsResult response =
				await automation.ExecuteAsync<
					ApplyCandidateEffectsRequest,
					ApplyCandidateEffectsResult>(
				VegasOperations.ApplyCandidateEffects,
				new ApplyCandidateEffectsRequest
				{
					Workspace = workspace,
					Plan = acceptedRoughCut,
					Effects = effectsPlan,
					PlanSha256 = acceptedEffectsSha256
				},
				intent.AttemptId + "-effects",
				cancellationToken: cancellationToken);
			if (response?.Materialization == null ||
				!response.Materialization.FullyApplied)
				throw new InvalidOperationException(
					"VEGAS did not restore the accepted effects baseline.");
			snapshot = response.Materialization.Snapshot;
		}
		ValidateRestoredSnapshot(
			snapshot,
			acceptedRoughCut,
			workspace,
			rejectedPass);
		PolishBaselineRestoration receipt = new()
		{
			SessionId = intent.SessionId,
			RejectedPass = intent.RejectedPass,
			RejectedPlanSha256 = intent.RejectedPlanSha256,
			BaselineRoughCutSha256 = intent.BaselineRoughCutSha256,
			AcceptedEffectsSha256 = intent.AcceptedEffectsSha256,
			AttemptId = intent.AttemptId,
			Workspace = intent.Workspace,
			Completed = true,
			RestoredSnapshotSha256 = SnapshotHash(snapshot),
			CreatedUtc = intent.CreatedUtc,
			CompletedUtc = DateTimeOffset.UtcNow
		};
		artifacts.SaveRestorationReceipt(receipt);
	}

	private static void ValidateRestorationBinding(
		PolishBaselineRestoration value,
		PolishPassKind rejectedPass,
		string rejectedPlanSha256,
		string roughCutSha256,
		string acceptedEffectsSha256,
		CandidateWorkspaceId workspace)
	{
		PolishPassContractValidator.Validate(value);
		if (value.RejectedPass != rejectedPass ||
			!string.Equals(
				value.RejectedPlanSha256,
				rejectedPlanSha256,
				StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(
				value.BaselineRoughCutSha256,
				roughCutSha256,
				StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(
				value.AcceptedEffectsSha256,
				acceptedEffectsSha256,
				StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(
				value.Workspace.ToString(),
				workspace.ToString(),
				StringComparison.Ordinal))
			throw new InvalidDataException(
				"The persisted baseline restoration targets different polish state.");
	}

	private void ValidateRestoredSnapshot(
		CandidateTimelineSnapshot snapshot,
		EditPlanDocument acceptedRoughCut,
		CandidateWorkspaceId workspace,
		PolishPassKind rejectedPass)
	{
		if (snapshot?.Workspace == null ||
			!string.Equals(
				snapshot.Workspace.ToString(),
				workspace.ToString(),
				StringComparison.Ordinal))
			throw new InvalidOperationException(
				"VEGAS restored a different candidate workspace.");
		EditPlanDocument reconciled =
			ContractSerializer.Deserialize<EditPlanDocument>(
				ContractSerializer.Serialize(acceptedRoughCut));
		AssemblyTimelineReconciler.Apply(
			reconciled,
			snapshot,
			acceptedRoughCut.Montage.Placements.Count,
			rejectedPass == PolishPassKind.Audio
				? new AssemblyArtifactStore(sessionRoot)
					.ReadRoughCutBaseline(
						"effects",
						acceptedRoughCut.Montage.Placements.Count)
					?? throw new InvalidDataException(
						"The accepted effects timeline baseline is missing.")
				: RequireAcceptedRoughCutBaseline());
		EditPlanDocumentValidator.ValidateAndNormalize(reconciled);
	}

	private static string SnapshotHash(CandidateTimelineSnapshot snapshot)
	{
		using SHA256 sha = SHA256.Create();
		return Convert.ToHexString(
			sha.ComputeHash(
				Encoding.UTF8.GetBytes(ContractSerializer.Serialize(snapshot))))
			.ToLowerInvariant();
	}

	private async Task RequireRoughCutLayoutAsync(
		EditPlanDocument acceptedRoughCut,
		CandidateWorkspaceId workspace,
		CancellationToken cancellationToken)
	{
		CandidateTimelineSnapshot live = await ReadLiveSnapshotAsync(
			workspace,
			"polish-layout",
			cancellationToken);
		EditPlanDocument comparison =
			ContractSerializer.Deserialize<EditPlanDocument>(
				ContractSerializer.Serialize(acceptedRoughCut));
		TimelineAdjustmentDelta delta =
			AssemblyTimelineReconciler.Apply(
				comparison,
				live,
				acceptedRoughCut.Montage.Placements.Count,
				RequireAcceptedRoughCutBaseline());
		if (delta.Changes.Count == 0) return;
		RequireRecovery(
			"The live VEGAS placement layout changed after rough-cut acceptance. " +
			"Restore or explicitly re-audit the rough cut before polishing.");
	}

	private CandidateMaterializationBaseline RequireAcceptedRoughCutBaseline() =>
		acceptedRoughCutBaseline ??
			throw new InvalidDataException(
				"The exact accepted rough-cut timeline baseline is missing.");

	private CandidateMaterializationBaseline SaveAcceptedPolishBaseline(
		string stage,
		PolishPassKind pass,
		EditPlanDocument acceptedRoughCut)
	{
		PolishPassStateRecord state = artifacts.ReadLatestState(pass)
			?? throw new InvalidDataException(
				"The accepted polish pass state is missing.");
		if (state.Status != PolishPlanStatus.Accepted)
			throw new InvalidDataException(
				"A candidate baseline can be captured only from an accepted pass.");
		PolishPassMaterialization materialization =
			artifacts.ReadMaterialization(pass, state.PlanRevision)
			?? throw new InvalidDataException(
				"The accepted polish materialization is missing.");
		if (!materialization.FullyApplied ||
			materialization.Snapshot == null)
			throw new InvalidDataException(
				"The accepted polish materialization has no complete snapshot.");
		return new AssemblyArtifactStore(sessionRoot).SaveRoughCutBaseline(
			stage,
			acceptedRoughCut.Montage.Placements.Count,
			PlanSha256(acceptedRoughCut),
			materialization.Snapshot);
	}

	private static string PlanSha256(EditPlanDocument plan) =>
		Convert.ToHexString(
			SHA256.HashData(
				new UTF8Encoding(false).GetBytes(
					EditPlanDocumentSerializer.SerializePlan(plan))))
			.ToLowerInvariant();

	private async Task RequireExactLiveSnapshotAsync(
		CandidateTimelineSnapshot expected,
		CandidateWorkspaceId workspace,
		string boundary,
		CancellationToken cancellationToken)
	{
		CandidateTimelineSnapshot live = await ReadLiveSnapshotAsync(
			workspace,
			"polish-boundary-" +
				boundary.Replace(' ', '-').ToLowerInvariant(),
			cancellationToken);
		if (string.Equals(
			SnapshotHash(expected),
			SnapshotHash(live),
			StringComparison.OrdinalIgnoreCase))
			return;
		RequireRecovery(
			"The live VEGAS candidate diverged from the rendered " + boundary +
			". Reapply and render the exact pass before accepting it.");
	}

	private Task<CandidateTimelineSnapshot> ReadLiveSnapshotAsync(
		CandidateWorkspaceId workspace,
		string purpose,
		CancellationToken cancellationToken) =>
		automation.ExecuteAsync<
			GetCandidateSnapshotRequest,
			CandidateTimelineSnapshot>(
			VegasOperations.GetCandidateSnapshot,
			new GetCandidateSnapshotRequest { Workspace = workspace },
			purpose + "-" + Guid.NewGuid().ToString("N"),
			cancellationToken: cancellationToken);

	private void RequireRecovery(string message)
	{
		if (publisher.State != EditSessionState.NeedsRecovery)
			publisher.TransitionTo(EditSessionState.NeedsRecovery, message);
		throw new InvalidOperationException(message);
	}

	private AssemblySessionState AwaitReview(
		AssemblyPhase phase,
		CandidateWorkspaceId workspace,
		string status)
	{
		if (publisher.State == EditSessionState.Rendering)
			publisher.TransitionTo(
				EditSessionState.Reviewing,
				"Polish preview is ready for review.");
		if (publisher.State == EditSessionState.Polishing ||
			publisher.State == EditSessionState.Reviewing ||
			publisher.State == EditSessionState.NeedsRecovery)
			publisher.TransitionTo(
				EditSessionState.AwaitingUser,
				"Awaiting an explicit polish-pass decision.");
		return PublishState(phase, workspace, status);
	}

	private async Task<AssemblyAction> WaitAsync(
		AssemblySessionState state,
		CancellationToken cancellationToken,
		AssemblyRuntimeLease? runtimeLease,
		PolishPassStateRecord? passState = null)
	{
		AssemblyActionExecutionStart? pending =
			actionExecutions.ReadPendingForRecovery(sessionId);
		if (pending != null)
		{
			RequirePolishActionBinding(pending, passState);
			bool exact =
				pending.Action.Checkpoint == state.Checkpoint &&
				pending.Action.ExpectedStateRevision == state.StateRevision &&
				pending.Phase == state.Phase;
			bool reflectedReviewRepublish =
				passState != null &&
				pending.Action.Checkpoint == state.Checkpoint &&
				pending.Action.ExpectedStateRevision < state.StateRevision &&
				pending.Phase == state.Phase;
			if (!exact && !reflectedReviewRepublish)
				throw new InvalidDataException(
					"A pending polish action targets another exact review state.");
			return pending.Action;
		}
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Action<AssemblyAction> begin = candidate =>
				actionExecutions.Begin(
					candidate,
					state.Phase,
					passState?.PlanSha256 ?? "",
					passState?.PlanRevision ?? 0);
			AssemblyAction? action = actions.TryRecoverClaimed(
				state.Checkpoint,
				sessionId,
				state.StateRevision,
				begin);
			action ??= actions.TryConsume(
				state.Checkpoint, sessionId, state.StateRevision, begin);
			if (action != null) return action;
			await Task.Delay(PollInterval, cancellationToken);
		}
	}

	private async Task ResumePersistedPauseAsync(
		CandidateWorkspaceId workspace,
		CancellationToken cancellationToken,
		AssemblyRuntimeLease? runtimeLease)
	{
		AssemblySessionState? paused = actions.ReadState();
		if (paused?.Phase == AssemblyPhase.Abandoned)
		{
			AssemblyActionExecutionStart? reflected =
				actionExecutions.ReadPendingForRecovery(sessionId);
			if (reflected != null)
			{
				if (reflected.Action.Kind != AssemblyActionKind.AbandonSession)
					throw new InvalidDataException(
						"An abandoned polish state has a non-abandon action pending.");
				RequirePolishActionBinding(
					reflected,
					CurrentPolishBinding());
				await AbandonAsync(
					workspace,
					reflected.Action,
					"Recovered a reflected polish abandon action.",
					cancellationToken);
			}
			if (publisher.State != EditSessionState.Cancelled)
				publisher.TransitionTo(
					EditSessionState.Cancelled,
					"Recovered an already completed polish abandon action.");
			throw new AssemblySessionAbandonedException(
				"AI editing session was already abandoned during polish review.");
		}
		if (paused?.Phase != AssemblyPhase.Paused) return;

		AssemblyActionExecutionStart? pending =
			actionExecutions.ReadPendingForRecovery(sessionId);
		if (pending?.Action.Kind == AssemblyActionKind.PauseSession &&
			pending.Action.Checkpoint == paused.Checkpoint &&
			pending.Action.ExpectedStateRevision < paused.StateRevision)
		{
			actionExecutions.Complete(
				pending.Action,
				"recovered-reflected-polish-pause");
			pending = actionExecutions.ReadPendingForRecovery(sessionId);
		}
		if (pending?.Action.Kind == AssemblyActionKind.ResumeSession &&
			publisher.State != EditSessionState.Paused)
		{
			actionExecutions.Complete(
				pending.Action,
				"recovered-reflected-polish-resume");
			return;
		}

		while (true)
		{
			PolishPassStateRecord? passState = CurrentPolishBinding();
			AssemblyAction action = await WaitAsync(
				paused,
				cancellationToken,
				runtimeLease,
				passState);
			if (action.Kind == AssemblyActionKind.ResumeSession)
			{
				if (publisher.State == EditSessionState.Paused)
					publisher.TransitionTo(
						EditSessionState.Polishing,
						"Polish review resumed from its persisted pause.");
				actionExecutions.Complete(
					action,
					"polish-review-resumed");
				return;
			}
			if (action.Kind == AssemblyActionKind.AbandonSession)
			{
				await AbandonAsync(
					workspace,
					action,
					"Polish workflow abandoned from its persisted pause.",
					cancellationToken);
			}
			actionExecutions.Complete(
				action,
				"ignored-while-polish-paused");
		}
	}

	private void CompleteRecoveredPolishAction(
		PolishPassStateRecord state,
		PolishPassKind pass)
	{
		AssemblyActionExecutionStart? pending =
			actionExecutions.ReadPendingForRecovery(sessionId);
		if (pending == null) return;
		PolishPassKind? actionPass = PolishActionPass(pending.Action.Kind);
		if (actionPass != null && actionPass != pass) return;
		RequirePolishActionBinding(pending, state);
		bool completed = pending.Action.Kind switch
		{
			AssemblyActionKind.ApproveEffectsPlan
				when pass == PolishPassKind.Effects =>
				state.Status != PolishPlanStatus.AwaitingApproval,
			AssemblyActionKind.SkipEffectsPlan
				when pass == PolishPassKind.Effects =>
				state.Status != PolishPlanStatus.AwaitingApproval,
			AssemblyActionKind.AcceptEffectsPreview
				when pass == PolishPassKind.Effects =>
				state.Status == PolishPlanStatus.Accepted,
			AssemblyActionKind.SkipEffectsPreview
				when pass == PolishPassKind.Effects =>
				state.Status == PolishPlanStatus.Rejected,
			AssemblyActionKind.ApproveAudioPlan
				when pass == PolishPassKind.Audio =>
				state.Status != PolishPlanStatus.AwaitingApproval,
			AssemblyActionKind.SkipAudioPlan
				when pass == PolishPassKind.Audio =>
				state.Status != PolishPlanStatus.AwaitingApproval,
			AssemblyActionKind.AcceptAudioPreview
				when pass == PolishPassKind.Audio =>
				state.Status == PolishPlanStatus.Accepted,
			AssemblyActionKind.SkipAudioPreview
				when pass == PolishPassKind.Audio =>
				state.Status == PolishPlanStatus.Rejected,
			_ => false
		};
		if (completed)
			actionExecutions.Complete(
				pending.Action,
				"recovered-persisted-polish-state");
	}

	private async Task<bool> HandleLifecycleAsync(
		AssemblyAction action,
		AssemblySessionState review,
		PolishPassStateRecord passState,
		CancellationToken cancellationToken,
		AssemblyRuntimeLease? runtimeLease)
	{
		if (action.Kind == AssemblyActionKind.AbandonSession)
		{
			await AbandonAsync(
				review.Workspace,
				action,
				"Polish workflow explicitly abandoned.",
				cancellationToken);
		}
		if (action.Kind != AssemblyActionKind.PauseSession) return false;
		publisher.TransitionTo(
			EditSessionState.Paused,
			"Polish review paused at a durable boundary.");
		AssemblySessionState paused = PublishState(
			AssemblyPhase.Paused,
			review.Workspace,
			"Polish review paused. Resume or abandon the session.");
		actionExecutions.Complete(action, "polish-review-paused");
		while (true)
		{
			AssemblyAction next = await WaitAsync(
				paused, cancellationToken, runtimeLease, passState);
			if (next.Kind == AssemblyActionKind.ResumeSession)
			{
				publisher.TransitionTo(
					EditSessionState.Polishing,
					"Polish review resumed from durable artifacts.");
				actionExecutions.Complete(next, "polish-review-resumed");
				return true;
			}
			if (next.Kind == AssemblyActionKind.AbandonSession)
			{
				await AbandonAsync(
					review.Workspace,
					next,
					"Polish workflow abandoned while paused.",
					cancellationToken);
			}
			paused = PublishState(
				AssemblyPhase.Paused,
				review.Workspace,
				"Only Resume or Abandon is available while paused.");
			actionExecutions.Complete(next, "ignored-while-polish-paused");
		}
	}

	private async Task AbandonAsync(
		CandidateWorkspaceId workspace,
		AssemblyAction action,
		string reason,
		CancellationToken cancellationToken)
	{
		await CandidateAbandonCleanup.CleanupAsync(
			automation,
			workspace,
			action.ActionId,
			cancellationToken);
		if (actions.ReadState()?.Phase != AssemblyPhase.Abandoned)
			PublishState(
				AssemblyPhase.Abandoned,
				workspace,
				reason + " Candidate-owned tracks were removed; durable artifacts were retained.");
		if (publisher.State != EditSessionState.Cancelled)
			publisher.TransitionTo(
				EditSessionState.Cancelled,
				reason);
		if (!actionExecutions.IsComplete(action.ActionId))
			actionExecutions.Complete(
				action,
				"polish-review-abandoned");
		throw new AssemblySessionAbandonedException(
			"AI editing session was abandoned during polish review.");
	}

	private PolishPassStateRecord? CurrentPolishBinding()
	{
		PolishPassStateRecord? audio =
			artifacts.ReadLatestState(PolishPassKind.Audio);
		if (audio != null && audio.Status != PolishPlanStatus.Accepted)
			return audio;
		return artifacts.ReadLatestState(PolishPassKind.Effects);
	}

	private static void RequirePolishActionBinding(
		AssemblyActionExecutionStart pending,
		PolishPassStateRecord? state)
	{
		bool decision = pending.Action.Kind is
			AssemblyActionKind.ApproveEffectsPlan or
			AssemblyActionKind.SkipEffectsPlan or
			AssemblyActionKind.AcceptEffectsPreview or
			AssemblyActionKind.SkipEffectsPreview or
			AssemblyActionKind.ApproveAudioPlan or
			AssemblyActionKind.SkipAudioPlan or
			AssemblyActionKind.AcceptAudioPreview or
			AssemblyActionKind.SkipAudioPreview;
		if (!decision && state == null) return;
		if (state == null ||
			pending.ProposalRevision != state.PlanRevision ||
			!string.Equals(
				pending.PlanSha256,
				state.PlanSha256,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The pending polish action is not bound to the exact pass " +
				"revision and plan hash currently under review.");
	}

	private static PolishPassKind? PolishActionPass(AssemblyActionKind kind) =>
		kind switch
		{
			AssemblyActionKind.ApproveEffectsPlan or
			AssemblyActionKind.SkipEffectsPlan or
			AssemblyActionKind.AcceptEffectsPreview or
			AssemblyActionKind.SkipEffectsPreview => PolishPassKind.Effects,
			AssemblyActionKind.ApproveAudioPlan or
			AssemblyActionKind.SkipAudioPlan or
			AssemblyActionKind.AcceptAudioPreview or
			AssemblyActionKind.SkipAudioPreview => PolishPassKind.Audio,
			_ => null
		};

	private void EnsurePolishing(string reason)
	{
		if (publisher.State == EditSessionState.Polishing) return;
		publisher.TransitionTo(EditSessionState.Polishing, reason);
	}

	private AssemblySessionState PublishState(
		AssemblyPhase phase,
		CandidateWorkspaceId workspace,
		string status)
	{
		AssemblySessionState prior = actions.ReadState() ??
			throw new InvalidDataException(
				"Polish workflow requires the completed assembly state.");
		AssemblySessionState state = new()
		{
			SessionId = sessionId,
			Phase = phase,
			Checkpoint = prior.TotalClips,
			StateRevision = prior.StateRevision + 1,
			TotalClips = prior.TotalClips,
			CurrentClipPath = "",
			RemainingClipPaths = Array.Empty<string>(),
			Status = status,
			Workspace = workspace
		};
		actions.PublishState(state);
		return state;
	}
}
