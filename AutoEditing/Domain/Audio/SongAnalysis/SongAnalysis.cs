using System;
using System.Collections.Generic;

namespace Core.Domain.Audio.SongAnalysis;

public sealed class SongAnalysis
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;

	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public SongIdentity Song { get; set; }

	public double? TempoBpm { get; set; }

	public double? BeatPhaseSeconds { get; set; }

	public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

	public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

	public List<MusicEvent> Events { get; set; } = new List<MusicEvent>();

	public List<MusicRegion> Regions { get; set; } = new List<MusicRegion>();
}
