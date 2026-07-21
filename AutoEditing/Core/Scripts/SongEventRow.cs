using System;
using System.Collections.Generic;
using System.Linq;
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
	public EditorialUse AnchorUse { get; set; }
	public EditorialUse VisualUse { get; set; }
	public EditorialUse TimingUse { get; set; }
	public int Priority { get; set; }
	public bool IsLocked { get; set; }
	public double? TimingOffsetSeconds { get; set; }
	public double? Intensity { get; set; }
	public string Notes { get; set; }
	public List<DisplayChoice<EditorialUse>> AnchorUseOptions { get; } = new List<DisplayChoice<EditorialUse>> { Choice(EditorialUse.None, "Unassigned"), Choice(EditorialUse.GameplayAnchor, "Gameplay / kill anchor"), Choice(EditorialUse.CutOrTransition, "Cut / transition"), Choice(EditorialUse.TitleReveal, "Title reveal"), Choice(EditorialUse.CinematicTransition, "Cinematic transition"), Choice(EditorialUse.IntentionallyUnused, "Intentionally unused") };
	public List<DisplayChoice<EditorialUse>> VisualUseOptions { get; } = new List<DisplayChoice<EditorialUse>> { Choice(EditorialUse.None, "Unassigned"), Choice(EditorialUse.Flash, "Flash"), Choice(EditorialUse.ScreenPump, "Screen pump / punch-in"), Choice(EditorialUse.Shake, "Shake") };
	public List<DisplayChoice<EditorialUse>> TimingUseOptions { get; } = new List<DisplayChoice<EditorialUse>> { Choice(EditorialUse.None, "Unassigned"), Choice(EditorialUse.SpeedChange, "Speed change") };
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
		EditorialMetadata editorial = model.Editorial ?? new EditorialMetadata();
		List<EditorialAssignment> assignments = editorial.Assignments ?? new List<EditorialAssignment>();
		AnchorUse = Assigned(assignments, "structural anchor");
		VisualUse = Assigned(assignments, "visual accent");
		TimingUse = Assigned(assignments, "timing effect");
		Priority = editorial.Priority;
		IsLocked = editorial.IsLocked;
		TimingOffsetSeconds = editorial.TimingOffsetSeconds;
		Intensity = editorial.Intensity;
		Notes = editorial.Notes;
	}

	internal void Apply()
	{
		if (Type != _originalType || IsIncluded != _originalIncluded)
		{
			Model.Type = Type;
			Model.ReviewState = IsIncluded ? MusicAnalysisReviewState.Reviewed : MusicAnalysisReviewState.Rejected;
		}
		EditorialMetadata editorial = Model.Editorial ?? new EditorialMetadata();
		editorial.Priority = Priority;
		editorial.IsLocked = IsLocked;
		editorial.TimingOffsetSeconds = TimingOffsetSeconds;
		editorial.Intensity = Intensity;
		editorial.Notes = Notes;
		editorial.Assignments = new[] { AnchorUse, VisualUse, TimingUse }
			.Where((EditorialUse use) => use != EditorialUse.None)
			.Select((EditorialUse use) => new EditorialAssignment { Use = use, Origin = EditorialAssignmentOrigin.UserChosen })
			.ToList();
		Model.Editorial = editorial;
	}

	private static EditorialUse Assigned(IEnumerable<EditorialAssignment> assignments, string category)
	{
		EditorialAssignment assignment = assignments.FirstOrDefault((EditorialAssignment item) => item != null && EditorialMetadataValidator.Category(item.Use) == category);
		return assignment?.Use ?? EditorialUse.None;
	}

	private static DisplayChoice<EditorialUse> Choice(EditorialUse value, string label) { return new DisplayChoice<EditorialUse>(value, label); }
}
