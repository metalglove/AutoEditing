using System;
using System.Collections.Generic;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Sessions;

namespace AutoEditing.Iteration.Contracts.Assembly;

public sealed class FinalRenderArtifact
{
	public string ArtifactKind { get; set; } = "";
	public string RelativePath { get; set; } = "";
	public string Sha256 { get; set; } = "";
	public long LengthBytes { get; set; }
	public TimeSpan Duration { get; set; }
	public string RenderProfile { get; set; } = "";
	public IList<FinalRenderComponent> Components { get; set; } =
		new List<FinalRenderComponent>();
}

public sealed class FinalRenderComponent
{
	public string RelativePath { get; set; } = "";
	public string Sha256 { get; set; } = "";
	public long LengthBytes { get; set; }
	public TimeSpan TimelineStart { get; set; }
	public TimeSpan Duration { get; set; }
}

public sealed class FinalizationPromotionIntent
{
	public string SessionId { get; set; } = "";
	public string RequestId { get; set; } = "";
	public string PromotionId { get; set; } = "";
	public DateTimeOffset CreatedUtc { get; set; }
	public string ExpectedProjectFingerprint { get; set; } = "";
	public string FinalPlanSha256 { get; set; } = "";
	public CandidateTimelineSnapshot ValidatedCandidateSnapshot { get; set; }
	public string ValidatedCandidateSnapshotSha256 { get; set; } = "";
	public IList<CandidatePromotionTrackMapping> PlannedTrackMappings { get; set; } =
		new List<CandidatePromotionTrackMapping>();
	public bool FinalRenderRequested { get; set; }
	public FinalRenderArtifact FinalRender { get; set; }
}

public sealed class FinalizationRollbackIntent
{
	public string RollbackAttemptId { get; set; } = "";
	public string PromotionId { get; set; } = "";
	public DateTimeOffset CreatedUtc { get; set; }
	public string ExpectedProjectFingerprint { get; set; } = "";
	public string ExpectedPromotedSnapshotSha256 { get; set; } = "";
}

public sealed class FinalizationRecoveryBundle
{
	public DateTimeOffset PromotedUtc { get; set; }
	public VegasHostIdentity Host { get; set; }
	public PromoteCandidateResult Promotion { get; set; }
}

public sealed class FinalArtifactEntry
{
	public string RelativePath { get; set; } = "";
	public long LengthBytes { get; set; }
	public string Sha256 { get; set; } = "";
}

public sealed class FinalArtifactArchiveReceipt
{
	public string ArchivePath { get; set; } = "";
	public DateTimeOffset CreatedUtc { get; set; }
	public long LengthBytes { get; set; }
	public string Sha256 { get; set; } = "";
	public int EntryCount { get; set; }
}

public sealed class FinalModelUsageReport
{
	public string Model { get; set; } = "";
	public string Provider { get; set; } = "";
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
	public string CostCurrency { get; set; } = "";
}

public sealed class FinalSessionReport
{
	public string SessionId { get; set; } = "";
	public string RequestId { get; set; } = "";
	public string PromotionId { get; set; } = "";
	public DateTimeOffset CompletedUtc { get; set; }
	public string ProjectPath { get; set; } = "";
	public string ProjectFingerprint { get; set; } = "";
	public string FinalPlanSha256 { get; set; } = "";
	public string PromotedSnapshotSha256 { get; set; } = "";
	public int PlacementCount { get; set; }
	public TimeSpan TimelineDuration { get; set; }
	public FinalRenderArtifact FinalRender { get; set; }
	public IList<FinalModelUsageReport> ModelUsage { get; set; } =
		new List<FinalModelUsageReport>();
	public InferenceUsageSummary SessionUsage { get; set; }
	public IList<FinalArtifactEntry> ArchivedArtifacts { get; set; } =
		new List<FinalArtifactEntry>();
	public FinalArtifactArchiveReceipt Archive { get; set; }
}
