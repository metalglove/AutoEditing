using System;

namespace AutoEditing.Iteration.Contracts.Sessions;

public sealed class InferenceUsageRecord
{
	public DateTimeOffset TimestampUtc { get; set; }
	public string SessionId { get; set; } = "";
	public string Model { get; set; } = "";
	public string Provider { get; set; }
	public string Operation { get; set; }
	public string ResponseId { get; set; }
	public string FinishReason { get; set; }
	public int PromptTokens { get; set; }
	public int CachedPromptTokens { get; set; }
	public int GeneratedTokens { get; set; }
	public int TotalTokens { get; set; }
	public double PromptMilliseconds { get; set; }
	public double TimeToFirstTokenMilliseconds { get; set; }
	public double GenerationMilliseconds { get; set; }
	public double PromptTokensPerSecond { get; set; }
	public double GenerationTokensPerSecond { get; set; }
	public int? ContextTokensPeak { get; set; }
	public int RetryCount { get; set; }
	public decimal? Cost { get; set; }
	public string CostCurrency { get; set; }
}

public sealed class InferenceUsageSummary
{
	public string Scope { get; set; } = "";
	public string SessionId { get; set; }
	public string Model { get; set; }
	public string Provider { get; set; }
	public bool HasMixedModels { get; set; }
	public bool HasMixedProviders { get; set; }
	public DateTimeOffset UpdatedUtc { get; set; }
	public long CallCount { get; set; }
	public long PromptTokens { get; set; }
	public long CachedPromptTokens { get; set; }
	public long GeneratedTokens { get; set; }
	public long TotalTokens { get; set; }
	public double PromptMilliseconds { get; set; }
	public double TimeToFirstTokenMilliseconds { get; set; }
	public long TimeToFirstTokenSampleCount { get; set; }
	public double GenerationMilliseconds { get; set; }
	public int? ContextTokensPeak { get; set; }
	public long RetryCount { get; set; }
	public decimal? Cost { get; set; }
	public string CostCurrency { get; set; }
}
