using Core.Domain.Editing;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Assembly;

internal static class AssemblyPlanSlices
{
	public static EditPlanDocument Prefix(EditPlanDocument source, int count)
	{
		if (count < 1 || count > source.Montage.Placements.Count)
			throw new ArgumentOutOfRangeException(nameof(count));
		List<ClipPlacement> placements = source.Montage.Placements
			.OrderBy(item => item.TimelineStartSeconds)
			.Take(count)
			.ToList();
		HashSet<string> paths = new(
			placements.Select(item => item.Clip.FilePath),
			StringComparer.OrdinalIgnoreCase);
		return new EditPlanDocument
		{
			RequestId = source.RequestId,
			PlannerId = source.PlannerId,
			PlannerVersion = source.PlannerVersion,
			StyleProfileIds = source.StyleProfileIds.ToList(),
			Montage = new PreparedMontage
			{
				Placements = placements,
				SongPlan = source.Montage.SongPlan,
				SyncAssignments = source.Montage.SyncAssignments
					.Where(item => paths.Contains(item.ClipPath))
					.ToList(),
				EffectOptions = EffectsDisabled(source.Montage.EffectOptions),
				EffectTreatments = new EffectTreatmentPlan()
			},
			Diagnostics = source.Diagnostics.ToList()
		};
	}

	private static EffectSelectionOptions EffectsDisabled(EffectSelectionOptions source) =>
		new()
		{
			PresetId = source.PresetId,
			Intensity = source.Intensity,
			Density = source.Density,
			IncludeManualTreatments = false,
			EnableScreenPumps = false,
			EnableFlashes = false,
			EnableShake = false,
			EnableTransitions = false,
			EnableTitles = false,
			EnableSpeedChanges = source.EnableSpeedChanges
		};
}
