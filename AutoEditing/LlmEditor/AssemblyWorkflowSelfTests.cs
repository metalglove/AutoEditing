using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.LlmEditor.Assembly;
using AutoEditing.LlmEditor.Automation;
using AutoEditing.LlmEditor.Iteration;
using AutoEditing.LlmEditor.Workbench;
using Core.Domain.Editing;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor;

internal static class AssemblyWorkflowSelfTests
{
	public static void RunReconciliation(EditPlanDocument sourcePlan)
	{
		TestMissingExtraAndDuplicateEvents(sourcePlan);
		TestDurablePlacementIdentity(sourcePlan);
		TestMaterializationBaselineRejectsUnsupportedChanges(sourcePlan);
		RunVelocityReconciliation(sourcePlan);
	}

	public static void Run(
		string testRoot,
		EditPlanningRequest sourceRequest,
		EditPlanDocument sourcePlan)
	{
		TestActionStateVersions(testRoot);
		Console.WriteLine("self-test: assembly action state versions");
		RunReconciliation(sourcePlan);
		Console.WriteLine("self-test: assembly reconciliation");
		TestMultiClipReviseAndAccept(testRoot, sourceRequest, sourcePlan);
		Console.WriteLine("self-test: assembly multi-clip");
		TestAcceptedAdjustmentConflictIsRejected(testRoot, sourceRequest, sourcePlan);
		Console.WriteLine("self-test: assembly accepted conflict");
		AssemblyReconciliationConflictSelfTests.Run(
			testRoot, sourceRequest, sourcePlan);
		Console.WriteLine("self-test: assembly conflict resolutions");
		ProgressiveAssemblyWorkflowSelfTests.Run(
			testRoot, sourceRequest, sourcePlan);
		Console.WriteLine("self-test: progressive assembly");
	}

	private static void TestActionStateVersions(string testRoot)
	{
		string sessionRoot = Path.Combine(testRoot, "assembly-action-revisions");
		Directory.CreateDirectory(sessionRoot);
		AssemblyActionStore store = new(sessionRoot);
		string actionDirectory = Path.Combine(sessionRoot, "assembly", "actions");
		Directory.CreateDirectory(actionDirectory);

		WriteAction(actionDirectory, new AssemblyAction
		{
			ActionId = "wrong-session",
			SessionId = "another-session",
			Checkpoint = 3,
			ExpectedStateRevision = 12,
			Kind = AssemblyActionKind.ResetCurrentClip,
			CreatedUtc = new DateTimeOffset(2026, 7, 26, 10, 0, 0, TimeSpan.Zero)
		});
		WriteAction(actionDirectory, new AssemblyAction
		{
			ActionId = "stale-state",
			SessionId = "revision-session",
			Checkpoint = 3,
			ExpectedStateRevision = 11,
			Kind = AssemblyActionKind.ReviseCurrentClip,
			CreatedUtc = new DateTimeOffset(2026, 7, 26, 10, 0, 1, TimeSpan.Zero)
		});
		WriteAction(actionDirectory, new AssemblyAction
		{
			ActionId = "oldest-valid",
			SessionId = "revision-session",
			Checkpoint = 3,
			ExpectedStateRevision = 12,
			Kind = AssemblyActionKind.AcceptTimelineAndContinue,
			CreatedUtc = new DateTimeOffset(2026, 7, 26, 10, 0, 2, TimeSpan.Zero)
		});
		WriteAction(actionDirectory, new AssemblyAction
		{
			ActionId = "duplicate-valid",
			SessionId = "revision-session",
			Checkpoint = 3,
			ExpectedStateRevision = 12,
			Kind = AssemblyActionKind.ResetCurrentClip,
			CreatedUtc = new DateTimeOffset(2026, 7, 26, 10, 0, 3, TimeSpan.Zero)
		});

		AssemblyAction? consumed = store.TryConsume(3, "revision-session", 12);
		Assert(consumed?.ActionId == "oldest-valid",
			"Assembly commands were not consumed in creation order after rejecting stale commands.");
		Assert(Directory.EnumerateFiles(
				Path.Combine(sessionRoot, "assembly", "quarantined"),
				"*.wrong-session.json").Any(),
			"A command for another session was not quarantined.");
		Assert(Directory.EnumerateFiles(
				Path.Combine(sessionRoot, "assembly", "quarantined"),
				"*.stale-state.json").Any(),
			"A command targeting a stale state revision was not quarantined.");

		Assert(store.TryConsume(3, "revision-session", 13) == null,
			"A second command for an already-consumed state revision was applied.");
		Assert(Directory.EnumerateFiles(
				Path.Combine(sessionRoot, "assembly", "quarantined"),
				"duplicate-valid.stale-state.json").Any(),
			"A contradictory command left behind by the prior state was not quarantined.");
	}

