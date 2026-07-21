using System;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class LayoutSongAnalysisCommandHandler : VegasCommandHandler<LayoutSongAnalysisCommand>
{
	public override string CommandType => "LayoutSongAnalysis";

	protected override void Execute(Vegas vegas, LayoutSongAnalysisCommand command)
	{
		if (command.Analysis == null || string.IsNullOrWhiteSpace(command.SongPath))
		{
			throw new InvalidOperationException("Song-analysis layout request is incomplete.");
		}
		new SongReviewWorkflow().LayOut(vegas, command.SongPath, command.Analysis);
	}
}
