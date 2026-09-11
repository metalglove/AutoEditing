#if !NETFRAMEWORK
using System.Threading.Tasks;
using AutoEditing.Iteration.Contracts.Automation;

// AutomationBroker.Tests links the pure broker sources without referencing the
// net48 VEGAS integration assembly. These test-only shapes mirror the internal
// query boundary so the bridge can be exercised on net8 without ScriptPortal.
namespace Core.Scripts
{
	internal interface IVegasQuery<TResult>
	{
		string CommandType { get; }
	}

	internal interface IVegasQueryClient
	{
		Task<TResult> QueryAsync<TResult>(IVegasQuery<TResult> query);
	}

	internal sealed class PreflightCandidateCommand : IVegasQuery<PreflightCandidateResult>
	{
		public string CommandType => VegasOperations.PreflightCandidate;
		public PreflightCandidateRequest Request { get; set; }
	}

	internal sealed class MaterializeCandidateCommand : IVegasQuery<MaterializeCandidateResult>
	{
		public string CommandType => VegasOperations.MaterializeCandidate;
		public MaterializeCandidateRequest Request { get; set; }
	}

	internal sealed class GetCandidateSnapshotCommand : IVegasQuery<CandidateTimelineSnapshot>
	{
		public string CommandType => VegasOperations.GetCandidateSnapshot;
		public GetCandidateSnapshotRequest Request { get; set; }
	}

	internal sealed class RenderCandidatePreviewCommand : IVegasQuery<RenderCandidatePreviewResult>
	{
		public string CommandType => VegasOperations.RenderCandidatePreview;
		public RenderCandidatePreviewRequest Request { get; set; }
	}

	internal sealed class CaptureCandidatePreviewFramesCommand :
		IVegasQuery<CaptureCandidatePreviewFramesResult>
	{
		public string CommandType => VegasOperations.CaptureCandidatePreviewFrames;
		public CaptureCandidatePreviewFramesRequest Request { get; set; }
	}

	internal sealed class CleanupCandidateCommand : IVegasQuery<CleanupCandidateResult>
	{
		public string CommandType => VegasOperations.CleanupCandidate;
		public CleanupCandidateRequest Request { get; set; }
	}

	internal sealed class ApplyCandidateEffectsCommand :
		IVegasQuery<ApplyCandidateEffectsResult>
	{
		public string CommandType => VegasOperations.ApplyCandidateEffects;
		public ApplyCandidateEffectsRequest Request { get; set; }
	}

	internal sealed class ApplyCandidateAudioCommand :
		IVegasQuery<ApplyCandidateAudioResult>
	{
		public string CommandType => VegasOperations.ApplyCandidateAudio;
		public ApplyCandidateAudioRequest Request { get; set; }
	}

	internal sealed class PromoteCandidateCommand : IVegasQuery<PromoteCandidateResult>
	{
		public string CommandType => VegasOperations.PromoteCandidate;
		public PromoteCandidateRequest Request { get; set; }
	}

	internal sealed class RollbackCandidatePromotionCommand :
		IVegasQuery<RollbackCandidatePromotionResult>
	{
		public string CommandType => VegasOperations.RollbackCandidatePromotion;
		public RollbackCandidatePromotionRequest Request { get; set; }
	}
}
#endif