	private static void TestMissingExtraAndDuplicateEvents(EditPlanDocument source)
	{
		EditPlanDocument prefix = Clone(source);
		ClipPlacement placement = prefix.Montage.Placements.Single();

		CandidateTimelineSnapshot missing = SnapshotFor(prefix);
		missing.Tracks[0].Events.Clear();
		ExpectFailureContaining(
			() => AssemblyTimelineReconciler.Apply(Clone(source), missing),
			"deleted",
			"A missing expected VEGAS event was not classified as a deletion.");

		CandidateTimelineSnapshot unexpected = SnapshotFor(prefix);
		unexpected.Tracks[0].Events.Add(new CandidateEventSnapshot
		{
			PlacementId = "unexpected",
			MediaPath = "fixtures/unexpected.mp4",
			TimelineStart = TimeSpan.FromSeconds(3),
			TimelineDuration = TimeSpan.FromSeconds(1),
			SourceOffset = TimeSpan.Zero
		});
		ExpectFailureContaining(
			() => AssemblyTimelineReconciler.Apply(Clone(source), unexpected),
			"unexpected",
			"An unexpected VEGAS event was silently ignored.");

		CandidateTimelineSnapshot duplicate = SnapshotFor(prefix);
		duplicate.Tracks[0].Events.Add(new CandidateEventSnapshot
		{
			PlacementId = Path.GetFullPath(placement.Clip.FilePath) + "-duplicate",
			MediaPath = placement.Clip.FilePath,
			TimelineStart = TimeSpan.FromSeconds(2.5),
			TimelineDuration = TimeSpan.FromSeconds(placement.LengthSeconds),
			SourceOffset = TimeSpan.FromSeconds(placement.SourceOffsetSeconds)
		});
		ExpectFailureContaining(
			() => AssemblyTimelineReconciler.Apply(Clone(source), duplicate),
			"ambiguous",
			"Duplicate VEGAS events for one selected clip were not rejected as ambiguous.");
	}

	private static void TestDurablePlacementIdentity(EditPlanDocument source)
	{
		EditPlanDocument plan = Clone(source);
		CandidateWorkspaceId workspace = new()
		{
			SessionId = "identity-self-test",
			Iteration = 1,
			Nonce = "candidate"
		};
		CandidateTimelineSnapshot snapshot = SnapshotFor(plan, workspace);
		for (int index = 0; index < snapshot.Tracks[0].Events.Count; index++)
			snapshot.Tracks[0].Events[index].PlacementId =
				CandidatePlacementIdentity.Create(
					workspace,
					index + 1,
					plan.Montage.Placements[index].Clip.FilePath);
		AssemblyTimelineReconciler.Apply(Clone(plan), snapshot);

		snapshot.Tracks[0].Events[0].MediaPath =
			Path.Combine(Path.GetDirectoryName(
				plan.Montage.Placements[0].Clip.FilePath)!, "replacement.mp4");
		ExpectFailureContaining(
			() => AssemblyTimelineReconciler.Apply(Clone(plan), snapshot),
			"durable placement identity",
			"A durable event identity was allowed to point at replacement media.");
	}

