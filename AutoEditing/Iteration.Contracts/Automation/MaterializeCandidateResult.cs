using System.Collections.Generic;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class MaterializeCandidateResult
{
	public CandidateWorkspaceId Workspace { get; set; }
	public IList<string> CreatedTrackNames { get; set; } = new List<string>();
	public int CreatedEventCount { get; set; }
	public CandidateTimelineSnapshot Snapshot { get; set; }
}
