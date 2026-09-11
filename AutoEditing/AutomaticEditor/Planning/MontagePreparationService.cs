using System.Collections.Generic;
using System.Linq;
using Core.Domain;
using Core.Domain.Audio;
using Core.Domain.Clip;

namespace Core.Domain.Editing;

public sealed class MontagePreparationService
{
	public PreparedMontage Prepare(List<Core.Domain.Clip.Clip> reviewedClips, string songPath)
	{
		return Prepare(reviewedClips, songPath, null);
	}

	public PreparedMontage Prepare(List<Core.Domain.Clip.Clip> reviewedClips, string songPath, EffectSelectionOptions effectOptions)
	{
		effectOptions = effectOptions ?? new EffectSelectionOptions();
		effectOptions.Validate();
		MonoAudio audio = AudioLoader.LoadMono(songPath);
		BeatGrid beats;
		Core.Domain.Audio.SongAnalysis.SongAnalysis reviewedAnalysis;
		MontageSongPlanningInput songPlan = new MontageSongPlanningInputProvider().Load(songPath, audio, out beats, out reviewedAnalysis);
		ShotDetectionConfig config = ConfigurationManager.GetShotDetection();
		MontagePlanner planner = new MontagePlanner(config.PreRollSeconds, config.PostRollSeconds, config.MinVelocity, config.MaxVelocity);
		MontagePlanningResult result = planner.PlanMontage(reviewedClips, songPlan);
		if (!result.IsFeasible)
		{
			/* Only the error diagnostics explain the failure; informational lines such as the automatic
			   anchor-suggestion count otherwise read as the cause. */
			throw new System.InvalidOperationException("The reviewed song map cannot be planned: " +
				string.Join(" ", result.Diagnostics
					.Where((MontageSongPlanningDiagnostic item) => item.Severity == MontageSongPlanningDiagnosticSeverity.Error)
					.Select((MontageSongPlanningDiagnostic item) => item.Message)
					.Distinct(System.StringComparer.Ordinal)
					.ToList()));
		}
		EffectTreatmentPlan effectTreatments = reviewedAnalysis == null
			? new EffectTreatmentPlan()
			: new AutomaticEffectTreatmentPlanner().Plan(reviewedAnalysis, effectOptions.CreatePreset(), effectOptions);
		effectTreatments = new PlacementAwareEffectTreatmentPlanner().Plan(
			reviewedAnalysis,
			result.Placements,
			result.Assignments,
			effectOptions,
			effectTreatments);
		return new PreparedMontage
		{
			Placements = result.Placements,
			Beats = beats,
			SongPlan = songPlan,
			SyncAssignments = result.Assignments,
			PlanningDiagnostics = result.Diagnostics,
			TimelineGaps = result.TimelineGaps,
			EffectOptions = effectOptions,
			EffectTreatments = effectTreatments
		};
	}
}