	private static void TestMaterializationBaselineRejectsUnsupportedChanges(
		EditPlanDocument source)
	{
		EditPlanDocument plan = Clone(source);
		CandidateTimelineSnapshot materialized = SnapshotFor(plan);
		materialized.Tracks.Add(new CandidateTrackSnapshot
		{
			Index = 1,
			Name = "AE|LLM|music",
			MediaKind = "Audio",
			Events = new List<CandidateEventSnapshot>
			{
				new()
				{
					PlacementId = "song",
					MediaPath = @"fixtures\song.wav",
					TimelineStart = TimeSpan.Zero,
					TimelineDuration = TimeSpan.FromSeconds(30),
					SourceOffset = TimeSpan.Zero,
					Gain = 0.5
				}
			}
		});
		CandidateMaterializationBaseline baseline = new()
		{
			Checkpoint = 1,
			PlanSha256 = Convert.ToHexString(
				System.Security.Cryptography.SHA256.HashData(
					System.Text.Encoding.UTF8.GetBytes(
						EditPlanDocumentSerializer.SerializePlan(plan))))
				.ToLowerInvariant(),
			SnapshotSha256 = new string('b', 64),
			Snapshot = CloneSnapshot(materialized),
			CapturedUtc = DateTimeOffset.UtcNow
		};

		CandidateTimelineSnapshot supported = CloneSnapshot(materialized);
		supported.Tracks[0].Events[0].TimelineStart +=
			TimeSpan.FromMilliseconds(50);
		AssemblyTimelineReconciler.Apply(
			Clone(plan),
			supported,
			1,
			baseline);
		CandidateMaterializationBaseline wrongPlan = new()
		{
			Checkpoint = baseline.Checkpoint,
			PlanSha256 = new string('a', 64),
			SnapshotSha256 = baseline.SnapshotSha256,
			Snapshot = baseline.Snapshot,
			CapturedUtc = baseline.CapturedUtc
		};
		ExpectFailureContaining(
			() => AssemblyTimelineReconciler.Apply(
				Clone(plan), supported, 1, wrongPlan),
			"another exact plan",
			"A materialization baseline for another plan hash was accepted.");

		CandidateTimelineSnapshot muted = CloneSnapshot(materialized);
		muted.Tracks[0].Muted = true;
		ExpectFailureContaining(
			() => AssemblyTimelineReconciler.Apply(
				Clone(plan), muted, 1, baseline),
			"mute changed",
			"A track mute was silently ignored during synchronization reconciliation.");
		CandidateTimelineSnapshot trackGainChanged =
			CloneSnapshot(materialized);
		trackGainChanged.Tracks[1].Gain = 0.75;
		ExpectFailureContaining(
			() => AssemblyTimelineReconciler.Apply(
				Clone(plan), trackGainChanged, 1, baseline),
			"gain changed",
			"A track gain edit was silently ignored during synchronization reconciliation.");

		CandidateTimelineSnapshot audioChanged = CloneSnapshot(materialized);
		audioChanged.Tracks[1].Events[0].Gain = 0.25;
		ExpectFailureContaining(
			() => AssemblyTimelineReconciler.Apply(
				Clone(plan), audioChanged, 1, baseline),
			"gain changed",
			"A song gain edit was silently ignored during synchronization reconciliation.");
		CandidateTimelineSnapshot automationChanged =
			CloneSnapshot(materialized);
		automationChanged.Tracks[1].VolumeAutomation.Add(
			new CandidateEnvelopePoint
			{
				Offset = TimeSpan.FromSeconds(1),
				Value = 0.25
			});
		ExpectFailureContaining(
			() => AssemblyTimelineReconciler.Apply(
				Clone(plan), automationChanged, 1, baseline),
			"volume automation changed",
			"A song volume-automation edit was silently ignored during reconciliation.");

		CandidateTimelineSnapshot faded = CloneSnapshot(materialized);
		faded.Tracks[0].Events[0].FadeIn = TimeSpan.FromMilliseconds(250);
		ExpectFailureContaining(
			() => AssemblyTimelineReconciler.Apply(
				Clone(plan), faded, 1, baseline),
			"fade-in changed",
			"A video fade was silently ignored during synchronization reconciliation.");

		CandidateTimelineSnapshot effected = CloneSnapshot(materialized);
		effected.Tracks[0].Events[0].Effects.Add("Manual effect");
		ExpectFailureContaining(
			() => AssemblyTimelineReconciler.Apply(
				Clone(plan), effected, 1, baseline),
			"effects changed",
			"An event effect was silently ignored during synchronization reconciliation.");

		CandidateTimelineSnapshot grouped = CloneSnapshot(materialized);
		grouped.Tracks[0].Events[0].GroupSignature = "manual-group";
		ExpectFailureContaining(
			() => AssemblyTimelineReconciler.Apply(
				Clone(plan), grouped, 1, baseline),
			"event grouping changed",
			"An event grouping change was silently ignored during synchronization reconciliation.");
	}

