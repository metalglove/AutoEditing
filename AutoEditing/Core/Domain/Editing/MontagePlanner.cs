using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain.Audio;

namespace Core.Domain.Editing
{
    /// <summary>
    /// Where a clip goes on the timeline: which slice of the source file plays,
    /// starting at which timeline position, warped by which speed profile.
    /// </summary>
    public class ClipPlacement
    {
        public Clip.Clip Clip { get; set; }
        public double TimelineStartSeconds { get; set; }
        public double SourceOffsetSeconds { get; set; }
        public double LengthSeconds { get; set; }

        /// <summary>
        /// Playback-speed profile over the event (event-local time). Defaults to
        /// flat 1.0x; the planner replaces it with a kill dip where feasible.
        /// </summary>
        public SpeedProfile Profile { get; set; } = SpeedProfile.Flat();

        public double TimelineEndSeconds
        {
            get { return TimelineStartSeconds + LengthSeconds; }
        }

        /// <summary>Seconds of source media the event consumes under its profile.</summary>
        public double SourceConsumedSeconds
        {
            get { return Profile.SourceConsumedAt(LengthSeconds); }
        }

        /// <summary>
        /// Kill times relative to the montage timeline (for effects such as
        /// shake at impact), mapped through the speed profile: a kill shows on
        /// the timeline at the moment its source position is consumed.
        /// </summary>
        public List<double> TimelineKillTimesSeconds
        {
            get
            {
                List<double> times = new List<double>();
                double consumedTotal = SourceConsumedSeconds;
                foreach (double killSource in Clip.KillTimesSeconds)
                {
                    double sourceIntoEvent = killSource - SourceOffsetSeconds;
                    if (sourceIntoEvent < 0.0 || sourceIntoEvent > consumedTotal)
                    {
                        continue;
                    }
                    double eventTime = Profile.EventTimeForSource(sourceIntoEvent);
                    if (eventTime <= LengthSeconds)
                    {
                        times.Add(TimelineStartSeconds + eventTime);
                    }
                }
                return times;
            }
        }
    }

    /// <summary>
    /// Plans the montage: orders clips, gives each one a slot that is a whole
    /// number of beats long, and picks the source window plus a velocity
    /// profile so the first detected kill plays exactly on a beat — at the
    /// bottom of a slow-motion dip — after time remapping. All cuts fall on
    /// beats. The profile math relies on the fact that velocity envelopes never
    /// change event length: source consumed is the integral of the speed curve.
    /// </summary>
    public class MontagePlanner
    {
        /// <summary>Seconds of lead-in gameplay shown before a clip's first kill (rounded to whole beats).</summary>
        private const double LeadInSeconds = 1.25;

        /// <summary>Seconds of gameplay kept after the last kill before cutting (rounded up to whole beats).</summary>
        private const double TailSeconds = 0.75;

        /// <summary>Slot length for clips where no kills were detected.</summary>
        private const double DefaultSlotSeconds = 2.5;

        private const double MinSlotSeconds = 1.2;
        private const double MaxSlotSeconds = 10.0;

        /// <summary>Playback speed during the lead-in, before the ramp into the kill dip.</summary>
        private const double LeadInSpeed = 1.2;

        /// <summary>
        /// Lowest lead-in speed the solver may pick when the clip has little
        /// source before its first kill. Below this the warp would look like
        /// wading through mud, so the planner falls back to a flat profile.
        /// </summary>
        private const double MinLeadInSpeed = 0.5;

        /// <summary>Speed at the bottom of the dip; the kill frame shows at this speed, on the beat.</summary>
        private const double KillDipSpeed = 0.35;

        /// <summary>Dip speed for highlight clips (enders/multis): a full freeze frame.</summary>
        private const double FreezeSpeed = 0.0;

