using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain.Audio.SongAnalysis;

namespace Core.Domain.Editing;

public sealed class AutomaticEffectTreatmentPlanner
{
	public EffectTreatmentPlan Plan(SongAnalysis analysis, AutomaticEffectTreatmentPreset preset = null)
	{
		return Plan(analysis, preset, null);
	}

	public EffectTreatmentPlan Plan(SongAnalysis analysis, AutomaticEffectTreatmentPreset preset, EffectSelectionOptions options)
	{
		if (analysis == null) throw new ArgumentNullException(nameof(analysis));
		options = options ?? new EffectSelectionOptions();
		options.Validate();
		preset = preset ?? new AutomaticEffectTreatmentPreset();
		if (preset.SchemaVersion != AutomaticEffectTreatmentPreset.CurrentSchemaVersion) throw new NotSupportedException("Unsupported effect preset schema version " + preset.SchemaVersion + ".");
		if (string.IsNullOrWhiteSpace(preset.Id) || preset.Revision < 1) throw new ArgumentException("The effect preset needs a stable ID and positive revision.", nameof(preset));
		EffectTreatmentPlan result = new EffectTreatmentPlan { PresetSchemaVersion = preset.SchemaVersion, PresetId = preset.Id, PresetRevision = preset.Revision, Seed = preset.Seed };
		List<MusicEvent> events = (analysis.Events ?? new List<MusicEvent>())
			.Where(item => item != null && item.ReviewState != MusicAnalysisReviewState.Rejected)
			.OrderBy(item => item.TimeSeconds)
			.ThenBy(item => item.Id, StringComparer.Ordinal)
			.ToList();
		List<MusicRegion> regions = (analysis.Regions ?? new List<MusicRegion>())
			.Where(item => item != null && item.EndSeconds >= item.StartSeconds)
			.OrderBy(item => item.StartSeconds)
			.ToList();

		foreach (MusicEvent musicEvent in events)
		{
			MusicRegion region = RegionAt(regions, musicEvent.TimeSeconds);
			List<EditorialAssignment> assignments = musicEvent.Editorial?.Assignments ?? new List<EditorialAssignment>();
			if (assignments.Any(item => item?.Use == EditorialUse.IntentionallyUnused))
			{
				result.Diagnostics.Add(Diagnostic(musicEvent, "intentionally-unused", "No treatment was planned because the event is intentionally unused."));
				continue;
			}

			List<EditorialUse> manual = assignments
				.Where(item => item != null && item.Origin == EditorialAssignmentOrigin.UserChosen && AutomaticEffectTreatmentPreset.IsSupported(item.Use))
				.Select(item => item.Use)
				.Where(options.Allows)
				.Distinct()
				.ToList();
			if (options.IncludeManualTreatments)
				foreach (EditorialUse use in manual)
					AddManual(result, musicEvent, use, preset);
			else
				manual.Clear();

			List<EditorialUse> suggestions = assignments
				.Where(item => item != null && item.Origin == EditorialAssignmentOrigin.Suggested && AutomaticEffectTreatmentPreset.IsSupported(item.Use))
				.Select(item => item.Use)
				.Concat(preset.EnableAutomaticTreatments ? Suggest(musicEvent, region, preset) : Enumerable.Empty<EditorialUse>())
				.Where(options.Allows)
				.Distinct()
				.ToList();
			foreach (EditorialUse suggested in suggestions)
			{
				string category = AutomaticEffectTreatmentPreset.Category(suggested);
				if (manual.Any(use => AutomaticEffectTreatmentPreset.Category(use) == category))
				{
					result.Diagnostics.Add(Diagnostic(musicEvent, "manual-override", "Automatic " + suggested + " was suppressed by a manual " + category + " assignment."));
					continue;
				}
				if (region?.Type == MusicRegionType.Unused)
				{
					result.Diagnostics.Add(Diagnostic(musicEvent, "unused-region", "Automatic " + suggested + " was suppressed because the event is in an unused region."));
					continue;
				}
				TryAddAutomatic(result, musicEvent, region, suggested, preset);
			}
		}
		return result;
	}

