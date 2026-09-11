using System.Text.Json;

namespace AutoEditing.LlmEditor.Sessions;

internal static class SessionRecovery
{
	public static IReadOnlyList<SessionEvent> ReadEvents(string path)
	{
		if (!File.Exists(path)) return Array.Empty<SessionEvent>();

		List<SessionEvent> events = new();
		long expectedSequence = 1;
		int lineNumber = 0;
		foreach (string line in File.ReadLines(path))
		{
			lineNumber++;
			if (string.IsNullOrWhiteSpace(line)) continue;
			SessionEvent? entry;
			try
			{
				entry = JsonSerializer.Deserialize<SessionEvent>(line);
			}
			catch (JsonException exception)
			{
				throw new InvalidDataException(
					$"Session event log is corrupt at line {lineNumber}.",
					exception);
			}
			if (entry == null || entry.Sequence != expectedSequence)
			{
				throw new InvalidDataException(
					$"Session event sequence is invalid at line {lineNumber}; expected {expectedSequence}.");
			}
			events.Add(entry);
			expectedSequence++;
		}
		return events;
	}

	public static IReadOnlyList<ArtifactIntegrityResult> VerifyArtifacts(
		SessionPathResolver paths,
		IEnumerable<SessionArtifactReference> artifacts,
		SessionArtifactHasher? hasher = null)
	{
		hasher ??= new SessionArtifactHasher();
		return artifacts
			.Select(artifact => hasher.Verify(paths.Resolve(artifact.RelativePath), artifact.Sha256))
			.ToArray();
	}
}

internal sealed record SessionArtifactReference(string RelativePath, string Sha256);
