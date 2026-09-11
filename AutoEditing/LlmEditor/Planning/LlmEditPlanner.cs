using AutoEditing.LlmEditor.Inference;
using AutoEditing.LlmEditor.Iteration;
using AutoEditing.Iteration.Contracts.Serialization;
using Core.Domain.Planning;
using Newtonsoft.Json;

namespace AutoEditing.LlmEditor.Planning;

internal sealed class LlmEditPlanner : IIterativeEditPlanner
{
	private const string SystemPrompt = """
		You are the planning component of an iterative professional video editor. Design one
		complete, globally coherent baseline montage from the supplied, already validated media
		and song analysis. Consider the whole musical arc before assigning individual clips.

		Priority order:
		1. Hard constraints in the planning request and deterministic editing rules.
		2. User direction and explicitly selected style profiles.
		3. Replicated findings in the supplied editor-style evidence profile.
		4. Limited or editor-specific findings only when their stated conditions apply.

		The findings profile is evidence, not a checklist. Adapt it to the current material.
		Never invent media, sync points, plugins, renderer capabilities, or unsupported effects.
		Keep strong clips available for structural peaks. Prefer clear pacing, readable
		synchronization, and purposeful contrast over density.

		The request's songAnalysis is the authoritative reviewed musical map. Build the montage
		across its regions and use effective event times, classifications, priorities, locks, and
		editorial uses when choosing synchronization. Never infer a replacement beat grid, invent
		musical event IDs or times, or ignore the song map in favor of clip-only sequencing.
		songAnalysis.eventTimeline is a compact positional table whose columns are declared by
		eventTimelineColumns. It contains every detected event, including rejected observations,
		so use it to understand pulse, density, accents, and musical development. Rejected rows
		are context only. Only detailed entries in songAnalysis.events are legal sync targets.

		Return exactly one compact JSON object matching the supplied LlmEditDecisionDocument
		schema. Author editorial decisions only: clip identity, source window, constant speed,
		and kill-to-music-event mappings. Do not reproduce clip metadata, songPlan, calculated
		timeline starts or ends, transformed shot events, speed-profile totals, effects, or
		derived diagnostics. Deterministic code compiles and validates those fields.
		Do not return Markdown, commentary, private reasoning, commands, source code, or file
		operations. Preserve requestId and schemaVersion. Use only clipPath values and musical
		event IDs present in the request.
		""";

	private readonly ITextGenerationClient generationClient;
	private readonly InferenceBudgets budgets;
	private readonly string styleFindings;

	public LlmEditPlanner(
		ITextGenerationClient generationClient,
		InferenceBudgets? budgets = null,
		string? styleFindings = null)
	{
		this.generationClient = generationClient ??
			throw new ArgumentNullException(nameof(generationClient));
		this.budgets = budgets ?? InferenceBudgets.FromEnvironment();
		this.styleFindings = styleFindings ?? EditorStyleFindings.LoadDefault();
	}

	public Task<EditPlanDocument> CreatePlanAsync(
		EditPlanningRequest request,
		CancellationToken cancellationToken)
	{
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		RequireReviewedSongAnalysis(request);
		string prompt =
			"VERSIONED EDITOR-STYLE EVIDENCE\n\n" + styleFindings +
			"\n\nACTUAL VALIDATED PLANNING REQUEST\n" +
			"This is the authoritative input.\n\n" +
			EditPlanDocumentSerializer.SerializeRequest(request) +
			"\n\nFINAL TASK\n" +
			"Create a complete original edit for the actual request. Make deliberate clip-order, " +
			"source-window, synchronization, and allowed constant-speed choices. Use songAnalysis " +
			"to shape the overall arc and select only detailed songAnalysis.events as sync targets. " +
			"Place every selected clip exactly once and cover every usable reviewed song region " +
			"that contains an eligible gameplay anchor. " +
			"Every placement must have a primarySync. killIndex is zero-based within confirmedKills " +
			"ordered by sourceConfirmationTimeSeconds. Use additionalSyncs only when the same " +
			"constant-speed placement makes those kills align exactly with their selected events. " +
			"Return only the compact JSON decisions.";
		return GenerateValidatedPlanAsync(
			request,
			new TextGenerationRequest
			{
				SystemPrompt = SystemPrompt,
				UserPrompt = prompt,
				Temperature = 0.1,
				MaxOutputTokens = budgets.PlanningMaxOutputTokens,
				JsonSchemaName = "llm_edit_decisions",
				JsonSchema = LlmDecisionSchema.Json
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
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		RequireReviewedSongAnalysis(request);
		string prompt =
			$"Revise candidate edit after review iteration {iteration}.\n\n" +
			"Versioned editor-style evidence:\n\n" + styleFindings +
			"\n\nPlanning request:\n" + EditPlanDocumentSerializer.SerializeRequest(request) +
			"\n\nPrevious compiled candidate (evidence only; answer with compact decisions):\n" +
			EditPlanDocumentSerializer.SerializePlan(previousPlan) +
			"\n\nReview evidence and critique:\n" + feedback.Summary +
			"\n\nAuthoritative VEGAS timeline adjustment delta:\n" +
			(feedback.TimelineAdjustment == null
				? "No timeline adjustment was captured."
				: ContractSerializer.Serialize(feedback.TimelineAdjustment)) +
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
				MaxOutputTokens = budgets.PlanningMaxOutputTokens,
				JsonSchemaName = "llm_edit_decision_revision",
				JsonSchema = LlmDecisionSchema.Json
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
		LlmEditDecisionDocument decisions;
		try
		{
			decisions = JsonConvert.DeserializeObject<LlmEditDecisionDocument>(
				ExtractJsonObject(result.Text),
				new JsonSerializerSettings
				{
					MissingMemberHandling = MissingMemberHandling.Error
				}) ??
				throw new JsonException("The decision document is empty.");
		}
		catch (JsonException exception)
		{
			throw new InvalidOperationException("The model returned invalid edit-decision JSON.", exception);
		}
		return new LlmDecisionCompiler().Compile(request, decisions, result.Model);
	}

	private static void RequireReviewedSongAnalysis(EditPlanningRequest request)
	{
		if (request.SongAnalysis == null ||
			request.SongAnalysis.Mode != Core.Domain.Editing.MontageSongPlanningMode.ReviewedSongMap)
			throw new InvalidOperationException(
				"LLM planning requires the committed reviewed song analysis.");
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
