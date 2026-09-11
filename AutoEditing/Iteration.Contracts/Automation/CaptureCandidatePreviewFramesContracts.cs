using System;
using System.Collections.Generic;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class CaptureCandidatePreviewFramesRequest
{
	public CandidateWorkspaceId Workspace { get; set; }
	public IList<TimeSpan> TimelineTimes { get; set; } = new List<TimeSpan>();
	public string OutputDirectoryRelativePath { get; set; } = "";
}

public sealed class CapturedCandidatePreviewFrame
{
	public TimeSpan TimelineTime { get; set; }
	public string OutputRelativePath { get; set; } = "";
	public string Sha256 { get; set; } = "";
}

public sealed class CaptureCandidatePreviewFramesResult
{
	public IList<CapturedCandidatePreviewFrame> Frames { get; set; } =
		new List<CapturedCandidatePreviewFrame>();
}
