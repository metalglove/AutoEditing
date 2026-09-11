using System;

namespace AutoEditing.Iteration.Contracts.Sessions;

public sealed class EditSessionProgress
{
	public int SchemaVersion { get; set; } = ContractSchema.CurrentVersion;
	public string SessionId { get; set; } = "";
	public string Stage { get; set; } = "";
	public DateTimeOffset UpdatedUtc { get; set; }
	public TimeSpan Elapsed { get; set; }
	public bool IsProcessing { get; set; }
	public int PromptTokens { get; set; }
	public int PromptTokensProcessed { get; set; }
	public int GeneratedTokens { get; set; }
	public int MaximumGeneratedTokens { get; set; }
	public int GeneratedCharacters { get; set; }
	public bool IsTokenEstimate { get; set; }
	public bool HasReceivedFirstToken { get; set; }
	public DateTimeOffset? LastActivityUtc { get; set; }
	public string Provider { get; set; } = "";
	public string Operation { get; set; } = "";
	public string Message { get; set; } = "";
}
