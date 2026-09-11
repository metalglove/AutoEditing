using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using Core.Domain.Editing;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Assembly;

internal static class AssemblyReconciliationConflictSelfTests
{
	public static void Run(
		string testRoot,
		EditPlanningRequest sourceRequest,
		EditPlanDocument sourcePlan)
	{
		string root = Path.Combine(testRoot, "reconciliation-choices");
		Directory.CreateDirectory(root);
		TestRestoreAndDefer(root, sourceRequest, sourcePlan);
		TestExcludeCurrent(root, sourceRequest, sourcePlan);
		TestAdoptAdditional(root, sourceRequest, sourcePlan);
		TestAdoptAsCurrent(root, sourceRequest, sourcePlan);
		TestUnsafeAndChangedCandidatesFailClosed(root, sourceRequest, sourcePlan);
		TestPendingResolutionReplay(root, sourceRequest, sourcePlan);
		TestForeignAndAmbiguousEventsRemainBlocked(root, sourceRequest, sourcePlan);
	}

	private static void TestRestoreAndDefer(
		string root,
		EditPlanningRequest request,
		EditPlanDocument source)
	{
		EditPlanDocument plan = Clone(source);
		CandidateTimelineSnapshot missing = SnapshotFor(plan);
		missing.Tracks[0].Events.Clear();
		AssemblyReconciliationConflictService service =
			new(Path.Combine(root, "restore-defer"));
		AssemblyReconciliationConflict conflict = service.Create(
			"restore-defer",
			1,
			request,
			plan,
			missing,
			new InvalidOperationException("deleted"));
		string immutablePath = Path.Combine(
			root,
			"restore-defer",
			"assembly",
			"checkpoints",
			"0001",
			"reconciliation",
			"conflicts",
			conflict.ConflictId + ".json");
		string immutableBefore = File.ReadAllText(immutablePath);
		AssemblyReconciliationConflict repeated = service.Create(
			"restore-defer",
			1,
			request,
			plan,
			missing,
			new InvalidOperationException("deleted"));
		Assert(repeated.CreatedUtc == conflict.CreatedUtc &&
			File.ReadAllText(immutablePath) == immutableBefore,
			"Repeated conflict detection mutated the immutable conflict artifact.");
		Assert(conflict.SupportedResolutions.Contains(
				AssemblyReconciliationResolutionKind.RestoreExactProposal) &&
			conflict.SupportedResolutions.Contains(
				AssemblyReconciliationResolutionKind.DeferAndPause),
			"Every conflict did not expose exact restoration and deferral.");
		AssemblyReconciliationResolution restored = service.SaveResolution(
			conflict,
			AssemblyReconciliationResolutionKind.RestoreExactProposal,
			"",
			"Restore the reviewed proposal.");
		AssemblyReconciliationResolution deferred = service.SaveResolution(
			conflict,
			AssemblyReconciliationResolutionKind.DeferAndPause,
			"",
			"Pause until the editor can inspect the source.");
		Assert(
			restored.Kind ==
				AssemblyReconciliationResolutionKind.RestoreExactProposal &&
			deferred.Kind ==
				AssemblyReconciliationResolutionKind.DeferAndPause,
			"Restore/defer resolution artifacts were not persisted truthfully.");
	}

	private static void TestExcludeCurrent(
		string root,
		EditPlanningRequest sourceRequest,
		EditPlanDocument source)
	{
		(EditPlanningRequest request, EditPlanDocument plan) =
			TwoClipFixture(sourceRequest, source);
		CandidateTimelineSnapshot missingCurrent = SnapshotFor(plan);
		missingCurrent.Tracks[0].Events.RemoveAt(1);
		AssemblyReconciliationConflictService service =
			new(Path.Combine(root, "exclude"));
		AssemblyReconciliationConflict conflict = service.Create(
			"exclude",
			2,
			request,
			plan,
			missingCurrent,
			new InvalidOperationException("deleted current"));
		Assert(conflict.SupportedResolutions.Contains(
				AssemblyReconciliationResolutionKind.ExcludeCurrentClip),
			"A safely absent current clip was not eligible for explicit exclusion.");
		AssemblyReconciliationResolution resolution = service.SaveResolution(
			conflict,
			AssemblyReconciliationResolutionKind.ExcludeCurrentClip,
			"",
			"Exclude the unavailable second clip.");
		Assert(
			resolution.Kind ==
				AssemblyReconciliationResolutionKind.ExcludeCurrentClip,
			"The exclusion choice was not persisted.");

		AssemblySketch sketch = SketchFor(request);
		string excluded = sketch.ClipOrder[1].Clip.MediaPath;
		AssemblyReconciliationConflictService.ExcludeCurrentFromSketch(
			sketch, 2, excluded);
		Assert(sketch.ClipOrder.Count == 1 &&
			sketch.ClipOrder[0].Order == 1,
			"Excluding the current clip did not keep a contiguous sketch.");
	}

