using System;
using System.Collections.Generic;
using System.Linq;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class CandidateWorkspaceCleaner
{
	public void RemoveRecordedArtifacts(Project project, MontageBuildArtifacts artifacts)
	{
		if (project == null) throw new ArgumentNullException(nameof(project));
		if (artifacts == null) throw new ArgumentNullException(nameof(artifacts));
		if (!artifacts.Context.IsCandidate || artifacts.Context.Workspace == null)
			throw new InvalidOperationException("Only a candidate artifact manifest can be cleaned by the candidate workspace cleaner.");

		CandidateWorkspaceOwnershipValidator.ValidateOwnedTrackNames(
			artifacts.Context.Workspace,
			artifacts.CreatedTracks.Select(track => track?.Name));

		HashSet<Track> projectTracks = new HashSet<Track>(((IEnumerable<Track>)project.Tracks));
		for (int index = artifacts.CreatedTracks.Count - 1; index >= 0; index--)
		{
			Track track = artifacts.CreatedTracks[index];
			if (track != null && projectTracks.Contains(track))
				((BaseList<Track>)(object)project.Tracks).Remove(track);
		}
	}
}
