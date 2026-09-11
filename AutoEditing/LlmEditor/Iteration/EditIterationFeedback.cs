using AutoEditing.LlmEditor.Inference;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Iterations;

namespace AutoEditing.LlmEditor.Iteration;

internal sealed class EditIterationFeedback
{
	public bool IsAccepted { get; init; }

	public string Summary { get; init; } = "";

	public IReadOnlyList<VisualEvidence> VisualEvidence { get; init; } =
		Array.Empty<VisualEvidence>();

	public IReadOnlyList<EditDecisionRecord> Decisions { get; init; } =
		Array.Empty<EditDecisionRecord>();

	public IReadOnlyList<EditReviewFinding> Findings { get; init; } =
		Array.Empty<EditReviewFinding>();

	public IReadOnlyList<string> SteeringInstructions { get; init; } =
		Array.Empty<string>();

	public TimelineAdjustmentDelta? TimelineAdjustment { get; init; }
}
