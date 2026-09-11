using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Sessions;
using Newtonsoft.Json.Linq;

namespace AutoEditing.LlmEditor.Polish;

internal sealed class PolishPassArtifactStore
{
	private readonly SessionPathResolver paths;
	private readonly AtomicFileWriter writer = new();

	public PolishPassArtifactStore(string sessionRoot)
	{
		paths = new SessionPathResolver(sessionRoot);
	}

	public string SavePlan(EffectsPassPlan plan)
	{
		PolishPassContractValidator.Validate(plan);
		return WritePlan(PolishPassKind.Effects, plan.Revision,
			ContractSerializer.Serialize(plan));
	}

	public string SavePlan(AudioPassPlan plan)
	{
		PolishPassContractValidator.Validate(plan);
		return WritePlan(PolishPassKind.Audio, plan.Revision,
			ContractSerializer.Serialize(plan));
	}

	public void SaveApproval(PolishPassApproval approval)
	{
		WriteImmutable(
			$"{PassRoot(approval.Pass)}/revisions/{approval.PlanRevision:D4}/approval.json",
			ContractSerializer.Serialize(approval));
	}

	public string SaveMaterialization(PolishPassMaterialization value)
	{
		PolishPassContractValidator.Validate(value);
		string json = ContractSerializer.Serialize(value);
		WriteImmutable(
			$"{PassRoot(value.Pass)}/revisions/{value.PlanRevision:D4}/materialization.json",
			json);
		return Hash(json);
	}

	public string SavePreview(PolishPassPreviewManifest value)
	{
		PolishPassContractValidator.Validate(value);
		string json = ContractSerializer.Serialize(value);
		WriteImmutable(
			$"{PassRoot(value.Pass)}/revisions/{value.PlanRevision:D4}/preview/manifest.json",
			json);
		return Hash(json);
	}

	public void SavePreviewChunk(
		PolishPassKind pass,
		int revision,
		RoughCutRenderChunk chunk)
	{
		if (chunk == null || chunk.ChunkIndex < 1 ||
			chunk.Duration <= TimeSpan.Zero ||
			chunk.Duration > TimeSpan.FromSeconds(20) ||
			string.IsNullOrWhiteSpace(chunk.OutputRelativePath) ||
			chunk.Sha256 == null || chunk.Sha256.Length != 64 ||
			chunk.Sha256.Any(character => !Uri.IsHexDigit(character)))
			throw new InvalidDataException("Polish preview chunk is invalid.");
		WriteImmutable(
			$"{PassRoot(pass)}/revisions/{revision:D4}/preview/chunks/" +
				$"{chunk.ChunkIndex:D4}.json",
			ContractSerializer.Serialize(chunk));
	}

	public RoughCutRenderChunk? ReadPreviewChunk(
		PolishPassKind pass,
		int revision,
		int chunkIndex)
	{
		string path = paths.Resolve(
			$"{PassRoot(pass)}/revisions/{revision:D4}/preview/chunks/" +
			$"{chunkIndex:D4}.json");
		return !File.Exists(path) ? null :
			ContractSerializer.Deserialize<RoughCutRenderChunk>(File.ReadAllText(path));
	}

	public RoughCutRenderChunk? ReadValidPreviewChunk(
		PolishPassKind pass,
		int revision,
		int chunkIndex,
		TimeSpan expectedStart,
		TimeSpan expectedDuration)
	{
		RoughCutRenderChunk? chunk;
		try
		{
			chunk = ReadPreviewChunk(pass, revision, chunkIndex);
		}
		catch (Exception exception) when (
			exception is IOException or InvalidDataException)
		{
			QuarantinePreviewChunk(
				pass, revision, chunkIndex, "invalid-metadata");
			return null;
		}
		if (chunk == null) return null;
		if (chunk.ChunkIndex != chunkIndex ||
			chunk.Start != expectedStart ||
			chunk.Duration != expectedDuration ||
			!string.Equals(
				chunk.OutputRelativePath,
				PreviewOutputRelativePath(pass, revision, chunkIndex),
				StringComparison.OrdinalIgnoreCase) ||
			!OutputMatches(chunk.OutputRelativePath, chunk.Sha256))
		{
			QuarantinePreviewChunk(
				pass, revision, chunkIndex, "stale-or-corrupt");
			return null;
		}
		return chunk;
	}

