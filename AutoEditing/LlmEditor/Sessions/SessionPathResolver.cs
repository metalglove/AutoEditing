namespace AutoEditing.LlmEditor.Sessions;

internal sealed class SessionPathResolver
{
	private readonly string rootWithSeparator;

	public SessionPathResolver(string sessionRoot)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionRoot);
		Root = Path.GetFullPath(sessionRoot);
		rootWithSeparator = Path.TrimEndingDirectorySeparator(Root) + Path.DirectorySeparatorChar;
	}

	public string Root { get; }

	public string Resolve(string relativePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
		if (Path.IsPathRooted(relativePath))
		{
			throw new InvalidDataException("Session artifact paths must be relative.");
		}

		string resolved = Path.GetFullPath(relativePath, Root);
		if (!resolved.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The path escapes the session root.");
		}

		return resolved;
	}

	public string GetRelativePath(string path)
	{
		string fullPath = Path.GetFullPath(path);
		if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The path is outside the session root.");
		}

		return Path.GetRelativePath(Root, fullPath).Replace('\\', '/');
	}
}
