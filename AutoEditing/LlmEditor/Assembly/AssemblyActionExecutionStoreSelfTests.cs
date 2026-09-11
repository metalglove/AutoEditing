using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;

namespace AutoEditing.LlmEditor.Assembly;

internal static class AssemblyActionExecutionStoreSelfTests
{
	public static void Run(string testRoot)
	{
		TestBeginAndCompleteAreIdempotentAcrossClockChanges(testRoot);
		TestCorruptCompletionIsQuarantinedAndFailsClosed(testRoot);
		TestExactPendingLookupRejectsStaleStateTargets(testRoot);
		TestTimelineEvidenceIsIdentityBoundAndImmutable(testRoot);
		TestMultiplePendingExecutionsFailClosed(testRoot);
	}

	private static void TestTimelineEvidenceIsIdentityBoundAndImmutable(
		string testRoot)
	{
		string sessionRoot = CreateSession(testRoot, "action-timeline-evidence");
		AssemblyAction action = Action(
			"timeline-evidence",
			checkpoint: 2,
			stateRevision: 9,
			AssemblyActionKind.ReviseCurrentClip);
		AssemblyActionExecutionStore store = new(sessionRoot);
		store.Begin(
			action,
			AssemblyPhase.AwaitingHumanReview,
			new string('d', 64),
			proposalRevision: 3);
		CandidateTimelineSnapshot snapshot = new()
		{
			Workspace = new CandidateWorkspaceId
			{
				SessionId = action.SessionId,
				Iteration = 2,
				Nonce = "timeline-evidence"
			},
			TimelineEnd = TimeSpan.FromSeconds(3)
		};
		AssemblyActionTimelineEvidence saved =
			store.SaveTimelineEvidence(action, snapshot);
		Assert(saved.SnapshotSha256.Length == 64 &&
			store.ReadTimelineEvidence(action.ActionId)?.Snapshot.Workspace.Nonce ==
				"timeline-evidence",
			"The exact live timeline evidence was not durably recoverable.");

		snapshot.TimelineEnd = TimeSpan.FromSeconds(4);
		ExpectInvalidData(
			() => store.SaveTimelineEvidence(action, snapshot),
			"Different live evidence replaced an action's immutable snapshot.");

		AssemblyAction foreign = Action(
			"foreign-evidence",
			checkpoint: 2,
			stateRevision: 10,
			AssemblyActionKind.CompareCurrentTimeline);
		store.Begin(foreign, AssemblyPhase.AwaitingHumanReview);
		ExpectInvalidData(
			() => store.SaveTimelineEvidence(
				foreign,
				new CandidateTimelineSnapshot
				{
					Workspace = new CandidateWorkspaceId
					{
						SessionId = "another-session",
						Iteration = 2,
						Nonce = "foreign"
					}
				}),
			"Timeline evidence from another session was accepted.");
		ExpectInvalidData(
			() => store.SaveTimelineEvidence(
				foreign,
				new CandidateTimelineSnapshot
				{
					Workspace = new CandidateWorkspaceId
					{
						SessionId = action.SessionId,
						Iteration = 3,
						Nonce = "foreign-checkpoint"
					}
				}),
			"Timeline evidence from another checkpoint workspace was accepted.");
	}

	private static void TestMultiplePendingExecutionsFailClosed(string testRoot)
	{
		string sessionRoot = CreateSession(testRoot, "action-multiple-pending");
		AssemblyActionExecutionStore store = new(sessionRoot);
		store.Begin(
			Action(
				"pending-one",
				1,
				2,
				AssemblyActionKind.CompareCurrentTimeline),
			AssemblyPhase.AwaitingHumanReview);
		store.Begin(
			Action(
				"pending-two",
				1,
				2,
				AssemblyActionKind.RenderCheckpointPreview),
			AssemblyPhase.AwaitingHumanReview);
		ExpectInvalidData(
			() => store.ReadPendingForRecovery("execution-session"),
			"Multiple incomplete action starts did not fail closed.");
	}

	private static void TestBeginAndCompleteAreIdempotentAcrossClockChanges(
		string testRoot)
	{
		string sessionRoot = CreateSession(testRoot, "action-execution-idempotency");
		DateTimeOffset firstClock =
			DateTimeOffset.Parse("2026-07-27T08:00:00Z");
		DateTimeOffset secondClock =
			DateTimeOffset.Parse("2026-07-27T09:00:00Z");
		AssemblyAction action = Action(
			"idempotent-action",
			checkpoint: 3,
			stateRevision: 17,
			AssemblyActionKind.AcceptTimelineAndContinue);
		AssemblyActionExecutionStore first =
			new(sessionRoot, () => firstClock);

		AssemblyActionExecutionStart initial = first.Begin(
			action,
			AssemblyPhase.AwaitingHumanReview,
			new string('a', 64),
			proposalRevision: 4,
			previewAttempt: 2,
			new string('b', 64));
		first.Complete(
			action,
			"accepted",
			"assembly/accepted/checkpoint-0003.json",
			new string('c', 64));

		AssemblyActionExecutionStore restarted =
			new(sessionRoot, () => secondClock);
		AssemblyActionExecutionStart recovered = restarted.Begin(
			action,
			AssemblyPhase.AwaitingHumanReview,
			new string('a', 64),
			proposalRevision: 4,
			previewAttempt: 2,
			new string('b', 64));
		restarted.Complete(
			action,
			"accepted",
			"assembly/accepted/checkpoint-0003.json",
			new string('c', 64));
		AssemblyActionExecutionCompletion completion =
			restarted.ReadCompletion(action.ActionId) ??
			throw new InvalidOperationException(
				"The durable action completion disappeared after an idempotent replay.");

		Assert(initial.StartedUtc == firstClock &&
			recovered.StartedUtc == firstClock,
			"Replaying Begin replaced the original durable start time.");
		Assert(completion.CompletedUtc == firstClock,
			"Replaying Complete with a later clock replaced the original completion.");
		Assert(restarted.IsComplete(action.ActionId),
			"The idempotently completed action was not recognized as complete.");
		Assert(restarted.ReadPending(
				action.SessionId,
				action.Checkpoint,
				action.ExpectedStateRevision,
				AssemblyPhase.AwaitingHumanReview) == null,
			"A completed action remained pending after restart.");
	}

