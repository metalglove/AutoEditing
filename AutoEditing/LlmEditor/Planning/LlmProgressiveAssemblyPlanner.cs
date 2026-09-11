using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Inference;
using Core.Domain.Editing;
using Core.Domain.Planning;
using Newtonsoft.Json;

namespace AutoEditing.LlmEditor.Planning;

internal sealed class LlmProgressiveAssemblyPlanner : IProgressiveAssemblyPlanner
{
	private const int MaximumSketchAttempts = 3;

	private const string SystemPrompt = """
		You are the progressive synchronization planner for a professional video editor.
		Return exactly one JSON object matching the supplied strict schema. Never return
		Markdown, prose outside JSON, commands, or private reasoning. Deterministic editing
		rules and the reviewed song map are authoritative. Style findings are advisory.
		Use clip referenceId/mediaPath pairs and music event IDs exactly as supplied. Never
		invent media, events, effects, or renderer capabilities.
		""";

	private readonly ITextGenerationClient generationClient;
	private readonly InferenceBudgets budgets;
	private readonly string styleFindings;

	public LlmProgressiveAssemblyPlanner(
		ITextGenerationClient generationClient,
		InferenceBudgets? budgets = null,
		string? styleFindings = null)
	{
		this.generationClient = generationClient ??
			throw new ArgumentNullException(nameof(generationClient));
		this.budgets = budgets ?? InferenceBudgets.FromEnvironment();
		this.styleFindings = styleFindings ?? EditorStyleFindings.LoadDefault();
	}

	public async Task<AssemblySketch> CreateSketchAsync(
		EditPlanningRequest request,
		CancellationToken cancellationToken)
	{
		ValidateRequest(request);
		string basePrompt =
			"Create a loose global assembly sketch before making any clip-level timing decision. " +
			"Give every selected clip a tentative order, preserve strong material with explicit " +
			"reservations, record uncertainties and alternatives, and map sections to the reviewed " +
			"song arc. Later clip decisions may deliberately deviate from this sketch. " +
			"First create sections[]. Every clipOrder[].sectionId MUST exactly equal one " +
			"sections[].sectionId from your same response; it is never a song regionId and " +
			"you must not invent an undeclared section. Copy every clip referenceId/mediaPath " +
			"pair exactly from the authoritative input without modifying either value. " +
			"There are exactly " + request.Clips.Count +
			" selected clips, so clipOrder MUST contain exactly " + request.Clips.Count +
			" entries and include every supplied clip exactly once.\n\n" +
			"VERSIONED STYLE FINDINGS (ADVISORY)\n" + styleFindings +
			"\n\nAUTHORITATIVE PLANNING INPUT\n" +
			JsonConvert.SerializeObject(CreateSketchInput(request), Formatting.Indented);
		string prompt = basePrompt;
		for (int attempt = 1; attempt <= MaximumSketchAttempts; attempt++)
		{
			AssemblySketch? sketch = null;
			try
			{
				sketch = await GenerateAsync<AssemblySketch>(
					prompt,
					"progressive_assembly_sketch",
					ProgressiveAssemblySchemas.Sketch,
					cancellationToken);
				CanonicalizeUnambiguousClipReferenceAliases(request, sketch);
				ValidateSketchAgainstRequest(request, sketch);
				return sketch;
			}
			catch (Exception exception) when (
				IsRepairableSketchOutputFailure(exception))
			{
				if (attempt == MaximumSketchAttempts)
					throw new InvalidOperationException(
						"AI assembly sketch remained invalid after " +
						MaximumSketchAttempts +
						" attempts. Last deterministic diagnostic: " +
						exception.Message,
						exception);
				prompt = basePrompt +
					(sketch == null
						? ""
						: "\n\nPREVIOUS REJECTED SKETCH\n" +
							ContractSerializer.Serialize(sketch)) +
					"\n\nAUTOMATIC SKETCH REPAIR (FINAL AUTHORITATIVE INSTRUCTION), attempt " +
					(attempt + 1) +
					" of " + MaximumSketchAttempts + ". The previous response was " +
					"rejected before any VEGAS timeline mutation. Deterministic diagnostic: " +
					exception.Message +
					"\nReturn a complete corrected sketch, not a patch. In particular, " +
					"clipOrder[].sectionId must reference sections[].sectionId, while " +
					"sections[].regionId references the reviewed song region. Preserve every " +
					"authoritative clip referenceId/mediaPath pair exactly. This correction " +
					"and its diagnostic appear after the rejected sketch deliberately and " +
					"override every conflicting value in that rejected sketch.";
			}
		}
		throw new InvalidOperationException(
			"Automatic assembly-sketch repair ended unexpectedly.");
	}

