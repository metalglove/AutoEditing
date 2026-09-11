using AutoEditing.Iteration.Contracts.Sessions;

namespace AutoEditing.Iteration.Contracts.Assembly;

public sealed class AssemblyLifecycleAvailability
{
	public bool CanResume { get; set; }
	public bool CanPause { get; set; }
	public bool CanAbandon { get; set; }
}

public static class AssemblyLifecyclePolicy
{
	public static AssemblyLifecycleAvailability Evaluate(
		EditSessionState sessionState,
		AssemblyPhase assemblyPhase,
		int checkpoint,
		long stateRevision,
		bool hasLiveConsumer)
	{
		if (stateRevision <= 0 ||
			sessionState == EditSessionState.Accepted ||
			sessionState == EditSessionState.Completed ||
			sessionState == EditSessionState.Cancelled ||
			assemblyPhase == AssemblyPhase.SyncPassComplete ||
			assemblyPhase == AssemblyPhase.Abandoned)
			return new AssemblyLifecycleAvailability();

		bool paused =
			sessionState == EditSessionState.Paused ||
			assemblyPhase == AssemblyPhase.Paused;
		bool reviewBoundary =
			(assemblyPhase is
				AssemblyPhase.AwaitingHumanReview or
				AssemblyPhase.ReconciliationConflict or
				AssemblyPhase.RoughCutReview or
				AssemblyPhase.RoughCutCorrection or
				AssemblyPhase.EffectsPlanReview or
				AssemblyPhase.EffectsPreviewReview or
				AssemblyPhase.AudioPlanReview or
				AssemblyPhase.AudioPreviewReview or
				AssemblyPhase.FinalReview) &&
			checkpoint > 0;
		return new AssemblyLifecycleAvailability
		{
			CanResume = hasLiveConsumer
				? paused && checkpoint > 0
				: true,
			CanPause = hasLiveConsumer && reviewBoundary && !paused,
			CanAbandon = !hasLiveConsumer ||
				((reviewBoundary || paused) && checkpoint > 0)
		};
	}
}