	private static void TestAdoptAdditional(
		string root,
		EditPlanningRequest sourceRequest,
		EditPlanDocument source)
	{
		EditPlanningRequest request = Clone(sourceRequest);
		Core.Domain.Clip.Clip remaining = Clone(request.Clips[0]);
		remaining.FilePath = "fixtures/remaining-additional.mp4";
		remaining.DurationSeconds = Math.Max(remaining.DurationSeconds, 10);
		request.Clips.Add(remaining);
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		EditPlanDocument plan = Clone(source);
		CandidateTimelineSnapshot snapshot = SnapshotFor(plan);
		CandidateEventSnapshot added = Event(
			remaining.FilePath,
			plan.Montage.Placements.Max(item => item.TimelineEndSeconds) + 0.25,
			1.5,
			0.5);
		snapshot.Tracks[0].Events.Add(added);
		AssemblyReconciliationConflictService service =
			new(Path.Combine(root, "adopt-additional"));
		AssemblyReconciliationConflict conflict = service.Create(
			"adopt-additional",
			1,
			request,
			plan,
			snapshot,
			new InvalidOperationException("unexpected"));
		AssemblyReconciliationEventCandidate candidate = conflict.Candidates
			.Single(item => item.CanAdoptAsAdditional);
		service.SaveResolution(
			conflict,
			AssemblyReconciliationResolutionKind.AdoptKnownRemainingEvent,
			candidate.CandidateId,
			"Adopt this reviewed remaining clip.");
		EditPlanDocument adopted =
			AssemblyReconciliationConflictService.Adopt(
				request, plan, added, asCurrent: false);
		Assert(adopted.Montage.Placements.Count ==
				plan.Montage.Placements.Count + 1 &&
			string.Equals(
				adopted.Montage.Placements.Last().Clip.FilePath,
				remaining.FilePath,
				StringComparison.OrdinalIgnoreCase),
			"Known remaining media was not adopted as an additional clip.");
	}

	private static void TestAdoptAsCurrent(
		string root,
		EditPlanningRequest sourceRequest,
		EditPlanDocument source)
	{
		(EditPlanningRequest request, EditPlanDocument plan) =
			TwoClipFixture(sourceRequest, source);
		Core.Domain.Clip.Clip remaining = Clone(request.Clips[0]);
		remaining.FilePath = "fixtures/remaining-current.mp4";
		remaining.DurationSeconds = Math.Max(remaining.DurationSeconds, 10);
		request.Clips.Add(remaining);
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		CandidateTimelineSnapshot snapshot = SnapshotFor(plan);
		snapshot.Tracks[0].Events.RemoveAt(1);
		CandidateEventSnapshot replacement = Event(
			remaining.FilePath,
			plan.Montage.Placements[1].TimelineStartSeconds,
			1.25,
			0.25);
		snapshot.Tracks[0].Events.Add(replacement);
		AssemblyReconciliationConflictService service =
			new(Path.Combine(root, "adopt-current"));
		AssemblyReconciliationConflict conflict = service.Create(
			"adopt-current",
			2,
			request,
			plan,
			snapshot,
			new InvalidOperationException("missing current plus unexpected"));
		AssemblyReconciliationEventCandidate candidate = conflict.Candidates
			.Single(item => item.CanAdoptAsCurrent);
		EditPlanDocument adopted =
			AssemblyReconciliationConflictService.Adopt(
				request, plan, replacement, asCurrent: true);
		Assert(adopted.Montage.Placements.Count ==
				plan.Montage.Placements.Count &&
			string.Equals(
				adopted.Montage.Placements.Last().Clip.FilePath,
				remaining.FilePath,
				StringComparison.OrdinalIgnoreCase),
			"Known remaining media did not replace the unavailable current clip.");
	}

