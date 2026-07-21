using System;
using Core.Domain.Audio;
using Core.Domain.Editing;
using ScriptPortal.Vegas;
namespace Core.Scripts;
internal sealed class BuildMontageCommandHandler : VegasCommandHandler<BuildMontageCommand>
{
	public override string CommandType => "BuildMontage";
	protected override void Execute(Vegas vegas, BuildMontageCommand command)
	{
		if (command.Montage == null) throw new InvalidOperationException("Montage build request is empty.");
		ShotReviewWorkflow.CleanupGenerated(vegas);
		new MontageOrchestrator().BuildPreparedMontage(vegas, command.Montage, command.SongPath, applyEffects: true);
	}
}
