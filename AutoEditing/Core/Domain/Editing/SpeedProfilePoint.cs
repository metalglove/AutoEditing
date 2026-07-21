using System;

namespace Core.Domain.Editing;

public class SpeedProfilePoint
{
	public double SourceTimeSeconds { get; }

	public double Speed { get; }

	public SpeedProfilePoint(double sourceTimeSeconds, double speed)
	{
		if (speed < -10.0 || speed > 10.0)
		{
			throw new ArgumentOutOfRangeException("speed", speed, "Speed must be within [-10, 10] (VEGAS Velocity envelope range).");
		}
		SourceTimeSeconds = sourceTimeSeconds;
		Speed = speed;
	}
}
