using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
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
	private readonly UserPreferences _preferences;
	private CancellationTokenSource _operationCancellation;
	private ShotReviewWorkflow.AnalysisBatch _analysisBatch;
	private string _clipsFolder;
	private string _songPath;
	private string _sfxRoot;
	private string _status = "Ready";
	private string _logText = string.Empty;
	private bool _isBusy;
	private bool _isIndeterminate;
	private bool _showOnboarding;
	private bool _sfxValid;
	private int _progressValue;
	private int _progressMaximum = 1;
	private int _reviewPosition;
	private WizardStep _currentStep;
	private MarkerRow _selectedMarker;

	public ObservableCollection<WizardStepDefinition> Steps { get; } = new ObservableCollection<WizardStepDefinition>();
	public ObservableCollection<MarkerRow> Markers { get; } = new ObservableCollection<MarkerRow>();
	public ObservableCollection<ClipDrawerRow> DrawerRows { get; } = new ObservableCollection<ClipDrawerRow>();
	public MarkerRow SelectedMarker { get => _selectedMarker; set { if (Set(ref _selectedMarker, value)) RefreshCommands(); } }

	public string ClipsFolder { get => _clipsFolder; set { if (Set(ref _clipsFolder, value)) PathsChanged("ClipsFolderExists"); } }
	public string SongPath { get => _songPath; set { if (Set(ref _songPath, value)) PathsChanged("SongExists"); } }
	public string SfxRoot { get => _sfxRoot; set { if (Set(ref _sfxRoot, value)) { _sfxValid = false; PathsChanged("SfxRootExists"); OnPropertyChanged("SfxValid"); } } }
	public string Status { get => _status; private set => Set(ref _status, value); }
	public string LogText { get => _logText; private set => Set(ref _logText, value); }
	public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) { OnPropertyChanged("IsIdle"); RefreshCommands(); } } }
	public bool IsIdle => !IsBusy;
	public bool ClipsFolderExists => Directory.Exists(ClipsFolder);
	public bool SongExists => File.Exists(SongPath);
	public bool SfxRootExists => Directory.Exists(SfxRoot);
	public bool SfxValid => _sfxValid;
	public bool IsIndeterminate { get => _isIndeterminate; private set => Set(ref _isIndeterminate, value); }
	public int ProgressValue { get => _progressValue; private set => Set(ref _progressValue, value); }
	public int ProgressMaximum { get => _progressMaximum; private set => Set(ref _progressMaximum, value); }
	public bool ShowOnboarding { get => _showOnboarding; private set => Set(ref _showOnboarding, value); }
	public WizardStep CurrentStep { get => _currentStep; private set { if (Set(ref _currentStep, value)) UpdateStepState(); } }
	public bool IsSourcesStep => CurrentStep == WizardStep.Sources;
	public bool IsSfxStep => CurrentStep == WizardStep.SfxIndex;
	public bool IsAnalyzeStep => CurrentStep == WizardStep.Analyze;
	public bool IsReviewStep => CurrentStep == WizardStep.Review;
	public bool IsDrawerStep => CurrentStep == WizardStep.Drawer;
	public string ReviewHeader => _analysisBatch == null || _analysisBatch.Items.Count == 0 ? "No clips to review" : "Clip " + (_reviewPosition + 1) + " of " + _analysisBatch.Items.Count + " · " + Path.GetFileName(_analysisBatch.Items[_reviewPosition].Clip.FilePath);

	public ICommand BrowseClipsCommand { get; }
	public ICommand BrowseSongCommand { get; }
	public ICommand BrowseSfxCommand { get; }
	public ICommand IndexSfxCommand { get; }
	public ICommand ValidateSfxCommand { get; }
	public ICommand AnalyzeCommand { get; }
	public ICommand BuildMontageCommand { get; }
	public ICommand CancelCommand { get; }
	public ICommand ClearLogCommand { get; }
	public ICommand NextStepCommand { get; }
	public ICommand PreviousStepCommand { get; }
	public ICommand PreviousClipCommand { get; }
	public ICommand NextClipCommand { get; }
	public ICommand RefreshMarkersCommand { get; }
	public ICommand DeleteMarkerCommand { get; }
	public ICommand JumpMarkerCommand { get; }
	public ICommand AddHitCommand { get; }
	public ICommand AddHeadshotCommand { get; }
	public ICommand AddMissCommand { get; }
	public ICommand MarkClipReadyCommand { get; }
	public ICommand AddKnownFolderCommand { get; }
	public ICommand DismissOnboardingCommand { get; }
	public ICommand ShowOnboardingCommand { get; }

	public event PropertyChangedEventHandler PropertyChanged;

	public ShotReviewViewModel(Vegas vegas, Action<Action> queueVegasAction)
	{
		_vegas = vegas;
		_queueVegasAction = queueVegasAction ?? throw new ArgumentNullException("queueVegasAction");
		_dispatcher = Dispatcher.CurrentDispatcher;
		_clipsFolder = ConfigurationManager.GetQuickTestingClipsFolder();
		_songPath = ConfigurationManager.GetQuickTestingSongPath();
		_sfxRoot = ConfigurationManager.GetShotDetection().SfxRoot;
		_preferences = ConfigurationManager.LoadUserPreferences();
		_showOnboarding = !_preferences.HasSeenOnboarding;
		InitializeSteps();
		BrowseClipsCommand = Command(BrowseClips, () => IsIdle);
		BrowseSongCommand = Command(BrowseSong, () => IsIdle);
		BrowseSfxCommand = Command(BrowseSfx, () => IsIdle);
		IndexSfxCommand = AsyncCommand("Indexing SFX templates", IndexSfxAsync, () => IsIdle && SfxRootExists);
		ValidateSfxCommand = AsyncCommand("Validating SFX index", ValidateSfxAsync, () => IsIdle && SfxRootExists);
		AnalyzeCommand = AsyncCommand("Analyzing clips", AnalyzeClipsAsync, () => IsIdle && ClipsFolderExists && SfxRootExists);
		BuildMontageCommand = AsyncCommand("Building montage", BuildFromLibraryAsync, () => IsIdle && SongExists);
		CancelCommand = Command(Cancel, () => IsBusy);
		ClearLogCommand = Command(ClearLog, () => IsIdle && LogText.Length > 0);
		NextStepCommand = Command(NextStep, CanGoNext);
		PreviousStepCommand = Command(() => SetStep((WizardStep)Math.Max(0, (int)CurrentStep - 1)), () => IsIdle && CurrentStep != WizardStep.Sources);
		PreviousClipCommand = Command(() => ChangeClip(-1), () => IsIdle && _reviewPosition > 0);
		NextClipCommand = Command(() => ChangeClip(1), () => IsIdle && _analysisBatch != null && _reviewPosition + 1 < _analysisBatch.Items.Count);
		RefreshMarkersCommand = Command(RefreshMarkers, () => IsIdle && IsReviewStep);
		DeleteMarkerCommand = Command(DeleteSelectedMarker, () => IsIdle && SelectedMarker != null);
		JumpMarkerCommand = Command(JumpToSelectedMarker, () => IsIdle && SelectedMarker != null);
		AddHitCommand = Command(async delegate { await AddMarkerAtCursor(ShotOutcome.Hit); }, () => IsIdle && IsReviewStep);
		AddHeadshotCommand = Command(async delegate { await AddMarkerAtCursor(ShotOutcome.Headshot); }, () => IsIdle && IsReviewStep);
		AddMissCommand = Command(async delegate { await AddMarkerAtCursor(ShotOutcome.Miss); }, () => IsIdle && IsReviewStep);
		MarkClipReadyCommand = Command(MarkCurrentClipReady, () => IsIdle && _analysisBatch != null && _analysisBatch.Items.Count > 0);
		AddKnownFolderCommand = Command(AddKnownFolder, () => IsIdle);
		DismissOnboardingCommand = Command(DismissOnboarding, () => true);
		ShowOnboardingCommand = Command(() => ShowOnboarding = true, () => true);
		Logger.SetSink(AppendLog);
		RefreshDrawer();
		Logger.Log("AE wizard ready. Existing non-AE timeline objects are preserved.");
	}

	private void InitializeSteps()
	{
		Steps.Add(new WizardStepDefinition { Step = WizardStep.Sources, Number = "1", Title = "Sources", Subtitle = "Clips, song, SFX" });
		Steps.Add(new WizardStepDefinition { Step = WizardStep.SfxIndex, Number = "2", Title = "SFX index", Subtitle = "Validate templates" });
		Steps.Add(new WizardStepDefinition { Step = WizardStep.Analyze, Number = "3", Title = "Analyze", Subtitle = "Find candidates" });
		Steps.Add(new WizardStepDefinition { Step = WizardStep.Review, Number = "4", Title = "Review", Subtitle = "Confirm sync points" });
		Steps.Add(new WizardStepDefinition { Step = WizardStep.Drawer, Number = "5", Title = "Clip drawer", Subtitle = "Build from ready clips" });
		UpdateStepState();
	}

	private RelayCommand AsyncCommand(string title, Func<CancellationToken, Task> action, Func<bool> canExecute)
	{
		return Command(async delegate { await RunBusyAsync(title, action); }, canExecute);
	}

	private void NextStep()
	{
		if (CurrentStep == WizardStep.Sources)
		{
			SetStep(WizardStep.SfxIndex);
			if (SfxRootExists) ((RelayCommand)ValidateSfxCommand).Execute(null);
		}
		else if (CurrentStep == WizardStep.SfxIndex) SetStep(WizardStep.Analyze);
		else if (CurrentStep == WizardStep.Analyze) ((RelayCommand)AnalyzeCommand).Execute(null);
		else if (CurrentStep == WizardStep.Review) SetStep(WizardStep.Drawer);
	}

	private bool CanGoNext()
	{
		if (!IsIdle || CurrentStep == WizardStep.Drawer) return false;
		if (CurrentStep == WizardStep.Sources) return ClipsFolderExists && SongExists && SfxRootExists;
		if (CurrentStep == WizardStep.SfxIndex) return SfxValid;
		return true;
	}

	private void SetStep(WizardStep step)
	{
		CurrentStep = step;
		if (step == WizardStep.Review) RefreshMarkers();
		if (step == WizardStep.Drawer) RefreshDrawer();
	}

	private void UpdateStepState()
	{
		foreach (WizardStepDefinition step in Steps) step.IsCurrent = step.Step == CurrentStep;
		OnPropertyChanged("IsSourcesStep"); OnPropertyChanged("IsSfxStep"); OnPropertyChanged("IsAnalyzeStep"); OnPropertyChanged("IsReviewStep"); OnPropertyChanged("IsDrawerStep");
		OnPropertyChanged("Steps"); RefreshCommands();
	}

	private async Task AnalyzeClipsAsync(CancellationToken token)
	{
		ShotReviewWorkflow workflow = new ShotReviewWorkflow();
		_analysisBatch = await Task.Run(() => workflow.AnalyzeClipAudio(ClipsFolder, SfxRoot, ReportProgress, token), token);
		await InvokeVegasAsync(() => VegasScriptBridge.LayoutAnalysis(_vegas, _analysisBatch));
		_reviewPosition = 0;
		SetStep(_analysisBatch.Items.Count == 0 ? WizardStep.Drawer : WizardStep.Review);
	}

	private async Task IndexSfxAsync(CancellationToken token)
	{
		await Task.Run(() => { token.ThrowIfCancellationRequested(); new ShotReviewWorkflow().CalibrateSfx(_vegas, SfxRoot); }, token);
		_sfxValid = true; OnPropertyChanged("SfxValid"); RefreshCommands();
	}

	private async Task ValidateSfxAsync(CancellationToken token)
	{
		await Task.Run(() => { token.ThrowIfCancellationRequested(); new ShotReviewWorkflow().SaveCalibration(_vegas, SfxRoot); }, token);
		_sfxValid = true; OnPropertyChanged("SfxValid"); RefreshCommands();
	}

	private void RefreshMarkers()
	{
		Markers.Clear();
		if (_analysisBatch == null || _analysisBatch.Items.Count == 0) return;
		ShotReviewWorkflow.AnalysisItem item = _analysisBatch.Items[_reviewPosition];
		RunVegas("RefreshMarkers", delegate
		{
			Region region = FindRegion(item.Index);
			if (region == null) return;
			double start = Seconds(((Marker)region).Position);
			_vegas.Transport.CursorPosition = ((Marker)region).Position;
			foreach (Marker marker in ((IEnumerable<Marker>)_vegas.Project.Markers).Where((Marker value) => MarkerIndex(value.Label) == item.Index).OrderBy((Marker value) => Seconds(value.Position)))
			{
				string[] parts = marker.Label.Split('|');
				string outcomeText = parts[1].Replace("HighConfidence-", string.Empty).Replace("Candidate-", string.Empty);
				ShotOutcome outcome;
				if (!Enum.TryParse(outcomeText, true, out outcome) || (outcome != ShotOutcome.Hit && outcome != ShotOutcome.Headshot && outcome != ShotOutcome.Miss)) outcome = ShotOutcome.Miss;
				string[] source = parts.Length > 3 ? parts[3].Split(new char[] { ';' }, 2) : new string[0];
				MarkerRow row = new MarkerRow { TimelineSeconds = Seconds(marker.Position), Time = (Seconds(marker.Position) - start).ToString("0.000s", CultureInfo.InvariantCulture), Outcome = outcome, Gun = parts.Length > 4 && !string.IsNullOrWhiteSpace(parts[4]) ? parts[4] : item.Clip.Gun, Confidence = source.Length > 0 ? source[0] : string.Empty, TemplateId = source.Length > 1 ? source[1] : string.Empty, OriginalLabel = marker.Label };
				row.GunOptions = KnownGuns(item.Clip.Gun, row.Gun);
				try
				{
					foreach (string gun in SfxTemplateCatalog.Load(SfxRoot).Templates.Select((SfxTemplate template) => template.Gun).Where((string gun) => !string.IsNullOrWhiteSpace(gun)).Distinct(StringComparer.OrdinalIgnoreCase))
					{
						if (!row.GunOptions.Contains(gun, StringComparer.OrdinalIgnoreCase)) row.GunOptions.Add(gun);
					}
				}
				catch (Exception)
				{
				}
				row.Changed = PersistMarkerRowChange;
				Markers.Add(row);
			}
		});
		OnPropertyChanged("ReviewHeader"); RefreshCommands();
	}

	private void PersistMarkerRowChange(MarkerRow row) { PersistMarkerLabel(row); }

	// Rewrites a row's marker label from its current Outcome/Gun, stripping any
	// "Candidate-"/"HighConfidence-" prefix, regardless of whether the value actually
	// changed. Used both for live edits and to confirm rows the reviewer left untouched
	// because the detected outcome was already correct.
	// IMarkerCOM.SetLabel throws E_UNEXPECTED in this VEGAS build (confirmed via stack
	// trace) - mutating an existing marker's Label is not reliable, so instead remove
	// the old marker and add a replacement at the same position, mirroring the only
	// marker operations already proven to work elsewhere (Add/Remove).
	private void PersistMarkerLabel(MarkerRow row)
	{
		string oldLabel = row.OriginalLabel;
		double timelineSeconds = row.TimelineSeconds;
		string newLabel = ShotReviewWorkflow.BuildMarkerLabel(row.Outcome.ToString(), CurrentClipIndex(), row.Confidence + ";" + row.TemplateId, row.Gun);
		if (newLabel == oldLabel) return;
		RunVegas("PersistMarkerLabel", delegate
		{
			Marker marker = ((IEnumerable<Marker>)_vegas.Project.Markers).FirstOrDefault((Marker m) => m.Label == oldLabel && Math.Abs(Seconds(m.Position) - timelineSeconds) < 0.001);
			if (marker != null)
			{
				Timecode position = marker.Position;
				((BaseList<Marker>)(object)_vegas.Project.Markers).Remove(marker);
				((BaseList<Marker>)(object)_vegas.Project.Markers).Add(new Marker(position, newLabel));
			}
		});
		row.OriginalLabel = newLabel;
	}

	private void ConfirmAllMarkers()
	{
		foreach (MarkerRow row in Markers.ToList()) PersistMarkerLabel(row);
	}

	private async Task AddMarkerAtCursor(ShotOutcome outcome)
	{
		try
		{
			await InvokeVegasAsync(() => new ShotReviewWorkflow().MarkAtCursor(_vegas, outcome));
			RefreshMarkers();
		}
		catch (Exception exception)
		{
			Logger.LogError("[AddMarkerAtCursor] " + exception.Message, exception);
			Status = "Failed: " + exception.Message;
		}
	}

	private void DeleteSelectedMarker()
	{
		MarkerRow row = SelectedMarker; if (row == null) return;
		RunVegas("DeleteSelectedMarker", () => { Marker marker = FindMarker(row); if (marker != null) ((BaseList<Marker>)(object)_vegas.Project.Markers).Remove(marker); });
		RefreshMarkers();
	}

	private void JumpToSelectedMarker()
	{
		MarkerRow row = SelectedMarker; if (row != null) RunVegas("JumpToSelectedMarker", () => _vegas.Transport.CursorPosition = Timecode.FromSeconds(row.TimelineSeconds));
	}

	private async void MarkCurrentClipReady()
	{
		try
		{
			int index = CurrentClipIndex();
			ConfirmAllMarkers();
			await InvokeVegasAsync(() => VegasScriptBridge.MarkClipReady(_vegas, ClipsFolder, SfxRoot, index));
			int next = FindNextLiveClip(_reviewPosition + 1);
			if (next < 0) { SetStep(WizardStep.Drawer); } else { _reviewPosition = next; RefreshMarkers(); }
		}
		catch (Exception exception) { Logger.LogError("[MarkCurrentClipReady] " + exception.Message, exception); Status = "Failed: " + exception.Message; }
	}

	private int FindNextLiveClip(int start)
	{
		if (_analysisBatch == null) return -1;
		for (int offset = 0; offset < _analysisBatch.Items.Count; offset++)
		{
			int position = (start + offset) % _analysisBatch.Items.Count;
			if (FindRegion(_analysisBatch.Items[position].Index) != null) return position;
		}
		return -1;
	}

	private void RefreshDrawer()
	{
		DrawerRows.Clear();
		ClipSyncLibrary library = ClipSyncLibrary.Load();
		List<string> folders = new List<string>(_preferences.KnownClipDirectories); if (ClipsFolderExists) folders.Add(ClipsFolder);
		ClipParser parser = new ClipParser();
		bool anyOrphaned = library.Entries.Any((ClipSyncEntry entry) => string.IsNullOrWhiteSpace(entry.LastKnownPath) || !File.Exists(entry.LastKnownPath));
		if (anyOrphaned)
		{
			foreach (string folder in folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				foreach (Clip discovered in parser.ParseAllClips(folder)) library.Find(discovered.FilePath);
			}
			library.Save();
		}
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (ClipSyncEntry entry in library.Entries)
		{
			Clip clip = new Clip { Gun = entry.PrimaryGun, ShotEvents = entry.Events ?? new List<ShotEvent>() };
			bool isReady = entry.State == ClipSyncState.Ready;
			string leadTimes = isReady ? "Lead-ins: " + string.Join(", ", clip.LeadTimesSeconds.Select((double value) => value.ToString("0.0s", CultureInfo.InvariantCulture))) : "Needs review";
			DrawerRows.Add(new ClipDrawerRow { FilePath = entry.LastKnownPath, FileExists = File.Exists(entry.LastKnownPath), Player = entry.PlayerName, Game = entry.Game, Map = entry.Map, Guns = string.Join(", ", clip.GunsUsed.Count == 0 ? new List<string> { entry.PrimaryGun } : clip.GunsUsed), IsSwap = clip.IsSwap, SyncPointCount = clip.ConfirmedKills.Count, LeadTimes = leadTimes, IsReady = isReady });
			if (!string.IsNullOrWhiteSpace(entry.LastKnownPath)) seen.Add(Path.GetFullPath(entry.LastKnownPath));
		}
		foreach (string folder in folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			foreach (Clip clip in parser.ParseAllClips(folder)) if (seen.Add(Path.GetFullPath(clip.FilePath))) DrawerRows.Add(new ClipDrawerRow { FilePath = clip.FilePath, FileExists = true, Player = clip.PlayerName, Game = clip.Game, Map = clip.Map, Guns = clip.Gun, LeadTimes = "Not analyzed", IsReady = false });
		}
		RefreshCommands();
	}

	private async Task BuildFromLibraryAsync(CancellationToken token)
	{
		List<string> paths = DrawerRows.Where((ClipDrawerRow row) => row.IsSelected && row.IsReady).Select((ClipDrawerRow row) => row.FilePath).ToList();
		List<Clip> clips = new ShotReviewWorkflow().HydrateFromLibrary(paths);
		if (clips.Count == 0) throw new InvalidOperationException("Select at least one available ready clip.");
		MontageOrchestrator.PreparedMontage prepared = await Task.Run(() => new MontageOrchestrator().PrepareMontage(clips, SongPath), token);
		await InvokeVegasAsync(() => VegasScriptBridge.BuildMontage(_vegas, prepared, SongPath));
	}

	private void AddKnownFolder() { string folder = SelectFolder(ClipsFolder); if (folder != null && !_preferences.KnownClipDirectories.Contains(folder, StringComparer.OrdinalIgnoreCase)) { _preferences.KnownClipDirectories.Add(folder); ConfigurationManager.SaveUserPreferences(_preferences); RefreshDrawer(); } }
	private void DismissOnboarding() { ShowOnboarding = false; _preferences.HasSeenOnboarding = true; ConfigurationManager.SaveUserPreferences(_preferences); }
	private void ChangeClip(int delta) { int value = _reviewPosition + delta; if (_analysisBatch != null && value >= 0 && value < _analysisBatch.Items.Count) { _reviewPosition = value; RefreshMarkers(); } }
	private int CurrentClipIndex() { return _analysisBatch.Items[_reviewPosition].Index; }
	private Region FindRegion(int index) { string prefix = "AE|CLIP|" + index + "|"; return ((IEnumerable<Region>)_vegas.Project.Regions).FirstOrDefault((Region region) => ((Marker)region).Label != null && ((Marker)region).Label.StartsWith(prefix, StringComparison.Ordinal)); }
	private Marker FindMarker(MarkerRow row) { return ((IEnumerable<Marker>)_vegas.Project.Markers).FirstOrDefault((Marker marker) => marker.Label == row.OriginalLabel && Math.Abs(Seconds(marker.Position) - row.TimelineSeconds) < 0.001); }
	private static int MarkerIndex(string label) { string[] parts = (label ?? string.Empty).Split('|'); int index; return parts.Length >= 3 && parts[0] == "AE" && int.TryParse(parts[2], out index) ? index : -1; }
	private static double Seconds(Timecode value) { return value.ToMilliseconds() / 1000.0; }
	private static List<string> KnownGuns(string primary, string current) { HashSet<string> guns = new HashSet<string>(StringComparer.OrdinalIgnoreCase); if (!string.IsNullOrWhiteSpace(primary)) guns.Add(primary); if (!string.IsNullOrWhiteSpace(current)) guns.Add(current); return guns.OrderBy((string gun) => gun).ToList(); }

	private void BrowseClips() { string path = SelectFolder(ClipsFolder); if (path != null) ClipsFolder = path; }
	private void BrowseSfx() { string path = SelectFolder(SfxRoot); if (path != null) SfxRoot = path; }
	private void BrowseSong() { Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Audio|*.mp3;*.wav;*.m4a;*.aac|All files|*.*", FileName = SongPath }; if (dialog.ShowDialog() == true) SongPath = dialog.FileName; }
	private static string SelectFolder(string current) { using FolderBrowserDialog dialog = new FolderBrowserDialog(); if (Directory.Exists(current)) dialog.SelectedPath = current; return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null; }

	public void Cancel() { if (_operationCancellation != null) { Status = "Cancelling..."; _operationCancellation.Cancel(); } }
	public void Dispose() { _operationCancellation?.Cancel(); Logger.SetSink(null); }
	private RelayCommand Command(Action execute, Func<bool> canExecute) { RelayCommand command = new RelayCommand(execute, canExecute); _commands.Add(command); return command; }
	private async Task RunBusyAsync(string operation, Func<CancellationToken, Task> action) { if (_operationCancellation != null) return; _operationCancellation = new CancellationTokenSource(); IsBusy = true; IsIndeterminate = true; Status = operation; try { await action(_operationCancellation.Token); ReportProgress(1, 1, operation + " complete"); } catch (OperationCanceledException) { Status = "Cancelled"; } catch (Exception exception) { Logger.LogError(exception.Message, exception); Status = "Failed: " + exception.Message; } finally { _operationCancellation.Dispose(); _operationCancellation = null; IsIndeterminate = false; IsBusy = false; } }
	private void ReportProgress(int completed, int total, string message) { Dispatch(() => { ProgressMaximum = Math.Max(1, total); ProgressValue = Math.Max(0, Math.Min(ProgressMaximum, completed)); IsIndeterminate = false; Status = message ?? string.Empty; }); }
	private void AppendLog(string message, bool isError) { Dispatch(() => { LogText += message + Environment.NewLine; ((RelayCommand)ClearLogCommand).RaiseCanExecuteChanged(); }); }
	private void ClearLog() { LogText = string.Empty; ((RelayCommand)ClearLogCommand).RaiseCanExecuteChanged(); }
	private async void RunVegas(string context, Action action) { try { await InvokeVegasAsync(action); } catch (Exception exception) { Logger.LogError("[" + context + "] " + exception.Message, exception); } }
	private Task InvokeVegasAsync(Action action) { return InvokeVegasAsync(() => { action(); return (object)null; }); }
	private Task<T> InvokeVegasAsync<T>(Func<T> action) { TaskCompletionSource<T> completion = new TaskCompletionSource<T>(); _queueVegasAction(() => { try { completion.SetResult(action()); } catch (Exception exception) { completion.SetException(exception); } }); return completion.Task; }
	private void Dispatch(Action action) { if (_dispatcher.CheckAccess()) action(); else _dispatcher.BeginInvoke(action); }
	private void RefreshCommands() { foreach (RelayCommand command in _commands) command.RaiseCanExecuteChanged(); }
	private void PathsChanged(string property) { OnPropertyChanged(property); RefreshCommands(); }
	private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(propertyName); return true; }
	private void OnPropertyChanged([CallerMemberName] string propertyName = null) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }
}
