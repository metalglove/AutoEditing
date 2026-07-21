using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Core.Domain.Audio;
using Core.Domain.Clip;
using Core.Domain.Logging;
using ScriptPortal.Vegas;

namespace Core.Domain.Editing;

public class TimelineBuilder
{
	public Dictionary<ClipPlacement, VideoEvent> BuildTimeline(Vegas vegas, List<ClipPlacement> placements, string songPath)
	{
		Project project = vegas.Project;
		MatchProjectVideoToSourceClips(project, placements);
		VideoTrack val = project.AddVideoTrack();
		((Track)val).Name = "AE|Montage Clips";
		AudioTrack val2 = project.AddAudioTrack();
		((Track)val2).Name = "AE|Montage Clip Audio";
		vegas.UpdateUI();
		Dictionary<ClipPlacement, VideoEvent> dictionary = new Dictionary<ClipPlacement, VideoEvent>();
		foreach (ClipPlacement placement in placements)
		{
			try
			{
				VideoEvent val3 = PlaceClip(vegas, project, val, val2, placement);
				if (val3 != (VideoEvent)null)
				{
					dictionary[placement] = val3;
				}
			}
			catch (Exception ex)
			{
				Logger.LogError("Error placing clip " + placement.Clip.FilePath, ex);
			}
		}
		AddSongTrack(project, songPath);
		vegas.UpdateUI();
		return dictionary;
	}

	private static void MatchProjectVideoToSourceClips(Project project, List<ClipPlacement> placements)
	{
		if (placements.Count == 0)
		{
			return;
		}
		Core.Domain.Clip.Clip clip = placements[0].Clip;
		Media val = project.MediaPool.AddMedia(clip.FilePath);
		if (val == (Media)null)
		{
			Logger.LogError("Could not load media to match project video properties: " + clip.FilePath);
			return;
		}
		VideoStream videoStreamByIndex = val.GetVideoStreamByIndex(0);
		if (videoStreamByIndex == (VideoStream)null)
		{
			Logger.LogError("No video stream found to match project video properties: " + clip.FilePath);
			return;
		}
		Logger.Log($"Source video properties: {videoStreamByIndex.Width}x{videoStreamByIndex.Height}, " + $"PAR {videoStreamByIndex.PixelAspectRatio:F4}, {videoStreamByIndex.FrameRate:F3}fps. " + $"Project before match: {((VideoProperties)project.Video).Width}x{((VideoProperties)project.Video).Height}, " + $"PAR {((VideoProperties)project.Video).PixelAspectRatio:F4}, {((VideoProperties)project.Video).FrameRate:F3}fps.");
		((VideoProperties)project.Video).Width = videoStreamByIndex.Width;
		((VideoProperties)project.Video).Height = videoStreamByIndex.Height;
		((VideoProperties)project.Video).PixelAspectRatio = videoStreamByIndex.PixelAspectRatio;
		((VideoProperties)project.Video).FrameRate = videoStreamByIndex.FrameRate;
		Logger.Log($"Project video properties after match: {((VideoProperties)project.Video).Width}x{((VideoProperties)project.Video).Height}, " + $"PAR {((VideoProperties)project.Video).PixelAspectRatio:F4}, {((VideoProperties)project.Video).FrameRate:F3}fps.");
	}

