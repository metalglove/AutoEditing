using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using Core.Scripts;
using Newtonsoft.Json.Linq;

namespace Core.Host.Automation
{
	/// <summary>
	/// Framework-free deterministic checks that AnalysisHarness can invoke without
	/// introducing a test-framework dependency into the deployed extension.
	/// </summary>
	public static class VegasAutomationBrokerSelfTests
	{
		public static async Task RunAllAsync()
		{
			string root = Path.Combine(
				Path.GetTempPath(), "AutoEditing-AutomationSelfTest-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				FakeClock clock = new FakeClock(new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero));
				VegasAutomationConfiguration configuration =
					new VegasAutomationConfiguration(root, TimeSpan.FromMinutes(5));
				VegasAutomationJobStore store = new VegasAutomationJobStore(configuration, clock);
				store.EnsureDirectories();

				VegasJobEnvelope first = CreateEnvelope("job-1", "same-operation", clock.UtcNow);
				WriteRequest(configuration, "0001-job-1.json", first);
				int executions = 0;
				VegasAutomationDispatcher dispatcher = new VegasAutomationDispatcher(
					store,
					(envelope, cancellationToken) =>
					{
						executions++;
						return Task.FromResult(new VegasJobResponse
						{
							Status = VegasJobStatus.Completed,
							Result = new JObject { ["executed"] = true }
						});
					},
					clock);

				Assert(await dispatcher.TryProcessNextAsync(CancellationToken.None), "The first request was not processed.");
				Assert(executions == 1, "The handler did not execute exactly once.");

				VegasJobEnvelope duplicate = CreateEnvelope("job-2", "same-operation", clock.UtcNow);
				WriteRequest(configuration, "0002-job-2.json", duplicate);
				Assert(await dispatcher.TryProcessNextAsync(CancellationToken.None), "The duplicate request was not consumed.");
				Assert(executions == 1, "An idempotent duplicate re-executed the handler.");
				Assert(File.Exists(Path.Combine(configuration.ResponsesDirectory, "job-2.response.json")),
					"The duplicate did not receive its own response.");

				VegasJobEnvelope stale = CreateEnvelope("job-3", "stale-operation", clock.UtcNow);
				string staleRequest = WriteRequest(configuration, "0003-job-3.json", stale);
				VegasAutomationClaim staleClaim;
				Assert(store.TryClaim(staleRequest, out staleClaim), "The stale fixture could not be claimed.");
				File.SetLastWriteTimeUtc(staleClaim.RunningPath, clock.UtcNow.UtcDateTime - TimeSpan.FromMinutes(10));
				Assert(store.RecoverStaleClaims() == 1, "The stale claim was not recovered.");
				Assert(File.Exists(Path.Combine(configuration.RequestsDirectory, staleClaim.RequestName)),
					"The stale claim was not returned to requests.");

				await VerifyFailedAttemptsStayRetryableAsync(root, clock);
				await VerifyTypedRequestBridgeAsync(clock);
				await VerifySessionPumpProcessesOnlyOneJobAsync(root, clock);
				VerifyProjectIdentity();
			}
			finally
			{
				if (Directory.Exists(root))
					Directory.Delete(root, true);
			}
		}

		private static async Task VerifyFailedAttemptsStayRetryableAsync(string testRoot, FakeClock clock)
		{
			VegasAutomationConfiguration configuration =
				new VegasAutomationConfiguration(Path.Combine(testRoot, "retry-session"), TimeSpan.FromMinutes(5));
			VegasAutomationJobStore store = new VegasAutomationJobStore(configuration, clock);
			store.EnsureDirectories();
			int executions = 0;
			VegasAutomationDispatcher dispatcher = new VegasAutomationDispatcher(
				store,
				(envelope, cancellationToken) =>
				{
					executions++;
					if (executions == 1)
						throw new InvalidOperationException("Transient VEGAS failure.");
					return Task.FromResult(new VegasJobResponse
					{
						Status = VegasJobStatus.Completed,
						Result = new JObject { ["attempt"] = executions }
					});
				},
				clock);

			WriteRequest(configuration, "0001-retry-1.json", CreateEnvelope("retry-1", "retried-operation", clock.UtcNow));
			Assert(await dispatcher.TryProcessNextAsync(CancellationToken.None), "The failing attempt was not processed.");
			Assert(ReadResponse(configuration, "retry-1").Status == VegasJobStatus.Failed,
				"The failing attempt did not report failure.");
			Assert(store.FindCompletedByIdempotencyKey("retried-operation") == null,
				"A failed attempt was journaled as the idempotent result.");

			WriteRequest(configuration, "0002-retry-2.json", CreateEnvelope("retry-2", "retried-operation", clock.UtcNow));
			Assert(await dispatcher.TryProcessNextAsync(CancellationToken.None), "The retry was not processed.");
			Assert(executions == 2, "A retry after a failed attempt replayed the failure instead of running.");
			Assert(ReadResponse(configuration, "retry-2").Status == VegasJobStatus.Completed,
				"The retry after a failed attempt did not complete.");

			WriteRequest(configuration, "0003-retry-3.json", CreateEnvelope("retry-3", "retried-operation", clock.UtcNow));
			Assert(await dispatcher.TryProcessNextAsync(CancellationToken.None), "The duplicate was not processed.");
			Assert(executions == 2, "A completed operation re-executed after it was journaled.");
		}

