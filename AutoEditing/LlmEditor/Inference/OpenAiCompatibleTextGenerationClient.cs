using System.Net;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AutoEditing.Iteration.Contracts.Sessions;

namespace AutoEditing.LlmEditor.Inference;

internal sealed class OpenAiCompatibleTextGenerationClient : ITextGenerationClient
{
	private readonly HttpClient httpClient;
	private readonly OpenAiCompatibleOptions options;
	private readonly Uri serverRoot;
	private readonly IInferenceUsageSink? usageSink;
	private readonly InferenceUsageContext? usageContext;

	public OpenAiCompatibleTextGenerationClient(
		HttpClient httpClient,
		OpenAiCompatibleOptions options,
		IInferenceUsageSink? usageSink = null,
		InferenceUsageContext? usageContext = null)
	{
		this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
		this.options = options ?? throw new ArgumentNullException(nameof(options));
		options.Validate();
		this.httpClient.BaseAddress = options.Endpoint;
		this.httpClient.Timeout = Timeout.InfiniteTimeSpan;
		serverRoot = GetServerRoot(options.Endpoint);
		this.usageSink = usageSink;
		this.usageContext = usageContext;
		if (!string.IsNullOrWhiteSpace(options.ApiKey))
			this.httpClient.DefaultRequestHeaders.Authorization =
				new AuthenticationHeaderValue("Bearer", options.ApiKey);
	}

	public Task<LlamaCppHealth> ProbeHealthAsync(CancellationToken cancellationToken)
	{
		return GetProbeAsync("health", ParseHealth, cancellationToken);
	}

	public Task<LlamaCppProps> ProbePropsAsync(CancellationToken cancellationToken)
	{
		return GetProbeAsync("props", ParseProps, cancellationToken);
	}

