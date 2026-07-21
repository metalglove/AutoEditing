using System;
using System.Windows.Input;

namespace Core.Scripts;

internal sealed class RelayCommand : ICommand
{
	private readonly Action _execute;

	private readonly Func<bool> _canExecute;

	public event EventHandler CanExecuteChanged;

	public RelayCommand(Action execute, Func<bool> canExecute)
	{
		_execute = execute ?? throw new ArgumentNullException("execute");
		_canExecute = canExecute;
	}

	public bool CanExecute(object parameter)
	{
		return _canExecute == null || _canExecute();
	}

	public void Execute(object parameter)
	{
		_execute();
	}

	public void RaiseCanExecuteChanged()
	{
		this.CanExecuteChanged?.Invoke(this, EventArgs.Empty);
	}
}
