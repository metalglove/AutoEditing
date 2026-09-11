using System;
using System.Diagnostics;
using System.IO;
using AutoEditing.Iteration.Contracts.Sessions;

namespace Core.Scripts;

internal static class LlmEditorCompanionProcess
{
	public static void Start(string command, string sessionId)
	{
		string companionRoot = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"AutoEditing", "bin", "LlmEditor");
		ProcessStartInfo startInfo = CreateStartInfo(
			companionRoot, command, sessionId);
		if (!File.Exists(startInfo.FileName))
			throw new FileNotFoundException(
				"The LLM editor companion is not deployed. Build the solution using Deploy.",
				startInfo.FileName);
		Process process = Process.Start(startInfo);
		if (process == null)
			throw new InvalidOperationException(
				"The LLM editor companion process could not be started.");
		process.Dispose();
	}

	internal static ProcessStartInfo CreateStartInfo(
		string companionRoot,
		string command,
		string sessionId)
	{
		if (string.IsNullOrWhiteSpace(companionRoot))
			throw new ArgumentException(
				"A companion directory is required.", nameof(companionRoot));
		if (string.IsNullOrWhiteSpace(sessionId))
			throw new ArgumentException("A session ID is required.", nameof(sessionId));
		EditSessionIdValidator.Validate(sessionId);
		if (!string.Equals(command, "workbench-resume", StringComparison.Ordinal) &&
			!string.Equals(command, "workbench-abandon", StringComparison.Ordinal) &&
			!string.Equals(command, "workbench-rollback", StringComparison.Ordinal))
			throw new ArgumentException(
				"Only recovery or rollback commands may be launched.", nameof(command));
		string fullRoot = Path.GetFullPath(companionRoot);
		string arguments =
			command + " --session-id " + QuoteArgument(sessionId);
		if (string.Equals(command, "workbench-resume", StringComparison.Ordinal))
			arguments += " --planner configured";
		return new ProcessStartInfo
		{
			FileName = Path.Combine(fullRoot, "AutoEditing.LlmEditor.exe"),
			Arguments = arguments,
			WorkingDirectory = fullRoot,
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden
		};
	}

	private static string QuoteArgument(string value) =>
		"\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
}
