using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain.Audio;
using Core.Domain.Clip;

namespace Core.Domain.Editing;

public class MontagePlanner
{
	private const double KillDipSpeed = 0.35;

	private const double RampSourceSeconds = 0.01;

	private readonly double _preRoll;

	private readonly double _postRoll;

	private readonly double _minVelocity;

	private readonly double _maxVelocity;

	public MontagePlanner()
		: this(1.25, 0.75, 0.35, 2.0)
	{
	}

	public MontagePlanner(double preRoll, double postRoll, double minVelocity, double maxVelocity)
	{
		if (preRoll < 0.0 || postRoll < 0.0 || minVelocity <= 0.0 || maxVelocity < minVelocity)
		{
			throw new ArgumentOutOfRangeException("Invalid montage timing or velocity bounds.");
		}
		_preRoll = preRoll;
		_postRoll = postRoll;
		_minVelocity = minVelocity;
		_maxVelocity = maxVelocity;
	}

	public List<ClipPlacement> PlanMontage(List<Core.Domain.Clip.Clip> clips, BeatGrid beats, double songDurationSeconds)
	{
		if (beats == null || beats.BeatIntervalSeconds <= 0.0)
		{
			throw new ArgumentException("A valid beat grid is required.");
		}
		List<ClipPlacement> list = new List<ClipPlacement>();
		double num = Math.Max(0.0, beats.FirstBeatOffsetSeconds);
		foreach (Core.Domain.Clip.Clip item in OrderClips(clips))
		{
			List<ShotEvent> list2 = item.ConfirmedKills.OrderBy((ShotEvent e) => e.SourceConfirmationTimeSeconds).ToList();
			if (list2.Count == 0)
			{
				throw new InvalidOperationException("Clip has no reviewed Hit/Headshot markers: " + item.FilePath);
			}
			double num2 = Math.Max(0.0, list2[0].SourceConfirmationTimeSeconds - _preRoll);
			double num3 = Math.Min(item.DurationSeconds, list2[list2.Count - 1].SourceConfirmationTimeSeconds + _postRoll);
			if (num3 <= num2)
			{
				throw new InvalidOperationException("Invalid marker/pre-roll/post-roll range: " + item.FilePath);
			}
			List<double> list3 = AssignBeats(list2, num2, num, beats, songDurationSeconds);
			List<SpeedProfilePoint> points = new List<SpeedProfilePoint>();
			AddSegment(points, num2, list2[0].SourceConfirmationTimeSeconds, list3[0] - num, dipAtStart: false, dipAtEnd: true);
			for (int num4 = 1; num4 < list2.Count; num4++)
			{
				AddSegment(points, list2[num4 - 1].SourceConfirmationTimeSeconds, list2[num4].SourceConfirmationTimeSeconds, list3[num4] - list3[num4 - 1], dipAtStart: true, dipAtEnd: true);
			}
			double targetDuration = num3 - list2[list2.Count - 1].SourceConfirmationTimeSeconds;
			AddSegment(points, list2[list2.Count - 1].SourceConfirmationTimeSeconds, num3, targetDuration, dipAtStart: true, dipAtEnd: false);
			SpeedProfile speedProfile = new SpeedProfile(Coalesce(points));
			double timelineDurationSeconds = speedProfile.TimelineDurationSeconds;
			if (num + timelineDurationSeconds > songDurationSeconds + 0.002)
			{
				throw new InvalidOperationException("Complete reviewed kill sequence does not fit in the song: " + item.FilePath);
			}
			ClipPlacement clipPlacement = new ClipPlacement
			{
				Clip = item,
				TimelineStartSeconds = num,
				SourceOffsetSeconds = num2,
				LengthSeconds = timelineDurationSeconds,
				SpeedProfile = speedProfile,
				AssignedBeatTimesSeconds = list3
			};
			Verify(clipPlacement, list2);
			list.Add(clipPlacement);
			num = clipPlacement.TimelineEndSeconds;
		}
		return list;
	}

