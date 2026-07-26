using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Iteration;

internal sealed class EditIterationOrchestrator
{
	private readonly IIterativeEditPlanner planner;
	private readonly IEditPlanReviewer reviewer;
	private readonly IEditIterationObserver observer;

	public EditIterationOrchestrator(
		IIterativeEditPlanner planner,
		IEditPlanReviewer reviewer,
		IEditIterationObserver? observer = null)
	{
		this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
		this.reviewer = reviewer ?? throw new ArgumentNullException(nameof(reviewer));
		this.observer = observer ?? new NullEditIterationObserver();
	}

	public async Task<EditIterationResult> RunAsync(
		EditPlanningRequest request,
		int maximumIterations,
		CancellationToken cancellationToken)
	{
		if (maximumIterations < 1)
			throw new ArgumentOutOfRangeException(nameof(maximumIterations));

		EditPlanDocument candidate = await planner.CreatePlanAsync(request, cancellationToken);
		for (int iteration = 1; iteration <= maximumIterations; iteration++)
		{
			EditIterationFeedback feedback = await reviewer.ReviewAsync(
				request,
				candidate,
				iteration,
				cancellationToken) ??
				throw new InvalidOperationException("The edit-plan reviewer returned no feedback.");
			EditPlanDocument snapshotPlan = EditPlanDocumentSerializer.DeserializePlan(
				EditPlanDocumentSerializer.SerializePlan(candidate));
			await observer.OnSnapshotAsync(
				new EditIterationSnapshot
				{
					Iteration = iteration,
					Phase = feedback.IsAccepted ? "accepted" : "revision-requested",
					Candidate = snapshotPlan,
					Summary = feedback.Summary,
					Decisions = feedback.Decisions.ToArray()
				},
				cancellationToken);
			if (feedback.IsAccepted)
			{
				return new EditIterationResult
				{
					Plan = candidate,
					Iterations = iteration,
					WasAccepted = true
				};
			}

			if (iteration < maximumIterations)
			{
				candidate = await planner.RevisePlanAsync(
					request,
					candidate,
					feedback,
					iteration,
					cancellationToken);
			}
		}

		return new EditIterationResult
		{
			Plan = candidate,
			Iterations = maximumIterations,
			WasAccepted = false
		};
	}
}
