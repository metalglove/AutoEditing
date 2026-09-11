using System.Collections.Generic;

namespace AutoEditing.Iteration.Contracts.Assembly;

public enum TimelineAdjustmentKind
{
	TimelineStartChanged,
	SourceTrimChanged,
	DurationChanged,
	ConstantSpeedChanged,
	SyncAssignmentInvalidated
}

public sealed class TimelineAdjustment
{
	public TimelineAdjustmentKind Kind { get; set; }
	public string ClipPath { get; set; } = "";
	public double Before { get; set; }
	public double After { get; set; }
}

public sealed class TimelineAdjustmentDelta
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public int Checkpoint { get; set; }
	public IList<TimelineAdjustment> Changes { get; set; } =
		new List<TimelineAdjustment>();
	public IList<string> UnsupportedChanges { get; set; } =
		new List<string>();
}
