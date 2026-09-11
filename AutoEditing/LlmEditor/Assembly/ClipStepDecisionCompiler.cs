using AutoEditing.Iteration.Contracts.Assembly;
using Core.Domain.Audio;
using Core.Domain.Editing;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Assembly;

internal sealed record ClipStepCompilationResult(
	ClipPlacement Placement,
	IReadOnlyList<MontageSyncAssignment> SyncAssignments,
	EditPlanDocument CombinedPlan);

/// <summary>
/// Compiles one untrusted clip-step decision into domain objects, then validates the
/// complete accepted-prefix-plus-current-step candidate.
/// </summary>
internal sealed class ClipStepDecisionCompiler
{
	private const double ToleranceSeconds = 0.002;

	public ClipStepCompilationResult Append(
		EditPlanningRequest request,
		EditPlanDocument? acceptedPrefix,
		ClipStepDecision decision,
		string plannerId = "llm.openai-compatible.progressive",
		string plannerVersion = "unknown")
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(decision);
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		if (request.SongAnalysis == null)
			throw new InvalidOperationException("The planning request has no reviewed song analysis.");
		ProgressiveAssemblyContractValidator.Validate(decision);
		if (!string.Equals(decision.RequestId, request.RequestId, StringComparison.Ordinal))
			throw new InvalidOperationException("The clip-step decision changed the planning request ID.");

		List<ClipPlacement> prefixPlacements = acceptedPrefix?.Montage?.Placements?
			.OrderBy(item => item.TimelineStartSeconds).ToList() ?? new();
		List<MontageSyncAssignment> prefixAssignments =
			acceptedPrefix?.Montage?.SyncAssignments?.ToList() ?? new();
		ValidatePrefix(request, acceptedPrefix, prefixPlacements, prefixAssignments);
		if (decision.StepIndex != prefixPlacements.Count + 1)
			throw new InvalidOperationException(
				"The clip-step index is inconsistent with the accepted prefix.");

		Core.Domain.Clip.Clip clip = ResolveClip(request, decision);
		if (prefixPlacements.Any(item => string.Equals(
			item.Clip.FilePath, clip.FilePath, StringComparison.OrdinalIgnoreCase)))
			throw new InvalidOperationException(
				"The clip-step decision reused an already accepted clip: " + clip.FilePath);
		ValidateWindow(decision, clip, request.EffectOptions.EnableSpeedChanges);

		Dictionary<string, MontageSongPlanningEvent> eligibleEvents = EligibleEvents(request);
		HashSet<string> usedEvents = prefixAssignments
			.Select(item => item.MusicEventId)
			.ToHashSet(StringComparer.Ordinal);
		SpeedProfile speed = BuildSpeedProfile(decision.SourceWindow);
		ShotEvent primaryKill = ResolveKill(clip, decision.PrimarySync);
		MontageSongPlanningEvent primaryEvent =
			ResolveEvent(decision.PrimarySync, eligibleEvents, usedEvents);
		if (!speed.TryGetTimelineTimeForSourceTime(
			primaryKill.SourceConfirmationTimeSeconds, out double primaryRelative))
			throw new InvalidOperationException(
				"The primary synced kill lies outside the selected source window.");
		double timelineStart = primaryEvent.EffectiveTimeSeconds - primaryRelative;
		if (timelineStart < -ToleranceSeconds)
			throw new InvalidOperationException(
				"The primary sync would place the clip before the song starts.");
		timelineStart = Math.Max(0, timelineStart);

		ClipPlacement placement = new()
		{
			Clip = clip,
			TimelineStartSeconds = timelineStart,
			SourceOffsetSeconds = decision.SourceWindow.StartSeconds,
			LengthSeconds = speed.TimelineDurationSeconds,
			SpeedProfile = speed
		};
		if (placement.TimelineEndSeconds >
			request.SongAnalysis.SongDurationSeconds + ToleranceSeconds)
			throw new InvalidOperationException(
				"The clip-step placement extends beyond the song.");

		List<MontageSyncAssignment> stepAssignments = new();
		HashSet<int> assignedKills = new();
		AddAssignment(
			decision.PrimarySync, placement, primaryKill, primaryEvent,
			usedEvents, assignedKills, stepAssignments, "Primary");
		foreach (AssemblySyncDecision additional in decision.AdditionalSyncs ??
			new List<AssemblySyncDecision>())
		{
			ShotEvent kill = ResolveKill(clip, additional);
			MontageSongPlanningEvent musicEvent =
				ResolveEvent(additional, eligibleEvents, usedEvents);
			AddAssignment(
				additional, placement, kill, musicEvent,
				usedEvents, assignedKills, stepAssignments, "Additional");
		}
		placement.AssignedBeatTimesSeconds = stepAssignments
			.Select(item => item.TimelineTimeSeconds)
			.OrderBy(value => value)
			.ToList();

