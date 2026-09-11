using System;
using AutoEditing.Iteration.Contracts.Automation;

namespace AutoEditing.Iteration.Contracts.Assembly;

/// <summary>
/// Exact read-back of a candidate immediately after deterministic
/// materialization. Later synchronization reconciliation may adopt only the
/// explicitly supported video timing fields; every other difference is a
/// conflict against this baseline.
/// </summary>
public sealed class CandidateMaterializationBaseline
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public int Checkpoint { get; set; }
	public string PlanSha256 { get; set; } = "";
	public string SnapshotSha256 { get; set; } = "";
	public CandidateTimelineSnapshot Snapshot { get; set; } = new();
	public DateTimeOffset CapturedUtc { get; set; }
}
