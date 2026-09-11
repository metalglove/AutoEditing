using System.Text.Json;
using AutoEditing.Iteration.Contracts.Sessions;

namespace AutoEditing.LlmEditor.Inference;

internal sealed class FileInferenceUsageSink : IInferenceUsageSink
{
	private static readonly object Gate = new();
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
	private readonly string sessionRoot;
	private readonly string lifetimeRoot;

	public FileInferenceUsageSink(string sessionRoot, string? telemetryRoot = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionRoot);
		this.sessionRoot = Path.GetFullPath(sessionRoot);
		lifetimeRoot = Path.GetFullPath(telemetryRoot ?? Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"AutoEditing", "telemetry", "models"));
	}

	public void Record(InferenceUsageRecord record)
	{
		ArgumentNullException.ThrowIfNull(record);
		if (string.IsNullOrWhiteSpace(record.Model))
			throw new InvalidOperationException("Usage records require an exact model identifier.");
		lock (Gate)
		{
			Directory.CreateDirectory(sessionRoot);
			string ledger = Path.Combine(sessionRoot, "usage.ndjson");
			File.AppendAllText(ledger, JsonSerializer.Serialize(record) + Environment.NewLine);
			InferenceUsageSummary session = ReadSummary(Path.Combine(sessionRoot, "usage-summary.json"));
			WriteAtomic(Path.Combine(sessionRoot, "usage-summary.json"),
				Add(session, record, "session", record.SessionId));

			string modelDirectory = Path.Combine(
				lifetimeRoot,
				SafeIdentityKey(record.Provider, record.Model));
			Directory.CreateDirectory(modelDirectory);
			string lifetimePath = Path.Combine(modelDirectory, "usage-summary.json");
			InferenceUsageSummary lifetime = ReadSummary(lifetimePath);
			WriteAtomic(lifetimePath, Add(lifetime, record, "lifetime-model", null));
		}
	}

	internal static string SafeIdentityKey(string? provider, string model)
	{
		byte[] bytes = System.Security.Cryptography.SHA256.HashData(
			System.Text.Encoding.UTF8.GetBytes(
				(provider ?? "") + "\n" + model));
		return Convert.ToHexString(bytes).ToLowerInvariant();
	}

	private static InferenceUsageSummary Add(
		InferenceUsageSummary value,
		InferenceUsageRecord record,
		string scope,
		string? sessionId)
	{
		bool hasPrior = value.CallCount > 0;
		bool mixedModels = value.HasMixedModels ||
			(hasPrior &&
				!string.IsNullOrWhiteSpace(value.Model) &&
				!string.Equals(value.Model, record.Model, StringComparison.Ordinal));
		bool mixedProviders = value.HasMixedProviders ||
			(hasPrior &&
				!string.IsNullOrWhiteSpace(value.Provider) &&
				!string.Equals(
				value.Provider ?? "",
				record.Provider ?? "",
				StringComparison.Ordinal));
		return new InferenceUsageSummary
		{
			Scope = scope,
			SessionId = sessionId,
			Model = mixedModels ? null : record.Model,
			Provider = mixedProviders ? null : record.Provider,
			HasMixedModels = mixedModels,
			HasMixedProviders = mixedProviders,
			UpdatedUtc = record.TimestampUtc,
			CallCount = value.CallCount + 1,
			PromptTokens = value.PromptTokens + record.PromptTokens,
			CachedPromptTokens = value.CachedPromptTokens + record.CachedPromptTokens,
			GeneratedTokens = value.GeneratedTokens + record.GeneratedTokens,
			TotalTokens = value.TotalTokens + record.TotalTokens,
			PromptMilliseconds = value.PromptMilliseconds + record.PromptMilliseconds,
			TimeToFirstTokenMilliseconds =
				value.TimeToFirstTokenMilliseconds +
				record.TimeToFirstTokenMilliseconds,
			TimeToFirstTokenSampleCount =
				value.TimeToFirstTokenSampleCount +
				(record.TimeToFirstTokenMilliseconds > 0 ? 1 : 0),
			GenerationMilliseconds =
				value.GenerationMilliseconds + record.GenerationMilliseconds,
			ContextTokensPeak = Max(value.ContextTokensPeak, record.ContextTokensPeak),
			RetryCount = value.RetryCount + record.RetryCount,
			Cost = record.Cost.HasValue || value.Cost.HasValue
				? (value.Cost ?? 0) + (record.Cost ?? 0)
				: null,
			CostCurrency = record.CostCurrency ?? value.CostCurrency
		};
	}

	private static int? Max(int? left, int? right) =>
		left.HasValue && right.HasValue ? Math.Max(left.Value, right.Value) : left ?? right;

	private static InferenceUsageSummary ReadSummary(string path)
	{
		if (!File.Exists(path)) return new InferenceUsageSummary();
		return JsonSerializer.Deserialize<InferenceUsageSummary>(File.ReadAllText(path))
			?? new InferenceUsageSummary();
	}

	private static void WriteAtomic(string path, InferenceUsageSummary value)
	{
		string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
		File.Move(temporary, path, true);
	}
}
