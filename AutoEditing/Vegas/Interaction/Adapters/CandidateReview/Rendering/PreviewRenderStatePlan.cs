using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Scripts;

internal sealed class PreviewTrackState
{
	public PreviewTrackState(string stableTrackKey, string name, bool mute, bool solo)
	{
		if (string.IsNullOrWhiteSpace(stableTrackKey))
			throw new ArgumentException("A stable track key is required.", nameof(stableTrackKey));
		StableTrackKey = stableTrackKey;
		Name = name ?? "";
		Mute = mute;
		Solo = solo;
	}

	public string StableTrackKey { get; }
	public string Name { get; }
	public bool Mute { get; }
	public bool Solo { get; }
}

internal sealed class PreviewRenderStateSnapshot
{
	public PreviewRenderStateSnapshot(
		double cursorSeconds,
		double selectionStartSeconds,
		double selectionLengthSeconds,
		bool loopEnabled,
		IEnumerable<PreviewTrackState> tracks)
	{
		if (!IsFiniteNonNegative(cursorSeconds) ||
			!IsFiniteNonNegative(selectionStartSeconds) ||
			!IsFiniteNonNegative(selectionLengthSeconds))
			throw new ArgumentOutOfRangeException(nameof(cursorSeconds));
		CursorSeconds = cursorSeconds;
		SelectionStartSeconds = selectionStartSeconds;
		SelectionLengthSeconds = selectionLengthSeconds;
		LoopEnabled = loopEnabled;
		Tracks = (tracks ?? throw new ArgumentNullException(nameof(tracks))).ToList().AsReadOnly();
		if (Tracks.Select(item => item.StableTrackKey).Distinct(StringComparer.Ordinal).Count() != Tracks.Count)
			throw new InvalidOperationException("Track snapshot keys must be unique.");
	}

	public double CursorSeconds { get; }
	public double SelectionStartSeconds { get; }
	public double SelectionLengthSeconds { get; }
	public bool LoopEnabled { get; }
	public IReadOnlyList<PreviewTrackState> Tracks { get; }

	private static bool IsFiniteNonNegative(double value) =>
		!double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
}

internal sealed class PreviewTrackStateChange
{
	public PreviewTrackStateChange(string stableTrackKey, bool mute, bool solo)
	{
		StableTrackKey = stableTrackKey;
		Mute = mute;
		Solo = solo;
	}

	public string StableTrackKey { get; }
	public bool Mute { get; }
	public bool Solo { get; }
}

internal sealed class PreviewRenderStatePlan
{
	public PreviewRenderStatePlan(
		PreviewRenderStateSnapshot captured,
		IEnumerable<PreviewTrackStateChange> isolateCandidate)
	{
		Captured = captured;
		IsolateCandidate = isolateCandidate.ToList().AsReadOnly();
	}

	public PreviewRenderStateSnapshot Captured { get; }
	public IReadOnlyList<PreviewTrackStateChange> IsolateCandidate { get; }
	public IReadOnlyList<PreviewTrackState> RestoreTracks => Captured.Tracks;
}

internal static class PreviewRenderStatePlanner
{
	public static PreviewRenderStatePlan Create(
		PreviewRenderStateSnapshot captured,
		IEnumerable<string> candidateTrackKeys)
	{
		if (captured == null) throw new ArgumentNullException(nameof(captured));
		HashSet<string> candidates = new HashSet<string>(
			candidateTrackKeys ?? throw new ArgumentNullException(nameof(candidateTrackKeys)),
			StringComparer.Ordinal);
		if (candidates.Count == 0)
			throw new InvalidOperationException("At least one candidate track is required for preview isolation.");
		HashSet<string> known = new HashSet<string>(
			captured.Tracks.Select(item => item.StableTrackKey),
			StringComparer.Ordinal);
		if (candidates.Any(item => !known.Contains(item)))
			throw new InvalidOperationException("Candidate isolation references an unknown track key.");

		List<PreviewTrackStateChange> changes = captured.Tracks
			.Select(track => new PreviewTrackStateChange(
				track.StableTrackKey,
				mute: !candidates.Contains(track.StableTrackKey),
				solo: candidates.Contains(track.StableTrackKey)))
			.ToList();
		return new PreviewRenderStatePlan(captured, changes);
	}
}