	private static bool IsRepairableSketchOutputFailure(Exception exception) =>
		exception is InvalidDataException ||
		exception is ArgumentException ||
		(exception is InvalidOperationException &&
			exception.InnerException is JsonException);

	public Task<ClipStepDecision> PlanClipAsync(
		EditPlanningRequest request,
		ProgressiveAssemblyPlanningContext context,
		CancellationToken cancellationToken) =>
		GenerateStepAsync(request, context, null, cancellationToken);

	public Task<ClipStepDecision> ReviseClipAsync(
		EditPlanningRequest request,
		ProgressiveAssemblyPlanningContext context,
		ClipStepDecision previousDecision,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(previousDecision);
		ProgressiveAssemblyContractValidator.Validate(previousDecision);
		if (context.TimelineAdjustment == null && string.IsNullOrWhiteSpace(context.ScopedInstruction))
			throw new InvalidOperationException(
				"A clip revision requires a timeline adjustment or scoped instruction.");
		return GenerateStepAsync(request, context, previousDecision, cancellationToken);
	}

	private async Task<ClipStepDecision> GenerateStepAsync(
		EditPlanningRequest request,
		ProgressiveAssemblyPlanningContext context,
		ClipStepDecision? previousDecision,
		CancellationToken cancellationToken)
	{
		ValidateRequest(request);
		ProgressiveAssemblyContractValidator.Validate(context);
		ValidateContextAgainstRequest(request, context);
		string task = previousDecision == null
			? "Plan exactly one clip from the remaining set. The sketch order is tentative; " +
				"a deliberate deviation is allowed when its rationale explains why. " +
				"Do not re-author the accepted prefix."
			: "Revise exactly the current single clip. Preserve the accepted prefix and respond " +
				"only to the authoritative timeline delta and scoped instruction.";
		string prompt =
			task +
			"\nChoose syncs only from nearbySongContext. killIndex is zero-based among the " +
			"selected clip's confirmed kills ordered by source confirmation time. " +
			"sourceWindow uses source-media seconds, never song or timeline seconds. " +
			"Choose 0 <= startSeconds < endSeconds <= durationSeconds. Every selected " +
			"kill's sourceConfirmationTimeSeconds must lie inside that sourceWindow. " +
			"Use only the supplied timing values; do not estimate or invent source times. " +
			"Retain readable setup before and recovery after the synchronized action when " +
			"the supplied media handles allow it. Do not select the whole source clip by " +
			"default. For a single synchronized kill, normally choose a compact 2-4 second " +
			"action window with roughly 0.75-2 seconds of setup and 0.5-1.25 seconds of " +
			"recovery. A window longer than 6 seconds must be justified by specific " +
			"multi-kill continuity, opener/closer storytelling, or song-section evidence. " +
			"constantSpeed is the default synchronization velocity: use 1.0 when no retiming " +
			"is editorially justified, otherwise use a supported constant rate to fit selected " +
			"source action to the supplied music-event spacing. For a synchronized impact, " +
			"prefer velocityCurve instead: a fast/slow/fast retiming shape with an approximately " +
			"0.5x plateau around the synced action (treat 0.5x as a candidate neighborhood, not " +
			"a fixed value) and above-normal entry and exit speeds recovering toward the window's " +
			"edges. Use a four-point single dip or a seven-point double dip; do not invent another " +
			"topology or a non-monotonic shape. Every velocityCurve's first point is offsetSeconds " +
			"0 and its last point is offsetSeconds (endSeconds - startSeconds) exactly; interior " +
			"points fall strictly between. Set velocityCurve to null and rely on constantSpeed " +
			"when no retiming is justified, or when the shape does not fit the selected action.\n\n" +
			"VERSIONED STYLE FINDINGS (ADVISORY)\n" + styleFindings +
			"\n\nGLOBAL SKETCH\n" + ContractSerializer.Serialize(context.Sketch) +
			"\n\nACCEPTED PREFIX SUMMARY (AUTHORITATIVE, IMMUTABLE)\n" +
			SummarizePrefix(context.AcceptedPrefix) +
			"\n\nAUTHORITATIVE REMAINING CLIP TIMING\n" +
			JsonConvert.SerializeObject(
				CreateRemainingClipTimingInput(request, context.RemainingClips),
				Formatting.Indented) +
			"\n\nNEARBY REVIEWED SONG CONTEXT\n" +
			ContractSerializer.Serialize(context.NearbySongContext) +
			"\n\nTIMELINE ADJUSTMENT DELTA\n" +
			(context.TimelineAdjustment == null
				? "none"
				: ContractSerializer.Serialize(context.TimelineAdjustment)) +
			"\n\nSCOPED INSTRUCTION\n" +
			(string.IsNullOrWhiteSpace(context.ScopedInstruction)
				? "none"
				: context.ScopedInstruction) +
			(previousDecision == null ? "" :
				"\n\nPREVIOUS CURRENT-CLIP DECISION\n" +
				ContractSerializer.Serialize(previousDecision));
		ClipStepDecision decision = await GenerateAsync<ClipStepDecision>(
			prompt,
			previousDecision == null ? "progressive_clip_step" : "progressive_clip_revision",
			ProgressiveAssemblySchemas.ClipStep,
			cancellationToken);
		ValidateStepAgainstContext(request, context, decision);
		return decision;
	}

