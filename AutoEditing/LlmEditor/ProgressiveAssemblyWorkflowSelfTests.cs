using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.LlmEditor.Assembly;
using AutoEditing.LlmEditor.Automation;
using AutoEditing.LlmEditor.Planning;
using AutoEditing.LlmEditor.Workbench;
using Core.Domain.Editing;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor;

internal static class ProgressiveAssemblyWorkflowSelfTests
{
	public static void Run(
		string testRoot,
		EditPlanningRequest sourceRequest,
		EditPlanDocument sourcePlan)
	{
		(EditPlanningRequest request, AssemblySketch sketch) =
			CreateFixture(sourceRequest, sourcePlan);
		TestContextFactory(request, sketch);
		TestArtifactRevisions(testRoot, request, sketch);
		TestAutomaticProposalRepair(testRoot, request, sketch);
		TestProgressiveCoordinator(testRoot, request, sketch);
		TestSectionMilestoneHandoff(testRoot, request, sketch);
		TestEarlySyncCompletion(testRoot, request, sketch);
		TestInterruptedRevisionRecovery(testRoot, request, sketch);
		TestInterruptedResetRecovery(testRoot, request, sketch);
	}

	private static void TestContextFactory(
		EditPlanningRequest request,
		AssemblySketch sketch)
	{
		ProgressiveAssemblyPlanningContext first =
			ProgressiveAssemblyContextFactory.Create(
				request, sketch, null, 1);
		Assert(first.StepIndex == 1 &&
			first.AcceptedPrefix.Count == 0 &&
			first.RemainingClips.Count == 2 &&
			first.NearbySongContext.Select(item => item.EventId)
				.SequenceEqual(new[] { "event-accent-1", "event-accent-2" }),
			"The initial progressive context did not expose only remaining clips and " +
			"eligible song events.");

		EditPlanDocument accepted = new ClipStepDecisionCompiler()
			.Append(request, null, Decision(
				request, 1, request.Clips[0].FilePath, "event-accent-1"))
			.CombinedPlan;
		TimelineAdjustmentDelta adjustment = new()
		{
			Checkpoint = 2,
			Changes = new List<TimelineAdjustment>
			{
				new()
				{
					Kind = TimelineAdjustmentKind.SourceTrimChanged,
					ClipPath = request.Clips[0].FilePath,
					Before = 0,
					After = 0.1
				}
			}
		};
		ProgressiveAssemblyPlanningContext second =
			ProgressiveAssemblyContextFactory.Create(
				request,
				sketch,
				accepted,
				2,
				adjustment,
				"Use the stronger remaining sequence.");
		Assert(second.StepIndex == 2 &&
			second.AcceptedPrefix.Count == 1 &&
			second.RemainingClips.Count == 1 &&
			second.RemainingClips[0].MediaPath == request.Clips[1].FilePath,
			"The next progressive context did not compact the accepted prefix and " +
			"remove its clip from the available set.");
		Assert(second.NearbySongContext.Count == 1 &&
			second.NearbySongContext[0].EventId == "event-accent-2",
			"The next progressive context offered a song event already consumed by " +
			"the accepted prefix.");
		Assert(ReferenceEquals(second.TimelineAdjustment, adjustment) &&
			second.ScopedInstruction == "Use the stronger remaining sequence.",
			"The progressive context lost its structured adjustment or scoped instruction.");

		ExpectFailure(
			() => ProgressiveAssemblyContextFactory.Create(
				request, sketch, accepted, 3),
			"A progressive context skipped over the immediate next step.");
	}

	private static void TestArtifactRevisions(
		string testRoot,
		EditPlanningRequest request,
		AssemblySketch sketch)
	{
		string sessionRoot = Path.Combine(testRoot, "progressive-artifacts");
		Directory.CreateDirectory(sessionRoot);
		AssemblyArtifactStore store = new(sessionRoot);
		store.SaveSketch(sketch, 1);
		AssemblySketch revisedSketch = Clone(sketch);
		revisedSketch.EditorialThesis = "Revised deterministic thesis.";
		store.SaveSketch(revisedSketch, 2);
		Assert(store.ReadCurrentSketch()?.EditorialThesis ==
			"Revised deterministic thesis.",
			"The sketch current pointer did not publish the latest valid revision.");
		Assert(File.Exists(Path.Combine(
				sessionRoot, "assembly", "sketch", "revisions", "0001.json")) &&
			File.Exists(Path.Combine(
				sessionRoot, "assembly", "sketch", "revisions", "0002.json")),
			"Immutable sketch revision artifacts were not retained.");

		ClipStepDecision first = Decision(
			request, 1, request.Clips[0].FilePath, "event-accent-1");
		store.SaveProposal(first, 1);
		ClipStepDecision revised = Clone(first);
		revised.SourceWindow.StartSeconds = 0.1;
		revised.SourceWindow.EndSeconds = 2.1;
		revised.Rationale = "Revised after timeline evidence.";
		store.SaveProposal(revised, 2);
		Assert(store.ReadCurrentProposal(1)?.Rationale ==
			"Revised after timeline evidence.",
			"The proposal current pointer did not publish the latest valid revision.");
		Assert(File.Exists(Path.Combine(
				sessionRoot, "assembly", "checkpoints", "0001",
				"proposals", "0001.json")) &&
			File.Exists(Path.Combine(
				sessionRoot, "assembly", "checkpoints", "0001",
				"proposals", "0002.json")),
			"Immutable proposal revision artifacts were not retained.");
		AssemblyProposalRejection rejection = store.SaveProposalRejection(
			1,
			1,
			1,
			3,
			"Deterministic invalid sync.");
		Assert(
			rejection.ProposalRelativePath.EndsWith(
				"assembly/checkpoints/0001/proposals/0001.json",
				StringComparison.Ordinal) &&
			File.Exists(Path.Combine(
				sessionRoot, "assembly", "checkpoints", "0001",
				"rejections", "0001.json")),
			"A rejected proposal was not durably linked to its immutable revision.");

		EditPlanDocument accepted = new ClipStepDecisionCompiler()
			.Append(request, null, revised)
			.CombinedPlan;
		store.SaveAcceptedPlan(1, accepted);
		EditPlanDocument? recovered = store.ReadLatestAcceptedPlan(1);
		Assert(recovered?.Montage.Placements.Count == 1 &&
			Math.Abs(recovered.Montage.Placements[0].SourceOffsetSeconds - 0.1) <
				0.000001,
			"The accepted progressive prefix could not be recovered from its checkpoint.");
	}

