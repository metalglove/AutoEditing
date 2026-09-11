using System;
using System.Collections.Generic;

namespace AutoEditing.Iteration.Contracts.Sessions;

public static class EditSessionStateTransitionValidator
{
	private static readonly IDictionary<EditSessionState, ISet<EditSessionState>> Allowed =
		new Dictionary<EditSessionState, ISet<EditSessionState>>
		{
			[EditSessionState.Created] = Set(EditSessionState.Planning, EditSessionState.Cancelled, EditSessionState.Failed),
			[EditSessionState.Planning] = Set(EditSessionState.Validating, EditSessionState.Paused, EditSessionState.NeedsRecovery, EditSessionState.Cancelled, EditSessionState.Failed),
			[EditSessionState.Validating] = Set(EditSessionState.Materializing, EditSessionState.Revising, EditSessionState.NeedsRecovery, EditSessionState.Failed),
			[EditSessionState.Materializing] = Set(EditSessionState.Rendering, EditSessionState.Reviewing, EditSessionState.NeedsRecovery, EditSessionState.Failed),
			[EditSessionState.Rendering] = Set(EditSessionState.Reviewing, EditSessionState.NeedsRecovery, EditSessionState.Failed),
			[EditSessionState.Reviewing] = Set(EditSessionState.AwaitingUser, EditSessionState.Revising, EditSessionState.Accepted, EditSessionState.NeedsRecovery, EditSessionState.Failed),
			[EditSessionState.AwaitingUser] = Set(EditSessionState.Rendering, EditSessionState.Reviewing, EditSessionState.Revising, EditSessionState.Polishing, EditSessionState.FinalReview, EditSessionState.Accepted, EditSessionState.Paused, EditSessionState.NeedsRecovery, EditSessionState.Cancelled),
			[EditSessionState.Revising] = Set(EditSessionState.Validating, EditSessionState.Paused, EditSessionState.NeedsRecovery, EditSessionState.Cancelled, EditSessionState.Failed),
			[EditSessionState.Paused] = Set(EditSessionState.Planning, EditSessionState.Revising, EditSessionState.AwaitingUser, EditSessionState.Polishing, EditSessionState.FinalReview, EditSessionState.NeedsRecovery, EditSessionState.Cancelled),
			[EditSessionState.Accepted] = Set(EditSessionState.Promoting, EditSessionState.Completed),
			[EditSessionState.Polishing] = Set(EditSessionState.Rendering, EditSessionState.AwaitingUser, EditSessionState.FinalReview, EditSessionState.Paused, EditSessionState.NeedsRecovery, EditSessionState.Cancelled, EditSessionState.Failed),
			[EditSessionState.FinalReview] = Set(EditSessionState.Promoting, EditSessionState.Paused, EditSessionState.NeedsRecovery, EditSessionState.Cancelled, EditSessionState.Failed),
			[EditSessionState.Promoting] = Set(EditSessionState.Completed, EditSessionState.NeedsRecovery, EditSessionState.Failed),
			[EditSessionState.NeedsRecovery] = Set(EditSessionState.Planning, EditSessionState.Validating, EditSessionState.Materializing, EditSessionState.Rendering, EditSessionState.Reviewing, EditSessionState.AwaitingUser, EditSessionState.Revising, EditSessionState.Polishing, EditSessionState.FinalReview, EditSessionState.Promoting, EditSessionState.Paused, EditSessionState.Cancelled, EditSessionState.Failed),
			[EditSessionState.Completed] = Set(),
			[EditSessionState.Cancelled] = Set(),
			[EditSessionState.Failed] = Set(EditSessionState.NeedsRecovery, EditSessionState.Cancelled)
		};

	public static bool CanTransition(EditSessionState from, EditSessionState to) =>
		from == to || Allowed.TryGetValue(from, out ISet<EditSessionState> targets) && targets.Contains(to);

	public static void Validate(EditSessionState from, EditSessionState to)
	{
		if (!CanTransition(from, to))
			throw new InvalidOperationException($"Session state cannot transition from {from} to {to}.");
	}

	private static ISet<EditSessionState> Set(params EditSessionState[] values) =>
		new HashSet<EditSessionState>(values);
}
