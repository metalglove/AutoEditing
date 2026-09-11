using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.LlmEditor.Assembly;
using AutoEditing.LlmEditor.Automation;
using AutoEditing.LlmEditor.Workbench;
using Core.Domain.Planning;
using System.Security.Cryptography;
using System.Text;

namespace AutoEditing.LlmEditor.Finalization;

/// <summary>
/// Owns the durable final-review boundary. Promotion is never implicit: only
/// the exact state-revision-bound FinalizeMontage action may invoke the
/// finalization transaction.
/// </summary>
internal sealed class PostPolishFinalizationCoordinator
{
	private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
	private readonly string sessionId;
	private readonly string sessionRoot;
	private readonly WorkbenchSessionPublisher publisher;
	private readonly IVegasAutomationClient automation;
	private readonly AssemblyActionStore actions;
	private readonly AssemblyActionExecutionStore actionExecutions;
	private readonly IFinalizationExecutor finalization;

	public PostPolishFinalizationCoordinator(
		string sessionId,
		string sessionRoot,
		IVegasAutomationClient automation,
		WorkbenchSessionPublisher publisher,
		IFinalizationExecutor finalization)
	{
		this.sessionId = string.IsNullOrWhiteSpace(sessionId)
			? throw new ArgumentException("A session ID is required.", nameof(sessionId))
			: sessionId;
		this.sessionRoot = Path.GetFullPath(
			string.IsNullOrWhiteSpace(sessionRoot)
				? throw new ArgumentException(
					"A session root is required.",
					nameof(sessionRoot))
				: sessionRoot);
		this.publisher = publisher ??
			throw new ArgumentNullException(nameof(publisher));
		this.automation = automation ??
			throw new ArgumentNullException(nameof(automation));
		this.finalization = finalization ??
			throw new ArgumentNullException(nameof(finalization));
		actions = new AssemblyActionStore(this.sessionRoot);
		actionExecutions = new AssemblyActionExecutionStore(this.sessionRoot);
	}

