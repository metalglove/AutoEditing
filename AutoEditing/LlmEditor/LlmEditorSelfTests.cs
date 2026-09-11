using Core.Domain.Planning;
using AutoEditing.LlmEditor.Inference;
using AutoEditing.LlmEditor.Iteration;
using AutoEditing.LlmEditor.Planning;
using AutoEditing.LlmEditor.Sessions;
using AutoEditing.LlmEditor.Workbench;
using AutoEditing.LlmEditor.Assembly;
using AutoEditing.LlmEditor.Automation;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.LlmEditor.Finalization;
using System.Net;
using System.Text;

namespace AutoEditing.LlmEditor;

internal static class LlmEditorSelfTests
{
	public static int RunAssemblyWorkflow()
	{
		string root = Path.Combine(
			Path.GetTempPath(),
			"AutoEditing.LlmEditor.AssemblyWorkflowTests",
			Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			EditPlanningRequest request =
				EditPlanDocumentSerializer.DeserializeRequest(ValidRequestJson());
			EditPlanDocument plan = new FakeLlmEditPlanner()
				.CreatePlanAsync(request, CancellationToken.None)
				.GetAwaiter().GetResult();
			AssemblyWorkflowSelfTests.Run(root, request, plan);
			Console.WriteLine("LLM assembly workflow self-tests passed.");
			return 0;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return 1;
		}
		finally
		{
			try { if (Directory.Exists(root)) Directory.Delete(root, true); }
			catch { }
		}
	}

	public static int RunTimelineReconciliation()
	{
		try
		{
			EditPlanningRequest request =
				EditPlanDocumentSerializer.DeserializeRequest(ValidRequestJson());
			EditPlanDocument plan = new FakeLlmEditPlanner()
				.CreatePlanAsync(request, CancellationToken.None)
				.GetAwaiter().GetResult();
			AssemblyWorkflowSelfTests.RunReconciliation(plan);
			Console.WriteLine("LLM timeline reconciliation self-tests passed.");
			return 0;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return 1;
		}
	}

	public static int RunLifecycleRecovery()
	{
		string root = Path.Combine(
			Path.GetTempPath(),
			"AutoEditing.LlmEditor.LifecycleRecoveryTests",
			Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			EditPlanningRequest request =
				EditPlanDocumentSerializer.DeserializeRequest(ValidRequestJson());
			EditPlanDocument plan = new FakeLlmEditPlanner()
				.CreatePlanAsync(request, CancellationToken.None)
				.GetAwaiter().GetResult();
			FinalizationSelfTests.Run(root, plan);
			PolishPassSelfTests.Run(root, plan);
			Console.WriteLine("LLM polish/finalization recovery self-tests passed.");
			return 0;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return 1;
		}
		finally
		{
			try { if (Directory.Exists(root)) Directory.Delete(root, true); }
			catch { }
		}
	}

	public static int RunRecovery()
	{
		string root = Path.Combine(
			Path.GetTempPath(),
			"AutoEditing.LlmEditor.RecoveryTests",
			Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			EditPlanningRequest request =
				EditPlanDocumentSerializer.DeserializeRequest(ValidRequestJson());
			AssemblyActionExecutionStoreSelfTests.Run(root);
			AssemblySteeringDirectiveStoreSelfTests.Run(root);
			SectionMilestoneRenderSelfTests.Run(root);
			AssemblyRecoverySelfTests.Run(root, request);
			Console.WriteLine("LLM assembly recovery self-tests passed.");
			return 0;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return 1;
		}
		finally
		{
			try { if (Directory.Exists(root)) Directory.Delete(root, true); }
			catch { }
		}
	}

	public static int RunVelocityReconciliation()
	{
		try
		{
			EditPlanningRequest request =
				EditPlanDocumentSerializer.DeserializeRequest(ValidRequestJson());
			EditPlanDocument plan = new FakeLlmEditPlanner()
				.CreatePlanAsync(request, CancellationToken.None)
				.GetAwaiter().GetResult();
			AssemblyWorkflowSelfTests.RunVelocityReconciliation(plan);
			Console.WriteLine(
				"LLM timeline velocity reconciliation self-tests passed.");
			return 0;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return 1;
		}
	}

