using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain.Audio.SongAnalysis;

namespace Core.Domain.Editing;

[Flags]
public enum MontageSongEventClassification
{
	None = 0,
	GameplayAnchor = 1,
	Effect = 2,
	Structural = 4,
	IntentionallyUnused = 8
}

public sealed class MontageSongPlanningInput
{
	public MontageSongPlanningMode Mode { get; set; }

	public string SongFingerprint { get; set; }

	public double SongDurationSeconds { get; set; }

	public List<MontageSongPlanningRegion> Regions { get; set; } = new List<MontageSongPlanningRegion>();

	public List<MontageSongPlanningEvent> Events { get; set; } = new List<MontageSongPlanningEvent>();

	public List<MontageSongPlanningDiagnostic> Diagnostics { get; set; } = new List<MontageSongPlanningDiagnostic>();

	public bool HasErrors => Diagnostics.Any((MontageSongPlanningDiagnostic item) => item.Severity == MontageSongPlanningDiagnosticSeverity.Error);
}

public sealed class MontageSongPlanningRegion
{
	public string Id { get; set; }

	public double StartSeconds { get; set; }

	public double EndSeconds { get; set; }

	public MusicRegionType Type { get; set; }

	public bool IsLocked { get; set; }
}

public sealed class MontageSongPlanningEvent
{
	public string Id { get; set; }

	public double SourceTimeSeconds { get; set; }

	public double EffectiveTimeSeconds { get; set; }

	public string ContainingRegionId { get; set; }

	public MusicEventType MusicalType { get; set; }

	public MontageSongEventClassification Classification { get; set; }

	public List<EditorialUse> Uses { get; set; } = new List<EditorialUse>();

	public int Priority { get; set; }

	public bool IsLocked { get; set; }

	public double? Intensity { get; set; }

	public bool IsSuggestedGameplayAnchor { get; set; }

	public bool IsReviewed { get; set; }

	public bool IsGameplayAnchor => (Classification & MontageSongEventClassification.GameplayAnchor) != 0;

	public bool IsEffectOnly => (Classification & MontageSongEventClassification.Effect) != 0 && !IsGameplayAnchor;

	public bool IsIntentionallyUnused => (Classification & MontageSongEventClassification.IntentionallyUnused) != 0;
}
