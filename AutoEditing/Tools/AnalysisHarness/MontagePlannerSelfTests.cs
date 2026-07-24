using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain.Audio;
using Core.Domain.Audio.SongAnalysis;
using Core.Domain.Clip;
using Core.Domain.Editing;

namespace AnalysisHarness
{
	internal static class MontagePlannerSelfTests
	{
		public static void RunAll()
		{
			BeatGrid beats = new BeatGrid { Bpm = 94.6, FirstBeatOffsetSeconds = 0.488 };
			for (double time = beats.FirstBeatOffsetSeconds; time < 30.0; time += beats.BeatIntervalSeconds) beats.BeatTimesSeconds.Add(time);
			Clip clip = new Clip
			{
				FilePath = "velocity-test.mp4",
				DurationSeconds = 8.0,
				Gun = "TEST",
				ShotEvents = new List<ShotEvent> { ShotEvent.Reviewed(4.0, ShotOutcome.Hit, "TEST") }
			};
			ClipPlacement placement = new MontagePlanner().PlanMontage(new List<Clip> { clip }, beats, 30.0).Single();
			double killTimeline;
			Assert(placement.SpeedProfile.TryGetTimelineTimeForSourceTime(4.0, out killTimeline), "Kill did not map into the velocity profile.");
			Assert(Math.Abs(placement.TimelineStartSeconds + killTimeline - placement.AssignedBeatTimesSeconds.Single()) <= 0.002, "Extended velocity treatment moved the kill off its beat.");
			SpeedProfilePoint killPoint = placement.SpeedProfile.Points.Single((SpeedProfilePoint item) => Math.Abs(item.SourceTimeSeconds - 4.0) < 0.000001);
			Assert(killPoint.Speed >= 1.199, "Kill is not kept in the accelerated cruise portion of the clip.");
			List<double> slowTimelinePoints = new List<double>();
			foreach (SpeedProfilePoint point in placement.SpeedProfile.Points.Where((SpeedProfilePoint item) => item.Speed <= 0.351))
			{
				double timeline;
				if (placement.SpeedProfile.TryGetTimelineTimeForSourceTime(point.SourceTimeSeconds, out timeline)) slowTimelinePoints.Add(timeline);
			}
			Assert(slowTimelinePoints.Count >= 2, "Velocity treatment has no visible slow-motion hold.");
			Assert(slowTimelinePoints.Max() - slowTimelinePoints.Min() >= 0.08, "Velocity slow-motion valley is still approximately frame-sized.");
			Assert(slowTimelinePoints.Max() - slowTimelinePoints.Min() <= 0.35, "Velocity slow-motion hold is excessively long on the rendered timeline.");
			Assert(slowTimelinePoints.Min() > killTimeline, "Slow-motion starts before the kill instead of after scope-out.");
			Assert(placement.SpeedProfile.Points.Where((SpeedProfilePoint item) => item.SourceTimeSeconds <= 4.0).All((SpeedProfilePoint item) => item.Speed >= 1.0), "Normal footage slowed below 100% before the kill.");

			MontageSongPlanningInput slowCruiseInput = CreateReviewedInput();
			slowCruiseInput.Events.Add(Event("too-far", 2.0, MontageSongEventClassification.GameplayAnchor, "region"));
			MontagePlanningResult slowCruise = new MontagePlanner().PlanMontage(new List<Clip> { CreateClip("slow-cruise.mp4", 2.0) }, slowCruiseInput);
			Assert(!slowCruise.IsFeasible, "Planner stretched normal footage below 100% to reach a distant anchor.");
			TestNonuniformBeatTimesAreHonored();
			TestEditorialClassificationAndCapacity();
			TestTimingOffsetAndDeterminism();
			TestUnassignedMapUsesSuggestedAnchors();
			TestLockedAndRegionBehavior();
			TestClipOrderUsesRolesAndTimingInsteadOfSequence();
			Console.WriteLine("Montage-planner velocity self-tests passed.");
		}

		private static void TestNonuniformBeatTimesAreHonored()
		{
			BeatGrid grid = new BeatGrid { Bpm = 120.0, FirstBeatOffsetSeconds = 0.37, BeatTimesSeconds = new List<double> { 0.37, 1.60, 4.90 } };
			ClipPlacement placement = new MontagePlanner().PlanMontage(new List<Clip> { CreateClip("nonuniform.mp4", 2.0) }, grid, 10.0).Single();
			Assert(Math.Abs(placement.AssignedBeatTimesSeconds.Single() - 1.60) < 0.000001, "Legacy adaptation synthesized an arithmetic beat instead of honoring BeatTimesSeconds.");
		}

