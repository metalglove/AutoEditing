using System.Security.Cryptography;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Inference;
using Newtonsoft.Json;

namespace AutoEditing.LlmEditor.RoughCut;

internal sealed class LlmRoughCutAuditContribution
{
	public string ModelId { get; init; } = "";
	public string Summary { get; init; } = "";
	public IReadOnlyList<RoughCutAuditFinding> Findings { get; init; } =
		Array.Empty<RoughCutAuditFinding>();
	public IReadOnlyList<RoughCutCorrectionProposal> Corrections { get; init; } =
		Array.Empty<RoughCutCorrectionProposal>();
}

internal interface IRoughCutMultimodalAuditor
{
	Task<LlmRoughCutAuditContribution> AuditAsync(
		string sessionRoot,
		RoughCutAuditInput input,
		RoughCutAuditReport deterministicReport,
		CancellationToken cancellationToken);
}

internal sealed class LlmRoughCutAuditor : IRoughCutMultimodalAuditor
{
	private const string SystemPrompt = """
		You are auditing a completed synchronization-only montage rough cut.
		Return exactly one JSON object matching the supplied strict schema. Do not
		return Markdown, private reasoning, timeline commands, effects, audio
		treatments, or a replacement montage. Evaluate only pacing, continuity,
		repetition, accidental gaps/dead space, major reviewed musical-event
		coverage, and whether sketch reservations fulfilled their intended role.
		The supplied PNGs are VEGAS snapshots captured from the same isolated
		candidate workspace as the full-render chunks; they are authoritative only
		for their listed sample instants. The full-render manifest is authoritative
		for render coverage, and deterministic metrics are authoritative timing
		evidence. Every model finding must cite at least one
		supplied evidenceId and every correction must target explicit existing
		checkpoint numbers. A correction must use exactly one supported operation:
		move, trim, duration, or constant_speed. Never propose substitution,
		reordering, deletion, duplication, variable velocity, effects, or audio
		changes as corrections. Unsupported creative ideas may appear only as
		advisory finding detail, without a correction. Corrections are proposals for
		human approval; they never reopen or mutate a checkpoint themselves.
		""";

	private readonly ITextGenerationClient generationClient;
	private readonly InferenceBudgets budgets;

	public LlmRoughCutAuditor(
		ITextGenerationClient generationClient,
		InferenceBudgets? budgets = null)
	{
		this.generationClient = generationClient ??
			throw new ArgumentNullException(nameof(generationClient));
		this.budgets = budgets ?? InferenceBudgets.FromEnvironment();
	}

	public async Task<LlmRoughCutAuditContribution> AuditAsync(
		string sessionRoot,
		RoughCutAuditInput input,
		RoughCutAuditReport deterministicReport,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionRoot);
		ArgumentNullException.ThrowIfNull(input);
		RoughCutAuditContractValidator.Validate(deterministicReport);
		string fullRoot = Path.GetFullPath(sessionRoot);
		List<VisualEvidence> visual = LoadVisualEvidence(
			fullRoot, deterministicReport.Evidence);
		if (visual.Count == 0)
			throw new InvalidOperationException(
				"A multimodal rough-cut audit requires at least one persisted VEGAS " +
				"snapshot from the full rough-cut candidate workspace.");