		private static VegasJobResponse ReadResponse(VegasAutomationConfiguration configuration, string jobId) =>
			ContractSerializer.Deserialize<VegasJobResponse>(
				File.ReadAllText(Path.Combine(configuration.ResponsesDirectory, jobId + ".response.json")));

		private static void VerifyProjectIdentity()
		{
			string firstPath = Path.Combine(
				Path.GetTempPath(), "AutoEditing", "Project.veg");
			VegasHostIdentity first = VegasProjectIdentity.Create(
				"editing-host", 100, "20.0", firstPath);
			VegasHostIdentity sameProjectNewProcess = VegasProjectIdentity.Create(
				"editing-host", 200, "20.0", firstPath.ToUpperInvariant());
			Assert(
				first.ProjectFingerprint == sameProjectNewProcess.ProjectFingerprint,
				"A saved project identity changed with the VEGAS process.");
			Assert(
				string.Equals(first.ProjectPath, Path.GetFullPath(firstPath),
					StringComparison.OrdinalIgnoreCase),
				"The saved project path was not normalized.");

			VegasHostIdentity unsavedFirst = VegasProjectIdentity.Create(
				"editing-host", 100, "20.0", "");
			VegasHostIdentity unsavedSecond = VegasProjectIdentity.Create(
				"editing-host", 200, "20.0", "");
			Assert(
				unsavedFirst.ProjectFingerprint != unsavedSecond.ProjectFingerprint,
				"An unsaved project was incorrectly treated as recoverable across processes.");
		}

