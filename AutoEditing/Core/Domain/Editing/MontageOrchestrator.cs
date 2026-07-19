using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain.Audio;
using Core.Domain.Logging;
using ScriptPortal.Vegas;

namespace Core.Domain.Editing
{
    /// <summary>
    /// Runs the full montage pipeline: parse clips, analyse audio (beats and
    /// shots), plan the timeline, build it in VEGAS, and apply effects.
    /// </summary>
    public class MontageOrchestrator
    {
        public void CreateMontage(Vegas vegas, string clipsFolder, string songPath)
        {
            try
            {
                List<Clip.Clip> clips = ParseAndValidateClips(vegas, clipsFolder, validate: true);
                List<ClipPlacement> placements = AnalyseAndPlan(clips, songPath);
                TimelineBuilder builder = new TimelineBuilder();
                Dictionary<ClipPlacement, VideoEvent> videoEvents = builder.BuildTimeline(vegas, placements, songPath);

                ApplyEffects(videoEvents);

                Logger.Log($"Montage creation completed! Placed {placements.Count} of {clips.Count} clips.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error creating montage: {ex.Message}", ex);
                throw new InvalidOperationException($"Error creating montage: {ex.Message}", ex);
            }
        }

        public void CreateQuickMontage(Vegas vegas, string clipsFolder, string songPath, bool skipValidation = false)
        {
            try
            {
                List<Clip.Clip> clips = ParseAndValidateClips(vegas, clipsFolder, validate: !skipValidation);
                List<ClipPlacement> placements = AnalyseAndPlan(clips, songPath);
                TimelineBuilder builder = new TimelineBuilder();
                builder.BuildTimeline(vegas, placements, songPath);

                Logger.Log($"Quick montage created with {placements.Count} clips.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error creating quick montage: {ex.Message}", ex);
                throw new InvalidOperationException($"Error creating quick montage: {ex.Message}", ex);
            }
        }

        private static List<Clip.Clip> ParseAndValidateClips(Vegas vegas, string clipsFolder, bool validate)
        {
            Clip.ClipParser parser = new Clip.ClipParser();
            List<Clip.Clip> clips = parser.ParseAllClips(clipsFolder);

            if (clips.Count == 0)
            {
                throw new InvalidOperationException("No clips found in the specified folder.");
            }

            if (validate)
            {
                Clip.ClipValidator validator = new Clip.ClipValidator();
                clips = clips.Where(c => validator.Validate(c, vegas)).ToList();
                if (clips.Count == 0)
                {
                    throw new InvalidOperationException("No valid clips found. Check file formats and quality.");
                }
            }

            return clips;
        }

        /// <summary>
        /// Audio analysis and planning. Everything here is VEGAS-free and can be
        /// exercised outside the editor by the analysis harness.
        /// </summary>
        private static List<ClipPlacement> AnalyseAndPlan(List<Clip.Clip> clips, string songPath)
        {
            Logger.Log("Analysing song for beats...");
            MonoAudio songAudio = AudioLoader.LoadMono(songPath);
            BeatDetector beatDetector = new BeatDetector();
            BeatGrid beats = beatDetector.DetectBeats(songAudio);
            Logger.Log($"Detected {beats.Bpm:F1} BPM, {beats.BeatTimesSeconds.Count} beats, first beat at {beats.FirstBeatOffsetSeconds:F2}s.");

            ShotDetector shotDetector = new ShotDetector();
            foreach (Clip.Clip clip in clips)
            {
                MonoAudio clipAudio = AudioLoader.LoadMono(clip.FilePath);
                clip.DurationSeconds = clipAudio.DurationSeconds;
                clip.KillTimesSeconds = shotDetector.DetectShots(clipAudio);
                Logger.Log($"{clip.Gun} {clip.ClipType} #{clip.SequenceNumber}: {clip.KillTimesSeconds.Count} shots detected " +
                           $"({string.Join(", ", clip.KillTimesSeconds.Select(k => k.ToString("F2")))})");
            }

            MontagePlanner planner = new MontagePlanner();
            List<ClipPlacement> placements = planner.PlanMontage(clips, beats, songAudio.DurationSeconds);
            Logger.Log($"Planned {placements.Count} clip placements covering " +
                       $"{(placements.Count > 0 ? placements.Last().TimelineEndSeconds : 0.0):F1}s of the song.");
            return placements;
        }

        private static void ApplyEffects(Dictionary<ClipPlacement, VideoEvent> videoEvents)
        {
            EffectsApplier applier = new EffectsApplier();
            foreach (KeyValuePair<ClipPlacement, VideoEvent> pair in videoEvents)
            {
                ClipPlacement placement = pair.Key;
                VideoEvent videoEvent = pair.Value;

                applier.ApplyVelocityEnvelope(videoEvent, placement.Profile);
                applier.AddNameTag(videoEvent, $"{placement.Clip.PlayerName} - {placement.Clip.ClipType}");
                applier.ApplyColorCorrection(videoEvent, "Cinematic");
            }
        }
    }
}