	private static void TestCorruptCompletionIsQuarantinedAndFailsClosed(
		string testRoot)
	{
		string sessionRoot = CreateSession(testRoot, "action-execution-corrupt");
		AssemblyAction action = Action(
			"corrupt-completion",
			checkpoint: 5,
			stateRevision: 23,
			AssemblyActionKind.ApproveEffectsPlan);
		AssemblyActionExecutionStore store = new(
			sessionRoot,
			() => DateTimeOffset.Parse("2026-07-27T10:00:00Z"));
		store.Begin(action, AssemblyPhase.EffectsPlanReview);
		string executionRoot =
			Path.Combine(sessionRoot, "assembly", "action-executions");
		string completionPath =
			Path.Combine(executionRoot, action.ActionId + ".completed.json");
		File.WriteAllText(completionPath, "{");

		ExpectInvalidData(
			() => store.IsComplete(action.ActionId),
			"A malformed action completion did not fail closed.");

		Assert(!File.Exists(completionPath),
			"The corrupt completion remained in the active journal.");
		string quarantine =
			Path.Combine(executionRoot, "quarantine");
		Assert(Directory.Exists(quarantine) &&
			Directory.EnumerateFiles(
				quarantine,
				action.ActionId + ".completed.json.*.corrupt").Any(),
			"The corrupt completion was not quarantined for diagnosis.");
		Assert(store.ReadPending(
				action.SessionId,
				action.Checkpoint,
				action.ExpectedStateRevision,
				AssemblyPhase.EffectsPlanReview)?.Action.ActionId ==
			action.ActionId,
			"Quarantining a corrupt completion suppressed recovery of its start record.");
	}

	private static void TestExactPendingLookupRejectsStaleStateTargets(
		string testRoot)
	{
		string sessionRoot = CreateSession(testRoot, "action-execution-exact-state");
		AssemblyAction action = Action(
			"exact-state-action",
			checkpoint: 7,
			stateRevision: 31,
			AssemblyActionKind.FinalizeMontage);
		AssemblyActionExecutionStore store = new(
			sessionRoot,
			() => DateTimeOffset.Parse("2026-07-27T11:00:00Z"));
		store.Begin(action, AssemblyPhase.FinalReview);

		AssemblyActionExecutionStart? exact = store.ReadPending(
			action.SessionId,
			action.Checkpoint,
			action.ExpectedStateRevision,
			AssemblyPhase.FinalReview);
		Assert(exact?.Action.ActionId == action.ActionId,
			"The exact state-targeted pending action could not be recovered.");

		ExpectInvalidData(
			() => store.ReadPending(
				action.SessionId,
				action.Checkpoint,
				action.ExpectedStateRevision + 1,
				AssemblyPhase.FinalReview),
			"A stale state-revision action was accepted as exact.");
		ExpectInvalidData(
			() => store.ReadPending(
				action.SessionId,
				action.Checkpoint,
				action.ExpectedStateRevision,
				AssemblyPhase.PolishAccepted),
			"A cross-phase pending action was accepted as exact.");
		ExpectInvalidData(
			() => store.ReadPending(
				action.SessionId,
				action.Checkpoint + 1,
				action.ExpectedStateRevision,
				AssemblyPhase.FinalReview),
			"A cross-checkpoint pending action was accepted as exact.");
	}

	private static AssemblyAction Action(
		string actionId,
		int checkpoint,
		long stateRevision,
		AssemblyActionKind kind) =>
		new()
		{
			ActionId = actionId,
			SessionId = "execution-session",
			Checkpoint = checkpoint,
			ExpectedStateRevision = stateRevision,
			Kind = kind,
			CreatedUtc = DateTimeOffset.Parse("2026-07-27T07:00:00Z")
		};

	private static string CreateSession(string testRoot, string name)
	{
		string root = Path.Combine(testRoot, name);
		Directory.CreateDirectory(root);
		return root;
	}

	private static void ExpectInvalidData(Action action, string message)
	{
		try
		{
			action();
		}
		catch (InvalidDataException)
		{
			return;
		}
		throw new InvalidOperationException(message);
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}
}
