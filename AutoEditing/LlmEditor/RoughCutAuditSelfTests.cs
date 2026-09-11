using System.Security.Cryptography;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.LlmEditor.Inference;
using AutoEditing.LlmEditor.RoughCut;
using Core.Domain.Audio;
using Core.Domain.Audio.SongAnalysis;
using Core.Domain.Clip;
using Core.Domain.Editing;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor;

internal static class RoughCutAuditSelfTests
{
	public static void Run(string testRoot)
	{
		string sessionRoot = Path.Combine(testRoot, "rough-cut-audit");
		Directory.CreateDirectory(sessionRoot);
		RoughCutAuditInput input = CreateInput(sessionRoot);
		DateTimeOffset now = new(2026, 7, 26, 19, 0, 0, TimeSpan.Zero);

		RoughCutAuditReport deterministic = new RoughCutAuditService(
				clock: () => now)
			.RunAsync(sessionRoot, input, CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(deterministic.ModelStatus == "skipped",
			"A deterministic-only audit did not identify its model status.");
		Assert(deterministic.Findings.Any(item =>
				item.Category == RoughCutAuditCategory.Gap) &&
			deterministic.Findings.Any(item =>
				item.Category == RoughCutAuditCategory.Continuity) &&
			deterministic.Findings.Any(item =>
				item.Category == RoughCutAuditCategory.Repetition) &&
			deterministic.Findings.Any(item =>
				item.Category == RoughCutAuditCategory.Pacing) &&
			deterministic.Findings.Any(item =>
				item.Category == RoughCutAuditCategory.MusicEventCoverage) &&
			deterministic.Findings.Any(item =>
				item.Category == RoughCutAuditCategory.ReservationFulfillment),
			"The deterministic audit missed a required rough-cut review category.");
		Assert(deterministic.Metrics.Gaps.Count == 1 &&
			deterministic.Metrics.UnusedMajorMusicEventIds.Contains("event-unused") &&
			deterministic.Metrics.UnfulfilledReservationIds.Contains("reserve-peak"),
			"The deterministic rough-cut metrics lost gaps, major events, or reservations.");
		Assert(deterministic.Corrections.All(item =>
				item.TargetCheckpoints.Count > 0) &&
			deterministic.Corrections.Select(item => item.CorrectionId).Distinct().Count() ==
				deterministic.Corrections.Count,
			"Rough-cut corrections are not independently targetable.");
		Assert(deterministic.Corrections.All(item =>
				Enum.IsDefined(typeof(RoughCutCorrectionOperation), item.Operation)) &&
			deterministic.Findings
				.Where(item => item.Category == RoughCutAuditCategory.Repetition)
				.All(finding => deterministic.Corrections.All(correction =>
					!correction.FindingIds.Contains(finding.FindingId))),
			"Unsupported substitution or reorder ideas escaped the advisory findings.");

		TestStrictMultimodalAudit(sessionRoot, input, deterministic);
		TestFallback(sessionRoot, input, now);
		TestRenderChunks(sessionRoot, input, now);
		TestPersistenceAndMilestone(sessionRoot, deterministic, now);
		TestVisualSamplingAndCorrectionScope(input);
		TestWorkflowRecovery(testRoot, input);
		TestFinalTimelineIdentity(input);
	}

	private static void TestStrictMultimodalAudit(
		string sessionRoot,
		RoughCutAuditInput input,
		RoughCutAuditReport deterministic)
	{
		string valid = """
			{
			  "schemaVersion":1,
			  "sessionId":"audit-session",
			  "summary":"The render confirms one weak join.",
			  "findings":[{
			    "findingId":"model-join-1",
			    "category":"continuity",
			    "severity":"warning",
			    "summary":"The cut lacks visual direction continuity.",
			    "details":"The contact sheet shows an abrupt viewpoint reversal.",
			    "startSeconds":2.0,
			    "endSeconds":3.0,
			    "affectedCheckpoints":[1,2],
			    "evidenceIds":["contact-sheet-main"],
			    "confidence":0.82
			  }],
			  "corrections":[{
			    "correctionId":"model-fix-join-1",
			    "findingIds":["model-join-1"],
			    "targetCheckpoints":[2],
			    "operationType":"trim",
			    "instruction":"Reopen checkpoint 2 and test a later source in-point.",
			    "expectedOutcome":"The viewpoint transition reads continuously.",
			    "risk":"A later in-point may reduce setup readability.",
			    "confidence":0.76
			  }]
			}
			""";
		StubGenerationClient client = new(valid, "vision-test");
		LlmRoughCutAuditor auditor = new(client, new InferenceBudgets
		{
			MinimumContextTokens = 65536,
			PlanningMaxOutputTokens = 4096,
			ReviewMaxOutputTokens = 2048
		});
		LlmRoughCutAuditContribution result = auditor.AuditAsync(
				sessionRoot, input, deterministic, CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(result.Findings.Single().EvidenceIds.SequenceEqual(
				new[] { "contact-sheet-main" }) &&
			result.Corrections.Single().TargetCheckpoints.SequenceEqual(new[] { 2 }) &&
			result.Corrections.Single().Operation == RoughCutCorrectionOperation.Trim &&
			client.Requests.Single().JsonSchema == RoughCutAuditSchema.Json &&
			client.Requests.Single().VisualEvidence.Count == 1,
			"The strict multimodal audit lost rendered evidence or checkpoint scope.");

		ExpectFailure(
			() => new LlmRoughCutAuditor(
					new StubGenerationClient(
						valid.Replace(
							"\"summary\":\"The render confirms one weak join.\",",
							"\"summary\":\"The render confirms one weak join.\",\"extra\":true,"),
						"vision-test"),
					new InferenceBudgets
					{
						MinimumContextTokens = 65536,
						PlanningMaxOutputTokens = 4096,
						ReviewMaxOutputTokens = 2048
					})
				.AuditAsync(sessionRoot, input, deterministic, CancellationToken.None)
				.GetAwaiter().GetResult(),
			"The strict rough-cut schema accepted an unknown response property.");
		ExpectFailure(
			() => new LlmRoughCutAuditor(
					new StubGenerationClient(
						valid.Replace(
							"\"targetCheckpoints\":[2]",
							"\"targetCheckpoints\":[99]"),
						"vision-test"),
					new InferenceBudgets
					{
						MinimumContextTokens = 65536,
						PlanningMaxOutputTokens = 4096,
						ReviewMaxOutputTokens = 2048
					})
				.AuditAsync(sessionRoot, input, deterministic, CancellationToken.None)
				.GetAwaiter().GetResult(),
			"The multimodal audit accepted an out-of-range checkpoint target.");
		ExpectFailure(
			() => new LlmRoughCutAuditor(
					new StubGenerationClient(
						valid.Replace("\"operationType\":\"trim\"",
							"\"operationType\":\"substitute\""),
						"vision-test"),
					new InferenceBudgets
					{
						MinimumContextTokens = 65536,
						PlanningMaxOutputTokens = 4096,
						ReviewMaxOutputTokens = 2048
					})
				.AuditAsync(sessionRoot, input, deterministic, CancellationToken.None)
				.GetAwaiter().GetResult(),
			"The multimodal audit accepted an unsupported correction operation.");
	}

	private static void TestFallback(
		string sessionRoot,
		RoughCutAuditInput input,
		DateTimeOffset now)
	{
		RoughCutAuditReport report = new RoughCutAuditService(
				new ThrowingAuditor(),
				clock: () => now)
			.RunAsync(sessionRoot, input, CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(report.ModelStatus == "failed-deterministic-fallback" &&
			report.Findings.All(item =>
				item.Source == RoughCutFindingSource.Deterministic) &&
			report.Diagnostics.Single().Contains(
				"synthetic multimodal failure", StringComparison.Ordinal),
			"A multimodal failure did not preserve the deterministic rough-cut report.");
	}

	private static void TestRenderChunks(
		string sessionRoot,
		RoughCutAuditInput input,
		DateTimeOffset now)
	{
		FakeChunkRenderer renderer = new(sessionRoot);
		CandidateTimelineSnapshot longTimeline = new()
		{
			Workspace = input.Timeline.Workspace,
			TimelineStart = TimeSpan.Zero,
			TimelineEnd = TimeSpan.FromSeconds(45)
		};
		string planHash = new string('a', 64);
		RoughCutFullRenderService service = new(
			"audit-session",
			sessionRoot,
			renderer,
			clock: () => now);
		(RoughCutRenderManifest manifest, RoughCutEvidenceReference evidence) =
			service.RenderAsync(longTimeline, planHash, CancellationToken.None)
				.GetAwaiter().GetResult();
		Assert(manifest.Chunks.Select(item => item.Duration.TotalSeconds)
				.SequenceEqual(new[] { 20d, 20d, 5d }) &&
			evidence.Kind == RoughCutEvidenceKind.FullRender &&
			evidence.MediaType == "application/json" &&
			renderer.Calls == 3,
			"The complete rough cut was not rendered as contiguous bounded chunks.");
		RoughCutRenderContractValidator.Validate(manifest);
		service.RenderAsync(longTimeline, planHash, CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(renderer.Calls == 3,
			"A completed rough-cut render was repeated instead of recovered.");
		string corruptOutput = Path.Combine(
			sessionRoot,
			manifest.Chunks[1].OutputRelativePath.Replace(
				'/', Path.DirectorySeparatorChar));
		File.WriteAllBytes(corruptOutput, new byte[] { 0, 1, 2, 3 });
		service.RenderAsync(longTimeline, planHash, CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(renderer.Calls == 4 &&
			Directory.EnumerateFiles(
				Path.Combine(
					sessionRoot,
					"assembly",
					"rough-cut",
					"quarantine",
					manifest.RenderId),
				"*",
				SearchOption.TopDirectoryOnly).Any(),
			"A corrupt cached rough-cut chunk was reused instead of quarantined and rerendered.");
		RecoveringChunkRenderer missingOutput =
			new(sessionRoot);
		new RoughCutFullRenderService(
				"audit-session",
				sessionRoot,
				missingOutput,
				clock: () => now)
			.RenderAsync(
				new CandidateTimelineSnapshot
				{
					Workspace = longTimeline.Workspace,
					TimelineStart = TimeSpan.Zero,
					TimelineEnd = TimeSpan.FromSeconds(5)
				},
				new string('b', 64),
				CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(missingOutput.Calls == 2,
			"A recovered render response with a missing output was not forced through a fresh repair render.");

		RoughCutRenderManifest broken = new()
		{
			SessionId = manifest.SessionId,
			RenderId = "broken",
			Workspace = manifest.Workspace,
			PlanSha256 = manifest.PlanSha256,
			RenderProfileId = manifest.RenderProfileId,
			TimelineStart = manifest.TimelineStart,
			TimelineEnd = manifest.TimelineEnd,
			CompletedUtc = manifest.CompletedUtc,
			Chunks = manifest.Chunks.Select(item => new RoughCutRenderChunk
			{
				ChunkIndex = item.ChunkIndex,
				Start = item.Start,
				Duration = item.Duration,
				OutputRelativePath = item.OutputRelativePath,
				Sha256 = item.Sha256
			}).ToList()
		};
		broken.Chunks[1].Start += TimeSpan.FromMilliseconds(10);
		ExpectFailure(
			() => RoughCutRenderContractValidator.Validate(broken),
			"A full-render manifest accepted a gap between chunks.");
	}

	private static void TestPersistenceAndMilestone(
		string sessionRoot,
		RoughCutAuditReport report,
		DateTimeOffset now)
	{
		RoughCutAuditArtifactStore store = new(sessionRoot, () => now);
		store.SaveReport(report);
		Assert(store.ReadReport(report.ReportId).Findings.Count == report.Findings.Count,
			"The persisted rough-cut report did not round-trip.");
		Assert(store.ReadCurrentReport()?.ReportId == report.ReportId,
			"The current rough-cut report pointer did not round-trip.");
		RoughCutAuditReport changed =
			AutoEditing.Iteration.Contracts.Serialization.ContractSerializer
				.Deserialize<RoughCutAuditReport>(
					AutoEditing.Iteration.Contracts.Serialization.ContractSerializer
						.Serialize(report));
		changed.Summary += " changed";
		ExpectFailure(
			() => store.SaveReport(changed),
			"An immutable rough-cut report was overwritten.");
		ExpectFailure(
			() => store.AcceptMilestone(report.ReportId, "editor"),
			"A rough cut was accepted with unresolved correction proposals.");

		int sequence = 0;
		foreach (RoughCutCorrectionProposal correction in report.Corrections)
		{
			DateTimeOffset decisionTime = now.AddSeconds(++sequence);
			RoughCutCorrectionDecision decision = new()
			{
				SessionId = report.SessionId,
				ReportId = report.ReportId,
				CorrectionId = correction.CorrectionId,
				Disposition = sequence == 1
					? RoughCutCorrectionDisposition.Deferred
					: RoughCutCorrectionDisposition.Rejected,
				Note = "Deterministic test decision.",
				DecidedUtc = decisionTime
			};
			store.SaveCorrectionDecision(decision);
		}
		string first = report.Corrections[0].CorrectionId;
		ExpectFailure(
			() => store.CreateApprovedReopenRequest(report.ReportId, first),
			"A deferred correction reopened an accepted checkpoint.");
		RoughCutCorrectionDecision approval = new()
		{
			SessionId = report.SessionId,
			ReportId = report.ReportId,
			CorrectionId = first,
			Disposition = RoughCutCorrectionDisposition.ApprovedForReopen,
			Note = "Test the correction.",
			DecidedUtc = now.AddMinutes(1)
		};
		store.SaveCorrectionDecision(approval);
		RoughCutCheckpointReopenRequest reopen =
			store.CreateApprovedReopenRequest(report.ReportId, first);
		Assert(reopen.TargetCheckpoints.SequenceEqual(
				report.Corrections[0].TargetCheckpoints) &&
			reopen.ScopedInstruction == report.Corrections[0].Instruction,
			"An approved targeted reopen changed its checkpoint scope or instruction.");
		ExpectFailure(
			() => store.AcceptMilestone(report.ReportId, "editor"),
			"A rough cut was accepted while an approved correction was not applied.");
		approval.Disposition = RoughCutCorrectionDisposition.Applied;
		approval.Note = "Applied and reviewed.";
		approval.DecidedUtc = now.AddMinutes(2);
		store.SaveCorrectionDecision(approval);
		AcceptedRoughCutMilestone milestone =
			store.AcceptMilestone(report.ReportId, "editor");
		Assert(store.ReadAcceptedMilestone(report.ReportId)?.ReportSha256 ==
				milestone.ReportSha256 &&
			milestone.CorrectionDecisions.All(item =>
				item.Disposition is RoughCutCorrectionDisposition.Applied or
					RoughCutCorrectionDisposition.Rejected),
			"The accepted rough-cut milestone was not durable and terminal.");
	}

	private static void TestVisualSamplingAndCorrectionScope(
		RoughCutAuditInput input)
	{
		IReadOnlyList<TimeSpan> samples =
			RoughCutEvidenceCaptureService.CreateSampleTimes(
				TimeSpan.FromSeconds(10),
				TimeSpan.FromSeconds(20));
		Assert(
			samples.Count == 9 &&
			samples.SequenceEqual(samples.OrderBy(value => value)) &&
			samples.All(value =>
				value > TimeSpan.FromSeconds(10) &&
				value < TimeSpan.FromSeconds(20)),
			"Full rough-cut visual samples are not ordered interior evidence.");
		IReadOnlyList<RoughCutEvidenceSample> targeted =
			RoughCutEvidenceCaptureService.CreateSamples(
				input.Timeline,
				input.AcceptedSyncPlan,
				input.PlanningRequest);
		string purposes = string.Join(" ", targeted.Select(item => item.Purpose));
		Assert(targeted.Count <= 9 &&
			targeted.Select(item => item.TimelineTime).SequenceEqual(
				targeted.Select(item => item.TimelineTime).OrderBy(value => value)) &&
			purposes.Contains("overview", StringComparison.Ordinal) &&
			purposes.Contains("before join", StringComparison.Ordinal) &&
			purposes.Contains("after join", StringComparison.Ordinal) &&
			(purposes.Contains("confirmed action", StringComparison.Ordinal) ||
				purposes.Contains("sync ", StringComparison.Ordinal)) &&
			purposes.Contains("reviewed", StringComparison.Ordinal) &&
			purposes.Contains("section", StringComparison.Ordinal),
			"Full rough-cut evidence did not target overview, joins, action, music, and sections.");

		RoughCutCorrectionProposal correction = new()
		{
			CorrectionId = "scope-test",
			TargetCheckpoints = new List<int> { 2 }
		};
		string allowed = input.AcceptedSyncPlan.Montage.Placements
			.OrderBy(item => item.TimelineStartSeconds)
			.ElementAt(1)
			.Clip.FilePath;
		PostSyncRoughCutCoordinator.ValidateCorrectionScope(
			input.AcceptedSyncPlan,
			correction,
			new TimelineAdjustmentDelta
			{
				Changes = new List<TimelineAdjustment>
				{
					new()
					{
						Kind = TimelineAdjustmentKind.SourceTrimChanged,
						ClipPath = allowed,
						Before = 1,
						After = 1.1
					}
				}
			});
		string outside = input.AcceptedSyncPlan.Montage.Placements
			.OrderBy(item => item.TimelineStartSeconds)
			.First()
			.Clip.FilePath;
		ExpectFailure(
			() => PostSyncRoughCutCoordinator.ValidateCorrectionScope(
				input.AcceptedSyncPlan,
				correction,
				new TimelineAdjustmentDelta
				{
					Changes = new List<TimelineAdjustment>
					{
						new()
						{
							Kind = TimelineAdjustmentKind.DurationChanged,
							ClipPath = outside,
							Before = 2,
							After = 2.1
						}
					}
				}),
			"Rough-cut correction scope accepted a change to another checkpoint.");
		ExpectFailure(
			() => PostSyncRoughCutCoordinator.ValidateCorrectionScope(
				input.AcceptedSyncPlan,
				correction,
				new TimelineAdjustmentDelta()),
			"Rough-cut correction scope accepted a no-op application.");
	}

	private static void TestWorkflowRecovery(
		string testRoot,
		RoughCutAuditInput input)
	{
		string root = Path.Combine(testRoot, "rough-cut-recovery-journal");
		Directory.CreateDirectory(root);
		string planHash = Convert.ToHexString(SHA256.HashData(
			System.Text.Encoding.UTF8.GetBytes(
				EditPlanDocumentSerializer.SerializePlan(
					input.AcceptedSyncPlan)))).ToLowerInvariant();
		RoughCutWorkflowRecoveryStore recovery =
			new(root, () => new DateTimeOffset(
				2026, 7, 27, 9, 0, 0, TimeSpan.Zero));
		AssemblyAction action = new()
		{
			ActionId = "approve-fix-1",
			SessionId = input.SessionId,
			Checkpoint = 4,
			ExpectedStateRevision = 12,
			Kind = AssemblyActionKind.ApproveRoughCutCorrection,
			TargetId = "fix-1",
			CreatedUtc = new DateTimeOffset(
				2026, 7, 27, 8, 59, 0, TimeSpan.Zero)
		};
		recovery.Begin(
			action,
			AssemblyPhase.RoughCutReview,
			planHash,
			"report-1");
		Assert(
			recovery.ReadPending(
				input.SessionId, planHash, "report-1")?.Action.ActionId ==
				action.ActionId,
			"A consumed rough-cut action was not recoverable before completion.");
		recovery.Complete(action, "correction-approved");
		Assert(
			recovery.ReadPending(input.SessionId, planHash, "report-1") == null,
			"A completed rough-cut operation was replayed.");
		recovery.SaveContext(new RoughCutWorkflowContext
		{
			SessionId = input.SessionId,
			PlanSha256 = planHash,
			ReportId = "report-1",
			Phase = AssemblyPhase.Paused,
			PhaseBeforePause = AssemblyPhase.RoughCutCorrection,
			ActiveCorrectionId = "fix-1"
		});
		Assert(
			RoughCutWorkflowRecoveryStore.IsRoughCutResumeState(
				root,
				new AssemblySessionState
				{
					SessionId = input.SessionId,
					Phase = AssemblyPhase.Paused
				}),
			"A paused rough-cut correction was not recognized as a rough-cut resume.");
	}

	private static void TestFinalTimelineIdentity(RoughCutAuditInput input)
	{
		CandidateTimelineSnapshot exact = new()
		{
			Workspace = input.Timeline.Workspace,
			TimelineStart = input.Timeline.TimelineStart,
			TimelineEnd = input.Timeline.TimelineEnd,
			Tracks = new[]
			{
				new CandidateTrackSnapshot
				{
					Index = 0,
					Name = "AI candidate video",
					MediaKind = "Video",
					Events = input.AcceptedSyncPlan.Montage.Placements
						.Select((placement, index) =>
							new CandidateEventSnapshot
							{
								PlacementId = "placement-" + (index + 1),
								MediaPath = placement.Clip.FilePath,
								TimelineStart = TimeSpan.FromSeconds(
									placement.TimelineStartSeconds),
								TimelineDuration = TimeSpan.FromSeconds(
									placement.LengthSeconds),
								SourceOffset = TimeSpan.FromSeconds(
									placement.SourceOffsetSeconds)
							})
						.ToList()
				}
			}
		};
		PostSyncRoughCutCoordinator.EnsureTimelineExactlyMatchesAcceptedPlan(
			input.AcceptedSyncPlan,
			exact);
		exact.Tracks[0].Events[0].TimelineStart += TimeSpan.FromMilliseconds(1);
		ExpectFailure(
			() => PostSyncRoughCutCoordinator
				.EnsureTimelineExactlyMatchesAcceptedPlan(
					input.AcceptedSyncPlan,
					exact),
			"A changed final VEGAS timeline was rendered under the accepted-plan hash.");
	}

	private static RoughCutAuditInput CreateInput(string sessionRoot)
	{
		string renderPath = "assembly/rough-cut/test/full-render.json";
		string renderChunkPath = "assembly/rough-cut/test/chunk.mp4";
		string contactPath = "assembly/rough-cut/test/contact.png";
		Write(sessionRoot, renderChunkPath, new byte[] { 5, 6, 7, 8 });
		Write(sessionRoot, contactPath, new byte[] { 1, 2, 3, 4 });
		List<ClipPlacement> placements = new()
		{
			Placement("c1.mp4", "Map A", "XRK", "Triple", 0, 2, 0.2),
			Placement("c2.mp4", "Map A", "XRK", "Triple", 2.3, 6, 4),
			Placement("c3.mp4", "Map B", "XRK", "Quad", 8.3, 2, 0.4),
			Placement("c4.mp4", "Map C", "MORS", "Closer", 10.3, 2, 1)
		};
		MontageSongPlanningInput song = new()
		{
			Mode = MontageSongPlanningMode.ReviewedSongMap,
			SongFingerprint = new string('b', 64),
			SongDurationSeconds = 20,
			Regions = new List<MontageSongPlanningRegion>
			{
				new()
				{
					Id = "main",
					StartSeconds = 0,
					EndSeconds = 14,
					Type = MusicRegionType.Action
				},
				new()
				{
					Id = "peak",
					StartSeconds = 14,
					EndSeconds = 20,
					Type = MusicRegionType.Climax
				}
			},
			Events = new List<MontageSongPlanningEvent>
			{
				Event("event-used", 1, "main", MusicEventType.BuildHit),
				Event("event-unused", 10, "main", MusicEventType.Drop),
				Event("event-peak", 16, "peak", MusicEventType.Drop)
			},
			EventTimelineColumns = new List<string>
			{
				"timeSeconds", "type", "strength", "confidence", "reviewState"
			},
			EventTimeline = new List<List<object>>
			{
				new() { 1d, "BuildHit", 1d, 1d, "Reviewed" },
				new() { 10d, "Drop", 1d, 1d, "Reviewed" },
				new() { 16d, "Drop", 1d, 1d, "Reviewed" }
			}
		};
		EditPlanningRequest request = new()
		{
			RequestId = "rough-cut-request",
			SongPath = "song.wav",
			Clips = placements.Select(item => item.Clip).ToList(),
			SongAnalysis = song,
			CreativeBrief = "Build toward the peak."
		};
		EditPlanDocument plan = new()
		{
			RequestId = request.RequestId,
			PlannerId = "test",
			PlannerVersion = "1",
			Montage = new PreparedMontage
			{
				Placements = placements,
				SongPlan = song,
				SyncAssignments = new List<MontageSyncAssignment>
				{
					new()
					{
						ClipPath = placements[0].Clip.FilePath,
						KillIndex = 0,
						SourceConfirmationTimeSeconds = 0.2,
						MusicEventId = "event-used",
						TimelineTimeSeconds = 0.2
					}
				}
			}
		};
		AssemblySketch sketch = new()
		{
			RequestId = request.RequestId,
			EditorialThesis = "Build from readable engagements into a peak.",
			Sections = new List<AssemblySectionIntent>
			{
				new()
				{
					SectionId = "section-main",
					RegionId = "main",
					EditorialRole = "build",
					EnergyDirection = "rising",
					PacingIntent = "accelerate",
					Rationale = "Support the song."
				},
				new()
				{
					SectionId = "section-peak",
					RegionId = "peak",
					EditorialRole = "peak",
					EnergyDirection = "maximum",
					PacingIntent = "sustain",
					Rationale = "Reserve the closer."
				}
			},
			ClipOrder = placements.Select((item, index) => new AssemblyClipIntent
			{
				Order = index + 1,
				Clip = new AssemblyClipReference
				{
					ReferenceId = AssemblyReferenceIds.ForClipPath(item.Clip.FilePath),
					MediaPath = item.Clip.FilePath
				},
				SectionId = index == placements.Count - 1 ? "section-peak" : "section-main",
				EditorialRole = "progression",
				Rationale = "Test order.",
				Confidence = 0.8
			}).ToList(),
			SyncStrategy = new AssemblySyncStrategy
			{
				Density = "moderate",
				Rationale = "Use major anchors."
			},
			Reservations = new List<AssemblyReservation>
			{
				new()
				{
					ReservationId = "reserve-peak",
					Purpose = "Save the closer for the peak.",
					RegionId = "peak",
					PreferredClipReferenceIds = new[]
					{
						AssemblyReferenceIds.ForClipPath(placements[3].Clip.FilePath)
					}
				}
			}
		};
		CandidateWorkspaceId workspace = new()
		{
			SessionId = "audit-session",
			Iteration = 4,
			Nonce = "roughcut"
		};
		string planHash = Convert.ToHexString(SHA256.HashData(
			System.Text.Encoding.UTF8.GetBytes(
				EditPlanDocumentSerializer.SerializePlan(plan)))).ToLowerInvariant();
		using (FileStream chunkStream =
			File.OpenRead(Path.Combine(sessionRoot, renderChunkPath)))
		{
			RoughCutRenderManifest manifest = new()
			{
				SessionId = "audit-session",
				RenderId = "fixture-render",
				Workspace = workspace,
				PlanSha256 = planHash,
				RenderProfileId = "review-1080p",
				TimelineStart = TimeSpan.Zero,
				TimelineEnd = TimeSpan.FromSeconds(20),
				CompletedUtc = new DateTimeOffset(
					2026, 7, 26, 18, 0, 0, TimeSpan.Zero),
				Chunks = new[]
				{
					new RoughCutRenderChunk
					{
						ChunkIndex = 1,
						Start = TimeSpan.Zero,
						Duration = TimeSpan.FromSeconds(20),
						OutputRelativePath = renderChunkPath,
						Sha256 = Convert.ToHexString(
							SHA256.HashData(chunkStream)).ToLowerInvariant()
					}
				}
			};
			Write(
				sessionRoot,
				renderPath,
				System.Text.Encoding.UTF8.GetBytes(
					AutoEditing.Iteration.Contracts.Serialization.ContractSerializer
						.Serialize(manifest)));
		}
		return new RoughCutAuditInput
		{
			SessionId = "audit-session",
			PlanningRequest = request,
			AcceptedSyncPlan = plan,
			Sketch = sketch,
			Timeline = new CandidateTimelineSnapshot
			{
				Workspace = workspace,
				TimelineStart = TimeSpan.Zero,
				TimelineEnd = TimeSpan.FromSeconds(20)
			},
			Evidence = new[]
			{
				Evidence(
					sessionRoot,
					"full-render-main",
					RoughCutEvidenceKind.FullRender,
					renderPath,
					"application/json"),
				Evidence(
					sessionRoot,
					"contact-sheet-main",
					RoughCutEvidenceKind.ContactSheet,
					contactPath,
					"image/png")
			}
		};
	}

	private static ClipPlacement Placement(
		string path,
		string map,
		string gun,
		string type,
		double start,
		double duration,
		double kill)
	{
		Clip clip = new()
		{
			FilePath = Path.GetFullPath(path),
			Map = map,
			Gun = gun,
			ClipType = type,
			DurationSeconds = duration + 2,
			ShotEvents = new List<ShotEvent>
			{
				ShotEvent.Reviewed(kill, ShotOutcome.Hit, gun)
			}
		};
		return new ClipPlacement
		{
			Clip = clip,
			TimelineStartSeconds = start,
			SourceOffsetSeconds = 0,
			LengthSeconds = duration,
			SpeedProfile = new SpeedProfile(new[]
			{
				new SpeedProfilePoint(0, 1),
				new SpeedProfilePoint(duration, 1)
			})
		};
	}

	private static MontageSongPlanningEvent Event(
		string id,
		double time,
		string region,
		MusicEventType type) =>
		new()
		{
			Id = id,
			SourceTimeSeconds = time,
			EffectiveTimeSeconds = time,
			ContainingRegionId = region,
			MusicalType = type,
			Classification = MontageSongEventClassification.GameplayAnchor,
			Priority = 3,
			IsReviewed = true
		};

	private static RoughCutEvidenceReference Evidence(
		string root,
		string id,
		RoughCutEvidenceKind kind,
		string relative,
		string mediaType)
	{
		using FileStream stream = File.OpenRead(Path.Combine(root, relative));
		return new RoughCutEvidenceReference
		{
			EvidenceId = id,
			Kind = kind,
			RelativePath = relative,
			Sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
			MediaType = mediaType,
			Description = id
		};
	}

	private static void Write(string root, string relative, byte[] bytes)
	{
		string path = Path.Combine(root, relative);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllBytes(path, bytes);
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private static void ExpectFailure(Action action, string message)
	{
		try
		{
			action();
		}
		catch
		{
			return;
		}
		throw new InvalidOperationException(message);
	}

	private sealed class StubGenerationClient : ITextGenerationClient
	{
		private readonly string response;
		private readonly string model;

		public StubGenerationClient(string response, string model)
		{
			this.response = response;
			this.model = model;
		}

		public List<TextGenerationRequest> Requests { get; } = new();

		public Task<TextGenerationResult> GenerateAsync(
			TextGenerationRequest request,
			CancellationToken cancellationToken)
		{
			Requests.Add(request);
			return Task.FromResult(new TextGenerationResult
			{
				Text = response,
				Model = model,
				FinishReason = "stop"
			});
		}
	}

	private sealed class ThrowingAuditor : IRoughCutMultimodalAuditor
	{
		public Task<LlmRoughCutAuditContribution> AuditAsync(
			string sessionRoot,
			RoughCutAuditInput input,
			RoughCutAuditReport deterministicReport,
			CancellationToken cancellationToken) =>
			throw new InvalidOperationException("synthetic multimodal failure");
	}

	private sealed class FakeChunkRenderer : IRoughCutChunkRenderer
	{
		private readonly string sessionRoot;

		public FakeChunkRenderer(string sessionRoot)
		{
			this.sessionRoot = sessionRoot;
		}

		public int Calls { get; private set; }

		public Task<RenderCandidatePreviewResult> RenderAsync(
			CandidateWorkspaceId workspace,
			TimeSpan start,
			TimeSpan duration,
			string outputRelativePath,
			string idempotencyKey,
			CancellationToken cancellationToken)
		{
			Calls++;
			string path = Path.Combine(
				sessionRoot,
				outputRelativePath.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			byte[] bytes = System.Text.Encoding.UTF8.GetBytes(
				$"rough-cut:{start.Ticks}:{duration.Ticks}:{Calls}");
			File.WriteAllBytes(path, bytes);
			return Task.FromResult(new RenderCandidatePreviewResult
			{
				OutputRelativePath = outputRelativePath,
				Sha256 = Convert.ToHexString(
					SHA256.HashData(bytes)).ToLowerInvariant(),
				RenderedDuration = duration,
				RenderProfileId = "review-1080p"
			});
		}
	}

	private sealed class RecoveringChunkRenderer : IRoughCutChunkRenderer
	{
		private readonly string sessionRoot;

		public RecoveringChunkRenderer(string sessionRoot)
		{
			this.sessionRoot = sessionRoot;
		}

		public int Calls { get; private set; }

		public Task<RenderCandidatePreviewResult> RenderAsync(
			CandidateWorkspaceId workspace,
			TimeSpan start,
			TimeSpan duration,
			string outputRelativePath,
			string idempotencyKey,
			CancellationToken cancellationToken)
		{
			Calls++;
			byte[] bytes = System.Text.Encoding.UTF8.GetBytes(
				"recovered-render-response");
			if (Calls > 1)
			{
				string path = Path.Combine(
					sessionRoot,
					outputRelativePath.Replace('/', Path.DirectorySeparatorChar));
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				File.WriteAllBytes(path, bytes);
			}
			return Task.FromResult(new RenderCandidatePreviewResult
			{
				OutputRelativePath = outputRelativePath,
				Sha256 = Convert.ToHexString(
					SHA256.HashData(bytes)).ToLowerInvariant(),
				RenderedDuration = duration,
				RenderProfileId = "review-1080p"
			});
		}
	}
}
