namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class VegasHostIdentity
{
	public string MachineName { get; set; } = "";
	public int ProcessId { get; set; }
	public string VegasVersion { get; set; } = "";
	public string ProjectPath { get; set; } = "";
	public string ProjectFingerprint { get; set; } = "";
}