	private static void TestForeignAndAmbiguousEventsRemainBlocked(
		string root,
		EditPlanningRequest request,
		EditPlanDocument source)
	{
		EditPlanDocument plan = Clone(source);
		CandidateTimelineSnapshot snapshot = SnapshotFor(plan);
		snapshot.Tracks[0].Events.Add(Event(
			"fixtures/foreign.mp4", 5, 1, 0));
		AssemblyReconciliationConflict conflict =
			new AssemblyReconciliationConflictService(
				Path.Combine(root, "foreign"))
			.Create(
				"foreign",
				1,
				request,
				plan,
				snapshot,
				new InvalidOperationException("unexpected foreign event"));
		Assert(conflict.Candidates.All(item => !item.CanAdoptAsCurrent &&
				!item.CanAdoptAsAdditional) &&
			!conflict.SupportedResolutions.Contains(
				AssemblyReconciliationResolutionKind.AdoptKnownRemainingEvent),
			"Foreign media was incorrectly offered for adoption.");

		CandidateTimelineSnapshot ambiguous = SnapshotFor(plan);
		CandidateEventSnapshot duplicate = Event(
			plan.Montage.Placements[0].Clip.FilePath,
			4,
			plan.Montage.Placements[0].LengthSeconds,
			plan.Montage.Placements[0].SourceOffsetSeconds);
		ambiguous.Tracks[0].Events.Add(duplicate);
		AssemblyReconciliationConflict duplicateConflict =
			new AssemblyReconciliationConflictService(
				Path.Combine(root, "ambiguous"))
			.Create(
				"ambiguous",
				1,
				request,
				plan,
				ambiguous,
				new InvalidOperationException("ambiguous"));
		Assert(duplicateConflict.Issues.Any(item =>
				item.Kind == AssemblyReconciliationConflictKind.AmbiguousIdentity) &&
			!duplicateConflict.SupportedResolutions.Contains(
				AssemblyReconciliationResolutionKind.AdoptKnownRemainingEvent),
			"Ambiguous duplicate media was incorrectly offered for silent adoption.");
	}

