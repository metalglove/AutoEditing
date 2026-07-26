using AutoEditing.LlmEditor.Inference;

namespace AutoEditing.LlmEditor.Iteration;

internal sealed class EditIterationFeedback
{
	public bool IsAccepted { get; init; }

	public string Summary { get; init; } = "";

	public IReadOnlyList<VisualEvidence> VisualEvidence { get; init; } =
		Array.Empty<VisualEvidence>();

	public IReadOnlyList<EditDecisionRecord> Decisions { get; init; } =
		Array.Empty<EditDecisionRecord>();

	public IReadOnlyList<string> SteeringInstructions { get; init; } =
		Array.Empty<string>();
}
