using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Automation;
using AutoEditing.LlmEditor.Sessions;
using Newtonsoft.Json.Linq;

namespace AutoEditing.LlmEditor;

internal static class AutomationClientSelfTests
{
	public static async Task RunAsync()
	{
		string root = Path.Combine(Path.GetTempPath(), "autoediting-automation-client-" + Guid.NewGuid().ToString("N"));
		try
		{
			VegasAutomationClientOptions options = new()
			{
				SpoolRoot = root,
				SessionId = "session-a",
				DefaultTimeout = TimeSpan.FromSeconds(2),
				PollInterval = TimeSpan.FromMilliseconds(10)
			};
			FileVegasAutomationClient client = new(options);
			Task<TestResult> first = client.ExecuteAsync<TestRequest, TestResult>(
				VegasOperations.GetCandidateSnapshot,
				new TestRequest
				{
					Value = 7,
					Duration = TimeSpan.FromSeconds(12.9045563)
				},
				"same-request");
			VegasJobEnvelope firstEnvelope = await WaitForEnvelope(root);
			Assert(firstEnvelope.Sequence == 1, "First automation sequence was not one.");
			Assert(firstEnvelope.PayloadSha256 == ContractHash.Compute(firstEnvelope.Payload),
				"Published request payload hash was not canonical.");
			VegasJobEnvelope transportedEnvelope =
				ContractSerializer.Deserialize<VegasJobEnvelope>(
					ContractSerializer.Serialize(firstEnvelope));
			Assert(
				transportedEnvelope.PayloadSha256 ==
					ContractHash.Compute(transportedEnvelope.Payload),
				"A request containing TimeSpan values changed hash across JSON transport.");
			Assert(!Directory.EnumerateFiles(Path.Combine(root, "ipc", "requests"), "*.tmp").Any(),
				"Atomic request publication left a temporary file.");
			WriteResponse(root, firstEnvelope, new TestResult { Value = 8 });
			Assert((await first).Value == 8, "Completed automation result was not returned.");
			Assert(
				client.LastHost?.ProjectFingerprint == "project-fixture" &&
				client.ExpectedProjectFingerprint == "project-fixture",
				"The automation client did not retain the responding VEGAS host identity.");

			TestResult reused = await client.ExecuteAsync<TestRequest, TestResult>(
				VegasOperations.GetCandidateSnapshot,
				new TestRequest
				{
					Value = 7,
					Duration = TimeSpan.FromSeconds(12.9045563)
				},
				"same-request");
			Assert(reused.Value == 8, "Idempotent submission did not reuse its completed response.");
			Assert(Directory.EnumerateFiles(Path.Combine(root, "submissions"), "*.json").Count() == 1,
				"Idempotent submission allocated a second job.");

			Task<TestResult> second = client.ExecuteAsync<TestRequest, TestResult>(
				VegasOperations.GetCandidateSnapshot,
				new TestRequest { Value = 9 },
				"second-request");
			VegasJobEnvelope secondEnvelope = await WaitForEnvelope(root, expectedCount: 2);
			Assert(secondEnvelope.Sequence == 2, "Unique automation submission did not advance sequence.");
			Assert(
				secondEnvelope.ExpectedProjectFingerprint == "project-fixture",
				"The first successful response did not pin later automation requests to its project.");
			WriteResponse(root, secondEnvelope, new TestResult { Value = 10 });
			await second;

			Task<TestResult> tampered = client.ExecuteAsync<TestRequest, TestResult>(
				VegasOperations.GetCandidateSnapshot,
				new TestRequest { Value = 11 },
				"tampered");
			VegasJobEnvelope tamperedEnvelope = await WaitForEnvelope(root, expectedCount: 3);
			WriteResponse(root, tamperedEnvelope, new TestResult { Value = 12 }, hash: "bad");
			await AssertThrowsAsync<InvalidDataException>(async () => await tampered,
				"Tampered response hash was accepted.");

			using CancellationTokenSource cancellation = new();
			Task<TestResult> cancelled = client.ExecuteAsync<TestRequest, TestResult>(
				VegasOperations.GetCandidateSnapshot,
				new TestRequest { Value = 13 },
				"cancelled",
				cancellationToken: cancellation.Token);
			await WaitForEnvelope(root, expectedCount: 4);
			cancellation.Cancel();
			await AssertThrowsAsync<OperationCanceledException>(async () => await cancelled,
				"Cancellation did not stop response polling.");

			// A failed attempt is retried as a fresh request instead of replaying the failure.
			Task<TestResult> failing = client.ExecuteAsync<TestRequest, TestResult>(
				VegasOperations.GetCandidateSnapshot,
				new TestRequest { Value = 15 },
				"retry-after-failure");
			VegasJobEnvelope failedAttempt = await WaitForEnvelope(root, expectedCount: 5);
			WriteFailure(root, failedAttempt, VegasJobStatus.Failed);
			await AssertThrowsAsync<VegasAutomationException>(async () => await failing,
				"A failed VEGAS response was not surfaced.");
			Task<TestResult> retried = client.ExecuteAsync<TestRequest, TestResult>(
				VegasOperations.GetCandidateSnapshot,
				new TestRequest { Value = 15 },
				"retry-after-failure");
			VegasJobEnvelope retryAttempt = await WaitForNewerAttempt(root, failedAttempt);
			Assert(retryAttempt.JobId == failedAttempt.JobId && retryAttempt.DeadlineUtc > failedAttempt.DeadlineUtc,
				"A retry after a failed attempt did not publish a fresh attempt.");
			WriteResponse(root, retryAttempt, new TestResult { Value = 16 });
			Assert((await retried).Value == 16, "A retry after a failed attempt replayed the failure.");

			// A request that timed out before VEGAS ran it is re-published with a new deadline.
			Task<TestResult> unanswered = client.ExecuteAsync<TestRequest, TestResult>(
				VegasOperations.GetCandidateSnapshot,
				new TestRequest { Value = 17 },
				"retry-after-expiry",
				timeout: TimeSpan.FromMilliseconds(100));
			await AssertThrowsAsync<TimeoutException>(async () => await unanswered,
				"An unanswered request did not time out.");
			VegasJobEnvelope expiredAttempt = await WaitForEnvelope(root, expectedCount: 6);
			Task<TestResult> resubmitted = client.ExecuteAsync<TestRequest, TestResult>(
				VegasOperations.GetCandidateSnapshot,
				new TestRequest { Value = 17 },
				"retry-after-expiry");
			VegasJobEnvelope freshAttempt = await WaitForNewerAttempt(root, expiredAttempt);
			VegasJobEnvelope spooled = ContractSerializer.Deserialize<VegasJobEnvelope>(
				File.ReadAllText(Path.Combine(root, "ipc", "requests", freshAttempt.JobId + ".json")));
			Assert(freshAttempt.DeadlineUtc > expiredAttempt.DeadlineUtc && spooled.DeadlineUtc == freshAttempt.DeadlineUtc,
				"An expired request was re-spooled with its stale deadline.");
			WriteResponse(root, freshAttempt, new TestResult { Value = 18 });
			Assert((await resubmitted).Value == 18, "A retry after an expired request did not complete.");
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	private static async Task<VegasJobEnvelope> WaitForEnvelope(string root, int expectedCount = 1)
	{
		string directory = Path.Combine(root, "submissions");
		for (int attempt = 0; attempt < 100; attempt++)
		{
			string[] files = Directory.Exists(directory)
				? Directory.GetFiles(directory, "*.json")
				: Array.Empty<string>();
			if (files.Length >= expectedCount)
			{
				return files.Select(path => ContractSerializer.Deserialize<VegasJobEnvelope>(File.ReadAllText(path)))
					.OrderBy(item => item.Sequence)
					.Last();
			}
			await Task.Delay(10);
		}
		throw new TimeoutException("Automation request was not published.");
	}

	private static async Task<VegasJobEnvelope> WaitForNewerAttempt(string root, VegasJobEnvelope previous)
	{
		string path = Path.Combine(root, "submissions", previous.JobId + ".json");
		for (int attempt = 0; attempt < 100; attempt++)
		{
			try
			{
				VegasJobEnvelope current = ContractSerializer.Deserialize<VegasJobEnvelope>(File.ReadAllText(path));
				if (current.Sequence > previous.Sequence) return current;
			}
			catch (IOException)
			{
				// The submission is being replaced atomically; read it again.
			}
			await Task.Delay(10);
		}
		throw new TimeoutException("The retry was not published as a fresh attempt.");
	}

	private static void WriteFailure(string root, VegasJobEnvelope envelope, VegasJobStatus status)
	{
		VegasJobResponse response = new()
		{
			SessionId = envelope.SessionId,
			JobId = envelope.JobId,
			Status = status,
			StartedUtc = DateTimeOffset.UtcNow,
			CompletedUtc = DateTimeOffset.UtcNow,
			Error = new AutomationError
			{
				Code = "dispatch-failed",
				Stage = "dispatch",
				Message = "Fixture failure.",
				IsTransient = true
			}
		};
		new AtomicFileWriter().WriteText(
			Path.Combine(root, "ipc", "responses", envelope.JobId + ".response.json"),
			ContractSerializer.Serialize(response));
	}

	private static void WriteResponse(
		string root,
		VegasJobEnvelope envelope,
		TestResult result,
		string? hash = null)
	{
		JToken token = JToken.FromObject(result);
		VegasJobResponse response = new()
		{
			SessionId = envelope.SessionId,
			JobId = envelope.JobId,
			Status = VegasJobStatus.Completed,
			StartedUtc = DateTimeOffset.UtcNow,
			CompletedUtc = DateTimeOffset.UtcNow,
			Host = new VegasHostIdentity
			{
				MachineName = "fixture-host",
				ProcessId = 42,
				VegasVersion = "20.0",
				ProjectPath = @"C:\fixture.veg",
				ProjectFingerprint = "project-fixture"
			},
			Result = token,
			ResultSha256 = hash ?? ContractHash.Compute(token)
		};
		new AtomicFileWriter().WriteText(
			Path.Combine(root, "ipc", "responses", envelope.JobId + ".response.json"),
			ContractSerializer.Serialize(response));
	}

	private static async Task AssertThrowsAsync<TException>(Func<Task> action, string message)
		where TException : Exception
	{
		try { await action(); }
		catch (TException) { return; }
		throw new InvalidOperationException(message);
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private sealed class TestRequest
	{
		public int Value { get; set; }
		public TimeSpan Duration { get; set; }
	}
	private sealed class TestResult { public int Value { get; set; } }
}
