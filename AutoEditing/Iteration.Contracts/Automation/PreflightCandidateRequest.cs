using Core.Domain.Planning;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class PreflightCandidateRequest
{
	public CandidateWorkspaceId Workspace { get; set; }
	public EditPlanDocument Plan { get; set; }
	public string SongPath { get; set; } = "";
}
