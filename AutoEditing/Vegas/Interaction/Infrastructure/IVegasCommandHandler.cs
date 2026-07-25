using ScriptPortal.Vegas;

namespace Core.Scripts;

internal interface IVegasCommandHandler
{
	string CommandType { get; }
	string Execute(Vegas vegas, string payloadJson);
}
