using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Sessions;

namespace AutoEditing.LlmEditor.Assembly;

internal sealed class AssemblyActionExecutionStart
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public AssemblyPhase Phase { get; set; }
	public AssemblyAction Action { get; set; } = new();
	public string PlanSha256 { get; set; } = "";
	public int ProposalRevision { get; set; }
	public int PreviewAttempt { get; set; }
	public string AdjustmentSha256 { get; set; } = "";
	public DateTimeOffset StartedUtc { get; set; }
}

internal sealed class AssemblyActionExecutionCompletion
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public string ActionId { get; set; } = "";
	public string Outcome { get; set; } = "";
	public string ResultReference { get; set; } = "";
	public string ResultSha256 { get; set; } = "";
	public DateTimeOffset CompletedUtc { get; set; }
}

internal sealed class AssemblyActionTimelineEvidence
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public string ActionId { get; set; } = "";
	public string PlanSha256 { get; set; } = "";
	public CandidateTimelineSnapshot Snapshot { get; set; } = new();
	public string SnapshotSha256 { get; set; } = "";
	public DateTimeOffset CapturedUtc { get; set; }
}

/// <summary>
/// Exact state-revision operation journal shared by progressive assembly and
/// later review coordinators. The start record is committed by the action-store
/// consume callback, before the consumed disposition becomes visible. A process
/// restart can therefore recover the exact action even when its source action
/// has already been marked consumed.
/// </summary>
internal sealed class AssemblyActionExecutionStore
{
	private readonly SessionPathResolver paths;
	private readonly AtomicFileWriter writer = new();
	private readonly Func<DateTimeOffset> clock;

	public AssemblyActionExecutionStore(
		string sessionRoot,
		Func<DateTimeOffset>? clock = null)
	{
		paths = new SessionPathResolver(sessionRoot);
		this.clock = clock ?? (() => DateTimeOffset.UtcNow);
	}

