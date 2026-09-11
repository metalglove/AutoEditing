using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Sessions;

namespace AutoEditing.LlmEditor.Assembly;

internal sealed class AssemblyActionStore
{
	private const int MaximumInstructionLength = 16_384;
	private readonly SessionPathResolver paths;
	private readonly AtomicFileWriter writer = new();

	public AssemblyActionStore(string sessionRoot)
	{
		paths = new SessionPathResolver(sessionRoot);
	}

	public void PublishState(AssemblySessionState state)
	{
		ArgumentNullException.ThrowIfNull(state);
		state.UpdatedUtc = DateTimeOffset.UtcNow;
		writer.WriteText(paths.Resolve("assembly/state.json"), ContractSerializer.Serialize(state));
	}

	public AssemblySessionState? ReadState()
	{
		string path = paths.Resolve("assembly/state.json");
		return File.Exists(path)
			? ContractSerializer.Deserialize<AssemblySessionState>(File.ReadAllText(path))
			: null;
	}

	public AssemblyAction? TryConsume(
		int checkpoint,
		string? expectedSessionId = null,
		long expectedStateRevision = 0,
		Action<AssemblyAction>? beforeDisposition = null)
	{
		string directory = paths.Resolve("assembly/actions");
		if (!Directory.Exists(directory)) return null;
		List<(string Path, AssemblyAction Action)> pending = new();
		foreach (string actionPath in Directory.EnumerateFiles(directory, "*.json"))
		{
			if (HasDisposition(actionPath)) continue;
			try
			{
				AssemblyAction action = ContractSerializer.Deserialize<AssemblyAction>(
					File.ReadAllText(actionPath));
				pending.Add((actionPath, action));
			}
			catch (Exception exception)
			{
				TryRecordDisposition(
					actionPath,
					"quarantined",
					"malformed",
					exception.GetType().Name);
			}
		}
		foreach ((string actionPath, AssemblyAction action) in pending
			.OrderBy(item => item.Action.CreatedUtc)
			.ThenBy(item => item.Action.ActionId, StringComparer.Ordinal))
		{
			string? rejection = RejectionReason(
				action, checkpoint, expectedSessionId, expectedStateRevision);
			if (rejection != null)
			{
				TryRecordDisposition(actionPath, "quarantined", rejection, action.ActionId);
				continue;
			}

			ClaimResult claim = TryClaimState(action);
			if (claim == ClaimResult.AlreadyClaimedByThisAction)
			{
				TryRecordDisposition(
					actionPath, "consumed", "already-consumed", action.ActionId);
				continue;
			}
			if (claim == ClaimResult.AlreadyClaimedByAnotherAction)
			{
				TryRecordDisposition(
					actionPath, "quarantined", "conflicting-action", action.ActionId);
				continue;
			}

			beforeDisposition?.Invoke(action);
			if (!TryRecordDisposition(actionPath, "consumed", "accepted", action.ActionId))
			{
				// Another consumer completed the disposition between enumeration and claim.
				continue;
			}
			return action;
		}
		return null;
	}

	/// <summary>
	/// Recovers the action whose durable claim was written before the previous
	/// process failed. Callers must hold the session runtime lease so this cannot
	/// race the consumer that originally created the claim.
	/// </summary>
	public AssemblyAction? TryRecoverClaimed(
		int checkpoint,
		string expectedSessionId,
		long expectedStateRevision,
		Action<AssemblyAction>? beforeDisposition = null)
	{
		string claimPath = paths.Resolve(
			"assembly/action-claims/" +
			checkpoint.ToString("D6") + "-" +
			expectedStateRevision.ToString("D19") + ".json");
		if (!File.Exists(claimPath)) return null;
		AssemblyAction action = ContractSerializer.Deserialize<AssemblyAction>(
			File.ReadAllText(claimPath));
		string? rejection = RejectionReason(
			action,
			checkpoint,
			expectedSessionId,
			expectedStateRevision);
		if (rejection != null)
			throw new InvalidDataException(
				"The claimed assembly action is not valid for the recoverable state: " +
				rejection + ".");
		string sourcePath = paths.Resolve(
			"assembly/actions/" + action.ActionId + ".json");
		if (!File.Exists(sourcePath))
		{
			sourcePath = Directory.Exists(paths.Resolve("assembly/actions"))
				? Directory.EnumerateFiles(paths.Resolve("assembly/actions"), "*.json")
					.FirstOrDefault(path =>
					{
						try
						{
							return string.Equals(
								ContractSerializer.Deserialize<AssemblyAction>(
									File.ReadAllText(path)).ActionId,
								action.ActionId,
								StringComparison.Ordinal);
						}
						catch
						{
							return false;
						}
					}) ?? ""
				: "";
		}
		if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
			throw new InvalidDataException(
				"The claimed assembly action source artifact is missing.");
		if (HasDisposition(sourcePath)) return null;
		beforeDisposition?.Invoke(action);
		TryRecordDisposition(
			sourcePath, "consumed", "recovered-claim", action.ActionId);
		return action;
	}

