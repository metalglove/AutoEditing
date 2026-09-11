namespace AutoEditing.LlmEditor.Inference;

internal sealed class TextGenerationRequest
{
	public string SystemPrompt { get; init; } = "";

	public string UserPrompt { get; init; } = "";

	public IReadOnlyList<VisualEvidence> VisualEvidence { get; init; } =
		Array.Empty<VisualEvidence>();

	public double Temperature { get; init; }

	public int MaxOutputTokens { get; init; } = 8192;

	public string? JsonSchemaName { get; init; }

	public string? JsonSchema { get; init; }

	public Action<string>? OnTextDelta { get; init; }

	public Action<string>? OnReasoningDelta { get; init; }
}