	private static VideoEvent PlaceClip(Vegas vegas, Project project, VideoTrack videoTrack, AudioTrack clipAudioTrack, ClipPlacement placement)
	{
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Expected O, but got Unknown
		//IL_010e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Expected O, but got Unknown
		//IL_0159: Unknown result type (might be due to invalid IL or missing references)
		//IL_0160: Expected O, but got Unknown
		//IL_0170: Unknown result type (might be due to invalid IL or missing references)
		//IL_0177: Expected O, but got Unknown
		Core.Domain.Clip.Clip clip = placement.Clip;
		Logger.Log($"Placing {clip.Gun} {clip.ClipType} at {placement.TimelineStartSeconds:F2}s " + $"(source {placement.SourceOffsetSeconds:F2}s, length {placement.LengthSeconds:F2}s)");
		Media val = project.MediaPool.AddMedia(clip.FilePath);
		if (val == (Media)null)
		{
			Logger.LogError("Media is null for clip: " + clip.FilePath);
			return null;
		}
		VideoStream videoStreamByIndex = val.GetVideoStreamByIndex(0);
		if (videoStreamByIndex == (VideoStream)null)
		{
			Logger.LogError("No video stream found for: " + clip.FilePath);
			return null;
		}
		Timecode val2 = Timecode.FromSeconds(placement.TimelineStartSeconds);
		Timecode val3 = Timecode.FromSeconds(placement.LengthSeconds);
		Timecode offset = Timecode.FromSeconds(placement.SourceOffsetSeconds);
		VideoEvent val4 = new VideoEvent(val2, val3);
		((BaseList<TrackEvent>)(object)((Track)videoTrack).Events).Add((TrackEvent)(object)val4);
		Take val5 = new Take((MediaStream)(object)videoStreamByIndex);
		((BaseList<Take>)(object)((TrackEvent)val4).Takes).Add(val5);
		val5.Offset = offset;
		vegas.UpdateUI();
		AudioStream val6 = ((IEnumerable)val.Streams).OfType<AudioStream>().FirstOrDefault();
		if (val6 != (AudioStream)null)
		{
			AudioEvent val7 = new AudioEvent(val2, val3);
			((BaseList<TrackEvent>)(object)((Track)clipAudioTrack).Events).Add((TrackEvent)(object)val7);
			Take val8 = new Take((MediaStream)(object)val6);
			((BaseList<Take>)(object)((TrackEvent)val7).Takes).Add(val8);
			val8.Offset = offset;
			vegas.UpdateUI();
		}
		return val4;
	}

	public void AddMontageMarkers(Vegas vegas, List<ClipPlacement> placements, BeatGrid beats)
	{
		//IL_0291: Unknown result type (might be due to invalid IL or missing references)
		//IL_029b: Expected O, but got Unknown
		if (placements.Count == 0)
		{
			return;
		}
		double start = placements.First().TimelineStartSeconds;
		double end = placements.Last().TimelineEndSeconds;
		Dictionary<long, string> labels = new Dictionary<long, string>();
		foreach (double item in beats.BeatTimesSeconds.Where((double b) => b >= start && b <= end))
		{
			labels[(long)Math.Round(item * 1000000.0)] = "AE|BEAT";
		}
		foreach (ClipPlacement placement in placements)
		{
			foreach (TimelineShotEvent timelineShotEvent in placement.TimelineShotEvents)
			{
				long key = (long)Math.Round(timelineShotEvent.TimelineTimeSeconds * 1000000.0);
				long num = (from k in labels.Keys
					where labels[k].Contains("AE|BEAT") && Math.Abs(k - key) <= 2000
					orderby Math.Abs(k - key)
					select k).FirstOrDefault();
				if (num != 0L || labels.ContainsKey(0L))
				{
					key = num;
				}
				string text = "AE|" + timelineShotEvent.SourceEvent.Outcome;
				labels[key] = (labels.TryGetValue(key, out var value) ? (value + "+" + text) : text);
			}
		}
		foreach (KeyValuePair<long, string> item2 in labels.OrderBy((KeyValuePair<long, string> p) => p.Key))
		{
			((BaseList<Marker>)(object)vegas.Project.Markers).Add(new Marker(Timecode.FromSeconds((double)item2.Key / 1000000.0), item2.Value));
		}
	}

	private static void AddSongTrack(Project project, string songPath)
	{
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Expected O, but got Unknown
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Expected O, but got Unknown
		Media val = project.MediaPool.AddMedia(songPath);
		if (val == (Media)null)
		{
			throw new InvalidOperationException("Could not import song file.");
		}
		AudioTrack val2 = project.AddAudioTrack();
		((Track)val2).Name = "AE|Montage Song";
		AudioEvent val3 = new AudioEvent(Timecode.FromSeconds(0.0), val.Length);
		((BaseList<TrackEvent>)(object)((Track)val2).Events).Add((TrackEvent)(object)val3);
		((BaseList<Take>)(object)((TrackEvent)val3).Takes).Add(new Take((MediaStream)(object)val.GetAudioStreamByIndex(0)));
	}
}
