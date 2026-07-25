using System.Collections.Generic;
using System.Linq;
using Core.Domain.Audio;
using Core.Domain.Clip;

namespace Core.Domain.Editing;

public class ClipPlacement
{
	public Core.Domain.Clip.Clip Clip { get; set; }

	public double TimelineStartSeconds { get; set; }

	public double SourceOffsetSeconds { get; set; }

	public double LengthSeconds { get; set; }

	public SpeedProfile SpeedProfile { get; set; }

	public List<double> AssignedBeatTimesSeconds { get; set; } = new List<double>();

	public double TimelineEndSeconds => TimelineStartSeconds + LengthSeconds;

	public List<TimelineShotEvent> TimelineShotEvents
	{
		get
		{
			List<TimelineShotEvent> list = new List<TimelineShotEvent>();
			foreach (ShotEvent item in Clip.ShotEvents.Where((ShotEvent e) => e.ReviewState == ShotReviewState.Reviewed))
			{
				if (SpeedProfile.TryGetTimelineTimeForSourceTime(item.SourceConfirmationTimeSeconds, out var timelineTimeSeconds))
				{
					list.Add(new TimelineShotEvent
					{
						SourceEvent = item,
						TimelineTimeSeconds = TimelineStartSeconds + timelineTimeSeconds
					});
				}
			}
			return list;
		}
	}

	public List<double> TimelineKillTimesSeconds => (from e in TimelineShotEvents
		where e.SourceEvent.IsConfirmedKill
		select e.TimelineTimeSeconds).ToList();
}
