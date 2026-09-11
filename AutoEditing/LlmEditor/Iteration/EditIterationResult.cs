using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Iteration;

internal sealed class EditIterationResult
{
	public required EditPlanDocument Plan { get; init; }

	public required int Iterations { get; init; }

	public required bool WasAccepted { get; init; }
}
