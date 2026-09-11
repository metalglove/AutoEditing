using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.LlmEditor.Automation;
using AutoEditing.LlmEditor.Planning;
using AutoEditing.LlmEditor.Workbench;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Assembly;

internal static class AssemblyRecoverySelfTests
{
	public static void Run(string testRoot, EditPlanningRequest request)
	{
		TestRuntimeLease(Path.Combine(testRoot, "runtime-lease"));
		TestCreatingSketchRecovery(
			Path.Combine(testRoot, "creating-sketch-recovery"), request);
		TestSyncPassCompleteRecovery(
			Path.Combine(testRoot, "sync-complete-recovery"), request);
		string sessionsRoot = Path.Combine(testRoot, "recovery-sessions");
		string sessionId = "recovery-session";
		WorkbenchSessionPublisher publisher =
			WorkbenchSessionPublisher.Create(sessionsRoot, sessionId);
		publisher.TransitionTo(EditSessionState.Planning, "test");
		AssemblyArtifactStore artifacts = new(publisher.SessionRoot);
		artifacts.InitializeSession(
			sessionId, request, @"C:\projects\montage.veg", "project-a");
		AssemblySketch sketch = Sketch(request);
		artifacts.SaveSketch(sketch, 1);
		ClipStepDecision proposal = Proposal(request);
		artifacts.SaveProposal(proposal, 1);
		CandidateWorkspaceId workspace = new()
		{
			SessionId = sessionId,
			Iteration = 1,
			Nonce = "assembly"
		};
		new AssemblyActionStore(publisher.SessionRoot).PublishState(
			new AssemblySessionState
			{
				SessionId = sessionId,
				Phase = AssemblyPhase.AwaitingHumanReview,
				Checkpoint = 1,
				StateRevision = 7,
				TotalClips = request.Clips.Count,
				CurrentClipPath = request.Clips[0].FilePath,
				RemainingClipPaths = request.Clips.Select(item => item.FilePath).ToList(),
				Status = "review",
				Workspace = workspace
			});

		AssemblyRecoveryService recovery = new();
		Assert(
			recovery.DiscoverIncompleteSessions(sessionsRoot)
				.SequenceEqual(new[] { sessionId }),
			"Incomplete assembly discovery did not find the recoverable session.");
		AssemblyRecoveryInspection ready = recovery.Inspect(
			sessionsRoot,
			sessionId,
			request,
			new VegasHostIdentity
			{
				ProjectPath = @"C:\projects\montage.veg",
				ProjectFingerprint = "project-a"
			});
		Assert(
			ready.Summary.Disposition == AssemblyRecoveryDisposition.ReadyToResume &&
			ready.ResumePlan?.CurrentProposal?.StepIndex == 1 &&
			ready.ResumePlan.State.StateRevision == 7,
			"Restart inspection did not recover the exact checkpoint proposal and revision.");

		EditPlanningRequest conflicting = EditPlanDocumentSerializer.DeserializeRequest(
			EditPlanDocumentSerializer.SerializeRequest(request));
		conflicting.CreativeBrief = (conflicting.CreativeBrief ?? "") + " changed";
		AssemblyRecoveryInspection requestConflict = recovery.Inspect(
			sessionsRoot, sessionId, conflicting);
		Assert(
			requestConflict.Summary.Disposition == AssemblyRecoveryDisposition.Conflict &&
			requestConflict.Summary.Issues.Any(item => item.Code == "REQUEST_CONFLICT"),
			"Recovery accepted a different planning request.");

		AssemblyRecoveryInspection projectConflict = recovery.Inspect(
			sessionsRoot,
			sessionId,
			request,
			new VegasHostIdentity
			{
				ProjectPath = @"C:\projects\other.veg",
				ProjectFingerprint = "project-b"
			});
		Assert(
			projectConflict.Summary.Disposition == AssemblyRecoveryDisposition.Conflict &&
			projectConflict.Summary.Issues.Any(item =>
				item.Code == "PROJECT_FINGERPRINT_CONFLICT"),
			"Recovery accepted a different VEGAS project identity.");

		CandidateTimelineSnapshot wrongWorkspace = new()
		{
			Workspace = new CandidateWorkspaceId
			{
				SessionId = sessionId,
				Iteration = 9,
				Nonce = "other"
			}
		};
		AssemblyRecoveryInspection divergence = recovery.Inspect(
			sessionsRoot,
			sessionId,
			request,
			currentTimeline: wrongWorkspace);
		Assert(
			divergence.Summary.Disposition == AssemblyRecoveryDisposition.Diverged &&
			divergence.Summary.Issues.Any(item => item.Code == "WORKSPACE_DIVERGED"),
			"Recovery did not stop at a divergent materialized workspace.");
		AssemblyRecoveryInspection retryAfterDivergence = recovery.Inspect(
			sessionsRoot,
			sessionId,
			request,
			new VegasHostIdentity
			{
				ProjectPath = @"C:\projects\montage.veg",
				ProjectFingerprint = "project-a"
			});
		Assert(
			retryAfterDivergence.Summary.Disposition ==
				AssemblyRecoveryDisposition.ReadyToResume &&
			retryAfterDivergence.ResumePlan != null,
			"A reported timeline divergence incorrectly made the session terminal.");

		TestCoordinatorResume(publisher, ready.ResumePlan!, request);
	}

