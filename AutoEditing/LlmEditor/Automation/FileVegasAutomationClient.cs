using System.Security.Cryptography;
using System.Text;
using AutoEditing.Iteration.Contracts;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Sessions;
using Newtonsoft.Json.Linq;

namespace AutoEditing.LlmEditor.Automation;

internal sealed class FileVegasAutomationClient : IVegasAutomationClient
{
	private readonly VegasAutomationClientOptions options;
	private readonly VegasAutomationSpoolPaths paths;
	private readonly AtomicFileWriter writer = new();
	private readonly SemaphoreSlim submissionGate = new(1, 1);
	private string expectedProjectFingerprint;

	public VegasHostIdentity? LastHost { get; private set; }
	public string? ExpectedProjectFingerprint => expectedProjectFingerprint;

	public FileVegasAutomationClient(VegasAutomationClientOptions options)
	{
		this.options = options ?? throw new ArgumentNullException(nameof(options));
		options.Validate();
		paths = new VegasAutomationSpoolPaths(options.SpoolRoot);
		expectedProjectFingerprint = options.ExpectedProjectFingerprint ?? "";
	}

	public async Task<TResult> ExecuteAsync<TRequest, TResult>(
		string operation,
		TRequest request,
		string idempotencyKey,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
		TimeSpan effectiveTimeout = timeout ?? options.DefaultTimeout;
		if (effectiveTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

		// Build the payload through the contract serializer before hashing it.
		// JToken.FromObject can retain CLR-only scalar kinds (notably TimeSpan);
		// those become JSON strings on disk and otherwise produce a different
		// canonical hash when the VEGAS host reads the envelope back.
		JToken payload = JToken.Parse(ContractSerializer.Serialize(request));
		VegasJobEnvelope envelope = await PublishAsync(
			operation,
			payload,
			idempotencyKey,
			effectiveTimeout,
			cancellationToken).ConfigureAwait(false);
		VegasJobResponse response = await WaitForResponseAsync(
			envelope,
			effectiveTimeout,
			cancellationToken).ConfigureAwait(false);
		LastHost = response.Host;

		if (response.Status != VegasJobStatus.Completed)
			throw new VegasAutomationException(response.Status, response.Error);
		PinProjectIdentity(response.Host);
		if (response.Result == null)
			throw new InvalidDataException("Completed VEGAS response has no result payload.");
		return response.Result.ToObject<TResult>()
			?? throw new InvalidDataException("VEGAS response result could not be deserialized.");
	}

	private async Task<VegasJobEnvelope> PublishAsync(
		string operation,
		JToken payload,
		string idempotencyKey,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		await submissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			using FileStream crossProcessLock = await AcquireSubmissionLockAsync(cancellationToken)
				.ConfigureAwait(false);
			string jobId = CreateJobId(options.SessionId, idempotencyKey);
			VegasJobEnvelope? existing = ReadExisting(jobId);
			if (existing != null)
			{
				ValidateIdempotentReuse(existing, operation, payload, idempotencyKey);
				if (!RequiresNewAttempt(existing, jobId))
				{
					if (!File.Exists(paths.Request(jobId)) &&
						!File.Exists(paths.RunningJob(jobId)) &&
						!File.Exists(paths.Response(jobId)))
						writer.WriteText(paths.Request(jobId), ContractSerializer.Serialize(existing));
					return existing;
				}
				// The previous attempt failed or expired before running. Publish a fresh
				// attempt under the same job id so the retry gets a new deadline instead
				// of replaying the old failure.
				File.Delete(paths.Response(jobId));
			}

			DateTimeOffset created = DateTimeOffset.UtcNow;
			VegasJobEnvelope envelope = new()
			{
				SessionId = options.SessionId,
				JobId = jobId,
				Sequence = FindNextSequence(),
				IdempotencyKey = idempotencyKey,
				Operation = operation,
				CreatedUtc = created,
				DeadlineUtc = created + timeout,
				Payload = payload,
				PayloadSha256 = ContractHash.Compute(payload),
				ExpectedProjectFingerprint = expectedProjectFingerprint
			};
			VegasContractValidator.Validate(envelope, created);
			writer.WriteText(paths.Submission(jobId), ContractSerializer.Serialize(envelope));
			writer.WriteText(paths.Request(jobId), ContractSerializer.Serialize(envelope));
			return envelope;
		}
		finally
		{
			submissionGate.Release();
		}
	}

