using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Automation;
using AutoEditing.LlmEditor.Sessions;

namespace AutoEditing.LlmEditor.RoughCut;

internal interface IRoughCutChunkRenderer
{
	Task<RenderCandidatePreviewResult> RenderAsync(
		CandidateWorkspaceId workspace,
		TimeSpan start,
		TimeSpan duration,
		string outputRelativePath,
		string idempotencyKey,
		CancellationToken cancellationToken);
}

internal sealed class VegasRoughCutChunkRenderer : IRoughCutChunkRenderer
{
	private readonly IVegasAutomationClient automation;
	private readonly string renderProfileId;

	public VegasRoughCutChunkRenderer(
		IVegasAutomationClient automation,
		string renderProfileId = "review-1080p")
	{
		this.automation = automation ?? throw new ArgumentNullException(nameof(automation));
		this.renderProfileId = string.IsNullOrWhiteSpace(renderProfileId)
			? throw new ArgumentException("A render profile is required.", nameof(renderProfileId))
			: renderProfileId;
	}

	public Task<RenderCandidatePreviewResult> RenderAsync(
		CandidateWorkspaceId workspace,
		TimeSpan start,
		TimeSpan duration,
		string outputRelativePath,
		string idempotencyKey,
		CancellationToken cancellationToken) =>
		automation.ExecuteAsync<RenderCandidatePreviewRequest, RenderCandidatePreviewResult>(
			VegasOperations.RenderCandidatePreview,
			new RenderCandidatePreviewRequest
			{
				Workspace = workspace,
				Start = start,
				Duration = duration,
				RenderProfileId = renderProfileId,
				OutputRelativePath = outputRelativePath
			},
			idempotencyKey,
			cancellationToken: cancellationToken);
}

internal sealed class RoughCutFullRenderService
{
	private static readonly TimeSpan MaximumChunkDuration = TimeSpan.FromSeconds(20);
	private static readonly TimeSpan ResultDurationTolerance = TimeSpan.FromMilliseconds(10);

	private readonly string sessionId;
	private readonly RoughCutRenderArtifactStore artifacts;
	private readonly IRoughCutChunkRenderer renderer;
	private readonly string renderProfileId;
	private readonly Func<DateTimeOffset> clock;

	public RoughCutFullRenderService(
		string sessionId,
		string sessionRoot,
		IRoughCutChunkRenderer renderer,
		string renderProfileId = "review-1080p",
		Func<DateTimeOffset>? clock = null)
	{
		this.sessionId = string.IsNullOrWhiteSpace(sessionId)
			? throw new ArgumentException("A session ID is required.", nameof(sessionId))
			: sessionId;
		artifacts = new RoughCutRenderArtifactStore(sessionRoot);
		this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
		this.renderProfileId = string.IsNullOrWhiteSpace(renderProfileId)
			? throw new ArgumentException("A render profile is required.", nameof(renderProfileId))
			: renderProfileId;
		this.clock = clock ?? (() => DateTimeOffset.UtcNow);
	}

