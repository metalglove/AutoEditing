using Core.Domain.Planning;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class MaterializeCandidateRequest
{
	public CandidateWorkspaceId Workspace { get; set; }
	public EditPlanDocument Plan { get; set; }
	public string SongPath { get; set; } = "";
	public bool IncludeSong { get; set; } = true;
	public bool IncludeSfx { get; set; } = true;
	public bool ApplyEffects { get; set; } = true;
}
