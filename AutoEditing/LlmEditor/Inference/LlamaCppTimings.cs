namespace AutoEditing.LlmEditor.Inference;

internal sealed class LlamaCppTimings
{
	public double PromptMilliseconds { get; init; }
	public double TimeToFirstTokenMilliseconds { get; init; }
	public double PredictedMilliseconds { get; init; }
	public double PromptTokensPerSecond { get; init; }
	public double PredictedTokensPerSecond { get; init; }
}