	public void RecordRecoveredDisposition(AssemblyAction action)
	{
		ArgumentNullException.ThrowIfNull(action);
		string directory = paths.Resolve("assembly/actions");
		string sourcePath = Path.Combine(directory, action.ActionId + ".json");
		if (!File.Exists(sourcePath) && Directory.Exists(directory))
			sourcePath = Directory.EnumerateFiles(directory, "*.json")
				.FirstOrDefault(path =>
				{
					try
					{
						return string.Equals(
							ContractSerializer.Deserialize<AssemblyAction>(
								File.ReadAllText(path)).ActionId,
							action.ActionId,
							StringComparison.Ordinal);
					}
					catch
					{
						return false;
					}
				}) ?? "";
		if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
			throw new InvalidDataException(
				"The recovered action source artifact is missing.");
		if (!HasDisposition(sourcePath))
			TryRecordDisposition(
				sourcePath,
				"consumed",
				"recovered-execution",
				action.ActionId);
	}

	private static string? RejectionReason(
		AssemblyAction action,
		int checkpoint,
		string? expectedSessionId,
		long expectedStateRevision)
	{
		if (action.SchemaVersion != AssemblyAction.CurrentSchemaVersion)
			return "unsupported-schema";
		if (string.IsNullOrWhiteSpace(action.ActionId) ||
			action.ActionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
			return "invalid-action-id";
		if (string.IsNullOrWhiteSpace(action.SessionId))
			return "missing-session";
		if (action.Checkpoint <= 0)
			return "invalid-checkpoint";
		if (action.Checkpoint != checkpoint)
			return "stale-checkpoint";
		if (!string.IsNullOrWhiteSpace(expectedSessionId) &&
			!string.Equals(action.SessionId, expectedSessionId, StringComparison.Ordinal))
			return "wrong-session";
		if (action.ExpectedStateRevision <= 0)
			return "invalid-state-revision";
		if (expectedStateRevision > 0 &&
			action.ExpectedStateRevision != expectedStateRevision)
			return "stale-state";
		if (!Enum.IsDefined(action.Kind))
			return "unknown-action-kind";
		if (!Enum.IsDefined(action.SteeringScope))
			return "unknown-steering-scope";
		if (action.CreatedUtc == default)
			return "missing-created-time";
		if ((action.Instruction?.Length ?? 0) > MaximumInstructionLength)
			return "instruction-too-long";
		if ((action.TargetId?.Length ?? 0) > 256)
			return "target-id-too-long";
		return null;
	}

	private ClaimResult TryClaimState(AssemblyAction action)
	{
		string claimPath = paths.Resolve(
			"assembly/action-claims/" +
			action.Checkpoint.ToString("D6") + "-" +
			action.ExpectedStateRevision.ToString("D19") + ".json");
		Directory.CreateDirectory(Path.GetDirectoryName(claimPath)!);
		string serialized = ContractSerializer.Serialize(action);
		string temporaryPath = claimPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			using (FileStream stream = new(
					temporaryPath,
					FileMode.CreateNew,
					FileAccess.Write,
					FileShare.None,
					4096,
					FileOptions.WriteThrough))
			using (StreamWriter text = new(stream, new System.Text.UTF8Encoding(false)))
			{
				text.Write(serialized);
				text.Flush();
				stream.Flush(true);
			}
			File.Move(temporaryPath, claimPath);
			return ClaimResult.Claimed;
		}
		catch (IOException) when (File.Exists(claimPath))
		{
			try
			{
				AssemblyAction claimed = ContractSerializer.Deserialize<AssemblyAction>(
					File.ReadAllText(claimPath));
				return string.Equals(
						claimed.ActionId,
						action.ActionId,
						StringComparison.Ordinal)
					? ClaimResult.AlreadyClaimedByThisAction
					: ClaimResult.AlreadyClaimedByAnotherAction;
			}
			catch
			{
				throw new InvalidDataException(
					"The assembly action claim is unreadable: " + claimPath);
			}
		}
		finally
		{
			if (File.Exists(temporaryPath))
				File.Delete(temporaryPath);
		}
	}

