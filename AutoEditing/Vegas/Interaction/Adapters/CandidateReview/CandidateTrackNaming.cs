using System;
using System.Globalization;
using AutoEditing.Iteration.Contracts.Automation;

namespace Core.Scripts;

internal static class CandidateTrackNaming
{
	public static string Video(CandidateWorkspaceId workspace)
	{
		Validate(workspace);
		return workspace.OwnershipPrefix + "|VIDEO";
	}

	public static string Song(CandidateWorkspaceId workspace)
	{
		Validate(workspace);
		return workspace.OwnershipPrefix + "|SONG";
	}

	public static string Sfx(CandidateWorkspaceId workspace, int oneBasedIndex)
	{
		Validate(workspace);
		if (oneBasedIndex < 1) throw new ArgumentOutOfRangeException(nameof(oneBasedIndex));
		return workspace.OwnershipPrefix + "|SFX|" +
			oneBasedIndex.ToString("D2", CultureInfo.InvariantCulture);
	}

	public static bool OwnsTrackName(CandidateWorkspaceId workspace, string trackName)
	{
		Validate(workspace);
		return !string.IsNullOrEmpty(trackName) &&
			trackName.StartsWith(workspace.OwnershipPrefix + "|", StringComparison.Ordinal);
	}

	private static void Validate(CandidateWorkspaceId workspace)
	{
		if (workspace == null) throw new ArgumentNullException(nameof(workspace));
		workspace.Validate();
	}
}
