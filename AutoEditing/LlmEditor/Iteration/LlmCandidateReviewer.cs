using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Iterations;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Inference;
using Core.Domain.Planning;
using Newtonsoft.Json;
using System.Security.Cryptography;

namespace AutoEditing.LlmEditor.Iteration;

internal sealed class LlmCandidateReviewer : IWorkbenchCandidateReviewer
{
	private const string SystemPrompt = """
		You are an offline video-edit quality reviewer. Judge only the supplied edit plan,
		the materialized timeline snapshot, and the supplied preview evidence. Return exactly
		one JSON object matching the supplied schema. Do not return Markdown, prose outside
		the object, commands, source code, or file operations. Evidence IDs must be copied
		from the supplied preview descriptions. Accept only when no error-severity finding
		remains. Steering instructions must be concrete directions for the next edit plan.
		""";

	private const string ResponseSchema = """
		{
		  "type": "object",
		  "additionalProperties": false,
		  "required": ["accepted", "summary", "findings", "decisions", "steeringInstructions"],
		  "properties": {
		    "accepted": { "type": "boolean" },
		    "summary": { "type": "string", "minLength": 1 },
		    "findings": {
		      "type": "array",
		      "items": {
		        "type": "object", "additionalProperties": false,
		        "required": ["findingId", "code", "severity", "message", "evidenceIds"],
		        "properties": {
		          "findingId": { "type": "string", "minLength": 1 },
		          "code": { "type": "string", "minLength": 1 },
		          "severity": { "type": "string", "enum": ["info", "warning", "error"] },
		          "message": { "type": "string", "minLength": 1 },
		          "evidenceIds": { "type": "array", "items": { "type": "string" } }
		        }
		      }
		    },
		    "decisions": {
		      "type": "array",
		      "items": {
		        "type": "object", "additionalProperties": false,
		        "required": ["decisionId", "category", "summary", "confidence", "evidenceIds"],
		        "properties": {
		          "decisionId": { "type": "string", "minLength": 1 },
		          "category": { "type": "string", "minLength": 1 },
		          "summary": { "type": "string", "minLength": 1 },
		          "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
		          "evidenceIds": { "type": "array", "items": { "type": "string" } }
		        }
		      }
		    },
		    "steeringInstructions": {
		      "type": "array", "items": { "type": "string", "minLength": 1 }
		    }
		  }
		}
		""";

	private readonly ITextGenerationClient generationClient;
	private readonly string? sessionRoot;
	private readonly InferenceBudgets budgets;

	public LlmCandidateReviewer(
		ITextGenerationClient generationClient,
		string? sessionRoot = null,
		InferenceBudgets? budgets = null)
	{
		this.generationClient = generationClient ??
			throw new ArgumentNullException(nameof(generationClient));
		this.sessionRoot = sessionRoot == null ? null : Path.GetFullPath(sessionRoot);
		this.budgets = budgets ?? InferenceBudgets.FromEnvironment();
	}

	public Task<EditIterationFeedback> ReviewAsync(
		EditPlanningRequest request,
		EditPlanDocument candidate,
		CandidateTimelineSnapshot timeline,
		RenderCandidatePreviewResult preview,
		int iteration,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(preview);
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		if (!string.Equals(request.RequestId, candidate.RequestId, StringComparison.Ordinal))
			throw new InvalidOperationException("The candidate does not belong to the planning request.");
		VisualEvidence evidence = LoadPreviewEvidence(preview);
		return ReviewAsync(
			candidate,
			timeline,
			new[] { evidence },
			iteration,
			cancellationToken);
	}

	public async Task<EditIterationFeedback> ReviewAsync(
		EditPlanDocument candidate,
		CandidateTimelineSnapshot timeline,
		IReadOnlyList<VisualEvidence> previewEvidence,
		int iteration,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(candidate);
		ArgumentNullException.ThrowIfNull(timeline);
		ArgumentNullException.ThrowIfNull(previewEvidence);
		if (iteration < 1) throw new ArgumentOutOfRangeException(nameof(iteration));
		EditPlanDocumentValidator.ValidateAndNormalize(candidate);
		ValidateTimeline(timeline);
		ValidateEvidence(previewEvidence);

		string evidenceCatalog = string.Join(
			"\n",
			previewEvidence.Select(item => "- " + item.Description));
		TextGenerationResult generated = await generationClient.GenerateAsync(
			new TextGenerationRequest
			{
				SystemPrompt = SystemPrompt,
				UserPrompt =
					$"Review candidate iteration {iteration}.\n\nEdit plan:\n" +
					EditPlanDocumentSerializer.SerializePlan(candidate) +
					"\n\nMaterialized timeline snapshot:\n" +
					ContractSerializer.Serialize(timeline) +
					"\n\nAllowed preview evidence IDs:\n" + evidenceCatalog,
				VisualEvidence = previewEvidence,
				Temperature = 0,
				MaxOutputTokens = budgets.ReviewMaxOutputTokens,
				JsonSchemaName = "candidate_edit_review",
				JsonSchema = ResponseSchema
			},
			cancellationToken);

		ReviewResponse response;
		try
		{
			response = JsonConvert.DeserializeObject<ReviewResponse>(
				ExtractStrictJson(generated.Text),
				new JsonSerializerSettings
				{
					MissingMemberHandling = MissingMemberHandling.Error
				}) ?? throw new JsonSerializationException("Response was null.");
		}
		catch (JsonException exception)
		{
			throw new InvalidOperationException(
				"The model returned invalid candidate-review JSON.", exception);
		}

		return ValidateAndMap(response, previewEvidence);
	}

