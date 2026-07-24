using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain.Audio.SongAnalysis;

namespace Core.Domain.Editing;

/// <summary>
/// Adds treatments that can only be decided after clips and reviewed kills have
/// been placed on the montage timeline.
/// </summary>
public sealed class PlacementAwareEffectTreatmentPlanner
{
	private const double TimeTolerance = 0.001;

	public EffectTreatmentPlan Plan(
		SongAnalysis analysis,
		IEnumerable<ClipPlacement> placements,
		IEnumerable<MontageSyncAssignment> assignments,
		EffectSelectionOptions options,
		EffectTreatmentPlan plan)
	{
		options = options ?? new EffectSelectionOptions();
		options.Validate();
		plan = plan ?? new EffectTreatmentPlan();
		plan.Actions = plan.Actions ?? new List<EffectTreatmentAction>();
		plan.Diagnostics = plan.Diagnostics ?? new List<EffectTreatmentDiagnostic>();
		if (!options.EnableScreenPumps || options.PresetId == EffectSelectionOptions.NoAutomaticEffectsPresetId) return plan;

		List<ClipPlacement> placed = (placements ?? Enumerable.Empty<ClipPlacement>())
			.Where(item => item != null)
			.OrderBy(item => item.TimelineStartSeconds)
			.ToList();
		List<MontageSyncAssignment> kills = (assignments ?? Enumerable.Empty<MontageSyncAssignment>())
			.Where(item => item != null)
			.OrderBy(item => item.TimelineTimeSeconds)
			.ThenBy(item => item.ClipPath, StringComparer.OrdinalIgnoreCase)
			.ThenBy(item => item.KillIndex)
			.ToList();

		foreach (MontageSyncAssignment kill in kills)
		{
			if (!TargetsVideo(placed, kill.TimelineTimeSeconds))
			{
				plan.Diagnostics.Add(Diagnostic(kill.MusicEventId, kill.TimelineTimeSeconds, "kill-pump-outside-placement",
					"Mandatory kill screen pump was not planned because the assigned kill is outside every placed video interval."));
				continue;
			}
			if (HasManualPump(plan, kill.MusicEventId, kill.TimelineTimeSeconds)) continue;
			RemoveDuplicateAutomaticPumps(plan, kill.MusicEventId, kill.TimelineTimeSeconds);
			plan.Actions.Add(new EffectTreatmentAction
			{
				EventId = kill.MusicEventId,
				TimeSeconds = kill.TimelineTimeSeconds,
				Type = EditorialUse.ScreenPump,
				RecipeId = "native.pump.impact",
				Intensity = Clamp(0.82 * options.Intensity),
				DurationSeconds = 0.24,
				Origin = EffectTreatmentOrigin.Automatic,
				Reason = "Placement-aware mandatory pump on reviewed kill " + (kill.KillIndex + 1) + "."
			});
		}

		if (analysis == null || kills.Count < 2) return Sort(plan);
		int maximumBetweenKills = options.Density < 0.75 ? 1 : 2;
		if (maximumBetweenKills == 0) return Sort(plan);
		List<MusicEvent> eligible = (analysis.Events ?? new List<MusicEvent>())
			.Where(item => item != null && item.ReviewState != MusicAnalysisReviewState.Rejected && IsIntermediateType(item.Type))
			.Where(item => !(item.Editorial?.Assignments ?? new List<EditorialAssignment>())
				.Any(assignment => assignment != null && assignment.Use == EditorialUse.IntentionallyUnused))
			.OrderBy(item => item.TimeSeconds)
			.ThenBy(item => item.Id, StringComparer.Ordinal)
			.ToList();

		for (int index = 1; index < kills.Count; index++)
		{
			MontageSyncAssignment before = kills[index - 1];
			MontageSyncAssignment after = kills[index];
			List<MusicEvent> between = eligible
				.Where(item => item.TimeSeconds > before.TimelineTimeSeconds + TimeTolerance &&
					item.TimeSeconds < after.TimelineTimeSeconds - TimeTolerance)
				.Where(item => !HasPump(plan, item.Id, item.TimeSeconds))
				.ToList();
			// This recipe is specifically for a short one- or two-beat pocket
			// between kills. A longer gap is a different editorial situation
			// and must not receive pumps merely on its first beats.
			if (between.Count == 0 || between.Count > 2) continue;
			between = between.Take(maximumBetweenKills).ToList();
			foreach (MusicEvent musicEvent in between)
			{
				if (!TargetsVideo(placed, musicEvent.TimeSeconds))
				{
					plan.Diagnostics.Add(Diagnostic(musicEvent.Id, musicEvent.TimeSeconds, "intermediate-pump-outside-placement",
						"Intermediate screen pump was not planned because the eligible musical event is outside every placed video interval."));
					continue;
				}
				plan.Actions.Add(new EffectTreatmentAction
				{
					EventId = musicEvent.Id,
					TimeSeconds = musicEvent.TimeSeconds,
					Type = EditorialUse.ScreenPump,
					RecipeId = "native.pump.subtle",
					Intensity = Clamp((0.32 + 0.18 * (musicEvent.Strength ?? 0.5)) * options.Intensity),
					DurationSeconds = 0.18,
					Origin = EffectTreatmentOrigin.Automatic,
					Reason = "Conservative placement-aware pump between consecutive reviewed kills."
				});
			}
		}
		return Sort(plan);
	}

	private static bool IsIntermediateType(MusicEventType type)
	{
		return type == MusicEventType.Beat || type == MusicEventType.Downbeat || type == MusicEventType.Accent;
	}

	private static bool TargetsVideo(IEnumerable<ClipPlacement> placements, double time)
	{
		return placements.Any(item => time >= item.TimelineStartSeconds - TimeTolerance && time <= item.TimelineEndSeconds + TimeTolerance);
	}

	private static bool HasPump(EffectTreatmentPlan plan, string eventId, double time)
	{
		return plan.Actions.Any(item => item.Type == EditorialUse.ScreenPump &&
			((!string.IsNullOrWhiteSpace(eventId) && item.EventId == eventId) || Math.Abs(item.TimeSeconds - time) <= TimeTolerance));
	}

	private static bool HasManualPump(EffectTreatmentPlan plan, string eventId, double time)
	{
		return plan.Actions.Any(item => item.Type == EditorialUse.ScreenPump && item.Origin == EffectTreatmentOrigin.Manual &&
			((!string.IsNullOrWhiteSpace(eventId) && item.EventId == eventId) || Math.Abs(item.TimeSeconds - time) <= TimeTolerance));
	}

	private static void RemoveDuplicateAutomaticPumps(EffectTreatmentPlan plan, string eventId, double time)
	{
		plan.Actions.RemoveAll(item => item.Type == EditorialUse.ScreenPump && item.Origin == EffectTreatmentOrigin.Automatic &&
			((!string.IsNullOrWhiteSpace(eventId) && item.EventId == eventId) || Math.Abs(item.TimeSeconds - time) <= TimeTolerance));
	}

	private static EffectTreatmentPlan Sort(EffectTreatmentPlan plan)
	{
		plan.Actions = plan.Actions.OrderBy(item => item.TimeSeconds).ThenBy(item => item.EventId, StringComparer.Ordinal).ToList();
		return plan;
	}

	private static EffectTreatmentDiagnostic Diagnostic(string eventId, double time, string code, string message)
	{
		return new EffectTreatmentDiagnostic { EventId = eventId, TimeSeconds = time, Code = code, Message = message };
	}

	private static double Clamp(double value) => Math.Max(0, Math.Min(1, value));
}
