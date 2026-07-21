using ScriptPortal.Vegas;
namespace Core.Scripts;
internal sealed class SetCursorCommandHandler : VegasCommandHandler<SetCursorCommand>
{
	public override string CommandType => "SetCursor";
	protected override void Execute(Vegas vegas, SetCursorCommand command) { vegas.Transport.CursorPosition = Timecode.FromSeconds(command.TimelineSeconds); }
}