	public async Task<FinalizationResult?> RunAsync(
		FinalizationRequest request,
		CancellationToken cancellationToken,
		AssemblyRuntimeLease? runtimeLease = null)
	{
		ArgumentNullException.ThrowIfNull(request);
		using AssemblyRuntimeLease? ownedLease = runtimeLease == null
			? AssemblyRuntimeLease.Acquire(sessionRoot, sessionId)
			: null;
		runtimeLease ??= ownedLease;
		string finalPlanSha256 = PlanSha256(request.FinalPlan);

		string finalizationRoot = Path.Combine(sessionRoot, "finalization");
		string reportPath =
			Path.Combine(finalizationRoot, "final-session-report.json");
		string promotionIntentPath =
			Path.Combine(finalizationRoot, "promotion-intent.json");
		string rollbackResultPath =
			Path.Combine(finalizationRoot, "rollback-result.json");
		if (File.Exists(rollbackResultPath))
		{
			RequireRecovery(
				"The prior promotion was explicitly rolled back.");
			throw new InvalidOperationException(
				"The previous finalization was rolled back. Start a new " +
				"finalization transaction after reviewing the candidate.");
		}
		AssemblySessionState review = actions.ReadState() ??
			throw new InvalidDataException(
				"The final-review assembly state is missing.");
		if (review.Phase == AssemblyPhase.Abandoned)
			await ReconcileAbandonedAsync(
				review,
				request,
				finalPlanSha256,
				cancellationToken);
		if (File.Exists(promotionIntentPath) || File.Exists(reportPath))
		{
			AssemblyActionExecutionStart? finalizationExecution =
				actionExecutions.ReadUniqueExecution(
					sessionId,
					AssemblyActionKind.FinalizeMontage);
			if (finalizationExecution == null)
				throw new InvalidDataException(
					"A persisted finalization transaction has no matching " +
					"FinalizeMontage action execution.");
			request = ApplyFinalizationPreference(
				request,
				finalizationExecution.Action);
			RequireFinalizationBinding(
				finalizationExecution,
				finalPlanSha256,
				FinalizationRequestSha256(request, finalPlanSha256));
			if (publisher.State == EditSessionState.NeedsRecovery)
				publisher.TransitionTo(
					EditSessionState.FinalReview,
					"Resuming a persisted finalization transaction.");
			if (publisher.State != EditSessionState.Promoting &&
				publisher.State != EditSessionState.Completed)
				publisher.TransitionTo(
					EditSessionState.Promoting,
					"Recovering the exact persisted finalization transaction.");
			FinalizationResult recovered = await finalization.FinalizeAsync(
				request,
				cancellationToken);
			if (recovered?.Report == null ||
				!string.Equals(
					recovered.Report.SessionId,
					sessionId,
					StringComparison.Ordinal))
				throw new InvalidDataException(
					"The recovered final report belongs to another session.");
			PublishCompleted(
				request.Workspace,
				"The persisted finalization transaction and report were verified.");
			if (!actionExecutions.IsComplete(
				finalizationExecution.Action.ActionId))
				actionExecutions.Complete(
					finalizationExecution.Action,
					"finalization-recovered",
					"finalization/final-session-report.json");
			return recovered;
		}

		review = await ResumePersistedPauseAsync(
			review,
			cancellationToken,
			finalPlanSha256,
			request);
		AssemblyAction? promoteAction = null;
		while (true)
		{
			AssemblyAction action = await WaitAsync(
				review,
				cancellationToken,
				finalPlanSha256,
				request);
			switch (action.Kind)
			{
				case AssemblyActionKind.FinalizeMontage:
					request = ApplyFinalizationPreference(request, action);
					promoteAction = action;
					goto Promote;
				case AssemblyActionKind.AbandonSession:
					await AbandonAsync(
						review,
						action,
						"Final review explicitly abandoned.",
						cancellationToken);
					break;
				case AssemblyActionKind.PauseSession:
					review = await PauseAsync(
						review,
						action,
						cancellationToken,
						finalPlanSha256,
						request);
					break;
				default:
					review = Publish(
						review,
						AssemblyPhase.FinalReview,
						"Choose Finalize montage, Pause, or Abandon.");
					actionExecutions.Complete(
						action,
						"unsupported-final-review-action");
					break;
			}
		}

	Promote:
		publisher.TransitionTo(
			EditSessionState.Promoting,
			"Final review accepted; validating and promoting the exact candidate.");
		review = Publish(
			review,
			AssemblyPhase.FinalReview,
			"Validating the exact candidate snapshot and promoting owned tracks.");
		FinalizationResult result = await finalization.FinalizeAsync(
			request,
			cancellationToken);
		if (result?.Report == null ||
			!string.Equals(
				result.Report.SessionId,
				sessionId,
				StringComparison.Ordinal))
			throw new InvalidDataException(
				"The final report belongs to another session.");
		actionExecutions.Complete(
			promoteAction ??
				throw new InvalidDataException(
					"The finalization action journal is missing."),
			"montage-finalized",
			"finalization/final-session-report.json");
		PublishCompleted(
			request.Workspace,
			"Montage promoted. Final report, archive, and rollback bundle are available.");
		return result;
	}

