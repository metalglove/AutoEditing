using AutoEditing.Iteration.Contracts.Automation;

namespace Core.Scripts;

internal sealed class MaterializeCandidateCommand : IVegasQuery<MaterializeCandidateResult>
{
	public string CommandType => VegasOperations.MaterializeCandidate;
	public MaterializeCandidateRequest Request { get; set; }
}
