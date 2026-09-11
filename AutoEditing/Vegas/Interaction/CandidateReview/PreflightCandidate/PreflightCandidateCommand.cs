using AutoEditing.Iteration.Contracts.Automation;

namespace Core.Scripts;

internal sealed class PreflightCandidateCommand : IVegasQuery<PreflightCandidateResult>
{
	public string CommandType => VegasOperations.PreflightCandidate;
	public PreflightCandidateRequest Request { get; set; }
}