	public async Task<(RoughCutRenderManifest Manifest, RoughCutEvidenceReference Evidence)>
		RenderAsync(
			CandidateTimelineSnapshot timeline,
			string planSha256,
			CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(timeline);
		timeline.Workspace?.Validate();
		if (timeline.Workspace == null)
			throw new InvalidOperationException(
				"A complete rough-cut render requires a VEGAS workspace.");
		if (timeline.TimelineStart < TimeSpan.Zero ||
			timeline.TimelineEnd <= timeline.TimelineStart)
			throw new InvalidOperationException(
				"The candidate timeline has no complete renderable range.");
		if (planSha256 == null || planSha256.Length != 64 ||
			planSha256.Any(character => !Uri.IsHexDigit(character)))
			throw new InvalidOperationException("A valid accepted-plan hash is required.");

		string renderId = "sync-" + planSha256[..16];
		RoughCutRenderManifest? existing = artifacts.ReadValidManifest(renderId);
		if (existing != null)
		{
			if (!string.Equals(existing.SessionId, sessionId, StringComparison.Ordinal) ||
				!string.Equals(
					existing.PlanSha256, planSha256, StringComparison.OrdinalIgnoreCase) ||
				existing.TimelineStart != timeline.TimelineStart ||
				existing.TimelineEnd != timeline.TimelineEnd ||
				!string.Equals(
					existing.Workspace.ToString(),
					timeline.Workspace.ToString(),
					StringComparison.Ordinal))
				throw new InvalidDataException(
					"A completed rough-cut render exists for different session state.");
			return (existing, artifacts.EvidenceForManifest(existing));
		}
		IReadOnlyList<(TimeSpan Start, TimeSpan Duration)> windows =
			CreateWindows(timeline.TimelineStart, timeline.TimelineEnd);
		List<RoughCutRenderChunk> chunks = new();
		for (int index = 0; index < windows.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			int chunkIndex = index + 1;
			(TimeSpan start, TimeSpan duration) = windows[index];
			RoughCutRenderChunk? chunk = artifacts.ReadValidChunk(
				renderId, chunkIndex, start, duration);
			if (chunk != null)
			{
				chunks.Add(chunk);
				continue;
			}
			string output = RoughCutRenderArtifactStore.ChunkOutputRelativePath(
				renderId, chunkIndex);
			bool freshRenderRequired =
				artifacts.RequiresFreshRender(renderId, chunkIndex);
			RenderCandidatePreviewResult result = await renderer.RenderAsync(
				timeline.Workspace,
				start,
				duration,
				output,
				$"rough-cut-{renderId}-chunk-{chunkIndex:D4}" +
					(freshRenderRequired
						? "-repair-" + Guid.NewGuid().ToString("N")
						: ""),
				cancellationToken);
			ValidateResult(result, output, duration, renderProfileId);
			try
			{
				artifacts.ValidateOutput(output, result.Sha256);
			}
			catch (InvalidDataException) when (!freshRenderRequired)
			{
				// The automation broker may have recovered an idempotent response
				// whose output disappeared before chunk metadata was committed.
				// Quarantine any untrusted bytes and force one genuinely new render.
				artifacts.QuarantineUntrustedOutput(
					renderId, chunkIndex, "uncommitted-output-invalid");
				result = await renderer.RenderAsync(
					timeline.Workspace,
					start,
					duration,
					output,
					$"rough-cut-{renderId}-chunk-{chunkIndex:D4}-repair-" +
						Guid.NewGuid().ToString("N"),
					cancellationToken);
				ValidateResult(result, output, duration, renderProfileId);
				artifacts.ValidateOutput(output, result.Sha256);
			}
			chunk = new RoughCutRenderChunk
			{
				ChunkIndex = chunkIndex,
				Start = start,
				Duration = duration,
				OutputRelativePath = result.OutputRelativePath,
				Sha256 = result.Sha256.ToLowerInvariant()
			};
			artifacts.SaveChunk(renderId, chunk);
			chunks.Add(chunk);
		}
		RoughCutRenderManifest manifest = new()
		{
			SessionId = sessionId,
			RenderId = renderId,
			Workspace = timeline.Workspace,
			PlanSha256 = planSha256.ToLowerInvariant(),
			RenderProfileId = renderProfileId,
			TimelineStart = timeline.TimelineStart,
			TimelineEnd = timeline.TimelineEnd,
			CompletedUtc = clock(),
			Chunks = chunks
		};
		RoughCutRenderContractValidator.Validate(manifest);
		RoughCutEvidenceReference evidence = artifacts.SaveManifest(manifest);
		return (manifest, evidence);
	}

	internal static IReadOnlyList<(TimeSpan Start, TimeSpan Duration)> CreateWindows(
		TimeSpan start,
		TimeSpan end)
	{
		if (start < TimeSpan.Zero || end <= start)
			throw new ArgumentOutOfRangeException(nameof(end));
		List<(TimeSpan, TimeSpan)> result = new();
		for (TimeSpan cursor = start; cursor < end;)
		{
			TimeSpan duration = end - cursor;
			if (duration > MaximumChunkDuration) duration = MaximumChunkDuration;
			result.Add((cursor, duration));
			cursor += duration;
		}
		return result;
	}

	private static void ValidateResult(
		RenderCandidatePreviewResult result,
		string expectedPath,
		TimeSpan expectedDuration,
		string expectedProfile)
	{
		if (result == null)
			throw new InvalidOperationException("VEGAS returned no rough-cut render result.");
		if (!string.Equals(
			result.OutputRelativePath,
			expectedPath,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"VEGAS changed the rough-cut render output path.");
		if ((result.RenderedDuration - expectedDuration).Duration() >
			ResultDurationTolerance)
			throw new InvalidOperationException(
				"VEGAS returned a rough-cut render with an unexpected duration.");
		if (!string.Equals(
			result.RenderProfileId,
			expectedProfile,
			StringComparison.Ordinal))
			throw new InvalidOperationException(
				"VEGAS changed the rough-cut render profile.");
		if (result.Sha256 == null || result.Sha256.Length != 64 ||
			result.Sha256.Any(character => !Uri.IsHexDigit(character)))
			throw new InvalidOperationException(
				"VEGAS returned an invalid rough-cut render hash.");
	}
}

internal sealed class RoughCutRenderArtifactStore
{
	private readonly SessionPathResolver paths;
	private readonly AtomicFileWriter writer = new();

