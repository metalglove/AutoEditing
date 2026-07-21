using Core.Domain.Audio;
namespace Core.Scripts;
internal sealed class LayoutSingleClipCommand : IVegasCommand
{
	public string CommandType => "LayoutSingleClip";
	public ShotReviewWorkflow.AnalysisItem Item { get; set; }
}
