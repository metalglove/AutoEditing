using System.Collections.Generic;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class AutomationError
{
	public string Code { get; set; } = "";
	public string Stage { get; set; } = "";
	public string Message { get; set; } = "";
	public bool IsTransient { get; set; }
	public IDictionary<string, string> Details { get; set; } = new Dictionary<string, string>();
}
