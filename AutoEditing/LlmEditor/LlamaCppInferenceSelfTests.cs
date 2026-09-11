using AutoEditing.LlmEditor.Inference;
using AutoEditing.LlmEditor.Workbench;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Configuration;
using AutoEditing.Iteration.Contracts.Sessions;
using System.Net;
using System.Text;
using System.Text.Json;

namespace AutoEditing.LlmEditor;

internal static class LlamaCppInferenceSelfTests
{
	public static int Run()
	{
		try
		{
			TestSchemaMultimodalAndDiagnostics();
			TestOpenAiProviderPayload();
			TestDeepSeekProviderPayloadAndReasoningStream();
			TestProviderSettingsPrecedence();
			TestProviderSettingsDefaultModelMigration();
			TestStreamingGeneration();
			TestOperationSpecificProgressStages();
			TestHealthAndProps();
			TestTransientRetry();
			TestDurableUsageTelemetry();
			TestPermanentFailureIsNotRetried();
			TestTruncationRejected();
			TestInferenceBudgets();
			TestEnvironmentFile();
			Console.WriteLine("llama.cpp inference self-tests passed.");
			return 0;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return 1;
		}
	}

	private static void TestProviderSettingsDefaultModelMigration()
	{
		string root = Path.Combine(
			Path.GetTempPath(),
			"AutoEditing-ProviderMigration-" + Guid.NewGuid().ToString("N"));
		string path = Path.Combine(root, "inference.json");
		try
		{
			Directory.CreateDirectory(root);
			File.WriteAllText(
				path,
				"""
				{
				  "SchemaVersion": 1,
				  "Provider": "openai",
				  "LlamaCppEndpoint": "http://localhost:8080/v1/",
				  "LlamaCppModel": "",
				  "OpenAiEndpoint": "https://api.openai.com/v1/",
				  "OpenAiModel": "gpt-5.6-sol",
				  "OpenAiCredentialTarget": "AutoEditing/Inference/OpenAI"
				}
				""");
			InferenceProviderSettings migrated =
				InferenceProviderSettingsStore.LoadOrDefault(path);
			Assert(
				migrated.SchemaVersion ==
					InferenceProviderSettings.CurrentSchemaVersion &&
				migrated.OpenAiModel == "gpt-5.6-luna",
				"The previous default OpenAI model was not migrated to gpt-5.6-luna.");
			InferenceProviderSettings reloaded =
				InferenceProviderSettingsStore.LoadOrDefault(path);
			Assert(
				reloaded.OpenAiModel == "gpt-5.6-luna",
				"The migrated OpenAI model was not persisted.");
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	private static void TestProviderSettingsPrecedence()
	{
		string root = Path.Combine(
			Path.GetTempPath(),
			"AutoEditing-ProviderTest-" + Guid.NewGuid().ToString("N"));
		string path = Path.Combine(root, "inference.json");
		string[] names =
		{
			"AUTOEDITING_LLM_PROVIDER",
			"AUTOEDITING_LLM_ENDPOINT",
			"AUTOEDITING_LLM_MODEL",
			"AUTOEDITING_LLM_API_KEY"
		};
		Dictionary<string, string?> original = names.ToDictionary(
			name => name,
			Environment.GetEnvironmentVariable);
		try
		{
			InferenceProviderSettingsStore.Save(
				new InferenceProviderSettings
				{
					Provider = InferenceProviderIds.OpenAi,
					LlamaCppEndpoint = "http://localhost:8080/v1/",
					LlamaCppModel = "local-settings-model",
					OpenAiEndpoint = "https://api.openai.com/v1/",
					OpenAiModel = "openai-settings-model"
				},
				path);
			Environment.SetEnvironmentVariable(
				"AUTOEDITING_LLM_PROVIDER", null);
			Environment.SetEnvironmentVariable(
				"AUTOEDITING_LLM_ENDPOINT", "http://legacy-local:8080/v1/");
			Environment.SetEnvironmentVariable(
				"AUTOEDITING_LLM_MODEL", "legacy-local-model");
			Environment.SetEnvironmentVariable(
				"AUTOEDITING_LLM_API_KEY", "test-key");

			OpenAiCompatibleOptions fromUi =
				OpenAiCompatibleOptions.FromEnvironment(path);
			Assert(
				!fromUi.IsLlamaCpp &&
				fromUi.Endpoint.AbsoluteUri ==
					"https://api.openai.com/v1/" &&
				fromUi.Model == "openai-settings-model",
				"Legacy llama.cpp environment values overrode the UI OpenAI selection.");

			Environment.SetEnvironmentVariable(
				"AUTOEDITING_LLM_PROVIDER", InferenceProviderIds.OpenAi);
			Environment.SetEnvironmentVariable(
				"AUTOEDITING_LLM_ENDPOINT", "https://example.test/v1/");
			Environment.SetEnvironmentVariable(
				"AUTOEDITING_LLM_MODEL", "environment-model");
			OpenAiCompatibleOptions explicitEnvironment =
				OpenAiCompatibleOptions.FromEnvironment(path);
			Assert(
				explicitEnvironment.Endpoint.AbsoluteUri ==
					"https://example.test/v1/" &&
				explicitEnvironment.Model == "environment-model",
				"An explicit environment provider did not honor its endpoint/model overrides.");
		}
		finally
		{
			foreach (KeyValuePair<string, string?> item in original)
				Environment.SetEnvironmentVariable(item.Key, item.Value);
			if (Directory.Exists(root))
				Directory.Delete(root, true);
		}
	}

	private static void TestOpenAiProviderPayload()
	{
		SequenceHandler handler = new(
			Response(
				HttpStatusCode.OK,
				"""{"id":"openai-1","model":"gpt-test","choices":[{"finish_reason":"stop","message":{"content":"{\"ok\":true}"}}],"usage":{"prompt_tokens":12,"completion_tokens":4,"total_tokens":16,"prompt_tokens_details":{"cached_tokens":7}}}"""));
		OpenAiCompatibleTextGenerationClient client = new(
			new HttpClient(handler),
			new OpenAiCompatibleOptions
			{
				Provider = "openai",
				Endpoint = new Uri("https://api.openai.com/v1/"),
				Model = "gpt-test",
				ApiKey = "test-only-key",
				ReasoningEffort = "high",
				Seed = 17,
				RequestTimeout = TimeSpan.FromSeconds(1),
				MaxAttempts = 1
			});
		TextGenerationResult result = client.GenerateAsync(
			new TextGenerationRequest
			{
				SystemPrompt = "system",
				UserPrompt = "user",
				MaxOutputTokens = 1234
			},
			CancellationToken.None).GetAwaiter().GetResult();

		string body = handler.Bodies.Single();
		Assert(
			body.Contains(
				"\"max_completion_tokens\":1234",
				StringComparison.Ordinal) &&
			body.Contains("\"reasoning_effort\":\"high\"", StringComparison.Ordinal) &&
			!body.Contains("\"max_tokens\"", StringComparison.Ordinal) &&
			!body.Contains("\"temperature\"", StringComparison.Ordinal) &&
			!body.Contains("\"seed\"", StringComparison.Ordinal),
			"OpenAI payload used llama.cpp-only or model-restricted generation fields.");
		Assert(
			handler.AuthorizationSchemes.Single() == "Bearer" &&
			handler.AuthorizationParameters.Single() == "test-only-key",
			"OpenAI API key was not sent through Bearer authorization.");
		Assert(
			result.Usage.CachedPromptTokens == 7,
			"OpenAI cached-token usage was not captured.");
	}

	private static void TestDeepSeekProviderPayloadAndReasoningStream()
	{
		SequenceHandler handler = new(
			Response(
				HttpStatusCode.OK,
				string.Join(
					"\n",
					"""data: {"id":"deepseek-1","model":"deepseek-v4-pro","choices":[{"delta":{"reasoning_content":"checking timing"},"finish_reason":null}]}""",
					"""data: {"id":"deepseek-1","model":"deepseek-v4-pro","choices":[{"delta":{"content":"{\"ok\":true}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":20,"prompt_cache_hit_tokens":12,"prompt_cache_miss_tokens":8,"completion_tokens":8,"total_tokens":28}}""",
					"data: [DONE]",
					"")));
		OpenAiCompatibleTextGenerationClient client = new(
			new HttpClient(handler),
			new OpenAiCompatibleOptions
			{
				Provider = InferenceProviderIds.DeepSeek,
				Endpoint = new Uri("https://api.deepseek.com/v1/"),
				Model = "deepseek-v4-pro",
				ApiKey = "test-only-key",
				ThinkingEnabled = true,
				ReasoningEffort = "max",
				RequestTimeout = TimeSpan.FromSeconds(1),
				MaxAttempts = 1
			});
		StringBuilder answer = new();
		StringBuilder reasoning = new();
		TextGenerationResult result = client.GenerateAsync(
			new TextGenerationRequest
			{
				SystemPrompt = "Return JSON.",
				UserPrompt = "user",
				MaxOutputTokens = 2048,
				JsonSchemaName = "deepseek_test",
				JsonSchema =
					"""{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}""",
				OnTextDelta = delta => answer.Append(delta),
				OnReasoningDelta = delta => reasoning.Append(delta)
			},
			CancellationToken.None).GetAwaiter().GetResult();

		string body = handler.Bodies.Single();
		Assert(
			body.Contains("\"model\":\"deepseek-v4-pro\"", StringComparison.Ordinal) &&
			body.Contains("\"max_tokens\":2048", StringComparison.Ordinal) &&
			body.Contains("\"thinking\":{\"type\":\"enabled\"}", StringComparison.Ordinal) &&
			body.Contains("\"reasoning_effort\":\"max\"", StringComparison.Ordinal) &&
			body.Contains("\"response_format\":{\"type\":\"json_object\"}", StringComparison.Ordinal) &&
			body.Contains("additionalProperties", StringComparison.Ordinal) &&
			!body.Contains("\"temperature\"", StringComparison.Ordinal) &&
			!body.Contains("\"max_completion_tokens\"", StringComparison.Ordinal),
			"DeepSeek payload did not match the documented V4 thinking/JSON contract.");
		Assert(
			reasoning.ToString() == "checking timing" &&
			answer.ToString() == "{\"ok\":true}" &&
			result.Text == "{\"ok\":true}" &&
			result.Usage.CachedPromptTokens == 12,
			"DeepSeek reasoning activity was not separated from the final JSON response.");
	}

	private static void TestOperationSpecificProgressStages()
	{
		Assert(
			LlamaCppProgressMonitor.DescribeStage(new AssemblySessionState
			{
				Phase = AssemblyPhase.PlanningClip,
				Checkpoint = 4
			}) == "Planning clip 4" &&
			LlamaCppProgressMonitor.DescribeStage(new AssemblySessionState
			{
				Phase = AssemblyPhase.RoughCutAuditing
			}) == "Auditing full rough cut" &&
			LlamaCppProgressMonitor.DescribeStage(new AssemblySessionState
			{
				Phase = AssemblyPhase.AudioMaterializing
			}) == "Rendering audio preview",
			"llama.cpp progress stages do not identify the active editing operation.");
	}

	private static void TestInferenceBudgets()
	{
		InferenceBudgets defaults = InferenceBudgets.FromEnvironment();
		Assert(defaults.MinimumContextTokens == 65536 &&
			defaults.PlanningMaxOutputTokens == 32768 &&
			defaults.ReviewMaxOutputTokens == 8192,
			"The quality-first inference budget defaults changed unexpectedly.");
		defaults.ValidateServerContext(65536);
		ExpectFailure(
			() => defaults.ValidateServerContext(32768),
			"A server context below the configured minimum was accepted.");
	}

	private static void TestStreamingGeneration()
	{
		string sse = string.Join("\n\n", new[]
		{
			"""data: {"id":"stream-1","model":"stream-model","choices":[{"delta":{"content":"hello "},"finish_reason":null}]}""",
			"""data: {"id":"stream-1","model":"stream-model","choices":[{"delta":{"content":"world"},"finish_reason":"stop"}]}""",
			"""data: {"id":"stream-1","model":"stream-model","choices":[],"usage":{"prompt_tokens":9,"completion_tokens":2,"total_tokens":11}}""",
			"data: [DONE]",
			""
		});
		SequenceHandler handler = new(Response(HttpStatusCode.OK, sse, "text/event-stream"));
		StringBuilder observed = new();
		TextGenerationResult result = Client(handler).GenerateAsync(
			new TextGenerationRequest
			{
				SystemPrompt = "system",
				UserPrompt = "user",
				OnTextDelta = delta => observed.Append(delta)
			},
			CancellationToken.None).GetAwaiter().GetResult();

		Assert(result.Text == "hello world" && observed.ToString() == result.Text,
			"Streamed text deltas were not delivered or reconstructed exactly.");
		Assert(result.ResponseId == "stream-1" && result.Usage.TotalTokens == 11 &&
			result.Timings.TimeToFirstTokenMilliseconds > 0,
			"Measured first-token latency was lost when the stream omitted llama.cpp timings.");
		Assert(handler.Bodies.Single().Contains("\"stream\":true", StringComparison.Ordinal) &&
			handler.Bodies.Single().Contains("\"include_usage\":true", StringComparison.Ordinal),
			"The streaming request did not ask llama.cpp for usage diagnostics.");
	}

	private static void TestEnvironmentFile()
	{
		string root = Path.Combine(Path.GetTempPath(), "AutoEditing-EnvTest-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		string variable = "AUTOEDITING_ENV_TEST_VALUE";
		try
		{
			Environment.SetEnvironmentVariable(variable, null);
			File.WriteAllText(Path.Combine(root, ".env"),
				variable + "=from-file\nAUTOEDITING_ENV_QUOTED=\"quoted value\"\n");
			Assert(EnvironmentFile.LoadNearest(root) == Path.Combine(root, ".env"),
				"The nearest .env file was not discovered.");
			Assert(Environment.GetEnvironmentVariable(variable) == "from-file" &&
				Environment.GetEnvironmentVariable("AUTOEDITING_ENV_QUOTED") == "quoted value",
				"The .env values were not loaded.");
			Environment.SetEnvironmentVariable(variable, "from-process");
			EnvironmentFile.Load(Path.Combine(root, ".env"));
			Assert(Environment.GetEnvironmentVariable(variable) == "from-process",
				"An explicit process variable was overwritten by .env.");
		}
		finally
		{
			Environment.SetEnvironmentVariable(variable, null);
			Environment.SetEnvironmentVariable("AUTOEDITING_ENV_QUOTED", null);
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	private static void TestSchemaMultimodalAndDiagnostics()
	{
		SequenceHandler handler = new(
			Response(HttpStatusCode.OK,
				"""{"id":"chat-1","model":"gguf-model","choices":[{"finish_reason":"stop","message":{"content":"{\"ok\":true}"}}],"usage":{"prompt_tokens":12,"completion_tokens":4,"total_tokens":16},"timings":{"prompt_ms":20.5,"predicted_ms":40.5,"prompt_per_second":50.0,"predicted_per_second":25.0}}"""));
		OpenAiCompatibleTextGenerationClient client = Client(handler);
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
						Description = "frame"
					}
				},
				JsonSchemaName = "edit_plan",
				JsonSchema = """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}"""
			},
			CancellationToken.None).GetAwaiter().GetResult();

		string body = handler.Bodies.Single();
		Assert(body.Contains("\"seed\":7", StringComparison.Ordinal), "Configured seed was not sent.");
		Assert(body.Contains("\"type\":\"json_schema\"", StringComparison.Ordinal) &&
			body.Contains("\"strict\":true", StringComparison.Ordinal) &&
			body.Contains("\"name\":\"edit_plan\"", StringComparison.Ordinal),
			"Strict JSON-schema response format was not sent.");
		Assert(body.Contains("\"type\":\"image_url\"", StringComparison.Ordinal),
			"Multimodal image_url content was lost.");
		Assert(result.FinishReason == "stop" && result.ResponseId == "chat-1",
			"Response completion metadata was not captured.");
		Assert(result.Usage.TotalTokens == 16 && result.Timings.PredictedMilliseconds == 40.5,
			"llama.cpp usage or timings were not captured.");
		Assert(result.Timings.TimeToFirstTokenMilliseconds == 0,
			"A completed non-streaming response was mislabeled with first-token latency.");
	}

	private static void TestHealthAndProps()
	{
		SequenceHandler handler = new(
			Response(HttpStatusCode.OK, """{"status":"ok"}"""),
			Response(HttpStatusCode.OK,
				"""{"model_path":"models/editor.gguf","total_slots":2,"chat_template":"chatml","default_generation_settings":{"n_ctx":32768}}"""));
		OpenAiCompatibleTextGenerationClient client = Client(handler);
		LlamaCppHealth health = client.ProbeHealthAsync(CancellationToken.None).GetAwaiter().GetResult();
		LlamaCppProps props = client.ProbePropsAsync(CancellationToken.None).GetAwaiter().GetResult();
		Assert(health.Status == "ok", "llama.cpp health status was not parsed.");
		Assert(props.ModelPath == "models/editor.gguf" && props.TotalSlots == 2 &&
			props.ContextSize == 32768 && props.ChatTemplate == "chatml",
			"llama.cpp props were not parsed.");
		Assert(handler.Uris[0].AbsoluteUri == "http://localhost:8080/health" &&
			handler.Uris[1].AbsoluteUri == "http://localhost:8080/props",
			"llama.cpp probes did not target server-root endpoints.");
	}

	private static void TestTransientRetry()
	{
		HttpResponseMessage busy = Response(HttpStatusCode.TooManyRequests, "busy");
		busy.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(1));
		SequenceHandler handler = new(
			busy,
			Response(HttpStatusCode.OK,
				"""{"choices":[{"finish_reason":"stop","message":{"content":"ok"}}]}"""));
		TextGenerationResult result = Client(handler).GenerateAsync(
			new TextGenerationRequest(),
			CancellationToken.None).GetAwaiter().GetResult();
		Assert(result.Text == "ok" && handler.CallCount == 2,
			"A transient 429 response was not retried exactly once.");
	}

