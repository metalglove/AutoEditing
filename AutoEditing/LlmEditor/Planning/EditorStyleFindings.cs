namespace AutoEditing.LlmEditor.Planning;

internal static class EditorStyleFindings
{
	private const string RelativePath = "Prompts/editor-style-findings.v1.md";

	public static string LoadDefault()
	{
		string path = Path.Combine(
			AppContext.BaseDirectory,
			"Prompts",
			"editor-style-findings.v1.md");
		if (!File.Exists(path))
			throw new FileNotFoundException(
				"The versioned editor-style findings profile was not deployed.",
				path);
		string findings = File.ReadAllText(path);
		if (string.IsNullOrWhiteSpace(findings))
			throw new InvalidDataException("The editor-style findings profile is empty.");
		return findings;
	}

	public static string ArtifactPath => RelativePath;
}
