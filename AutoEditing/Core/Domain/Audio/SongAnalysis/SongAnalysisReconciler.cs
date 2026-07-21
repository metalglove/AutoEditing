using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Domain.Audio.SongAnalysis;

public sealed class SongAnalysisReconciler
{
	public SongAnalysis Reconcile(SongAnalysis existing, SongAnalysis detected, double eventToleranceSeconds = 0.08, double regionToleranceSeconds = 0.5)
	{
		if (existing == null)
		{
			return detected ?? throw new ArgumentNullException(nameof(detected));
		}
		if (detected == null)
		{
			throw new ArgumentNullException(nameof(detected));
		}
		if (!string.Equals(existing.Song?.ContentFingerprint, detected.Song?.ContentFingerprint, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Cannot reconcile analyses for different songs.");
		}

		List<MusicEvent> unmatchedEvents = new List<MusicEvent>(existing.Events.Where((MusicEvent item) => item.Origin == MusicAnalysisOrigin.Detected));
		foreach (MusicEvent proposal in detected.Events.Where((MusicEvent item) => item.Origin == MusicAnalysisOrigin.Detected))
		{
			MusicEvent match = FindEvent(unmatchedEvents, proposal, eventToleranceSeconds);
			if (match != null)
			{
				proposal.Id = match.Id;
				PreserveReviewedEvent(match, proposal);
				unmatchedEvents.Remove(match);
			}
		}
		foreach (MusicEvent preserved in unmatchedEvents.Where((MusicEvent item) => item.ReviewState != MusicAnalysisReviewState.Proposed || item.Editorial?.IsLocked == true))
		{
			PreserveEventById(detected, preserved);
		}
		foreach (MusicEvent manual in existing.Events.Where((MusicEvent item) => item.Origin == MusicAnalysisOrigin.UserCreated))
		{
			PreserveEventById(detected, manual);
		}

		List<MusicRegion> unmatchedRegions = new List<MusicRegion>(existing.Regions.Where((MusicRegion item) => item.Origin == MusicAnalysisOrigin.Detected));
		foreach (MusicRegion proposal in detected.Regions.Where((MusicRegion item) => item.Origin == MusicAnalysisOrigin.Detected))
		{
			MusicRegion match = FindRegion(unmatchedRegions, proposal, regionToleranceSeconds);
			if (match != null)
			{
				proposal.Id = match.Id;
				PreserveReviewedRegion(match, proposal);
				unmatchedRegions.Remove(match);
			}
		}
		foreach (MusicRegion preserved in unmatchedRegions.Where((MusicRegion item) => item.ReviewState != MusicAnalysisReviewState.Proposed || item.Editorial?.IsLocked == true))
		{
			PreserveRegionById(detected, preserved);
		}
		foreach (MusicRegion manual in existing.Regions.Where((MusicRegion item) => item.Origin == MusicAnalysisOrigin.UserCreated))
		{
			PreserveRegionById(detected, manual);
		}
		detected.Id = existing.Id;
		detected.CreatedUtc = existing.CreatedUtc;
		EnsureUniqueIds(detected);
		return detected;
	}

	private static void EnsureUniqueIds(SongAnalysis analysis)
	{
		HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
		foreach (MusicEvent musicEvent in analysis.Events.OrderByDescending(EventIdentityPriority).ThenBy((MusicEvent item) => item.TimeSeconds))
		{
			if (!string.IsNullOrWhiteSpace(musicEvent.Id) && used.Add(musicEvent.Id)) continue;
			musicEvent.Id = UniqueId(analysis.Song.ContentFingerprint, "reconciled-event-" + musicEvent.Type + "-" + Math.Round(musicEvent.DetectedTimeSeconds ?? musicEvent.TimeSeconds, 3), used);
		}
		foreach (MusicRegion region in analysis.Regions.OrderByDescending(RegionIdentityPriority).ThenBy((MusicRegion item) => item.StartSeconds))
		{
			if (!string.IsNullOrWhiteSpace(region.Id) && used.Add(region.Id)) continue;
			region.Id = UniqueId(analysis.Song.ContentFingerprint, "reconciled-region-" + region.Type + "-" + Math.Round(region.DetectedStartSeconds ?? region.StartSeconds, 3), used);
		}
	}

	private static string UniqueId(string fingerprint, string kind, HashSet<string> used)
	{
		for (int ordinal = 0; ; ordinal++)
		{
			string candidate = MusicAnalysisId.Create(fingerprint, kind, ordinal);
			if (used.Add(candidate)) return candidate;
		}
	}

	private static int EventIdentityPriority(MusicEvent musicEvent)
	{
		if (musicEvent.Origin == MusicAnalysisOrigin.UserCreated) return 3;
		if (musicEvent.Editorial?.IsLocked == true) return 2;
		return musicEvent.ReviewState == MusicAnalysisReviewState.Reviewed ? 1 : 0;
	}

	private static int RegionIdentityPriority(MusicRegion region)
	{
		if (region.Origin == MusicAnalysisOrigin.UserCreated) return 3;
		if (region.Editorial?.IsLocked == true) return 2;
		return region.ReviewState == MusicAnalysisReviewState.Reviewed ? 1 : 0;
	}

	private static void PreserveEventById(SongAnalysis analysis, MusicEvent preserved)
	{
		analysis.Events.RemoveAll((MusicEvent item) => string.Equals(item.Id, preserved.Id, StringComparison.Ordinal));
		analysis.Events.Add(preserved);
	}

	private static void PreserveRegionById(SongAnalysis analysis, MusicRegion preserved)
	{
		analysis.Regions.RemoveAll((MusicRegion item) => string.Equals(item.Id, preserved.Id, StringComparison.Ordinal));
		analysis.Regions.Add(preserved);
	}

	private static MusicEvent FindEvent(IEnumerable<MusicEvent> candidates, MusicEvent proposal, double tolerance)
	{
		double proposalTime = proposal.DetectedTimeSeconds ?? proposal.TimeSeconds;
		MusicEventType proposalType = proposal.DetectedType ?? proposal.Type;
		return candidates
			.Where((MusicEvent item) => (item.DetectedType ?? item.Type) == proposalType)
			.Where((MusicEvent item) => Math.Abs((item.DetectedTimeSeconds ?? item.TimeSeconds) - proposalTime) <= tolerance)
			.OrderBy((MusicEvent item) => Math.Abs((item.DetectedTimeSeconds ?? item.TimeSeconds) - proposalTime))
			.FirstOrDefault();
	}

	private static MusicRegion FindRegion(IEnumerable<MusicRegion> candidates, MusicRegion proposal, double tolerance)
	{
		double proposalStart = proposal.DetectedStartSeconds ?? proposal.StartSeconds;
		double proposalEnd = proposal.DetectedEndSeconds ?? proposal.EndSeconds;
		MusicRegionType proposalType = proposal.DetectedType ?? proposal.Type;
		return candidates
			.Where((MusicRegion item) => (item.DetectedType ?? item.Type) == proposalType)
			.Where((MusicRegion item) => Math.Abs((item.DetectedStartSeconds ?? item.StartSeconds) - proposalStart) <= tolerance)
			.Where((MusicRegion item) => Math.Abs((item.DetectedEndSeconds ?? item.EndSeconds) - proposalEnd) <= tolerance)
			.OrderBy((MusicRegion item) => Math.Abs((item.DetectedStartSeconds ?? item.StartSeconds) - proposalStart))
			.FirstOrDefault();
	}

	private static void PreserveReviewedEvent(MusicEvent existing, MusicEvent proposal)
	{
		if (existing.ReviewState != MusicAnalysisReviewState.Proposed || existing.Editorial?.IsLocked == true)
		{
			proposal.TimeSeconds = existing.TimeSeconds;
			proposal.Type = existing.Type;
			proposal.ReviewState = existing.ReviewState;
			proposal.Editorial = existing.Editorial ?? new EditorialMetadata();
		}
	}

	private static void PreserveReviewedRegion(MusicRegion existing, MusicRegion proposal)
	{
		if (existing.ReviewState != MusicAnalysisReviewState.Proposed || existing.Editorial?.IsLocked == true)
		{
			proposal.StartSeconds = existing.StartSeconds;
			proposal.EndSeconds = existing.EndSeconds;
			proposal.Type = existing.Type;
			proposal.ReviewState = existing.ReviewState;
			proposal.Editorial = existing.Editorial ?? new EditorialMetadata();
		}
	}
}
