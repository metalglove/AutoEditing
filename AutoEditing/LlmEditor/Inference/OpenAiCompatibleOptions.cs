namespace AutoEditing.LlmEditor.Inference;

internal sealed class OpenAiCompatibleOptions
{
	public required Uri Endpoint { get; init; }

	public required string Model { get; init; }

	public string? ApiKey { get; init; }

	public static OpenAiCompatibleOptions FromEnvironment()
	{
		string endpoint = Environment.GetEnvironmentVariable("AUTOEDITING_LLM_ENDPOINT")
			?? "http://127.0.0.1:8000/v1/";
		string model = Environment.GetEnvironmentVariable("AUTOEDITING_LLM_MODEL")
			?? throw new InvalidOperationException(
				"AUTOEDITING_LLM_MODEL must name the model served by the local inference server.");
		if (string.IsNullOrWhiteSpace(model))
			throw new InvalidOperationException("AUTOEDITING_LLM_MODEL cannot be empty.");

		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? endpointUri) ||
			(endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
		{
			throw new InvalidOperationException(
				"AUTOEDITING_LLM_ENDPOINT must be an absolute HTTP or HTTPS URL.");
		}

		return new OpenAiCompatibleOptions
		{
			Endpoint = EnsureTrailingSlash(endpointUri),
			Model = model.Trim(),
			ApiKey = Environment.GetEnvironmentVariable("AUTOEDITING_LLM_API_KEY")
		};
	}

	private static Uri EnsureTrailingSlash(Uri value)
	{
		string text = value.AbsoluteUri;
		return text.EndsWith("/", StringComparison.Ordinal) ? value : new Uri(text + "/");
	}
}
