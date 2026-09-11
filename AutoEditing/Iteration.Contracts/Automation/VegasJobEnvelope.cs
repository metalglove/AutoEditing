using System;
using Newtonsoft.Json.Linq;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class VegasJobEnvelope
{
	public int SchemaVersion { get; set; } = ContractSchema.CurrentVersion;
	public string Protocol { get; set; } = ContractSchema.Protocol;
	public string SessionId { get; set; } = "";
	public string JobId { get; set; } = "";
	public long Sequence { get; set; }
	public string IdempotencyKey { get; set; } = "";
	public string Operation { get; set; } = "";
	public DateTimeOffset CreatedUtc { get; set; }
	public DateTimeOffset DeadlineUtc { get; set; }
	public string PayloadSha256 { get; set; } = "";
	public string ExpectedProjectFingerprint { get; set; } = "";
	public JToken Payload { get; set; } = new JObject();
}