	public static void RunVelocityReconciliation(
		EditPlanDocument source)
	{
		EditPlanDocument withoutEnvelope = Clone(source);
		ClipPlacement proposed = withoutEnvelope.Montage.Placements.Single();
		double sourceStart = proposed.SourceOffsetSeconds;
		proposed.SpeedProfile = new SpeedProfile(new[]
		{
			new SpeedProfilePoint(sourceStart, 1.5),
			new SpeedProfilePoint(sourceStart + 3, 1.5)
		});
		proposed.LengthSeconds = 2;
		proposed.AssignedBeatTimesSeconds.Clear();
		withoutEnvelope.Montage.SyncAssignments.Clear();
		CandidateTimelineSnapshot liveAtNormalSpeed =
			SnapshotFor(withoutEnvelope);
		TimelineAdjustmentDelta normalDelta =
			AssemblyTimelineReconciler.Apply(
				withoutEnvelope,
				liveAtNormalSpeed);
		ClipPlacement adoptedNormal =
			withoutEnvelope.Montage.Placements.Single();
		Assert(Math.Abs(adoptedNormal.SpeedProfile.Points[0].Speed - 1) <
				0.000001 &&
			Math.Abs(adoptedNormal.SpeedProfile.TotalSourceConsumptionSeconds -
				2) < 0.000001,
			"An absent VEGAS velocity envelope was replaced with the proposed " +
			"non-1x rate instead of observed 1.0x playback.");
		Assert(normalDelta.Changes.Any(item =>
				item.Kind == TimelineAdjustmentKind.ConstantSpeedChanged &&
				Math.Abs(item.Before - 1.5) < 0.000001 &&
				Math.Abs(item.After - 1) < 0.000001),
			"The missing-envelope 1.0x correction was not recorded in the " +
			"timeline adjustment delta.");

		EditPlanDocument withEnvelope = Clone(source);
		ClipPlacement expected = withEnvelope.Montage.Placements.Single();
		expected.AssignedBeatTimesSeconds.Clear();
		withEnvelope.Montage.SyncAssignments.Clear();
		CandidateTimelineSnapshot liveAccelerated = SnapshotFor(withEnvelope);
		liveAccelerated.Tracks[0].Events[0].Velocity =
			new List<CandidateVelocityPoint>
			{
				new()
				{
					Offset = TimeSpan.Zero,
					Velocity = 1.25
				},
				new()
				{
					Offset = liveAccelerated.Tracks[0].Events[0].TimelineDuration,
					Velocity = 1.25
				}
			};
		AssemblyTimelineReconciler.Apply(withEnvelope, liveAccelerated);
		Assert(Math.Abs(
				withEnvelope.Montage.Placements.Single()
					.SpeedProfile.Points[0].Speed - 1.25) < 0.000001,
			"A constant live VEGAS velocity envelope was not adopted.");

		EditPlanDocument variable = Clone(source);
		variable.Montage.Placements.Single().AssignedBeatTimesSeconds.Clear();
		variable.Montage.SyncAssignments.Clear();
		CandidateTimelineSnapshot liveVariable = SnapshotFor(variable);
		double variableSourceStart =
			liveVariable.Tracks[0].Events[0].SourceOffset.TotalSeconds;
		double variableTimelineDuration =
			liveVariable.Tracks[0].Events[0].TimelineDuration.TotalSeconds;
		liveVariable.Tracks[0].Events[0].Velocity =
			new List<CandidateVelocityPoint>
			{
				new() { Offset = TimeSpan.Zero, Velocity = 1 },
				new()
				{
					Offset = liveVariable.Tracks[0].Events[0].TimelineDuration,
					Velocity = 1.25
				}
			};
		TimelineAdjustmentDelta variableDelta =
			AssemblyTimelineReconciler.Apply(variable, liveVariable);
		SpeedProfile adoptedCurve =
			variable.Montage.Placements.Single().SpeedProfile;
		double expectedSourceEnd = variableSourceStart +
			variableTimelineDuration * 1.125;
		Assert(
			adoptedCurve.Points.Count == 2 &&
			Math.Abs(adoptedCurve.Points[0].SourceTimeSeconds - variableSourceStart) <
				0.000001 &&
			Math.Abs(adoptedCurve.Points[0].Speed - 1) < 0.000001 &&
			Math.Abs(adoptedCurve.Points[1].SourceTimeSeconds - expectedSourceEnd) <
				0.000001 &&
			Math.Abs(adoptedCurve.Points[1].Speed - 1.25) < 0.000001,
			"A variable live VEGAS velocity envelope was not reconstructed as an " +
			"authoritative source-time speed profile.");
		Assert(variableDelta.Changes.Any(item =>
				item.Kind == TimelineAdjustmentKind.ConstantSpeedChanged),
			"Adopting a variable live velocity envelope was not recorded in the " +
			"timeline adjustment delta.");
	}

