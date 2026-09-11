namespace AutoEditing.LlmEditor.Inference;

internal sealed class TextGenerationResult
{
	public string Text { get; init; } = "";

	public string Model { get; init; } = "";

	public string FinishReason { get; init; } = "";

	public string ResponseId { get; init; } = "";

	public TextGenerationUsage Usage { get; init; } = new();

	public LlamaCppTimings Timings { get; init; } = new();

	public int RetryCount { get; init; }
}
