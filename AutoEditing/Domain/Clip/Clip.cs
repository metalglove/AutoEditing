using System.Collections.Generic;
using System.Linq;
using Core.Domain.Audio;

namespace Core.Domain.Clip;

public class Clip
{
	public string FilePath { get; set; }

	public bool IsOpener { get; set; }

	public bool IsCloser { get; set; }

	public string PlayerName { get; set; }

	public string Game { get; set; }

	public string Map { get; set; }

	public string Gun { get; set; }

	public string ClipType { get; set; }

	public int SequenceNumber { get; set; }

	public string Notes { get; set; }

	public double DurationSeconds { get; set; }

	public List<ShotEvent> ShotEvents { get; set; } = new List<ShotEvent>();

	public List<ShotEvent> ConfirmedKills => ShotEvents.FindAll((ShotEvent e) => e.IsConfirmedKill && e.ReviewState == ShotReviewState.Reviewed);

	public List<string> GunsUsed => ConfirmedKills.Select((ShotEvent shot) => string.IsNullOrWhiteSpace(shot.Gun) ? Gun : shot.Gun)
		.Where((string gun) => !string.IsNullOrWhiteSpace(gun)).Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();

	public bool IsSwap => GunsUsed.Count > 1;

	public List<double> LeadTimesSeconds
	{
		get
		{
			List<ShotEvent> kills = ConfirmedKills.OrderBy((ShotEvent shot) => shot.SourceConfirmationTimeSeconds).ToList();
			List<double> result = new List<double>();
			double previous = 0.0;
			foreach (ShotEvent kill in kills)
			{
				result.Add(System.Math.Max(0.0, kill.SourceConfirmationTimeSeconds - previous));
				previous = kill.SourceConfirmationTimeSeconds;
			}
			return result;
		}
	}
}