	private static void TestDurableUsageTelemetry()
	{
		string root = Path.Combine(Path.GetTempPath(), "AutoEditing-UsageTest-" + Guid.NewGuid().ToString("N"));
		string sessionRoot = Path.Combine(root, "session");
		string telemetryRoot = Path.Combine(root, "telemetry");
		try
		{
			HttpResponseMessage busy = Response(HttpStatusCode.TooManyRequests, "busy");
			busy.Headers.RetryAfter =
				new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(1));
			SequenceHandler handler = new(
				busy,
				Response(HttpStatusCode.OK,
					"""{"id":"usage-1","model":"exact/model:q4","choices":[{"finish_reason":"stop","message":{"content":"ok"}}],"usage":{"prompt_tokens":20,"completion_tokens":5,"total_tokens":25,"prompt_tokens_details":{"cached_tokens":12},"context_tokens_peak":24},"timings":{"prompt_ms":10,"predicted_ms":30,"prompt_per_second":2000,"predicted_per_second":166.6}}"""));
			FileInferenceUsageSink sink = new(sessionRoot, telemetryRoot);
			OpenAiCompatibleTextGenerationClient client = new(
				new HttpClient(handler),
				Options(),
				sink,
				new InferenceUsageContext { SessionId = "session-1", Operation = "planning" });
			TextGenerationResult result = client.GenerateAsync(
				new TextGenerationRequest(), CancellationToken.None).GetAwaiter().GetResult();

			Assert(result.RetryCount == 1 && result.Usage.CachedPromptTokens == 12,
				"Retry or cached-token telemetry was not captured.");
			string[] lines = File.ReadAllLines(Path.Combine(sessionRoot, "usage.ndjson"));
			Assert(lines.Length == 1 && lines[0].Contains("\"Model\":\"exact/model:q4\"", StringComparison.Ordinal),
				"The per-call usage ledger was not appended.");
			InferenceUsageSummary sessionSummary =
				JsonSerializer.Deserialize<InferenceUsageSummary>(
					File.ReadAllText(Path.Combine(sessionRoot, "usage-summary.json")))!;
			Assert(sessionSummary.CallCount == 1 &&
				sessionSummary.CachedPromptTokens == 12 &&
				sessionSummary.Provider == "llama.cpp" &&
				sessionSummary.Model == "exact/model:q4" &&
				sessionSummary.TimeToFirstTokenMilliseconds == 0 &&
				sessionSummary.TimeToFirstTokenSampleCount == 0,
				"The current-session usage summary was not aggregated.");
			string lifetimePath = Path.Combine(
				telemetryRoot,
				FileInferenceUsageSink.SafeIdentityKey(
					"llama.cpp", "exact/model:q4"),
				"usage-summary.json");
			string lifetimeSummary = File.ReadAllText(lifetimePath);
			Assert(lifetimeSummary.Contains("\"Model\": \"exact/model:q4\"", StringComparison.Ordinal) &&
				lifetimeSummary.Contains("\"Provider\": \"llama.cpp\"", StringComparison.Ordinal) &&
				lifetimeSummary.Contains("\"RetryCount\": 1", StringComparison.Ordinal),
				"The exact backend-and-model lifetime summary was not aggregated.");

			sink.Record(new InferenceUsageRecord
			{
				TimestampUtc = new DateTimeOffset(
					2026, 7, 27, 14, 0, 0, TimeSpan.Zero),
				SessionId = "session-1",
				Provider = "online-provider",
				Model = "exact/model:q4",
				PromptTokens = 3,
				GeneratedTokens = 2,
				TotalTokens = 5,
				TimeToFirstTokenMilliseconds = 25
			});
			sessionSummary = JsonSerializer.Deserialize<InferenceUsageSummary>(
				File.ReadAllText(Path.Combine(sessionRoot, "usage-summary.json")))!;
			Assert(sessionSummary.HasMixedProviders &&
				!sessionSummary.HasMixedModels &&
				sessionSummary.Provider == null &&
				sessionSummary.Model == "exact/model:q4" &&
				sessionSummary.TotalTokens == 30 &&
				sessionSummary.TimeToFirstTokenSampleCount == 1 &&
				sessionSummary.TimeToFirstTokenMilliseconds == 25,
				"A mixed-backend session was presented as one exact inference identity.");
			string otherLifetimePath = Path.Combine(
				telemetryRoot,
				FileInferenceUsageSink.SafeIdentityKey(
					"online-provider", "exact/model:q4"),
				"usage-summary.json");
			Assert(!string.Equals(
					lifetimePath, otherLifetimePath, StringComparison.Ordinal) &&
				File.Exists(otherLifetimePath),
				"Lifetime usage was not partitioned by backend and model.");

			string legacySessionRoot = Path.Combine(root, "legacy-session");
			Directory.CreateDirectory(legacySessionRoot);
			File.WriteAllText(
				Path.Combine(legacySessionRoot, "usage-summary.json"),
				JsonSerializer.Serialize(new InferenceUsageSummary
				{
					Scope = "session",
					SessionId = "legacy",
					Model = "exact/model:q4",
					CallCount = 1,
					TotalTokens = 10
				}));
			new FileInferenceUsageSink(legacySessionRoot, telemetryRoot).Record(
				new InferenceUsageRecord
				{
					TimestampUtc = new DateTimeOffset(
						2026, 7, 27, 14, 1, 0, TimeSpan.Zero),
					SessionId = "legacy",
					Provider = "llama.cpp",
					Model = "exact/model:q4",
					TotalTokens = 5
				});
			InferenceUsageSummary migrated =
				JsonSerializer.Deserialize<InferenceUsageSummary>(
					File.ReadAllText(Path.Combine(
						legacySessionRoot, "usage-summary.json")))!;
			Assert(!migrated.HasMixedProviders &&
				migrated.Provider == "llama.cpp" &&
				migrated.TotalTokens == 15,
				"A pre-provider summary was incorrectly marked as mixed after migration.");
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	private static void TestPermanentFailureIsNotRetried()
	{
		SequenceHandler handler = new(Response(HttpStatusCode.BadRequest, "invalid"));
		ExpectFailure(
			() => Client(handler).GenerateAsync(new TextGenerationRequest(), CancellationToken.None)
				.GetAwaiter().GetResult(),
			"A permanent 400 response was accepted.");
		Assert(handler.CallCount == 1, "A permanent 4xx response was retried.");
	}

	private static void TestTruncationRejected()
	{
		SequenceHandler handler = new(
			Response(HttpStatusCode.OK,
				"""{"choices":[{"finish_reason":"length","message":{"content":"partial"}}]}"""));
		ExpectFailure(
			() => Client(handler).GenerateAsync(new TextGenerationRequest(), CancellationToken.None)
				.GetAwaiter().GetResult(),
			"A length-truncated response was accepted.");
		Assert(handler.CallCount == 1, "A semantic truncation failure was retried.");
	}

	private static OpenAiCompatibleTextGenerationClient Client(SequenceHandler handler) =>
		new(new HttpClient(handler), Options());

	private static OpenAiCompatibleOptions Options() =>
		new()
		{
			Endpoint = new Uri("http://localhost:8080/v1/"),
			Model = "configured-model",
			Seed = 7,
			RequestTimeout = TimeSpan.FromSeconds(1),
			MaxAttempts = 3,
			InitialRetryDelay = TimeSpan.FromMilliseconds(1)
		};

	private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
		new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

	private static HttpResponseMessage Response(
		HttpStatusCode status,
		string body,
		string mediaType) =>
		new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private static void ExpectFailure(Action action, string message)
	{
		try { action(); }
		catch { return; }
		throw new InvalidOperationException(message);
	}

	private sealed class SequenceHandler : HttpMessageHandler
	{
		private readonly Queue<HttpResponseMessage> responses;

		public SequenceHandler(params HttpResponseMessage[] responses)
		{
			this.responses = new Queue<HttpResponseMessage>(responses);
		}

		public int CallCount { get; private set; }
		public List<Uri> Uris { get; } = new();
		public List<string> Bodies { get; } = new();
		public List<string?> AuthorizationSchemes { get; } = new();
		public List<string?> AuthorizationParameters { get; } = new();

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			CallCount++;
			Uris.Add(request.RequestUri!);
			AuthorizationSchemes.Add(request.Headers.Authorization?.Scheme);
			AuthorizationParameters.Add(
				request.Headers.Authorization?.Parameter);
			Bodies.Add(request.Content == null
				? ""
				: await request.Content.ReadAsStringAsync(cancellationToken));
			if (responses.Count == 0) throw new InvalidOperationException("No response remains.");
			return responses.Dequeue();
		}
	}
}