	private async Task<AssemblySessionState> PauseAsync(
		AssemblySessionState review,
		AssemblyAction pauseAction,
		CancellationToken cancellationToken,
		string finalPlanSha256,
		FinalizationRequest request)
	{
		publisher.TransitionTo(
			EditSessionState.Paused,
			"Final review paused at a durable boundary.");
		AssemblySessionState paused = Publish(
			review,
			AssemblyPhase.Paused,
			"Final review paused. Resume or abandon the session.");
		actionExecutions.Complete(pauseAction, "final-review-paused");
		while (true)
		{
			AssemblyAction action = await WaitAsync(
				paused,
				cancellationToken,
				finalPlanSha256,
				request);
			if (action.Kind == AssemblyActionKind.ResumeSession)
			{
				publisher.TransitionTo(
					EditSessionState.FinalReview,
					"Final review resumed.");
				AssemblySessionState resumed = Publish(
					paused,
					AssemblyPhase.FinalReview,
					"Review the complete candidate, then explicitly finalize.");
				actionExecutions.Complete(action, "final-review-resumed");
				return resumed;
			}
			if (action.Kind == AssemblyActionKind.AbandonSession)
				await AbandonAsync(
					paused,
					action,
					"Final review abandoned while paused.",
					cancellationToken);
			paused = Publish(
				paused,
				AssemblyPhase.Paused,
				"Only Resume or Abandon is available while paused.");
			actionExecutions.Complete(action, "ignored-while-final-review-paused");
		}
	}

	private async Task<AssemblySessionState> ResumePersistedPauseAsync(
		AssemblySessionState current,
		CancellationToken cancellationToken,
		string finalPlanSha256,
		FinalizationRequest request)
	{
		AssemblyActionExecutionStart? pending =
			actionExecutions.ReadPendingForRecovery(sessionId);
		if (current.Phase != AssemblyPhase.Paused)
		{
			if (pending?.Action.Kind == AssemblyActionKind.ResumeSession &&
				pending.Phase == AssemblyPhase.Paused &&
				pending.Action.ExpectedStateRevision < current.StateRevision)
				actionExecutions.Complete(
					pending.Action,
					"recovered-reflected-final-review-resume");
			return current;
		}

		if (pending?.Action.Kind == AssemblyActionKind.PauseSession &&
			pending.Action.Checkpoint == current.Checkpoint &&
			pending.Action.ExpectedStateRevision < current.StateRevision)
		{
			actionExecutions.Complete(
				pending.Action,
				"recovered-reflected-final-review-pause");
		}

		while (true)
		{
			AssemblyAction action = await WaitAsync(
				current,
				cancellationToken,
				finalPlanSha256,
				request);
			if (action.Kind == AssemblyActionKind.ResumeSession)
			{
				if (publisher.State == EditSessionState.Paused)
					publisher.TransitionTo(
						EditSessionState.FinalReview,
						"Final review resumed from its persisted pause.");
				AssemblySessionState resumed = Publish(
					current,
					AssemblyPhase.FinalReview,
					"Review the complete candidate, then explicitly finalize.");
				actionExecutions.Complete(
					action,
					"final-review-resumed");
				return resumed;
			}
			if (action.Kind == AssemblyActionKind.AbandonSession)
				await AbandonAsync(
					current,
					action,
					"Final review abandoned from its persisted pause.",
					cancellationToken);
			actionExecutions.Complete(
				action,
				"ignored-while-final-review-paused");
		}
	}

