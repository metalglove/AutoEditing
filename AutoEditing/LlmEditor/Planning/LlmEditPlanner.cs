using AutoEditing.LlmEditor.Inference;
using AutoEditing.LlmEditor.Iteration;
using Core.Domain.Planning;
using Newtonsoft.Json;

namespace AutoEditing.LlmEditor.Planning;

internal sealed class LlmEditPlanner : IIterativeEditPlanner
{
	private const string SystemPrompt = """
		You are an offline edit-planning component. Return exactly one JSON object matching
		the supplied EditPlanDocument schema. Do not return Markdown, prose, commands, source
		code, or file operations. Preserve requestId and schemaVersion. Use only clip filePath
		values and styleProfileIds present in the request. Your output is untrusted and will
		be structurally validated before it can be imported by an editor.
		""";

	private readonly ITextGenerationClient generationClient;

	public LlmEditPlanner(ITextGenerationClient generationClient)
	{
		this.generationClient = generationClient ??
			throw new ArgumentNullException(nameof(generationClient));
	}

	public async Task<EditPlanDocument> CreatePlanAsync(
		EditPlanningRequest request,
		CancellationToken cancellationToken)
	{
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		string requestJson = EditPlanDocumentSerializer.SerializeRequest(request);
		EditPlanDocument examplePlan = await new FakeLlmEditPlanner()
			.CreatePlanAsync(request, cancellationToken);
		return await GenerateValidatedPlanAsync(
			request,
			new TextGenerationRequest
			{
				SystemPrompt = SystemPrompt,
				UserPrompt =
					"Create an edit plan for this validated planning request.\n\n" +
					requestJson +
					"\n\nThe output must have this JSON shape. This is a structural example, " +
					"not the requested creative result:\n\n" +
					EditPlanDocumentSerializer.SerializePlan(examplePlan),
				Temperature = 0.1,
				MaxOutputTokens = 16384
			},
			cancellationToken);
	}

	public Task<EditPlanDocument> RevisePlanAsync(
		EditPlanningRequest request,
		EditPlanDocument previousPlan,
		EditIterationFeedback feedback,
		int iteration,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(previousPlan);
		ArgumentNullException.ThrowIfNull(feedback);
		string prompt =
			$"Revise candidate edit plan after review iteration {iteration}.\n\n" +
			"Planning request:\n" + EditPlanDocumentSerializer.SerializeRequest(request) +
			"\n\nPrevious candidate:\n" + EditPlanDocumentSerializer.SerializePlan(previousPlan) +
			"\n\nReview evidence and critique:\n" + feedback.Summary +
			"\n\nUser steering constraints:\n" +
			string.Join("\n", feedback.SteeringInstructions.Select(value => "- " + value));
		return GenerateValidatedPlanAsync(
			request,
			new TextGenerationRequest
			{
				SystemPrompt = SystemPrompt,
				UserPrompt = prompt,
				VisualEvidence = feedback.VisualEvidence,
				Temperature = 0.1,
				MaxOutputTokens = 16384
			},
			cancellationToken);
	}

	private async Task<EditPlanDocument> GenerateValidatedPlanAsync(
		EditPlanningRequest request,
		TextGenerationRequest generationRequest,
		CancellationToken cancellationToken)
	{
		TextGenerationResult result = await generationClient.GenerateAsync(
			generationRequest,
			cancellationToken);
		EditPlanDocument plan;
		try
		{
			plan = EditPlanDocumentSerializer.DeserializePlan(ExtractJsonObject(result.Text));
		}
		catch (JsonException exception)
		{
			throw new InvalidOperationException("The model returned invalid edit-plan JSON.", exception);
		}

		if (!string.Equals(plan.RequestId, request.RequestId, StringComparison.Ordinal))
			throw new InvalidOperationException("The model changed the planning request ID.");

		Dictionary<string, Core.Domain.Clip.Clip> requestedClips =
			new(StringComparer.OrdinalIgnoreCase);
		foreach (Core.Domain.Clip.Clip requestedClip in request.Clips)
		{
			if (!requestedClips.TryAdd(requestedClip.FilePath, requestedClip))
				throw new InvalidOperationException(
					"The planning request contains the same clip path more than once.");
		}
		foreach (var placement in plan.Montage.Placements)
		{
			if (!requestedClips.TryGetValue(placement.Clip.FilePath, out Core.Domain.Clip.Clip? requestedClip))
				throw new InvalidOperationException(
					"The model referenced a clip that was not present in the planning request.");
			placement.Clip = requestedClip;
		}

		plan.PlannerId = "llm.openai-compatible";
		plan.PlannerVersion = string.IsNullOrWhiteSpace(result.Model) ? "unknown" : result.Model;
		plan.StyleProfileIds = request.StyleProfileIds.ToList();
		plan.Montage.EffectOptions = request.EffectOptions;
		EditPlanDocumentValidator.ValidateAndNormalize(plan);
		return plan;
	}

	private static string ExtractJsonObject(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			throw new InvalidOperationException("The model returned an empty response.");

		string trimmed = text.Trim();
		int first = trimmed.IndexOf('{');
		int last = trimmed.LastIndexOf('}');
		if (first < 0 || last < first)
			throw new InvalidOperationException("The model response did not contain a JSON object.");
		return trimmed[first..(last + 1)];
	}
}
