namespace AutoEditing.LlmEditor.Automation;

internal sealed class VegasAutomationClientOptions
{
	public required string SpoolRoot { get; init; }
	public required string SessionId { get; init; }
	public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromMinutes(2);
	public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);
	public string ExpectedProjectFingerprint { get; init; } = "";

	public void Validate()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(SpoolRoot);
		ArgumentException.ThrowIfNullOrWhiteSpace(SessionId);
		if (DefaultTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(DefaultTimeout));
		if (PollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(PollInterval));
	}
}
