using AutoEditing.Iteration.Contracts.Automation;

namespace Core.Scripts;

internal sealed class RollbackCandidatePromotionCommand :
	IVegasQuery<RollbackCandidatePromotionResult>
{
	public string CommandType => VegasOperations.RollbackCandidatePromotion;
	public RollbackCandidatePromotionRequest Request { get; set; }
}