	private async Task<T> GenerateAsync<T>(
		string prompt,
		string schemaName,
		string schema,
		CancellationToken cancellationToken)
	{
		TextGenerationResult result = await generationClient.GenerateAsync(
			new TextGenerationRequest
			{
				SystemPrompt = SystemPrompt,
				UserPrompt = prompt,
				Temperature = 0,
				MaxOutputTokens = budgets.PlanningMaxOutputTokens,
				JsonSchemaName = schemaName,
				JsonSchema = schema
			},
			cancellationToken);
		try
		{
			string text = ExtractJsonObject(result.Text);
			return JsonConvert.DeserializeObject<T>(text, new JsonSerializerSettings
			{
				MissingMemberHandling = MissingMemberHandling.Error
			}) ?? throw new JsonException("Response was null.");
		}
		catch (JsonException exception)
		{
			throw new InvalidOperationException(
				"The model returned invalid progressive-assembly JSON.", exception);
		}
	}

	private static object CreateSketchInput(EditPlanningRequest request) => new
	{
		request.RequestId,
		request.CreativeBrief,
		request.StyleProfileIds,
		clips = request.Clips.Select(clip => new
		{
			referenceId = AssemblyReferenceIds.ForClipPath(clip.FilePath),
			mediaPath = clip.FilePath,
			clip.DurationSeconds,
			clip.IsOpener,
			clip.IsCloser,
			clip.PlayerName,
			clip.Game,
			clip.Map,
			clip.Gun,
			clip.ClipType,
			confirmedKills = clip.ConfirmedKills
				.OrderBy(kill => kill.SourceConfirmationTimeSeconds)
				.Select((kill, index) => new
				{
					killIndex = index,
					kill.SourceConfirmationTimeSeconds,
					kill.Gun
				})
		}),
		song = new
		{
			request.SongAnalysis.SongDurationSeconds,
			request.SongAnalysis.Regions,
			request.SongAnalysis.Events,
			request.SongAnalysis.EventTimelineColumns,
			request.SongAnalysis.EventTimeline
		}
	};

