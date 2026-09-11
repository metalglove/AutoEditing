using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Automation;
using Core.Domain.Planning;
using Newtonsoft.Json.Linq;

namespace AutoEditing.LlmEditor.Polish;

internal interface IPolishPreviewRenderer
{
	string RenderProfileId { get; }

	Task<RenderCandidatePreviewResult> RenderAsync(
		CandidateWorkspaceId workspace,
		TimeSpan start,
		TimeSpan duration,
		string outputRelativePath,
		string idempotencyKey,
		CancellationToken cancellationToken);
}

internal sealed class VegasPolishPreviewRenderer : IPolishPreviewRenderer
{
	private readonly IVegasAutomationClient automation;
	private readonly string profile;

	public VegasPolishPreviewRenderer(
		IVegasAutomationClient automation,
		string profile = "review-1080p")
	{
		this.automation = automation ?? throw new ArgumentNullException(nameof(automation));
		this.profile = string.IsNullOrWhiteSpace(profile)
			? throw new ArgumentException("A render profile is required.", nameof(profile))
			: profile;
	}

	public string RenderProfileId => profile;

	public Task<RenderCandidatePreviewResult> RenderAsync(
		CandidateWorkspaceId workspace,
		TimeSpan start,
		TimeSpan duration,
		string outputRelativePath,
		string idempotencyKey,
		CancellationToken cancellationToken) =>
		automation.ExecuteAsync<RenderCandidatePreviewRequest, RenderCandidatePreviewResult>(
			VegasOperations.RenderCandidatePreview,
			new RenderCandidatePreviewRequest
			{
				Workspace = workspace,
				Start = start,
				Duration = duration,
				RenderProfileId = profile,
				OutputRelativePath = outputRelativePath
			},
			idempotencyKey,
			cancellationToken: cancellationToken);
}

/// <summary>
/// Explicit plan -> approval -> materialize -> preview -> accept orchestration
/// for the two post-sync passes. It never treats a plan as rendered merely
/// because it was authored.
/// </summary>
internal sealed class PolishPassWorkflow
{
	private static readonly TimeSpan MaximumPreviewChunk = TimeSpan.FromSeconds(20);
	private readonly string sessionId;
	private readonly PolishPassArtifactStore artifacts;
	private readonly PolishPassPlanningService planner;
	private readonly IVegasAutomationClient automation;
	private readonly IPolishPreviewRenderer previewRenderer;
	private readonly Func<DateTimeOffset> clock;

	public PolishPassWorkflow(
		string sessionId,
		string sessionRoot,
		IVegasAutomationClient automation,
		IPolishPreviewRenderer previewRenderer,
		Func<DateTimeOffset>? clock = null)
	{
		this.sessionId = string.IsNullOrWhiteSpace(sessionId)
			? throw new ArgumentException("A session ID is required.", nameof(sessionId))
			: sessionId;
		artifacts = new PolishPassArtifactStore(sessionRoot);
		this.clock = clock ?? (() => DateTimeOffset.UtcNow);
		planner = new PolishPassPlanningService(this.clock);
		this.automation = automation ?? throw new ArgumentNullException(nameof(automation));
		this.previewRenderer = previewRenderer ??
			throw new ArgumentNullException(nameof(previewRenderer));
	}

	public (EffectsPassPlan Plan, string Sha256) CreateEffectsPlan(
		int revision,
		string roughCutSha256,
		EditPlanDocument acceptedRoughCut,
		PolishRendererCapabilities? capabilities = null)
	{
		EnsureNewRevision(PolishPassKind.Effects, revision);
		EffectsPassPlan plan = planner.PlanEffects(
			sessionId, revision, roughCutSha256, acceptedRoughCut, capabilities);
		string hash = artifacts.SavePlan(plan);
		Transition(plan, hash, PolishPlanStatus.AwaitingApproval,
			"Effects plan is ready for explicit approval.");
		return (plan, hash);
	}

	public (AudioPassPlan Plan, string Sha256) CreateAudioPlan(
		int revision,
		string roughCutSha256,
		string acceptedEffectsSha256,
		string songPath,
		EditPlanDocument acceptedRoughCut,
		PolishRendererCapabilities? capabilities = null)
	{
		EnsureAcceptedEffects(acceptedEffectsSha256);
		EnsureNewRevision(PolishPassKind.Audio, revision);
		AudioPassPlan plan = planner.PlanAudio(sessionId, revision,
			roughCutSha256, acceptedEffectsSha256, songPath, acceptedRoughCut,
			capabilities);
		string hash = artifacts.SavePlan(plan);
		Transition(plan, hash, PolishPlanStatus.AwaitingApproval,
			"Audio and SFX plan is ready for explicit approval.");
		return (plan, hash);
	}

