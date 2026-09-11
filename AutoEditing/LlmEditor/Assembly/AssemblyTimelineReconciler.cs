using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Assembly;
using Core.Domain.Editing;
using Core.Domain.Planning;
using System.Security.Cryptography;
using System.Text;

namespace AutoEditing.LlmEditor.Assembly;

internal static class AssemblyTimelineReconciler
{
	private const double TimingToleranceSeconds = 0.000001;

	public static TimelineAdjustmentDelta Apply(
		EditPlanDocument plan,
		CandidateTimelineSnapshot snapshot,
		int checkpoint = 0,
		CandidateMaterializationBaseline? baseline = null)
	{
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(snapshot);
		TimelineAdjustmentDelta delta = new() { Checkpoint = checkpoint };
		if (baseline != null)
			ValidateAgainstMaterializationBaseline(
				plan,
				snapshot,
				baseline,
				checkpoint,
				delta);
		List<CandidateEventSnapshot> videoEvents = snapshot.Tracks
			.Where(track => string.Equals(track.MediaKind, "Video", StringComparison.OrdinalIgnoreCase))
			.SelectMany(track => track.Events)
			.Where(item => !string.IsNullOrWhiteSpace(item.MediaPath))
			.ToList();
		snapshot.Workspace?.Validate();
		Dictionary<string, List<CandidateEventSnapshot>> videoByPath = videoEvents
			.GroupBy(item => Path.GetFullPath(item.MediaPath), StringComparer.OrdinalIgnoreCase)
			.ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
		bool hasDurableIdentity = snapshot.Workspace != null &&
			videoEvents.Any(item =>
			CandidatePlacementIdentity.IsOwned(
				snapshot.Workspace,
				item.PlacementId));
		Dictionary<string, List<CandidateEventSnapshot>> videoByIdentity =
			videoEvents
				.Where(item => !string.IsNullOrWhiteSpace(item.PlacementId))
				.GroupBy(item => item.PlacementId, StringComparer.Ordinal)
				.ToDictionary(
					group => group.Key,
					group => group.ToList(),
					StringComparer.Ordinal);
		HashSet<CandidateEventSnapshot> matched = new();
		for (int placementIndex = 0;
			placementIndex < plan.Montage.Placements.Count;
			placementIndex++)
		{
			ClipPlacement placement = plan.Montage.Placements[placementIndex];
			string path = Path.GetFullPath(placement.Clip.FilePath);
			string expectedIdentity = hasDurableIdentity
				? CandidatePlacementIdentity.Create(
					snapshot.Workspace!,
					placementIndex + 1,
					path)
				: path;
			List<CandidateEventSnapshot>? matches;
			if (hasDurableIdentity)
				videoByIdentity.TryGetValue(expectedIdentity, out matches);
			else
				videoByPath.TryGetValue(path, out matches);
			if (matches == null)
				throw new InvalidOperationException(
					"The expected VEGAS candidate event was deleted: " + path +
					". Restore it or explicitly exclude the clip before continuing.");
			if (matches.Count != 1)
				throw new InvalidOperationException(
					"The VEGAS candidate contains " + matches.Count +
					" events for placement '" + expectedIdentity +
					"', which is ambiguous: " + path);
			CandidateEventSnapshot actual = matches[0];
			if (!string.Equals(
				Path.GetFullPath(actual.MediaPath),
				path,
				StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"The durable placement identity '" + expectedIdentity +
					"' now points at different media. Restore the proposal or " +
					"explicitly resolve the replacement before continuing.");
			matched.Add(actual);
			double sourceStart = actual.SourceOffset.TotalSeconds;
			double timelineDuration = actual.TimelineDuration.TotalSeconds;
			if (timelineDuration <= 0.0)
				throw new InvalidOperationException("A manually adjusted clip has zero duration: " + path);
			SpeedProfile liveProfile = BuildLiveSpeedProfile(actual, sourceStart);
			double sourceEnd = liveProfile.Points[^1].SourceTimeSeconds;
			if (sourceStart < 0.0 || sourceEnd > placement.Clip.DurationSeconds + 0.002)
				throw new InvalidOperationException("A manual adjustment exceeds its source media: " + path);
			AddChange(delta, TimelineAdjustmentKind.TimelineStartChanged, path,
				placement.TimelineStartSeconds, actual.TimelineStart.TotalSeconds);
			AddChange(delta, TimelineAdjustmentKind.SourceTrimChanged, path,
				placement.SourceOffsetSeconds, sourceStart);
			AddChange(delta, TimelineAdjustmentKind.DurationChanged, path,
				placement.LengthSeconds, timelineDuration);
			if (!SameSpeedProfile(placement.SpeedProfile, liveProfile))
				delta.Changes.Add(new TimelineAdjustment
				{
					Kind = TimelineAdjustmentKind.ConstantSpeedChanged,
					ClipPath = path,
					Before = placement.SpeedProfile.Points[0].Speed,
					After = liveProfile.Points[0].Speed
				});
			placement.TimelineStartSeconds = actual.TimelineStart.TotalSeconds;
			placement.SourceOffsetSeconds = sourceStart;
			placement.SpeedProfile = liveProfile;
			placement.LengthSeconds = liveProfile.TimelineDurationSeconds;
		}
		List<string> unexpected = videoEvents
			.Where(item => !matched.Contains(item))
			.Select(item => Path.GetFullPath(item.MediaPath))
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (unexpected.Count > 0)
			throw new InvalidOperationException(
				"The VEGAS candidate contains unexpected video events that cannot yet be " +
				"adopted during the synchronization pass: " +
				string.Join(", ", unexpected));
		plan.Montage.SyncAssignments = plan.Montage.SyncAssignments
			.Where(assignment =>
			{
				ClipPlacement placement = plan.Montage.Placements.First(item =>
					string.Equals(item.Clip.FilePath, assignment.ClipPath, StringComparison.OrdinalIgnoreCase));
				bool remainsAligned = placement.SpeedProfile.TryGetTimelineTimeForSourceTime(
					assignment.SourceConfirmationTimeSeconds, out double relative) &&
					Math.Abs(
						placement.TimelineStartSeconds + relative -
						assignment.TimelineTimeSeconds) <= 0.02;
				if (!remainsAligned)
					delta.Changes.Add(new TimelineAdjustment
					{
						Kind = TimelineAdjustmentKind.SyncAssignmentInvalidated,
						ClipPath = assignment.ClipPath,
						Before = assignment.TimelineTimeSeconds,
						After = placement.TimelineStartSeconds
					});
				return remainsAligned;
			})
			.ToList();
		EditPlanDocumentValidator.ValidateAndNormalize(plan);
		return delta;
	}

	private static void ValidateAgainstMaterializationBaseline(
		EditPlanDocument plan,
		CandidateTimelineSnapshot live,
		CandidateMaterializationBaseline baseline,
		int checkpoint,
		TimelineAdjustmentDelta delta)
	{
		if (baseline.SchemaVersion !=
			CandidateMaterializationBaseline.CurrentSchemaVersion ||
			baseline.Snapshot == null ||
			baseline.Checkpoint != checkpoint)
			throw new InvalidDataException(
				"The materialization baseline is missing, unsupported, or targets " +
				"another checkpoint.");
		CandidateTimelineSnapshot expected = baseline.Snapshot;
		string planSha256 = Convert.ToHexString(
			SHA256.HashData(
				new UTF8Encoding(false).GetBytes(
					EditPlanDocumentSerializer.SerializePlan(plan))))
			.ToLowerInvariant();
		if (!string.Equals(
				baseline.PlanSha256,
				planSha256,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The materialization baseline belongs to another exact plan.");
		if (!SameWorkspace(expected.Workspace, live.Workspace))
			AddUnsupported(
				delta,
				"The candidate workspace identity changed after materialization.");

		List<CandidateTrackSnapshot> expectedTracks =
			expected.Tracks?.ToList() ?? new();
		List<CandidateTrackSnapshot> liveTracks =
			live.Tracks?.ToList() ?? new();
		foreach (CandidateTrackSnapshot expectedTrack in expectedTracks)
		{
			List<CandidateTrackSnapshot> matches = liveTracks
				.Where(track =>
					string.Equals(
						track.Name,
						expectedTrack.Name,
						StringComparison.Ordinal) &&
					string.Equals(
						track.MediaKind,
						expectedTrack.MediaKind,
						StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (matches.Count != 1)
			{
				AddUnsupported(
					delta,
					$"Candidate track '{expectedTrack.Name}' ({expectedTrack.MediaKind}) " +
					$"has {matches.Count} live matches; expected exactly one.");
				continue;
			}
			CandidateTrackSnapshot actualTrack = matches[0];
			liveTracks.Remove(actualTrack);
			if (actualTrack.Muted != expectedTrack.Muted)
				AddUnsupported(
					delta,
					$"Track '{expectedTrack.Name}' mute changed from " +
					$"{expectedTrack.Muted} to {actualTrack.Muted}.");
			if (actualTrack.Solo != expectedTrack.Solo)
				AddUnsupported(
					delta,
					$"Track '{expectedTrack.Name}' solo changed from " +
					$"{expectedTrack.Solo} to {actualTrack.Solo}.");
			if (Math.Abs(actualTrack.Gain - expectedTrack.Gain) > 0.0001)
				AddUnsupported(
					delta,
					$"Track '{expectedTrack.Name}' gain changed from " +
					$"{expectedTrack.Gain:G17} to {actualTrack.Gain:G17}.");
			CompareVolumeAutomation(
				expectedTrack,
				actualTrack,
				delta);
			ValidateTrackEvents(expectedTrack, actualTrack, delta);
		}
		foreach (CandidateTrackSnapshot unexpected in liveTracks)
			AddUnsupported(
				delta,
				$"Unexpected candidate track '{unexpected.Name}' " +
				$"({unexpected.MediaKind}) was added.");

		if (delta.UnsupportedChanges.Count > 0)
			throw new InvalidOperationException(
				"Unsupported VEGAS candidate changes must be restored or explicitly " +
				"resolved before continuing: " +
				string.Join(" ", delta.UnsupportedChanges));
	}

	private static void CompareVolumeAutomation(
		CandidateTrackSnapshot expected,
		CandidateTrackSnapshot actual,
		TimelineAdjustmentDelta delta)
	{
		IList<CandidateEnvelopePoint> before =
			expected.VolumeAutomation ??
				Array.Empty<CandidateEnvelopePoint>();
		IList<CandidateEnvelopePoint> after =
			actual.VolumeAutomation ??
				Array.Empty<CandidateEnvelopePoint>();
		bool same = before.Count == after.Count &&
			before.Zip(
				after,
				(left, right) =>
					Math.Abs(
						(left.Offset - right.Offset).TotalSeconds) <=
						TimingToleranceSeconds &&
					Math.Abs(left.Value - right.Value) <= 0.0001)
				.All(value => value);
		if (!same)
			AddUnsupported(
				delta,
				$"Track '{expected.Name}' volume automation changed.");
	}

	private static void ValidateTrackEvents(
		CandidateTrackSnapshot expectedTrack,
		CandidateTrackSnapshot liveTrack,
		TimelineAdjustmentDelta delta)
	{
		List<CandidateEventSnapshot> remaining =
			liveTrack.Events?.ToList() ?? new();
		foreach (CandidateEventSnapshot expected in
			expectedTrack.Events ?? Array.Empty<CandidateEventSnapshot>())
		{
			List<CandidateEventSnapshot> matches = remaining
				.Where(actual => SameEventIdentity(expected, actual))
				.ToList();
			if (matches.Count != 1)
			{
				AddUnsupported(
					delta,
					$"{expectedTrack.MediaKind} event '{EventLabel(expected)}' has " +
					$"{matches.Count} live identity matches; expected exactly one.");
				continue;
			}
			CandidateEventSnapshot actual = matches[0];
			remaining.Remove(actual);
			bool video = string.Equals(
				expectedTrack.MediaKind,
				"Video",
				StringComparison.OrdinalIgnoreCase);
			if (!video)
			{
				CompareTime(delta, expected, "timeline start",
					expected.TimelineStart, actual.TimelineStart);
				CompareTime(delta, expected, "duration",
					expected.TimelineDuration, actual.TimelineDuration);
				CompareTime(delta, expected, "source offset",
					expected.SourceOffset, actual.SourceOffset);
				CompareDouble(delta, expected, "gain", expected.Gain, actual.Gain);
				CompareVelocity(delta, expected, actual);
			}
			else
			{
				// Timing, source trim, duration, and velocity (constant or a
				// retiming curve) are the synchronization-pass edits that
				// reconciliation adopts via Apply(), not this baseline check.
				CompareDouble(delta, expected, "event gain", expected.Gain, actual.Gain);
			}
			CompareTime(delta, expected, "fade-in", expected.FadeIn, actual.FadeIn);
			CompareTime(delta, expected, "fade-out", expected.FadeOut, actual.FadeOut);
			CompareText(
				delta, expected, "fade-in transition",
				expected.FadeInTransition, actual.FadeInTransition);
			CompareText(
				delta, expected, "fade-out transition",
				expected.FadeOutTransition, actual.FadeOutTransition);
			CompareText(
				delta, expected, "event grouping",
				expected.GroupSignature, actual.GroupSignature);
			if (!(expected.Effects ?? Array.Empty<string>()).SequenceEqual(
				actual.Effects ?? Array.Empty<string>(),
				StringComparer.Ordinal))
				AddUnsupported(
					delta,
					$"Event '{EventLabel(expected)}' effects changed from " +
					$"[{string.Join(", ", expected.Effects ?? Array.Empty<string>())}] " +
					$"to [{string.Join(", ", actual.Effects ?? Array.Empty<string>())}].");
		}
		foreach (CandidateEventSnapshot unexpected in remaining)
			AddUnsupported(
				delta,
				$"Unexpected {expectedTrack.MediaKind} event " +
				$"'{EventLabel(unexpected)}' was added.");
	}

	private static bool SameWorkspace(
		CandidateWorkspaceId? expected,
		CandidateWorkspaceId? actual) =>
		expected != null &&
		actual != null &&
		string.Equals(expected.SessionId, actual.SessionId, StringComparison.Ordinal) &&
		expected.Iteration == actual.Iteration &&
		string.Equals(expected.Nonce, actual.Nonce, StringComparison.Ordinal);

	private static bool SameEventIdentity(
		CandidateEventSnapshot expected,
		CandidateEventSnapshot actual)
	{
		if (!string.IsNullOrWhiteSpace(expected.PlacementId))
			return string.Equals(
				expected.PlacementId,
				actual.PlacementId,
				StringComparison.Ordinal);
		return string.Equals(
			FullPathOrEmpty(expected.MediaPath),
			FullPathOrEmpty(actual.MediaPath),
			StringComparison.OrdinalIgnoreCase);
	}

	private static void CompareTime(
		TimelineAdjustmentDelta delta,
		CandidateEventSnapshot item,
		string field,
		TimeSpan expected,
		TimeSpan actual)
	{
		if (Math.Abs((expected - actual).TotalSeconds) <= TimingToleranceSeconds)
			return;
		AddUnsupported(
			delta,
			$"Event '{EventLabel(item)}' {field} changed from " +
			$"{expected.TotalSeconds:F6}s to {actual.TotalSeconds:F6}s.");
	}

	private static void CompareDouble(
		TimelineAdjustmentDelta delta,
		CandidateEventSnapshot item,
		string field,
		double expected,
		double actual)
	{
		if (Math.Abs(expected - actual) <= TimingToleranceSeconds) return;
		AddUnsupported(
			delta,
			$"Event '{EventLabel(item)}' {field} changed from " +
			$"{expected:F6} to {actual:F6}.");
	}

	private static void CompareText(
		TimelineAdjustmentDelta delta,
		CandidateEventSnapshot item,
		string field,
		string? expected,
		string? actual)
	{
		if (string.Equals(expected ?? "", actual ?? "", StringComparison.Ordinal))
			return;
		AddUnsupported(
			delta,
			$"Event '{EventLabel(item)}' {field} changed from " +
			$"'{expected ?? ""}' to '{actual ?? ""}'.");
	}

	private static void CompareVelocity(
		TimelineAdjustmentDelta delta,
		CandidateEventSnapshot expected,
		CandidateEventSnapshot actual)
	{
		IList<CandidateVelocityPoint> before =
			expected.Velocity ?? Array.Empty<CandidateVelocityPoint>();
		IList<CandidateVelocityPoint> after =
			actual.Velocity ?? Array.Empty<CandidateVelocityPoint>();
		bool same = before.Count == after.Count &&
			before.Zip(after, (left, right) =>
				Math.Abs((left.Offset - right.Offset).TotalSeconds) <=
					TimingToleranceSeconds &&
				Math.Abs(left.Velocity - right.Velocity) <= 0.0001)
			.All(value => value);
		if (!same)
			AddUnsupported(
				delta,
				$"Audio event '{EventLabel(expected)}' velocity/envelope data changed.");
	}

	private static void AddUnsupported(
		TimelineAdjustmentDelta delta,
		string message)
	{
		if (!delta.UnsupportedChanges.Contains(message, StringComparer.Ordinal))
			delta.UnsupportedChanges.Add(message);
	}

	private static string EventLabel(CandidateEventSnapshot item) =>
		!string.IsNullOrWhiteSpace(item.PlacementId)
			? item.PlacementId
			: FullPathOrEmpty(item.MediaPath);

	private static string FullPathOrEmpty(string? path) =>
		string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path);

	private static void AddChange(
		TimelineAdjustmentDelta delta,
		TimelineAdjustmentKind kind,
		string clipPath,
		double before,
		double after)
	{
		if (Math.Abs(before - after) <= TimingToleranceSeconds) return;
		delta.Changes.Add(new TimelineAdjustment
		{
			Kind = kind,
			ClipPath = clipPath,
			Before = before,
			After = after
		});
	}

	/// <summary>
	/// Reconstructs the placement's authoritative source-time speed profile from
	/// the live VEGAS velocity envelope. VEGAS defines speed linearly between
	/// consecutive envelope points along the TIMELINE axis (matching the Linear
	/// curve type EffectsApplier always writes), so source-time consumption
	/// across each segment is exactly its trapezoidal integral: the average of
	/// the two endpoint speeds times the timeline gap between them. This is the
	/// exact inverse of SpeedProfile's own source-to-timeline mapping.
	/// </summary>
	private static SpeedProfile BuildLiveSpeedProfile(
		CandidateEventSnapshot actual,
		double sourceStart)
	{
		if (actual.Velocity == null || actual.Velocity.Count == 0)
			// In VEGAS, an event without a velocity envelope plays at 1.0x.
			// The live timeline is authoritative: never substitute a proposed
			// non-1x rate for speed evidence that is absent from that timeline.
			return new SpeedProfile(new[]
			{
				new SpeedProfilePoint(sourceStart, 1.0),
				new SpeedProfilePoint(
					sourceStart + actual.TimelineDuration.TotalSeconds, 1.0)
			});
		List<CandidateVelocityPoint> ordered = actual.Velocity
			.OrderBy(item => item.Offset)
			.ToList();
		if (Math.Abs(ordered[0].Offset.TotalSeconds) > TimingToleranceSeconds)
			throw new InvalidOperationException(
				"A live velocity envelope must start at the event's first frame.");
		List<SpeedProfilePoint> points = new(ordered.Count)
		{
			new SpeedProfilePoint(sourceStart, ordered[0].Velocity)
		};
		double sourceTime = sourceStart;
		for (int index = 0; index < ordered.Count - 1; index++)
		{
			double timelineGap =
				(ordered[index + 1].Offset - ordered[index].Offset).TotalSeconds;
			if (timelineGap <= 0.0)
				throw new InvalidOperationException(
					"A live velocity envelope has duplicate or unordered points.");
			double averageSpeed = (ordered[index].Velocity + ordered[index + 1].Velocity) / 2.0;
			sourceTime += timelineGap * averageSpeed;
			points.Add(new SpeedProfilePoint(sourceTime, ordered[index + 1].Velocity));
		}
		return new SpeedProfile(points);
	}

	private static bool SameSpeedProfile(SpeedProfile before, SpeedProfile after)
	{
		if (before.Points.Count != after.Points.Count) return false;
		for (int index = 0; index < before.Points.Count; index++)
		{
			SpeedProfilePoint left = before.Points[index];
			SpeedProfilePoint right = after.Points[index];
			if (Math.Abs(left.SourceTimeSeconds - right.SourceTimeSeconds) > 0.0001 ||
				Math.Abs(left.Speed - right.Speed) > 0.0001)
				return false;
		}
		return true;
	}
}
