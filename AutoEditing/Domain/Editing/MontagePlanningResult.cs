using System.Collections.Generic;

namespace Core.Domain.Editing;

public sealed class MontagePlanningResult
{
	public bool IsFeasible { get; set; }

	public List<ClipPlacement> Placements { get; set; } = new List<ClipPlacement>();

	public List<MontageSyncAssignment> Assignments { get; set; } = new List<MontageSyncAssignment>();

	public List<MontageSongPlanningDiagnostic> Diagnostics { get; set; } = new List<MontageSongPlanningDiagnostic>();

	/* Montage spans no gameplay clip covers, left where a reviewed region the montage must skip (an
	   Unused region, or a hole between reviewed regions) is wider than the preceding clip's remaining
	   footage. They are deliberate editorial slots, not failures: a cinematic, title, or b-roll clip
	   belongs here (EDIT-VEL-004). */
	public List<MontageTimelineGap> TimelineGaps { get; set; } = new List<MontageTimelineGap>();
}

public sealed class MontageTimelineGap
{
	public double StartSeconds { get; set; }

	public double EndSeconds { get; set; }

	public string PrecedingRegionId { get; set; }

	public string FollowingRegionId { get; set; }

	public double DurationSeconds => EndSeconds - StartSeconds;
}

public sealed class MontageSyncAssignment
{
	public string ClipPath { get; set; }

	public int KillIndex { get; set; }

	public double SourceConfirmationTimeSeconds { get; set; }

	public string MusicEventId { get; set; }

	public double TimelineTimeSeconds { get; set; }
}