	private static object CreateRemainingClipTimingInput(
		EditPlanningRequest request,
		IList<AssemblyClipReference> remainingClips)
	{
		Dictionary<string, Core.Domain.Clip.Clip> clips = request.Clips
			.ToDictionary(
				clip => AssemblyReferenceIds.ForClipPath(clip.FilePath),
				StringComparer.Ordinal);
		return remainingClips.Select(reference =>
		{
			if (!clips.TryGetValue(reference.ReferenceId, out Core.Domain.Clip.Clip? clip))
				throw new InvalidOperationException(
					"The assembly context contains an unknown clip.");
			return new
			{
				referenceId = reference.ReferenceId,
				mediaPath = reference.MediaPath,
				durationSeconds = clip.DurationSeconds,
				confirmedKills = clip.ConfirmedKills
					.OrderBy(kill => kill.SourceConfirmationTimeSeconds)
					.Select((kill, index) => new
					{
						killIndex = index,
						sourceMuzzleTimeSeconds = kill.SourceMuzzleTimeSeconds,
						sourceConfirmationTimeSeconds =
							kill.SourceConfirmationTimeSeconds,
						outcome = kill.Outcome.ToString(),
						gun = kill.Gun
					})
			};
		}).ToArray();
	}

	private static string SummarizePrefix(IList<AcceptedClipPlacementSummary> prefix)
	{
		if (prefix.Count == 0) return "none";
		return ContractSerializer.Serialize(prefix.Select(step => new
		{
			step.StepIndex,
			clipReferenceId = step.Clip.ReferenceId,
			step.Clip.MediaPath,
			step.TimelineStartSeconds,
			step.TimelineEndSeconds,
			step.SourceStartSeconds,
			step.SourceEndSeconds,
			step.ConstantSpeed,
			step.VelocityCurve,
			survivingSyncs = step.SurvivingSyncs,
			step.WasHumanAdjusted,
			step.AdjustmentRationale
		}));
	}

	private static string ExtractJsonObject(string? response)
	{
		if (string.IsNullOrWhiteSpace(response))
			throw new JsonException("Response was empty.");
		string text = response.Trim();
		int openingFence = text.IndexOf("```", StringComparison.Ordinal);
		if (openingFence >= 0)
		{
			int firstLine = text.IndexOf('\n', openingFence);
			int closingFence = text.IndexOf("```", firstLine + 1, StringComparison.Ordinal);
			if (firstLine < 0 || closingFence <= firstLine)
				throw new JsonException("The fenced JSON response was malformed.");
			text = text[(firstLine + 1)..closingFence].Trim();
		}
		int firstObject = text.IndexOf('{');
		int lastObject = text.LastIndexOf('}');
		if (firstObject < 0 || lastObject < firstObject)
			throw new JsonException("Response did not contain a JSON object.");
		return text[firstObject..(lastObject + 1)];
	}

	private static void ValidateRequest(EditPlanningRequest request)
	{
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		if (request.SongAnalysis == null ||
			request.SongAnalysis.Mode != MontageSongPlanningMode.ReviewedSongMap)
			throw new InvalidOperationException(
				"Progressive assembly requires the committed reviewed song map.");
	}

	private static void CanonicalizeUnambiguousClipReferenceAliases(
		EditPlanningRequest request,
		AssemblySketch sketch)
	{
		ArgumentNullException.ThrowIfNull(sketch);
		Dictionary<string, string> canonicalByPath = request.Clips
			.ToDictionary(
				clip => clip.FilePath,
				clip => AssemblyReferenceIds.ForClipPath(clip.FilePath),
				StringComparer.OrdinalIgnoreCase);
		HashSet<string> canonicalReferences =
			canonicalByPath.Values.ToHashSet(StringComparer.Ordinal);
		Dictionary<string, HashSet<string>> candidates =
			new(StringComparer.Ordinal);
		foreach (AssemblyClipIntent intent in sketch.ClipOrder ??
			Array.Empty<AssemblyClipIntent>())
		{
			if (intent?.Clip == null ||
				string.IsNullOrWhiteSpace(intent.Clip.ReferenceId) ||
				!canonicalByPath.TryGetValue(
					intent.Clip.MediaPath,
					out string? canonicalReference) ||
				string.Equals(
					intent.Clip.ReferenceId,
					canonicalReference,
					StringComparison.Ordinal) ||
				canonicalReferences.Contains(intent.Clip.ReferenceId))
				continue;
			if (!candidates.TryGetValue(
				intent.Clip.ReferenceId,
				out HashSet<string>? aliasTargets))
			{
				aliasTargets = new HashSet<string>(StringComparer.Ordinal);
				candidates.Add(intent.Clip.ReferenceId, aliasTargets);
			}
			aliasTargets.Add(canonicalReference);
		}
		Dictionary<string, string> aliases = candidates
			.Where(item => item.Value.Count == 1)
			.ToDictionary(
				item => item.Key,
				item => item.Value.Single(),
				StringComparer.Ordinal);
		if (aliases.Count == 0) return;

		foreach (AssemblyClipIntent intent in sketch.ClipOrder ??
			Array.Empty<AssemblyClipIntent>())
		{
			if (intent?.Clip != null &&
				aliases.TryGetValue(
					intent.Clip.ReferenceId,
					out string? canonicalReference) &&
				canonicalByPath.TryGetValue(
					intent.Clip.MediaPath,
					out string? pathReference) &&
				string.Equals(
					canonicalReference,
					pathReference,
					StringComparison.Ordinal))
				intent.Clip.ReferenceId = canonicalReference;
			RewriteAliases(intent?.AlternativeClipReferenceIds, aliases);
		}
		foreach (AssemblyReservation reservation in sketch.Reservations ??
			Array.Empty<AssemblyReservation>())
			RewriteAliases(reservation?.PreferredClipReferenceIds, aliases);
	}

