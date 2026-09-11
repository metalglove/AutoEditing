using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.LlmEditor.Assembly;
using AutoEditing.LlmEditor.Inference;
using Newtonsoft.Json.Linq;

namespace AutoEditing.LlmEditor.Workbench;

internal sealed class LlamaCppProgressMonitor
{
	private readonly HttpClient client;
	private readonly OpenAiCompatibleOptions options;
	private readonly WorkbenchSessionPublisher publisher;
	private readonly string sessionId;
	private readonly DateTimeOffset startedUtc;
	private EditSessionProgress latest;

	public LlamaCppProgressMonitor(
		HttpClient client,
		OpenAiCompatibleOptions options,
		WorkbenchSessionPublisher publisher,
		string sessionId)
	{
		this.client = client ?? throw new ArgumentNullException(nameof(client));
		this.options = options ?? throw new ArgumentNullException(nameof(options));
		this.publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
		this.sessionId = sessionId;
		startedUtc = DateTimeOffset.UtcNow;
		latest = NewProgress("Preparing request", false);
	}

	public async Task RunAsync(CancellationToken cancellationToken)
	{
		publisher.PublishProgress(latest);
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				AssemblySessionState? state =
					new AssemblyActionStore(publisher.SessionRoot).ReadState();
				string durableStage = DescribeStage(state);
				string durableMessage = string.IsNullOrWhiteSpace(state?.Status)
					? "Waiting for the current workflow operation."
					: state.Status;
				latest.Stage = durableStage;
				latest.Message = durableMessage;
				latest.UpdatedUtc = DateTimeOffset.UtcNow;
				latest.Elapsed = latest.UpdatedUtc - startedUtc;
				publisher.PublishProgress(latest);
				Uri slotsUri = new Uri(options.Endpoint, "../slots");
				using HttpResponseMessage response =
					await client.GetAsync(slotsUri, cancellationToken);
				if (response.IsSuccessStatusCode)
				{
					string json = await response.Content.ReadAsStringAsync();
					JToken root = JToken.Parse(json);
					JToken? slot = root.Type == JTokenType.Array
						? root.Children().FirstOrDefault(item => item.Value<bool?>("is_processing") == true)
							?? root.First
						: root;
					if (slot != null)
					{
						JToken? parameters = slot["params"];
						JToken? nextToken = slot["next_token"] is JArray nextArray
							? nextArray.First
							: slot["next_token"];
						latest = new EditSessionProgress
						{
							SessionId = sessionId,
							Stage = durableStage,
							UpdatedUtc = DateTimeOffset.UtcNow,
							Elapsed = DateTimeOffset.UtcNow - startedUtc,
							IsProcessing = slot.Value<bool?>("is_processing") ?? false,
							PromptTokens = slot.Value<int?>("n_prompt_tokens") ?? latest.PromptTokens,
							PromptTokensProcessed =
								slot.Value<int?>("n_prompt_tokens_processed") ?? latest.PromptTokensProcessed,
							GeneratedTokens = nextToken?.Value<int?>("n_decoded") ?? latest.GeneratedTokens,
							MaximumGeneratedTokens =
								parameters?.Value<int?>("max_tokens") ?? latest.MaximumGeneratedTokens,
							Message = durableMessage
						};
						publisher.PublishProgress(latest);
					}
				}
			}
			catch (Exception) when (!cancellationToken.IsCancellationRequested)
			{
				// A transient monitoring failure must never cancel model inference.
			}
			await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
		}
	}

	public void Complete(string stage, string message)
	{
		latest.Stage = stage;
		latest.Message = message;
		latest.IsProcessing = false;
		latest.UpdatedUtc = DateTimeOffset.UtcNow;
		latest.Elapsed = latest.UpdatedUtc - startedUtc;
		publisher.PublishProgress(latest);
	}

	internal static string DescribeStage(AssemblySessionState? state)
		=> InferenceProgressStage.Describe(state);

	private EditSessionProgress NewProgress(string stage, bool processing) =>
		new EditSessionProgress
		{
			SessionId = sessionId,
			Stage = stage,
			UpdatedUtc = DateTimeOffset.UtcNow,
			Elapsed = DateTimeOffset.UtcNow - startedUtc,
			IsProcessing = processing
		};
}
