using AutoEditing.Iteration.Contracts.Assembly;
using Core.Domain.Audio;
using Core.Domain.Audio.SongAnalysis;
using Core.Domain.Clip;
using Core.Domain.Editing;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Assembly;

internal static class ClipStepDecisionCompilerSelfTests
{
	public static void Run()
	{
		EditPlanningRequest request = Request();
		ClipStepDecisionCompiler compiler = new();
		ClipStepCompilationResult first = compiler.Append(
			request, null, Decision(1, "a.mp4", 0, 3, 1, "event-2", 0,
				("event-3", 1)));
		Assert(first.Placement.Clip == request.Clips[0] &&
			first.Placement.TimelineStartSeconds == 1 &&
			first.Placement.LengthSeconds == 3 &&
			first.SyncAssignments.Count == 2 &&
			first.CombinedPlan.Montage.Placements.Count == 1,
			"A valid clip step did not compile to canonical domain objects.");
		EditPlanDocument persistedPrefix = EditPlanDocumentSerializer.DeserializePlan(
			EditPlanDocumentSerializer.SerializePlan(first.CombinedPlan));
		ClipStepCompilationResult resumed = compiler.Append(
			request,
			persistedPrefix,
			Decision(2, "b.mp4", 0, 3, 1, "event-5", 0));
		Assert(ReferenceEquals(
				resumed.CombinedPlan.Montage.Placements[0].Clip,
				request.Clips[0]),
			"A persisted accepted prefix was not rebound to authoritative request media.");

		ClipStepCompilationResult second = compiler.Append(
			request, first.CombinedPlan,
			Decision(2, "b.mp4", 0, 3, 1, "event-5", 0));
		Assert(second.CombinedPlan.Montage.Placements
				.Select(item => item.Clip.FilePath)
				.SequenceEqual(new[] { "a.mp4", "b.mp4" }) &&
			second.CombinedPlan.Montage.SyncAssignments.Count == 3,
			"Appending a step did not preserve and revalidate the accepted prefix.");

		ClipStepCompilationResult replaced = compiler.ReplaceCurrent(
			request, second.CombinedPlan,
			Decision(2, "c.mp4", 0, 3, 1, "event-5", 0));
		Assert(replaced.CombinedPlan.Montage.Placements
				.Select(item => item.Clip.FilePath)
				.SequenceEqual(new[] { "a.mp4", "c.mp4" }) &&
			replaced.CombinedPlan.Montage.SyncAssignments.All(item =>
				item.ClipPath != "b.mp4"),
			"Replacing the current step changed the accepted prefix or retained stale syncs.");

		ExpectFailure(() => compiler.Append(
			request, null, Decision(1, "missing.mp4", 0, 3, 1, "event-2", 0)),
			"An unavailable clip was accepted.");
		ExpectFailure(() => compiler.Append(
			request, first.CombinedPlan, Decision(2, "a.mp4", 0, 3, 1, "event-5", 0)),
			"An already-used clip was accepted.");
		ExpectFailure(() => compiler.Append(
			request, null, Decision(2, "a.mp4", 0, 3, 1, "event-2", 0)),
			"A step index inconsistent with the prefix was accepted.");
		ExpectFailure(() => compiler.Append(
			request, null, Decision(1, "a.mp4", -1, 3, 1, "event-2", 0)),
			"A negative source window was accepted.");
		ExpectFailure(() => compiler.Append(
			request, null, Decision(1, "a.mp4", 0, 7, 1, "event-2", 0)),
			"A source window extending outside the media was accepted.");
		ExpectFailure(() => compiler.Append(
			request, null, Decision(1, "a.mp4", 0, 3, 5, "event-2", 0)),
			"An unsupported speed was accepted.");

		request.EffectOptions.EnableSpeedChanges = false;
		ExpectFailure(() => compiler.Append(
			request, null, Decision(1, "a.mp4", 0, 3, 2, "event-2", 0)),
			"A speed change was accepted while speed changes were disabled.");
		request.EffectOptions.EnableSpeedChanges = true;

		ExpectFailure(() => compiler.Append(
			request, null, Decision(1, "a.mp4", 0, 3, 1, "event-2", 9)),
			"An ineligible kill index was accepted.");
		ExpectFailure(() => compiler.Append(
			request, null, Decision(1, "a.mp4", 0, 3, 1, "effect-only", 0)),
			"An ineligible song event was accepted.");
		ExpectFailure(() => compiler.Append(
			request, first.CombinedPlan, Decision(2, "b.mp4", 0, 3, 1, "event-2", 0)),
			"An already-used song event was accepted.");
		ExpectFailure(() => compiler.Append(
			request, first.CombinedPlan, Decision(2, "b.mp4", 0, 3, 1, "event-3", 0)),
			"A placement overlapping the accepted prefix was accepted.");
		ExpectFailure(() => compiler.Append(
			request, null, Decision(1, "a.mp4", 0, 3, 1, "event-0", 0)),
			"A placement before the song was accepted.");
		ExpectFailure(() => compiler.Append(
			request, null, Decision(1, "a.mp4", 0, 5, 1, "event-9", 0)),
			"A placement extending after the song was accepted.");
		ExpectFailure(() => compiler.Append(
			request, null, Decision(1, "a.mp4", 0, 3, 1, "event-2", 0,
				("event-4", 1))),
			"An inconsistent additional sync was accepted.");
		ExpectFailure(() => compiler.Append(
			request, null, Decision(1, "a.mp4", 0, 3, 1, "event-2", 0,
				("event-3", 0))),
			"A kill reused by an additional sync was accepted.");

		first.CombinedPlan.Montage.SyncAssignments[0].TimelineTimeSeconds += 0.5;
		ExpectFailure(() => compiler.Append(
			request, first.CombinedPlan, Decision(2, "b.mp4", 0, 3, 1, "event-5", 0)),
			"A corrupt accepted-prefix sync assignment was accepted.");

		VelocityCurvePoint[] dipCurve =
		{
			new() { OffsetSeconds = 0, Speed = 2.0 },
			new() { OffsetSeconds = 4, Speed = 1.0 }
		};
		ClipStepCompilationResult curved = compiler.Append(
			request, null,
			Decision(1, "a.mp4", 0, 4, 1, "event-2", 0, curve: dipCurve));
		SpeedProfile expectedProfile = new(new[]
		{
			new SpeedProfilePoint(0, 2.0),
			new SpeedProfilePoint(4, 1.0)
		});
		Assert(
			expectedProfile.TryGetTimelineTimeForSourceTime(1, out double expectedRelative),
			"Test fixture curve does not cover the expected kill source time.");
		double expectedTimelineStart = 2 - expectedRelative;
		Assert(
			curved.Placement.SpeedProfile.Points.Count == 2 &&
			Math.Abs(curved.Placement.SpeedProfile.Points[0].Speed - 2.0) < 0.000001 &&
			Math.Abs(curved.Placement.SpeedProfile.Points[1].Speed - 1.0) < 0.000001 &&
			Math.Abs(curved.Placement.TimelineStartSeconds - expectedTimelineStart) < 0.000001 &&
			Math.Abs(curved.Placement.LengthSeconds -
				expectedProfile.TimelineDurationSeconds) < 0.000001,
			"A velocity-curve decision did not compile to the expected multi-point speed profile.");

		ExpectFailure(() => compiler.Append(
			request, null,
			Decision(1, "a.mp4", 0, 4, 1, "event-2", 0, curve: new VelocityCurvePoint[]
			{
				new() { OffsetSeconds = 0.5, Speed = 2.0 },
				new() { OffsetSeconds = 4, Speed = 1.0 }
			})),
			"A velocity curve not starting at the window's first frame was accepted.");
		ExpectFailure(() => compiler.Append(
			request, null,
			Decision(1, "a.mp4", 0, 4, 1, "event-2", 0, curve: new VelocityCurvePoint[]
			{
				new() { OffsetSeconds = 0, Speed = 2.0 },
				new() { OffsetSeconds = 3, Speed = 1.0 }
			})),
			"A velocity curve not ending at the window's last frame was accepted.");

		request.EffectOptions.EnableSpeedChanges = false;
		ExpectFailure(() => compiler.Append(
			request, null,
			Decision(1, "a.mp4", 0, 4, 1, "event-2", 0, curve: dipCurve)),
			"A velocity curve was accepted while speed changes were disabled.");
		request.EffectOptions.EnableSpeedChanges = true;
	}