	public void ValidatePreviewOutput(
		string outputRelativePath,
		string expectedSha256)
	{
		if (!OutputMatches(outputRelativePath, expectedSha256))
			throw new InvalidDataException(
				"The rendered polish preview chunk is missing or its bytes do not " +
				"match the SHA-256 returned by VEGAS.");
	}

	public bool RequiresFreshPreviewRender(
		PolishPassKind pass,
		int revision,
		int chunkIndex)
	{
		string directory = paths.Resolve(
			$"{PassRoot(pass)}/quarantine/revision-{revision:D4}");
		return Directory.Exists(directory) &&
			Directory.EnumerateFiles(
				directory,
				$"chunk-{chunkIndex:D4}-*",
				SearchOption.TopDirectoryOnly)
				.Any();
	}

	public void QuarantineUntrustedPreviewOutput(
		PolishPassKind pass,
		int revision,
		int chunkIndex,
		string reason)
	{
		Quarantine(
			pass,
			revision,
			PreviewOutputRelativePath(pass, revision, chunkIndex),
			$"chunk-{chunkIndex:D4}-output-{reason}");
		string marker = paths.Resolve(
			$"{PassRoot(pass)}/quarantine/revision-{revision:D4}/" +
			$"chunk-{chunkIndex:D4}-repair-required-" +
			Guid.NewGuid().ToString("N") + ".txt");
		writer.WriteText(
			marker,
			"Fresh render required: " + reason + Environment.NewLine);
	}

	public void SaveAccepted(AcceptedPolishPass value)
	{
		PolishPassContractValidator.Validate(value);
		WriteImmutable(
			$"{PassRoot(value.Pass)}/revisions/accepted-{value.PlanSha256[..16]}.json",
			ContractSerializer.Serialize(value));
	}

	public void SaveRejected(RejectedPolishPassPreview value)
	{
		PolishPassContractValidator.Validate(value);
		WriteImmutable(
			$"{PassRoot(value.Pass)}/revisions/rejected-{value.PlanSha256[..16]}.json",
			ContractSerializer.Serialize(value));
	}

