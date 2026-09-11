using System.Security.Cryptography;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Automation;
using AutoEditing.LlmEditor.Sessions;
using Core.Domain.Audio.SongAnalysis;
using Core.Domain.Editing;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.RoughCut;

internal sealed class RoughCutEvidenceCaptureService
{
	private readonly string sessionRoot;
	private readonly SessionPathResolver paths;
	private readonly AtomicFileWriter writer = new();
	private readonly IVegasAutomationClient automation;

	public RoughCutEvidenceCaptureService(
		string sessionRoot,
		IVegasAutomationClient automation)
	{
		this.sessionRoot = Path.GetFullPath(
			string.IsNullOrWhiteSpace(sessionRoot)
				? throw new ArgumentException("A session root is required.", nameof(sessionRoot))
				: sessionRoot);
		paths = new SessionPathResolver(this.sessionRoot);
		this.automation = automation ?? throw new ArgumentNullException(nameof(automation));
	}

	public async Task<IReadOnlyList<RoughCutEvidenceReference>> CaptureAsync(
		CandidateTimelineSnapshot timeline,
		EditPlanDocument acceptedSyncPlan,
		EditPlanningRequest planningRequest,
		string planSha256,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(timeline);
		ArgumentNullException.ThrowIfNull(acceptedSyncPlan);
		ArgumentNullException.ThrowIfNull(planningRequest);
		timeline.Workspace?.Validate();
		if (timeline.Workspace == null ||
			timeline.TimelineStart < TimeSpan.Zero ||
			timeline.TimelineEnd <= timeline.TimelineStart)
			throw new InvalidOperationException(
				"A complete candidate timeline is required for rough-cut evidence.");
		ValidateHash(planSha256);

		string evidenceId = "sync-v2-" + planSha256[..16].ToLowerInvariant();
		string root = "assembly/rough-cut/evidence/" + evidenceId;
		string manifestRelativePath = root + "/snapshot-manifest.json";
		string manifestPath = paths.Resolve(manifestRelativePath);
		if (File.Exists(manifestPath))
		{
			RoughCutVisualEvidenceManifest persisted =
				ContractSerializer.Deserialize<RoughCutVisualEvidenceManifest>(
					File.ReadAllText(manifestPath));
			ValidateManifest(persisted, timeline, planSha256);
			return EvidenceFor(persisted);
		}

		string snapshotRelativePath = root + "/timeline.json";
		string snapshotJson = ContractSerializer.Serialize(timeline);
		writer.WriteText(paths.Resolve(snapshotRelativePath), snapshotJson);

		IReadOnlyList<RoughCutEvidenceSample> samples = CreateSamples(
			timeline, acceptedSyncPlan, planningRequest);
		IReadOnlyList<TimeSpan> sampleTimes =
			samples.Select(item => item.TimelineTime).ToArray();
		CaptureCandidatePreviewFramesResult captured =
			await automation.ExecuteAsync<
				CaptureCandidatePreviewFramesRequest,
				CaptureCandidatePreviewFramesResult>(
				VegasOperations.CaptureCandidatePreviewFrames,
				new CaptureCandidatePreviewFramesRequest
				{
					Workspace = timeline.Workspace,
					TimelineTimes = sampleTimes.ToList(),
					OutputDirectoryRelativePath = root + "/frames"
				},
				"rough-cut-snapshots-" + evidenceId,
				cancellationToken: cancellationToken);
		if (captured?.Frames == null ||
			captured.Frames.Count != sampleTimes.Count)
			throw new InvalidOperationException(
				"VEGAS returned an incomplete full rough-cut frame set.");

		List<RoughCutVisualFrame> frames = new();
		for (int index = 0; index < sampleTimes.Count; index++)
		{
			CapturedCandidatePreviewFrame frame = captured.Frames[index];
			if (frame.TimelineTime != sampleTimes[index])
				throw new InvalidOperationException(
					"VEGAS changed a full rough-cut frame sample time.");
			string fullPath = paths.Resolve(frame.OutputRelativePath);
			if (!File.Exists(fullPath))
				throw new FileNotFoundException(
					"A full rough-cut frame is missing.", fullPath);
			string actualHash = Sha256File(fullPath);
			if (!string.Equals(
				actualHash, frame.Sha256, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"A full rough-cut frame failed its SHA-256 check.");
			frames.Add(new RoughCutVisualFrame
			{
				Index = index + 1,
				TimelineTime = frame.TimelineTime,
				Purpose = samples[index].Purpose,
				RelativePath = frame.OutputRelativePath,
				Sha256 = actualHash
			});
		}

		RoughCutVisualEvidenceManifest manifest = new()
		{
			SessionId = timeline.Workspace.SessionId,
			PlanSha256 = planSha256.ToLowerInvariant(),
			Workspace = timeline.Workspace,
			TimelineStart = timeline.TimelineStart,
			TimelineEnd = timeline.TimelineEnd,
			TimelineSnapshotRelativePath = snapshotRelativePath,
			TimelineSnapshotSha256 = Sha256File(paths.Resolve(snapshotRelativePath)),
			Frames = frames
		};
		ValidateManifest(manifest, timeline, planSha256);
		writer.WriteText(manifestPath, ContractSerializer.Serialize(manifest));
		return EvidenceFor(manifest);
	}

	internal static IReadOnlyList<TimeSpan> CreateSampleTimes(
		TimeSpan start,
		TimeSpan end)
	{
		if (start < TimeSpan.Zero || end <= start)
			throw new ArgumentOutOfRangeException(nameof(end));
		TimeSpan duration = end - start;
		if (duration.Ticks < 2)
			throw new ArgumentOutOfRangeException(
				nameof(end),
				"A rough-cut evidence window must contain an interior sample time.");
		int sampleCount = (int)Math.Min(9, duration.Ticks - 1);
		return Enumerable.Range(0, sampleCount)
			.Select(index =>
			{
				double ratio = sampleCount == 1
					? .5
					: .05 + index * .90 / (sampleCount - 1);
				return start + TimeSpan.FromTicks(
					Math.Clamp(
						(long)Math.Round(duration.Ticks * ratio),
						1,
						duration.Ticks - 1));
			})
			.Distinct()
			.OrderBy(value => value)
			.ToArray();
	}

	internal static IReadOnlyList<RoughCutEvidenceSample> CreateSamples(
		CandidateTimelineSnapshot timeline,
		EditPlanDocument acceptedSyncPlan,
		EditPlanningRequest planningRequest)
	{
		ArgumentNullException.ThrowIfNull(timeline);
		ArgumentNullException.ThrowIfNull(acceptedSyncPlan);
		ArgumentNullException.ThrowIfNull(planningRequest);
		if (timeline.TimelineStart < TimeSpan.Zero ||
			timeline.TimelineEnd <= timeline.TimelineStart)
			throw new ArgumentOutOfRangeException(nameof(timeline));
		if ((timeline.TimelineEnd - timeline.TimelineStart).Ticks < 2)
			throw new ArgumentOutOfRangeException(
				nameof(timeline),
				"A rough-cut evidence window must contain an interior sample time.");

		List<SampleCandidate> candidates = new();
		AddCandidate(candidates, Interior(timeline, .05), "overview",
			"opening overview", 0);
		AddCandidate(candidates, Interior(timeline, .50), "overview",
			"midpoint overview", 1);
		AddCandidate(candidates, Interior(timeline, .95), "overview",
			"closing overview", 2);

		List<ClipPlacement> placements = acceptedSyncPlan.Montage.Placements
			.OrderBy(item => item.TimelineStartSeconds)
			.ToList();
		for (int index = 1; index < placements.Count; index++)
		{
			TimeSpan join = TimeSpan.FromSeconds(
				placements[index].TimelineStartSeconds);
			AddCandidate(candidates,
				join - TimeSpan.FromMilliseconds(50),
				"join-before",
				$"50 ms before join between checkpoints {index} and {index + 1}",
				index);
			AddCandidate(candidates,
				join + TimeSpan.FromMilliseconds(50),
				"join-after",
				$"50 ms after join between checkpoints {index} and {index + 1}",
				index);
		}
		foreach (var assignment in acceptedSyncPlan.Montage.SyncAssignments)
			AddCandidate(candidates,
				TimeSpan.FromSeconds(assignment.TimelineTimeSeconds),
				"action",
				"sync " + assignment.MusicEventId,
				0);
		foreach (var item in placements.SelectMany((placement, index) =>
			placement.TimelineKillTimesSeconds.Select(time => new
			{
				Time = time,
				Checkpoint = index + 1
			})))
			AddCandidate(candidates,
				TimeSpan.FromSeconds(item.Time),
				"action",
				$"confirmed action in checkpoint {item.Checkpoint}",
				item.Checkpoint);

		MontageSongPlanningInput? song = planningRequest.SongAnalysis;
		if (song != null)
		{
			foreach (MontageSongPlanningEvent item in song.Events
				.Where(IsMajorReviewedEvent)
				.OrderByDescending(item => item.Priority)
				.ThenBy(item => item.EffectiveTimeSeconds))
				AddCandidate(candidates,
					TimeSpan.FromSeconds(item.EffectiveTimeSeconds),
					"music",
					$"reviewed {item.MusicalType} event {item.Id}",
					-Math.Max(0, item.Priority));
			foreach (MontageSongPlanningRegion region in song.Regions
				.OrderBy(item => item.StartSeconds))
			{
				AddCandidate(candidates,
					TimeSpan.FromSeconds(region.StartSeconds),
					"section",
					$"start of {region.Type} section {region.Id}",
					0);
				AddCandidate(candidates,
					TimeSpan.FromSeconds(region.EndSeconds),
					"section",
					$"end of {region.Type} section {region.Id}",
					1);
			}
		}

		candidates = candidates
			.Where(item =>
				item.Time >= timeline.TimelineStart &&
				item.Time <= timeline.TimelineEnd)
			.ToList();
		foreach (SampleCandidate item in candidates)
			item.Time = ClampInterior(item.Time, timeline);
		candidates = candidates
			.GroupBy(item => item.Time)
			.Select(group => Merge(group))
			.ToList();

		List<SampleCandidate> selected = new();
		foreach (SampleCandidate overview in candidates
			.Where(item => item.Categories.Contains("overview"))
			.OrderBy(item => item.Rank)
			.ThenBy(item => item.Time))
			AddSelected(selected, overview);
		foreach (string category in new[]
		{
			"join-before", "join-after", "action", "music", "section"
		})
		{
			SampleCandidate? candidate = candidates
				.Where(item => item.Categories.Contains(category))
				.OrderBy(item => item.Rank)
				.ThenBy(item => DistanceFromMiddle(item.Time, timeline))
				.ThenBy(item => item.Time)
				.FirstOrDefault();
			if (candidate != null) AddSelected(selected, candidate);
		}
		foreach (SampleCandidate candidate in candidates
			.OrderBy(item => item.Rank)
			.ThenBy(item => item.Time))
		{
			if (selected.Count >= 9) break;
			AddSelected(selected, candidate);
		}
		return selected
			.OrderBy(item => item.Time)
			.Select(item => new RoughCutEvidenceSample(
				item.Time,
				string.Join("; ", item.Purposes.Distinct(StringComparer.Ordinal))))
			.ToArray();
	}

	private static bool IsMajorReviewedEvent(MontageSongPlanningEvent item) =>
		item.IsReviewed &&
		!item.IsIntentionallyUnused &&
		(item.Priority >= 2 ||
			item.MusicalType is MusicEventType.Drop or MusicEventType.BuildHit or
				MusicEventType.PhraseBoundary or MusicEventType.ManualSyncPoint);

	private static TimeSpan Interior(CandidateTimelineSnapshot timeline, double ratio) =>
		timeline.TimelineStart + TimeSpan.FromTicks(
			(long)Math.Round(
				(timeline.TimelineEnd - timeline.TimelineStart).Ticks * ratio));

	private static TimeSpan ClampInterior(
		TimeSpan value,
		CandidateTimelineSnapshot timeline) =>
		TimeSpan.FromTicks(Math.Clamp(
			value.Ticks,
			timeline.TimelineStart.Ticks + 1,
			timeline.TimelineEnd.Ticks - 1));

	private static long DistanceFromMiddle(
		TimeSpan time,
		CandidateTimelineSnapshot timeline) =>
		Math.Abs(time.Ticks -
			(timeline.TimelineStart.Ticks + timeline.TimelineEnd.Ticks) / 2);

	private static void AddCandidate(
		ICollection<SampleCandidate> candidates,
		TimeSpan time,
		string category,
		string purpose,
		int rank) =>
		candidates.Add(new SampleCandidate(time, category, purpose, rank));

	private static SampleCandidate Merge(IEnumerable<SampleCandidate> source)
	{
		SampleCandidate[] items = source.ToArray();
		SampleCandidate merged = new(
			items[0].Time,
			items[0].Categories[0],
			items[0].Purposes[0],
			items.Min(item => item.Rank));
		foreach (SampleCandidate item in items.Skip(1))
		{
			merged.Categories.AddRange(item.Categories);
			merged.Purposes.AddRange(item.Purposes);
		}
		return merged;
	}

	private static void AddSelected(
		ICollection<SampleCandidate> selected,
		SampleCandidate candidate)
	{
		if (selected.Any(item => item.Time == candidate.Time)) return;
		selected.Add(candidate);
	}

	private IReadOnlyList<RoughCutEvidenceReference> EvidenceFor(
		RoughCutVisualEvidenceManifest manifest)
	{
		List<RoughCutEvidenceReference> evidence = new()
		{
			new()
			{
				EvidenceId = "rough-cut-timeline-" + manifest.PlanSha256[..16],
				Kind = RoughCutEvidenceKind.TimelineSnapshot,
				RelativePath = manifest.TimelineSnapshotRelativePath,
				Sha256 = manifest.TimelineSnapshotSha256,
				MediaType = "application/json",
				Description =
					"Authoritative complete candidate timeline used for the rough-cut audit."
			}
		};
		foreach (RoughCutVisualFrame frame in manifest.Frames)
			evidence.Add(new RoughCutEvidenceReference
			{
				EvidenceId =
					$"rough-cut-snapshot-{manifest.PlanSha256[..16]}-{frame.Index:D2}",
				Kind = RoughCutEvidenceKind.SnapshotFrame,
				RelativePath = frame.RelativePath,
				Sha256 = frame.Sha256,
				MediaType = "image/png",
				TimelineTimeSeconds = frame.TimelineTime.TotalSeconds,
				Description =
					$"VEGAS snapshot {frame.Index} of {manifest.Frames.Count} from " +
					$"the isolated full rough-cut candidate at " +
					$"{frame.TimelineTime.TotalSeconds:0.###} s; target: {frame.Purpose}."
			});
		return evidence;
	}

	private void ValidateManifest(
		RoughCutVisualEvidenceManifest manifest,
		CandidateTimelineSnapshot timeline,
		string planSha256)
	{
		if (manifest.SchemaVersion != RoughCutVisualEvidenceManifest.CurrentSchemaVersion ||
			!string.Equals(
				manifest.SessionId, timeline.Workspace.SessionId, StringComparison.Ordinal) ||
			!string.Equals(
				manifest.PlanSha256, planSha256, StringComparison.OrdinalIgnoreCase) ||
			manifest.Workspace == null ||
			!string.Equals(
				manifest.Workspace.ToString(),
				timeline.Workspace.ToString(),
				StringComparison.Ordinal) ||
			manifest.TimelineStart != timeline.TimelineStart ||
			manifest.TimelineEnd != timeline.TimelineEnd ||
			manifest.Frames == null ||
			manifest.Frames.Count < 1 ||
			manifest.Frames.Count > 9)
			throw new InvalidDataException(
				"The persisted rough-cut visual evidence does not match the current plan.");
		ValidateHash(manifest.TimelineSnapshotSha256);
		if (!File.Exists(paths.Resolve(manifest.TimelineSnapshotRelativePath)) ||
			!string.Equals(
				Sha256File(paths.Resolve(manifest.TimelineSnapshotRelativePath)),
				manifest.TimelineSnapshotSha256,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The persisted rough-cut timeline evidence is missing or corrupt.");
		TimeSpan previous = TimeSpan.MinValue;
		int expectedIndex = 1;
		foreach (RoughCutVisualFrame frame in manifest.Frames)
		{
			ValidateHash(frame.Sha256);
			if (frame.Index != expectedIndex ||
				string.IsNullOrWhiteSpace(frame.Purpose) ||
				frame.TimelineTime <= previous ||
				frame.TimelineTime <= manifest.TimelineStart ||
				frame.TimelineTime >= manifest.TimelineEnd)
				throw new InvalidDataException(
					"A persisted rough-cut snapshot frame is invalid.");
			string path = paths.Resolve(frame.RelativePath);
			if (!File.Exists(path) ||
				!string.Equals(
					Sha256File(path), frame.Sha256, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					"A persisted rough-cut snapshot frame is missing or corrupt.");
			previous = frame.TimelineTime;
			expectedIndex++;
		}
	}

	private static void ValidateHash(string value)
	{
		if (value == null || value.Length != 64 ||
			value.Any(character => !Uri.IsHexDigit(character)))
			throw new InvalidDataException("A valid SHA-256 value is required.");
	}

	private static string Sha256File(string path)
	{
		using FileStream stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
	}
}

internal sealed class RoughCutVisualEvidenceManifest
{
	public const int CurrentSchemaVersion = 2;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public string PlanSha256 { get; set; } = "";
	public CandidateWorkspaceId Workspace { get; set; } = null!;
	public TimeSpan TimelineStart { get; set; }
	public TimeSpan TimelineEnd { get; set; }
	public string TimelineSnapshotRelativePath { get; set; } = "";
	public string TimelineSnapshotSha256 { get; set; } = "";
	public IList<RoughCutVisualFrame> Frames { get; set; } =
		new List<RoughCutVisualFrame>();
}

internal sealed class RoughCutVisualFrame
{
	public int Index { get; set; }
	public TimeSpan TimelineTime { get; set; }
	public string Purpose { get; set; } = "";
	public string RelativePath { get; set; } = "";
	public string Sha256 { get; set; } = "";
}

internal sealed record RoughCutEvidenceSample(TimeSpan TimelineTime, string Purpose);

internal sealed class SampleCandidate
{
	public SampleCandidate(
		TimeSpan time,
		string category,
		string purpose,
		int rank)
	{
		Time = time;
		Categories.Add(category);
		Purposes.Add(purpose);
		Rank = rank;
	}

	public TimeSpan Time { get; set; }
	public List<string> Categories { get; } = new();
	public List<string> Purposes { get; } = new();
	public int Rank { get; }
}
