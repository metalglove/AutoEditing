using AutoEditing.Iteration.Contracts.Assembly;
using Core.Domain.Editing;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Assembly;

internal static class ProgressiveAssemblyContextFactory
{
	public static ProgressiveAssemblyPlanningContext Create(
		EditPlanningRequest request,
		AssemblySketch sketch,
		EditPlanDocument? acceptedPlan,
		int stepIndex,
		TimelineAdjustmentDelta? currentAdjustment = null,
		string? scopedInstruction = null)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(sketch);
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		ProgressiveAssemblyContractValidator.Validate(sketch);
		List<ClipPlacement> accepted = acceptedPlan?.Montage.Placements
			.OrderBy(item => item.TimelineStartSeconds)
			.ToList() ?? new List<ClipPlacement>();
		if (stepIndex != accepted.Count + 1)
			throw new InvalidOperationException(
				"The progressive context step does not immediately follow the accepted prefix.");

		HashSet<string> acceptedPaths = new(
			accepted.Select(item => item.Clip.FilePath),
			StringComparer.OrdinalIgnoreCase);
		HashSet<string> usedEvents = new(
			acceptedPlan?.Montage.SyncAssignments.Select(item => item.MusicEventId) ??
				Enumerable.Empty<string>(),
			StringComparer.Ordinal);
		double acceptedEnd = accepted.Count == 0
			? 0
			: accepted.Max(item => item.TimelineEndSeconds);

		return new ProgressiveAssemblyPlanningContext
		{
			RequestId = request.RequestId,
			StepIndex = stepIndex,
			Sketch = sketch,
			AcceptedPrefix = accepted.Select((placement, index) =>
				SummarizeAccepted(
					placement,
					index + 1,
					acceptedPlan?.Montage.SyncAssignments ??
						new List<MontageSyncAssignment>()))
				.ToList(),
			RemainingClips = sketch.ClipOrder
				.Select(item => item.Clip)
				.Where(item => !acceptedPaths.Contains(item.MediaPath))
				.ToList(),
			NearbySongContext = request.SongAnalysis.Events
				.Where(item => item.IsGameplayAnchor && !item.IsIntentionallyUnused)
				.Where(item => !usedEvents.Contains(item.Id))
				.Where(item => item.EffectiveTimeSeconds >= acceptedEnd - 0.02)
				.OrderBy(item => item.EffectiveTimeSeconds)
				.ThenByDescending(item => item.Priority)
				.Select(item => new AssemblySongEventReference
				{
					EventId = item.Id,
					EffectiveTimeSeconds = item.EffectiveTimeSeconds,
					RegionId = item.ContainingRegionId ?? "",
					MusicalType = item.MusicalType.ToString(),
					Priority = item.Priority
				})
				.ToList(),
			TimelineAdjustment = currentAdjustment,
			ScopedInstruction = (scopedInstruction ?? "").Trim()
		};
	}

	private static AcceptedClipPlacementSummary SummarizeAccepted(
		ClipPlacement placement,
		int stepIndex,
		IEnumerable<MontageSyncAssignment> assignments)
	{
		double firstSpeed = placement.SpeedProfile.Points.First().Speed;
		bool isConstant = placement.SpeedProfile.Points.All(
			item => Math.Abs(item.Speed - firstSpeed) <= 0.000001);
		double sourceStart = placement.SourceOffsetSeconds;
		List<MontageSyncAssignment> surviving = assignments
			.Where(item => string.Equals(
				item.ClipPath, placement.Clip.FilePath,
				StringComparison.OrdinalIgnoreCase))
			.OrderBy(item => item.TimelineTimeSeconds)
			.ToList();
		return new AcceptedClipPlacementSummary
		{
			StepIndex = stepIndex,
			Clip = new AssemblyClipReference
			{
				ReferenceId = AssemblyReferenceIds.ForClipPath(placement.Clip.FilePath),
				MediaPath = placement.Clip.FilePath
			},
			TimelineStartSeconds = placement.TimelineStartSeconds,
			TimelineEndSeconds = placement.TimelineEndSeconds,
			SourceStartSeconds = placement.SourceOffsetSeconds,
			SourceEndSeconds =
				placement.SourceOffsetSeconds +
				placement.SpeedProfile.TotalSourceConsumptionSeconds,
			ConstantSpeed = isConstant ? firstSpeed : placement.SpeedProfile.Points.First().Speed,
			VelocityCurve = isConstant ? null : placement.SpeedProfile.Points.Select(point =>
				new VelocityCurvePoint
				{
					OffsetSeconds = point.SourceTimeSeconds - sourceStart,
					Speed = point.Speed
				}).ToList(),
			SurvivingSyncs = surviving.Select(item => new AssemblySyncDecision
				{
					MusicEventId = item.MusicEventId,
					KillIndex = item.KillIndex
				})
				.ToList(),
			WasHumanAdjusted = surviving.Count == 0,
			AdjustmentRationale = surviving.Count == 0
				? "The accepted VEGAS timing no longer preserves a proposed musical sync."
				: ""
		};
	}
}
