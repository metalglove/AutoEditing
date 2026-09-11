using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.LlmEditor.Automation;

namespace AutoEditing.LlmEditor.Assembly;

internal static class CandidateAbandonCleanup
{
	public static async Task CleanupAsync(
		IVegasAutomationClient automation,
		CandidateWorkspaceId workspace,
		string actionId,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(automation);
		workspace?.Validate();
		if (workspace == null)
			throw new ArgumentNullException(nameof(workspace));
		if (string.IsNullOrWhiteSpace(actionId) ||
			actionId.Any(character =>
				!char.IsLetterOrDigit(character) &&
				character is not ('-' or '_' or '.')))
			throw new InvalidDataException(
				"An abandon action requires a safe stable action ID.");
		await automation.ExecuteAsync<
			CleanupCandidateRequest,
			CleanupCandidateResult>(
				VegasOperations.CleanupCandidate,
				new CleanupCandidateRequest { Workspace = workspace },
				"abandon-" + actionId + "-cleanup",
				cancellationToken: cancellationToken);
	}
}