	private static void TestAutomaticProposalRepair(
		string testRoot,
		EditPlanningRequest request,
		AssemblySketch sketch)
	{
		const string sessionId = "progressive-automatic-repair";
		string sessionsRoot = Path.Combine(testRoot, sessionId);
		WorkbenchSessionPublisher publisher =
			WorkbenchSessionPublisher.Create(sessionsRoot, sessionId);
		publisher.TransitionTo(
			EditSessionState.Planning,
			"automatic proposal repair self-test");
		RepairingProgressivePlanner planner = new(request);
		ProgressiveAutomation automation = new(
			new Action<CandidateTimelineSnapshot>[] { _ => { }, _ => { } });
		using ProgressiveActionDriver driver = new(
			publisher.SessionRoot,
			sessionId,
			new[]
			{
				new ScriptedProgressiveAction(
					1,
					AssemblyActionKind.FinishSyncPass)
			});

		EditPlanDocument result = new AssemblyCoordinator(
				planner,
				automation,
				publisher,
				sessionId)
			.RunProgressiveAsync(
				request,
				Clone(sketch),
				CancellationToken.None)
			.GetAwaiter().GetResult();

		string checkpointRoot = Path.Combine(
			publisher.SessionRoot, "assembly", "checkpoints", "0001");
		Assert(
			planner.RevisionContexts.Count == 1 &&
			planner.RevisionContexts[0].ScopedInstruction.Contains(
				"AUTOMATIC PRE-MATERIALIZATION REPAIR",
				StringComparison.Ordinal) &&
			planner.RevisionContexts[0].ScopedInstruction.Contains(
				"lies outside the selected source window",
				StringComparison.OrdinalIgnoreCase),
			"The invalid proposal did not receive one precise deterministic repair prompt.");
		Assert(
			File.Exists(Path.Combine(
				checkpointRoot, "proposals", "0001.json")) &&
			File.Exists(Path.Combine(
				checkpointRoot, "proposals", "0002.json")) &&
			File.Exists(Path.Combine(
				checkpointRoot, "rejections", "0001.json")),
			"The rejected attempt, diagnostic, and repaired proposal were not all durable.");
		Assert(
			automation.MaterializedPlacementCounts.SequenceEqual(new[] { 1 }) &&
			result.Montage.Placements.Count == 1,
			"The coordinator materialized an invalid proposal or failed to materialize " +
			"the repaired proposal exactly once.");
	}

