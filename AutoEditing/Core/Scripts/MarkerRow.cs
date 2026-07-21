using System.Collections.Generic;
using Core.Domain.Audio;

namespace Core.Scripts;

public sealed class MarkerRow
{
	private ShotOutcome _outcome;
	private string _gun;

	public double TimelineSeconds { get; set; }
	public string Time { get; set; }

	public ShotOutcome Outcome
	{
		get { return _outcome; }
		set { _outcome = value; }
	}

	public string Gun
	{
		get { return _gun; }
		set { _gun = value; }
	}

	public string Confidence { get; set; }
	public double DetectionConfidence { get; set; }
	public string TemplateId { get; set; }
	public ShotEventOrigin Origin { get; set; }
	public string OriginalLabel { get; set; }
	public List<ShotOutcome> OutcomeOptions { get; set; } = new List<ShotOutcome> { ShotOutcome.Hit, ShotOutcome.Headshot, ShotOutcome.Miss };
	public List<string> GunOptions { get; set; } = new List<string>();
}