	private async Task<FileStream> AcquireSubmissionLockAsync(CancellationToken cancellationToken)
	{
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				return new FileStream(
					paths.SubmissionLock,
					FileMode.OpenOrCreate,
					FileAccess.ReadWrite,
					FileShare.None);
			}
			catch (IOException)
			{
				await Task.Delay(options.PollInterval, cancellationToken).ConfigureAwait(false);
			}
		}
	}

	private async Task<VegasJobResponse> WaitForResponseAsync(
		VegasJobEnvelope envelope,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		using CancellationTokenSource deadline = new(timeout);
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken,
			deadline.Token);
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (deadline.IsCancellationRequested)
				throw new TimeoutException(
					$"Timed out waiting for VEGAS automation job '{envelope.JobId}' after {timeout}.");
			string responsePath = paths.Response(envelope.JobId);
			if (File.Exists(responsePath))
			{
				VegasJobResponse response = ContractSerializer.Deserialize<VegasJobResponse>(
					File.ReadAllText(responsePath));
				ValidateResponse(envelope, response);
				if (IsTerminal(response.Status)) return response;
			}
			try
			{
				await Task.Delay(options.PollInterval, linked.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
			{
				throw new TimeoutException(
					$"Timed out waiting for VEGAS automation job '{envelope.JobId}' after {timeout}.");
			}
		}
	}

	private VegasJobEnvelope? ReadExisting(string jobId)
	{
		foreach (string path in new[] { paths.Submission(jobId), paths.RunningJob(jobId), paths.Request(jobId) })
		{
			if (!File.Exists(path)) continue;
			return ContractSerializer.Deserialize<VegasJobEnvelope>(File.ReadAllText(path));
		}
		return null;
	}

	private bool RequiresNewAttempt(VegasJobEnvelope existing, string jobId)
	{
		string responsePath = paths.Response(jobId);
		if (File.Exists(responsePath))
		{
			VegasJobResponse response = ContractSerializer.Deserialize<VegasJobResponse>(
				File.ReadAllText(responsePath));
			return IsTerminal(response.Status) && response.Status != VegasJobStatus.Completed;
		}
		if (File.Exists(paths.RunningJob(jobId))) return false;
		// A request still waiting (or lost) past its deadline can only expire.
		return existing.DeadlineUtc <= DateTimeOffset.UtcNow;
	}

	private long FindNextSequence()
	{
		long maximum = 0;
		foreach (string directory in new[] { paths.Submissions })
		{
			foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
			{
				VegasJobEnvelope envelope = ContractSerializer.Deserialize<VegasJobEnvelope>(File.ReadAllText(path));
				if (string.Equals(envelope.SessionId, options.SessionId, StringComparison.Ordinal))
					maximum = Math.Max(maximum, envelope.Sequence);
			}
		}
		return checked(maximum + 1);
	}

	private static void ValidateIdempotentReuse(
		VegasJobEnvelope existing,
		string operation,
		JToken payload,
		string idempotencyKey)
	{
		if (!string.Equals(existing.IdempotencyKey, idempotencyKey, StringComparison.Ordinal) ||
			!string.Equals(existing.Operation, operation, StringComparison.Ordinal) ||
			!string.Equals(existing.PayloadSha256, ContractHash.Compute(payload), StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Idempotency key was already used for a different VEGAS automation request.");
	}

	private static void ValidateResponse(VegasJobEnvelope request, VegasJobResponse response)
	{
		if (response.SchemaVersion != ContractSchema.CurrentVersion ||
			!string.Equals(response.Protocol, ContractSchema.Protocol, StringComparison.Ordinal))
			throw new InvalidDataException("VEGAS response uses an unsupported contract version.");
		if (!string.Equals(response.SessionId, request.SessionId, StringComparison.Ordinal) ||
			!string.Equals(response.JobId, request.JobId, StringComparison.Ordinal))
			throw new InvalidDataException("VEGAS response identity does not match the request.");
		if (response.Status != VegasJobStatus.Completed)
		{
			if (IsTerminal(response.Status) && response.Error == null)
				throw new InvalidDataException("Failed VEGAS response has no structured error.");
			return;
		}
		if (response.Host == null ||
			string.IsNullOrWhiteSpace(response.Host.ProjectFingerprint))
			throw new InvalidDataException(
				"Completed VEGAS response has no project identity, so timeline ownership cannot be proven.");
		if (!string.IsNullOrWhiteSpace(request.ExpectedProjectFingerprint) &&
			!string.Equals(
				request.ExpectedProjectFingerprint,
				response.Host.ProjectFingerprint,
				StringComparison.Ordinal))
			throw new InvalidDataException(
				"VEGAS response came from a different project than the request required.");
		if (response.Result == null) throw new InvalidDataException("Completed VEGAS response has no result.");
		string actual = ContractHash.Compute(response.Result);
		if (!string.Equals(actual, response.ResultSha256, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException("VEGAS response result SHA-256 does not match.");
	}

	private static bool IsTerminal(VegasJobStatus status) =>
		status is VegasJobStatus.Completed or VegasJobStatus.Failed or
			VegasJobStatus.Cancelled or VegasJobStatus.Expired;

	private void PinProjectIdentity(VegasHostIdentity host)
	{
		if (string.IsNullOrWhiteSpace(expectedProjectFingerprint))
		{
			expectedProjectFingerprint = host.ProjectFingerprint;
			return;
		}
		if (!string.Equals(
				expectedProjectFingerprint,
				host.ProjectFingerprint,
				StringComparison.Ordinal))
			throw new InvalidDataException(
				"The active VEGAS project changed while the automation client was running.");
	}

	private static string CreateJobId(string sessionId, string idempotencyKey)
	{
		using SHA256 sha = SHA256.Create();
		byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(sessionId + "\n" + idempotencyKey));
		return "job-" + Convert.ToHexString(digest).ToLowerInvariant()[..32];
	}
}
