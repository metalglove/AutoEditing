using System;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class CandidateVelocityPoint
{
	public TimeSpan Offset { get; set; }
	public double Velocity { get; set; }
}
