using Core.Domain.Audio;

namespace Core.Domain.Editing;

public sealed class TimelineShotEvent
{
	public ShotEvent SourceEvent { get; set; }

	public double TimelineTimeSeconds { get; set; }
}
