using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.LlmEditor.Assembly;
using AutoEditing.LlmEditor.Inference;

namespace AutoEditing.LlmEditor.Workbench;

/// <summary>
/// Publishes provider-neutral progress while an inference request is in flight.
/// Remote APIs do not expose llama.cpp slot counters, so prompt and generated
/// token counts remain clearly marked estimates until the final usage block.
/// </summary>
internal sealed class StreamingInferenceProgressClient : ITextGenerationClient
{
	private static readonly TimeSpan HeartbeatInterval =
		TimeSpan.FromSeconds(1);
	private static readonly TimeSpan DeltaPublishInterval =
		TimeSpan.FromMilliseconds(500);

	private readonly ITextGenerationClient inner;
	private readonly WorkbenchSessionPublisher publisher;
	private readonly string sessionId;
	private readonly string provider;
	private readonly object publishLock = new();

	public StreamingInferenceProgressClient(
		ITextGenerationClient inner,
		WorkbenchSessionPublisher publisher,
		string sessionId,
		string provider)
	{
		this.inner = inner ??
			throw new ArgumentNullException(nameof(inner));
		this.publisher = publisher ??
			throw new ArgumentNullException(nameof(publisher));
		this.sessionId = string.IsNullOrWhiteSpace(sessionId)
			? throw new ArgumentException(
				"A session ID is required.",
				nameof(sessionId))
			: sessionId;
		this.provider = string.IsNullOrWhiteSpace(provider)
			? "inference provider"
			: provider.Trim();
	}

