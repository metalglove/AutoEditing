using System;
using System.Collections.Generic;
using System.Linq;
using AutoEditing.Iteration.Contracts.Automation;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal static class CandidateSnapshotReader
{
	public static CandidateTimelineSnapshot Read(Project project, CandidateWorkspaceId workspace)
	{
		List<Track> tracks = CandidateWorkspaceDiscovery.FindOwnedTracks(project, workspace);
		return ReadTracks(workspace, tracks);
	}

	public static CandidateTimelineSnapshot ReadTracks(
		CandidateWorkspaceId workspace,
		IEnumerable<Track> tracks)
	{
		if (workspace == null) throw new ArgumentNullException(nameof(workspace));
		if (tracks == null) throw new ArgumentNullException(nameof(tracks));
		workspace.Validate();
		List<CandidateTrackSnapshot> snapshots = tracks
			.OrderBy(track => track.Name, StringComparer.Ordinal)
			.ThenBy(track => track.MediaType.ToString(), StringComparer.Ordinal)
			.Select((track, index) => ReadTrack(workspace, track, index))
			.ToList();
		List<CandidateEventSnapshot> events = snapshots.SelectMany(track => track.Events).ToList();
		TimeSpan start = events.Count == 0 ? TimeSpan.Zero : events.Min(item => item.TimelineStart);
		TimeSpan end = events.Count == 0
			? TimeSpan.Zero
			: events.Max(item => item.TimelineStart + item.TimelineDuration);
		return new CandidateTimelineSnapshot
		{
			Workspace = workspace,
			TimelineStart = Round(start),
			TimelineEnd = Round(end),
			Tracks = snapshots
		};
	}

	private static CandidateTrackSnapshot ReadTrack(
		CandidateWorkspaceId workspace,
		Track track,
		int stableIndex)
	{
		return new CandidateTrackSnapshot
		{
			Index = stableIndex,
			Name = track.Name ?? "",
			MediaKind = track.MediaType.ToString(),
			Muted = track.Mute,
			Solo = track.Solo,
			Gain = track is AudioTrack audioTrack
				? audioTrack.Volume
				: 1.0,
			VolumeAutomation = ReadVolumeAutomation(track),
			Events = ((IEnumerable<TrackEvent>)track.Events)
				.OrderBy(item => Seconds(item.Start))
				.ThenBy(item => Seconds(item.Length))
				.ThenBy(item => item.ActiveTake?.MediaPath ?? "", StringComparer.Ordinal)
				.Select(item => ReadEvent(workspace, item))
				.ToList()
		};
	}

	private static IList<CandidateEnvelopePoint> ReadVolumeAutomation(
		Track track)
	{
		AudioTrack audioTrack = track as AudioTrack;
		if (audioTrack == null)
			return new List<CandidateEnvelopePoint>();
		List<CandidateEnvelopePoint> result =
			new List<CandidateEnvelopePoint>();
		foreach (Envelope envelope in
			(IEnumerable<Envelope>)audioTrack.Envelopes)
		{
			if ((int)envelope.Type != (int)EnvelopeType.Volume) continue;
			result.AddRange(
				((IEnumerable<EnvelopePoint>)envelope.Points)
					.Select(point => new CandidateEnvelopePoint
					{
						Offset = Round(point.X),
						Value = point.Y
					}));
		}
		return result
			.OrderBy(item => item.Offset)
			.ThenBy(item => item.Value)
			.ToList();
	}

	private static CandidateEventSnapshot ReadEvent(
		CandidateWorkspaceId workspace,
		TrackEvent trackEvent)
	{
		Take take = trackEvent.ActiveTake;
		string mediaPath = take?.MediaPath ?? "";
		return new CandidateEventSnapshot
		{
			// New candidates carry an explicit event identity that survives
			// direct movement, trimming, duration, and speed edits. The media
			// path fallback is retained only for recovery of legacy sessions.
			PlacementId = CandidatePlacementIdentity.IsOwned(
				workspace,
				trackEvent.Name)
				? trackEvent.Name
				: string.IsNullOrWhiteSpace(mediaPath)
					? ""
					: System.IO.Path.GetFullPath(mediaPath),
			MediaPath = mediaPath,
			TimelineStart = Round(trackEvent.Start),
			TimelineDuration = Round(trackEvent.Length),
			SourceOffset = Round(take?.Offset ?? Timecode.FromSeconds(0)),
			Gain = trackEvent.Track is AudioTrack audioTrack ? audioTrack.Volume : 1.0,
			FadeIn = Round(trackEvent.FadeIn.Length),
			FadeOut = Round(trackEvent.FadeOut.Length),
			FadeInTransition = TransitionName(trackEvent.FadeIn.Transition),
			FadeOutTransition = TransitionName(trackEvent.FadeOut.Transition),
			GroupSignature = ReadGroupSignature(workspace, trackEvent),
			Velocity = ReadVelocity(trackEvent),
			Effects = ReadEffects(trackEvent)
		};
	}

	private static string TransitionName(object transition) =>
		transition == null ? "" : transition.ToString() ?? "";

	private static string ReadGroupSignature(
		CandidateWorkspaceId workspace,
		TrackEvent trackEvent)
	{
		if (!trackEvent.IsGrouped || trackEvent.Group == null) return "";
		return string.Join(
			"|",
			((IEnumerable<TrackEvent>)trackEvent.Group)
				.Select(item =>
				{
					string mediaPath = item.ActiveTake?.MediaPath ?? "";
					string identity = CandidatePlacementIdentity.IsOwned(
						workspace,
						item.Name)
						? item.Name
						: mediaPath;
					return item.MediaType + ":" + identity;
				})
				.OrderBy(value => value, StringComparer.Ordinal));
	}

	private static IList<string> ReadEffects(TrackEvent trackEvent)
	{
		IEnumerable<Effect> effects = trackEvent is VideoEvent video
			? (IEnumerable<Effect>)video.Effects
			: trackEvent is AudioEvent audio
				? (IEnumerable<Effect>)audio.Effects
				: Enumerable.Empty<Effect>();
		return effects
				.Select(effect => effect.PlugIn?.Name ?? effect.Description ?? "")
				.Where(name => !string.IsNullOrWhiteSpace(name))
				.OrderBy(name => name, StringComparer.Ordinal)
				.ToList();
	}

	private static IList<CandidateVelocityPoint> ReadVelocity(TrackEvent trackEvent)
	{
		List<CandidateVelocityPoint> result = new List<CandidateVelocityPoint>();
		VideoEvent videoEvent = trackEvent as VideoEvent;
		if (videoEvent == null) return result;
		foreach (Envelope envelope in (IEnumerable<Envelope>)videoEvent.Envelopes)
		{
			if ((int)envelope.Type != 202) continue;
			result.AddRange(((IEnumerable<EnvelopePoint>)envelope.Points)
				.Select(point => new CandidateVelocityPoint
				{
					Offset = Round(point.X),
					Velocity = point.Y
				}));
		}
		return result.OrderBy(item => item.Offset).ThenBy(item => item.Velocity).ToList();
	}

	private static TimeSpan Round(Timecode value) => TimeSpan.FromTicks((long)Math.Round(Seconds(value) * TimeSpan.TicksPerSecond));
	private static TimeSpan Round(TimeSpan value) => TimeSpan.FromTicks((long)Math.Round(value.TotalSeconds * TimeSpan.TicksPerSecond));
	private static double Seconds(Timecode value) => value.ToMilliseconds() / 1000.0;
}
