using System.Collections.Generic;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class CandidateTrackSnapshot
{
	public int Index { get; set; }
	public string Name { get; set; } = "";
	public string MediaKind { get; set; } = "";
	public bool Muted { get; set; }
	public bool Solo { get; set; }
	public double Gain { get; set; } = 1.0;
	public IList<CandidateEnvelopePoint> VolumeAutomation { get; set; } =
		new List<CandidateEnvelopePoint>();
	public IList<CandidateEventSnapshot> Events { get; set; } = new List<CandidateEventSnapshot>();
}
