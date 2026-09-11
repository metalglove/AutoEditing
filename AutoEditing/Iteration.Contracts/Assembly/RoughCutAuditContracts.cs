using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AutoEditing.Iteration.Contracts.Assembly;

public enum RoughCutAuditCategory
{
	Pacing,
	Continuity,
	Repetition,
	Gap,
	MusicEventCoverage,
	ReservationFulfillment
}

public enum RoughCutFindingSeverity
{
	Information,
	Warning,
	Error
}

public enum RoughCutFindingSource
{
	Deterministic,
	MultimodalModel
}

public enum RoughCutEvidenceKind
{
	FullRender,
	ContactSheet,
	SnapshotFrame,
	TimingVisualization,
	TimelineSnapshot
}

public enum RoughCutCorrectionDisposition
{
	ApprovedForReopen,
	Applied,
	Rejected,
	Deferred
}

public enum RoughCutCorrectionOperation
{
	Unknown,
	Move,
	Trim,
	Duration,
	ConstantSpeed
}

public sealed class RoughCutEvidenceReference
{
	public string EvidenceId { get; set; } = "";
	public RoughCutEvidenceKind Kind { get; set; }
	public string RelativePath { get; set; } = "";
	public string Sha256 { get; set; } = "";
	public string MediaType { get; set; } = "";
	public string Description { get; set; } = "";
	public double? TimelineTimeSeconds { get; set; }
}

public sealed class RoughCutGapMetric
{
	public int BeforeCheckpoint { get; set; }
	public int AfterCheckpoint { get; set; }
	public double StartSeconds { get; set; }
	public double EndSeconds { get; set; }
	public double DurationSeconds { get; set; }
}

public sealed class RoughCutJoinMetric
{
	public int BeforeCheckpoint { get; set; }
	public int AfterCheckpoint { get; set; }
	public double JoinTimeSeconds { get; set; }
	public double BeforeDurationSeconds { get; set; }
	public double AfterDurationSeconds { get; set; }
	public double DurationRatio { get; set; }
	public bool RepeatsMap { get; set; }
	public bool RepeatsWeapon { get; set; }
}

public sealed class RoughCutSectionMetric
{
	public string RegionId { get; set; } = "";
	public double StartSeconds { get; set; }
	public double EndSeconds { get; set; }
	public double PlacementCoverageRatio { get; set; }
	public int PlacementCount { get; set; }
	public int SyncCount { get; set; }
	public int EligibleMajorEventCount { get; set; }
	public int UsedMajorEventCount { get; set; }
}

public sealed class RoughCutAuditMetrics
{
	public double TimelineStartSeconds { get; set; }
	public double TimelineEndSeconds { get; set; }
	public double MontageDurationSeconds { get; set; }
	public int PlacementCount { get; set; }
	public double MinimumPlacementDurationSeconds { get; set; }
	public double MedianPlacementDurationSeconds { get; set; }
	public double MaximumPlacementDurationSeconds { get; set; }
	public double AveragePlacementDurationSeconds { get; set; }
	public double TotalGapDurationSeconds { get; set; }
	public IList<RoughCutGapMetric> Gaps { get; set; } = new List<RoughCutGapMetric>();
	public IList<RoughCutJoinMetric> Joins { get; set; } = new List<RoughCutJoinMetric>();
	public IList<RoughCutSectionMetric> Sections { get; set; } =
		new List<RoughCutSectionMetric>();
	public IList<string> UnusedMajorMusicEventIds { get; set; } = new List<string>();
	public IList<string> UnfulfilledReservationIds { get; set; } = new List<string>();
}

public sealed class RoughCutAuditFinding
{
	public string FindingId { get; set; } = "";
	public RoughCutAuditCategory Category { get; set; }
	public RoughCutFindingSeverity Severity { get; set; }
	public RoughCutFindingSource Source { get; set; }
	public string Summary { get; set; } = "";
	public string Details { get; set; } = "";
	public double? StartSeconds { get; set; }
	public double? EndSeconds { get; set; }
	public IList<int> AffectedCheckpoints { get; set; } = new List<int>();
	public IList<string> EvidenceIds { get; set; } = new List<string>();
	public double Confidence { get; set; }
}

