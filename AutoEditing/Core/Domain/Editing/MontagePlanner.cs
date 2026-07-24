using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain.Audio;
using Core.Domain.Clip;

namespace Core.Domain.Editing;

public class MontagePlanner
{
	private const double KillDipSpeed = 0.35;

	private const double PreferredMinimumCruiseSpeed = 1.2;

	private const double NormalPlaybackMinimumSpeed = 1.0;

	private const double MinimumPostKillFastSourceSeconds = 0.005;

	private const double MaximumPostKillFastSourceSeconds = 0.03;

	private const double MinimumPostKillRampSourceSeconds = 0.1;

	private const double MaximumPostKillRampSourceSeconds = 0.18;

	private const double MinimumPostKillHoldSourceSeconds = 0.035;

	private const double MaximumPostKillHoldSourceSeconds = 0.11;

	private const double SegmentBoundaryEpsilonSeconds = 0.000001;

	private readonly double _preRoll;

	private readonly double _postRoll;

	private readonly double _minVelocity;

	private readonly double _maxVelocity;

	public MontagePlanner()
		: this(1.25, 0.75, 0.35, 2.0)
	{
	}

	public MontagePlanner(double preRoll, double postRoll, double minVelocity, double maxVelocity)
	{
		if (preRoll < 0.0 || postRoll < 0.0 || minVelocity <= 0.0 || maxVelocity < minVelocity)
		{
			throw new ArgumentOutOfRangeException("Invalid montage timing or velocity bounds.");
		}
		_preRoll = preRoll;
		_postRoll = postRoll;
		_minVelocity = minVelocity;
		_maxVelocity = maxVelocity;
	}

	public List<ClipPlacement> PlanMontage(List<Core.Domain.Clip.Clip> clips, BeatGrid beats, double songDurationSeconds)
	{
		MontageSongPlanningInput input = new BeatGridPlanningInputAdapter().Create(beats, songDurationSeconds);
		MontagePlanningResult result = PlanMontage(clips, input);
		if (!result.IsFeasible)
		{
			throw new InvalidOperationException(result.Diagnostics.FirstOrDefault()?.Message ?? "No bounded sequential beat assignment exists for the reviewed kills.");
		}
		return result.Placements;
	}

	public MontagePlanningResult PlanMontage(List<Core.Domain.Clip.Clip> clips, MontageSongPlanningInput song)
	{
		List<List<Core.Domain.Clip.Clip>> orders = CandidateClipOrders(clips ?? new List<Core.Domain.Clip.Clip>());
		MontagePlanningResult best = null;
		double bestScore = double.PositiveInfinity;
		MontagePlanningResult firstFailure = null;
		foreach (List<Core.Domain.Clip.Clip> order in orders)
		{
			MontagePlanningResult candidate = PlanOrderedMontage(order, song);
			if (!candidate.IsFeasible)
			{
				if (firstFailure == null) firstFailure = candidate;
				continue;
			}
			double score = PlanQuality(candidate, song);
			if (best == null || score < bestScore - 0.000001)
			{
				best = candidate;
				bestScore = score;
			}
		}
		return best ?? firstFailure ?? Fail(new MontagePlanningResult(), "no-clip-order", "No clip order is available for montage planning.");
	}

