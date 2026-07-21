namespace Core.Scripts;

internal sealed class RemoveSongEventCommand : IVegasCommand
{
	public string CommandType => "RemoveSongEvent";

	public string EventId { get; set; }
}
