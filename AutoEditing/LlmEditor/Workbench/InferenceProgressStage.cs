using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.LlmEditor.Assembly;

namespace AutoEditing.LlmEditor.Workbench;

internal static class InferenceProgressStage
{
	public static string Describe(AssemblySessionState? state)
	{
		if (state == null)
			return "Preparing session";
		return state.Phase switch
		{
			AssemblyPhase.CreatingSketch =>
				"Generating assembly sketch",
			AssemblyPhase.PlanningClip =>
				"Planning clip " + Math.Max(1, state.Checkpoint),
			AssemblyPhase.RepairingProposal =>
				"Repairing clip " + Math.Max(1, state.Checkpoint),
			AssemblyPhase.MaterializingClip =>
				"Materializing clip " +
				Math.Max(1, state.Checkpoint),
			AssemblyPhase.RenderingCheckpoint =>
				"Rendering checkpoint " +
				Math.Max(1, state.Checkpoint),
			AssemblyPhase.ReconcilingTimeline =>
				"Comparing live VEGAS timeline",
			AssemblyPhase.ReconciliationConflict =>
				"Waiting on synchronization conflict",
			AssemblyPhase.RoughCutRendering =>
				"Rendering full rough cut",
			AssemblyPhase.RoughCutAuditing =>
				"Auditing full rough cut",
			AssemblyPhase.RoughCutCorrection =>
				"Applying rough-cut correction",
			AssemblyPhase.EffectsPlanReview =>
				"Reviewing effects plan",
			AssemblyPhase.EffectsMaterializing =>
				"Rendering effects preview",
			AssemblyPhase.AudioPlanReview =>
				"Reviewing audio plan",
			AssemblyPhase.AudioMaterializing =>
				"Rendering audio preview",
			AssemblyPhase.FinalReview =>
				"Validating final candidate",
			AssemblyPhase.Paused => "Paused",
			AssemblyPhase.Abandoned => "Abandoned",
			AssemblyPhase.Completed => "Completed",
			_ => state.Phase.ToString()
		};
	}
}
