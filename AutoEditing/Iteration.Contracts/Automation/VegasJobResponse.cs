using System;
using Newtonsoft.Json.Linq;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class VegasJobResponse
{
	public int SchemaVersion { get; set; } = ContractSchema.CurrentVersion;
	public string Protocol { get; set; } = ContractSchema.Protocol;
	public string SessionId { get; set; } = "";
	public string JobId { get; set; } = "";
	public VegasJobStatus Status { get; set; }
	public DateTimeOffset StartedUtc { get; set; }
	public DateTimeOffset CompletedUtc { get; set; }
	public VegasHostIdentity Host { get; set; }
	public string ResultSha256 { get; set; } = "";
	public JToken Result { get; set; }
	public AutomationError Error { get; set; }
}
