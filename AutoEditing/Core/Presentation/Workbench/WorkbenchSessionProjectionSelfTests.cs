using System;
using System.IO;
using System.Linq;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.Iteration.Contracts.Sessions;

namespace Core.Scripts;

internal static class WorkbenchSessionProjectionSelfTests
{
	public static void Run()
	{
		TestAssemblyProjection();
		TestRoughCutProjection();
		TestIncompleteSessionDiscovery();
		TestRecoveryCompanionCommandLines();
		TestPolishAndFinalizationProjection();
		TestPolishActionPolicy();
		TestUsageIdentityProjection();
		TestFailedSessionReasonProjection();
		TestPhaseSpecificLifecycleConsequences();
		TestEarlySyncCompletionPresentation();
	}

	private static void TestFailedSessionReasonProjection()
	{
		string root = Path.Combine(
			Path.GetTempPath(), "AutoEditing-FailedProjection-" +
			Guid.NewGuid().ToString("N"));
		try
		{
			string sessionsRoot = Path.Combine(root, "sessions");
			string sessionRoot = Path.Combine(sessionsRoot, "failed-session");
			Write(sessionRoot, "manifest.json", new EditSessionManifest
			{
				SessionId = "failed-session",
				State = EditSessionState.Failed,
				UpdatedUtc = new DateTimeOffset(
					2026, 7, 27, 14, 0, 0, TimeSpan.Zero)
			});
			Directory.CreateDirectory(sessionRoot);
			File.WriteAllText(
				Path.Combine(sessionRoot, "events.ndjson"),
				"{\"EventType\":\"state-changed\",\"Payload\":{\"state\":\"Failed\"," +
				"\"reason\":\"The selected kill is outside the source window.\"}}" +
				Environment.NewLine);
			WorkbenchUiProjection projected = new WorkbenchSessionProjectionService(
				sessionsRoot,
				Path.Combine(root, "telemetry")).ReadLatest();
			Assert(
				projected.State == EditSessionState.Failed.ToString() &&
				projected.FailureReason.Contains("outside the source window"),
				"The workbench did not project the durable failure reason.");
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	private static void TestEarlySyncCompletionPresentation()
	{
		AssemblySessionState middle = new()
		{
			SessionId = "early-finish-presentation",
			Phase = AssemblyPhase.AwaitingHumanReview,
			Checkpoint = 2,
			StateRevision = 4,
			TotalClips = 5
		};
		EarlySyncCompletionPresentation active =
			EarlySyncCompletionPresentation.Create(middle, true);
		Assert(
			active.CanFinishEarly &&
			active.AcceptLabel.Contains("plan next clip") &&
			active.FinishEarlyLabel.Contains("3 unused") &&
			active.Consequence == "3 selected clips will remain unused.",
			"The workbench did not explain early synchronization consequences.");
		EarlySyncCompletionPresentation final =
			EarlySyncCompletionPresentation.Create(
				new AssemblySessionState
				{
					SessionId = "final-finish-presentation",
					Phase = AssemblyPhase.AwaitingHumanReview,
					Checkpoint = 5,
					StateRevision = 8,
					TotalClips = 5
				},
				true);
		Assert(
			!final.CanFinishEarly &&
			final.AcceptLabel.Contains("finish sync pass"),
			"The final checkpoint was incorrectly presented as an early finish.");
	}

	private static void TestUsageIdentityProjection()
	{
		string root = Path.Combine(
			Path.GetTempPath(), "AutoEditing-UsageProjection-" +
			Guid.NewGuid().ToString("N"));
		try
		{
			string sessionsRoot = Path.Combine(root, "sessions");
			string telemetryRoot = Path.Combine(root, "telemetry");
			string sessionRoot = Path.Combine(sessionsRoot, "usage-session");
			Write(sessionRoot, "manifest.json", new EditSessionManifest
			{
				SessionId = "usage-session",
				State = EditSessionState.AwaitingUser,
				UpdatedUtc = new DateTimeOffset(
					2026, 7, 27, 14, 0, 0, TimeSpan.Zero)
			});
			InferenceUsageSummary exact = new InferenceUsageSummary
			{
				Scope = "session",
				SessionId = "usage-session",
				Provider = "llama.cpp",
				Model = "editor-q4",
				CallCount = 2,
				TotalTokens = 100,
				TimeToFirstTokenMilliseconds = 500
			};
			Write(sessionRoot, "usage-summary.json", exact);
			string identity = WorkbenchSessionProjectionService.IdentityKey(
				"llama.cpp", "editor-q4");
			Write(telemetryRoot, identity + "/usage-summary.json",
				new InferenceUsageSummary
				{
					Scope = "lifetime-model",
					Provider = "llama.cpp",
					Model = "editor-q4",
					CallCount = 9,
					TotalTokens = 900
				});
			WorkbenchSessionProjectionService service =
				new WorkbenchSessionProjectionService(sessionsRoot, telemetryRoot);
			WorkbenchUiProjection projected = service.ReadLatest();
			Assert(projected.Usage.Session.Provider == "llama.cpp" &&
				projected.Usage.Lifetime.CallCount == 9,
				"Exact backend-and-model lifetime usage was not projected.");

			exact.Provider = null;
			exact.HasMixedProviders = true;
			Write(sessionRoot, "usage-summary.json", exact);
			projected = service.ReadLatest();
			Assert(projected.Usage.Session.HasMixedProviders &&
				projected.Usage.Lifetime == null,
				"A mixed-backend session was assigned a misleading lifetime total.");
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	private static void TestPhaseSpecificLifecycleConsequences()
	{
		WorkbenchIncompleteSession planning = new WorkbenchIncompleteSession(
			Path.GetTempPath(),
			"planning",
			EditSessionState.NeedsRecovery,
			1,
			new DateTimeOffset(2026, 7, 27, 14, 0, 0, TimeSpan.Zero),
			1,
			1,
			AssemblyPhase.PlanningClip.ToString(),
			"",
			false);
		WorkbenchIncompleteSession finalReview = new WorkbenchIncompleteSession(
			Path.GetTempPath(),
			"final",
			EditSessionState.NeedsRecovery,
			1,
			new DateTimeOffset(2026, 7, 27, 14, 1, 0, TimeSpan.Zero),
			3,
			2,
			AssemblyPhase.FinalReview.ToString(),
			"",
			false);
		Assert(planning.CanAbandon &&
			planning.AbandonConsequence.IndexOf(
				"cancels planning", StringComparison.Ordinal) >= 0 &&
			finalReview.CanAbandon &&
			finalReview.AbandonConsequence.IndexOf(
				"unpromoted candidate", StringComparison.Ordinal) >= 0 &&
			finalReview.AbandonConsequence.IndexOf(
				"candidate-only polish", StringComparison.Ordinal) >= 0,
			"Abandon consequences did not explain the current workflow phase.");
		Assert(
			WorkbenchConsequenceCopy.FinalizeMontage.IndexOf(
				"validates the complete live candidate", StringComparison.Ordinal) >= 0 &&
			WorkbenchConsequenceCopy.FinalizeMontage.IndexOf(
				"candidate-owned track labels", StringComparison.Ordinal) >= 0 &&
			WorkbenchConsequenceCopy.FinalizeMontage.IndexOf(
				"external artifact archive", StringComparison.Ordinal) >= 0 &&
			WorkbenchConsequenceCopy.FinalizeMontage.IndexOf(
				"verified rollback path", StringComparison.Ordinal) >= 0,
			"The finalization confirmation no longer explains validation, promotion, archive, and rollback.");
	}

	private static void TestRoughCutProjection()
	{
		string root = Path.Combine(
			Path.GetTempPath(), "AutoEditing-RoughCutProjection-" +
			Guid.NewGuid().ToString("N"));
		try
		{
			string chunkRelative = "assembly/rough-cut/renders/render-1/chunk-0001.mp4";
			string frameRelative = "assembly/rough-cut/evidence/frame-0001.png";
			string manifestRelative =
				"assembly/rough-cut/renders/render-1/manifest.json";
			WriteBytes(root, chunkRelative, new byte[] { 1, 2, 3 });
			WriteBytes(root, frameRelative, new byte[] { 4, 5, 6 });
			CandidateWorkspaceId workspace = new CandidateWorkspaceId
			{
				SessionId = "rough-projection",
				Iteration = 1,
				Nonce = "projection"
			};
			RoughCutRenderManifest manifest = new RoughCutRenderManifest
			{
				SessionId = "rough-projection",
				RenderId = "render-1",
				Workspace = workspace,
				PlanSha256 = new string('a', 64),
				RenderProfileId = "review-1080p",
				TimelineStart = TimeSpan.Zero,
				TimelineEnd = TimeSpan.FromSeconds(5),
				CompletedUtc = new DateTimeOffset(
					2026, 7, 27, 10, 0, 0, TimeSpan.Zero),
				Chunks = new[]
				{
					new RoughCutRenderChunk
					{
						ChunkIndex = 1,
						Start = TimeSpan.Zero,
						Duration = TimeSpan.FromSeconds(5),
						OutputRelativePath = chunkRelative,
						Sha256 = HashFile(Path.Combine(root,
							chunkRelative.Replace('/', Path.DirectorySeparatorChar)))
					}
				}
			};
			Write(root, manifestRelative, manifest);
			RoughCutAuditReport report = new RoughCutAuditReport
			{
				SessionId = "rough-projection",
				ReportId = "report-1",
				CreatedUtc = new DateTimeOffset(
					2026, 7, 27, 10, 1, 0, TimeSpan.Zero),
				RequestId = "request-1",
				PlanSha256 = new string('a', 64),
				Summary = "One evidence-backed finding.",
				ModelStatus = "completed",
				Metrics = new RoughCutAuditMetrics
				{
					TimelineStartSeconds = 0,
					TimelineEndSeconds = 5,
					MontageDurationSeconds = 5,
					PlacementCount = 1,
					MinimumPlacementDurationSeconds = 5,
					MedianPlacementDurationSeconds = 5,
					MaximumPlacementDurationSeconds = 5,
					AveragePlacementDurationSeconds = 5
				},
				Evidence = new[]
				{
					new RoughCutEvidenceReference
					{
						EvidenceId = "render",
						Kind = RoughCutEvidenceKind.FullRender,
						RelativePath = manifestRelative,
						Sha256 = HashFile(Path.Combine(root,
							manifestRelative.Replace('/', Path.DirectorySeparatorChar))),
						MediaType = "application/json",
						Description = "Full-render chunk manifest."
					},
					new RoughCutEvidenceReference
					{
						EvidenceId = "frame",
						Kind = RoughCutEvidenceKind.ContactSheet,
						RelativePath = frameRelative,
						Sha256 = HashFile(Path.Combine(root,
							frameRelative.Replace('/', Path.DirectorySeparatorChar))),
						MediaType = "image/png",
						Description = "VEGAS snapshot at the action.",
						TimelineTimeSeconds = 2.5
					}
				},
				Findings = new[]
				{
					new RoughCutAuditFinding
					{
						FindingId = "finding-1",
						Category = RoughCutAuditCategory.Continuity,
						Severity = RoughCutFindingSeverity.Warning,
						Source = RoughCutFindingSource.MultimodalModel,
						Summary = "Join needs review.",
						Details = "Snapshot evidence is linked.",
						StartSeconds = 2,
						EndSeconds = 3,
						AffectedCheckpoints = new[] { 1 },
						EvidenceIds = new[] { "frame" },
						Confidence = .8
					}
				},
				Corrections = new[]
				{
					new RoughCutCorrectionProposal
					{
						CorrectionId = "correction-1",
						FindingIds = new[] { "finding-1" },
						TargetCheckpoints = new[] { 1 },
						Operation = RoughCutCorrectionOperation.Trim,
						Instruction = "Trim the inactive tail.",
						ExpectedOutcome = "The join reads cleanly.",
						Risk = "Setup may become shorter.",
						Confidence = .75
					}
				}
			};
			Write(root, "assembly/rough-cut/audits/report-1/report.json", report);
			Write(root, "assembly/rough-cut/current-report.json",
				new WorkbenchRoughCutPointer
				{
					SessionId = report.SessionId,
					ReportId = report.ReportId,
					ReportSha256 = new string('b', 64),
					PlanSha256 = report.PlanSha256,
					UpdatedUtc = report.CreatedUtc
				});
			WorkbenchRoughCutProjection projection =
				WorkbenchSessionProjectionService.ReadRoughCut(root);
			Assert(projection.Chunks.Count == 1 &&
				projection.Chunks[0].Chunk.Start == TimeSpan.Zero &&
				projection.Evidence.Single(item =>
					item.Evidence.EvidenceId == "frame")
					.Evidence.TimelineTimeSeconds == 2.5 &&
				projection.Findings.Single().Evidence.Single()
					.Evidence.EvidenceId == "frame" &&
				projection.Findings.Single().Corrections.Single().Operation ==
					RoughCutCorrectionOperation.Trim,
				"The rough-cut workbench projection lost chunk order, navigation time, " +
				"or finding-to-evidence-to-proposal traceability.");
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	private static void TestAssemblyProjection()
	{
		string root = Path.Combine(
			Path.GetTempPath(), "AutoEditing-AssemblyProjection-" + Guid.NewGuid().ToString("N"));
		try
		{
			Write(root, "assembly/state.json", new AssemblySessionState
			{
				SessionId = "projection-session",
				Checkpoint = 2,
				StateRevision = 7,
				TotalClips = 3,
				Phase = AssemblyPhase.AwaitingHumanReview
			});
			Write(root, "assembly/sketch/revisions/0001.json", Sketch("old", 2));
			Write(root, "assembly/sketch/revisions/0002.json", Sketch("current", 3));
			Write(root, "assembly/checkpoints/0001/proposals/0001.json", Decision(1, "one.mp4"));
			Write(root, "assembly/checkpoints/0001/accepted-plan.json", new { accepted = true });
			Write(root, "assembly/checkpoints/0002/proposals/0001.json", Decision(2, "old-two.mp4"));
			Write(root, "assembly/checkpoints/0002/proposals/0002.json", Decision(2, "two.mp4"));
			Write(root, "assembly/checkpoints/0002/rejections/0001.json",
				new AssemblyProposalRejection
				{
					Checkpoint = 2,
					ProposalRevision = 1,
					Attempt = 1,
					MaximumAttempts = 3,
					Diagnostic = "Additional sync is inconsistent.",
					ProposalRelativePath =
						"assembly/checkpoints/0002/proposals/0001.json"
				});
			Write(root, "assembly/checkpoints/0001/adjustment-delta.json",
				new TimelineAdjustmentDelta { Checkpoint = 1 });
			Write(root, "assembly/checkpoints/0002/adjustment-delta.json",
				new TimelineAdjustmentDelta { Checkpoint = 2 });
			Write(root,
				"assembly/checkpoints/0002/reconciliation/current-conflict.json",
				new AssemblyReconciliationConflict
				{
					ConflictId = "sync-conflict-projection",
					SessionId = "projection-session",
					Checkpoint = 2,
					CurrentClipPath = "two.mp4",
					FailureSummary = "Expected event was deleted.",
					CreatedUtc = new DateTimeOffset(
						2026, 7, 26, 11, 0, 0, TimeSpan.Zero),
					Issues = new[]
					{
						new AssemblyReconciliationConflictIssue
						{
							Kind = AssemblyReconciliationConflictKind
								.MissingExpectedEvent,
							ExpectedMediaPath = "two.mp4",
							Detail = "Expected event is absent."
						}
					},
					Candidates =
						new AssemblyReconciliationEventCandidate[0],
					SupportedResolutions = new[]
					{
						AssemblyReconciliationResolutionKind
							.RestoreExactProposal,
						AssemblyReconciliationResolutionKind.DeferAndPause
					}
				});
			CheckpointPreviewArtifact previewTwo =
				PreviewArtifact(2, "Earlier timing.");
			CheckpointPreviewArtifact previewThree =
				PreviewArtifact(3, "Timing is readable.");
			Write(root,
				"assembly/checkpoints/0002/previews/0002/preview-artifact.json",
				previewTwo);
			Write(root,
				"assembly/checkpoints/0002/previews/0003/preview-artifact.json",
				previewThree);
			Write(root, "assembly/checkpoints/0002/previews/current.json",
				previewThree);
			Write(root, "assembly/checkpoints/0002/previews/0002/review.json",
				new CheckpointReviewReport
				{
					SessionId = "projection-session",
					Checkpoint = 2,
					Attempt = 2,
					Summary = "Earlier timing.",
					Confidence = .6
				});
			Write(root, "assembly/checkpoints/0002/previews/0003/review.json",
				new CheckpointReviewReport
				{
					SessionId = "projection-session",
					Checkpoint = 2,
					Attempt = 3,
					Summary = "Timing is readable.",
					Confidence = .8
				});
			Write(root,
				"assembly/milestones/sections/test/current.json",
				new SectionMilestoneRenderManifest
				{
					SessionId = "projection-session",
					SectionId = "section-a",
					CompletedCheckpoint = 2,
					Workspace = new CandidateWorkspaceId
					{
						SessionId = "projection-session",
						Iteration = 2,
						Nonce = "candidate"
					},
					PlanSha256 = new string('c', 64),
					RenderProfileId = "review-1080p",
					TimelineStart = TimeSpan.Zero,
					TimelineEnd = TimeSpan.FromSeconds(5),
					CompletedUtc = new DateTimeOffset(
						2026, 7, 26, 11, 5, 0, TimeSpan.Zero),
					Chunks = new[]
					{
						new RoughCutRenderChunk
						{
							ChunkIndex = 1,
							Start = TimeSpan.Zero,
							Duration = TimeSpan.FromSeconds(5),
							OutputRelativePath =
								"assembly/milestones/sections/test/chunk-0001.mp4",
							Sha256 = new string('d', 64)
						}
					}
				});
			Write(root,
				"assembly/action-dispositions/action.json.quarantined.stale-state.json",
				new AssemblyActionDispositionProjection
				{
					ActionFile = "action.json",
					Outcome = "quarantined",
					Reason = "stale-state",
					Detail = "action-1",
					RecordedUtc = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero)
				});
			Write(
				root,
				"assembly/action-executions/pending.started.json",
				new
				{
					SchemaVersion = 1,
					SessionId = "projection-session",
					Action = new AssemblyAction
					{
						ActionId = "pending",
						SessionId = "projection-session",
						Checkpoint = 2,
						ExpectedStateRevision = 3,
						Kind = AssemblyActionKind.ReviseCurrentClip,
						CreatedUtc = new DateTimeOffset(
							2026, 7, 26, 12, 1, 0, TimeSpan.Zero)
					},
					Phase = AssemblyPhase.AwaitingHumanReview,
					PlanSha256 = "",
					ProposalRevision = 0,
					PreviewAttempt = 0,
					AdjustmentSha256 = "",
					StartedUtc = new DateTimeOffset(
						2026, 7, 26, 12, 1, 0, TimeSpan.Zero)
				});

			WorkbenchAssemblyProjection result =
				WorkbenchSessionProjectionService.ReadAssembly(root);
			Assert(result.ActiveSketch.EditorialThesis == "current",
				"The latest append-only sketch revision was not selected.");
			Assert(result.CurrentProposal != null &&
				result.CurrentProposal.Clip.MediaPath == "two.mp4",
				"The latest proposal revision for the active checkpoint was not selected.");
			Assert(result.CurrentCheckpoint.ProposalRejections.Count == 1 &&
				result.CurrentCheckpoint.ProposalRejections[0].Diagnostic.IndexOf(
					"inconsistent",
					StringComparison.OrdinalIgnoreCase) >= 0,
				"Rejected AI proposal diagnostics were not projected for inspection.");
			Assert(result.AcceptedCheckpoints.Count == 1 &&
				result.AcceptedCheckpoints[0].Checkpoint == 1,
				"Accepted checkpoints were not projected.");
			Assert(result.CurrentCheckpoint != null &&
				result.CurrentCheckpoint.Checkpoint == 2 &&
				result.RemainingCheckpoints.Count == 2,
				"Current and remaining checkpoints were not projected.");
			Assert(result.LatestTimelineAdjustment != null &&
				result.LatestTimelineAdjustment.Checkpoint == 2,
				"The latest timeline adjustment was not projected.");
			Assert(result.CurrentCheckpoint.Preview != null &&
				result.CurrentCheckpoint.Preview.Attempt == 3 &&
				result.CurrentCheckpoint.PreviewReview != null &&
				result.CurrentCheckpoint.PreviewReview.Summary ==
					"Timing is readable." &&
				result.CurrentCheckpoint.Previews.Count == 2 &&
				result.CurrentCheckpoint.Previews[0].Artifact.Attempt == 2 &&
				result.CurrentCheckpoint.Previews[1].Artifact.Attempt == 3 &&
				result.CurrentCheckpoint.SectionMilestones.Count == 1 &&
				result.CurrentCheckpoint.SectionMilestones[0].SectionId ==
					"section-a",
				"The current checkpoint preview evidence was not projected.");
			Assert(result.QuarantinedActions.Count == 1 &&
				result.QuarantinedActions[0].Reason == "stale-state",
				"Quarantined action diagnostics were not projected.");
			Assert(result.HasPendingActionExecution,
				"A pending action execution did not suppress stale review controls.");
			Assert(result.ReconciliationConflict != null &&
				result.ReconciliationConflict.ConflictId ==
					"sync-conflict-projection",
				"The active synchronization conflict was not projected.");
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	private static void TestIncompleteSessionDiscovery()
	{
		string root = Path.Combine(
			Path.GetTempPath(), "AutoEditing-RecoveryProjection-" + Guid.NewGuid().ToString("N"));
		try
		{
			Write(root, "sessions/paused/manifest.json", new EditSessionManifest
			{
				SessionId = "paused",
				State = EditSessionState.Paused,
				Revision = 9,
				UpdatedUtc = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero)
			});
			Write(root, "sessions/paused/assembly/state.json", new AssemblySessionState
			{
				SessionId = "paused",
				Checkpoint = 4,
				StateRevision = 11,
				Phase = AssemblyPhase.Paused,
				Status = "Waiting to resume"
			});
			Write(root, "sessions/completed/manifest.json", new EditSessionManifest
			{
				SessionId = "completed",
				State = EditSessionState.Completed,
				UpdatedUtc = new DateTimeOffset(2026, 7, 26, 13, 0, 0, TimeSpan.Zero)
			});
			Write(root, "sessions/wrong-directory/manifest.json", new EditSessionManifest
			{
				SessionId = "another-session",
				State = EditSessionState.Paused,
				UpdatedUtc = new DateTimeOffset(2026, 7, 26, 13, 30, 0, TimeSpan.Zero)
			});
			Write(root, "sessions/active/manifest.json", new EditSessionManifest
			{
				SessionId = "active",
				State = EditSessionState.AwaitingUser,
				Revision = 3,
				UpdatedUtc = new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero)
			});
			Write(root, "sessions/active/assembly/state.json", new AssemblySessionState
			{
				SessionId = "active",
				Checkpoint = 2,
				StateRevision = 5,
				Phase = AssemblyPhase.AwaitingHumanReview
			});
			WorkbenchSessionProjectionService service =
				new WorkbenchSessionProjectionService(
					Path.Combine(root, "sessions"),
					Path.Combine(root, "telemetry"));
			string activeRoot = Path.Combine(root, "sessions", "active");
			string pausedRoot = Path.Combine(root, "sessions", "paused");
			using AssemblyRuntimeLease activeLease =
				AssemblyRuntimeLease.Acquire(activeRoot, "active");
			using AssemblyRuntimeLease pausedLease =
				AssemblyRuntimeLease.Acquire(pausedRoot, "paused");
			var sessions = service.ReadIncompleteSessions();
			Assert(sessions.Count == 2 && sessions[0].SessionId == "active" &&
				sessions[1].SessionId == "paused",
				"Incomplete sessions were not filtered and ordered deterministically.");
			Assert(sessions[1].HasLiveConsumer &&
				sessions[1].CanResume && !sessions[1].CanPause &&
				sessions[1].Checkpoint == 4,
				"Live paused-session recovery capabilities were projected incorrectly.");
			Assert(sessions[0].HasLiveConsumer &&
				!sessions[0].CanResume && sessions[0].CanPause,
				"Live review-session recovery capabilities were projected incorrectly.");
			AssemblyAction queued = service.SubmitAssemblyAction(
				sessions[1].SessionRoot,
				sessions[1].SessionId,
				sessions[1].Checkpoint,
				sessions[1].AssemblyStateRevision,
				AssemblyActionKind.ResumeSession,
				"");
			string actionPath = Path.Combine(
				sessions[1].SessionRoot,
				"assembly",
				"actions",
				"checkpoint-000004-revision-0000000000000000011.json");
			AssemblyAction persisted = ContractSerializer.Deserialize<AssemblyAction>(
				File.ReadAllText(actionPath));
			Assert(persisted.ActionId == queued.ActionId &&
				persisted.Kind == AssemblyActionKind.ResumeSession &&
				persisted.ExpectedStateRevision == 11,
				"The resume command was not persisted against the projected durable state.");
			activeLease.Dispose();
			var stopped = service.ReadIncompleteSessions()
				.First(value => value.SessionId == "active");
			Assert(!stopped.HasLiveConsumer &&
				stopped.CanResume && !stopped.CanPause && stopped.CanAbandon,
				"A stopped companion was not offered process-based Resume and Abandon.");
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	private static void TestRecoveryCompanionCommandLines()
	{
		string root = Path.Combine(Path.GetTempPath(), "companion");
		var resume = LlmEditorCompanionProcess.CreateStartInfo(
			root, "workbench-resume", "session-with-spaces");
		Assert(
			resume.Arguments ==
				"workbench-resume --session-id \"session-with-spaces\" --planner configured" &&
			!resume.UseShellExecute &&
			resume.CreateNoWindow,
			"The stopped-session Resume command line is not deterministic or hidden.");
		var abandon = LlmEditorCompanionProcess.CreateStartInfo(
			root, "workbench-abandon", "session-a");
		Assert(
			abandon.Arguments ==
				"workbench-abandon --session-id \"session-a\"" &&
			!abandon.UseShellExecute &&
			abandon.CreateNoWindow,
			"The stopped-session Abandon command line is not deterministic or hidden.");
		var rollback = LlmEditorCompanionProcess.CreateStartInfo(
			root, "workbench-rollback", "session-a");
		Assert(
			rollback.Arguments ==
				"workbench-rollback --session-id \"session-a\"" &&
			!rollback.UseShellExecute &&
			rollback.CreateNoWindow,
			"The promotion Rollback command line is not deterministic or hidden.");
	}

	private static void TestPolishAndFinalizationProjection()
	{
		string root = Path.Combine(
			Path.GetTempPath(), "AutoEditing-PolishProjection-" +
			Guid.NewGuid().ToString("N"));
		try
		{
			string sessionRoot = Path.Combine(root, "sessions", "polish-session");
			string hash = new string('a', 64);
			CandidateWorkspaceId workspace = new CandidateWorkspaceId
			{
				SessionId = "polish-session",
				Iteration = 1,
				Nonce = "candidate"
			};
			Write(sessionRoot, "manifest.json", new EditSessionManifest
			{
				SessionId = "polish-session",
				State = EditSessionState.Completed,
				UpdatedUtc = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero)
			});
			Write(sessionRoot, "assembly/state.json", new AssemblySessionState
			{
				SessionId = "polish-session",
				Checkpoint = 3,
				StateRevision = 17,
				Phase = AssemblyPhase.Completed,
				Workspace = workspace
			});
			Write(sessionRoot, "assembly/polish/effects/states/00000005.json",
				new PolishPassStateRecord
				{
					SessionId = "polish-session",
					Pass = PolishPassKind.Effects,
					PlanId = "effects-1",
					PlanRevision = 1,
					PlanSha256 = hash,
					Sequence = 5,
					Status = PolishPlanStatus.PreviewReady,
					Reason = "Preview complete.",
					RecordedUtc = new DateTimeOffset(
						2026, 7, 27, 11, 0, 0, TimeSpan.Zero)
				});
			Write(sessionRoot,
				"assembly/polish/effects/revisions/0001/plan.json",
				new EffectsPassPlan
				{
					SessionId = "polish-session",
					PlanId = "effects-1",
					Revision = 1,
					BaseRoughCutSha256 = new string('b', 64),
					CreatedUtc = new DateTimeOffset(
						2026, 7, 27, 10, 0, 0, TimeSpan.Zero),
					Actions =
					{
						new EffectsPassAction
						{
							ActionId = "pump-1",
							PlacementPath = Path.Combine(root, "clip.mp4"),
							MusicEventId = "event-1",
							TimelineTimeSeconds = 2,
							LocalTimeSeconds = 1,
							Intensity = .5,
							DurationSeconds = .2,
							RecipeId = "native-screen-pump",
							Reason = "Reviewed impact."
						}
					}
				});
			Write(sessionRoot,
				"assembly/polish/effects/revisions/0001/preview/manifest.json",
				new PolishPassPreviewManifest
				{
					SessionId = "polish-session",
					Pass = PolishPassKind.Effects,
					PlanId = "effects-1",
					PlanRevision = 1,
					PlanSha256 = hash,
					Workspace = workspace,
					CompletedUtc = new DateTimeOffset(
						2026, 7, 27, 11, 0, 0, TimeSpan.Zero),
					RenderProfileId = "preview",
					TimelineStart = TimeSpan.Zero,
					TimelineEnd = TimeSpan.FromSeconds(10),
					Chunks =
					{
						new RoughCutRenderChunk
						{
							ChunkIndex = 1,
							Start = TimeSpan.Zero,
							Duration = TimeSpan.FromSeconds(10),
							OutputRelativePath =
								"assembly/polish/effects/revisions/0001/preview/chunks/0001.mp4",
							Sha256 = new string('c', 64)
						}
					}
				});
			Write(sessionRoot, "assembly/polish/audio/states/00000001.json",
				new PolishPassStateRecord
				{
					SessionId = "polish-session",
					Pass = PolishPassKind.Audio,
					PlanId = "audio-1",
					PlanRevision = 1,
					PlanSha256 = new string('d', 64),
					Sequence = 1,
					Status = PolishPlanStatus.AwaitingApproval,
					Reason = "Awaiting exact plan approval.",
					RecordedUtc = new DateTimeOffset(
						2026, 7, 27, 11, 10, 0, TimeSpan.Zero)
				});
			Write(sessionRoot,
				"assembly/polish/audio/revisions/0001/plan.json",
				new AudioPassPlan
				{
					SessionId = "polish-session",
					PlanId = "audio-1",
					Revision = 1,
					BaseRoughCutSha256 = new string('b', 64),
					EffectsPassSha256 = hash,
					CreatedUtc = new DateTimeOffset(
						2026, 7, 27, 11, 10, 0, TimeSpan.Zero),
					Song = new AudioPassSongAction
					{
						SongPath = Path.Combine(root, "song.wav")
					}
				});
			Write(sessionRoot, "finalization/recovery-bundle.json",
				new FinalizationRecoveryBundle
				{
					PromotedUtc = new DateTimeOffset(
						2026, 7, 27, 12, 0, 0, TimeSpan.Zero),
					Promotion = new PromoteCandidateResult
					{
						PromotionId = "promotion-1",
						Workspace = workspace
					}
				});
			Write(sessionRoot, "finalization/final-session-report.json",
				new FinalSessionReport
				{
					SessionId = "polish-session",
					RequestId = "request-1",
					PromotionId = "promotion-1",
					CompletedUtc = new DateTimeOffset(
						2026, 7, 27, 12, 0, 0, TimeSpan.Zero),
					FinalPlanSha256 = new string('e', 64),
					PromotedSnapshotSha256 = new string('f', 64),
					PlacementCount = 3,
					TimelineDuration = TimeSpan.FromSeconds(10)
				});
			Write(sessionRoot, "finalization/archive-receipt.json",
				new FinalArtifactArchiveReceipt
				{
					ArchivePath = Path.Combine(root, "archive.zip"),
					CreatedUtc = new DateTimeOffset(
						2026, 7, 27, 12, 0, 0, TimeSpan.Zero),
					LengthBytes = 100,
					Sha256 = new string('1', 64),
					EntryCount = 4
				});

			WorkbenchSessionProjectionService service =
				new WorkbenchSessionProjectionService(
					Path.Combine(root, "sessions"),
					Path.Combine(root, "telemetry"));
			WorkbenchUiProjection result = service.ReadLatest();
			Assert(result.Polish != null &&
				result.Polish.Effects.State.Status == PolishPlanStatus.PreviewReady &&
				result.Polish.Effects.PreviewChunks.Count == 1 &&
				result.Polish.Audio.State.Status == PolishPlanStatus.AwaitingApproval,
				"Versioned polish plans and preview chunks were not projected.");
			Assert(result.Finalization != null &&
				result.Finalization.Report.PlacementCount == 3 &&
				result.Finalization.Archive.EntryCount == 4 &&
				result.Finalization.CanRollback,
				"Final report, archive, and rollback availability were not projected.");
			Write(sessionRoot,
				"finalization/rollback-attempts/rollback-1.intent.json",
				new FinalizationRollbackIntent
				{
					RollbackAttemptId = "rollback-1",
					PromotionId = "promotion-1",
					CreatedUtc = new DateTimeOffset(
						2026, 7, 27, 12, 1, 0, TimeSpan.Zero),
					ExpectedProjectFingerprint = "project",
					ExpectedPromotedSnapshotSha256 = new string('f', 64)
				});
			result = service.ReadLatest();
			Assert(result.Finalization.RollbackInterrupted &&
				!result.Finalization.RollbackPending &&
				result.Finalization.CanRollback,
				"A dead rollback attempt was not projected as safely retryable.");
			using (AssemblyRuntimeLease rollbackLease =
				AssemblyRuntimeLease.Acquire(sessionRoot, "polish-session"))
			{
				result = service.ReadLatest();
				Assert(result.Finalization.RollbackPending &&
					!result.Finalization.RollbackInterrupted &&
					!result.Finalization.CanRollback,
					"A live rollback companion did not suppress a concurrent launch.");
			}
			Write(sessionRoot,
				"finalization/rollback-attempts/rollback-1.result.json",
				new RollbackCandidatePromotionResult
				{
					PromotionId = "promotion-1",
					Workspace = workspace,
					RestoredSnapshotSha256 = new string('2', 64)
				});
			result = service.ReadLatest();
			Assert(result.Finalization.RollbackInterrupted &&
				result.Finalization.CanRollback,
				"An attempt missing the canonical result was not retryable after its lease ended.");
			Write(sessionRoot, "finalization/rollback-result.json",
				new RollbackCandidatePromotionResult
				{
					PromotionId = "promotion-1",
					Workspace = workspace,
					RestoredSnapshotSha256 = new string('2', 64)
				});
			result = service.ReadLatest();
			Assert(!result.Finalization.RollbackPending &&
				!result.Finalization.RollbackInterrupted &&
				!result.Finalization.CanRollback &&
				result.Finalization.Rollback.PromotionId == "promotion-1",
				"A completed rollback did not replace rollback availability.");
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	private static void TestPolishActionPolicy()
	{
		Assert(
			WorkbenchPolishActionPolicy.Resolve(
				AssemblyPhase.EffectsPlanReview, true, false) ==
				AssemblyActionKind.ApproveEffectsPlan &&
			WorkbenchPolishActionPolicy.Resolve(
				AssemblyPhase.EffectsPreviewReview, false, true) ==
				AssemblyActionKind.SkipEffectsPreview &&
			WorkbenchPolishActionPolicy.Resolve(
				AssemblyPhase.AudioPlanReview, false, false) ==
				AssemblyActionKind.SkipAudioPlan &&
			WorkbenchPolishActionPolicy.Resolve(
				AssemblyPhase.AudioPreviewReview, true, true) ==
				AssemblyActionKind.AcceptAudioPreview,
			"Polish review controls do not map to their exact durable phase.");
		bool rejected = false;
		try
		{
			WorkbenchPolishActionPolicy.Resolve(
				AssemblyPhase.EffectsPlanReview, true, true);
		}
		catch (InvalidOperationException)
		{
			rejected = true;
		}
		Assert(rejected,
			"A preview control was allowed to submit against a plan-review revision.");
	}

	private static AssemblySketch Sketch(string thesis, int clips)
	{
		AssemblySketch sketch = new AssemblySketch { EditorialThesis = thesis };
		for (int index = 1; index <= clips; index++)
			sketch.ClipOrder.Add(new AssemblyClipIntent
			{
				Order = index,
				Clip = new AssemblyClipReference
				{
					ReferenceId = "clip-" + index,
					MediaPath = index + ".mp4"
				}
			});
		return sketch;
	}

	private static ClipStepDecision Decision(int checkpoint, string path) =>
		new ClipStepDecision
		{
			StepIndex = checkpoint,
			Clip = new AssemblyClipReference
			{
				ReferenceId = "clip-" + checkpoint,
				MediaPath = path
			}
		};

	private static CheckpointPreviewArtifact PreviewArtifact(
		int attempt,
		string summary) =>
		new CheckpointPreviewArtifact
		{
			SessionId = "projection-session",
			Checkpoint = 2,
			Attempt = attempt,
			Status = CheckpointPreviewStatus.Completed,
			StartedUtc = new DateTimeOffset(
				2026, 7, 26, 11, attempt, 0, TimeSpan.Zero),
			CompletedUtc = new DateTimeOffset(
				2026, 7, 26, 11, attempt, 10, TimeSpan.Zero),
			Workspace = new CandidateWorkspaceId
			{
				SessionId = "projection-session",
				Iteration = 2,
				Nonce = "candidate"
			},
			Window = new CheckpointPreviewWindow
			{
				Start = TimeSpan.Zero,
				End = TimeSpan.FromSeconds(5),
				PlacementStart = TimeSpan.FromSeconds(1),
				PlacementEnd = TimeSpan.FromSeconds(4),
				ContextBefore = TimeSpan.FromSeconds(1),
				ContextAfter = TimeSpan.FromSeconds(1)
			},
			RenderProfileId = "preview-test",
			PreviewRelativePath =
				$"assembly/checkpoints/0002/previews/{attempt:D4}/preview.mp4",
			PreviewSha256 = new string(
				(char)('a' + (attempt % 6)),
				64),
			TimelineSnapshotRelativePath =
				$"assembly/checkpoints/0002/previews/{attempt:D4}/timeline.json",
			TimingSidecarRelativePath =
				$"assembly/checkpoints/0002/previews/{attempt:D4}/timing.json",
			TimingVisualizationRelativePath =
				$"assembly/checkpoints/0002/previews/{attempt:D4}/timing.png",
			ContactSheetManifestRelativePath =
				$"assembly/checkpoints/0002/previews/{attempt:D4}/contact-sheet.json",
			ReviewReportRelativePath =
				$"assembly/checkpoints/0002/previews/{attempt:D4}/review.json"
		};

	private static void Write<T>(string root, string relativePath, T value)
	{
		string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		File.WriteAllText(path, ContractSerializer.Serialize(value));
	}

	private static void WriteBytes(string root, string relativePath, byte[] bytes)
	{
		string path = Path.Combine(
			root, relativePath.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		File.WriteAllBytes(path, bytes);
	}

	private static string HashFile(string path)
	{
		using (System.Security.Cryptography.SHA256 sha =
			System.Security.Cryptography.SHA256.Create())
		using (FileStream stream = File.OpenRead(path))
			return BitConverter.ToString(sha.ComputeHash(stream))
				.Replace("-", "").ToLowerInvariant();
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}
}
