using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.LlmEditor.Automation;

namespace AutoEditing.LlmEditor.Assembly;

internal static class CandidateAbandonCleanupSelfTests
{
	public static void Run()
	{
		CandidateWorkspaceId workspace = new()
		{
			SessionId = "abandon-cleanup-session",
			Iteration = 4,
			Nonce = "candidate"
		};
		RecordingAutomation automation = new();
		for (int replay = 0; replay < 2; replay++)
			CandidateAbandonCleanup.CleanupAsync(
					automation,
					workspace,
					"durable-action-42",
					CancellationToken.None)
				.GetAwaiter().GetResult();
		Assert(
			automation.Requests.Count == 2 &&
			automation.Requests.All(item =>
				item.Workspace.ToString() == workspace.ToString()) &&
			automation.IdempotencyKeys.Distinct(StringComparer.Ordinal)
				.SequenceEqual(new[]
				{
					"abandon-durable-action-42-cleanup"
				}),
			"Abandon cleanup did not remain workspace-scoped and idempotent " +
			"across crash recovery replay.");
		ExpectFailure(
			() => CandidateAbandonCleanup.CleanupAsync(
					automation,
					workspace,
					@"..\unsafe",
					CancellationToken.None)
				.GetAwaiter().GetResult(),
			"An unsafe abandon action ID reached VEGAS automation.");
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private static void ExpectFailure(Action action, string message)
	{
		try { action(); }
		catch (InvalidDataException) { return; }
		throw new InvalidOperationException(message);
	}

	private sealed class RecordingAutomation : IVegasAutomationClient
	{
		public List<CleanupCandidateRequest> Requests { get; } = new();
		public List<string> IdempotencyKeys { get; } = new();

		public Task<TResult> ExecuteAsync<TRequest, TResult>(
			string operation,
			TRequest request,
			string idempotencyKey,
			TimeSpan? timeout = null,
			CancellationToken cancellationToken = default)
		{
			if (operation != VegasOperations.CleanupCandidate ||
				request is not CleanupCandidateRequest cleanup)
				throw new InvalidOperationException(
					"Abandon cleanup issued an unexpected automation operation.");
			Requests.Add(cleanup);
			IdempotencyKeys.Add(idempotencyKey);
			return Task.FromResult((TResult)(object)new CleanupCandidateResult());
		}
	}
}
