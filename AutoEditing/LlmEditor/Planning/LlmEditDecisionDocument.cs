namespace AutoEditing.LlmEditor.Planning;

internal sealed class LlmEditDecisionDocument
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;

	public string RequestId { get; set; } = "";

	public List<LlmPlacementDecision> Placements { get; set; } = new();

	public List<LlmDecisionDiagnostic> Diagnostics { get; set; } = new();
}

internal sealed class LlmPlacementDecision
{
	public string ClipPath { get; set; } = "";

	public double SourceStartSeconds { get; set; }

	public double SourceEndSeconds { get; set; }

	public double Speed { get; set; } = 1.0;

	public LlmSyncDecision PrimarySync { get; set; } = new();

	public List<LlmSyncDecision> AdditionalSyncs { get; set; } = new();
}

internal sealed class LlmSyncDecision
{
	public int KillIndex { get; set; }

	public string MusicEventId { get; set; } = "";
}

internal sealed class LlmDecisionDiagnostic
{
	public string Severity { get; set; } = "Info";

	public string Code { get; set; } = "";

	public string Message { get; set; } = "";
}
