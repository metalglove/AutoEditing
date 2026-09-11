using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Automation;
using AutoEditing.LlmEditor.Assembly;
using AutoEditing.LlmEditor.Polish;
using AutoEditing.LlmEditor.Sessions;
using Core.Domain.Audio.SongAnalysis;
using Core.Domain.Editing;
using Core.Domain.Planning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AutoEditing.LlmEditor;

internal static class PolishPassSelfTests
{
	public static void Run(string root, EditPlanDocument source)
	{
		string sessionRoot = Path.Combine(root, "polish");
		Directory.CreateDirectory(sessionRoot);
		EditPlanDocument plan = JsonConvert.DeserializeObject<EditPlanDocument>(
			JsonConvert.SerializeObject(source))!;
		EditPlanDocumentValidator.ValidateAndNormalize(plan);
		ClipPlacement placement = plan.Montage.Placements
			.OrderBy(item => item.TimelineStartSeconds).First();
		double effectTime = Math.Min(
			placement.TimelineEndSeconds - 0.05,
			placement.TimelineStartSeconds + 0.2);
		plan.Montage.EffectTreatments = new EffectTreatmentPlan
		{
			Actions = new List<EffectTreatmentAction>
			{
				new()
				{
					EventId = "supported",
					TimeSeconds = effectTime,
					Type = EditorialUse.ScreenPump,
					RecipeId = "native.pump.explicit",
					Intensity = 0.7,
					DurationSeconds = 0.2,
					Origin = EffectTreatmentOrigin.Manual,
					Reason = "self-test supported pump"
				},
				new()
				{
					EventId = "unsupported",
					TimeSeconds = effectTime,
					Type = EditorialUse.Flash,
					RecipeId = "unsupported",
					Intensity = 0.5,
					DurationSeconds = 0.1,
					Origin = EffectTreatmentOrigin.Manual,
					Reason = "self-test unsupported intent"
				}
			}
		};
		string roughCutHash = Convert.ToHexString(SHA256.HashData(
			new UTF8Encoding(false).GetBytes(
				EditPlanDocumentSerializer.SerializePlan(plan)))).ToLowerInvariant();
		CandidateWorkspaceId workspace = new CandidateWorkspaceId
		{
			SessionId = "polish-self-test",
			Iteration = 1,
			Nonce = "phase7"
		};
		DateTimeOffset now = new DateTimeOffset(2026, 7, 27, 12, 0, 0,
			TimeSpan.Zero);
		FakePolishAutomation automation = new(plan, workspace, now);
		FakePolishPreviewRenderer preview = new(sessionRoot);
		PolishPassWorkflow workflow = new(
			"polish-self-test", sessionRoot, automation, preview, () => now);

		(EffectsPassPlan effects, string effectsHash) =
			workflow.CreateEffectsPlan(1, roughCutHash, plan);
		Assert(effects.Actions.All(action =>
			action.RecipeId.StartsWith("native.pump.", StringComparison.Ordinal)),
			"Effects planning emitted only supported native pumps.");
		Assert(effects.Diagnostics.Any(item =>
			item.Contains("unsupported intent", StringComparison.OrdinalIgnoreCase)),
			"Unsupported visual intent is explicit.");
		AssertThrows<InvalidOperationException>(() =>
			workflow.ApplyEffectsAsync(effects, effectsHash, workspace, plan,
				CancellationToken.None).GetAwaiter().GetResult(),
			"Effects cannot materialize without exact approval.");

		workflow.Decide(effects, effectsHash, PolishApprovalDisposition.Approve,
			"self-test");
		PolishPassMaterialization effectResult =
			workflow.ApplyEffectsAsync(effects, effectsHash, workspace, plan,
				CancellationToken.None).GetAwaiter().GetResult();
		Assert(effectResult.FullyApplied, "Approved supported effects fully apply.");
		CandidateTimelineSnapshot divergentSnapshot =
			ContractSerializer.Deserialize<CandidateTimelineSnapshot>(
				ContractSerializer.Serialize(automation.Snapshot));
		divergentSnapshot.Warnings = new List<string> { "simulated divergence" };
		AssertThrows<InvalidOperationException>(() =>
			workflow.RenderPreviewAsync(
					PolishPassKind.Effects,
					1,
					effectsHash,
					effects.PlanId,
					divergentSnapshot,
					CancellationToken.None)
				.GetAwaiter().GetResult(),
			"Preview accepted a timeline that differed from materialization evidence.");
		PolishPassPreviewManifest effectPreview = workflow.RenderPreviewAsync(
			PolishPassKind.Effects, 1, effectsHash, effects.PlanId,
			automation.Snapshot, CancellationToken.None).GetAwaiter().GetResult();
		Assert(effectPreview.Chunks.All(item =>
			item.Duration <= TimeSpan.FromSeconds(20)),
			"Effects previews remain bounded.");
		int renderCallsBeforeRepair = preview.RenderCalls;
		string damagedChunk = Path.Combine(
			sessionRoot,
			effectPreview.Chunks[0].OutputRelativePath.Replace(
				'/', Path.DirectorySeparatorChar));
		File.WriteAllText(damagedChunk, "damaged cached preview");
		PolishPassPreviewManifest repairedPreview = workflow.RenderPreviewAsync(
			PolishPassKind.Effects, 1, effectsHash, effects.PlanId,
			automation.Snapshot, CancellationToken.None).GetAwaiter().GetResult();
		Assert(preview.RenderCalls > renderCallsBeforeRepair &&
			new SessionArtifactHasher().ComputeSha256(damagedChunk) ==
				repairedPreview.Chunks[0].Sha256,
			"Corrupt cached preview bytes were quarantined and freshly rendered.");
		AcceptedPolishPass acceptedEffects =
			workflow.Accept(PolishPassKind.Effects, 1, effectsHash, "self-test");
		Assert(acceptedEffects.PlanSha256 == effectsHash,
			"Accepted effects bind exact plan hash.");

		string song = Path.Combine(Path.GetPathRoot(root)!, "music", "song.wav");
		(AudioPassPlan audio, string audioHash) = workflow.CreateAudioPlan(
			1, roughCutHash, effectsHash, song, plan);
		int expectedKills = plan.Montage.Placements.Sum(item =>
			item.TimelineShotEvents.Count(shot => shot.SourceEvent.IsConfirmedKill));
		Assert(audio.Song != null && audio.Sfx.Count == expectedKills,
			"Audio planning includes one song action and every reviewed kill.");
		workflow.Decide(audio, audioHash, PolishApprovalDisposition.Approve,
			"self-test");
		PolishPassMaterialization audioResult =
			workflow.ApplyAudioAsync(audio, audioHash, workspace, plan,
				CancellationToken.None).GetAwaiter().GetResult();
		Assert(audioResult.Actions.Count == expectedKills + 1,
			"Audio materialization reports every planned action.");
		PolishPassPreviewManifest audioPreview = workflow.RenderPreviewAsync(
			PolishPassKind.Audio, 1, audioHash, audio.PlanId,
			automation.Snapshot, CancellationToken.None).GetAwaiter().GetResult();
		Assert(audioPreview.Chunks.Count >= 1, "Audio preview evidence is persisted.");
		AcceptedPolishPass acceptedAudio =
			workflow.Accept(PolishPassKind.Audio, 1, audioHash, "self-test");
		Assert(acceptedAudio.PlanSha256 == audioHash,
			"Accepted audio binds exact plan hash.");

		PolishPassArtifactStore store = new(sessionRoot);
		Assert(store.ReadLatestState(PolishPassKind.Audio)?.Status ==
			PolishPlanStatus.Accepted, "Audio state journal reaches Accepted.");

		effects.Diagnostics.Add("mutated");
		AssertThrows<InvalidOperationException>(() => store.SavePlan(effects),
			"Immutable plan revisions cannot be overwritten.");

		PolishRendererCapabilities unavailable = new()
		{
			ScreenPump = false,
			SongTrack = true,
			ReviewedGunHitSfx = true
		};
		PolishPassPlanningService planner = new(() => now);
		EffectsPassPlan noEffects = planner.PlanEffects(
			"other-session", 1, roughCutHash, plan, unavailable);
		Assert(noEffects.Actions.Count == 0,
			"Unavailable effect renderers produce no executable actions.");

		TestNoOpAndPreviewRejection(
			root, plan, roughCutHash, workspace, now, song);
		TestPersistedDecisionActionRestart(
			root, plan, roughCutHash, workspace, now);
	}