	private static void TestPendingResolutionReplay(
		string root,
		EditPlanningRequest sourceRequest,
		EditPlanDocument source)
	{
		string restoreRoot = Path.Combine(root, "replay-restore");
		CandidateTimelineSnapshot missing = SnapshotFor(source);
		missing.Tracks[0].Events.Clear();
		AssemblyReconciliationConflictService restoreService =
			new(restoreRoot);
		AssemblyReconciliationConflict restoreConflict = restoreService.Create(
			"replay-restore",
			1,
			sourceRequest,
			source,
			missing,
			new InvalidOperationException("deleted"));
		AssemblyReconciliationResolution restoreIntent =
			restoreService.SaveResolution(
				restoreConflict,
				AssemblyReconciliationResolutionKind.RestoreExactProposal,
				"",
				"restore");
		AssemblyReconciliationConflictService restartedRestore =
			new(restoreRoot);
		Assert(restartedRestore.ReadCurrentResolution(1)?.ResolutionId ==
				restoreIntent.ResolutionId &&
			restartedRestore.ReadConflict(1, restoreIntent.ConflictId)
				.ConflictId == restoreConflict.ConflictId,
			"A saved restore intent was not recoverable after restart.");
		restartedRestore.CompleteResolution(restoreIntent);
		Assert(restartedRestore.ReadCurrentResolution(1) == null,
			"A completed restore intent remained pending.");

		(EditPlanningRequest excludeRequest, EditPlanDocument excludePlan) =
			TwoClipFixture(sourceRequest, source);
		CandidateTimelineSnapshot missingCurrent = SnapshotFor(excludePlan);
		missingCurrent.Tracks[0].Events.RemoveAt(1);
		string excludeRoot = Path.Combine(root, "replay-exclude");
		AssemblyReconciliationConflictService excludeService =
			new(excludeRoot);
		AssemblyReconciliationConflict excludeConflict = excludeService.Create(
			"replay-exclude",
			2,
			excludeRequest,
			excludePlan,
			missingCurrent,
			new InvalidOperationException("deleted"));
		excludeService.SaveActionIntent(
			excludeConflict,
			ActionFor(
				"exclude-action",
				"replay-exclude",
				2,
				AssemblyActionKind.ExcludeReconciliationClip));
		AssemblyReconciliationResolution excludeIntent =
			excludeService.SaveResolution(
				excludeConflict,
				AssemblyReconciliationResolutionKind.ExcludeCurrentClip,
				"",
				"exclude");
		AssemblySketch excludeSketch = SketchFor(excludeRequest);
		string excludedPath = excludePlan.Montage.Placements.Last().Clip.FilePath;
		Assert(
			AssemblyReconciliationConflictService.ExcludeCurrentFromSketch(
				excludeSketch, 2, excludedPath) &&
			!AssemblyReconciliationConflictService.ExcludeCurrentFromSketch(
				excludeSketch, 2, excludedPath),
			"Replaying exclusion was not idempotent.");
		Assert(new AssemblyReconciliationConflictService(excludeRoot)
				.ReadCurrentResolution(2)?.ResolutionId ==
					excludeIntent.ResolutionId,
			"A saved exclusion intent was not recoverable after sketch mutation.");
		EditPlanDocument excludedPlan = Clone(excludePlan);
		excludedPlan.Montage.Placements.RemoveAt(
			excludedPlan.Montage.Placements.Count - 1);
		excludedPlan.Montage.SyncAssignments.Clear();
		EditPlanDocumentValidator.ValidateAndNormalize(excludedPlan);
		excludeService.SaveVerifiedOutcome(
			excludeIntent, excludedPlan, missingCurrent);
		Assert(new AssemblyReconciliationConflictService(excludeRoot)
				.CompleteAcceptedResolutionIfVerified(2, excludedPlan) &&
			new AssemblyReconciliationConflictService(excludeRoot)
				.ReadCurrentResolution(2) == null &&
			new AssemblyReconciliationConflictService(excludeRoot)
				.ReadActionIntent(2) == null,
			"Recovery did not complete an exclusion intent after the accepted plan " +
			"was persisted.");

		EditPlanningRequest adoptRequest = Clone(sourceRequest);
		Core.Domain.Clip.Clip remaining = Clone(adoptRequest.Clips[0]);
		remaining.FilePath = "fixtures/replay-adopt.mp4";
		remaining.DurationSeconds = Math.Max(remaining.DurationSeconds, 10);
		adoptRequest.Clips.Add(remaining);
		EditPlanningRequestValidator.ValidateAndNormalize(adoptRequest);
		CandidateTimelineSnapshot added = SnapshotFor(source);
		added.Tracks[0].Events.Add(Event(
			remaining.FilePath,
			source.Montage.Placements.Last().TimelineEndSeconds + 0.25,
			1,
			0));
		string adoptRoot = Path.Combine(root, "replay-adopt");
		AssemblyReconciliationConflictService adoptService = new(adoptRoot);
		AssemblyReconciliationConflict adoptConflict = adoptService.Create(
			"replay-adopt",
			1,
			adoptRequest,
			source,
			added,
			new InvalidOperationException("unexpected"));
		AssemblyReconciliationEventCandidate candidate =
			adoptConflict.Candidates.Single(item => item.CanAdoptAsAdditional);
		AssemblyAction adoptAction = ActionFor(
			"adopt-action",
			"replay-adopt",
			1,
			AssemblyActionKind.AdoptReconciliationEvent);
		adoptAction.TargetId = candidate.CandidateId;
		adoptService.SaveActionIntent(adoptConflict, adoptAction);
		AssemblyReconciliationResolution adoptIntent =
			adoptService.SaveResolution(
				adoptConflict,
				AssemblyReconciliationResolutionKind.AdoptKnownRemainingEvent,
				candidate.CandidateId,
				"adopt");
		AssemblySketch adoptSketch = SketchFor(adoptRequest);
		AssemblyReconciliationConflictService.ReorderSketchForAdoption(
			adoptSketch, 1, remaining.FilePath, asCurrent: false);
		Assert(!AssemblyReconciliationConflictService.ReorderSketchForAdoption(
				adoptSketch, 1, remaining.FilePath, asCurrent: false),
			"Replaying an additional-clip adoption was not idempotent.");
		Assert(new AssemblyReconciliationConflictService(adoptRoot)
				.ReadCurrentResolution(1)?.ResolutionId ==
					adoptIntent.ResolutionId,
			"A saved adoption intent was not recoverable after sketch mutation.");
		CandidateEventSnapshot adoptedEvent =
			adoptService.RequireLiveCandidate(added, candidate);
		EditPlanDocument adoptedPlan =
			AssemblyReconciliationConflictService.Adopt(
				adoptRequest, source, adoptedEvent, asCurrent: false);
		adoptService.SaveVerifiedOutcome(
			adoptIntent, adoptedPlan, added);
		Assert(new AssemblyReconciliationConflictService(adoptRoot)
				.CompleteAcceptedResolutionIfVerified(1, adoptedPlan) &&
			new AssemblyReconciliationConflictService(adoptRoot)
				.ReadCurrentResolution(1) == null &&
			new AssemblyReconciliationConflictService(adoptRoot)
				.ReadActionIntent(1) == null,
			"Recovery did not complete an adoption intent after the accepted plan " +
			"was persisted.");
	}