        /// <summary>
        /// Source media kept in reserve at the tail of each warped event so
        /// VEGAS never runs out of frames (which would freeze the last frame)
        /// even if envelope curve integrals differ marginally from the
        /// planner's linear math.
        /// </summary>
        private const double SourceTailSafetySeconds = 0.1;

        private const double TimeEpsilon = 1e-6;

        public List<ClipPlacement> PlanMontage(List<Clip.Clip> clips, BeatGrid beats, double songDurationSeconds)
        {
            List<Clip.Clip> orderedClips = OrderClips(clips);
            double beatInterval = beats.BeatIntervalSeconds;
            double firstBeat = beats.FirstBeatOffsetSeconds;
            int leadInBeats = Math.Max(1, (int)Math.Round(LeadInSeconds / beatInterval));

            List<ClipPlacement> placements = new List<ClipPlacement>();
            int cursorBeat = 0;

            foreach (Clip.Clip clip in orderedClips)
            {
                double timelineStart = firstBeat + cursorBeat * beatInterval;
                int slotBeats = ChooseSlotBeats(clip, beatInterval, leadInBeats);

                if (timelineStart + slotBeats * beatInterval > songDurationSeconds)
                {
                    break;
                }

                ClipPlacement placement = PlanClipPlacement(clip, timelineStart, beatInterval, leadInBeats, ref slotBeats);
                placements.Add(placement);
                cursorBeat += slotBeats;
            }

            return placements;
        }

        /// <summary>
        /// Builds the placement for one clip: a speed profile plus the source
        /// window solved so the first kill's source frame is consumed exactly at
        /// its planned beat. Falls back to a flat (unwarped) placement when the
        /// clip cannot support the profile, and may shrink the slot by whole
        /// beats when the source runs out after the kill.
        /// </summary>
        private static ClipPlacement PlanClipPlacement(Clip.Clip clip, double timelineStart, double beatInterval, int leadInBeats, ref int slotBeats)
        {
            double length = slotBeats * beatInterval;
            double killEventTime = leadInBeats * beatInterval;

            if (clip.KillTimesSeconds.Count == 0 || killEventTime >= length - TimeEpsilon)
            {
                return CreateFlatPlacement(clip, timelineStart, beatInterval, length, leadInBeats);
            }

            double killSource = clip.KillTimesSeconds.Min();
            bool freeze = IsHighlightClip(clip);
            double leadInSpeed = LeadInSpeed;
            SpeedProfile profile = BuildKillSpeedProfile(beatInterval, killEventTime, length, leadInSpeed, freeze);
            double sourceOffset = killSource - profile.SourceConsumedAt(killEventTime);

            if (sourceOffset < 0.0)
            {
                // Not enough source before the kill at full lead-in speed: slow
                // the lead-in down so the profile consumes exactly the source
                // that exists, keeping the kill frame on its beat.
                leadInSpeed = SolveLeadInSpeed(killSource, killEventTime, beatInterval, freeze);
                if (leadInSpeed < MinLeadInSpeed)
                {
                    return CreateFlatPlacement(clip, timelineStart, beatInterval, length, leadInBeats);
                }
                profile = BuildKillSpeedProfile(beatInterval, killEventTime, length, leadInSpeed, freeze);
                sourceOffset = Math.Max(0.0, killSource - profile.SourceConsumedAt(killEventTime));
            }

            double availableSource = clip.DurationSeconds - SourceTailSafetySeconds;
            while (sourceOffset + profile.SourceConsumedAt(length) > availableSource
                && slotBeats - 1 > leadInBeats
                && (slotBeats - 1) * beatInterval >= MinSlotSeconds)
            {
                slotBeats -= 1;
                length = slotBeats * beatInterval;
                profile = BuildKillSpeedProfile(beatInterval, killEventTime, length, leadInSpeed, freeze);
            }

            if (sourceOffset + profile.SourceConsumedAt(length) > availableSource)
            {
                return CreateFlatPlacement(clip, timelineStart, beatInterval, length, leadInBeats);
            }

            return new ClipPlacement
            {
                Clip = clip,
                TimelineStartSeconds = timelineStart,
                SourceOffsetSeconds = sourceOffset,
                LengthSeconds = length,
                Profile = profile,
            };
        }

