using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using Core.Domain;
using Core.Domain.Audio;
using Core.Domain.Clip;
using Core.Domain.Editing;
using Core.Domain.Logging;
using Microsoft.Win32;
using ScriptPortal.Vegas;

namespace Core.Scripts;

public sealed class ShotReviewViewModel : INotifyPropertyChanged, IDisposable
{
	private readonly Vegas _vegas;

	private readonly Dispatcher _dispatcher;

	private readonly Action<Action> _queueVegasAction;

	private readonly List<RelayCommand> _commands = new List<RelayCommand>();

	private CancellationTokenSource _operationCancellation;

	private string _clipsFolder;

	private string _songPath;

	private string _sfxRoot;

	private string _status = "Ready";

	private string _logText = string.Empty;

	private bool _isBusy;

	private bool _isIndeterminate;

	private int _progressValue;

	private int _progressMaximum = 1;

	public string ClipsFolder
	{
		get
		{
			return _clipsFolder;
		}
		set
		{
			if (Set(ref _clipsFolder, value, "ClipsFolder"))
			{
				PathsChanged("ClipsFolderExists");
			}
		}
	}

	public string SongPath
	{
		get
		{
			return _songPath;
		}
		set
		{
			if (Set(ref _songPath, value, "SongPath"))
			{
				PathsChanged("SongExists");
			}
		}
	}

	public string SfxRoot
	{
		get
		{
			return _sfxRoot;
		}
		set
		{
			if (Set(ref _sfxRoot, value, "SfxRoot"))
			{
				PathsChanged("SfxRootExists");
			}
		}
	}

	public string Status
	{
		get
		{
			return _status;
		}
		private set
		{
			Set(ref _status, value, "Status");
		}
	}

	public string LogText
	{
		get
		{
			return _logText;
		}
		private set
		{
			Set(ref _logText, value, "LogText");
		}
	}

	public bool IsBusy
	{
		get
		{
			return _isBusy;
		}
		private set
		{
			if (Set(ref _isBusy, value, "IsBusy"))
			{
				OnPropertyChanged("IsIdle");
				RefreshCommands();
			}
		}
	}

	public bool IsIdle => !IsBusy;

	public bool ClipsFolderExists => Directory.Exists(ClipsFolder);

	public bool SongExists => File.Exists(SongPath);

	public bool SfxRootExists => Directory.Exists(SfxRoot);

	public bool IsIndeterminate
	{
		get
		{
			return _isIndeterminate;
		}
		private set
		{
			Set(ref _isIndeterminate, value, "IsIndeterminate");
		}
	}

	public int ProgressValue
	{
		get
		{
			return _progressValue;
		}
		private set
		{
			Set(ref _progressValue, value, "ProgressValue");
		}
	}

	public int ProgressMaximum
	{
		get
		{
			return _progressMaximum;
		}
		private set
		{
			Set(ref _progressMaximum, value, "ProgressMaximum");
		}
	}

	public ICommand BrowseClipsCommand { get; }

	public ICommand BrowseSongCommand { get; }

	public ICommand BrowseSfxCommand { get; }

	public ICommand IndexSfxCommand { get; }

	public ICommand ValidateSfxCommand { get; }

	public ICommand AnalyzeCommand { get; }

	public ICommand MarkHitCommand { get; }

	public ICommand MarkHeadshotCommand { get; }

	public ICommand MarkMissCommand { get; }

	public ICommand BuildMontageCommand { get; }

	public ICommand CancelCommand { get; }

	public ICommand ClearLogCommand { get; }

	public event PropertyChangedEventHandler PropertyChanged;

