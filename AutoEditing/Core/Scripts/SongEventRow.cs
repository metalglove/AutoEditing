using System.Collections.Generic;
using Core.Domain.Audio.SongAnalysis;

namespace Core.Scripts;

public sealed class SongEventRow
{
	internal MusicEvent Model { get; }
	private readonly bool _originalIncluded;
	private readonly MusicEventType _originalType;
	public bool IsIncluded { get; set; }
	public string Time { get; }
	public MusicEventType Type { get; set; }
	public string Strength { get; }
	public string Confidence { get; }
	public string Origin { get; }
	public List<MusicEventType> TypeOptions { get; } = new List<MusicEventType>
	{
		MusicEventType.Beat, MusicEventType.Downbeat, MusicEventType.Accent,
		MusicEventType.Transient, MusicEventType.BuildHit, MusicEventType.Drop,
		MusicEventType.PhraseBoundary, MusicEventType.ManualSyncPoint
	};

	internal SongEventRow(MusicEvent model)
	{
		Model = model;
		IsIncluded = model.ReviewState != MusicAnalysisReviewState.Rejected;
		_originalIncluded = IsIncluded;
		Time = model.TimeSeconds.ToString("0.000s");
		Type = model.Type;
		_originalType = Type;
		Strength = model.Strength.HasValue ? model.Strength.Value.ToString("P0") : "—";
		Confidence = model.Confidence.HasValue ? model.Confidence.Value.ToString("P0") : "—";
		Origin = model.Origin.ToString();
	}

	internal void Apply()
	{
		if (Type != _originalType || IsIncluded != _originalIncluded)
		{
			Model.Type = Type;
			Model.ReviewState = IsIncluded ? MusicAnalysisReviewState.Reviewed : MusicAnalysisReviewState.Rejected;
		}
	}
}
