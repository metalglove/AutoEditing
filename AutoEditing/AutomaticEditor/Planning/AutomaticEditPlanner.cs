using System;
using System.Threading;
using System.Threading.Tasks;
using Core.Domain.Planning;

namespace Core.Domain.Editing;

public sealed class AutomaticEditPlanner : IEditPlanner
{
	public Task<EditPlanDocument> CreatePlanAsync(EditPlanningRequest request, CancellationToken cancellationToken)
	{
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		cancellationToken.ThrowIfCancellationRequested();
		PreparedMontage montage = new MontagePreparationService().Prepare(request.Clips, request.SongPath, request.EffectOptions);
		cancellationToken.ThrowIfCancellationRequested();
		EditPlanDocument document = new EditPlanDocument
		{
			RequestId = request.RequestId,
			PlannerId = "autoediting.automatic",
			PlannerVersion = "1",
			StyleProfileIds = request.StyleProfileIds,
			Montage = montage
		};
		EditPlanDocumentValidator.ValidateAndNormalize(document);
		return Task.FromResult(document);
	}
}
