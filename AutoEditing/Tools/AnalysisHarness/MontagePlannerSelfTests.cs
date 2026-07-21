using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain.Audio;
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

			ClipPlacement constrainedPlacement = new MontagePlanner(0.5, 0.75, 0.35, 2.0).PlanMontage(new List<Clip> { clip }, beats, 30.0).Single();
			SpeedProfilePoint constrainedKillPoint = constrainedPlacement.SpeedProfile.Points.Single((SpeedProfilePoint item) => Math.Abs(item.SourceTimeSeconds - 4.0) < 0.000001);
			Assert(constrainedKillPoint.Speed < 1.2 && constrainedKillPoint.Speed >= 0.35, "Planner did not use its bounded fallback when no preferred accelerated beat assignment existed.");
			Console.WriteLine("Montage-planner velocity self-tests passed.");
		}

		private static void Assert(bool condition, string message)
		{
			if (!condition) throw new InvalidOperationException(message);
		}
	}
}