		private static async Task VerifyTypedRequestBridgeAsync(FakeClock clock)
		{
			CandidateWorkspaceId workspace = new CandidateWorkspaceId
			{
				SessionId = "session-1",
				Iteration = 1,
				Nonce = "fixture"
			};
			GetCandidateSnapshotRequest request = new GetCandidateSnapshotRequest { Workspace = workspace };
			JObject payload = JObject.Parse(ContractSerializer.Serialize(request));
			VegasJobEnvelope envelope = CreateEnvelope("bridge-job", "bridge-operation", clock.UtcNow);
			envelope.Payload = payload;
			envelope.PayloadSha256 = ContractHash.Compute(payload);
			envelope.ExpectedProjectFingerprint = "project-1";

			FakeQueryClient queries = new FakeQueryClient(workspace);
			VegasAutomationRequestHandler handler = new VegasAutomationRequestHandler(
				queries,
				() => new VegasHostIdentity { ProjectFingerprint = "project-1" },
				clock);
			VegasJobResponse response =
				await handler.HandleAsync(envelope, CancellationToken.None);
			Assert(response.Status == VegasJobStatus.Completed, "The typed bridge did not complete.");
			Assert(queries.QueryCount == 1, "The typed bridge did not issue exactly one query.");
			Assert(response.Result?["Workspace"]?["SessionId"]?.Value<string>() == "session-1",
				"The typed bridge did not serialize its query result.");

			CaptureCandidatePreviewFramesRequest frameRequest =
				new CaptureCandidatePreviewFramesRequest
				{
					Workspace = workspace,
					TimelineTimes = new[] { TimeSpan.FromSeconds(.5) },
					OutputDirectoryRelativePath = "frames"
				};
			envelope.Operation = VegasOperations.CaptureCandidatePreviewFrames;
			envelope.Payload = JObject.Parse(
				ContractSerializer.Serialize(frameRequest));
			envelope.PayloadSha256 = ContractHash.Compute(envelope.Payload);
			VegasJobResponse frameResponse =
				await handler.HandleAsync(envelope, CancellationToken.None);
			Assert(
				frameResponse.Result?["Frames"]?[0]?["OutputRelativePath"]?
					.Value<string>() == "frames/frame-0001.png",
				"The typed bridge did not map preview-frame capture.");
			Assert(queries.QueryCount == 2,
				"The preview-frame bridge did not issue exactly one query.");

			PromoteCandidateRequest promoteRequest = new PromoteCandidateRequest
			{
				PromotionId = "promotion-1",
				Workspace = workspace,
				ExpectedCandidateSnapshotSha256 = "candidate-hash"
			};
			envelope.Operation = VegasOperations.PromoteCandidate;
			envelope.Payload = JObject.Parse(
				ContractSerializer.Serialize(promoteRequest));
			envelope.PayloadSha256 = ContractHash.Compute(envelope.Payload);
			VegasJobResponse promoteResponse =
				await handler.HandleAsync(envelope, CancellationToken.None);
			Assert(
				promoteResponse.Result?["PromotionId"]?.Value<string>() ==
					"promotion-1",
				"The typed bridge did not route candidate promotion.");

			RollbackCandidatePromotionRequest rollbackRequest =
				new RollbackCandidatePromotionRequest
				{
					Promotion = queries.Promotion
				};
			envelope.Operation = VegasOperations.RollbackCandidatePromotion;
			envelope.Payload = JObject.Parse(
				ContractSerializer.Serialize(rollbackRequest));
			envelope.PayloadSha256 = ContractHash.Compute(envelope.Payload);
			VegasJobResponse rollbackResponse =
				await handler.HandleAsync(envelope, CancellationToken.None);
			Assert(
				rollbackResponse.Result?["RestoredSnapshotSha256"]?
					.Value<string>() == "candidate-hash",
				"The typed bridge did not route promotion rollback.");
			Assert(queries.QueryCount == 4,
				"Promotion bridge operations did not issue exactly one query each.");

			envelope.ExpectedProjectFingerprint = "different-project";
			try
			{
				await handler.HandleAsync(envelope, CancellationToken.None);
				throw new InvalidOperationException("A stale project fingerprint was accepted.");
			}
			catch (InvalidOperationException exception)
			{
				Assert(exception.Message.Contains("fingerprint"),
					"The project mismatch did not produce the expected failure.");
			}
			Assert(queries.QueryCount == 4, "The stale-project request reached the query client.");
		}

		private static async Task VerifySessionPumpProcessesOnlyOneJobAsync(
			string testRoot,
			FakeClock clock)
		{
			string sessionsRoot = Path.Combine(testRoot, "sessions");
			string firstRoot = Path.Combine(sessionsRoot, "a-session");
			string secondRoot = Path.Combine(sessionsRoot, "b-session");
			VegasAutomationConfiguration first =
				new VegasAutomationConfiguration(firstRoot, TimeSpan.FromMinutes(5));
			VegasAutomationConfiguration second =
				new VegasAutomationConfiguration(secondRoot, TimeSpan.FromMinutes(5));
			new VegasAutomationJobStore(first, clock).EnsureDirectories();
			new VegasAutomationJobStore(second, clock).EnsureDirectories();
			WriteRequest(first, "0001-a.json", CreateEnvelope("pump-a", "pump-a", clock.UtcNow));
			WriteRequest(second, "0001-b.json", CreateEnvelope("pump-b", "pump-b", clock.UtcNow));

			int executions = 0;
			VegasAutomationSessionPump pump = new VegasAutomationSessionPump(
				sessionsRoot,
				TimeSpan.FromMinutes(5),
				(envelope, cancellationToken) =>
				{
					executions++;
					return Task.FromResult(new VegasJobResponse
					{
						Status = VegasJobStatus.Completed,
						Result = new JObject { ["job"] = envelope.JobId }
					});
				},
				clock);

			Assert(await pump.PumpOnceAsync(CancellationToken.None), "The session pump found no work.");
			Assert(executions == 1, "One pump call processed more than one session job.");
			Assert(await pump.PumpOnceAsync(CancellationToken.None), "The second session job was not found.");
			Assert(executions == 2, "The second pump call did not process the remaining job.");
			Assert(!await pump.PumpOnceAsync(CancellationToken.None), "An empty pump reported work.");
		}

