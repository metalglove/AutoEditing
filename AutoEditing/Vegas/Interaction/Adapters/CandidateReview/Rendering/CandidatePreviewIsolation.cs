#if NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.Linq;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal static class CandidatePreviewIsolation
{
	public static PreviewRenderStateSnapshot Capture(Vegas vegas) =>
		new PreviewRenderStateSnapshot(
			vegas.Cursor.ToMilliseconds() / 1000.0,
			vegas.SelectionStart.ToMilliseconds() / 1000.0,
			vegas.SelectionLength.ToMilliseconds() / 1000.0,
			vegas.LoopPlayback,
			((IEnumerable<Track>)vegas.Project.Tracks).Select(track =>
				new PreviewTrackState(
					StableTrackKey(track), track.Name, track.Mute, track.Solo)));

	public static string StableTrackKey(Track track) =>
		track.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) +
		"|" + (track.Name ?? "");

	public static void Apply(Project project, IEnumerable<PreviewTrackStateChange> changes)
	{
		Dictionary<string, PreviewTrackStateChange> byKey =
			changes.ToDictionary(item => item.StableTrackKey, StringComparer.Ordinal);
		foreach (Track track in (IEnumerable<Track>)project.Tracks)
		{
			PreviewTrackStateChange change;
			if (!byKey.TryGetValue(StableTrackKey(track), out change))
				throw new InvalidOperationException(
					"The project track set changed during preview evidence capture.");
			track.Mute = change.Mute;
			track.Solo = change.Solo;
		}
	}

	public static void Restore(Vegas vegas, PreviewRenderStateSnapshot captured)
	{
		Dictionary<string, PreviewTrackState> byKey =
			captured.Tracks.ToDictionary(
				item => item.StableTrackKey, StringComparer.Ordinal);
		foreach (Track track in (IEnumerable<Track>)vegas.Project.Tracks)
		{
			PreviewTrackState original;
			if (byKey.TryGetValue(StableTrackKey(track), out original))
			{
				track.Mute = original.Mute;
				track.Solo = original.Solo;
			}
		}
		vegas.Cursor = Timecode.FromSeconds(captured.CursorSeconds);
		vegas.SelectionStart = Timecode.FromSeconds(captured.SelectionStartSeconds);
		vegas.SelectionLength = Timecode.FromSeconds(captured.SelectionLengthSeconds);
		vegas.LoopPlayback = captured.LoopEnabled;
	}
}
#endif