	public AssemblyActionExecutionStart Begin(
		AssemblyAction action,
		AssemblyPhase phase,
		string planSha256 = "",
		int proposalRevision = 0,
		int previewAttempt = 0,
		string adjustmentSha256 = "")
	{
		ArgumentNullException.ThrowIfNull(action);
		string relative = StartedRelativePath(action.ActionId);
		string path = paths.Resolve(relative);
		if (File.Exists(path))
		{
			AssemblyActionExecutionStart existing =
				ContractSerializer.Deserialize<AssemblyActionExecutionStart>(
					File.ReadAllText(path));
			Validate(existing);
			if (!SameAction(existing.Action, action))
				throw new InvalidDataException(
					"The persisted action execution targets different state.");
			if (existing.Phase != phase ||
				!string.Equals(
					existing.PlanSha256,
					NormalizeHash(planSha256),
					StringComparison.OrdinalIgnoreCase) ||
				existing.ProposalRevision != proposalRevision ||
				existing.PreviewAttempt != previewAttempt ||
				!string.Equals(
					existing.AdjustmentSha256,
					NormalizeHash(adjustmentSha256),
					StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					"The persisted action execution is bound to different " +
					"plan, pass, preview, or adjustment evidence.");
			return existing;
		}
		AssemblyActionExecutionStart value = new()
		{
			SessionId = action.SessionId,
			Phase = phase,
			Action = action,
			PlanSha256 = NormalizeHash(planSha256),
			ProposalRevision = proposalRevision,
			PreviewAttempt = previewAttempt,
			AdjustmentSha256 = NormalizeHash(adjustmentSha256),
			StartedUtc = clock()
		};
		Validate(value);
		WriteImmutable(relative, ContractSerializer.Serialize(value));
		return value;
	}

	public AssemblyActionTimelineEvidence SaveTimelineEvidence(
		AssemblyAction action,
		CandidateTimelineSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(action);
		ArgumentNullException.ThrowIfNull(snapshot);
		AssemblyActionExecutionStart started = Read(action.ActionId) ??
			throw new InvalidOperationException(
				"Timeline evidence cannot be saved without a durable action start.");
		if (!SameAction(started.Action, action))
			throw new InvalidDataException(
				"The timeline evidence targets a different action.");
		ValidateWorkspace(snapshot, started);
		string snapshotJson = ContractSerializer.Serialize(snapshot);
		AssemblyActionTimelineEvidence value = new()
		{
			SessionId = started.SessionId,
			ActionId = started.Action.ActionId,
			PlanSha256 = started.PlanSha256,
			Snapshot = snapshot,
			SnapshotSha256 = ContentSha256(snapshotJson),
			CapturedUtc = clock()
		};
		string relative = EvidenceRelativePath(action.ActionId);
		string path = paths.Resolve(relative);
		if (File.Exists(path))
		{
			AssemblyActionTimelineEvidence existing =
				ReadTimelineEvidence(action.ActionId) ??
				throw new InvalidDataException(
					"The persisted timeline evidence could not be read.");
			if (!string.Equals(
					existing.SnapshotSha256,
					value.SnapshotSha256,
					StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					"Timeline evidence was already captured with different live content.");
			return existing;
		}
		WriteImmutable(relative, ContractSerializer.Serialize(value));
		return value;
	}

	public AssemblyActionTimelineEvidence? ReadTimelineEvidence(string actionId)
	{
		string path = paths.Resolve(EvidenceRelativePath(actionId));
		if (!File.Exists(path)) return null;
		AssemblyActionTimelineEvidence value =
			ContractSerializer.Deserialize<AssemblyActionTimelineEvidence>(
				File.ReadAllText(path));
		Validate(value);
		AssemblyActionExecutionStart started = Read(actionId) ??
			throw new InvalidDataException(
				"Timeline evidence has no matching action start.");
		if (!string.Equals(value.SessionId, started.SessionId, StringComparison.Ordinal) ||
			!string.Equals(value.ActionId, started.Action.ActionId, StringComparison.Ordinal) ||
			!string.Equals(
				value.PlanSha256,
				started.PlanSha256,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"Timeline evidence is not bound to its action start.");
		ValidateWorkspace(value.Snapshot, started);
		string actualHash = ContentSha256(
			ContractSerializer.Serialize(value.Snapshot));
		if (!string.Equals(
				actualHash,
				value.SnapshotSha256,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The durable action timeline evidence hash does not match its snapshot.");
		return value;
	}

	public AssemblyActionExecutionStart? Read(string actionId)
	{
		string path = paths.Resolve(StartedRelativePath(actionId));
		if (!File.Exists(path)) return null;
		AssemblyActionExecutionStart value =
			ContractSerializer.Deserialize<AssemblyActionExecutionStart>(
				File.ReadAllText(path));
		Validate(value);
		return value;
	}

	public AssemblyActionExecutionStart? ReadPending(
		string sessionId,
		int checkpoint,
		long expectedStateRevision,
		AssemblyPhase expectedPhase)
	{
		AssemblyActionExecutionStart? value =
			ReadPendingForRecovery(sessionId);
		if (value == null) return null;
		if (value.Action.Checkpoint != checkpoint ||
			value.Action.ExpectedStateRevision != expectedStateRevision ||
			value.Phase != expectedPhase)
			throw new InvalidDataException(
				"A pending action execution targets a different review state. " +
				"Recover or reconcile that exact persisted operation first.");
		return value;
	}

	public AssemblyActionExecutionStart? ReadPendingForRecovery(string sessionId)
	{
		string root = paths.Resolve("assembly/action-executions");
		if (!Directory.Exists(root)) return null;
		List<AssemblyActionExecutionStart> pending = new();
		foreach (string path in Directory.EnumerateFiles(root, "*.started.json")
			.OrderBy(value => value, StringComparer.Ordinal))
		{
			AssemblyActionExecutionStart value =
				ContractSerializer.Deserialize<AssemblyActionExecutionStart>(
					File.ReadAllText(path));
			Validate(value);
			if (!string.Equals(value.SessionId, sessionId, StringComparison.Ordinal) ||
				ReadCompletion(value.Action.ActionId) != null)
				continue;
			pending.Add(value);
		}
		if (pending.Count > 1)
			throw new InvalidDataException(
				"Multiple incomplete assembly action executions exist for the " +
				"same session. Recovery cannot safely choose a winner.");
		return pending.SingleOrDefault();
	}

	public AssemblyActionExecutionStart? ReadUniqueExecution(
		string sessionId,
		AssemblyActionKind kind)
	{
		string root = paths.Resolve("assembly/action-executions");
		if (!Directory.Exists(root)) return null;
		List<AssemblyActionExecutionStart> matches = new();
		foreach (string path in Directory.EnumerateFiles(root, "*.started.json")
			.OrderBy(value => value, StringComparer.Ordinal))
		{
			AssemblyActionExecutionStart value =
				ContractSerializer.Deserialize<AssemblyActionExecutionStart>(
					File.ReadAllText(path));
			Validate(value);
			if (string.Equals(value.SessionId, sessionId, StringComparison.Ordinal) &&
				value.Action.Kind == kind)
				matches.Add(value);
		}
		if (matches.Count > 1)
			throw new InvalidDataException(
				"Multiple durable " + kind +
				" executions exist for the same session. Recovery cannot " +
				"safely select a final transaction.");
		return matches.SingleOrDefault();
	}

	public void Complete(
		AssemblyAction action,
		string outcome,
		string resultReference = "",
		string resultSha256 = "")
	{
		ArgumentNullException.ThrowIfNull(action);
		ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
		AssemblyActionExecutionStart started = Read(action.ActionId) ??
			throw new InvalidOperationException(
				"An assembly action cannot complete without a durable start record.");
		if (!SameAction(started.Action, action))
			throw new InvalidDataException(
				"The action completion targets different state.");
		AssemblyActionExecutionCompletion? existing =
			ReadCompletion(action.ActionId);
		if (existing != null)
		{
			if (!string.Equals(existing.SessionId, action.SessionId,
					StringComparison.Ordinal) ||
				!string.Equals(existing.Outcome, outcome,
					StringComparison.Ordinal) ||
				!string.Equals(existing.ResultReference, resultReference ?? "",
					StringComparison.Ordinal) ||
				!string.Equals(existing.ResultSha256, NormalizeHash(resultSha256),
					StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"The action already completed with a different durable result.");
			return;
		}
		AssemblyActionExecutionCompletion value = new()
		{
			SessionId = action.SessionId,
			ActionId = action.ActionId,
			Outcome = outcome,
			ResultReference = resultReference ?? "",
			ResultSha256 = NormalizeHash(resultSha256),
			CompletedUtc = clock()
		};
		WriteImmutable(
			CompletedRelativePath(action.ActionId),
			ContractSerializer.Serialize(value));
	}

	public bool IsComplete(string actionId) => ReadCompletion(actionId) != null;

	public AssemblyActionExecutionCompletion? ReadCompletion(string actionId)
	{
		string path = paths.Resolve(CompletedRelativePath(actionId));
		if (!File.Exists(path)) return null;
		try
		{
			AssemblyActionExecutionCompletion value =
				ContractSerializer.Deserialize<AssemblyActionExecutionCompletion>(
					File.ReadAllText(path));
			Validate(value);
			AssemblyActionExecutionStart started = Read(actionId) ??
				throw new InvalidDataException(
					"An action completion has no matching start record.");
			if (!string.Equals(
				value.SessionId,
				started.SessionId,
				StringComparison.Ordinal) ||
				!string.Equals(
					value.ActionId,
					started.Action.ActionId,
					StringComparison.Ordinal))
				throw new InvalidDataException(
					"An action completion targets a different start record.");
			return value;
		}
		catch (Exception exception) when (
			exception is IOException or InvalidDataException or
				Newtonsoft.Json.JsonException)
		{
			QuarantineCorruptCompletion(path);
			throw new InvalidDataException(
				"The action completion is corrupt and was quarantined.",
				exception);
		}
	}

	private void WriteImmutable(string relativePath, string content)
	{
		string path = paths.Resolve(relativePath);
		if (File.Exists(path))
		{
			if (string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
				return;
			throw new InvalidOperationException(
				"A durable action execution artifact already exists with different content.");
		}
		writer.WriteText(path, content);
	}

	private static string StartedRelativePath(string actionId) =>
		"assembly/action-executions/" + SafeId(actionId) + ".started.json";

	private static string CompletedRelativePath(string actionId) =>
		"assembly/action-executions/" + SafeId(actionId) + ".completed.json";

	private static string EvidenceRelativePath(string actionId) =>
		"assembly/action-executions/" + SafeId(actionId) + ".timeline.json";

	private static string SafeId(string value)
	{
		if (string.IsNullOrWhiteSpace(value) ||
			value.Any(character =>
				!char.IsLetterOrDigit(character) &&
				character is not ('-' or '_' or '.')))
			throw new InvalidOperationException("The assembly action ID is unsafe.");
		return value;
	}

	private static string NormalizeHash(string value)
	{
		if (string.IsNullOrEmpty(value)) return "";
		if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
			throw new InvalidDataException("An action execution hash is invalid.");
		return value.ToLowerInvariant();
	}

	private static void Validate(AssemblyActionExecutionStart value)
	{
		if (value == null ||
			value.SchemaVersion != AssemblyActionExecutionStart.CurrentSchemaVersion ||
			value.StartedUtc == default ||
			value.Action == null ||
			string.IsNullOrWhiteSpace(value.SessionId) ||
			!string.Equals(
				value.SessionId,
				value.Action.SessionId,
				StringComparison.Ordinal) ||
			value.Action.ExpectedStateRevision <= 0 ||
			value.Action.Checkpoint <= 0 ||
			value.ProposalRevision < 0 ||
			value.PreviewAttempt < 0)
			throw new InvalidDataException(
				"The durable assembly action execution is invalid.");
		SafeId(value.Action.ActionId);
		NormalizeHash(value.PlanSha256);
		NormalizeHash(value.AdjustmentSha256);
	}

	private static void Validate(AssemblyActionExecutionCompletion value)
	{
		if (value == null ||
			value.SchemaVersion !=
				AssemblyActionExecutionCompletion.CurrentSchemaVersion ||
			string.IsNullOrWhiteSpace(value.SessionId) ||
			string.IsNullOrWhiteSpace(value.ActionId) ||
			string.IsNullOrWhiteSpace(value.Outcome) ||
			value.CompletedUtc == default)
			throw new InvalidDataException(
				"The durable action execution completion is invalid.");
		SafeId(value.ActionId);
		NormalizeHash(value.ResultSha256);
		if (!string.IsNullOrEmpty(value.ResultReference) &&
			(Path.IsPathRooted(value.ResultReference) ||
				value.ResultReference.Split('/', '\\').Any(segment => segment == "..")))
			throw new InvalidDataException(
				"The action result reference is not a safe session-relative path.");
	}

	private static void Validate(AssemblyActionTimelineEvidence value)
	{
		if (value == null ||
			value.SchemaVersion != AssemblyActionTimelineEvidence.CurrentSchemaVersion ||
			string.IsNullOrWhiteSpace(value.SessionId) ||
			string.IsNullOrWhiteSpace(value.ActionId) ||
			value.Snapshot == null ||
			value.CapturedUtc == default)
			throw new InvalidDataException(
				"The durable action timeline evidence is invalid.");
		SafeId(value.ActionId);
		NormalizeHash(value.PlanSha256);
		NormalizeHash(value.SnapshotSha256);
	}

	private static void ValidateWorkspace(
		CandidateTimelineSnapshot snapshot,
		AssemblyActionExecutionStart started)
	{
		if (snapshot.Workspace == null)
			throw new InvalidDataException(
				"Timeline evidence has no candidate workspace identity.");
		snapshot.Workspace.Validate();
		if (!string.Equals(
				snapshot.Workspace.SessionId,
				started.SessionId,
				StringComparison.Ordinal) ||
			snapshot.Workspace.Iteration != started.Action.Checkpoint)
			throw new InvalidDataException(
				"Timeline evidence belongs to another exact checkpoint workspace.");
	}

	private static string ContentSha256(string content) =>
		Convert.ToHexString(
			System.Security.Cryptography.SHA256.HashData(
				new System.Text.UTF8Encoding(false).GetBytes(content)))
			.ToLowerInvariant();

	private void QuarantineCorruptCompletion(string path)
	{
		string destination = paths.Resolve(
			"assembly/action-executions/quarantine/" +
			Path.GetFileName(path) + "." +
			DateTimeOffset.UtcNow.UtcTicks.ToString("D19") + "." +
			Guid.NewGuid().ToString("N") + ".corrupt");
		Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
		File.Move(path, destination);
	}

	private static bool SameAction(AssemblyAction left, AssemblyAction right) =>
		string.Equals(left.ActionId, right.ActionId, StringComparison.Ordinal) &&
		string.Equals(left.SessionId, right.SessionId, StringComparison.Ordinal) &&
		left.Checkpoint == right.Checkpoint &&
		left.ExpectedStateRevision == right.ExpectedStateRevision &&
		left.Kind == right.Kind &&
		left.SteeringScope == right.SteeringScope &&
		string.Equals(left.TargetId, right.TargetId, StringComparison.Ordinal) &&
		string.Equals(left.Instruction, right.Instruction, StringComparison.Ordinal);
}