	public ShotReviewViewModel(Vegas vegas, Action<Action> queueVegasAction)
	{
		_vegas = vegas;
		_queueVegasAction = queueVegasAction ?? throw new ArgumentNullException("queueVegasAction");
		_dispatcher = Dispatcher.CurrentDispatcher;
		_clipsFolder = ConfigurationManager.GetQuickTestingClipsFolder();
		_songPath = ConfigurationManager.GetQuickTestingSongPath();
		_sfxRoot = ConfigurationManager.GetShotDetection().SfxRoot;
		BrowseClipsCommand = Command(BrowseClips, () => IsIdle);
		BrowseSongCommand = Command(BrowseSong, () => IsIdle);
		BrowseSfxCommand = Command(BrowseSfx, () => IsIdle);
		IndexSfxCommand = Command(async delegate
		{
			await RunBusyAsync("Indexing SFX templates", IndexSfxAsync);
		}, () => IsIdle && SfxRootExists);
		ValidateSfxCommand = Command(async delegate
		{
			await RunBusyAsync("Validating SFX index", ValidateSfxAsync);
		}, () => IsIdle && SfxRootExists);
		AnalyzeCommand = Command(async delegate
		{
			await RunBusyAsync("Analyzing clips", AnalyzeClipsAsync);
		}, () => IsIdle && ClipsFolderExists && SfxRootExists);
		MarkHitCommand = Command(delegate
		{
			RunVegas(delegate
			{
				new ShotReviewWorkflow().MarkAtCursor(_vegas, ShotOutcome.Hit);
			});
		}, () => IsIdle);
		MarkHeadshotCommand = Command(delegate
		{
			RunVegas(delegate
			{
				new ShotReviewWorkflow().MarkAtCursor(_vegas, ShotOutcome.Headshot);
			});
		}, () => IsIdle);
		MarkMissCommand = Command(delegate
		{
			RunVegas(delegate
			{
				new ShotReviewWorkflow().MarkAtCursor(_vegas, ShotOutcome.Miss);
			});
		}, () => IsIdle);
		BuildMontageCommand = Command(async delegate
		{
			await RunBusyAsync("Building montage", BuildFromMarkersAsync);
		}, () => IsIdle && ClipsFolderExists && SfxRootExists && SongExists);
		CancelCommand = Command(Cancel, () => IsBusy);
		ClearLogCommand = Command(ClearLog, () => IsIdle && LogText.Length > 0);
		Logger.SetSink(AppendLog);
		Logger.Log("AE workflow ready. Existing non-AE tracks, regions, and markers are preserved.");
	}

	public void Cancel()
	{
		if (_operationCancellation != null)
		{
			Status = "Cancelling current operation...";
			_operationCancellation.Cancel();
		}
	}

	public void Dispose()
	{
		_operationCancellation?.Cancel();
		Logger.SetSink(null);
	}

	private RelayCommand Command(Action execute, Func<bool> canExecute)
	{
		RelayCommand relayCommand = new RelayCommand(execute, canExecute);
		_commands.Add(relayCommand);
		return relayCommand;
	}

	private async Task RunBusyAsync(string operation, Func<CancellationToken, Task> action)
	{
		if (_operationCancellation != null)
		{
			return;
		}
		_operationCancellation = new CancellationTokenSource();
		IsBusy = true;
		IsIndeterminate = true;
		ProgressValue = 0;
		ProgressMaximum = 1;
		Status = operation;
		try
		{
			await action(_operationCancellation.Token);
			ReportProgress(1, 1, operation + " complete");
		}
		catch (OperationCanceledException)
		{
			Logger.Log(operation + " cancelled.");
			Status = "Cancelled";
		}
		catch (Exception ex2)
		{
			Exception ex3 = ex2;
			Logger.LogError(ex3.Message, ex3);
			Status = "Failed: " + ex3.Message;
		}
		finally
		{
			_operationCancellation.Dispose();
			_operationCancellation = null;
			IsIndeterminate = false;
			IsBusy = false;
		}
	}

	private async Task IndexSfxAsync(CancellationToken cancellationToken)
	{
		string root = SfxRoot;
		await Task.Run(delegate
		{
			cancellationToken.ThrowIfCancellationRequested();
			new ShotReviewWorkflow().CalibrateSfx(_vegas, root);
		}, cancellationToken);
	}

	private async Task ValidateSfxAsync(CancellationToken cancellationToken)
	{
		string root = SfxRoot;
		await Task.Run(delegate
		{
			cancellationToken.ThrowIfCancellationRequested();
			new ShotReviewWorkflow().SaveCalibration(_vegas, root);
		}, cancellationToken);
	}

