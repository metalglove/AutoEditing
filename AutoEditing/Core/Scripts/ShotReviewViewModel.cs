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

namespace Core.Scripts;

public sealed class ShotReviewViewModel : INotifyPropertyChanged, IDisposable
{
	private readonly Dispatcher _dispatcher;
	private readonly IVegasCommandClient _vegasCommands;
	private readonly IVegasQueryClient _vegasQueries;
	private readonly IVegasHostEventSource _vegasEvents;
	private readonly List<RelayCommand> _commands = new List<RelayCommand>();
	private readonly Dictionary<int, List<MarkerRow>> _reviewDrafts = new Dictionary<int, List<MarkerRow>>();
	private readonly HashSet<int> _completedReviewIndices = new HashSet<int>();
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

	internal ShotReviewViewModel(IVegasCommandClient vegasCommands, IVegasQueryClient vegasQueries, IVegasHostEventSource vegasEvents)
	{
		_vegasCommands = vegasCommands ?? throw new ArgumentNullException("vegasCommands");
		_vegasQueries = vegasQueries ?? throw new ArgumentNullException("vegasQueries");
		_vegasEvents = vegasEvents ?? throw new ArgumentNullException("vegasEvents");
		_vegasEvents.Changed += HandleVegasHostChanged;
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
		await _vegasCommands.ExecuteAsync(new LayoutAnalysisCommand { Analysis = _analysisBatch });
		_reviewDrafts.Clear();
		_completedReviewIndices.Clear();
		_reviewPosition = 0;
		SetStep(_analysisBatch.Items.Count == 0 ? WizardStep.Drawer : WizardStep.Review);
	}

	private async Task IndexSfxAsync(CancellationToken token)
	{
		await Task.Run(() => { token.ThrowIfCancellationRequested(); new ShotReviewWorkflow().CalibrateSfx(SfxRoot); }, token);
		_sfxValid = true; OnPropertyChanged("SfxValid"); RefreshCommands();
	}

	private async Task ValidateSfxAsync(CancellationToken token)
	{
		await Task.Run(() => { token.ThrowIfCancellationRequested(); new ShotReviewWorkflow().SaveCalibration(SfxRoot); }, token);
		_sfxValid = true; OnPropertyChanged("SfxValid"); RefreshCommands();
	}

	private async void RefreshMarkers()
	{
		Markers.Clear();
		if (_analysisBatch == null || _analysisBatch.Items.Count == 0) return;
		ShotReviewWorkflow.AnalysisItem item = _analysisBatch.Items[_reviewPosition];
		try
		{
			ReviewClipSnapshot snapshot = await _vegasQueries.QueryAsync(new GetReviewClipSnapshotQuery { ClipIndex = item.Index });
			if (snapshot == null || !snapshot.Exists) return;
			await _vegasCommands.ExecuteAsync(new SetCursorCommand { TimelineSeconds = snapshot.RegionStartSeconds });
			double start = snapshot.RegionStartSeconds;
			List<string> templateGuns = new List<string>();
			try
			{
				templateGuns = SfxTemplateCatalog.Load(SfxRoot).Templates
					.Select((SfxTemplate template) => template.Gun)
					.Where((string gun) => !string.IsNullOrWhiteSpace(gun))
					.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			}
			catch (Exception) { }
			List<MarkerRow> draft;
			if (_reviewDrafts.TryGetValue(item.Index, out draft))
			{
				List<ReviewMarkerSnapshot> timelineMarkers = new List<ReviewMarkerSnapshot>(snapshot.Markers);
				foreach (MarkerRow existingRow in draft)
				{
					ReviewMarkerSnapshot timelineMarker = timelineMarkers
						.Where((ReviewMarkerSnapshot marker) => marker.Label == existingRow.OriginalLabel)
						.OrderBy((ReviewMarkerSnapshot marker) => Math.Abs(marker.TimelineSeconds - existingRow.TimelineSeconds))
						.FirstOrDefault();
					if (timelineMarker != null)
					{
						existingRow.TimelineSeconds = timelineMarker.TimelineSeconds;
						existingRow.Time = (existingRow.TimelineSeconds - start).ToString("0.000s", CultureInfo.InvariantCulture);
						timelineMarkers.Remove(timelineMarker);
					}
					Markers.Add(existingRow);
				}
				return;
			}
			draft = new List<MarkerRow>();
			foreach (ReviewMarkerSnapshot marker in snapshot.Markers)
			{
				string[] parts = marker.Label.Split('|');
				string outcomeText = parts[1].Replace("HighConfidence-", string.Empty).Replace("Candidate-", string.Empty);
				ShotOutcome outcome;
				if (!Enum.TryParse(outcomeText, true, out outcome) || (outcome != ShotOutcome.Hit && outcome != ShotOutcome.Headshot && outcome != ShotOutcome.Miss)) outcome = ShotOutcome.Miss;
				string[] source = parts.Length > 3 ? parts[3].Split(new char[] { ';' }, 2) : new string[0];
				string confidenceText = source.Length > 0 ? source[0] : string.Empty;
				string templateId = source.Length > 1 ? source[1] : string.Empty;
				double detectionConfidence;
				string confidenceNumber = confidenceText.EndsWith("%", StringComparison.Ordinal) ? confidenceText.Substring(0, confidenceText.Length - 1) : confidenceText;
				if (double.TryParse(confidenceNumber, NumberStyles.Float, CultureInfo.InvariantCulture, out detectionConfidence) && confidenceText.EndsWith("%", StringComparison.Ordinal)) detectionConfidence /= 100.0;
				else if (string.IsNullOrWhiteSpace(confidenceNumber)) detectionConfidence = templateId == "manual" ? 1.0 : 0.0;
				MarkerRow row = new MarkerRow { TimelineSeconds = marker.TimelineSeconds, Time = (marker.TimelineSeconds - start).ToString("0.000s", CultureInfo.InvariantCulture), Outcome = outcome, Gun = parts.Length > 4 && !string.IsNullOrWhiteSpace(parts[4]) ? parts[4] : item.Clip.Gun, Confidence = confidenceText, DetectionConfidence = detectionConfidence, TemplateId = templateId, Origin = templateId == "manual" ? ShotEventOrigin.UserMarked : ShotEventOrigin.Detected, OriginalLabel = marker.Label };
				row.GunOptions = KnownGuns(item.Clip.Gun, row.Gun);
				foreach (string gun in templateGuns)
				{
					if (!row.GunOptions.Contains(gun, StringComparer.OrdinalIgnoreCase)) row.GunOptions.Add(gun);
				}
				draft.Add(row);
				Markers.Add(row);
			}
			_reviewDrafts[item.Index] = draft;
		}
		catch (Exception exception) { Logger.LogError("[RefreshMarkers] " + exception.Message, exception); Status = "Failed: " + exception.Message; }
		OnPropertyChanged("ReviewHeader"); RefreshCommands();
	}

