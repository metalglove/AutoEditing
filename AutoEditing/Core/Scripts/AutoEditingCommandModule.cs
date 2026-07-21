using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Core.Domain;
using ScriptPortal.Vegas;

namespace Core.Scripts;

public sealed class AutoEditingCommandModule : ICustomCommandModule
{
	private sealed class AutoEditingDockControl : DockableControl
	{
		private readonly ShotReviewViewModel _viewModel;

		public AutoEditingDockControl(Vegas vegas, CustomCommand autoLoadCommand, Action<Action> queueHostAction)
			: base("AutoEditingShotReviewDock")
		{
			((DockableControl)this).DisplayName = "AutoEditing Shot Review";
			((DockableControl)this).DefaultDockWindowStyle = (DockWindowStyle)1;
			((DockableControl)this).DefaultFloatingSize = new Size(1040, 790);
			((DockableControl)this).PersistDockWindowState = true;
			((DockableControl)this).AutoLoadCommand = autoLoadCommand;
			_viewModel = new ShotReviewViewModel(vegas, queueHostAction);
			ElementHost value = new ElementHost
			{
				Dock = DockStyle.Fill,
				Child = ShotReviewView.Create(_viewModel)
			};
			((Control)this).Controls.Add(value);
			((DockableControl)this).Closing += HandleClosing;
			((DockableControl)this).Closed += HandleClosed;
		}

		private void HandleClosing(object sender, CancelEventArgs args)
		{
			if (_viewModel.IsBusy)
			{
				_viewModel.Cancel();
				args.Cancel = true;
			}
		}

		private void HandleClosed(object sender, EventArgs args)
		{
			_viewModel.Dispose();
		}
	}

	public const string CommandName = "AutoEditingShotReview";

	private const string DockName = "AutoEditingShotReviewDock";

	private const string HostCommandName = "AutoEditingShotReviewHostAction";

	private Vegas _vegas;

	private readonly CustomCommand _viewCommand = new CustomCommand((CommandCategory)1, "AutoEditingShotReview");

	private readonly CustomCommand _hostCommand = new CustomCommand((CommandCategory)2, "AutoEditingShotReviewHostAction");

	private readonly Queue<Action> _hostActions = new Queue<Action>();

	public void InitializeModule(Vegas vegas)
	{
		_vegas = vegas;
		ConfigurationManager.ReloadConfiguration();
	}

	public ICollection GetCustomCommands()
	{
		_viewCommand.DisplayName = "AutoEditing Shot Review";
		_viewCommand.MenuItemName = "AutoEditing Shot Review";
		_viewCommand.CanAddToKeybindings = true;
		_viewCommand.CanAddToToolbar = true;
		_viewCommand.Invoked += HandleInvoked;
		_viewCommand.MenuPopup += HandleMenuPopup;
		_hostCommand.DisplayName = "AutoEditing Host Action";
		_hostCommand.CanAddToMenu = false;
		_hostCommand.CanAddToKeybindings = false;
		_hostCommand.CanAddToToolbar = false;
		_hostCommand.Invoked += HandleHostAction;
		return new CustomCommand[2] { _viewCommand, _hostCommand };
	}

	private void HandleInvoked(object sender, EventArgs args)
	{
		if (!_vegas.ActivateDockView("AutoEditingShotReviewDock"))
		{
			AutoEditingDockControl autoEditingDockControl = new AutoEditingDockControl(_vegas, _viewCommand, QueueHostAction);
			_vegas.LoadDockView((IDockView)(object)autoEditingDockControl);
		}
	}

	private void QueueHostAction(Action action)
	{
		if (action == null)
		{
			throw new ArgumentNullException("action");
		}
		lock (_hostActions)
		{
			_hostActions.Enqueue(action);
		}
		try
		{
			_vegas.InvokeCommand(_hostCommand);
		}
		catch
		{
			lock (_hostActions)
			{
				if (_hostActions.Count > 0 && (object)_hostActions.Peek() == action)
				{
					_hostActions.Dequeue();
				}
			}
			throw;
		}
	}

	private void HandleHostAction(object sender, EventArgs args)
	{
		Action action;
		lock (_hostActions)
		{
			if (_hostActions.Count == 0)
			{
				return;
			}
			action = _hostActions.Dequeue();
		}
		action();
	}

	private void HandleMenuPopup(object sender, EventArgs args)
	{
		_viewCommand.Checked = _vegas.FindDockView("AutoEditingShotReviewDock");
	}
}