		object compactPlan = new
		{
			deterministicReport.SessionId,
			deterministicReport.ReportId,
			deterministicReport.RequestId,
			deterministicReport.PlanSha256,
			sketchThesis = input.Sketch.EditorialThesis,
			placements = input.AcceptedSyncPlan.Montage.Placements
				.OrderBy(item => item.TimelineStartSeconds)
				.Select((item, index) => new
				{
					checkpoint = index + 1,
					clipReferenceId = AssemblyReferenceIds.ForClipPath(item.Clip.FilePath),
					item.Clip.Map,
					item.Clip.Gun,
					item.Clip.ClipType,
					item.TimelineStartSeconds,
					item.TimelineEndSeconds,
					item.SourceOffsetSeconds,
					item.LengthSeconds,
					killTimesSeconds = item.TimelineKillTimesSeconds
				}),
			metrics = deterministicReport.Metrics,
			deterministicFindings = deterministicReport.Findings,
			evidence = deterministicReport.Evidence.Select(item => new
			{
				item.EvidenceId,
				kind = item.Kind.ToString(),
				item.Description
			})
		};
		TextGenerationResult result = await generationClient.GenerateAsync(
			new TextGenerationRequest
			{
				SystemPrompt = SystemPrompt,
				UserPrompt =
					"Audit the complete rendered synchronization pass. Confirm or challenge " +
					"the deterministic signals using the supplied full-render manifest and " +
					"timestamped VEGAS snapshot evidence. Return " +
					"only independently targetable correction proposals. If a surfaced signal " +
					"is an intentional creative choice, omit a correction for it.\n\n" +
					ContractSerializer.Serialize(compactPlan),
				VisualEvidence = visual,
				Temperature = 0,
				MaxOutputTokens = budgets.ReviewMaxOutputTokens,
				JsonSchemaName = "rough_cut_audit",
				JsonSchema = RoughCutAuditSchema.Json
			},
			cancellationToken);
		ModelAuditResponse response;
		try
		{
			response = JsonConvert.DeserializeObject<ModelAuditResponse>(
				ExtractJson(result.Text),
				new JsonSerializerSettings
				{
					MissingMemberHandling = MissingMemberHandling.Error
				}) ?? throw new JsonException("The rough-cut audit response was null.");
		}
		catch (JsonException exception)
		{
			throw new InvalidOperationException(
				"The model returned invalid rough-cut audit JSON.", exception);
		}
		return ValidateAndCompile(
			response,
			result.Model,
			deterministicReport);
	}

	private static LlmRoughCutAuditContribution ValidateAndCompile(
		ModelAuditResponse response,
		string modelId,
		RoughCutAuditReport deterministicReport)
	{
		if (response.SchemaVersion != 1 ||
			!string.Equals(response.SessionId, deterministicReport.SessionId,
				StringComparison.Ordinal))
			throw new InvalidOperationException(
				"The model changed the rough-cut audit identity.");
		RequireText(response.Summary, "audit summary");
		HashSet<string> evidence = deterministicReport.Evidence
			.Select(item => item.EvidenceId)
			.ToHashSet(StringComparer.Ordinal);
		HashSet<string> existingFindings = deterministicReport.Findings
			.Select(item => item.FindingId)
			.ToHashSet(StringComparer.Ordinal);
		HashSet<string> modelFindingIds = new(StringComparer.Ordinal);
		List<RoughCutAuditFinding> findings = new();
		foreach (ModelFinding source in response.Findings ??
			throw new InvalidOperationException("Model findings are required."))
		{
			RequireText(source.FindingId, "finding ID");
			if (!modelFindingIds.Add(source.FindingId) ||
				existingFindings.Contains(source.FindingId))
				throw new InvalidOperationException(
					"The model returned a duplicate rough-cut finding ID.");
			if (source.EvidenceIds == null || source.EvidenceIds.Count == 0 ||
				source.EvidenceIds.Any(id => !evidence.Contains(id)))
				throw new InvalidOperationException(
					"The model finding did not cite supplied audit evidence.");
			int[] checkpoints = ValidateCheckpoints(
				source.AffectedCheckpoints,
				deterministicReport.Metrics.PlacementCount,
				requireAny: false);
			if (source.StartSeconds.HasValue != source.EndSeconds.HasValue ||
				(source.StartSeconds.HasValue &&
					(source.StartSeconds.Value < 0 ||
						source.EndSeconds!.Value <= source.StartSeconds.Value)))
				throw new InvalidOperationException("The model finding time range is invalid.");
			findings.Add(new RoughCutAuditFinding
			{
				FindingId = source.FindingId,
				Category = ParseCategory(source.Category),
				Severity = ParseSeverity(source.Severity),
				Source = RoughCutFindingSource.MultimodalModel,
				Summary = RequiredText(source.Summary, "finding summary"),
				Details = RequiredText(source.Details, "finding details"),
				StartSeconds = source.StartSeconds,
				EndSeconds = source.EndSeconds,
				AffectedCheckpoints = checkpoints,
				EvidenceIds = source.EvidenceIds,
				Confidence = Confidence(source.Confidence, "finding confidence")
			});
		}

		HashSet<string> allFindings = existingFindings
			.Concat(modelFindingIds)
			.ToHashSet(StringComparer.Ordinal);
		HashSet<string> existingCorrections = deterministicReport.Corrections
			.Select(item => item.CorrectionId)
			.ToHashSet(StringComparer.Ordinal);
		HashSet<string> modelCorrectionIds = new(StringComparer.Ordinal);
		List<RoughCutCorrectionProposal> corrections = new();
		foreach (ModelCorrection source in response.Corrections ??
			throw new InvalidOperationException("Model corrections are required."))
		{
			RequireText(source.CorrectionId, "correction ID");
			if (!modelCorrectionIds.Add(source.CorrectionId) ||
				existingCorrections.Contains(source.CorrectionId))
				throw new InvalidOperationException(
					"The model returned a duplicate rough-cut correction ID.");
			if (source.FindingIds == null || source.FindingIds.Count == 0 ||
				source.FindingIds.Any(id => !allFindings.Contains(id)))
				throw new InvalidOperationException(
					"The model correction references an unknown finding.");
			corrections.Add(new RoughCutCorrectionProposal
			{
				CorrectionId = source.CorrectionId,
				FindingIds = source.FindingIds,
				TargetCheckpoints = ValidateCheckpoints(
					source.TargetCheckpoints,
					deterministicReport.Metrics.PlacementCount,
					requireAny: true),
				Operation = ParseOperation(source.OperationType),
				Instruction = RequiredText(source.Instruction, "correction instruction"),
				ExpectedOutcome = RequiredText(
					source.ExpectedOutcome, "correction expected outcome"),
				Risk = RequiredText(source.Risk, "correction risk"),
				Confidence = Confidence(source.Confidence, "correction confidence")
			});
		}
		return new LlmRoughCutAuditContribution
		{
			ModelId = modelId ?? "",
			Summary = response.Summary,
			Findings = findings,
			Corrections = corrections
		};
	}

	private static List<VisualEvidence> LoadVisualEvidence(
		string sessionRoot,
		IEnumerable<RoughCutEvidenceReference> evidence)
	{
		List<VisualEvidence> result = new();
		foreach (RoughCutEvidenceReference item in evidence.Where(item =>
			item.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)))
		{
			string path = Path.GetFullPath(Path.Combine(sessionRoot, item.RelativePath));
			string separatorRoot = sessionRoot.EndsWith(Path.DirectorySeparatorChar)
				? sessionRoot
				: sessionRoot + Path.DirectorySeparatorChar;
			if (!path.StartsWith(separatorRoot, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"A rough-cut evidence path escaped the session directory.");
			if (!File.Exists(path))
				throw new FileNotFoundException("Rough-cut image evidence is missing.", path);
			byte[] bytes = File.ReadAllBytes(path);
			string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
			if (!string.Equals(hash, item.Sha256, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"Rough-cut image evidence failed its SHA-256 check.");
			result.Add(new VisualEvidence
			{
				DataUrl = "data:" + item.MediaType + ";base64," +
					Convert.ToBase64String(bytes),
				Description = "evidenceId=" + item.EvidenceId + " " + item.Description
			});
		}
		return result;
	}

	private static int[] ValidateCheckpoints(
		List<int>? source,
		int count,
		bool requireAny)
	{
		if (source == null || (requireAny && source.Count == 0) ||
			source.Any(value => value < 1 || value > count) ||
			source.Distinct().Count() != source.Count)
			throw new InvalidOperationException(
				"The model returned invalid target checkpoints.");
		return source.ToArray();
	}

	private static RoughCutAuditCategory ParseCategory(string value) => value switch
	{
		"pacing" => RoughCutAuditCategory.Pacing,
		"continuity" => RoughCutAuditCategory.Continuity,
		"repetition" => RoughCutAuditCategory.Repetition,
		"gap" => RoughCutAuditCategory.Gap,
		"music_event_coverage" => RoughCutAuditCategory.MusicEventCoverage,
		"reservation_fulfillment" => RoughCutAuditCategory.ReservationFulfillment,
		_ => throw new InvalidOperationException("The model finding category is invalid.")
	};

	private static RoughCutFindingSeverity ParseSeverity(string value) => value switch
	{
		"information" => RoughCutFindingSeverity.Information,
		"warning" => RoughCutFindingSeverity.Warning,
		"error" => RoughCutFindingSeverity.Error,
		_ => throw new InvalidOperationException("The model finding severity is invalid.")
	};

	private static RoughCutCorrectionOperation ParseOperation(string value) => value switch
	{
		"move" => RoughCutCorrectionOperation.Move,
		"trim" => RoughCutCorrectionOperation.Trim,
		"duration" => RoughCutCorrectionOperation.Duration,
		"constant_speed" => RoughCutCorrectionOperation.ConstantSpeed,
		_ => throw new InvalidOperationException(
			"The model correction operation is unsupported.")
	};

	private static double Confidence(double value, string field)
	{
		if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 1)
			throw new InvalidOperationException("The model " + field + " is invalid.");
		return value;
	}

	private static string RequiredText(string value, string field)
	{
		RequireText(value, field);
		return value;
	}

	private static void RequireText(string value, string field)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new InvalidOperationException("The model " + field + " is required.");
	}

	private static string ExtractJson(string response)
	{
		if (string.IsNullOrWhiteSpace(response))
			throw new JsonException("The model returned an empty rough-cut audit.");
		string text = response.Trim();
		if (text.StartsWith("```", StringComparison.Ordinal))
		{
			int body = text.IndexOf('\n');
			int closing = text.LastIndexOf("```", StringComparison.Ordinal);
			if (body < 0 || closing <= body)
				throw new JsonException("The fenced rough-cut audit was malformed.");
			text = text[(body + 1)..closing].Trim();
		}
		if (!text.StartsWith('{') || !text.EndsWith('}'))
			throw new JsonException(
				"The rough-cut audit must contain exactly one JSON object.");
		return text;
	}

	private sealed class ModelAuditResponse
	{
		[JsonProperty("schemaVersion", Required = Required.Always)]
		public int SchemaVersion { get; set; }
		[JsonProperty("sessionId", Required = Required.Always)]
		public string SessionId { get; set; } = "";
		[JsonProperty("summary", Required = Required.Always)]
		public string Summary { get; set; } = "";
		[JsonProperty("findings", Required = Required.Always)]
		public List<ModelFinding>? Findings { get; set; }
		[JsonProperty("corrections", Required = Required.Always)]
		public List<ModelCorrection>? Corrections { get; set; }
	}

	private sealed class ModelFinding
	{
		[JsonProperty("findingId", Required = Required.Always)]
		public string FindingId { get; set; } = "";
		[JsonProperty("category", Required = Required.Always)]
		public string Category { get; set; } = "";
		[JsonProperty("severity", Required = Required.Always)]
		public string Severity { get; set; } = "";
		[JsonProperty("summary", Required = Required.Always)]
		public string Summary { get; set; } = "";
		[JsonProperty("details", Required = Required.Always)]
		public string Details { get; set; } = "";
		[JsonProperty("startSeconds", Required = Required.Always)]
		public double? StartSeconds { get; set; }
		[JsonProperty("endSeconds", Required = Required.Always)]
		public double? EndSeconds { get; set; }
		[JsonProperty("affectedCheckpoints", Required = Required.Always)]
		public List<int>? AffectedCheckpoints { get; set; }
		[JsonProperty("evidenceIds", Required = Required.Always)]
		public List<string>? EvidenceIds { get; set; }
		[JsonProperty("confidence", Required = Required.Always)]
		public double Confidence { get; set; }
	}

	private sealed class ModelCorrection
	{
		[JsonProperty("correctionId", Required = Required.Always)]
		public string CorrectionId { get; set; } = "";
		[JsonProperty("findingIds", Required = Required.Always)]
		public List<string>? FindingIds { get; set; }
		[JsonProperty("targetCheckpoints", Required = Required.Always)]
		public List<int>? TargetCheckpoints { get; set; }
		[JsonProperty("operationType", Required = Required.Always)]
		public string OperationType { get; set; } = "";
		[JsonProperty("instruction", Required = Required.Always)]
		public string Instruction { get; set; } = "";
		[JsonProperty("expectedOutcome", Required = Required.Always)]
		public string ExpectedOutcome { get; set; } = "";
		[JsonProperty("risk", Required = Required.Always)]
		public string Risk { get; set; } = "";
		[JsonProperty("confidence", Required = Required.Always)]
		public double Confidence { get; set; }
	}
}
