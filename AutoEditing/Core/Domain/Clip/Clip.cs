using System.Collections.Generic;
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
}
