using System.Collections.Generic;

namespace AutoEditing.Iteration.Contracts.Iterations;

public sealed class EditDecisionRecord
{
	public string DecisionId { get; set; } = "";
	public string Summary { get; set; } = "";
	public double Confidence { get; set; }
	public IList<string> EvidenceIds { get; set; } = new List<string>();
}
