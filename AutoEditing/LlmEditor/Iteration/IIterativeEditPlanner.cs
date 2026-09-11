using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Iteration;

internal interface IIterativeEditPlanner : IEditPlanner
{
	Task<EditPlanDocument> RevisePlanAsync(
		EditPlanningRequest request,
		EditPlanDocument previousPlan,
		EditIterationFeedback feedback,
		int iteration,
		CancellationToken cancellationToken);
}
