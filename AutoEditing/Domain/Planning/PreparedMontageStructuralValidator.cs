using System;
using System.Collections.Generic;
using System.IO;
using Core.Domain.Editing;

namespace Core.Domain.Planning;

public static class PreparedMontageStructuralValidator
{
	public static void ValidateAndNormalize(PreparedMontage prepared)
	{
		if (prepared == null) throw new InvalidOperationException("The prepared montage is empty.");
		prepared.Placements = prepared.Placements ?? new List<ClipPlacement>();
		prepared.SyncAssignments = prepared.SyncAssignments ?? new List<MontageSyncAssignment>();
		prepared.PlanningDiagnostics = prepared.PlanningDiagnostics ?? new List<MontageSongPlanningDiagnostic>();
		prepared.EffectTreatments = prepared.EffectTreatments ?? new EffectTreatmentPlan();
		prepared.EffectOptions = prepared.EffectOptions ?? new EffectSelectionOptions();
		prepared.EffectOptions.Validate();
		prepared.EffectTreatments.Actions = prepared.EffectTreatments.Actions ?? new List<EffectTreatmentAction>();
		prepared.EffectTreatments.Diagnostics = prepared.EffectTreatments.Diagnostics ?? new List<EffectTreatmentDiagnostic>();
		if (prepared.SongPlan != null)
		{
			prepared.SongPlan.Events = prepared.SongPlan.Events ?? new List<MontageSongPlanningEvent>();
			prepared.SongPlan.Regions = prepared.SongPlan.Regions ?? new List<MontageSongPlanningRegion>();
			prepared.SongPlan.Diagnostics = prepared.SongPlan.Diagnostics ?? new List<MontageSongPlanningDiagnostic>();
		}
		if (prepared.Placements.Count == 0)
			throw new InvalidOperationException("The prepared montage contains no clip placements.");

		double previousEnd = -1.0;
		foreach (ClipPlacement placement in prepared.Placements)
		{
			if (placement?.Clip == null || string.IsNullOrWhiteSpace(placement.Clip.FilePath))
				throw new InvalidDataException("A prepared montage clip has no identity or media path.");
			if (!Finite(placement.Clip.DurationSeconds) || placement.Clip.DurationSeconds <= 0.0)
				throw new InvalidDataException("A prepared montage clip has an invalid duration: " + placement.Clip.FilePath);
			if (placement.SpeedProfile == null || placement.SpeedProfile.Points == null || placement.SpeedProfile.Points.Count < 2)
				throw new InvalidDataException("A prepared montage clip has no valid speed profile: " + placement.Clip.FilePath);
			if (!Finite(placement.TimelineStartSeconds) || !Finite(placement.SourceOffsetSeconds) ||
				!Finite(placement.LengthSeconds) || placement.LengthSeconds <= 0.0 ||
				placement.TimelineStartSeconds < 0.0 || placement.SourceOffsetSeconds < 0.0 ||
				placement.TimelineStartSeconds < previousEnd - 0.002)
				throw new InvalidDataException("Prepared montage placements are invalid or overlap: " + placement.Clip.FilePath);
			if (placement.SourceOffsetSeconds + placement.SpeedProfile.TotalSourceConsumptionSeconds >
				placement.Clip.DurationSeconds + 0.002)
				throw new InvalidDataException("A prepared montage placement exceeds its source clip: " + placement.Clip.FilePath);
			previousEnd = placement.TimelineEndSeconds;
		}
	}

	private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
