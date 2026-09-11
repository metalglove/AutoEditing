using System.Text;

namespace AutoEditing.LlmEditor.Sessions;

internal sealed class AtomicFileWriter
{
	private const int ReplacementAttempts = 20;
	private static readonly TimeSpan ReplacementRetryDelay =
		TimeSpan.FromMilliseconds(25);

	public void WriteText(string path, string content)
	{
		WriteBytes(path, new UTF8Encoding(false).GetBytes(content));
	}

	public void WriteBytes(string path, byte[] content)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(content);

		string fullPath = Path.GetFullPath(path);
		string directory = Path.GetDirectoryName(fullPath)
			?? throw new InvalidOperationException("The destination has no parent directory.");
		Directory.CreateDirectory(directory);

		string temporaryPath = Path.Combine(
			directory,
			"." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
		try
		{
			using (FileStream stream = new FileStream(
				temporaryPath,
				FileMode.CreateNew,
				FileAccess.Write,
				FileShare.None,
				4096,
				FileOptions.WriteThrough))
			{
				stream.Write(content, 0, content.Length);
				stream.Flush(true);
			}

			ReplaceWithRetry(temporaryPath, fullPath);
		}
		finally
		{
			if (File.Exists(temporaryPath))
			{
				File.Delete(temporaryPath);
			}
		}
	}

	private static void ReplaceWithRetry(string temporaryPath, string destinationPath)
	{
		for (int attempt = 1; ; attempt++)
		{
			try
			{
				File.Move(temporaryPath, destinationPath, true);
				return;
			}
			catch (IOException) when (attempt < ReplacementAttempts)
			{
				Thread.Sleep(ReplacementRetryDelay);
			}
			catch (UnauthorizedAccessException) when (attempt < ReplacementAttempts)
			{
				// Windows can report a sharing violation as access denied while the
				// workbench briefly has the previous state file open for reading.
				Thread.Sleep(ReplacementRetryDelay);
			}
		}
	}
}
