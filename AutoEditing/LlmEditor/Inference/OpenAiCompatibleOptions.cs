using AutoEditing.Iteration.Contracts.Configuration;

namespace AutoEditing.LlmEditor.Inference;

internal sealed class OpenAiCompatibleOptions
{
	public string Provider { get; init; } = InferenceProviderIds.LlamaCpp;

	public required Uri Endpoint { get; init; }

	public required string Model { get; init; }

	public string? ApiKey { get; init; }

	public string ReasoningEffort { get; init; } = "high";
	public bool ThinkingEnabled { get; init; } = true;

	public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(10);

	public int Seed { get; init; } = 0;

	public int MaxAttempts { get; init; } = 3;

	public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

	public bool IsLlamaCpp =>
		string.Equals(
			Provider,
			InferenceProviderIds.LlamaCpp,
			StringComparison.Ordinal);
	public bool IsDeepSeek =>
		string.Equals(
			Provider,
			InferenceProviderIds.DeepSeek,
			StringComparison.Ordinal);

	public string ProviderDisplayName =>
		IsLlamaCpp ? "llama.cpp" : IsDeepSeek ? "DeepSeek" : "OpenAI";

	public static OpenAiCompatibleOptions FromEnvironment(
		string? settingsPath = null)
	{
		InferenceProviderSettings settings =
			InferenceProviderSettingsStore.LoadOrDefault(settingsPath);
		string? providerOverride =
			Environment.GetEnvironmentVariable("AUTOEDITING_LLM_PROVIDER");
		string provider =
			(providerOverride ?? settings.Provider)
			.Trim()
			.ToLowerInvariant();
		if (provider != InferenceProviderIds.LlamaCpp &&
			provider != InferenceProviderIds.OpenAi &&
			provider != InferenceProviderIds.DeepSeek)
			throw new InvalidOperationException(
				"AUTOEDITING_LLM_PROVIDER must be 'llamacpp', 'openai', or 'deepseek'.");
		// Existing local installations commonly carry generic llama.cpp
		// endpoint/model values in .env. Do not let those silently override an
		// OpenAI selection made in the settings UI. Generic overrides still
		// apply when the provider itself was explicitly selected through the
		// environment.
		bool useGenericEndpointOverrides =
			!string.IsNullOrWhiteSpace(providerOverride) ||
			provider == InferenceProviderIds.LlamaCpp;
		string endpoint =
			(useGenericEndpointOverrides
				? Environment.GetEnvironmentVariable(
					"AUTOEDITING_LLM_ENDPOINT")
				: null) ??
			(provider == InferenceProviderIds.OpenAi
				? settings.OpenAiEndpoint
				: provider == InferenceProviderIds.DeepSeek
					? settings.DeepSeekEndpoint
					: settings.LlamaCppEndpoint);
		string model =
			(useGenericEndpointOverrides
				? Environment.GetEnvironmentVariable(
					"AUTOEDITING_LLM_MODEL")
				: null) ??
			(provider == InferenceProviderIds.OpenAi
				? settings.OpenAiModel
				: provider == InferenceProviderIds.DeepSeek
					? settings.DeepSeekModel
					: settings.LlamaCppModel);
		if (string.IsNullOrWhiteSpace(model))
			throw new InvalidOperationException(
				provider == InferenceProviderIds.OpenAi
					? "Configure an OpenAI model in AI settings."
					: "Configure the model served by llama.cpp in AI settings or AUTOEDITING_LLM_MODEL.");

		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? endpointUri) ||
			(endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
		{
			throw new InvalidOperationException(
				"AUTOEDITING_LLM_ENDPOINT must be an absolute HTTP or HTTPS URL.");
		}

		string? apiKey =
			Environment.GetEnvironmentVariable("AUTOEDITING_LLM_API_KEY");
		if (provider == InferenceProviderIds.OpenAi &&
			string.IsNullOrWhiteSpace(apiKey))
			apiKey = WindowsCredentialStore.Read(
				settings.OpenAiCredentialTarget);
		if (provider == InferenceProviderIds.DeepSeek &&
			string.IsNullOrWhiteSpace(apiKey))
			apiKey = WindowsCredentialStore.Read(
				settings.DeepSeekCredentialTarget);
		if ((provider == InferenceProviderIds.OpenAi ||
			 provider == InferenceProviderIds.DeepSeek) &&
			string.IsNullOrWhiteSpace(apiKey))
			throw new InvalidOperationException(
				(provider == InferenceProviderIds.DeepSeek ? "DeepSeek" : "OpenAI") +
				" is selected, but no API key is configured. Open AI settings in VEGAS.");

		return new OpenAiCompatibleOptions
		{
			Provider = provider,
			Endpoint = EnsureTrailingSlash(endpointUri),
			Model = model.Trim(),
			ApiKey = apiKey,
			ReasoningEffort =
				Environment.GetEnvironmentVariable(
					"AUTOEDITING_LLM_REASONING_EFFORT")?.Trim().ToLowerInvariant() ??
				(provider == InferenceProviderIds.DeepSeek
					? settings.DeepSeekReasoningEffort
					: settings.OpenAiReasoningEffort),
			ThinkingEnabled =
				provider != InferenceProviderIds.DeepSeek ||
				settings.DeepSeekThinkingMode == "enabled",
			RequestTimeout = ReadPositiveSeconds("AUTOEDITING_LLM_TIMEOUT_SECONDS", 600),
			Seed = ReadInteger("AUTOEDITING_LLM_SEED", 0),
			MaxAttempts = ReadPositiveInteger("AUTOEDITING_LLM_MAX_ATTEMPTS", 3),
			InitialRetryDelay = ReadPositiveSeconds("AUTOEDITING_LLM_RETRY_DELAY_SECONDS", 1)
		};
	}

