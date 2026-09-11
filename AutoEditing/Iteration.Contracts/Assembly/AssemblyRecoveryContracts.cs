using System;
using System.Collections.Generic;

namespace AutoEditing.Iteration.Contracts.Assembly;

public sealed class AssemblySessionDescriptor
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public string RequestId { get; set; } = "";
	public string RequestSha256 { get; set; } = "";
	public string SongPath { get; set; } = "";
	public int TotalClips { get; set; }
	public string ProjectPath { get; set; } = "";
	public string ProjectFingerprint { get; set; } = "";
	public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public enum AssemblyRecoveryDisposition
{
	ReadyToResume,
	NotFound,
	Terminal,
	Conflict,
	Diverged,
	Corrupt
}

public sealed class AssemblyRecoveryIssue
{
	public string Code { get; set; } = "";
	public string Message { get; set; } = "";
}

public sealed class AssemblyRecoverySummary
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public AssemblyRecoveryDisposition Disposition { get; set; }
	public int Checkpoint { get; set; }
	public int AcceptedCheckpoint { get; set; }
	public long StateRevision { get; set; }
	public bool ReuseMaterializedWorkspace { get; set; }
	public bool FinalizeAcceptedPlan { get; set; }
	public bool NeedsSketch { get; set; }
	public IList<AssemblyRecoveryIssue> Issues { get; set; } =
		new List<AssemblyRecoveryIssue>();
	public DateTimeOffset InspectedUtc { get; set; } = DateTimeOffset.UtcNow;
}
