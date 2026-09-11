using System;
using System.Collections.Generic;
using System.Linq;
using AutoEditing.Iteration.Contracts.Automation;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal static class CandidateWorkspaceDiscovery
{
	public static List<Track> FindOwnedTracks(Project project, CandidateWorkspaceId workspace)
	{
		if (project == null) throw new ArgumentNullException(nameof(project));
		if (workspace == null) throw new ArgumentNullException(nameof(workspace));
		workspace.Validate();
		return ((IEnumerable<Track>)project.Tracks)
			.Where(track => CandidateTrackNaming.OwnsTrackName(workspace, track.Name))
			.OrderBy(track => track.Index)
			.ToList();
	}

	public static CleanupCandidateResult RemoveOwnedTracks(Project project, CandidateWorkspaceId workspace)
	{
		List<Track> tracks = FindOwnedTracks(project, workspace);
		CandidateWorkspaceOwnershipValidator.ValidateOwnedTrackNames(workspace, tracks.Select(track => track.Name));
		int eventCount = tracks.Sum(track => ((IEnumerable<TrackEvent>)track.Events).Count());
		for (int index = tracks.Count - 1; index >= 0; index--)
			((BaseList<Track>)(object)project.Tracks).Remove(tracks[index]);
		return new CleanupCandidateResult { RemovedTrackCount = tracks.Count, RemovedEventCount = eventCount };
	}
}
