using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Sessions;

namespace AutoEditing.LlmEditor.RoughCut;

internal sealed class RoughCutWorkflowContext
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public string PlanSha256 { get; set; } = "";
	public string ReportId { get; set; } = "";
	public AssemblyPhase Phase { get; set; }
	public AssemblyPhase PhaseBeforePause { get; set; }
	public string ActiveCorrectionId { get; set; } = "";
	public DateTimeOffset UpdatedUtc { get; set; }
}

internal sealed class RoughCutOperationStart
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public string PlanSha256 { get; set; } = "";
	public string ReportId { get; set; } = "";
	public AssemblyPhase StartedInPhase { get; set; }
	public AssemblyAction Action { get; set; } = new();
	public DateTimeOffset StartedUtc { get; set; }
}

internal sealed class RoughCutOperationCompletion
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string ActionId { get; set; } = "";
	public string Outcome { get; set; } = "";
	public DateTimeOffset CompletedUtc { get; set; }
}

/// <summary>
/// Stores the rough-cut subphase independently from the workbench projection and
/// journals each consumed action before its effect is executed. Started operations
/// are replayed after a crash; completed operations are immutable terminal facts.
/// </summary>
internal sealed class RoughCutWorkflowRecoveryStore
{
	private readonly SessionPathResolver paths;
	private readonly AtomicFileWriter writer = new();
	private readonly Func<DateTimeOffset> clock;

	public RoughCutWorkflowRecoveryStore(
		string sessionRoot,
		Func<DateTimeOffset>? clock = null)
	{
		paths = new SessionPathResolver(sessionRoot);
		this.clock = clock ?? (() => DateTimeOffset.UtcNow);
	}

	public void SaveContext(RoughCutWorkflowContext context)
	{
		Validate(context);
		context.UpdatedUtc = clock();
		writer.WriteText(
			paths.Resolve("assembly/rough-cut/workflow.json"),
			ContractSerializer.Serialize(context));
	}

	public RoughCutWorkflowContext? ReadContext()
	{
		string path = paths.Resolve("assembly/rough-cut/workflow.json");
		if (!File.Exists(path)) return null;
		RoughCutWorkflowContext context =
			ContractSerializer.Deserialize<RoughCutWorkflowContext>(
				File.ReadAllText(path));
		Validate(context);
		return context;
	}

