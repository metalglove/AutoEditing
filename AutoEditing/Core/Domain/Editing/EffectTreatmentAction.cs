using Core.Domain.Audio.SongAnalysis;

namespace Core.Domain.Editing;

public enum EffectTreatmentOrigin
{
	Automatic,
	Manual
}

public sealed class EffectTreatmentAction
{
	public string EventId { get; set; }

	public double TimeSeconds { get; set; }

	public EditorialUse Type { get; set; }

	public string RecipeId { get; set; }

	public double Intensity { get; set; }

	public double DurationSeconds { get; set; }

	public EffectTreatmentOrigin Origin { get; set; }

	public string Reason { get; set; }
}

public sealed class EffectTreatmentDiagnostic
{
	public string EventId { get; set; }

	public double? TimeSeconds { get; set; }

	public string Code { get; set; }

	public string Message { get; set; }
}
