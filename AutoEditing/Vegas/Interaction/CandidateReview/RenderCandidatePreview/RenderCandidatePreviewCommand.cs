using AutoEditing.Iteration.Contracts.Automation;

namespace Core.Scripts;

internal sealed class RenderCandidatePreviewCommand : IVegasQuery<RenderCandidatePreviewResult>
{
	public string CommandType => VegasOperations.RenderCandidatePreview;
	public RenderCandidatePreviewRequest Request { get; set; }
}
