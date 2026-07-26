using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Iteration;

internal sealed class EditIterationSnapshot
{
	public required int Iteration { get; init; }

	public required string Phase { get; init; }

	public required EditPlanDocument Candidate { get; init; }

	public string Summary { get; init; } = "";

	public IReadOnlyList<EditDecisionRecord> Decisions { get; init; } =
		Array.Empty<EditDecisionRecord>();
}
