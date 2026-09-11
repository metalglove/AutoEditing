using System;

namespace Core.Scripts;

/// <summary>
/// A renderer-facing effect instruction. Times are local to the target video
/// event, so planning code does not need to know about VEGAS timeline objects.
/// </summary>
internal sealed class EditorialEffectRenderAction
{
	public EditorialEffectRenderAction(
		EditorialEffectRenderKind kind,
		double eventTimeSeconds,
		double intensity,
		double durationSeconds,
		double? nextPumpStartSeconds = null)
	{
		if (eventTimeSeconds < 0) throw new ArgumentOutOfRangeException(nameof(eventTimeSeconds));
		if (intensity < 0 || intensity > 1) throw new ArgumentOutOfRangeException(nameof(intensity));
		if (durationSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(durationSeconds));

		Kind = kind;
		EventTimeSeconds = eventTimeSeconds;
		Intensity = intensity;
		DurationSeconds = durationSeconds;
		NextPumpStartSeconds = nextPumpStartSeconds;
	}

	public EditorialEffectRenderKind Kind { get; }
	public double EventTimeSeconds { get; }
	public double Intensity { get; }
	public double DurationSeconds { get; }

	/// <summary>Event-local time where the next pump on the same event starts punching in.</summary>
	public double? NextPumpStartSeconds { get; }
}

internal enum EditorialEffectRenderKind
{
	ScreenPump,
	WhiteFlash,
	Shake,
	Transition,
	TitleReveal,
	ColorCorrection
}
