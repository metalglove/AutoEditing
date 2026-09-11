using System.Collections.Generic;
using Core.Domain.Editing;

namespace Core.Domain.Planning;

public sealed class EditPlanningRequest
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;

	public string RequestId { get; set; }

	public List<Core.Domain.Clip.Clip> Clips { get; set; } = new List<Core.Domain.Clip.Clip>();

	public string SongPath { get; set; }

	public MontageSongPlanningInput SongAnalysis { get; set; }

	public EffectSelectionOptions EffectOptions { get; set; } = new EffectSelectionOptions();

	public string CreativeBrief { get; set; }

	public List<string> StyleProfileIds { get; set; } = new List<string>();
}
