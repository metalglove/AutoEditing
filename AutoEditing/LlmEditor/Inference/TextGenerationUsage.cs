namespace AutoEditing.LlmEditor.Inference;

internal sealed class TextGenerationUsage
{
	public int PromptTokens { get; init; }
	public int CachedPromptTokens { get; init; }
	public int CompletionTokens { get; init; }
	public int TotalTokens { get; init; }
	public int? ContextTokensPeak { get; init; }
}
