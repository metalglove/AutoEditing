using System.Collections.Generic;
using Core.Domain.Audio;

namespace Core.Domain.Editing;

public sealed class PreparedMontage
{
	public List<ClipPlacement> Placements { get; set; }
	public BeatGrid Beats { get; set; }
	public MontageSongPlanningInput SongPlan { get; set; }
	public List<MontageSyncAssignment> SyncAssignments { get; set; } = new List<MontageSyncAssignment>();
	public List<MontageSongPlanningDiagnostic> PlanningDiagnostics { get; set; } = new List<MontageSongPlanningDiagnostic>();
	public EffectSelectionOptions EffectOptions { get; set; } = new EffectSelectionOptions();
	public EffectTreatmentPlan EffectTreatments { get; set; } = new EffectTreatmentPlan();
}
