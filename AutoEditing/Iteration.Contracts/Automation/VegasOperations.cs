using System.Collections.Generic;

namespace AutoEditing.Iteration.Contracts.Automation;

public static class VegasOperations
{
	public const string PreflightCandidate = "candidate.preflight";
	public const string MaterializeCandidate = "candidate.materialize";
	public const string GetCandidateSnapshot = "candidate.snapshot";
	public const string RenderCandidatePreview = "candidate.render-preview";
	public const string CaptureCandidatePreviewFrames = "candidate.capture-preview-frames";
	public const string ApplyCandidateEffects = "candidate.apply-effects-pass";
	public const string ApplyCandidateAudio = "candidate.apply-audio-pass";
	public const string CleanupCandidate = "candidate.cleanup";
	public const string PromoteCandidate = "candidate.promote";
	public const string RollbackCandidatePromotion = "candidate.rollback-promotion";

	public static ISet<string> Supported { get; } = new HashSet<string>
	{
		PreflightCandidate,
		MaterializeCandidate,
		GetCandidateSnapshot,
		RenderCandidatePreview,
		CaptureCandidatePreviewFrames,
		ApplyCandidateEffects,
		ApplyCandidateAudio,
		CleanupCandidate,
		PromoteCandidate,
		RollbackCandidatePromotion
	};
}