	private static EditIterationFeedback ValidateAndMap(
		ReviewResponse response,
		IReadOnlyList<VisualEvidence> evidence)
	{
		RequireText(response.Summary, "summary");
		HashSet<string> evidenceIds = evidence
			.Select(item => item.Description)
			.ToHashSet(StringComparer.Ordinal);
		HashSet<string> findingIds = new(StringComparer.Ordinal);
		HashSet<string> decisionIds = new(StringComparer.Ordinal);
		List<EditReviewFinding> findings = new();
		foreach (ReviewFinding item in response.Findings ??
			throw new InvalidOperationException("Review findings are required."))
		{
			RequireText(item.FindingId, "findingId");
			RequireText(item.Code, "code");
			RequireText(item.Message, "finding message");
			if (!findingIds.Add(item.FindingId))
				throw new InvalidOperationException("Review finding IDs must be unique.");
			EditReviewSeverity severity = item.Severity switch
			{
				"info" => EditReviewSeverity.Information,
				"warning" => EditReviewSeverity.Warning,
				"error" => EditReviewSeverity.Error,
				_ => throw new InvalidOperationException("Review finding severity is invalid.")
			};
			ValidateEvidenceIds(item.EvidenceIds, evidenceIds);
			findings.Add(new EditReviewFinding
			{
				FindingId = item.FindingId,
				Code = item.Code,
				Severity = severity,
				Message = item.Message,
				EvidenceIds = item.EvidenceIds!
			});
		}
		if (response.Accepted && findings.Any(item => item.Severity == EditReviewSeverity.Error))
			throw new InvalidOperationException("An accepted review cannot contain error findings.");

		List<EditDecisionRecord> decisions = new();
		foreach (ReviewDecision item in response.Decisions ??
			throw new InvalidOperationException("Review decisions are required."))
		{
			RequireText(item.DecisionId, "decisionId");
			RequireText(item.Category, "decision category");
			RequireText(item.Summary, "decision summary");
			if (!decisionIds.Add(item.DecisionId))
				throw new InvalidOperationException("Review decision IDs must be unique.");
			if (item.Confidence is < 0 or > 1)
				throw new InvalidOperationException("Decision confidence must be between zero and one.");
			ValidateEvidenceIds(item.EvidenceIds, evidenceIds);
			decisions.Add(new EditDecisionRecord
			{
				DecisionId = item.DecisionId,
				Category = item.Category,
				Summary = item.Summary,
				Confidence = item.Confidence,
				EvidenceIds = item.EvidenceIds!
			});
		}

		IReadOnlyList<string> steering = response.SteeringInstructions ??
			throw new InvalidOperationException("Steering instructions are required.");
		if (steering.Any(string.IsNullOrWhiteSpace))
			throw new InvalidOperationException("Steering instructions cannot be blank.");
		if (!response.Accepted && steering.Count == 0)
			throw new InvalidOperationException("A rejected review requires steering instructions.");

		return new EditIterationFeedback
		{
			IsAccepted = response.Accepted,
			Summary = response.Summary,
			VisualEvidence = evidence,
			Findings = findings,
			Decisions = decisions,
			SteeringInstructions = steering
		};
	}

	private static void ValidateTimeline(CandidateTimelineSnapshot timeline)
	{
		if (timeline.Workspace == null)
			throw new InvalidOperationException("The candidate timeline workspace is required.");
		if (timeline.TimelineEnd < timeline.TimelineStart)
			throw new InvalidOperationException("The candidate timeline range is invalid.");
		if (timeline.Tracks == null)
			throw new InvalidOperationException("Candidate timeline tracks are required.");
	}

