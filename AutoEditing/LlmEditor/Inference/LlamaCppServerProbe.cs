namespace AutoEditing.LlmEditor.Inference;

internal sealed class LlamaCppHealth
{
	public string Status { get; init; } = "";
}

internal sealed class LlamaCppProps
{
	public string ModelPath { get; init; } = "";
	public int TotalSlots { get; init; }
	public int ContextSize { get; init; }
	public string ChatTemplate { get; init; } = "";
}