	private static IEnumerable<EditorialUse> Suggest(MusicEvent musicEvent, MusicRegion region, AutomaticEffectTreatmentPreset preset)
	{
		if (region?.Type == MusicRegionType.Unused) yield break;
		bool activeVisualRegion = region == null || region.Type == MusicRegionType.Action || region.Type == MusicRegionType.Climax || region.Type == MusicRegionType.BuildUp;
		if (activeVisualRegion && musicEvent.Type == MusicEventType.Drop)
		{
			yield return EditorialUse.ScreenPump;
			yield return EditorialUse.SpeedChange;
		}
		else if (activeVisualRegion && musicEvent.Type == MusicEventType.BuildHit) yield return EditorialUse.ScreenPump;
		else if (activeVisualRegion && musicEvent.Type == MusicEventType.Accent && Unit(preset.Seed, musicEvent.Id, "accent") >= 0.38) yield return EditorialUse.Flash;

		if (musicEvent.Type != MusicEventType.PhraseBoundary) yield break;
		if (region?.Type == MusicRegionType.Intro) yield return EditorialUse.TitleReveal;
		else if (region?.Type == MusicRegionType.Cinematic || region?.Type == MusicRegionType.Outro) yield return EditorialUse.CinematicTransition;
		else if (Unit(preset.Seed, musicEvent.Id, "cut") >= 0.68) yield return EditorialUse.CutOrTransition;
	}

	private static void AddManual(EffectTreatmentPlan plan, MusicEvent musicEvent, EditorialUse use, AutomaticEffectTreatmentPreset preset)
	{
		double variation = Unit(preset.Seed, musicEvent.Id, use.ToString());
		string recipe = Recipe(use, musicEvent.Type, musicEvent.Editorial?.Intensity);
		plan.Actions.Add(new EffectTreatmentAction
		{
			EventId = musicEvent.Id,
			TimeSeconds = musicEvent.TimeSeconds + (musicEvent.Editorial?.TimingOffsetSeconds ?? 0),
			Type = use,
			RecipeId = recipe,
			Intensity = Clamp((musicEvent.Editorial?.Intensity ?? (0.65 + variation * 0.2)) * preset.IntensityScale),
			DurationSeconds = Duration(preset, use, recipe, variation),
			Origin = EffectTreatmentOrigin.Manual,
			Reason = "Explicit user editorial assignment."
		});
	}

	private static void TryAddAutomatic(EffectTreatmentPlan plan, MusicEvent musicEvent, MusicRegion region, EditorialUse use, AutomaticEffectTreatmentPreset preset)
	{
		if (preset.DensityScale <= 0)
		{
			plan.Diagnostics.Add(Diagnostic(musicEvent, "density-disabled", use + " was omitted because automatic effect density is zero."));
			return;
		}
		string category = AutomaticEffectTreatmentPreset.Category(use);
		List<EffectTreatmentAction> prior = plan.Actions.Where(item => item.Origin == EffectTreatmentOrigin.Automatic && AutomaticEffectTreatmentPreset.Category(item.Type) == category).ToList();
		double spacing = category == "visual" ? preset.MinimumSecondsBetweenVisualAccents :
			category == "speed" ? preset.MinimumSecondsBetweenSpeedChanges : preset.MinimumSecondsBetweenStructuralTreatments;
		if (prior.Any(item => Math.Abs(item.TimeSeconds - musicEvent.TimeSeconds) < spacing))
		{
			plan.Diagnostics.Add(Diagnostic(musicEvent, "spacing-limit", use + " was omitted by the " + category + " spacing limit."));
			return;
		}
		int baseMaximum = category == "visual" ? preset.MaximumVisualAccentsPerRegion :
			category == "speed" ? preset.MaximumSpeedChangesPerRegion : preset.MaximumStructuralTreatmentsPerRegion;
		int maximum = Math.Max(1, (int)Math.Round(baseMaximum * preset.RegionDensityScale(region?.Type ?? MusicRegionType.Action)));
		int inRegion = prior.Count(item => SameRegion(item.TimeSeconds, region));
		if (inRegion >= maximum)
		{
			plan.Diagnostics.Add(Diagnostic(musicEvent, "region-density-limit", use + " was omitted by the region density limit."));
			return;
		}
		List<EffectTreatmentAction> recent = plan.Actions.OrderByDescending(item => item.TimeSeconds).Take(preset.MaximumConsecutiveSameType).ToList();
		if (recent.Count == preset.MaximumConsecutiveSameType && recent.All(item => item.Type == use))
		{
			plan.Diagnostics.Add(Diagnostic(musicEvent, "repetition-limit", use + " was omitted to avoid repetitive treatment."));
			return;
		}
		double variation = Unit(preset.Seed, musicEvent.Id, use.ToString());
		double strength = musicEvent.Strength ?? 0.65;
		string recipe = Recipe(use, musicEvent.Type, null);
		plan.Actions.Add(new EffectTreatmentAction
		{
			EventId = musicEvent.Id,
			TimeSeconds = musicEvent.TimeSeconds + (musicEvent.Editorial?.TimingOffsetSeconds ?? 0),
			Type = use,
			RecipeId = recipe,
			Intensity = Clamp(AutomaticIntensity(use, musicEvent.Type, Clamp((0.45 + strength * 0.35 + variation * 0.1) * preset.RegionIntensityScale(region?.Type ?? MusicRegionType.Action))) * preset.IntensityScale),
			DurationSeconds = Duration(preset, use, recipe, variation),
			Origin = EffectTreatmentOrigin.Automatic,
			Reason = "Conservative default for " + musicEvent.Type + " in " + (region?.Type.ToString() ?? "unmapped") + " region."
		});
	}

