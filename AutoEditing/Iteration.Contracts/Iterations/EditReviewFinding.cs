using System.Collections.Generic;

namespace AutoEditing.Iteration.Contracts.Iterations;

public sealed class EditReviewFinding
{
	public string FindingId { get; set; } = "";
	public string Code { get; set; } = "";
	public EditReviewSeverity Severity { get; set; }
	public string Message { get; set; } = "";
	public IList<string> EvidenceIds { get; set; } = new List<string>();
}
