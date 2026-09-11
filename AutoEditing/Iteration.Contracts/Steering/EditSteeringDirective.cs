using System;
using System.Collections.Generic;

namespace AutoEditing.Iteration.Contracts.Steering;

public sealed class EditSteeringDirective
{
	public string DirectiveId { get; set; } = "";
	public EditSteeringKind Kind { get; set; }
	public string Instruction { get; set; } = "";
	public IList<string> TargetIds { get; set; } = new List<string>();
	public TimeSpan? TimelineStart { get; set; }
	public TimeSpan? TimelineEnd { get; set; }
	public int ApplicableFromIteration { get; set; }
	public bool IsActive { get; set; } = true;
}
