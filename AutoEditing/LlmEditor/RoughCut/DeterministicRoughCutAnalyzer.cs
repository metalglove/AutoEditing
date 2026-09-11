using System.Security.Cryptography;
using System.Text;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Serialization;
using Core.Domain.Audio.SongAnalysis;
using Core.Domain.Editing;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.RoughCut;

internal sealed class DeterministicRoughCutAnalyzer
{
	private const double GapToleranceSeconds = 0.05;
	private const double WeakJoinActionDistanceSeconds = 1.5;
	private const double AbruptDurationRatio = 2.75;

	public RoughCutAuditReport Analyze(
		RoughCutAuditInput input,
		DateTimeOffset createdUtc)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentException.ThrowIfNullOrWhiteSpace(input.SessionId);
		EditPlanningRequestValidator.ValidateAndNormalize(input.PlanningRequest);
		EditPlanDocumentValidator.ValidateAndNormalize(input.AcceptedSyncPlan);
		ProgressiveAssemblyContractValidator.Validate(input.Sketch);
		if (!string.Equals(
			input.PlanningRequest.RequestId,
			input.AcceptedSyncPlan.RequestId,
			StringComparison.Ordinal) ||
			!string.Equals(
				input.PlanningRequest.RequestId,
				input.Sketch.RequestId,
				StringComparison.Ordinal))
			throw new InvalidOperationException(
				"The request, accepted plan, and assembly sketch do not share one identity.");
		if (input.Timeline == null || input.Timeline.TimelineEnd <= input.Timeline.TimelineStart)
			throw new InvalidOperationException(
				"A complete candidate timeline snapshot is required for rough-cut audit.");

		List<ClipPlacement> placements = input.AcceptedSyncPlan.Montage.Placements
			.OrderBy(item => item.TimelineStartSeconds)
			.ToList();
		if (placements.Count == 0)
			throw new InvalidOperationException("The accepted synchronization plan is empty.");
		if (input.Evidence == null ||
			!input.Evidence.Any(item => item.Kind == RoughCutEvidenceKind.FullRender))
			throw new InvalidOperationException(
				"A complete synchronization-pass render is required before rough-cut audit.");

		string planJson = EditPlanDocumentSerializer.SerializePlan(input.AcceptedSyncPlan);
		string planHash = Sha256(planJson);
		RoughCutAuditMetrics metrics = CalculateMetrics(input, placements);
		List<RoughCutAuditFinding> findings = new();
		List<RoughCutCorrectionProposal> corrections = new();
		string fullRenderId = input.Evidence.First(item =>
			item.Kind == RoughCutEvidenceKind.FullRender).EvidenceId;

		AddGapFindings(metrics, fullRenderId, findings, corrections);
		AddJoinFindings(input.AcceptedSyncPlan, placements, metrics, fullRenderId,
			findings, corrections);
		AddRepetitionFindings(placements, fullRenderId, findings, corrections);
		AddMusicCoverageFinding(metrics, placements, fullRenderId, findings, corrections);
		AddSectionDensityFindings(metrics, placements, fullRenderId, findings, corrections);
		AddReservationFindings(input, placements, metrics, fullRenderId, findings, corrections);

