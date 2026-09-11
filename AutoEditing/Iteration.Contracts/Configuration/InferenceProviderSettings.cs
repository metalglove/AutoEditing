using System;
using System.IO;
using Newtonsoft.Json;

namespace AutoEditing.Iteration.Contracts.Configuration;

public static class InferenceProviderIds
{
	public const string LlamaCpp = "llamacpp";
	public const string OpenAi = "openai";
	public const string DeepSeek = "deepseek";
}

public sealed class InferenceProviderSettings
{
	public const int CurrentSchemaVersion = 3;
	public const string DefaultOpenAiCredentialTarget =
		"AutoEditing/Inference/OpenAI";
	public const string DefaultDeepSeekCredentialTarget =
		"AutoEditing/Inference/DeepSeek";

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string Provider { get; set; } = InferenceProviderIds.LlamaCpp;
	public string LlamaCppEndpoint { get; set; } =
		"http://192.168.1.71:18080/v1/";
	public string LlamaCppModel { get; set; } = "";
	public string OpenAiEndpoint { get; set; } =
		"https://api.openai.com/v1/";
	public string OpenAiModel { get; set; } = "gpt-5.6-luna";
	public string OpenAiReasoningEffort { get; set; } = "high";
	public string OpenAiCredentialTarget { get; set; } =
		DefaultOpenAiCredentialTarget;
	public string DeepSeekEndpoint { get; set; } =
		"https://api.deepseek.com/";
	public string DeepSeekModel { get; set; } = "deepseek-v4-pro";
	public string DeepSeekThinkingMode { get; set; } = "enabled";
	public string DeepSeekReasoningEffort { get; set; } = "max";
	public string DeepSeekCredentialTarget { get; set; } =
		DefaultDeepSeekCredentialTarget;

	public void ValidateAndNormalize()
	{
		if (SchemaVersion != CurrentSchemaVersion)
			throw new InvalidDataException(
				$"Unsupported inference-settings schema {SchemaVersion}.");
		Provider = (Provider ?? "").Trim().ToLowerInvariant();
		if (Provider != InferenceProviderIds.LlamaCpp &&
			Provider != InferenceProviderIds.OpenAi &&
			Provider != InferenceProviderIds.DeepSeek)
			throw new InvalidDataException(
				"Inference provider must be 'llamacpp', 'openai', or 'deepseek'.");
		LlamaCppEndpoint = NormalizeEndpoint(
			LlamaCppEndpoint, nameof(LlamaCppEndpoint));
		OpenAiEndpoint = NormalizeEndpoint(
			OpenAiEndpoint, nameof(OpenAiEndpoint));
		LlamaCppModel = (LlamaCppModel ?? "").Trim();
		OpenAiModel = RequireText(OpenAiModel, nameof(OpenAiModel));
		OpenAiReasoningEffort = RequireText(
			OpenAiReasoningEffort, nameof(OpenAiReasoningEffort)).ToLowerInvariant();
		if (OpenAiReasoningEffort != "minimal" &&
			OpenAiReasoningEffort != "low" &&
			OpenAiReasoningEffort != "medium" &&
			OpenAiReasoningEffort != "high" &&
			OpenAiReasoningEffort != "xhigh")
			throw new InvalidDataException(
				"OpenAiReasoningEffort must be minimal, low, medium, high, or xhigh.");
		OpenAiCredentialTarget = RequireText(
			OpenAiCredentialTarget, nameof(OpenAiCredentialTarget));
		DeepSeekEndpoint = NormalizeEndpoint(
			DeepSeekEndpoint, nameof(DeepSeekEndpoint));
		DeepSeekModel = RequireText(
			DeepSeekModel, nameof(DeepSeekModel)).ToLowerInvariant();
		if (DeepSeekModel != "deepseek-v4-flash" &&
			DeepSeekModel != "deepseek-v4-pro")
			throw new InvalidDataException(
				"DeepSeekModel must be deepseek-v4-flash or deepseek-v4-pro.");
		DeepSeekThinkingMode = RequireText(
			DeepSeekThinkingMode, nameof(DeepSeekThinkingMode)).ToLowerInvariant();
		if (DeepSeekThinkingMode != "enabled" &&
			DeepSeekThinkingMode != "disabled")
			throw new InvalidDataException(
				"DeepSeekThinkingMode must be enabled or disabled.");
		DeepSeekReasoningEffort = RequireText(
			DeepSeekReasoningEffort, nameof(DeepSeekReasoningEffort)).ToLowerInvariant();
		if (DeepSeekReasoningEffort != "high" &&
			DeepSeekReasoningEffort != "max")
			throw new InvalidDataException(
				"DeepSeekReasoningEffort must be high or max.");
		DeepSeekCredentialTarget = RequireText(
			DeepSeekCredentialTarget, nameof(DeepSeekCredentialTarget));
	}

	private static string NormalizeEndpoint(string value, string name)
	{
		if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
			(uri.Scheme != Uri.UriSchemeHttp &&
			 uri.Scheme != Uri.UriSchemeHttps))
			throw new InvalidDataException(
				name + " must be an absolute HTTP or HTTPS URL.");
		string text = uri.AbsoluteUri;
		return text.EndsWith("/", StringComparison.Ordinal)
			? text
			: text + "/";
	}

	private static string RequireText(string value, string name)
	{
		string normalized = (value ?? "").Trim();
		if (normalized.Length == 0)
			throw new InvalidDataException(name + " cannot be empty.");
		return normalized;
	}
}

public static class InferenceProviderSettingsStore
{
	public static string DefaultPath =>
		Path.Combine(
			Environment.GetFolderPath(
				Environment.SpecialFolder.LocalApplicationData),
			"AutoEditing",
			"settings",
			"inference.json");

	public static InferenceProviderSettings LoadOrDefault(
		string path = null)
	{
		string resolved = Path.GetFullPath(path ?? DefaultPath);
		if (!File.Exists(resolved))
		{
			InferenceProviderSettings defaults = new();
			defaults.ValidateAndNormalize();
			return defaults;
		}
		InferenceProviderSettings settings =
			JsonConvert.DeserializeObject<InferenceProviderSettings>(
				File.ReadAllText(resolved))
			?? throw new InvalidDataException(
				"Inference settings contain JSON null.");
		if (settings.SchemaVersion == 1 || settings.SchemaVersion == 2)
		{
			if (settings.SchemaVersion == 1 && string.Equals(
					settings.OpenAiModel,
					"gpt-5.6-sol",
					StringComparison.Ordinal))
				settings.OpenAiModel = "gpt-5.6-luna";
			settings.OpenAiReasoningEffort = "high";
			settings.SchemaVersion = InferenceProviderSettings.CurrentSchemaVersion;
			Save(settings, resolved);
			return settings;
		}
		settings.ValidateAndNormalize();
		return settings;
	}

	public static void Save(
		InferenceProviderSettings settings,
		string path = null)
	{
		if (settings == null)
			throw new ArgumentNullException(nameof(settings));
		settings.ValidateAndNormalize();
		string resolved = Path.GetFullPath(path ?? DefaultPath);
		Directory.CreateDirectory(Path.GetDirectoryName(resolved));
		string temporary = resolved + "." + Guid.NewGuid().ToString("N") +
			".tmp";
		try
		{
			File.WriteAllText(
				temporary,
				JsonConvert.SerializeObject(
					settings, Formatting.Indented));
			if (File.Exists(resolved))
				File.Replace(temporary, resolved, null);
			else
				File.Move(temporary, resolved);
		}
		finally
		{
			if (File.Exists(temporary))
				File.Delete(temporary);
		}
	}
}
