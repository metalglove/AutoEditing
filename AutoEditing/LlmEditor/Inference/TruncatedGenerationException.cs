namespace AutoEditing.LlmEditor.Inference;

internal sealed class TruncatedGenerationException : InvalidOperationException
{
	public TruncatedGenerationException(TextGenerationResult result)
		: base("The inference server truncated the response because the output token limit was reached.")
	{
		Result = result ?? throw new ArgumentNullException(nameof(result));
	}

	public TextGenerationResult Result { get; }
}