	public async Task<TextGenerationResult> GenerateAsync(
		TextGenerationRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		Stopwatch requestClock = Stopwatch.StartNew();
		string payload = BuildPayload(request);
		(HttpResponseMessage response, int retryCount) = await SendWithRetriesAsync(
			() => new HttpRequestMessage(HttpMethod.Post, "chat/completions")
			{
				Content = new StringContent(payload, Encoding.UTF8, "application/json")
			},
			cancellationToken);
		using (response)
		{
			if (request.OnTextDelta != null)
			{
				TextGenerationResult streamed = await ParseStreamingGenerationAsync(
					response,
					retryCount,
					request.OnTextDelta,
					request.OnReasoningDelta,
					requestClock,
					cancellationToken);
				RecordUsage(streamed);
				if (string.Equals(streamed.FinishReason, "length", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(streamed.FinishReason, "max_tokens", StringComparison.OrdinalIgnoreCase))
					throw new TruncatedGenerationException(streamed);
				return streamed;
			}
			string body = await response.Content.ReadAsStringAsync(cancellationToken);
			TextGenerationResult result = ParseGeneration(body, retryCount);
			RecordUsage(result);
			if (string.Equals(result.FinishReason, "length", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(result.FinishReason, "max_tokens", StringComparison.OrdinalIgnoreCase))
				throw new TruncatedGenerationException(result);
			return result;
		}
	}

	private string BuildPayload(TextGenerationRequest request)
	{
		if (request.MaxOutputTokens < 1)
			throw new InvalidOperationException("MaxOutputTokens must be positive.");
		if (options.IsDeepSeek && request.VisualEvidence.Count > 0)
			throw new InvalidOperationException(
				"DeepSeek V4's documented chat-completions API is text-only; " +
				"visual evidence cannot be sent to this provider.");
		List<object> userContent = new()
		{
			new { type = "text", text = request.UserPrompt }
		};
		foreach (VisualEvidence evidence in request.VisualEvidence)
		{
			if (!evidence.DataUrl.StartsWith("data:image/", StringComparison.Ordinal))
				throw new InvalidOperationException("Visual evidence must be an image data URL.");
			if (!string.IsNullOrWhiteSpace(evidence.Description))
				userContent.Add(new { type = "text", text = "Visual evidence: " + evidence.Description });
			userContent.Add(new
			{
				type = "image_url",
				image_url = new { url = evidence.DataUrl }
			});
		}
		object userMessage = options.IsDeepSeek
			? new { role = "user", content = request.UserPrompt }
			: new { role = "user", content = userContent };

		Dictionary<string, object?> payload = new()
		{
			["model"] = options.Model,
			["messages"] = new object[]
			{
				new
				{
					role = "system",
					content = options.IsDeepSeek &&
						!string.IsNullOrWhiteSpace(request.JsonSchema)
						? request.SystemPrompt +
							"\nThe JSON response must match this schema exactly:\n" +
							request.JsonSchema
						: request.SystemPrompt
				},
				userMessage
			},
			[options.IsLlamaCpp || options.IsDeepSeek
				? "max_tokens"
				: "max_completion_tokens"] = request.MaxOutputTokens
		};
		if (options.IsLlamaCpp)
		{
			payload["temperature"] = request.Temperature;
			payload["seed"] = options.Seed;
			// The progressive planner sends a long, mostly-repeated prefix
			// (system prompt, style findings, sketch, accepted prefix) on
			// every sequential clip-by-clip call within a session. Explicit
			// cache_prompt lets llama.cpp reuse that prefix's KV cache
			// instead of recomputing it each request.
			payload["cache_prompt"] = true;
		}
		else if (options.IsDeepSeek)
		{
			payload["thinking"] = new
			{
				type = options.ThinkingEnabled ? "enabled" : "disabled"
			};
			if (options.ThinkingEnabled)
				payload["reasoning_effort"] = options.ReasoningEffort;
		}
		else
		{
			payload["reasoning_effort"] = options.ReasoningEffort;
		}
		if (request.OnTextDelta != null)
		{
			payload["stream"] = true;
			payload["stream_options"] = new { include_usage = true };
		}
		if (!string.IsNullOrWhiteSpace(request.JsonSchema))
		{
			using JsonDocument schema = JsonDocument.Parse(request.JsonSchema);
			if (schema.RootElement.ValueKind != JsonValueKind.Object)
				throw new InvalidOperationException("The response JSON schema must be a JSON object.");
			payload["response_format"] = options.IsDeepSeek
				? new { type = "json_object" }
				: (object)new
				{
					type = "json_schema",
					json_schema = new
					{
						name = string.IsNullOrWhiteSpace(request.JsonSchemaName) ? "autoediting_response" : request.JsonSchemaName,
						strict = true,
						schema = schema.RootElement.Clone()
					}
				};
		}
		return JsonSerializer.Serialize(payload);
	}

	private async Task<TextGenerationResult> ParseStreamingGenerationAsync(
		HttpResponseMessage response,
		int retryCount,
		Action<string> onTextDelta,
		Action<string>? onReasoningDelta,
		Stopwatch requestClock,
		CancellationToken cancellationToken)
	{
		await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using StreamReader reader = new(stream);
		StringBuilder text = new();
		string model = options.Model;
		string responseId = "";
		string finishReason = "";
		TextGenerationUsage usage = new();
		LlamaCppTimings timings = new();
		double timeToFirstTokenMilliseconds = 0;

		while (await reader.ReadLineAsync(cancellationToken) is string line)
		{
			if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
			string data = line.Substring(5).TrimStart();
			if (data.Length == 0 || string.Equals(data, "[DONE]", StringComparison.Ordinal)) continue;
			using JsonDocument document = JsonDocument.Parse(data);
			JsonElement root = document.RootElement;
			model = StringProperty(root, "model", model);
			responseId = StringProperty(root, "id", responseId);
			JsonElement choices = PropertyOrDefault(root, "choices");
			if (choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
			{
				JsonElement choice = choices[0];
				string chunkFinishReason = StringProperty(choice, "finish_reason");
				if (!string.IsNullOrWhiteSpace(chunkFinishReason)) finishReason = chunkFinishReason;
				JsonElement delta = PropertyOrDefault(choice, "delta");
				string reasoningContent =
					StringProperty(delta, "reasoning_content");
				if (reasoningContent.Length > 0)
				{
					if (timeToFirstTokenMilliseconds <= 0)
						timeToFirstTokenMilliseconds =
							Math.Max(
								0.001,
								requestClock.Elapsed.TotalMilliseconds);
					onReasoningDelta?.Invoke(reasoningContent);
				}
				string content = StringProperty(delta, "content");
				if (content.Length > 0)
				{
					if (timeToFirstTokenMilliseconds <= 0)
						timeToFirstTokenMilliseconds =
							Math.Max(
								0.001,
								requestClock.Elapsed.TotalMilliseconds);
					text.Append(content);
					onTextDelta(content);
				}
			}
			JsonElement usageElement = PropertyOrDefault(root, "usage");
			if (usageElement.ValueKind == JsonValueKind.Object)
			{
				JsonElement promptDetails = PropertyOrDefault(usageElement, "prompt_tokens_details");
				int promptTokens = IntProperty(usageElement, "prompt_tokens");
				int completionTokens = IntProperty(usageElement, "completion_tokens");
				int totalTokens = IntProperty(usageElement, "total_tokens");
				usage = new TextGenerationUsage
				{
					PromptTokens = promptTokens,
					CachedPromptTokens = IntProperty(promptDetails, "cached_tokens",
						IntProperty(
							usageElement,
							"prompt_cache_hit_tokens",
							IntProperty(usageElement, "prompt_tokens_cached"))),
					CompletionTokens = completionTokens,
					TotalTokens = totalTokens > 0 ? totalTokens : promptTokens + completionTokens,
					ContextTokensPeak = NullableIntProperty(usageElement, "context_tokens_peak")
				};
			}
			JsonElement timingsElement = PropertyOrDefault(root, "timings");
			if (timingsElement.ValueKind == JsonValueKind.Object)
			{
				timings = new LlamaCppTimings
				{
					PromptMilliseconds = DoubleProperty(timingsElement, "prompt_ms"),
					TimeToFirstTokenMilliseconds = timeToFirstTokenMilliseconds,
					PredictedMilliseconds = DoubleProperty(timingsElement, "predicted_ms"),
					PromptTokensPerSecond = DoubleProperty(timingsElement, "prompt_per_second"),
					PredictedTokensPerSecond = DoubleProperty(timingsElement, "predicted_per_second")
				};
				if (!usage.ContextTokensPeak.HasValue)
					usage = new TextGenerationUsage
					{
						PromptTokens = usage.PromptTokens,
						CachedPromptTokens = usage.CachedPromptTokens,
						CompletionTokens = usage.CompletionTokens,
						TotalTokens = usage.TotalTokens,
						ContextTokensPeak = NullableIntProperty(timingsElement, "n_prompt_tokens")
					};
			}
		}

		return new TextGenerationResult
		{
			Text = text.ToString(),
			Model = model,
			ResponseId = responseId,
			FinishReason = finishReason,
			Usage = usage,
			Timings = new LlamaCppTimings
			{
				PromptMilliseconds = timings.PromptMilliseconds,
				TimeToFirstTokenMilliseconds = timeToFirstTokenMilliseconds,
				PredictedMilliseconds = timings.PredictedMilliseconds,
				PromptTokensPerSecond = timings.PromptTokensPerSecond,
				PredictedTokensPerSecond = timings.PredictedTokensPerSecond
			},
			RetryCount = retryCount
		};
	}

	private async Task<(HttpResponseMessage Response, int RetryCount)> SendWithRetriesAsync(
		Func<HttpRequestMessage> requestFactory,
		CancellationToken cancellationToken)
	{
		for (int attempt = 1; ; attempt++)
		{
			try
			{
				using HttpRequestMessage request = requestFactory();
				using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				timeout.CancelAfter(options.RequestTimeout);
				HttpResponseMessage response = await httpClient.SendAsync(
					request,
					HttpCompletionOption.ResponseHeadersRead,
					timeout.Token);
				if (response.IsSuccessStatusCode) return (response, attempt - 1);

				string body = await response.Content.ReadAsStringAsync(cancellationToken);
				if (!IsTransient(response.StatusCode) || attempt >= options.MaxAttempts)
				{
					response.Dispose();
					throw new InvalidOperationException(
						$"The inference server returned {(int)response.StatusCode} ({response.ReasonPhrase}): " +
						Limit(body, 1000));
				}
				TimeSpan delay = RetryDelay(response, attempt);
				response.Dispose();
				await Task.Delay(delay, cancellationToken);
			}
			catch (HttpRequestException) when (attempt < options.MaxAttempts)
			{
				await Task.Delay(Backoff(attempt), cancellationToken);
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < options.MaxAttempts)
			{
				await Task.Delay(Backoff(attempt), cancellationToken);
			}
		}
	}

	private async Task<T> GetProbeAsync<T>(
		string relativePath,
		Func<string, T> parser,
		CancellationToken cancellationToken)
	{
		(HttpResponseMessage response, _) = await SendWithRetriesAsync(
			() => new HttpRequestMessage(HttpMethod.Get, new Uri(serverRoot, relativePath)),
			cancellationToken);
		using (response)
			return parser(await response.Content.ReadAsStringAsync(cancellationToken));
	}

	private TextGenerationResult ParseGeneration(string responseBody, int retryCount)
	{
		using JsonDocument document = JsonDocument.Parse(responseBody);
		JsonElement root = document.RootElement;
		if (!root.TryGetProperty("choices", out JsonElement choices) ||
			choices.ValueKind != JsonValueKind.Array ||
			choices.GetArrayLength() == 0 ||
			!choices[0].TryGetProperty("message", out JsonElement message) ||
			!message.TryGetProperty("content", out JsonElement content) ||
			content.ValueKind != JsonValueKind.String)
		{
			throw new InvalidOperationException(
				"The inference server response did not contain choices[0].message.content.");
		}
		string finishReason = StringProperty(choices[0], "finish_reason");
		JsonElement usage = PropertyOrDefault(root, "usage");
		JsonElement promptDetails = PropertyOrDefault(usage, "prompt_tokens_details");
		JsonElement timings = PropertyOrDefault(root, "timings");
		int promptTokens = IntProperty(usage, "prompt_tokens");
		int completionTokens = IntProperty(usage, "completion_tokens");
		int totalTokens = IntProperty(usage, "total_tokens");
		return new TextGenerationResult
		{
			Text = content.GetString() ?? "",
			Model = StringProperty(root, "model", options.Model),
			FinishReason = finishReason,
			ResponseId = StringProperty(root, "id"),
			Usage = new TextGenerationUsage
			{
				PromptTokens = promptTokens,
				CachedPromptTokens = IntProperty(promptDetails, "cached_tokens",
					IntProperty(
						usage,
						"prompt_cache_hit_tokens",
						IntProperty(usage, "prompt_tokens_cached"))),
				CompletionTokens = completionTokens,
				TotalTokens = totalTokens > 0 ? totalTokens : promptTokens + completionTokens,
				ContextTokensPeak = NullableIntProperty(usage, "context_tokens_peak") ??
					NullableIntProperty(timings, "n_prompt_tokens")
			},
			Timings = new LlamaCppTimings
			{
				PromptMilliseconds = DoubleProperty(timings, "prompt_ms"),
				// A non-streaming response exposes only completed-response latency,
				// not time to its first generated token.
				TimeToFirstTokenMilliseconds = 0,
				PredictedMilliseconds = DoubleProperty(timings, "predicted_ms"),
				PromptTokensPerSecond = DoubleProperty(timings, "prompt_per_second"),
				PredictedTokensPerSecond = DoubleProperty(timings, "predicted_per_second")
			},
			RetryCount = retryCount
		};
	}

	private void RecordUsage(TextGenerationResult result)
	{
		if (usageSink == null || usageContext == null) return;
		usageSink.Record(new InferenceUsageRecord
		{
			TimestampUtc = DateTimeOffset.UtcNow,
			SessionId = usageContext.SessionId,
			Model = string.IsNullOrWhiteSpace(result.Model) ? options.Model : result.Model,
			Provider = options.ProviderDisplayName,
			Operation = usageContext.Operation,
			ResponseId = result.ResponseId,
			FinishReason = result.FinishReason,
			PromptTokens = result.Usage.PromptTokens,
			CachedPromptTokens = result.Usage.CachedPromptTokens,
			GeneratedTokens = result.Usage.CompletionTokens,
			TotalTokens = result.Usage.TotalTokens,
			PromptMilliseconds = result.Timings.PromptMilliseconds,
			TimeToFirstTokenMilliseconds =
				result.Timings.TimeToFirstTokenMilliseconds,
			GenerationMilliseconds = result.Timings.PredictedMilliseconds,
			PromptTokensPerSecond = result.Timings.PromptTokensPerSecond,
			GenerationTokensPerSecond = result.Timings.PredictedTokensPerSecond,
			ContextTokensPeak = result.Usage.ContextTokensPeak,
			RetryCount = result.RetryCount
		});
	}

	private static LlamaCppHealth ParseHealth(string body)
	{
		using JsonDocument document = JsonDocument.Parse(body);
		return new LlamaCppHealth { Status = StringProperty(document.RootElement, "status") };
	}

	private static LlamaCppProps ParseProps(string body)
	{
		using JsonDocument document = JsonDocument.Parse(body);
		JsonElement root = document.RootElement;
		JsonElement defaultGenerationSettings = PropertyOrDefault(root, "default_generation_settings");
		return new LlamaCppProps
		{
			ModelPath = StringProperty(root, "model_path"),
			TotalSlots = IntProperty(root, "total_slots"),
			ContextSize = IntProperty(defaultGenerationSettings, "n_ctx"),
			ChatTemplate = StringProperty(root, "chat_template")
		};
	}

	private static bool IsTransient(HttpStatusCode statusCode) =>
		statusCode == HttpStatusCode.RequestTimeout ||
		(int)statusCode == 429 ||
		(int)statusCode >= 500;

	private TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
	{
		RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;
		if (retryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero) return delta;
		if (retryAfter?.Date is DateTimeOffset date)
		{
			TimeSpan until = date - DateTimeOffset.UtcNow;
			if (until > TimeSpan.Zero) return until;
		}
		return Backoff(attempt);
	}

	private TimeSpan Backoff(int attempt)
	{
		double multiplier = Math.Pow(2, Math.Max(0, attempt - 1));
		return TimeSpan.FromMilliseconds(Math.Min(
			options.InitialRetryDelay.TotalMilliseconds * multiplier,
			TimeSpan.FromSeconds(30).TotalMilliseconds));
	}

	private static Uri GetServerRoot(Uri endpoint)
	{
		string path = endpoint.AbsolutePath.TrimEnd('/');
		if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
			path = path[..^3];
		UriBuilder builder = new(endpoint) { Path = path.TrimEnd('/') + "/", Query = "", Fragment = "" };
		return builder.Uri;
	}

	private static JsonElement PropertyOrDefault(JsonElement element, string name) =>
		element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value)
			? value
			: default;

	private static string StringProperty(JsonElement element, string name, string fallback = "") =>
		element.ValueKind == JsonValueKind.Object &&
		element.TryGetProperty(name, out JsonElement value) &&
		value.ValueKind == JsonValueKind.String
			? value.GetString() ?? fallback
			: fallback;

	private static int IntProperty(JsonElement element, string name, int fallback = 0) =>
		element.ValueKind == JsonValueKind.Object &&
		element.TryGetProperty(name, out JsonElement value) &&
		value.TryGetInt32(out int result)
			? result
			: fallback;

	private static int? NullableIntProperty(JsonElement element, string name) =>
		element.ValueKind == JsonValueKind.Object &&
		element.TryGetProperty(name, out JsonElement value) &&
		value.TryGetInt32(out int result)
			? result
			: null;

	private static double DoubleProperty(JsonElement element, string name) =>
		element.ValueKind == JsonValueKind.Object &&
		element.TryGetProperty(name, out JsonElement value) &&
		value.TryGetDouble(out double result)
			? result
			: 0;

	private static string Limit(string value, int maximumLength) =>
		value.Length <= maximumLength ? value : value[..maximumLength] + "...";
}
