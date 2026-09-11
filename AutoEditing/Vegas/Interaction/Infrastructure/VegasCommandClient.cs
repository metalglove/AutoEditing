using System;
using System.Threading.Tasks;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class VegasCommandClient : IVegasCommandClient, IVegasQueryClient
{
	private readonly Vegas _vegas;
	private readonly Action<Action> _queueHostAction;
	public event EventHandler<VegasCommandCompletedEventArgs> CommandCompleted;

	public VegasCommandClient(Vegas vegas, Action<Action> queueHostAction)
	{
		_vegas = vegas ?? throw new ArgumentNullException("vegas");
		_queueHostAction = queueHostAction ?? throw new ArgumentNullException("queueHostAction");
	}

	public Task ExecuteAsync(IVegasCommand command)
	{
		return ExecuteRequestAsync<object>(command);
	}

	public Task<TResult> QueryAsync<TResult>(IVegasQuery<TResult> query)
	{
		return ExecuteRequestAsync<TResult>(query);
	}

	private Task<TResult> ExecuteRequestAsync<TResult>(IVegasRequest command)
	{
		if (command == null) throw new ArgumentNullException("command");
		TaskCompletionSource<TResult> completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		_queueHostAction(delegate
		{
			try
			{
				TResult result = VegasScriptCommandExecutor.Execute<TResult>(_vegas, command);
				completion.SetResult(result);
				CommandCompleted?.Invoke(this, new VegasCommandCompletedEventArgs { CommandType = command.CommandType });
			}
			catch (Exception exception)
			{
				completion.SetException(exception);
			}
		});
		return completion.Task;
	}
}
