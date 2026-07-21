using System;
using System.Windows.Input;

namespace Core.Scripts;

internal sealed class RelayCommand : ICommand
{
	private readonly Action _execute;

	private readonly Func<bool> _canExecute;
	private readonly Action<object> _parameterizedExecute;
	private readonly Func<object, bool> _parameterizedCanExecute;

	public event EventHandler CanExecuteChanged;

	public RelayCommand(Action execute, Func<bool> canExecute)
	{
		_execute = execute ?? throw new ArgumentNullException("execute");
		_canExecute = canExecute;
	}

	public RelayCommand(Action<object> execute, Func<object, bool> canExecute)
	{
		_parameterizedExecute = execute ?? throw new ArgumentNullException("execute");
		_parameterizedCanExecute = canExecute;
	}

	public bool CanExecute(object parameter)
	{
		return _parameterizedCanExecute != null ? _parameterizedCanExecute(parameter) : _canExecute == null || _canExecute();
	}

	public void Execute(object parameter)
	{
		if (_parameterizedExecute != null) _parameterizedExecute(parameter);
		else _execute();
	}

	public void RaiseCanExecuteChanged()
	{
		this.CanExecuteChanged?.Invoke(this, EventArgs.Empty);
	}
}