	public RoughCutRenderArtifactStore(string sessionRoot)
	{
		paths = new SessionPathResolver(sessionRoot);
	}

	public void SaveChunk(string renderId, RoughCutRenderChunk chunk)
	{
		string relative = ChunkMetadataRelativePath(renderId, chunk.ChunkIndex);
		WriteImmutable(relative, ContractSerializer.Serialize(chunk));
	}

	public RoughCutRenderChunk? ReadChunk(string renderId, int chunkIndex)
	{
		string path = paths.Resolve(ChunkMetadataRelativePath(renderId, chunkIndex));
		return File.Exists(path)
			? ContractSerializer.Deserialize<RoughCutRenderChunk>(File.ReadAllText(path))
			: null;
	}

	public RoughCutRenderChunk? ReadValidChunk(
		string renderId,
		int chunkIndex,
		TimeSpan expectedStart,
		TimeSpan expectedDuration)
	{
		RoughCutRenderChunk? chunk;
		try
		{
			chunk = ReadChunk(renderId, chunkIndex);
		}
		catch (Exception exception) when (
			exception is IOException or InvalidDataException)
		{
			QuarantineChunk(renderId, chunkIndex, "invalid-metadata");
			return null;
		}
		if (chunk == null) return null;
		if (chunk.ChunkIndex != chunkIndex ||
			chunk.Start != expectedStart ||
			chunk.Duration != expectedDuration ||
			!string.Equals(
				chunk.OutputRelativePath,
				ChunkOutputRelativePath(renderId, chunkIndex),
				StringComparison.OrdinalIgnoreCase) ||
			!OutputMatches(chunk.OutputRelativePath, chunk.Sha256))
		{
			QuarantineChunk(renderId, chunkIndex, "stale-or-corrupt");
			return null;
		}
		return chunk;
	}

	public void ValidateOutput(string outputRelativePath, string expectedSha256)
	{
		if (!OutputMatches(outputRelativePath, expectedSha256))
			throw new InvalidDataException(
				"The rendered rough-cut chunk is missing or its bytes do not match " +
				"the SHA-256 returned by VEGAS.");
	}

	public bool RequiresFreshRender(string renderId, int chunkIndex)
	{
		string directory = paths.Resolve(
			$"assembly/rough-cut/quarantine/{renderId}");
		return Directory.Exists(directory) &&
			Directory.EnumerateFiles(
				directory,
				$"chunk-{chunkIndex:D4}-*",
				SearchOption.TopDirectoryOnly)
				.Any();
	}

	public void QuarantineUntrustedOutput(
		string renderId,
		int chunkIndex,
		string reason)
	{
		Quarantine(
			ChunkOutputRelativePath(renderId, chunkIndex),
			renderId,
			$"chunk-{chunkIndex:D4}-output-{reason}");
		string marker = paths.Resolve(
			$"assembly/rough-cut/quarantine/{renderId}/" +
			$"chunk-{chunkIndex:D4}-repair-required-" +
			Guid.NewGuid().ToString("N") + ".txt");
		writer.WriteText(
			marker,
			"Fresh render required: " + reason + Environment.NewLine);
	}

	public RoughCutEvidenceReference SaveManifest(RoughCutRenderManifest manifest)
	{
		RoughCutRenderContractValidator.Validate(manifest);
		string relative = ManifestRelativePath(manifest.RenderId);
		string json = ContractSerializer.Serialize(manifest);
		WriteImmutable(relative, json);
		return EvidenceForManifest(manifest);
	}

	public RoughCutRenderManifest? ReadManifest(string renderId)
	{
		string path = paths.Resolve(ManifestRelativePath(renderId));
		if (!File.Exists(path)) return null;
		RoughCutRenderManifest manifest =
			ContractSerializer.Deserialize<RoughCutRenderManifest>(File.ReadAllText(path));
		RoughCutRenderContractValidator.Validate(manifest);
		return manifest;
	}