	internal void Validate()
	{
		if (Provider != InferenceProviderIds.LlamaCpp &&
			Provider != InferenceProviderIds.OpenAi &&
			Provider != InferenceProviderIds.DeepSeek)
			throw new InvalidOperationException(
				"The inference provider is unsupported.");
		if (Endpoint == null || !Endpoint.IsAbsoluteUri)
			throw new InvalidOperationException("The inference endpoint must be absolute.");
		if (string.IsNullOrWhiteSpace(Model))
			throw new InvalidOperationException("The inference model cannot be empty.");
		if (RequestTimeout <= TimeSpan.Zero)
			throw new InvalidOperationException("The inference request timeout must be positive.");
		if (MaxAttempts < 1 || MaxAttempts > 10)
			throw new InvalidOperationException("Inference MaxAttempts must be between 1 and 10.");
		if (InitialRetryDelay <= TimeSpan.Zero)
			throw new InvalidOperationException("The inference retry delay must be positive.");
		if ((Provider == InferenceProviderIds.OpenAi ||
			 Provider == InferenceProviderIds.DeepSeek) &&
			string.IsNullOrWhiteSpace(ApiKey))
			throw new InvalidOperationException(
				"OpenAI requires a configured API key.");
		if (!IsLlamaCpp && !IsDeepSeek &&
			ReasoningEffort != "minimal" &&
			ReasoningEffort != "low" &&
			ReasoningEffort != "medium" &&
			ReasoningEffort != "high" &&
			ReasoningEffort != "xhigh")
			throw new InvalidOperationException(
				"OpenAI reasoning effort must be minimal, low, medium, high, or xhigh.");
		if (IsDeepSeek &&
			ReasoningEffort != "high" &&
			ReasoningEffort != "max")
			throw new InvalidOperationException(
				"DeepSeek reasoning effort must be high or max.");
	}

	private static Uri EnsureTrailingSlash(Uri value)
	{
		string text = value.AbsoluteUri;
		return text.EndsWith("/", StringComparison.Ordinal) ? value : new Uri(text + "/");
	}

	private static int ReadInteger(string name, int fallback)
	{
		string? value = Environment.GetEnvironmentVariable(name);
		return string.IsNullOrWhiteSpace(value) ? fallback :
			int.TryParse(value, out int parsed) ? parsed :
			throw new InvalidOperationException(name + " must be an integer.");
	}

	private static int ReadPositiveInteger(string name, int fallback)
	{
		int value = ReadInteger(name, fallback);
		if (value < 1) throw new InvalidOperationException(name + " must be positive.");
		return value;
	}

	private static TimeSpan ReadPositiveSeconds(string name, int fallback)
	{
		return TimeSpan.FromSeconds(ReadPositiveInteger(name, fallback));
	}
}
