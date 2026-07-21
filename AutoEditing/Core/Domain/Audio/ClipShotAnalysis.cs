using System.Collections.Generic;

namespace Core.Domain.Audio;

public sealed class ClipShotAnalysis
{
	public string ClipPath { get; set; }

	public long Size { get; set; }

	public long LastWriteUtcTicks { get; set; }

	public string TemplateFingerprint { get; set; }

	public List<ShotEvent> Events { get; set; } = new List<ShotEvent>();
}