	private MontagePlanningResult PlanOrderedMontage(List<Core.Domain.Clip.Clip> orderedClips, MontageSongPlanningInput song)
	{
		MontagePlanningResult result = new MontagePlanningResult();
		if (song == null)
		{
			return Fail(result, "invalid-song-plan", "A reviewed song planning input is required.");
		}
		result.Diagnostics.AddRange(song.Diagnostics ?? new List<MontageSongPlanningDiagnostic>());
		if (song.HasErrors) return result;
		List<KillDemand> demands = CreateDemands(orderedClips, result);
		if (result.Diagnostics.Any((MontageSongPlanningDiagnostic item) => item.Severity == MontageSongPlanningDiagnosticSeverity.Error)) return result;
		if (demands.Count == 0) return Fail(result, "no-reviewed-kills", "No reviewed kills are available for montage planning.");
		List<MontageSongPlanningEvent> targets = (song.Events ?? new List<MontageSongPlanningEvent>())
			.Where((MontageSongPlanningEvent item) => item != null && item.IsGameplayAnchor && !item.IsIntentionallyUnused)
			.Where((MontageSongPlanningEvent item) => song.Regions == null || song.Regions.Count == 0 || song.Regions.Any((MontageSongPlanningRegion region) => region.Id == item.ContainingRegionId && region.Type != Core.Domain.Audio.SongAnalysis.MusicRegionType.Unused))
			.OrderBy((MontageSongPlanningEvent item) => item.EffectiveTimeSeconds)
			.ThenBy((MontageSongPlanningEvent item) => item.Id, StringComparer.Ordinal)
			.ToList();
		double planningInterval = EstimateInterval(targets);
		if (targets.Count < demands.Count)
			return Fail(result, "insufficient-gameplay-anchors", demands.Count + " reviewed kills require anchors, but only " + targets.Count + " eligible gameplay anchors are available after editorial and region filtering.");
		int chronologicalCapacity = targets.Select((MontageSongPlanningEvent item) => Math.Round(item.EffectiveTimeSeconds, 6)).Distinct().Count();
		if (chronologicalCapacity < demands.Count)
			return Fail(result, "insufficient-distinct-anchor-times", demands.Count + " reviewed kills require chronological anchors, but only " + chronologicalCapacity + " distinct eligible anchor times are available.");
		int[] selected = Allocate(demands, targets, song, planningInterval);
		if (selected == null)
			return Fail(result, "velocity-allocation-infeasible", "No complete sequential assignment satisfies the reviewed regions, song boundary, and configured velocity bounds.");
		try
		{
			double montageStart = InitialCursor(song, targets[selected[0]]);
			BuildPlacements(orderedClips, demands, targets, selected, song.SongDurationSeconds, planningInterval, montageStart, result);
			result.IsFeasible = true;
			return result;
		}
		catch (InvalidOperationException exception)
		{
			return Fail(result, "velocity-profile-verification-failed", exception.Message);
		}
	}