	private static void TestProgressiveCoordinator(
		string testRoot,
		EditPlanningRequest request,
		AssemblySketch sketch)
	{
		string sessionsRoot = Path.Combine(testRoot, "progressive-coordinator");
		WorkbenchSessionPublisher publisher =
			WorkbenchSessionPublisher.Create(sessionsRoot, "progressive-run");
		publisher.TransitionTo(
			EditSessionState.Planning,
			"progressive assembly self-test");
		CapturingProgressivePlanner planner = new(request);
		CapturingCheckpointPreviewPipeline previews = new();
		ProgressiveAutomation automation = new(
			new Action<CandidateTimelineSnapshot>[]
			{
				_ => { },
				_ => { },
				_ => { },
				snapshot =>
					snapshot.Tracks[0].Events[0].SourceOffset +=
						TimeSpan.FromSeconds(0.1),
				_ => { },
				snapshot =>
					snapshot.Tracks[0].Events[0].TimelineStart +=
						TimeSpan.FromSeconds(0.1),
				_ => { },
				_ => { }
			});
		using ProgressiveActionDriver driver = new(
			publisher.SessionRoot,
			"progressive-run",
			new[]
			{
				new ScriptedProgressiveAction(
					1,
					AssemblyActionKind.CompareCurrentTimeline),
				new ScriptedProgressiveAction(
					1,
					AssemblyActionKind.RenderCheckpointPreview),
				new ScriptedProgressiveAction(
					1,
					AssemblyActionKind.ReviseCurrentClip,
					"Respect my tighter source trim."),
				new ScriptedProgressiveAction(
					1,
					AssemblyActionKind.AcceptTimelineAndContinue,
					"Use a stronger second sequence."),
				new ScriptedProgressiveAction(
					2,
					AssemblyActionKind.FinishSyncPass)
			});

		EditPlanDocument result = new AssemblyCoordinator(
				planner, automation, publisher, "progressive-run", previews)
			.RunProgressiveAsync(request, sketch, CancellationToken.None)
			.GetAwaiter().GetResult();

		Assert(planner.CreateSketchCalls == 0,
			"The coordinator regenerated a persisted sketch instead of executing it.");
		Assert(previews.Calls == 1 &&
			previews.Checkpoints.SequenceEqual(new[] { 1 }),
			"The explicit checkpoint-preview action was not handled exactly once " +
			"without advancing the assembly. Calls=" + previews.Calls +
			", submitted=" + string.Join(",", driver.SubmittedActionKinds) + ".");
		Assert(driver.SubmittedActionKinds.Count(kind =>
				kind == AssemblyActionKind.CompareCurrentTimeline) == 1,
			"A completed read-only comparison blocked the next action for the checkpoint.");
		Assert(planner.PlanContexts.Count == 2 &&
			planner.PlanContexts[0].StepIndex == 1 &&
			planner.PlanContexts[0].AcceptedPrefix.Count == 0 &&
			planner.PlanContexts[1].StepIndex == 2 &&
			planner.PlanContexts[1].AcceptedPrefix.Count == 1,
			"The progressive planner was not asked for exactly one step at a time.");
		Assert(planner.RevisionContexts.Count == 1 &&
			planner.RevisionContexts[0].StepIndex == 1 &&
			planner.RevisionContexts[0].TimelineAdjustment?.Changes.Any(change =>
				change.Kind == TimelineAdjustmentKind.SourceTrimChanged &&
				Math.Abs(change.After - change.Before - 0.1) < 0.000001) == true,
			"The current-step revision did not receive the actual VEGAS timeline delta.");
		Assert(planner.RevisionContexts[0].ScopedInstruction ==
			"Respect my tighter source trim.",
			"The current-step revision lost its human instruction.");
		Assert(planner.PlanContexts[1].ScopedInstruction ==
			"[next clip] Use a stronger second sequence.",
			"The accepted checkpoint instruction was not scoped to the next step.");

		AcceptedClipPlacementSummary accepted =
			planner.PlanContexts[1].AcceptedPrefix.Single();
		Assert(accepted.WasHumanAdjusted &&
			Math.Abs(accepted.TimelineStartSeconds - 0.7) < 0.000001,
			"The next planning step did not receive the human-authoritative accepted prefix.");
		Assert(result.Montage.Placements.Count == 2 &&
			Math.Abs(result.Montage.Placements[0].TimelineStartSeconds - 0.7) <
				0.000001,
			"The final progressive montage lost the accepted actual first placement.");
		Assert(automation.MaterializedPlacementCounts.SequenceEqual(new[] { 1, 1, 2 }),
			"The progressive workflow did not materialize only the current growing prefix.");
		Assert(
			automation.MaterializeRequests.All(item =>
				item.IncludeSfx && !item.ApplyEffects),
			"Synchronization checkpoints did not include deterministic gunshot SFX " +
			"while keeping creative effects disabled.");

		string assembly = Path.Combine(publisher.SessionRoot, "assembly");
		Assert(File.Exists(Path.Combine(
				assembly, "sketch", "revisions", "0001.json")) &&
			File.Exists(Path.Combine(
				assembly, "checkpoints", "0001", "proposals", "0001.json")) &&
			File.Exists(Path.Combine(
				assembly, "checkpoints", "0001", "proposals", "0002.json")) &&
			File.Exists(Path.Combine(
				assembly, "checkpoints", "0001", "accepted-plan.json")) &&
			File.Exists(Path.Combine(
				assembly, "checkpoints", "0002", "proposals", "0001.json")) &&
			File.Exists(Path.Combine(
				assembly, "checkpoints", "0002", "accepted-plan.json")),
			"The progressive coordinator did not persist its sketch, proposal revisions, " +
			"and accepted checkpoint plans.");
	}

	private static void TestSectionMilestoneHandoff(
		string testRoot,
		EditPlanningRequest request,
		AssemblySketch sourceSketch)
	{
		const string sessionId = "progressive-section-handoff";
		string sessionsRoot = Path.Combine(
			testRoot,
			"progressive-section-handoff-sessions");
		WorkbenchSessionPublisher publisher =
			WorkbenchSessionPublisher.Create(sessionsRoot, sessionId);
		publisher.TransitionTo(
			EditSessionState.Planning,
			"section milestone handoff self-test");
		AssemblySketch sketch = Clone(sourceSketch);
		AssemblySectionIntent secondSection = Clone(sketch.Sections.Single());
		secondSection.SectionId = "section-payoff";
		secondSection.EditorialRole = "payoff";
		sketch.Sections.Add(secondSection);
		sketch.ClipOrder[0].SectionId = sketch.Sections[0].SectionId;
		sketch.ClipOrder[1].SectionId = secondSection.SectionId;
		ProgressiveAssemblyContractValidator.Validate(sketch);

		CapturingProgressivePlanner planner = new(request);
		ProgressiveAutomation automation = new(
			Array.Empty<Action<CandidateTimelineSnapshot>>());
		CapturingSectionMilestoneRenderer milestones = new(sessionId);
		using ProgressiveActionDriver driver = new(
			publisher.SessionRoot,
			sessionId,
			new[]
			{
				new ScriptedProgressiveAction(
					1,
					AssemblyActionKind.AcceptTimelineAndContinue),
				new ScriptedProgressiveAction(
					2,
					AssemblyActionKind.FinishSyncPass)
			});

		EditPlanDocument result = new AssemblyCoordinator(
				planner,
				automation,
				publisher,
				sessionId,
				checkpointPreviews: null,
				sectionMilestones: milestones)
			.RunProgressiveAsync(
				request,
				sketch,
				CancellationToken.None)
			.GetAwaiter().GetResult();

		Assert(
			result.Montage.Placements.Count == 2 &&
			planner.PlanContexts.Select(context => context.StepIndex)
				.SequenceEqual(new[] { 1, 2 }),
			"A completed first-section render prevented planning the next clip.");
		Assert(
			milestones.CompletedCheckpoints.SequenceEqual(new[] { 1, 2 }) &&
			publisher.State == EditSessionState.Rendering,
			"Section milestones did not return through review before the next " +
			"checkpoint or finish in rough-cut rendering state.");
	}

