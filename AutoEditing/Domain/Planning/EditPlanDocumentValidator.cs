using System;
using System.Collections.Generic;

namespace Core.Domain.Planning;

public static class EditPlanDocumentValidator
{
	public static void ValidateAndNormalize(EditPlanDocument document)
	{
		if (document == null) throw new InvalidOperationException("The edit plan document is empty.");
		if (document.SchemaVersion != EditPlanDocument.CurrentSchemaVersion)
			throw new NotSupportedException("Unsupported edit plan schema version " + document.SchemaVersion + ".");
		if (string.IsNullOrWhiteSpace(document.RequestId))
			throw new InvalidOperationException("The edit plan has no request ID.");
		if (string.IsNullOrWhiteSpace(document.PlannerId))
			throw new InvalidOperationException("The edit plan has no planner ID.");
		if (string.IsNullOrWhiteSpace(document.PlannerVersion))
			throw new InvalidOperationException("The edit plan has no planner version.");
		document.StyleProfileIds = document.StyleProfileIds ?? new List<string>();
		document.Diagnostics = document.Diagnostics ?? new List<EditPlanDiagnostic>();
		PreparedMontageStructuralValidator.ValidateAndNormalize(document.Montage);
	}
}