	public void Decide(
		EffectsPassPlan plan,
		string hash,
		PolishApprovalDisposition disposition,
		string actor,
		string note = "")
	{
		PolishPassContractValidator.Validate(plan);
		DecideCore(PolishPassKind.Effects, plan.PlanId, plan.Revision, hash,
			disposition, actor, note,
			value => PolishPassContractValidator.Validate(value, plan, hash));
	}

	public void Decide(
		AudioPassPlan plan,
		string hash,
		PolishApprovalDisposition disposition,
		string actor,
		string note = "")
	{
		PolishPassContractValidator.Validate(plan);
		DecideCore(PolishPassKind.Audio, plan.PlanId, plan.Revision, hash,
			disposition, actor, note,
			value => PolishPassContractValidator.Validate(value, plan, hash));
	}

	public async Task<PolishPassMaterialization> ApplyEffectsAsync(
		EffectsPassPlan plan,
		string planSha256,
		CandidateWorkspaceId workspace,
		EditPlanDocument acceptedRoughCut,
		CancellationToken cancellationToken)
	{
		RequireApproved(PolishPassKind.Effects, plan.Revision, planSha256);
		Transition(plan, planSha256, PolishPlanStatus.Materializing,
			"Applying approved native screen-pump actions.");
		try
		{
			ApplyCandidateEffectsResult response =
				await automation.ExecuteAsync<ApplyCandidateEffectsRequest,
					ApplyCandidateEffectsResult>(
					VegasOperations.ApplyCandidateEffects,
					new ApplyCandidateEffectsRequest
					{
						Workspace = workspace,
						Plan = acceptedRoughCut,
						Effects = plan,
						PlanSha256 = planSha256
					},
					$"polish-effects-{planSha256}",
					cancellationToken: cancellationToken);
			PolishPassMaterialization result = response?.Materialization ??
				throw new InvalidOperationException(
					"VEGAS returned no effects materialization result.");
			ValidateMaterialization(result, planSha256, PolishPassKind.Effects,
				workspace);
			ValidateExpectedActions(
				plan.Actions.Select(item => item.ActionId), result);
			artifacts.SaveMaterialization(result);
			Transition(plan, planSha256,
				result.FullyApplied ? PolishPlanStatus.Applied : PolishPlanStatus.Failed,
				result.FullyApplied
					? "Every executable effects action was applied."
					: "At least one effects action was rejected; approval cannot proceed.");
			return result;
		}
		catch (Exception exception)
		{
			Transition(plan, planSha256, PolishPlanStatus.Failed,
				"Effects materialization failed: " + exception.Message);
			throw;
		}
	}

	public async Task<PolishPassMaterialization> ApplyAudioAsync(
		AudioPassPlan plan,
		string planSha256,
		CandidateWorkspaceId workspace,
		EditPlanDocument acceptedRoughCut,
		CancellationToken cancellationToken)
	{
		RequireApproved(PolishPassKind.Audio, plan.Revision, planSha256);
		EnsureAcceptedEffects(plan.EffectsPassSha256);
		Transition(plan, planSha256, PolishPlanStatus.Materializing,
			"Applying approved song and reviewed gun/hit SFX actions.");
		try
		{
			ApplyCandidateAudioResult response =
				await automation.ExecuteAsync<ApplyCandidateAudioRequest,
					ApplyCandidateAudioResult>(
					VegasOperations.ApplyCandidateAudio,
					new ApplyCandidateAudioRequest
					{
						Workspace = workspace,
						Plan = acceptedRoughCut,
						Audio = plan,
						PlanSha256 = planSha256
					},
					$"polish-audio-{planSha256}",
					cancellationToken: cancellationToken);
			PolishPassMaterialization result = response?.Materialization ??
				throw new InvalidOperationException(
					"VEGAS returned no audio materialization result.");
			ValidateMaterialization(result, planSha256, PolishPassKind.Audio,
				workspace);
			IEnumerable<string> expectedActions =
				plan.Sfx.Select(item => item.ActionId);
			if (plan.Song != null)
				expectedActions = new[] { plan.Song.ActionId }.Concat(expectedActions);
			ValidateExpectedActions(expectedActions, result);
			artifacts.SaveMaterialization(result);
			Transition(plan, planSha256,
				result.FullyApplied ? PolishPlanStatus.Applied : PolishPlanStatus.Failed,
				result.FullyApplied
					? "Every executable audio action was applied."
					: "At least one audio action was rejected; approval cannot proceed.");
			return result;
		}
		catch (Exception exception)
		{
			Transition(plan, planSha256, PolishPlanStatus.Failed,
				"Audio materialization failed: " + exception.Message);
			throw;
		}
	}

