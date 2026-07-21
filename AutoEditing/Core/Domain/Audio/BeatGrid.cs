using System.Collections.Generic;

namespace Core.Domain.Audio;

public class BeatGrid
{
	public double Bpm { get; set; }

	public double FirstBeatOffsetSeconds { get; set; }

	public List<double> BeatTimesSeconds { get; set; } = new List<double>();

	public double BeatIntervalSeconds => 60.0 / Bpm;
}
