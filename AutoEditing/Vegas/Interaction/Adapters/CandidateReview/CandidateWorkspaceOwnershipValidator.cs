using System;
using System.Collections.Generic;
using AutoEditing.Iteration.Contracts.Automation;

namespace Core.Scripts;

internal static class CandidateWorkspaceOwnershipValidator
{
	public static void ValidateOwnedTrackNames(CandidateWorkspaceId workspace, IEnumerable<string> trackNames)
	{
		if (workspace == null) throw new ArgumentNullException(nameof(workspace));
		if (trackNames == null) throw new ArgumentNullException(nameof(trackNames));

		foreach (string trackName in trackNames)
		{
			if (!CandidateTrackNaming.OwnsTrackName(workspace, trackName))
				throw new InvalidOperationException(
					"Refusing candidate cleanup because a recorded track is not owned by workspace '" +
					workspace.OwnershipPrefix + "': '" + (trackName ?? "<null>") + "'.");
		}
	}
}
