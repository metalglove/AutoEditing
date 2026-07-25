using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain.Audio.SongAnalysis;

namespace Core.Domain.Editing;

public sealed class SongAnalysisPlanningInputAdapter
{
	private const double BoundaryToleranceSeconds = 0.001;

	public MontageSongPlanningInput Create(SongAnalysis analysis, MontageSongPlanningMode mode = MontageSongPlanningMode.ReviewedSongMap)
	{
		if (analysis == null) throw new ArgumentNullException(nameof(analysis));
		if (analysis.Song == null) throw new ArgumentException("Song analysis has no song identity.", nameof(analysis));

		MontageSongPlanningInput input = new MontageSongPlanningInput
		{
			Mode = mode,
			SongFingerprint = analysis.Song.ContentFingerprint,
			SongDurationSeconds = analysis.Song.DurationSeconds
		};

		foreach (MusicRegion region in (analysis.Regions ?? new List<MusicRegion>())
			.Where((MusicRegion item) => item != null && item.ReviewState == MusicAnalysisReviewState.Reviewed)
			.OrderBy((MusicRegion item) => item.StartSeconds)
			.ThenBy((MusicRegion item) => item.EndSeconds)
			.ThenBy((MusicRegion item) => item.Id, StringComparer.Ordinal))
		{
			input.Regions.Add(new MontageSongPlanningRegion
			{
				Id = region.Id,
				StartSeconds = region.StartSeconds,
				EndSeconds = region.EndSeconds,
				Type = region.Type,
				IsLocked = region.Editorial?.IsLocked == true
			});
		}

		foreach (MusicEvent musicEvent in (analysis.Events ?? new List<MusicEvent>())
			.Where((MusicEvent item) => item != null && item.ReviewState != MusicAnalysisReviewState.Rejected)
			.OrderBy((MusicEvent item) => item.TimeSeconds)
			.ThenBy((MusicEvent item) => item.Id, StringComparer.Ordinal))
		{
			AddEvent(input, musicEvent);
		}
		PromoteSuggestedGameplayAnchors(input);

		return input;
	}

	private static void PromoteSuggestedGameplayAnchors(MontageSongPlanningInput input)
	{
		List<MontageSongPlanningEvent> candidates = input.Events
			.Where((MontageSongPlanningEvent item) => item.Classification == MontageSongEventClassification.None)
			.Where((MontageSongPlanningEvent item) => input.Regions.Count == 0 || input.Regions.Any((MontageSongPlanningRegion region) => region.Id == item.ContainingRegionId && region.Type != MusicRegionType.Unused))
			.OrderBy((MontageSongPlanningEvent item) => item.EffectiveTimeSeconds)
			.ThenBy((MontageSongPlanningEvent item) => item.Id, StringComparer.Ordinal)
			.ToList();
		foreach (MontageSongPlanningEvent candidate in candidates)
		{
			candidate.Classification |= MontageSongEventClassification.GameplayAnchor;
			candidate.Uses.Add(EditorialUse.GameplayAnchor);
			candidate.IsSuggestedGameplayAnchor = true;
			candidate.Priority = Math.Max(candidate.Priority, SuggestedPriority(candidate.MusicalType));
		}
		if (candidates.Count > 0)
		{
			int explicitCount = input.Events.Count((MontageSongPlanningEvent item) => item.IsGameplayAnchor && !item.IsSuggestedGameplayAnchor);
			input.Diagnostics.Add(new MontageSongPlanningDiagnostic
			{
				Code = "suggested-gameplay-anchors",
				Severity = MontageSongPlanningDiagnosticSeverity.Information,
				Message = candidates.Count + " detected musical events are available as automatic gameplay suggestions alongside " + explicitCount + " explicit gameplay anchors."
			});
		}
	}

	private static int SuggestedPriority(MusicEventType type)
	{
		if (type == MusicEventType.Drop) return 80;
		if (type == MusicEventType.BuildHit) return 70;
		if (type == MusicEventType.Accent) return 60;
		if (type == MusicEventType.PhraseBoundary) return 50;
		if (type == MusicEventType.ManualSyncPoint) return 45;
		if (type == MusicEventType.Downbeat) return 40;
		if (type == MusicEventType.Transient) return 20;
		return 10;
	}

