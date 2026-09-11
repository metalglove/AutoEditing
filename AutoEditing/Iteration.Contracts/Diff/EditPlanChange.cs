namespace AutoEditing.Iteration.Contracts.Diff;

public sealed class EditPlanChange
{
	public string Path { get; set; } = "";
	public string ChangeKind { get; set; } = "";
	public string Before { get; set; } = "";
	public string After { get; set; } = "";
}
