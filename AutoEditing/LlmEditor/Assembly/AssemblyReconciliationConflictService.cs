using System.Security.Cryptography;
using System.Text;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Sessions;
using Core.Domain.Editing;
using Core.Domain.Planning;
using Newtonsoft.Json.Linq;

namespace AutoEditing.LlmEditor.Assembly;

internal sealed class AssemblyReconciliationConflictService
{
	private readonly SessionPathResolver paths;
	private readonly AtomicFileWriter writer = new();

	public AssemblyReconciliationConflictService(string sessionRoot)
	{
		paths = new SessionPathResolver(sessionRoot);
	}

	public AssemblyReconciliationConflict Create(
		string sessionId,
		int checkpoint,
		EditPlanningRequest request,
		EditPlanDocument proposal,
		CandidateTimelineSnapshot snapshot,
		Exception failure)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(proposal);
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(failure);
		List<CandidateEventSnapshot> videoEvents = VideoEvents(snapshot);
		bool durable = HasDurableIdentity(snapshot, videoEvents);
		HashSet<CandidateEventSnapshot> matched = new();
		List<AssemblyReconciliationConflictIssue> issues = new();
		for (int index = 0; index < proposal.Montage.Placements.Count; index++)
		{
			ClipPlacement expected = proposal.Montage.Placements[index];
			string expectedPath = Path.GetFullPath(expected.Clip.FilePath);
			string expectedId = durable
				? CandidatePlacementIdentity.Create(
					snapshot.Workspace!, index + 1, expectedPath)
				: expectedPath;
			List<CandidateEventSnapshot> matches = durable
				? videoEvents.Where(item => string.Equals(
					item.PlacementId, expectedId, StringComparison.Ordinal)).ToList()
				: videoEvents.Where(item => string.Equals(
					Path.GetFullPath(item.MediaPath), expectedPath,
					StringComparison.OrdinalIgnoreCase)).ToList();
			if (matches.Count == 0)
			{
				issues.Add(Issue(
					AssemblyReconciliationConflictKind.MissingExpectedEvent,
					expectedId,
					expectedPath,
					"Expected candidate event is absent from the live VEGAS workspace."));
				continue;
			}
			if (matches.Count > 1)
			{
				issues.Add(Issue(
					AssemblyReconciliationConflictKind.AmbiguousIdentity,
					expectedId,
					expectedPath,
					matches.Count + " live events resolve to the same expected placement."));
				continue;
			}
			CandidateEventSnapshot actual = matches[0];
			if (!string.Equals(
				Path.GetFullPath(actual.MediaPath), expectedPath,
				StringComparison.OrdinalIgnoreCase))
			{
				issues.Add(Issue(
					AssemblyReconciliationConflictKind.MediaReplacement,
					expectedId,
					expectedPath,
					"The expected durable identity points at different media."));
				continue;
			}
			matched.Add(actual);
		}
		foreach (CandidateEventSnapshot item in videoEvents.Where(item => !matched.Contains(item)))
		{
			if (issues.Any(issue =>
				string.Equals(issue.ExpectedPlacementId, item.PlacementId,
					StringComparison.Ordinal)))
				continue;
			issues.Add(Issue(
				AssemblyReconciliationConflictKind.UnexpectedEvent,
				item.PlacementId,
				item.MediaPath,
				"Live candidate contains an event that is not in the exact proposal."));
		}
		if (issues.Count == 0)
		{
			issues.Add(Issue(
				AssemblyReconciliationConflictKind.UnsupportedTimelineEdit,
				"",
				proposal.Montage.Placements.Last().Clip.FilePath,
				failure.Message));
		}