	private async Task AddMarkerAtCursor(ShotOutcome outcome)
	{
		try
		{
			int index = CurrentClipIndex();
			ReviewClipSnapshot snapshot = await _vegasQueries.QueryAsync(new GetReviewClipSnapshotQuery { ClipIndex = index });
			if (snapshot == null || !snapshot.Exists) throw new InvalidOperationException("The current review clip is no longer on the timeline.");
			if (snapshot.CursorSeconds < snapshot.RegionStartSeconds || snapshot.CursorSeconds > snapshot.RegionEndSeconds) throw new InvalidOperationException("Cursor is not inside the current review clip.");
			double timelineSeconds = snapshot.CursorSeconds;
			double regionStart = snapshot.RegionStartSeconds;
			ShotReviewWorkflow.AnalysisItem item = _analysisBatch.Items[_reviewPosition];
			MarkerRow row = new MarkerRow
			{
				TimelineSeconds = timelineSeconds,
				Time = (timelineSeconds - regionStart).ToString("0.000s", CultureInfo.InvariantCulture),
				Outcome = outcome,
				Gun = item.Clip.Gun,
				Confidence = "manual",
				DetectionConfidence = 1.0,
				TemplateId = "manual",
				Origin = ShotEventOrigin.UserMarked
			};
			row.GunOptions = KnownGuns(item.Clip.Gun, row.Gun);
			List<MarkerRow> draft;
			if (!_reviewDrafts.TryGetValue(index, out draft))
			{
				draft = new List<MarkerRow>();
				_reviewDrafts[index] = draft;
			}
			draft.Add(row);
			Markers.Add(row);
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
		List<MarkerRow> draft;
		if (_reviewDrafts.TryGetValue(CurrentClipIndex(), out draft)) draft.Remove(row);
		Markers.Remove(row);
		SelectedMarker = null;
	}

	private void JumpToSelectedMarker()
	{
		MarkerRow row = SelectedMarker; if (row != null) _ = _vegasCommands.ExecuteAsync(new SetCursorCommand { TimelineSeconds = row.TimelineSeconds });
	}

	private async void MarkCurrentClipReady()
	{
		try
		{
			int index = CurrentClipIndex();
			List<ReviewMarkerSubmission> reviewedMarkers = Markers.Select((MarkerRow row) => new ReviewMarkerSubmission
			{
				TimelineSeconds = row.TimelineSeconds,
				Outcome = row.Outcome,
				Gun = row.Gun,
				DetectionConfidence = row.DetectionConfidence,
				TemplateId = row.TemplateId,
				Origin = row.Origin
			}).ToList();
			CommitClipReviewCommand command = new CommitClipReviewCommand
			{
				ClipsFolder = ClipsFolder,
				SfxRoot = SfxRoot,
				ClipIndex = index,
				ReviewedMarkers = reviewedMarkers
			};
			await _vegasCommands.ExecuteAsync(command);
			_reviewDrafts.Remove(index);
			_completedReviewIndices.Add(index);
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
			if (!_completedReviewIndices.Contains(_analysisBatch.Items[position].Index)) return position;
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
		PreparedMontage prepared = await Task.Run(() => new MontagePreparationService().Prepare(clips, SongPath), token);
		await _vegasCommands.ExecuteAsync(new BuildMontageCommand { Montage = prepared, SongPath = SongPath });
	}

	private void AddKnownFolder() { string folder = SelectFolder(ClipsFolder); if (folder != null && !_preferences.KnownClipDirectories.Contains(folder, StringComparer.OrdinalIgnoreCase)) { _preferences.KnownClipDirectories.Add(folder); ConfigurationManager.SaveUserPreferences(_preferences); RefreshDrawer(); } }
	private void DismissOnboarding() { ShowOnboarding = false; _preferences.HasSeenOnboarding = true; ConfigurationManager.SaveUserPreferences(_preferences); }
	private void ChangeClip(int delta) { int value = _reviewPosition + delta; if (_analysisBatch != null && value >= 0 && value < _analysisBatch.Items.Count) { _reviewPosition = value; RefreshMarkers(); } }
	private int CurrentClipIndex() { return _analysisBatch.Items[_reviewPosition].Index; }
	private static List<string> KnownGuns(string primary, string current) { HashSet<string> guns = new HashSet<string>(StringComparer.OrdinalIgnoreCase); if (!string.IsNullOrWhiteSpace(primary)) guns.Add(primary); if (!string.IsNullOrWhiteSpace(current)) guns.Add(current); return guns.OrderBy((string gun) => gun).ToList(); }

	private void BrowseClips() { string path = SelectFolder(ClipsFolder); if (path != null) ClipsFolder = path; }
	private void BrowseSfx() { string path = SelectFolder(SfxRoot); if (path != null) SfxRoot = path; }
	private void BrowseSong() { Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Audio|*.mp3;*.wav;*.m4a;*.aac|All files|*.*", FileName = SongPath }; if (dialog.ShowDialog() == true) SongPath = dialog.FileName; }
	private static string SelectFolder(string current) { using FolderBrowserDialog dialog = new FolderBrowserDialog(); if (Directory.Exists(current)) dialog.SelectedPath = current; return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null; }

	public void Cancel() { if (_operationCancellation != null) { Status = "Cancelling..."; _operationCancellation.Cancel(); } }
	public void Dispose() { _operationCancellation?.Cancel(); _vegasEvents.Changed -= HandleVegasHostChanged; _vegasEvents.Dispose(); Logger.SetSink(null); }
	private void HandleVegasHostChanged(object sender, VegasHostEventArgs args)
	{
		if (args.Kind == VegasHostEventKind.ProjectClosed)
		{
			Dispatch(() => { _reviewDrafts.Clear(); _completedReviewIndices.Clear(); Markers.Clear(); Status = "VEGAS project closed"; });
		}
		else if (args.Kind == VegasHostEventKind.MarkersChanged && IsReviewStep && !IsBusy)
		{
			Dispatch(() => Status = "Timeline markers changed; use Refresh after nudge to update the review draft.");
		}
	}
	private RelayCommand Command(Action execute, Func<bool> canExecute) { RelayCommand command = new RelayCommand(execute, canExecute); _commands.Add(command); return command; }
	private async Task RunBusyAsync(string operation, Func<CancellationToken, Task> action) { if (_operationCancellation != null) return; _operationCancellation = new CancellationTokenSource(); IsBusy = true; IsIndeterminate = true; Status = operation; try { await action(_operationCancellation.Token); ReportProgress(1, 1, operation + " complete"); } catch (OperationCanceledException) { Status = "Cancelled"; } catch (Exception exception) { Logger.LogError(exception.Message, exception); Status = "Failed: " + exception.Message; } finally { _operationCancellation.Dispose(); _operationCancellation = null; IsIndeterminate = false; IsBusy = false; } }
	private void ReportProgress(int completed, int total, string message) { Dispatch(() => { ProgressMaximum = Math.Max(1, total); ProgressValue = Math.Max(0, Math.Min(ProgressMaximum, completed)); IsIndeterminate = false; Status = message ?? string.Empty; }); }
	private void AppendLog(string message, bool isError) { Dispatch(() => { LogText += message + Environment.NewLine; ((RelayCommand)ClearLogCommand).RaiseCanExecuteChanged(); }); }
	private void ClearLog() { LogText = string.Empty; ((RelayCommand)ClearLogCommand).RaiseCanExecuteChanged(); }
	private void Dispatch(Action action) { if (_dispatcher.CheckAccess()) action(); else _dispatcher.BeginInvoke(action); }
	private void RefreshCommands() { foreach (RelayCommand command in _commands) command.RaiseCanExecuteChanged(); }
	private void PathsChanged(string property) { OnPropertyChanged(property); RefreshCommands(); }
	private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(propertyName); return true; }
	private void OnPropertyChanged([CallerMemberName] string propertyName = null) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }
}
