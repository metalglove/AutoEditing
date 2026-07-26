namespace AutoEditing.LlmEditor.Inference;

internal interface ITextGenerationClient
{
	Task<TextGenerationResult> GenerateAsync(
		TextGenerationRequest request,
		CancellationToken cancellationToken);
}