	public RoughCutOperationStart Begin(
		AssemblyAction action,
		AssemblyPhase phase,
		string planSha256,
		string reportId)
	{
		ArgumentNullException.ThrowIfNull(action);
		ValidateHash(planSha256);
		ArgumentException.ThrowIfNullOrWhiteSpace(reportId);
		string startedPath = paths.Resolve(StartedRelativePath(action.ActionId));
		if (File.Exists(startedPath))
		{
			RoughCutOperationStart existing =
				ContractSerializer.Deserialize<RoughCutOperationStart>(
					File.ReadAllText(startedPath));
			Validate(existing);
			if (!string.Equals(
					existing.Action.ActionId,
					action.ActionId,
					StringComparison.Ordinal) ||
				!string.Equals(
					existing.PlanSha256,
					planSha256,
					StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(
					existing.ReportId,
					reportId,
					StringComparison.Ordinal))
				throw new InvalidDataException(
					"The recovered rough-cut operation targets different workflow state.");
			return existing;
		}
		RoughCutOperationStart operation = new()
		{
			SessionId = action.SessionId,
			PlanSha256 = planSha256.ToLowerInvariant(),
			ReportId = reportId,
			StartedInPhase = phase,
			Action = action,
			StartedUtc = clock()
		};
		Validate(operation);
		WriteImmutable(
			StartedRelativePath(action.ActionId),
			ContractSerializer.Serialize(operation));
		return operation;
	}

	public RoughCutOperationStart? ReadPending(
		string sessionId,
		string planSha256,
		string reportId)
	{
		ValidateHash(planSha256);
		string root = paths.Resolve("assembly/rough-cut/operations");
		if (!Directory.Exists(root)) return null;
		foreach (string path in Directory.EnumerateFiles(root, "*.started.json")
			.OrderBy(item => item, StringComparer.Ordinal))
		{
			RoughCutOperationStart operation =
				ContractSerializer.Deserialize<RoughCutOperationStart>(
					File.ReadAllText(path));
			Validate(operation);
			if (File.Exists(paths.Resolve(
				CompletedRelativePath(operation.Action.ActionId))))
				continue;
			if (string.Equals(
					operation.SessionId, sessionId, StringComparison.Ordinal) &&
				string.Equals(
					operation.PlanSha256,
					planSha256,
					StringComparison.OrdinalIgnoreCase) &&
				string.Equals(
					operation.ReportId, reportId, StringComparison.Ordinal))
				return operation;
		}
		return null;
	}

	public void Complete(AssemblyAction action, string outcome)
	{
		ArgumentNullException.ThrowIfNull(action);
		ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
		string startPath = paths.Resolve(StartedRelativePath(action.ActionId));
		if (!File.Exists(startPath))
			throw new InvalidOperationException(
				"A rough-cut action cannot complete before its operation is durable.");
		RoughCutOperationCompletion completion = new()
		{
			ActionId = action.ActionId,
			Outcome = outcome,
			CompletedUtc = clock()
		};
		WriteImmutable(
			CompletedRelativePath(action.ActionId),
			ContractSerializer.Serialize(completion));
	}

	public static bool IsRoughCutResumeState(
		string sessionRoot,
		AssemblySessionState state)
	{
		ArgumentNullException.ThrowIfNull(state);
		if (state.Phase is
			AssemblyPhase.RoughCutRendering or
			AssemblyPhase.RoughCutAuditing or
			AssemblyPhase.RoughCutReview or
			AssemblyPhase.RoughCutCorrection or
			AssemblyPhase.RoughCutAccepted)
			return true;
		if (state.Phase != AssemblyPhase.Paused) return false;
		RoughCutWorkflowContext? context =
			new RoughCutWorkflowRecoveryStore(sessionRoot).ReadContext();
		return context?.Phase == AssemblyPhase.Paused &&
			context.PhaseBeforePause is
				AssemblyPhase.RoughCutReview or
				AssemblyPhase.RoughCutCorrection;
	}

	private void WriteImmutable(string relativePath, string content)
	{
		string path = paths.Resolve(relativePath);
		if (File.Exists(path))
		{
			if (string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
				return;
			throw new InvalidOperationException(
				"A durable rough-cut operation artifact already exists with different content.");
		}
		writer.WriteText(path, content);
	}

	private static string StartedRelativePath(string actionId) =>
		"assembly/rough-cut/operations/" + SafeId(actionId) + ".started.json";

	private static string CompletedRelativePath(string actionId) =>
		"assembly/rough-cut/operations/" + SafeId(actionId) + ".completed.json";

	private static string SafeId(string value)
	{
		if (string.IsNullOrWhiteSpace(value) ||
			value.Any(character =>
				!char.IsLetterOrDigit(character) &&
				character is not ('-' or '_' or '.')))
			throw new InvalidOperationException("The rough-cut action ID is unsafe.");
		return value;
	}

	private static void Validate(RoughCutWorkflowContext context)
	{
		if (context == null ||
			context.SchemaVersion != RoughCutWorkflowContext.CurrentSchemaVersion ||
			string.IsNullOrWhiteSpace(context.SessionId))
			throw new InvalidDataException(
				"The durable rough-cut workflow context is invalid.");
		ValidateHash(context.PlanSha256);
		if (context.Phase is not (
				AssemblyPhase.RoughCutRendering or
				AssemblyPhase.RoughCutAuditing or
				AssemblyPhase.RoughCutReview or
				AssemblyPhase.RoughCutCorrection or
				AssemblyPhase.RoughCutAccepted or
				AssemblyPhase.Paused))
			throw new InvalidDataException(
				"The durable rough-cut workflow phase is invalid.");
		if (context.Phase == AssemblyPhase.Paused &&
			context.PhaseBeforePause is not (
				AssemblyPhase.RoughCutReview or
				AssemblyPhase.RoughCutCorrection))
			throw new InvalidDataException(
				"A paused rough-cut context must retain its previous review phase.");
		if (context.Phase == AssemblyPhase.RoughCutCorrection &&
			string.IsNullOrWhiteSpace(context.ActiveCorrectionId))
			throw new InvalidDataException(
				"A rough-cut correction phase requires an active correction ID.");
	}

	private static void Validate(RoughCutOperationStart operation)
	{
		if (operation == null ||
			operation.SchemaVersion != RoughCutOperationStart.CurrentSchemaVersion ||
			string.IsNullOrWhiteSpace(operation.SessionId) ||
			string.IsNullOrWhiteSpace(operation.ReportId) ||
			operation.StartedUtc == default ||
			operation.Action == null ||
			!string.Equals(
				operation.Action.SessionId,
				operation.SessionId,
				StringComparison.Ordinal))
			throw new InvalidDataException(
				"The durable rough-cut operation is invalid.");
		ValidateHash(operation.PlanSha256);
		SafeId(operation.Action.ActionId);
	}

	private static void ValidateHash(string value)
	{
		if (value == null || value.Length != 64 ||
			value.Any(character => !Uri.IsHexDigit(character)))
			throw new InvalidDataException(
				"The durable rough-cut plan hash is invalid.");
	}
}
