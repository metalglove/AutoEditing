using System;

namespace Core.Host.Automation
{
	internal interface IVegasAutomationClock
	{
		DateTimeOffset UtcNow { get; }
	}

	internal sealed class SystemVegasAutomationClock : IVegasAutomationClock
	{
		public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
	}
}
