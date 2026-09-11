using System.Text;
using System.Text.Json;

namespace AutoEditing.LlmEditor.Sessions;

internal sealed class SessionEventWriter
{
	private readonly object gate = new();
	private readonly string path;
	private long nextSequence;

	public SessionEventWriter(string path)
	{
		this.path = Path.GetFullPath(path);
		nextSequence = SessionRecovery.ReadEvents(this.path).LastOrDefault()?.Sequence + 1 ?? 1;
	}

	public SessionEvent Append(string eventType, object? payload, DateTimeOffset occurredUtc)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
		lock (gate)
		{
			SessionEvent entry = new(
				nextSequence++,
				eventType,
				occurredUtc,
				JsonSerializer.SerializeToElement(payload));
			string? directory = Path.GetDirectoryName(path);
			if (directory != null) Directory.CreateDirectory(directory);
			string json = JsonSerializer.Serialize(entry) + "\n";
			using FileStream stream = new(
				path,
				FileMode.Append,
				FileAccess.Write,
				FileShare.Read,
				4096,
				FileOptions.WriteThrough);
			byte[] bytes = new UTF8Encoding(false).GetBytes(json);
			stream.Write(bytes, 0, bytes.Length);
			stream.Flush(true);
			return entry;
		}
	}
}

internal sealed record SessionEvent(
	long Sequence,
	string EventType,
	DateTimeOffset OccurredUtc,
	JsonElement Payload);
