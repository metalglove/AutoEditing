namespace Core.Scripts;

internal sealed class PreviewRenderPlan
{
	public PreviewRenderPlan(
		PreviewRendererSelection renderer,
		string outputPath,
		double startSeconds,
		double durationSeconds)
	{
		Renderer = renderer;
		OutputPath = outputPath;
		StartSeconds = startSeconds;
		DurationSeconds = durationSeconds;
	}

	public PreviewRendererSelection Renderer { get; }
	public string OutputPath { get; }
	public double StartSeconds { get; }
	public double DurationSeconds { get; }
}