	private static List<List<Core.Domain.Clip.Clip>> CandidateClipOrders(List<Core.Domain.Clip.Clip> clips)
	{
		List<Core.Domain.Clip.Clip> openers = clips.Where((Core.Domain.Clip.Clip clip) => clip.IsOpener).OrderBy((Core.Domain.Clip.Clip clip) => clip.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
		List<Core.Domain.Clip.Clip> closers = clips.Where((Core.Domain.Clip.Clip clip) => clip.IsCloser).OrderBy((Core.Domain.Clip.Clip clip) => clip.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
		List<Core.Domain.Clip.Clip> ordinary = clips.Where((Core.Domain.Clip.Clip clip) => !clip.IsOpener && !clip.IsCloser).ToList();
		List<IEnumerable<Core.Domain.Clip.Clip>> variants = new List<IEnumerable<Core.Domain.Clip.Clip>>
		{
			ordinary.OrderByDescending((Core.Domain.Clip.Clip clip) => clip.ConfirmedKills.Count).ThenByDescending(NaturalKillSpan).ThenBy((Core.Domain.Clip.Clip clip) => clip.FilePath, StringComparer.OrdinalIgnoreCase),
			ordinary.OrderBy(FirstKillLead).ThenBy(NaturalKillSpan).ThenBy((Core.Domain.Clip.Clip clip) => clip.FilePath, StringComparer.OrdinalIgnoreCase),
			ordinary.OrderByDescending(FirstKillLead).ThenByDescending(NaturalKillSpan).ThenBy((Core.Domain.Clip.Clip clip) => clip.FilePath, StringComparer.OrdinalIgnoreCase)
		};
		List<List<Core.Domain.Clip.Clip>> orders = new List<List<Core.Domain.Clip.Clip>>();
		HashSet<string> signatures = new HashSet<string>(StringComparer.Ordinal);
		foreach (IEnumerable<Core.Domain.Clip.Clip> variant in variants)
		{
			List<Core.Domain.Clip.Clip> order = openers.Concat(variant).Concat(closers).ToList();
			string signature = string.Join("\n", order.Select((Core.Domain.Clip.Clip clip) => clip.FilePath ?? string.Empty));
			if (signatures.Add(signature)) orders.Add(order);
		}
		return orders.Count > 0 ? orders : new List<List<Core.Domain.Clip.Clip>> { new List<Core.Domain.Clip.Clip>() };
	}

	private static double FirstKillLead(Core.Domain.Clip.Clip clip)
	{
		return clip.ConfirmedKills.Count == 0 ? double.MaxValue : clip.ConfirmedKills.Min((ShotEvent kill) => kill.SourceConfirmationTimeSeconds);
	}

	private static double NaturalKillSpan(Core.Domain.Clip.Clip clip)
	{
		List<ShotEvent> kills = clip.ConfirmedKills.OrderBy((ShotEvent kill) => kill.SourceConfirmationTimeSeconds).ToList();
		return kills.Count < 2 ? 0.0 : kills[kills.Count - 1].SourceConfirmationTimeSeconds - kills[0].SourceConfirmationTimeSeconds;
	}

	private static double PlanQuality(MontagePlanningResult result, MontageSongPlanningInput song)
	{
		Dictionary<string, MontageSongPlanningEvent> events = (song?.Events ?? new List<MontageSongPlanningEvent>()).Where((MontageSongPlanningEvent item) => item != null && item.Id != null).GroupBy((MontageSongPlanningEvent item) => item.Id).ToDictionary((IGrouping<string, MontageSongPlanningEvent> group) => group.Key, (IGrouping<string, MontageSongPlanningEvent> group) => group.First(), StringComparer.Ordinal);
		double score = 0.0;
		foreach (MontageSyncAssignment assignment in result.Assignments)
		{
			if (!events.TryGetValue(assignment.MusicEventId, out MontageSongPlanningEvent target)) continue;
			if (target.IsSuggestedGameplayAnchor) score += 100000.0;
			score -= target.Priority * 1000.0 + (target.Intensity ?? 0.0);
		}
		foreach (ClipPlacement placement in result.Placements)
		{
			foreach (SpeedProfilePoint point in placement.SpeedProfile.Points.Where((SpeedProfilePoint point) => point.Speed >= NormalPlaybackMinimumSpeed)) score += Math.Abs(Math.Log(point.Speed));
		}
		return score;
	}

	private static MontagePlanningResult Fail(MontagePlanningResult result, string code, string message)
	{
		result.IsFeasible = false;
		result.Diagnostics.Add(new MontageSongPlanningDiagnostic { Code = code, Severity = MontageSongPlanningDiagnosticSeverity.Error, Message = message });
		return result;
	}

	private sealed class KillDemand
	{
		public Core.Domain.Clip.Clip Clip;
		public ShotEvent Kill;
		public int ClipIndex;
		public int KillIndex;
		public double SourceStart;
		public double SourceEnd;
		public bool FirstInClip;
		public bool LastInClip;
	}

	private List<KillDemand> CreateDemands(List<Core.Domain.Clip.Clip> clips, MontagePlanningResult result)
	{
		List<KillDemand> demands = new List<KillDemand>();
		for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
		{
			Core.Domain.Clip.Clip clip = clips[clipIndex];
			List<ShotEvent> kills = clip.ConfirmedKills.OrderBy((ShotEvent item) => item.SourceConfirmationTimeSeconds).ToList();
			if (kills.Count == 0)
			{
				Fail(result, "clip-without-reviewed-kill", "Clip has no reviewed Hit/Headshot markers: " + clip.FilePath);
				continue;
			}
			double sourceStart = Math.Max(0.0, kills[0].SourceConfirmationTimeSeconds - _preRoll);
			double sourceEnd = Math.Min(clip.DurationSeconds, kills[kills.Count - 1].SourceConfirmationTimeSeconds + _postRoll);
			if (sourceEnd <= sourceStart)
			{
				Fail(result, "invalid-clip-window", "Invalid marker/pre-roll/post-roll range: " + clip.FilePath);
				continue;
			}
			for (int killIndex = 0; killIndex < kills.Count; killIndex++)
				demands.Add(new KillDemand { Clip = clip, Kill = kills[killIndex], ClipIndex = clipIndex, KillIndex = killIndex, SourceStart = sourceStart, SourceEnd = sourceEnd, FirstInClip = killIndex == 0, LastInClip = killIndex == kills.Count - 1 });
		}
		return demands;
	}

	private int[] Allocate(List<KillDemand> demands, List<MontageSongPlanningEvent> targets, MontageSongPlanningInput song, double interval)
	{
		if (demands.Count == 0) return new int[0];
		double[,] costs = new double[demands.Count, targets.Count];
		int[,] previous = new int[demands.Count, targets.Count];
		for (int d = 0; d < demands.Count; d++) for (int t = 0; t < targets.Count; t++) { costs[d, t] = double.PositiveInfinity; previous[d, t] = -1; }
		for (int t = 0; t < targets.Count; t++)
		{
			double initialCursor = InitialCursor(song, targets[t]);
			if (!TransitionFeasible(null, demands[0], initialCursor, targets[t].EffectiveTimeSeconds, interval) || !RegionFeasible(song, null, demands[0], null, targets[t], initialCursor, interval)) continue;
			costs[0, t] = TargetCost(targets[t]) + TransitionCost(null, demands[0], initialCursor, targets[t].EffectiveTimeSeconds);
		}
		for (int d = 1; d < demands.Count; d++)
		{
			for (int t = d; t < targets.Count; t++)
			{
				for (int p = d - 1; p < t; p++)
				{
					if (double.IsPositiveInfinity(costs[d - 1, p])) continue;
					double cursor = CursorAfter(demands[d - 1], targets[p].EffectiveTimeSeconds, interval);
					if (!TransitionFeasible(demands[d - 1], demands[d], cursor, targets[t].EffectiveTimeSeconds, interval) || !RegionFeasible(song, demands[d - 1], demands[d], targets[p], targets[t], cursor, interval)) continue;
					double cost = costs[d - 1, p] + TargetCost(targets[t]) + TransitionCost(demands[d - 1], demands[d], cursor, targets[t].EffectiveTimeSeconds);
					if (cost < costs[d, t] - 1E-09 || (Math.Abs(cost - costs[d, t]) <= 1E-09 && p < previous[d, t])) { costs[d, t] = cost; previous[d, t] = p; }
				}
			}
		}
		int last = -1;
		double best = double.PositiveInfinity;
		for (int t = demands.Count - 1; t < targets.Count; t++)
		{
			if (double.IsPositiveInfinity(costs[demands.Count - 1, t])) continue;
			double end = CursorAfter(demands[demands.Count - 1], targets[t].EffectiveTimeSeconds, interval);
			if (end > song.SongDurationSeconds + 0.002) continue;
			if (costs[demands.Count - 1, t] < best - 1E-09) { best = costs[demands.Count - 1, t]; last = t; }
		}
		if (last < 0) return null;
		int[] selected = new int[demands.Count];
		for (int d = demands.Count - 1; d >= 0; d--) { selected[d] = last; last = previous[d, last]; }
		return selected;
	}

	private static double InitialCursor(MontageSongPlanningInput song, MontageSongPlanningEvent target)
	{
		if (song.Mode == MontageSongPlanningMode.LegacyBeatGrid) return song.Events.Where((MontageSongPlanningEvent item) => item.IsGameplayAnchor).Min((MontageSongPlanningEvent item) => item.EffectiveTimeSeconds);
		MontageSongPlanningRegion region = (song.Regions ?? new List<MontageSongPlanningRegion>()).FirstOrDefault((MontageSongPlanningRegion item) => item.Id == target.ContainingRegionId);
		return region?.StartSeconds ?? 0.0;
	}

	private bool RegionFeasible(MontageSongPlanningInput song, KillDemand previousDemand, KillDemand currentDemand, MontageSongPlanningEvent previousTarget, MontageSongPlanningEvent currentTarget, double cursor, double interval)
	{
		if (song.Mode == MontageSongPlanningMode.LegacyBeatGrid || song.Regions == null || song.Regions.Count == 0) return true;
		MontageSongPlanningRegion region = song.Regions.FirstOrDefault((MontageSongPlanningRegion item) => item.Id == currentTarget.ContainingRegionId && item.Type != Core.Domain.Audio.SongAnalysis.MusicRegionType.Unused);
		if (region == null || cursor < region.StartSeconds - 0.002) return false;
		if (!currentDemand.FirstInClip && previousTarget?.ContainingRegionId != currentTarget.ContainingRegionId) return false;
		if (currentDemand.LastInClip && CursorAfter(currentDemand, currentTarget.EffectiveTimeSeconds, interval) > region.EndSeconds + 0.002) return false;
		return true;
	}

	private double CursorAfter(KillDemand demand, double targetTime, double interval)
	{
		if (!demand.LastInClip) return targetTime;
		double tail = demand.SourceEnd - demand.Kill.SourceConfirmationTimeSeconds;
		return targetTime + PostKillTargetDuration(tail, interval, demand.Kill.SourceConfirmationTimeSeconds);
	}

	private bool TransitionFeasible(KillDemand previous, KillDemand current, double cursor, double target, double interval)
	{
		double sourceDistance = current.FirstInClip ? current.Kill.SourceConfirmationTimeSeconds - current.SourceStart : current.Kill.SourceConfirmationTimeSeconds - previous.Kill.SourceConfirmationTimeSeconds;
		double duration = target - cursor;
		if (duration <= 0.0) return false;
		double delay, ramp, hold;
		GetPostKillShape(sourceDistance, !current.FirstInClip, current.FirstInClip ? current.SourceStart : previous.Kill.SourceConfirmationTimeSeconds, out delay, out ramp, out hold);
		double longest = SegmentDuration(sourceDistance, delay, ramp, hold, !current.FirstInClip, MinimumAllowedCruiseSpeed);
		double shortest = SegmentDuration(sourceDistance, delay, ramp, hold, !current.FirstInClip, _maxVelocity);
		return duration <= longest + 0.002 && duration >= shortest - 0.002;
	}

	private static double TargetCost(MontageSongPlanningEvent target) => (target.IsSuggestedGameplayAnchor ? 100000.0 : 0.0) - target.Priority * 1000.0 - (target.Intensity ?? 0.0);

	private double TransitionCost(KillDemand previous, KillDemand demand, double cursor, double target)
	{
		double sourceStart = demand.FirstInClip ? demand.SourceStart : previous.Kill.SourceConfirmationTimeSeconds;
		double sourceDistance = demand.Kill.SourceConfirmationTimeSeconds - sourceStart;
		double duration = target - cursor;
		double delay, ramp, hold;
		GetPostKillShape(sourceDistance, !demand.FirstInClip, sourceStart, out delay, out ramp, out hold);
		double preferredLongest = SegmentDuration(sourceDistance, delay, ramp, hold, !demand.FirstInClip, MinimumCruiseSpeed);
		double belowPreferredCruisePenalty = duration > preferredLongest + 0.002 ? 100.0 : 0.0;
		return belowPreferredCruisePenalty + Math.Abs(Math.Log(Math.Max(1E-09, sourceDistance / Math.Max(1E-09, duration))));
	}

	private void BuildPlacements(List<Core.Domain.Clip.Clip> clips, List<KillDemand> demands, List<MontageSongPlanningEvent> targets, int[] selected, double songEnd, double interval, double montageStart, MontagePlanningResult result)
	{
		int demandIndex = 0;
		double timelineStart = montageStart;
		foreach (Core.Domain.Clip.Clip clip in clips)
		{
			List<KillDemand> clipDemands = demands.Where((KillDemand item) => item.Clip == clip).ToList();
			List<double> assigned = new List<double>();
			foreach (KillDemand demand in clipDemands)
			{
				MontageSongPlanningEvent target = targets[selected[demandIndex]];
				assigned.Add(target.EffectiveTimeSeconds);
				result.Assignments.Add(new MontageSyncAssignment { ClipPath = clip.FilePath, KillIndex = demand.KillIndex, SourceConfirmationTimeSeconds = demand.Kill.SourceConfirmationTimeSeconds, MusicEventId = target.Id, TimelineTimeSeconds = target.EffectiveTimeSeconds });
				demandIndex++;
			}
			double sourceStart = clipDemands[0].SourceStart;
			double sourceEnd = clipDemands[0].SourceEnd;
			List<SpeedProfilePoint> points = new List<SpeedProfilePoint>();
			AddSegment(points, sourceStart, clipDemands[0].Kill.SourceConfirmationTimeSeconds, assigned[0] - timelineStart, false);
			for (int index = 1; index < clipDemands.Count; index++) AddSegment(points, clipDemands[index - 1].Kill.SourceConfirmationTimeSeconds, clipDemands[index].Kill.SourceConfirmationTimeSeconds, assigned[index] - assigned[index - 1], true);
			double tailSource = sourceEnd - clipDemands[clipDemands.Count - 1].Kill.SourceConfirmationTimeSeconds;
			AddSegment(points, clipDemands[clipDemands.Count - 1].Kill.SourceConfirmationTimeSeconds, sourceEnd, PostKillTargetDuration(tailSource, interval, clipDemands[clipDemands.Count - 1].Kill.SourceConfirmationTimeSeconds), true);
			SpeedProfile profile = new SpeedProfile(Coalesce(points));
			ClipPlacement placement = new ClipPlacement { Clip = clip, TimelineStartSeconds = timelineStart, SourceOffsetSeconds = sourceStart, LengthSeconds = profile.TimelineDurationSeconds, SpeedProfile = profile, AssignedBeatTimesSeconds = assigned };
			if (placement.TimelineEndSeconds > songEnd + 0.002) throw new InvalidOperationException("Complete reviewed kill sequence does not fit in the song: " + clip.FilePath);
			Verify(placement, clipDemands.Select((KillDemand item) => item.Kill).ToList());
			result.Placements.Add(placement);
			timelineStart = placement.TimelineEndSeconds;
		}
	}

	private static double EstimateInterval(IEnumerable<MontageSongPlanningEvent> events)
	{
		List<double> times = (events ?? Enumerable.Empty<MontageSongPlanningEvent>())
			.Where((MontageSongPlanningEvent item) => item != null)
			.Select((MontageSongPlanningEvent item) => item.EffectiveTimeSeconds)
			.OrderBy((double item) => item)
			.ToList();
		List<double> gaps = times.Zip(times.Skip(1), (double left, double right) => right - left).Where((double gap) => gap > 0.01).OrderBy((double gap) => gap).ToList();
		return gaps.Count == 0 ? 0.5 : gaps[gaps.Count / 2];
	}

	/* Legacy greedy allocator retained privately for binary/source archaeology; new calls use the global allocator above. */
	private List<ClipPlacement> PlanMontageGreedy(List<Core.Domain.Clip.Clip> clips, BeatGrid beats, double songDurationSeconds)
	{
		List<ClipPlacement> list = new List<ClipPlacement>();
		double num = Math.Max(0.0, beats.FirstBeatOffsetSeconds);
		foreach (Core.Domain.Clip.Clip item in OrderClips(clips))
		{
			List<ShotEvent> list2 = item.ConfirmedKills.OrderBy((ShotEvent e) => e.SourceConfirmationTimeSeconds).ToList();
			if (list2.Count == 0)
			{
				throw new InvalidOperationException("Clip has no reviewed Hit/Headshot markers: " + item.FilePath);
			}
			double num2 = Math.Max(0.0, list2[0].SourceConfirmationTimeSeconds - _preRoll);
			double num3 = Math.Min(item.DurationSeconds, list2[list2.Count - 1].SourceConfirmationTimeSeconds + _postRoll);
			if (num3 <= num2)
			{
				throw new InvalidOperationException("Invalid marker/pre-roll/post-roll range: " + item.FilePath);
			}
			List<double> list3 = AssignBeats(list2, num2, num, beats, songDurationSeconds);
			List<SpeedProfilePoint> points = new List<SpeedProfilePoint>();
			AddSegment(points, num2, list2[0].SourceConfirmationTimeSeconds, list3[0] - num, postKillTreatmentAtStart: false);
			for (int num4 = 1; num4 < list2.Count; num4++)
			{
				AddSegment(points, list2[num4 - 1].SourceConfirmationTimeSeconds, list2[num4].SourceConfirmationTimeSeconds, list3[num4] - list3[num4 - 1], postKillTreatmentAtStart: true);
			}
			double postKillSourceDuration = num3 - list2[list2.Count - 1].SourceConfirmationTimeSeconds;
			double targetDuration = PostKillTargetDuration(postKillSourceDuration, beats.BeatIntervalSeconds, list2[list2.Count - 1].SourceConfirmationTimeSeconds);
			AddSegment(points, list2[list2.Count - 1].SourceConfirmationTimeSeconds, num3, targetDuration, postKillTreatmentAtStart: true);
			SpeedProfile speedProfile = new SpeedProfile(Coalesce(points));
			double timelineDurationSeconds = speedProfile.TimelineDurationSeconds;
			if (num + timelineDurationSeconds > songDurationSeconds + 0.002)
			{
				throw new InvalidOperationException("Complete reviewed kill sequence does not fit in the song: " + item.FilePath);
			}
			ClipPlacement clipPlacement = new ClipPlacement
			{
				Clip = item,
				TimelineStartSeconds = num,
				SourceOffsetSeconds = num2,
				LengthSeconds = timelineDurationSeconds,
				SpeedProfile = speedProfile,
				AssignedBeatTimesSeconds = list3
			};
			Verify(clipPlacement, list2);
			list.Add(clipPlacement);
			num = clipPlacement.TimelineEndSeconds;
		}
		return list;
	}

	private List<double> AssignBeats(List<ShotEvent> kills, double sourceStart, double timelineStart, BeatGrid beats, double songEnd)
	{
		List<double> list = new List<double>();
		double num = timelineStart;
		double num2 = sourceStart;
		foreach (ShotEvent kill in kills)
		{
			double num3 = kill.SourceConfirmationTimeSeconds - num2;
			int num4 = Math.Max(0, (int)Math.Ceiling((num - beats.FirstBeatOffsetSeconds + 1E-06) / beats.BeatIntervalSeconds));
			double num5 = -1.0;
			double num6 = double.MaxValue;
			double fallbackBeat = -1.0;
			double fallbackScore = double.MaxValue;
			int num7 = num4;
			while (true)
			{
				double num8 = beats.FirstBeatOffsetSeconds + (double)num7 * beats.BeatIntervalSeconds;
				if (num8 > songEnd)
				{
					break;
				}
				double num9 = num8 - num;
				if (!(num9 <= 0.0))
				{
					bool postKillTreatment = list.Count > 0;
					double delay;
					double ramp;
					double hold;
					GetPostKillShape(num3, postKillTreatment, num2, out delay, out ramp, out hold);
					double preferredLongestDuration = SegmentDuration(num3, delay, ramp, hold, postKillTreatment, MinimumCruiseSpeed);
					double boundedLongestDuration = SegmentDuration(num3, delay, ramp, hold, postKillTreatment, MinimumAllowedCruiseSpeed);
					double num11 = SegmentDuration(num3, delay, ramp, hold, postKillTreatment, _maxVelocity);
					if (!(num9 > boundedLongestDuration + 0.002) && !(num9 < num11 - 0.002))
					{
						double num12 = num3 / num9;
						double num13 = Math.Abs(Math.Log(num12));
						if (num9 <= preferredLongestDuration + 0.002 && num13 < num6)
						{
							num5 = num8;
							num6 = num13;
						}
						else if (num13 < fallbackScore)
						{
							fallbackBeat = num8;
							fallbackScore = num13;
						}
					}
				}
				num7++;
			}
			if (num5 < 0.0) num5 = fallbackBeat;
			if (num5 < 0.0)
			{
				throw new InvalidOperationException("No bounded sequential beat assignment exists for a reviewed kill.");
			}
			list.Add(num5);
			num = num5;
			num2 = kill.SourceConfirmationTimeSeconds;
		}
		return list;
	}

	private void AddSegment(List<SpeedProfilePoint> points, double sourceA, double sourceB, double targetDuration, bool postKillTreatmentAtStart)
	{
		double num = sourceB - sourceA;
		if (num < -1E-09 || targetDuration < -1E-09)
		{
			throw new InvalidOperationException("Markers are not chronological.");
		}
		if (!(num <= 1E-09))
		{
			double delay;
			double ramp;
			double hold;
			GetPostKillShape(num, postKillTreatmentAtStart, sourceA, out delay, out ramp, out hold);
			double num3 = SolveCruise(num, targetDuration, delay, ramp, hold, postKillTreatmentAtStart);
			double segmentStart = sourceA;
			if (points.Count > 0 && Math.Abs(points[points.Count - 1].SourceTimeSeconds - sourceA) < 1E-08 && Math.Abs(points[points.Count - 1].Speed - num3) >= 1E-08)
			{
				segmentStart = Math.Min(sourceB, sourceA + SegmentBoundaryEpsilonSeconds);
			}
			AddPoint(points, segmentStart, num3);
			if (postKillTreatmentAtStart)
			{
				AddPoint(points, sourceA + delay, num3);
				AddPoint(points, sourceA + delay + ramp, KillDipSpeed);
				AddPoint(points, sourceA + delay + ramp + hold, KillDipSpeed);
				AddPoint(points, sourceA + delay + ramp + hold + ramp, num3);
			}
			AddPoint(points, sourceB, num3);
		}
	}

	private static void GetPostKillShape(double distance, bool enabled, double anchorSourceTime, out double delay, out double ramp, out double hold)
	{
		if (!enabled)
		{
			delay = 0.0;
			ramp = 0.0;
			hold = 0.0;
			return;
		}
		double requestedDelay = Vary(MinimumPostKillFastSourceSeconds, MaximumPostKillFastSourceSeconds, anchorSourceTime, 0.17);
		double requestedRamp = Vary(MinimumPostKillRampSourceSeconds, MaximumPostKillRampSourceSeconds, anchorSourceTime, 1.31);
		double requestedHold = Vary(MinimumPostKillHoldSourceSeconds, MaximumPostKillHoldSourceSeconds, anchorSourceTime, 2.73);
		double requested = requestedDelay + 2.0 * requestedRamp + requestedHold;
		double scale = Math.Min(1.0, distance * 0.85 / requested);
		delay = requestedDelay * scale;
		ramp = requestedRamp * scale;
		hold = requestedHold * scale;
	}

	private static double Vary(double minimum, double maximum, double anchorSourceTime, double salt)
	{
		double value = Math.Sin((anchorSourceTime + salt) * 12.9898) * 43758.5453;
		double fraction = value - Math.Floor(value);
		return minimum + (maximum - minimum) * fraction;
	}

	private static double SegmentDuration(double distance, double delay, double ramp, double hold, bool postKillTreatment, double speed)
	{
		if (!postKillTreatment) return distance / speed;
		double cruiseDistance = Math.Max(0.0, distance - delay - 2.0 * ramp - hold);
		return (delay + cruiseDistance) / speed + 2.0 * ramp / ((KillDipSpeed + speed) / 2.0) + hold / KillDipSpeed;
	}

	private double SolveCruise(double distance, double duration, double delay, double ramp, double hold, bool postKillTreatment)
	{
		Func<double, double> func = (double speed) => SegmentDuration(distance, delay, ramp, hold, postKillTreatment, speed);
		double minimumCruise = MinimumCruiseSpeed;
		double num = func(minimumCruise);
		double num2 = func(_maxVelocity);
		if (duration > num + 0.002)
		{
			minimumCruise = MinimumAllowedCruiseSpeed;
			num = func(minimumCruise);
		}
		if (duration > num + 0.002 || duration < num2 - 0.002)
		{
			throw new InvalidOperationException("Marker spacing cannot be solved within configured velocity bounds.");
		}
		double num3 = minimumCruise;
		double num4 = _maxVelocity;
		for (int num5 = 0; num5 < 80; num5++)
		{
			double num6 = (num3 + num4) / 2.0;
			if (func(num6) > duration)
			{
				num3 = num6;
			}
			else
			{
				num4 = num6;
			}
		}
		return (num3 + num4) / 2.0;
	}

	private double PostKillTargetDuration(double sourceDuration, double beatInterval, double anchorSourceTime)
	{
		double delay;
		double ramp;
		double hold;
		GetPostKillShape(sourceDuration, enabled: true, anchorSourceTime, out delay, out ramp, out hold);
		return SegmentDuration(sourceDuration, delay, ramp, hold, postKillTreatment: true, MinimumAllowedCruiseSpeed);
	}

	private double MinimumCruiseSpeed => Math.Min(_maxVelocity, Math.Max(_minVelocity, PreferredMinimumCruiseSpeed));

	private double MinimumAllowedCruiseSpeed => Math.Min(_maxVelocity, Math.Max(_minVelocity, NormalPlaybackMinimumSpeed));

	private static void AddPoint(List<SpeedProfilePoint> points, double source, double speed)
	{
		if (points.Count > 0 && Math.Abs(points[points.Count - 1].SourceTimeSeconds - source) < 1E-08)
		{
			if (Math.Abs(points[points.Count - 1].Speed - speed) < 1E-08)
			{
				return;
			}
			points.RemoveAt(points.Count - 1);
		}
		points.Add(new SpeedProfilePoint(source, speed));
	}

	private static IEnumerable<SpeedProfilePoint> Coalesce(List<SpeedProfilePoint> points)
	{
		return (from p in points
			orderby p.SourceTimeSeconds
			group p by Math.Round(p.SourceTimeSeconds, 8) into g
			select g.Last()).ToList();
	}

	private static void Verify(ClipPlacement placement, List<ShotEvent> kills)
	{
		for (int i = 0; i < kills.Count; i++)
		{
			if (!placement.SpeedProfile.TryGetTimelineTimeForSourceTime(kills[i].SourceConfirmationTimeSeconds, out var timelineTimeSeconds) || Math.Abs(placement.TimelineStartSeconds + timelineTimeSeconds - placement.AssignedBeatTimesSeconds[i]) > 0.002)
			{
				throw new InvalidOperationException("Velocity integration failed to preserve every reviewed kill within 2 ms: " + placement.Clip.FilePath);
			}
		}
	}

	private static List<Core.Domain.Clip.Clip> OrderClips(List<Core.Domain.Clip.Clip> clips)
	{
		return (from c in clips
			orderby c.IsOpener descending, c.IsCloser, c.Map, c.SequenceNumber
			select c).ToList();
	}
}