	private async Task<AssemblyAction> WaitAsync(
		AssemblySessionState state,
		CancellationToken cancellationToken,
		string finalPlanSha256,
		FinalizationRequest request)
	{
		AssemblyActionExecutionStart? pending =
			actionExecutions.ReadPendingForRecovery(sessionId);
		if (pending != null)
		{
			FinalizationRequest boundRequest =
				RequestForAction(request, pending.Action);
			RequireFinalizationBinding(
				pending,
				finalPlanSha256,
				FinalizationRequestSha256(
					boundRequest,
					finalPlanSha256));
			bool exact =
				pending.Action.Checkpoint == state.Checkpoint &&
				pending.Action.ExpectedStateRevision == state.StateRevision &&
				pending.Phase == state.Phase;
			bool recoverableFinalize =
				pending.Action.Kind == AssemblyActionKind.FinalizeMontage &&
				pending.Action.Checkpoint == state.Checkpoint &&
				pending.Action.ExpectedStateRevision < state.StateRevision &&
				state.Phase == AssemblyPhase.FinalReview;
			if (!exact && !recoverableFinalize)
				throw new InvalidDataException(
					"A pending final-review action targets another exact state.");
			return pending.Action;
		}
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Action<AssemblyAction> begin = candidate =>
			{
				FinalizationRequest boundRequest =
					RequestForAction(request, candidate);
				actionExecutions.Begin(
					candidate,
					state.Phase,
					finalPlanSha256,
					adjustmentSha256: FinalizationRequestSha256(
						boundRequest,
						finalPlanSha256));
			};
			AssemblyAction? action = actions.TryRecoverClaimed(
				state.Checkpoint,
				sessionId,
				state.StateRevision,
				begin);
			action ??= actions.TryConsume(
				state.Checkpoint,
				sessionId,
				state.StateRevision,
				begin);
			if (action != null) return action;
			await Task.Delay(PollInterval, cancellationToken);
		}
	}

	private static FinalizationRequest RequestForAction(
		FinalizationRequest request,
		AssemblyAction action) =>
		action.Kind == AssemblyActionKind.FinalizeMontage
			? ApplyFinalizationPreference(request, action)
			: WithRenderPreference(request, false);

	private static FinalizationRequest ApplyFinalizationPreference(
		FinalizationRequest request,
		AssemblyAction action)
	{
		if (action.Kind != AssemblyActionKind.FinalizeMontage)
			throw new InvalidDataException(
				"A final-render preference can only be read from a " +
				"FinalizeMontage action.");
		if (!FinalizationActionTargets.IsSupported(action.TargetId))
			throw new InvalidDataException(
				"The FinalizeMontage action must explicitly choose whether " +
				"to render a final preview.");
		return WithRenderPreference(
			request,
			string.Equals(
				action.TargetId,
				FinalizationActionTargets.RenderFinalPreview,
				StringComparison.Ordinal));
	}

	private static FinalizationRequest WithRenderPreference(
		FinalizationRequest request,
		bool renderFinalPreview) =>
		new()
		{
			SessionId = request.SessionId,
			RequestId = request.RequestId,
			ProjectFingerprint = request.ProjectFingerprint,
			Workspace = request.Workspace,
			FinalPlan = request.FinalPlan,
			AcceptedCandidateBaseline = request.AcceptedCandidateBaseline,
			RenderFinalPreview = renderFinalPreview
		};

	private static void RequireFinalizationBinding(
		AssemblyActionExecutionStart pending,
		string finalPlanSha256,
		string finalizationRequestSha256)
	{
		if (!string.Equals(
				pending.PlanSha256,
				finalPlanSha256,
				StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(
				pending.AdjustmentSha256,
				finalizationRequestSha256,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The pending final-review action is not bound to the exact " +
				"final plan and finalization request under review.");
	}

	internal static string PlanSha256(EditPlanDocument plan)
	{
		ArgumentNullException.ThrowIfNull(plan);
		return ContentSha256(EditPlanDocumentSerializer.SerializePlan(plan));
	}

	internal static string FinalizationRequestSha256(
		FinalizationRequest request,
		string finalPlanSha256)
	{
		ArgumentNullException.ThrowIfNull(request);
		CandidateWorkspaceId workspace = request.Workspace ??
			throw new InvalidDataException(
				"The finalization request has no candidate workspace.");
		workspace.Validate();
		string canonical = string.Join(
			"\n",
			request.SessionId,
			request.RequestId,
			request.ProjectFingerprint,
			workspace.SessionId,
			workspace.Iteration.ToString(
				System.Globalization.CultureInfo.InvariantCulture),
			workspace.Nonce,
			request.AcceptedCandidateBaseline?.PlanSha256 ?? "",
			request.AcceptedCandidateBaseline?.SnapshotSha256 ?? "",
			request.RenderFinalPreview ? "1" : "0",
			finalPlanSha256);
		return ContentSha256(canonical);
	}

	private static string ContentSha256(string value) =>
		Convert.ToHexString(
			SHA256.HashData(new UTF8Encoding(false).GetBytes(value)))
			.ToLowerInvariant();

	private async Task ReconcileAbandonedAsync(
		AssemblySessionState abandoned,
		FinalizationRequest request,
		string finalPlanSha256,
		CancellationToken cancellationToken)
	{
		AssemblyActionExecutionStart? pending =
			actionExecutions.ReadPendingForRecovery(sessionId);
		if (pending != null)
		{
			if (pending.Action.Kind != AssemblyActionKind.AbandonSession)
				throw new InvalidDataException(
					"An abandoned final-review state has a non-abandon action pending.");
			FinalizationRequest boundRequest =
				RequestForAction(request, pending.Action);
			RequireFinalizationBinding(
				pending,
				finalPlanSha256,
				FinalizationRequestSha256(
					boundRequest,
					finalPlanSha256));
			await AbandonAsync(
				abandoned,
				pending.Action,
				"Recovered a reflected final-review abandon action.",
				cancellationToken);
		}
		if (publisher.State != EditSessionState.Cancelled)
			publisher.TransitionTo(
				EditSessionState.Cancelled,
				"Recovered an already completed final-review abandon action.");
		throw new AssemblySessionAbandonedException(
			"AI editing session was already abandoned during final review.");
	}

	private async Task AbandonAsync(
		AssemblySessionState state,
		AssemblyAction abandonAction,
		string reason,
		CancellationToken cancellationToken)
	{
		await CandidateAbandonCleanup.CleanupAsync(
			automation,
			state.Workspace,
			abandonAction.ActionId,
			cancellationToken);
		if (actions.ReadState()?.Phase != AssemblyPhase.Abandoned)
			actions.PublishState(
				Next(
					state,
					AssemblyPhase.Abandoned,
					reason +
					" Candidate-owned tracks were removed; recovery artifacts were retained."));
		if (publisher.State != EditSessionState.Cancelled)
			publisher.TransitionTo(EditSessionState.Cancelled, reason);
		if (!actionExecutions.IsComplete(abandonAction.ActionId))
			actionExecutions.Complete(
				abandonAction,
				"final-review-abandoned");
		throw new AssemblySessionAbandonedException(
			"AI editing session was abandoned during final review.");
	}

	private void RequireRecovery(string reason)
	{
		if (publisher.State != EditSessionState.NeedsRecovery)
			publisher.TransitionTo(EditSessionState.NeedsRecovery, reason);
	}

	private AssemblySessionState Publish(
		AssemblySessionState prior,
		AssemblyPhase phase,
		string status)
	{
		AssemblySessionState next = Next(prior, phase, status);
		actions.PublishState(next);
		return next;
	}

	private void PublishCompleted(
		AutoEditing.Iteration.Contracts.Automation.CandidateWorkspaceId workspace,
		string status)
	{
		AssemblySessionState prior = actions.ReadState() ??
			throw new InvalidDataException(
				"The completed workflow has no assembly state.");
		AssemblySessionState completed =
			Next(prior, AssemblyPhase.Completed, status);
		completed.Workspace = workspace;
		actions.PublishState(completed);
		if (publisher.State == EditSessionState.Completed)
			return;
		if (publisher.State != EditSessionState.Promoting)
			publisher.TransitionTo(
				EditSessionState.Promoting,
				"Recovering a completed promotion from its durable final report.");
		publisher.TransitionTo(
			EditSessionState.Completed,
			"Final promotion and durable reporting completed.");
	}

	private static AssemblySessionState Next(
		AssemblySessionState prior,
		AssemblyPhase phase,
		string status) =>
		new()
		{
			SessionId = prior.SessionId,
			Phase = phase,
			Checkpoint = prior.Checkpoint,
			StateRevision = prior.StateRevision + 1,
			TotalClips = prior.TotalClips,
			CurrentClipPath = "",
			RemainingClipPaths = Array.Empty<string>(),
			Status = status,
			Workspace = prior.Workspace
		};
}
