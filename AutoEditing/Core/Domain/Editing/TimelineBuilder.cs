using System;
using System.Collections.Generic;
using System.Linq;
using ScriptPortal.Vegas;

namespace Core.Domain.Editing
{
    public class TimelineBuilder
    {
        public void BuildTimeline(Vegas vegas, List<Core.Domain.Clip.Clip> clips, AudioEvent songEvent, List<Timecode> beats)
        {
            // Clear existing tracks to start fresh
            vegas.Project.Tracks.Clear();
            
            var videoTrack = new VideoTrack();
            var audioTrack = new AudioTrack();
            vegas.Project.Tracks.Add(videoTrack);
            vegas.Project.Tracks.Add(audioTrack);

            // Sort clips: Openers first, then variety, closers last
            var sortedClips = clips.OrderBy(c => c.IsOpener ? 0 : (c.IsCloser ? 2 : 1))
                                  .ThenBy(c => c.Game)
                                  .ThenBy(c => c.Map)
                                  .ThenBy(c => c.Gun)
                                  .ToList();

            Timecode currentPos = Timecode.FromSeconds(0);

            foreach (var clip in sortedClips)
            {
                try
                {
                    var media = vegas.Project.MediaPool.AddMedia(clip.FilePath);
                    if (media != null)
                    {
                        var videoEvent = new VideoEvent(currentPos, media.Length);
                        videoEvent.Takes.Add(new Take(media.GetVideoStreamByIndex(0)));
                        videoTrack.Events.Add(videoEvent);
                        clip.VideoEvent = videoEvent;

                        // Add audio take if the media has audio
                        var audioStream = media.Streams.OfType<AudioStream>().FirstOrDefault();
                        if (audioStream != null)
                        {
                            var audioEvent = new AudioEvent(currentPos, media.Length);
                            audioEvent.Takes.Add(new Take(audioStream));
                            audioTrack.Events.Add(audioEvent);
                        }

                        // Trim missed shots (placeholder - would need kill detection integration)
                        // Insert cinematics if slow BPM (check beats density)

                        currentPos += media.Length;
                    }
                }
                catch (Exception ex)
                {
                    // Log error and continue with next clip
                    System.Diagnostics.Debug.WriteLine($"Error processing clip {clip.FilePath}: {ex.Message}");
                }
            }

            // Place song on separate audio track
            var songTrack = new AudioTrack();
            vegas.Project.Tracks.Add(songTrack);
            songEvent.Start = Timecode.FromSeconds(0);
            songTrack.Events.Add(songEvent);
        }

        public void PlaceClip(Core.Domain.Clip.Clip clip, Timecode position)
        {
            // Implementation for individual placement
            if (clip.VideoEvent != null)
            {
                clip.VideoEvent.Start = position;
            }
        }

        public void SyncClipsToBeats(List<Core.Domain.Clip.Clip> clips, List<Timecode> beats)
        {
            // Sync clip transitions to beat timings for better flow
            for (int i = 0; i < clips.Count && i < beats.Count; i++)
            {
                if (clips[i].VideoEvent != null && i > 0)
                {
                    clips[i].VideoEvent.Start = beats[i];
                }
            }
        }
    }
}
