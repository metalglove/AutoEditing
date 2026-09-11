using System;

namespace Core.Scripts;

/// <summary>
/// Event-local timing of one native screen pump, kept free of VEGAS types so it can be
/// self-tested. A pump punches in quickly and spends the rest of its duration easing back
/// to the baseline. The release stops short of the next pump on the same event so a
/// neighbouring pump never cuts it off mid-curve.
/// </summary>
internal sealed class ScreenPumpShape
{
	public const double MinimumAttackSeconds = 0.05;
	public const double MaximumAttackSeconds = 0.09;
	public const double MaximumDurationSeconds = 0.80;
	private const double AttackShare = 0.2;
	private const double NeighbourGapSeconds = 0.002;

	private ScreenPumpShape(double beforeSeconds, double peakSeconds, double afterSeconds)
	{
		BeforeSeconds = beforeSeconds;
		PeakSeconds = peakSeconds;
		AfterSeconds = afterSeconds;
	}

	public double BeforeSeconds { get; }
	public double PeakSeconds { get; }
	public double AfterSeconds { get; }
	public bool HasAttack => PeakSeconds - BeforeSeconds >= 0.001;
	public bool HasRelease => AfterSeconds - PeakSeconds >= 0.001;

	public static double AttackSeconds(double durationSeconds) =>
		Math.Max(MinimumAttackSeconds, Math.Min(MaximumAttackSeconds, durationSeconds * AttackShare));

	public static ScreenPumpShape Create(
		double eventTimeSeconds,
		double durationSeconds,
		double eventLengthSeconds,
		double? nextPumpStartSeconds)
	{
		double safeEnd = Math.Max(0, eventLengthSeconds - 0.001);
		// Keep the event's zero-time keyframe as the immutable neutral baseline.
		// A pump assigned to the cut starts one millisecond into the incoming clip.
		double peak = Math.Max(0.001, Math.Min(eventTimeSeconds, safeEnd));
		double duration = Math.Min(durationSeconds, MaximumDurationSeconds);
		double attack = AttackSeconds(duration);
		double before = Math.Max(0, peak - attack);
		double after = Math.Min(safeEnd, peak + Math.Max(0, duration - attack));
		if (nextPumpStartSeconds.HasValue)
			after = Math.Min(after, Math.Max(peak, nextPumpStartSeconds.Value - NeighbourGapSeconds));
		return new ScreenPumpShape(before, peak, after);
	}
}
