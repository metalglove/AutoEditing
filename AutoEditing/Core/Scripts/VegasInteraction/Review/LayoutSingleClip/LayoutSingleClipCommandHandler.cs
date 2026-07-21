using System;
using Core.Domain.Audio;
using ScriptPortal.Vegas;
namespace Core.Scripts;
internal sealed class LayoutSingleClipCommandHandler : VegasCommandHandler<LayoutSingleClipCommand>
{
	public override string CommandType => "LayoutSingleClip";
	protected override void Execute(Vegas vegas, LayoutSingleClipCommand command)
	{
		if (command.Item == null) throw new InvalidOperationException("Single clip layout request is empty.");
		new ShotReviewWorkflow().LayOutSingleClip(vegas, command.Item);
	}
}