	public async Task<TextGenerationResult> GenerateAsync(
		TextGenerationRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
		DateTimeOffset lastActivityUtc = startedUtc;
		DateTimeOffset lastPublishedUtc = DateTimeOffset.MinValue;
		int generatedCharacters = 0;
		int promptTokenEstimate = EstimatePromptTokens(request);
		bool receivedFirstToken = false;
		string activityMessage = "Request accepted; waiting for the first response token.";
		string operation = string.IsNullOrWhiteSpace(request.JsonSchemaName)
			? "text-generation"
			: request.JsonSchemaName;

		void Publish(bool processing, string message, bool force = false)
		{
			DateTimeOffset now = DateTimeOffset.UtcNow;
			lock (publishLock)
			{
				if (!force &&
					now - lastPublishedUtc < DeltaPublishInterval)
					return;
				lastPublishedUtc = now;
				try
				{
					AssemblySessionState? state =
						new AssemblyActionStore(
							publisher.SessionRoot).ReadState();
					publisher.PublishProgress(new EditSessionProgress
					{
						SessionId = sessionId,
						Stage =
							InferenceProgressStage.Describe(state),
						UpdatedUtc = now,
						Elapsed = now - startedUtc,
						IsProcessing = processing,
						PromptTokens = promptTokenEstimate,
						PromptTokensProcessed = receivedFirstToken
							? promptTokenEstimate
							: 0,
						GeneratedTokens =
							EstimateTokens(generatedCharacters),
						MaximumGeneratedTokens =
							request.MaxOutputTokens,
						GeneratedCharacters =
							generatedCharacters,
						IsTokenEstimate = true,
						HasReceivedFirstToken =
							receivedFirstToken,
						LastActivityUtc = lastActivityUtc,
						Provider = provider,
						Operation = operation,
						Message = message
					});
				}
				catch
				{
					// Progress reporting is non-authoritative and must never
					// fail or cancel the inference request it observes.
				}
			}
		}

		TextGenerationRequest observedRequest =
			CopyWithDeltaObserver(
				request,
				delta =>
				{
					if (!string.IsNullOrEmpty(delta))
					{
						lock (publishLock)
						{
							generatedCharacters += delta.Length;
							receivedFirstToken = true;
							lastActivityUtc = DateTimeOffset.UtcNow;
							activityMessage = "Streaming model response.";
							Publish(
								processing: true,
								message: activityMessage);
						}
					}
					request.OnTextDelta?.Invoke(delta);
				},
				delta =>
				{
					if (!string.IsNullOrEmpty(delta))
					{
						lock (publishLock)
						{
							generatedCharacters += delta.Length;
							receivedFirstToken = true;
							lastActivityUtc = DateTimeOffset.UtcNow;
							activityMessage = "Model is reasoning.";
							Publish(
								processing: true,
								message: activityMessage);
						}
					}
					request.OnReasoningDelta?.Invoke(delta);
				});

		Publish(
			processing: true,
			message: "Request sent; waiting for the first response token.",
			force: true);
		using CancellationTokenSource heartbeatCancellation = new();
		Task heartbeat = RunHeartbeatAsync(
			() => Publish(
				processing: true,
				message: receivedFirstToken
					? activityMessage
					: "Request accepted; waiting for the first response token.",
				force: true),
			heartbeatCancellation.Token);
		try
		{
			TextGenerationResult result =
				await inner.GenerateAsync(
					observedRequest,
					cancellationToken);
			heartbeatCancellation.Cancel();
			await IgnoreHeartbeatCancellationAsync(heartbeat);
			DateTimeOffset completedUtc = DateTimeOffset.UtcNow;
			lock (publishLock)
			{
				try
				{
					publisher.PublishProgress(
						new EditSessionProgress
						{
							SessionId = sessionId,
							Stage = InferenceProgressStage.Describe(
								new AssemblyActionStore(
									publisher.SessionRoot)
								.ReadState()),
							UpdatedUtc = completedUtc,
							Elapsed =
								completedUtc - startedUtc,
							IsProcessing = false,
							PromptTokens =
								result.Usage.PromptTokens,
							PromptTokensProcessed =
								result.Usage.PromptTokens,
							GeneratedTokens =
								result.Usage.CompletionTokens,
							MaximumGeneratedTokens =
								request.MaxOutputTokens,
							GeneratedCharacters =
								generatedCharacters,
							IsTokenEstimate = false,
							HasReceivedFirstToken =
								receivedFirstToken,
							LastActivityUtc =
								lastActivityUtc,
							Provider = provider,
							Operation = operation,
							Message =
								"Model response received; validating it."
						});
				}
				catch
				{
					// The completed model response remains authoritative even
					// when its optional UI heartbeat cannot be persisted.
				}
			}
			return result;
		}
		catch
		{
			heartbeatCancellation.Cancel();
			await IgnoreHeartbeatCancellationAsync(heartbeat);
			Publish(
				processing: false,
				message: "Inference request failed. Inspect the saved exchange.",
				force: true);
			throw;
		}
	}

	private static async Task RunHeartbeatAsync(
		Action publish,
		CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(
					HeartbeatInterval,
					cancellationToken);
				if (!cancellationToken.IsCancellationRequested)
					publish();
			}
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	private static async Task IgnoreHeartbeatCancellationAsync(
		Task heartbeat)
	{
		try
		{
			await heartbeat;
		}
		catch (OperationCanceledException)
		{
		}
	}

	private static TextGenerationRequest CopyWithDeltaObserver(
		TextGenerationRequest request,
		Action<string> observer,
		Action<string> reasoningObserver) =>
		new()
		{
			SystemPrompt = request.SystemPrompt,
			UserPrompt = request.UserPrompt,
			VisualEvidence = request.VisualEvidence,
			Temperature = request.Temperature,
			MaxOutputTokens = request.MaxOutputTokens,
			JsonSchemaName = request.JsonSchemaName,
			JsonSchema = request.JsonSchema,
			OnTextDelta = observer,
			OnReasoningDelta = reasoningObserver
		};

	private static int EstimatePromptTokens(
		TextGenerationRequest request)
	{
		long characters =
			(long)(request.SystemPrompt?.Length ?? 0) +
			(request.UserPrompt?.Length ?? 0) +
			(request.JsonSchema?.Length ?? 0);
		return EstimateTokens(characters);
	}

	private static int EstimateTokens(long characters) =>
		(int)Math.Min(
			int.MaxValue,
			Math.Max(0, (characters + 3) / 4));
}
