using System;
using System.Collections.Generic;
using System.Linq;
using AutoEditing.Iteration.Contracts.Automation;

namespace AutoEditing.Iteration.Contracts.Assembly;

public enum CheckpointPreviewStatus
{
	Rendering,
	Completed,
	RenderFailed,
	ReviewFailed,
	Cancelled
}

/// <summary>
/// The deterministic timeline range rendered for one synchronization checkpoint.
/// The active placement is always complete; only surrounding context may be reduced.
/// </summary>
public sealed class CheckpointPreviewWindow
{
	public TimeSpan Start { get; set; }
	public TimeSpan End { get; set; }
	public TimeSpan PlacementStart { get; set; }
	public TimeSpan PlacementEnd { get; set; }
	public TimeSpan ContextBefore { get; set; }
	public TimeSpan ContextAfter { get; set; }
}

public sealed class CheckpointTimingPoint
{
	public string PointId { get; set; } = "";
	public string Kind { get; set; } = "";
	public TimeSpan TimelineTime { get; set; }
	public string Description { get; set; } = "";
}

public sealed class CheckpointTimingSidecar
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public int Checkpoint { get; set; }
	public int Attempt { get; set; }
	public CheckpointPreviewWindow Window { get; set; } = new();
	public IList<CheckpointTimingPoint> Points { get; set; } =
		new List<CheckpointTimingPoint>();
	public TimelineAdjustmentDelta TimelineAdjustment { get; set; }
}

/// <summary>
/// Frame sampling instructions retained even when no local video decoder is
/// available. Consumers may populate the sample paths later without changing
/// the immutable preview attempt.
/// </summary>
public sealed class CheckpointContactSheetManifest
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SourcePreviewRelativePath { get; set; } = "";
	public IList<TimeSpan> SampleOffsets { get; set; } = new List<TimeSpan>();
	public IList<string> FrameRelativePaths { get; set; } = new List<string>();
	public string Status { get; set; } = "";
	public string Diagnostic { get; set; } = "";
}

public sealed class CheckpointReviewObservation
{
	public string ObservationId { get; set; } = "";
	public string Category { get; set; } = "";
	public string Severity { get; set; } = "";
	public string Message { get; set; } = "";
	public double Confidence { get; set; }
	public IList<string> EvidenceIds { get; set; } = new List<string>();
}

/// <summary>
/// Read-only reviewer output. Suggestions are evidence for a future human or
/// planner decision and never mutate the candidate timeline directly.
/// </summary>
public sealed class CheckpointReviewReport
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public int Checkpoint { get; set; }
	public int Attempt { get; set; }
	public DateTimeOffset CreatedUtc { get; set; }
	public string Summary { get; set; } = "";
	public double Confidence { get; set; }
	public IList<CheckpointReviewObservation> Observations { get; set; } =
		new List<CheckpointReviewObservation>();
	public IList<string> SuggestedChanges { get; set; } = new List<string>();
}

/// <summary>
/// Durable result of one explicit checkpoint-preview request.
/// </summary>
public sealed class CheckpointPreviewArtifact
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public int Checkpoint { get; set; }
	public int Attempt { get; set; }
	public CheckpointPreviewStatus Status { get; set; }
	public DateTimeOffset StartedUtc { get; set; }
	public DateTimeOffset? CompletedUtc { get; set; }
	public CandidateWorkspaceId Workspace { get; set; }
	public CheckpointPreviewWindow Window { get; set; } = new();
	public string RenderProfileId { get; set; } = "";
	public string PreviewRelativePath { get; set; } = "";
	public string PreviewSha256 { get; set; } = "";
	public string TimelineSnapshotRelativePath { get; set; } = "";
	public string TimingSidecarRelativePath { get; set; } = "";
	public string TimingVisualizationRelativePath { get; set; } = "";
	public string ContactSheetManifestRelativePath { get; set; } = "";
	public string ReviewReportRelativePath { get; set; } = "";
	public string FailureCode { get; set; } = "";
	public string FailureMessage { get; set; } = "";
}

public static class CheckpointPreviewContractValidator
{
	private static readonly TimeSpan MaximumPreviewDuration = TimeSpan.FromSeconds(20);

