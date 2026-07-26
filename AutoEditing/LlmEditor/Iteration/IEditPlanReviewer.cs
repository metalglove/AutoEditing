using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Iteration;

internal interface IEditPlanReviewer
{
	Task<EditIterationFeedback> ReviewAsync(
		EditPlanningRequest request,
		EditPlanDocument candidate,
		int iteration,
		CancellationToken cancellationToken);
}
