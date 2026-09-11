using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Sessions;

namespace AutoEditing.LlmEditor.Assembly;

internal sealed class AssemblySteeringDirectiveStore
{
	private readonly SessionPathResolver paths;
	private readonly AtomicFileWriter writer = new();

	public AssemblySteeringDirectiveStore(string sessionRoot)
	{
		paths = new SessionPathResolver(sessionRoot);
	}

	public AssemblySteeringDirective? SaveAcceptedDirection(
		AssemblyAction action,
		AssemblySketch sketch,
		int acceptedCheckpoint)
	{
		ArgumentNullException.ThrowIfNull(action);
		ArgumentNullException.ThrowIfNull(sketch);
		string instruction = (action.Instruction ?? "").Trim();
		if (instruction.Length == 0) return null;
		if (action.Kind is not
			(AssemblyActionKind.AcceptTimelineAndContinue or
			 AssemblyActionKind.FinishSyncPass))
			throw new InvalidOperationException(
				"Only an accepted synchronization checkpoint may create " +
				"future steering.");
		if (action.SteeringScope == AssemblySteeringScope.CurrentClip)
			throw new InvalidOperationException(
				"Current-clip direction must be submitted with Revise. Choose " +
				"Next clip, Remaining section, or Global remainder before accepting.");
		if (acceptedCheckpoint != action.Checkpoint)
			throw new InvalidDataException(
				"The steering source checkpoint does not match the accepted action.");
		if (!sketch.ClipOrder.Any(item => item.Order > acceptedCheckpoint))
			throw new InvalidOperationException(
				"There are no remaining clips to receive future steering. Clear " +
				"the instruction before finishing the synchronization pass.");
		AssemblyClipIntent sourceIntent = sketch.ClipOrder.SingleOrDefault(
			item => item.Order == acceptedCheckpoint)
			?? throw new InvalidDataException(
				"The accepted checkpoint is absent from the semantic clip order.");
		AssemblySteeringDirective value = new()
		{
			DirectiveId = action.ActionId,
			SessionId = action.SessionId,
			SourceCheckpoint = acceptedCheckpoint,
			ApplicableFromCheckpoint = acceptedCheckpoint + 1,
			Scope = action.SteeringScope,
			SectionId = action.SteeringScope ==
				AssemblySteeringScope.RemainingSection
					? sourceIntent.SectionId
					: "",
			Instruction = instruction,
			CreatedUtc = action.CreatedUtc
		};
		if (value.Scope == AssemblySteeringScope.RemainingSection &&
			!sketch.ClipOrder.Any(item =>
				item.Order > acceptedCheckpoint &&
				string.Equals(
					item.SectionId,
					value.SectionId,
					StringComparison.Ordinal)))
			throw new InvalidOperationException(
				"The current song section has no remaining clips. Choose Next " +
				"clip or Global remainder, or clear the instruction.");
		Validate(value);
		string path = paths.Resolve(
			"assembly/steering/" + value.DirectiveId + ".json");
		string json = ContractSerializer.Serialize(value);
		if (File.Exists(path))
		{
			AssemblySteeringDirective existing = Read(path);
			if (!string.Equals(
				ContractSerializer.Serialize(existing),
				json,
				StringComparison.Ordinal))
				throw new InvalidDataException(
					"A durable steering directive already exists with different content.");
			return existing;
		}
		writer.WriteText(path, json);
		return value;
	}

	public IReadOnlyList<AssemblySteeringDirective> ReadApplicable(
		string sessionId,
		AssemblySketch sketch,
		int checkpoint)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
		ArgumentNullException.ThrowIfNull(sketch);
		if (checkpoint < 1) throw new ArgumentOutOfRangeException(nameof(checkpoint));
		AssemblyClipIntent intent = sketch.ClipOrder.SingleOrDefault(
			item => item.Order == checkpoint)
			?? throw new InvalidDataException(
				"The planning checkpoint is absent from the semantic clip order.");
		string root = paths.Resolve("assembly/steering");
		if (!Directory.Exists(root))
			return Array.Empty<AssemblySteeringDirective>();
		return Directory.EnumerateFiles(root, "*.json")
			.Select(Read)
			.Where(value => string.Equals(
				value.SessionId,
				sessionId,
				StringComparison.Ordinal))
			.Where(value => Applies(value, intent, checkpoint))
			.OrderBy(value => value.SourceCheckpoint)
			.ThenBy(value => value.CreatedUtc)
			.ThenBy(value => value.DirectiveId, StringComparer.Ordinal)
			.ToList();
	}

	public string DescribeApplicable(
		string sessionId,
		AssemblySketch sketch,
		int checkpoint)
	{
		IReadOnlyList<AssemblySteeringDirective> directives =
			ReadApplicable(sessionId, sketch, checkpoint);
		return string.Join(
			Environment.NewLine,
			directives.Select(value =>
				"[" + ScopeLabel(value.Scope) + "] " + value.Instruction));
	}

	private static bool Applies(
		AssemblySteeringDirective value,
		AssemblyClipIntent intent,
		int checkpoint)
	{
		if (checkpoint < value.ApplicableFromCheckpoint) return false;
		return value.Scope switch
		{
			AssemblySteeringScope.NextClip =>
				checkpoint == value.ApplicableFromCheckpoint,
			AssemblySteeringScope.RemainingSection =>
				string.Equals(
					intent.SectionId,
					value.SectionId,
					StringComparison.Ordinal),
			AssemblySteeringScope.GlobalRemainder => true,
			_ => false
		};
	}

	private static string ScopeLabel(AssemblySteeringScope scope) =>
		scope switch
		{
			AssemblySteeringScope.NextClip => "next clip",
			AssemblySteeringScope.RemainingSection => "remaining section",
			AssemblySteeringScope.GlobalRemainder => "global remainder",
			_ => "current clip"
		};

	private static AssemblySteeringDirective Read(string path)
	{
		AssemblySteeringDirective value =
			ContractSerializer.Deserialize<AssemblySteeringDirective>(
				File.ReadAllText(path));
		Validate(value);
		return value;
	}

	private static void Validate(AssemblySteeringDirective value)
	{
		if (value.SchemaVersion !=
			AssemblySteeringDirective.CurrentSchemaVersion ||
			string.IsNullOrWhiteSpace(value.DirectiveId) ||
			string.IsNullOrWhiteSpace(value.SessionId) ||
			value.SourceCheckpoint < 1 ||
			value.ApplicableFromCheckpoint != value.SourceCheckpoint + 1 ||
			!Enum.IsDefined(value.Scope) ||
			value.Scope == AssemblySteeringScope.CurrentClip ||
			string.IsNullOrWhiteSpace(value.Instruction) ||
			value.CreatedUtc == default)
			throw new InvalidDataException(
				"The durable assembly steering directive is invalid.");
		if (value.Scope == AssemblySteeringScope.RemainingSection &&
			string.IsNullOrWhiteSpace(value.SectionId))
			throw new InvalidDataException(
				"A remaining-section directive requires a semantic section ID.");
		if (value.Scope != AssemblySteeringScope.RemainingSection &&
			!string.IsNullOrWhiteSpace(value.SectionId))
			throw new InvalidDataException(
				"Only remaining-section direction may carry a section ID.");
	}
}
