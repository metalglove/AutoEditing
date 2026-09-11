using System;
using System.Threading;
using System.Threading.Tasks;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;

namespace Core.Host.Automation
{
	internal delegate Task<VegasJobResponse> VegasAutomationHandler(
		VegasJobEnvelope envelope,
		CancellationToken cancellationToken);

	/// <summary>
	/// Serializes host operations and delegates execution to an injected scheduler.
	/// The scheduler must use the existing RunScriptFile command boundary; this type
	/// deliberately has no ScriptPortal dependency and never invokes COM.
	/// </summary>
	internal sealed class VegasAutomationDispatcher
	{
		private readonly VegasAutomationJobStore _store;
		private readonly VegasAutomationHandler _handler;
		private readonly IVegasAutomationClock _clock;
		private readonly SemaphoreSlim _operationGate = new SemaphoreSlim(1, 1);

		public VegasAutomationDispatcher(
			VegasAutomationJobStore store,
			VegasAutomationHandler handler,
			IVegasAutomationClock clock = null)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
			_handler = handler ?? throw new ArgumentNullException(nameof(handler));
			_clock = clock ?? new SystemVegasAutomationClock();
		}

		public async Task<bool> TryProcessNextAsync(CancellationToken cancellationToken)
		{
			await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
                if (!_store.TryAcquireDispatchLease(out IDisposable dispatchLease))
                    return false;
                using (dispatchLease)
				{
					foreach (string requestPath in _store.GetPendingRequestPaths())
					{
						VegasAutomationClaim claim;
						try
						{
							if (!_store.TryClaim(requestPath, out claim))
								continue;
						}
						catch (Exception ex)
						{
							RetainUnreadableClaim(requestPath, ex);
							return true;
						}

						await ProcessClaimAsync(claim, cancellationToken).ConfigureAwait(false);
						return true;
					}
					return false;
				}
			}
			finally
			{
				_operationGate.Release();
			}
		}

		private async Task ProcessClaimAsync(VegasAutomationClaim claim, CancellationToken cancellationToken)
		{
			DateTimeOffset started = _clock.UtcNow;
			try
			{
				VegasContractValidator.Validate(claim.Envelope, started);
				VegasJobResponse existing =
					_store.FindCompletedByIdempotencyKey(claim.Envelope.IdempotencyKey);
				if (existing != null)
				{
					VegasJobResponse replay = CloneForJob(existing, claim.Envelope, started, _clock.UtcNow);
					_store.Complete(claim, replay);
					return;
				}

				VegasJobResponse response =
					await _handler(claim.Envelope, cancellationToken).ConfigureAwait(false);
				NormalizeResponse(response, claim.Envelope, started, _clock.UtcNow);
				_store.Complete(claim, response);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				_store.Complete(claim, Failure(
					claim.Envelope, VegasJobStatus.Cancelled, "cancelled", "dispatch",
					"The automation job was cancelled between VEGAS operations.", true, started, _clock.UtcNow));
			}
			catch (Exception ex)
			{
				VegasJobStatus status = claim.Envelope.DeadlineUtc <= _clock.UtcNow
					? VegasJobStatus.Expired
					: VegasJobStatus.Failed;
				_store.Complete(claim, Failure(
					claim.Envelope, status, status == VegasJobStatus.Expired ? "expired" : "dispatch-failed",
					"dispatch", ex.Message, false, started, _clock.UtcNow));
			}
		}

		private void RetainUnreadableClaim(string requestPath, Exception exception)
		{
			string name = System.IO.Path.GetFileName(requestPath);
			string jobId = System.IO.Path.GetFileNameWithoutExtension(name);
			string rawJson = "";
			try
			{
				rawJson = System.IO.File.ReadAllText(requestPath);
			}
			catch (System.IO.IOException)
			{
				// The store already moved the file to running; it will read it there
				// when retaining the invalid claim.
			}
			VegasJobResponse failure = new VegasJobResponse
			{
				SessionId = "",
				JobId = jobId,
				Status = VegasJobStatus.Failed,
				StartedUtc = _clock.UtcNow,
				CompletedUtc = _clock.UtcNow,
				Error = new AutomationError
				{
					Code = "invalid-envelope",
					Stage = "claim",
					Message = exception.Message,
					IsTransient = false
				}
			};
			_store.RetainInvalidClaim(name, rawJson, failure);
		}

		private static VegasJobResponse CloneForJob(
			VegasJobResponse existing,
			VegasJobEnvelope envelope,
			DateTimeOffset started,
			DateTimeOffset completed)
		{
			VegasJobResponse clone =
				ContractSerializer.Deserialize<VegasJobResponse>(ContractSerializer.Serialize(existing));
			clone.SessionId = envelope.SessionId;
			clone.JobId = envelope.JobId;
			clone.StartedUtc = started;
			clone.CompletedUtc = completed;
			return clone;
		}

		private static void NormalizeResponse(
			VegasJobResponse response,
			VegasJobEnvelope envelope,
			DateTimeOffset started,
			DateTimeOffset completed)
		{
			if (response == null)
				throw new InvalidOperationException("The VEGAS automation handler returned no response.");
			response.SessionId = envelope.SessionId;
			response.JobId = envelope.JobId;
			response.StartedUtc = response.StartedUtc == default ? started : response.StartedUtc;
			response.CompletedUtc = response.CompletedUtc == default ? completed : response.CompletedUtc;
			if (response.Result != null)
				response.ResultSha256 = ContractHash.Compute(response.Result);
		}

		private static VegasJobResponse Failure(
			VegasJobEnvelope envelope,
			VegasJobStatus status,
			string code,
			string stage,
			string message,
			bool transient,
			DateTimeOffset started,
			DateTimeOffset completed) =>
			new VegasJobResponse
			{
				SessionId = envelope.SessionId,
				JobId = envelope.JobId,
				Status = status,
				StartedUtc = started,
				CompletedUtc = completed,
				Error = new AutomationError
				{
					Code = code,
					Stage = stage,
					Message = message,
					IsTransient = transient
				}
			};
	}
}
