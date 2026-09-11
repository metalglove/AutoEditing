using System;
using System.IO;
using System.Linq;
using AutoEditing.Iteration.Contracts.Automation;

namespace AutoEditing.Iteration.Contracts.Assembly;

/// <summary>
/// Allows the later audio pass to reuse the exact song track that was audible
/// during synchronization. Reuse is permitted only when path, start, and gain
/// already equal the approved audio action.
/// </summary>
public static class CandidateAudioReuseContract
{
	private const double Tolerance = 0.001;

	public static void ValidateSongTrack(
		CandidateTrackSnapshot track,
		AudioPassSongAction planned)
	{
		if (track == null) throw new ArgumentNullException(nameof(track));
		if (planned == null) throw new ArgumentNullException(nameof(planned));
		if (!string.Equals(
			track.MediaKind,
			"Audio",
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"The reusable synchronization song track is not an audio track.");
		CandidateEventSnapshot song = track.Events?.SingleOrDefault() ??
			throw new InvalidOperationException(
				"The reusable synchronization song track must contain exactly one event.");
		if (string.IsNullOrWhiteSpace(song.MediaPath) ||
			!string.Equals(
				Path.GetFullPath(song.MediaPath),
				Path.GetFullPath(planned.SongPath),
				StringComparison.OrdinalIgnoreCase) ||
			Math.Abs(
				song.TimelineStart.TotalSeconds -
				planned.TimelineStartSeconds) > Tolerance ||
			Math.Abs(song.Gain - planned.TrackGain) > Tolerance)
			throw new InvalidOperationException(
				"The existing synchronization song differs from the approved " +
				"audio-pass song path, start, or gain.");
	}
}
