using System;
using AutoEditing.Iteration.Contracts.Automation;
using Newtonsoft.Json;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class CleanupCandidateCommandHandler : IVegasCommandHandler
{
	public string CommandType => VegasOperations.CleanupCandidate;

	public string Execute(Vegas vegas, string payloadJson)
	{
		CleanupCandidateCommand command = JsonConvert.DeserializeObject<CleanupCandidateCommand>(payloadJson);
		if (command?.Request?.Workspace == null)
			throw new InvalidOperationException("Candidate cleanup request is empty.");
		command.Request.Workspace.Validate();
		return JsonConvert.SerializeObject(
			CandidateWorkspaceDiscovery.RemoveOwnedTracks(vegas.Project, command.Request.Workspace));
	}
}
