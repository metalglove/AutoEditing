using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.RoughCut;

internal sealed class RoughCutAuditInput
{
	public string SessionId { get; init; } = "";
	public required EditPlanningRequest PlanningRequest { get; init; }
	public required EditPlanDocument AcceptedSyncPlan { get; init; }
	public required AssemblySketch Sketch { get; init; }
	public required CandidateTimelineSnapshot Timeline { get; init; }
	public IReadOnlyList<RoughCutEvidenceReference> Evidence { get; init; } =
		Array.Empty<RoughCutEvidenceReference>();
}