	private static void TestUnsafeAndChangedCandidatesFailClosed(
		string root,
		EditPlanningRequest sourceRequest,
		EditPlanDocument source)
	{
		EditPlanningRequest request = Clone(sourceRequest);
		Core.Domain.Clip.Clip remaining = Clone(request.Clips[0]);
		remaining.FilePath = "fixtures/unsafe-overlap.mp4";
		remaining.DurationSeconds = Math.Max(remaining.DurationSeconds, 10);
		request.Clips.Add(remaining);
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		EditPlanDocument plan = Clone(source);
		CandidateTimelineSnapshot overlap = SnapshotFor(plan);
		overlap.Tracks[0].Events.Add(Event(
			remaining.FilePath,
			plan.Montage.Placements[0].TimelineStartSeconds + 0.1,
			1,
			0));
		AssemblyReconciliationConflict overlapConflict =
			new AssemblyReconciliationConflictService(
				Path.Combine(root, "unsafe-overlap"))
			.Create(
				"unsafe-overlap",
				1,
				request,
				plan,
				overlap,
				new InvalidOperationException("unexpected overlap"));
		Assert(overlapConflict.Candidates.All(item => !item.CanAdoptAsAdditional) &&
			!overlapConflict.SupportedResolutions.Contains(
				AssemblyReconciliationResolutionKind.AdoptKnownRemainingEvent),
			"An overlapping unexpected event was offered for adoption.");

		CandidateTimelineSnapshot valid = SnapshotFor(plan);
		CandidateEventSnapshot added = Event(
			remaining.FilePath,
			plan.Montage.Placements[0].TimelineEndSeconds + 0.25,
			1,
			0);
		valid.Tracks[0].Events.Add(added);
		AssemblyReconciliationConflictService service =
			new(Path.Combine(root, "changed-after-display"));
		AssemblyReconciliationConflict conflict = service.Create(
			"changed-after-display",
			1,
			request,
			plan,
			valid,
			new InvalidOperationException("unexpected"));
		AssemblyReconciliationEventCandidate candidate =
			conflict.Candidates.Single(item => item.CanAdoptAsAdditional);
		CandidateTimelineSnapshot changed = SnapshotFor(plan);
		CandidateEventSnapshot moved = Event(
			remaining.FilePath,
			added.TimelineStart.TotalSeconds + 0.5,
			1,
			0);
		changed.Tracks[0].Events.Add(moved);
		ExpectFailure(
			() => service.RequireLiveCandidate(changed, candidate),
			"changed",
			"A candidate changed after display but was still resolved by its stale ID.");

		CandidateEventSnapshot speedChanged = Event(
			remaining.FilePath,
			added.TimelineStart.TotalSeconds,
			1,
			0);
		speedChanged.Velocity = new List<CandidateVelocityPoint>
		{
			new() { Offset = TimeSpan.Zero, Velocity = 1.25 }
		};
		Assert(
			!string.Equals(
				AssemblyReconciliationConflictService.CandidateId(added),
				AssemblyReconciliationConflictService.CandidateId(speedChanged),
				StringComparison.Ordinal),
			"Candidate identity did not bind the displayed constant velocity.");
	}

