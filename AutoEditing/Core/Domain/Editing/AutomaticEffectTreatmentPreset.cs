using System;
using System.Collections.Generic;
using Core.Domain.Audio.SongAnalysis;

namespace Core.Domain.Editing;

public sealed class AutomaticEffectTreatmentPreset
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;

	public string Id { get; set; } = "autoediting.sniper.conservative";

	public int Revision { get; set; } = 1;

	public string DisplayName { get; set; } = "Sniper - Conservative";

	public bool EnableAutomaticTreatments { get; set; } = true;

	public int Seed { get; set; } = 173;

	public double MinimumSecondsBetweenVisualAccents { get; set; } = 1.25;

	public double MinimumSecondsBetweenStructuralTreatments { get; set; } = 3.0;

	public double MinimumSecondsBetweenSpeedChanges { get; set; } = 4.0;

	public int MaximumVisualAccentsPerRegion { get; set; } = 4;

	public int MaximumStructuralTreatmentsPerRegion { get; set; } = 2;

	public int MaximumSpeedChangesPerRegion { get; set; } = 1;

	public int MaximumConsecutiveSameType { get; set; } = 2;

	public double IntensityScale { get; set; } = 1.0;

	public double DensityScale { get; set; } = 1.0;

	public static AutomaticEffectTreatmentPreset None()
	{
		return new AutomaticEffectTreatmentPreset
		{
			Id = "autoediting.none",
			DisplayName = "No automatic effects",
			EnableAutomaticTreatments = false
		};
	}

	public double RegionDensityScale(MusicRegionType type)
	{
		return RegionIntensityScale(type) * DensityScale;
	}

	public double RegionIntensityScale(MusicRegionType type)
	{
		double regionScale;
		switch (type)
		{
			case MusicRegionType.Intro:
			case MusicRegionType.Outro:
			case MusicRegionType.Cinematic:
				regionScale = 0.45;
				break;
			case MusicRegionType.Breakdown:
				regionScale = 0.55;
				break;
			case MusicRegionType.BuildUp:
				regionScale = 0.75;
				break;
			case MusicRegionType.Climax:
				regionScale = 1.15;
				break;
			default:
				regionScale = 1.0;
				break;
		}
		return regionScale;
	}

	public double Duration(EditorialUse use, double variation)
	{
		switch (use)
		{
			case EditorialUse.Flash: return 0.07 + (0.03 * variation);
			case EditorialUse.ScreenPump: return 0.20 + (0.10 * variation);
			case EditorialUse.Shake: return 0.18 + (0.12 * variation);
			case EditorialUse.SpeedChange: return 0.35 + (0.20 * variation);
			case EditorialUse.CutOrTransition: return 0.30 + (0.20 * variation);
			case EditorialUse.TitleReveal: return 1.00 + (0.50 * variation);
			case EditorialUse.CinematicTransition: return 0.65 + (0.35 * variation);
			default: throw new ArgumentOutOfRangeException(nameof(use), use, "Unsupported treatment type.");
		}
	}

	internal static bool IsSupported(EditorialUse use)
	{
		return use == EditorialUse.Flash || use == EditorialUse.ScreenPump ||
			use == EditorialUse.Shake || use == EditorialUse.SpeedChange ||
			use == EditorialUse.CutOrTransition || use == EditorialUse.TitleReveal ||
			use == EditorialUse.CinematicTransition;
	}

	internal static string Category(EditorialUse use)
	{
		if (use == EditorialUse.Flash || use == EditorialUse.ScreenPump || use == EditorialUse.Shake) return "visual";
		if (use == EditorialUse.SpeedChange) return "speed";
		return "structural";
	}
}
