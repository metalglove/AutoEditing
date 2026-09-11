using System.Security.Cryptography;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Inference;
using Core.Domain.Planning;
using Newtonsoft.Json;

namespace AutoEditing.LlmEditor.Assembly;

internal sealed class LlmCheckpointMultimodalReviewer : ICheckpointMultimodalReviewer
{
	private const string SystemPrompt = """
		You review one synchronization checkpoint in a video montage. You are an
		observer only: never output a timeline, edit plan, command, acceptance decision,
		or file operation. Judge only evidence actually supplied. The first image is a
		timing visualization; the remaining images are sampled VEGAS frames, not continuous
		video. Do not infer motion between samples. Relate gameplay event timing to musical anchors, cuts,
		editorial intent, and any human timeline adjustment. Return exactly one JSON
		object matching the schema, with concise observations and optional suggested
		changes for the human or a later planning request.
		""";

	private const string ResponseSchema = """
		{
		  "type": "object",
		  "additionalProperties": false,
		  "required": ["summary", "confidence", "observations", "suggestedChanges"],
		  "properties": {
		    "summary": { "type": "string", "minLength": 1 },
		    "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
		    "observations": {
		      "type": "array",
		      "items": {
		        "type": "object",
		        "additionalProperties": false,
		        "required": ["observationId", "category", "severity", "message", "confidence", "evidenceIds"],
		        "properties": {
		          "observationId": { "type": "string", "minLength": 1 },
		          "category": { "type": "string", "minLength": 1 },
		          "severity": { "type": "string", "enum": ["info", "warning", "error"] },
		          "message": { "type": "string", "minLength": 1 },
		          "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
		          "evidenceIds": {
		            "type": "array",
		            "items": { "type": "string", "minLength": 1 }
		          }
		        }
		      }
		    },
		    "suggestedChanges": {
		      "type": "array",
		      "items": { "type": "string", "minLength": 1 }
		    }
		  }
		}
		""";

	private readonly ITextGenerationClient client;
	private readonly InferenceBudgets budgets;
	private readonly Func<DateTimeOffset> clock;

	public LlmCheckpointMultimodalReviewer(
		ITextGenerationClient client,
		InferenceBudgets? budgets = null,
		Func<DateTimeOffset>? clock = null)
	{
		this.client = client ?? throw new ArgumentNullException(nameof(client));
		this.budgets = budgets ?? InferenceBudgets.FromEnvironment();
		this.clock = clock ?? (() => DateTimeOffset.UtcNow);
	}

