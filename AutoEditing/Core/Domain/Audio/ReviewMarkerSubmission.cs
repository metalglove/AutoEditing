namespace Core.Domain.Audio;

public sealed class ReviewMarkerSubmission
{
	public double TimelineSeconds { get; set; }

	public ShotOutcome Outcome { get; set; }

	public string Gun { get; set; }

	public double DetectionConfidence { get; set; }

	public string TemplateId { get; set; }

	public ShotEventOrigin Origin { get; set; }
}