	public static void Validate(CheckpointPreviewArtifact artifact)
	{
		if (artifact == null) throw new ArgumentNullException(nameof(artifact));
		Schema(artifact.SchemaVersion, CheckpointPreviewArtifact.CurrentSchemaVersion);
		Text(artifact.SessionId, "Preview session ID");
		if (artifact.Checkpoint < 1 || artifact.Attempt < 1)
			throw new InvalidOperationException("Preview checkpoint and attempt must be positive.");
		artifact.Workspace?.Validate();
		if (artifact.Workspace == null)
			throw new InvalidOperationException("Preview workspace is required.");
		Validate(artifact.Window);
		Text(artifact.RenderProfileId, "Preview render profile");
		Text(artifact.TimelineSnapshotRelativePath, "Timeline snapshot path");
		Text(artifact.TimingSidecarRelativePath, "Timing sidecar path");
		Text(artifact.TimingVisualizationRelativePath, "Timing visualization path");
		Text(artifact.ContactSheetManifestRelativePath, "Contact-sheet manifest path");
		if (artifact.Status is CheckpointPreviewStatus.Completed or CheckpointPreviewStatus.ReviewFailed)
		{
			Text(artifact.PreviewRelativePath, "Completed preview path");
			if (artifact.PreviewSha256.Length != 64)
				throw new InvalidOperationException("Completed preview SHA-256 is invalid.");
			if (artifact.Status == CheckpointPreviewStatus.Completed)
				Text(artifact.ReviewReportRelativePath, "Completed preview review report");
			if (!artifact.CompletedUtc.HasValue)
				throw new InvalidOperationException("Completed preview timestamp is required.");
		}
		if (artifact.Status is CheckpointPreviewStatus.RenderFailed or
			CheckpointPreviewStatus.ReviewFailed or CheckpointPreviewStatus.Cancelled)
		{
			Text(artifact.FailureCode, "Preview failure code");
			Text(artifact.FailureMessage, "Preview failure message");
			if (!artifact.CompletedUtc.HasValue)
				throw new InvalidOperationException("Failed preview timestamp is required.");
		}
	}

	public static void Validate(CheckpointPreviewWindow window)
	{
		if (window == null) throw new ArgumentNullException(nameof(window));
		if (window.Start < TimeSpan.Zero || window.End <= window.Start ||
			window.PlacementStart < window.Start || window.PlacementEnd > window.End ||
			window.PlacementEnd <= window.PlacementStart)
			throw new InvalidOperationException("Checkpoint preview bounds are invalid.");
		if (window.End - window.Start > MaximumPreviewDuration)
			throw new InvalidOperationException("Checkpoint previews cannot exceed twenty seconds.");
		if (window.ContextBefore < TimeSpan.Zero || window.ContextAfter < TimeSpan.Zero)
			throw new InvalidOperationException("Checkpoint preview context cannot be negative.");
	}

	public static void Validate(CheckpointReviewReport report)
	{
		if (report == null) throw new ArgumentNullException(nameof(report));
		Schema(report.SchemaVersion, CheckpointReviewReport.CurrentSchemaVersion);
		Text(report.SessionId, "Review session ID");
		if (report.Checkpoint < 1 || report.Attempt < 1)
			throw new InvalidOperationException("Review checkpoint and attempt must be positive.");
		Text(report.Summary, "Review summary");
		if (report.Confidence < 0 || report.Confidence > 1)
			throw new InvalidOperationException("Review confidence must be between zero and one.");
		HashSet<string> ids = new(StringComparer.Ordinal);
		foreach (CheckpointReviewObservation item in report.Observations ??
			throw new InvalidOperationException("Review observations are required."))
		{
			Text(item.ObservationId, "Observation ID");
			if (!ids.Add(item.ObservationId))
				throw new InvalidOperationException("Observation IDs must be unique.");
			Text(item.Category, "Observation category");
			if (item.Severity is not ("info" or "warning" or "error"))
				throw new InvalidOperationException("Observation severity is invalid.");
			Text(item.Message, "Observation message");
			if (item.Confidence < 0 || item.Confidence > 1)
				throw new InvalidOperationException(
					"Observation confidence must be between zero and one.");
			if (item.EvidenceIds == null || item.EvidenceIds.Any(string.IsNullOrWhiteSpace))
				throw new InvalidOperationException("Observation evidence IDs are invalid.");
		}
		if (report.SuggestedChanges == null ||
			report.SuggestedChanges.Any(string.IsNullOrWhiteSpace))
			throw new InvalidOperationException("Suggested review changes are invalid.");
	}

	private static void Text(string value, string name)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new InvalidOperationException(name + " is required.");
	}

	private static void Schema(int actual, int expected)
	{
		if (actual != expected)
			throw new InvalidOperationException(
				$"Unsupported checkpoint preview schema version {actual}.");
	}
}
