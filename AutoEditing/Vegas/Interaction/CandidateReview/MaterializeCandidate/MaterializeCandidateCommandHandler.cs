using System;
using System.Linq;
using AutoEditing.Iteration.Contracts.Automation;
using Newtonsoft.Json;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class MaterializeCandidateCommandHandler : IVegasCommandHandler
{
	public string CommandType => VegasOperations.MaterializeCandidate;

	public string Execute(Vegas vegas, string payloadJson)
	{
		MaterializeCandidateCommand command = JsonConvert.DeserializeObject<MaterializeCandidateCommand>(payloadJson);
		MaterializeCandidateRequest request = command?.Request;
		if (request == null) throw new InvalidOperationException("Candidate materialization request is empty.");

		// Every potentially failing resource check happens before the first project mutation.
		CandidateResourcePreflight.ValidateReady(request);
		if (CandidateWorkspaceDiscovery.FindOwnedTracks(vegas.Project, request.Workspace).Count != 0)
			throw new InvalidOperationException(
				"Candidate workspace already exists. Cleanup the exact workspace before materializing it again.");

		MontageBuildContext context = MontageBuildContext.Candidate(
			request.Workspace,
			request.ApplyEffects,
			request.IncludeSong,
			request.IncludeSfx);
		MontageBuildArtifacts artifacts = null;
		try
		{
			artifacts = new MontageOrchestrator().BuildPreparedMontage(
				vegas,
				request.Plan.Montage,
				request.SongPath,
				context);
			CandidateWorkspaceOwnershipValidator.ValidateOwnedTrackNames(
				request.Workspace,
				artifacts.CreatedTracks.Select(track => track.Name));

			CandidateTimelineSnapshot snapshot = CandidateSnapshotReader.Read(vegas.Project, request.Workspace);
			return JsonConvert.SerializeObject(new MaterializeCandidateResult
			{
				Workspace = request.Workspace,
				CreatedTrackNames = artifacts.CreatedTracks.Select(track => track.Name)
					.OrderBy(name => name, StringComparer.Ordinal)
					.ToList(),
				CreatedEventCount = artifacts.CreatedEvents.Count,
				Snapshot = snapshot
			});
		}
		catch
		{
			if (artifacts != null)
				new CandidateWorkspaceCleaner().RemoveRecordedArtifacts(vegas.Project, artifacts);
			throw;
		}
	}
}