	private static void TestCreatingSketchRecovery(
		string sessionsRoot,
		EditPlanningRequest request)
	{
		const string sessionId = "creating-sketch-session";
		WorkbenchSessionPublisher publisher =
			WorkbenchSessionPublisher.Create(sessionsRoot, sessionId);
		publisher.TransitionTo(EditSessionState.Planning, "creating sketch");
		new AssemblyArtifactStore(publisher.SessionRoot)
			.InitializeSession(sessionId, request);
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
				Status = "creating sketch",
				Workspace = new CandidateWorkspaceId
				{
					SessionId = sessionId,
					Iteration = 1,
					Nonce = "assembly"
				}
			});
		AssemblyRecoveryInspection inspection = new AssemblyRecoveryService()
			.Inspect(sessionsRoot, sessionId, request);
		Assert(
			inspection.Summary.Disposition ==
				AssemblyRecoveryDisposition.ReadyToResume &&
			inspection.Summary.NeedsSketch &&
			inspection.Summary.Checkpoint == 1 &&
			inspection.ResumePlan != null,
			"A process restart during initial sketch generation was not recoverable.");
		RestartPlanner planner = new(request);
		RecoveryAutomation automation = new();
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
		Task driver = FinishAtNextReviewAsync(
			publisher.SessionRoot,
			sessionId,
			inspection.ResumePlan!.State.StateRevision,
			timeout.Token);
		EditPlanDocument completed = new AssemblyCoordinator(
				planner,
				automation,
				publisher,
				sessionId)
			.ResumeProgressiveAsync(inspection.ResumePlan, timeout.Token)
			.GetAwaiter().GetResult();
		driver.GetAwaiter().GetResult();
		Assert(
			planner.CreateSketchCalls == 1 &&
			planner.PlanClipCalls == 1 &&
			automation.MaterializeCalls == 1 &&
			completed.Montage.Placements.Count == 1,
			"A restarted companion did not regenerate only the missing sketch and " +
			"continue through one durable clip proposal.");
	}

	private static async Task FinishAtNextReviewAsync(
		string sessionRoot,
		string sessionId,
		long priorStateRevision,
		CancellationToken cancellationToken)
	{
		string statePath = Path.Combine(
			sessionRoot, "assembly", "state.json");
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (File.Exists(statePath))
			{
				AssemblySessionState state =
					ContractSerializer.Deserialize<AssemblySessionState>(
						File.ReadAllText(statePath));
				if (state.Phase == AssemblyPhase.AwaitingHumanReview &&
					state.StateRevision > priorStateRevision)
				{
					string actionPath = Path.Combine(
						sessionRoot,
						"assembly",
						"actions",
						"restart-finish.json");
					Directory.CreateDirectory(Path.GetDirectoryName(actionPath)!);
					File.WriteAllText(
						actionPath,
						ContractSerializer.Serialize(new AssemblyAction
					{
						SessionId = sessionId,
						Checkpoint = state.Checkpoint,
						ExpectedStateRevision = state.StateRevision,
						Kind = AssemblyActionKind.FinishSyncPass
					}));
					return;
				}
			}
			await Task.Delay(20, cancellationToken);
		}
	}

	private static void TestRuntimeLease(string sessionRoot)
	{
		using (AssemblyRuntimeLease.Acquire(sessionRoot, "lease-session"))
		{
			Assert(
				AssemblyRuntimeLease.IsHeld(sessionRoot),
				"A live assembly runtime lease was not observable.");
			try
			{
				using AssemblyRuntimeLease unexpected =
					AssemblyRuntimeLease.Acquire(sessionRoot, "lease-session");
				throw new InvalidOperationException(
					"A second companion acquired the same assembly runtime lease.");
			}
			catch (InvalidOperationException exception) when (
				exception.InnerException is IOException)
			{
			}
		}
		Assert(
			!AssemblyRuntimeLease.IsHeld(sessionRoot),
			"The assembly runtime lease remained held after disposal.");
	}

	private static void TestSyncPassCompleteRecovery(
		string sessionsRoot,
		EditPlanningRequest request)
	{
		EditPlanningRequest singleRequest =
			EditPlanDocumentSerializer.DeserializeRequest(
				EditPlanDocumentSerializer.SerializeRequest(request));
		singleRequest.Clips = new List<Core.Domain.Clip.Clip>
		{
			singleRequest.Clips[0]
		};
		const string sessionId = "sync-complete-session";
		WorkbenchSessionPublisher publisher =
			WorkbenchSessionPublisher.Create(sessionsRoot, sessionId);
		publisher.TransitionTo(EditSessionState.Planning, "sync recovery fixture");
		publisher.TransitionTo(
			EditSessionState.NeedsRecovery,
			"simulate project closing after sync completion");
		publisher.TransitionTo(EditSessionState.Rendering, "rough-cut audit");
		AssemblyArtifactStore artifacts = new(publisher.SessionRoot);
		artifacts.InitializeSession(
			sessionId,
			singleRequest,
			@"C:\projects\montage.veg",
			"project-a");
		artifacts.SaveSketch(Sketch(singleRequest), 1);
		EditPlanDocument accepted = new ClipStepDecisionCompiler()
			.Append(singleRequest, null, Proposal(singleRequest))
			.CombinedPlan;
		artifacts.SaveAcceptedPlan(1, accepted);
		CandidateWorkspaceId workspace = new()
		{
			SessionId = sessionId,
			Iteration = 1,
			Nonce = "assembly"
		};
		new AssemblyActionStore(publisher.SessionRoot).PublishState(
			new AssemblySessionState
			{
				SessionId = sessionId,
				Phase = AssemblyPhase.SyncPassComplete,
				Checkpoint = 1,
				StateRevision = 4,
				TotalClips = 1,
				Status = "sync complete",
				Workspace = workspace
			});

		AssemblyRecoveryInspection inspection = new AssemblyRecoveryService()
			.Inspect(
				sessionsRoot,
				sessionId,
				singleRequest,
				new VegasHostIdentity
				{
					ProjectPath = @"C:\projects\montage.veg",
					ProjectFingerprint = "project-a"
				});
		Assert(
			inspection.Summary.Disposition ==
				AssemblyRecoveryDisposition.ReadyToResume &&
			inspection.Summary.FinalizeAcceptedPlan &&
			inspection.ResumePlan?.AcceptedPlan?.Montage.Placements.Count == 1,
			"Reopening the VEGAS project after synchronization did not recover " +
			"the exact accepted final checkpoint.");
		RecoveryAutomation automation = new();
		EditPlanDocument resumed = new AssemblyCoordinator(
				new UnusedPlanner(),
				automation,
				publisher,
				sessionId)
			.ResumeProgressiveAsync(
				inspection.ResumePlan!,
				CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(
			resumed.Montage.Placements.Count == 1 &&
			automation.MaterializeCalls == 0 &&
			automation.CleanupCalls == 0 &&
			publisher.State == EditSessionState.Rendering,
			"A completed synchronization pass was rematerialized or restarted " +
			"instead of resuming at mandatory rough-cut review.");
	}

	private static void TestCoordinatorResume(
		WorkbenchSessionPublisher publisher,
		AssemblyResumePlan resume,
		EditPlanningRequest request)
	{
		RecoveryAutomation automation = new();
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
		Task driver = Task.Run(async () =>
		{
			string statePath = Path.Combine(
				publisher.SessionRoot, "assembly", "state.json");
			while (!timeout.IsCancellationRequested)
			{
				if (File.Exists(statePath))
				{
					AssemblySessionState state =
						ContractSerializer.Deserialize<AssemblySessionState>(
							File.ReadAllText(statePath));
					if (state.Phase == AssemblyPhase.AwaitingHumanReview &&
						state.StateRevision > resume.State.StateRevision)
					{
						string actionPath = Path.Combine(
							publisher.SessionRoot,
							"assembly",
							"actions",
							"resume-finish.json");
						Directory.CreateDirectory(Path.GetDirectoryName(actionPath)!);
						File.WriteAllText(
							actionPath,
							ContractSerializer.Serialize(new AssemblyAction
							{
								SessionId = state.SessionId,
								Checkpoint = state.Checkpoint,
								ExpectedStateRevision = state.StateRevision,
								Kind = AssemblyActionKind.FinishSyncPass
							}));
						return;
					}
				}
				await Task.Delay(20, timeout.Token);
			}
		}, timeout.Token);
		EditPlanDocument completed = new AssemblyCoordinator(
				new UnusedPlanner(), automation, publisher, resume.State.SessionId)
			.ResumeProgressiveAsync(resume, timeout.Token)
			.GetAwaiter().GetResult();
		driver.GetAwaiter().GetResult();
		Assert(
			completed.Montage.Placements.Count == request.Clips.Count &&
			automation.MaterializeCalls == 1 &&
			automation.CleanupCalls == 1,
			"Restart recovery did not rebuild and complete the current checkpoint exactly once.");
	}

	private static AssemblySketch Sketch(EditPlanningRequest request)
	{
		string regionId = request.SongAnalysis.Regions[0].Id;
		AssemblySectionIntent section = new()
		{
			SectionId = "recovery-section",
			RegionId = regionId,
			EditorialRole = "setup",
			EnergyDirection = "steady",
			PacingIntent = "readable",
			Rationale = "Recovery fixture"
		};
		AssemblySketch sketch = new()
		{
			RequestId = request.RequestId,
			EditorialThesis = "Recover the exact synchronization checkpoint.",
			Sections = new List<AssemblySectionIntent> { section },
			ClipOrder = request.Clips.Select((clip, index) => new AssemblyClipIntent
			{
				Order = index + 1,
				Clip = new AssemblyClipReference
				{
					ReferenceId = AssemblyReferenceIds.ForClipPath(clip.FilePath),
					MediaPath = clip.FilePath
				},
				SectionId = section.SectionId,
				EditorialRole = "setup",
				Rationale = "Recovery fixture",
				Confidence = 1
			}).ToList(),
			SyncStrategy = new AssemblySyncStrategy
			{
				Density = "readable",
				PreferredMusicalTypes = new List<string> { "Accent" },
				Rationale = "Recovery fixture"
			}
		};
		ProgressiveAssemblyContractValidator.Validate(sketch);
		return sketch;
	}

	private static ClipStepDecision Proposal(EditPlanningRequest request)
	{
		Core.Domain.Clip.Clip clip = request.Clips[0];
		double kill = clip.ConfirmedKills[0].SourceConfirmationTimeSeconds;
		double start = Math.Max(0, kill - 0.5);
		double end = Math.Min(clip.DurationSeconds, kill + 0.5);
		return new ClipStepDecision
		{
			RequestId = request.RequestId,
			StepIndex = 1,
			Clip = new AssemblyClipReference
			{
				ReferenceId = AssemblyReferenceIds.ForClipPath(clip.FilePath),
				MediaPath = clip.FilePath
			},
			SourceWindow = new AssemblySourceWindow
			{
				StartSeconds = start,
				EndSeconds = end,
				ConstantSpeed = 1
			},
			PrimarySync = new AssemblySyncDecision
			{
				MusicEventId = request.SongAnalysis.Events
					.First(item => item.IsGameplayAnchor).Id,
				KillIndex = 0
			},
			Rationale = "Recovery fixture",
			Confidence = 1
		};
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private sealed class UnusedPlanner : IProgressiveAssemblyPlanner
	{
		public Task<AssemblySketch> CreateSketchAsync(
			EditPlanningRequest request, CancellationToken cancellationToken) =>
			throw new InvalidOperationException("Recovery must reuse the persisted sketch.");

		public Task<ClipStepDecision> PlanClipAsync(
			EditPlanningRequest request,
			ProgressiveAssemblyPlanningContext context,
			CancellationToken cancellationToken) =>
			throw new InvalidOperationException("Recovery must reuse the persisted proposal.");

		public Task<ClipStepDecision> ReviseClipAsync(
			EditPlanningRequest request,
			ProgressiveAssemblyPlanningContext context,
			ClipStepDecision previousDecision,
			CancellationToken cancellationToken) =>
			throw new InvalidOperationException("No revision is expected.");
	}

	private sealed class RestartPlanner : IProgressiveAssemblyPlanner
	{
		private readonly EditPlanningRequest request;
		public int CreateSketchCalls { get; private set; }
		public int PlanClipCalls { get; private set; }

		public RestartPlanner(EditPlanningRequest request) =>
			this.request = request;

		public Task<AssemblySketch> CreateSketchAsync(
			EditPlanningRequest ignored,
			CancellationToken cancellationToken)
		{
			CreateSketchCalls++;
			return Task.FromResult(Sketch(request));
		}

		public Task<ClipStepDecision> PlanClipAsync(
			EditPlanningRequest ignored,
			ProgressiveAssemblyPlanningContext context,
			CancellationToken cancellationToken)
		{
			PlanClipCalls++;
			return Task.FromResult(Proposal(request));
		}

		public Task<ClipStepDecision> ReviseClipAsync(
			EditPlanningRequest ignored,
			ProgressiveAssemblyPlanningContext context,
			ClipStepDecision previousDecision,
			CancellationToken cancellationToken) =>
			throw new InvalidOperationException("No revision is expected.");
	}

	private sealed class RecoveryAutomation : IVegasAutomationClient
	{
		private EditPlanDocument? plan;
		private CandidateWorkspaceId? workspace;
		public int MaterializeCalls { get; private set; }
		public int CleanupCalls { get; private set; }

		public Task<TResult> ExecuteAsync<TRequest, TResult>(
			string operation,
			TRequest request,
			string idempotencyKey,
			TimeSpan? timeout = null,
			CancellationToken cancellationToken = default)
		{
			object result;
			if (operation == VegasOperations.PreflightCandidate)
				result = new PreflightCandidateResult { IsReady = true };
			else if (operation == VegasOperations.CleanupCandidate)
			{
				CleanupCalls++;
				result = new CleanupCandidateResult();
			}
			else if (operation == VegasOperations.MaterializeCandidate)
			{
				MaterializeCandidateRequest materialize =
					(MaterializeCandidateRequest)(object)request!;
				plan = materialize.Plan;
				workspace = materialize.Workspace;
				MaterializeCalls++;
				result = new MaterializeCandidateResult
				{
					CreatedEventCount = plan.Montage.Placements.Count
				};
			}
			else if (operation == VegasOperations.GetCandidateSnapshot)
				result = Snapshot(plan!, workspace!);
			else
				throw new InvalidOperationException("Unexpected recovery operation " + operation);
			return Task.FromResult((TResult)result);
		}

		private static CandidateTimelineSnapshot Snapshot(
			EditPlanDocument plan,
			CandidateWorkspaceId workspace) => new()
		{
			Workspace = workspace,
			Tracks = new List<CandidateTrackSnapshot>
			{
				new()
				{
					MediaKind = "Video",
					Events = plan.Montage.Placements.Select(placement =>
						new CandidateEventSnapshot
						{
							MediaPath = placement.Clip.FilePath,
							TimelineStart =
								TimeSpan.FromSeconds(placement.TimelineStartSeconds),
							TimelineDuration =
								TimeSpan.FromSeconds(placement.LengthSeconds),
							SourceOffset =
								TimeSpan.FromSeconds(placement.SourceOffsetSeconds)
						}).ToList()
				}
			}
		};
	}
}
