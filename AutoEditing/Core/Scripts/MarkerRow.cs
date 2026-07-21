using System;
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
		set
		{
			if (_outcome == value) return;
			_outcome = value;
			Changed?.Invoke(this);
		}
	}

	public string Gun
	{
		get { return _gun; }
		set
		{
			if (_gun == value) return;
			_gun = value;
			Changed?.Invoke(this);
		}
	}

	public string Confidence { get; set; }
	public string TemplateId { get; set; }
	public string OriginalLabel { get; set; }
	public List<ShotOutcome> OutcomeOptions { get; set; } = new List<ShotOutcome> { ShotOutcome.Hit, ShotOutcome.Headshot, ShotOutcome.Miss };
	public List<string> GunOptions { get; set; } = new List<string>();

	// Invoked whenever Outcome or Gun changes so edits commit to VEGAS immediately;
	// otherwise a later "refresh after nudge" would silently discard unsaved edits.
	public Action<MarkerRow> Changed { get; set; }
}