	private static void TestPersistedDecisionActionRestart(
		string root,
		EditPlanDocument plan,
		string roughCutHash,
		CandidateWorkspaceId workspace,
		DateTimeOffset now)
	{
		string sessionId = "polish-decision-restart";
		string sessionRoot = Path.Combine(root, sessionId);
		Directory.CreateDirectory(sessionRoot);
		CandidateWorkspaceId restartWorkspace = new()
		{
			SessionId = sessionId,
			Iteration = workspace.Iteration,
			Nonce = workspace.Nonce
		};
		PolishRendererCapabilities none = new()
		{
			ScreenPump = false,
			SongTrack = false,
			ReviewedGunHitSfx = false
		};
		PolishPassWorkflow first = new(
			sessionId,
			sessionRoot,
			new FakePolishAutomation(plan, restartWorkspace, now),
			new FakePolishPreviewRenderer(sessionRoot),
			() => now);
		(EffectsPassPlan effects, string effectsHash) =
			first.CreateEffectsPlan(1, roughCutHash, plan, none);
		AssemblyAction action = new()
		{
			ActionId = "approve-effects-before-restart",
			SessionId = sessionId,
			Checkpoint = 1,
			ExpectedStateRevision = 41,
			Kind = AssemblyActionKind.ApproveEffectsPlan,
			CreatedUtc = now
		};
		AssemblyActionExecutionStore executions =
			new(sessionRoot, () => now);
		executions.Begin(
			action,
			AssemblyPhase.EffectsPlanReview,
			effectsHash,
			effects.Revision);
		first.Decide(
			effects,
			effectsHash,
			PolishApprovalDisposition.Approve,
			"restart-self-test");
		PolishPassArtifactStore artifacts = new(sessionRoot);
		PolishPassStateRecord persisted =
			artifacts.ReadLatestState(PolishPassKind.Effects) ??
			throw new InvalidOperationException(
				"The persisted polish decision state is missing.");
		PolishPassApproval approval =
			artifacts.ReadApproval(PolishPassKind.Effects, effects.Revision) ??
			throw new InvalidOperationException(
				"The persisted polish approval is missing.");
		Assert(persisted.Status == PolishPlanStatus.Approved &&
			!executions.IsComplete(action.ActionId),
			"The crash-window fixture did not persist the decision before journal completion.");

		DateTimeOffset restartedClock = now.AddMinutes(5);
		PolishPassWorkflow restarted = new(
			sessionId,
			sessionRoot,
			new FakePolishAutomation(plan, restartWorkspace, restartedClock),
			new FakePolishPreviewRenderer(sessionRoot),
			() => restartedClock);
		restarted.Decide(
			effects,
			effectsHash,
			PolishApprovalDisposition.Approve,
			"restart-self-test");
		PolishPassStateRecord replayed =
			artifacts.ReadLatestState(PolishPassKind.Effects) ??
			throw new InvalidOperationException(
				"The replayed polish decision state is missing.");
		PolishPassApproval replayedApproval =
			artifacts.ReadApproval(PolishPassKind.Effects, effects.Revision) ??
			throw new InvalidOperationException(
				"The replayed polish approval is missing.");

		Assert(replayed.Sequence == persisted.Sequence &&
			replayed.Status == PolishPlanStatus.Approved &&
			replayedApproval.DecidedUtc == approval.DecidedUtc,
			"Restart replay duplicated or rewrote the persisted polish decision.");
		AssemblyActionExecutionStart recovered = executions.ReadPending(
				sessionId,
				action.Checkpoint,
				action.ExpectedStateRevision,
				AssemblyPhase.EffectsPlanReview) ??
			throw new InvalidOperationException(
				"The begun polish action was not recoverable after restart.");
		executions.Complete(
			recovered.Action,
			"effects-plan-approved",
			$"polish/effects/revisions/{effects.Revision:D4}/approval.json");
		Assert(executions.IsComplete(action.ActionId) &&
			executions.ReadPending(
				sessionId,
				action.Checkpoint,
				action.ExpectedStateRevision,
				AssemblyPhase.EffectsPlanReview) == null,
			"The recovered polish action journal did not complete exactly once.");
	}

