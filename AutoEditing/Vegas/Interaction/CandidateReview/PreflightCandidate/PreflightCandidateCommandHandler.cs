using System;
using AutoEditing.Iteration.Contracts.Automation;
using Newtonsoft.Json;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class PreflightCandidateCommandHandler : IVegasCommandHandler
{
	public string CommandType => VegasOperations.PreflightCandidate;

	public string Execute(Vegas vegas, string payloadJson)
	{
		PreflightCandidateCommand command = JsonConvert.DeserializeObject<PreflightCandidateCommand>(payloadJson);
		if (command?.Request == null) throw new InvalidOperationException("Candidate preflight request is empty.");
		return JsonConvert.SerializeObject(CandidateResourcePreflight.Run(command.Request));
	}
}
