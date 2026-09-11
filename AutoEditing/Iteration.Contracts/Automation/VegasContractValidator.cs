using System;
using System.IO;
using AutoEditing.Iteration.Contracts.Evidence;
using AutoEditing.Iteration.Contracts.Serialization;

namespace AutoEditing.Iteration.Contracts.Automation;

public static class VegasContractValidator
{
	public static void Validate(VegasJobEnvelope envelope, DateTimeOffset now)
	{
		if (envelope == null) throw new ArgumentNullException(nameof(envelope));
		if (envelope.SchemaVersion != ContractSchema.CurrentVersion)
			throw new InvalidOperationException($"Unsupported schema version {envelope.SchemaVersion}.");
		if (!string.Equals(envelope.Protocol, ContractSchema.Protocol, StringComparison.Ordinal))
			throw new InvalidOperationException("Unsupported automation protocol.");
		RequireIdentifier(envelope.SessionId, nameof(envelope.SessionId));
		RequireIdentifier(envelope.JobId, nameof(envelope.JobId));
		RequireIdentifier(envelope.IdempotencyKey, nameof(envelope.IdempotencyKey));
		if (envelope.Sequence < 0) throw new ArgumentOutOfRangeException(nameof(envelope.Sequence));
		if (!VegasOperations.Supported.Contains(envelope.Operation))
			throw new InvalidOperationException($"Unsupported VEGAS operation '{envelope.Operation}'.");
		if (envelope.CreatedUtc == default) throw new ArgumentException("CreatedUtc is required.");
		if (envelope.DeadlineUtc <= envelope.CreatedUtc) throw new ArgumentException("DeadlineUtc must follow CreatedUtc.");
		if (envelope.DeadlineUtc <= now) throw new InvalidOperationException("The VEGAS job has expired.");
		if (envelope.Payload == null) throw new ArgumentException("Payload is required.");
		string actualHash = ContractHash.Compute(envelope.Payload);
		if (!string.Equals(actualHash, envelope.PayloadSha256, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Payload SHA-256 does not match. Expected " +
				envelope.PayloadSha256 + ", calculated " + actualHash + ".");
	}

	public static void Validate(RenderCandidatePreviewRequest request)
	{
		if (request == null) throw new ArgumentNullException(nameof(request));
		request.Workspace?.Validate();
		if (request.Workspace == null) throw new ArgumentException("Workspace is required.");
		if (request.Start < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(request.Start));
		if (request.Duration <= TimeSpan.Zero || request.Duration > TimeSpan.FromSeconds(20))
			throw new ArgumentOutOfRangeException(nameof(request.Duration), "Preview duration must be between zero and twenty seconds.");
		if (string.IsNullOrWhiteSpace(request.RenderProfileId)) throw new ArgumentException("RenderProfileId is required.");
		request.OutputRelativePath = SafeRelativePath.Validate(request.OutputRelativePath, nameof(request.OutputRelativePath));
	}

	public static void Validate(CaptureCandidatePreviewFramesRequest request)
	{
		if (request == null) throw new ArgumentNullException(nameof(request));
		request.Workspace?.Validate();
		if (request.Workspace == null) throw new ArgumentException("Workspace is required.");
		if (request.TimelineTimes == null ||
			request.TimelineTimes.Count < 1 ||
			request.TimelineTimes.Count > 9)
			throw new ArgumentOutOfRangeException(
				nameof(request.TimelineTimes),
				"Preview frame capture requires between one and nine sample times.");
		TimeSpan previous = TimeSpan.MinValue;
		foreach (TimeSpan time in request.TimelineTimes)
		{
			if (time < TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(
					nameof(request.TimelineTimes),
					"Preview frame times cannot be negative.");
			if (time <= previous)
				throw new ArgumentException(
					"Preview frame times must be strictly increasing and unique.",
					nameof(request.TimelineTimes));
			previous = time;
		}
		request.OutputDirectoryRelativePath = SafeRelativePath.Validate(
			request.OutputDirectoryRelativePath,
			nameof(request.OutputDirectoryRelativePath));
	}

	public static void Validate(PreflightCandidateRequest request)
	{
		if (request == null) throw new ArgumentNullException(nameof(request));
		ValidateCandidateInputs(request.Workspace, request.Plan, request.SongPath);
	}

	public static void Validate(MaterializeCandidateRequest request)
	{
		if (request == null) throw new ArgumentNullException(nameof(request));
		ValidateCandidateInputs(request.Workspace, request.Plan, request.SongPath);
	}

	public static void Validate(PromoteCandidateRequest request)
	{
		if (request == null) throw new ArgumentNullException(nameof(request));
		RequireIdentifier(request.PromotionId, nameof(request.PromotionId));
		if (request.Workspace == null)
			throw new ArgumentException("Workspace is required.");
		request.Workspace.Validate();
		RequireSha256(
			request.ExpectedCandidateSnapshotSha256,
			nameof(request.ExpectedCandidateSnapshotSha256));
	}

	public static void Validate(RollbackCandidatePromotionRequest request)
	{
		if (request == null) throw new ArgumentNullException(nameof(request));
		CandidatePromotionContract.ValidateResult(
			request.Promotion ?? throw new ArgumentException(
				"Promotion recovery evidence is required."));
	}

	public static void Validate(EditEvidenceReference evidence)
	{
		if (evidence == null) throw new ArgumentNullException(nameof(evidence));
		evidence.RelativePath = SafeRelativePath.Validate(evidence.RelativePath, nameof(evidence.RelativePath));
		if (evidence.TimelineStart < TimeSpan.Zero || evidence.TimelineEnd < evidence.TimelineStart)
			throw new ArgumentException("Evidence timeline range is invalid.");
	}

	private static void RequireIdentifier(string value, string name)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
			throw new ArgumentException($"{name} is required and may contain at most 128 characters.", name);
		foreach (char c in value)
			if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.'))
				throw new ArgumentException($"{name} contains unsupported characters.", name);
	}

	private static void RequireSha256(string value, string name)
	{
		if (value == null || value.Length != 64)
			throw new ArgumentException(
				name + " must be a SHA-256 value.",
				name);
		foreach (char character in value)
			if (!Uri.IsHexDigit(character))
				throw new ArgumentException(
					name + " must be a SHA-256 value.",
					name);
	}

	private static void ValidateCandidateInputs(
		CandidateWorkspaceId workspace,
		Core.Domain.Planning.EditPlanDocument plan,
		string songPath)
	{
		if (workspace == null) throw new ArgumentException("Workspace is required.");
		workspace.Validate();
		if (plan == null) throw new ArgumentException("Plan is required.");
		if (string.IsNullOrWhiteSpace(songPath) || !Path.IsPathRooted(songPath))
			throw new ArgumentException("SongPath must be an absolute path.", nameof(songPath));
	}
}