	private static void TestNoOpAndPreviewRejection(
		string root,
		EditPlanDocument plan,
		string roughCutHash,
		CandidateWorkspaceId workspace,
		DateTimeOffset now,
		string song)
	{
		string sessionRoot = Path.Combine(root, "polish-no-op");
		Directory.CreateDirectory(sessionRoot);
		CandidateWorkspaceId noOpWorkspace = new()
		{
			SessionId = "polish-no-op",
			Iteration = workspace.Iteration,
			Nonce = workspace.Nonce
		};
		FakePolishAutomation noOpAutomation =
			new(plan, noOpWorkspace, now);
		FakePolishPreviewRenderer preview =
			new(sessionRoot);
		PolishPassWorkflow workflow = new(
			"polish-no-op", sessionRoot, noOpAutomation, preview, () => now);
		PolishRendererCapabilities none = new()
		{
			ScreenPump = false,
			SongTrack = false,
			ReviewedGunHitSfx = false
		};
		(EffectsPassPlan effects, string effectsHash) =
			workflow.CreateEffectsPlan(1, roughCutHash, plan, none);
		Assert(effects.Actions.Count == 0,
			"Capabilities-disabled effects are an explicit no-op revision.");
		workflow.Decide(effects, effectsHash,
			PolishApprovalDisposition.Approve, "self-test");
		PolishPassArtifactStore recoveryStore = new(sessionRoot);
		PolishPassStateRecord approvedState =
			recoveryStore.ReadLatestState(PolishPassKind.Effects)!;
		recoveryStore.AppendState(new PolishPassStateRecord
		{
			SessionId = "polish-no-op",
			Pass = PolishPassKind.Effects,
			PlanId = effects.PlanId,
			PlanRevision = effects.Revision,
			PlanSha256 = effectsHash,
			Sequence = approvedState.Sequence + 1,
			Status = PolishPlanStatus.Materializing,
			Reason = "Simulated process loss after durable Materializing transition.",
			RecordedUtc = now
		});
		PolishPassMaterialization effectResult = workflow.ApplyEffectsAsync(
			effects, effectsHash, noOpWorkspace, plan, CancellationToken.None)
			.GetAwaiter().GetResult();
		workflow.RenderPreviewAsync(PolishPassKind.Effects, 1, effectsHash,
			effects.PlanId, effectResult.Snapshot, CancellationToken.None)
			.GetAwaiter().GetResult();
		workflow.Accept(PolishPassKind.Effects, 1, effectsHash, "self-test");
		Assert(recoveryStore.ReadLatestState(PolishPassKind.Effects)?.Status ==
			PolishPlanStatus.Accepted,
			"An exact-hash Materializing state could not replay idempotent apply.");

		(AudioPassPlan audio, string audioHash) = workflow.CreateAudioPlan(
			1, roughCutHash, effectsHash, song, plan, none);
		Assert(audio.Song == null && audio.Sfx.Count == 0,
			"Capabilities-disabled audio is an explicit no-op revision.");
		workflow.Decide(audio, audioHash, PolishApprovalDisposition.Approve,
			"self-test");
		PolishPassStateRecord approvedAudioState =
			recoveryStore.ReadLatestState(PolishPassKind.Audio)!;
		recoveryStore.AppendState(new PolishPassStateRecord
		{
			SessionId = "polish-no-op",
			Pass = PolishPassKind.Audio,
			PlanId = audio.PlanId,
			PlanRevision = audio.Revision,
			PlanSha256 = audioHash,
			Sequence = approvedAudioState.Sequence + 1,
			Status = PolishPlanStatus.Materializing,
			Reason =
				"Simulated process loss after durable audio Materializing transition.",
			RecordedUtc = now
		});
		PolishPassMaterialization audioResult = workflow.ApplyAudioAsync(
			audio, audioHash, noOpWorkspace, plan, CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(audioResult.FullyApplied && audioResult.Actions.Count == 0,
			"An exact-hash audio Materializing state could not replay " +
			"idempotent apply.");
		workflow.RenderPreviewAsync(PolishPassKind.Audio, 1, audioHash,
			audio.PlanId, audioResult.Snapshot, CancellationToken.None)
			.GetAwaiter().GetResult();
		RejectedPolishPassPreview rejected = workflow.RejectPreview(
			PolishPassKind.Audio, 1, audioHash, "self-test",
			"Do not accept this revision.");
		PolishPassArtifactStore store = new(sessionRoot);
		Assert(rejected.BaselineRestoreRequired &&
			store.ReadLatestState(PolishPassKind.Audio)?.Status ==
				PolishPlanStatus.Rejected,
			"Preview rejection is terminal and requires baseline restoration.");
		PolishBaselineRestoration restorationIntent = new()
		{
			SessionId = "polish-no-op",
			RejectedPass = PolishPassKind.Audio,
			RejectedPlanSha256 = audioHash,
			BaselineRoughCutSha256 = roughCutHash,
			AcceptedEffectsSha256 = effectsHash,
			AttemptId = "restore-self-test",
			Workspace = noOpWorkspace,
			CreatedUtc = now
		};
		store.SaveRestorationIntent(restorationIntent);
		Assert(store.ReadRestorationIntent(
			PolishPassKind.Audio, audioHash)?.Completed == false,
			"Baseline restoration intent is durable before VEGAS mutation.");
		PolishBaselineRestoration restorationReceipt = new()
		{
			SessionId = restorationIntent.SessionId,
			RejectedPass = restorationIntent.RejectedPass,
			RejectedPlanSha256 = restorationIntent.RejectedPlanSha256,
			BaselineRoughCutSha256 = restorationIntent.BaselineRoughCutSha256,
			AcceptedEffectsSha256 = restorationIntent.AcceptedEffectsSha256,
			AttemptId = restorationIntent.AttemptId,
			Workspace = restorationIntent.Workspace,
			Completed = true,
			RestoredSnapshotSha256 = new string('b', 64),
			CreatedUtc = restorationIntent.CreatedUtc,
			CompletedUtc = now.AddSeconds(1)
		};
		store.SaveRestorationReceipt(restorationReceipt);
		Assert(store.ReadRestorationReceipt(
			PolishPassKind.Audio, audioHash)?.Completed == true,
			"Baseline restoration completion evidence is durable and hash-bound.");
		(AudioPassPlan later, _) = workflow.CreateAudioPlan(
			2, roughCutHash, effectsHash, song, plan, none);
		Assert(later.Revision == 2,
			"A rejected pass may be followed by a higher plan revision.");
	}

	private sealed class FakePolishAutomation : IVegasAutomationClient
	{
		private readonly EditPlanDocument plan;
		private readonly CandidateWorkspaceId workspace;
		private readonly DateTimeOffset now;
		public CandidateTimelineSnapshot Snapshot { get; }

		public FakePolishAutomation(EditPlanDocument plan,
			CandidateWorkspaceId workspace, DateTimeOffset now)
		{
			this.plan = plan;
			this.workspace = workspace;
			this.now = now;
			Snapshot = new CandidateTimelineSnapshot
			{
				Workspace = workspace,
				TimelineStart = TimeSpan.FromSeconds(
					plan.Montage.Placements.Min(item => item.TimelineStartSeconds)),
				TimelineEnd = TimeSpan.FromSeconds(
					plan.Montage.Placements.Max(item => item.TimelineEndSeconds))
			};
		}

		public Task<TResult> ExecuteAsync<TRequest, TResult>(
			string operation, TRequest request, string idempotencyKey,
			TimeSpan? timeout = null, CancellationToken cancellationToken = default)
		{
			object result;
			if (operation == VegasOperations.ApplyCandidateEffects)
			{
				ApplyCandidateEffectsRequest value =
					(ApplyCandidateEffectsRequest)(object)request!;
				result = new ApplyCandidateEffectsResult
				{
					Materialization = Materialization(
						PolishPassKind.Effects, value.Effects.SessionId,
						value.Effects.PlanId, value.Effects.Revision,
						value.PlanSha256, value.Effects.Actions.Select(item =>
							item.ActionId))
				};
			}
			else if (operation == VegasOperations.ApplyCandidateAudio)
			{
				ApplyCandidateAudioRequest value =
					(ApplyCandidateAudioRequest)(object)request!;
				IEnumerable<string> ids = value.Audio.Sfx.Select(item => item.ActionId);
				if (value.Audio.Song != null) ids = new[] { value.Audio.Song.ActionId }
					.Concat(ids);
				result = new ApplyCandidateAudioResult
				{
					Materialization = Materialization(
						PolishPassKind.Audio, value.Audio.SessionId,
						value.Audio.PlanId, value.Audio.Revision,
						value.PlanSha256, ids)
				};
			}
			else throw new InvalidOperationException("Unexpected operation " + operation);
			return Task.FromResult((TResult)result);
		}

		private PolishPassMaterialization Materialization(PolishPassKind pass,
			string sessionId, string planId, int revision, string hash,
			IEnumerable<string> ids) => new()
		{
			SessionId = sessionId,
			Pass = pass,
			PlanId = planId,
			PlanRevision = revision,
			PlanSha256 = hash,
			Workspace = workspace,
			CompletedUtc = now,
			FullyApplied = true,
			Actions = ids.Select(id => new PolishActionResult
			{
				ActionId = id,
				Outcome = PolishActionOutcome.Applied,
				Detail = "fake applied"
			}).ToList(),
			Snapshot = Snapshot
		};
	}

	private sealed class FakePolishPreviewRenderer : IPolishPreviewRenderer
	{
		private readonly string sessionRoot;

		public FakePolishPreviewRenderer(string sessionRoot)
		{
			this.sessionRoot = sessionRoot;
		}

		public string RenderProfileId => "review-1080p";
		public int RenderCalls { get; private set; }

		public Task<RenderCandidatePreviewResult> RenderAsync(
			CandidateWorkspaceId workspace, TimeSpan start, TimeSpan duration,
			string outputRelativePath, string idempotencyKey,
			CancellationToken cancellationToken)
		{
			RenderCalls++;
			string path = Path.Combine(
				sessionRoot,
				outputRelativePath.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(
				path,
				$"render {RenderCalls}: {start:c} + {duration:c}; {idempotencyKey}");
			return Task.FromResult(new RenderCandidatePreviewResult
			{
				OutputRelativePath = outputRelativePath,
				RenderProfileId = "review-1080p",
				RenderedDuration = duration,
				Sha256 = new SessionArtifactHasher().ComputeSha256(path)
			});
		}
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(
			"Polish-pass self-test failed: " + message);
	}
	private static void AssertThrows<T>(Action action, string message)
		where T : Exception
	{
		try { action(); }
		catch (T) { return; }
		throw new InvalidOperationException("Polish-pass self-test failed: " + message);
	}
}
