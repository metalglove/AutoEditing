namespace Core.Domain.Audio.SongAnalysis;

public sealed class EditorialMetadata
{
	public int Priority { get; set; }

	public bool IsLocked { get; set; }

	public string Notes { get; set; }
}