	public async Task<PolishPassPreviewManifest> RenderPreviewAsync(
		PolishPassKind pass,
		int revision,
		string planSha256,
		string planId,
		CandidateTimelineSnapshot timeline,
		CancellationToken cancellationToken)
	{
		PolishPassStateRecord state = RequireState(pass, revision, planSha256);
		PolishPassMaterialization? applied =
			artifacts.ReadMaterialization(pass, revision);
		bool recoverablePreviewFailure =
			state.Status == PolishPlanStatus.Failed &&
			applied != null && applied.FullyApplied;
		if (state.Status != PolishPlanStatus.Applied &&
			state.Status != PolishPlanStatus.PreviewReady &&
			!recoverablePreviewFailure)
			throw new InvalidOperationException(
				"A polish preview may be rendered only after complete materialization.");
		PolishPassPreviewManifest? existing =
			artifacts.ReadValidPreview(pass, revision);
		if (existing != null)
		{
			ValidatePreviewBinding(
				existing, pass, revision, planSha256, planId, timeline);
			return existing;
		}
		if (timeline == null || timeline.Workspace == null ||
			timeline.TimelineEnd <= timeline.TimelineStart)
			throw new InvalidOperationException(
				"A complete candidate timeline is required for polish preview.");
		if (applied == null || applied.Snapshot == null ||
			applied.Snapshot.Workspace == null ||
			!string.Equals(applied.Snapshot.Workspace.ToString(),
				timeline.Workspace.ToString(), StringComparison.Ordinal) ||
			applied.Snapshot.TimelineStart != timeline.TimelineStart ||
			applied.Snapshot.TimelineEnd != timeline.TimelineEnd ||
			!string.Equals(
				ContractHash.Compute(JToken.FromObject(applied.Snapshot)),
				ContractHash.Compute(JToken.FromObject(timeline)),
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Polish preview must use the exact post-materialization timeline snapshot.");
		try
		{
			List<RoughCutRenderChunk> chunks = new();
			string renderProfile = previewRenderer.RenderProfileId;
			TimeSpan cursor = timeline.TimelineStart;
			for (int index = 1; cursor < timeline.TimelineEnd; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				TimeSpan duration = timeline.TimelineEnd - cursor;
				if (duration > MaximumPreviewChunk) duration = MaximumPreviewChunk;
				string output = PolishPassArtifactStore.PreviewOutputRelativePath(
					pass, revision, index);
				RoughCutRenderChunk? completed =
					artifacts.ReadValidPreviewChunk(
						pass, revision, index, cursor, duration);
				if (completed != null)
				{
					chunks.Add(completed);
					cursor += duration;
					continue;
				}
				bool freshRenderRequired =
					artifacts.RequiresFreshPreviewRender(pass, revision, index);
				RenderCandidatePreviewResult rendered = await previewRenderer.RenderAsync(
					timeline.Workspace, cursor, duration, output,
					$"polish-{pass.ToString().ToLowerInvariant()}-{planSha256}-" +
						$"{index:D4}" +
						(freshRenderRequired
							? "-repair-" + Guid.NewGuid().ToString("N")
							: ""),
					cancellationToken);
				ValidateRender(rendered, output, duration);
				try
				{
					artifacts.ValidatePreviewOutput(output, rendered.Sha256);
				}
				catch (InvalidDataException) when (!freshRenderRequired)
				{
					// A broker may replay a completed response after its output
					// disappeared before chunk metadata was committed. Force a new
					// idempotency key after quarantining any untrusted bytes.
					artifacts.QuarantineUntrustedPreviewOutput(
						pass, revision, index, "uncommitted-output-invalid");
					rendered = await previewRenderer.RenderAsync(
						timeline.Workspace, cursor, duration, output,
						$"polish-{pass.ToString().ToLowerInvariant()}-{planSha256}-" +
							$"{index:D4}-repair-{Guid.NewGuid():N}",
						cancellationToken);
					ValidateRender(rendered, output, duration);
					artifacts.ValidatePreviewOutput(output, rendered.Sha256);
				}
				if (!string.Equals(renderProfile, rendered.RenderProfileId,
					StringComparison.Ordinal))
					throw new InvalidOperationException(
						"Polish preview chunks used different render profiles.");
				RoughCutRenderChunk chunk = new RoughCutRenderChunk
				{
					ChunkIndex = index,
					Start = cursor,
					Duration = duration,
					OutputRelativePath = rendered.OutputRelativePath,
					Sha256 = rendered.Sha256.ToLowerInvariant()
				};
				artifacts.SavePreviewChunk(pass, revision, chunk);
				chunks.Add(chunk);
				cursor += duration;
			}
			PolishPassPreviewManifest preview = new()
			{
				SessionId = sessionId,
				Pass = pass,
				PlanId = planId,
				PlanRevision = revision,
				PlanSha256 = planSha256,
				Workspace = timeline.Workspace,
				CompletedUtc = clock(),
				RenderProfileId = renderProfile,
				TimelineStart = timeline.TimelineStart,
				TimelineEnd = timeline.TimelineEnd,
				Chunks = chunks
			};
			artifacts.SavePreview(preview);
			Transition(pass, planId, revision, planSha256,
				PolishPlanStatus.PreviewReady,
				"Complete bounded preview evidence is ready for final pass approval.");
			return preview;
		}
		catch (Exception exception)
		{
			Transition(pass, planId, revision, planSha256,
				PolishPlanStatus.Failed,
				"Polish preview failed: " + exception.Message);
			throw;
		}
	}

	public AcceptedPolishPass Accept(
		PolishPassKind pass,
		int revision,
		string planSha256,
		string actor)
	{
		PolishPassStateRecord state = RequireState(pass, revision, planSha256);
		AcceptedPolishPass? existing =
			artifacts.ReadAccepted(pass, planSha256);
		if (existing != null)
		{
			if (state.Status != PolishPlanStatus.Accepted)
				Transition(
					pass,
					state.PlanId,
					revision,
					planSha256,
					PolishPlanStatus.Accepted,
					"Recovered exact accepted polish-pass evidence.");
			return existing;
		}
		if (state.Status != PolishPlanStatus.PreviewReady)
			throw new InvalidOperationException(
				"A polish pass can be accepted only after complete preview evidence.");
		PolishPassMaterialization materialization =
			artifacts.ReadMaterialization(pass, revision) ??
			throw new InvalidDataException("Polish materialization evidence is missing.");
		if (!materialization.FullyApplied)
			throw new InvalidOperationException(
				"A partially applied polish pass cannot be accepted.");
		PolishPassPreviewManifest preview =
			artifacts.ReadValidPreview(pass, revision) ??
			throw new InvalidDataException("Polish preview evidence is missing.");
		if (!string.Equals(materialization.PlanSha256, planSha256,
				StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(preview.PlanSha256, planSha256,
				StringComparison.OrdinalIgnoreCase) ||
			materialization.Pass != pass || preview.Pass != pass)
			throw new InvalidDataException(
				"Polish acceptance evidence targets a different pass or plan.");
		AcceptedPolishPass accepted = new()
		{
			SessionId = sessionId,
			Pass = pass,
			PlanSha256 = planSha256,
			MaterializationSha256 = artifacts.ArtifactSha256(
				pass, revision, "materialization"),
			PreviewSha256 = artifacts.ArtifactSha256(pass, revision, "preview"),
			AcceptedBy = actor,
			AcceptedUtc = clock()
		};
		PolishPassContractValidator.Validate(accepted);
		artifacts.SaveAccepted(accepted);
		Transition(pass, state.PlanId, revision, planSha256,
			PolishPlanStatus.Accepted,
			"Rendered polish pass and preview evidence were explicitly accepted.");
		return accepted;
	}

	public RejectedPolishPassPreview RejectPreview(
		PolishPassKind pass,
		int revision,
		string planSha256,
		string actor,
		string note)
	{
		PolishPassStateRecord state = RequireState(pass, revision, planSha256);
		RejectedPolishPassPreview? existing =
			artifacts.ReadRejected(pass, planSha256);
		if (existing != null)
		{
			if (state.Status != PolishPlanStatus.Rejected)
				Transition(
					pass,
					state.PlanId,
					revision,
					planSha256,
					PolishPlanStatus.Rejected,
					"Recovered exact rejected polish-preview evidence.");
			return existing;
		}
		if (state.Status != PolishPlanStatus.PreviewReady)
			throw new InvalidOperationException(
				"Only a rendered PreviewReady polish revision can be rejected.");
		PolishPassMaterialization materialization =
			artifacts.ReadMaterialization(pass, revision) ??
			throw new InvalidDataException("Polish materialization evidence is missing.");
		PolishPassPreviewManifest preview =
			artifacts.ReadValidPreview(pass, revision) ??
			throw new InvalidDataException("Polish preview evidence is missing.");
		if (!string.Equals(materialization.PlanSha256, planSha256,
				StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(preview.PlanSha256, planSha256,
				StringComparison.OrdinalIgnoreCase) ||
			materialization.Pass != pass || preview.Pass != pass)
			throw new InvalidDataException(
				"Polish rejection evidence targets a different pass or plan.");
		RejectedPolishPassPreview rejected = new()
		{
			SessionId = sessionId,
			Pass = pass,
			PlanSha256 = planSha256,
			MaterializationSha256 = artifacts.ArtifactSha256(
				pass, revision, "materialization"),
			PreviewSha256 = artifacts.ArtifactSha256(pass, revision, "preview"),
			RejectedBy = actor,
			Note = note ?? "",
			BaselineRestoreRequired = true,
			RejectedUtc = clock()
		};
		PolishPassContractValidator.Validate(rejected);
		artifacts.SaveRejected(rejected);
		Transition(pass, state.PlanId, revision, planSha256,
			PolishPlanStatus.Rejected,
			"Rendered polish preview was rejected; restore the accepted " +
			"candidate baseline before applying a later revision.");
		return rejected;
	}

	private void DecideCore(PolishPassKind pass, string planId, int revision,
		string hash, PolishApprovalDisposition disposition, string actor, string note,
		Action<PolishPassApproval> validate)
	{
		PolishPassStateRecord state = RequireState(pass, revision, hash);
		PolishPassApproval? existing = artifacts.ReadApproval(pass, revision);
		if (existing != null)
		{
			validate(existing);
			if (existing.Disposition != disposition)
				throw new InvalidOperationException(
					"The exact polish plan already has a different decision.");
			if (state.Status == PolishPlanStatus.AwaitingApproval)
				Transition(
					pass,
					planId,
					revision,
					hash,
					disposition == PolishApprovalDisposition.Approve
						? PolishPlanStatus.Approved
						: PolishPlanStatus.Rejected,
					"Recovered exact persisted polish-plan decision.");
			return;
		}
		if (state.Status != PolishPlanStatus.AwaitingApproval)
			throw new InvalidOperationException(
				"Only a newly planned polish revision can receive an approval decision.");
		PolishPassApproval approval = new()
		{
			SessionId = sessionId,
			Pass = pass,
			PlanId = planId,
			PlanRevision = revision,
			PlanSha256 = hash,
			Disposition = disposition,
			Note = note ?? "",
			DecidedBy = actor,
			DecidedUtc = clock()
		};
		validate(approval);
		artifacts.SaveApproval(approval);
		Transition(pass, planId, revision, hash,
			disposition == PolishApprovalDisposition.Approve
				? PolishPlanStatus.Approved : PolishPlanStatus.Rejected,
			disposition == PolishApprovalDisposition.Approve
				? "Editor approved exact polish plan revision."
				: "Editor rejected exact polish plan revision.");
	}

	private void RequireApproved(PolishPassKind pass, int revision, string hash)
	{
		PolishPassStateRecord state = RequireState(pass, revision, hash);
		if (state.Status != PolishPlanStatus.Approved &&
			state.Status != PolishPlanStatus.Failed &&
			state.Status != PolishPlanStatus.Materializing)
			throw new InvalidOperationException(
				"Polish materialization requires exact plan approval.");
		PolishPassApproval approval = artifacts.ReadApproval(pass, revision) ??
			throw new InvalidDataException("Polish approval artifact is missing.");
		if (approval.Disposition != PolishApprovalDisposition.Approve ||
			!string.Equals(approval.PlanSha256, hash,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"Polish approval does not authorize this exact plan.");
	}

	private void EnsureAcceptedEffects(string effectsHash)
	{
		if (effectsHash == null || effectsHash.Length != 64)
			throw new InvalidDataException(
				"Audio planning requires an accepted effects-pass hash.");
		PolishPassStateRecord state = artifacts.ReadLatestState(PolishPassKind.Effects) ??
			throw new InvalidOperationException(
				"Audio planning requires an accepted effects pass.");
		if (state.Status != PolishPlanStatus.Accepted ||
			!string.Equals(state.PlanSha256, effectsHash,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Audio planning must bind to the exact accepted effects pass.");
	}

	private void EnsureNewRevision(PolishPassKind pass, int revision)
	{
		if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
		PolishPassStateRecord? latest = artifacts.ReadLatestState(pass);
		if (latest != null && revision <= latest.PlanRevision)
			throw new InvalidOperationException(
				"A new polish plan revision must increase monotonically.");
	}

	private PolishPassStateRecord RequireState(
		PolishPassKind pass, int revision, string hash)
	{
		PolishPassStateRecord state = artifacts.ReadLatestState(pass) ??
			throw new InvalidOperationException("The polish pass has not been planned.");
		if (state.PlanRevision != revision ||
			!string.Equals(state.PlanSha256, hash, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"The requested operation targets a stale polish plan revision.");
		return state;
	}

	private void Transition(EffectsPassPlan plan, string hash,
		PolishPlanStatus status, string reason) =>
		Transition(PolishPassKind.Effects, plan.PlanId, plan.Revision, hash,
			status, reason);
	private void Transition(AudioPassPlan plan, string hash,
		PolishPlanStatus status, string reason) =>
		Transition(PolishPassKind.Audio, plan.PlanId, plan.Revision, hash,
			status, reason);
	private void Transition(PolishPassKind pass, string planId, int revision,
		string hash, PolishPlanStatus status, string reason)
	{
		PolishPassStateRecord? previous = artifacts.ReadLatestState(pass);
		PolishPassStateRecord next = new()
		{
			SessionId = sessionId,
			Pass = pass,
			PlanId = planId,
			PlanRevision = revision,
			PlanSha256 = hash,
			Sequence = (previous?.Sequence ?? 0) + 1,
			Status = status,
			Reason = reason,
			RecordedUtc = clock()
		};
		PolishPassContractValidator.Validate(next);
		artifacts.AppendState(next);
	}

	private static void ValidateMaterialization(PolishPassMaterialization result,
		string hash, PolishPassKind pass, CandidateWorkspaceId workspace)
	{
		PolishPassContractValidator.Validate(result);
		if (result.Pass != pass ||
			!string.Equals(result.PlanSha256, hash, StringComparison.OrdinalIgnoreCase) ||
			result.Workspace.ToString() != workspace.ToString())
			throw new InvalidDataException(
				"VEGAS materialization result targets different polish state.");
	}

	private static void ValidateExpectedActions(
		IEnumerable<string> expected,
		PolishPassMaterialization result)
	{
		List<string> expectedIds = expected.ToList();
		List<string> actualIds = result.Actions.Select(item => item.ActionId).ToList();
		if (!actualIds.SequenceEqual(expectedIds, StringComparer.Ordinal))
			throw new InvalidDataException(
				"VEGAS materialization did not report every planned action in order.");
	}

	private static void ValidateRender(RenderCandidatePreviewResult value,
		string path, TimeSpan duration)
	{
		if (value == null ||
			!string.Equals(value.OutputRelativePath, path,
				StringComparison.OrdinalIgnoreCase) ||
			(value.RenderedDuration - duration).Duration() >
				TimeSpan.FromMilliseconds(10) ||
			string.IsNullOrWhiteSpace(value.RenderProfileId) ||
			value.Sha256 == null || value.Sha256.Length != 64 ||
			value.Sha256.Any(character => !Uri.IsHexDigit(character)))
			throw new InvalidOperationException(
				"VEGAS returned invalid polish preview evidence.");
	}

	private static void ValidatePreviewBinding(
		PolishPassPreviewManifest preview,
		PolishPassKind pass,
		int revision,
		string planSha256,
		string planId,
		CandidateTimelineSnapshot timeline)
	{
		if (preview.Pass != pass ||
			preview.PlanRevision != revision ||
			!string.Equals(
				preview.PlanSha256, planSha256, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(preview.PlanId, planId, StringComparison.Ordinal) ||
			preview.Workspace == null ||
			timeline.Workspace == null ||
			!string.Equals(
				preview.Workspace.ToString(),
				timeline.Workspace.ToString(),
				StringComparison.Ordinal) ||
			preview.TimelineStart != timeline.TimelineStart ||
			preview.TimelineEnd != timeline.TimelineEnd)
			throw new InvalidDataException(
				"A completed polish preview exists for different session state.");
	}
}
