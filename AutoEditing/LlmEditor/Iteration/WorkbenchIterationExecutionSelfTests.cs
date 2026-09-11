using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.LlmEditor.Automation;
using AutoEditing.LlmEditor.Inference;
using AutoEditing.LlmEditor.Workbench;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Iteration;

internal static class WorkbenchIterationExecutionSelfTests
{
	public static void Run(string root, EditPlanningRequest request, EditPlanDocument plan)
	{
		string sessions = Path.Combine(root, "execution-sessions");
		WorkbenchSessionPublisher publisher = WorkbenchSessionPublisher.Create(sessions, "execution-test");
		publisher.TransitionTo(EditSessionState.Planning, "test");
		FakeAutomation automation = new();
		WorkbenchIterationExecutionService service = new(
			new FakePlanner(plan),
			new AcceptingReviewer(),
			automation,
			new FakePreview(),
			publisher,
			iteration => new CandidateWorkspaceId
			{
				SessionId = "execution-test", Iteration = iteration, Nonce = "test"
			});
		WorkbenchIterationExecutionResult result = service.RunAsync(
			request, plan, 2, CancellationToken.None).GetAwaiter().GetResult();
		Assert(result.WasAccepted && result.Iterations == 1, "Execution did not accept the first iteration.");
		Assert(automation.Operations.SequenceEqual(new[]
		{
			VegasOperations.PreflightCandidate, VegasOperations.MaterializeCandidate,
			VegasOperations.GetCandidateSnapshot, VegasOperations.CleanupCandidate
		}), "Execution operations or cleanup order changed.");
		Assert(File.Exists(Path.Combine(publisher.SessionRoot, "iterations", "0001", "timeline.json")),
			"Timeline snapshot was not persisted.");
		Assert(File.Exists(Path.Combine(publisher.SessionRoot, "iterations", "0001", "preview.json")),
			"Preview request result was not persisted.");
		EditSessionManifest manifest = ContractSerializer.Deserialize<EditSessionManifest>(
			File.ReadAllText(Path.Combine(publisher.SessionRoot, "manifest.json")));
		Assert(manifest.State == EditSessionState.Accepted && manifest.CurrentIteration == 1,
			"Accepted session state was not persisted.");

		// Re-running the same iteration must reach VEGAS again for snapshot and cleanup;
		// only the plan-bound preflight and materialize keys stay stable.
		WorkbenchSessionPublisher retryPublisher = WorkbenchSessionPublisher.Create(sessions, "execution-retry");
		retryPublisher.TransitionTo(EditSessionState.Planning, "test");
		new WorkbenchIterationExecutionService(
			new FakePlanner(plan),
			new AcceptingReviewer(),
			automation,
			new FakePreview(),
			retryPublisher,
			iteration => new CandidateWorkspaceId
			{
				SessionId = "execution-test", Iteration = iteration, Nonce = "test"
			}).RunAsync(request, plan, 2, CancellationToken.None).GetAwaiter().GetResult();
		Assert(automation.KeysFor(VegasOperations.GetCandidateSnapshot).Distinct().Count() == 2,
			"A re-run iteration reused the snapshot idempotency key and would replay a stale timeline.");
		Assert(automation.KeysFor(VegasOperations.CleanupCandidate).Distinct().Count() == 2,
			"A re-run iteration reused the cleanup idempotency key and would leave candidate tracks behind.");
		Assert(automation.KeysFor(VegasOperations.MaterializeCandidate).Distinct().Count() == 1,
			"Materialization lost its stable idempotency key.");
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private sealed class FakeAutomation : IVegasAutomationClient
	{
		public List<string> Operations { get; } = new();
		private readonly List<string> keys = new();

		public IEnumerable<string> KeysFor(string operation) =>
			Operations.Select((item, index) => (item, index))
				.Where(entry => entry.item == operation)
				.Select(entry => keys[entry.index]);

		public Task<TResult> ExecuteAsync<TRequest, TResult>(
			string operation, TRequest request, string idempotencyKey,
			TimeSpan? timeout = null, CancellationToken cancellationToken = default)
		{
			Operations.Add(operation);
			keys.Add(idempotencyKey);
			object result = operation switch
			{
				VegasOperations.PreflightCandidate => new PreflightCandidateResult { IsReady = true },
				VegasOperations.MaterializeCandidate => new MaterializeCandidateResult
				{
					CreatedEventCount = 1,
					CreatedTrackNames = new[] { "candidate" }
				},
				VegasOperations.GetCandidateSnapshot => new CandidateTimelineSnapshot
				{
					Workspace = ((GetCandidateSnapshotRequest)(object)request!).Workspace,
					TimelineStart = TimeSpan.Zero,
					TimelineEnd = TimeSpan.FromSeconds(5),
					Tracks = new[] { new CandidateTrackSnapshot { Name = "candidate" } }
				},
				VegasOperations.CleanupCandidate => new CleanupCandidateResult { RemovedTrackCount = 1 },
				_ => throw new InvalidOperationException("Unexpected fake operation: " + operation)
			};
			return Task.FromResult((TResult)result);
		}
	}

	private sealed class FakePreview : ICandidatePreviewService
	{
		public Task<RenderCandidatePreviewResult> RenderAsync(
			CandidateWorkspaceId workspace, CandidateTimelineSnapshot timeline, int iteration,
			CancellationToken cancellationToken) =>
			Task.FromResult(new RenderCandidatePreviewResult
			{
				OutputRelativePath = $"iterations/{iteration:D4}/preview.mp4",
				Sha256 = new string('a', 64),
				RenderedDuration = TimeSpan.FromSeconds(5),
				RenderProfileId = "test"
			});
	}

	private sealed class AcceptingReviewer : IWorkbenchCandidateReviewer
	{
		public Task<EditIterationFeedback> ReviewAsync(
			EditPlanningRequest request, EditPlanDocument candidate,
			CandidateTimelineSnapshot timeline, RenderCandidatePreviewResult preview,
			int iteration, CancellationToken cancellationToken) =>
			Task.FromResult(new EditIterationFeedback
			{
				IsAccepted = true,
				Summary = "accepted",
				Decisions = new[] { new EditDecisionRecord
				{
					DecisionId = "test", Category = "review", Summary = "accepted"
				} }
			});
	}

	private sealed class FakePlanner : IIterativeEditPlanner
	{
		private readonly EditPlanDocument plan;
		public FakePlanner(EditPlanDocument plan) => this.plan = plan;
		public Task<EditPlanDocument> CreatePlanAsync(
			EditPlanningRequest request, CancellationToken cancellationToken) => Task.FromResult(plan);
		public Task<EditPlanDocument> RevisePlanAsync(
			EditPlanningRequest request, EditPlanDocument previousPlan,
			EditIterationFeedback feedback, int iteration, CancellationToken cancellationToken) =>
			Task.FromResult(plan);
	}
}
