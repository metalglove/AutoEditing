using System;
using System.Collections.Generic;
using AutoEditing.Iteration.Contracts.Automation;
using Core.Domain.Planning;

namespace Core.Scripts;

internal static class CandidateResourcePreflight
{
	public static PreflightCandidateResult Run(PreflightCandidateRequest request)
	{
		PreflightCandidateResult result = new PreflightCandidateResult();
		try
		{
			ValidateRequest(request);
			PreparedMontageResourcePreflight.ValidateAndNormalize(request.Plan.Montage, request.SongPath);
			result.IsReady = true;
		}
		catch (Exception exception)
		{
			result.IsReady = false;
			result.Errors = new List<AutomationError>
			{
				new AutomationError
				{
					Code = "candidate_preflight_failed",
					Stage = "preflight",
					Message = exception.Message,
					IsTransient = false
				}
			};
		}
		return result;
	}

	public static void ValidateReady(MaterializeCandidateRequest request)
	{
		if (request == null) throw new ArgumentNullException(nameof(request));
		PreflightCandidateResult result = Run(new PreflightCandidateRequest
		{
			Workspace = request.Workspace,
			Plan = request.Plan,
			SongPath = request.SongPath
		});
		if (!result.IsReady)
			throw new InvalidOperationException("Candidate preflight failed: " + result.Errors[0].Message);
	}

	private static void ValidateRequest(PreflightCandidateRequest request)
	{
		if (request == null) throw new ArgumentNullException(nameof(request));
		if (request.Workspace == null) throw new InvalidOperationException("Candidate workspace is required.");
		request.Workspace.Validate();
		if (request.Plan == null) throw new InvalidOperationException("Candidate edit plan is required.");
		EditPlanDocumentValidator.ValidateAndNormalize(request.Plan);
	}
}