        /// <summary>
        /// The pre-Phase-1 placement: no warping, first kill a whole number of
        /// beats after the cut at normal speed.
        /// </summary>
        private static ClipPlacement CreateFlatPlacement(Clip.Clip clip, double timelineStart, double beatInterval, double length, int leadInBeats)
        {
            double sourceOffset = ChooseSourceOffset(clip, beatInterval, length, leadInBeats);
            return new ClipPlacement
            {
                Clip = clip,
                TimelineStartSeconds = timelineStart,
                SourceOffsetSeconds = sourceOffset,
                LengthSeconds = Math.Min(length, clip.DurationSeconds - sourceOffset),
                Profile = SpeedProfile.Flat(),
            };
        }

        /// <summary>
        /// The quickscope-feel warp: lead-in slightly fast, smooth ramp down
        /// over the last beat before the kill, the kill frame at the bottom of
        /// the dip exactly on its beat, then back to normal speed by the next
        /// beat. Highlight clips freeze on the kill frame for a full beat
        /// instead, then recover over half a beat.
        /// </summary>
        private static SpeedProfile BuildKillSpeedProfile(double beatInterval, double killEventTime, double eventLength, double leadInSpeed, bool freeze)
        {
            SpeedProfile profile = new SpeedProfile();
            double rampStart = Math.Max(0.0, killEventTime - beatInterval);
            double dipSpeed = freeze ? FreezeSpeed : KillDipSpeed;

            if (rampStart > TimeEpsilon)
            {
                profile.AddPoint(0.0, leadInSpeed, SpeedCurve.Linear);
                profile.AddPoint(rampStart, leadInSpeed, SpeedCurve.Smooth);
            }
            else
            {
                profile.AddPoint(0.0, leadInSpeed, SpeedCurve.Smooth);
            }

            profile.AddPoint(killEventTime, dipSpeed, freeze ? SpeedCurve.Linear : SpeedCurve.Smooth);

            if (freeze)
            {
                double freezeEnd = Math.Min(killEventTime + beatInterval, eventLength);
                if (freezeEnd > killEventTime + TimeEpsilon)
                {
                    profile.AddPoint(freezeEnd, FreezeSpeed, SpeedCurve.Smooth);
                }
                double recoveryEnd = Math.Min(freezeEnd + beatInterval / 2.0, eventLength);
                if (recoveryEnd > freezeEnd + TimeEpsilon)
                {
                    profile.AddPoint(recoveryEnd, 1.0, SpeedCurve.Linear);
                }
            }
            else
            {
                double recoveryEnd = Math.Min(killEventTime + beatInterval, eventLength);
                if (recoveryEnd > killEventTime + TimeEpsilon)
                {
                    profile.AddPoint(recoveryEnd, 1.0, SpeedCurve.Linear);
                }
            }

            return profile;
        }

        /// <summary>
        /// Lead-in speed at which the pre-kill portion of the profile consumes
        /// exactly the source that exists before the first kill. The pre-kill
        /// consumption is lead*rampStart + (lead+dip)/2 * rampLength, which is
        /// linear in the lead speed, so this solves in closed form.
        /// </summary>
        private static double SolveLeadInSpeed(double killSource, double killEventTime, double beatInterval, bool freeze)
        {
            double rampStart = Math.Max(0.0, killEventTime - beatInterval);
            double rampLength = killEventTime - rampStart;
            double dipSpeed = freeze ? FreezeSpeed : KillDipSpeed;
            return (killSource - dipSpeed * rampLength / 2.0) / (rampStart + rampLength / 2.0);
        }