	private static void TestInterruptedRevisionRecovery(
		string testRoot,
		EditPlanningRequest request,
		AssemblySketch sketch)
	{
		const string sessionId = "progressive-revise-recovery";
		string sessionsRoot = Path.Combine(
			testRoot,
			"progressive-revise-recovery-sessions");
		WorkbenchSessionPublisher publisher =
			WorkbenchSessionPublisher.Create(sessionsRoot, sessionId);
		publisher.TransitionTo(
			EditSessionState.Planning,
			"interrupted revision recovery self-test");
		CapturingProgressivePlanner planner = new(request);
		ProgressiveAutomation automation = new(
			Enumerable.Repeat<Action<CandidateTimelineSnapshot>>(
				_ => { },
				32))
		{
			FailMaterializeCall = 2
		};
		using (ProgressiveActionDriver firstAction = new(
			publisher.SessionRoot,
			sessionId,
			new[]
			{
				new ScriptedProgressiveAction(
					1,
					AssemblyActionKind.ReviseCurrentClip,
					"Persist this revision once.")
			}))
		{
			ExpectFailureContaining(
				() => new AssemblyCoordinator(
						planner,
						automation,
						publisher,
						sessionId)
					.RunProgressiveAsync(
						request,
						sketch,
						CancellationToken.None)
					.GetAwaiter().GetResult(),
				"injected materialization interruption",
				"The revision interruption was not injected after its proposal was saved.");
		}

		Assert(planner.RevisionContexts.Count == 1,
			"The interrupted revision did not make exactly one planner call.");
		AssemblyActionExecutionStart pending =
			new AssemblyActionExecutionStore(publisher.SessionRoot)
				.ReadPendingForRecovery(sessionId) ??
			throw new InvalidOperationException(
				"The interrupted revision action was not durably pending.");
		Assert(pending.Action.Kind == AssemblyActionKind.ReviseCurrentClip &&
			new AssemblyArtifactStore(publisher.SessionRoot)
				.GetLatestProposalRevision(1) == 2,
			"The revised proposal and its exact pending action were not both durable.");

		AssemblyRecoveryInspection inspection = new AssemblyRecoveryService()
			.Inspect(sessionsRoot, sessionId);
		Assert(inspection.ResumePlan != null &&
			inspection.Summary.Disposition ==
				AssemblyRecoveryDisposition.ReadyToResume,
			"A saved revision awaiting rematerialization was rejected by recovery.");
		WorkbenchSessionPublisher resumedPublisher =
			WorkbenchSessionPublisher.Open(sessionsRoot, sessionId);
		using ProgressiveActionDriver remainingActions = new(
			resumedPublisher.SessionRoot,
			sessionId,
			new[]
			{
				new ScriptedProgressiveAction(
					1,
					AssemblyActionKind.AcceptTimelineAndContinue),
				new ScriptedProgressiveAction(
					2,
					AssemblyActionKind.FinishSyncPass)
			});
		new AssemblyCoordinator(
				planner,
				automation,
				resumedPublisher,
				sessionId)
			.ResumeProgressiveAsync(
				inspection.ResumePlan!,
				CancellationToken.None)
			.GetAwaiter().GetResult();

		Assert(planner.RevisionContexts.Count == 1,
			"Recovery duplicated the LLM revision call after its proposal was durable.");
		Assert(new AssemblyActionExecutionStore(resumedPublisher.SessionRoot)
				.ReadPendingForRecovery(sessionId) == null,
			"The recovered revision action was not durably completed.");
		string reviseScope = "revise-" + pending.Action.ActionId;
		string[] reviseKeys = automation.MaterializeCalls
			.Where(call => call.Key.Contains(reviseScope, StringComparison.Ordinal))
			.Select(call => call.Key)
			.ToArray();
		Assert(reviseKeys.Length == 2 &&
			reviseKeys.Distinct(StringComparer.Ordinal).Count() == 1,
			"The interrupted revision did not reuse its stable materialization key.");
	}

	private static void TestEarlySyncCompletion(
		string testRoot,
		EditPlanningRequest request,
		AssemblySketch sketch)
	{
		const string sessionId = "progressive-early-finish";
		string sessionsRoot = Path.Combine(
			testRoot,
			"progressive-early-finish-sessions");
		WorkbenchSessionPublisher publisher =
			WorkbenchSessionPublisher.Create(sessionsRoot, sessionId);
		publisher.TransitionTo(
			EditSessionState.Planning,
			"early synchronization completion self-test");
		CapturingProgressivePlanner planner = new(request);
		ProgressiveAutomation automation = new(
			new Action<CandidateTimelineSnapshot>[] { _ => { }, _ => { } });
		using ProgressiveActionDriver driver = new(
			publisher.SessionRoot,
			sessionId,
			new[]
			{
				new ScriptedProgressiveAction(
					1,
					AssemblyActionKind.FinishSyncPass)
			});

		EditPlanDocument result = new AssemblyCoordinator(
				planner,
				automation,
				publisher,
				sessionId)
			.RunProgressiveAsync(
				request,
				Clone(sketch),
				CancellationToken.None)
			.GetAwaiter().GetResult();

		AssemblySessionState completed =
			new AssemblyActionStore(publisher.SessionRoot).ReadState()
			?? throw new InvalidDataException(
				"The early-completion assembly state is missing.");
		Assert(
			result.Montage.Placements.Count == 1 &&
			planner.PlanContexts.Count == 1 &&
			!File.Exists(Path.Combine(
				publisher.SessionRoot,
				"assembly",
				"checkpoints",
				"0002",
				"proposal.json")),
			"Early synchronization completion planned or materialized an unused clip.");
		Assert(
			completed.Phase == AssemblyPhase.SyncPassComplete &&
			completed.Checkpoint == 1 &&
			completed.TotalClips == 2 &&
			completed.RemainingClipPaths.SequenceEqual(
				new[] { request.Clips[1].FilePath }) &&
			completed.Status.Contains(
				"finished early",
				StringComparison.OrdinalIgnoreCase) &&
			completed.Status.Contains(
				Path.GetFileName(request.Clips[1].FilePath),
				StringComparison.OrdinalIgnoreCase),
			"Early completion did not preserve and explain the unused selected media.");
		Assert(
			publisher.State == EditSessionState.Rendering,
			"Early completion did not advance into the mandatory rough-cut audit.");
	}