	private VisualEvidence LoadPreviewEvidence(RenderCandidatePreviewResult preview)
	{
		if (sessionRoot == null)
			throw new InvalidOperationException(
				"A session root is required to load rendered preview evidence.");
		if (string.IsNullOrWhiteSpace(preview.OutputRelativePath))
			throw new InvalidOperationException("The rendered preview path is required.");
		string fullPath = Path.GetFullPath(Path.Combine(
			sessionRoot,
			preview.OutputRelativePath.Replace('/', Path.DirectorySeparatorChar)));
		string prefix = sessionRoot.EndsWith(Path.DirectorySeparatorChar)
			? sessionRoot
			: sessionRoot + Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("The rendered preview path escaped the session root.");
		if (!File.Exists(fullPath))
			throw new FileNotFoundException("The rendered candidate preview was not found.", fullPath);
		byte[] bytes = File.ReadAllBytes(fullPath);
		string actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
		if (string.IsNullOrWhiteSpace(preview.Sha256) ||
			!string.Equals(actualHash, preview.Sha256, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("The rendered candidate preview hash did not match.");
		string mediaType = Path.GetExtension(fullPath).ToLowerInvariant() switch
		{
			".png" => "image/png",
			".jpg" or ".jpeg" => "image/jpeg",
			".webp" => "image/webp",
			".mp4" => "video/mp4",
			_ => throw new InvalidOperationException("The rendered preview format is unsupported.")
		};
		return new VisualEvidence
		{
			DataUrl = "data:" + mediaType + ";base64," + Convert.ToBase64String(bytes),
			Description =
				$"preview:{preview.Sha256.ToLowerInvariant()} " +
				$"duration={preview.RenderedDuration.TotalSeconds:0.###}s " +
				$"profile={preview.RenderProfileId}"
		};
	}

	private static void ValidateEvidence(IReadOnlyList<VisualEvidence> evidence)
	{
		HashSet<string> ids = new(StringComparer.Ordinal);
		foreach (VisualEvidence item in evidence)
		{
			if (string.IsNullOrWhiteSpace(item.DataUrl))
				throw new InvalidOperationException("Preview evidence data is required.");
			RequireText(item.Description, "preview evidence description");
			if (!ids.Add(item.Description))
				throw new InvalidOperationException("Preview evidence descriptions must be unique.");
		}
	}

	private static void ValidateEvidenceIds(
		IReadOnlyList<string>? referenced,
		HashSet<string> allowed)
	{
		if (referenced == null)
			throw new InvalidOperationException("Evidence IDs are required.");
		foreach (string id in referenced)
			if (!allowed.Contains(id))
				throw new InvalidOperationException(
					"The review referenced preview evidence that was not supplied.");
	}

	private static void RequireText(string? value, string field)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new InvalidOperationException("Review " + field + " is required.");
	}

	private static string ExtractStrictJson(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			throw new InvalidOperationException("The model returned an empty review.");
		string trimmed = text.Trim();
		if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
			throw new InvalidOperationException("The model review must contain only one JSON object.");
		return trimmed;
	}

	private sealed class ReviewResponse
	{
		[JsonProperty("accepted", Required = Required.Always)]
		public bool Accepted { get; set; }
		[JsonProperty("summary", Required = Required.Always)]
		public string Summary { get; set; } = "";
		[JsonProperty("findings", Required = Required.Always)]
		public List<ReviewFinding>? Findings { get; set; }
		[JsonProperty("decisions", Required = Required.Always)]
		public List<ReviewDecision>? Decisions { get; set; }
		[JsonProperty("steeringInstructions", Required = Required.Always)]
		public List<string>? SteeringInstructions { get; set; }
	}

	private sealed class ReviewFinding
	{
		[JsonProperty("findingId", Required = Required.Always)] public string FindingId { get; set; } = "";
		[JsonProperty("code", Required = Required.Always)] public string Code { get; set; } = "";
		[JsonProperty("severity", Required = Required.Always)] public string Severity { get; set; } = "";
		[JsonProperty("message", Required = Required.Always)] public string Message { get; set; } = "";
		[JsonProperty("evidenceIds", Required = Required.Always)] public List<string>? EvidenceIds { get; set; }
	}

	private sealed class ReviewDecision
	{
		[JsonProperty("decisionId", Required = Required.Always)] public string DecisionId { get; set; } = "";
		[JsonProperty("category", Required = Required.Always)] public string Category { get; set; } = "";
		[JsonProperty("summary", Required = Required.Always)] public string Summary { get; set; } = "";
		[JsonProperty("confidence", Required = Required.Always)] public double Confidence { get; set; }
		[JsonProperty("evidenceIds", Required = Required.Always)] public List<string>? EvidenceIds { get; set; }
	}
}
