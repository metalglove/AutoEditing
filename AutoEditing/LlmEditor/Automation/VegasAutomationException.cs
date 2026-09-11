using AutoEditing.Iteration.Contracts.Automation;

namespace AutoEditing.LlmEditor.Automation;

internal sealed class VegasAutomationException : Exception
{
	public VegasAutomationException(VegasJobStatus status, AutomationError? error)
		: base(error?.Message ?? "VEGAS automation job ended with status " + status + ".")
	{
		Status = status;
		Error = error;
	}

	public VegasJobStatus Status { get; }
	public AutomationError? Error { get; }
}
