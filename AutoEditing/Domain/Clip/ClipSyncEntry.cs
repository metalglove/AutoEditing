using System;
using System.Collections.Generic;
using Core.Domain.Audio;

namespace Core.Domain.Clip;

public enum ClipSyncState
{
	Candidate,
	Reviewed,
	Ready
}

public sealed class ClipSyncEntry
{
	public string ContentSignature { get; set; }
	public string LastKnownPath { get; set; }
	public long Size { get; set; }
	public long LastWriteUtcTicks { get; set; }
	public string TemplateFingerprint { get; set; }
	public string PlayerName { get; set; }
	public string Game { get; set; }
	public string Map { get; set; }
	public string PrimaryGun { get; set; }
	public double DurationSeconds { get; set; }
	public List<ShotEvent> Events { get; set; } = new List<ShotEvent>();
	public ClipSyncState State { get; set; }
	public DateTime? ReviewedUtc { get; set; }
}
