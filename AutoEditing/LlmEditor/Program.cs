using Core.Domain.Planning;
using AutoEditing.LlmEditor.Inference;
using AutoEditing.LlmEditor.Automation;
using AutoEditing.LlmEditor.Planning;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.LlmEditor.Iteration;
using AutoEditing.LlmEditor.Workbench;
using AutoEditing.LlmEditor.Assembly;
using AutoEditing.LlmEditor.RoughCut;
using AutoEditing.LlmEditor.Polish;
using AutoEditing.LlmEditor.Finalization;
using Newtonsoft.Json;

namespace AutoEditing.LlmEditor;

internal static class Program
{
	private const int Success = 0;
	private const int UnexpectedFailure = 1;
	private const int UsageOrValidationFailure = 2;

	private static async Task<int> Main(string[] args)
	{
		if (args.Length == 1 && args[0] == "--self-test-lifecycle-recovery")
			return LlmEditorSelfTests.RunLifecycleRecovery();

		if (args.Length == 1 && args[0] == "--self-test-reconciliation")
			return LlmEditorSelfTests.RunTimelineReconciliation();

		if (args.Length == 1 && args[0] == "--self-test-assembly-workflow")
			return LlmEditorSelfTests.RunAssemblyWorkflow();

		if (args.Length == 1 && args[0] == "--self-test-recovery")
			return LlmEditorSelfTests.RunRecovery();

		if (args.Length == 1 &&
			args[0] == "--self-test-velocity-reconciliation")
			return LlmEditorSelfTests.RunVelocityReconciliation();

		if (args.Length == 1 && args[0] == "--self-test")
		{
			int result = LlmEditorSelfTests.Run();
			if (result != Success) return result;
			LlamaCppInferenceSelfTests.Run();
			await AutomationClientSelfTests.RunAsync();
			Console.WriteLine("VEGAS automation client self-tests passed.");
			return Success;
		}
		if (args.Length == 1 && args[0] == "--self-test-inference")
			return LlamaCppInferenceSelfTests.Run();

		if (args.Length == 3 && args[0] == "automation-smoke" && args[1] == "--session-id")
			return await RunAutomationSmokeAsync(args[2]);

		if (args.Length == 5 && args[0] == "automation-cycle" &&
			args[1] == "--request" && args[3] == "--session-id")
			return await RunAutomationCycleAsync(args[2], args[4]);

		if (args.Length == 7 && args[0] == "workbench-start" &&
			args[1] == "--request" && args[3] == "--session-id" &&
			args[5] == "--planner")
			return await RunWorkbenchStartAsync(args[2], args[4], args[6]);

		if (args.Length == 5 && args[0] == "workbench-resume" &&
			args[1] == "--session-id" && args[3] == "--planner")
			return await RunWorkbenchResumeAsync(args[2], args[4]);

		if (args.Length == 3 && args[0] == "workbench-abandon" &&
			args[1] == "--session-id")
			return await RunWorkbenchAbandonAsync(args[2]);

		if (args.Length == 3 && args[0] == "workbench-rollback" &&
			args[1] == "--session-id")
			return await RunWorkbenchRollbackAsync(args[2]);

		if (args.Length != 7 || args[0] != "plan" || args[1] != "--request" ||
			args[3] != "--output" || args[5] != "--planner")
		{
			PrintUsage();
			return UsageOrValidationFailure;
		}
		try
		{
			EditPlanningRequest request = EditPlanDocumentSerializer.ReadRequest(args[2]);
			IEditPlanner planner = CreatePlanner(args[6]);
			EditPlanDocument document = await planner.CreatePlanAsync(request, CancellationToken.None);
			if (!string.Equals(document.RequestId, request.RequestId, StringComparison.Ordinal))
				throw new InvalidOperationException("The edit plan request ID does not match its planning request.");
			EditPlanDocumentSerializer.WritePlanNew(args[4], document);
			Console.WriteLine("Wrote validated edit plan: " + Path.GetFullPath(args[4]));
			return Success;
		}
		catch (Exception exception) when (
			exception is JsonException ||
			exception is IOException ||
			exception is InvalidDataException ||
			exception is InvalidOperationException ||
			exception is ArgumentException ||
			exception is NotSupportedException)
		{
			Console.Error.WriteLine(exception.Message);
			return UsageOrValidationFailure;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return UnexpectedFailure;
		}
	}