public sealed class RoughCutCorrectionProposal
{
	public string CorrectionId { get; set; } = "";
	public IList<string> FindingIds { get; set; } = new List<string>();
	public IList<int> TargetCheckpoints { get; set; } = new List<int>();
	public RoughCutCorrectionOperation Operation { get; set; }
	public string Instruction { get; set; } = "";
	public string ExpectedOutcome { get; set; } = "";
	public string Risk { get; set; } = "";
	public double Confidence { get; set; }
}

public sealed class RoughCutAuditReport
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public string ReportId { get; set; } = "";
	public DateTimeOffset CreatedUtc { get; set; }
	public string RequestId { get; set; } = "";
	public string PlanSha256 { get; set; } = "";
	public string Summary { get; set; } = "";
	public string ModelId { get; set; } = "";
	public string ModelStatus { get; set; } = "";
	public RoughCutAuditMetrics Metrics { get; set; } = new();
	public IList<RoughCutEvidenceReference> Evidence { get; set; } =
		new List<RoughCutEvidenceReference>();
	public IList<RoughCutAuditFinding> Findings { get; set; } =
		new List<RoughCutAuditFinding>();
	public IList<RoughCutCorrectionProposal> Corrections { get; set; } =
		new List<RoughCutCorrectionProposal>();
	public IList<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class RoughCutCorrectionDecision
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public string ReportId { get; set; } = "";
	public string CorrectionId { get; set; } = "";
	public RoughCutCorrectionDisposition Disposition { get; set; }
	public string Note { get; set; } = "";
	public DateTimeOffset DecidedUtc { get; set; }
}

public sealed class RoughCutCheckpointReopenRequest
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string RequestId { get; set; } = "";
	public string SessionId { get; set; } = "";
	public string ReportId { get; set; } = "";
	public string ReportSha256 { get; set; } = "";
	public string CorrectionId { get; set; } = "";
	public IList<int> TargetCheckpoints { get; set; } = new List<int>();
	public string ScopedInstruction { get; set; } = "";
	public DateTimeOffset CreatedUtc { get; set; }
}

public sealed class AcceptedRoughCutMilestone
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public string ReportId { get; set; } = "";
	public string ReportSha256 { get; set; } = "";
	public string PlanSha256 { get; set; } = "";
	public string FullRenderSha256 { get; set; } = "";
	public DateTimeOffset AcceptedUtc { get; set; }
	public string AcceptedBy { get; set; } = "";
	public IList<RoughCutCorrectionDecision> CorrectionDecisions { get; set; } =
		new List<RoughCutCorrectionDecision>();
}