	private static (EditPlanningRequest Request, EditPlanDocument Plan) TwoClipFixture(
		EditPlanningRequest sourceRequest,
		EditPlanDocument source)
	{
		EditPlanningRequest request = Clone(sourceRequest);
		EditPlanDocument plan = Clone(source);
		Core.Domain.Clip.Clip clip = Clone(request.Clips[0]);
		clip.FilePath = "fixtures/conflict-second.mp4";
		clip.DurationSeconds = Math.Max(clip.DurationSeconds, 10);
		request.Clips.Add(clip);
		ClipPlacement second = Clone(plan.Montage.Placements[0]);
		second.Clip = clip;
		second.TimelineStartSeconds =
			plan.Montage.Placements[0].TimelineEndSeconds + 0.25;
		second.AssignedBeatTimesSeconds.Clear();
		plan.Montage.Placements.Add(second);
		plan.Montage.SyncAssignments.Clear();
		EditPlanDocumentValidator.ValidateAndNormalize(plan);
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		return (request, plan);
	}

	private static AssemblySketch SketchFor(EditPlanningRequest request) => new()
	{
		RequestId = request.RequestId,
		EditorialThesis = "Test deterministic conflict choices.",
		Sections = new List<AssemblySectionIntent>
		{
			new()
			{
				SectionId = "section",
				EditorialRole = "assembly",
				EnergyDirection = "build",
				PacingIntent = "clear",
				Rationale = "test"
			}
		},
		ClipOrder = request.Clips.Select((clip, index) => new AssemblyClipIntent
		{
			Order = index + 1,
			Clip = new AssemblyClipReference
			{
				ReferenceId = AssemblyReferenceIds.ForClipPath(clip.FilePath),
				MediaPath = clip.FilePath
			},
			SectionId = "section",
			EditorialRole = "clip",
			Rationale = "test",
			Confidence = 1
		}).ToList(),
		SyncStrategy = new AssemblySyncStrategy
		{
			Density = "sparse",
			Rationale = "test"
		}
	};

	private static CandidateTimelineSnapshot SnapshotFor(EditPlanDocument plan) => new()
	{
		Workspace = new CandidateWorkspaceId
		{
			SessionId = "conflict-tests",
			Iteration = 1,
			Nonce = "assembly"
		},
		Tracks = new List<CandidateTrackSnapshot>
		{
			new()
			{
				MediaKind = "Video",
				Events = plan.Montage.Placements.Select(item => Event(
					item.Clip.FilePath,
					item.TimelineStartSeconds,
					item.LengthSeconds,
					item.SourceOffsetSeconds)).ToList()
			}
		}
	};

	private static CandidateEventSnapshot Event(
		string path,
		double timelineStart,
		double duration,
		double sourceOffset) => new()
	{
		PlacementId = Path.GetFullPath(path),
		MediaPath = path,
		TimelineStart = TimeSpan.FromSeconds(timelineStart),
		TimelineDuration = TimeSpan.FromSeconds(duration),
		SourceOffset = TimeSpan.FromSeconds(sourceOffset)
	};

	private static AssemblyAction ActionFor(
		string actionId,
		string sessionId,
		int checkpoint,
		AssemblyActionKind kind) => new()
	{
		ActionId = actionId,
		SessionId = sessionId,
		Checkpoint = checkpoint,
		ExpectedStateRevision = 1,
		Kind = kind,
		CreatedUtc = new DateTimeOffset(
			2026, 7, 27, 12, 0, 0, TimeSpan.Zero)
	};

	private static EditPlanDocument Clone(EditPlanDocument value) =>
		EditPlanDocumentSerializer.DeserializePlan(
			EditPlanDocumentSerializer.SerializePlan(value));

	private static EditPlanningRequest Clone(EditPlanningRequest value) =>
		EditPlanDocumentSerializer.DeserializeRequest(
			EditPlanDocumentSerializer.SerializeRequest(value));

	private static Core.Domain.Clip.Clip Clone(Core.Domain.Clip.Clip value)
		=> ContractSerializer.Deserialize<Core.Domain.Clip.Clip>(
			ContractSerializer.Serialize(value));

	private static ClipPlacement Clone(ClipPlacement value)
		=> ContractSerializer.Deserialize<ClipPlacement>(
			ContractSerializer.Serialize(value));

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private static void ExpectFailure(
		Action action,
		string expected,
		string message)
	{
		try
		{
			action();
		}
		catch (Exception exception)
		{
			if (exception.Message.Contains(
				expected, StringComparison.OrdinalIgnoreCase))
				return;
			throw new InvalidOperationException(
				message + " Unexpected failure: " + exception.Message,
				exception);
		}
		throw new InvalidOperationException(message);
	}
}
