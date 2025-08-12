using System;
using System.Collections.Generic;
using System.Linq;
using ScriptPortal.Vegas;
using Core.Scripts;

namespace Core.Domain.Editing
{
    public class TimelineBuilder
    {
        public void BuildTimeline(Vegas vegas, List<Core.Domain.Clip.Clip> clips, string songPath, List<Timecode> beats, VideoTrack videoTrack, AudioTrack audioTrack)
        {

            // Sort clips: Openers first, then variety, closers last
            var sortedClips = clips.OrderBy(c => c.IsOpener ? 0 : (c.IsCloser ? 2 : 1))
                                  .ThenBy(c => c.Game)
                                  .ThenBy(c => c.Map)
                                  .ThenBy(c => c.Gun)
                                  .ToList();

            Timecode currentPos = Timecode.FromSeconds(0);
            vegas.Project.Tracks.Add(videoTrack);
            vegas.UpdateUI(); // Ensure UI is updated before adding takes
            vegas.Project.Tracks.Add(audioTrack);
            vegas.UpdateUI(); // Ensure UI is updated before adding takes

            foreach (var clip in sortedClips)
            {
                try
                {
                    Logger.Log($"Processing clip: {clip.FilePath}");

                    var media = vegas.Project.MediaPool.AddMedia(clip.FilePath);
                    if (media == null)
                    {
                        Logger.LogError($"Media is null for clip: {clip.FilePath}");
                        continue;
                    }

                    Logger.Log($"Media loaded: {media.FilePath}, Length: {media.Length}, Streams: {media.Streams.Count}");

                    var videoStream = media.GetVideoStreamByIndex(0);
                    Logger.Log($"Video stream: {videoStream?.Parent.KeyString?? "None"}");


                    if (videoStream == null)
                    {
                        Logger.LogError($"No video stream found for: {clip.FilePath}");
                        continue;
                    }
                    Logger.Log($"Video stream found for: {clip.FilePath}");

                    var videoEvent = new VideoEvent(currentPos, media.Length);
                    try
                    {

                        videoTrack.Events.Add(videoEvent);
                        Logger.Log($"VideoEvent created for: {clip.FilePath} at {currentPos} with length {media.Length}");
                        vegas.UpdateUI();

                        Logger.Log($"Adding video take for: {clip.FilePath}");
                        videoEvent.Takes.Add(new Take(videoStream));
                        Logger.Log($"Take added for: {clip.FilePath}");
                        vegas.UpdateUI();

                        Logger.Log($"VideoEvent added for: {clip.FilePath} at {currentPos} with length {media.Length}");
                        clip.VideoEvent = videoEvent;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to add VideoEvent for: {clip.FilePath}", ex);
                        continue;
                    }

                    // Add audio take if the media has audio
                    var audioStream = media.Streams.OfType<AudioStream>().FirstOrDefault();
                    if (audioStream != null)
                    {
                        try
                        {
                            var audioEvent = new AudioEvent(currentPos, media.Length);
                            audioTrack.Events.Add(audioEvent);
                            audioEvent.Takes.Add(new Take(audioStream));
                            vegas.UpdateUI();
                            Logger.Log($"AudioEvent added for: {clip.FilePath}");
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError($"Failed to add AudioEvent for: {clip.FilePath}", ex);
                        }
                        currentPos += media.Length;
                    }
                    else
                    {
                        Logger.Log($"No audio stream found for: {clip.FilePath}");
                    }

                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error processing clip {clip.FilePath}", ex);
                }
            }

            var songMedia = vegas.Project.MediaPool.AddMedia(songPath);
            // Import song
            if (songMedia == null)
            {
                throw new InvalidOperationException("Could not import song file.");
            }
            var songTrack = new AudioTrack(vegas.Project, vegas.Project.Tracks.Count, "Song Track");
            vegas.Project.Tracks.Add(songTrack);

            var songEvent = new AudioEvent(Timecode.FromSeconds(0), songMedia.Length);
            songTrack.Events.Add(songEvent);

            Take take = new Take(songMedia.GetAudioStreamByIndex(0));
            songEvent.Takes.Add(take);
            vegas.UpdateUI();
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
