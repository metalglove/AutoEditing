using System;

namespace AutoEditing.Iteration.Contracts.Evidence;

public sealed class EditEvidenceReference
{
	public string EvidenceId { get; set; } = "";
	public string RelativePath { get; set; } = "";
	public string MediaType { get; set; } = "";
	public TimeSpan TimelineStart { get; set; }
	public TimeSpan TimelineEnd { get; set; }
	public string Sha256 { get; set; } = "";
	public string Description { get; set; } = "";
}