	private static void RewriteAliases(
		IList<string>? references,
		IReadOnlyDictionary<string, string> aliases)
	{
		if (references == null) return;
		for (int index = 0; index < references.Count; index++)
			if (aliases.TryGetValue(references[index], out string? canonicalReference))
				references[index] = canonicalReference;
	}

	private static void ValidateSketchAgainstRequest(
		EditPlanningRequest request,
		AssemblySketch sketch)
	{
		ArgumentNullException.ThrowIfNull(sketch);
		List<string> diagnostics = new();
		if (!string.Equals(sketch.RequestId, request.RequestId, StringComparison.Ordinal))
			diagnostics.Add(
				"requestId must be '" + request.RequestId + "', but the sketch returned '" +
				(sketch.RequestId ?? "") + "'.");
		Dictionary<string, string> expected = request.Clips
			.ToDictionary(
				clip => AssemblyReferenceIds.ForClipPath(clip.FilePath),
				clip => clip.FilePath,
				StringComparer.Ordinal);
		IList<AssemblyClipIntent> clipOrder =
			sketch.ClipOrder ?? Array.Empty<AssemblyClipIntent>();
		Dictionary<string, List<AssemblyClipIntent>> actualByReference = clipOrder
			.Where(item => item?.Clip != null &&
				!string.IsNullOrWhiteSpace(item.Clip.ReferenceId))
			.GroupBy(item => item.Clip.ReferenceId, StringComparer.Ordinal)
			.ToDictionary(
				group => group.Key,
				group => group.ToList(),
				StringComparer.Ordinal);
		if (clipOrder.Count != request.Clips.Count)
			diagnostics.Add(
				"clipOrder must contain exactly " + request.Clips.Count +
				" entries, but it contains " + clipOrder.Count + ".");
		foreach (KeyValuePair<string, string> authoritative in expected)
			if (!actualByReference.ContainsKey(authoritative.Key))
				diagnostics.Add(
					"Missing selected clip " + authoritative.Key + " ('" +
					Path.GetFileName(authoritative.Value) +
					"'); add its exact authoritative referenceId/mediaPath pair to clipOrder.");
		foreach (KeyValuePair<string, List<AssemblyClipIntent>> actual in actualByReference)
		{
			if (!expected.TryGetValue(actual.Key, out string? authoritativePath))
			{
				diagnostics.Add(
					"Invented clip referenceId '" + actual.Key +
					"' is not present in the authoritative input.");
				continue;
			}
			if (actual.Value.Count > 1)
				diagnostics.Add(
					"Selected clip " + actual.Key + " ('" +
					Path.GetFileName(authoritativePath) + "') appears " +
					actual.Value.Count + " times; every selected clip must appear once.");
			foreach (AssemblyClipIntent intent in actual.Value)
				if (!string.Equals(
						intent.Clip.MediaPath,
						authoritativePath,
						StringComparison.OrdinalIgnoreCase))
					diagnostics.Add(
						"Clip " + actual.Key + " changed mediaPath to '" +
						intent.Clip.MediaPath + "'; use the exact authoritative path '" +
						authoritativePath + "'.");
		}
		HashSet<string> actualClipReferences =
			actualByReference.Keys.ToHashSet(StringComparer.Ordinal);
		foreach (AssemblyClipIntent intent in clipOrder.Where(item => item != null))
			foreach (string alternative in intent.AlternativeClipReferenceIds ??
				Array.Empty<string>())
			{
				if (!expected.TryGetValue(alternative, out string? alternativePath))
					diagnostics.Add(
						"Clip order " + intent.Order + " references invented alternative '" +
						alternative + "'.");
				else if (!actualClipReferences.Contains(alternative))
					diagnostics.Add(
						"Clip order " + intent.Order + " references " + alternative +
						" ('" + Path.GetFileName(alternativePath) +
						"') as an alternative, but that selected clip is missing from " +
						"clipOrder; add the missing selected clip rather than removing it.");
			}
		IList<AssemblySectionIntent> sections =
			sketch.Sections ?? Array.Empty<AssemblySectionIntent>();
		HashSet<string> sectionIds = sections
			.Where(section => section != null &&
				!string.IsNullOrWhiteSpace(section.SectionId))
			.Select(section => section.SectionId)
			.ToHashSet(StringComparer.Ordinal);
		foreach (IGrouping<string, AssemblySectionIntent> duplicate in sections
			.Where(section => section != null &&
				!string.IsNullOrWhiteSpace(section.SectionId))
			.GroupBy(section => section.SectionId, StringComparer.Ordinal)
			.Where(group => group.Count() > 1))
			diagnostics.Add(
				"Section ID '" + duplicate.Key + "' is declared " +
				duplicate.Count() + " times; section IDs must be unique.");
		foreach (AssemblyClipIntent intent in clipOrder.Where(item => item != null))
			if (!sectionIds.Contains(intent.SectionId))
				diagnostics.Add(
					"Clip order " + intent.Order + " ('" +
					Path.GetFileName(intent.Clip?.MediaPath ?? "") +
					"') references undeclared sectionId '" + intent.SectionId +
					"'; use one sections[].sectionId from this sketch.");
		HashSet<string> regions = request.SongAnalysis.Regions
			.Select(region => region.Id)
			.ToHashSet(StringComparer.Ordinal);
		foreach (AssemblySectionIntent section in sections.Where(item => item != null))
			if (!string.IsNullOrWhiteSpace(section.RegionId) &&
				!regions.Contains(section.RegionId))
				diagnostics.Add(
					"Section '" + section.SectionId +
					"' references invented song regionId '" + section.RegionId + "'.");
		if (diagnostics.Count > 0)
			throw new InvalidDataException(
				"The assembly sketch differs from the authoritative request:\n- " +
				string.Join("\n- ", diagnostics));
		ProgressiveAssemblyContractValidator.Validate(sketch);
	}