	public async Task<CheckpointReviewReport> ReviewAsync(
		string sessionId,
		int checkpoint,
		int attempt,
		AssemblySketch sketch,
		ClipStepDecision decision,
		EditPlanDocument candidate,
		CandidateTimelineSnapshot timeline,
		CheckpointTimingSidecar timing,
		string timingVisualizationPath,
		RenderCandidatePreviewResult preview,
		IReadOnlyList<CheckpointPreviewFrameEvidence> frames,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
		if (checkpoint < 1 || attempt < 1)
			throw new ArgumentOutOfRangeException(nameof(checkpoint));
		ArgumentNullException.ThrowIfNull(sketch);
		ArgumentNullException.ThrowIfNull(decision);
		ArgumentNullException.ThrowIfNull(candidate);
		ArgumentNullException.ThrowIfNull(timeline);
		ArgumentNullException.ThrowIfNull(timing);
		ArgumentNullException.ThrowIfNull(preview);
		ArgumentNullException.ThrowIfNull(frames);
		if (!File.Exists(timingVisualizationPath))
			throw new FileNotFoundException(
				"Checkpoint timing visualization was not found.",
				timingVisualizationPath);
		byte[] image = File.ReadAllBytes(timingVisualizationPath);
		string evidenceHash =
			Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();
		List<VisualEvidence> evidence = new()
		{
			new()
			{
				DataUrl = "data:image/png;base64," + Convert.ToBase64String(image),
				Description = "timing-visualization"
			}
		};
		for (int index = 0; index < frames.Count; index++)
		{
			CheckpointPreviewFrameEvidence frame = frames[index];
			if (!File.Exists(frame.FullPath))
				throw new FileNotFoundException(
					"A checkpoint preview frame was not found.", frame.FullPath);
			byte[] frameBytes = File.ReadAllBytes(frame.FullPath);
			string actualFrameHash =
				Convert.ToHexString(SHA256.HashData(frameBytes)).ToLowerInvariant();
			if (!string.Equals(
				actualFrameHash, frame.Sha256, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"A checkpoint preview frame hash did not match.");
			evidence.Add(new VisualEvidence
			{
				DataUrl = "data:image/png;base64," +
					Convert.ToBase64String(frameBytes),
				Description = "frame-" + (index + 1).ToString("D2") +
					"@" + frame.TimelineTime.TotalSeconds.ToString(
						"0.###", System.Globalization.CultureInfo.InvariantCulture) + "s"
			});
		}
		TextGenerationResult generated = await client.GenerateAsync(
			new TextGenerationRequest
			{
				SystemPrompt = SystemPrompt,
				UserPrompt =
					$"Review synchronization checkpoint {checkpoint}, preview attempt {attempt}.\n\n" +
					"Global editorial thesis:\n" + sketch.EditorialThesis +
					"\n\nCurrent clip rationale:\n" + decision.Rationale +
					"\n\nCurrent clip decision:\n" + ContractSerializer.Serialize(decision) +
					"\n\nTiming sidecar:\n" + ContractSerializer.Serialize(timing) +
					"\n\nMaterialized timeline snapshot:\n" +
					ContractSerializer.Serialize(timeline) +
					"\n\nRendered preview metadata:\n" +
					ContractSerializer.Serialize(preview) +
					"\n\nTiming evidence SHA-256: " + evidenceHash +
					"\n\nThe supplied PNG frames are real VEGAS snapshots from the isolated " +
					"candidate at the sample times named in their evidence IDs.",
				VisualEvidence = evidence,
				Temperature = 0,
				MaxOutputTokens = budgets.ReviewMaxOutputTokens,
				JsonSchemaName = "checkpoint_sync_observations",
				JsonSchema = ResponseSchema
			},
			cancellationToken);
		ReviewResponse response;
		try
		{
			response = JsonConvert.DeserializeObject<ReviewResponse>(
				ExtractJson(generated.Text),
				new JsonSerializerSettings
				{
					MissingMemberHandling = MissingMemberHandling.Error
				}) ?? throw new JsonSerializationException("Response was null.");
		}
		catch (JsonException exception)
		{
			throw new InvalidOperationException(
				"The model returned invalid checkpoint-review JSON.",
				exception);
		}
		CheckpointReviewReport report = new()
		{
			SessionId = sessionId,
			Checkpoint = checkpoint,
			Attempt = attempt,
			CreatedUtc = clock(),
			Summary = response.Summary ?? "",
			Confidence = response.Confidence,
			Observations = (response.Observations ??
				throw new InvalidOperationException("Review observations are required."))
				.Select(item => new CheckpointReviewObservation
				{
					ObservationId = item.ObservationId ?? "",
					Category = item.Category ?? "",
					Severity = item.Severity ?? "",
					Message = item.Message ?? "",
					Confidence = item.Confidence,
					EvidenceIds = item.EvidenceIds ?? new List<string>()
				})
				.ToList(),
			SuggestedChanges = response.SuggestedChanges ??
				throw new InvalidOperationException("Suggested changes are required.")
		};
		CheckpointPreviewContractValidator.Validate(report);
		HashSet<string> allowedEvidence =
			evidence.Select(item => item.Description).ToHashSet(StringComparer.Ordinal);
		if (report.Observations.SelectMany(item => item.EvidenceIds)
			.Any(id => !allowedEvidence.Contains(id)))
			throw new InvalidOperationException(
				"Checkpoint review referenced evidence that was not supplied.");
		return report;
	}

	private static string ExtractJson(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			throw new InvalidOperationException("The model returned an empty checkpoint review.");
		string trimmed = text.Trim();
		if (trimmed.StartsWith("```", StringComparison.Ordinal))
		{
			int firstLine = trimmed.IndexOf('\n');
			int closing = trimmed.LastIndexOf("```", StringComparison.Ordinal);
			if (firstLine < 0 || closing <= firstLine)
				throw new InvalidOperationException(
					"The fenced checkpoint review was incomplete.");
			trimmed = trimmed.Substring(firstLine + 1, closing - firstLine - 1).Trim();
		}
		if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
			throw new InvalidOperationException(
				"The checkpoint review must contain one JSON object.");
		return trimmed;
	}

	private sealed class ReviewResponse
	{
		[JsonProperty("summary", Required = Required.Always)]
		public string? Summary { get; set; }

		[JsonProperty("confidence", Required = Required.Always)]
		public double Confidence { get; set; }

		[JsonProperty("observations", Required = Required.Always)]
		public List<ReviewObservation>? Observations { get; set; }

		[JsonProperty("suggestedChanges", Required = Required.Always)]
		public List<string>? SuggestedChanges { get; set; }
	}

	private sealed class ReviewObservation
	{
		[JsonProperty("observationId", Required = Required.Always)]
		public string? ObservationId { get; set; }

		[JsonProperty("category", Required = Required.Always)]
		public string? Category { get; set; }

		[JsonProperty("severity", Required = Required.Always)]
		public string? Severity { get; set; }

		[JsonProperty("message", Required = Required.Always)]
		public string? Message { get; set; }

		[JsonProperty("confidence", Required = Required.Always)]
		public double Confidence { get; set; }

		[JsonProperty("evidenceIds", Required = Required.Always)]
		public List<string>? EvidenceIds { get; set; }
	}
}