	private static void TestInterruptedResetRecovery(
		string testRoot,
		EditPlanningRequest request,
		AssemblySketch sketch)
	{
		const string sessionId = "progressive-reset-recovery";
		string sessionsRoot = Path.Combine(
			testRoot,
			"progressive-reset-recovery-sessions");
		WorkbenchSessionPublisher publisher =
			WorkbenchSessionPublisher.Create(sessionsRoot, sessionId);
		publisher.TransitionTo(
			EditSessionState.Planning,
			"interrupted reset recovery self-test");
		CapturingProgressivePlanner planner = new(request);
		ProgressiveAutomation automation = new(
			Enumerable.Repeat<Action<CandidateTimelineSnapshot>>(
				_ => { },
				32))
		{
			FailMaterializeCall = 2
		};
		using (ProgressiveActionDriver firstAction = new(
			publisher.SessionRoot,
			sessionId,
			new[]
			{
				new ScriptedProgressiveAction(
					1,
					AssemblyActionKind.ResetCurrentClip)
			}))
		{
			ExpectFailureContaining(
				() => new AssemblyCoordinator(
						planner,
						automation,
						publisher,
						sessionId)
					.RunProgressiveAsync(
						request,
						sketch,
						CancellationToken.None)
					.GetAwaiter().GetResult(),
				"injected materialization interruption",
				"The reset interruption was not injected after cleanup.");
		}
		AssemblyActionExecutionStart pending =
			new AssemblyActionExecutionStore(publisher.SessionRoot)
				.ReadPendingForRecovery(sessionId) ??
			throw new InvalidOperationException(
				"The interrupted reset action was not durably pending.");
		Assert(pending.Action.Kind == AssemblyActionKind.ResetCurrentClip,
			"The reset crash left the wrong action pending.");

		AssemblyRecoveryInspection inspection = new AssemblyRecoveryService()
			.Inspect(sessionsRoot, sessionId);
		Assert(inspection.ResumePlan != null,
			"The interrupted reset was not recoverable.");
		WorkbenchSessionPublisher resumedPublisher =
			WorkbenchSessionPublisher.Open(sessionsRoot, sessionId);
		using ProgressiveActionDriver remainingActions = new(
			resumedPublisher.SessionRoot,
			sessionId,
			new[]
			{
				new ScriptedProgressiveAction(
					1,
					AssemblyActionKind.AcceptTimelineAndContinue),
				new ScriptedProgressiveAction(
					2,
					AssemblyActionKind.FinishSyncPass)
			});
		new AssemblyCoordinator(
				planner,
				automation,
				resumedPublisher,
				sessionId)
			.ResumeProgressiveAsync(
				inspection.ResumePlan!,
				CancellationToken.None)
			.GetAwaiter().GetResult();

		string resetScope = "reset-" + pending.Action.ActionId;
		string[] resetKeys = automation.MaterializeCalls
			.Where(call => call.Key.Contains(resetScope, StringComparison.Ordinal))
			.Select(call => call.Key)
			.ToArray();
		Assert(resetKeys.Length == 2 &&
			resetKeys.Distinct(StringComparer.Ordinal).Count() == 1,
			"Reset recovery did not reuse the interrupted operation key.");
		Assert(automation.MaterializeCalls
			.Where(call => !call.Key.Contains(resetScope, StringComparison.Ordinal))
			.All(call => !resetKeys.Contains(call.Key, StringComparer.Ordinal)),
			"A reset reused a proposal or recovery materialization key.");
	}

