using AutoEditing.Iteration.Contracts.Automation;

namespace Core.Scripts;

internal sealed class PromoteCandidateCommand : IVegasQuery<PromoteCandidateResult>
{
	public string CommandType => VegasOperations.PromoteCandidate;
	public PromoteCandidateRequest Request { get; set; }
}