public static class RoughCutAuditContractValidator
{
	public static void Validate(RoughCutAuditReport report)
	{
		if (report == null) throw new ArgumentNullException(nameof(report));
		Schema(report.SchemaVersion, RoughCutAuditReport.CurrentSchemaVersion, "rough-cut report");
		Text(report.SessionId, "Report session ID");
		Text(report.ReportId, "Report ID");
		Text(report.RequestId, "Report request ID");
		Hash(report.PlanSha256, "Report plan hash");
		Text(report.Summary, "Report summary");
		Text(report.ModelStatus, "Report model status");
		Validate(report.Metrics);

		HashSet<string> evidenceIds = new(StringComparer.Ordinal);
		int fullRenderCount = 0;
		foreach (RoughCutEvidenceReference evidence in report.Evidence ??
			throw new InvalidDataException("Report evidence is required."))
		{
			Text(evidence.EvidenceId, "Evidence ID");
			if (!evidenceIds.Add(evidence.EvidenceId))
				throw new InvalidDataException("Rough-cut evidence IDs must be unique.");
			RelativePath(evidence.RelativePath, "Evidence path");
			Hash(evidence.Sha256, "Evidence hash");
			Text(evidence.MediaType, "Evidence media type");
			Text(evidence.Description, "Evidence description");
			if (evidence.TimelineTimeSeconds.HasValue &&
				(double.IsNaN(evidence.TimelineTimeSeconds.Value) ||
					double.IsInfinity(evidence.TimelineTimeSeconds.Value) ||
					evidence.TimelineTimeSeconds.Value < 0))
				throw new InvalidDataException(
					"Evidence timeline time must be a finite non-negative value.");
			if (evidence.Kind == RoughCutEvidenceKind.FullRender)
				fullRenderCount++;
		}
		if (fullRenderCount != 1)
			throw new InvalidDataException(
				"A rough-cut audit requires exactly one full-render manifest.");

		HashSet<string> findingIds = new(StringComparer.Ordinal);
		foreach (RoughCutAuditFinding finding in report.Findings ??
			throw new InvalidDataException("Report findings are required."))
		{
			Text(finding.FindingId, "Finding ID");
			if (!findingIds.Add(finding.FindingId))
				throw new InvalidDataException("Rough-cut finding IDs must be unique.");
			Text(finding.Summary, "Finding summary");
			Text(finding.Details, "Finding details");
			TimeRange(finding.StartSeconds, finding.EndSeconds);
			Checkpoints(finding.AffectedCheckpoints, report.Metrics.PlacementCount,
				"Finding checkpoints");
			if (finding.EvidenceIds == null ||
				finding.EvidenceIds.Any(id => !evidenceIds.Contains(id)))
				throw new InvalidDataException("A rough-cut finding references unknown evidence.");
			Confidence(finding.Confidence, "Finding confidence");
		}

		HashSet<string> correctionIds = new(StringComparer.Ordinal);
		foreach (RoughCutCorrectionProposal correction in report.Corrections ??
			throw new InvalidDataException("Report corrections are required."))
		{
			Text(correction.CorrectionId, "Correction ID");
			if (!correctionIds.Add(correction.CorrectionId))
				throw new InvalidDataException("Rough-cut correction IDs must be unique.");
			if (correction.FindingIds == null || correction.FindingIds.Count == 0 ||
				correction.FindingIds.Any(id => !findingIds.Contains(id)))
				throw new InvalidDataException("A correction must reference known findings.");
			Checkpoints(correction.TargetCheckpoints, report.Metrics.PlacementCount,
				"Correction checkpoints", requireAny: true);
			if (correction.Operation == RoughCutCorrectionOperation.Unknown ||
				!Enum.IsDefined(typeof(RoughCutCorrectionOperation), correction.Operation))
				throw new InvalidDataException(
					"A correction operation must be move, trim, duration, or constant speed.");
			Text(correction.Instruction, "Correction instruction");
			Text(correction.ExpectedOutcome, "Correction expected outcome");
			Text(correction.Risk, "Correction risk");
			Confidence(correction.Confidence, "Correction confidence");
		}
		if (report.Diagnostics == null || report.Diagnostics.Any(string.IsNullOrWhiteSpace))
			throw new InvalidDataException("Report diagnostics are invalid.");
	}

	public static void Validate(RoughCutCorrectionDecision decision, RoughCutAuditReport report)
	{
		if (decision == null) throw new ArgumentNullException(nameof(decision));
		Validate(report);
		Schema(decision.SchemaVersion, RoughCutCorrectionDecision.CurrentSchemaVersion,
			"rough-cut correction decision");
		if (!string.Equals(decision.SessionId, report.SessionId, StringComparison.Ordinal) ||
			!string.Equals(decision.ReportId, report.ReportId, StringComparison.Ordinal))
			throw new InvalidDataException("The correction decision targets another report.");
		if (!report.Corrections.Any(item =>
			string.Equals(item.CorrectionId, decision.CorrectionId, StringComparison.Ordinal)))
			throw new InvalidDataException("The correction decision targets an unknown correction.");
		if (decision.DecidedUtc == default)
			throw new InvalidDataException("Correction decision time is required.");
	}