		private static void TestEditorialClassificationAndCapacity()
		{
			MontageSongPlanningInput input = CreateReviewedInput();
			input.Events.Add(Event("effect", 0.8, MontageSongEventClassification.Effect, "region"));
			input.Events.Add(Event("unused", 1.0, MontageSongEventClassification.GameplayAnchor | MontageSongEventClassification.IntentionallyUnused, "region"));
			input.Events.Add(Event("gameplay", 1.2, MontageSongEventClassification.GameplayAnchor, "region"));
			MontagePlanningResult feasible = new MontagePlanner().PlanMontage(new List<Clip> { CreateClip("classification.mp4", 2.0) }, input);
			Assert(feasible.IsFeasible, "A valid gameplay anchor was not planned.");
			Assert(feasible.Assignments.Single().MusicEventId == "gameplay", "Effect-only or intentionally-unused event was consumed as a gameplay anchor.");

			MontagePlanningResult sparse = new MontagePlanner().PlanMontage(new List<Clip> { CreateClip("sparse.mp4", 2.0, 3.5) }, input);
			Assert(!sparse.IsFeasible, "A sparse reviewed map unexpectedly satisfied two kills with one anchor.");
			Assert(sparse.Diagnostics.Any((MontageSongPlanningDiagnostic item) => item.Code == "insufficient-gameplay-anchors"), "Sparse-map failure did not return the structured capacity diagnostic.");
		}

		private static void TestTimingOffsetAndDeterminism()
		{
			SongAnalysis analysis = new SongAnalysis
			{
				Song = new SongIdentity { ContentFingerprint = "test-song", DurationSeconds = 10.0 },
				Regions = new List<MusicRegion> { new MusicRegion { Id = "region", StartSeconds = 0.0, EndSeconds = 10.0, Type = MusicRegionType.Action, ReviewState = MusicAnalysisReviewState.Reviewed } },
				Events = new List<MusicEvent>
				{
					new MusicEvent
					{
						Id = "offset-anchor", TimeSeconds = 1.0, Type = MusicEventType.Accent, ReviewState = MusicAnalysisReviewState.Reviewed,
						Editorial = new EditorialMetadata { TimingOffsetSeconds = 0.2, Assignments = new List<EditorialAssignment> { new EditorialAssignment { Use = EditorialUse.GameplayAnchor } } }
					}
				}
			};
			MontageSongPlanningInput input = new SongAnalysisPlanningInputAdapter().Create(analysis);
			Assert(Math.Abs(input.Events.Single().EffectiveTimeSeconds - 1.2) < 0.000001, "Editorial timing offset was not reflected in planner input.");
			MontagePlanner planner = new MontagePlanner();
			MontagePlanningResult first = planner.PlanMontage(new List<Clip> { CreateClip("offset.mp4", 2.0) }, input);
			MontagePlanningResult second = planner.PlanMontage(new List<Clip> { CreateClip("offset.mp4", 2.0) }, input);
			Assert(first.IsFeasible && second.IsFeasible, "Offset anchor was not feasible.");
			Assert(Math.Abs(first.Assignments.Single().TimelineTimeSeconds - 1.2) < 0.000001, "Planner ignored the effective offset time.");
			Assert(first.Assignments.Single().MusicEventId == second.Assignments.Single().MusicEventId && Math.Abs(first.Placements.Single().LengthSeconds - second.Placements.Single().LengthSeconds) < 0.000000001, "Repeated reviewed-map planning was not deterministic.");
		}

		private static void TestLockedAndRegionBehavior()
		{
			MontageSongPlanningInput input = CreateReviewedInput();
			input.Events.Add(Event("outside-region", 1.0, MontageSongEventClassification.GameplayAnchor, null));
			input.Events.Add(Event("unlocked", 1.2, MontageSongEventClassification.GameplayAnchor, "region"));
			MontageSongPlanningEvent locked = Event("locked", 1.1, MontageSongEventClassification.GameplayAnchor, "region");
			locked.IsLocked = true;
			locked.Priority = 1;
			input.Events.Add(locked);
			MontagePlanningResult result = new MontagePlanner().PlanMontage(new List<Clip> { CreateClip("locked.mp4", 2.0) }, input);
			Assert(result.IsFeasible, "A single locked in-region anchor should be feasible.");
			Assert(result.Assignments.Single().MusicEventId == "locked", "Planner skipped a locked anchor or consumed an out-of-region candidate: " + result.Assignments.Single().MusicEventId);
		}

