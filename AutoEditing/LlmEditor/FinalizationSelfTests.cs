using System.Security.Cryptography;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.LlmEditor.Automation;
using AutoEditing.LlmEditor.Assembly;
using AutoEditing.LlmEditor.Finalization;
using AutoEditing.LlmEditor.RoughCut;
using AutoEditing.LlmEditor.Workbench;
using Core.Domain.Planning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AutoEditing.LlmEditor;

internal static class FinalizationSelfTests
{
	public static void Run(string testRoot, EditPlanDocument sourcePlan)
	{
		string root = Path.Combine(testRoot, "finalization");
		string sessionRoot = Path.Combine(root, "sessions", "session-final");
		string archiveRoot = Path.Combine(root, "archives");
		Directory.CreateDirectory(sessionRoot);
		File.WriteAllText(
			Path.Combine(sessionRoot, "usage.ndjson"),
			JsonConvert.SerializeObject(new InferenceUsageRecord
			{
				TimestampUtc = new DateTimeOffset(
					2026, 7, 27, 8, 0, 0, TimeSpan.Zero),
				SessionId = "session-final",
				Model = "local-model",
				Provider = "llama.cpp",
				Operation = "clip-step",
				PromptTokens = 100,
				CachedPromptTokens = 25,
				GeneratedTokens = 40,
				TotalTokens = 140,
				TimeToFirstTokenMilliseconds = 321,
				ContextTokensPeak = 2048
			}) + Environment.NewLine);
		File.WriteAllText(Path.Combine(sessionRoot, "inspectable.txt"), "kept");

		CandidateWorkspaceId workspace = new()
		{
			SessionId = "session-final",
			Iteration = 1,
			Nonce = "candidate"
		};
		CandidateTimelineSnapshot candidate = Snapshot(sourcePlan, workspace);
		FakeFinalizationAutomation automation = new(
			"project-fingerprint",
			candidate);
		FixedFinalizationClock clock = new();
		FinalizationService service = new(
			sessionRoot,
			archiveRoot,
			automation,
			new FakeFinalRenderHook(),
			clock);

		FinalizationResult result = service.FinalizeAsync(
				new FinalizationRequest
				{
					SessionId = "session-final",
					RequestId = sourcePlan.RequestId,
					ProjectFingerprint = "project-fingerprint",
					Workspace = workspace,
					FinalPlan = sourcePlan,
					AcceptedCandidateBaseline = Baseline(sourcePlan, candidate),
					RenderFinalPreview = true
				})
			.GetAwaiter().GetResult();
		Assert(automation.Operations.SequenceEqual(new[]
			{
				VegasOperations.GetCandidateSnapshot,
				VegasOperations.PromoteCandidate
			}), "Finalization did not validate before promotion.");
		TestUnsupportedFinalTimelineChanges(
			root,
			archiveRoot,
			sourcePlan,
			workspace,
			candidate);
		Assert(result.Report.ModelUsage.Count == 1 &&
			result.Report.ModelUsage[0].CachedPromptTokens == 25 &&
			result.Report.SessionUsage.TotalTokens == 140 &&
			result.Report.SessionUsage.Provider == "llama.cpp" &&
			result.Report.SessionUsage.TimeToFirstTokenSampleCount == 1 &&
			result.Report.ModelUsage[0].TimeToFirstTokenMilliseconds == 321 &&
			result.Report.ModelUsage[0].Cost == null &&
			result.Report.SessionUsage.Cost == null,
			"Final report did not aggregate session and per-model usage.");
		Assert(result.Report.FinalRender != null &&
			File.Exists(Path.Combine(sessionRoot,
				result.Report.FinalRender.RelativePath.Replace('/', Path.DirectorySeparatorChar))),
			"Optional final render evidence was not retained.");
		Assert(result.Recovery.Promotion.PromotedSnapshot.Tracks.All(track =>
			!track.Name.StartsWith(workspace.OwnershipPrefix + "|",
				StringComparison.Ordinal)),
			"Candidate-only labels survived final promotion.");
		Assert(result.Recovery.Promotion.TrackMappings.Count ==
			candidate.Tracks.Count,
			"Promotion did not retain a complete recoverable rename map.");
		Assert(File.Exists(Path.Combine(
			sessionRoot, "finalization", "promotion-intent.json")) &&
			File.Exists(Path.Combine(
				sessionRoot, "finalization", "recovery-bundle.json")) &&
			File.Exists(Path.Combine(
				sessionRoot, "finalization", "final-session-report.json")) &&
			File.Exists(result.Report.Archive.ArchivePath),
			"Finalization artifacts or archive are incomplete.");
		using (System.IO.Compression.ZipArchive archive =
			System.IO.Compression.ZipFile.OpenRead(
				result.Report.Archive.ArchivePath))
		{
			Assert(archive.Entries.Any(entry =>
				entry.FullName == "finalization/recovery-bundle.json") &&
				archive.Entries.Any(entry =>
					entry.FullName == "inspectable.txt"),
				"The final archive omitted recovery or session evidence.");
		}

		int completedOperationCount = automation.Operations.Count;
		FinalizationResult repeated = service.FinalizeAsync(
				new FinalizationRequest
				{
					SessionId = "session-final",
					RequestId = sourcePlan.RequestId,
					ProjectFingerprint = "project-fingerprint",
					Workspace = workspace,
					FinalPlan = sourcePlan,
					AcceptedCandidateBaseline = Baseline(sourcePlan, candidate),
					RenderFinalPreview = true
				})
			.GetAwaiter().GetResult();
		Assert(automation.Operations.Count == completedOperationCount &&
			repeated.Report.PromotionId == result.Report.PromotionId,
			"A completed finalization retry did not reuse durable evidence.");

		TestInterruptedFinalizationResume(
			root,
			sessionRoot,
			sourcePlan,
			workspace,
			candidate,
			clock);

		RollbackCandidatePromotionResult rollback =
			service.RollbackAsync().GetAwaiter().GetResult();
		Assert(rollback.RestoredSnapshotSha256 ==
			result.Recovery.Promotion.CandidateSnapshotSha256 &&
			automation.Operations.Last() ==
				VegasOperations.RollbackCandidatePromotion,
			"Recoverable promotion rollback did not restore the candidate snapshot.");
		Assert(File.Exists(Path.Combine(
			sessionRoot, "finalization", "rollback-result.json")),
			"Rollback evidence was not persisted.");
		int rollbackOperationCount = automation.Operations.Count;
		RollbackCandidatePromotionResult repeatedRollback =
			service.RollbackAsync().GetAwaiter().GetResult();
		Assert(automation.Operations.Count == rollbackOperationCount &&
			repeatedRollback.RestoredSnapshotSha256 ==
				rollback.RestoredSnapshotSha256,
			"A completed rollback retry did not reuse its exact receipt.");
		ExpectFailure(
			() => service.FinalizeAsync(
					new FinalizationRequest
					{
						SessionId = "session-final",
						RequestId = sourcePlan.RequestId,
						ProjectFingerprint = "project-fingerprint",
						Workspace = workspace,
						FinalPlan = sourcePlan,
						AcceptedCandidateBaseline = Baseline(sourcePlan, candidate),
						RenderFinalPreview = true
					})
				.GetAwaiter().GetResult(),
			"A rolled-back promotion was incorrectly reported as completed.");

		string finalRenderRoot = Path.Combine(
			root, "sessions", "final-render-hook");
		Directory.CreateDirectory(finalRenderRoot);
		RoughCutFinalRenderHook finalRenderHook = new(
			finalRenderRoot,
			new RoughCutFullRenderService(
				"session-final",
				finalRenderRoot,
				new FakeFinalChunkRenderer(finalRenderRoot)));
		FinalRenderArtifact chunkedRender = finalRenderHook.RenderAsync(
				new FinalRenderContext
				{
					SessionRoot = finalRenderRoot,
					SessionId = "session-final",
					Workspace = workspace,
					Snapshot = candidate,
					Plan = sourcePlan,
					FinalPlanSha256 = new string('a', 64)
				},
				CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(chunkedRender.ArtifactKind == "chunk-manifest" &&
			chunkedRender.Components.Count == 1 &&
			File.Exists(Path.Combine(
				finalRenderRoot,
				chunkedRender.RelativePath.Replace(
					'/', Path.DirectorySeparatorChar))),
			"The concrete bounded final-render hook did not retain a complete manifest.");

		string interruptedRoot = Path.Combine(
			root, "sessions", "session-interrupted");
		Directory.CreateDirectory(Path.Combine(
			interruptedRoot, "finalization"));
		File.Copy(
			Path.Combine(
				sessionRoot, "finalization", "promotion-intent.json"),
			Path.Combine(
				interruptedRoot, "finalization", "promotion-intent.json"));
		FakeFinalizationAutomation interruptedAutomation = new(
			"project-fingerprint",
			candidate);
		RollbackCandidatePromotionResult interruptedRollback =
			new FinalizationService(
					interruptedRoot,
					archiveRoot,
					interruptedAutomation,
					clock: clock)
				.RollbackAsync()
				.GetAwaiter().GetResult();
		Assert(interruptedRollback.RestoredSnapshotSha256 ==
			result.Recovery.Promotion.CandidateSnapshotSha256,
			"An interrupted promotion could not recover from its pre-mutation intent.");

		TestFinalReviewCoordinator(
			root,
			sourcePlan,
			workspace);
		TestFinalReviewAbandonLifecycle(
			root,
			sourcePlan,
			workspace);
		TestConsumedFinalReviewActionRecovery(
			root,
			sourcePlan,
			workspace);

		ExpectFailure(
			() => new FinalizationService(
					Path.Combine(root, "identity-mismatch"),
					archiveRoot,
					new FakeFinalizationAutomation("other-project", candidate))
				.FinalizeAsync(
					new FinalizationRequest
					{
						SessionId = "session-final",
						RequestId = sourcePlan.RequestId,
						ProjectFingerprint = "project-fingerprint",
						Workspace = workspace,
						FinalPlan = sourcePlan,
						AcceptedCandidateBaseline = Baseline(sourcePlan, candidate)
					})
				.GetAwaiter().GetResult(),
			"Finalization accepted an automation client bound to another project.");
	}

	private static void TestFinalReviewAbandonLifecycle(
		string root,
		EditPlanDocument plan,
		CandidateWorkspaceId sourceWorkspace)
	{
		string sessionsRoot = Path.Combine(
			root,
			"coordinator-abandon-sessions");
		const string sessionId = "final-review-abandon";
		WorkbenchSessionPublisher publisher =
			CreateFinalReviewPublisher(sessionsRoot, sessionId);
		CandidateWorkspaceId workspace = new()
		{
			SessionId = sessionId,
			Iteration = sourceWorkspace.Iteration,
			Nonce = sourceWorkspace.Nonce
		};
		AssemblyActionStore actions = new(publisher.SessionRoot);
		AssemblySessionState review = new()
		{
			SessionId = sessionId,
			Phase = AssemblyPhase.FinalReview,
			Checkpoint = 1,
			StateRevision = 1,
			TotalClips = 1,
			Status = "Final review",
			Workspace = workspace
		};
		actions.PublishState(review);
		AssemblyAction abandon = new()
		{
			ActionId = "final-abandon-action",
			SessionId = sessionId,
			Checkpoint = 1,
			ExpectedStateRevision = 1,
			Kind = AssemblyActionKind.AbandonSession,
			CreatedUtc = new DateTimeOffset(
				2026, 7, 27, 11, 0, 0, TimeSpan.Zero)
		};
		WriteAction(publisher.SessionRoot, "abandon.json", abandon);
		FinalizationRequest request = FinalRequest(
			sessionId, plan, workspace);
		FakeFinalizationAutomation automation = new(
			"project-fingerprint",
			Snapshot(plan, workspace));
		FakeFinalizationExecutor executor = new();
		ExpectAbandoned(() =>
			new PostPolishFinalizationCoordinator(
					sessionId,
					publisher.SessionRoot,
					automation,
					publisher,
					executor)
				.RunAsync(request, CancellationToken.None)
				.GetAwaiter().GetResult());
		AssemblyActionExecutionStore executions =
			new(publisher.SessionRoot);
		Assert(
			automation.Operations.SequenceEqual(
				new[] { VegasOperations.CleanupCandidate }) &&
			automation.IdempotencyKeys.Single() ==
				"abandon-final-abandon-action-cleanup" &&
			executor.CallCount == 0 &&
			publisher.State == EditSessionState.Cancelled &&
			actions.ReadState()?.Phase == AssemblyPhase.Abandoned &&
			executions.IsComplete(abandon.ActionId),
			"Final-review abandon did not clean only the candidate and terminate durably.");

		const string reflectedId = "final-review-abandon-reflected";
		WorkbenchSessionPublisher reflectedPublisher =
			CreateFinalReviewPublisher(sessionsRoot, reflectedId);
		CandidateWorkspaceId reflectedWorkspace = new()
		{
			SessionId = reflectedId,
			Iteration = sourceWorkspace.Iteration,
			Nonce = sourceWorkspace.Nonce
		};
		AssemblyActionStore reflectedActions =
			new(reflectedPublisher.SessionRoot);
		AssemblySessionState reflectedReview = new()
		{
			SessionId = reflectedId,
			Phase = AssemblyPhase.FinalReview,
			Checkpoint = 1,
			StateRevision = 1,
			TotalClips = 1,
			Status = "Final review",
			Workspace = reflectedWorkspace
		};
		reflectedActions.PublishState(reflectedReview);
		AssemblyAction reflectedAction = new()
		{
			ActionId = "reflected-final-abandon",
			SessionId = reflectedId,
			Checkpoint = 1,
			ExpectedStateRevision = 1,
			Kind = AssemblyActionKind.AbandonSession,
			CreatedUtc = new DateTimeOffset(
				2026, 7, 27, 11, 5, 0, TimeSpan.Zero)
		};
		FinalizationRequest reflectedRequest = FinalRequest(
			reflectedId, plan, reflectedWorkspace);
		string planHash =
			PostPolishFinalizationCoordinator.PlanSha256(plan);
		AssemblyActionExecutionStore reflectedExecutions =
			new(reflectedPublisher.SessionRoot);
		reflectedExecutions.Begin(
			reflectedAction,
			AssemblyPhase.FinalReview,
			planHash,
			adjustmentSha256:
				PostPolishFinalizationCoordinator.FinalizationRequestSha256(
					reflectedRequest,
					planHash));
		reflectedActions.PublishState(new AssemblySessionState
		{
			SessionId = reflectedId,
			Phase = AssemblyPhase.Abandoned,
			Checkpoint = 1,
			StateRevision = 2,
			TotalClips = 1,
			Status = "Crash after reflected abandon state.",
			Workspace = reflectedWorkspace
		});
		reflectedPublisher.TransitionTo(
			EditSessionState.Cancelled,
			"Crash after reflected cancelled state.");
		FakeFinalizationAutomation reflectedAutomation = new(
			"project-fingerprint",
			Snapshot(plan, reflectedWorkspace));
		ExpectAbandoned(() =>
			new PostPolishFinalizationCoordinator(
					reflectedId,
					reflectedPublisher.SessionRoot,
					reflectedAutomation,
					reflectedPublisher,
					new FakeFinalizationExecutor())
				.RunAsync(reflectedRequest, CancellationToken.None)
				.GetAwaiter().GetResult());
		Assert(
			reflectedAutomation.Operations.SequenceEqual(
				new[] { VegasOperations.CleanupCandidate }) &&
			reflectedExecutions.IsComplete(reflectedAction.ActionId) &&
			reflectedActions.ReadState()?.StateRevision == 2,
			"A reflected final-review abandon did not replay cleanup and complete without republishing state.");
	}

	private static WorkbenchSessionPublisher CreateFinalReviewPublisher(
		string sessionsRoot,
		string sessionId)
	{
		WorkbenchSessionPublisher publisher =
			WorkbenchSessionPublisher.Create(sessionsRoot, sessionId);
		publisher.TransitionTo(EditSessionState.Planning, "test");
		publisher.TransitionTo(EditSessionState.Validating, "test");
		publisher.TransitionTo(EditSessionState.Materializing, "test");
		publisher.TransitionTo(EditSessionState.Reviewing, "test");
		publisher.TransitionTo(EditSessionState.AwaitingUser, "test");
		publisher.TransitionTo(EditSessionState.Polishing, "test");
		publisher.TransitionTo(EditSessionState.FinalReview, "test");
		return publisher;
	}

	private static FinalizationRequest FinalRequest(
		string sessionId,
		EditPlanDocument plan,
		CandidateWorkspaceId workspace) =>
		new()
		{
			SessionId = sessionId,
			RequestId = plan.RequestId,
			ProjectFingerprint = "project-fingerprint",
			Workspace = workspace,
			FinalPlan = plan,
			AcceptedCandidateBaseline =
				Baseline(plan, Snapshot(plan, workspace))
		};

	private static void WriteAction(
		string sessionRoot,
		string fileName,
		AssemblyAction action)
	{
		string actionRoot = Path.Combine(
			sessionRoot,
			"assembly",
			"actions");
		Directory.CreateDirectory(actionRoot);
		File.WriteAllText(
			Path.Combine(actionRoot, fileName),
			ContractSerializer.Serialize(action));
	}

	private static void ExpectAbandoned(Action action)
	{
		try
		{
			action();
		}
		catch (AssemblySessionAbandonedException)
		{
			return;
		}
		throw new InvalidOperationException(
			"The lifecycle scenario did not terminate as abandoned.");
	}

	private static void TestInterruptedFinalizationResume(
		string root,
		string completedSessionRoot,
		EditPlanDocument sourcePlan,
		CandidateWorkspaceId workspace,
		CandidateTimelineSnapshot candidate,
		IFinalizationClock clock)
	{
		string resumedRoot = Path.Combine(
			root, "sessions", "session-final-resume");
		string resumedFinalization = Path.Combine(resumedRoot, "finalization");
		Directory.CreateDirectory(resumedFinalization);
		foreach (string fileName in new[]
			{
				"promotion-intent.json",
				"final-plan.json",
				"final-preview.mp4"
			})
		{
			File.Copy(
				Path.Combine(completedSessionRoot, "finalization", fileName),
				Path.Combine(resumedFinalization, fileName));
		}
		FakeFinalizationAutomation resumedAutomation = new(
			"project-fingerprint",
			candidate);
		FinalizationResult resumed =
			new FinalizationService(
					resumedRoot,
					Path.Combine(root, "resume-archives"),
					resumedAutomation,
					new FakeFinalRenderHook(),
					clock)
				.FinalizeAsync(
					new FinalizationRequest
					{
						SessionId = "session-final",
						RequestId = sourcePlan.RequestId,
						ProjectFingerprint = "project-fingerprint",
						Workspace = workspace,
						FinalPlan = sourcePlan,
						AcceptedCandidateBaseline = Baseline(sourcePlan, candidate),
						RenderFinalPreview = true
					})
				.GetAwaiter().GetResult();
		Assert(resumedAutomation.Operations.SequenceEqual(new[]
			{
				VegasOperations.RollbackCandidatePromotion,
				VegasOperations.PromoteCandidate
			}) &&
			resumed.Report.PromotionId ==
				ContractSerializer.Deserialize<FinalizationPromotionIntent>(
					File.ReadAllText(Path.Combine(
						resumedFinalization,
						"promotion-intent.json"))).PromotionId &&
			File.Exists(Path.Combine(
				resumedFinalization,
				"promotion-resume-restoration.json")),
			"An interrupted promotion intent did not restore and resume exactly once.");
	}

	private static void TestFinalReviewCoordinator(
		string root,
		EditPlanDocument plan,
		CandidateWorkspaceId workspace)
	{
		string sessionsRoot = Path.Combine(root, "coordinator-sessions");
		WorkbenchSessionPublisher publisher =
			WorkbenchSessionPublisher.Create(
				sessionsRoot,
				"final-review-session");
		publisher.TransitionTo(EditSessionState.Planning, "test");
		publisher.TransitionTo(EditSessionState.Validating, "test");
		publisher.TransitionTo(EditSessionState.Materializing, "test");
		publisher.TransitionTo(EditSessionState.Reviewing, "test");
		publisher.TransitionTo(EditSessionState.AwaitingUser, "test");
		publisher.TransitionTo(EditSessionState.Polishing, "test");
		publisher.TransitionTo(EditSessionState.FinalReview, "test");
		CandidateWorkspaceId coordinatorWorkspace = new()
		{
			SessionId = "final-review-session",
			Iteration = workspace.Iteration,
			Nonce = workspace.Nonce
		};
		AssemblyActionStore actions = new(publisher.SessionRoot);
		actions.PublishState(new AssemblySessionState
		{
			SessionId = "final-review-session",
			Phase = AssemblyPhase.FinalReview,
			Checkpoint = 1,
			StateRevision = 1,
			TotalClips = 1,
			Status = "Final review",
			Workspace = coordinatorWorkspace
		});
		string actionRoot = Path.Combine(
			publisher.SessionRoot,
			"assembly",
			"actions");
		Directory.CreateDirectory(actionRoot);
		File.WriteAllText(
			Path.Combine(actionRoot, "finalize.json"),
			ContractSerializer.Serialize(new AssemblyAction
			{
				ActionId = "finalize-action",
				SessionId = "final-review-session",
				Checkpoint = 1,
				ExpectedStateRevision = 1,
				Kind = AssemblyActionKind.FinalizeMontage,
				TargetId = FinalizationActionTargets.RenderFinalPreview,
				CreatedUtc = new DateTimeOffset(
					2026, 7, 27, 10, 0, 0, TimeSpan.Zero)
			}));
		FakeFinalizationExecutor executor = new();
		FakeFinalizationAutomation coordinatorAutomation = new(
			"project-fingerprint",
			Snapshot(plan, coordinatorWorkspace));
		new PostPolishFinalizationCoordinator(
				"final-review-session",
				publisher.SessionRoot,
				coordinatorAutomation,
				publisher,
				executor)
			.RunAsync(
				new FinalizationRequest
				{
					SessionId = "final-review-session",
					RequestId = plan.RequestId,
					ProjectFingerprint = "project-fingerprint",
					Workspace = coordinatorWorkspace,
					FinalPlan = plan,
					AcceptedCandidateBaseline = Baseline(
						plan,
						Snapshot(plan, coordinatorWorkspace))
				},
				CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(executor.CallCount == 1 &&
			executor.LastRenderFinalPreview &&
			publisher.State == EditSessionState.Completed &&
			actions.ReadState()?.Phase == AssemblyPhase.Completed,
			"Exact final-review acceptance did not invoke one promotion and complete.");

		WorkbenchSessionPublisher recoveryPublisher =
			WorkbenchSessionPublisher.Create(
				sessionsRoot,
				"final-review-recovery");
		recoveryPublisher.TransitionTo(EditSessionState.Planning, "test");
		recoveryPublisher.TransitionTo(EditSessionState.Validating, "test");
		recoveryPublisher.TransitionTo(EditSessionState.Materializing, "test");
		recoveryPublisher.TransitionTo(EditSessionState.Reviewing, "test");
		recoveryPublisher.TransitionTo(EditSessionState.AwaitingUser, "test");
		recoveryPublisher.TransitionTo(EditSessionState.Polishing, "test");
		recoveryPublisher.TransitionTo(EditSessionState.FinalReview, "test");
		CandidateWorkspaceId recoveryWorkspace = new()
		{
			SessionId = "final-review-recovery",
			Iteration = workspace.Iteration,
			Nonce = workspace.Nonce
		};
		AssemblyActionStore recoveryActions =
			new(recoveryPublisher.SessionRoot);
		recoveryActions.PublishState(new AssemblySessionState
		{
			SessionId = "final-review-recovery",
			Phase = AssemblyPhase.FinalReview,
			Checkpoint = 1,
			StateRevision = 1,
			TotalClips = 1,
			Status = "Interrupted promotion",
			Workspace = recoveryWorkspace
		});
		Directory.CreateDirectory(Path.Combine(
			recoveryPublisher.SessionRoot,
			"finalization"));
		File.WriteAllText(
			Path.Combine(
				recoveryPublisher.SessionRoot,
				"finalization",
				"promotion-intent.json"),
			"persisted");
		FinalizationRequest recoveryRequest = new()
		{
			SessionId = "final-review-recovery",
			RequestId = plan.RequestId,
			ProjectFingerprint = "project-fingerprint",
			Workspace = recoveryWorkspace,
			FinalPlan = plan,
			AcceptedCandidateBaseline = Baseline(
				plan,
				Snapshot(plan, recoveryWorkspace))
		};
		AssemblyAction recoveryAction = new()
		{
			ActionId = "finalize-recovery-action",
			SessionId = recoveryRequest.SessionId,
			Checkpoint = 1,
			ExpectedStateRevision = 1,
			Kind = AssemblyActionKind.FinalizeMontage,
			TargetId =
				FinalizationActionTargets.PromoteWithoutFinalPreview,
			CreatedUtc = new DateTimeOffset(
				2026, 7, 27, 10, 15, 0, TimeSpan.Zero)
		};
		AssemblyActionExecutionStore recoveryExecutions =
			new(recoveryPublisher.SessionRoot);
		recoveryExecutions.Begin(
			recoveryAction,
			AssemblyPhase.FinalReview,
			PostPolishFinalizationCoordinator.PlanSha256(plan),
			adjustmentSha256:
				PostPolishFinalizationCoordinator.FinalizationRequestSha256(
					recoveryRequest,
					PostPolishFinalizationCoordinator.PlanSha256(plan)));
		recoveryExecutions.Complete(
			recoveryAction,
			"montage-finalized",
			"finalization/final-session-report.json");
		FakeFinalizationExecutor recoveryExecutor = new();
		FakeFinalizationAutomation recoveryAutomation = new(
			"project-fingerprint",
			Snapshot(plan, recoveryWorkspace));
		new PostPolishFinalizationCoordinator(
				"final-review-recovery",
				recoveryPublisher.SessionRoot,
				recoveryAutomation,
				recoveryPublisher,
				recoveryExecutor)
			.RunAsync(
				recoveryRequest,
				CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(recoveryExecutor.CallCount == 1 &&
			recoveryPublisher.State == EditSessionState.Completed &&
			recoveryActions.ReadState()?.Phase == AssemblyPhase.Completed &&
			recoveryExecutions.ReadPendingForRecovery(
				recoveryRequest.SessionId) == null,
			"A written finalization transaction with a completed action did not " +
			"idempotently publish the missing Completed state.");
	}

	private static void TestConsumedFinalReviewActionRecovery(
		string root,
		EditPlanDocument plan,
		CandidateWorkspaceId workspace)
	{
		string sessionsRoot = Path.Combine(
			root,
			"coordinator-consumed-action-sessions");
		string sessionId = "final-review-consumed-restart";
		WorkbenchSessionPublisher publisher =
			WorkbenchSessionPublisher.Create(sessionsRoot, sessionId);
		publisher.TransitionTo(EditSessionState.Planning, "test");
		publisher.TransitionTo(EditSessionState.Validating, "test");
		publisher.TransitionTo(EditSessionState.Materializing, "test");
		publisher.TransitionTo(EditSessionState.Reviewing, "test");
		publisher.TransitionTo(EditSessionState.AwaitingUser, "test");
		publisher.TransitionTo(EditSessionState.Polishing, "test");
		publisher.TransitionTo(EditSessionState.FinalReview, "test");
		CandidateWorkspaceId recoveryWorkspace = new()
		{
			SessionId = sessionId,
			Iteration = workspace.Iteration,
			Nonce = workspace.Nonce
		};
		AssemblyActionStore actions = new(publisher.SessionRoot);
		AssemblySessionState review = new()
		{
			SessionId = sessionId,
			Phase = AssemblyPhase.FinalReview,
			Checkpoint = 1,
			StateRevision = 1,
			TotalClips = 1,
			Status = "Final review before simulated restart",
			Workspace = recoveryWorkspace
		};
		actions.PublishState(review);
		AssemblyAction action = new()
		{
			ActionId = "finalize-consumed-before-restart",
			SessionId = sessionId,
			Checkpoint = review.Checkpoint,
			ExpectedStateRevision = review.StateRevision,
			Kind = AssemblyActionKind.FinalizeMontage,
			TargetId =
				FinalizationActionTargets.PromoteWithoutFinalPreview,
			CreatedUtc = new DateTimeOffset(
				2026, 7, 27, 10, 30, 0, TimeSpan.Zero)
		};
		string actionRoot = Path.Combine(
			publisher.SessionRoot,
			"assembly",
			"actions");
		Directory.CreateDirectory(actionRoot);
		File.WriteAllText(
			Path.Combine(actionRoot, "finalize-before-restart.json"),
			ContractSerializer.Serialize(action));
		AssemblyActionExecutionStore executions =
			new(publisher.SessionRoot);
		FinalizationRequest recoveryRequest = new()
		{
			SessionId = sessionId,
			RequestId = plan.RequestId,
			ProjectFingerprint = "project-fingerprint",
			Workspace = recoveryWorkspace,
			FinalPlan = plan,
			AcceptedCandidateBaseline = Baseline(
				plan,
				Snapshot(plan, recoveryWorkspace))
		};
		string planSha256 =
			PostPolishFinalizationCoordinator.PlanSha256(plan);
		AssemblyAction? consumed = actions.TryConsume(
			review.Checkpoint,
			sessionId,
			review.StateRevision,
			candidate => executions.Begin(
				candidate,
				review.Phase,
				planSha256,
				adjustmentSha256:
					PostPolishFinalizationCoordinator.FinalizationRequestSha256(
						recoveryRequest,
						planSha256)));
		Assert(consumed?.ActionId == action.ActionId &&
			actions.TryConsume(
				review.Checkpoint,
				sessionId,
				review.StateRevision) == null &&
			!executions.IsComplete(action.ActionId),
			"The restart fixture did not durably begin and consume the finalization action.");

		FakeFinalizationExecutor executor = new();
		FakeFinalizationAutomation coordinatorAutomation = new(
			"project-fingerprint",
			Snapshot(plan, recoveryWorkspace));
		new PostPolishFinalizationCoordinator(
				sessionId,
				publisher.SessionRoot,
				coordinatorAutomation,
				publisher,
				executor)
			.RunAsync(
				recoveryRequest,
				CancellationToken.None)
			.GetAwaiter().GetResult();

		Assert(executor.CallCount == 1 &&
			publisher.State == EditSessionState.Completed &&
			actions.ReadState()?.Phase == AssemblyPhase.Completed &&
			executions.IsComplete(action.ActionId) &&
			executions.ReadPendingForRecovery(sessionId) == null,
			"A consumed finalization action was not recovered, finalized once, and completed.");
	}

	private static CandidateTimelineSnapshot Snapshot(
		EditPlanDocument plan,
		CandidateWorkspaceId workspace)
	{
		Core.Domain.Editing.ClipPlacement placement =
			plan.Montage.Placements.Single();
		return new CandidateTimelineSnapshot
		{
			Workspace = workspace,
			TimelineStart = TimeSpan.FromSeconds(placement.TimelineStartSeconds),
			TimelineEnd = TimeSpan.FromSeconds(placement.TimelineEndSeconds),
			Tracks = new List<CandidateTrackSnapshot>
			{
				new()
				{
					Index = 0,
					Name = workspace.OwnershipPrefix + "|VIDEO",
					MediaKind = "Video",
					Events = new List<CandidateEventSnapshot>
					{
						new()
						{
							PlacementId =
								Path.GetFullPath(placement.Clip.FilePath),
							MediaPath = placement.Clip.FilePath,
							TimelineStart = TimeSpan.FromSeconds(
								placement.TimelineStartSeconds),
							TimelineDuration = TimeSpan.FromSeconds(
								placement.LengthSeconds),
							SourceOffset = TimeSpan.FromSeconds(
								placement.SourceOffsetSeconds)
						}
					}
				},
				new()
				{
					Index = 1,
					Name = workspace.OwnershipPrefix + "|SONG",
					MediaKind = "Audio"
				}
			}
		};
	}

	private static void TestUnsupportedFinalTimelineChanges(
		string root,
		string archiveRoot,
		EditPlanDocument plan,
		CandidateWorkspaceId workspace,
		CandidateTimelineSnapshot baselineSnapshot)
	{
		Action<CandidateTimelineSnapshot>[] mutations =
		{
			value => value.Tracks[1].Muted = true,
			value => value.Tracks[1].Solo = true,
			value => value.Tracks[1].Gain = 0.75,
			value => value.Tracks[1].VolumeAutomation.Add(
				new CandidateEnvelopePoint
				{
					Offset = TimeSpan.FromSeconds(0.5),
					Value = 0.5
				}),
			value => value.Tracks[0].Events[0].FadeIn =
				TimeSpan.FromSeconds(0.1),
			value => value.Tracks[0].Events[0].FadeOut =
				TimeSpan.FromSeconds(0.1),
			value => value.Tracks[0].Events[0].FadeInTransition =
				"Manual fade curve",
			value => value.Tracks[0].Events[0].Effects.Add("Test effect"),
			value => value.Tracks.Add(new CandidateTrackSnapshot
			{
				Index = 2,
				Name = workspace.OwnershipPrefix + "|EXTRA",
				MediaKind = "Audio"
			}),
			value => value.Tracks.RemoveAt(1),
			value => value.Tracks[0].Events[0].GroupSignature = "group-a"
		};
		for (int index = 0; index < mutations.Length; index++)
		{
			CandidateTimelineSnapshot changed = Clone(baselineSnapshot);
			mutations[index](changed);
			FinalizationService service = new(
				Path.Combine(root, "unsupported-final-" + index),
				archiveRoot,
				new FakeFinalizationAutomation(
					"project-fingerprint",
					changed));
			ExpectFailure(
				() => service.FinalizeAsync(
						new FinalizationRequest
						{
							SessionId = "session-final",
							RequestId = plan.RequestId,
							ProjectFingerprint = "project-fingerprint",
							Workspace = workspace,
							FinalPlan = plan,
							AcceptedCandidateBaseline =
								Baseline(plan, baselineSnapshot)
						})
					.GetAwaiter().GetResult(),
				"Finalization silently accepted unsupported timeline mutation " +
				index + ".");
		}
	}

	private sealed class FakeFinalizationAutomation : IVegasAutomationClient
	{
		private readonly CandidateTimelineSnapshot candidate;

		public FakeFinalizationAutomation(
			string projectFingerprint,
			CandidateTimelineSnapshot candidate)
		{
			ExpectedProjectFingerprint = projectFingerprint;
			this.candidate = Clone(candidate);
		}

		public string? ExpectedProjectFingerprint { get; }
		public VegasHostIdentity? LastHost { get; private set; }
		public List<string> Operations { get; } = new();
		public List<string> IdempotencyKeys { get; } = new();

		public Task<TResult> ExecuteAsync<TRequest, TResult>(
			string operation,
			TRequest request,
			string idempotencyKey,
			TimeSpan? timeout = null,
			CancellationToken cancellationToken = default)
		{
			Operations.Add(operation);
			IdempotencyKeys.Add(idempotencyKey);
			LastHost = new VegasHostIdentity
			{
				MachineName = "test",
				ProcessId = 1,
				VegasVersion = "20",
				ProjectPath = @"C:\projects\final.veg",
				ProjectFingerprint = ExpectedProjectFingerprint!
			};
			object result;
			switch (operation)
			{
				case VegasOperations.CleanupCandidate:
					result = new CleanupCandidateResult
					{
						RemovedTrackCount = candidate.Tracks.Count,
						RemovedEventCount =
							candidate.Tracks.Sum(track => track.Events.Count)
					};
					break;
				case VegasOperations.GetCandidateSnapshot:
					result = Clone(candidate);
					break;
				case VegasOperations.PromoteCandidate:
					PromoteCandidateRequest promote =
						(PromoteCandidateRequest)(object)request!;
					string candidateHash = Hash(candidate);
					Assert(promote.ExpectedCandidateSnapshotSha256 ==
						candidateHash,
						"Promotion was not bound to the validated snapshot.");
					IList<CandidatePromotionTrackMapping> mappings =
						CandidatePromotionContract.Plan(
							candidate,
							new[] { "User notes" });
					CandidateTimelineSnapshot promoted = Clone(candidate);
					foreach (CandidatePromotionTrackMapping mapping in mappings)
					{
						promoted.Tracks.Single(track =>
							track.Name == mapping.CandidateName).Name =
							mapping.FinalName;
					}
					result = new PromoteCandidateResult
					{
						PromotionId = promote.PromotionId,
						Workspace = promote.Workspace,
						CandidateSnapshot = Clone(candidate),
						CandidateSnapshotSha256 = candidateHash,
						PromotedSnapshot = promoted,
						PromotedSnapshotSha256 = Hash(promoted),
						TrackMappings = mappings
					};
					break;
				case VegasOperations.RollbackCandidatePromotion:
					RollbackCandidatePromotionRequest rollback =
						(RollbackCandidatePromotionRequest)(object)request!;
					result = new RollbackCandidatePromotionResult
					{
						PromotionId = rollback.Promotion.PromotionId,
						Workspace = rollback.Promotion.Workspace,
						RestoredSnapshot = Clone(candidate),
						RestoredSnapshotSha256 =
							rollback.Promotion.CandidateSnapshotSha256
					};
					break;
				default:
					throw new InvalidOperationException(
						"Unexpected finalization operation: " + operation);
			}
			return Task.FromResult((TResult)result);
		}
	}

	private sealed class FakeFinalRenderHook : IFinalRenderHook
	{
		public Task<FinalRenderArtifact> RenderAsync(
			FinalRenderContext context,
			CancellationToken cancellationToken)
		{
			string relative = "finalization/final-preview.mp4";
			string path = Path.Combine(
				context.SessionRoot,
				relative.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
			return Task.FromResult(new FinalRenderArtifact
			{
				ArtifactKind = "video",
				RelativePath = relative,
				LengthBytes = 4,
				Sha256 = FileHash(path),
				Duration = TimeSpan.FromSeconds(2),
				RenderProfile = "test-final"
			});
		}
	}

	private sealed class FakeFinalChunkRenderer : IRoughCutChunkRenderer
	{
		private readonly string sessionRoot;

		public FakeFinalChunkRenderer(string sessionRoot)
		{
			this.sessionRoot = sessionRoot;
		}

		public Task<RenderCandidatePreviewResult> RenderAsync(
			CandidateWorkspaceId workspace,
			TimeSpan start,
			TimeSpan duration,
			string outputRelativePath,
			string idempotencyKey,
			CancellationToken cancellationToken)
		{
			string path = Path.Combine(
				sessionRoot,
				outputRelativePath.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllBytes(path, new byte[] { 5, 6, 7 });
			return Task.FromResult(new RenderCandidatePreviewResult
			{
				OutputRelativePath = outputRelativePath,
				RenderProfileId = "review-1080p",
				RenderedDuration = duration,
				Sha256 = FileHash(path)
			});
		}
	}

	private sealed class FixedFinalizationClock : IFinalizationClock
	{
		public DateTimeOffset UtcNow =>
			new(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);
	}

	private sealed class FakeFinalizationExecutor : IFinalizationExecutor
	{
		public int CallCount { get; private set; }
		public bool LastRenderFinalPreview { get; private set; }

		public Task<FinalizationResult> FinalizeAsync(
			FinalizationRequest request,
			CancellationToken cancellationToken = default)
		{
			CallCount++;
			LastRenderFinalPreview = request.RenderFinalPreview;
			return Task.FromResult(new FinalizationResult
			{
				Report = new FinalSessionReport
				{
					SessionId = request.SessionId,
					RequestId = request.RequestId
				}
			});
		}
	}

	private static T Clone<T>(T value) =>
		ContractSerializer.Deserialize<T>(ContractSerializer.Serialize(value));

	private static CandidateMaterializationBaseline Baseline(
		EditPlanDocument plan,
		CandidateTimelineSnapshot snapshot)
	{
		string snapshotJson = ContractSerializer.Serialize(snapshot);
		return new CandidateMaterializationBaseline
		{
			Checkpoint = plan.Montage.Placements.Count,
			PlanSha256 = Convert.ToHexString(
				SHA256.HashData(
					System.Text.Encoding.UTF8.GetBytes(
						EditPlanDocumentSerializer.SerializePlan(plan))))
				.ToLowerInvariant(),
			SnapshotSha256 = Convert.ToHexString(
				SHA256.HashData(
					System.Text.Encoding.UTF8.GetBytes(snapshotJson)))
				.ToLowerInvariant(),
			Snapshot = Clone(snapshot),
			CapturedUtc = new DateTimeOffset(
				2026, 7, 27, 8, 30, 0, TimeSpan.Zero)
		};
	}

	private static string Hash(CandidateTimelineSnapshot value) =>
		ContractHash.Compute(JToken.FromObject(value));

	private static string FileHash(string path) =>
		Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
			.ToLowerInvariant();

	private static void ExpectFailure(Action action, string message)
	{
		try { action(); }
		catch { return; }
		throw new InvalidOperationException(message);
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}
}