	public RoughCutRenderManifest? ReadValidManifest(string renderId)
	{
		RoughCutRenderManifest? manifest;
		try
		{
			manifest = ReadManifest(renderId);
		}
		catch (Exception exception) when (
			exception is IOException or InvalidDataException)
		{
			QuarantineManifest(renderId, "invalid");
			return null;
		}
		if (manifest == null) return null;
		foreach (RoughCutRenderChunk chunk in manifest.Chunks)
		{
			if (!OutputMatches(chunk.OutputRelativePath, chunk.Sha256))
			{
				QuarantineChunk(
					renderId, chunk.ChunkIndex, "manifest-output-corrupt");
				QuarantineManifest(renderId, "output-corrupt");
				return null;
			}
		}
		return manifest;
	}

	public RoughCutEvidenceReference EvidenceForManifest(
		RoughCutRenderManifest manifest)
	{
		RoughCutRenderContractValidator.Validate(manifest);
		string relative = ManifestRelativePath(manifest.RenderId);
		string path = paths.Resolve(relative);
		if (!File.Exists(path))
			throw new FileNotFoundException(
				"The completed rough-cut render manifest is missing.", path);
		string hash = new SessionArtifactHasher().ComputeSha256(path);
		return new RoughCutEvidenceReference
		{
			EvidenceId = "full-render-" + manifest.RenderId,
			Kind = RoughCutEvidenceKind.FullRender,
			RelativePath = relative,
			Sha256 = hash,
			MediaType = "application/json",
			Description =
				$"Complete synchronization-pass render manifest: {manifest.Chunks.Count} " +
				$"contiguous chunk(s), {(manifest.TimelineEnd - manifest.TimelineStart).TotalSeconds:0.###} s total."
		};
	}

	public static string ChunkOutputRelativePath(string renderId, int chunkIndex) =>
		$"{RenderRoot(renderId)}/chunks/{chunkIndex:D4}.mp4";

	private static string ChunkMetadataRelativePath(string renderId, int chunkIndex) =>
		$"{RenderRoot(renderId)}/chunks/{chunkIndex:D4}.json";

	private static string ManifestRelativePath(string renderId) =>
		$"{RenderRoot(renderId)}/manifest.json";

	private static string RenderRoot(string renderId)
	{
		if (string.IsNullOrWhiteSpace(renderId) ||
			renderId.Any(character =>
				!char.IsLetterOrDigit(character) &&
				character is not ('-' or '_' or '.')))
			throw new InvalidOperationException("The rough-cut render ID is unsafe.");
		return "assembly/rough-cut/renders/" + renderId;
	}

	private void WriteImmutable(string relativePath, string content)
	{
		string path = paths.Resolve(relativePath);
		if (File.Exists(path))
		{
			if (string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
				return;
			throw new InvalidOperationException(
				"An immutable rough-cut render artifact already exists.");
		}
		writer.WriteText(path, content);
	}

	private bool OutputMatches(string relativePath, string expectedSha256)
	{
		try
		{
			string path = paths.Resolve(relativePath);
			return File.Exists(path) &&
				string.Equals(
					new SessionArtifactHasher().ComputeSha256(path),
					expectedSha256,
					StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception exception) when (
			exception is IOException or UnauthorizedAccessException or
				InvalidOperationException or ArgumentException)
		{
			return false;
		}
	}

	private void QuarantineChunk(
		string renderId,
		int chunkIndex,
		string reason)
	{
		Quarantine(
			ChunkMetadataRelativePath(renderId, chunkIndex),
			renderId, $"chunk-{chunkIndex:D4}-metadata-{reason}");
		Quarantine(
			ChunkOutputRelativePath(renderId, chunkIndex),
			renderId, $"chunk-{chunkIndex:D4}-output-{reason}");
	}

	private void QuarantineManifest(string renderId, string reason) =>
		Quarantine(
			ManifestRelativePath(renderId),
			renderId,
			"manifest-" + reason);

	private void Quarantine(
		string relativePath,
		string renderId,
		string label)
	{
		string source = paths.Resolve(relativePath);
		if (!File.Exists(source)) return;
		string extension = Path.GetExtension(source);
		string destination = paths.Resolve(
			$"assembly/rough-cut/quarantine/{renderId}/" +
			$"{label}-{DateTimeOffset.UtcNow.UtcTicks:D19}-" +
			Guid.NewGuid().ToString("N") + extension);
		Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
		File.Move(source, destination);
	}
}