	private static (EditPlanningRequest Request, AssemblySketch Sketch) CreateFixture(
		EditPlanningRequest sourceRequest,
		EditPlanDocument sourcePlan)
	{
		EditPlanningRequest request = EditPlanDocumentSerializer.DeserializeRequest(
			EditPlanDocumentSerializer.SerializeRequest(sourceRequest));
		EditPlanDocument secondSource = EditPlanDocumentSerializer.DeserializePlan(
			EditPlanDocumentSerializer.SerializePlan(sourcePlan));
		Core.Domain.Clip.Clip secondClip =
			secondSource.Montage.Placements.Single().Clip;
		secondClip.FilePath = "fixtures/clip-002.mp4";
		request.Clips.Add(secondClip);
		MontageSongPlanningEvent firstEvent = request.SongAnalysis.Events.Single();
		request.SongAnalysis.Events.Add(new MontageSongPlanningEvent
		{
			Id = "event-accent-2",
			SourceTimeSeconds = 4,
			EffectiveTimeSeconds = 4,
			ContainingRegionId = firstEvent.ContainingRegionId,
			MusicalType = firstEvent.MusicalType,
			Classification = firstEvent.Classification,
			Uses = firstEvent.Uses.ToList(),
			Priority = firstEvent.Priority,
			IsLocked = true,
			Intensity = firstEvent.Intensity,
			IsSuggestedGameplayAnchor = false,
			IsReviewed = true
		});
		request.SongAnalysis.EventTimeline.Add(new List<object>
		{
			4.0, "Accent", 0.8, 0.95, "Reviewed"
		});
		EditPlanningRequestValidator.ValidateAndNormalize(request);

		AssemblySectionIntent section = new()
		{
			SectionId = "section-main",
			RegionId = request.SongAnalysis.Regions.Single().Id,
			EditorialRole = "build",
			EnergyDirection = "rising",
			PacingIntent = "two readable synchronized sequences",
			Rationale = "Deterministic progressive workflow fixture."
		};
		AssemblySketch sketch = new()
		{
			RequestId = request.RequestId,
			EditorialThesis = "Build from one clean sequence into a stronger second clip.",
			Sections = new List<AssemblySectionIntent> { section },
			ClipOrder = request.Clips.Select((clip, index) =>
				new AssemblyClipIntent
				{
					Order = index + 1,
					Clip = new AssemblyClipReference
					{
						ReferenceId = AssemblyReferenceIds.ForClipPath(clip.FilePath),
						MediaPath = clip.FilePath
					},
					SectionId = section.SectionId,
					EditorialRole = index == 0 ? "setup" : "payoff",
					Rationale = "Deterministic clip order.",
					Confidence = 0.9
				}).ToList(),
			SyncStrategy = new AssemblySyncStrategy
			{
				Density = "readable",
				PreferredMusicalTypes = new List<string> { "Accent" },
				Rationale = "Use one primary gameplay anchor per clip."
			}
		};
		ProgressiveAssemblyContractValidator.Validate(sketch);
		return (request, sketch);
	}

	private static ClipStepDecision Decision(
		EditPlanningRequest request,
		int step,
		string clipPath,
		string eventId,
		double sourceStart = 0,
		double sourceEnd = 2) => new()
	{
		RequestId = request.RequestId,
		StepIndex = step,
		Clip = new AssemblyClipReference
		{
			ReferenceId = AssemblyReferenceIds.ForClipPath(clipPath),
			MediaPath = clipPath
		},
		SourceWindow = new AssemblySourceWindow
		{
			StartSeconds = sourceStart,
			EndSeconds = sourceEnd,
			ConstantSpeed = 1
		},
		PrimarySync = new AssemblySyncDecision
		{
			MusicEventId = eventId,
			KillIndex = 0
		},
		Rationale = "One deterministic clip-step decision.",
		Confidence = 0.9
	};

	private static CandidateTimelineSnapshot SnapshotFor(
		EditPlanDocument plan,
		CandidateWorkspaceId workspace)
	{
		return new CandidateTimelineSnapshot
		{
			Workspace = workspace,
			Tracks = new List<CandidateTrackSnapshot>
			{
				new()
				{
					MediaKind = "Video",
					Events = plan.Montage.Placements.Select(placement =>
						new CandidateEventSnapshot
						{
							PlacementId = Path.GetFullPath(placement.Clip.FilePath),
							MediaPath = placement.Clip.FilePath,
							TimelineStart =
								TimeSpan.FromSeconds(placement.TimelineStartSeconds),
							TimelineDuration =
								TimeSpan.FromSeconds(placement.LengthSeconds),
							SourceOffset =
								TimeSpan.FromSeconds(placement.SourceOffsetSeconds)
						}).ToList()
				}
			}
		};
	}

	private static T Clone<T>(T value) =>
		ContractSerializer.Deserialize<T>(ContractSerializer.Serialize(value!));

	private static void ExpectFailure(Action action, string message)
	{
		try { action(); }
		catch { return; }
		throw new InvalidOperationException(message);
	}

