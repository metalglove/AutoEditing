using System;
using System.Collections.Generic;
using Core.Domain.Audio;
using Core.Domain.Editing;

namespace AnalysisHarness
{
    /// <summary>
    /// Self-tests for the Phase 1 velocity time-remapping math: the
    /// <see cref="SpeedProfile"/> integrals and the planner's source-window
    /// solve. Everything is synthetic — no audio or video files needed — so the
    /// kill-on-beat guarantee can be asserted without VEGAS. Run with
    /// AnalysisHarness.exe --test-speed; exits non-zero on any failure.
    /// </summary>
    internal static class SpeedProfileTests
    {
        private const double Tolerance = 1e-6;

        /// <summary>120 BPM grid, first beat at 0.5s: interval is a round 0.5s.</summary>
        private const double BeatInterval = 0.5;
        private const double FirstBeat = 0.5;

        private static int failures;

        public static int RunAll()
        {
            failures = 0;

            Run("Flat profile consumes real time", TestFlatProfileConsumesRealTime);
            Run("Constant-speed integral", TestConstantSpeedIntegral);
            Run("Ramp integral matches trapezoid", TestRampIntegralMatchesTrapezoid);
            Run("Inverse mapping round trip", TestInverseMappingRoundTrip);
            Run("Freeze consumes no source", TestFreezeConsumesNoSource);
            Run("Planner lands kill on beat", TestPlannerLandsKillOnBeat);
            Run("Planner freezes highlight clips", TestPlannerFreezesHighlightClips);
            Run("Planner solves slow lead-in near clip start", TestPlannerSolvesSlowLeadIn);
            Run("Planner falls back flat when lead-in impossible", TestPlannerFallsBackWhenLeadInImpossible);
            Run("Planner shrinks slot when tail source runs out", TestPlannerShrinksSlotWhenTailRunsOut);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL SPEED TESTS PASSED" : $"{failures} SPEED TEST FAILURE(S)");
            return failures == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            int failuresBefore = failures;
            try
            {
                test();
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"  FAIL {name}: threw {ex.GetType().Name}: {ex.Message}");
                return;
            }
            Console.WriteLine(failures == failuresBefore ? $"  PASS {name}" : $"  FAIL {name}");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                failures++;
                Console.WriteLine($"    assert failed: {message}");
            }
        }

        private static void CheckClose(double actual, double expected, string message)
        {
            if (Math.Abs(actual - expected) > Tolerance)
            {
                failures++;
                Console.WriteLine($"    assert failed: {message} (expected {expected:F6}, got {actual:F6})");
            }
        }

        private static void TestFlatProfileConsumesRealTime()
        {
            SpeedProfile flat = SpeedProfile.Flat();
            Check(flat.IsFlat, "Flat() should report IsFlat");
            CheckClose(flat.SourceConsumedAt(3.7), 3.7, "flat consumption at 3.7s");
            CheckClose(flat.EventTimeForSource(3.7), 3.7, "flat inverse at 3.7s");
        }

        private static void TestConstantSpeedIntegral()
        {
            SpeedProfile profile = new SpeedProfile();
            profile.AddPoint(0.0, 2.0, SpeedCurve.Linear);
            CheckClose(profile.SourceConsumedAt(1.5), 3.0, "2x speed consumes 2x source");
            CheckClose(profile.EventTimeForSource(3.0), 1.5, "inverse of 2x speed");
        }

        private static void TestRampIntegralMatchesTrapezoid()
        {
            // 1.0 -> 2.0 over one second: trapezoid area is 1.5.
            SpeedProfile profile = new SpeedProfile();
            profile.AddPoint(0.0, 1.0, SpeedCurve.Linear);
            profile.AddPoint(1.0, 2.0, SpeedCurve.Linear);
            CheckClose(profile.SourceConsumedAt(1.0), 1.5, "ramp integral over full segment");
            // Halfway through the ramp: v(t) = 1 + t, integral = t + t^2/2 = 0.625 at t=0.5.
            CheckClose(profile.SourceConsumedAt(0.5), 0.625, "ramp integral over half segment");
            // Past the last point the final speed holds: 1.5 + 2.0 * 0.5.
            CheckClose(profile.SourceConsumedAt(1.5), 2.5, "constant tail after last point");
        }

        private static void TestInverseMappingRoundTrip()
        {
            SpeedProfile profile = new SpeedProfile();
            profile.AddPoint(0.0, 1.2, SpeedCurve.Linear);
            profile.AddPoint(0.5, 1.2, SpeedCurve.Smooth);
            profile.AddPoint(1.0, 0.35, SpeedCurve.Smooth);
            profile.AddPoint(1.5, 1.0, SpeedCurve.Linear);

            double[] sampleTimes = { 0.0, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
            foreach (double t in sampleTimes)
            {
                double roundTrip = profile.EventTimeForSource(profile.SourceConsumedAt(t));
                CheckClose(roundTrip, t, $"round trip at t={t:F2}");
            }
        }

        private static void TestFreezeConsumesNoSource()
        {
            // Ramp 1.0 -> 0.0 over [0,1], freeze over [1,2], recover to 1.0 by 2.5.
            SpeedProfile profile = new SpeedProfile();
            profile.AddPoint(0.0, 1.0, SpeedCurve.Linear);
            profile.AddPoint(1.0, 0.0, SpeedCurve.Linear);
            profile.AddPoint(2.0, 0.0, SpeedCurve.Smooth);
            profile.AddPoint(2.5, 1.0, SpeedCurve.Linear);

            CheckClose(profile.SourceConsumedAt(1.0), 0.5, "consumption at freeze start");
            CheckClose(profile.SourceConsumedAt(2.0), 0.5, "freeze adds no consumption");
            CheckClose(profile.SourceConsumedAt(2.5), 0.75, "recovery ramp consumption");
            // The frozen source position maps back to the moment the freeze begins.
            CheckClose(profile.EventTimeForSource(0.5), 1.0, "frozen source maps to freeze start");
        }

        private static BeatGrid MakeBeatGrid(double songDurationSeconds)
        {
            BeatGrid grid = new BeatGrid { Bpm = 60.0 / BeatInterval, FirstBeatOffsetSeconds = FirstBeat };
            for (double t = FirstBeat; t < songDurationSeconds; t += BeatInterval)
            {
                grid.BeatTimesSeconds.Add(t);
            }
            return grid;
        }

        private static Core.Domain.Clip.Clip MakeClip(string clipType, double durationSeconds, params double[] killTimes)
        {
            return new Core.Domain.Clip.Clip
            {
                FilePath = $"synthetic-{clipType}.mp4",
                PlayerName = "Test",
                Game = "MWIII",
                Map = "Synthetic",
                Gun = "MORS",
                ClipType = clipType,
                DurationSeconds = durationSeconds,
                KillTimesSeconds = new List<double>(killTimes),
            };
        }

        private static ClipPlacement PlanSingle(Core.Domain.Clip.Clip clip)
        {
            MontagePlanner planner = new MontagePlanner();
            List<ClipPlacement> placements = planner.PlanMontage(
                new List<Core.Domain.Clip.Clip> { clip }, MakeBeatGrid(60.0), 60.0);
            Check(placements.Count == 1, "planner should place the synthetic clip");
            return placements.Count == 1 ? placements[0] : null;
        }

        private static void CheckKillOnBeat(ClipPlacement placement)
        {
            List<double> kills = placement.TimelineKillTimesSeconds;
            Check(kills.Count > 0, "first kill should be visible inside the event");
            if (kills.Count == 0)
            {
                return;
            }
            double beatsFromGridStart = (kills[0] - FirstBeat) / BeatInterval;
            double distanceFromBeat = Math.Abs(beatsFromGridStart - Math.Round(beatsFromGridStart));
            Check(distanceFromBeat < 1e-3, $"first kill at {kills[0]:F4}s is {distanceFromBeat:F4} beats off the grid");
        }

        private static void CheckSourceWindowFeasible(ClipPlacement placement)
        {
            Check(placement.SourceOffsetSeconds >= -Tolerance, "source offset must not be negative");
            Check(placement.SourceOffsetSeconds + placement.SourceConsumedSeconds
                <= placement.Clip.DurationSeconds + Tolerance,
                "event must not consume more source than the clip has");
        }

        private static void TestPlannerLandsKillOnBeat()
        {
            // Kill mid-clip with plenty of source on both sides: full warp.
            ClipPlacement placement = PlanSingle(MakeClip("3ON", 20.0, 8.0));
            if (placement == null)
            {
                return;
            }
            Check(!placement.Profile.IsFlat, "clip with a mid-clip kill should be warped");
            CheckKillOnBeat(placement);
            CheckSourceWindowFeasible(placement);

            // The kill's source frame must be consumed exactly at the kill beat:
            // lead-in beats = round(1.25 / 0.5) = 2, so the kill beat is 1.0s
            // into the event.
            double killEventTime = 2 * BeatInterval;
            CheckClose(placement.SourceOffsetSeconds + placement.Profile.SourceConsumedAt(killEventTime),
                8.0, "kill source frame shows exactly on the kill beat");
        }

        private static void TestPlannerFreezesHighlightClips()
        {
            ClipPlacement placement = PlanSingle(MakeClip("Triple Ender", 20.0, 8.0));
            if (placement == null)
            {
                return;
            }
            bool hasFreeze = false;
            foreach (SpeedPoint point in placement.Profile.Points)
            {
                if (Math.Abs(point.Speed) < Tolerance)
                {
                    hasFreeze = true;
                }
            }
            Check(hasFreeze, "highlight clip should get a freeze-frame profile");
            CheckKillOnBeat(placement);
            CheckSourceWindowFeasible(placement);
        }

        private static void TestPlannerSolvesSlowLeadIn()
        {
            // Kill only 0.6s into the clip: the standard 1.2x lead-in would need
            // ~0.99s of source, so the solver must slow the lead-in instead —
            // required lead speed (0.6 - 0.0875) / 0.75 ~= 0.683, above the
            // 0.5x floor — keeping the kill on its beat with offset 0.
            ClipPlacement placement = PlanSingle(MakeClip("3ON", 20.0, 0.6));
            if (placement == null)
            {
                return;
            }
            Check(!placement.Profile.IsFlat, "solvable slow lead-in should stay warped");
            CheckClose(placement.SourceOffsetSeconds, 0.0, "slow lead-in starts from the clip start");
            CheckKillOnBeat(placement);
            CheckSourceWindowFeasible(placement);
        }

        private static void TestPlannerFallsBackWhenLeadInImpossible()
        {
            // Kill 0.3s into the clip: required lead speed ~0.28x is below the
            // floor, so the planner must fall back to the flat profile.
            ClipPlacement placement = PlanSingle(MakeClip("3ON", 20.0, 0.3));
            if (placement == null)
            {
                return;
            }
            Check(placement.Profile.IsFlat, "impossible lead-in should fall back to flat");
            CheckSourceWindowFeasible(placement);
        }

        private static void TestPlannerShrinksSlotWhenTailRunsOut()
        {
            // Kill 11.5s into a 12s clip: the standard 4-beat slot would consume
            // past the end of the source, so the planner must drop to 3 beats
            // (1.5s) while keeping the kill on its beat.
            ClipPlacement placement = PlanSingle(MakeClip("3ON", 12.0, 11.5));
            if (placement == null)
            {
                return;
            }
            Check(!placement.Profile.IsFlat, "tail-limited clip should stay warped");
            CheckClose(placement.LengthSeconds, 3 * BeatInterval, "slot shrinks to 3 beats");
            CheckKillOnBeat(placement);
            CheckSourceWindowFeasible(placement);
        }
    }
}
