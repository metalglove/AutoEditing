using System;
using System.Collections.Generic;
using Core.Domain.Editing;

namespace Core.Scripts;

/// <summary>
/// Converts the authoritative source-time speed profile into the exact
/// timeline-relative points written to a VEGAS velocity envelope.
/// </summary>
internal sealed class VelocityEnvelopeRenderPlan
{
	private VelocityEnvelopeRenderPlan(
		IReadOnlyList<VelocityEnvelopeRenderPoint> points)
	{
		Points = points;
	}

	public IReadOnlyList<VelocityEnvelopeRenderPoint> Points { get; }

	public static VelocityEnvelopeRenderPlan Create(SpeedProfile profile)
	{
		if (profile == null) throw new ArgumentNullException(nameof(profile));
		if (profile.Points == null || profile.Points.Count < 2)
			throw new InvalidOperationException(
				"A synchronization speed profile requires at least two points.");

		List<VelocityEnvelopeRenderPoint> points =
			new List<VelocityEnvelopeRenderPoint>(profile.Points.Count);
		double previousOffset = -1;
		foreach (SpeedProfilePoint point in profile.Points)
		{
			if (double.IsNaN(point.Speed) || double.IsInfinity(point.Speed) ||
				point.Speed <= 0)
				throw new InvalidOperationException(
					"Synchronization velocity must be a finite positive rate.");
			if (!profile.TryGetTimelineTimeForSourceTime(
				point.SourceTimeSeconds,
				out double timelineOffsetSeconds) ||
				double.IsNaN(timelineOffsetSeconds) ||
				double.IsInfinity(timelineOffsetSeconds) ||
				timelineOffsetSeconds < 0 ||
				timelineOffsetSeconds + 0.000001 < previousOffset)
				throw new InvalidOperationException(
					"A synchronization speed point could not be mapped to a " +
					"monotonic timeline offset.");
			points.Add(new VelocityEnvelopeRenderPoint(
				point.SourceTimeSeconds,
				timelineOffsetSeconds,
				point.Speed));
			previousOffset = timelineOffsetSeconds;
		}
		return new VelocityEnvelopeRenderPlan(points);
	}
}

internal sealed class VelocityEnvelopeRenderPoint
{
	public VelocityEnvelopeRenderPoint(
		double sourceTimeSeconds,
		double timelineOffsetSeconds,
		double speed)
	{
		SourceTimeSeconds = sourceTimeSeconds;
		TimelineOffsetSeconds = timelineOffsetSeconds;
		Speed = speed;
	}

	public double SourceTimeSeconds { get; }
	public double TimelineOffsetSeconds { get; }
	public double Speed { get; }
}