		private static VegasJobEnvelope CreateEnvelope(
			string jobId,
			string idempotencyKey,
			DateTimeOffset now)
		{
			JObject payload = new JObject { ["workspace"] = "fixture" };
			return new VegasJobEnvelope
			{
				SessionId = "session-1",
				JobId = jobId,
				Sequence = 1,
				IdempotencyKey = idempotencyKey,
				Operation = VegasOperations.GetCandidateSnapshot,
				CreatedUtc = now,
				DeadlineUtc = now + TimeSpan.FromMinutes(10),
				Payload = payload,
				PayloadSha256 = ContractHash.Compute(payload)
			};
		}

		private static string WriteRequest(
			VegasAutomationConfiguration configuration,
			string name,
			VegasJobEnvelope envelope)
		{
			string path = Path.Combine(configuration.RequestsDirectory, name);
			File.WriteAllText(path, ContractSerializer.Serialize(envelope));
			return path;
		}

		private static void Assert(bool condition, string message)
		{
			if (!condition)
				throw new InvalidOperationException(message);
		}

		private sealed class FakeClock : IVegasAutomationClock
		{
			public FakeClock(DateTimeOffset utcNow)
			{
				UtcNow = utcNow;
			}

			public DateTimeOffset UtcNow { get; set; }
		}

		private sealed class FakeQueryClient : IVegasQueryClient
		{
			private readonly CandidateWorkspaceId _workspace;

			public FakeQueryClient(CandidateWorkspaceId workspace)
			{
				_workspace = workspace;
			}

			public int QueryCount { get; private set; }
			public PromoteCandidateResult Promotion { get; private set; }

			public Task<TResult> QueryAsync<TResult>(IVegasQuery<TResult> query)
			{
				QueryCount++;
				if (query is GetCandidateSnapshotCommand)
				{
					object result = new CandidateTimelineSnapshot
					{
						Workspace = _workspace,
						TimelineStart = TimeSpan.Zero,
						TimelineEnd = TimeSpan.FromSeconds(1)
					};
					return Task.FromResult((TResult)result);
				}
				if (query is CaptureCandidatePreviewFramesCommand)
				{
					object result = new CaptureCandidatePreviewFramesResult
					{
						Frames = new[]
						{
							new CapturedCandidatePreviewFrame
							{
								TimelineTime = TimeSpan.FromSeconds(.5),
								OutputRelativePath = "frames/frame-0001.png",
								Sha256 = new string('a', 64)
							}
						}
					};
					return Task.FromResult((TResult)result);
				}
				if (query is PromoteCandidateCommand promote)
				{
					CandidateTimelineSnapshot candidate = new CandidateTimelineSnapshot
					{
						Workspace = _workspace,
						Tracks = new[]
						{
							new CandidateTrackSnapshot
							{
								Name = _workspace.OwnershipPrefix + "|VIDEO",
								MediaKind = "Video"
							}
						}
					};
					CandidateTimelineSnapshot promoted = new CandidateTimelineSnapshot
					{
						Workspace = _workspace,
						Tracks = new[]
						{
							new CandidateTrackSnapshot
							{
								Name = CandidatePromotionContract.FinalVideoTrackName,
								MediaKind = "Video"
							}
						}
					};
					Promotion = new PromoteCandidateResult
					{
						PromotionId = promote.Request.PromotionId,
						Workspace = _workspace,
						CandidateSnapshot = candidate,
						CandidateSnapshotSha256 = "candidate-hash",
						PromotedSnapshot = promoted,
						PromotedSnapshotSha256 = "promoted-hash",
						TrackMappings = CandidatePromotionContract.Plan(
							candidate,
							new string[0])
					};
					return Task.FromResult((TResult)(object)Promotion);
				}
				if (query is RollbackCandidatePromotionCommand rollback)
				{
					object result = new RollbackCandidatePromotionResult
					{
						PromotionId = rollback.Request.Promotion.PromotionId,
						Workspace = _workspace,
						RestoredSnapshot =
							rollback.Request.Promotion.CandidateSnapshot,
						RestoredSnapshotSha256 = "candidate-hash"
					};
					return Task.FromResult((TResult)result);
				}
				throw new InvalidOperationException("Unexpected query type.");
			}
		}
	}
}