	private static double Duration(AutomaticEffectTreatmentPreset preset, EditorialUse use, string recipe, double variation)
	{
		if (recipe == "native.pump.subtle") return 0.16 + 0.04 * variation;
		if (recipe == "native.pump.medium") return 0.18 + 0.06 * variation;
		if (recipe == "native.pump.impact") return 0.20 + 0.10 * variation;
		return preset.Duration(use, variation);
	}

	private static double AutomaticIntensity(EditorialUse use, MusicEventType eventType, double value)
	{
		if (use != EditorialUse.ScreenPump) return value;
		if (eventType == MusicEventType.Drop) return Math.Max(0.75, value);
		if (eventType == MusicEventType.BuildHit) return Math.Max(0.45, Math.Min(0.74, value));
		return Math.Min(0.44, value);
	}

	private static string Recipe(EditorialUse use, MusicEventType eventType, double? explicitIntensity)
	{
		if (use == EditorialUse.ScreenPump)
		{
			if (explicitIntensity.HasValue)
			{
				if (explicitIntensity.Value >= 0.75) return "native.pump.impact";
				if (explicitIntensity.Value >= 0.45) return "native.pump.medium";
				return "native.pump.subtle";
			}
			if (eventType == MusicEventType.Drop) return "native.pump.impact";
			if (eventType == MusicEventType.BuildHit) return "native.pump.medium";
			return "native.pump.subtle";
		}
		if (use == EditorialUse.Flash) return "native.flash.short";
		if (use == EditorialUse.Shake) return "native.shake.micro";
		if (use == EditorialUse.SpeedChange) return "native.speed.impact";
		if (use == EditorialUse.TitleReveal) return "native.title.reveal";
		if (use == EditorialUse.CinematicTransition) return "native.transition.cinematic";
		if (use == EditorialUse.CutOrTransition) return "native.transition.short";
		return "native.none";
	}

	private static MusicRegion RegionAt(IEnumerable<MusicRegion> regions, double time)
	{
		return regions.FirstOrDefault(item => time >= item.StartSeconds && time <= item.EndSeconds);
	}

	private static bool SameRegion(double time, MusicRegion region)
	{
		return region == null || (time >= region.StartSeconds && time <= region.EndSeconds);
	}

	private static EffectTreatmentDiagnostic Diagnostic(MusicEvent musicEvent, string code, string message)
	{
		return new EffectTreatmentDiagnostic { EventId = musicEvent.Id, TimeSeconds = musicEvent.TimeSeconds, Code = code, Message = message };
	}

	private static double Clamp(double value) => Math.Max(0, Math.Min(1, value));

	private static double Unit(int seed, string eventId, string salt)
	{
		unchecked
		{
			uint hash = 2166136261;
			string input = seed + "|" + (eventId ?? string.Empty) + "|" + salt;
			foreach (char character in input) hash = (hash ^ character) * 16777619;
			return (hash & 0x00ffffff) / 16777215.0;
		}
	}
}
