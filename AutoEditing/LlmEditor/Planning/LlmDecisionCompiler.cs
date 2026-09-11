using Core.Domain.Audio;
using Core.Domain.Editing;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Planning;

internal sealed class LlmDecisionCompiler
{
	private const double TimingToleranceSeconds = 0.02;

	public EditPlanDocument Compile(
		EditPlanningRequest request,
		LlmEditDecisionDocument decisions,
		string model)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(decisions);
		if (decisions.SchemaVersion != LlmEditDecisionDocument.CurrentSchemaVersion)
			throw new NotSupportedException(
				"Unsupported LLM decision schema version " + decisions.SchemaVersion + ".");
		if (!string.Equals(decisions.RequestId, request.RequestId, StringComparison.Ordinal))
			throw new InvalidOperationException("The model changed the planning request ID.");
		if (decisions.Placements == null || decisions.Placements.Count == 0)
			throw new InvalidOperationException("The model returned no placement decisions.");
		if (request.SongAnalysis == null)
			throw new InvalidOperationException("The planning request has no reviewed song analysis.");

		Dictionary<string, Core.Domain.Clip.Clip> clips = request.Clips
			.ToDictionary(item => item.FilePath, StringComparer.OrdinalIgnoreCase);
		Dictionary<string, MontageSongPlanningEvent> anchors = request.SongAnalysis.Events
			.Where(item => item.IsGameplayAnchor && !item.IsIntentionallyUnused)
			.ToDictionary(item => item.Id, StringComparer.Ordinal);
		HashSet<string> usedClips = new(StringComparer.OrdinalIgnoreCase);
		HashSet<string> usedEvents = new(StringComparer.Ordinal);
		List<ClipPlacement> placements = new();
		List<MontageSyncAssignment> assignments = new();

		foreach (LlmPlacementDecision decision in decisions.Placements)
		{
			if (!clips.TryGetValue(decision.ClipPath, out Core.Domain.Clip.Clip? clip))
				throw new InvalidOperationException("The model referenced an unavailable clip: " + decision.ClipPath);
			if (!usedClips.Add(clip.FilePath))
				throw new InvalidOperationException("The model placed the same clip more than once: " + clip.FilePath);
			ValidateWindow(decision, clip, request.EffectOptions.EnableSpeedChanges);
			SpeedProfile speed = new(new[]
			{
				new SpeedProfilePoint(decision.SourceStartSeconds, decision.Speed),
				new SpeedProfilePoint(decision.SourceEndSeconds, decision.Speed)
			});
			double timelineStart = ResolveTimelineStart(
				decision.PrimarySync, clip, speed, anchors);
			ClipPlacement placement = new()
			{
				Clip = clip,
				TimelineStartSeconds = timelineStart,
				SourceOffsetSeconds = decision.SourceStartSeconds,
				LengthSeconds = speed.TimelineDurationSeconds,
				SpeedProfile = speed
			};
			AddAssignment(
				decision.PrimarySync, placement, anchors, assignments, usedEvents, true);
			foreach (LlmSyncDecision sync in decision.AdditionalSyncs ?? new List<LlmSyncDecision>())
				AddAssignment(sync, placement, anchors, assignments, usedEvents, false);
			placement.AssignedBeatTimesSeconds = assignments
				.Where(item => string.Equals(item.ClipPath, clip.FilePath, StringComparison.OrdinalIgnoreCase))
				.Select(item => item.TimelineTimeSeconds)
				.OrderBy(value => value)
				.ToList();
			placements.Add(placement);
		}
		if (usedClips.Count != clips.Count)
		{
			string omitted = string.Join(", ", clips.Keys
				.Where(path => !usedClips.Contains(path))
				.Select(Path.GetFileName));
			throw new InvalidOperationException(
				"The model omitted selected clips from the complete baseline: " + omitted);
		}
		HashSet<string> coveredRegions = new(
			assignments
				.Select(item => anchors[item.MusicEventId].ContainingRegionId)
				.Where(id => !string.IsNullOrWhiteSpace(id)),
			StringComparer.Ordinal);
		List<string> uncoveredRegions = request.SongAnalysis.Regions
			.Where(region => region.Type != Core.Domain.Audio.SongAnalysis.MusicRegionType.Unused)
			.Where(region => anchors.Values.Any(anchor =>
				string.Equals(anchor.ContainingRegionId, region.Id, StringComparison.Ordinal)))
			.Where(region => !coveredRegions.Contains(region.Id))
			.Select(region => region.Id)
			.ToList();
		if (uncoveredRegions.Count > 0)
			throw new InvalidOperationException(
				"The model did not cover every usable reviewed song region: " +
				string.Join(", ", uncoveredRegions));

		placements = placements.OrderBy(item => item.TimelineStartSeconds).ToList();
		double previousEnd = -1.0;
		foreach (ClipPlacement placement in placements)
		{
			if (placement.TimelineStartSeconds < previousEnd - TimingToleranceSeconds)
				throw new InvalidOperationException(
					"Model decisions produce overlapping placements near " +
					placement.TimelineStartSeconds.ToString("0.000") + "s.");
			previousEnd = placement.TimelineEndSeconds;
		}

