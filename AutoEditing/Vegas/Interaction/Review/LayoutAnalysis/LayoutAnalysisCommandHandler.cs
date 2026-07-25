using System;
using System.Threading;
using Core.Domain.Audio;
using ScriptPortal.Vegas;
namespace Core.Scripts;
internal sealed class LayoutAnalysisCommandHandler : VegasCommandHandler<LayoutAnalysisCommand>
{
	public override string CommandType => "LayoutAnalysis";
	protected override void Execute(Vegas vegas, LayoutAnalysisCommand command)
	{
		if (command.Analysis == null) throw new InvalidOperationException("Analysis layout request is empty.");
		new ShotReviewWorkflow().LayOutAnalysis(vegas, command.Analysis, null, CancellationToken.None);
	}
}
