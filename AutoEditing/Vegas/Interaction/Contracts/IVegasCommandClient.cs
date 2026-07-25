using System;
using System.Threading.Tasks;

namespace Core.Scripts;

internal interface IVegasCommandClient
{
	Task ExecuteAsync(IVegasCommand command);
	event EventHandler<VegasCommandCompletedEventArgs> CommandCompleted;
}