	private static void ValidateContextAgainstRequest(
		EditPlanningRequest request,
		ProgressiveAssemblyPlanningContext context)
	{
		if (!string.Equals(context.RequestId, request.RequestId, StringComparison.Ordinal))
			throw new InvalidOperationException("The assembly context changed the request ID.");
		if (context.StepIndex != context.AcceptedPrefix.Count + 1)
			throw new InvalidOperationException(
				"The progressive step must immediately follow the accepted prefix.");
		if (context.RemainingClips.Count == 0)
			throw new InvalidOperationException("No clip remains to plan.");
		HashSet<string> requestClips = request.Clips
			.Select(clip => AssemblyReferenceIds.ForClipPath(clip.FilePath))
			.ToHashSet(StringComparer.Ordinal);
		if (context.RemainingClips.Any(clip => !requestClips.Contains(clip.ReferenceId)))
			throw new InvalidOperationException("The assembly context contains an unknown clip.");
		Dictionary<string, MontageSongPlanningEvent> events = request.SongAnalysis.Events
			.ToDictionary(item => item.Id, StringComparer.Ordinal);
		HashSet<string> nearbyIds = new(StringComparer.Ordinal);
		foreach (AssemblySongEventReference nearby in context.NearbySongContext)
		{
			if (!nearbyIds.Add(nearby.EventId) ||
				!events.TryGetValue(nearby.EventId, out MontageSongPlanningEvent? actual))
				throw new InvalidOperationException(
					"Nearby song context contains a duplicate or unknown event.");
			if (Math.Abs(nearby.EffectiveTimeSeconds - actual.EffectiveTimeSeconds) > 0.0001 ||
				!string.Equals(nearby.RegionId, actual.ContainingRegionId, StringComparison.Ordinal) ||
				!string.Equals(nearby.MusicalType, actual.MusicalType.ToString(), StringComparison.Ordinal))
				throw new InvalidOperationException(
					"A nearby song-event reference does not match the reviewed song map.");
		}
	}