		string currentPath = Path.GetFullPath(
			proposal.Montage.Placements.Last().Clip.FilePath);
		HashSet<string> proposalPaths = new(
			proposal.Montage.Placements.Select(item =>
				Path.GetFullPath(item.Clip.FilePath)),
			StringComparer.OrdinalIgnoreCase);
		HashSet<string> selectedPaths = new(
			request.Clips.Select(item => Path.GetFullPath(item.FilePath)),
			StringComparer.OrdinalIgnoreCase);
		bool currentUnavailable = issues.Any(item =>
			string.Equals(
				Path.GetFullPath(string.IsNullOrWhiteSpace(item.ExpectedMediaPath)
					? currentPath : item.ExpectedMediaPath),
				currentPath,
				StringComparison.OrdinalIgnoreCase) &&
			item.Kind is AssemblyReconciliationConflictKind.MissingExpectedEvent or
				AssemblyReconciliationConflictKind.MediaReplacement);
		List<AssemblyReconciliationEventCandidate> candidates = videoEvents
			.Where(item => !matched.Contains(item))
			.Select(item =>
			{
				string path = Path.GetFullPath(item.MediaPath);
				bool selected = selectedPaths.Contains(path);
				bool remaining = selected && !proposalPaths.Contains(path);
				bool timingValid = IsAdoptableTiming(
					item,
					request.Clips.FirstOrDefault(clip => string.Equals(
						Path.GetFullPath(clip.FilePath), path,
						StringComparison.OrdinalIgnoreCase)));
				bool canCurrent = remaining && timingValid && currentUnavailable &&
					CanSafelyAdopt(request, proposal, item, asCurrent: true);
				bool canAdditional = remaining && timingValid && !currentUnavailable &&
					CanSafelyAdopt(request, proposal, item, asCurrent: false);
				return new AssemblyReconciliationEventCandidate
				{
					CandidateId = CandidateId(item),
					PlacementId = item.PlacementId ?? "",
					MediaPath = path,
					TimelineStartSeconds = item.TimelineStart.TotalSeconds,
					TimelineDurationSeconds = item.TimelineDuration.TotalSeconds,
					SourceOffsetSeconds = item.SourceOffset.TotalSeconds,
					ConstantSpeed = SafeConstantSpeed(item),
					IsKnownSelectedClip = selected,
					IsRemainingClip = remaining,
					CanAdoptAsCurrent = canCurrent,
					CanAdoptAsAdditional = canAdditional,
					AdoptionConstraint = !selected
						? "Media is not part of the reviewed request."
						: !remaining
							? "Media is already represented by the proposal."
							: !timingValid
								? "Timing, source bounds, or velocity cannot be represented safely."
								: !canCurrent && !canAdditional
									? "Adoption would make the combined plan invalid."
								: currentUnavailable
									? "May explicitly replace the unavailable current proposal."
									: "May explicitly become an additional accepted clip."
				};
			})
			.GroupBy(item => item.CandidateId, StringComparer.Ordinal)
			.Select(group => group.First())
			.OrderBy(item => item.TimelineStartSeconds)
			.ThenBy(item => item.CandidateId, StringComparer.Ordinal)
			.ToList();
		List<AssemblyReconciliationResolutionKind> supported = new()
		{
			AssemblyReconciliationResolutionKind.RestoreExactProposal,
			AssemblyReconciliationResolutionKind.DeferAndPause
		};
		if (CanExcludeCurrent(proposal, snapshot, currentPath, issues))
			supported.Add(AssemblyReconciliationResolutionKind.ExcludeCurrentClip);
		if (candidates.Any(item => item.CanAdoptAsCurrent || item.CanAdoptAsAdditional))
			supported.Add(
				AssemblyReconciliationResolutionKind.AdoptKnownRemainingEvent);
		string identityMaterial = sessionId + "|" + checkpoint + "|" +
			string.Join("|", issues.Select(item =>
				item.Kind + ":" + item.ExpectedPlacementId + ":" +
				item.ExpectedMediaPath)) + "|" +
			string.Join("|", candidates.Select(item => item.CandidateId));
		AssemblyReconciliationConflict conflict = new()
		{
			ConflictId = "sync-conflict-" + Hash(identityMaterial)[..24],
			SessionId = sessionId,
			Checkpoint = checkpoint,
			CurrentClipPath = currentPath,
			FailureSummary = failure.Message,
			Issues = issues,
			Candidates = candidates,
			SupportedResolutions = supported,
			CreatedUtc = DateTimeOffset.UtcNow
		};
		AssemblyReconciliationConflictValidator.Validate(conflict);
		string root = Root(checkpoint);
		string immutablePath = paths.Resolve(root + "/conflicts/" +
			conflict.ConflictId + ".json");
		if (File.Exists(immutablePath))
		{
			AssemblyReconciliationConflict existing =
				ContractSerializer.Deserialize<AssemblyReconciliationConflict>(
					File.ReadAllText(immutablePath));
			AssemblyReconciliationConflictValidator.Validate(existing);
			if (!string.Equals(
				SemanticConflictHash(existing),
				SemanticConflictHash(conflict),
				StringComparison.Ordinal))
				throw new InvalidDataException(
					"A deterministic conflict ID resolved to different evidence.");
			conflict = existing;
		}
		else
		{
			writer.WriteText(
				immutablePath,
				ContractSerializer.Serialize(conflict));
		}
		string json = ContractSerializer.Serialize(conflict);
		writer.WriteText(paths.Resolve(root + "/current-conflict.json"), json);
		return conflict;
	}

	public AssemblyReconciliationResolution SaveResolution(
		AssemblyReconciliationConflict conflict,
		AssemblyReconciliationResolutionKind kind,
		string targetCandidateId,
		string instruction)
	{
		AssemblyReconciliationResolution resolution = new()
		{
			ResolutionId = "sync-resolution-" + Hash(
				conflict.ConflictId + "|" + kind + "|" +
				(targetCandidateId ?? "").Trim())[..24],
			ConflictId = conflict.ConflictId,
			SessionId = conflict.SessionId,
			Checkpoint = conflict.Checkpoint,
			Kind = kind,
			TargetCandidateId = (targetCandidateId ?? "").Trim(),
			Instruction = (instruction ?? "").Trim(),
			ResolvedBy = "VEGAS workbench editor",
			ResolvedUtc = DateTimeOffset.UtcNow
		};
		AssemblyReconciliationConflictValidator.Validate(resolution, conflict);
		string root = Root(conflict.Checkpoint);
		string immutable = paths.Resolve(
			root + "/resolutions/" + resolution.ResolutionId + ".json");
		if (File.Exists(immutable))
		{
			AssemblyReconciliationResolution existing =
				ContractSerializer.Deserialize<AssemblyReconciliationResolution>(
					File.ReadAllText(immutable));
			AssemblyReconciliationConflictValidator.Validate(existing, conflict);
			if (!string.Equals(
				existing.ResolutionId,
				resolution.ResolutionId,
				StringComparison.Ordinal))
				throw new InvalidDataException(
					"The persisted reconciliation resolution identity changed.");
			resolution = existing;
		}
		else
		{
			writer.WriteText(
				immutable,
				ContractSerializer.Serialize(resolution));
		}
		if (kind != AssemblyReconciliationResolutionKind.DeferAndPause)
			writer.WriteText(
				paths.Resolve(root + "/current-resolution.json"),
				ContractSerializer.Serialize(resolution));
		return resolution;
	}

	public AssemblyReconciliationResolution? ReadCurrentResolution(int checkpoint)
	{
		string path = paths.Resolve(Root(checkpoint) + "/current-resolution.json");
		return File.Exists(path)
			? ContractSerializer.Deserialize<AssemblyReconciliationResolution>(
				File.ReadAllText(path))
			: null;
	}

	public void SaveActionIntent(
		AssemblyReconciliationConflict conflict,
		AssemblyAction action)
	{
		ArgumentNullException.ThrowIfNull(conflict);
		ArgumentNullException.ThrowIfNull(action);
		if (!string.Equals(
			conflict.SessionId, action.SessionId, StringComparison.Ordinal) ||
			conflict.Checkpoint != action.Checkpoint)
			throw new InvalidDataException(
				"The conflict action targets another session or checkpoint.");
		ReconciliationActionIntent intent = new()
		{
			ConflictId = conflict.ConflictId,
			Action = action
		};
		string path = paths.Resolve(
			Root(conflict.Checkpoint) + "/current-action.json");
		if (File.Exists(path))
		{
			ReconciliationActionIntent existing =
				ContractSerializer.Deserialize<ReconciliationActionIntent>(
					File.ReadAllText(path));
			if (!string.Equals(
				existing.ConflictId, intent.ConflictId, StringComparison.Ordinal) ||
				!string.Equals(
					existing.Action.ActionId,
					action.ActionId,
					StringComparison.Ordinal))
				throw new InvalidDataException(
					"Another reconciliation action intent is already pending.");
			return;
		}
		writer.WriteText(path, ContractSerializer.Serialize(intent));
	}

	public ReconciliationActionIntent? ReadActionIntent(int checkpoint)
	{
		string path = paths.Resolve(Root(checkpoint) + "/current-action.json");
		return File.Exists(path)
			? ContractSerializer.Deserialize<ReconciliationActionIntent>(
				File.ReadAllText(path))
			: null;
	}

	public void CompleteActionIntent(AssemblyAction action)
	{
		ArgumentNullException.ThrowIfNull(action);
		string root = Root(action.Checkpoint);
		string current = paths.Resolve(root + "/current-action.json");
		if (!File.Exists(current)) return;
		ReconciliationActionIntent intent =
			ContractSerializer.Deserialize<ReconciliationActionIntent>(
				File.ReadAllText(current));
		if (!string.Equals(
			intent.Action.ActionId, action.ActionId, StringComparison.Ordinal))
			throw new InvalidDataException(
				"The completed action does not match the pending conflict action.");
		writer.WriteText(
			paths.Resolve(
				root + "/completed-actions/" + action.ActionId + ".json"),
			ContractSerializer.Serialize(intent));
		File.Delete(current);
	}

	public AssemblyReconciliationConflict ReadConflict(
		int checkpoint,
		string conflictId)
	{
		string path = paths.Resolve(
			Root(checkpoint) + "/conflicts/" + conflictId + ".json");
		if (!File.Exists(path))
			throw new InvalidDataException(
				"The pending reconciliation intent has no conflict artifact.");
		AssemblyReconciliationConflict value =
			ContractSerializer.Deserialize<AssemblyReconciliationConflict>(
				File.ReadAllText(path));
		AssemblyReconciliationConflictValidator.Validate(value);
		return value;
	}

	public void CompleteResolution(AssemblyReconciliationResolution resolution)
	{
		ArgumentNullException.ThrowIfNull(resolution);
		string root = Root(resolution.Checkpoint);
		writer.WriteText(
			paths.Resolve(root + "/completed/" + resolution.ResolutionId + ".json"),
			ContractSerializer.Serialize(resolution));
		string current = paths.Resolve(root + "/current-resolution.json");
		if (!File.Exists(current)) return;
		AssemblyReconciliationResolution persisted =
			ContractSerializer.Deserialize<AssemblyReconciliationResolution>(
				File.ReadAllText(current));
		if (string.Equals(
			persisted.ResolutionId,
			resolution.ResolutionId,
			StringComparison.Ordinal))
			File.Delete(current);
	}

	public void SaveVerifiedOutcome(
		AssemblyReconciliationResolution resolution,
		EditPlanDocument plan,
		CandidateTimelineSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(resolution);
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(snapshot);
		VerifiedReconciliationOutcome outcome = new()
		{
			ResolutionId = resolution.ResolutionId,
			ConflictId = resolution.ConflictId,
			SessionId = resolution.SessionId,
			Checkpoint = resolution.Checkpoint,
			PlanSha256 = PlanHash(plan),
			SnapshotSha256 = ContractHash.Compute(JToken.FromObject(snapshot)),
			VerifiedUtc = DateTimeOffset.UtcNow
		};
		writer.WriteText(
			paths.Resolve(
				Root(resolution.Checkpoint) + "/verified/" +
				resolution.ResolutionId + ".json"),
			ContractSerializer.Serialize(outcome));
	}

	public bool CompleteAcceptedResolutionIfVerified(
		int checkpoint,
		EditPlanDocument acceptedPlan)
	{
		AssemblyReconciliationResolution? pending =
			ReadCurrentResolution(checkpoint);
		if (pending == null) return false;
		string path = paths.Resolve(
			Root(checkpoint) + "/verified/" + pending.ResolutionId + ".json");
		if (!File.Exists(path))
			throw new InvalidDataException(
				"An accepted checkpoint has a pending reconciliation intent " +
				"without a verified outcome.");
		VerifiedReconciliationOutcome outcome =
			ContractSerializer.Deserialize<VerifiedReconciliationOutcome>(
				File.ReadAllText(path));
		if (!string.Equals(
				outcome.ResolutionId, pending.ResolutionId, StringComparison.Ordinal) ||
			!string.Equals(
				outcome.ConflictId, pending.ConflictId, StringComparison.Ordinal) ||
			!string.Equals(
				outcome.SessionId, pending.SessionId, StringComparison.Ordinal) ||
			outcome.Checkpoint != checkpoint ||
			!string.Equals(
				outcome.PlanSha256,
				PlanHash(acceptedPlan),
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The accepted checkpoint does not match its verified " +
				"reconciliation outcome.");
		CompleteResolution(pending);
		ReconciliationActionIntent? actionIntent =
			ReadActionIntent(checkpoint);
		if (actionIntent != null)
			CompleteActionIntent(actionIntent.Action);
		return true;
	}

	public AssemblyReconciliationEventCandidate RequireCandidate(
		AssemblyReconciliationConflict conflict,
		string candidateId) =>
		conflict.Candidates.SingleOrDefault(item => string.Equals(
			item.CandidateId, candidateId, StringComparison.Ordinal))
		?? throw new InvalidOperationException(
			"The selected live event is not part of the persisted conflict.");

	public CandidateEventSnapshot RequireLiveCandidate(
		CandidateTimelineSnapshot snapshot,
		AssemblyReconciliationEventCandidate candidate) =>
		VideoEvents(snapshot).SingleOrDefault(item => string.Equals(
			CandidateId(item), candidate.CandidateId, StringComparison.Ordinal))
		?? throw new InvalidOperationException(
			"The selected live event changed after the conflict was presented.");

	public static EditPlanDocument Adopt(
		EditPlanningRequest request,
		EditPlanDocument proposal,
		CandidateEventSnapshot actual,
		bool asCurrent)
	{
		Core.Domain.Clip.Clip clip = request.Clips.Single(item => string.Equals(
			Path.GetFullPath(item.FilePath),
			Path.GetFullPath(actual.MediaPath),
			StringComparison.OrdinalIgnoreCase));
		double speed = ConstantSpeed(actual);
		double sourceEnd =
			actual.SourceOffset.TotalSeconds +
			actual.TimelineDuration.TotalSeconds * speed;
		ClipPlacement adopted = new()
		{
			Clip = clip,
			TimelineStartSeconds = actual.TimelineStart.TotalSeconds,
			SourceOffsetSeconds = actual.SourceOffset.TotalSeconds,
			SpeedProfile = new SpeedProfile(new[]
			{
				new SpeedProfilePoint(actual.SourceOffset.TotalSeconds, speed),
				new SpeedProfilePoint(sourceEnd, speed)
			}),
			LengthSeconds = actual.TimelineDuration.TotalSeconds,
			AssignedBeatTimesSeconds = new List<double>()
		};
		EditPlanDocument resolved = Clone(proposal);
		if (asCurrent)
		{
			string removed = resolved.Montage.Placements.Last().Clip.FilePath;
			resolved.Montage.Placements[resolved.Montage.Placements.Count - 1] = adopted;
			resolved.Montage.SyncAssignments = resolved.Montage.SyncAssignments
				.Where(item => !string.Equals(
					item.ClipPath, removed, StringComparison.OrdinalIgnoreCase))
				.ToList();
		}
		else
		{
			resolved.Montage.Placements.Add(adopted);
		}
		EditPlanDocumentValidator.ValidateAndNormalize(resolved);
		return resolved;
	}

	public static bool ReorderSketchForAdoption(
		AssemblySketch sketch,
		int checkpoint,
		string adoptedPath,
		bool asCurrent)
	{
		List<AssemblyClipIntent> original = sketch.ClipOrder
			.OrderBy(item => item.Order).ToList();
		int existingIndex = original.FindIndex(item =>
			string.Equals(
				Path.GetFullPath(item.Clip.MediaPath),
				Path.GetFullPath(adoptedPath),
				StringComparison.OrdinalIgnoreCase));
		if (existingIndex < 0)
			throw new InvalidOperationException(
				"The adopted clip is no longer in the semantic sketch.");
		int targetIndex = asCurrent ? checkpoint - 1 : checkpoint;
		if (existingIndex == targetIndex) return false;
		AssemblyClipIntent adopted = original[existingIndex];
		List<AssemblyClipIntent> ordered = original.ToList();
		ordered.Remove(adopted);
		if (asCurrent)
		{
			AssemblyClipIntent displaced = ordered[targetIndex];
			ordered.RemoveAt(targetIndex);
			ordered.Insert(targetIndex, adopted);
			ordered.Insert(Math.Min(adopted.Order - 1, ordered.Count), displaced);
		}
		else
		{
			ordered.Insert(Math.Min(targetIndex, ordered.Count), adopted);
		}
		for (int index = 0; index < ordered.Count; index++)
			ordered[index].Order = index + 1;
		sketch.ClipOrder = ordered;
		ProgressiveAssemblyContractValidator.Validate(sketch);
		return true;
	}

	public static bool ExcludeCurrentFromSketch(
		AssemblySketch sketch,
		int checkpoint,
		string currentPath)
	{
		AssemblyClipIntent? current = sketch.ClipOrder.SingleOrDefault(item =>
			string.Equals(
				Path.GetFullPath(item.Clip.MediaPath),
				Path.GetFullPath(currentPath),
				StringComparison.OrdinalIgnoreCase));
		if (current == null) return false;
		sketch.ClipOrder.Remove(current);
		List<AssemblyClipIntent> ordered = sketch.ClipOrder
			.OrderBy(item => item.Order).ToList();
		for (int index = 0; index < ordered.Count; index++)
			ordered[index].Order = index + 1;
		sketch.ClipOrder = ordered;
		ProgressiveAssemblyContractValidator.Validate(sketch);
		return true;
	}

	private static bool CanExcludeCurrent(
		EditPlanDocument proposal,
		CandidateTimelineSnapshot snapshot,
		string currentPath,
		IReadOnlyCollection<AssemblyReconciliationConflictIssue> issues)
	{
		if (proposal.Montage.Placements.Count <= 1) return false;
		if (!issues.Any(item =>
			item.Kind == AssemblyReconciliationConflictKind.MissingExpectedEvent &&
			string.Equals(
				Path.GetFullPath(item.ExpectedMediaPath), currentPath,
				StringComparison.OrdinalIgnoreCase)))
			return false;
		try
		{
			EditPlanDocument withoutCurrent = Clone(proposal);
			withoutCurrent.Montage.Placements.RemoveAt(
				withoutCurrent.Montage.Placements.Count - 1);
			withoutCurrent.Montage.SyncAssignments =
				withoutCurrent.Montage.SyncAssignments.Where(item =>
					withoutCurrent.Montage.Placements.Any(placement =>
						string.Equals(
							placement.Clip.FilePath, item.ClipPath,
							StringComparison.OrdinalIgnoreCase))).ToList();
			AssemblyTimelineReconciler.Apply(withoutCurrent, snapshot);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static AssemblyReconciliationConflictIssue Issue(
		AssemblyReconciliationConflictKind kind,
		string expectedId,
		string expectedPath,
		string detail) => new()
	{
		Kind = kind,
		ExpectedPlacementId = expectedId ?? "",
		ExpectedMediaPath = expectedPath ?? "",
		Detail = detail
	};

	private static bool IsAdoptableTiming(
		CandidateEventSnapshot item,
		Core.Domain.Clip.Clip? clip)
	{
		if (clip == null ||
			item.TimelineStart < TimeSpan.Zero ||
			item.TimelineDuration <= TimeSpan.Zero ||
			item.SourceOffset < TimeSpan.Zero)
			return false;
		try
		{
			double speed = ConstantSpeed(item);
			return item.SourceOffset.TotalSeconds +
				item.TimelineDuration.TotalSeconds * speed <=
				clip.DurationSeconds + 0.002;
		}
		catch
		{
			return false;
		}
	}

	private static bool CanSafelyAdopt(
		EditPlanningRequest request,
		EditPlanDocument proposal,
		CandidateEventSnapshot item,
		bool asCurrent)
	{
		try
		{
			Adopt(request, proposal, item, asCurrent);
			return true;
		}
		catch (Exception exception) when (
			exception is InvalidOperationException ||
			exception is InvalidDataException ||
			exception is ArgumentException)
		{
			return false;
		}
	}

	private static double ConstantSpeed(CandidateEventSnapshot actual)
	{
		if (actual.Velocity == null || actual.Velocity.Count == 0) return 1;
		double first = actual.Velocity[0].Velocity;
		if (first < 0.25 || first > 4 ||
			actual.Velocity.Any(item =>
				Math.Abs(item.Velocity - first) > 0.0001))
			throw new InvalidOperationException(
				"Only a representable constant velocity may be adopted.");
		return first;
	}

	private static double SafeConstantSpeed(CandidateEventSnapshot actual)
	{
		try { return ConstantSpeed(actual); }
		catch { return 1; }
	}

	private static List<CandidateEventSnapshot> VideoEvents(
		CandidateTimelineSnapshot snapshot) =>
		snapshot.Tracks
			.Where(track => string.Equals(
				track.MediaKind, "Video", StringComparison.OrdinalIgnoreCase))
			.SelectMany(track => track.Events)
			.Where(item => !string.IsNullOrWhiteSpace(item.MediaPath))
			.ToList();

	private static bool HasDurableIdentity(
		CandidateTimelineSnapshot snapshot,
		IEnumerable<CandidateEventSnapshot> events) =>
		snapshot.Workspace != null &&
		events.Any(item => CandidatePlacementIdentity.IsOwned(
			snapshot.Workspace, item.PlacementId));

	internal static string CandidateId(CandidateEventSnapshot item)
	{
		string material = (item.PlacementId ?? "") + "|" +
			Path.GetFullPath(item.MediaPath).ToLowerInvariant() + "|" +
			item.TimelineStart.Ticks + "|" + item.TimelineDuration.Ticks + "|" +
			item.SourceOffset.Ticks + "|" +
			string.Join(",", (item.Velocity ?? new List<CandidateVelocityPoint>())
				.Select(point =>
					point.Offset.Ticks + ":" +
					point.Velocity.ToString("R",
						System.Globalization.CultureInfo.InvariantCulture)));
		return "event-" + Hash(material)[..24];
	}

	private static EditPlanDocument Clone(EditPlanDocument source) =>
		EditPlanDocumentSerializer.DeserializePlan(
			EditPlanDocumentSerializer.SerializePlan(source));

	private static string Hash(string value)
	{
		using SHA256 sha = SHA256.Create();
		return Convert.ToHexString(
			sha.ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
	}

	private static string PlanHash(EditPlanDocument plan) =>
		Hash(EditPlanDocumentSerializer.SerializePlan(plan));

	private static string SemanticConflictHash(
		AssemblyReconciliationConflict value)
	{
		AssemblyReconciliationConflict clone =
			ContractSerializer.Deserialize<AssemblyReconciliationConflict>(
				ContractSerializer.Serialize(value));
		clone.CreatedUtc = DateTimeOffset.UnixEpoch;
		return ContractHash.Compute(JToken.FromObject(clone));
	}

	private static string Root(int checkpoint) =>
		$"assembly/checkpoints/{checkpoint:D4}/reconciliation";
}

internal sealed class VerifiedReconciliationOutcome
{
	public string ResolutionId { get; set; } = "";
	public string ConflictId { get; set; } = "";
	public string SessionId { get; set; } = "";
	public int Checkpoint { get; set; }
	public string PlanSha256 { get; set; } = "";
	public string SnapshotSha256 { get; set; } = "";
	public DateTimeOffset VerifiedUtc { get; set; }
}

internal sealed class ReconciliationActionIntent
{
	public string ConflictId { get; set; } = "";
	public AssemblyAction Action { get; set; } = new();
}
