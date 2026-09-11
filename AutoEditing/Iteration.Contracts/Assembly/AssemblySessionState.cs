using System;
using System.Collections.Generic;
using AutoEditing.Iteration.Contracts.Automation;

namespace AutoEditing.Iteration.Contracts.Assembly;

public enum AssemblyPhase
{
	CreatingSketch,
	PlanningClip,
	RepairingProposal,
	MaterializingClip,
	AwaitingHumanReview,
	RenderingCheckpoint,
	ReconcilingTimeline,
	ReconciliationConflict,
	Paused,
	Recovering,
	SyncPassComplete,
	RoughCutRendering,
	RoughCutAuditing,
	RoughCutReview,
	RoughCutCorrection,
	RoughCutAccepted,
	EffectsPlanReview,
	EffectsMaterializing,
	EffectsPreviewReview,
	AudioPlanReview,
	AudioMaterializing,
	AudioPreviewReview,
	PolishAccepted,
	FinalReview,
	Completed,
	Abandoned,
	Failed
}

public sealed class AssemblySessionState
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public AssemblyPhase Phase { get; set; }
	public int Checkpoint { get; set; }
	public long StateRevision { get; set; }
	public int TotalClips { get; set; }
	public string CurrentClipPath { get; set; } = "";
	public IList<string> RemainingClipPaths { get; set; } = new List<string>();
	public string Status { get; set; } = "";
	public CandidateWorkspaceId Workspace { get; set; }
	public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