	private static void ValidateStepAgainstContext(
		EditPlanningRequest request,
		ProgressiveAssemblyPlanningContext context,
		ClipStepDecision decision)
	{
		ProgressiveAssemblyContractValidator.Validate(decision);
		if (!string.Equals(decision.RequestId, request.RequestId, StringComparison.Ordinal) ||
			decision.StepIndex != context.StepIndex)
			throw new InvalidOperationException("The clip decision changed its request or step identity.");
		AssemblyClipReference? expected = context.RemainingClips.SingleOrDefault(item =>
			string.Equals(decision.Clip.ReferenceId, item.ReferenceId, StringComparison.Ordinal));
		if (expected == null ||
			!string.Equals(decision.Clip.MediaPath, expected.MediaPath, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("The model planned a clip outside the remaining set.");
		Core.Domain.Clip.Clip source = request.Clips.Single(clip =>
			string.Equals(AssemblyReferenceIds.ForClipPath(clip.FilePath), expected.ReferenceId,
				StringComparison.Ordinal));
		if (decision.SourceWindow.EndSeconds > source.DurationSeconds)
			throw new InvalidOperationException("The source window exceeds the selected clip.");
		if (!request.EffectOptions.EnableSpeedChanges &&
			(Math.Abs(decision.SourceWindow.ConstantSpeed - 1) > 0.000001 ||
				(decision.SourceWindow.VelocityCurve?.Count ?? 0) > 0))
			throw new InvalidOperationException(
				"The clip decision changed speed while speed changes are disabled.");
		Core.Domain.Audio.ShotEvent[] orderedKills = source.ConfirmedKills
			.OrderBy(kill => kill.SourceConfirmationTimeSeconds)
			.ToArray();
		int killCount = orderedKills.Length;
		IEnumerable<AssemblySyncDecision> syncs =
			new[] { decision.PrimarySync }.Concat(decision.AdditionalSyncs);
		HashSet<string> allowedEvents = context.NearbySongContext
			.Select(item => item.EventId)
			.ToHashSet(StringComparer.Ordinal);
		HashSet<int> killIndices = new();
		foreach (AssemblySyncDecision sync in syncs)
		{
			if (!allowedEvents.Contains(sync.MusicEventId))
				throw new InvalidOperationException("The clip decision used an event outside nearby context.");
			MontageSongPlanningEvent songEvent = request.SongAnalysis.Events.Single(
				item => string.Equals(item.Id, sync.MusicEventId, StringComparison.Ordinal));
			if (!songEvent.IsReviewed || !songEvent.IsGameplayAnchor ||
				songEvent.IsIntentionallyUnused)
				throw new InvalidOperationException(
					"The clip decision used an ineligible reviewed song event.");
			if (sync.KillIndex >= killCount || !killIndices.Add(sync.KillIndex))
				throw new InvalidOperationException("The clip decision used an invalid or repeated kill index.");
			double sourceTime = orderedKills[sync.KillIndex].SourceConfirmationTimeSeconds;
			if (sourceTime < decision.SourceWindow.StartSeconds - 0.0001 ||
				sourceTime > decision.SourceWindow.EndSeconds + 0.0001)
				throw new InvalidOperationException(
					"The clip decision selected kill index " + sync.KillIndex +
					" at source " + sourceTime.ToString("0.000") +
					"s outside its source window " +
					decision.SourceWindow.StartSeconds.ToString("0.000") + "s-" +
					decision.SourceWindow.EndSeconds.ToString("0.000") + "s.");
		}
	}
}
