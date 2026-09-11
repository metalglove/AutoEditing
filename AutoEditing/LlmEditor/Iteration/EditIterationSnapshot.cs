using Core.Domain.Planning;
using AutoEditing.Iteration.Contracts.Evidence;
using AutoEditing.Iteration.Contracts.Iterations;

namespace AutoEditing.LlmEditor.Iteration;

internal sealed class EditIterationSnapshot
{
	public required int Iteration { get; init; }

	public required string Phase { get; init; }

	public required EditPlanDocument Candidate { get; init; }

	public string Summary { get; init; } = "";

	public IReadOnlyList<EditDecisionRecord> Decisions { get; init; } =
		Array.Empty<EditDecisionRecord>();

	public IReadOnlyList<EditReviewFinding> Findings { get; init; } =
		Array.Empty<EditReviewFinding>();

	public IReadOnlyList<EditEvidenceReference> Evidence { get; init; } =
		Array.Empty<EditEvidenceReference>();
}
