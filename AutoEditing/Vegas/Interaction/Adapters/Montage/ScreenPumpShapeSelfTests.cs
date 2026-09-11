using System;

namespace Core.Scripts;

internal static class ScreenPumpShapeSelfTests
{
	public static void Run()
	{
		ScreenPumpShape impact = ScreenPumpShape.Create(2.0, 0.42, 10.0, null);
		AssertClose(impact.PeakSeconds, 2.0, "The pump peak moved off its treatment time.");
		AssertClose(impact.PeakSeconds - impact.BeforeSeconds, 0.084,
			"The punch-in did not take a fifth of the pump duration.");
		AssertClose(impact.AfterSeconds - impact.PeakSeconds, 0.336,
			"The release did not use the rest of the pump duration.");

		ScreenPumpShape shortPump = ScreenPumpShape.Create(2.0, 0.1, 10.0, null);
		AssertClose(shortPump.PeakSeconds - shortPump.BeforeSeconds, ScreenPumpShape.MinimumAttackSeconds,
			"A short pump punched in faster than the minimum attack.");

		ScreenPumpShape crowded = ScreenPumpShape.Create(2.0, 0.42, 10.0, 2.2);
		Assert(crowded.HasRelease && crowded.AfterSeconds <= 2.198 + 0.000001,
			"The release ran into the next pump's punch-in.");

		ScreenPumpShape atEnd = ScreenPumpShape.Create(4.95, 0.42, 5.0, null);
		Assert(atEnd.AfterSeconds <= 4.999 + 0.000001, "The release ran past the end of the event.");

		ScreenPumpShape atCut = ScreenPumpShape.Create(0.0, 0.42, 5.0, null);
		AssertClose(atCut.BeforeSeconds, 0.0, "A pump on the cut did not start at the event start.");
		AssertClose(atCut.PeakSeconds, 0.001, "A pump on the cut did not peak inside the incoming clip.");

		ScreenPumpShape bounded = ScreenPumpShape.Create(2.0, 5.0, 10.0, null);
		AssertClose(bounded.AfterSeconds - bounded.BeforeSeconds, ScreenPumpShape.MaximumDurationSeconds,
			"An oversized pump duration was not bounded.");
	}

	private static void AssertClose(double actual, double expected, string message)
	{
		if (Math.Abs(actual - expected) > 0.000001)
			throw new InvalidOperationException(message + " Expected " + expected + ", got " + actual + ".");
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}
}
