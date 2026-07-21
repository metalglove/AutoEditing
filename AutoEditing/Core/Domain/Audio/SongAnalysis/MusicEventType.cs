namespace Core.Domain.Audio.SongAnalysis;

public enum MusicEventType
{
	Beat,
	Downbeat,
	Accent,
	Transient,
	BuildHit,
	Drop,
	PhraseBoundary,
	ManualSyncPoint
}
