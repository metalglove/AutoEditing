using System.Collections.Generic;

namespace Core.Domain.Editing;

public sealed class MontagePlanningResult
{
	public bool IsFeasible { get; set; }

	public List<ClipPlacement> Placements { get; set; } = new List<ClipPlacement>();

	public List<MontageSyncAssignment> Assignments { get; set; } = new List<MontageSyncAssignment>();

	public List<MontageSongPlanningDiagnostic> Diagnostics { get; set; } = new List<MontageSongPlanningDiagnostic>();
}

public sealed class MontageSyncAssignment
{
	public string ClipPath { get; set; }

	public int KillIndex { get; set; }

	public double SourceConfirmationTimeSeconds { get; set; }

	public string MusicEventId { get; set; }

	public double TimelineTimeSeconds { get; set; }
}