	private static ClipStepDecision Decision(
		int step,
		string path,
		double sourceStart,
		double sourceEnd,
		double speed,
		string eventId,
		int killIndex,
		params (string EventId, int KillIndex)[] additional) =>
		Decision(step, path, sourceStart, sourceEnd, speed, eventId, killIndex, null, additional);

	private static ClipStepDecision Decision(
		int step,
		string path,
		double sourceStart,
		double sourceEnd,
		double speed,
		string eventId,
		int killIndex,
		IList<VelocityCurvePoint>? curve,
		params (string EventId, int KillIndex)[] additional) => new()
	{
		RequestId = "step-compiler-test",
		StepIndex = step,
		Clip = new AssemblyClipReference
		{
			ReferenceId = AssemblyReferenceIds.ForClipPath(path),
			MediaPath = path
		},
		SourceWindow = new AssemblySourceWindow
		{
			StartSeconds = sourceStart,
			EndSeconds = sourceEnd,
			ConstantSpeed = speed,
			VelocityCurve = curve
		},
		PrimarySync = new AssemblySyncDecision
		{
			MusicEventId = eventId,
			KillIndex = killIndex
		},
		AdditionalSyncs = additional.Select(item => new AssemblySyncDecision
		{
			MusicEventId = item.EventId,
			KillIndex = item.KillIndex
		}).ToList(),
		Rationale = "Deterministic test decision.",
		Confidence = 0.8
	};

