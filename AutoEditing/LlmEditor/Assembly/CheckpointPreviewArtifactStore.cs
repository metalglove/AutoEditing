using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Sessions;

namespace AutoEditing.LlmEditor.Assembly;

internal sealed class CheckpointPreviewArtifactStore
{
	private readonly SessionPathResolver paths;
	private readonly AtomicFileWriter writer = new();

	public CheckpointPreviewArtifactStore(string sessionRoot)
	{
		paths = new SessionPathResolver(sessionRoot);
	}

	public int NextAttempt(int checkpoint)
	{
		if (checkpoint < 1) throw new ArgumentOutOfRangeException(nameof(checkpoint));
		string root = paths.Resolve($"assembly/checkpoints/{checkpoint:D4}/previews");
		if (!Directory.Exists(root)) return 1;
		int latest = Directory.EnumerateDirectories(root)
			.Select(Path.GetFileName)
			.Select(name => int.TryParse(name, out int value) ? value : 0)
			.DefaultIfEmpty(0)
			.Max();
		return checked(latest + 1);
	}

	public void SaveArtifact(CheckpointPreviewArtifact artifact)
	{
		CheckpointPreviewContractValidator.Validate(artifact);
		string json = ContractSerializer.Serialize(artifact);
		writer.WriteText(
			ResolveAttempt(artifact.Checkpoint, artifact.Attempt, "preview-artifact.json"),
			json);
		writer.WriteText(
			paths.Resolve(
				$"assembly/checkpoints/{artifact.Checkpoint:D4}/previews/current.json"),
			json);
	}

	public CheckpointPreviewArtifact? ReadCurrent(int checkpoint)
	{
		if (checkpoint < 1) throw new ArgumentOutOfRangeException(nameof(checkpoint));
		string path = paths.Resolve(
			$"assembly/checkpoints/{checkpoint:D4}/previews/current.json");
		if (!File.Exists(path)) return null;
		CheckpointPreviewArtifact artifact =
			ContractSerializer.Deserialize<CheckpointPreviewArtifact>(File.ReadAllText(path));
		CheckpointPreviewContractValidator.Validate(artifact);
		return artifact;
	}

	public string SaveTimeline(
		int checkpoint,
		int attempt,
		CandidateTimelineSnapshot timeline)
	{
		ArgumentNullException.ThrowIfNull(timeline);
		string relative = RelativeAttempt(checkpoint, attempt, "timeline.json");
		writer.WriteText(paths.Resolve(relative), ContractSerializer.Serialize(timeline));
		return relative;
	}

	public string SaveTimingSidecar(
		int checkpoint,
		int attempt,
		CheckpointTimingSidecar sidecar)
	{
		ArgumentNullException.ThrowIfNull(sidecar);
		string relative = RelativeAttempt(checkpoint, attempt, "timing.json");
		writer.WriteText(paths.Resolve(relative), ContractSerializer.Serialize(sidecar));
		return relative;
	}

	public string SaveTimingVisualization(int checkpoint, int attempt, byte[] png)
	{
		if (png == null || png.Length == 0)
			throw new ArgumentException("Timing visualization is required.", nameof(png));
		string relative = RelativeAttempt(checkpoint, attempt, "timing.png");
		writer.WriteBytes(paths.Resolve(relative), png);
		return relative;
	}

	public string SaveContactSheetManifest(
		int checkpoint,
		int attempt,
		CheckpointContactSheetManifest manifest)
	{
		ArgumentNullException.ThrowIfNull(manifest);
		string relative = RelativeAttempt(checkpoint, attempt, "contact-sheet.json");
		writer.WriteText(paths.Resolve(relative), ContractSerializer.Serialize(manifest));
		return relative;
	}

	public string SaveReviewReport(
		int checkpoint,
		int attempt,
		CheckpointReviewReport report)
	{
		CheckpointPreviewContractValidator.Validate(report);
		string relative = RelativeAttempt(checkpoint, attempt, "review.json");
		writer.WriteText(paths.Resolve(relative), ContractSerializer.Serialize(report));
		return relative;
	}

	public string Resolve(string relativePath) => paths.Resolve(relativePath);

	public static string PreviewRelativePath(int checkpoint, int attempt) =>
		RelativeAttempt(checkpoint, attempt, "preview.mp4");

	private string ResolveAttempt(int checkpoint, int attempt, string fileName) =>
		paths.Resolve(RelativeAttempt(checkpoint, attempt, fileName));

	private static string RelativeAttempt(int checkpoint, int attempt, string fileName)
	{
		if (checkpoint < 1) throw new ArgumentOutOfRangeException(nameof(checkpoint));
		if (attempt < 1) throw new ArgumentOutOfRangeException(nameof(attempt));
		if (string.IsNullOrWhiteSpace(fileName))
			throw new ArgumentException("Artifact file name is required.", nameof(fileName));
		return $"assembly/checkpoints/{checkpoint:D4}/previews/{attempt:D4}/{fileName}";
	}
}
