using Core.Domain.Audio;
namespace Core.Scripts;
internal sealed class LayoutAnalysisCommand : IVegasCommand
{
	public string CommandType => "LayoutAnalysis";
	public ShotReviewWorkflow.AnalysisBatch Analysis { get; set; }
}