	private static EditPlanningRequest Request()
	{
		List<MontageSongPlanningEvent> events = new()
		{
			Event("event-0", 0, true),
			Event("event-2", 2, true),
			Event("event-3", 3, true),
			Event("event-4", 4, true),
			Event("event-5", 5, true),
			Event("event-9", 9, true),
			Event("effect-only", 6, false)
		};
		return new EditPlanningRequest
		{
			RequestId = "step-compiler-test",
			SongPath = "song.wav",
			Clips = new List<Clip>
			{
				Clip("a.mp4"), Clip("b.mp4"), Clip("c.mp4")
			},
			EffectOptions = new EffectSelectionOptions
			{
				PresetId = EffectSelectionOptions.NoAutomaticEffectsPresetId,
				EnableSpeedChanges = true
			},
			SongAnalysis = new MontageSongPlanningInput
			{
				SongFingerprint = "song-hash",
				SongDurationSeconds = 10,
				Regions = new List<MontageSongPlanningRegion>
				{
					new()
					{
						Id = "region-1",
						StartSeconds = 0,
						EndSeconds = 10,
						Type = MusicRegionType.Action
					}
				},
				Events = events,
				EventTimelineColumns = new List<string>
				{
					"timeSeconds", "type", "strength", "confidence", "reviewState"
				},
				EventTimeline = events.Select(item => new List<object>
				{
					item.EffectiveTimeSeconds, item.MusicalType.ToString(), 1.0, 1.0, "Reviewed"
				}).ToList()
			}
		};
	}

	private static Clip Clip(string path) => new()
	{
		FilePath = path,
		DurationSeconds = 5,
		ShotEvents = new List<ShotEvent>
		{
			ShotEvent.Reviewed(1, ShotOutcome.Hit),
			ShotEvent.Reviewed(2, ShotOutcome.Headshot)
		}
	};

	private static MontageSongPlanningEvent Event(
		string id,
		double time,
		bool gameplay) => new()
	{
		Id = id,
		EffectiveTimeSeconds = time,
		SourceTimeSeconds = time,
		ContainingRegionId = "region-1",
		MusicalType = MusicEventType.Beat,
		Classification = gameplay
			? MontageSongEventClassification.GameplayAnchor
			: MontageSongEventClassification.Effect,
		IsReviewed = true
	};

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
