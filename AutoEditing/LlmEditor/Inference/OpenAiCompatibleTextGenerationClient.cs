using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AutoEditing.LlmEditor.Inference;

internal sealed class OpenAiCompatibleTextGenerationClient : ITextGenerationClient
{
	private readonly HttpClient httpClient;
	private readonly OpenAiCompatibleOptions options;

	public OpenAiCompatibleTextGenerationClient(HttpClient httpClient, OpenAiCompatibleOptions options)
	{
		this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
		this.options = options ?? throw new ArgumentNullException(nameof(options));
		this.httpClient.BaseAddress = options.Endpoint;
		this.httpClient.Timeout = Timeout.InfiniteTimeSpan;
		if (!string.IsNullOrWhiteSpace(options.ApiKey))
			this.httpClient.DefaultRequestHeaders.Authorization =
				new AuthenticationHeaderValue("Bearer", options.ApiKey);
	}

	public async Task<TextGenerationResult> GenerateAsync(
		TextGenerationRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		List<object> userContent = new()
		{
			new { type = "text", text = request.UserPrompt }
		};
		foreach (VisualEvidence evidence in request.VisualEvidence)
		{
			if (!evidence.DataUrl.StartsWith("data:image/", StringComparison.Ordinal))
				throw new InvalidOperationException("Visual evidence must be an image data URL.");
			if (!string.IsNullOrWhiteSpace(evidence.Description))
			{
				userContent.Add(new
				{
					type = "text",
					text = "Visual evidence: " + evidence.Description
				});
			}
			userContent.Add(new
			{
				type = "image_url",
				image_url = new { url = evidence.DataUrl }
			});
		}

		var payload = new
		{
			model = options.Model,
			messages = new object[]
			{
				new { role = "system", content = request.SystemPrompt },
				new { role = "user", content = userContent }
			},
			temperature = request.Temperature,
			max_tokens = request.MaxOutputTokens
		};

		using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
			"chat/completions",
			payload,
			cancellationToken);
		string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException(
				$"The inference server returned {(int)response.StatusCode} ({response.ReasonPhrase}): " +
				Limit(responseBody, 1000));
		}

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

		return new TextGenerationResult
		{
			Text = content.GetString() ?? "",
			Model = root.TryGetProperty("model", out JsonElement model) &&
				model.ValueKind == JsonValueKind.String
				? model.GetString() ?? options.Model
				: options.Model
		};
	}

	private static string Limit(string value, int maximumLength)
	{
		return value.Length <= maximumLength ? value : value[..maximumLength] + "...";
	}
}
