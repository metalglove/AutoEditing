using System;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class RenderCandidatePreviewRequest
{
	public CandidateWorkspaceId Workspace { get; set; }
	public TimeSpan Start { get; set; }
	public TimeSpan Duration { get; set; }
	public string RenderProfileId { get; set; } = "";
	public string OutputRelativePath { get; set; } = "";
}
