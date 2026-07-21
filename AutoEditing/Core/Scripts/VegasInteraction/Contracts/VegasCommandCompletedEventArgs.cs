using System;

namespace Core.Scripts;

internal sealed class VegasCommandCompletedEventArgs : EventArgs
{
	public string CommandType { get; set; }
}