	private static void TestMultiClipReviseAndAccept(
		string testRoot,
		EditPlanningRequest sourceRequest,
		EditPlanDocument sourcePlan)
	{
		(EditPlanningRequest request, EditPlanDocument plan) =
			CreateTwoClipFixture(sourceRequest, sourcePlan);
		double proposedSourceOffset =
			plan.Montage.Placements[0].SourceOffsetSeconds;
		string sessionsRoot = Path.Combine(testRoot, "assembly-multi-clip");
		WorkbenchSessionPublisher publisher = CreatePublisher(sessionsRoot, "multi-clip");
		SequencePlanner planner = new();
		ScriptedAssemblyAutomation automation = new()
		{
			FirstSnapshotAdjustment = placement =>
			{
				placement.SourceOffset += TimeSpan.FromSeconds(0.2);
			}
		};
		using AssemblyActionDriver driver = new(
			publisher.SessionRoot,
			"multi-clip",
			new[]
			{
				new ScriptedAction(1, AssemblyActionKind.ReviseCurrentClip,
					"Keep my tighter source trim."),
				new ScriptedAction(1, AssemblyActionKind.AcceptTimelineAndContinue),
				new ScriptedAction(2, AssemblyActionKind.FinishSyncPass)
			});

		EditPlanDocument result = new AssemblyCoordinator(
				planner, automation, publisher, "multi-clip")
			.RunAsync(request, plan, CancellationToken.None)
			.GetAwaiter().GetResult();

		Assert(planner.Revisions.Count == 2,
			"The two-clip revise/accept workflow did not perform one current-clip revision " +
			"and one next-clip planning call.");
		PlannerRevision currentRevision = planner.Revisions[0];
		Assert(currentRevision.Iteration == 1 &&
			currentRevision.Feedback.TimelineAdjustment != null,
			"The current-clip revision did not receive its structured timeline adjustment.");
		Assert(currentRevision.Feedback.TimelineAdjustment!.Changes.Any(change =>
				change.Kind == TimelineAdjustmentKind.SourceTrimChanged &&
				Math.Abs(change.After - change.Before - 0.2) < 0.000001),
			"The planner feedback did not contain the actual manual source-trim delta.");
		Assert(Math.Abs(
				currentRevision.PreviousPlan.Montage.Placements[0].SourceOffsetSeconds -
				(proposedSourceOffset + 0.2)) < 0.000001,
			"The plan supplied as revision evidence did not contain the human-adjusted timeline.");
		Assert(currentRevision.Feedback.SteeringInstructions
				.Contains("Keep my tighter source trim."),
			"The explicit human revision instruction was not propagated to the planner.");

		Assert(Math.Abs(
				result.Montage.Placements[0].SourceOffsetSeconds -
				(proposedSourceOffset + 0.2)) < 0.000001,
			"The accepted manual adjustment was not preserved while planning the second clip.");
		Assert(automation.MaterializedPlacementCounts.SequenceEqual(new[] { 1, 1, 2 }),
			"The workflow did not materialize the first proposal, revised first proposal, " +
			"and two-clip accepted prefix in order.");
		Assert(automation.CleanupCount == 2,
			"The workflow did not clean the replaced revision and the accepted intermediate workspace.");
		Assert(File.Exists(Path.Combine(
				publisher.SessionRoot,
				"assembly", "checkpoints", "0001", "adjustment-delta.json")),
			"The accepted/revised checkpoint did not persist its adjustment evidence.");
	}

