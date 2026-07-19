using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain.Audio;

namespace Core.Domain.Editing
{
    /// <summary>
    /// Where a clip goes on the timeline: which slice of the source file plays,
    /// starting at which timeline position.
    /// </summary>
    public class ClipPlacement
    {
        public Clip.Clip Clip { get; set; }
        public double TimelineStartSeconds { get; set; }
        public double SourceOffsetSeconds { get; set; }
        public double LengthSeconds { get; set; }

        public double TimelineEndSeconds
        {
            get { return TimelineStartSeconds + LengthSeconds; }
        }

        /// <summary>
        /// Kill times relative to the montage timeline (for effects such as
        /// slow motion or shake at impact).
        /// </summary>
        public List<double> TimelineKillTimesSeconds
        {
            get
            {
                return Clip.KillTimesSeconds
                    .Select(k => k - SourceOffsetSeconds + TimelineStartSeconds)
                    .Where(k => k >= TimelineStartSeconds && k <= TimelineEndSeconds)
                    .ToList();
            }
        }
    }

    /// <summary>
    /// Plans the montage: orders clips, gives each one a slot that is a whole
    /// number of beats long, and picks the source window so the first detected
    /// kill lands exactly on a beat. All cuts fall on beats.
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
                double length = slotBeats * beatInterval;

                if (timelineStart + length > songDurationSeconds)
                {
                    break;
                }

                double sourceOffset = ChooseSourceOffset(clip, beatInterval, length, leadInBeats);
                length = Math.Min(length, clip.DurationSeconds - sourceOffset);

                placements.Add(new ClipPlacement
                {
                    Clip = clip,
                    TimelineStartSeconds = timelineStart,
                    SourceOffsetSeconds = sourceOffset,
                    LengthSeconds = length,
                });

                cursorBeat += slotBeats;
            }

            return placements;
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
        /// Picks which part of the source clip plays: the first kill is placed
        /// exactly a whole number of beats after the cut, so every first shot
        /// lands on a beat. Clips without kills play from a point that centres
        /// the slot.
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
