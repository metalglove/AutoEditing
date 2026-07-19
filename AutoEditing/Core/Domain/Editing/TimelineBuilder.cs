using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain.Logging;
using ScriptPortal.Vegas;

namespace Core.Domain.Editing
{
    /// <summary>
    /// Executes a montage plan on the VEGAS timeline: creates the tracks, places
    /// each clip's planned slice at its planned position, and lays the song
    /// underneath. All placement decisions are made by <see cref="MontagePlanner"/>;
    /// this class only talks to the VEGAS API.
    /// </summary>
    public class TimelineBuilder
    {
        /// <summary>
        /// Builds the timeline and returns the created video event for each
        /// placement so effects can be applied afterwards.
        /// </summary>
        public Dictionary<ClipPlacement, VideoEvent> BuildTimeline(Vegas vegas, List<ClipPlacement> placements, string songPath)
        {
            VideoTrack videoTrack = new VideoTrack(vegas.Project, vegas.Project.Tracks.Count, "Clips");
            vegas.Project.Tracks.Add(videoTrack);
            AudioTrack clipAudioTrack = new AudioTrack(vegas.Project, vegas.Project.Tracks.Count, "Clip Audio");
            vegas.Project.Tracks.Add(clipAudioTrack);
            vegas.UpdateUI(); // Ensure UI is updated before adding events

            Dictionary<ClipPlacement, VideoEvent> videoEvents = new Dictionary<ClipPlacement, VideoEvent>();

            foreach (ClipPlacement placement in placements)
            {
                try
                {
                    VideoEvent videoEvent = PlaceClip(vegas, videoTrack, clipAudioTrack, placement);
                    if (videoEvent != null)
                    {
                        videoEvents[placement] = videoEvent;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error placing clip {placement.Clip.FilePath}", ex);
                }
            }

            AddSongTrack(vegas, songPath);
            vegas.UpdateUI();
            return videoEvents;
        }

        private static VideoEvent PlaceClip(Vegas vegas, VideoTrack videoTrack, AudioTrack clipAudioTrack, ClipPlacement placement)
        {
            Clip.Clip clip = placement.Clip;
            Logger.Log($"Placing {clip.Gun} {clip.ClipType} at {placement.TimelineStartSeconds:F2}s " +
                       $"(source {placement.SourceOffsetSeconds:F2}s, length {placement.LengthSeconds:F2}s)");

            Media media = vegas.Project.MediaPool.AddMedia(clip.FilePath);
            if (media == null)
            {
                Logger.LogError($"Media is null for clip: {clip.FilePath}");
                return null;
            }

            VideoStream videoStream = media.GetVideoStreamByIndex(0);
            if (videoStream == null)
            {
                Logger.LogError($"No video stream found for: {clip.FilePath}");
                return null;
            }

            Timecode start = Timecode.FromSeconds(placement.TimelineStartSeconds);
            Timecode length = Timecode.FromSeconds(placement.LengthSeconds);
            Timecode sourceOffset = Timecode.FromSeconds(placement.SourceOffsetSeconds);

            VideoEvent videoEvent = new VideoEvent(start, length);
            videoTrack.Events.Add(videoEvent);
            Take videoTake = new Take(videoStream);
            videoEvent.Takes.Add(videoTake);
            videoTake.Offset = sourceOffset;
            vegas.UpdateUI();

            AudioStream audioStream = media.Streams.OfType<AudioStream>().FirstOrDefault();
            if (audioStream != null)
            {
                AudioEvent audioEvent = new AudioEvent(start, length);
                clipAudioTrack.Events.Add(audioEvent);
                Take audioTake = new Take(audioStream);
                audioEvent.Takes.Add(audioTake);
                audioTake.Offset = sourceOffset;
                vegas.UpdateUI();
            }

            return videoEvent;
        }

        private static void AddSongTrack(Vegas vegas, string songPath)
        {
            Media songMedia = vegas.Project.MediaPool.AddMedia(songPath);
            if (songMedia == null)
            {
                throw new InvalidOperationException("Could not import song file.");
            }

            AudioTrack songTrack = new AudioTrack(vegas.Project, vegas.Project.Tracks.Count, "Song Track");
            vegas.Project.Tracks.Add(songTrack);

            AudioEvent songEvent = new AudioEvent(Timecode.FromSeconds(0), songMedia.Length);
            songTrack.Events.Add(songEvent);
            songEvent.Takes.Add(new Take(songMedia.GetAudioStreamByIndex(0)));
        }
    }
}