	private async Task AnalyzeClipsAsync(CancellationToken cancellationToken)
	{
		string clips = ClipsFolder;
		string sfx = SfxRoot;
		if (!Directory.Exists(clips))
		{
			throw new DirectoryNotFoundException(clips);
		}
		if (!Directory.Exists(sfx))
		{
			throw new DirectoryNotFoundException(sfx);
		}
		ShotReviewWorkflow workflow = new ShotReviewWorkflow();
		ShotReviewWorkflow.AnalysisBatch batch = await Task.Run(() => workflow.AnalyzeClipAudio(clips, sfx, ReportProgress, cancellationToken), cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		Logger.Log("Audio analysis complete. Applying AE review tracks and markers through the VEGAS script host.");
		await InvokeVegasAsync(delegate
		{
			VegasScriptBridge.LayoutAnalysis(_vegas, batch);
		});
	}

	private async Task BuildFromMarkersAsync(CancellationToken cancellationToken)
	{
		string clipsFolder = ClipsFolder;
		string sfxRoot = SfxRoot;
		string song = SongPath;
		if (!Directory.Exists(clipsFolder))
		{
			throw new DirectoryNotFoundException(clipsFolder);
		}
		if (!File.Exists(song))
		{
			throw new FileNotFoundException("Song not found.", song);
		}
		ShotReviewWorkflow workflow = new ShotReviewWorkflow();
		List<Clip> clips = await InvokeVegasAsync(() => workflow.CaptureReviewedMarkers(_vegas, clipsFolder, sfxRoot));
		clips = clips.FindAll((Clip c) => c.ConfirmedKills.Count > 0);
		if (clips.Count == 0)
		{
			throw new InvalidOperationException("No reviewed Hit/Headshot markers were found.");
		}
		ReportProgress(0, 1, "Detecting song beats and planning velocity...");
		MontageOrchestrator orchestrator = new MontageOrchestrator();
		MontageOrchestrator.PreparedMontage prepared = await Task.Run(() => orchestrator.PrepareMontage(clips, song), cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		ReportProgress(1, 1, "Applying montage to VEGAS timeline...");
		await InvokeVegasAsync(delegate
		{
			VegasScriptBridge.BuildMontage(_vegas, prepared, song);
		});
	}

	private void BrowseClips()
	{
		string text = SelectFolder(ClipsFolder);
		if (text != null)
		{
			ClipsFolder = text;
		}
	}

	private void BrowseSfx()
	{
		string text = SelectFolder(SfxRoot);
		if (text != null)
		{
			SfxRoot = text;
		}
	}

	private void BrowseSong()
	{
		Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
		{
			Filter = "Audio|*.mp3;*.wav;*.m4a;*.aac|All files|*.*",
			FileName = SongPath
		};
		if (openFileDialog.ShowDialog() == true)
		{
			SongPath = openFileDialog.FileName;
		}
	}

	private static string SelectFolder(string current)
	{
		using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
		if (Directory.Exists(current))
		{
			folderBrowserDialog.SelectedPath = current;
		}
		return (folderBrowserDialog.ShowDialog() == DialogResult.OK) ? folderBrowserDialog.SelectedPath : null;
	}

	private void ReportProgress(int completed, int total, string message)
	{
		Dispatch(delegate
		{
			ProgressMaximum = Math.Max(1, total);
			ProgressValue = Math.Max(0, Math.Min(ProgressMaximum, completed));
			IsIndeterminate = false;
			Status = message ?? string.Empty;
		});
	}

	private void AppendLog(string message, bool isError)
	{
		Dispatch(delegate
		{
			LogText = LogText + message + Environment.NewLine;
			((RelayCommand)ClearLogCommand).RaiseCanExecuteChanged();
		});
	}

	private void ClearLog()
	{
		LogText = string.Empty;
		((RelayCommand)ClearLogCommand).RaiseCanExecuteChanged();
	}

	private void Dispatch(Action action)
	{
		if (_dispatcher.CheckAccess())
		{
			action();
		}
		else
		{
			_dispatcher.BeginInvoke(action);
		}
	}

	private void Run(Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			Logger.LogError(ex.Message, ex);
		}
	}

	private async void RunVegas(Action action)
	{
		try
		{
			await InvokeVegasAsync(action);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Logger.LogError(ex2.Message, ex2);
		}
	}

	private Task InvokeVegasAsync(Action action)
	{
		return InvokeVegasAsync(delegate
		{
			action();
			return (object)null;
		});
	}

	private Task<T> InvokeVegasAsync<T>(Func<T> action)
	{
		TaskCompletionSource<T> completion = new TaskCompletionSource<T>();
		QueueOnVegasHost(delegate
		{
			try
			{
				completion.SetResult(action());
			}
			catch (Exception exception)
			{
				completion.SetException(exception);
			}
		});
		return completion.Task;
	}

	private void QueueOnVegasHost(Action action)
	{
		_queueVegasAction(action);
	}

	private void RefreshCommands()
	{
		foreach (RelayCommand command in _commands)
		{
			command.RaiseCanExecuteChanged();
		}
	}

	private void PathsChanged(string validityProperty)
	{
		OnPropertyChanged(validityProperty);
		RefreshCommands();
	}

	private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return false;
		}
		field = value;
		OnPropertyChanged(propertyName);
		return true;
	}

	private void OnPropertyChanged([CallerMemberName] string propertyName = null)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
