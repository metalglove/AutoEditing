using System;
using AutoEditing.Iteration.Contracts.Automation;
using Newtonsoft.Json;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class GetCandidateSnapshotCommandHandler : IVegasCommandHandler
{
	public string CommandType => VegasOperations.GetCandidateSnapshot;

	public string Execute(Vegas vegas, string payloadJson)
	{
		GetCandidateSnapshotCommand command = JsonConvert.DeserializeObject<GetCandidateSnapshotCommand>(payloadJson);
		if (command?.Request?.Workspace == null)
			throw new InvalidOperationException("Candidate snapshot request is empty.");
		command.Request.Workspace.Validate();
		return JsonConvert.SerializeObject(CandidateSnapshotReader.Read(vegas.Project, command.Request.Workspace));
	}
}
