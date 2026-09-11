using System;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class RenderCandidatePreviewResult
{
	public string OutputRelativePath { get; set; } = "";
	public string Sha256 { get; set; } = "";
	public TimeSpan RenderedDuration { get; set; }
	public string RenderProfileId { get; set; } = "";
}