	public AcceptedPolishPass? ReadAccepted(
		PolishPassKind pass,
		string planSha256)
	{
		ValidateHash(planSha256, nameof(planSha256));
		string path = paths.Resolve(
			$"{PassRoot(pass)}/revisions/accepted-{planSha256[..16]}.json");
		if (!File.Exists(path)) return null;
		AcceptedPolishPass value =
			ContractSerializer.Deserialize<AcceptedPolishPass>(File.ReadAllText(path));
		PolishPassContractValidator.Validate(value);
		if (!string.Equals(
			value.PlanSha256,
			planSha256,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The accepted polish artifact does not match its requested plan hash.");
		return value;
	}

	public RejectedPolishPassPreview? ReadRejected(
		PolishPassKind pass,
		string planSha256)
	{
		ValidateHash(planSha256, nameof(planSha256));
		string path = paths.Resolve(
			$"{PassRoot(pass)}/revisions/rejected-{planSha256[..16]}.json");
		if (!File.Exists(path)) return null;
		RejectedPolishPassPreview value =
			ContractSerializer.Deserialize<RejectedPolishPassPreview>(
				File.ReadAllText(path));
		PolishPassContractValidator.Validate(value);
		if (!string.Equals(
			value.PlanSha256,
			planSha256,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The rejected polish artifact does not match its requested plan hash.");
		return value;
	}

	public void SaveRestorationIntent(PolishBaselineRestoration value)
	{
		PolishPassContractValidator.Validate(value);
		if (value.Completed)
			throw new InvalidOperationException(
				"A restoration intent cannot already claim completion.");
		WriteImmutable(
			RestorationRoot(value) + "/intent.json",
			ContractSerializer.Serialize(value));
	}

	public void SaveRestorationReceipt(PolishBaselineRestoration value)
	{
		PolishPassContractValidator.Validate(value);
		if (!value.Completed)
			throw new InvalidOperationException(
				"A restoration receipt must contain completion evidence.");
		WriteImmutable(
			RestorationRoot(value) + "/receipt.json",
			ContractSerializer.Serialize(value));
	}

	public PolishBaselineRestoration? ReadRestorationIntent(
		PolishPassKind pass,
		string rejectedPlanSha256)
	{
		ValidateHash(rejectedPlanSha256, nameof(rejectedPlanSha256));
		string directory = paths.Resolve(
			$"{PassRoot(pass)}/restorations/{rejectedPlanSha256[..16]}");
		if (!Directory.Exists(directory)) return null;
		string? path = Directory.EnumerateFiles(
				directory,
				"intent.json",
				SearchOption.AllDirectories)
			.OrderBy(value => value, StringComparer.Ordinal)
			.FirstOrDefault();
		return path == null
			? null
			: ReadRestoration(path, pass, rejectedPlanSha256, completed: false);
	}

	public PolishBaselineRestoration? ReadRestorationReceipt(
		PolishPassKind pass,
		string rejectedPlanSha256)
	{
		ValidateHash(rejectedPlanSha256, nameof(rejectedPlanSha256));
		string directory = paths.Resolve(
			$"{PassRoot(pass)}/restorations/{rejectedPlanSha256[..16]}");
		if (!Directory.Exists(directory)) return null;
		string? path = Directory.EnumerateFiles(
				directory,
				"receipt.json",
				SearchOption.AllDirectories)
			.OrderBy(value => value, StringComparer.Ordinal)
			.FirstOrDefault();
		return path == null
			? null
			: ReadRestoration(path, pass, rejectedPlanSha256, completed: true);
	}

	public void AppendState(PolishPassStateRecord value)
	{
		PolishPassContractValidator.Validate(value);
		WriteImmutable(
			$"{PassRoot(value.Pass)}/states/{value.Sequence:D8}.json",
			ContractSerializer.Serialize(value));
	}

	public PolishPassStateRecord? ReadLatestState(PolishPassKind pass)
	{
		string directory = paths.Resolve($"{PassRoot(pass)}/states");
		if (!Directory.Exists(directory)) return null;
		string? path = Directory.EnumerateFiles(directory, "*.json")
			.OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
			.FirstOrDefault();
		return path == null ? null :
			ContractSerializer.Deserialize<PolishPassStateRecord>(File.ReadAllText(path));
	}

	public EffectsPassPlan? ReadEffectsPlan(int revision)
	{
		string path = paths.Resolve(
			$"{PassRoot(PolishPassKind.Effects)}/revisions/{revision:D4}/plan.json");
		return !File.Exists(path) ? null :
			ContractSerializer.Deserialize<EffectsPassPlan>(File.ReadAllText(path));
	}

	public AudioPassPlan? ReadAudioPlan(int revision)
	{
		string path = paths.Resolve(
			$"{PassRoot(PolishPassKind.Audio)}/revisions/{revision:D4}/plan.json");
		return !File.Exists(path) ? null :
			ContractSerializer.Deserialize<AudioPassPlan>(File.ReadAllText(path));
	}

	public PolishPassApproval? ReadApproval(PolishPassKind pass, int revision)
	{
		string path = paths.Resolve(
			$"{PassRoot(pass)}/revisions/{revision:D4}/approval.json");
		return !File.Exists(path) ? null :
			ContractSerializer.Deserialize<PolishPassApproval>(File.ReadAllText(path));
	}

	public PolishPassMaterialization? ReadMaterialization(
		PolishPassKind pass,
		int revision)
	{
		string path = paths.Resolve(
			$"{PassRoot(pass)}/revisions/{revision:D4}/materialization.json");
		return !File.Exists(path) ? null :
			ContractSerializer.Deserialize<PolishPassMaterialization>(
				File.ReadAllText(path));
	}

	public PolishPassPreviewManifest? ReadPreview(PolishPassKind pass, int revision)
	{
		string path = paths.Resolve(
			$"{PassRoot(pass)}/revisions/{revision:D4}/preview/manifest.json");
		return !File.Exists(path) ? null :
			ContractSerializer.Deserialize<PolishPassPreviewManifest>(
				File.ReadAllText(path));
	}

	public PolishPassPreviewManifest? ReadValidPreview(
		PolishPassKind pass,
		int revision)
	{
		PolishPassPreviewManifest? preview;
		try
		{
			preview = ReadPreview(pass, revision);
			if (preview != null)
				PolishPassContractValidator.Validate(preview);
		}
		catch (Exception exception) when (
			exception is IOException or InvalidDataException)
		{
			QuarantinePreviewManifest(pass, revision, "invalid");
			return null;
		}
		if (preview == null) return null;
		foreach (RoughCutRenderChunk chunk in preview.Chunks)
		{
			if (OutputMatches(chunk.OutputRelativePath, chunk.Sha256))
				continue;
			QuarantinePreviewChunk(
				pass, revision, chunk.ChunkIndex, "manifest-output-corrupt");
			QuarantinePreviewManifest(pass, revision, "output-corrupt");
			return null;
		}
		return preview;
	}

	public string ArtifactSha256(PolishPassKind pass, int revision, string name)
	{
		string relative = name switch
		{
			"materialization" =>
				$"{PassRoot(pass)}/revisions/{revision:D4}/materialization.json",
			"preview" =>
				$"{PassRoot(pass)}/revisions/{revision:D4}/preview/manifest.json",
			_ => throw new ArgumentOutOfRangeException(nameof(name))
		};
		string path = paths.Resolve(relative);
		if (!File.Exists(path)) throw new FileNotFoundException("Polish artifact is missing.", path);
		return new SessionArtifactHasher().ComputeSha256(path);
	}

	public static string PreviewOutputRelativePath(
		PolishPassKind pass,
		int revision,
		int chunk) =>
		$"assembly/polish/{(pass == PolishPassKind.Effects ? "effects" : "audio")}/" +
		$"revisions/{revision:D4}/preview/chunks/{chunk:D4}.mp4";

	private string WritePlan(PolishPassKind pass, int revision, string json)
	{
		WriteImmutable($"{PassRoot(pass)}/revisions/{revision:D4}/plan.json", json);
		return Hash(json);
	}

	private void WriteImmutable(string relative, string content)
	{
		string path = paths.Resolve(relative);
		if (File.Exists(path))
		{
			if (string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
				return;
			throw new InvalidOperationException(
				"An immutable polish artifact already exists with different content.");
		}
		writer.WriteText(path, content);
	}

	private static string PassRoot(PolishPassKind pass) =>
		"assembly/polish/" + (pass == PolishPassKind.Effects ? "effects" : "audio");
	private static string Hash(string json) => ContractHash.Compute(JToken.Parse(json));

	private static string RestorationRoot(PolishBaselineRestoration value) =>
		$"{PassRoot(value.RejectedPass)}/restorations/" +
		$"{value.RejectedPlanSha256[..16]}/{value.AttemptId}";

	private static PolishBaselineRestoration ReadRestoration(
		string path,
		PolishPassKind pass,
		string rejectedPlanSha256,
		bool completed)
	{
		PolishBaselineRestoration value =
			ContractSerializer.Deserialize<PolishBaselineRestoration>(
				File.ReadAllText(path));
		PolishPassContractValidator.Validate(value);
		if (value.RejectedPass != pass ||
			value.Completed != completed ||
			!string.Equals(
				value.RejectedPlanSha256,
				rejectedPlanSha256,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"A baseline-restoration artifact is bound to different polish state.");
		return value;
	}

	private static void ValidateHash(string value, string parameterName)
	{
		if (value == null || value.Length != 64 ||
			value.Any(character => !Uri.IsHexDigit(character)))
			throw new ArgumentException(
				"A 64-character hexadecimal SHA-256 is required.",
				parameterName);
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

	private void QuarantinePreviewChunk(
		PolishPassKind pass,
		int revision,
		int chunkIndex,
		string reason)
	{
		Quarantine(
			pass,
			revision,
			$"{PassRoot(pass)}/revisions/{revision:D4}/preview/chunks/" +
				$"{chunkIndex:D4}.json",
			$"chunk-{chunkIndex:D4}-metadata-{reason}");
		Quarantine(
			pass,
			revision,
			PreviewOutputRelativePath(pass, revision, chunkIndex),
			$"chunk-{chunkIndex:D4}-output-{reason}");
	}

	private void QuarantinePreviewManifest(
		PolishPassKind pass,
		int revision,
		string reason) =>
		Quarantine(
			pass,
			revision,
			$"{PassRoot(pass)}/revisions/{revision:D4}/preview/manifest.json",
			"manifest-" + reason);

	private void Quarantine(
		PolishPassKind pass,
		int revision,
		string relativePath,
		string label)
	{
		string source = paths.Resolve(relativePath);
		if (!File.Exists(source)) return;
		string extension = Path.GetExtension(source);
		string destination = paths.Resolve(
			$"{PassRoot(pass)}/quarantine/revision-{revision:D4}/" +
			$"{label}-{DateTimeOffset.UtcNow.UtcTicks:D19}-" +
			Guid.NewGuid().ToString("N") + extension);
		Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
		File.Move(source, destination);
	}
}