        /// <summary>
        /// Highlight clips get the freeze-frame treatment: game-ending kills
        /// ("ender") and multikills, marked in the clip type or notes.
        /// </summary>
        private static bool IsHighlightClip(Clip.Clip clip)
        {
            string haystack = ((clip.ClipType ?? "") + " " + (clip.Notes ?? "")).ToLowerInvariant();
            return haystack.Contains("ender")
                || haystack.Contains("mult")
                || haystack.Contains("triple")
                || haystack.Contains("quad");
        }

        /// <summary>
        /// Openers first, closers last. Regular clips are ordered for variety:
        /// consecutive clips avoid repeating the same map or gun where possible.
        /// </summary>
        private static List<Clip.Clip> OrderClips(List<Clip.Clip> clips)
        {
            List<Clip.Clip> openers = clips.Where(c => c.IsOpener && !c.IsCloser).ToList();
            List<Clip.Clip> closers = clips.Where(c => c.IsCloser).ToList();
            List<Clip.Clip> middle = clips.Where(c => !c.IsOpener && !c.IsCloser).ToList();

            List<Clip.Clip> orderedMiddle = new List<Clip.Clip>();
            List<Clip.Clip> remaining = middle
                .OrderBy(c => c.Map)
                .ThenBy(c => c.SequenceNumber)
                .ToList();

            Clip.Clip previous = openers.LastOrDefault();
            while (remaining.Count > 0)
            {
                Clip.Clip pick = remaining.FirstOrDefault(c => previous == null || (c.Map != previous.Map && c.Gun != previous.Gun))
                    ?? remaining.FirstOrDefault(c => previous == null || c.Map != previous.Map)
                    ?? remaining[0];
                remaining.Remove(pick);
                orderedMiddle.Add(pick);
                previous = pick;
            }

            return openers.Concat(orderedMiddle).Concat(closers).ToList();
        }

        /// <summary>
        /// Slot length in beats: lead-in plus the span from first to last kill
        /// plus a short tail, clamped to sensible bounds and to what the source
        /// clip can actually fill.
        /// </summary>
        private static int ChooseSlotBeats(Clip.Clip clip, double beatInterval, int leadInBeats)
        {
            int tailBeats = Math.Max(1, (int)Math.Ceiling(TailSeconds / beatInterval));

            int slotBeats;
            if (clip.KillTimesSeconds.Count == 0)
            {
                slotBeats = Math.Max(1, (int)Math.Round(DefaultSlotSeconds / beatInterval));
            }
            else
            {
                double killSpan = clip.KillTimesSeconds.Max() - clip.KillTimesSeconds.Min();
                int killSpanBeats = (int)Math.Ceiling(killSpan / beatInterval);
                slotBeats = leadInBeats + killSpanBeats + tailBeats;
            }

            int maxBeatsFromClip = (int)Math.Floor(clip.DurationSeconds / beatInterval);
            int minBeats = Math.Max(1, (int)Math.Ceiling(MinSlotSeconds / beatInterval));
            int maxBeats = Math.Max(minBeats, (int)Math.Floor(MaxSlotSeconds / beatInterval));

            slotBeats = Math.Min(slotBeats, maxBeatsFromClip);
            return Math.Max(minBeats, Math.Min(maxBeats, slotBeats));
        }

        /// <summary>
        /// Source offset for flat (unwarped) placements: the first kill is
        /// placed exactly a whole number of beats after the cut. Clips without
        /// kills play from a point that centres the slot.
        /// </summary>
        private static double ChooseSourceOffset(Clip.Clip clip, double beatInterval, double slotLength, int leadInBeats)
        {
            double offset;
            if (clip.KillTimesSeconds.Count == 0)
            {
                offset = (clip.DurationSeconds - slotLength) / 2.0;
            }
            else
            {
                offset = clip.KillTimesSeconds.Min() - leadInBeats * beatInterval;
            }

            offset = Math.Min(offset, clip.DurationSeconds - slotLength);
            return Math.Max(0.0, offset);
        }
    }
}