	private static async Task<int> RunWorkbenchResumeAsync(
		string sessionId,
		string plannerName)
	{
		if (!IsConfiguredPlanner(plannerName))
		{
			Console.Error.WriteLine(
				"The progressive workbench resume path requires the configured inference provider.");
			return UsageOrValidationFailure;
		}
		WorkbenchSessionPublisher? publisher = null;
		LlamaCppProgressMonitor? monitor = null;
		HttpClient? monitorHttpClient = null;
		CancellationTokenSource? monitorCancellation = null;
		Task? monitorTask = null;
		try
		{
			EnvironmentFile.LoadNearest();
			string sessionsRoot = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"AutoEditing", "automation", "sessions");
			AssemblyRecoveryService recovery = new();
			AssemblyRecoveryInspection inspection =
				recovery.Inspect(sessionsRoot, sessionId);
			if (inspection.ResumePlan == null)
			{
				PrintRecoveryFailure(inspection.Summary);
				return UsageOrValidationFailure;
			}

			OpenAiCompatibleOptions options = OpenAiCompatibleOptions.FromEnvironment();
			InferenceBudgets budgets = InferenceBudgets.FromEnvironment();
			HttpClient httpClient = new();
			publisher = WorkbenchSessionPublisher.Open(sessionsRoot, sessionId);
			if (options.IsLlamaCpp)
			{
				monitorHttpClient = new HttpClient();
				monitor = new LlamaCppProgressMonitor(
					monitorHttpClient, options, publisher, sessionId);
				monitorCancellation = new CancellationTokenSource();
				monitorTask = monitor.RunAsync(monitorCancellation.Token);
			}
			using AssemblyRuntimeLease runtimeLease =
				AssemblyRuntimeLease.Acquire(publisher.SessionRoot, sessionId);
			publisher.TransitionTo(
				EditSessionState.NeedsRecovery,
				"Recovery requested; validating the persisted VEGAS checkpoint.");
			FileInferenceUsageSink usageSink = new(publisher.SessionRoot);
			OpenAiCompatibleTextGenerationClient inferenceClient = new(
				httpClient,
				options,
				usageSink,
				new InferenceUsageContext
				{
					SessionId = sessionId,
					Operation = "workbench-resume"
				});
			if (options.IsLlamaCpp)
			{
				LlamaCppProps serverProps =
					await inferenceClient.ProbePropsAsync(CancellationToken.None);
				budgets.ValidateServerContext(serverProps.ContextSize);
			}
			ITextGenerationClient generationClient =
				new RecordingTextGenerationClient(
					inferenceClient, publisher.SessionRoot);
			if (!options.IsLlamaCpp)
				generationClient =
					new StreamingInferenceProgressClient(
						generationClient,
						publisher,
						sessionId,
						options.ProviderDisplayName);
			LlmProgressiveAssemblyPlanner planner = new(
				generationClient,
				budgets);
			AssemblySessionDescriptor descriptor =
				new AssemblyArtifactStore(publisher.SessionRoot).ReadSessionDescriptor()
				?? throw new InvalidDataException(
					"The assembly session descriptor is missing.");
			FileVegasAutomationClient automation = new(
				new VegasAutomationClientOptions
				{
					SpoolRoot = Path.Combine(sessionsRoot, sessionId),
					SessionId = sessionId,
					DefaultTimeout = TimeSpan.FromMinutes(30),
					ExpectedProjectFingerprint = descriptor.ProjectFingerprint
				});
			CheckpointPreviewPipeline checkpointPreviews = new(
				sessionId,
				publisher.SessionRoot,
				new VegasCheckpointPreviewRenderer(automation),
				new LlmCheckpointMultimodalReviewer(generationClient, budgets));
			SectionMilestoneRenderService sectionMilestones = new(
				sessionId,
				publisher.SessionRoot,
				new VegasRoughCutChunkRenderer(automation));

			AssemblyResumePlan resume = inspection.ResumePlan;
			AssemblyActionExecutionStart? pendingAssemblyAction =
				new AssemblyActionExecutionStore(publisher.SessionRoot)
					.ReadPendingForRecovery(sessionId);
			bool pendingNeedsLiveTimeline =
				pendingAssemblyAction != null &&
				AssemblyCoordinator.RequiresManualTimelineEvidence(
					pendingAssemblyAction.Action.Kind);
			if (resume.CurrentProposal != null &&
				(pendingNeedsLiveTimeline ||
					resume.State.Phase is
						AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.AwaitingHumanReview or
						AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.ReconciliationConflict or
						AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.Paused))
			{
				CandidateTimelineSnapshot timeline =
					await automation.ExecuteAsync<
						GetCandidateSnapshotRequest,
						CandidateTimelineSnapshot>(
						VegasOperations.GetCandidateSnapshot,
						new GetCandidateSnapshotRequest
						{
							Workspace = resume.State.Workspace
						},
						"recovery-inspect-" + Guid.NewGuid().ToString("N"),
						cancellationToken: CancellationToken.None);
				inspection = recovery.Inspect(
					sessionsRoot,
					sessionId,
					resume.Request,
					automation.LastHost,
					currentTimeline: timeline);
				if (inspection.ResumePlan == null)
				{
					PrintRecoveryFailure(inspection.Summary);
					return UsageOrValidationFailure;
				}
				resume = inspection.ResumePlan;
			}

			bool resumePostSync =
				resume.Summary.FinalizeAcceptedPlan ||
				RoughCutWorkflowRecoveryStore.IsRoughCutResumeState(
					publisher.SessionRoot,
					resume.State);
			EditPlanDocument result = resumePostSync
				? resume.AcceptedPlan ?? throw new InvalidDataException(
					"The post-sync workflow has no accepted synchronization plan.")
				: await new AssemblyCoordinator(
						planner,
						automation,
						publisher,
						sessionId,
						checkpointPreviews,
						sectionMilestones)
					.ResumeProgressiveAsync(
						resume, CancellationToken.None, runtimeLease);
			AssemblySessionState roughCutState =
				new AssemblyActionStore(publisher.SessionRoot).ReadState()
				?? throw new InvalidDataException(
					"The synchronization pass did not publish its final workspace.");
			AssemblySketch roughCutSketch =
				new AssemblyArtifactStore(publisher.SessionRoot).ReadCurrentSketch()
				?? throw new InvalidDataException(
					"The synchronization pass did not persist its assembly sketch.");
			RoughCutWorkflowResult roughCut =
				await new PostSyncRoughCutCoordinator(
						sessionId,
						publisher.SessionRoot,
						automation,
						publisher,
						new RoughCutAuditService(
							new LlmRoughCutAuditor(generationClient, budgets)))
					.RunAsync(
						resume.Request,
						roughCutSketch,
						result,
						resumePostSync ? resume.State.Workspace : roughCutState.Workspace,
						CancellationToken.None,
						runtimeLease);
			AssemblySessionDescriptor finalDescriptor =
				new AssemblyArtifactStore(publisher.SessionRoot)
					.ReadSessionDescriptor() ??
				throw new InvalidDataException(
					"The final workflow has no persisted project identity.");
			await CompletePostSyncWorkflowAsync(
				sessionId,
				resume.Request,
				roughCut,
				resumePostSync ? resume.State.Workspace : roughCutState.Workspace,
				finalDescriptor,
				automation,
				publisher,
				runtimeLease,
				CancellationToken.None);
			monitor?.Complete(
				"Montage finalized",
				"Recovered, validated, and promoted " +
				roughCut.AcceptedPlan.Montage.Placements.Count +
				" synchronized clip placements.");
			Console.WriteLine(
				"Resumed and finalized AI assembly session " + sessionId +
				" with " + roughCut.AcceptedPlan.Montage.Placements.Count +
				" clips.");
			return Success;
		}
		catch (AssemblySessionAbandonedException exception)
		{
			monitor?.Complete("Assembly abandoned", exception.Message);
			Console.WriteLine(exception.Message);
			return Success;
		}
		catch (Exception exception)
		{
			// A resume attempt can fail because VEGAS is closed, the project differs,
			// or inference is temporarily unavailable. Preserve NeedsRecovery so the
			// editor can correct the environment and retry without losing the session.
			monitor?.Complete("Recovery needs attention", exception.Message);
			Console.Error.WriteLine(exception);
			return UnexpectedFailure;
		}
		finally
		{
			await StopProgressMonitorAsync(monitorCancellation, monitorTask);
			monitorHttpClient?.Dispose();
		}
	}

	private static async Task<int> RunWorkbenchAbandonAsync(string sessionId)
	{
		try
		{
			string sessionsRoot = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"AutoEditing", "automation", "sessions");
			WorkbenchSessionPublisher publisher =
				WorkbenchSessionPublisher.Open(sessionsRoot, sessionId);
			using AssemblyRuntimeLease runtimeLease =
				AssemblyRuntimeLease.Acquire(publisher.SessionRoot, sessionId);
			AssemblyArtifactStore artifacts = new(publisher.SessionRoot);
			AssemblySessionDescriptor descriptor = artifacts.ReadSessionDescriptor()
				?? throw new InvalidDataException(
					"The assembly session descriptor is missing.");
			AssemblyActionStore actions = new(publisher.SessionRoot);
			AssemblySessionState state = actions.ReadState()
				?? throw new InvalidDataException("The assembly state is missing.");
			publisher.TransitionTo(
				EditSessionState.NeedsRecovery,
				"Validating candidate ownership before abandoning a stopped session.");
			FileVegasAutomationClient automation = new(
				new VegasAutomationClientOptions
				{
					SpoolRoot = Path.Combine(sessionsRoot, sessionId),
					SessionId = sessionId,
					DefaultTimeout = TimeSpan.FromMinutes(5),
					ExpectedProjectFingerprint = descriptor.ProjectFingerprint
				});
			await automation.ExecuteAsync<
				CleanupCandidateRequest,
				CleanupCandidateResult>(
				VegasOperations.CleanupCandidate,
				new CleanupCandidateRequest { Workspace = state.Workspace },
				"recovery-abandon-" + sessionId + "-" +
					state.StateRevision.ToString(
						System.Globalization.CultureInfo.InvariantCulture),
				cancellationToken: CancellationToken.None);
			state.Phase = AssemblyPhase.Abandoned;
			state.StateRevision++;
			state.Status =
				"Assembly abandoned. Candidate-owned tracks were removed.";
			actions.PublishState(state);
			publisher.TransitionTo(
				EditSessionState.Cancelled,
				"Stopped assembly abandoned by the editor.");
			Console.WriteLine("Abandoned assembly session " + sessionId + ".");
			return Success;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return UnexpectedFailure;
		}
	}

	private static void PrintRecoveryFailure(
		AutoEditing.Iteration.Contracts.Assembly.AssemblyRecoverySummary summary)
	{
		Console.Error.WriteLine(
			"Cannot resume assembly session '" + summary.SessionId +
			"': " + summary.Disposition + ".");
		foreach (AutoEditing.Iteration.Contracts.Assembly.AssemblyRecoveryIssue issue
			in summary.Issues)
			Console.Error.WriteLine("  " + issue.Code + ": " + issue.Message);
	}

	private static IEditPlanner CreatePlanner(string planner)
	{
		if (string.Equals(planner, "fake", StringComparison.Ordinal))
			return new FakeLlmEditPlanner();
		if (string.Equals(planner, "local", StringComparison.Ordinal))
		{
			EnvironmentFile.LoadNearest();
			OpenAiCompatibleOptions options = OpenAiCompatibleOptions.FromEnvironment();
			return new LlmEditPlanner(
				new OpenAiCompatibleTextGenerationClient(new HttpClient(), options));
		}

		throw new ArgumentException(
			"Unknown planner '" + planner + "'. Expected 'fake' or 'local'.");
	}

	private static async Task<int> RunAutomationSmokeAsync(string sessionId)
	{
		try
		{
			CandidateWorkspaceId workspace = new CandidateWorkspaceId
			{
				SessionId = sessionId,
				Iteration = 1,
				Nonce = "boundary-smoke"
			};
			workspace.Validate();
			string spoolRoot = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"AutoEditing", "automation", "sessions", sessionId);
			FileVegasAutomationClient client = new FileVegasAutomationClient(
				new VegasAutomationClientOptions
				{
					SpoolRoot = spoolRoot,
					SessionId = sessionId,
					DefaultTimeout = TimeSpan.FromSeconds(30)
				});
			CandidateTimelineSnapshot snapshot =
				await client.ExecuteAsync<GetCandidateSnapshotRequest, CandidateTimelineSnapshot>(
					VegasOperations.GetCandidateSnapshot,
					new GetCandidateSnapshotRequest { Workspace = workspace },
					"boundary-smoke-snapshot");
			CleanupCandidateResult cleanup =
				await client.ExecuteAsync<CleanupCandidateRequest, CleanupCandidateResult>(
					VegasOperations.CleanupCandidate,
					new CleanupCandidateRequest { Workspace = workspace },
					"boundary-smoke-cleanup");
			Console.WriteLine(
				"VEGAS automation smoke passed: " + snapshot.Tracks.Count +
				" candidate tracks observed; cleanup removed " +
				cleanup.RemovedTrackCount + " tracks.");
			return Success;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return UnexpectedFailure;
		}
	}

	private static async Task<int> RunWorkbenchStartAsync(
		string requestPath,
		string sessionId,
		string plannerName)
	{
		WorkbenchSessionPublisher? publisher = null;
		LlamaCppProgressMonitor? monitor = null;
		CancellationTokenSource? monitorCancellation = null;
		Task? monitorTask = null;
		try
		{
			EnvironmentFile.LoadNearest();
			string sessionsRoot = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"AutoEditing", "automation", "sessions");
			publisher = WorkbenchSessionPublisher.Create(sessionsRoot, sessionId);
			using AssemblyRuntimeLease runtimeLease =
				AssemblyRuntimeLease.Acquire(publisher.SessionRoot, sessionId);
			publisher.TransitionTo(EditSessionState.Planning, "LLM planning started.");
			OpenAiCompatibleOptions inferenceOptions =
				OpenAiCompatibleOptions.FromEnvironment();
			if (inferenceOptions.IsLlamaCpp)
			{
				monitor = new LlamaCppProgressMonitor(
					new HttpClient(), inferenceOptions, publisher, sessionId);
				monitorCancellation = new CancellationTokenSource();
				monitorTask = monitor.RunAsync(monitorCancellation.Token);
			}
			EditPlanningRequest request = EditPlanDocumentSerializer.ReadRequest(requestPath);
			AssemblyArtifactStore initialArtifacts = new(publisher.SessionRoot);
			initialArtifacts.InitializeSession(sessionId, request);
			new AssemblyActionStore(publisher.SessionRoot).PublishState(
				new AssemblySessionState
				{
					SessionId = sessionId,
					Phase = AssemblyPhase.CreatingSketch,
					Checkpoint = 0,
					StateRevision = 1,
					TotalClips = request.Clips.Count,
					RemainingClipPaths =
						request.Clips.Select(item => item.FilePath).ToList(),
					Status = "Creating the global semantic assembly sketch.",
					Workspace = new CandidateWorkspaceId
					{
						SessionId = sessionId,
						Iteration = 1,
						Nonce = "assembly"
					}
				});
			if (!IsConfiguredPlanner(plannerName))
				throw new NotSupportedException(
					"The iterative workbench requires the configured inference provider.");
			InferenceBudgets inferenceBudgets = InferenceBudgets.FromEnvironment();
			HttpClient inferenceHttpClient = new HttpClient();
			FileInferenceUsageSink usageSink = new FileInferenceUsageSink(publisher.SessionRoot);
			OpenAiCompatibleTextGenerationClient inferenceClient =
				new OpenAiCompatibleTextGenerationClient(
					inferenceHttpClient,
					inferenceOptions,
					usageSink,
					new InferenceUsageContext
					{
						SessionId = sessionId,
						Operation = "workbench"
					});
			if (inferenceOptions.IsLlamaCpp)
			{
				LlamaCppProps serverProps =
					await inferenceClient.ProbePropsAsync(CancellationToken.None);
				inferenceBudgets.ValidateServerContext(serverProps.ContextSize);
			}
			ITextGenerationClient generationClient =
				new RecordingTextGenerationClient(
					inferenceClient,
					publisher.SessionRoot);
			if (!inferenceOptions.IsLlamaCpp)
				generationClient =
					new StreamingInferenceProgressClient(
						generationClient,
						publisher,
						sessionId,
						inferenceOptions.ProviderDisplayName);
			LlmProgressiveAssemblyPlanner planner =
				new LlmProgressiveAssemblyPlanner(generationClient, inferenceBudgets);
			publisher.TransitionTo(
				EditSessionState.Planning,
				"Creating the global semantic assembly sketch.");
			AutoEditing.Iteration.Contracts.Assembly.AssemblySketch sketch =
				await planner.CreateSketchAsync(request, CancellationToken.None);
			string spoolRoot = Path.Combine(sessionsRoot, sessionId);
			FileVegasAutomationClient automation = new FileVegasAutomationClient(
				new VegasAutomationClientOptions
				{
					SpoolRoot = spoolRoot,
					SessionId = sessionId,
					DefaultTimeout = TimeSpan.FromMinutes(30)
				});
			CheckpointPreviewPipeline checkpointPreviews = new(
				sessionId,
				publisher.SessionRoot,
				new VegasCheckpointPreviewRenderer(automation),
				new LlmCheckpointMultimodalReviewer(
					generationClient,
					inferenceBudgets));
			SectionMilestoneRenderService sectionMilestones = new(
				sessionId,
				publisher.SessionRoot,
				new VegasRoughCutChunkRenderer(automation));
			EditPlanDocument result = await new AssemblyCoordinator(
					planner,
					automation,
					publisher,
					sessionId,
					checkpointPreviews,
					sectionMilestones)
				.RunProgressiveAsync(
					request, sketch, CancellationToken.None, runtimeLease);
			AssemblySessionDescriptor boundDescriptor =
				new AssemblyArtifactStore(publisher.SessionRoot)
					.ReadSessionDescriptor() ??
				throw new InvalidDataException(
					"The synchronization pass did not persist its project identity.");
			if (string.IsNullOrWhiteSpace(boundDescriptor.ProjectFingerprint))
				throw new InvalidDataException(
					"The synchronization pass did not bind the active VEGAS project.");
			FileVegasAutomationClient postSyncAutomation = new(
				new VegasAutomationClientOptions
				{
					SpoolRoot = spoolRoot,
					SessionId = sessionId,
					DefaultTimeout = TimeSpan.FromMinutes(30),
					ExpectedProjectFingerprint =
						boundDescriptor.ProjectFingerprint
				});
			AssemblySessionState roughCutState =
				new AssemblyActionStore(publisher.SessionRoot).ReadState()
				?? throw new InvalidDataException(
					"The synchronization pass did not publish its final workspace.");
			RoughCutWorkflowResult roughCut =
				await new PostSyncRoughCutCoordinator(
						sessionId,
						publisher.SessionRoot,
						postSyncAutomation,
						publisher,
						new RoughCutAuditService(
							new LlmRoughCutAuditor(
								generationClient,
								inferenceBudgets)))
					.RunAsync(
						request,
						sketch,
						result,
						roughCutState.Workspace,
						CancellationToken.None,
						runtimeLease);
			await CompletePostSyncWorkflowAsync(
				sessionId,
				request,
				roughCut,
				roughCutState.Workspace,
				boundDescriptor,
				postSyncAutomation,
				publisher,
				runtimeLease,
				CancellationToken.None);
			monitor?.Complete(
				"Montage finalized",
				"Promoted " + roughCut.AcceptedPlan.Montage.Placements.Count +
				" synchronized clip placements after explicit polish and final review.");
			Console.WriteLine(
				"Finalized AI assembly session " + sessionId + " with " +
				roughCut.AcceptedPlan.Montage.Placements.Count +
				" clips.");
			return Success;
		}
		catch (AssemblySessionAbandonedException exception)
		{
			monitor?.Complete("Assembly abandoned", exception.Message);
			Console.WriteLine(exception.Message);
			return Success;
		}
		catch (Exception exception)
		{
			if (publisher != null)
			{
				try
				{
					monitor?.Complete("Planning failed", exception.Message);
					publisher.TransitionTo(EditSessionState.Failed, exception.Message);
				}
				catch
				{
					// Preserve the original planning exception.
				}
			}
			Console.Error.WriteLine(exception);
			return UnexpectedFailure;
		}
		finally
		{
			await StopProgressMonitorAsync(monitorCancellation, monitorTask);
		}
	}

	private static async Task StopProgressMonitorAsync(
		CancellationTokenSource? cancellation,
		Task? monitorTask)
	{
		if (cancellation == null) return;
		try
		{
			cancellation.Cancel();
			if (monitorTask != null)
			{
				Task completed = await Task.WhenAny(
					monitorTask,
					Task.Delay(TimeSpan.FromSeconds(5)));
				if (ReferenceEquals(completed, monitorTask))
				{
					try { await monitorTask; }
					catch (OperationCanceledException) { }
				}
			}
		}
		finally
		{
			cancellation.Dispose();
		}
	}

	private static async Task<int> RunWorkbenchRollbackAsync(string sessionId)
	{
		try
		{
			EnvironmentFile.LoadNearest();
			string sessionsRoot = Path.Combine(
				Environment.GetFolderPath(
					Environment.SpecialFolder.LocalApplicationData),
				"AutoEditing", "automation", "sessions");
			WorkbenchSessionPublisher publisher =
				WorkbenchSessionPublisher.Open(sessionsRoot, sessionId);
			using AssemblyRuntimeLease runtimeLease =
				AssemblyRuntimeLease.Acquire(publisher.SessionRoot, sessionId);
			AssemblySessionDescriptor descriptor =
				new AssemblyArtifactStore(publisher.SessionRoot)
					.ReadSessionDescriptor() ??
				throw new InvalidDataException(
					"The rollback session descriptor is missing.");
			if (string.IsNullOrWhiteSpace(descriptor.ProjectFingerprint))
				throw new InvalidDataException(
					"The rollback session has no bound VEGAS project identity.");
			FileVegasAutomationClient automation = new(
				new VegasAutomationClientOptions
				{
					SpoolRoot = Path.Combine(sessionsRoot, sessionId),
					SessionId = sessionId,
					DefaultTimeout = TimeSpan.FromMinutes(30),
					ExpectedProjectFingerprint = descriptor.ProjectFingerprint
				});
			FinalizationService finalization = new(
				publisher.SessionRoot,
				FinalArchiveRoot(),
				automation);
			RollbackCandidatePromotionResult rollback =
				await finalization.RollbackAsync(CancellationToken.None);
			if (publisher.State == EditSessionState.NeedsRecovery)
				publisher.TransitionTo(
					EditSessionState.FinalReview,
					"Candidate promotion was rolled back; final review may be retried.");
			AssemblyActionStore actions = new(publisher.SessionRoot);
			AssemblySessionState? prior = actions.ReadState();
			if (publisher.State != EditSessionState.Completed && prior != null)
				actions.PublishState(
					NextAssemblyState(
						prior,
						AssemblyPhase.FinalReview,
						"Final promotion was rolled back. Review the restored candidate before finalizing again."));
			Console.WriteLine(
				"Rolled back promotion " + rollback.PromotionId +
				" for AI editing session " + sessionId + ".");
			return Success;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return UnexpectedFailure;
		}
	}

	private static async Task CompletePostSyncWorkflowAsync(
		string sessionId,
		EditPlanningRequest request,
		RoughCutWorkflowResult roughCut,
		CandidateWorkspaceId workspace,
		AssemblySessionDescriptor descriptor,
		IVegasAutomationClient automation,
		WorkbenchSessionPublisher publisher,
		AssemblyRuntimeLease runtimeLease,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(descriptor.ProjectFingerprint))
			throw new InvalidDataException(
				"Final polish and promotion require a VEGAS-bound project identity.");
		PolishWorkflowResult polish =
			await new PostRoughCutPolishCoordinator(
					sessionId,
					publisher.SessionRoot,
					automation,
					publisher)
				.RunAsync(
					roughCut.Milestone,
					request,
					roughCut.AcceptedPlan,
					roughCut.AcceptedBaseline,
					workspace,
					cancellationToken,
					runtimeLease);
		if (polish.Effects.Pass != PolishPassKind.Effects ||
			polish.Audio.Pass != PolishPassKind.Audio)
			throw new InvalidDataException(
				"The final review requires accepted effects and audio milestones.");

		await new PostPolishFinalizationCoordinator(
				sessionId,
				publisher.SessionRoot,
				automation,
				publisher,
				new FinalizationService(
					publisher.SessionRoot,
					FinalArchiveRoot(),
					automation))
			.RunAsync(
				new FinalizationRequest
				{
					SessionId = sessionId,
					RequestId = descriptor.RequestId,
					ProjectFingerprint = descriptor.ProjectFingerprint,
					Workspace = workspace,
					FinalPlan = roughCut.AcceptedPlan,
					AcceptedCandidateBaseline = polish.FinalBaseline,
					// The accepted audio pass already rendered the complete
					// candidate; avoid a redundant final render by default.
					RenderFinalPreview = false
				},
				cancellationToken,
				runtimeLease);
	}

	private static string FinalArchiveRoot() =>
		Path.Combine(
			Environment.GetFolderPath(
				Environment.SpecialFolder.LocalApplicationData),
			"AutoEditing",
			"archives");

	private static AssemblySessionState NextAssemblyState(
		AssemblySessionState prior,
		AssemblyPhase phase,
		string status) =>
		new()
		{
			SessionId = prior.SessionId,
			Phase = phase,
			Checkpoint = prior.Checkpoint,
			StateRevision = prior.StateRevision + 1,
			TotalClips = prior.TotalClips,
			CurrentClipPath = "",
			RemainingClipPaths = Array.Empty<string>(),
			Status = status,
			Workspace = prior.Workspace
		};

	private static async Task<int> RunAutomationCycleAsync(string requestPath, string sessionId)
	{
		CandidateWorkspaceId? workspace = null;
		FileVegasAutomationClient? client = null;
		bool materialized = false;
		try
		{
			EditPlanningRequest request = EditPlanDocumentSerializer.ReadRequest(requestPath);
			EditPlanDocument plan = await new FakeLlmEditPlanner()
				.CreatePlanAsync(request, CancellationToken.None);
			workspace = new CandidateWorkspaceId
			{
				SessionId = sessionId,
				Iteration = 1,
				Nonce = "live-cycle"
			};
			workspace.Validate();
			string spoolRoot = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"AutoEditing", "automation", "sessions", sessionId);
			client = new FileVegasAutomationClient(
				new VegasAutomationClientOptions
				{
					SpoolRoot = spoolRoot,
					SessionId = sessionId,
					DefaultTimeout = TimeSpan.FromSeconds(60)
				});

			PreflightCandidateResult preflight =
				await client.ExecuteAsync<PreflightCandidateRequest, PreflightCandidateResult>(
					VegasOperations.PreflightCandidate,
					new PreflightCandidateRequest
					{
						Workspace = workspace,
						Plan = plan,
						SongPath = request.SongPath
					},
					"live-cycle-preflight");
			if (!preflight.IsReady)
				throw new InvalidOperationException(
					"VEGAS candidate preflight failed: " +
					string.Join("; ", preflight.Errors.Select(error => error.Message)));

			MaterializeCandidateResult created =
				await client.ExecuteAsync<MaterializeCandidateRequest, MaterializeCandidateResult>(
					VegasOperations.MaterializeCandidate,
					new MaterializeCandidateRequest
					{
						Workspace = workspace,
						Plan = plan,
						SongPath = request.SongPath,
						IncludeSong = true,
						IncludeSfx = false,
						ApplyEffects = false
					},
					"live-cycle-materialize");
			materialized = true;

			CandidateTimelineSnapshot snapshot =
				await client.ExecuteAsync<GetCandidateSnapshotRequest, CandidateTimelineSnapshot>(
					VegasOperations.GetCandidateSnapshot,
					new GetCandidateSnapshotRequest { Workspace = workspace },
					"live-cycle-snapshot");
			if (created.CreatedTrackNames.Count == 0 || created.CreatedEventCount == 0 ||
				snapshot.Tracks.Count == 0)
				throw new InvalidOperationException(
					"VEGAS returned an empty candidate after materialization.");

			Console.WriteLine(
				"VEGAS candidate cycle passed: created " + created.CreatedTrackNames.Count +
				" tracks and " + created.CreatedEventCount + " events; read back " +
				snapshot.Tracks.Count + " tracks.");
			return Success;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return UnexpectedFailure;
		}
		finally
		{
			if (materialized && client != null && workspace != null)
			{
				try
				{
					CleanupCandidateResult cleanup =
						await client.ExecuteAsync<CleanupCandidateRequest, CleanupCandidateResult>(
							VegasOperations.CleanupCandidate,
							new CleanupCandidateRequest { Workspace = workspace },
							"live-cycle-cleanup");
					Console.WriteLine(
						"VEGAS candidate cleanup removed " + cleanup.RemovedTrackCount +
						" owned tracks.");
				}
				catch (Exception cleanupException)
				{
					Console.Error.WriteLine(
						"Candidate cleanup failed; remove only workspace '" +
						workspace.OwnershipPrefix + "': " + cleanupException);
				}
			}
		}
	}

	private static void PrintUsage()
	{
		Console.Error.WriteLine(
			"Usage: AutoEditing.LlmEditor plan --request <request.json> --output <plan.json> " +
			"--planner <fake|configured|local>\n" +
			"       AutoEditing.LlmEditor automation-smoke --session-id <id>\n" +
			"       AutoEditing.LlmEditor automation-cycle --request <request.json> " +
			"--session-id <id>\n" +
			"       AutoEditing.LlmEditor workbench-start --request <request.json> " +
			"--session-id <id> --planner <fake|configured|local>\n" +
			"       AutoEditing.LlmEditor workbench-resume --session-id <id> " +
			"--planner <configured|local>\n" +
			"       AutoEditing.LlmEditor workbench-abandon --session-id <id>\n" +
			"       AutoEditing.LlmEditor workbench-rollback --session-id <id>");
	}

	private static bool IsConfiguredPlanner(string plannerName) =>
		string.Equals(
			plannerName,
			"configured",
			StringComparison.Ordinal) ||
		string.Equals(
			plannerName,
			"local",
			StringComparison.Ordinal);
}
