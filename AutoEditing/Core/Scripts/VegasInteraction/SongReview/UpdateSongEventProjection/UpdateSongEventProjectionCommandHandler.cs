using System;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class UpdateSongEventProjectionCommandHandler : VegasCommandHandler<UpdateSongEventProjectionCommand>
{
	public override string CommandType => "UpdateSongEventProjection";

	protected override void Execute(Vegas vegas, UpdateSongEventProjectionCommand command)
	{
		if (command.Analysis == null) throw new InvalidOperationException("Song-event projection has no analysis.");
		new SongReviewWorkflow().UpdateEventMarkers(vegas, command.Analysis, command.EventIds);
	}
}
