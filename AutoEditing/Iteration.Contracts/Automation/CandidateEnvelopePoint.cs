using System;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class CandidateEnvelopePoint
{
	public TimeSpan Offset { get; set; }
	public double Value { get; set; }
}
