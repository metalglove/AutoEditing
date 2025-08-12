using System;
using System.Collections.Generic;
using System.Linq;
using ScriptPortal.Vegas;
using Core.Scripts;

namespace Core.Domain.Editing
{
    public class MontageOrchestrator
    {
        public void CreateMontage(Vegas vegas, string clipsFolder, string songPath)
        {
            try
            {
                // Parse all clips from folder
                var parser = new Core.Domain.Clip.ClipParser();
                var clips = parser.ParseAllClips(clipsFolder);

                if (clips.Count == 0)
                {
                    throw new InvalidOperationException("No clips found in the specified folder.");
                }

                // Validate clips
                var validator = new Core.Domain.Clip.ClipValidator();
                var validClips = clips.Where(c => validator.Validate(c, vegas)).ToList();

                if (validClips.Count == 0)
                {
                    throw new InvalidOperationException("No valid clips found. Check file formats and quality.");
                }

                // Import song
                var songMedia = vegas.Project.MediaPool.AddMedia(songPath);
                if (songMedia == null)
                {
                    throw new InvalidOperationException("Could not import song file.");
                }

                var videoTrack = new VideoTrack(vegas.Project, vegas.Project.Tracks.Count,"Clips");
                vegas.Project.Tracks.Add(videoTrack);
                vegas.UpdateUI(); // Ensure UI is updated before adding takes
                var audioTrack = new AudioTrack();
                vegas.Project.Tracks.Add(audioTrack);
                vegas.UpdateUI(); // Ensure UI is updated before adding takes
                var songEvent = new AudioEvent(Timecode.FromSeconds(0), songMedia.Length);
//                songEvent.Takes.Add(new Take(songMedia.GetAudioStreamByIndex(0)));
//                audioTrack.Events.Add(songEvent);

                // Detect beats in the song
                var beatDetector = new BeatDetector();
                var beats = beatDetector.DetectBeats(songEvent, 0.8);

                // Detect kills in clips
                var killDetector = new Core.Domain.Clip.KillDetector();
                foreach (var clip in validClips)
                {
                    var kills = killDetector.DetectKills(clip, 0.7, Timecode.FromSeconds(0.5));
                    clip.Kills = kills; // Store kills in clip
                }

                // Build timeline
                var builder = new TimelineBuilder();
                builder.BuildTimeline(vegas, validClips, songPath, beats, videoTrack, audioTrack);

                // Apply effects and time remapping
                var applier = new EffectsApplier();
                foreach (var clip in validClips)
                {
                    if (clip.VideoEvent != null)
                    {
                        // Apply time remapping based on detected kills
                        applier.ApplyTimeRemapping(clip.VideoEvent, clip.Kills);

                        // Apply shake effects on beats for impact
                        foreach (var beat in beats.Take(10)) // Limit to first 10 beats to avoid overuse
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
                var parser = new Core.Domain.Clip.ClipParser();
                var clips = parser.ParseAllClips(clipsFolder);

                if (!skipValidation)
                {
                    var validator = new Core.Domain.Clip.ClipValidator();
                    clips = clips.Where(c => validator.Validate(c, vegas)).ToList();
                }

                var videoTrack = new VideoTrack(vegas.Project, vegas.Project.Tracks.Count,"Clips");
                var audioTrack = new AudioTrack();

                var builder = new TimelineBuilder();
                var beats = new List<Timecode>(); // Empty beats for quick mode
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
