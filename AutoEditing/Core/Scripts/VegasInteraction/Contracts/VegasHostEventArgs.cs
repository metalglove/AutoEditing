using System;

namespace Core.Scripts;

internal sealed class VegasHostEventArgs : EventArgs
{
	public VegasHostEventKind Kind { get; set; }
}
