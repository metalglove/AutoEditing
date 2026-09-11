using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.LlmEditor.Automation;
using AutoEditing.LlmEditor.Workbench;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Iteration;

internal interface IWorkbenchCandidateReviewer
{
	Task<EditIterationFeedback> ReviewAsync(
		EditPlanningRequest request,
		EditPlanDocument candidate,
		CandidateTimelineSnapshot timeline,
		RenderCandidatePreviewResult preview,
		int iteration,
		CancellationToken cancellationToken);
}

internal sealed record WorkbenchIterationExecutionResult(
	EditPlanDocument Plan,
	int Iterations,
	bool WasAccepted);

/// <summary>Executes the durable plan/materialize/render/review loop without UI ownership.</summary>
internal sealed class WorkbenchIterationExecutionService
{
	private readonly IIterativeEditPlanner planner;
	private readonly IWorkbenchCandidateReviewer reviewer;
	private readonly IVegasAutomationClient automation;
	private readonly ICandidatePreviewService previews;
	private readonly WorkbenchSessionPublisher publisher;
	private readonly Func<int, CandidateWorkspaceId> workspaceFactory;

	public WorkbenchIterationExecutionService(
		IIterativeEditPlanner planner,
		IWorkbenchCandidateReviewer reviewer,
		IVegasAutomationClient automation,
		ICandidatePreviewService previews,
		WorkbenchSessionPublisher publisher,
		Func<int, CandidateWorkspaceId> workspaceFactory)
	{
		this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
		this.reviewer = reviewer ?? throw new ArgumentNullException(nameof(reviewer));
		this.automation = automation ?? throw new ArgumentNullException(nameof(automation));
		this.previews = previews ?? throw new ArgumentNullException(nameof(previews));
		this.publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
		this.workspaceFactory = workspaceFactory ?? throw new ArgumentNullException(nameof(workspaceFactory));
	}

	public async Task<WorkbenchIterationExecutionResult> RunAsync(
		EditPlanningRequest request,
		EditPlanDocument initialPlan,
		int maximumIterations,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(initialPlan);
		if (maximumIterations < 1) throw new ArgumentOutOfRangeException(nameof(maximumIterations));
		EditPlanDocument candidate = initialPlan;

		try
		{
			for (int iteration = 1; iteration <= maximumIterations; iteration++)
			{
				CandidateWorkspaceId workspace = workspaceFactory(iteration);
				workspace.Validate();
				bool materialized = false;
				try
				{
					publisher.TransitionTo(EditSessionState.Validating, $"Validating iteration {iteration}.");
					PreflightCandidateResult preflight =
						await automation.ExecuteAsync<PreflightCandidateRequest, PreflightCandidateResult>(
							VegasOperations.PreflightCandidate,
							new PreflightCandidateRequest { Workspace = workspace, Plan = candidate, SongPath = request.SongPath },
							$"iteration-{iteration:D4}-preflight",
							cancellationToken: cancellationToken);
					if (!preflight.IsReady)
						throw new InvalidOperationException("VEGAS candidate preflight failed: " +
							string.Join("; ", preflight.Errors.Select(error => error.Message)));

					publisher.TransitionTo(EditSessionState.Materializing, $"Materializing iteration {iteration}.");
					MaterializeCandidateResult created =
						await automation.ExecuteAsync<MaterializeCandidateRequest, MaterializeCandidateResult>(
							VegasOperations.MaterializeCandidate,
							new MaterializeCandidateRequest
							{
								Workspace = workspace, Plan = candidate, SongPath = request.SongPath,
								IncludeSong = true, IncludeSfx = true, ApplyEffects = true
							},
							$"iteration-{iteration:D4}-materialize",
							cancellationToken: cancellationToken);
					materialized = true;
					CandidateTimelineSnapshot timeline =
						await automation.ExecuteAsync<GetCandidateSnapshotRequest, CandidateTimelineSnapshot>(
							VegasOperations.GetCandidateSnapshot,
							new GetCandidateSnapshotRequest { Workspace = workspace },
							$"iteration-{iteration:D4}-snapshot",
							cancellationToken: cancellationToken);
					if (created.CreatedEventCount == 0 || timeline.Tracks.Count == 0)
						throw new InvalidOperationException("VEGAS returned an empty candidate after materialization.");
					publisher.PublishTimeline(iteration, timeline);

					publisher.TransitionTo(EditSessionState.Rendering, $"Rendering iteration {iteration} preview.");
					RenderCandidatePreviewResult preview =
						await previews.RenderAsync(workspace, timeline, iteration, cancellationToken);
					publisher.PublishPreview(iteration, preview);
					publisher.TransitionTo(EditSessionState.Reviewing, $"Reviewing iteration {iteration}.");
					EditIterationFeedback feedback = await reviewer.ReviewAsync(
						request, candidate, timeline, preview, iteration, cancellationToken)
						?? throw new InvalidOperationException("The candidate reviewer returned no feedback.");
					await publisher.OnSnapshotAsync(new EditIterationSnapshot
					{
						Iteration = iteration,
						Phase = feedback.IsAccepted ? "accepted" : "revision-requested",
						Candidate = candidate,
						Summary = feedback.Summary,
						Decisions = feedback.Decisions,
						Findings = feedback.Findings,
						Evidence = new[]
						{
							new AutoEditing.Iteration.Contracts.Evidence.EditEvidenceReference
							{
								EvidenceId = $"preview-{iteration:D4}",
								RelativePath = preview.OutputRelativePath,
								MediaType = Path.GetExtension(preview.OutputRelativePath)
									.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
										? "video/mp4"
										: "image/jpeg",
								TimelineStart = timeline.TimelineStart,
								TimelineEnd = timeline.TimelineStart + preview.RenderedDuration,
								Sha256 = preview.Sha256,
								Description = $"preview-{iteration:D4}"
							}
						}
					}, cancellationToken);
					if (feedback.IsAccepted)
					{
						publisher.TransitionTo(EditSessionState.Accepted, $"Iteration {iteration} accepted.");
						return new WorkbenchIterationExecutionResult(candidate, iteration, true);
					}
					if (iteration == maximumIterations)
					{
						publisher.TransitionTo(EditSessionState.AwaitingUser, "Iteration limit reached.");
						return new WorkbenchIterationExecutionResult(candidate, iteration, false);
					}
					publisher.TransitionTo(EditSessionState.Revising, $"Revising after iteration {iteration}.");
					candidate = await planner.RevisePlanAsync(
						request, candidate, feedback, iteration, cancellationToken);
				}
				finally
				{
					if (materialized)
						await CleanupAsync(workspace, iteration);
				}
			}
			throw new InvalidOperationException("The iteration loop ended unexpectedly.");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch
		{
			publisher.TransitionTo(EditSessionState.Failed, "Iteration execution failed.");
			throw;
		}
	}

	private async Task CleanupAsync(CandidateWorkspaceId workspace, int iteration)
	{
		try
		{
			await automation.ExecuteAsync<CleanupCandidateRequest, CleanupCandidateResult>(
				VegasOperations.CleanupCandidate,
				new CleanupCandidateRequest { Workspace = workspace },
				$"iteration-{iteration:D4}-cleanup");
		}
		catch
		{
			// Cleanup is rollback best-effort; the durable workspace ID supports later recovery.
		}
	}
}
