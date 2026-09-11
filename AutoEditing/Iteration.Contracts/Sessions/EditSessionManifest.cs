using System;
using System.Collections.Generic;

namespace AutoEditing.Iteration.Contracts.Sessions;

public sealed class EditSessionManifest
{
	public int SchemaVersion { get; set; } = ContractSchema.CurrentVersion;
	public string SessionId { get; set; } = "";
	public long Revision { get; set; }
	public EditSessionState State { get; set; } = EditSessionState.Created;
	public DateTimeOffset CreatedUtc { get; set; }
	public DateTimeOffset UpdatedUtc { get; set; }
	public int CurrentIteration { get; set; }
	public string AcceptedPlanHash { get; set; } = "";
	public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
}
