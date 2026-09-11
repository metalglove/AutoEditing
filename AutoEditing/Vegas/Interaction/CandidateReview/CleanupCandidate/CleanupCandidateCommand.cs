using AutoEditing.Iteration.Contracts.Automation;

namespace Core.Scripts;

internal sealed class CleanupCandidateCommand : IVegasQuery<CleanupCandidateResult>
{
	public string CommandType => VegasOperations.CleanupCandidate;
	public CleanupCandidateRequest Request { get; set; }
}
