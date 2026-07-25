using System.Collections.Generic;

namespace Core.Domain.Audio.SongAnalysis;

public sealed class EditorialMetadata
{
	public int Priority { get; set; }

	public bool IsLocked { get; set; }

	public double? TimingOffsetSeconds { get; set; }

	public double? Intensity { get; set; }

	public List<EditorialUse> AllowedUses { get; set; } = new List<EditorialUse>();

	public List<EditorialAssignment> Assignments { get; set; } = new List<EditorialAssignment>();

	public string Notes { get; set; }
}
