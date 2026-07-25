using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain.Audio;
using Core.Domain.Audio.SongAnalysis;

namespace Core.Domain.Editing;

public sealed class BeatGridPlanningInputAdapter
{
	public MontageSongPlanningInput Create(BeatGrid grid, double songDurationSeconds)
	{
		if (grid == null) throw new ArgumentNullException(nameof(grid));
		if (songDurationSeconds < 0.0) throw new ArgumentOutOfRangeException(nameof(songDurationSeconds));

		List<double> times = (grid.BeatTimesSeconds ?? new List<double>())
			.Where((double time) => IsFinite(time) && time >= 0.0 && time <= songDurationSeconds + 0.001)
			.Distinct()
			.OrderBy((double time) => time)
			.ToList();
		if (times.Count == 0)
		{
			if (grid.Bpm <= 0.0 || !IsFinite(grid.Bpm)) throw new ArgumentException("A valid beat grid is required.", nameof(grid));
			double interval = 60.0 / grid.Bpm;
			for (double time = Math.Max(0.0, grid.FirstBeatOffsetSeconds); time <= songDurationSeconds + 0.001; time += interval)
			{
				times.Add(time);
			}
		}

		MontageSongPlanningInput input = new MontageSongPlanningInput
		{
			Mode = MontageSongPlanningMode.LegacyBeatGrid,
			SongDurationSeconds = songDurationSeconds
		};
		for (int index = 0; index < times.Count; index++)
		{
			input.Events.Add(new MontageSongPlanningEvent
			{
				Id = "legacy-beat-" + index.ToString("D6"),
				SourceTimeSeconds = times[index],
				EffectiveTimeSeconds = times[index],
				MusicalType = MusicEventType.Beat,
				Classification = MontageSongEventClassification.GameplayAnchor,
				Uses = new List<EditorialUse> { EditorialUse.GameplayAnchor }
			});
		}
		input.Diagnostics.Add(new MontageSongPlanningDiagnostic
		{
			Code = "legacy-beat-grid-fallback",
			Severity = MontageSongPlanningDiagnosticSeverity.Information,
			Message = "No reviewed song map was available; montage planning is using the legacy beat grid."
		});
		return input;
	}

	private static bool IsFinite(double value)
	{
		return !double.IsNaN(value) && !double.IsInfinity(value);
	}
}
