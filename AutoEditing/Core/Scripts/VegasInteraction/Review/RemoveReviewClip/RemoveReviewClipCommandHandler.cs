using Core.Domain.Audio;
using ScriptPortal.Vegas;
namespace Core.Scripts;
internal sealed class RemoveReviewClipCommandHandler : VegasCommandHandler<RemoveReviewClipCommand>
{
	public override string CommandType => "RemoveReviewClip";
	protected override void Execute(Vegas vegas, RemoveReviewClipCommand command) { new ShotReviewWorkflow().RemoveClipFromTimeline(vegas, command.ClipIndex); }
}
