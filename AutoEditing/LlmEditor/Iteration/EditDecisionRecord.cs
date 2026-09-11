namespace AutoEditing.LlmEditor.Iteration;

internal sealed class EditDecisionRecord
{
	public required string DecisionId { get; init; }

	public required string Category { get; init; }

	public required string Summary { get; init; }

	public double? Confidence { get; init; }

	public IReadOnlyList<string> EvidenceIds { get; init; } = Array.Empty<string>();
}