	private static void AddEvent(MontageSongPlanningInput input, MusicEvent musicEvent)
	{
		EditorialMetadata editorial = musicEvent.Editorial ?? new EditorialMetadata();
		List<EditorialUse> uses = (editorial.Assignments ?? new List<EditorialAssignment>())
			.Where((EditorialAssignment item) => item != null && item.Use != EditorialUse.None)
			.Select((EditorialAssignment item) => item.Use)
			.Distinct()
			.ToList();
		double offset = editorial.TimingOffsetSeconds ?? 0.0;
		double effectiveTime = musicEvent.TimeSeconds + offset;
		MontageSongPlanningRegion sourceRegion = FindContainingRegion(input.Regions, musicEvent.TimeSeconds);
		MontageSongPlanningRegion effectiveRegion = FindContainingRegion(input.Regions, effectiveTime);

		MontageSongPlanningEvent planningEvent = new MontageSongPlanningEvent
		{
			Id = musicEvent.Id,
			SourceTimeSeconds = musicEvent.TimeSeconds,
			EffectiveTimeSeconds = effectiveTime,
			ContainingRegionId = effectiveRegion?.Id,
			MusicalType = musicEvent.Type,
			Classification = Classify(uses),
			Uses = uses,
			Priority = editorial.Priority,
			IsLocked = editorial.IsLocked,
			Intensity = editorial.Intensity,
			IsReviewed = musicEvent.ReviewState == MusicAnalysisReviewState.Reviewed
		};
		input.Events.Add(planningEvent);

		if (double.IsNaN(effectiveTime) || double.IsInfinity(effectiveTime) || effectiveTime < -BoundaryToleranceSeconds || effectiveTime > input.SongDurationSeconds + BoundaryToleranceSeconds)
		{
			AddDiagnostic(input, "event-offset-outside-song", MontageSongPlanningDiagnosticSeverity.Error,
				"The editorial timing offset moves this event outside the song.", planningEvent, sourceRegion);
		}
		else if (sourceRegion != null && effectiveRegion?.Id != sourceRegion.Id)
		{
			AddDiagnostic(input, "event-offset-crosses-region", editorial.IsLocked || sourceRegion.IsLocked
				? MontageSongPlanningDiagnosticSeverity.Error
				: MontageSongPlanningDiagnosticSeverity.Warning,
				"The editorial timing offset moves this event outside its reviewed region.", planningEvent, sourceRegion);
		}
		else if (sourceRegion == null)
		{
			AddDiagnostic(input, "event-without-region", MontageSongPlanningDiagnosticSeverity.Warning,
				"The reviewed event is not contained by a reviewed song region.", planningEvent, null);
		}

		if (planningEvent.IsIntentionallyUnused && uses.Count > 1)
		{
			AddDiagnostic(input, "unused-event-has-other-uses", MontageSongPlanningDiagnosticSeverity.Error,
				"An intentionally unused event cannot also have gameplay, effect, or structural uses.", planningEvent, sourceRegion);
		}
	}

	private static MontageSongPlanningRegion FindContainingRegion(IEnumerable<MontageSongPlanningRegion> regions, double timeSeconds)
	{
		return regions
			.Where((MontageSongPlanningRegion item) => timeSeconds >= item.StartSeconds - BoundaryToleranceSeconds && timeSeconds <= item.EndSeconds + BoundaryToleranceSeconds)
			.OrderBy((MontageSongPlanningRegion item) => item.EndSeconds - item.StartSeconds)
			.ThenByDescending((MontageSongPlanningRegion item) => item.StartSeconds)
			.ThenBy((MontageSongPlanningRegion item) => item.Id, StringComparer.Ordinal)
			.FirstOrDefault();
	}

	private static MontageSongEventClassification Classify(IEnumerable<EditorialUse> uses)
	{
		MontageSongEventClassification result = MontageSongEventClassification.None;
		foreach (EditorialUse use in uses)
		{
			if (use == EditorialUse.GameplayAnchor) result |= MontageSongEventClassification.GameplayAnchor;
			else if (use == EditorialUse.Flash || use == EditorialUse.ScreenPump || use == EditorialUse.Shake || use == EditorialUse.SpeedChange) result |= MontageSongEventClassification.Effect;
			else if (use == EditorialUse.IntentionallyUnused) result |= MontageSongEventClassification.IntentionallyUnused;
			else if (use != EditorialUse.None) result |= MontageSongEventClassification.Structural;
		}
		return result;
	}

	private static void AddDiagnostic(MontageSongPlanningInput input, string code, MontageSongPlanningDiagnosticSeverity severity, string message, MontageSongPlanningEvent musicEvent, MontageSongPlanningRegion region)
	{
		input.Diagnostics.Add(new MontageSongPlanningDiagnostic
		{
			Code = code,
			Severity = severity,
			Message = message,
			EventId = musicEvent?.Id,
			RegionId = region?.Id
		});
	}
}
