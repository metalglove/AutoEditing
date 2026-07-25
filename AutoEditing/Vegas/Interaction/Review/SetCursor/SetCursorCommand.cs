namespace Core.Scripts;
internal sealed class SetCursorCommand : IVegasCommand
{
	public string CommandType => "SetCursor";
	public double TimelineSeconds { get; set; }
}
