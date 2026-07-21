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
		detected.Events.AddRange(unmatchedEvents.Where((MusicEvent item) => item.ReviewState != MusicAnalysisReviewState.Proposed || item.Editorial?.IsLocked == true));
		detected.Events.AddRange(existing.Events.Where((MusicEvent item) => item.Origin == MusicAnalysisOrigin.UserCreated));

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
		detected.Regions.AddRange(unmatchedRegions.Where((MusicRegion item) => item.ReviewState != MusicAnalysisReviewState.Proposed || item.Editorial?.IsLocked == true));
		detected.Regions.AddRange(existing.Regions.Where((MusicRegion item) => item.Origin == MusicAnalysisOrigin.UserCreated));
		detected.Id = existing.Id;
		detected.CreatedUtc = existing.CreatedUtc;
		return detected;
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