	public static void Validate(
		RoughCutCheckpointReopenRequest request,
		RoughCutAuditReport report,
		RoughCutCorrectionDecision approval)
	{
		if (request == null) throw new ArgumentNullException(nameof(request));
		Validate(approval, report);
		Schema(request.SchemaVersion, RoughCutCheckpointReopenRequest.CurrentSchemaVersion,
			"rough-cut reopen request");
		Text(request.RequestId, "Reopen request ID");
		if (!string.Equals(request.SessionId, report.SessionId, StringComparison.Ordinal) ||
			!string.Equals(request.ReportId, report.ReportId, StringComparison.Ordinal) ||
			!string.Equals(request.CorrectionId, approval.CorrectionId, StringComparison.Ordinal))
			throw new InvalidDataException("The reopen request targets another report or correction.");
		Hash(request.ReportSha256, "Reopen report hash");
		if (approval.Disposition != RoughCutCorrectionDisposition.ApprovedForReopen)
			throw new InvalidDataException(
				"A checkpoint may be reopened only after explicit correction approval.");
		RoughCutCorrectionProposal correction = report.Corrections.Single(item =>
			string.Equals(item.CorrectionId, request.CorrectionId, StringComparison.Ordinal));
		if (!request.TargetCheckpoints.SequenceEqual(correction.TargetCheckpoints))
			throw new InvalidDataException("The reopen request changed the approved checkpoint scope.");
		if (!string.Equals(request.ScopedInstruction, correction.Instruction,
			StringComparison.Ordinal))
			throw new InvalidDataException("The reopen request changed the approved instruction.");
		if (request.CreatedUtc == default)
			throw new InvalidDataException("Reopen request time is required.");
	}

