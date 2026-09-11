using AutoEditing.Iteration.Contracts.Assembly;
using Core.Domain.Planning;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class ApplyCandidateEffectsRequest
{
	public CandidateWorkspaceId Workspace { get; set; }
	public EditPlanDocument Plan { get; set; }
	public EffectsPassPlan Effects { get; set; }
	public string PlanSha256 { get; set; } = "";
}
public sealed class ApplyCandidateEffectsResult
{
	public PolishPassMaterialization Materialization { get; set; }
}
public sealed class ApplyCandidateAudioRequest
{
	public CandidateWorkspaceId Workspace { get; set; }
	public EditPlanDocument Plan { get; set; }
	public AudioPassPlan Audio { get; set; }
	public string PlanSha256 { get; set; } = "";
}
public sealed class ApplyCandidateAudioResult
{
	public PolishPassMaterialization Materialization { get; set; }
}
