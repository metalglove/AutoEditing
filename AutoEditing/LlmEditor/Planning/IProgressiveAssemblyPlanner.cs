using AutoEditing.Iteration.Contracts.Assembly;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Planning;

internal interface IProgressiveAssemblyPlanner
{
	Task<AssemblySketch> CreateSketchAsync(
		EditPlanningRequest request,
		CancellationToken cancellationToken);

	Task<ClipStepDecision> PlanClipAsync(
		EditPlanningRequest request,
		ProgressiveAssemblyPlanningContext context,
		CancellationToken cancellationToken);

	Task<ClipStepDecision> ReviseClipAsync(
		EditPlanningRequest request,
		ProgressiveAssemblyPlanningContext context,
		ClipStepDecision previousDecision,
		CancellationToken cancellationToken);
}
