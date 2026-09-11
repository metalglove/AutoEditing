using System.Security.Cryptography;
using System.Text;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.RoughCut;
using AutoEditing.LlmEditor.Sessions;

namespace AutoEditing.LlmEditor.Assembly;

internal interface ISectionMilestoneRenderer
{
	Task<SectionMilestoneRenderManifest> RenderAsync(
		string sectionId,
		int completedCheckpoint,
		CandidateWorkspaceId workspace,
		TimeSpan timelineStart,
		TimeSpan timelineEnd,
		string planSha256,
		CancellationToken cancellationToken);
}

internal sealed class SectionMilestoneRenderService :
	ISectionMilestoneRenderer
{
	private static readonly TimeSpan DurationTolerance =
		TimeSpan.FromMilliseconds(10);
	private readonly string sessionId;
	private readonly SessionPathResolver paths;
	private readonly AtomicFileWriter writer = new();
	private readonly IRoughCutChunkRenderer renderer;
	private readonly string renderProfileId;
	private readonly Func<DateTimeOffset> clock;

	public SectionMilestoneRenderService(
		string sessionId,
		string sessionRoot,
		IRoughCutChunkRenderer renderer,
		string renderProfileId = "review-1080p",
		Func<DateTimeOffset>? clock = null)
	{
		this.sessionId = string.IsNullOrWhiteSpace(sessionId)
			? throw new ArgumentException(
				"A session ID is required.",
				nameof(sessionId))
			: sessionId;
		paths = new SessionPathResolver(sessionRoot);
		this.renderer = renderer ??
			throw new ArgumentNullException(nameof(renderer));
		this.renderProfileId = string.IsNullOrWhiteSpace(renderProfileId)
			? throw new ArgumentException(
				"A render profile is required.",
				nameof(renderProfileId))
			: renderProfileId;
		this.clock = clock ?? (() => DateTimeOffset.UtcNow);
	}

	public async Task<SectionMilestoneRenderManifest> RenderAsync(
		string sectionId,
		int completedCheckpoint,
		CandidateWorkspaceId workspace,
		TimeSpan timelineStart,
		TimeSpan timelineEnd,
		string planSha256,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);
		if (completedCheckpoint < 1)
			throw new ArgumentOutOfRangeException(nameof(completedCheckpoint));
		ArgumentNullException.ThrowIfNull(workspace);
		workspace.Validate();
		string normalizedHash = NormalizeHash(planSha256);
		if (timelineStart < TimeSpan.Zero || timelineEnd <= timelineStart)
			throw new InvalidOperationException(
				"The completed song section has no renderable range.");
		string key = SectionKey(sectionId);
		string root =
			$"assembly/milestones/sections/{key}/checkpoint-{completedCheckpoint:D4}";
		string currentPath = paths.Resolve(root + "/manifest.json");
		if (File.Exists(currentPath))
		{
			SectionMilestoneRenderManifest existing = Read(currentPath);
			if (!string.Equals(
					existing.SessionId,
					sessionId,
					StringComparison.Ordinal) ||
				!string.Equals(
					existing.SectionId,
					sectionId,
					StringComparison.Ordinal) ||
				existing.CompletedCheckpoint != completedCheckpoint ||
				!string.Equals(
					existing.Workspace.ToString(),
					workspace.ToString(),
					StringComparison.Ordinal) ||
				!string.Equals(
					existing.PlanSha256,
					normalizedHash,
					StringComparison.OrdinalIgnoreCase) ||
				existing.TimelineStart != timelineStart ||
				existing.TimelineEnd != timelineEnd)
				throw new InvalidDataException(
					"A section milestone exists for different accepted state.");
			ValidateFiles(existing);
			return existing;
		}

		IReadOnlyList<(TimeSpan Start, TimeSpan Duration)> windows =
			RoughCutFullRenderService.CreateWindows(timelineStart, timelineEnd);
		List<RoughCutRenderChunk> chunks = new();
		for (int index = 0; index < windows.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			int chunkIndex = index + 1;
			(TimeSpan start, TimeSpan duration) = windows[index];
			string relative =
				$"{root}/chunk-{chunkIndex:D4}.mp4";
			RenderCandidatePreviewResult result = await renderer.RenderAsync(
				workspace,
				start,
				duration,
				relative,
				$"section-{key}-checkpoint-{completedCheckpoint:D4}-" +
					$"{normalizedHash[..16]}-chunk-{chunkIndex:D4}",
				cancellationToken);
			if (result == null ||
				!string.Equals(
					result.OutputRelativePath,
					relative,
					StringComparison.Ordinal) ||
				(result.RenderedDuration - duration).Duration() >
					DurationTolerance ||
				!string.Equals(
					result.RenderProfileId,
					renderProfileId,
					StringComparison.Ordinal))
				throw new InvalidDataException(
					"VEGAS returned invalid section milestone render evidence.");
			string resultHash = NormalizeHash(result.Sha256);
			string outputPath = paths.Resolve(relative);
			if (!File.Exists(outputPath) ||
				!string.Equals(
					HashFile(outputPath),
					resultHash,
					StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					"A section milestone output is missing or its hash differs.");
			chunks.Add(new RoughCutRenderChunk
			{
				ChunkIndex = chunkIndex,
				Start = start,
				Duration = duration,
				OutputRelativePath = relative,
				Sha256 = resultHash
			});
		}
		SectionMilestoneRenderManifest manifest = new()
		{
			SessionId = sessionId,
			SectionId = sectionId,
			CompletedCheckpoint = completedCheckpoint,
			Workspace = workspace,
			PlanSha256 = normalizedHash,
			RenderProfileId = renderProfileId,
			TimelineStart = timelineStart,
			TimelineEnd = timelineEnd,
			Chunks = chunks,
			CompletedUtc = clock()
		};
		SectionMilestoneRenderContractValidator.Validate(manifest);
		string json = ContractSerializer.Serialize(manifest);
		writer.WriteText(currentPath, json);
		writer.WriteText(
			paths.Resolve(
				$"assembly/milestones/sections/{key}/current.json"),
			json);
		return manifest;
	}

	private void ValidateFiles(SectionMilestoneRenderManifest manifest)
	{
		foreach (RoughCutRenderChunk chunk in manifest.Chunks)
		{
			string path = paths.Resolve(chunk.OutputRelativePath);
			if (!File.Exists(path) ||
				!string.Equals(
					HashFile(path),
					chunk.Sha256,
					StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					"A cached section milestone chunk is missing or corrupt.");
		}
	}

	private static SectionMilestoneRenderManifest Read(string path)
	{
		SectionMilestoneRenderManifest value =
			ContractSerializer.Deserialize<SectionMilestoneRenderManifest>(
				File.ReadAllText(path));
		SectionMilestoneRenderContractValidator.Validate(value);
		return value;
	}

	private static string SectionKey(string sectionId)
	{
		using SHA256 sha = SHA256.Create();
		return Convert.ToHexString(
			sha.ComputeHash(
				new UTF8Encoding(false).GetBytes(sectionId)))
			.ToLowerInvariant()[..16];
	}

	private static string NormalizeHash(string value)
	{
		string normalized = value?.Trim().ToLowerInvariant() ?? "";
		if (normalized.Length != 64 ||
			normalized.Any(character => !Uri.IsHexDigit(character)))
			throw new InvalidDataException("A valid SHA-256 value is required.");
		return normalized;
	}

	private static string HashFile(string path)
	{
		using SHA256 sha = SHA256.Create();
		using FileStream stream = File.OpenRead(path);
		return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
	}
}
