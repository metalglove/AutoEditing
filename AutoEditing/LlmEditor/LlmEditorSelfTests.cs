using Core.Domain.Planning;
using AutoEditing.LlmEditor.Inference;
using AutoEditing.LlmEditor.Iteration;
using AutoEditing.LlmEditor.Planning;
using System.Net;
using System.Text;

namespace AutoEditing.LlmEditor;

internal static class LlmEditorSelfTests
{
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

			TestLlmPlanner(request, firstJson);
			TestOpenAiCompatibleClient();
			TestIterationLoop(request, firstJson);

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

	private static void TestLlmPlanner(EditPlanningRequest request, string validPlanJson)
	{
		StubGenerationClient initialClient =
			new StubGenerationClient("```json\n" + validPlanJson + "\n```", "test-model");
		LlmEditPlanner planner = new LlmEditPlanner(initialClient);
		EditPlanDocument plan = planner.CreatePlanAsync(request, CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(plan.PlannerId == "llm.openai-compatible", "The LLM planner identity was not recorded.");
		Assert(plan.PlannerVersion == "test-model", "The serving model identity was not recorded.");
		Assert(initialClient.Requests[0].UserPrompt
			.Contains("\"montage\"", StringComparison.Ordinal),
			"The initial planning prompt did not include an output-contract example.");

		string alteredMetadataPlan = validPlanJson.Replace(
			"\"durationSeconds\": 4.0",
			"\"durationSeconds\": 999.0",
			StringComparison.Ordinal);
		Assert(alteredMetadataPlan != validPlanJson, "The metadata-tampering fixture was not changed.");
		EditPlanDocument canonicalizedPlan = new LlmEditPlanner(
				new StubGenerationClient(alteredMetadataPlan, "test-model"))
			.CreatePlanAsync(request, CancellationToken.None).GetAwaiter().GetResult();
		Assert(ReferenceEquals(canonicalizedPlan.Montage.Placements[0].Clip, request.Clips[0]) &&
			canonicalizedPlan.Montage.Placements[0].Clip.DurationSeconds == 4.0,
			"Model-authored clip metadata was not replaced with validated request data.");

		string foreignPlan = validPlanJson.Replace(
			"fixtures/clip-001.mp4",
			"fixtures/not-in-request.mp4",
			StringComparison.Ordinal);
		ExpectFailure(
			() => new LlmEditPlanner(new StubGenerationClient(foreignPlan, "test-model"))
				.CreatePlanAsync(request, CancellationToken.None).GetAwaiter().GetResult(),
			"An LLM plan was allowed to reference a clip outside its request.");
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
				MaxOutputTokens = 42
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
		Assert(result.Text == "{\"ok\":true}", "The response content was not extracted.");
		Assert(result.Model == "served-model", "The served model identity was not extracted.");
	}

	private static void TestIterationLoop(EditPlanningRequest request, string validPlanJson)
	{
		StubGenerationClient client = new StubGenerationClient(validPlanJson, "test-model");
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
		      "shotEvents": []
		    }
		  ],
		  "songPath": "fixtures/song.wav",
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
}
