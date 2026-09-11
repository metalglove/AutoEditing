using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Sessions;
using Core.Domain.Planning;
using System.Security.Cryptography;
using System.Text;

namespace AutoEditing.LlmEditor.Assembly;

internal sealed class AssemblyArtifactStore
{
	private readonly SessionPathResolver paths;
	private readonly AtomicFileWriter writer = new();

	public AssemblyArtifactStore(string sessionRoot)
	{
		paths = new SessionPathResolver(sessionRoot);
	}

	public AssemblySessionDescriptor InitializeSession(
		string sessionId,
		EditPlanningRequest request,
		string projectPath = "",
		string projectFingerprint = "")
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
		ArgumentNullException.ThrowIfNull(request);
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		string requestJson = EditPlanDocumentSerializer.SerializeRequest(request);
		AssemblySessionDescriptor descriptor = new()
		{
			SessionId = sessionId,
			RequestId = request.RequestId,
			RequestSha256 = ComputeSha256(requestJson),
			SongPath = request.SongPath,
			TotalClips = request.Clips.Count,
			ProjectPath = projectPath ?? "",
			ProjectFingerprint = projectFingerprint ?? ""
		};
		writer.WriteText(paths.Resolve("assembly/request.json"), requestJson);
		writer.WriteText(
			paths.Resolve("assembly/session.json"),
			ContractSerializer.Serialize(descriptor));
		return descriptor;
	}

	public AssemblySessionDescriptor? ReadSessionDescriptor()
	{
		string path = paths.Resolve("assembly/session.json");
		return File.Exists(path)
			? ContractSerializer.Deserialize<AssemblySessionDescriptor>(
				File.ReadAllText(path))
			: null;
	}

	public AssemblySessionDescriptor CaptureProjectIdentity(VegasHostIdentity host)
	{
		ArgumentNullException.ThrowIfNull(host);
		AssemblySessionDescriptor descriptor = ReadSessionDescriptor()
			?? throw new InvalidDataException(
				"The assembly session descriptor must exist before project identity is captured.");
		if (string.IsNullOrWhiteSpace(host.ProjectFingerprint))
			throw new InvalidDataException(
				"VEGAS did not provide a project fingerprint for recovery.");
		if (!string.IsNullOrWhiteSpace(descriptor.ProjectFingerprint) &&
			!string.Equals(
				descriptor.ProjectFingerprint,
				host.ProjectFingerprint,
				StringComparison.Ordinal))
			throw new InvalidOperationException(
				"The active VEGAS project changed during the assembly session.");
		descriptor.ProjectPath = host.ProjectPath ?? "";
		descriptor.ProjectFingerprint = host.ProjectFingerprint;
		writer.WriteText(
			paths.Resolve("assembly/session.json"),
			ContractSerializer.Serialize(descriptor));
		return descriptor;
	}

	public EditPlanningRequest? ReadRequest()
	{
		string path = paths.Resolve("assembly/request.json");
		return File.Exists(path)
			? EditPlanDocumentSerializer.ReadRequest(path)
			: null;
	}

	public void SaveSketch(AssemblySketch sketch, int revision)
	{
		ArgumentNullException.ThrowIfNull(sketch);
		if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
		ProgressiveAssemblyContractValidator.Validate(sketch);
		string json = ContractSerializer.Serialize(sketch);
		writer.WriteText(
			paths.Resolve($"assembly/sketch/revisions/{revision:D4}.json"),
			json);
		writer.WriteText(paths.Resolve("assembly/sketch/current.json"), json);
	}

	public AssemblySketch? ReadCurrentSketch()
	{
		string path = paths.Resolve("assembly/sketch/current.json");
		if (!File.Exists(path)) return null;
		AssemblySketch sketch =
			ContractSerializer.Deserialize<AssemblySketch>(File.ReadAllText(path));
		ProgressiveAssemblyContractValidator.Validate(sketch);
		return sketch;
	}

	public void SaveProposal(ClipStepDecision decision, int revision)
	{
		ArgumentNullException.ThrowIfNull(decision);
		if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
		ProgressiveAssemblyContractValidator.Validate(decision);
		string json = ContractSerializer.Serialize(decision);
		string root = $"assembly/checkpoints/{decision.StepIndex:D4}";
		writer.WriteText(
			paths.Resolve($"{root}/proposals/{revision:D4}.json"),
			json);
		writer.WriteText(paths.Resolve($"{root}/proposal.json"), json);
	}

	public AssemblyProposalRejection SaveProposalRejection(
		int checkpoint,
		int proposalRevision,
		int attempt,
		int maximumAttempts,
		string diagnostic)
	{
		if (checkpoint < 1) throw new ArgumentOutOfRangeException(nameof(checkpoint));
		if (proposalRevision < 1)
			throw new ArgumentOutOfRangeException(nameof(proposalRevision));
		if (attempt < 1 || maximumAttempts < attempt)
			throw new ArgumentOutOfRangeException(nameof(attempt));
		ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic);
		AssemblyProposalRejection rejection = new()
		{
			Checkpoint = checkpoint,
			ProposalRevision = proposalRevision,
			Attempt = attempt,
			MaximumAttempts = maximumAttempts,
			Diagnostic = diagnostic.Trim(),
			ProposalRelativePath =
				$"assembly/checkpoints/{checkpoint:D4}/proposals/" +
				$"{proposalRevision:D4}.json",
			RejectedUtc = DateTimeOffset.UtcNow
		};
		writer.WriteText(
			paths.Resolve(
				$"assembly/checkpoints/{checkpoint:D4}/rejections/" +
				$"{proposalRevision:D4}.json"),
			ContractSerializer.Serialize(rejection));
		return rejection;
	}

	public ClipStepDecision? ReadCurrentProposal(int checkpoint)
	{
		if (checkpoint < 1) throw new ArgumentOutOfRangeException(nameof(checkpoint));
		string path = paths.Resolve(
			$"assembly/checkpoints/{checkpoint:D4}/proposal.json");
		if (!File.Exists(path)) return null;
		ClipStepDecision decision =
			ContractSerializer.Deserialize<ClipStepDecision>(File.ReadAllText(path));
		ProgressiveAssemblyContractValidator.Validate(decision);
		return decision;
	}

	public int GetLatestProposalRevision(int checkpoint)
	{
		if (checkpoint < 1) throw new ArgumentOutOfRangeException(nameof(checkpoint));
		string directory = paths.Resolve(
			$"assembly/checkpoints/{checkpoint:D4}/proposals");
		if (!Directory.Exists(directory)) return 1;
		return Directory.EnumerateFiles(directory, "*.json")
			.Select(Path.GetFileNameWithoutExtension)
			.Select(value => int.TryParse(value, out int revision) ? revision : 0)
			.DefaultIfEmpty(0)
			.Max() is int latest && latest > 0 ? latest : 1;
	}

	public int GetLatestSketchRevision()
	{
		string directory = paths.Resolve("assembly/sketch/revisions");
		if (!Directory.Exists(directory)) return 0;
		return Directory.EnumerateFiles(directory, "*.json")
			.Select(Path.GetFileNameWithoutExtension)
			.Select(value => int.TryParse(value, out int revision) ? revision : 0)
			.DefaultIfEmpty(0)
			.Max();
	}

	public void SaveAcceptedPlan(int checkpoint, Core.Domain.Planning.EditPlanDocument plan)
	{
		if (checkpoint < 1) throw new ArgumentOutOfRangeException(nameof(checkpoint));
		ArgumentNullException.ThrowIfNull(plan);
		writer.WriteText(
			paths.Resolve($"assembly/checkpoints/{checkpoint:D4}/accepted-plan.json"),
			Core.Domain.Planning.EditPlanDocumentSerializer.SerializePlan(plan));
	}

	public Core.Domain.Planning.EditPlanDocument? ReadLatestAcceptedPlan(int checkpoint)
	{
		if (checkpoint < 1) return null;
		string path = paths.Resolve(
			$"assembly/checkpoints/{checkpoint:D4}/accepted-plan.json");
		return File.Exists(path)
			? Core.Domain.Planning.EditPlanDocumentSerializer.ReadPlan(path)
			: null;
	}

	public (int Checkpoint, EditPlanDocument? Plan) ReadLatestAcceptedPlan()
	{
		string checkpointsRoot = paths.Resolve("assembly/checkpoints");
		if (!Directory.Exists(checkpointsRoot)) return (0, null);
		foreach (string directory in Directory.EnumerateDirectories(checkpointsRoot)
			.OrderByDescending(Path.GetFileName, StringComparer.Ordinal))
		{
			if (!int.TryParse(Path.GetFileName(directory), out int checkpoint))
				continue;
			string path = Path.Combine(directory, "accepted-plan.json");
			if (File.Exists(path))
				return (checkpoint, EditPlanDocumentSerializer.ReadPlan(path));
		}
		return (0, null);
	}

	public CandidateMaterializationBaseline SaveMaterializedBaseline(
		int checkpoint,
		string planSha256,
		CandidateTimelineSnapshot snapshot)
	{
		if (checkpoint < 1) throw new ArgumentOutOfRangeException(nameof(checkpoint));
		ArgumentException.ThrowIfNullOrWhiteSpace(planSha256);
		ArgumentNullException.ThrowIfNull(snapshot);
		snapshot.Workspace?.Validate();
		string snapshotJson = ContractSerializer.Serialize(snapshot);
		CandidateMaterializationBaseline baseline = new()
		{
			Checkpoint = checkpoint,
			PlanSha256 = NormalizeSha256(planSha256),
			SnapshotSha256 = ComputeSha256(snapshotJson),
			Snapshot = snapshot,
			CapturedUtc = DateTimeOffset.UtcNow
		};
		string json = ContractSerializer.Serialize(baseline);
		string root = $"assembly/checkpoints/{checkpoint:D4}/materialization";
		string immutablePath = paths.Resolve(
			$"{root}/baselines/{baseline.PlanSha256}-{baseline.SnapshotSha256}.json");
		if (File.Exists(immutablePath))
		{
			CandidateMaterializationBaseline existing =
				ReadAndValidateBaseline(immutablePath, checkpoint);
			if (!string.Equals(
				existing.SnapshotSha256,
				baseline.SnapshotSha256,
				StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					"The immutable materialization baseline conflicts with its plan.");
		}
		else
		{
			writer.WriteText(immutablePath, json);
		}
		writer.WriteText(paths.Resolve($"{root}/current-baseline.json"), json);
		return baseline;
	}

	public CandidateMaterializationBaseline? ReadMaterializedBaseline(
		int checkpoint)
	{
		if (checkpoint < 1) throw new ArgumentOutOfRangeException(nameof(checkpoint));
		string path = paths.Resolve(
			$"assembly/checkpoints/{checkpoint:D4}/materialization/current-baseline.json");
		return File.Exists(path)
			? ReadAndValidateBaseline(path, checkpoint)
			: null;
	}

	public CandidateMaterializationBaseline SaveRoughCutBaseline(
		string stage,
		int checkpoint,
		string planSha256,
		CandidateTimelineSnapshot snapshot)
	{
		if (stage is not ("review" or "accepted" or "effects" or "audio"))
			throw new ArgumentException(
				"Candidate baseline stage must be review, accepted, effects, or audio.",
				nameof(stage));
		if (checkpoint < 1) throw new ArgumentOutOfRangeException(nameof(checkpoint));
		ArgumentException.ThrowIfNullOrWhiteSpace(planSha256);
		ArgumentNullException.ThrowIfNull(snapshot);
		snapshot.Workspace?.Validate();
		string snapshotJson = ContractSerializer.Serialize(snapshot);
		CandidateMaterializationBaseline baseline = new()
		{
			Checkpoint = checkpoint,
			PlanSha256 = NormalizeSha256(planSha256),
			SnapshotSha256 = ComputeSha256(snapshotJson),
			Snapshot = snapshot,
			CapturedUtc = DateTimeOffset.UtcNow
		};
		string root = $"assembly/rough-cut/baselines/{stage}";
		string immutable = paths.Resolve(
			$"{root}/{baseline.PlanSha256}-{baseline.SnapshotSha256}.json");
		string json = ContractSerializer.Serialize(baseline);
		if (File.Exists(immutable))
		{
			CandidateMaterializationBaseline existing =
				ReadAndValidateBaseline(immutable, checkpoint);
			if (!string.Equals(
				existing.SnapshotSha256,
				baseline.SnapshotSha256,
				StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					"The immutable rough-cut baseline conflicts with its plan.");
		}
		else
		{
			writer.WriteText(immutable, json);
		}
		writer.WriteText(
			paths.Resolve($"assembly/rough-cut/{stage}-baseline.json"),
			json);
		return baseline;
	}

	public CandidateMaterializationBaseline? ReadRoughCutBaseline(
		string stage,
		int checkpoint)
	{
		if (stage is not ("review" or "accepted" or "effects" or "audio"))
			throw new ArgumentException(
				"Candidate baseline stage must be review, accepted, effects, or audio.",
				nameof(stage));
		string path = paths.Resolve(
			$"assembly/rough-cut/{stage}-baseline.json");
		return File.Exists(path)
			? ReadAndValidateBaseline(path, checkpoint)
			: null;
	}

	public static string RequestSha256(EditPlanningRequest request) =>
		ComputeSha256(EditPlanDocumentSerializer.SerializeRequest(request));

	private static CandidateMaterializationBaseline ReadAndValidateBaseline(
		string path,
		int checkpoint)
	{
		CandidateMaterializationBaseline value =
			ContractSerializer.Deserialize<CandidateMaterializationBaseline>(
				File.ReadAllText(path));
		if (value.SchemaVersion !=
			CandidateMaterializationBaseline.CurrentSchemaVersion ||
			value.Checkpoint != checkpoint ||
			value.Snapshot == null ||
			string.IsNullOrWhiteSpace(value.PlanSha256) ||
			string.IsNullOrWhiteSpace(value.SnapshotSha256))
			throw new InvalidDataException(
				"The materialization baseline is invalid or targets another checkpoint.");
		value.Snapshot.Workspace?.Validate();
		string actual = ComputeSha256(
			ContractSerializer.Serialize(value.Snapshot));
		if (!string.Equals(
			actual,
			value.SnapshotSha256,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The materialization baseline snapshot hash does not match.");
		value.PlanSha256 = NormalizeSha256(value.PlanSha256);
		value.SnapshotSha256 = NormalizeSha256(value.SnapshotSha256);
		return value;
	}

	private static string NormalizeSha256(string value)
	{
		string normalized = value?.Trim().ToLowerInvariant() ?? "";
		if (normalized.Length != 64 ||
			normalized.Any(character =>
				!((character >= '0' && character <= '9') ||
					(character >= 'a' && character <= 'f'))))
			throw new InvalidDataException("A valid SHA-256 value is required.");
		return normalized;
	}

	private static string ComputeSha256(string content)
	{
		using SHA256 hash = SHA256.Create();
		return Convert.ToHexString(
			hash.ComputeHash(new UTF8Encoding(false).GetBytes(content)))
			.ToLowerInvariant();
	}
}
