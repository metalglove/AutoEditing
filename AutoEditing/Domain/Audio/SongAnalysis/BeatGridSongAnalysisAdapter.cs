using System;

namespace Core.Domain.Audio.SongAnalysis;

public static class BeatGridSongAnalysisAdapter
{
	public static SongAnalysis Create(BeatGrid grid, SongIdentity song)
	{
		if (grid == null)
		{
			throw new ArgumentNullException(nameof(grid));
		}
		if (song == null || string.IsNullOrWhiteSpace(song.ContentFingerprint))
		{
			throw new ArgumentException("A fingerprinted song identity is required.", nameof(song));
		}
		SongAnalysis analysis = new SongAnalysis { Song = song };
		for (int index = 0; index < grid.BeatTimesSeconds.Count; index++)
		{
			double time = grid.BeatTimesSeconds[index];
			analysis.Events.Add(new MusicEvent
			{
				Id = MusicAnalysisId.Create(song.ContentFingerprint, "beat", index),
				TimeSeconds = time,
				Type = MusicEventType.Beat,
				Origin = MusicAnalysisOrigin.Detected,
				ReviewState = MusicAnalysisReviewState.Proposed,
				DetectedTimeSeconds = time,
				DetectedType = MusicEventType.Beat
			});
		}
		return analysis;
	}
}
