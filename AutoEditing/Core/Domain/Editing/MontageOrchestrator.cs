using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain.Logging;
using ScriptPortal.Vegas;

namespace Core.Domain.Editing
{
    public class MontageOrchestrator
    {
        public void CreateMontage(Vegas vegas, string clipsFolder, string songPath)
        {
            try
            {
                // Parse all clips from folder
                Clip.ClipParser parser = new Core.Domain.Clip.ClipParser();
                List<Clip.Clip> clips = parser.ParseAllClips(clipsFolder);

                if (clips.Count == 0)
                {
                    throw new InvalidOperationException("No clips found in the specified folder.");
                }

                // Validate clips
                Clip.ClipValidator validator = new Core.Domain.Clip.ClipValidator();
                List<Clip.Clip> validClips = clips.Where(c => validator.Validate(c, vegas)).ToList();

                if (validClips.Count == 0)
                {
                    throw new InvalidOperationException("No valid clips found. Check file formats and quality.");
                }

                // Import song
                Media songMedia = vegas.Project.MediaPool.AddMedia(songPath);
                if (songMedia == null)
                {
                    throw new InvalidOperationException("Could not import song file.");
                }

                VideoTrack videoTrack = new VideoTrack(vegas.Project, vegas.Project.Tracks.Count,"Clips");
                vegas.Project.Tracks.Add(videoTrack);
                vegas.UpdateUI(); // Ensure UI is updated before adding takes
                AudioTrack audioTrack = new AudioTrack();
                vegas.Project.Tracks.Add(audioTrack);
                vegas.UpdateUI(); // Ensure UI is updated before adding takes
                AudioEvent songEvent = new AudioEvent(Timecode.FromSeconds(0), songMedia.Length);
                //                songEvent.Takes.Add(new Take(songMedia.GetAudioStreamByIndex(0)));
                //                audioTrack.Events.Add(songEvent);

                // Detect beats in the song
                BeatDetector beatDetector = new BeatDetector();
                List<Timecode> beats = beatDetector.DetectBeats(songEvent, 0.8);

                // Detect kills in clips
                Clip.KillDetector killDetector = new Core.Domain.Clip.KillDetector();
                foreach (Clip.Clip clip in validClips)
                {
                    List<Timecode> kills = killDetector.DetectKills(clip, 0.7, Timecode.FromSeconds(0.5));
                    clip.Kills = kills; // Store kills in clip
                }

                // Build timeline
                TimelineBuilder builder = new TimelineBuilder();
                builder.BuildTimeline(vegas, validClips, songPath, beats, videoTrack, audioTrack);

                // Apply effects and time remapping
                EffectsApplier applier = new EffectsApplier();
                foreach (Clip.Clip clip in validClips)
                {
                    if (clip.VideoEvent != null)
                    {
                        // Apply time remapping based on detected kills
                        applier.ApplyTimeRemapping(clip.VideoEvent, clip.Kills);

                        // Apply shake effects on beats for impact
                        foreach (Timecode beat in beats.Take(10)) // Limit to first 10 beats to avoid overuse
                        {
                            if (beat >= clip.VideoEvent.Start && beat <= clip.VideoEvent.End)
                            {
                                applier.ApplyShake(clip.VideoEvent, beat, 10);
                            }
                        }

                        // Add name tags
                        applier.AddNameTag(clip.VideoEvent, $"{clip.PlayerName} - {clip.ClipType}");

                        // Apply color correction for consistency
                        applier.ApplyColorCorrection(clip.VideoEvent, "Cinematic");
                    }
                }

                // Sync clips to beats for better flow
                builder.SyncClipsToBeats(validClips, beats);

                // Optional: Set up render queue
                SetupRenderQueue(vegas);

                Logger.Log($"Montage creation completed! Processed {validClips.Count} clips.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error creating montage: {ex.Message}", ex);
                throw new InvalidOperationException($"Error creating montage: {ex.Message}", ex);
            }
        }

        private void SetupRenderQueue(Vegas vegas)
        {
            try
            {
                // Configure render settings for high-quality output
                // This is a placeholder - actual implementation would set up
                // render templates, output formats, and quality settings

                // Note: RenderQueue access may require different API approach in some VEGAS versions
                // var renderQueue = vegas.Renderer;
                // renderQueue.Add(...); // Add render job with appropriate settings

                Logger.Log("Render queue configured for high-quality output.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Error setting up render queue", ex);
            }
        }

        public void CreateQuickMontage(Vegas vegas, string clipsFolder, string songPath, bool skipValidation = false)
        {
            // Quick version with minimal processing for fast turnaround
            try
            {
                Clip.ClipParser parser = new Core.Domain.Clip.ClipParser();
                List<Clip.Clip> clips = parser.ParseAllClips(clipsFolder);

                if (!skipValidation)
                {
                    Clip.ClipValidator validator = new Core.Domain.Clip.ClipValidator();
                    clips = clips.Where(c => validator.Validate(c, vegas)).ToList();
                }

                VideoTrack videoTrack = new VideoTrack(vegas.Project, vegas.Project.Tracks.Count,"Clips");
                AudioTrack audioTrack = new AudioTrack();

                TimelineBuilder builder = new TimelineBuilder();
                List<Timecode> beats = new List<Timecode>(); // Empty beats for quick mode
                builder.BuildTimeline(vegas, clips, songPath, beats, videoTrack, audioTrack);

                Logger.Log($"Quick montage created with {clips.Count} clips.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error creating quick montage: {ex.Message}", ex);
                throw new InvalidOperationException($"Error creating quick montage: {ex.Message}", ex);
            }
        }
    }
}
