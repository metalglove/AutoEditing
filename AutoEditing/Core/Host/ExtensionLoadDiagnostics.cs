using System;
using System.IO;

namespace Core.Scripts;

internal static class ExtensionLoadDiagnostics
{
	private static readonly string LogPath = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"AutoEditing",
		"extension-loader.log");

	public static void Write(string message)
	{
		try
		{
			string directory = Path.GetDirectoryName(LogPath);
			if (!string.IsNullOrWhiteSpace(directory))
				Directory.CreateDirectory(directory);
			File.AppendAllText(
				LogPath,
				DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
		}
		catch
		{
			// Loader diagnostics must never prevent VEGAS from loading the module.
		}
	}
}
