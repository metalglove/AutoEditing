using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Domain.Audio.SongAnalysis;
using Core.Domain.Logging;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class SongReviewWorkflow
{
	private const string TrackName = "AE|Song Analysis Audio";
	private const string EventPrefix = "AE|MUSIC|";
	private const string RegionPrefix = "AE|MUSIC_REGION|";

	public void LayOut(Vegas vegas, string songPath, SongAnalysis analysis)
	{
		Project project = vegas.Project;
		RemoveOwnedObjects(project);
		Media media = project.MediaPool.AddMedia(songPath);
		if (media == (Media)null)
		{
			throw new InvalidOperationException("VEGAS could not load the selected song.");
		}
		AudioStream stream = ((IEnumerable)media.Streams).OfType<AudioStream>().FirstOrDefault();
		if (stream == (AudioStream)null)
		{
			throw new InvalidOperationException("The selected song has no audio stream.");
		}
		AudioTrack track = project.AddAudioTrack();
		((Track)track).Name = TrackName;
		AudioEvent audioEvent = new AudioEvent(Timecode.FromSeconds(0.0), Timecode.FromSeconds(analysis.Song.DurationSeconds));
		((BaseList<TrackEvent>)(object)((Track)track).Events).Add((TrackEvent)(object)audioEvent);
		((BaseList<Take>)(object)((TrackEvent)audioEvent).Takes).Add(new Take((MediaStream)(object)stream));

		foreach (MusicEvent musicEvent in analysis.Events.Where(IsUsefulTimelineEvent))
		{
			AddEventMarker(project, musicEvent);
		}
		foreach (MusicRegion region in analysis.Regions.Where((MusicRegion item) => item.ReviewState != MusicAnalysisReviewState.Rejected))
		{
			string label = RegionPrefix + region.Id + "|" + region.Type + "|" + Score(region.Energy) + "|" + Score(region.Confidence);
			((BaseList<Region>)(object)project.Regions).Add(new Region(Timecode.FromSeconds(region.StartSeconds), Timecode.FromSeconds(region.EndSeconds - region.StartSeconds), label));
		}
		vegas.UpdateUI();
		Logger.Log("Song analysis laid out: " + analysis.Events.Count + " event proposals and " + analysis.Regions.Count + " region proposals. Existing non-AE objects were preserved.");
	}

	public void UpdateEventMarkers(Vegas vegas, SongAnalysis analysis, IEnumerable<string> eventIds)
	{
		Project project = vegas.Project;
		foreach (Marker marker in ((IEnumerable<Marker>)project.Markers).Where((Marker item) => item.Label != null && item.Label.StartsWith(EventPrefix, StringComparison.Ordinal)).ToList())
		{
			((BaseList<Marker>)(object)project.Markers).Remove(marker);
		}
		HashSet<string> selected = new HashSet<string>(eventIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
		foreach (MusicEvent musicEvent in analysis.Events.Where((MusicEvent item) => item.ReviewState != MusicAnalysisReviewState.Rejected && selected.Contains(item.Id)).OrderBy((MusicEvent item) => item.TimeSeconds))
		{
			AddEventMarker(project, musicEvent);
		}
		vegas.UpdateUI();
	}

	private static void AddEventMarker(Project project, MusicEvent musicEvent)
	{
		string label = EventPrefix + musicEvent.Id + "|" + musicEvent.Type + "|" + Score(musicEvent.Strength) + "|" + Score(musicEvent.Confidence);
		((BaseList<Marker>)(object)project.Markers).Add(new Marker(Timecode.FromSeconds(musicEvent.TimeSeconds), label));
	}

	public static bool IsUsefulTimelineEvent(MusicEvent musicEvent)
	{
		if (musicEvent.ReviewState == MusicAnalysisReviewState.Rejected) return false;
		if (musicEvent.ReviewState == MusicAnalysisReviewState.Reviewed) return true;
		if (musicEvent.Type == MusicEventType.Beat || musicEvent.Type == MusicEventType.Transient) return false;
		if (musicEvent.Type == MusicEventType.Downbeat) return musicEvent.Confidence.GetValueOrDefault() >= 0.55;
		return true;
	}

	private static string Score(double? value)
	{
		return value.HasValue ? value.Value.ToString("0.000", CultureInfo.InvariantCulture) : string.Empty;
	}

	private static void RemoveOwnedObjects(Project project)
	{
		foreach (Track track in ((IEnumerable<Track>)project.Tracks).Where((Track item) => string.Equals(item.Name, TrackName, StringComparison.Ordinal)).ToList())
		{
			((BaseList<Track>)(object)project.Tracks).Remove(track);
		}
		foreach (Marker marker in ((IEnumerable<Marker>)project.Markers).Where((Marker item) => item.Label != null && item.Label.StartsWith(EventPrefix, StringComparison.Ordinal)).ToList())
		{
			((BaseList<Marker>)(object)project.Markers).Remove(marker);
		}
		foreach (Region region in ((IEnumerable<Region>)project.Regions).Where((Region item) => ((Marker)item).Label != null && ((Marker)item).Label.StartsWith(RegionPrefix, StringComparison.Ordinal)).ToList())
		{
			((BaseList<Region>)(object)project.Regions).Remove(region);
		}
	}
}