	private static void TestAcceptedAdjustmentConflictIsRejected(
		string testRoot,
		EditPlanningRequest sourceRequest,
		EditPlanDocument sourcePlan)
	{
		(EditPlanningRequest request, EditPlanDocument plan) =
			CreateTwoClipFixture(sourceRequest, sourcePlan);
		string sessionsRoot = Path.Combine(testRoot, "assembly-conflict");
		WorkbenchSessionPublisher publisher = CreatePublisher(sessionsRoot, "conflict");
		ScriptedAssemblyAutomation automation = new()
		{
			FirstSnapshotAdjustment = placement =>
			{
				placement.TimelineStart += TimeSpan.FromSeconds(1.0);
			}
		};
		using AssemblyActionDriver driver = new(
			publisher.SessionRoot,
			"conflict",
			new[]
			{
				new ScriptedAction(1, AssemblyActionKind.AcceptTimelineAndContinue)
			});

		ExpectFailureContaining(
			() => new AssemblyCoordinator(
					new SequencePlanner(), automation, publisher, "conflict")
				.RunAsync(request, plan, CancellationToken.None)
				.GetAwaiter().GetResult(),
			"overlap",
			"An accepted human adjustment that overlaps the next planned clip was not " +
			"revalidated after merging.");
		Assert(automation.CleanupCount == 0,
			"The valid candidate workspace was cleaned before the conflicting accepted merge " +
			"was rejected.");
	}

	private static WorkbenchSessionPublisher CreatePublisher(
		string sessionsRoot,
		string sessionId)
	{
		WorkbenchSessionPublisher publisher =
			WorkbenchSessionPublisher.Create(sessionsRoot, sessionId);
		publisher.TransitionTo(EditSessionState.Planning, "assembly workflow self-test");
		return publisher;
	}

