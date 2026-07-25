using System;
using System.IO;
using Core.Domain.Audio;
using Core.Domain.Audio.SongAnalysis;

namespace Core.Domain.Editing;

public sealed class MontageSongPlanningInputProvider
{
	public MontageSongPlanningInput Load(string songPath, MonoAudio audio, out BeatGrid legacyBeatGrid, out SongAnalysis reviewedAnalysis)
	{
		if (audio == null) throw new ArgumentNullException(nameof(audio));
		SongIdentity currentIdentity = SongIdentity.FromFile(songPath, audio.DurationSeconds);
		SongAnalysisStore store = new SongAnalysisStore();
		string sidecarPath = store.GetSidecarPath(songPath);
		legacyBeatGrid = null;
		reviewedAnalysis = null;

		if (!File.Exists(sidecarPath))
		{
			legacyBeatGrid = new BeatDetector().DetectBeats(audio);
			return new BeatGridPlanningInputAdapter().Create(legacyBeatGrid, audio.DurationSeconds);
		}

		SongAnalysis analysis = store.Load(sidecarPath);
		if (!string.Equals(analysis.Song.ContentFingerprint, currentIdentity.ContentFingerprint, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The reviewed song map is stale or belongs to a different song. Re-analyze the song before building the montage.");
		}
		if (Math.Abs(analysis.Song.DurationSeconds - audio.DurationSeconds) > 0.05)
		{
			throw new InvalidDataException("The reviewed song map duration no longer matches the selected song. Re-analyze the song before building the montage.");
		}

		MontageSongPlanningInput input = new SongAnalysisPlanningInputAdapter().Create(analysis);
		if (input.HasErrors)
		{
			throw new InvalidDataException("The reviewed song map contains planning errors: " + string.Join(" ", input.Diagnostics.ConvertAll((MontageSongPlanningDiagnostic item) => item.Message)));
		}
		reviewedAnalysis = analysis;
		return input;
	}
}
