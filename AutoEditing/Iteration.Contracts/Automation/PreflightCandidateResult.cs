using System.Collections.Generic;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class PreflightCandidateResult
{
	public bool IsReady { get; set; }
	public IList<AutomationError> Errors { get; set; } = new List<AutomationError>();
	public IList<string> Warnings { get; set; } = new List<string>();
}