		List<ClipPlacement> combinedPlacements = prefixPlacements
			.Append(placement)
			.OrderBy(item => item.TimelineStartSeconds)
			.ToList();
		List<MontageSyncAssignment> combinedAssignments = prefixAssignments
			.Concat(stepAssignments)
			.OrderBy(item => item.TimelineTimeSeconds)
			.ToList();
		EditPlanDocument combined = BuildPlan(
			request, combinedPlacements, combinedAssignments, plannerId, plannerVersion);
		ValidateCombined(request, combined);
		return new ClipStepCompilationResult(placement, stepAssignments, combined);
	}

	public ClipStepCompilationResult ReplaceCurrent(
		EditPlanningRequest request,
		EditPlanDocument combinedCandidate,
		ClipStepDecision replacement,
		string plannerId = "llm.openai-compatible.progressive",
		string plannerVersion = "unknown")
	{
		ArgumentNullException.ThrowIfNull(combinedCandidate);
		List<ClipPlacement> ordered = combinedCandidate.Montage?.Placements?
			.OrderBy(item => item.TimelineStartSeconds).ToList() ??
			throw new InvalidOperationException("The combined candidate has no placements.");
		if (ordered.Count == 0)
			throw new InvalidOperationException("There is no current clip step to replace.");
		HashSet<string> acceptedPaths = ordered.Take(ordered.Count - 1)
			.Select(item => item.Clip.FilePath)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		EditPlanDocument? prefix = acceptedPaths.Count == 0 ? null : BuildPlan(
			request,
			ordered.Take(ordered.Count - 1).ToList(),
			combinedCandidate.Montage.SyncAssignments
				.Where(item => acceptedPaths.Contains(item.ClipPath))
				.ToList(),
			combinedCandidate.PlannerId,
			combinedCandidate.PlannerVersion);
		return Append(request, prefix, replacement, plannerId, plannerVersion);
	}

	private static void ValidatePrefix(
		EditPlanningRequest request,
		EditPlanDocument? prefix,
		IReadOnlyList<ClipPlacement> placements,
		IReadOnlyList<MontageSyncAssignment> assignments)
	{
		if (prefix == null) return;
		if (!string.Equals(prefix.RequestId, request.RequestId, StringComparison.Ordinal))
			throw new InvalidOperationException("The accepted prefix belongs to another request.");
		ValidateCombined(request, prefix);
		if (placements.Select(item => item.Clip.FilePath)
			.Distinct(StringComparer.OrdinalIgnoreCase).Count() != placements.Count)
			throw new InvalidOperationException("The accepted prefix reuses a clip.");
		if (assignments.Select(item => item.MusicEventId)
			.Distinct(StringComparer.Ordinal).Count() != assignments.Count)
			throw new InvalidOperationException("The accepted prefix reuses a musical event.");
	}

	private static Core.Domain.Clip.Clip ResolveClip(
		EditPlanningRequest request,
		ClipStepDecision decision)
	{
		if (decision.Clip == null || string.IsNullOrWhiteSpace(decision.Clip.MediaPath))
			throw new InvalidOperationException("The clip-step decision has no clip reference.");
		List<Core.Domain.Clip.Clip> matches = request.Clips.Where(item =>
			string.Equals(item.FilePath, decision.Clip.MediaPath,
				StringComparison.OrdinalIgnoreCase)).ToList();
		if (matches.Count != 1)
			throw new InvalidOperationException(
				"The clip-step decision referenced an unavailable or ambiguous clip: " +
				decision.Clip.MediaPath);
		return matches[0];
	}

	private static void ValidateWindow(
		ClipStepDecision decision,
		Core.Domain.Clip.Clip clip,
		bool speedChangesEnabled)
	{
		if (decision.SourceWindow == null)
			throw new InvalidOperationException("The clip-step source window is required.");
		double start = decision.SourceWindow.StartSeconds;
		double end = decision.SourceWindow.EndSeconds;
		double speed = decision.SourceWindow.ConstantSpeed;
		bool hasCurve = (decision.SourceWindow.VelocityCurve?.Count ?? 0) > 0;
		if (!Finite(start) || !Finite(end) || start < 0 || end <= start ||
			end > clip.DurationSeconds + ToleranceSeconds)
			throw new InvalidOperationException(
				"The clip-step decision selected an invalid source window.");
		if (!Finite(speed) || speed < 0.25 || speed > 4)
			throw new InvalidOperationException(
				"The clip-step decision selected an invalid constant speed.");
		if (hasCurve && decision.SourceWindow.VelocityCurve!.Any(
			point => !Finite(point.OffsetSeconds) || !Finite(point.Speed)))
			throw new InvalidOperationException(
				"The clip-step decision selected an invalid velocity curve point.");
		if (!speedChangesEnabled &&
			(Math.Abs(speed - 1) > 0.000001 || hasCurve))
			throw new InvalidOperationException(
				"The clip-step decision changed speed while speed changes are disabled.");
	}

	private static SpeedProfile BuildSpeedProfile(AssemblySourceWindow window)
	{
		if ((window.VelocityCurve?.Count ?? 0) == 0)
			return new SpeedProfile(new[]
			{
				new SpeedProfilePoint(window.StartSeconds, window.ConstantSpeed),
				new SpeedProfilePoint(window.EndSeconds, window.ConstantSpeed)
			});
		return new SpeedProfile(window.VelocityCurve!.Select(point =>
			new SpeedProfilePoint(
				window.StartSeconds + point.OffsetSeconds,
				point.Speed)));
	}

	private static Dictionary<string, MontageSongPlanningEvent> EligibleEvents(
		EditPlanningRequest request) =>
		request.SongAnalysis.Events
			.Where(item => item.IsGameplayAnchor && !item.IsIntentionallyUnused)
			.ToDictionary(item => item.Id, StringComparer.Ordinal);

	private static ShotEvent ResolveKill(
		Core.Domain.Clip.Clip clip,
		AssemblySyncDecision? sync)
	{
		List<ShotEvent> kills = clip.ConfirmedKills
			.OrderBy(item => item.SourceConfirmationTimeSeconds)
			.ToList();
		if (sync == null || sync.KillIndex < 0 || sync.KillIndex >= kills.Count)
			throw new InvalidOperationException(
				"The clip-step decision referenced an invalid or ineligible kill.");
		return kills[sync.KillIndex];
	}

	private static MontageSongPlanningEvent ResolveEvent(
		AssemblySyncDecision? sync,
		IReadOnlyDictionary<string, MontageSongPlanningEvent> eligible,
		IReadOnlySet<string> alreadyUsed)
	{
		if (sync == null || string.IsNullOrWhiteSpace(sync.MusicEventId) ||
			!eligible.TryGetValue(sync.MusicEventId, out MontageSongPlanningEvent? musicEvent))
			throw new InvalidOperationException(
				"The clip-step decision referenced an ineligible musical event.");
		if (alreadyUsed.Contains(musicEvent.Id))
			throw new InvalidOperationException(
				"The clip-step decision reused a musical event: " + musicEvent.Id);
		return musicEvent;
	}

	private static void AddAssignment(
		AssemblySyncDecision sync,
		ClipPlacement placement,
		ShotEvent kill,
		MontageSongPlanningEvent musicEvent,
		HashSet<string> usedEvents,
		HashSet<int> assignedKills,
		List<MontageSyncAssignment> assignments,
		string kind)
	{
		if (!assignedKills.Add(sync.KillIndex))
			throw new InvalidOperationException(
				"The clip-step decision assigned the same kill more than once.");
		if (!usedEvents.Add(musicEvent.Id))
			throw new InvalidOperationException(
				"The clip-step decision reused a musical event: " + musicEvent.Id);
		if (!placement.SpeedProfile.TryGetTimelineTimeForSourceTime(
			kill.SourceConfirmationTimeSeconds, out double relative))
			throw new InvalidOperationException(
				kind + " synced kill lies outside the selected source window.");
		double actual = placement.TimelineStartSeconds + relative;
		if (Math.Abs(actual - musicEvent.EffectiveTimeSeconds) > ToleranceSeconds)
			throw new InvalidOperationException(
				kind + " sync is inconsistent with the primary placement: kill index " +
				sync.KillIndex + " at source " +
				kill.SourceConfirmationTimeSeconds.ToString(
					"0.######",
					System.Globalization.CultureInfo.InvariantCulture) +
				"s maps to timeline " +
				actual.ToString(
					"0.######",
					System.Globalization.CultureInfo.InvariantCulture) +
				"s, but music event '" + musicEvent.Id + "' is at " +
				musicEvent.EffectiveTimeSeconds.ToString(
					"0.######",
					System.Globalization.CultureInfo.InvariantCulture) +
				"s (delta " +
				(actual - musicEvent.EffectiveTimeSeconds).ToString(
					"+0.######;-0.######;0",
					System.Globalization.CultureInfo.InvariantCulture) +
				"s).");
		assignments.Add(new MontageSyncAssignment
		{
			ClipPath = placement.Clip.FilePath,
			KillIndex = sync.KillIndex,
			SourceConfirmationTimeSeconds = kill.SourceConfirmationTimeSeconds,
			MusicEventId = musicEvent.Id,
			TimelineTimeSeconds = musicEvent.EffectiveTimeSeconds
		});
	}

	private static EditPlanDocument BuildPlan(
		EditPlanningRequest request,
		List<ClipPlacement> placements,
		List<MontageSyncAssignment> assignments,
		string plannerId,
		string plannerVersion) => new()
	{
		RequestId = request.RequestId,
		PlannerId = string.IsNullOrWhiteSpace(plannerId) ? "unknown" : plannerId,
		PlannerVersion = string.IsNullOrWhiteSpace(plannerVersion) ? "unknown" : plannerVersion,
		StyleProfileIds = request.StyleProfileIds.ToList(),
		Montage = new PreparedMontage
		{
			Placements = placements,
			SongPlan = request.SongAnalysis,
			SyncAssignments = assignments,
			EffectOptions = request.EffectOptions,
			EffectTreatments = new EffectTreatmentPlan()
		}
	};

	private static void ValidateCombined(
		EditPlanningRequest request,
		EditPlanDocument plan)
	{
		if (plan?.Montage?.Placements == null)
			throw new InvalidOperationException("The combined candidate has no placements.");
		Dictionary<string, Core.Domain.Clip.Clip> clips = request.Clips
			.ToDictionary(item => item.FilePath, StringComparer.OrdinalIgnoreCase);
		foreach (ClipPlacement placement in plan.Montage.Placements)
		{
			if (placement.Clip == null ||
				!clips.TryGetValue(
					placement.Clip.FilePath,
					out Core.Domain.Clip.Clip? canonical))
				throw new InvalidOperationException(
					"The combined candidate contains unavailable media.");
			// Accepted checkpoints are persisted and later deserialized. Rebind those
			// equivalent DTOs to the authoritative request objects before validation.
			placement.Clip = canonical;
		}
		plan.Montage.SongPlan = request.SongAnalysis;
		EditPlanDocumentValidator.ValidateAndNormalize(plan);
		Dictionary<string, MontageSongPlanningEvent> events = EligibleEvents(request);
		HashSet<string> usedClips = new(StringComparer.OrdinalIgnoreCase);
		HashSet<string> usedEvents = new(StringComparer.Ordinal);
		foreach (ClipPlacement placement in plan.Montage.Placements)
		{
			if (!usedClips.Add(placement.Clip.FilePath))
				throw new InvalidOperationException("The combined candidate reuses a clip.");
			if (placement.TimelineEndSeconds >
				request.SongAnalysis.SongDurationSeconds + ToleranceSeconds)
				throw new InvalidOperationException(
					"The combined candidate extends beyond the song.");
		}
		foreach (MontageSyncAssignment assignment in plan.Montage.SyncAssignments)
		{
			if (!usedEvents.Add(assignment.MusicEventId) ||
				!events.TryGetValue(assignment.MusicEventId, out MontageSongPlanningEvent? musicEvent))
				throw new InvalidOperationException(
					"The combined candidate has an ineligible or reused musical event.");
			ClipPlacement placement = plan.Montage.Placements.SingleOrDefault(item =>
				string.Equals(item.Clip.FilePath, assignment.ClipPath,
					StringComparison.OrdinalIgnoreCase)) ??
				throw new InvalidOperationException(
					"A sync assignment has no matching placement.");
			ShotEvent kill = ResolveKill(placement.Clip, new AssemblySyncDecision
			{
				KillIndex = assignment.KillIndex,
				MusicEventId = assignment.MusicEventId
			});
			if (Math.Abs(kill.SourceConfirmationTimeSeconds -
				assignment.SourceConfirmationTimeSeconds) > ToleranceSeconds ||
				!placement.SpeedProfile.TryGetTimelineTimeForSourceTime(
					kill.SourceConfirmationTimeSeconds, out double relative) ||
				Math.Abs(placement.TimelineStartSeconds + relative -
					musicEvent.EffectiveTimeSeconds) > ToleranceSeconds ||
				Math.Abs(assignment.TimelineTimeSeconds -
					musicEvent.EffectiveTimeSeconds) > ToleranceSeconds)
				throw new InvalidOperationException(
					"The combined candidate contains an inconsistent sync assignment.");
		}
	}

	private static bool Finite(double value) =>
		!double.IsNaN(value) && !double.IsInfinity(value);
}