	public static void Validate(AcceptedRoughCutMilestone milestone, RoughCutAuditReport report)
	{
		if (milestone == null) throw new ArgumentNullException(nameof(milestone));
		Validate(report);
		Schema(milestone.SchemaVersion, AcceptedRoughCutMilestone.CurrentSchemaVersion,
			"accepted rough-cut milestone");
		if (!string.Equals(milestone.SessionId, report.SessionId, StringComparison.Ordinal) ||
			!string.Equals(milestone.ReportId, report.ReportId, StringComparison.Ordinal))
			throw new InvalidDataException("The milestone targets another rough-cut report.");
		Hash(milestone.ReportSha256, "Milestone report hash");
		Hash(milestone.PlanSha256, "Milestone plan hash");
		Hash(milestone.FullRenderSha256, "Milestone render hash");
		if (!string.Equals(milestone.PlanSha256, report.PlanSha256,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException("The milestone plan hash differs from the audited plan.");
		Text(milestone.AcceptedBy, "Milestone accepting actor");
		if (milestone.AcceptedUtc == default)
			throw new InvalidDataException("Milestone acceptance time is required.");

		IList<RoughCutCorrectionDecision> decisions = milestone.CorrectionDecisions ??
			throw new InvalidDataException("Milestone correction decisions are required.");
		if (decisions.Count != report.Corrections.Count)
			throw new InvalidDataException(
				"Every proposed correction requires a terminal decision before acceptance.");
		HashSet<string> ids = new(StringComparer.Ordinal);
		foreach (RoughCutCorrectionDecision decision in decisions)
		{
			Validate(decision, report);
			if (!ids.Add(decision.CorrectionId))
				throw new InvalidDataException("Milestone correction decisions must be unique.");
			if (decision.Disposition is not (RoughCutCorrectionDisposition.Applied or
				RoughCutCorrectionDisposition.Rejected))
				throw new InvalidDataException(
					"Approved or deferred corrections must be resolved before rough-cut acceptance.");
		}
	}

	private static void Validate(RoughCutAuditMetrics metrics)
	{
		if (metrics == null) throw new InvalidDataException("Report metrics are required.");
		if (metrics.PlacementCount < 1 ||
			metrics.TimelineStartSeconds < 0 ||
			metrics.TimelineEndSeconds <= metrics.TimelineStartSeconds ||
			metrics.MontageDurationSeconds <= 0 ||
			metrics.MinimumPlacementDurationSeconds <= 0 ||
			metrics.MedianPlacementDurationSeconds <= 0 ||
			metrics.MaximumPlacementDurationSeconds < metrics.MinimumPlacementDurationSeconds ||
			metrics.AveragePlacementDurationSeconds <= 0 ||
			metrics.TotalGapDurationSeconds < 0)
			throw new InvalidDataException("Rough-cut aggregate metrics are invalid.");
		foreach (RoughCutGapMetric gap in metrics.Gaps ??
			throw new InvalidDataException("Gap metrics are required."))
		{
			if (gap.BeforeCheckpoint < 1 || gap.AfterCheckpoint < 1 ||
				gap.EndSeconds <= gap.StartSeconds || gap.DurationSeconds <= 0)
				throw new InvalidDataException("A rough-cut gap metric is invalid.");
		}
		foreach (RoughCutJoinMetric join in metrics.Joins ??
			throw new InvalidDataException("Join metrics are required."))
		{
			if (join.BeforeCheckpoint < 1 || join.AfterCheckpoint < 1 ||
				join.JoinTimeSeconds < 0 || join.BeforeDurationSeconds <= 0 ||
				join.AfterDurationSeconds <= 0 || join.DurationRatio < 1)
				throw new InvalidDataException("A rough-cut join metric is invalid.");
		}
		foreach (RoughCutSectionMetric section in metrics.Sections ??
			throw new InvalidDataException("Section metrics are required."))
		{
			Text(section.RegionId, "Section region ID");
			if (section.StartSeconds < 0 || section.EndSeconds <= section.StartSeconds ||
				section.PlacementCoverageRatio < 0 || section.PlacementCoverageRatio > 1 ||
				section.PlacementCount < 0 || section.SyncCount < 0 ||
				section.EligibleMajorEventCount < 0 || section.UsedMajorEventCount < 0 ||
				section.UsedMajorEventCount > section.EligibleMajorEventCount)
				throw new InvalidDataException("A rough-cut section metric is invalid.");
		}
		if (metrics.UnusedMajorMusicEventIds == null ||
			metrics.UnusedMajorMusicEventIds.Any(string.IsNullOrWhiteSpace) ||
			metrics.UnfulfilledReservationIds == null ||
			metrics.UnfulfilledReservationIds.Any(string.IsNullOrWhiteSpace))
			throw new InvalidDataException("Rough-cut metric identifiers are invalid.");
	}

	private static void Checkpoints(
		IList<int> checkpoints,
		int count,
		string name,
		bool requireAny = false)
	{
		if (checkpoints == null || (requireAny && checkpoints.Count == 0) ||
			checkpoints.Any(value => value < 1 || value > count) ||
			checkpoints.Distinct().Count() != checkpoints.Count)
			throw new InvalidDataException(name + " are invalid.");
	}

	private static void TimeRange(double? start, double? end)
	{
		if (start.HasValue != end.HasValue ||
			(start.HasValue && (start.Value < 0 || end!.Value <= start.Value)))
			throw new InvalidDataException("Finding time bounds are invalid.");
	}

	private static void Confidence(double value, string name)
	{
		if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 1)
			throw new InvalidDataException(name + " must be between zero and one.");
	}

	private static void Text(string value, string name)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new InvalidDataException(name + " is required.");
	}

	private static void Hash(string value, string name)
	{
		if (value == null || value.Length != 64 ||
			value.Any(character => !Uri.IsHexDigit(character)))
			throw new InvalidDataException(name + " is not a SHA-256 value.");
	}

	private static void RelativePath(string value, string name)
	{
		Text(value, name);
		if (Path.IsPathRooted(value) ||
			value.Split('/', '\\').Any(segment => segment == ".."))
			throw new InvalidDataException(name + " must be a safe relative path.");
	}

	private static void Schema(int actual, int expected, string name)
	{
		if (actual != expected)
			throw new InvalidDataException($"Unsupported {name} schema version {actual}.");
	}
}
