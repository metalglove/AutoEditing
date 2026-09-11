namespace AutoEditing.LlmEditor.Iteration;

internal interface IEditIterationObserver
{
	Task OnSnapshotAsync(EditIterationSnapshot snapshot, CancellationToken cancellationToken);
}
