using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Domain.Editing;

public class SpeedProfile
{
	private readonly List<SpeedProfilePoint> _points;

	public IReadOnlyList<SpeedProfilePoint> Points => _points;

	public double TotalSourceConsumptionSeconds => _points[_points.Count - 1].SourceTimeSeconds - _points[0].SourceTimeSeconds;

	public double TimelineDurationSeconds
	{
		get
		{
			double num = 0.0;
			for (int i = 0; i < _points.Count - 1; i++)
			{
				num += SegmentTimelineDuration(_points[i], _points[i + 1]);
			}
			return num;
		}
	}

	public SpeedProfile(IEnumerable<SpeedProfilePoint> points)
	{
		_points = points.OrderBy((SpeedProfilePoint p) => p.SourceTimeSeconds).ToList();
		if (_points.Count < 2)
		{
			throw new ArgumentException("A speed profile needs at least two points.", "points");
		}
	}

	private static double SegmentTimelineDuration(SpeedProfilePoint a, SpeedProfilePoint b)
	{
		double num = b.SourceTimeSeconds - a.SourceTimeSeconds;
		double num2 = (a.Speed + b.Speed) / 2.0;
		return (num2 <= 0.0) ? 0.0 : (num / num2);
	}

	public bool TryGetTimelineTimeForSourceTime(double sourceTimeSeconds, out double timelineTimeSeconds)
	{
		timelineTimeSeconds = 0.0;
		if (sourceTimeSeconds < _points[0].SourceTimeSeconds || sourceTimeSeconds > _points[_points.Count - 1].SourceTimeSeconds)
		{
			return false;
		}
		double num = 0.0;
		for (int i = 0; i < _points.Count - 1; i++)
		{
			SpeedProfilePoint speedProfilePoint = _points[i];
			SpeedProfilePoint speedProfilePoint2 = _points[i + 1];
			if (sourceTimeSeconds <= speedProfilePoint2.SourceTimeSeconds)
			{
				double sourceIntoSegment = sourceTimeSeconds - speedProfilePoint.SourceTimeSeconds;
				timelineTimeSeconds = num + TimelineTimeForSourceOffset(speedProfilePoint, speedProfilePoint2, sourceIntoSegment);
				return true;
			}
			num += SegmentTimelineDuration(speedProfilePoint, speedProfilePoint2);
		}
		return false;
	}

	public double GetSourceTimeAtTimelineTime(double timelineTimeSeconds)
	{
		double num = 0.0;
		double sourceTimeSeconds = _points[0].SourceTimeSeconds;
		for (int i = 0; i < _points.Count - 1; i++)
		{
			SpeedProfilePoint a = _points[i];
			SpeedProfilePoint speedProfilePoint = _points[i + 1];
			double num2 = SegmentTimelineDuration(a, speedProfilePoint);
			bool flag = i == _points.Count - 2;
			if (timelineTimeSeconds <= num + num2 || flag)
			{
				double timelineOffsetSeconds = Math.Max(0.0, Math.Min(num2, timelineTimeSeconds - num));
				double num3 = IntegrateSpeedOverTimeline(a, speedProfilePoint, timelineOffsetSeconds);
				return sourceTimeSeconds + num3;
			}
			num += num2;
			sourceTimeSeconds = speedProfilePoint.SourceTimeSeconds;
		}
		return _points[_points.Count - 1].SourceTimeSeconds;
	}

	private static double TimelineTimeForSourceOffset(SpeedProfilePoint a, SpeedProfilePoint b, double sourceIntoSegment)
	{
		double num = SegmentTimelineDuration(a, b);
		if (num <= 0.0)
		{
			return 0.0;
		}
		double num2 = (b.Speed - a.Speed) / num;
		if (Math.Abs(num2) < 1E-09)
		{
			return (a.Speed <= 0.0) ? 0.0 : (sourceIntoSegment / a.Speed);
		}
		double num3 = num2 / 2.0;
		double speed = a.Speed;
		double num4 = 0.0 - sourceIntoSegment;
		double d = Math.Max(0.0, speed * speed - 4.0 * num3 * num4);
		double num5 = Math.Sqrt(d);
		double num6 = (0.0 - speed + num5) / (2.0 * num3);
		double num7 = (0.0 - speed - num5) / (2.0 * num3);
		double val = ((num6 >= -1E-06 && num6 <= num + 1E-06) ? num6 : num7);
		return Math.Max(0.0, Math.Min(num, val));
	}

	private static double IntegrateSpeedOverTimeline(SpeedProfilePoint a, SpeedProfilePoint b, double timelineOffsetSeconds)
	{
		double num = SegmentTimelineDuration(a, b);
		if (num <= 0.0)
		{
			return 0.0;
		}
		double num2 = (b.Speed - a.Speed) / num;
		return a.Speed * timelineOffsetSeconds + num2 / 2.0 * timelineOffsetSeconds * timelineOffsetSeconds;
	}
}
