using System.Collections.Generic;

namespace AutoEditing.Iteration.Contracts.Diff;

public sealed class EditPlanDiff
{
	public string BeforeHash { get; set; } = "";
	public string AfterHash { get; set; } = "";
	public IList<EditPlanChange> Changes { get; set; } = new List<EditPlanChange>();
}