	private static void ExpectFailureContaining(
		Action action,
		string expected,
		string message)
	{
		try { action(); }
		catch (Exception exception)
		{
			if (exception.ToString().Contains(
				expected,
				StringComparison.OrdinalIgnoreCase))
				return;
			throw new InvalidOperationException(
				message + " Unexpected failure: " + exception.Message,
				exception);
		}
		throw new InvalidOperationException(message);
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private sealed class CapturingProgressivePlanner : IProgressiveAssemblyPlanner
	{
		private readonly EditPlanningRequest request;

		public CapturingProgressivePlanner(EditPlanningRequest request)
		{
			this.request = request;
		}

		public int CreateSketchCalls { get; private set; }

		public List<ProgressiveAssemblyPlanningContext> PlanContexts { get; } = new();

		public List<ProgressiveAssemblyPlanningContext> RevisionContexts { get; } = new();

		public Task<AssemblySketch> CreateSketchAsync(
			EditPlanningRequest planningRequest,
			CancellationToken cancellationToken)
		{
			CreateSketchCalls++;
			throw new InvalidOperationException(
				"The coordinator should execute the supplied sketch.");
		}

		public Task<ClipStepDecision> PlanClipAsync(
			EditPlanningRequest planningRequest,
			ProgressiveAssemblyPlanningContext context,
			CancellationToken cancellationToken)
		{
			PlanContexts.Add(Clone(context));
			return Task.FromResult(context.StepIndex switch
			{
				1 => Decision(
					request, 1, request.Clips[0].FilePath, "event-accent-1"),
				2 => Decision(
					request, 2, request.Clips[1].FilePath, "event-accent-2"),
				_ => throw new InvalidOperationException(
					"Unexpected progressive step " + context.StepIndex)
			});
		}

		public Task<ClipStepDecision> ReviseClipAsync(
			EditPlanningRequest planningRequest,
			ProgressiveAssemblyPlanningContext context,
			ClipStepDecision previousDecision,
			CancellationToken cancellationToken)
		{
			RevisionContexts.Add(Clone(context));
			return Task.FromResult(Decision(
				request,
				1,
				request.Clips[0].FilePath,
				"event-accent-1",
				0.1,
				2.1));
		}
	}

	private sealed class RepairingProgressivePlanner : IProgressiveAssemblyPlanner
	{
		private readonly EditPlanningRequest request;

		public RepairingProgressivePlanner(EditPlanningRequest request)
		{
			this.request = request;
		}

		public List<ProgressiveAssemblyPlanningContext> RevisionContexts { get; } =
			new();

		public Task<AssemblySketch> CreateSketchAsync(
			EditPlanningRequest planningRequest,
			CancellationToken cancellationToken) =>
			throw new InvalidOperationException(
				"The coordinator should execute the supplied sketch.");

		public Task<ClipStepDecision> PlanClipAsync(
			EditPlanningRequest planningRequest,
			ProgressiveAssemblyPlanningContext context,
			CancellationToken cancellationToken) =>
			Task.FromResult(Decision(
				request,
				1,
				request.Clips[0].FilePath,
				"event-accent-1",
				0,
				0.001));

		public Task<ClipStepDecision> ReviseClipAsync(
			EditPlanningRequest planningRequest,
			ProgressiveAssemblyPlanningContext context,
			ClipStepDecision previousDecision,
			CancellationToken cancellationToken)
		{
			RevisionContexts.Add(Clone(context));
			return Task.FromResult(Decision(
				request,
				1,
				request.Clips[0].FilePath,
				"event-accent-1"));
		}
	}

	private sealed class ProgressiveAutomation : IVegasAutomationClient
	{
		private readonly Queue<Action<CandidateTimelineSnapshot>> adjustments;
		private EditPlanDocument? materialized;
		private CandidateWorkspaceId? workspace;
		private int materializeCallCount;
		private bool materializeFailureInjected;

		public ProgressiveAutomation(
			IEnumerable<Action<CandidateTimelineSnapshot>> adjustments)
		{
			this.adjustments = new Queue<Action<CandidateTimelineSnapshot>>(adjustments);
		}

		public List<int> MaterializedPlacementCounts { get; } = new();
		public List<MaterializeCandidateRequest> MaterializeRequests { get; } = new();
		public List<(string Key, int PlacementCount)> MaterializeCalls { get; } =
			new();
		public int? FailMaterializeCall { get; init; }

		public Task<TResult> ExecuteAsync<TRequest, TResult>(
			string operation,
			TRequest request,
			string idempotencyKey,
			TimeSpan? timeout = null,
			CancellationToken cancellationToken = default)
		{
			object result;
			switch (operation)
			{
				case VegasOperations.PreflightCandidate:
					result = new PreflightCandidateResult { IsReady = true };
					break;
				case VegasOperations.MaterializeCandidate:
					MaterializeCandidateRequest materialize =
						(MaterializeCandidateRequest)(object)request!;
					MaterializeRequests.Add(materialize);
					materializeCallCount++;
					MaterializeCalls.Add((
						idempotencyKey,
						materialize.Plan.Montage.Placements.Count));
					if (!materializeFailureInjected &&
						FailMaterializeCall == materializeCallCount)
					{
						materializeFailureInjected = true;
						throw new InvalidOperationException(
							"Injected materialization interruption.");
					}
					materialized = EditPlanDocumentSerializer.DeserializePlan(
						EditPlanDocumentSerializer.SerializePlan(materialize.Plan));
					workspace = materialize.Workspace;
					MaterializedPlacementCounts.Add(
						materialized.Montage.Placements.Count);
					result = new MaterializeCandidateResult
					{
						CreatedEventCount = materialized.Montage.Placements.Count
					};
					break;
				case VegasOperations.GetCandidateSnapshot:
					if (materialized == null)
						throw new InvalidOperationException(
							"No progressive candidate was materialized before its snapshot.");
					CandidateTimelineSnapshot snapshot = SnapshotFor(
						materialized,
						workspace ?? throw new InvalidOperationException(
							"The progressive workspace identity was not captured."));
					if (adjustments.Count > 0)
						adjustments.Dequeue()(snapshot);
					result = snapshot;
					break;
				case VegasOperations.CleanupCandidate:
					result = new CleanupCandidateResult();
					break;
				default:
					throw new InvalidOperationException(
						"Unexpected VEGAS operation " + operation);
			}
			return Task.FromResult((TResult)result);
		}
	}

	private sealed class CapturingCheckpointPreviewPipeline :
		ICheckpointPreviewPipeline
	{
		public int Calls { get; private set; }
		public List<int> Checkpoints { get; } = new();

		public Task<CheckpointPreviewArtifact> RenderAndReviewAsync(
			int checkpoint,
			AssemblySketch sketch,
			ClipStepDecision decision,
			EditPlanDocument candidate,
			CandidateTimelineSnapshot timeline,
			TimelineAdjustmentDelta? adjustment,
			CancellationToken cancellationToken)
		{
			Calls++;
			Checkpoints.Add(checkpoint);
			Assert(candidate.Montage.Placements.Count == checkpoint,
				"Checkpoint preview did not receive the current growing prefix.");
			Assert(adjustment?.Checkpoint == checkpoint,
				"Checkpoint preview did not receive the current VEGAS comparison.");
			return Task.FromResult(new CheckpointPreviewArtifact
			{
				SessionId = "progressive-run",
				Checkpoint = checkpoint,
				Attempt = Calls,
				Status = CheckpointPreviewStatus.Completed
			});
		}
	}

	private sealed class CapturingSectionMilestoneRenderer :
		ISectionMilestoneRenderer
	{
		private readonly string sessionId;

		public CapturingSectionMilestoneRenderer(string sessionId)
		{
			this.sessionId = sessionId;
		}

		public List<int> CompletedCheckpoints { get; } = new();

		public Task<SectionMilestoneRenderManifest> RenderAsync(
			string sectionId,
			int completedCheckpoint,
			CandidateWorkspaceId workspace,
			TimeSpan timelineStart,
			TimeSpan timelineEnd,
			string planSha256,
			CancellationToken cancellationToken)
		{
			CompletedCheckpoints.Add(completedCheckpoint);
			return Task.FromResult(new SectionMilestoneRenderManifest
			{
				SessionId = sessionId,
				SectionId = sectionId,
				CompletedCheckpoint = completedCheckpoint,
				Workspace = workspace,
				PlanSha256 = planSha256,
				RenderProfileId = "self-test",
				TimelineStart = timelineStart,
				TimelineEnd = timelineEnd,
				CompletedUtc = DateTimeOffset.UtcNow
			});
		}
	}

	private sealed record ScriptedProgressiveAction(
		int Checkpoint,
		AssemblyActionKind Kind,
		string Instruction = "");

	private sealed class ProgressiveActionDriver : IDisposable
	{
		private readonly CancellationTokenSource cancellation = new();
		private readonly Task task;
		public List<AssemblyActionKind> SubmittedActionKinds { get; } = new();

		public ProgressiveActionDriver(
			string sessionRoot,
			string sessionId,
			IEnumerable<ScriptedProgressiveAction> actions)
		{
			long initialStateRevision = -1;
			string statePath = Path.Combine(
				sessionRoot, "assembly", "state.json");
			if (File.Exists(statePath))
			{
				try
				{
					initialStateRevision =
						ContractSerializer.Deserialize<AssemblySessionState>(
							File.ReadAllText(statePath)).StateRevision;
				}
				catch (IOException) { }
			}
			task = DriveAsync(
				sessionRoot,
				sessionId,
				new Queue<ScriptedProgressiveAction>(actions),
				initialStateRevision,
				cancellation.Token,
				SubmittedActionKinds);
		}

		public void Dispose()
		{
			cancellation.Cancel();
			try { task.GetAwaiter().GetResult(); }
			catch (OperationCanceledException) { }
			cancellation.Dispose();
		}

		private static async Task DriveAsync(
			string sessionRoot,
			string sessionId,
			Queue<ScriptedProgressiveAction> scripted,
			long initialStateRevision,
			CancellationToken cancellationToken,
			List<AssemblyActionKind> submittedActionKinds)
		{
			string statePath = Path.Combine(sessionRoot, "assembly", "state.json");
			string actionDirectory = Path.Combine(sessionRoot, "assembly", "actions");
			long lastHandledRevision = -1;
			while (scripted.Count > 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (File.Exists(statePath))
				{
					AssemblySessionState state;
					try
					{
						state = ContractSerializer.Deserialize<AssemblySessionState>(
							File.ReadAllText(statePath));
					}
					catch (IOException)
					{
						await Task.Delay(10, cancellationToken);
						continue;
					}
					ScriptedProgressiveAction next = scripted.Peek();
					string executionRoot = Path.Combine(
						sessionRoot,
						"assembly",
						"action-executions");
					bool hasPendingExecution =
						Directory.Exists(executionRoot) &&
						Directory.EnumerateFiles(
								executionRoot,
								"*.started.json")
							.Any(started => !File.Exists(
								Path.Combine(
									executionRoot,
									Path.GetFileName(started)
										.Replace(
											".started.json",
											".completed.json",
											StringComparison.Ordinal))));
					if (state.Phase == AssemblyPhase.AwaitingHumanReview &&
						!hasPendingExecution &&
						state.Checkpoint == next.Checkpoint &&
						state.StateRevision > initialStateRevision &&
						state.StateRevision != lastHandledRevision)
					{
						lastHandledRevision = state.StateRevision;
						scripted.Dequeue();
						Directory.CreateDirectory(actionDirectory);
						AssemblyAction action = new()
						{
							ActionId = Guid.NewGuid().ToString("N"),
							SessionId = sessionId,
							Checkpoint = state.Checkpoint,
							ExpectedStateRevision = state.StateRevision,
							Kind = next.Kind,
							Instruction = next.Instruction,
							SteeringScope =
								next.Kind ==
									AssemblyActionKind.AcceptTimelineAndContinue &&
								!string.IsNullOrWhiteSpace(next.Instruction)
									? AssemblySteeringScope.NextClip
									: AssemblySteeringScope.CurrentClip,
							CreatedUtc = DateTimeOffset.UtcNow
						};
						submittedActionKinds.Add(action.Kind);
						File.WriteAllText(
							Path.Combine(actionDirectory, action.ActionId + ".json"),
							ContractSerializer.Serialize(action));
					}
				}
				await Task.Delay(10, cancellationToken);
			}
		}
	}
}
