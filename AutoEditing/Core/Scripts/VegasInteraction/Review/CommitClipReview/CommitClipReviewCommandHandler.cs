using Core.Domain.Audio;
using ScriptPortal.Vegas;
namespace Core.Scripts;
internal sealed class CommitClipReviewCommandHandler : VegasCommandHandler<CommitClipReviewCommand>
{
	public override string CommandType => "CommitClipReview";
	protected override void Execute(Vegas vegas, CommitClipReviewCommand command) { new ShotReviewWorkflow().MarkClipReady(vegas, command.ClipsFolder, command.SfxRoot, command.ClipIndex, command.ReviewedMarkers); }
}
