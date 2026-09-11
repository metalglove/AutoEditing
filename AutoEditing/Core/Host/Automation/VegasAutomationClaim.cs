using AutoEditing.Iteration.Contracts.Automation;

namespace Core.Host.Automation
{
	internal sealed class VegasAutomationClaim
	{
		public VegasAutomationClaim(string requestName, string runningPath, string rawJson, VegasJobEnvelope envelope)
		{
			RequestName = requestName;
			RunningPath = runningPath;
			RawJson = rawJson;
			Envelope = envelope;
		}

		public string RequestName { get; }
		public string RunningPath { get; }
		public string RawJson { get; }
		public VegasJobEnvelope Envelope { get; }
	}
}
