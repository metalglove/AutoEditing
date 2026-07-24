using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Domain;
using Core.Domain.Audio;
using Core.Domain.Editing;

namespace Core.Scripts;

internal static class PreparedMontageValidator
{
	public static void ValidateAndNormalize(PreparedMontage prepared, string songPath)
	{
		if (prepared == null) throw new InvalidOperationException("Montage build request is empty.");
		if (string.IsNullOrWhiteSpace(songPath) || !File.Exists(songPath)) throw new FileNotFoundException("The montage song is unavailable.", songPath);
		AudioLoader.GetDurationSeconds(songPath);
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
		if (prepared.Placements.Count == 0) throw new InvalidOperationException("The prepared montage contains no clip placements.");

		double previousEnd = -1.0;
		foreach (ClipPlacement placement in prepared.Placements)
		{
			if (placement?.Clip == null || string.IsNullOrWhiteSpace(placement.Clip.FilePath) || !File.Exists(placement.Clip.FilePath)) throw new FileNotFoundException("A prepared montage clip is unavailable.", placement?.Clip?.FilePath);
			if (placement.SpeedProfile == null || placement.SpeedProfile.Points == null || placement.SpeedProfile.Points.Count < 2) throw new InvalidDataException("A prepared montage clip has no valid speed profile: " + placement.Clip.FilePath);
			if (!Finite(placement.TimelineStartSeconds) || !Finite(placement.LengthSeconds) || placement.LengthSeconds <= 0.0 || placement.TimelineStartSeconds < previousEnd - 0.002) throw new InvalidDataException("Prepared montage placements are invalid or overlap: " + placement.Clip.FilePath);
			previousEnd = placement.TimelineEndSeconds;
		}

		ShotDetectionConfig config = ConfigurationManager.GetShotDetection();
		SfxTemplateCatalog catalog = SfxTemplateCatalog.Load(config.SfxRoot);
		foreach (string gun in prepared.Placements.Select((ClipPlacement item) => item.Clip.Gun).Where((string item) => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase)) catalog.ValidateForGun(config.SfxRoot, gun);
	}

	private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
