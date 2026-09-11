using AutoEditing.Iteration.Contracts.Automation;

namespace Core.Scripts;

internal sealed class CaptureCandidatePreviewFramesCommand :
	IVegasQuery<CaptureCandidatePreviewFramesResult>
{
	public string CommandType => VegasOperations.CaptureCandidatePreviewFrames;
	public CaptureCandidatePreviewFramesRequest Request { get; set; }
}
