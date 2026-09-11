using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.LlmEditor.Automation;

namespace AutoEditing.LlmEditor.Iteration;

internal interface ICandidatePreviewService
{
	Task<RenderCandidatePreviewResult> RenderAsync(
		CandidateWorkspaceId workspace,
		CandidateTimelineSnapshot timeline,
		int iteration,
		CancellationToken cancellationToken);
}

internal sealed class VegasCandidatePreviewService : ICandidatePreviewService
{
	private readonly IVegasAutomationClient client;
	private readonly string renderProfileId;

	public VegasCandidatePreviewService(IVegasAutomationClient client, string renderProfileId)
	{
		this.client = client ?? throw new ArgumentNullException(nameof(client));
		this.renderProfileId = string.IsNullOrWhiteSpace(renderProfileId)
			? throw new ArgumentException("A render profile is required.", nameof(renderProfileId))
			: renderProfileId;
	}

	public Task<RenderCandidatePreviewResult> RenderAsync(
		CandidateWorkspaceId workspace,
		CandidateTimelineSnapshot timeline,
		int iteration,
		CancellationToken cancellationToken)
	{
		TimeSpan duration = timeline.TimelineEnd - timeline.TimelineStart;
		if (duration <= TimeSpan.Zero)
			throw new InvalidOperationException("The candidate timeline has no renderable duration.");
		if (duration > TimeSpan.FromSeconds(20)) duration = TimeSpan.FromSeconds(20);
		return client.ExecuteAsync<RenderCandidatePreviewRequest, RenderCandidatePreviewResult>(
			VegasOperations.RenderCandidatePreview,
			new RenderCandidatePreviewRequest
			{
				Workspace = workspace,
				Start = timeline.TimelineStart,
				Duration = duration,
				RenderProfileId = renderProfileId,
				OutputRelativePath = $"iterations/{iteration:D4}/preview.mp4"
			},
			$"iteration-{iteration:D4}-preview",
			cancellationToken: cancellationToken);
	}
}