	public static int Run()
	{
		string root = Path.Combine(Path.GetTempPath(), "AutoEditing.LlmEditor.Tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			EditPlanningRequest request = EditPlanDocumentSerializer.DeserializeRequest(ValidRequestJson());
			FakeLlmEditPlanner planner = new FakeLlmEditPlanner();
			EditPlanDocument first = planner.CreatePlanAsync(request, CancellationToken.None).GetAwaiter().GetResult();
			EditPlanDocument second = planner.CreatePlanAsync(request, CancellationToken.None).GetAwaiter().GetResult();
			string firstJson = EditPlanDocumentSerializer.SerializePlan(first);
			string secondJson = EditPlanDocumentSerializer.SerializePlan(second);
			Assert(firstJson == secondJson, "The fake planner output is not deterministic.");

			string firstPath = Path.Combine(root, "first.json");
			string secondPath = Path.Combine(root, "second.json");
			EditPlanDocumentSerializer.WritePlanNew(firstPath, first);
			EditPlanDocumentSerializer.WritePlanNew(secondPath, second);
			Assert(File.ReadAllBytes(firstPath).SequenceEqual(File.ReadAllBytes(secondPath)),
				"Serialized plan bytes differ across identical runs.");

			EditPlanDocument roundTripped = EditPlanDocumentSerializer.ReadPlan(firstPath);
			Assert(roundTripped.RequestId == request.RequestId, "Request correlation did not survive serialization.");
			Assert(roundTripped.Montage.Placements.Count == 1, "The round-tripped plan lost its placement.");

			ExpectFailure(
				() => EditPlanDocumentSerializer.DeserializeRequest(
					ValidRequestJson().Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2")),
				"An unsupported request schema was accepted.");
			EditPlanningRequest missingSongAnalysis =
				EditPlanDocumentSerializer.DeserializeRequest(ValidRequestJson());
			missingSongAnalysis.SongAnalysis = null;
			ExpectFailure(
				() => new LlmEditPlanner(new StubGenerationClient(firstJson, "test-model"))
					.CreatePlanAsync(missingSongAnalysis, CancellationToken.None).GetAwaiter().GetResult(),
				"LLM planning accepted a request without reviewed song analysis.");

			TestLlmPlanner(request, firstJson);
			ProgressiveAssemblyPlannerSelfTests.Run(request);
			TestOpenAiCompatibleClient();
			TestInferenceTranscript(root);
			TestIterationLoop(request, firstJson);
			CandidateReviewerSelfTests.Run(request, first);
			TestSessionPersistence(root);
			TestAssemblyActions(root);
			AssemblyActionStoreSelfTests.Run(root);
			AssemblyActionExecutionStoreSelfTests.Run(root);
			CandidateAbandonCleanupSelfTests.Run();
			Console.WriteLine("self-test: action execution");
			AssemblyRecoverySelfTests.Run(root, request);
			Console.WriteLine("self-test: recovery");
			TestAssemblyReconciliation(first);
			TestAssemblyCoordinator(root, request, first);
			AssemblyWorkflowSelfTests.Run(root, request, first);
			Console.WriteLine("self-test: assembly workflow");
			ClipStepDecisionCompilerSelfTests.Run();
			TestWorkbenchSessionPublisher(root, first);
			WorkbenchIterationExecutionSelfTests.Run(root, request, first);
			CheckpointPreviewPipelineSelfTests.Run(root, request, first);
			WorkbenchProjectionSelfTests.Run(root);
			RoughCutAuditSelfTests.Run(root);
			FinalizationSelfTests.Run(root, first);
			Console.WriteLine("self-test: finalization");
			PolishPassSelfTests.Run(root, first);
			Console.WriteLine("self-test: polish");

			Console.WriteLine("LLM editor self-tests passed.");
			return 0;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return 1;
		}
		finally
		{
			try { if (Directory.Exists(root)) Directory.Delete(root, true); }
			catch { }
		}
	}

	private static void TestAssemblyActions(string testRoot)
	{
		string sessionRoot = Path.Combine(testRoot, "assembly-session");
		Directory.CreateDirectory(sessionRoot);
		AssemblyActionStore store = new(sessionRoot);
		store.PublishState(new AssemblySessionState
		{
			SessionId = "assembly-session",
			Phase = AssemblyPhase.AwaitingHumanReview,
			Checkpoint = 2,
			CurrentClipPath = "fixtures/clip-001.mp4"
		});
		string actions = Path.Combine(sessionRoot, "assembly", "actions");
		Directory.CreateDirectory(actions);
		AssemblyAction action = new()
		{
			ActionId = "accept-2",
			SessionId = "assembly-session",
			Checkpoint = 2,
			ExpectedStateRevision = 7,
			Kind = AssemblyActionKind.AcceptTimelineAndContinue
		};
		File.WriteAllText(
			Path.Combine(actions, "accept-2.json"),
			AutoEditing.Iteration.Contracts.Serialization.ContractSerializer.Serialize(action));
		AssemblyAction? consumed = store.TryConsume(2, "assembly-session", 7);
		Assert(consumed?.Kind == AssemblyActionKind.AcceptTimelineAndContinue,
			"The assembly action was not consumed at its target checkpoint.");
		Assert(store.TryConsume(2, "assembly-session", 7) == null,
			"A consumed assembly action remained pending.");

		AssemblyAction stale = new()
		{
			ActionId = "stale",
			SessionId = "assembly-session",
			Checkpoint = 1,
			Kind = AssemblyActionKind.ResetCurrentClip
		};
		File.WriteAllText(
			Path.Combine(actions, "stale.json"),
			AutoEditing.Iteration.Contracts.Serialization.ContractSerializer.Serialize(stale));
		Assert(store.TryConsume(2, "assembly-session", 7) == null,
			"A stale assembly action was applied to the wrong checkpoint.");
		Assert(Directory.EnumerateFiles(
				Path.Combine(sessionRoot, "assembly", "quarantined"),
				"*.stale-checkpoint*.json").Any(),
			"A stale assembly action was not quarantined.");
	}

	private static void TestAssemblyReconciliation(EditPlanDocument source)
	{
		EditPlanDocument prefix = AssemblyPlanSlices.Prefix(source, 1);
		TimelineAdjustmentDelta delta = AssemblyTimelineReconciler.Apply(
			prefix, new AutoEditing.Iteration.Contracts.Automation.CandidateTimelineSnapshot
		{
			Tracks = new List<AutoEditing.Iteration.Contracts.Automation.CandidateTrackSnapshot>
			{
				new()
				{
					MediaKind = "Video",
					Events = new List<AutoEditing.Iteration.Contracts.Automation.CandidateEventSnapshot>
					{
						new()
						{
							PlacementId = Path.GetFullPath(source.Montage.Placements[0].Clip.FilePath),
							MediaPath = source.Montage.Placements[0].Clip.FilePath,
							TimelineStart = TimeSpan.FromSeconds(0.75),
							TimelineDuration = TimeSpan.FromSeconds(1.5),
							SourceOffset = TimeSpan.FromSeconds(0.25)
						}
					}
				}
			}
		}, 1);
		Core.Domain.Editing.ClipPlacement placement = prefix.Montage.Placements.Single();
		Assert(Math.Abs(placement.TimelineStartSeconds - 0.75) < 0.000001 &&
			Math.Abs(placement.SourceOffsetSeconds - 0.25) < 0.000001 &&
			Math.Abs(placement.LengthSeconds - 1.5) < 0.000001,
			"Manual VEGAS timing changes were not adopted into the assembly checkpoint.");
		Assert(!prefix.Montage.EffectOptions.EnableScreenPumps &&
			!prefix.Montage.EffectOptions.IncludeManualTreatments,
			"The synchronization slice did not disable effect treatments.");
		Assert(delta.Checkpoint == 1 &&
			delta.Changes.Any(item =>
				item.Kind == TimelineAdjustmentKind.TimelineStartChanged) &&
			delta.Changes.Any(item =>
				item.Kind == TimelineAdjustmentKind.SourceTrimChanged),
			"The reconciliation did not return a structured timeline adjustment delta.");
		ExpectFailure(
			() => AssemblyTimelineReconciler.Apply(
				AssemblyPlanSlices.Prefix(source, 1),
				new AutoEditing.Iteration.Contracts.Automation.CandidateTimelineSnapshot()),
			"A deleted expected VEGAS event was silently accepted.");
	}

	private static void TestAssemblyCoordinator(
		string testRoot,
		EditPlanningRequest request,
		EditPlanDocument plan)
	{
		string sessionsRoot = Path.Combine(testRoot, "assembly-coordinator");
		WorkbenchSessionPublisher publisher = WorkbenchSessionPublisher.Create(
			sessionsRoot, "assembly-run");
		publisher.TransitionTo(
			AutoEditing.Iteration.Contracts.Sessions.EditSessionState.Planning,
			"test assembly");
		string actions = Path.Combine(publisher.SessionRoot, "assembly", "actions");
		Directory.CreateDirectory(actions);
		File.WriteAllText(
			Path.Combine(actions, "finish.json"),
			AutoEditing.Iteration.Contracts.Serialization.ContractSerializer.Serialize(
				new AssemblyAction
				{
					ActionId = "finish",
					SessionId = "assembly-run",
					Checkpoint = 1,
					ExpectedStateRevision = 2,
					Kind = AssemblyActionKind.FinishSyncPass
				}));
		FakeAssemblyAutomation automation = new(plan);
		EditPlanDocument result = new AssemblyCoordinator(
				new LlmEditPlanner(
					new StubGenerationClient(ValidDecisionJson(), "test-model")),
				automation, publisher, "assembly-run")
			.RunAsync(request, plan, CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(result.Montage.Placements.Count == 1 &&
			automation.Operations.SequenceEqual(new[]
			{
				AutoEditing.Iteration.Contracts.Automation.VegasOperations.PreflightCandidate,
				AutoEditing.Iteration.Contracts.Automation.VegasOperations.MaterializeCandidate,
				// Materialization is not considered successful until its exact
				// live VEGAS readback reconciles with the requested plan.
				AutoEditing.Iteration.Contracts.Automation.VegasOperations.GetCandidateSnapshot,
				AutoEditing.Iteration.Contracts.Automation.VegasOperations.GetCandidateSnapshot
			}),
			"The single-clip assembly did not pause, accept, and preserve its final workspace. " +
			"Observed operations: " + string.Join(", ", automation.Operations) + ".");
	}

	private static void TestInferenceTranscript(string testRoot)
	{
		string sessionRoot = Path.Combine(testRoot, "transcript-session");
		RecordingTextGenerationClient client = new(
			new FixedTextGenerationClient("{\"reply\":\"kept verbatim\"}"),
			sessionRoot);
		TextGenerationResult result = client.GenerateAsync(
			new TextGenerationRequest
			{
				SystemPrompt = "system context",
				UserPrompt = "reply context",
				JsonSchemaName = "planning response",
				VisualEvidence = new[]
				{
					new VisualEvidence
					{
						DataUrl = "data:image/png;base64,AA==",
						Description = "preview"
					}
				}
			},
			CancellationToken.None).GetAwaiter().GetResult();

		string exchange = Directory.GetDirectories(
			Path.Combine(sessionRoot, "inference", "exchanges")).Single();
		Assert(File.ReadAllText(Path.Combine(exchange, "assistant.txt")) == result.Text,
			"The inference transcript did not preserve the exact assistant response.");
		string request = File.ReadAllText(Path.Combine(exchange, "request.json"));
		Assert(request.Contains("reply context", StringComparison.Ordinal) &&
			!request.Contains("data:image/png;base64", StringComparison.Ordinal),
			"The inference transcript lost prompt context or duplicated embedded image data.");
		Assert(File.Exists(Path.Combine(exchange, "response.json")),
			"The inference transcript did not save response metadata.");
	}

	private static void TestSessionPersistence(string testRoot)
	{
		string sessionRoot = Path.Combine(testRoot, "session");
		SessionPathResolver paths = new SessionPathResolver(sessionRoot);
		string manifestPath = paths.Resolve("iterations/0001/candidate.json");
		AtomicFileWriter writer = new AtomicFileWriter();
		writer.WriteText(manifestPath, "{\"revision\":1}");
		writer.WriteText(manifestPath, "{\"revision\":2}");
		Assert(File.ReadAllText(manifestPath) == "{\"revision\":2}",
			"Atomic replacement did not publish the complete new content.");
		Assert(!Directory.EnumerateFiles(
				Path.GetDirectoryName(manifestPath)!,
				"*.tmp",
				SearchOption.TopDirectoryOnly).Any(),
			"Atomic writing left a temporary artifact behind.");

		ExpectFailure(
			() => paths.Resolve("../outside.json"),
			"A traversal path escaped the session root.");
		ExpectFailure(
			() => paths.Resolve(Path.GetFullPath(Path.Combine(testRoot, "outside.json"))),
			"An absolute artifact path was accepted.");
		Assert(paths.GetRelativePath(manifestPath) == "iterations/0001/candidate.json",
			"Session artifact paths were not normalized.");

		SessionArtifactHasher hasher = new SessionArtifactHasher();
		string hash = hasher.ComputeSha256(manifestPath);
		Assert(hash == "3b23faf0f40b160965c92927d2c09de991a25486c3d3d27063cd72874c158027",
			"Artifact hashing is not deterministic.");
		Assert(hasher.Verify(manifestPath, hash).IsValid,
			"An intact session artifact failed verification.");

		string eventPath = paths.Resolve("events.ndjson");
		DateTimeOffset occurred = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
		SessionEventWriter events = new SessionEventWriter(eventPath);
		Assert(events.Append("session-created", new { revision = 1 }, occurred).Sequence == 1,
			"The first session event has the wrong sequence.");
		Assert(events.Append("planning-started", null, occurred).Sequence == 2,
			"The second session event has the wrong sequence.");
		Assert(SessionRecovery.ReadEvents(eventPath).Select(item => item.Sequence)
				.SequenceEqual(new long[] { 1, 2 }),
			"The append-only event log did not recover its sequence.");
		SessionEventWriter resumedEvents = new SessionEventWriter(eventPath);
		Assert(resumedEvents.Append("planning-completed", new { }, occurred).Sequence == 3,
			"The recovered event writer did not resume the sequence.");

		writer.WriteText(manifestPath, "{\"revision\":3}");
		ArtifactIntegrityResult corrupted = SessionRecovery.VerifyArtifacts(
			paths,
			new[] { new SessionArtifactReference("iterations/0001/candidate.json", hash) },
			hasher).Single();
		Assert(!corrupted.IsValid && corrupted.ErrorCode == "hash-mismatch",
			"Corrupt session artifact content was not detected.");

		File.AppendAllText(eventPath, "{not-json}\n");
		ExpectFailure(
			() => SessionRecovery.ReadEvents(eventPath),
			"A corrupt event-log tail was accepted.");
	}

	private static void TestWorkbenchSessionPublisher(
		string testRoot,
		EditPlanDocument plan)
	{
		string sessionsRoot = Path.Combine(testRoot, "workbench");
		DateTimeOffset now = new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero);
		WorkbenchSessionPublisher publisher = WorkbenchSessionPublisher.Create(
			sessionsRoot,
			"session-a",
			() => now);
		publisher.TransitionTo(EditSessionState.Planning, "planner started");
		publisher.PublishProgress(new EditSessionProgress
		{
			SessionId = "session-a",
			Stage = "Generating plan",
			UpdatedUtc = now,
			Elapsed = TimeSpan.FromSeconds(2),
			IsProcessing = true,
			PromptTokens = 100,
			PromptTokensProcessed = 40,
			GeneratedTokens = 5,
			MaximumGeneratedTokens = 200
		});
		publisher.OnSnapshotAsync(
			new EditIterationSnapshot
			{
				Iteration = 1,
				Phase = "revision-requested",
				Candidate = plan,
				Summary = "First review",
				Decisions = Array.Empty<EditDecisionRecord>()
			},
			CancellationToken.None).GetAwaiter().GetResult();

		string sessionRoot = Path.Combine(sessionsRoot, "session-a");
		EditSessionManifest manifest = AutoEditing.Iteration.Contracts.Serialization.ContractSerializer
			.Deserialize<EditSessionManifest>(File.ReadAllText(Path.Combine(sessionRoot, "manifest.json")));
		Assert(manifest.State == EditSessionState.Planning &&
			manifest.CurrentIteration == 1 &&
			manifest.Metadata["lastPhase"] == "revision-requested",
			"The workbench manifest did not publish the latest iteration.");
		Assert(File.Exists(Path.Combine(sessionRoot, "iterations", "0001", "candidate.json")) &&
			File.Exists(Path.Combine(sessionRoot, "iterations", "0001", "trace.json")) &&
			File.Exists(Path.Combine(sessionRoot, "iterations", "0001", "snapshot.json")),
			"The workbench did not persist its candidate, trace, and canonical snapshot.");
		WorkbenchSession projected = new FileWorkbenchProjectionReader().Read(sessionRoot);
		Assert(projected.Iterations.Count == 1 &&
			projected.Iterations[0].Number == 1 &&
			projected.Iterations[0].Status.HasCandidate,
			"The published workbench session cannot be consumed by its projection reader.");
		Assert(SessionRecovery.ReadEvents(Path.Combine(sessionRoot, "events.ndjson")).Count == 3,
			"The workbench lifecycle event stream is incomplete.");
		EditSessionProgress progress =
			AutoEditing.Iteration.Contracts.Serialization.ContractSerializer
				.Deserialize<EditSessionProgress>(
					File.ReadAllText(Path.Combine(sessionRoot, "progress.json")));
		Assert(progress.PromptTokensProcessed == 40 && progress.GeneratedTokens == 5,
			"The workbench progress heartbeat was not persisted.");

		WorkbenchSessionPublisher reopened = WorkbenchSessionPublisher.Open(
			sessionsRoot,
			"session-a",
			() => now);
		Assert(reopened.SessionRoot == Path.GetFullPath(sessionRoot),
			"The workbench session could not be reopened.");
		ExpectFailure(
			() => WorkbenchSessionPublisher.Create(sessionsRoot, "../escape", () => now),
			"An unsafe workbench session ID was accepted.");
	}

	private static void TestLlmPlanner(EditPlanningRequest request, string validPlanJson)
	{
		string authoredPlanJson = ValidDecisionJson();
		StubGenerationClient initialClient =
			new StubGenerationClient("```json\n" + authoredPlanJson + "\n```", "test-model");
		LlmEditPlanner planner = new LlmEditPlanner(initialClient);
		EditPlanDocument plan = planner.CreatePlanAsync(request, CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(plan.PlannerId == "llm.openai-compatible.decisions", "The LLM planner identity was not recorded.");
		Assert(plan.PlannerVersion == "test-model", "The serving model identity was not recorded.");
		Assert(initialClient.Requests[0].JsonSchema == LlmDecisionSchema.Json,
			"The compact decision JSON schema was not supplied to inference.");
		Assert(initialClient.Requests[0].SystemPrompt.Contains(
				"Priority order:", StringComparison.Ordinal) &&
			initialClient.Requests[0].SystemPrompt.Contains(
				"Never invent media", StringComparison.Ordinal) &&
			initialClient.Requests[0].SystemPrompt.Contains(
				"songAnalysis is the authoritative reviewed musical map", StringComparison.Ordinal),
			"The planner system prompt does not establish evidence priority and capability safety.");
		Assert(initialClient.Requests[0].UserPrompt.Contains(
				"\"songAnalysis\"", StringComparison.Ordinal) &&
			initialClient.Requests[0].UserPrompt.Contains(
				"\"effectiveTimeSeconds\": 1.5", StringComparison.Ordinal) &&
			initialClient.Requests[0].UserPrompt.Contains(
				"\"eventTimelineColumns\"", StringComparison.Ordinal) &&
			initialClient.Requests[0].UserPrompt.Contains(
				"\"Rejected\"", StringComparison.Ordinal),
			"The reviewed song analysis was not included in the model request.");
		Assert(initialClient.Requests[0].UserPrompt.Contains(
				"forensic-cross-editor-v1", StringComparison.Ordinal) &&
			initialClient.Requests[0].UserPrompt.Contains(
				"Findings that must not become invariants", StringComparison.Ordinal),
			"The versioned forensic style profile was not included in the initial prompt.");
		Assert(initialClient.Requests[0].JsonSchemaName == "llm_edit_decisions",
			"The initial inference transcript is not labeled as an edit-plan operation.");
		int requestPosition = initialClient.Requests[0].UserPrompt.IndexOf(
			"ACTUAL VALIDATED PLANNING REQUEST", StringComparison.Ordinal);
		int finalTaskPosition = initialClient.Requests[0].UserPrompt.IndexOf(
			"FINAL TASK", StringComparison.Ordinal);
		Assert(requestPosition >= 0 && finalTaskPosition > requestPosition,
			"The planning prompt does not place the final task after the actual request.");
		Assert(ReferenceEquals(plan.Montage.Placements[0].Clip, request.Clips[0]) &&
			ReferenceEquals(plan.Montage.SongPlan, request.SongAnalysis) &&
			Math.Abs(plan.Montage.Placements[0].TimelineStartSeconds - 0.5) < 0.000001,
			"Compact decisions were not deterministically compiled from canonical request data.");
		StubGenerationClient revisionClient =
			new StubGenerationClient(authoredPlanJson, "test-model");
		new LlmEditPlanner(revisionClient)
			.RevisePlanAsync(
				request,
				plan,
				new EditIterationFeedback
				{
					Summary = "Use the editor adjustment.",
					TimelineAdjustment = new TimelineAdjustmentDelta
					{
						Checkpoint = 1,
						Changes =
						{
							new TimelineAdjustment
							{
								Kind = TimelineAdjustmentKind.TimelineStartChanged,
								ClipPath = request.Clips[0].FilePath,
								Before = 0.5,
								After = 0.75
							}
						}
					}
				},
				1,
				CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(revisionClient.Requests.Single().UserPrompt.Contains(
				"TimelineStartChanged", StringComparison.Ordinal) &&
			revisionClient.Requests[0].UserPrompt.Contains(
				"0.75", StringComparison.Ordinal),
			"The actual VEGAS timeline adjustment was not supplied to revision inference.");

		string foreignPlan = authoredPlanJson.Replace(
			"fixtures/clip-001.mp4",
			"fixtures/not-in-request.mp4",
			StringComparison.Ordinal);
		ExpectFailure(
			() => new LlmEditPlanner(new StubGenerationClient(foreignPlan, "test-model"))
				.CreatePlanAsync(request, CancellationToken.None).GetAwaiter().GetResult(),
			"An LLM plan was allowed to reference a clip outside its request.");

		string inventedAssignmentShape = authoredPlanJson.Replace(
			"\"musicEventId\"", "\"songEventId\"", StringComparison.Ordinal);
		ExpectFailure(
			() => new LlmEditPlanner(new StubGenerationClient(inventedAssignmentShape, "test-model"))
				.CreatePlanAsync(request, CancellationToken.None).GetAwaiter().GetResult(),
			"An invented sync-assignment property was silently accepted.");

		string unavailableAnchor = authoredPlanJson.Replace(
			"event-accent-1", "event-not-in-request", StringComparison.Ordinal);
		ExpectFailure(
			() => new LlmEditPlanner(new StubGenerationClient(unavailableAnchor, "test-model"))
				.CreatePlanAsync(request, CancellationToken.None).GetAwaiter().GetResult(),
			"An unavailable musical event was accepted as a sync target.");

		string forbiddenSpeed = authoredPlanJson.Replace(
			"\"speed\": 1.0", "\"speed\": 1.25", StringComparison.Ordinal);
		ExpectFailure(
			() => new LlmEditPlanner(new StubGenerationClient(forbiddenSpeed, "test-model"))
				.CreatePlanAsync(request, CancellationToken.None).GetAwaiter().GetResult(),
			"A speed change was accepted while speed changes are disabled.");

		Core.Domain.Clip.Clip omittedClip = new()
		{
			FilePath = "fixtures/clip-002.mp4",
			DurationSeconds = 4.0,
			ShotEvents = new List<Core.Domain.Audio.ShotEvent>(request.Clips[0].ShotEvents)
		};
		request.Clips.Add(omittedClip);
		try
		{
			ExpectFailure(
				() => new LlmEditPlanner(new StubGenerationClient(authoredPlanJson, "test-model"))
					.CreatePlanAsync(request, CancellationToken.None).GetAwaiter().GetResult(),
				"A supposedly complete baseline omitted a selected clip.");
		}
		finally
		{
			request.Clips.Remove(omittedClip);
		}
	}

	private static void TestOpenAiCompatibleClient()
	{
		CapturingHandler handler = new CapturingHandler(
			"""{"model":"served-model","choices":[{"message":{"content":"{\"ok\":true}"}}]}""");
		OpenAiCompatibleTextGenerationClient client = new OpenAiCompatibleTextGenerationClient(
			new HttpClient(handler),
			new OpenAiCompatibleOptions
			{
				Endpoint = new Uri("http://localhost:8000/v1/"),
				Model = "configured-model"
			});
		TextGenerationResult result = client.GenerateAsync(
			new TextGenerationRequest
			{
				SystemPrompt = "system",
				UserPrompt = "user",
				VisualEvidence = new[]
				{
					new VisualEvidence
					{
						DataUrl = "data:image/png;base64,AA==",
						Description = "Rendered frame at 3.5 seconds."
					}
				},
				Temperature = 0.1,
				MaxOutputTokens = 42,
				JsonSchemaName = "test_schema",
				JsonSchema = """{"type":"object","additionalProperties":false,"properties":{"ok":{"type":"boolean"}},"required":["ok"]}"""
			},
			CancellationToken.None).GetAwaiter().GetResult();

		Assert(handler.RequestUri?.AbsoluteUri == "http://localhost:8000/v1/chat/completions",
			"The OpenAI-compatible endpoint path is incorrect.");
		Assert(handler.RequestBody?.Contains("\"model\":\"configured-model\"", StringComparison.Ordinal) == true,
			"The configured model was not sent.");
		Assert(handler.RequestBody?.Contains("\"description\":", StringComparison.Ordinal) == false,
			"The image payload contains a nonstandard description field.");
		Assert(handler.RequestBody?.Contains("\"type\":\"image_url\"", StringComparison.Ordinal) == true &&
			handler.RequestBody.Contains("Rendered frame at 3.5 seconds.", StringComparison.Ordinal),
			"The visual evidence was not encoded as standard multimodal message content.");
		Assert(handler.RequestBody?.Contains("\"response_format\"", StringComparison.Ordinal) == true &&
			handler.RequestBody.Contains("\"type\":\"json_schema\"", StringComparison.Ordinal) &&
			handler.RequestBody.Contains("\"name\":\"test_schema\"", StringComparison.Ordinal),
			"The strict response JSON schema was not sent to the inference backend.");
		Assert(result.Text == "{\"ok\":true}", "The response content was not extracted.");
		Assert(result.Model == "served-model", "The served model identity was not extracted.");
	}

	private static void TestIterationLoop(EditPlanningRequest request, string validPlanJson)
	{
		StubGenerationClient client = new StubGenerationClient(
			ValidDecisionJson(),
			"test-model");
		SequenceReviewer reviewer = new SequenceReviewer();
		CapturingObserver observer = new CapturingObserver();
		EditIterationResult result = new EditIterationOrchestrator(
				new LlmEditPlanner(client),
				reviewer,
				observer)
			.RunAsync(request, 3, CancellationToken.None)
			.GetAwaiter().GetResult();

		Assert(result.WasAccepted, "The iteration loop did not return an accepted candidate.");
		Assert(result.Iterations == 2, "The iteration loop did not stop after acceptance.");
		Assert(client.Requests.Count == 2, "The rejected candidate was not revised exactly once.");
		Assert(client.Requests[1].VisualEvidence.Count == 1,
			"The intermediary render evidence was not supplied to the revision.");
		Assert(client.Requests[1].UserPrompt.Contains("framing is too late", StringComparison.Ordinal),
			"The reviewer critique was not supplied to the revision.");
		Assert(client.Requests[1].UserPrompt.Contains("Keep the opener", StringComparison.Ordinal),
			"User steering was not supplied to the revision.");
		Assert(client.Requests[1].UserPrompt.Contains(
				"forensic-cross-editor-v1", StringComparison.Ordinal) &&
			client.Requests[1].JsonSchemaName == "llm_edit_decision_revision" &&
			client.Requests[1].JsonSchema == LlmDecisionSchema.Json,
			"The versioned style evidence was not retained for the revision.");
		Assert(observer.Snapshots.Count == 2 &&
			observer.Snapshots[0].Phase == "revision-requested" &&
			observer.Snapshots[1].Phase == "accepted",
			"The observable iteration trace is incomplete.");
	}

	private static void ExpectFailure(Action action, string failureMessage)
	{
		try
		{
			action();
		}
		catch
		{
			return;
		}
		throw new InvalidOperationException(failureMessage);
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private static string ValidRequestJson()
	{
		return """
		{
		  "schemaVersion": 1,
		  "requestId": "skeleton-self-test",
		  "clips": [
		    {
		      "filePath": "fixtures/clip-001.mp4",
		      "durationSeconds": 4.0,
		      "shotEvents": [
		        {
		          "sourceMuzzleTimeSeconds": 1.0,
		          "sourceConfirmationTimeSeconds": 1.0,
		          "outcome": "Hit",
		          "confidence": 1.0,
		          "templateId": "manual",
		          "reviewState": "Reviewed",
		          "origin": "UserMarked",
		          "gun": "XRK"
		        }
		      ]
		    }
		  ],
		  "songPath": "fixtures/song.wav",
		  "songAnalysis": {
		    "mode": "ReviewedSongMap",
		    "songFingerprint": "fixture-song-fingerprint",
		    "songDurationSeconds": 10.0,
		    "regions": [
		      {
		        "id": "region-intro",
		        "startSeconds": 0.0,
		        "endSeconds": 10.0,
		        "type": "Intro",
		        "isLocked": true
		      }
		    ],
		    "events": [
		      {
		        "id": "event-accent-1",
		        "sourceTimeSeconds": 1.5,
		        "effectiveTimeSeconds": 1.5,
		        "containingRegionId": "region-intro",
		        "musicalType": "Accent",
		        "classification": "GameplayAnchor",
		        "uses": ["GameplayAnchor"],
		        "priority": 80,
		        "isLocked": true,
		        "intensity": 0.8,
		        "isSuggestedGameplayAnchor": false,
		        "isReviewed": true
		      }
		    ],
		    "eventTimelineColumns": [
		      "timeSeconds",
		      "type",
		      "strength",
		      "confidence",
		      "reviewState"
		    ],
		    "eventTimeline": [
		      [1.5, "Accent", 0.8, 0.95, "Reviewed"],
		      [2.0, "Beat", 0.5, 0.7, "Rejected"]
		    ],
		    "diagnostics": []
		  },
		  "effectOptions": {
		    "schemaVersion": 1,
		    "presetId": "autoediting.none",
		    "intensity": 1.0,
		    "density": 1.0,
		    "includeManualTreatments": true,
		    "enableScreenPumps": false,
		    "enableFlashes": false,
		    "enableShake": false,
		    "enableTransitions": false,
		    "enableTitles": false,
		    "enableSpeedChanges": false
		  },
		  "creativeBrief": "Contract-only deterministic skeleton.",
		  "styleProfileIds": ["editor-1"]
		}
		""";
	}

	private static string ValidDecisionJson()
	{
		return """
		{
		  "schemaVersion": 1,
		  "requestId": "skeleton-self-test",
		  "placements": [
		    {
		      "clipPath": "fixtures/clip-001.mp4",
		      "sourceStartSeconds": 0.0,
		      "sourceEndSeconds": 2.0,
		      "speed": 1.0,
		      "primarySync": {
		        "killIndex": 0,
		        "musicEventId": "event-accent-1"
		      },
		      "additionalSyncs": []
		    }
		  ],
		  "diagnostics": [
		    {
		      "severity": "Info",
		      "code": "TEST_DECISION",
		      "message": "Compact decision fixture."
		    }
		  ]
		}
		""";
	}

	private sealed class StubGenerationClient : ITextGenerationClient
	{
		private readonly TextGenerationResult result;

		public StubGenerationClient(string text, string model)
		{
			result = new TextGenerationResult { Text = text, Model = model };
		}

		public List<TextGenerationRequest> Requests { get; } = new();

		public Task<TextGenerationResult> GenerateAsync(
			TextGenerationRequest request,
			CancellationToken cancellationToken)
		{
			Requests.Add(request);
			return Task.FromResult(result);
		}
	}

	private sealed class SequenceReviewer : IEditPlanReviewer
	{
		private int calls;

		public Task<EditIterationFeedback> ReviewAsync(
			EditPlanningRequest request,
			EditPlanDocument candidate,
			int iteration,
			CancellationToken cancellationToken)
		{
			calls++;
			if (calls == 1)
			{
				return Task.FromResult(new EditIterationFeedback
				{
					IsAccepted = false,
					Summary = "The impact framing is too late.",
					VisualEvidence = new[]
					{
						new VisualEvidence
						{
							DataUrl = "data:image/png;base64,AA==",
							Description = "Contact sheet from intermediary render."
						}
					},
					SteeringInstructions = new[] { "Keep the opener unchanged." },
					Decisions = new[]
					{
						new EditDecisionRecord
						{
							DecisionId = "timing-001",
							Category = "sync",
							Summary = "Move the impact closer to the musical accent.",
							Confidence = 0.82,
							EvidenceIds = new[] { "preview@00:03.500" }
						}
					}
				});
			}
			return Task.FromResult(new EditIterationFeedback
			{
				IsAccepted = true,
				Summary = "Candidate passed review."
			});
		}
	}

	private sealed class CapturingObserver : IEditIterationObserver
	{
		public List<EditIterationSnapshot> Snapshots { get; } = new();

		public Task OnSnapshotAsync(
			EditIterationSnapshot snapshot,
			CancellationToken cancellationToken)
		{
			Snapshots.Add(snapshot);
			return Task.CompletedTask;
		}
	}

	private sealed class CapturingHandler : HttpMessageHandler
	{
		private readonly string responseBody;

		public CapturingHandler(string responseBody)
		{
			this.responseBody = responseBody;
		}

		public Uri? RequestUri { get; private set; }

		public string? RequestBody { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			RequestUri = request.RequestUri;
			RequestBody = request.Content == null
				? null
				: await request.Content.ReadAsStringAsync(cancellationToken);
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
			};
		}
	}

	private sealed class FixedTextGenerationClient : ITextGenerationClient
	{
		private readonly string response;

		public FixedTextGenerationClient(string response)
		{
			this.response = response;
		}

		public Task<TextGenerationResult> GenerateAsync(
			TextGenerationRequest request,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(new TextGenerationResult
			{
				Text = response,
				Model = "test-model",
				ResponseId = "response-1",
				FinishReason = "stop",
				Usage = new TextGenerationUsage
				{
					PromptTokens = 10,
					CompletionTokens = 5,
					TotalTokens = 15
				}
			});
		}
	}

	private sealed class FakeAssemblyAutomation : IVegasAutomationClient
	{
		private readonly EditPlanDocument plan;

		public FakeAssemblyAutomation(EditPlanDocument plan)
		{
			this.plan = plan;
		}

		public List<string> Operations { get; } = new();

		public Task<TResult> ExecuteAsync<TRequest, TResult>(
			string operation,
			TRequest request,
			string idempotencyKey,
			TimeSpan? timeout = null,
			CancellationToken cancellationToken = default)
		{
			Operations.Add(operation);
			object result = operation switch
			{
				AutoEditing.Iteration.Contracts.Automation.VegasOperations.PreflightCandidate =>
					new AutoEditing.Iteration.Contracts.Automation.PreflightCandidateResult
					{
						IsReady = true
					},
				AutoEditing.Iteration.Contracts.Automation.VegasOperations.MaterializeCandidate =>
					new AutoEditing.Iteration.Contracts.Automation.MaterializeCandidateResult
					{
						CreatedEventCount = 1
					},
				AutoEditing.Iteration.Contracts.Automation.VegasOperations.GetCandidateSnapshot =>
					Snapshot(
						((AutoEditing.Iteration.Contracts.Automation
							.GetCandidateSnapshotRequest)(object)request!)
							.Workspace),
				AutoEditing.Iteration.Contracts.Automation.VegasOperations.CleanupCandidate =>
					new AutoEditing.Iteration.Contracts.Automation.CleanupCandidateResult(),
				_ => throw new InvalidOperationException("Unexpected operation " + operation)
			};
			return Task.FromResult((TResult)result);
		}

		private AutoEditing.Iteration.Contracts.Automation.CandidateTimelineSnapshot Snapshot(
			AutoEditing.Iteration.Contracts.Automation.CandidateWorkspaceId workspace)
		{
			Core.Domain.Editing.ClipPlacement placement = plan.Montage.Placements[0];
			return new AutoEditing.Iteration.Contracts.Automation.CandidateTimelineSnapshot
			{
				Workspace = workspace,
				Tracks = new List<AutoEditing.Iteration.Contracts.Automation.CandidateTrackSnapshot>
				{
					new()
					{
						MediaKind = "Video",
						Events = new List<AutoEditing.Iteration.Contracts.Automation.CandidateEventSnapshot>
						{
							new()
							{
								PlacementId = Path.GetFullPath(placement.Clip.FilePath),
								MediaPath = placement.Clip.FilePath,
								TimelineStart = TimeSpan.FromSeconds(placement.TimelineStartSeconds),
								TimelineDuration = TimeSpan.FromSeconds(placement.LengthSeconds),
								SourceOffset = TimeSpan.FromSeconds(placement.SourceOffsetSeconds)
							}
						}
					}
				}
			};
		}
	}
}
