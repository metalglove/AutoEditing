using System.Collections.Generic;

namespace Core.Domain.Editing;

public sealed class EffectTreatmentPlan
{
	public int PresetSchemaVersion { get; set; }

	public string PresetId { get; set; }

	public int PresetRevision { get; set; }

	public int Seed { get; set; }

	public List<EffectTreatmentAction> Actions { get; set; } = new List<EffectTreatmentAction>();

	public List<EffectTreatmentDiagnostic> Diagnostics { get; set; } = new List<EffectTreatmentDiagnostic>();
}
