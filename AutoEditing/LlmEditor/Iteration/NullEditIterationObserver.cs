namespace AutoEditing.LlmEditor.Iteration;

internal sealed class NullEditIterationObserver : IEditIterationObserver
{
	public Task OnSnapshotAsync(EditIterationSnapshot snapshot, CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