		private static void TestUnassignedMapUsesSuggestedAnchors()
		{
			SongAnalysis analysis = new SongAnalysis
			{
				Song = new SongIdentity { ContentFingerprint = "unassigned-song", DurationSeconds = 10.0 },
				Regions = new List<MusicRegion> { new MusicRegion { Id = "region", StartSeconds = 0.0, EndSeconds = 10.0, Type = MusicRegionType.Action, ReviewState = MusicAnalysisReviewState.Reviewed } },
				Events = new List<MusicEvent>
				{
					new MusicEvent { Id = "beat", TimeSeconds = 1.2, Type = MusicEventType.Beat, ReviewState = MusicAnalysisReviewState.Reviewed },
					new MusicEvent { Id = "drop", TimeSeconds = 1.1, Type = MusicEventType.Drop, ReviewState = MusicAnalysisReviewState.Proposed }
				}
			};
			MontageSongPlanningInput suggested = new SongAnalysisPlanningInputAdapter().Create(analysis);
			Assert(suggested.Events.All((MontageSongPlanningEvent item) => item.IsSuggestedGameplayAnchor), "A completely unassigned map did not expose automatic gameplay suggestions.");
			Assert(!suggested.Events.Single((MontageSongPlanningEvent item) => item.Id == "drop").IsReviewed, "A proposed automatic suggestion was incorrectly marked as reviewed.");
			Assert(suggested.Diagnostics.Any((MontageSongPlanningDiagnostic item) => item.Code == "suggested-gameplay-anchors"), "Automatic suggestions were not explained by a planning diagnostic.");
			MontagePlanningResult result = new MontagePlanner().PlanMontage(new List<Clip> { CreateClip("suggested.mp4", 2.0) }, suggested);
			Assert(result.IsFeasible && result.Assignments.Single().MusicEventId == "drop", "Suggested musical priority did not prefer the drop anchor.");

			analysis.Events[0].Editorial.Assignments.Add(new EditorialAssignment { Use = EditorialUse.GameplayAnchor });
			MontageSongPlanningInput explicitMap = new SongAnalysisPlanningInputAdapter().Create(analysis);
			Assert(explicitMap.Events.Single((MontageSongPlanningEvent item) => item.Id == "beat").IsGameplayAnchor, "Explicit gameplay assignment was lost.");
			Assert(explicitMap.Events.Single((MontageSongPlanningEvent item) => item.Id == "drop").IsSuggestedGameplayAnchor, "Automatic suggestions did not supplement a partial explicit gameplay map.");
			MontagePlanningResult explicitResult = new MontagePlanner().PlanMontage(new List<Clip> { CreateClip("explicit.mp4", 2.0) }, explicitMap);
			Assert(explicitResult.IsFeasible && explicitResult.Assignments.Single().MusicEventId == "beat", "An automatic suggestion displaced a feasible explicit gameplay anchor.");
		}

		private static MontageSongPlanningInput CreateReviewedInput()
		{
			return new MontageSongPlanningInput
			{
				Mode = MontageSongPlanningMode.ReviewedSongMap,
				SongFingerprint = "test-song",
				SongDurationSeconds = 10.0,
				Regions = new List<MontageSongPlanningRegion> { new MontageSongPlanningRegion { Id = "region", StartSeconds = 0.0, EndSeconds = 10.0, Type = MusicRegionType.Action } }
			};
		}

		private static void TestClipOrderUsesRolesAndTimingInsteadOfSequence()
		{
			BeatGrid grid = new BeatGrid { Bpm = 120.0, FirstBeatOffsetSeconds = 0.5 };
			for (double time = 0.5; time < 30.0; time += 0.5) grid.BeatTimesSeconds.Add(time);
			Clip ordinaryFirst = CreateClip("A-ordinary.mp4", 2.0);
			ordinaryFirst.SequenceNumber = 99;
			Clip ordinarySecond = CreateClip("Z-ordinary.mp4", 2.0);
			ordinarySecond.SequenceNumber = 1;
			List<ClipPlacement> ordinaryPlan = new MontagePlanner().PlanMontage(new List<Clip> { ordinarySecond, ordinaryFirst }, grid, 30.0);
			Assert(ordinaryPlan[0].Clip == ordinaryFirst, "Ordinary clips were still ordered chronologically by sequence number.");

			Clip opener = CreateClip("opener.mp4", 2.0);
			opener.IsOpener = true;
			Clip closer = CreateClip("closer.mp4", 2.0);
			closer.IsCloser = true;
			List<ClipPlacement> constrained = new MontagePlanner().PlanMontage(new List<Clip> { closer, ordinaryFirst, opener }, grid, 30.0);
			Assert(constrained.First().Clip == opener && constrained.Last().Clip == closer, "Explicit opener/closer roles were not preserved.");
		}

		private static MontageSongPlanningEvent Event(string id, double time, MontageSongEventClassification classification, string regionId)
		{
			return new MontageSongPlanningEvent { Id = id, SourceTimeSeconds = time, EffectiveTimeSeconds = time, Classification = classification, ContainingRegionId = regionId };
		}

		private static Clip CreateClip(string path, params double[] killTimes)
		{
			return new Clip
			{
				FilePath = path,
				DurationSeconds = Math.Max(8.0, killTimes.Max() + 1.0),
				Gun = "TEST",
				ShotEvents = killTimes.Select((double time) => ShotEvent.Reviewed(time, ShotOutcome.Hit, "TEST")).ToList()
			};
		}

		private static void Assert(bool condition, string message)
		{
			if (!condition) throw new InvalidOperationException(message);
		}
	}
}
