using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutoEditing.LlmEditor.Inference;

internal sealed class RecordingTextGenerationClient : ITextGenerationClient
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true
	};

	private readonly ITextGenerationClient inner;
	private readonly string transcriptRoot;
	private int sequence;

	public RecordingTextGenerationClient(ITextGenerationClient inner, string sessionRoot)
	{
		this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
		if (string.IsNullOrWhiteSpace(sessionRoot))
			throw new ArgumentException("A session root is required.", nameof(sessionRoot));
		transcriptRoot = Path.Combine(sessionRoot, "inference", "exchanges");
		Directory.CreateDirectory(transcriptRoot);
		sequence = Directory.EnumerateDirectories(transcriptRoot).Count();
	}

	public async Task<TextGenerationResult> GenerateAsync(
		TextGenerationRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		int call = Interlocked.Increment(ref sequence);
		string operation = SafeName(request.JsonSchemaName ?? "text-generation");
		string exchangeRoot = Path.Combine(transcriptRoot, $"{call:0000}-{operation}");
		Directory.CreateDirectory(exchangeRoot);
		DateTimeOffset startedAt = DateTimeOffset.UtcNow;
		WriteJson(Path.Combine(exchangeRoot, "request.json"), new
		{
			startedAt,
			request.SystemPrompt,
			request.UserPrompt,
			request.Temperature,
			request.MaxOutputTokens,
			request.JsonSchemaName,
			request.JsonSchema,
			visualEvidence = request.VisualEvidence.Select(evidence => new
			{
				evidence.Description,
				mediaType = MediaType(evidence.DataUrl),
				encodedCharacters = evidence.DataUrl.Length,
				sha256 = Sha256(evidence.DataUrl)
			})
		});
		string partialPath = Path.Combine(exchangeRoot, "assistant.partial.txt");
		object partialLock = new();
		TextGenerationRequest recordedRequest = CopyWithDeltaObserver(
			request,
			delta =>
			{
				lock (partialLock)
					File.AppendAllText(
						partialPath,
						delta,
						new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				request.OnTextDelta?.Invoke(delta);
			});

		try
		{
			TextGenerationResult result = await inner.GenerateAsync(recordedRequest, cancellationToken);
			WriteResponse(exchangeRoot, startedAt, result);
			return result;
		}
		catch (TruncatedGenerationException error)
		{
			WriteResponse(exchangeRoot, startedAt, error.Result);
			WriteError(exchangeRoot, startedAt, error);
			throw;
		}
		catch (Exception error)
		{
			WriteError(exchangeRoot, startedAt, error);
			throw;
		}
	}

	private static TextGenerationRequest CopyWithDeltaObserver(
		TextGenerationRequest request,
		Action<string> observer)
	{
		return new TextGenerationRequest
		{
			SystemPrompt = request.SystemPrompt,
			UserPrompt = request.UserPrompt,
			VisualEvidence = request.VisualEvidence,
			Temperature = request.Temperature,
			MaxOutputTokens = request.MaxOutputTokens,
			JsonSchemaName = request.JsonSchemaName,
			JsonSchema = request.JsonSchema,
			OnTextDelta = observer,
			OnReasoningDelta = request.OnReasoningDelta
		};
	}

	private static void WriteResponse(
		string exchangeRoot,
		DateTimeOffset startedAt,
		TextGenerationResult result)
	{
		File.WriteAllText(
			Path.Combine(exchangeRoot, "assistant.txt"),
			result.Text,
			new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		WriteJson(Path.Combine(exchangeRoot, "response.json"), new
		{
			completedAt = DateTimeOffset.UtcNow,
			durationMilliseconds = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
			result.Model,
			result.ResponseId,
			result.FinishReason,
			result.RetryCount,
			result.Usage,
			result.Timings
		});
	}

	private static void WriteError(
		string exchangeRoot,
		DateTimeOffset startedAt,
		Exception error)
	{
		WriteJson(Path.Combine(exchangeRoot, "error.json"), new
		{
			failedAt = DateTimeOffset.UtcNow,
			durationMilliseconds = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
			type = error.GetType().FullName,
			error.Message,
			error.StackTrace
		});
	}

	private static void WriteJson(string path, object value)
	{
		File.WriteAllText(
			path,
			JsonSerializer.Serialize(value, JsonOptions),
			new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
	}

	private static string SafeName(string value)
	{
		StringBuilder result = new();
		foreach (char character in value.ToLowerInvariant())
			result.Append(char.IsLetterOrDigit(character) ? character : '-');
		string safe = result.ToString().Trim('-');
		return string.IsNullOrWhiteSpace(safe) ? "text-generation" : safe;
	}

	private static string MediaType(string dataUrl)
	{
		int separator = dataUrl.IndexOf(';');
		return dataUrl.StartsWith("data:", StringComparison.Ordinal) && separator > 5
			? dataUrl.Substring(5, separator - 5)
			: "unknown";
	}

	private static string Sha256(string value)
	{
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
		return Convert.ToHexString(hash).ToLowerInvariant();
	}
}