	private List<double> AssignBeats(List<ShotEvent> kills, double sourceStart, double timelineStart, BeatGrid beats, double songEnd)
	{
		List<double> list = new List<double>();
		double num = timelineStart;
		double num2 = sourceStart;
		foreach (ShotEvent kill in kills)
		{
			double num3 = kill.SourceConfirmationTimeSeconds - num2;
			int num4 = Math.Max(0, (int)Math.Ceiling((num - beats.FirstBeatOffsetSeconds + 1E-06) / beats.BeatIntervalSeconds));
			double num5 = -1.0;
			double num6 = double.MaxValue;
			int num7 = num4;
			while (true)
			{
				double num8 = beats.FirstBeatOffsetSeconds + (double)num7 * beats.BeatIntervalSeconds;
				if (num8 > songEnd)
				{
					break;
				}
				double num9 = num8 - num;
				if (!(num9 <= 0.0))
				{
					bool dipStart = list.Count > 0;
					double ramp = Math.Min(0.01, num3 / 4.0);
					double num10 = SegmentDuration(num3, ramp, dipStart, dipEnd: true, _minVelocity);
					double num11 = SegmentDuration(num3, ramp, dipStart, dipEnd: true, _maxVelocity);
					if (!(num9 > num10 + 0.002) && !(num9 < num11 - 0.002))
					{
						double num12 = num3 / num9;
						double num13 = Math.Abs(Math.Log(num12));
						if (num13 < num6)
						{
							num5 = num8;
							num6 = num13;
						}
						if (num12 < 1.0 && num5 >= 0.0)
						{
							break;
						}
					}
				}
				num7++;
			}
			if (num5 < 0.0)
			{
				throw new InvalidOperationException("No bounded sequential beat assignment exists for a reviewed kill.");
			}
			list.Add(num5);
			num = num5;
			num2 = kill.SourceConfirmationTimeSeconds;
		}
		return list;
	}

	private void AddSegment(List<SpeedProfilePoint> points, double sourceA, double sourceB, double targetDuration, bool dipAtStart, bool dipAtEnd)
	{
		double num = sourceB - sourceA;
		if (num < -1E-09 || targetDuration < -1E-09)
		{
			throw new InvalidOperationException("Markers are not chronological.");
		}
		if (!(num <= 1E-09))
		{
			double num2 = Math.Min(0.01, num / 4.0);
			double num3 = SolveCruise(num, targetDuration, num2, dipAtStart, dipAtEnd);
			AddPoint(points, sourceA, dipAtStart ? 0.35 : num3);
			if (dipAtStart)
			{
				AddPoint(points, sourceA + num2, num3);
			}
			if (dipAtEnd)
			{
				AddPoint(points, sourceB - num2, num3);
			}
			AddPoint(points, sourceB, dipAtEnd ? 0.35 : num3);
		}
	}

	private static double SegmentDuration(double distance, double ramp, bool dipStart, bool dipEnd, double speed)
	{
		double num = 0.0;
		double num2 = distance;
		if (dipStart)
		{
			num += ramp / ((0.35 + speed) / 2.0);
			num2 -= ramp;
		}
		if (dipEnd)
		{
			num += ramp / ((0.35 + speed) / 2.0);
			num2 -= ramp;
		}
		return num + Math.Max(0.0, num2) / speed;
	}

	private double SolveCruise(double distance, double duration, double ramp, bool dipStart, bool dipEnd)
	{
		Func<double, double> func = (double speed) => SegmentDuration(distance, ramp, dipStart, dipEnd, speed);
		double num = func(_minVelocity);
		double num2 = func(_maxVelocity);
		if (duration > num + 0.002 || duration < num2 - 0.002)
		{
			throw new InvalidOperationException("Marker spacing cannot be solved within configured velocity bounds.");
		}
		double num3 = _minVelocity;
		double num4 = _maxVelocity;
		for (int num5 = 0; num5 < 80; num5++)
		{
			double num6 = (num3 + num4) / 2.0;
			if (func(num6) > duration)
			{
				num3 = num6;
			}
			else
			{
				num4 = num6;
			}
		}
		return (num3 + num4) / 2.0;
	}

	private static void AddPoint(List<SpeedProfilePoint> points, double source, double speed)
	{
		if (points.Count > 0 && Math.Abs(points[points.Count - 1].SourceTimeSeconds - source) < 1E-08)
		{
			if (Math.Abs(points[points.Count - 1].Speed - speed) < 1E-08)
			{
				return;
			}
			points.RemoveAt(points.Count - 1);
		}
		points.Add(new SpeedProfilePoint(source, speed));
	}

	private static IEnumerable<SpeedProfilePoint> Coalesce(List<SpeedProfilePoint> points)
	{
		return (from p in points
			orderby p.SourceTimeSeconds
			group p by Math.Round(p.SourceTimeSeconds, 8) into g
			select g.Last()).ToList();
	}

	private static void Verify(ClipPlacement placement, List<ShotEvent> kills)
	{
		for (int i = 0; i < kills.Count; i++)
		{
			if (!placement.SpeedProfile.TryGetTimelineTimeForSourceTime(kills[i].SourceConfirmationTimeSeconds, out var timelineTimeSeconds) || Math.Abs(placement.TimelineStartSeconds + timelineTimeSeconds - placement.AssignedBeatTimesSeconds[i]) > 0.002)
			{
				throw new InvalidOperationException("Velocity integration failed to preserve every reviewed kill within 2 ms: " + placement.Clip.FilePath);
			}
		}
	}

	private static List<Core.Domain.Clip.Clip> OrderClips(List<Core.Domain.Clip.Clip> clips)
	{
		return (from c in clips
			orderby c.IsOpener descending, c.IsCloser, c.Map, c.SequenceNumber
			select c).ToList();
	}
}
