namespace Core.Domain.Editing;

public enum MontageSongPlanningDiagnosticSeverity
{
	Information,
	Warning,
	Error
}

public sealed class MontageSongPlanningDiagnostic
{
	public string Code { get; set; }

	public MontageSongPlanningDiagnosticSeverity Severity { get; set; }

	public string Message { get; set; }

	public string EventId { get; set; }

	public string RegionId { get; set; }
}
