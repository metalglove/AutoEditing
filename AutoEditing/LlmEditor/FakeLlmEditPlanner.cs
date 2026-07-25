using Core.Domain.Editing;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor;

internal sealed class FakeLlmEditPlanner : IEditPlanner
{
	public Task<EditPlanDocument> CreatePlanAsync(
		EditPlanningRequest request,
		CancellationToken cancellationToken)
	{
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		cancellationToken.ThrowIfCancellationRequested();

		Core.Domain.Clip.Clip clip = request.Clips[0];
		double sourceDuration = Math.Min(2.0, clip.DurationSeconds);
		SpeedProfile speedProfile = new SpeedProfile(new[]
		{
			new SpeedProfilePoint(0.0, 1.0),
			new SpeedProfilePoint(sourceDuration, 1.0)
		});
		PreparedMontage montage = new PreparedMontage
		{
			Placements = new List<ClipPlacement>
			{
				new ClipPlacement
				{
					Clip = clip,
					TimelineStartSeconds = 0.0,
					SourceOffsetSeconds = 0.0,
					LengthSeconds = speedProfile.TimelineDurationSeconds,
					SpeedProfile = speedProfile
				}
			},
			EffectOptions = request.EffectOptions,
			EffectTreatments = new EffectTreatmentPlan()
		};
		EditPlanDocument document = new EditPlanDocument
		{
			RequestId = request.RequestId,
			PlannerId = "skeleton.fake",
			PlannerVersion = "1",
			StyleProfileIds = new List<string>(request.StyleProfileIds),
			Montage = montage,
			Diagnostics = new List<EditPlanDiagnostic>
			{
				new EditPlanDiagnostic
				{
					Severity = "Info",
					Code = "SKELETON_FAKE_PLAN",
					Message = "This deterministic skeleton plan proves the exchange contract and is not an LLM-authored edit."
				}
			}
		};
		EditPlanDocumentValidator.ValidateAndNormalize(document);
		return Task.FromResult(document);
	}
}