	private bool HasDisposition(string actionPath)
	{
		string actionFile = Path.GetFileName(actionPath);
		return Directory.Exists(paths.Resolve("assembly/action-dispositions")) &&
			Directory.EnumerateFiles(
				paths.Resolve("assembly/action-dispositions"),
				actionFile + ".*.json")
				.Any();
	}

	private bool TryRecordDisposition(
		string actionPath,
		string outcome,
		string reason,
		string detail)
	{
		string safeOutcome = SanitizeFileSegment(outcome);
		string safeReason = SanitizeFileSegment(reason);
		string destination = paths.Resolve(
			"assembly/action-dispositions/" +
			Path.GetFileName(actionPath) + "." +
			safeOutcome + "." + safeReason + ".json");
		Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
		string temporaryPath =
			destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			string serialized = ContractSerializer.Serialize(new ActionDisposition
			{
				ActionFile = Path.GetFileName(actionPath),
				Outcome = outcome,
				Reason = reason,
				Detail = detail,
				RecordedUtc = DateTimeOffset.UtcNow
			});
			using (FileStream stream = new(
					temporaryPath,
					FileMode.CreateNew,
					FileAccess.Write,
					FileShare.None,
					4096,
					FileOptions.WriteThrough))
			using (StreamWriter text = new(stream, new System.Text.UTF8Encoding(false)))
			{
				text.Write(serialized);
				text.Flush();
				stream.Flush(true);
			}
			File.Move(temporaryPath, destination);
			WriteInspectionArtifact(actionPath, outcome, reason);
			return true;
		}
		catch (IOException) when (File.Exists(destination))
		{
			return false;
		}
		finally
		{
			if (File.Exists(temporaryPath))
				File.Delete(temporaryPath);
		}
	}

	private void WriteInspectionArtifact(
		string actionPath,
		string outcome,
		string reason)
	{
		string bucket = string.Equals(outcome, "consumed", StringComparison.Ordinal)
			? "consumed"
			: "quarantined";
		string suffix = bucket == "consumed" ? "" : "." + SanitizeFileSegment(reason);
		string destination = paths.Resolve(
			"assembly/" + bucket + "/" +
			Path.GetFileNameWithoutExtension(actionPath) + suffix + ".json");
		Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
		try
		{
			File.Copy(actionPath, destination, overwrite: false);
		}
		catch (IOException) when (File.Exists(destination))
		{
			// The disposition is authoritative. The bucket copy is for inspection only.
		}
	}

	private static string SanitizeFileSegment(string value)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		return new string(value
			.Select(character => invalid.Contains(character) ? '-' : character)
			.ToArray());
	}

	private enum ClaimResult
	{
		Claimed,
		AlreadyClaimedByThisAction,
		AlreadyClaimedByAnotherAction
	}

	private sealed class ActionDisposition
	{
		public string ActionFile { get; set; } = "";
		public string Outcome { get; set; } = "";
		public string Reason { get; set; } = "";
		public string Detail { get; set; } = "";
		public DateTimeOffset RecordedUtc { get; set; }
	}
}
