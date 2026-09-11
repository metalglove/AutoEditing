using System;
using System.Collections.Generic;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class CandidateEventSnapshot
{
	public string PlacementId { get; set; } = "";
	public string MediaPath { get; set; } = "";
	public TimeSpan TimelineStart { get; set; }
	public TimeSpan TimelineDuration { get; set; }
	public TimeSpan SourceOffset { get; set; }
	public double Gain { get; set; }
	public TimeSpan FadeIn { get; set; }
	public TimeSpan FadeOut { get; set; }
	public string FadeInTransition { get; set; } = "";
	public string FadeOutTransition { get; set; } = "";
	public string GroupSignature { get; set; } = "";
	public IList<CandidateVelocityPoint> Velocity { get; set; } = new List<CandidateVelocityPoint>();
	public IList<string> Effects { get; set; } = new List<string>();
}
