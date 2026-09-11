namespace AutoEditing.Iteration.Contracts.Sessions;

public enum EditSessionState
{
	Created, Planning, Validating, Materializing, Rendering, Reviewing,
	AwaitingUser, Revising, Paused, Accepted, Promoting, Completed,
	Polishing, FinalReview,
	Cancelled, Failed, NeedsRecovery
}
