using System;

namespace Core.Scripts;

internal interface IVegasHostEventSource : IDisposable
{
	event EventHandler<VegasHostEventArgs> Changed;
}
