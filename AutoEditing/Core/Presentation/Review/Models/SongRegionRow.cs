using System.Collections.Generic;
using Core.Domain.Audio.SongAnalysis;

namespace Core.Scripts;

public sealed class SongRegionRow
{
	internal MusicRegion Model { get; }
	private readonly bool _originalIncluded;
	private readonly MusicRegionType _originalType;
	public bool IsIncluded { get; set; }
	public string Range { get; }
	public MusicRegionType Type { get; set; }
	public string Energy { get; }
	public string Change { get; }
	public string Confidence { get; }
	public List<MusicRegionType> TypeOptions { get; } = new List<MusicRegionType>
	{
		MusicRegionType.Intro, MusicRegionType.BuildUp, MusicRegionType.Action,
		MusicRegionType.Climax, MusicRegionType.Breakdown, MusicRegionType.Cinematic,
		MusicRegionType.Outro, MusicRegionType.Unused
	};

	internal SongRegionRow(MusicRegion model)
	{
		Model = model;
		IsIncluded = model.ReviewState != MusicAnalysisReviewState.Rejected;
		_originalIncluded = IsIncluded;
		Range = model.StartSeconds.ToString("0.00") + "–" + model.EndSeconds.ToString("0.00") + "s";
		Type = model.Type;
		_originalType = Type;
		Energy = model.Energy.HasValue ? model.Energy.Value.ToString("P0") : "—";
		Change = model.EnergyDelta.HasValue ? model.EnergyDelta.Value.ToString("+0%;-0%;0%") : "—";
		Confidence = model.Confidence.HasValue ? model.Confidence.Value.ToString("P0") : "—";
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
