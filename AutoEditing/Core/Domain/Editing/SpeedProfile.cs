using System;
using System.Collections.Generic;

namespace Core.Domain.Editing
{
    /// <summary>
    /// Interpolation hint for the segment that leaves a speed point. Mirrors the
    /// ScriptPortal CurveType values the planner is allowed to use. Only shapes
    /// whose integral over a whole segment equals the straight-line trapezoid
    /// are offered (Linear trivially; Smooth because its cubic ease-in/ease-out
    /// is symmetric around the segment midpoint). Fast/Slow are asymmetric and
    /// would shift which source frame is shown at segment boundaries, breaking
    /// the planner's kill-on-beat solve, so they are deliberately absent.
    /// </summary>
    public enum SpeedCurve
    {
        Linear,
        Smooth,
    }

    /// <summary>One node of a speed profile: playback speed at an event-local time.</summary>
    public class SpeedPoint
    {
        public double EventTimeSeconds { get; set; }
        public double Speed { get; set; }
        public SpeedCurve CurveToNext { get; set; }
    }

    /// <summary>
    /// Piecewise-linear playback-speed profile over a video event's fixed
    /// timeline length (1.0 = normal speed, 0.0 = freeze frame).
    ///
    /// VEGAS velocity envelopes never change the event length: the event
    /// consumes source media equal to the integral of the speed curve over its
    /// timeline duration. This class does that math — forward (how much source
    /// a stretch of timeline consumes, trapezoid integration) and inverse (at
    /// which event-local moment a given source position is shown) — so the
    /// planner can solve source windows analytically and the harness can assert
    /// kills land on beats after warping. Speed is constant before the first
    /// point and after the last point.
    /// </summary>
    public class SpeedProfile
    {
        private const double Epsilon = 1e-9;

        public List<SpeedPoint> Points { get; } = new List<SpeedPoint>();

        /// <summary>A profile that plays the clip at normal speed throughout.</summary>
        public static SpeedProfile Flat()
        {
            SpeedProfile profile = new SpeedProfile();
            profile.AddPoint(0.0, 1.0, SpeedCurve.Linear);
            return profile;
        }

        /// <summary>True when the profile never deviates from normal speed.</summary>
        public bool IsFlat
        {
            get
            {
                foreach (SpeedPoint point in Points)
                {
                    if (Math.Abs(point.Speed - 1.0) > Epsilon)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>Appends a point; points must be added in chronological order.</summary>
        public void AddPoint(double eventTimeSeconds, double speed, SpeedCurve curveToNext)
        {
            if (Points.Count > 0 && eventTimeSeconds < Points[Points.Count - 1].EventTimeSeconds - Epsilon)
            {
                throw new ArgumentException("Speed points must be added in chronological order.");
            }
            Points.Add(new SpeedPoint
            {
                EventTimeSeconds = eventTimeSeconds,
                Speed = speed,
                CurveToNext = curveToNext,
            });
        }

        /// <summary>
        /// Seconds of source media consumed between event start and the given
        /// event-local time (trapezoid integral of the speed curve).
        /// </summary>
        public double SourceConsumedAt(double eventTimeSeconds)
        {
            if (eventTimeSeconds <= 0.0)
            {
                return 0.0;
            }
            if (Points.Count == 0)
            {
                return eventTimeSeconds;
            }

            SpeedPoint first = Points[0];
            if (eventTimeSeconds <= first.EventTimeSeconds)
            {
                return eventTimeSeconds * first.Speed;
            }

            double consumed = first.EventTimeSeconds * first.Speed;
            for (int i = 0; i < Points.Count - 1; i++)
            {
                SpeedPoint from = Points[i];
                SpeedPoint to = Points[i + 1];
                double segmentLength = to.EventTimeSeconds - from.EventTimeSeconds;
                if (segmentLength <= Epsilon)
                {
                    continue;
                }

                double slice = Math.Min(eventTimeSeconds, to.EventTimeSeconds) - from.EventTimeSeconds;
                double speedAtSliceEnd = from.Speed + (to.Speed - from.Speed) * slice / segmentLength;
                consumed += (from.Speed + speedAtSliceEnd) / 2.0 * slice;

                if (eventTimeSeconds <= to.EventTimeSeconds)
                {
                    return consumed;
                }
            }

            SpeedPoint last = Points[Points.Count - 1];
            consumed += (eventTimeSeconds - last.EventTimeSeconds) * last.Speed;
            return consumed;
        }

        /// <summary>
        /// Inverse of <see cref="SourceConsumedAt"/>: the event-local time at
        /// which the given amount of source media has been consumed (i.e. when
        /// that source position is shown). During a freeze the same source
        /// position spans a time range; the start of that range is returned.
        /// </summary>
        public double EventTimeForSource(double sourceSeconds)
        {
            if (sourceSeconds <= 0.0 || Points.Count == 0)
            {
                return Math.Max(0.0, sourceSeconds);
            }

            SpeedPoint first = Points[0];
            double accumulated = 0.0;
            if (first.EventTimeSeconds > 0.0)
            {
                double beforeFirst = first.EventTimeSeconds * first.Speed;
                if (beforeFirst >= sourceSeconds - Epsilon)
                {
                    return first.Speed <= Epsilon ? 0.0 : sourceSeconds / first.Speed;
                }
                accumulated = beforeFirst;
            }

            for (int i = 0; i < Points.Count - 1; i++)
            {
                SpeedPoint from = Points[i];
                SpeedPoint to = Points[i + 1];
                double segmentLength = to.EventTimeSeconds - from.EventTimeSeconds;
                if (segmentLength <= Epsilon)
                {
                    continue;
                }

                double segmentSource = (from.Speed + to.Speed) / 2.0 * segmentLength;
                if (accumulated + segmentSource >= sourceSeconds - Epsilon)
                {
                    double remaining = sourceSeconds - accumulated;
                    if (remaining <= Epsilon)
                    {
                        return from.EventTimeSeconds;
                    }
                    return from.EventTimeSeconds + SolveSegmentTime(from.Speed, to.Speed, segmentLength, remaining);
                }
                accumulated += segmentSource;
            }

            SpeedPoint last = Points[Points.Count - 1];
            if (last.Speed <= Epsilon)
            {
                return last.EventTimeSeconds;
            }
            return last.EventTimeSeconds + (sourceSeconds - accumulated) / last.Speed;
        }

        /// <summary>
        /// Time into a linear-speed segment (from speed v0 to v1 over dt) at
        /// which it has consumed the given amount of source. Solves
        /// s = v0*t + slope*t^2/2 for t.
        /// </summary>
        private static double SolveSegmentTime(double v0, double v1, double dt, double source)
        {
            double slope = (v1 - v0) / dt;
            double time;
            if (Math.Abs(slope) < Epsilon)
            {
                time = v0 <= Epsilon ? dt : source / v0;
            }
            else
            {
                double discriminant = v0 * v0 + 2.0 * slope * source;
                if (discriminant < 0.0)
                {
                    discriminant = 0.0;
                }
                time = (-v0 + Math.Sqrt(discriminant)) / slope;
            }
            return Math.Max(0.0, Math.Min(dt, time));
        }
    }
}