	private static (EditPlanningRequest Request, EditPlanDocument Plan) CreateTwoClipFixture(
		EditPlanningRequest sourceRequest,
		EditPlanDocument sourcePlan)
	{
		EditPlanningRequest request = EditPlanDocumentSerializer.DeserializeRequest(
			EditPlanDocumentSerializer.SerializeRequest(sourceRequest));
		EditPlanDocument plan = Clone(sourcePlan);
		EditPlanDocument secondSource = Clone(sourcePlan);
		ClipPlacement second = secondSource.Montage.Placements.Single();
		second.Clip.FilePath = "fixtures/clip-002.mp4";
		second.TimelineStartSeconds = 2.5;
		second.SourceOffsetSeconds = 0;
		second.SpeedProfile = new SpeedProfile(new[]
		{
			new SpeedProfilePoint(0, 1),
			new SpeedProfilePoint(2, 1)
		});
		second.LengthSeconds = 2;
		second.AssignedBeatTimesSeconds.Clear();
		plan.Montage.Placements.Add(second);
		plan.Montage.SyncAssignments.Clear();
		request.Clips.Add(second.Clip);
		EditPlanDocumentValidator.ValidateAndNormalize(plan);
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		return (request, plan);
	}

	private static CandidateTimelineSnapshot SnapshotFor(
		EditPlanDocument plan,
		CandidateWorkspaceId? workspace = null)
	{
		workspace ??= new CandidateWorkspaceId
		{
			SessionId = "assembly-self-test",
			Iteration = 1,
			Nonce = "candidate"
		};
		return new CandidateTimelineSnapshot
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
							PlacementId = Path.GetFullPath(placement.Clip.FilePath),
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

	private static EditPlanDocument Clone(EditPlanDocument source) =>
		EditPlanDocumentSerializer.DeserializePlan(
			EditPlanDocumentSerializer.SerializePlan(source));

	private static CandidateTimelineSnapshot CloneSnapshot(
		CandidateTimelineSnapshot source) =>
		ContractSerializer.Deserialize<CandidateTimelineSnapshot>(
			ContractSerializer.Serialize(source));

	private static void WriteAction(string directory, AssemblyAction action)
	{
		File.WriteAllText(
			Path.Combine(directory, action.ActionId + ".json"),
			ContractSerializer.Serialize(action));
	}

	private static void ExpectFailureContaining(
		Action action,
		string expectedText,
		string failureMessage)
	{
		try
		{
			action();
		}
		catch (Exception exception)
		{
			if (exception.ToString().Contains(expectedText, StringComparison.OrdinalIgnoreCase))
				return;
			throw new InvalidOperationException(
				failureMessage + " Unexpected failure: " + exception.Message, exception);
		}
		throw new InvalidOperationException(failureMessage);
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private sealed record PlannerRevision(
		EditPlanDocument PreviousPlan,
		EditIterationFeedback Feedback,
		int Iteration);

	private sealed class SequencePlanner : IIterativeEditPlanner
	{
		public List<PlannerRevision> Revisions { get; } = new();

		public Task<EditPlanDocument> CreatePlanAsync(
			EditPlanningRequest request,
			CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public Task<EditPlanDocument> RevisePlanAsync(
			EditPlanningRequest request,
			EditPlanDocument previousPlan,
			EditIterationFeedback feedback,
			int iteration,
			CancellationToken cancellationToken)
		{
			Revisions.Add(new PlannerRevision(
				Clone(previousPlan),
				feedback,
				iteration));
			return Task.FromResult(Clone(previousPlan));
		}
	}

	private sealed class ScriptedAssemblyAutomation : IVegasAutomationClient
	{
		private EditPlanDocument? materialized;
		private CandidateWorkspaceId? workspace;
		private int snapshotCount;

		public Action<CandidateEventSnapshot>? FirstSnapshotAdjustment { get; init; }

		public List<int> MaterializedPlacementCounts { get; } = new();

		public int CleanupCount { get; private set; }

		public Task<TResult> ExecuteAsync<TRequest, TResult>(
			string operation,
			TRequest request,
			string idempotencyKey,
			TimeSpan? timeout = null,
			CancellationToken cancellationToken = default)
		{
			object result;
			switch (operation)
			{
				case VegasOperations.PreflightCandidate:
					result = new PreflightCandidateResult { IsReady = true };
					break;
				case VegasOperations.MaterializeCandidate:
					MaterializeCandidateRequest materialize =
						(MaterializeCandidateRequest)(object)request!;
					materialized = Clone(materialize.Plan);
					workspace = materialize.Workspace;
					MaterializedPlacementCounts.Add(
						materialized.Montage.Placements.Count);
					result = new MaterializeCandidateResult
					{
						CreatedEventCount = materialized.Montage.Placements.Count
					};
					break;
				case VegasOperations.GetCandidateSnapshot:
					if (materialized == null)
						throw new InvalidOperationException(
							"No candidate was materialized before its snapshot.");
					CandidateTimelineSnapshot snapshot = SnapshotFor(
						materialized,
						workspace);
					// The first snapshot is the coordinator's immediate
					// post-materialization verification readback. Simulate the
					// human edit only on the later review snapshot.
					if (snapshotCount++ == 1)
						FirstSnapshotAdjustment?.Invoke(snapshot.Tracks[0].Events.Last());
					result = snapshot;
					break;
				case VegasOperations.CleanupCandidate:
					CleanupCount++;
					result = new CleanupCandidateResult();
					break;
				default:
					throw new InvalidOperationException("Unexpected VEGAS operation " + operation);
			}
			return Task.FromResult((TResult)result);
		}
	}

	private sealed record ScriptedAction(
		int Checkpoint,
		AssemblyActionKind Kind,
		string Instruction = "");

	private sealed class AssemblyActionDriver : IDisposable
	{
		private readonly CancellationTokenSource cancellation = new();
		private readonly Task task;

		public AssemblyActionDriver(
			string sessionRoot,
			string sessionId,
			IEnumerable<ScriptedAction> actions)
		{
			task = DriveAsync(
				sessionRoot,
				sessionId,
				new Queue<ScriptedAction>(actions),
				cancellation.Token);
		}

		public void Dispose()
		{
			cancellation.Cancel();
			try { task.GetAwaiter().GetResult(); }
			catch (OperationCanceledException) { }
			cancellation.Dispose();
		}

		private static async Task DriveAsync(
			string sessionRoot,
			string sessionId,
			Queue<ScriptedAction> scripted,
			CancellationToken cancellationToken)
		{
			string statePath = Path.Combine(sessionRoot, "assembly", "state.json");
			string actionDirectory = Path.Combine(sessionRoot, "assembly", "actions");
			long lastHandledRevision = -1;
			while (scripted.Count > 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (File.Exists(statePath))
				{
					AssemblySessionState state;
					try
					{
						state = ContractSerializer.Deserialize<AssemblySessionState>(
							File.ReadAllText(statePath));
					}
					catch (IOException)
					{
						await Task.Delay(10, cancellationToken);
						continue;
					}
					ScriptedAction next = scripted.Peek();
					if (state.Phase == AssemblyPhase.AwaitingHumanReview &&
						state.Checkpoint == next.Checkpoint &&
						state.StateRevision != lastHandledRevision)
					{
						lastHandledRevision = state.StateRevision;
						scripted.Dequeue();
						Directory.CreateDirectory(actionDirectory);
						AssemblyAction action = new()
						{
							ActionId = Guid.NewGuid().ToString("N"),
							SessionId = sessionId,
							Checkpoint = state.Checkpoint,
							ExpectedStateRevision = state.StateRevision,
							Kind = next.Kind,
							Instruction = next.Instruction,
							CreatedUtc = DateTimeOffset.UtcNow
						};
						WriteAction(actionDirectory, action);
					}
				}
				await Task.Delay(10, cancellationToken);
			}
		}
	}
}
