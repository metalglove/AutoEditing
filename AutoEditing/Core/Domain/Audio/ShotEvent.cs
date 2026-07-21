using System;

namespace Core.Domain.Audio;

public sealed class ShotEvent
{
	public double SourceMuzzleTimeSeconds { get; set; }

	public double SourceConfirmationTimeSeconds { get; set; }

	public ShotOutcome Outcome { get; set; }

	public double Confidence { get; set; }

	public string TemplateId { get; set; }

	public ShotReviewState ReviewState { get; set; }

	public string Gun { get; set; }

	public bool IsConfirmedKill => Outcome == ShotOutcome.Hit || Outcome == ShotOutcome.Headshot;

	public static ShotEvent Reviewed(double confirmationSeconds, ShotOutcome outcome, string gun = null)
	{
		if (outcome != ShotOutcome.Hit && outcome != ShotOutcome.Headshot && outcome != ShotOutcome.Miss)
		{
			throw new ArgumentException("A manual marker must be Hit, Headshot, or Miss.", "outcome");
		}
		return new ShotEvent
		{
			SourceMuzzleTimeSeconds = confirmationSeconds,
			SourceConfirmationTimeSeconds = confirmationSeconds,
			Outcome = outcome,
			Confidence = 1.0,
			TemplateId = "manual",
			ReviewState = ShotReviewState.Reviewed,
			Gun = gun
		};
	}
}
