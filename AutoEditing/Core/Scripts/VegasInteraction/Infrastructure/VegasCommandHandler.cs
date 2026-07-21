using System;
using Newtonsoft.Json;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal abstract class VegasCommandHandler<TCommand> : IVegasCommandHandler
{
	public abstract string CommandType { get; }

	public string Execute(Vegas vegas, string payloadJson)
	{
		TCommand command = JsonConvert.DeserializeObject<TCommand>(payloadJson);
		if (command == null) throw new InvalidOperationException(CommandType + " command payload is empty.");
		Execute(vegas, command);
		return null;
	}

	protected abstract void Execute(Vegas vegas, TCommand command);
}
