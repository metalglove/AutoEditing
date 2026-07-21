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
using Core.Domain.Audio.SongAnalysis;
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
	private SongAnalysis _songAnalysisDraft;
	private readonly List<SongEventRow> _allSongEventRows = new List<SongEventRow>();
	private readonly HashSet<string> _projectedSongEventIds = new HashSet<string>(StringComparer.Ordinal);
	private SongEventRow _selectedSongEvent;
	private SongRegionRow _selectedSongRegion;
	private SongEventViewMode _songEventViewMode = SongEventViewMode.MeaningfulSyncPoints;
	private bool _syncingSongTimeline;
	private bool _songProjectionPending;
	private bool _rebuildingSongRows;

	public ObservableCollection<WizardStepDefinition> Steps { get; } = new ObservableCollection<WizardStepDefinition>();
	public ObservableCollection<MarkerRow> Markers { get; } = new ObservableCollection<MarkerRow>();
	public ObservableCollection<ClipDrawerRow> DrawerRows { get; } = new ObservableCollection<ClipDrawerRow>();
	public ObservableCollection<SongEventRow> SongEvents { get; } = new ObservableCollection<SongEventRow>();
	public ObservableCollection<SongRegionRow> SongRegions { get; } = new ObservableCollection<SongRegionRow>();
	public SongEventRow SelectedSongEvent { get => _selectedSongEvent; set { if (Set(ref _selectedSongEvent, value)) RefreshCommands(); } }
	public SongRegionRow SelectedSongRegion { get => _selectedSongRegion; set { if (Set(ref _selectedSongRegion, value) && !_rebuildingSongRows && SongEventViewMode == SongEventViewMode.SelectedRegion) { RefreshSongEventFilter(); RefreshSongProjection(); } } }
	public SongEventViewMode SongEventViewMode { get => _songEventViewMode; set { if (Set(ref _songEventViewMode, value)) { RefreshSongEventFilter(); RefreshSongProjection(); } } }
	public List<SongEventViewMode> SongEventViewModes { get; } = new List<SongEventViewMode> { SongEventViewMode.RegionsOnly, SongEventViewMode.MeaningfulSyncPoints, SongEventViewMode.SelectedRegion, SongEventViewMode.AllEvents };
	public string SongAnalysisSummary => _songAnalysisDraft == null ? "Analyze the song to review its structure." : (_songAnalysisDraft.TempoBpm.HasValue ? _songAnalysisDraft.TempoBpm.Value.ToString("0.0 BPM") : "No reliable tempo") + " · " + _songAnalysisDraft.Events.Count + " events · " + _songAnalysisDraft.Regions.Count + " regions";
	public MarkerRow SelectedMarker { get => _selectedMarker; set { if (Set(ref _selectedMarker, value)) RefreshCommands(); } }

	public string ClipsFolder { get => _clipsFolder; set { if (Set(ref _clipsFolder, value)) PathsChanged("ClipsFolderExists"); } }
	public string SongPath { get => _songPath; set { if (Set(ref _songPath, value)) SongPathChanged(); } }
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
	public bool IsSongAnalysisStep => CurrentStep == WizardStep.SongAnalysis;
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
	public ICommand AnalyzeSongCommand { get; }
	public ICommand CommitSongReviewCommand { get; }
	public ICommand JumpSongEventCommand { get; }
	public ICommand DeleteSongEventCommand { get; }
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
		AnalyzeSongCommand = AsyncCommand("Analyzing song structure", AnalyzeSongAsync, () => IsIdle && SongExists);
		CommitSongReviewCommand = AsyncCommand("Committing song review", CommitSongReviewAsync, () => IsIdle && SongExists);
		JumpSongEventCommand = Command(JumpToSongEvent, () => IsIdle && SelectedSongEvent != null);
		DeleteSongEventCommand = AsyncCommand("Deleting song event", DeleteSelectedSongEventAsync, () => IsIdle && SelectedSongEvent != null);
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
		Steps.Add(new WizardStepDefinition { Step = WizardStep.SongAnalysis, Number = "2", Title = "Song map", Subtitle = "Review musical structure" });
		Steps.Add(new WizardStepDefinition { Step = WizardStep.SfxIndex, Number = "3", Title = "SFX index", Subtitle = "Validate templates" });
		Steps.Add(new WizardStepDefinition { Step = WizardStep.Analyze, Number = "4", Title = "Analyze", Subtitle = "Find candidates" });
		Steps.Add(new WizardStepDefinition { Step = WizardStep.Review, Number = "5", Title = "Review", Subtitle = "Confirm sync points" });
		Steps.Add(new WizardStepDefinition { Step = WizardStep.Drawer, Number = "6", Title = "Clip drawer", Subtitle = "Build from ready clips" });
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
			SetStep(WizardStep.SongAnalysis);
			if (_songAnalysisDraft == null) ((RelayCommand)AnalyzeSongCommand).Execute(null);
		}
		else if (CurrentStep == WizardStep.SongAnalysis)
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
		if (CurrentStep == WizardStep.SongAnalysis) return _songAnalysisDraft != null;
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
		OnPropertyChanged("IsSourcesStep"); OnPropertyChanged("IsSongAnalysisStep"); OnPropertyChanged("IsSfxStep"); OnPropertyChanged("IsAnalyzeStep"); OnPropertyChanged("IsReviewStep"); OnPropertyChanged("IsDrawerStep");
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
			bool fileExists = File.Exists(entry.LastKnownPath);
			DrawerRows.Add(new ClipDrawerRow { IsSelected = isReady && fileExists, FilePath = entry.LastKnownPath, FileExists = fileExists, Player = entry.PlayerName, Game = entry.Game, Map = entry.Map, Guns = string.Join(", ", clip.GunsUsed.Count == 0 ? new List<string> { entry.PrimaryGun } : clip.GunsUsed), IsSwap = clip.IsSwap, SyncPointCount = clip.ConfirmedKills.Count, LeadTimes = leadTimes, IsReady = isReady });
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

	private async Task AnalyzeSongAsync(CancellationToken token)
	{
		SongAnalysis analysis = await Task.Run(delegate
		{
			token.ThrowIfCancellationRequested();
			MonoAudio audio = AudioLoader.LoadMono(SongPath);
			SongIdentity identity = SongIdentity.FromFile(SongPath, audio.DurationSeconds);
			SongAnalysis proposal = new SongStructureAnalyzer().Analyze(audio, identity);
			SongAnalysisStore store = new SongAnalysisStore();
			string sidecarPath = store.GetSidecarPath(SongPath);
			SongAnalysis existing = store.Load(sidecarPath);
			if (existing != null)
			{
				proposal = new SongAnalysisReconciler().Reconcile(existing, proposal);
			}
			store.Save(sidecarPath, proposal);
			return proposal;
		}, token);
		await _vegasCommands.ExecuteAsync(new LayoutSongAnalysisCommand { SongPath = SongPath, Analysis = analysis });
		_songAnalysisDraft = analysis;
		ResetProjectedSongEvents(analysis.Events.Where(SongReviewWorkflow.IsUsefulTimelineEvent).Select((MusicEvent item) => item.Id));
		PopulateSongAnalysisRows();
		OnPropertyChanged("SongAnalysisSummary");
		RefreshSongProjection();
		Logger.Log("Song proposal saved beside the song. Review AE|MUSIC markers and AE|MUSIC_REGION regions in VEGAS.");
	}

	private async Task CommitSongReviewAsync(CancellationToken token)
	{
		SongAnalysisStore store = new SongAnalysisStore();
		string sidecarPath = store.GetSidecarPath(SongPath);
		SongAnalysis analysis = _songAnalysisDraft ?? await Task.Run(() => store.Load(sidecarPath), token);
		if (analysis == null) throw new InvalidOperationException("Analyze the song before committing its review.");
		SongReviewSnapshot snapshot = await _vegasQueries.QueryAsync(new GetSongReviewSnapshotQuery());
		ApplySongReviewSnapshot(analysis, snapshot);
		foreach (SongEventRow row in _allSongEventRows) row.Apply();
		foreach (SongRegionRow row in SongRegions) row.Apply();
		await Task.Run(delegate
		{
			token.ThrowIfCancellationRequested();
			store.Save(sidecarPath, analysis);
		}, token);
		_songAnalysisDraft = analysis;
		await _vegasCommands.ExecuteAsync(new LayoutSongAnalysisCommand { SongPath = SongPath, Analysis = analysis });
		ResetProjectedSongEvents(analysis.Events.Where(SongReviewWorkflow.IsUsefulTimelineEvent).Select((MusicEvent item) => item.Id));
		PopulateSongAnalysisRows();
		RefreshSongProjection();
		Logger.Log("Song review committed atomically: " + snapshot.Events.Count + " events and " + snapshot.Regions.Count + " regions.");
	}

	private static void ApplySongReviewSnapshot(SongAnalysis analysis, SongReviewSnapshot snapshot)
	{
		Dictionary<string, SongReviewEventSnapshot> events = snapshot.Events.ToDictionary((SongReviewEventSnapshot item) => item.Id, StringComparer.Ordinal);
		foreach (MusicEvent musicEvent in analysis.Events)
		{
			SongReviewEventSnapshot reviewed;
			if (events.TryGetValue(musicEvent.Id, out reviewed))
			{
				musicEvent.TimeSeconds = reviewed.TimeSeconds;
				musicEvent.Type = reviewed.Type;
				musicEvent.ReviewState = MusicAnalysisReviewState.Reviewed;
			}
			else if (SongReviewWorkflow.IsUsefulTimelineEvent(musicEvent)) musicEvent.ReviewState = MusicAnalysisReviewState.Rejected;
		}
		Dictionary<string, SongReviewRegionSnapshot> regions = snapshot.Regions.ToDictionary((SongReviewRegionSnapshot item) => item.Id, StringComparer.Ordinal);
		foreach (MusicRegion region in analysis.Regions)
		{
			SongReviewRegionSnapshot reviewed;
			if (regions.TryGetValue(region.Id, out reviewed))
			{
				region.StartSeconds = reviewed.StartSeconds;
				region.EndSeconds = reviewed.EndSeconds;
				region.Type = reviewed.Type;
				region.ReviewState = MusicAnalysisReviewState.Reviewed;
			}
			else region.ReviewState = MusicAnalysisReviewState.Rejected;
		}
	}

	private void PopulateSongAnalysisRows()
	{
		_rebuildingSongRows = true;
		try
		{
			string selectedEventId = _selectedSongEvent?.Model.Id;
			string selectedRegionId = _selectedSongRegion?.Model.Id;
			_allSongEventRows.Clear();
			SongRegions.Clear();
			if (_songAnalysisDraft != null)
			{
				foreach (MusicEvent musicEvent in _songAnalysisDraft.Events.OrderBy((MusicEvent item) => item.TimeSeconds)) _allSongEventRows.Add(new SongEventRow(musicEvent));
				foreach (MusicRegion region in _songAnalysisDraft.Regions.OrderBy((MusicRegion item) => item.StartSeconds)) SongRegions.Add(new SongRegionRow(region));
			}
			_selectedSongEvent = _allSongEventRows.FirstOrDefault((SongEventRow row) => row.Model.Id == selectedEventId);
			_selectedSongRegion = SongRegions.FirstOrDefault((SongRegionRow row) => row.Model.Id == selectedRegionId);
			RefreshSongEventFilter();
			OnPropertyChanged("SelectedSongEvent");
			OnPropertyChanged("SelectedSongRegion");
			RefreshCommands();
		}
		finally { _rebuildingSongRows = false; }
	}

	private void RefreshSongEventFilter()
	{
		SongEvents.Clear();
		IEnumerable<SongEventRow> visible = _allSongEventRows.Where(IsSongEventVisible);
		if (SongEventViewMode == SongEventViewMode.MeaningfulSyncPoints) visible = ConsolidateMeaningfulEvents(visible);
		foreach (SongEventRow row in visible) SongEvents.Add(row);
		OnPropertyChanged("SongAnalysisSummary");
	}

	private static IEnumerable<SongEventRow> ConsolidateMeaningfulEvents(IEnumerable<SongEventRow> rows)
	{
		List<SongEventRow> ordered = rows.OrderBy((SongEventRow row) => row.Model.TimeSeconds).ToList();
		for (int index = 0; index < ordered.Count;)
		{
			List<SongEventRow> cluster = new List<SongEventRow> { ordered[index] };
			int next = index + 1;
			while (next < ordered.Count && ordered[next].Model.TimeSeconds - cluster[0].Model.TimeSeconds <= 0.03) cluster.Add(ordered[next++]);
			yield return cluster.OrderByDescending((SongEventRow row) => MeaningfulPriority(row.Model.Type)).ThenByDescending((SongEventRow row) => row.Model.Strength.GetValueOrDefault()).First();
			index = next;
		}
	}

	private static int MeaningfulPriority(MusicEventType type)
	{
		if (type == MusicEventType.Drop) return 8;
		if (type == MusicEventType.BuildHit) return 7;
		if (type == MusicEventType.Accent) return 6;
		if (type == MusicEventType.PhraseBoundary) return 5;
		if (type == MusicEventType.ManualSyncPoint) return 4;
		if (type == MusicEventType.Downbeat) return 3;
		if (type == MusicEventType.Transient) return 2;
		return 1;
	}

	private bool IsSongEventVisible(SongEventRow row)
	{
		if (SongEventViewMode == SongEventViewMode.RegionsOnly) return false;
		if (SongEventViewMode == SongEventViewMode.AllEvents) return true;
		if (SongEventViewMode == SongEventViewMode.MeaningfulSyncPoints) return IsNotableSongEvent(row.Model);
		return SelectedSongRegion != null && row.Model.TimeSeconds >= SelectedSongRegion.Model.StartSeconds && row.Model.TimeSeconds <= SelectedSongRegion.Model.EndSeconds;
	}

	private static bool IsNotableSongEvent(MusicEvent musicEvent)
	{
		return musicEvent.ReviewState != MusicAnalysisReviewState.Rejected && (SongReviewWorkflow.IsUsefulTimelineEvent(musicEvent) || musicEvent.Strength.GetValueOrDefault() >= 0.85);
	}

	private void JumpToSongEvent()
	{
		if (SelectedSongEvent != null) _ = _vegasCommands.ExecuteAsync(new SetCursorCommand { TimelineSeconds = SelectedSongEvent.Model.TimeSeconds });
	}

	private async void RefreshSongProjection()
	{
		if (_songAnalysisDraft == null || !IsSongAnalysisStep) return;
		_songProjectionPending = true;
		if (_syncingSongTimeline) return;
		while (_songProjectionPending && _songAnalysisDraft != null && IsSongAnalysisStep)
		{
			_songProjectionPending = false;
			_syncingSongTimeline = true;
			try
			{
				SongReviewSnapshot snapshot = await _vegasQueries.QueryAsync(new GetSongReviewSnapshotQuery());
				ReconcileSongTimelineDraft(snapshot);
				List<string> eventIds = SongEvents.Select((SongEventRow row) => row.Model.Id).Distinct(StringComparer.Ordinal).ToList();
				await _vegasCommands.ExecuteAsync(new UpdateSongEventProjectionCommand { Analysis = _songAnalysisDraft, EventIds = eventIds });
				ResetProjectedSongEvents(eventIds);
			}
			catch (Exception exception) { Logger.LogError("[RefreshSongProjection] " + exception.Message, exception); }
			finally { _syncingSongTimeline = false; }
		}
	}

	private void ResetProjectedSongEvents(IEnumerable<string> eventIds)
	{
		_projectedSongEventIds.Clear();
		foreach (string eventId in eventIds) _projectedSongEventIds.Add(eventId);
	}

	private async Task DeleteSelectedSongEventAsync(CancellationToken token)
	{
		SongEventRow row = SelectedSongEvent;
		if (row == null) return;
		_syncingSongTimeline = true;
		try
		{
			await _vegasCommands.ExecuteAsync(new RemoveSongEventCommand { EventId = row.Model.Id });
			_songAnalysisDraft.Events.Remove(row.Model);
			_projectedSongEventIds.Remove(row.Model.Id);
			_allSongEventRows.Remove(row);
			SongEvents.Remove(row);
			SelectedSongEvent = null;
		}
		finally { _syncingSongTimeline = false; if (_songProjectionPending) RefreshSongProjection(); }
	}

	private void AddKnownFolder() { string folder = SelectFolder(ClipsFolder); if (folder != null && !_preferences.KnownClipDirectories.Contains(folder, StringComparer.OrdinalIgnoreCase)) { _preferences.KnownClipDirectories.Add(folder); ConfigurationManager.SaveUserPreferences(_preferences); RefreshDrawer(); } }
	private void DismissOnboarding() { ShowOnboarding = false; _preferences.HasSeenOnboarding = true; ConfigurationManager.SaveUserPreferences(_preferences); }
	private void ChangeClip(int delta) { int value = _reviewPosition + delta; if (_analysisBatch != null && value >= 0 && value < _analysisBatch.Items.Count) { _reviewPosition = value; RefreshMarkers(); } }
	private int CurrentClipIndex() { return _analysisBatch.Items[_reviewPosition].Index; }
	private static List<string> KnownGuns(string primary, string current) { HashSet<string> guns = new HashSet<string>(StringComparer.OrdinalIgnoreCase); if (!string.IsNullOrWhiteSpace(primary)) guns.Add(primary); if (!string.IsNullOrWhiteSpace(current)) guns.Add(current); return guns.OrderBy((string gun) => gun).ToList(); }

	private void BrowseClips() { string path = SelectFolder(ClipsFolder); if (path != null) ClipsFolder = path; }
	private void BrowseSfx() { string path = SelectFolder(SfxRoot); if (path != null) SfxRoot = path; }
	private void BrowseSong() { Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Audio|*.mp3;*.wav;*.m4a;*.aac;*.flac|All files|*.*", FileName = SongPath }; if (dialog.ShowDialog() == true) SongPath = dialog.FileName; }
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
		else if (args.Kind == VegasHostEventKind.MarkersChanged && IsSongAnalysisStep && !IsBusy && !_syncingSongTimeline)
		{
			RefreshSongTimelineDraft();
		}
		else if (args.Kind == VegasHostEventKind.CursorChanged && args.CursorSeconds.HasValue && IsSongAnalysisStep && !IsBusy)
		{
			double cursor = args.CursorSeconds.Value;
			Dispatch(() => SelectSongEventAtCursor(cursor));
		}
	}
	private async void RefreshSongTimelineDraft()
	{
		if (_syncingSongTimeline || _songAnalysisDraft == null) return;
		_syncingSongTimeline = true;
		try
		{
			SongReviewSnapshot snapshot = await _vegasQueries.QueryAsync(new GetSongReviewSnapshotQuery());
			Dispatch(() => ReconcileSongTimelineDraft(snapshot));
		}
		catch (Exception exception) { Logger.LogError("[RefreshSongTimelineDraft] " + exception.Message, exception); }
		finally { _syncingSongTimeline = false; if (_songProjectionPending) RefreshSongProjection(); }
	}
	private void ReconcileSongTimelineDraft(SongReviewSnapshot snapshot)
	{
		Dictionary<string, SongReviewEventSnapshot> events = snapshot.Events.ToDictionary((SongReviewEventSnapshot item) => item.Id, StringComparer.Ordinal);
		foreach (MusicEvent musicEvent in _songAnalysisDraft.Events.ToList())
		{
			SongReviewEventSnapshot timelineEvent;
			if (events.TryGetValue(musicEvent.Id, out timelineEvent))
			{
				musicEvent.TimeSeconds = timelineEvent.TimeSeconds;
				musicEvent.Type = timelineEvent.Type;
			}
			else if (_projectedSongEventIds.Contains(musicEvent.Id)) _songAnalysisDraft.Events.Remove(musicEvent);
		}
		Dictionary<string, SongReviewRegionSnapshot> regions = snapshot.Regions.ToDictionary((SongReviewRegionSnapshot item) => item.Id, StringComparer.Ordinal);
		foreach (MusicRegion region in _songAnalysisDraft.Regions.ToList())
		{
			SongReviewRegionSnapshot timelineRegion;
			if (regions.TryGetValue(region.Id, out timelineRegion))
			{
				region.StartSeconds = timelineRegion.StartSeconds;
				region.EndSeconds = timelineRegion.EndSeconds;
				region.Type = timelineRegion.Type;
			}
			else if (region.ReviewState != MusicAnalysisReviewState.Rejected) _songAnalysisDraft.Regions.Remove(region);
		}
		PopulateSongAnalysisRows();
		Status = "Song timeline changes synchronized to the review grid.";
	}
	private void SelectSongEventAtCursor(double cursorSeconds)
	{
		SongEventRow nearest = _allSongEventRows.OrderBy((SongEventRow row) => Math.Abs(row.Model.TimeSeconds - cursorSeconds)).FirstOrDefault();
		if (nearest == null || Math.Abs(nearest.Model.TimeSeconds - cursorSeconds) > 0.005) return;
		if (!SongEvents.Contains(nearest)) SongEvents.Add(nearest);
		SelectedSongEvent = nearest;
	}
	private RelayCommand Command(Action execute, Func<bool> canExecute) { RelayCommand command = new RelayCommand(execute, canExecute); _commands.Add(command); return command; }
	private async Task RunBusyAsync(string operation, Func<CancellationToken, Task> action) { if (_operationCancellation != null) return; _operationCancellation = new CancellationTokenSource(); IsBusy = true; IsIndeterminate = true; Status = operation; try { await action(_operationCancellation.Token); ReportProgress(1, 1, operation + " complete"); } catch (OperationCanceledException) { Status = "Cancelled"; } catch (Exception exception) { Logger.LogError(exception.Message, exception); Status = "Failed: " + exception.Message; } finally { _operationCancellation.Dispose(); _operationCancellation = null; IsIndeterminate = false; IsBusy = false; } }
	private void ReportProgress(int completed, int total, string message) { Dispatch(() => { ProgressMaximum = Math.Max(1, total); ProgressValue = Math.Max(0, Math.Min(ProgressMaximum, completed)); IsIndeterminate = false; Status = message ?? string.Empty; }); }
	private void AppendLog(string message, bool isError) { Dispatch(() => { LogText += message + Environment.NewLine; ((RelayCommand)ClearLogCommand).RaiseCanExecuteChanged(); }); }
	private void ClearLog() { LogText = string.Empty; ((RelayCommand)ClearLogCommand).RaiseCanExecuteChanged(); }
	private void Dispatch(Action action) { if (_dispatcher.CheckAccess()) action(); else _dispatcher.BeginInvoke(action); }
	private void RefreshCommands() { foreach (RelayCommand command in _commands) command.RaiseCanExecuteChanged(); }
	private void PathsChanged(string property) { OnPropertyChanged(property); RefreshCommands(); }
	private void SongPathChanged() { _songAnalysisDraft = null; _allSongEventRows.Clear(); _projectedSongEventIds.Clear(); SongEvents.Clear(); SongRegions.Clear(); OnPropertyChanged("SongExists"); OnPropertyChanged("SongAnalysisSummary"); RefreshCommands(); }
	private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(propertyName); return true; }
	private void OnPropertyChanged([CallerMemberName] string propertyName = null) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }
}
