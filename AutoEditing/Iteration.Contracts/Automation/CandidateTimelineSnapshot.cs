using System;
using System.Collections.Generic;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class CandidateTimelineSnapshot
{
	public CandidateWorkspaceId Workspace { get; set; }
	public TimeSpan TimelineStart { get; set; }
	public TimeSpan TimelineEnd { get; set; }
	public IList<CandidateTrackSnapshot> Tracks { get; set; } = new List<CandidateTrackSnapshot>();
	public IList<string> Warnings { get; set; } = new List<string>();
}
