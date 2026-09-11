namespace AutoEditing.Iteration.Contracts.Assembly;

public enum AssemblyActionKind
{
	ReviseCurrentClip,
	AcceptTimelineAndContinue,
	ResetCurrentClip,
	FinishSyncPass,
	CompareCurrentTimeline,
	RenderCheckpointPreview,
	RestoreReconciliationProposal,
	ExcludeReconciliationClip,
	AdoptReconciliationEvent,
	DeferReconciliation,
	ApproveRoughCutCorrection,
	RejectRoughCutCorrection,
	ApplyRoughCutCorrection,
	AcceptRoughCut,
	ApproveEffectsPlan,
	SkipEffectsPlan,
	AcceptEffectsPreview,
	SkipEffectsPreview,
	ApproveAudioPlan,
	SkipAudioPlan,
	AcceptAudioPreview,
	SkipAudioPreview,
	FinalizeMontage,
	PauseSession,
	ResumeSession,
	AbandonSession
}