		EditPlanDocument plan = new()
		{
			RequestId = request.RequestId,
			PlannerId = "llm.openai-compatible.decisions",
			PlannerVersion = string.IsNullOrWhiteSpace(model) ? "unknown" : model,
			StyleProfileIds = request.StyleProfileIds.ToList(),
			Montage = new PreparedMontage
			{
				Placements = placements,
				SongPlan = request.SongAnalysis,
				SyncAssignments = assignments.OrderBy(item => item.TimelineTimeSeconds).ToList(),
				EffectOptions = request.EffectOptions,
				EffectTreatments = new EffectTreatmentPlan()
			},
			Diagnostics = (decisions.Diagnostics ?? new List<LlmDecisionDiagnostic>())
				.Select(item => new EditPlanDiagnostic
				{
					Severity = item.Severity,
					Code = item.Code,
					Message = item.Message
				})
				.ToList()
		};
		EditPlanDocumentValidator.ValidateAndNormalize(plan);
		return plan;
	}

	private static void ValidateWindow(
		LlmPlacementDecision decision,
		Core.Domain.Clip.Clip clip,
		bool speedChangesEnabled)
	{
		if (!Finite(decision.SourceStartSeconds) || !Finite(decision.SourceEndSeconds) ||
			decision.SourceStartSeconds < 0.0 ||
			decision.SourceEndSeconds <= decision.SourceStartSeconds ||
			decision.SourceEndSeconds > clip.DurationSeconds + 0.002)
			throw new InvalidOperationException("The model selected an invalid source window for " + clip.FilePath);
		if (!Finite(decision.Speed) || decision.Speed < 0.25 || decision.Speed > 4.0)
			throw new InvalidOperationException("The model selected an invalid speed for " + clip.FilePath);
		if (!speedChangesEnabled && Math.Abs(decision.Speed - 1.0) > 0.000001)
			throw new InvalidOperationException(
				"The model changed speed while speed changes are disabled: " + clip.FilePath);
	}

	private static double ResolveTimelineStart(
		LlmSyncDecision sync,
		Core.Domain.Clip.Clip clip,
		SpeedProfile speed,
		IReadOnlyDictionary<string, MontageSongPlanningEvent> anchors)
	{
		ShotEvent kill = ResolveKill(sync, clip);
		MontageSongPlanningEvent anchor = ResolveAnchor(sync, anchors);
		if (!speed.TryGetTimelineTimeForSourceTime(kill.SourceConfirmationTimeSeconds, out double relative))
			throw new InvalidOperationException("The primary synced kill lies outside its selected source window.");
		double start = anchor.EffectiveTimeSeconds - relative;
		if (start < -TimingToleranceSeconds)
			throw new InvalidOperationException("The primary sync would place a clip before the song starts.");
		return Math.Max(0.0, start);
	}

	private static void AddAssignment(
		LlmSyncDecision sync,
		ClipPlacement placement,
		IReadOnlyDictionary<string, MontageSongPlanningEvent> anchors,
		List<MontageSyncAssignment> assignments,
		HashSet<string> usedEvents,
		bool primary)
	{
		ShotEvent kill = ResolveKill(sync, placement.Clip);
		MontageSongPlanningEvent anchor = ResolveAnchor(sync, anchors);
		if (!usedEvents.Add(anchor.Id))
			throw new InvalidOperationException("The model reused a musical anchor: " + anchor.Id);
		if (!placement.SpeedProfile.TryGetTimelineTimeForSourceTime(
			kill.SourceConfirmationTimeSeconds, out double relative))
			throw new InvalidOperationException("A synced kill lies outside its selected source window.");
		double actual = placement.TimelineStartSeconds + relative;
		if (Math.Abs(actual - anchor.EffectiveTimeSeconds) > TimingToleranceSeconds)
			throw new InvalidOperationException(
				(primary ? "Primary" : "Additional") +
				" sync does not align the selected kill with music event " + anchor.Id + ".");
		assignments.Add(new MontageSyncAssignment
		{
			ClipPath = placement.Clip.FilePath,
			KillIndex = sync.KillIndex,
			SourceConfirmationTimeSeconds = kill.SourceConfirmationTimeSeconds,
			MusicEventId = anchor.Id,
			TimelineTimeSeconds = anchor.EffectiveTimeSeconds
		});
	}

	private static ShotEvent ResolveKill(
		LlmSyncDecision sync,
		Core.Domain.Clip.Clip clip)
	{
		List<ShotEvent> kills = clip.ConfirmedKills
			.OrderBy(item => item.SourceConfirmationTimeSeconds)
			.ToList();
		if (sync == null || sync.KillIndex < 0 || sync.KillIndex >= kills.Count)
			throw new InvalidOperationException("The model referenced an invalid kill index for " + clip.FilePath);
		return kills[sync.KillIndex];
	}

	private static MontageSongPlanningEvent ResolveAnchor(
		LlmSyncDecision sync,
		IReadOnlyDictionary<string, MontageSongPlanningEvent> anchors)
	{
		if (sync == null || !anchors.TryGetValue(sync.MusicEventId, out MontageSongPlanningEvent? anchor))
			throw new InvalidOperationException(
				"The model referenced a musical event that is not an eligible gameplay anchor.");
		return anchor;
	}

	private static bool Finite(double value) =>
		!double.IsNaN(value) && !double.IsInfinity(value);
}
