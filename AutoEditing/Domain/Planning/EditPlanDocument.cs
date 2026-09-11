using System.Collections.Generic;
using Core.Domain.Editing;

namespace Core.Domain.Planning;

public sealed class EditPlanDocument
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;

	public string RequestId { get; set; }

	public string PlannerId { get; set; }

	public string PlannerVersion { get; set; }

	public List<string> StyleProfileIds { get; set; } = new List<string>();

	public PreparedMontage Montage { get; set; }

	public List<EditPlanDiagnostic> Diagnostics { get; set; } = new List<EditPlanDiagnostic>();
}
