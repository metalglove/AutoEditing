namespace AutoEditing.LlmEditor.Inference;

internal static class EnvironmentFile
{
	public static string? LoadNearest(string? startDirectory = null)
	{
		DirectoryInfo? directory = new DirectoryInfo(
			Path.GetFullPath(startDirectory ?? Directory.GetCurrentDirectory()));
		while (directory != null)
		{
			string path = Path.Combine(directory.FullName, ".env");
			if (File.Exists(path))
			{
				Load(path);
				return path;
			}
			directory = directory.Parent;
		}
		return null;
	}

	internal static void Load(string path)
	{
		foreach (string sourceLine in File.ReadLines(path))
		{
			string line = sourceLine.Trim();
			if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
				continue;
			int separator = line.IndexOf('=');
			if (separator <= 0)
				throw new InvalidDataException("Invalid .env line: " + sourceLine);
			string name = line[..separator].Trim();
			if (!name.StartsWith("AUTOEDITING_", StringComparison.Ordinal) ||
				name.Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
				throw new InvalidDataException("Unsupported .env variable: " + name);
			if (Environment.GetEnvironmentVariable(name) != null)
				continue;
			string value = Unquote(line[(separator + 1)..].Trim());
			Environment.SetEnvironmentVariable(name, value);
		}
	}

	private static string Unquote(string value)
	{
		if (value.Length >= 2 &&
			((value[0] == '"' && value[^1] == '"') ||
			 (value[0] == '\'' && value[^1] == '\'')))
			return value[1..^1];
		return value;
	}
}
