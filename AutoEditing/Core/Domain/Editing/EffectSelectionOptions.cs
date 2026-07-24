using System;
using Core.Domain.Audio.SongAnalysis;

namespace Core.Domain.Editing;

/// <summary>Serializable choices from the effect-selection wizard stage.</summary>
public sealed class EffectSelectionOptions
{
	public const int CurrentSchemaVersion = 1;
	public const string ConservativePresetId = "autoediting.sniper.conservative";
	public const string NoAutomaticEffectsPresetId = "autoediting.none";

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string PresetId { get; set; } = ConservativePresetId;
	public double Intensity { get; set; } = 1.0;
	public double Density { get; set; } = 1.0;
	public bool IncludeManualTreatments { get; set; } = true;
	public bool EnableScreenPumps { get; set; } = true;
	public bool EnableFlashes { get; set; } = true;
	public bool EnableShake { get; set; } = true;
	public bool EnableTransitions { get; set; } = true;
	public bool EnableTitles { get; set; } = true;
	public bool EnableSpeedChanges { get; set; } = true;

	public void Validate()
	{
		if (SchemaVersion != CurrentSchemaVersion) throw new NotSupportedException("Unsupported effect selection schema version " + SchemaVersion + ".");
		if (PresetId != ConservativePresetId && PresetId != NoAutomaticEffectsPresetId) throw new ArgumentException("Unknown built-in effect preset '" + PresetId + "'.", nameof(PresetId));
		if (Intensity < 0 || Intensity > 2) throw new ArgumentOutOfRangeException(nameof(Intensity), "Effect intensity must be between 0 and 2.");
		if (Density < 0 || Density > 2) throw new ArgumentOutOfRangeException(nameof(Density), "Effect density must be between 0 and 2.");
	}

	public bool Allows(EditorialUse use)
	{
		switch (use)
		{
			case EditorialUse.ScreenPump: return EnableScreenPumps;
			case EditorialUse.Flash: return EnableFlashes;
			case EditorialUse.Shake: return EnableShake;
			case EditorialUse.CutOrTransition:
			case EditorialUse.CinematicTransition: return EnableTransitions;
			case EditorialUse.TitleReveal: return EnableTitles;
			case EditorialUse.SpeedChange: return EnableSpeedChanges;
			default: return false;
		}
	}

	public AutomaticEffectTreatmentPreset CreatePreset()
	{
		Validate();
		AutomaticEffectTreatmentPreset preset = PresetId == NoAutomaticEffectsPresetId
			? AutomaticEffectTreatmentPreset.None()
			: new AutomaticEffectTreatmentPreset();
		preset.IntensityScale = Intensity;
		preset.DensityScale = Density;
		return preset;
	}
}
