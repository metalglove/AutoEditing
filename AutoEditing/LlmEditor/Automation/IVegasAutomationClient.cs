using AutoEditing.Iteration.Contracts.Automation;

namespace AutoEditing.LlmEditor.Automation;

internal interface IVegasAutomationClient
{
	VegasHostIdentity? LastHost => null;
	string? ExpectedProjectFingerprint => null;

	Task<TResult> ExecuteAsync<TRequest, TResult>(
		string operation,
		TRequest request,
		string idempotencyKey,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default);
}
