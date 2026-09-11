using AutoEditing.Iteration.Contracts.Sessions;

namespace AutoEditing.LlmEditor.Inference;

internal interface IInferenceUsageSink
{
	void Record(InferenceUsageRecord record);
}

internal sealed class InferenceUsageContext
{
	public required string SessionId { get; init; }
	public string? Operation { get; init; }
}
