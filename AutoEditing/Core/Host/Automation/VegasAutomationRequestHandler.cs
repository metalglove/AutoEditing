using System;
using System.Threading;
using System.Threading.Tasks;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using Core.Scripts;
using Newtonsoft.Json.Linq;

namespace Core.Host.Automation
{
	/// <summary>
	/// Converts transport contracts into the existing typed VEGAS requests.
	/// IVegasQueryClient remains responsible for scheduling every request through
	/// the verified RunScriptFile boundary.
	/// </summary>
	internal sealed class VegasAutomationRequestHandler
	{
		private readonly IVegasQueryClient _queries;
		private readonly Func<VegasHostIdentity> _getHostIdentity;
		private readonly IVegasAutomationClock _clock;

		public VegasAutomationRequestHandler(
			IVegasQueryClient queries,
			Func<VegasHostIdentity> getHostIdentity,
			IVegasAutomationClock clock = null)
		{
			_queries = queries ?? throw new ArgumentNullException(nameof(queries));
			_getHostIdentity = getHostIdentity ?? throw new ArgumentNullException(nameof(getHostIdentity));
			_clock = clock ?? new SystemVegasAutomationClock();
		}

		public async Task<VegasJobResponse> HandleAsync(
			VegasJobEnvelope envelope,
			CancellationToken cancellationToken)
		{
			if (envelope == null) throw new ArgumentNullException(nameof(envelope));
			cancellationToken.ThrowIfCancellationRequested();
			VegasHostIdentity host = _getHostIdentity()
				?? throw new InvalidOperationException("The VEGAS host identity is unavailable.");
			ValidateProjectIdentity(envelope, host);

			DateTimeOffset started = _clock.UtcNow;
			JObject result;
			switch (envelope.Operation)
			{
				case VegasOperations.PreflightCandidate:
					result = ToObject(await _queries.QueryAsync(
						new PreflightCandidateCommand
						{
							Request = ReadPayload<PreflightCandidateRequest>(envelope)
						}).ConfigureAwait(false));
					break;

				case VegasOperations.MaterializeCandidate:
					result = ToObject(await _queries.QueryAsync(
						new MaterializeCandidateCommand
						{
							Request = ReadPayload<MaterializeCandidateRequest>(envelope)
						}).ConfigureAwait(false));
					break;

				case VegasOperations.GetCandidateSnapshot:
					result = ToObject(await _queries.QueryAsync(
						new GetCandidateSnapshotCommand
						{
							Request = ReadPayload<GetCandidateSnapshotRequest>(envelope)
						}).ConfigureAwait(false));
					break;

				case VegasOperations.RenderCandidatePreview:
					result = ToObject(await _queries.QueryAsync(
						new RenderCandidatePreviewCommand
						{
							Request = ReadPayload<RenderCandidatePreviewRequest>(envelope)
						}).ConfigureAwait(false));
					break;

				case VegasOperations.CaptureCandidatePreviewFrames:
					result = ToObject(await _queries.QueryAsync(
						new CaptureCandidatePreviewFramesCommand
						{
							Request = ReadPayload<CaptureCandidatePreviewFramesRequest>(envelope)
						}).ConfigureAwait(false));
					break;

				case VegasOperations.ApplyCandidateEffects:
					result = ToObject(await _queries.QueryAsync(
						new ApplyCandidateEffectsCommand
						{
							Request = ReadPayload<ApplyCandidateEffectsRequest>(envelope)
						}).ConfigureAwait(false));
					break;

				case VegasOperations.ApplyCandidateAudio:
					result = ToObject(await _queries.QueryAsync(
						new ApplyCandidateAudioCommand
						{
							Request = ReadPayload<ApplyCandidateAudioRequest>(envelope)
						}).ConfigureAwait(false));
					break;

				case VegasOperations.CleanupCandidate:
					result = ToObject(await _queries.QueryAsync(
						new CleanupCandidateCommand
						{
							Request = ReadPayload<CleanupCandidateRequest>(envelope)
						}).ConfigureAwait(false));
					break;

				case VegasOperations.PromoteCandidate:
					result = ToObject(await _queries.QueryAsync(
						new PromoteCandidateCommand
						{
							Request = ReadPayload<PromoteCandidateRequest>(envelope)
						}).ConfigureAwait(false));
					break;

				case VegasOperations.RollbackCandidatePromotion:
					result = ToObject(await _queries.QueryAsync(
						new RollbackCandidatePromotionCommand
						{
							Request = ReadPayload<RollbackCandidatePromotionRequest>(envelope)
						}).ConfigureAwait(false));
					break;

				default:
					throw new InvalidOperationException(
						$"Operation '{envelope.Operation}' has no host-side automation adapter.");
			}

			return new VegasJobResponse
			{
				SessionId = envelope.SessionId,
				JobId = envelope.JobId,
				Status = VegasJobStatus.Completed,
				StartedUtc = started,
				CompletedUtc = _clock.UtcNow,
				Host = host,
				Result = result,
				ResultSha256 = ContractHash.Compute(result)
			};
		}

		private static TRequest ReadPayload<TRequest>(VegasJobEnvelope envelope)
		{
			// Reuse strict contract settings so unknown members fail rather than
			// disappearing while crossing the host boundary.
			return ContractSerializer.Deserialize<TRequest>(envelope.Payload.ToString());
		}

		private static JObject ToObject(object value)
		{
			if (value == null)
				throw new InvalidOperationException("The VEGAS query returned no result.");
			return JObject.Parse(ContractSerializer.Serialize(value));
		}

		private static void ValidateProjectIdentity(
			VegasJobEnvelope envelope,
			VegasHostIdentity host)
		{
			if (string.IsNullOrWhiteSpace(envelope.ExpectedProjectFingerprint))
				return;
			if (!string.Equals(
				envelope.ExpectedProjectFingerprint,
				host.ProjectFingerprint,
				StringComparison.Ordinal))
				throw new InvalidOperationException(
					"The active VEGAS project does not match the job's expected project fingerprint.");
		}
	}
}