		string reportId = "rough-cut-" + Sha256(
			input.SessionId + "\n" + planHash + "\n" +
			string.Join("\n", input.Evidence.Select(item => item.Sha256)))[..16];
		RoughCutAuditReport report = new()
		{
			SessionId = input.SessionId,
			ReportId = reportId,
			CreatedUtc = createdUtc,
			RequestId = input.PlanningRequest.RequestId,
			PlanSha256 = planHash,
			Summary = findings.Count == 0
				? "Deterministic rough-cut checks found no actionable timing, repetition, music-coverage, or reservation issue."
				: $"Deterministic rough-cut checks found {findings.Count} actionable issue(s).",
			ModelStatus = "not-run",
			Metrics = metrics,
			Evidence = input.Evidence.ToList(),
			Findings = findings,
			Corrections = corrections
		};
		RoughCutAuditContractValidator.Validate(report);
		return report;
	}

	private static RoughCutAuditMetrics CalculateMetrics(
		RoughCutAuditInput input,
		IReadOnlyList<ClipPlacement> placements)
	{
		double[] durations = placements.Select(item => item.LengthSeconds)
			.OrderBy(value => value).ToArray();
		double timelineStart = placements[0].TimelineStartSeconds;
		double timelineEnd = placements[^1].TimelineEndSeconds;
		RoughCutAuditMetrics result = new()
		{
			TimelineStartSeconds = timelineStart,
			TimelineEndSeconds = timelineEnd,
			MontageDurationSeconds = timelineEnd - timelineStart,
			PlacementCount = placements.Count,
			MinimumPlacementDurationSeconds = durations[0],
			MedianPlacementDurationSeconds = Median(durations),
			MaximumPlacementDurationSeconds = durations[^1],
			AveragePlacementDurationSeconds = durations.Average()
		};

		for (int index = 1; index < placements.Count; index++)
		{
			ClipPlacement before = placements[index - 1];
			ClipPlacement after = placements[index];
			double gap = after.TimelineStartSeconds - before.TimelineEndSeconds;
			if (gap > GapToleranceSeconds)
			{
				result.Gaps.Add(new RoughCutGapMetric
				{
					BeforeCheckpoint = index,
					AfterCheckpoint = index + 1,
					StartSeconds = before.TimelineEndSeconds,
					EndSeconds = after.TimelineStartSeconds,
					DurationSeconds = gap
				});
				result.TotalGapDurationSeconds += gap;
			}
			double minimum = Math.Min(before.LengthSeconds, after.LengthSeconds);
			double maximum = Math.Max(before.LengthSeconds, after.LengthSeconds);
			result.Joins.Add(new RoughCutJoinMetric
			{
				BeforeCheckpoint = index,
				AfterCheckpoint = index + 1,
				JoinTimeSeconds = after.TimelineStartSeconds,
				BeforeDurationSeconds = before.LengthSeconds,
				AfterDurationSeconds = after.LengthSeconds,
				DurationRatio = maximum / minimum,
				RepeatsMap = Same(before.Clip.Map, after.Clip.Map),
				RepeatsWeapon = SharesWeapon(before, after)
			});
		}

		HashSet<string> assignedEventIds = input.AcceptedSyncPlan.Montage.SyncAssignments
			.Select(item => item.MusicEventId)
			.ToHashSet(StringComparer.Ordinal);
		foreach (MontageSongPlanningRegion region in input.PlanningRequest.SongAnalysis.Regions
			.OrderBy(item => item.StartSeconds))
		{
			double regionDuration = region.EndSeconds - region.StartSeconds;
			double covered = placements.Sum(placement => Intersection(
				placement.TimelineStartSeconds,
				placement.TimelineEndSeconds,
				region.StartSeconds,
				region.EndSeconds));
			List<MontageSongPlanningEvent> major = input.PlanningRequest.SongAnalysis.Events
				.Where(item => IsMajorEligibleEvent(item) &&
					item.EffectiveTimeSeconds >= region.StartSeconds &&
					item.EffectiveTimeSeconds < region.EndSeconds)
				.ToList();
			result.Sections.Add(new RoughCutSectionMetric
			{
				RegionId = region.Id,
				StartSeconds = region.StartSeconds,
				EndSeconds = region.EndSeconds,
				PlacementCoverageRatio = Math.Min(1, covered / regionDuration),
				PlacementCount = placements.Count(placement =>
					Intersection(placement.TimelineStartSeconds, placement.TimelineEndSeconds,
						region.StartSeconds, region.EndSeconds) > 0),
				SyncCount = input.AcceptedSyncPlan.Montage.SyncAssignments.Count(item =>
					item.TimelineTimeSeconds >= region.StartSeconds &&
					item.TimelineTimeSeconds < region.EndSeconds),
				EligibleMajorEventCount = major.Count,
				UsedMajorEventCount = major.Count(item => assignedEventIds.Contains(item.Id))
			});
		}
		result.UnusedMajorMusicEventIds = input.PlanningRequest.SongAnalysis.Events
			.Where(item => IsMajorEligibleEvent(item) && !assignedEventIds.Contains(item.Id))
			.OrderBy(item => item.EffectiveTimeSeconds)
			.Select(item => item.Id)
			.ToList();

		foreach (AssemblyReservation reservation in input.Sketch.Reservations)
		{
			MontageSongPlanningRegion? region = input.PlanningRequest.SongAnalysis.Regions
				.SingleOrDefault(item => string.Equals(
					item.Id, reservation.RegionId, StringComparison.Ordinal));
			if (region == null) continue;
			bool fulfilled = placements.Any(placement =>
				reservation.PreferredClipReferenceIds.Contains(
					AssemblyReferenceIds.ForClipPath(placement.Clip.FilePath),
					StringComparer.Ordinal) &&
				Intersection(placement.TimelineStartSeconds, placement.TimelineEndSeconds,
					region.StartSeconds, region.EndSeconds) > 0);
			if (!fulfilled) result.UnfulfilledReservationIds.Add(reservation.ReservationId);
		}
		return result;
	}

	private static void AddGapFindings(
		RoughCutAuditMetrics metrics,
		string evidenceId,
		ICollection<RoughCutAuditFinding> findings,
		ICollection<RoughCutCorrectionProposal> corrections)
	{
		foreach (RoughCutGapMetric gap in metrics.Gaps)
		{
			string id = $"det-gap-{gap.BeforeCheckpoint:D4}-{gap.AfterCheckpoint:D4}";
			findings.Add(new RoughCutAuditFinding
			{
				FindingId = id,
				Category = RoughCutAuditCategory.Gap,
				Severity = gap.DurationSeconds >= 0.5
					? RoughCutFindingSeverity.Error
					: RoughCutFindingSeverity.Warning,
				Source = RoughCutFindingSource.Deterministic,
				Summary = $"There is {gap.DurationSeconds:0.###} s of uncovered timeline between clips.",
				Details = "The accepted synchronization placements do not form a continuous rough cut at this join.",
				StartSeconds = gap.StartSeconds,
				EndSeconds = gap.EndSeconds,
				AffectedCheckpoints = new[] { gap.BeforeCheckpoint, gap.AfterCheckpoint },
				EvidenceIds = new[] { evidenceId },
				Confidence = 1
			});
			corrections.Add(Correction(
				"fix-" + id,
				id,
				new[] { gap.BeforeCheckpoint, gap.AfterCheckpoint },
				RoughCutCorrectionOperation.Move,
				"Reopen this join and remove the uncovered timeline while preserving the strongest nearby synchronization anchor.",
				"The two placements meet without accidental dead space.",
				"Changing either trim can invalidate a nearby synchronization assignment.",
				1));
		}
	}

	private static void AddJoinFindings(
		EditPlanDocument plan,
		IReadOnlyList<ClipPlacement> placements,
		RoughCutAuditMetrics metrics,
		string evidenceId,
		ICollection<RoughCutAuditFinding> findings,
		ICollection<RoughCutCorrectionProposal> corrections)
	{
		foreach (RoughCutJoinMetric join in metrics.Joins)
		{
			if (join.DurationRatio >= AbruptDurationRatio)
			{
				string id = $"det-pacing-{join.BeforeCheckpoint:D4}-{join.AfterCheckpoint:D4}";
				findings.Add(new RoughCutAuditFinding
				{
					FindingId = id,
					Category = RoughCutAuditCategory.Pacing,
					Severity = RoughCutFindingSeverity.Warning,
					Source = RoughCutFindingSource.Deterministic,
					Summary = $"Adjacent clip durations change by {join.DurationRatio:0.##}x.",
					Details = "This is a deterministic pacing discontinuity signal, not an automatic judgment that the creative choice is wrong.",
					StartSeconds = Math.Max(0, join.JoinTimeSeconds - 0.5),
					EndSeconds = join.JoinTimeSeconds + 0.5,
					AffectedCheckpoints =
						new[] { join.BeforeCheckpoint, join.AfterCheckpoint },
					EvidenceIds = new[] { evidenceId },
					Confidence = 0.75
				});
				corrections.Add(Correction(
					"fix-" + id,
					id,
					new[] { join.BeforeCheckpoint, join.AfterCheckpoint },
					RoughCutCorrectionOperation.Duration,
					"Review the abrupt duration change at this join and rebalance one of the two source windows only if the rendered pacing feels unintended.",
					"The duration change reads as a deliberate pacing transition.",
					"Lengthening a placement may weaken action density; shortening it may remove readable setup.",
					0.7));
			}

			ClipPlacement before = placements[join.BeforeCheckpoint - 1];
			ClipPlacement after = placements[join.AfterCheckpoint - 1];
			double beforeActionDistance = before.TimelineKillTimesSeconds.Count == 0
				? before.LengthSeconds
				: before.TimelineEndSeconds - before.TimelineKillTimesSeconds.Max();
			double afterActionDistance = after.TimelineKillTimesSeconds.Count == 0
				? after.LengthSeconds
				: after.TimelineKillTimesSeconds.Min() - after.TimelineStartSeconds;
			if (beforeActionDistance > WeakJoinActionDistanceSeconds &&
				afterActionDistance > WeakJoinActionDistanceSeconds)
			{
				string id = $"det-continuity-{join.BeforeCheckpoint:D4}-{join.AfterCheckpoint:D4}";
				findings.Add(new RoughCutAuditFinding
				{
					FindingId = id,
					Category = RoughCutAuditCategory.Continuity,
					Severity = RoughCutFindingSeverity.Warning,
					Source = RoughCutFindingSource.Deterministic,
					Summary = "The join has low action proximity on both sides.",
					Details = $"The prior confirmed action is {beforeActionDistance:0.##} s before the cut and the next is {afterActionDistance:0.##} s after it.",
					StartSeconds = Math.Max(0, join.JoinTimeSeconds -
						Math.Min(beforeActionDistance, 2)),
					EndSeconds = join.JoinTimeSeconds + Math.Min(afterActionDistance, 2),
					AffectedCheckpoints =
						new[] { join.BeforeCheckpoint, join.AfterCheckpoint },
					EvidenceIds = new[] { evidenceId },
					Confidence = 0.85
				});
				corrections.Add(Correction(
					"fix-" + id,
					id,
					new[] { join.BeforeCheckpoint, join.AfterCheckpoint },
					RoughCutCorrectionOperation.Trim,
					"Reopen the join and tighten inactive tails or lead-in while keeping each retained confirmed action readable.",
					"The cut connects two readable action moments without an unintended lull.",
					"A tighter trim can remove spatial setup or confirmation readability.",
					0.8));
			}
		}
	}

	private static void AddRepetitionFindings(
		IReadOnlyList<ClipPlacement> placements,
		string evidenceId,
		ICollection<RoughCutAuditFinding> findings,
		ICollection<RoughCutCorrectionProposal> corrections)
	{
		AddAdjacentRepetition(
			placements,
			item => item.Clip.Map,
			"map",
			evidenceId,
			findings,
			corrections);
		AddAdjacentRepetition(
			placements,
			item => item.Clip.ClipType,
			"visual situation",
			evidenceId,
			findings,
			corrections);

		for (int start = 0; start < placements.Count;)
		{
			int end = start + 1;
			while (end < placements.Count && SharesWeapon(placements[start], placements[end]))
				end++;
			if (end - start >= 3)
			{
				int[] checkpoints = Enumerable.Range(start + 1, end - start).ToArray();
				string id = $"det-repeat-weapon-{start + 1:D4}-{end:D4}";
				findings.Add(new RoughCutAuditFinding
				{
					FindingId = id,
					Category = RoughCutAuditCategory.Repetition,
					Severity = RoughCutFindingSeverity.Warning,
					Source = RoughCutFindingSource.Deterministic,
					Summary = $"{checkpoints.Length} consecutive placements repeat a weapon.",
					Details = "A long same-weapon run may be intentional, but should be verified against the rendered visual progression.",
					StartSeconds = placements[start].TimelineStartSeconds,
					EndSeconds = placements[end - 1].TimelineEndSeconds,
					AffectedCheckpoints = checkpoints,
					EvidenceIds = new[] { evidenceId },
					Confidence = 0.8
				});
			}
			start = end;
		}
	}

	private static void AddAdjacentRepetition(
		IReadOnlyList<ClipPlacement> placements,
		Func<ClipPlacement, string> selector,
		string label,
		string evidenceId,
		ICollection<RoughCutAuditFinding> findings,
		ICollection<RoughCutCorrectionProposal> corrections)
	{
		for (int index = 1; index < placements.Count; index++)
		{
			string before = selector(placements[index - 1]) ?? "";
			string after = selector(placements[index]) ?? "";
			if (string.IsNullOrWhiteSpace(before) ||
				!string.Equals(before, after, StringComparison.OrdinalIgnoreCase))
				continue;
			string slug = label.Replace(' ', '-');
			string id = $"det-repeat-{slug}-{index:D4}-{index + 1:D4}";
			findings.Add(new RoughCutAuditFinding
			{
				FindingId = id,
				Category = RoughCutAuditCategory.Repetition,
				Severity = RoughCutFindingSeverity.Warning,
				Source = RoughCutFindingSource.Deterministic,
				Summary = $"Adjacent placements repeat the same {label}: {before}.",
				Details = "The repetition is surfaced for rendered review; it is not automatically reordered.",
				StartSeconds = placements[index - 1].TimelineStartSeconds,
				EndSeconds = placements[index].TimelineEndSeconds,
				AffectedCheckpoints = new[] { index, index + 1 },
				EvidenceIds = new[] { evidenceId },
				Confidence = 0.9
			});
		}
	}

	private static void AddMusicCoverageFinding(
		RoughCutAuditMetrics metrics,
		IReadOnlyList<ClipPlacement> placements,
		string evidenceId,
		ICollection<RoughCutAuditFinding> findings,
		ICollection<RoughCutCorrectionProposal> corrections)
	{
		if (metrics.UnusedMajorMusicEventIds.Count == 0) return;
		int[] target = metrics.Sections
			.Where(section => section.EligibleMajorEventCount > section.UsedMajorEventCount)
			.SelectMany(section => placements.Select((placement, index) => new
			{
				placement,
				checkpoint = index + 1
			}).Where(item => Intersection(
				item.placement.TimelineStartSeconds,
				item.placement.TimelineEndSeconds,
				section.StartSeconds,
				section.EndSeconds) > 0).Select(item => item.checkpoint))
			.Distinct()
			.OrderBy(value => value)
			.ToArray();
		if (target.Length == 0) target = new[] { placements.Count };
		string id = "det-unused-major-events";
		findings.Add(new RoughCutAuditFinding
		{
			FindingId = id,
			Category = RoughCutAuditCategory.MusicEventCoverage,
			Severity = RoughCutFindingSeverity.Warning,
			Source = RoughCutFindingSource.Deterministic,
			Summary = $"{metrics.UnusedMajorMusicEventIds.Count} reviewed major musical event(s) are unused.",
			Details = "Unused IDs: " + string.Join(", ", metrics.UnusedMajorMusicEventIds),
			AffectedCheckpoints = target,
			EvidenceIds = new[] { evidenceId },
			Confidence = 1
		});
		corrections.Add(Correction(
			"fix-" + id,
			id,
			target,
			RoughCutCorrectionOperation.Move,
			"Review these checkpoints against the listed unused major musical events and reopen only the smallest checkpoint set needed to use a stronger anchor.",
			"Major musical events are either used deliberately or explicitly left unused after review.",
			"Moving a confirmed action to another anchor can weaken source timing or create overlap.",
			0.85));
	}

	private static void AddReservationFindings(
		RoughCutAuditInput input,
		IReadOnlyList<ClipPlacement> placements,
		RoughCutAuditMetrics metrics,
		string evidenceId,
		ICollection<RoughCutAuditFinding> findings,
		ICollection<RoughCutCorrectionProposal> corrections)
	{
		foreach (string reservationId in metrics.UnfulfilledReservationIds)
		{
			AssemblyReservation reservation = input.Sketch.Reservations.Single(item =>
				string.Equals(item.ReservationId, reservationId, StringComparison.Ordinal));
			int[] target = placements.Select((placement, index) => new
				{
					referenceId = AssemblyReferenceIds.ForClipPath(placement.Clip.FilePath),
					checkpoint = index + 1
				})
				.Where(item => reservation.PreferredClipReferenceIds.Contains(
					item.referenceId, StringComparer.Ordinal))
				.Select(item => item.checkpoint)
				.Distinct()
				.OrderBy(value => value)
				.ToArray();
			if (target.Length == 0) target = new[] { placements.Count };
			string id = "det-reservation-" + reservationId;
			findings.Add(new RoughCutAuditFinding
			{
				FindingId = id,
				Category = RoughCutAuditCategory.ReservationFulfillment,
				Severity = RoughCutFindingSeverity.Warning,
				Source = RoughCutFindingSource.Deterministic,
				Summary = $"Sketch reservation '{reservationId}' was not fulfilled in its intended region.",
				Details = reservation.Purpose,
				AffectedCheckpoints = target,
				EvidenceIds = new[] { evidenceId },
				Confidence = 1
			});
			corrections.Add(Correction(
				"fix-" + id,
				id,
				target,
				RoughCutCorrectionOperation.Move,
				$"Review the placement of the clips reserved for '{reservation.Purpose}' and reopen only if moving one into region '{reservation.RegionId}' improves the rendered arc.",
				"The reservation is fulfilled or explicitly rejected as no longer appropriate.",
				"Moving reserved material can weaken another section or invalidate accepted syncs.",
				0.8));
		}
	}

	private static void AddSectionDensityFindings(
		RoughCutAuditMetrics metrics,
		IReadOnlyList<ClipPlacement> placements,
		string evidenceId,
		ICollection<RoughCutAuditFinding> findings,
		ICollection<RoughCutCorrectionProposal> corrections)
	{
		foreach (RoughCutSectionMetric section in metrics.Sections)
		{
			double duration = section.EndSeconds - section.StartSeconds;
			bool sparse = section.PlacementCoverageRatio < 0.5 ||
				(section.EligibleMajorEventCount >= 2 &&
					section.UsedMajorEventCount /
						(double)section.EligibleMajorEventCount < 0.35);
			bool dense = section.SyncCount / duration > 1.25;
			if (!sparse && !dense) continue;
			int[] target = placements
				.Select((placement, index) => new
				{
					placement,
					checkpoint = index + 1
				})
				.Where(item => Intersection(
					item.placement.TimelineStartSeconds,
					item.placement.TimelineEndSeconds,
					section.StartSeconds,
					section.EndSeconds) > 0)
				.Select(item => item.checkpoint)
				.ToArray();
			if (target.Length == 0)
			{
				int nearest = placements
					.Select((placement, index) => new
					{
						checkpoint = index + 1,
						distance = Math.Abs(
							placement.TimelineStartSeconds - section.StartSeconds)
					})
					.OrderBy(item => item.distance)
					.First().checkpoint;
				target = new[] { nearest };
			}
			string density = sparse ? "sparse" : "dense";
			string id = $"det-section-{density}-{SafeId(section.RegionId)}";
			findings.Add(new RoughCutAuditFinding
			{
				FindingId = id,
				Category = RoughCutAuditCategory.Pacing,
				Severity = RoughCutFindingSeverity.Warning,
				Source = RoughCutFindingSource.Deterministic,
				Summary = $"Song region '{section.RegionId}' is deterministically {density}.",
				Details = sparse
					? $"Placement coverage is {section.PlacementCoverageRatio:P0}; {section.UsedMajorEventCount} of {section.EligibleMajorEventCount} major anchors are used."
					: $"{section.SyncCount} sync assignments occur across {duration:0.##} seconds.",
				StartSeconds = section.StartSeconds,
				EndSeconds = section.EndSeconds,
				AffectedCheckpoints = target,
				EvidenceIds = new[] { evidenceId },
				Confidence = 0.9
			});
			corrections.Add(Correction(
				"fix-" + id,
				id,
				target,
				sparse
					? RoughCutCorrectionOperation.Duration
					: RoughCutCorrectionOperation.Trim,
				sparse
					? "Review this sparse song region and reopen the smallest checkpoint set needed to cover deliberate musical structure without inventing filler."
					: "Review this dense song region and reduce synchronization density only where the rendered action becomes unreadable.",
				sparse
					? "The region is intentionally spacious or has enough readable montage coverage."
					: "The region preserves energy without sacrificing action readability.",
				"Changing density may weaken the intended pacing arc or displace strong anchors.",
				0.75));
		}
	}

	private static RoughCutCorrectionProposal Correction(
		string id,
		string findingId,
		IList<int> checkpoints,
		RoughCutCorrectionOperation operation,
		string instruction,
		string outcome,
		string risk,
		double confidence) =>
		new()
		{
			CorrectionId = id,
			FindingIds = new[] { findingId },
			TargetCheckpoints = checkpoints,
			Operation = operation,
			Instruction = instruction,
			ExpectedOutcome = outcome,
			Risk = risk,
			Confidence = confidence
		};

	private static bool IsMajorEligibleEvent(MontageSongPlanningEvent item) =>
		item.IsReviewed &&
		item.IsGameplayAnchor &&
		!item.IsIntentionallyUnused &&
		(item.Priority >= 2 ||
			item.MusicalType is MusicEventType.Drop or MusicEventType.BuildHit or
				MusicEventType.PhraseBoundary or MusicEventType.ManualSyncPoint);

	private static bool SharesWeapon(ClipPlacement before, ClipPlacement after)
	{
		HashSet<string> weapons = before.Clip.GunsUsed.ToHashSet(StringComparer.OrdinalIgnoreCase);
		if (weapons.Count == 0 && !string.IsNullOrWhiteSpace(before.Clip.Gun))
			weapons.Add(before.Clip.Gun);
		IEnumerable<string> afterWeapons = after.Clip.GunsUsed.Count > 0
			? after.Clip.GunsUsed
			: new[] { after.Clip.Gun };
		return afterWeapons.Any(item => !string.IsNullOrWhiteSpace(item) && weapons.Contains(item));
	}

	private static bool Same(string? first, string? second) =>
		!string.IsNullOrWhiteSpace(first) &&
		string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

	private static double Intersection(
		double firstStart,
		double firstEnd,
		double secondStart,
		double secondEnd) =>
		Math.Max(0, Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart));

	private static double Median(double[] ordered) =>
		ordered.Length % 2 == 1
			? ordered[ordered.Length / 2]
			: (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2;

	private static string Sha256(string value) =>
		Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
			.ToLowerInvariant();

	private static string SafeId(string value) =>
		new(value.Select(character =>
			char.IsLetterOrDigit(character) ? character : '-').ToArray());
}
