using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;
using Core.Domain;
using Core.Domain.Audio;
using Core.Domain.Audio.SongAnalysis;
using Core.Domain.Clip;
using Core.Domain.Editing;
using Core.Domain.Planning;
using Core.Domain.Logging;
using Microsoft.Win32;
using AutoEditing.Iteration.Contracts.Configuration;

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
	private SongEventViewMode _songEventViewMode = SongEventViewMode.SelectedRegion;
	private bool _syncingSongTimeline;
	private bool _songProjectionPending;
	private bool _rebuildingSongRows;
	private bool _isLogExpanded;
	private string _drawerFilter = string.Empty;
	private double _effectIntensity = 1.0;
	private double _effectDensity = 1.0;
	private bool _isAiMontageMode;
	private string _aiSteeringText = string.Empty;
	private AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope
		_aiSteeringScope =
			AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope.CurrentClip;
	private string _aiSteeringStatus = "Steering will be applied to the next AI iteration.";
	private WorkbenchIterationRow _selectedAiIteration;
	private WorkbenchPreviewRevisionRow _selectedAiPreviewA;
	private WorkbenchPreviewRevisionRow _selectedAiPreviewB;
	private readonly WorkbenchSessionProjectionService _workbenchProjection =
		new WorkbenchSessionProjectionService();
	private readonly DispatcherTimer _workbenchRefreshTimer;
	private string _activeWorkbenchSessionRoot = string.Empty;
	private int _activeWorkbenchIteration;
	private string _activeWorkbenchSessionId = string.Empty;
	private int _activeAssemblyCheckpoint;
	private long _activeAssemblyStateRevision;
	private AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase _activeAssemblyPhase;
	private bool _isAwaitingAssemblyReview;
	private bool _canFinishAiSyncPass;
	private string _aiFinishSyncPassLabel = "Accept current clip & finish synchronization early";
	private string _aiFinishSyncPassConsequence = "";
	private string _aiSessionStatus = "No active AI editing session";
	private string _aiProgressStage = "Idle";
	private string _aiTokenSummary = "No model request is active.";
	private double _aiPromptProgress;
	private bool _isAiModelProcessing;
	private string _aiUsageModel = "No usage recorded";
	private string _aiSessionUsage = "No calls in this session.";
	private string _aiLifetimeUsage = "No lifetime usage for this model.";
	private string _aiUsagePerformance = "";
	private string _aiAcceptActionLabel = "Accept VEGAS timing & plan next clip";
	private string _aiReviewGuidance =
		"You may move, trim, change duration, or apply a constant speed in VEGAS. " +
		"Deletion, extra or duplicate events, and variable velocity must be resolved before continuing.";
	private bool _hasAiReconciliationConflict;
	private string _aiReconciliationSummary = "";
	private WorkbenchReconciliationCandidateRow _selectedAiReconciliationCandidate;
	private bool _canExcludeAiReconciliationClip;
	private WorkbenchIncompleteSession _selectedIncompleteAiSession;
	private string _aiRecoveryStatus = "No recovery command queued.";
	private bool _hasAiRoughCutReview;
	private string _aiRoughCutSummary = "";
	private WorkbenchRoughCutCorrectionRow _selectedAiRoughCutCorrection;
	private bool _hasAiPolishReview;
	private string _aiPolishTitle = "";
	private string _aiPolishSummary = "";
	private WorkbenchPreviewChunkRow _selectedAiPolishPreviewChunk;
	private bool _canApproveAiPolishPlan;
	private bool _canSkipAiPolishPlan;
	private bool _canAcceptAiPolishPreview;
	private bool _canSkipAiPolishPreview;
	private bool _hasAiFinalReview;
	private bool _renderAiFinalPreview;
	private bool _hasAiCompletedReport;
	private string _aiFinalSummary = "";
	private string _aiRollbackSummary = "";
	private bool _canRollbackAiPromotion;
	private WorkbenchRoughCutChunkRow _selectedAiRoughCutChunk;
	private WorkbenchRoughCutEvidenceRow _selectedAiRoughCutEvidence;
	private WorkbenchRoughCutFindingRow _selectedAiRoughCutFinding;
	private bool _isAiSettingsOpen;
	private string _selectedInferenceProvider =
		InferenceProviderIds.LlamaCpp;
	private string _aiLlamaCppEndpoint =
		"http://192.168.1.71:18080/v1/";
	private string _aiLlamaCppModel = "";
	private string _aiOpenAiEndpoint =
		"https://api.openai.com/v1/";
	private string _aiOpenAiModel = "gpt-5.6-luna";
	private string _aiOpenAiReasoningEffort = "high";
	private string _pendingOpenAiApiKey = "";
	private bool _hasConfiguredOpenAiApiKey;
	private string _aiDeepSeekEndpoint = "https://api.deepseek.com/";
	private string _aiDeepSeekModel = "deepseek-v4-pro";
	private string _aiDeepSeekThinkingMode = "enabled";
	private string _aiDeepSeekReasoningEffort = "max";
	private string _pendingDeepSeekApiKey = "";
	private bool _hasConfiguredDeepSeekApiKey;
	private string _aiSettingsStatus =
		"Choose the inference provider used by new and resumed sessions.";
	private int _aiApiKeyClearRequestVersion;
	private string _openAiCredentialTarget =
		InferenceProviderSettings.DefaultOpenAiCredentialTarget;
	private string _deepSeekCredentialTarget =
		InferenceProviderSettings.DefaultDeepSeekCredentialTarget;

	public ObservableCollection<WizardStepDefinition> Steps { get; } = new ObservableCollection<WizardStepDefinition>();
	public ObservableCollection<MarkerRow> Markers { get; } = new ObservableCollection<MarkerRow>();
	public ObservableCollection<ClipDrawerRow> DrawerRows { get; } = new ObservableCollection<ClipDrawerRow>();
	public ObservableCollection<SongEventRow> SongEvents { get; } = new ObservableCollection<SongEventRow>();
	public ObservableCollection<SongRegionRow> SongRegions { get; } = new ObservableCollection<SongRegionRow>();
	public ObservableCollection<EffectSelectionRow> EffectSelections { get; } = new ObservableCollection<EffectSelectionRow>();
	public ObservableCollection<WorkbenchIterationRow> AiIterations { get; } = new ObservableCollection<WorkbenchIterationRow>();
	public IReadOnlyList<string> AiOpenAiReasoningEfforts { get; } =
		new[] { "minimal", "low", "medium", "high", "xhigh" };
	public IReadOnlyList<string> AiDeepSeekModels { get; } =
		new[] { "deepseek-v4-pro", "deepseek-v4-flash" };
	public IReadOnlyList<string> AiDeepSeekThinkingModes { get; } =
		new[] { "enabled", "disabled" };
	public IReadOnlyList<string> AiDeepSeekReasoningEfforts { get; } =
		new[] { "high", "max" };
	public ObservableCollection<WorkbenchEvidenceRow> AiEvidence { get; } = new ObservableCollection<WorkbenchEvidenceRow>();
	public ObservableCollection<WorkbenchTrackRow> AiCandidateTracks { get; } = new ObservableCollection<WorkbenchTrackRow>();
	public ObservableCollection<WorkbenchIncompleteSession> IncompleteAiSessions { get; } =
		new ObservableCollection<WorkbenchIncompleteSession>();
	public ObservableCollection<WorkbenchRoughCutCorrectionRow> AiRoughCutCorrections { get; } =
		new ObservableCollection<WorkbenchRoughCutCorrectionRow>();
	public ObservableCollection<WorkbenchReconciliationCandidateRow>
		AiReconciliationCandidates { get; } =
			new ObservableCollection<WorkbenchReconciliationCandidateRow>();
	public ObservableCollection<WorkbenchPolishActionRow> AiPolishActions { get; } =
		new ObservableCollection<WorkbenchPolishActionRow>();
	public ObservableCollection<WorkbenchEvidenceRow> AiPolishDiagnostics { get; } =
		new ObservableCollection<WorkbenchEvidenceRow>();
	public ObservableCollection<WorkbenchPreviewChunkRow> AiPolishPreviewChunks { get; } =
		new ObservableCollection<WorkbenchPreviewChunkRow>();
	public ObservableCollection<WorkbenchRoughCutChunkRow> AiRoughCutChunks { get; } =
		new ObservableCollection<WorkbenchRoughCutChunkRow>();
	public ObservableCollection<WorkbenchRoughCutEvidenceRow> AiRoughCutEvidence { get; } =
		new ObservableCollection<WorkbenchRoughCutEvidenceRow>();
	public ObservableCollection<WorkbenchRoughCutFindingRow> AiRoughCutFindings { get; } =
		new ObservableCollection<WorkbenchRoughCutFindingRow>();
	public ICollectionView DrawerView { get; }
	public SongEventRow SelectedSongEvent { get => _selectedSongEvent; set { if (Set(ref _selectedSongEvent, value)) { OnPropertyChanged("HasSelectedSongEvent"); RefreshCommands(); } } }
	public bool HasSelectedSongEvent => SelectedSongEvent != null;
	public SongRegionRow SelectedSongRegion { get => _selectedSongRegion; set { if (Set(ref _selectedSongRegion, value) && !_rebuildingSongRows) { if (value != null && SongEventViewMode != SongEventViewMode.SelectedRegion) { SongEventViewMode = SongEventViewMode.SelectedRegion; } else { RefreshSongEventFilter(); RefreshSongProjection(); } } } }
	public SongEventViewMode SongEventViewMode { get => _songEventViewMode; set { if (Set(ref _songEventViewMode, value)) { OnPropertyChanged("SongEventScopeTitle"); RefreshSongEventFilter(); RefreshSongProjection(); } } }
	public List<DisplayChoice<SongEventViewMode>> SongEventViewModes { get; } = new List<DisplayChoice<SongEventViewMode>> { new DisplayChoice<SongEventViewMode>(SongEventViewMode.RegionsOnly, "Regions only"), new DisplayChoice<SongEventViewMode>(SongEventViewMode.MeaningfulSyncPoints, "Meaningful sync points"), new DisplayChoice<SongEventViewMode>(SongEventViewMode.SelectedRegion, "Selected region"), new DisplayChoice<SongEventViewMode>(SongEventViewMode.AllEvents, "All detected events") };
	public string SongAnalysisSummary => _songAnalysisDraft == null ? "Analyze the song to review its structure." : (_songAnalysisDraft.TempoBpm.HasValue ? _songAnalysisDraft.TempoBpm.Value.ToString("0.0 BPM") : "No reliable tempo") + " · " + _songAnalysisDraft.Events.Count + " events · " + _songAnalysisDraft.Regions.Count + " regions";
	public string SongEventScopeTitle => SongEventViewMode == SongEventViewMode.SelectedRegion ? "SYNC POINTS IN SELECTED REGION" : SongEventViewMode == SongEventViewMode.MeaningfulSyncPoints ? "MEANINGFUL SYNC POINTS" : SongEventViewMode == SongEventViewMode.AllEvents ? "ALL DETECTED EVENTS" : "REGIONS ONLY";
	public MarkerRow SelectedMarker { get => _selectedMarker; set { if (Set(ref _selectedMarker, value)) RefreshCommands(); } }

	public string ClipsFolder { get => _clipsFolder; set { if (Set(ref _clipsFolder, value)) PathsChanged("ClipsFolderExists"); } }
	public string SongPath { get => _songPath; set { if (Set(ref _songPath, value)) SongPathChanged(); } }
	public string SfxRoot { get => _sfxRoot; set { if (Set(ref _sfxRoot, value)) { _sfxValid = false; PathsChanged("SfxRootExists"); OnPropertyChanged("SfxValid"); } } }
	public string Status { get => _status; private set => Set(ref _status, value); }
	public string LogText { get => _logText; private set => Set(ref _logText, value); }
	public bool IsLogExpanded { get => _isLogExpanded; set => Set(ref _isLogExpanded, value); }
	public string DrawerFilter { get => _drawerFilter; set { if (Set(ref _drawerFilter, value)) DrawerView.Refresh(); } }
	public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) { OnPropertyChanged("IsIdle"); RefreshCommands(); } } }
	public bool IsIdle => !IsBusy;
	public bool IsAiMontageMode { get => _isAiMontageMode; set { if (Set(ref _isAiMontageMode, value) && value) RefreshAiWorkbench(); } }
	public string AiSteeringText { get => _aiSteeringText; set { if (Set(ref _aiSteeringText, value)) RefreshCommands(); } }
	public AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope
		AiSteeringScope
	{
		get => _aiSteeringScope;
		set
		{
			if (Set(ref _aiSteeringScope, value))
			{
				OnPropertyChanged("AiSteeringConsequence");
				RefreshCommands();
			}
		}
	}
	public List<DisplayChoice<AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope>>
		AiSteeringScopes { get; } =
		new List<DisplayChoice<AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope>>
		{
			new DisplayChoice<AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope>(
				AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope.CurrentClip,
				"Current clip"),
			new DisplayChoice<AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope>(
				AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope.NextClip,
				"Next clip only"),
			new DisplayChoice<AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope>(
				AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope.RemainingSection,
				"Remaining song section"),
			new DisplayChoice<AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope>(
				AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope.GlobalRemainder,
				"All remaining clips")
		};
	public string AiSteeringConsequence =>
		AiSteeringScope switch
		{
			AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope.CurrentClip =>
				"Use Revise. The instruction changes only this proposal and preserves the accepted prefix.",
			AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope.NextClip =>
				"Accepting queues this instruction for the next clip only.",
			AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope.RemainingSection =>
				"Accepting applies this instruction to later clips in the current semantic song section.",
			_ =>
				"Accepting applies this instruction to every unaccepted clip. Accepted clips are never rewritten."
		};
	public string AiSteeringStatus { get => _aiSteeringStatus; private set => Set(ref _aiSteeringStatus, value); }
	public WorkbenchIterationRow SelectedAiIteration { get => _selectedAiIteration; set { if (Set(ref _selectedAiIteration, value)) RefreshSelectedAiIteration(); } }
	public WorkbenchPreviewRevisionRow SelectedAiPreviewA
	{
		get => _selectedAiPreviewA;
		set
		{
			if (Set(ref _selectedAiPreviewA, value))
			{
				OnPropertyChanged("AiPreviewAUri");
				OnPropertyChanged("HasAiPreviewComparison");
				OnPropertyChanged("AiPreviewComparisonSummary");
			}
		}
	}
	public WorkbenchPreviewRevisionRow SelectedAiPreviewB
	{
		get => _selectedAiPreviewB;
		set
		{
			if (Set(ref _selectedAiPreviewB, value))
			{
				OnPropertyChanged("AiPreviewBUri");
				OnPropertyChanged("AiPreviewUri");
				OnPropertyChanged("HasAiPreview");
				OnPropertyChanged("HasAiPreviewComparison");
				OnPropertyChanged("AiPreviewComparisonSummary");
			}
		}
	}
	public string AiSessionStatus { get => _aiSessionStatus; private set => Set(ref _aiSessionStatus, value); }
	public string AiModelStatus =>
		UseOpenAiProvider
			? "OpenAI / " + AiOpenAiModel + " / " + AiOpenAiReasoningEffort
			: UseDeepSeekProvider
				? "DeepSeek / " + AiDeepSeekModel + " / " +
					(AiDeepSeekThinkingMode == "enabled"
						? AiDeepSeekReasoningEffort
						: "non-thinking")
			: "llama.cpp / " +
				(string.IsNullOrWhiteSpace(AiLlamaCppModel)
					? "model not configured"
					: AiLlamaCppModel);
	public bool IsAiSettingsOpen
	{
		get => _isAiSettingsOpen;
		set => Set(ref _isAiSettingsOpen, value);
	}
	public bool UseOpenAiProvider
	{
		get => string.Equals(
			_selectedInferenceProvider,
			InferenceProviderIds.OpenAi,
			StringComparison.Ordinal);
		set
		{
			if (!value || UseOpenAiProvider)
				return;
			_selectedInferenceProvider = InferenceProviderIds.OpenAi;
			OnPropertyChanged();
			OnPropertyChanged("UseLlamaCppProvider");
			OnPropertyChanged("UseDeepSeekProvider");
			OnPropertyChanged("AiModelStatus");
			RefreshCommands();
		}
	}
	public bool UseLlamaCppProvider
	{
		get => string.Equals(
			_selectedInferenceProvider,
			InferenceProviderIds.LlamaCpp,
			StringComparison.Ordinal);
		set
		{
			if (!value || UseLlamaCppProvider)
				return;
			_selectedInferenceProvider = InferenceProviderIds.LlamaCpp;
			OnPropertyChanged();
			OnPropertyChanged("UseOpenAiProvider");
			OnPropertyChanged("UseDeepSeekProvider");
			OnPropertyChanged("AiModelStatus");
			RefreshCommands();
		}
	}
	public bool UseDeepSeekProvider
	{
		get => string.Equals(
			_selectedInferenceProvider,
			InferenceProviderIds.DeepSeek,
			StringComparison.Ordinal);
		set
		{
			if (!value || UseDeepSeekProvider)
				return;
			_selectedInferenceProvider = InferenceProviderIds.DeepSeek;
			OnPropertyChanged();
			OnPropertyChanged("UseLlamaCppProvider");
			OnPropertyChanged("UseOpenAiProvider");
			OnPropertyChanged("AiModelStatus");
			RefreshCommands();
		}
	}
	public string AiLlamaCppEndpoint
	{
		get => _aiLlamaCppEndpoint;
		set { if (Set(ref _aiLlamaCppEndpoint, value)) RefreshCommands(); }
	}
	public string AiLlamaCppModel
	{
		get => _aiLlamaCppModel;
		set
		{
			if (Set(ref _aiLlamaCppModel, value))
			{
				OnPropertyChanged("AiModelStatus");
				RefreshCommands();
			}
		}
	}
	public string AiOpenAiEndpoint
	{
		get => _aiOpenAiEndpoint;
		set { if (Set(ref _aiOpenAiEndpoint, value)) RefreshCommands(); }
	}
	public string AiOpenAiModel
	{
		get => _aiOpenAiModel;
		set
		{
			if (Set(ref _aiOpenAiModel, value))
			{
				OnPropertyChanged("AiModelStatus");
				RefreshCommands();
			}
		}
	}
	public string AiOpenAiReasoningEffort
	{
		get => _aiOpenAiReasoningEffort;
		set
		{
			if (Set(ref _aiOpenAiReasoningEffort, value))
			{
				OnPropertyChanged("AiModelStatus");
				RefreshCommands();
			}
		}
	}
	public string AiDeepSeekEndpoint
	{
		get => _aiDeepSeekEndpoint;
		set { if (Set(ref _aiDeepSeekEndpoint, value)) RefreshCommands(); }
	}
	public string AiDeepSeekModel
	{
		get => _aiDeepSeekModel;
		set
		{
			if (Set(ref _aiDeepSeekModel, value))
			{
				OnPropertyChanged("AiModelStatus");
				RefreshCommands();
			}
		}
	}
	public string AiDeepSeekThinkingMode
	{
		get => _aiDeepSeekThinkingMode;
		set
		{
			if (Set(ref _aiDeepSeekThinkingMode, value))
			{
				OnPropertyChanged("AiModelStatus");
				OnPropertyChanged("IsDeepSeekThinkingEnabled");
				RefreshCommands();
			}
		}
	}
	public bool IsDeepSeekThinkingEnabled =>
		string.Equals(
			AiDeepSeekThinkingMode,
			"enabled",
			StringComparison.Ordinal);
	public string AiDeepSeekReasoningEffort
	{
		get => _aiDeepSeekReasoningEffort;
		set
		{
			if (Set(ref _aiDeepSeekReasoningEffort, value))
			{
				OnPropertyChanged("AiModelStatus");
				RefreshCommands();
			}
		}
	}
	public bool HasConfiguredOpenAiApiKey
	{
		get => _hasConfiguredOpenAiApiKey;
		private set => Set(ref _hasConfiguredOpenAiApiKey, value);
	}
	public string AiOpenAiApiKeyState =>
		HasConfiguredOpenAiApiKey
			? "An API key is configured in Windows Credential Manager. Enter a new key only to replace it."
			: "No API key is configured.";
	public bool HasConfiguredDeepSeekApiKey
	{
		get => _hasConfiguredDeepSeekApiKey;
		private set => Set(ref _hasConfiguredDeepSeekApiKey, value);
	}
	public string AiDeepSeekApiKeyState =>
		HasConfiguredDeepSeekApiKey
			? "A DeepSeek API key is configured in Windows Credential Manager."
			: "No DeepSeek API key is configured.";
	public string AiSettingsStatus
	{
		get => _aiSettingsStatus;
		private set => Set(ref _aiSettingsStatus, value);
	}
	public int AiApiKeyClearRequestVersion
	{
		get => _aiApiKeyClearRequestVersion;
		private set => Set(ref _aiApiKeyClearRequestVersion, value);
	}
	public string AiProgressStage { get => _aiProgressStage; private set => Set(ref _aiProgressStage, value); }
	public string AiTokenSummary { get => _aiTokenSummary; private set => Set(ref _aiTokenSummary, value); }
	public double AiPromptProgress { get => _aiPromptProgress; private set => Set(ref _aiPromptProgress, value); }
	public bool IsAiModelProcessing { get => _isAiModelProcessing; private set => Set(ref _isAiModelProcessing, value); }
	public bool IsAwaitingAssemblyReview { get => _isAwaitingAssemblyReview; private set { if (Set(ref _isAwaitingAssemblyReview, value)) RefreshCommands(); } }
	public bool CanFinishAiSyncPass { get => _canFinishAiSyncPass; private set { if (Set(ref _canFinishAiSyncPass, value)) RefreshCommands(); } }
	public string AiFinishSyncPassLabel { get => _aiFinishSyncPassLabel; private set => Set(ref _aiFinishSyncPassLabel, value); }
	public string AiFinishSyncPassConsequence { get => _aiFinishSyncPassConsequence; private set => Set(ref _aiFinishSyncPassConsequence, value); }
	public string AiUsageModel { get => _aiUsageModel; private set => Set(ref _aiUsageModel, value); }
	public string AiSessionUsage { get => _aiSessionUsage; private set => Set(ref _aiSessionUsage, value); }
	public string AiLifetimeUsage { get => _aiLifetimeUsage; private set => Set(ref _aiLifetimeUsage, value); }
	public string AiUsagePerformance { get => _aiUsagePerformance; private set => Set(ref _aiUsagePerformance, value); }
	public string AiAcceptActionLabel { get => _aiAcceptActionLabel; private set => Set(ref _aiAcceptActionLabel, value); }
	public string AiReviewGuidance { get => _aiReviewGuidance; private set => Set(ref _aiReviewGuidance, value); }
	public bool HasAiReconciliationConflict
	{
		get => _hasAiReconciliationConflict;
		private set { if (Set(ref _hasAiReconciliationConflict, value)) RefreshCommands(); }
	}
	public string AiReconciliationSummary
	{
		get => _aiReconciliationSummary;
		private set => Set(ref _aiReconciliationSummary, value);
	}
	public WorkbenchReconciliationCandidateRow SelectedAiReconciliationCandidate
	{
		get => _selectedAiReconciliationCandidate;
		set
		{
			if (Set(ref _selectedAiReconciliationCandidate, value))
				RefreshCommands();
		}
	}
	public bool CanExcludeAiReconciliationClip
	{
		get => _canExcludeAiReconciliationClip;
		private set
		{
			if (Set(ref _canExcludeAiReconciliationClip, value))
				RefreshCommands();
		}
	}
	public WorkbenchIncompleteSession SelectedIncompleteAiSession
	{
		get => _selectedIncompleteAiSession;
		set
		{
			if (Set(ref _selectedIncompleteAiSession, value))
			{
				OnPropertyChanged("AiRecoveryConsequence");
				RefreshCommands();
			}
		}
	}
	public string AiRecoveryStatus { get => _aiRecoveryStatus; private set => Set(ref _aiRecoveryStatus, value); }
	public bool HasAiRoughCutReview
	{
		get => _hasAiRoughCutReview;
		private set { if (Set(ref _hasAiRoughCutReview, value)) RefreshCommands(); }
	}
	public string AiRoughCutSummary
	{
		get => _aiRoughCutSummary;
		private set => Set(ref _aiRoughCutSummary, value);
	}
	public WorkbenchRoughCutCorrectionRow SelectedAiRoughCutCorrection
	{
		get => _selectedAiRoughCutCorrection;
		set { if (Set(ref _selectedAiRoughCutCorrection, value)) RefreshCommands(); }
	}
	public bool HasAiPolishReview
	{
		get => _hasAiPolishReview;
		private set { if (Set(ref _hasAiPolishReview, value)) RefreshCommands(); }
	}
	public string AiPolishTitle
	{
		get => _aiPolishTitle;
		private set => Set(ref _aiPolishTitle, value);
	}
	public string AiPolishSummary
	{
		get => _aiPolishSummary;
		private set => Set(ref _aiPolishSummary, value);
	}
	public WorkbenchPreviewChunkRow SelectedAiPolishPreviewChunk
	{
		get => _selectedAiPolishPreviewChunk;
		set
		{
			if (Set(ref _selectedAiPolishPreviewChunk, value))
			{
				OnPropertyChanged("AiPolishPreviewUri");
				OnPropertyChanged("HasAiPolishPreview");
			}
		}
	}
	public Uri AiPolishPreviewUri
	{
		get
		{
			string path = SelectedAiPolishPreviewChunk?.AbsolutePath;
			return string.IsNullOrWhiteSpace(path) || !File.Exists(path)
				? null
				: new Uri(path, UriKind.Absolute);
		}
	}
	public bool HasAiPolishPreview => AiPolishPreviewUri != null;
	public bool CanApproveAiPolishPlan
	{
		get => _canApproveAiPolishPlan;
		private set { if (Set(ref _canApproveAiPolishPlan, value)) RefreshCommands(); }
	}
	public bool CanSkipAiPolishPlan
	{
		get => _canSkipAiPolishPlan;
		private set { if (Set(ref _canSkipAiPolishPlan, value)) RefreshCommands(); }
	}
	public bool CanAcceptAiPolishPreview
	{
		get => _canAcceptAiPolishPreview;
		private set { if (Set(ref _canAcceptAiPolishPreview, value)) RefreshCommands(); }
	}
	public bool CanSkipAiPolishPreview
	{
		get => _canSkipAiPolishPreview;
		private set { if (Set(ref _canSkipAiPolishPreview, value)) RefreshCommands(); }
	}
	public bool HasAiFinalReview
	{
		get => _hasAiFinalReview;
		private set { if (Set(ref _hasAiFinalReview, value)) RefreshCommands(); }
	}
	public bool RenderAiFinalPreview
	{
		get => _renderAiFinalPreview;
		set
		{
			if (Set(ref _renderAiFinalPreview, value))
				OnPropertyChanged("AiFinalizationConsequence");
		}
	}
	public string AiFinalizationConsequence =>
		RenderAiFinalPreview
			? "A complete validated preview is rendered first. Promotion starts only after every render chunk is verified."
			: "The exact validated candidate is promoted without an additional final render. Existing checkpoint and section previews remain available.";
	public bool HasAiCompletedReport
	{
		get => _hasAiCompletedReport;
		private set => Set(ref _hasAiCompletedReport, value);
	}
	public string AiFinalSummary
	{
		get => _aiFinalSummary;
		private set => Set(ref _aiFinalSummary, value);
	}
	public string AiRollbackSummary
	{
		get => _aiRollbackSummary;
		private set => Set(ref _aiRollbackSummary, value);
	}
	public bool CanRollbackAiPromotion
	{
		get => _canRollbackAiPromotion;
		private set { if (Set(ref _canRollbackAiPromotion, value)) RefreshCommands(); }
	}
	public WorkbenchRoughCutChunkRow SelectedAiRoughCutChunk
	{
		get => _selectedAiRoughCutChunk;
		set
		{
			if (Set(ref _selectedAiRoughCutChunk, value))
			{
				OnPropertyChanged("AiRoughCutChunkUri");
				OnPropertyChanged("HasAiRoughCutChunk");
			}
		}
	}
	public WorkbenchRoughCutEvidenceRow SelectedAiRoughCutEvidence
	{
		get => _selectedAiRoughCutEvidence;
		set
		{
			if (Set(ref _selectedAiRoughCutEvidence, value))
			{
				OnPropertyChanged("AiRoughCutEvidenceUri");
				OnPropertyChanged("HasAiRoughCutEvidenceImage");
				RefreshCommands();
			}
		}
	}
	public WorkbenchRoughCutFindingRow SelectedAiRoughCutFinding
	{
		get => _selectedAiRoughCutFinding;
		set
		{
			if (Set(ref _selectedAiRoughCutFinding, value))
			{
				SelectEvidenceForFinding(value);
				RefreshCommands();
			}
		}
	}
	public Uri AiRoughCutChunkUri =>
		SelectedAiRoughCutChunk != null &&
			File.Exists(SelectedAiRoughCutChunk.AbsolutePath)
			? new Uri(SelectedAiRoughCutChunk.AbsolutePath, UriKind.Absolute)
			: null;
	public bool HasAiRoughCutChunk => AiRoughCutChunkUri != null;
	public Uri AiRoughCutEvidenceUri =>
		SelectedAiRoughCutEvidence != null &&
			SelectedAiRoughCutEvidence.IsImage &&
			File.Exists(SelectedAiRoughCutEvidence.AbsolutePath)
			? new Uri(SelectedAiRoughCutEvidence.AbsolutePath, UriKind.Absolute)
			: null;
	public bool HasAiRoughCutEvidenceImage => AiRoughCutEvidenceUri != null;
	public string AiRecoveryConsequence => SelectedIncompleteAiSession == null
		? "Select an incomplete session to see safe recovery options."
		: SelectedIncompleteAiSession.Consequence;
	public string AiDecisionSummary => SelectedAiIteration == null ? "No iteration selected." : SelectedAiIteration.Decision;
	public string AiDecisionConfidence => SelectedAiIteration == null ? "" : SelectedAiIteration.Confidence;
	public Uri AiPreviewUri
	{
		get
		{
			string path = SelectedAiPreviewB?.AbsolutePath ??
				SelectedAiIteration?.PreviewPath;
			return string.IsNullOrWhiteSpace(path) || !File.Exists(path)
				? null
				: new Uri(path, UriKind.Absolute);
		}
	}
	public bool HasAiPreview => AiPreviewUri != null;
	public Uri AiPreviewAUri => PreviewUri(SelectedAiPreviewA);
	public Uri AiPreviewBUri => PreviewUri(SelectedAiPreviewB);
	public bool HasAiPreviewComparison =>
		AiPreviewAUri != null && AiPreviewBUri != null;
	public string AiPreviewComparisonSummary =>
		SelectedAiPreviewA == null || SelectedAiPreviewB == null
			? "Render another checkpoint preview to compare revisions."
			: "A: " + SelectedAiPreviewA.Summary +
				Environment.NewLine +
				"B: " + SelectedAiPreviewB.Summary;
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
	public bool IsEffectsStep => CurrentStep == WizardStep.Effects;
	public bool IsDrawerStep => CurrentStep == WizardStep.Drawer;
	public double EffectIntensity { get => _effectIntensity; set { if (Set(ref _effectIntensity, value)) OnPropertyChanged("EffectIntensitySummary"); } }
	public double EffectDensity { get => _effectDensity; set { if (Set(ref _effectDensity, value)) OnPropertyChanged("EffectDensitySummary"); } }
	public List<DisplayChoice<double>> EffectIntensityChoices { get; } = new List<DisplayChoice<double>>
	{
		new DisplayChoice<double>(0.65, "Subtle"),
		new DisplayChoice<double>(1.0, "Balanced"),
		new DisplayChoice<double>(1.35, "Strong")
	};
	public List<DisplayChoice<double>> EffectDensityChoices { get; } = new List<DisplayChoice<double>>
	{
		new DisplayChoice<double>(0.65, "Sparse"),
		new DisplayChoice<double>(1.0, "Balanced"),
		new DisplayChoice<double>(1.35, "Frequent")
	};
	public string EffectIntensitySummary => EffectIntensity < 0.8 ? "Subtle" : EffectIntensity > 1.2 ? "Strong" : "Balanced";
	public string EffectDensitySummary => EffectDensity < 0.8 ? "Sparse" : EffectDensity > 1.2 ? "Frequent" : "Balanced";
	public EffectSelectionOptions EffectSelection => new EffectSelectionOptions
	{
		EnableScreenPumps = EffectSelections.First(item => item.Name == "Screen pumps").IsEnabled,
		EnableFlashes = false,
		EnableShake = false,
		EnableSpeedChanges = false,
		EnableTransitions = false,
		EnableTitles = false,
		IncludeManualTreatments = true,
		Intensity = EffectIntensity,
		Density = EffectDensity
	};

	private EffectSelectionOptions CreateAiSynchronizationEffectSelection()
	{
		EffectSelectionOptions selected = EffectSelection;
		return new EffectSelectionOptions
		{
			SchemaVersion = selected.SchemaVersion,
			PresetId = selected.PresetId,
			EnableScreenPumps = selected.EnableScreenPumps,
			EnableFlashes = selected.EnableFlashes,
			EnableShake = selected.EnableShake,
			EnableTransitions = selected.EnableTransitions,
			EnableTitles = selected.EnableTitles,
			// The progressive AI pass may choose one deterministic constant
			// synchronization rate. Variable velocity remains unsupported.
			EnableSpeedChanges = true,
			IncludeManualTreatments = selected.IncludeManualTreatments,
			Intensity = selected.Intensity,
			Density = selected.Density
		};
	}
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
	public ICommand NavigateStepCommand { get; }
	public ICommand ToggleLogCommand { get; }
	public ICommand SelectAllReadyClipsCommand { get; }
	public ICommand ClearClipSelectionCommand { get; }
	public ICommand ToggleAiMontageModeCommand { get; }
	public ICommand ApplyAiSteeringCommand { get; }
	public ICommand StartAiSessionCommand { get; }
	public ICommand ReviseAiClipCommand { get; }
	public ICommand AcceptAiClipCommand { get; }
	public ICommand ResetAiClipCommand { get; }
	public ICommand CompareAiClipCommand { get; }
	public ICommand RenderAiClipPreviewCommand { get; }
	public ICommand QuickAiSteeringCommand { get; }
	public ICommand FinishAiSyncPassCommand { get; }
	public ICommand RestoreAiReconciliationProposalCommand { get; }
	public ICommand ExcludeAiReconciliationClipCommand { get; }
	public ICommand AdoptAiReconciliationEventCommand { get; }
	public ICommand DeferAiReconciliationCommand { get; }
	public ICommand ResumeAiSessionCommand { get; }
	public ICommand PauseAiSessionCommand { get; }
	public ICommand AbandonAiSessionCommand { get; }
	public ICommand ApproveAiRoughCutCorrectionCommand { get; }
	public ICommand RejectAiRoughCutCorrectionCommand { get; }
	public ICommand ApplyAiRoughCutCorrectionCommand { get; }
	public ICommand AcceptAiRoughCutCommand { get; }
	public ICommand ApproveAiPolishPlanCommand { get; }
	public ICommand SkipAiPolishPlanCommand { get; }
	public ICommand AcceptAiPolishPreviewCommand { get; }
	public ICommand SkipAiPolishPreviewCommand { get; }
	public ICommand FinalizeAiMontageCommand { get; }
	public ICommand RollbackAiPromotionCommand { get; }
	public ICommand JumpAiRoughCutFindingCommand { get; }
	public ICommand JumpAiRoughCutEvidenceCommand { get; }
	public ICommand ToggleAiSettingsCommand { get; }
	public ICommand SaveAiSettingsCommand { get; }
	public ICommand ClearOpenAiApiKeyCommand { get; }
	public ICommand ClearDeepSeekApiKeyCommand { get; }

	public event PropertyChangedEventHandler PropertyChanged;

	internal ShotReviewViewModel(IVegasCommandClient vegasCommands, IVegasQueryClient vegasQueries, IVegasHostEventSource vegasEvents)
	{
		_vegasCommands = vegasCommands ?? throw new ArgumentNullException("vegasCommands");
		_vegasQueries = vegasQueries ?? throw new ArgumentNullException("vegasQueries");
		_vegasEvents = vegasEvents ?? throw new ArgumentNullException("vegasEvents");
		_vegasEvents.Changed += HandleVegasHostChanged;
		_dispatcher = Dispatcher.CurrentDispatcher;
		_workbenchRefreshTimer = new DispatcherTimer(
			TimeSpan.FromSeconds(1),
			DispatcherPriority.Background,
			delegate { if (IsAiMontageMode) RefreshAiWorkbench(); },
			_dispatcher);
		_workbenchRefreshTimer.Start();
		_clipsFolder = ConfigurationManager.GetQuickTestingClipsFolder();
		_songPath = ConfigurationManager.GetQuickTestingSongPath();
		_sfxRoot = ConfigurationManager.GetShotDetection().SfxRoot;
		_preferences = ConfigurationManager.LoadUserPreferences();
		LoadInferenceSettings();
		_showOnboarding = !_preferences.HasSeenOnboarding;
		DrawerView = CollectionViewSource.GetDefaultView(DrawerRows);
		DrawerView.Filter = IsDrawerRowVisible;
		InitializeSteps();
		InitializeEffectSelections();
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
		NextStepCommand = Command(async delegate { await NextStepAsync(); }, CanGoNext);
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
		NavigateStepCommand = new RelayCommand(NavigateToStep, CanNavigateToStep);
		ToggleLogCommand = Command(() => IsLogExpanded = !IsLogExpanded, () => true);
		SelectAllReadyClipsCommand = Command(() => SetVisibleClipSelection(true), () => IsIdle);
		ClearClipSelectionCommand = Command(() => SetVisibleClipSelection(false), () => IsIdle);
		ToggleAiMontageModeCommand = Command(
			() => IsAiMontageMode = !IsAiMontageMode,
			() => IsIdle);
		ToggleAiSettingsCommand = Command(
			ToggleAiSettings,
			() => IsIdle);
		SaveAiSettingsCommand = Command(
			SaveInferenceSettings,
			CanSaveInferenceSettings);
		ClearOpenAiApiKeyCommand = Command(
			ClearOpenAiApiKey,
			() => IsIdle && HasConfiguredOpenAiApiKey);
		ClearDeepSeekApiKeyCommand = Command(
			ClearDeepSeekApiKey,
			() => IsIdle && HasConfiguredDeepSeekApiKey);
		ApplyAiSteeringCommand = Command(
			ApplyAiSteering,
			() => IsIdle && !string.IsNullOrWhiteSpace(AiSteeringText));
		StartAiSessionCommand = Command(
			StartAiSession,
			() => IsIdle && SongExists);
		ReviseAiClipCommand = Command(
			() => SubmitAssemblyAction(AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind.ReviseCurrentClip),
			() => IsIdle && IsAwaitingAssemblyReview);
		AcceptAiClipCommand = Command(
			() => SubmitAssemblyAction(AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind.AcceptTimelineAndContinue),
			() => IsIdle && IsAwaitingAssemblyReview);
		ResetAiClipCommand = Command(
			() => SubmitAssemblyAction(AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind.ResetCurrentClip),
			() => IsIdle && IsAwaitingAssemblyReview);
		CompareAiClipCommand = Command(
			() => SubmitAssemblyAction(AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind.CompareCurrentTimeline),
			() => IsIdle && IsAwaitingAssemblyReview);
		RenderAiClipPreviewCommand = Command(
			() => SubmitAssemblyAction(AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind.RenderCheckpointPreview),
			() => IsIdle && IsAwaitingAssemblyReview);
		QuickAiSteeringCommand = new RelayCommand(
			value => AddQuickAiSteering(value as string),
			value => IsIdle && value is string);
		FinishAiSyncPassCommand = Command(
			ConfirmAndFinishAiSyncPass,
			() => IsIdle && IsAwaitingAssemblyReview && CanFinishAiSyncPass);
		RestoreAiReconciliationProposalCommand = Command(
			() => SubmitReconciliationAction(
				AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
					.RestoreReconciliationProposal),
			() => IsIdle && HasAiReconciliationConflict);
		ExcludeAiReconciliationClipCommand = Command(
			() => SubmitReconciliationAction(
				AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
					.ExcludeReconciliationClip),
			() => IsIdle && HasAiReconciliationConflict &&
				CanExcludeAiReconciliationClip);
		AdoptAiReconciliationEventCommand = Command(
			() => SubmitReconciliationAction(
				AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
					.AdoptReconciliationEvent),
			() => IsIdle && HasAiReconciliationConflict &&
				SelectedAiReconciliationCandidate?.CanAdopt == true);
		DeferAiReconciliationCommand = Command(
			() => SubmitReconciliationAction(
				AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
					.DeferReconciliation),
			() => IsIdle && HasAiReconciliationConflict);
		ResumeAiSessionCommand = Command(
			() => SubmitAiLifecycleAction("ResumeSession"),
			() => IsIdle && SelectedIncompleteAiSession != null &&
				SelectedIncompleteAiSession.CanResume);
		PauseAiSessionCommand = Command(
			() => SubmitAiLifecycleAction("PauseSession"),
			() => IsIdle && SelectedIncompleteAiSession != null &&
				SelectedIncompleteAiSession.CanPause);
		AbandonAiSessionCommand = Command(
			() => SubmitAiLifecycleAction("AbandonSession"),
			() => IsIdle && SelectedIncompleteAiSession != null &&
				SelectedIncompleteAiSession.CanAbandon);
		ApproveAiRoughCutCorrectionCommand = Command(
			() => SubmitRoughCutAction(
				AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
					.ApproveRoughCutCorrection),
			() => IsIdle && HasAiRoughCutReview &&
				SelectedAiRoughCutCorrection != null &&
				SelectedAiRoughCutCorrection.CanApprove);
		RejectAiRoughCutCorrectionCommand = Command(
			() => SubmitRoughCutAction(
				AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
					.RejectRoughCutCorrection),
			() => IsIdle && HasAiRoughCutReview &&
				SelectedAiRoughCutCorrection != null &&
				SelectedAiRoughCutCorrection.CanReject);
		ApplyAiRoughCutCorrectionCommand = Command(
			() => SubmitRoughCutAction(
				AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
					.ApplyRoughCutCorrection),
			() => IsIdle && HasAiRoughCutReview &&
				SelectedAiRoughCutCorrection != null &&
				SelectedAiRoughCutCorrection.CanApply);
		AcceptAiRoughCutCommand = Command(
			() => SubmitRoughCutAction(
				AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
					.AcceptRoughCut),
			() => IsIdle && HasAiRoughCutReview &&
				AiRoughCutCorrections.All(item => item.IsTerminal));
		ApproveAiPolishPlanCommand = Command(
			() => SubmitPolishAction(true, false),
			() => IsIdle && CanApproveAiPolishPlan);
		SkipAiPolishPlanCommand = Command(
			() => SubmitPolishAction(false, false),
			() => IsIdle && CanSkipAiPolishPlan);
		AcceptAiPolishPreviewCommand = Command(
			() => SubmitPolishAction(true, true),
			() => IsIdle && CanAcceptAiPolishPreview);
		SkipAiPolishPreviewCommand = Command(
			() => SubmitPolishAction(false, true),
			() => IsIdle && CanSkipAiPolishPreview);
		FinalizeAiMontageCommand = Command(
			SubmitFinalizationAction,
			() => IsIdle && HasAiFinalReview);
		RollbackAiPromotionCommand = Command(
			RollbackAiPromotion,
			() => IsIdle && CanRollbackAiPromotion);
		JumpAiRoughCutFindingCommand = Command(
			() => JumpToRoughCutTime(SelectedAiRoughCutFinding?.NavigateSeconds),
			() => IsIdle && SelectedAiRoughCutFinding?.NavigateSeconds != null);
		JumpAiRoughCutEvidenceCommand = Command(
			() => JumpToRoughCutTime(
				SelectedAiRoughCutEvidence?.TimelineTimeSeconds),
			() => IsIdle &&
				SelectedAiRoughCutEvidence?.TimelineTimeSeconds != null);
		Logger.SetSink(AppendLog);
		RefreshDrawer();
		Logger.Log("AE wizard ready. Existing non-AE timeline objects are preserved.");
	}

	private void ApplyAiSteering()
	{
		try
		{
			string instruction = AiSteeringText.Trim();
			AutoEditing.Iteration.Contracts.Steering.EditSteeringDirective directive =
				_workbenchProjection.SubmitSteering(
					_activeWorkbenchSessionRoot,
					_activeWorkbenchIteration,
					instruction);
			AiSteeringStatus = "Direction queued for iteration " +
				directive.ApplicableFromIteration + ": \"" + instruction + "\"";
			AiSteeringText = string.Empty;
		}
		catch (Exception exception)
		{
			AiSteeringStatus = "Could not queue steering: " + exception.Message;
		}
	}

	private bool SubmitAssemblyAction(
		AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind kind)
	{
		try
		{
			bool hasInstruction = !string.IsNullOrWhiteSpace(AiSteeringText);
			bool revise = kind ==
				AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
					.ReviseCurrentClip;
			bool accept = kind is
				AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
					.AcceptTimelineAndContinue or
				AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
					.FinishSyncPass;
			if (hasInstruction &&
				kind == AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
					.FinishSyncPass)
				throw new InvalidOperationException(
					"Finish early has no later planning step to steer. Clear the " +
					"instruction, or accept and continue instead.");
			if (hasInstruction && revise &&
				AiSteeringScope !=
					AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope
						.CurrentClip)
				throw new InvalidOperationException(
					"Revise can use only Current clip direction. Change the scope " +
					"or accept the timeline to guide future clips.");
			if (hasInstruction && accept &&
				AiSteeringScope ==
					AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope
						.CurrentClip)
				throw new InvalidOperationException(
					"Current clip direction cannot be applied after acceptance. " +
					"Choose a future scope or clear the instruction.");
			AutoEditing.Iteration.Contracts.Assembly.AssemblySteeringScope
				submittedScope = revise || accept
					? AiSteeringScope
					: AutoEditing.Iteration.Contracts.Assembly
						.AssemblySteeringScope.CurrentClip;
			_workbenchProjection.SubmitAssemblyAction(
				_activeWorkbenchSessionRoot,
				_activeWorkbenchSessionId,
				_activeAssemblyCheckpoint,
				_activeAssemblyStateRevision,
				kind,
				AiSteeringText,
				"",
				submittedScope);
			AiSteeringStatus = kind ==
				AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind.RenderCheckpointPreview
				? "Preview requested for checkpoint " + _activeAssemblyCheckpoint +
					". VEGAS timing will not be accepted or changed."
				: kind + " submitted for checkpoint " + _activeAssemblyCheckpoint + ".";
			AiSteeringText = string.Empty;
			IsAwaitingAssemblyReview = false;
			return true;
		}
		catch (Exception exception)
		{
			AiSteeringStatus = "Could not submit assembly action: " + exception.Message;
			return false;
		}
	}

	private void ConfirmAndFinishAiSyncPass()
	{
		if (!CanFinishAiSyncPass) return;
		string consequence =
			"Finish synchronization early?\n\n" +
			AiFinishSyncPassConsequence +
			"\n\nThe current VEGAS timing will be accepted. Unused selected clips " +
			"will remain outside the montage, and the complete rough-cut audit will " +
			"report unused media and uncovered musical structure before effects begin.";
		if (System.Windows.MessageBox.Show(
				consequence,
				"Finish synchronization early",
				System.Windows.MessageBoxButton.OKCancel,
				System.Windows.MessageBoxImage.Warning) !=
			System.Windows.MessageBoxResult.OK)
			return;
		SubmitAssemblyAction(
			AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
				.FinishSyncPass);
	}

	private void SubmitRoughCutAction(
		AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind kind)
	{
		try
		{
			string targetId = kind ==
				AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind.AcceptRoughCut
					? ""
					: SelectedAiRoughCutCorrection?.CorrectionId ?? "";
			_workbenchProjection.SubmitAssemblyAction(
				_activeWorkbenchSessionRoot,
				_activeWorkbenchSessionId,
				_activeAssemblyCheckpoint,
				_activeAssemblyStateRevision,
				kind,
				AiSteeringText,
				targetId);
			AiSteeringStatus = kind + " submitted" +
				(string.IsNullOrWhiteSpace(targetId)
					? "."
					: " for " + targetId + ".");
			AiSteeringText = string.Empty;
			HasAiRoughCutReview = false;
		}
		catch (Exception exception)
		{
			AiSteeringStatus =
				"Could not submit rough-cut action: " + exception.Message;
		}
	}

	private void SubmitReconciliationAction(
		AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind kind)
	{
		if (kind != AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
				.DeferReconciliation)
		{
			string consequence;
			if (kind == AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
					.RestoreReconciliationProposal)
				consequence =
					"Restore the exact AI proposal?\n\n" +
					"This discards manual changes on candidate-owned tracks at this " +
					"checkpoint and rebuilds them from the persisted proposal.";
			else if (kind == AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
					.ExcludeReconciliationClip)
				consequence =
					"Exclude the unavailable current clip?\n\n" +
					"This removes it from the semantic assembly order, rebuilds the " +
					"remaining accepted prefix, and continues without that clip.";
			else
				consequence =
					"Adopt the selected known event?\n\n" +
					"The exact displayed event will become " +
					(SelectedAiReconciliationCandidate?.AdoptionMode ?? "part of the assembly") +
					". The semantic clip order and candidate-owned tracks are rebuilt " +
					"and validated from that choice.";
			if (System.Windows.MessageBox.Show(
					consequence,
					"Resolve synchronization conflict",
					System.Windows.MessageBoxButton.OKCancel,
					System.Windows.MessageBoxImage.Warning) !=
				System.Windows.MessageBoxResult.OK)
				return;
		}
		try
		{
			string targetId = kind ==
				AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
					.AdoptReconciliationEvent
				? SelectedAiReconciliationCandidate?.CandidateId ?? ""
				: "";
			_workbenchProjection.SubmitAssemblyAction(
				_activeWorkbenchSessionRoot,
				_activeWorkbenchSessionId,
				_activeAssemblyCheckpoint,
				_activeAssemblyStateRevision,
				kind,
				AiSteeringText,
				targetId);
			AiSteeringStatus = kind + " submitted" +
				(string.IsNullOrWhiteSpace(targetId)
					? "."
					: " for " + targetId + ".");
			AiSteeringText = string.Empty;
			HasAiReconciliationConflict = false;
		}
		catch (Exception exception)
		{
			AiSteeringStatus =
				"Could not submit reconciliation choice: " + exception.Message;
		}
	}

	private void SubmitPolishAction(bool accept, bool preview)
	{
		try
		{
			AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind kind =
				WorkbenchPolishActionPolicy.Resolve(
					_activeAssemblyPhase,
					accept,
					preview);
			if (SubmitAssemblyAction(kind))
				HasAiPolishReview = false;
		}
		catch (InvalidOperationException)
		{
			AiSteeringStatus =
				"The polish review changed before this action was submitted. Refresh and try again.";
		}
	}

	private void SubmitFinalizationAction()
	{
		string targetId = RenderAiFinalPreview
			? AutoEditing.Iteration.Contracts.Assembly.FinalizationActionTargets
				.RenderFinalPreview
			: AutoEditing.Iteration.Contracts.Assembly.FinalizationActionTargets
				.PromoteWithoutFinalPreview;
		if (System.Windows.MessageBox.Show(
				"Finalize and promote this montage?\n\n" +
				AiFinalizationConsequence + "\n\n" +
				WorkbenchConsequenceCopy.FinalizeMontage,
				"Finalize AI montage",
				System.Windows.MessageBoxButton.OKCancel,
				System.Windows.MessageBoxImage.Warning) !=
			System.Windows.MessageBoxResult.OK)
			return;
		try
		{
			_workbenchProjection.SubmitAssemblyAction(
				_activeWorkbenchSessionRoot,
				_activeWorkbenchSessionId,
				_activeAssemblyCheckpoint,
				_activeAssemblyStateRevision,
				AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind
					.FinalizeMontage,
				"",
				targetId);
			AiSteeringStatus = RenderAiFinalPreview
				? "Finalization submitted: render, verify, then promote."
				: "Finalization submitted: validate and promote without an additional render.";
			HasAiFinalReview = false;
		}
		catch (Exception exception)
		{
			AiSteeringStatus =
				"Could not submit finalization action: " + exception.Message;
		}
	}

	private void RollbackAiPromotion()
	{
		if (System.Windows.MessageBox.Show(
				"Roll back the promoted montage?\n\n" +
				"This verifies the live project and promoted timeline hashes, then restores " +
				"the original candidate track labels. Unrelated timeline content is preserved.",
				"Roll back promoted montage",
				System.Windows.MessageBoxButton.OKCancel,
				System.Windows.MessageBoxImage.Warning) !=
			System.Windows.MessageBoxResult.OK)
			return;
		try
		{
			LlmEditorCompanionProcess.Start(
				"workbench-rollback",
				_activeWorkbenchSessionId);
			AiSteeringStatus =
				"Rollback companion started. Promotion identity and timeline evidence will be verified before any label is changed.";
			CanRollbackAiPromotion = false;
		}
		catch (Exception exception)
		{
			AiSteeringStatus =
				"Could not start promotion rollback: " + exception.Message;
		}
	}

	private void SubmitAiLifecycleAction(string actionName)
	{
		WorkbenchIncompleteSession session = SelectedIncompleteAiSession;
		if (session == null) return;
		if (actionName == "AbandonSession" &&
			System.Windows.MessageBox.Show(
				"Abandon '" + session.SessionId + "'?\n\n" +
				session.AbandonConsequence,
				"Abandon AI editing session",
				System.Windows.MessageBoxButton.OKCancel,
				System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.OK)
			return;
		try
		{
			AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind kind =
				(AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind)Enum.Parse(
					typeof(AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind),
					actionName,
					false);
			if ((kind ==
					AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind.ResumeSession &&
				!session.CanResume) ||
				(kind ==
					AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind.PauseSession &&
				!session.CanPause) ||
				(kind ==
					AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind.AbandonSession &&
				!session.CanAbandon))
				throw new InvalidOperationException(
					"The selected lifecycle action is not safe in the session's current runtime state.");
			bool launchRecoveryCompanion =
				!session.HasLiveConsumer &&
				(kind ==
					AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind.ResumeSession ||
				kind ==
					AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind.AbandonSession);
			if (launchRecoveryCompanion)
			{
				LlmEditorCompanionProcess.Start(
					kind ==
						AutoEditing.Iteration.Contracts.Assembly.AssemblyActionKind.ResumeSession
						? "workbench-resume"
						: "workbench-abandon",
					session.SessionId);
			}
			else
			{
				if (!session.HasLiveConsumer)
					throw new InvalidOperationException(
						"There is no live companion process to consume this lifecycle action.");
				_workbenchProjection.SubmitAssemblyAction(
					session.SessionRoot,
					session.SessionId,
					session.Checkpoint,
					session.AssemblyStateRevision,
					kind,
					"");
			}
			AiRecoveryStatus = actionName == "ResumeSession"
				? launchRecoveryCompanion
					? "Recovery companion started. It will verify the request, project, and live VEGAS workspace before continuing."
					: "Resume queued for the live paused companion."
				: actionName == "PauseSession"
					? "Pause queued at the current durable review boundary."
					: launchRecoveryCompanion
						? "Abandon companion started. It will verify ownership before removing candidate tracks."
						: "Abandon queued for the live companion. Candidate-owned tracks will be removed.";
			RefreshIncompleteAiSessions(session.SessionId);
		}
		catch (Exception exception)
		{
			AiRecoveryStatus = "Could not queue session command: " + exception.Message;
		}
	}

	private void RefreshIncompleteAiSessions(string preferredSessionId = null)
	{
		string selectedId = preferredSessionId ??
			(SelectedIncompleteAiSession == null ? null : SelectedIncompleteAiSession.SessionId);
		IReadOnlyList<WorkbenchIncompleteSession> sessions =
			_workbenchProjection.ReadIncompleteSessions();
		IncompleteAiSessions.Clear();
		foreach (WorkbenchIncompleteSession session in sessions)
			IncompleteAiSessions.Add(session);
		SelectedIncompleteAiSession = IncompleteAiSessions.FirstOrDefault(
			value => string.Equals(value.SessionId, selectedId, StringComparison.Ordinal)) ??
			IncompleteAiSessions.FirstOrDefault();
	}

	private void StartAiSession()
	{
		try
		{
			List<string> paths = DrawerRows
				.Where(row => row.IsSelected && row.IsReady)
				.Select(row => row.FilePath)
				.ToList();
			List<Clip> clips = new ShotReviewWorkflow().HydrateFromLibrary(paths);
			if (clips.Count == 0)
				throw new InvalidOperationException("Select at least one available ready clip in the montage drawer.");

			string sessionId = "edit-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
			string sessionRoot = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"AutoEditing", "automation", "sessions", sessionId);
			string inputsRoot = Path.Combine(sessionRoot, "inputs");
			Directory.CreateDirectory(inputsRoot);
			string requestPath = Path.Combine(inputsRoot, "request.json");
			EditPlanningRequest request = new EditPlanningRequest
			{
				RequestId = sessionId,
				Clips = clips,
				SongPath = SongPath,
				SongAnalysis = LoadReviewedSongPlanningInput(),
				EffectOptions = CreateAiSynchronizationEffectSelection(),
				CreativeBrief = string.IsNullOrWhiteSpace(AiSteeringText)
					? "Create a high-quality montage using the reviewed sync points and song structure."
					: AiSteeringText.Trim()
			};
			File.WriteAllText(requestPath, EditPlanDocumentSerializer.SerializeRequest(request));

			string companionRoot = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"AutoEditing", "bin", "LlmEditor");
			string executable = Path.Combine(companionRoot, "AutoEditing.LlmEditor.exe");
			if (!File.Exists(executable))
				throw new FileNotFoundException(
					"The LLM editor companion is not deployed. Build the solution using Deploy.",
					executable);
			Process.Start(new ProcessStartInfo
			{
				FileName = executable,
				Arguments = "workbench-start --request " + QuoteArgument(requestPath) +
					" --session-id " + QuoteArgument(sessionId) +
					" --planner configured",
				WorkingDirectory = companionRoot,
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden
			});
			AiSessionStatus = sessionId + " - starting " +
				(UseOpenAiProvider ? "OpenAI" : "local llama.cpp") +
				" planning";
			AiSteeringStatus = "The initial direction was submitted with the planning request.";
		}
		catch (Exception exception)
		{
			AiSessionStatus = "Could not start AI edit: " + exception.Message;
		}
	}

	private MontageSongPlanningInput LoadReviewedSongPlanningInput()
	{
		MonoAudio audio = AudioLoader.LoadMono(SongPath);
		BeatGrid ignoredBeatGrid;
		SongAnalysis ignoredAnalysis;
		MontageSongPlanningInput input = new MontageSongPlanningInputProvider()
			.Load(SongPath, audio, out ignoredBeatGrid, out ignoredAnalysis);
		if (input.Mode != MontageSongPlanningMode.ReviewedSongMap)
			throw new InvalidOperationException(
				"AI editing requires a committed song analysis. Analyze and commit the song review first.");
		return input;
	}

	private static string QuoteArgument(string value)
	{
		return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
	}

	public void SetPendingOpenAiApiKey(string apiKey)
	{
		_pendingOpenAiApiKey = apiKey ?? "";
		RefreshCommands();
	}

	public void SetPendingDeepSeekApiKey(string apiKey)
	{
		_pendingDeepSeekApiKey = apiKey ?? "";
		RefreshCommands();
	}

	private void LoadInferenceSettings()
	{
		try
		{
			InferenceProviderSettings settings =
				InferenceProviderSettingsStore.LoadOrDefault();
			_selectedInferenceProvider = settings.Provider;
			_aiLlamaCppEndpoint = settings.LlamaCppEndpoint;
			_aiLlamaCppModel = settings.LlamaCppModel;
			_aiOpenAiEndpoint = settings.OpenAiEndpoint;
			_aiOpenAiModel = settings.OpenAiModel;
			_aiOpenAiReasoningEffort = settings.OpenAiReasoningEffort;
			_openAiCredentialTarget = settings.OpenAiCredentialTarget;
			_aiDeepSeekEndpoint = settings.DeepSeekEndpoint;
			_aiDeepSeekModel = settings.DeepSeekModel;
			_aiDeepSeekThinkingMode = settings.DeepSeekThinkingMode;
			_aiDeepSeekReasoningEffort = settings.DeepSeekReasoningEffort;
			_deepSeekCredentialTarget = settings.DeepSeekCredentialTarget;
			_hasConfiguredOpenAiApiKey =
				WindowsCredentialStore.Exists(_openAiCredentialTarget);
			_hasConfiguredDeepSeekApiKey =
				WindowsCredentialStore.Exists(_deepSeekCredentialTarget);
			_aiSettingsStatus =
				"Using " +
				(_selectedInferenceProvider == InferenceProviderIds.OpenAi
					? "OpenAI."
					: _selectedInferenceProvider == InferenceProviderIds.DeepSeek
						? "DeepSeek."
					: "llama.cpp.");
		}
		catch (Exception exception)
		{
			_aiSettingsStatus =
				"Could not load inference settings: " + exception.Message;
		}
	}

	private void ToggleAiSettings()
	{
		IsAiSettingsOpen = !IsAiSettingsOpen;
		if (!IsAiSettingsOpen)
		{
			_pendingOpenAiApiKey = "";
			_pendingDeepSeekApiKey = "";
			AiApiKeyClearRequestVersion++;
		}
	}

	private bool CanSaveInferenceSettings()
	{
		if (!IsIdle)
			return false;
		string endpoint = UseOpenAiProvider
			? AiOpenAiEndpoint
			: UseDeepSeekProvider
				? AiDeepSeekEndpoint
			: AiLlamaCppEndpoint;
		string model = UseOpenAiProvider
			? AiOpenAiModel
			: UseDeepSeekProvider
				? AiDeepSeekModel
			: AiLlamaCppModel;
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri) ||
			(uri.Scheme != Uri.UriSchemeHttp &&
			 uri.Scheme != Uri.UriSchemeHttps) ||
			string.IsNullOrWhiteSpace(model))
			return false;
		if (UseOpenAiProvider)
			return HasConfiguredOpenAiApiKey ||
				!string.IsNullOrWhiteSpace(_pendingOpenAiApiKey);
		if (UseDeepSeekProvider)
			return HasConfiguredDeepSeekApiKey ||
				!string.IsNullOrWhiteSpace(_pendingDeepSeekApiKey);
		return true;
	}

	private void SaveInferenceSettings()
	{
		try
		{
			InferenceProviderSettings settings = new InferenceProviderSettings
			{
				Provider = UseOpenAiProvider
					? InferenceProviderIds.OpenAi
					: UseDeepSeekProvider
						? InferenceProviderIds.DeepSeek
					: InferenceProviderIds.LlamaCpp,
				LlamaCppEndpoint = AiLlamaCppEndpoint,
				LlamaCppModel = AiLlamaCppModel,
				OpenAiEndpoint = AiOpenAiEndpoint,
				OpenAiModel = AiOpenAiModel,
				OpenAiReasoningEffort = AiOpenAiReasoningEffort,
				OpenAiCredentialTarget = _openAiCredentialTarget,
				DeepSeekEndpoint = AiDeepSeekEndpoint,
				DeepSeekModel = AiDeepSeekModel,
				DeepSeekThinkingMode = AiDeepSeekThinkingMode,
				DeepSeekReasoningEffort = AiDeepSeekReasoningEffort,
				DeepSeekCredentialTarget = _deepSeekCredentialTarget
			};
			settings.ValidateAndNormalize();
			if (!string.IsNullOrWhiteSpace(_pendingOpenAiApiKey))
				WindowsCredentialStore.Write(
					_openAiCredentialTarget,
					_pendingOpenAiApiKey.Trim());
			if (!string.IsNullOrWhiteSpace(_pendingDeepSeekApiKey))
				WindowsCredentialStore.Write(
					_deepSeekCredentialTarget,
					_pendingDeepSeekApiKey.Trim());
			InferenceProviderSettingsStore.Save(settings);
			_selectedInferenceProvider = settings.Provider;
			_aiLlamaCppEndpoint = settings.LlamaCppEndpoint;
			_aiLlamaCppModel = settings.LlamaCppModel;
			_aiOpenAiEndpoint = settings.OpenAiEndpoint;
			_aiOpenAiModel = settings.OpenAiModel;
			_aiOpenAiReasoningEffort = settings.OpenAiReasoningEffort;
			_aiDeepSeekEndpoint = settings.DeepSeekEndpoint;
			_aiDeepSeekModel = settings.DeepSeekModel;
			_aiDeepSeekThinkingMode = settings.DeepSeekThinkingMode;
			_aiDeepSeekReasoningEffort = settings.DeepSeekReasoningEffort;
			HasConfiguredOpenAiApiKey =
				WindowsCredentialStore.Exists(_openAiCredentialTarget);
			HasConfiguredDeepSeekApiKey =
				WindowsCredentialStore.Exists(_deepSeekCredentialTarget);
			_pendingOpenAiApiKey = "";
			_pendingDeepSeekApiKey = "";
			AiApiKeyClearRequestVersion++;
			OnPropertyChanged("AiOpenAiApiKeyState");
			OnPropertyChanged("AiDeepSeekApiKeyState");
			OnPropertyChanged("AiModelStatus");
			AiSettingsStatus =
				"Saved. New and resumed sessions will use " +
				(UseOpenAiProvider
					? "OpenAI."
					: UseDeepSeekProvider
						? "DeepSeek."
						: "llama.cpp.");
			RefreshCommands();
		}
		catch (Exception exception)
		{
			AiSettingsStatus =
				"Could not save inference settings: " + exception.Message;
		}
	}

	private void ClearOpenAiApiKey()
	{
		try
		{
			WindowsCredentialStore.Delete(_openAiCredentialTarget);
			_pendingOpenAiApiKey = "";
			HasConfiguredOpenAiApiKey = false;
			AiApiKeyClearRequestVersion++;
			OnPropertyChanged("AiOpenAiApiKeyState");
			AiSettingsStatus =
				"OpenAI API key removed from Windows Credential Manager.";
			RefreshCommands();
		}
		catch (Exception exception)
		{
			AiSettingsStatus =
				"Could not remove the OpenAI API key: " + exception.Message;
		}
	}

	private void ClearDeepSeekApiKey()
	{
		try
		{
			WindowsCredentialStore.Delete(_deepSeekCredentialTarget);
			HasConfiguredDeepSeekApiKey = false;
			_pendingDeepSeekApiKey = "";
			AiApiKeyClearRequestVersion++;
			OnPropertyChanged("AiDeepSeekApiKeyState");
			AiSettingsStatus =
				"DeepSeek API key removed from Windows Credential Manager.";
			RefreshCommands();
		}
		catch (Exception exception)
		{
			AiSettingsStatus =
				"Could not remove the DeepSeek API key: " + exception.Message;
		}
	}

	private void RefreshAiWorkbench()
	{
		try
		{
			RefreshIncompleteAiSessions();
			WorkbenchUiProjection projection = _workbenchProjection.ReadLatest();
			_activeWorkbenchSessionRoot = projection.SessionRoot;
			_activeWorkbenchIteration = projection.CurrentIteration;
			_activeWorkbenchSessionId = projection.SessionId;
			_activeAssemblyCheckpoint = projection.Assembly == null ? 0 : projection.Assembly.Checkpoint;
			_activeAssemblyStateRevision =
				projection.Assembly == null ? 0 : projection.Assembly.StateRevision;
			_activeAssemblyPhase = projection.Assembly == null
				? AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.Failed
				: projection.Assembly.Phase;
			IsAwaitingAssemblyReview = projection.Assembly != null &&
				projection.AssemblyDetails != null &&
				!projection.AssemblyDetails.HasPendingActionExecution &&
				projection.Assembly.Phase ==
					AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.AwaitingHumanReview;
			EarlySyncCompletionPresentation earlyFinish =
				EarlySyncCompletionPresentation.Create(
					projection.Assembly,
					IsAwaitingAssemblyReview);
			CanFinishAiSyncPass = earlyFinish.CanFinishEarly;
			AiFinishSyncPassLabel = earlyFinish.FinishEarlyLabel;
			AiFinishSyncPassConsequence = earlyFinish.Consequence;
			AiAcceptActionLabel = earlyFinish.AcceptLabel;
			AiSessionStatus = FormatAiSessionStatus(projection);
			RefreshAiProgress(projection.Progress);
			RefreshAiUsage(projection.Usage);
			RefreshAiRoughCut(projection);
			RefreshAiPolishAndFinalization(projection);
			RefreshAiReconciliation(projection);
			if (string.Equals(
					projection.State,
					AutoEditing.Iteration.Contracts.Sessions.EditSessionState.Failed.ToString(),
					StringComparison.Ordinal))
			{
				AiProgressStage = "Failed";
				AiTokenSummary = string.IsNullOrWhiteSpace(projection.FailureReason)
					? "Planning stopped before a VEGAS candidate could be materialized."
					: projection.FailureReason;
				AiPromptProgress = 0;
				IsAiModelProcessing = false;
				AiReviewGuidance =
					"Nothing was added to the VEGAS timeline. The model response failed " +
					"deterministic validation before materialization. Start a new AI edit " +
					"after correcting the planning input or model response.";
			}

			int selectedNumber = SelectedAiIteration == null ? 0 : SelectedAiIteration.Number;
			AiIterations.Clear();
			if (projection.AssemblyDetails != null)
			{
				foreach (WorkbenchAssemblyCheckpoint checkpoint in
					projection.AssemblyDetails.Checkpoints)
				{
					AutoEditing.Iteration.Contracts.Assembly.ClipStepDecision proposal =
						checkpoint.Proposal;
					WorkbenchIterationRow row = new WorkbenchIterationRow
					{
						Number = checkpoint.Checkpoint,
						Title = "Clip " + checkpoint.Checkpoint + " of " +
							projection.AssemblyDetails.Checkpoints.Count,
						Status = checkpoint.IsAccepted
							? "Accepted"
							: checkpoint.IsCurrent
								? string.Equals(
										projection.State,
										AutoEditing.Iteration.Contracts.Sessions
											.EditSessionState.Failed.ToString(),
										StringComparison.Ordinal)
									? "Rejected before timeline"
									: projection.Assembly.Phase.ToString()
								: "Remaining",
						Summary = Path.GetFileName(checkpoint.ClipPath),
						Confidence = proposal == null
							? ""
							: proposal.Confidence.ToString("P0"),
						Decision = proposal == null
							? checkpoint.IsCurrent
								? projection.Assembly.Status
								: "Not planned yet."
							: proposal.Rationale,
						Rationale = proposal == null
							? ""
							: "Primary sync target: reviewed music event " +
								proposal.PrimarySync.MusicEventId,
					};
					if (proposal != null)
					{
						if (projection.AssemblyDetails.ActiveSketch != null)
							row.Evidence.Add(new WorkbenchEvidenceRow
							{
								Title = "Assembly intent",
								Detail = projection.AssemblyDetails.ActiveSketch.EditorialThesis
							});
						row.Evidence.Add(new WorkbenchEvidenceRow
						{
							Title = "Proposed timing",
							Detail =
								"Source " +
								proposal.SourceWindow.StartSeconds.ToString("0.000") +
								"s-" +
								proposal.SourceWindow.EndSeconds.ToString("0.000") +
								"s at " +
								proposal.SourceWindow.ConstantSpeed.ToString("0.###") +
								"x; primary music event " +
								proposal.PrimarySync.MusicEventId + "."
						});
						foreach (AutoEditing.Iteration.Contracts.Assembly.AssemblyDecisionAlternative
							alternative in proposal.Alternatives)
							row.Evidence.Add(new WorkbenchEvidenceRow
							{
								Title = "Alternative considered",
								Detail = alternative.Description + " - " +
									alternative.RejectedBecause
							});
					}
					foreach (AutoEditing.Iteration.Contracts.Assembly
						.AssemblyProposalRejection rejection in
							checkpoint.ProposalRejections)
						row.Evidence.Add(new WorkbenchEvidenceRow
						{
							Title = "Rejected AI proposal " +
								rejection.Attempt + " of " +
								rejection.MaximumAttempts,
							Detail = rejection.Diagnostic
						});
					if (checkpoint.TimelineAdjustment != null)
					{
						foreach (AutoEditing.Iteration.Contracts.Assembly.TimelineAdjustment change
							in checkpoint.TimelineAdjustment.Changes)
							row.Evidence.Add(new WorkbenchEvidenceRow
							{
								Title = HumanizeAdjustment(change.Kind.ToString()),
								Detail =
									change.Before.ToString("0.000") + " -> " +
									change.After.ToString("0.000")
							});
					}
					if (checkpoint.Preview != null)
					{
						foreach (WorkbenchCheckpointPreview previewAttempt in
							checkpoint.Previews)
						{
							AutoEditing.Iteration.Contracts.Assembly
								.CheckpointPreviewArtifact artifact =
									previewAttempt.Artifact;
							if (artifact.Status is not
								(AutoEditing.Iteration.Contracts.Assembly
									.CheckpointPreviewStatus.Completed or
								 AutoEditing.Iteration.Contracts.Assembly
									.CheckpointPreviewStatus.ReviewFailed) ||
								string.IsNullOrWhiteSpace(
									artifact.PreviewRelativePath))
								continue;
							string attemptPath = Path.GetFullPath(Path.Combine(
								projection.SessionRoot,
								artifact.PreviewRelativePath.Replace(
									'/', Path.DirectorySeparatorChar)));
							if (!File.Exists(attemptPath)) continue;
							row.PreviewRevisions.Add(
								new WorkbenchPreviewRevisionRow
								{
									Attempt = artifact.Attempt,
									Label = "Attempt " + artifact.Attempt,
									AbsolutePath = attemptPath,
									Summary = previewAttempt.Review == null
										? artifact.Status.ToString()
										: previewAttempt.Review.Summary,
									Confidence = previewAttempt.Review == null
										? ""
										: previewAttempt.Review.Confidence
											.ToString("P0")
								});
						}
						if ((checkpoint.Preview.Status ==
								AutoEditing.Iteration.Contracts.Assembly.CheckpointPreviewStatus.Completed ||
							checkpoint.Preview.Status ==
								AutoEditing.Iteration.Contracts.Assembly.CheckpointPreviewStatus.ReviewFailed) &&
							!string.IsNullOrWhiteSpace(checkpoint.Preview.PreviewRelativePath))
						{
							string previewPath = Path.GetFullPath(Path.Combine(
								projection.SessionRoot,
								checkpoint.Preview.PreviewRelativePath.Replace(
									'/', Path.DirectorySeparatorChar)));
							if (File.Exists(previewPath))
								row.PreviewPath = previewPath;
						}
						string detail = checkpoint.Preview.Status ==
							AutoEditing.Iteration.Contracts.Assembly.CheckpointPreviewStatus.Completed
								? "Rendered " +
									(checkpoint.Preview.Window.End -
										checkpoint.Preview.Window.Start).TotalSeconds.ToString("0.0") +
									"s with timing evidence and reviewer observations."
								: checkpoint.Preview.Status +
									(string.IsNullOrWhiteSpace(checkpoint.Preview.FailureCode)
										? ""
										: " [" + checkpoint.Preview.FailureCode + "]") +
									(string.IsNullOrWhiteSpace(checkpoint.Preview.FailureMessage)
										? ""
										: " - " + checkpoint.Preview.FailureMessage);
						row.Evidence.Add(new WorkbenchEvidenceRow
						{
							Title = checkpoint.Preview.Status ==
								AutoEditing.Iteration.Contracts.Assembly.CheckpointPreviewStatus.Completed
									? "Preview attempt " + checkpoint.Preview.Attempt + " completed"
									: "Preview attempt " + checkpoint.Preview.Attempt + " failed",
							Detail = detail
						});
						if (checkpoint.PreviewReview != null)
						{
							row.Evidence.Add(new WorkbenchEvidenceRow
							{
								Title = "Reviewer summary",
								Detail = checkpoint.PreviewReview.Summary + " (" +
									checkpoint.PreviewReview.Confidence.ToString("P0") + ")"
							});
							foreach (AutoEditing.Iteration.Contracts.Assembly.CheckpointReviewObservation
								observation in checkpoint.PreviewReview.Observations)
								row.Evidence.Add(new WorkbenchEvidenceRow
								{
									Title = "Review " + observation.Severity +
										" - " + observation.Category,
									Detail = observation.Message
								});
							foreach (string suggestion in
								checkpoint.PreviewReview.SuggestedChanges)
								row.Evidence.Add(new WorkbenchEvidenceRow
								{
									Title = "Suggested change",
									Detail = suggestion
								});
						}
					}
					foreach (AutoEditing.Iteration.Contracts.Assembly
						.SectionMilestoneRenderManifest milestone in
						checkpoint.SectionMilestones)
					{
						foreach (AutoEditing.Iteration.Contracts.Assembly
							.RoughCutRenderChunk chunk in milestone.Chunks)
						{
							string milestonePath = Path.GetFullPath(Path.Combine(
								projection.SessionRoot,
								chunk.OutputRelativePath.Replace(
									'/', Path.DirectorySeparatorChar)));
							if (!File.Exists(milestonePath)) continue;
							row.PreviewRevisions.Add(
								new WorkbenchPreviewRevisionRow
								{
									Attempt = 100000 +
										milestone.CompletedCheckpoint * 100 +
										chunk.ChunkIndex,
									Label = "Section " + milestone.SectionId +
										" milestone " + chunk.ChunkIndex +
										"/" + milestone.Chunks.Count,
									AbsolutePath = milestonePath,
									Summary = "Completed section " +
										milestone.SectionId + " · " +
										milestone.TimelineStart.ToString(
											@"mm\:ss\.f") + "–" +
										milestone.TimelineEnd.ToString(
											@"mm\:ss\.f"),
									Confidence = "accepted timeline"
								});
						}
						row.Evidence.Add(new WorkbenchEvidenceRow
						{
							Title = "Song-section milestone",
							Detail = "Rendered complete section " +
								milestone.SectionId + " in " +
								milestone.Chunks.Count +
								" bounded chunk(s)."
						});
					}
					AiIterations.Add(row);
				}
				foreach (WorkbenchQuarantinedActionDiagnostic diagnostic in
					projection.AssemblyDetails.QuarantinedActions.Take(3))
				{
					WorkbenchIterationRow current = AiIterations.FirstOrDefault(
						row => row.Number == projection.Assembly.Checkpoint);
					if (current != null)
						current.Evidence.Add(new WorkbenchEvidenceRow
						{
							Title = "Command requires attention",
							Detail = diagnostic.Reason + " - " + diagnostic.Detail
						});
				}
			}
			else
			{
				PopulateLegacyIterationRows(projection);
			}
			SelectedAiIteration =
				AiIterations.FirstOrDefault(row => row.Number == selectedNumber) ??
				AiIterations.FirstOrDefault(row =>
					projection.Assembly != null &&
					row.Number == projection.Assembly.Checkpoint) ??
				AiIterations.LastOrDefault();
			WorkbenchUiIteration latestPersisted = projection.Iterations.LastOrDefault();
			if (!string.Equals(
					projection.State,
					AutoEditing.Iteration.Contracts.Sessions.EditSessionState.Failed.ToString(),
					StringComparison.Ordinal) &&
				projection.Assembly != null &&
				(latestPersisted == null ||
					projection.Assembly.Checkpoint > latestPersisted.Snapshot.Iteration))
			{
				AiCandidateTracks.Clear();
				AutoEditing.Iteration.Contracts.Assembly.ClipStepDecision proposal =
					projection.AssemblyDetails == null
						? null
						: projection.AssemblyDetails.CurrentProposal;
				AiCandidateTracks.Add(new WorkbenchTrackRow
				{
					Name = "Current VEGAS candidate",
					Content = proposal == null
						? Path.GetFileName(projection.Assembly.CurrentClipPath)
						: Path.GetFileName(proposal.Clip.MediaPath) + " - source " +
							proposal.SourceWindow.StartSeconds.ToString("0.000") +
							"s-" + proposal.SourceWindow.EndSeconds.ToString("0.000") +
							"s at " +
							proposal.SourceWindow.ConstantSpeed.ToString("0.###") +
							"x; deterministic gunshot SFX included",
					Duration = proposal == null
						? "Review on timeline"
						: "Window " +
							(proposal.SourceWindow.EndSeconds -
								proposal.SourceWindow.StartSeconds).ToString("0.0") +
							"s"
				});
			}
			else
			{
				RefreshCandidateTracks(latestPersisted);
			}
		}
		catch (Exception exception)
		{
			AiSessionStatus = "Workbench refresh failed: " + exception.Message;
		}
	}

	private void RefreshAiReconciliation(WorkbenchUiProjection projection)
	{
		string selectedId = SelectedAiReconciliationCandidate?.CandidateId;
		AiReconciliationCandidates.Clear();
		AutoEditing.Iteration.Contracts.Assembly.AssemblyReconciliationConflict
			conflict = projection?.AssemblyDetails?.ReconciliationConflict;
		bool conflictPhase = projection?.Assembly != null &&
			projection.Assembly.Phase ==
				AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase
					.ReconciliationConflict;
		if (!conflictPhase || conflict == null)
		{
			SelectedAiReconciliationCandidate = null;
			CanExcludeAiReconciliationClip = false;
			AiReconciliationSummary = "";
			HasAiReconciliationConflict = false;
			return;
		}
		foreach (AutoEditing.Iteration.Contracts.Assembly
			.AssemblyReconciliationEventCandidate candidate in conflict.Candidates)
		{
			AiReconciliationCandidates.Add(
				new WorkbenchReconciliationCandidateRow
				{
					CandidateId = candidate.CandidateId,
					MediaName = Path.GetFileName(candidate.MediaPath),
					Timing = candidate.TimelineStartSeconds.ToString("0.000") +
						"s-" +
						(candidate.TimelineStartSeconds +
							candidate.TimelineDurationSeconds).ToString("0.000") +
						"s; source " +
						candidate.SourceOffsetSeconds.ToString("0.000") + "s",
					Constraint = candidate.AdoptionConstraint,
					CanAdopt =
						candidate.CanAdoptAsCurrent ||
						candidate.CanAdoptAsAdditional,
					AdoptionMode = candidate.CanAdoptAsCurrent
						? "Replace unavailable current clip"
						: candidate.CanAdoptAsAdditional
							? "Accept as an additional clip"
							: "Cannot adopt"
				});
		}
		SelectedAiReconciliationCandidate =
			AiReconciliationCandidates.FirstOrDefault(item =>
				string.Equals(
					item.CandidateId, selectedId, StringComparison.Ordinal))
			?? AiReconciliationCandidates.FirstOrDefault(item => item.CanAdopt)
			?? AiReconciliationCandidates.FirstOrDefault();
		CanExcludeAiReconciliationClip =
			conflict.SupportedResolutions.Contains(
				AutoEditing.Iteration.Contracts.Assembly
					.AssemblyReconciliationResolutionKind.ExcludeCurrentClip);
		AiReconciliationSummary =
			conflict.Issues.Count + " conflict(s): " +
			string.Join(" ", conflict.Issues.Select(item =>
				item.Kind + " - " + item.Detail)) +
			" No event will be adopted without your explicit choice.";
		AiReviewGuidance =
			"Resolve the live timeline mismatch explicitly. Restore the exact proposal, " +
			"exclude the unavailable current clip when safe, select a known remaining " +
			"event to adopt, or defer and pause. Ambiguous or foreign media cannot be adopted.";
		HasAiReconciliationConflict = true;
	}

	private static string FormatAiSessionStatus(WorkbenchUiProjection projection)
	{
		if (string.IsNullOrWhiteSpace(projection.SessionId))
			return projection.State;
		if (string.Equals(
				projection.State,
				AutoEditing.Iteration.Contracts.Sessions.EditSessionState.Failed.ToString(),
				StringComparison.Ordinal))
			return projection.SessionId + " - Failed" +
				(string.IsNullOrWhiteSpace(projection.FailureReason)
					? ""
					: " - " + projection.FailureReason);
		if (projection.Assembly == null)
			return projection.SessionId + " - " + projection.State +
				" - iteration " + projection.CurrentIteration;
		bool clipPhase = projection.Assembly.Phase is
			AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.PlanningClip or
			AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.RepairingProposal or
			AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.MaterializingClip or
			AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.AwaitingHumanReview or
			AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.RenderingCheckpoint or
			AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.ReconcilingTimeline or
			AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.ReconciliationConflict;
		return clipPhase
			? projection.SessionId + " - " + projection.Assembly.Phase +
				" - clip " + projection.Assembly.Checkpoint + " of " +
				projection.Assembly.TotalClips + " - " +
				Path.GetFileName(projection.Assembly.CurrentClipPath)
			: projection.SessionId + " - " + projection.Assembly.Phase +
				(string.IsNullOrWhiteSpace(projection.Assembly.Status)
					? ""
					: " - " + projection.Assembly.Status);
	}

	private void RefreshAiPolishAndFinalization(WorkbenchUiProjection projection)
	{
		int selectedChunk = SelectedAiPolishPreviewChunk?.Number ?? 0;
		AiPolishActions.Clear();
		AiPolishDiagnostics.Clear();
		AiPolishPreviewChunks.Clear();
		SelectedAiPolishPreviewChunk = null;
		CanApproveAiPolishPlan = false;
		CanSkipAiPolishPlan = false;
		CanAcceptAiPolishPreview = false;
		CanSkipAiPolishPreview = false;

		AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase phase =
			projection.Assembly == null
				? AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.Failed
				: projection.Assembly.Phase;
		bool effectsPhase = phase is
			AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.EffectsPlanReview or
			AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.EffectsMaterializing or
			AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.EffectsPreviewReview;
		bool audioPhase = phase is
			AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.AudioPlanReview or
			AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.AudioMaterializing or
			AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.AudioPreviewReview;
		WorkbenchPolishPassProjection pass = effectsPhase
			? projection.Polish?.Effects
			: audioPhase
				? projection.Polish?.Audio
				: projection.Polish?.Audio ?? projection.Polish?.Effects;
		HasAiPolishReview = effectsPhase || audioPhase;
		if (pass == null)
		{
			AiPolishTitle = "";
			AiPolishSummary = "";
		}
		else
		{
			AiPolishTitle = pass.Pass == AutoEditing.Iteration.Contracts.Assembly.PolishPassKind.Effects
				? "Effects pass"
				: "Audio and SFX pass";
			string exactRevision = "Revision " + pass.State.PlanRevision +
				" - " + pass.State.Status + " - plan " +
				ShortHash(pass.State.PlanSha256) + ".";
			if (pass.EffectsPlan != null)
			{
				AiPolishSummary = exactRevision + " " +
					pass.EffectsPlan.Actions.Count +
					" executable native screen-pump action(s).";
				foreach (AutoEditing.Iteration.Contracts.Assembly.EffectsPassAction action
					in pass.EffectsPlan.Actions)
					AiPolishActions.Add(new WorkbenchPolishActionRow
					{
						Title = "Screen pump",
						Target = Path.GetFileName(action.PlacementPath),
						Timing = action.TimelineTimeSeconds.ToString("0.000") +
							"s timeline / " +
							action.LocalTimeSeconds.ToString("0.000") + "s local",
						Treatment = action.RecipeId + " - intensity " +
							action.Intensity.ToString("0.00") + " for " +
							action.DurationSeconds.ToString("0.000") + "s",
						Rationale = action.Reason
					});
				foreach (string diagnostic in pass.EffectsPlan.Diagnostics)
					AiPolishDiagnostics.Add(new WorkbenchEvidenceRow
					{
						Title = "Capability diagnostic",
						Detail = diagnostic
					});
			}
			else if (pass.AudioPlan != null)
			{
				AiPolishSummary = exactRevision + " " +
					(pass.AudioPlan.Song == null ? "No song action; " : "1 song action; ") +
					pass.AudioPlan.Sfx.Count + " reviewed hit-SFX action(s).";
				if (pass.AudioPlan.Song != null)
					AiPolishActions.Add(new WorkbenchPolishActionRow
					{
						Title = "Music track",
						Target = Path.GetFileName(pass.AudioPlan.Song.SongPath),
						Timing = "Timeline start " +
							pass.AudioPlan.Song.TimelineStartSeconds.ToString("0.000") + "s",
						Treatment = "Track gain " +
							pass.AudioPlan.Song.TrackGain.ToString("0.00"),
						Rationale = "Stable song bed for the accepted synchronization pass."
					});
				foreach (AutoEditing.Iteration.Contracts.Assembly.AudioPassSfxAction action
					in pass.AudioPlan.Sfx)
					AiPolishActions.Add(new WorkbenchPolishActionRow
					{
						Title = action.Outcome + " SFX",
						Target = Path.GetFileName(action.PlacementPath),
						Timing = action.ConfirmationTimeSeconds.ToString("0.000") +
							"s - confirmed kill " + (action.ConfirmedKillIndex + 1),
						Treatment = action.Gun + " - gain " +
							action.TrackGain.ToString("0.00"),
						Rationale = string.IsNullOrWhiteSpace(action.PreferredTemplateId)
							? "Reviewed confirmed-kill emphasis."
							: "Preferred reviewed template " + action.PreferredTemplateId + "."
					});
				foreach (string diagnostic in pass.AudioPlan.Diagnostics)
					AiPolishDiagnostics.Add(new WorkbenchEvidenceRow
					{
						Title = "Capability diagnostic",
						Detail = diagnostic
					});
			}
			if (pass.Materialization != null)
				AiPolishDiagnostics.Add(new WorkbenchEvidenceRow
				{
					Title = "VEGAS materialization",
					Detail = pass.Materialization.FullyApplied
						? pass.Materialization.Actions.Count +
							" action result(s) applied and read back."
						: "The pass is only partially applied and cannot be accepted."
				});
			if (pass.Rejected != null)
				AiPolishDiagnostics.Add(new WorkbenchEvidenceRow
				{
					Title = "Rejected preview",
					Detail = pass.Restoration == null
						? "The exact revision was rejected; baseline restoration is still required."
						: pass.Restoration.Completed
							? "The accepted baseline was restored and verified."
							: "Baseline restoration is in progress."
				});
			foreach (WorkbenchPolishPreviewChunkProjection chunk in pass.PreviewChunks)
				AiPolishPreviewChunks.Add(new WorkbenchPreviewChunkRow
				{
					Number = chunk.Chunk.ChunkIndex,
					Label = "Chunk " + chunk.Chunk.ChunkIndex + " - " +
						chunk.Chunk.Start.TotalSeconds.ToString("0.0") + "s to " +
						(chunk.Chunk.Start + chunk.Chunk.Duration).TotalSeconds
							.ToString("0.0") + "s",
					AbsolutePath = chunk.AbsolutePath
				});
			SelectedAiPolishPreviewChunk = AiPolishPreviewChunks
				.FirstOrDefault(item => item.Number == selectedChunk) ??
				AiPolishPreviewChunks.FirstOrDefault();
		}

		CanApproveAiPolishPlan = phase is
			AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.EffectsPlanReview or
			AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.AudioPlanReview;
		CanSkipAiPolishPlan = CanApproveAiPolishPlan;
		CanAcceptAiPolishPreview = phase is
			AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.EffectsPreviewReview or
			AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.AudioPreviewReview;
		CanSkipAiPolishPreview = CanAcceptAiPolishPreview;

		HasAiFinalReview =
			phase == AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.FinalReview;
		WorkbenchFinalizationProjection finalization = projection.Finalization;
		HasAiCompletedReport = finalization?.Report != null;
		CanRollbackAiPromotion = finalization?.CanRollback == true;
		if (HasAiFinalReview)
		{
			AiFinalSummary =
				"Effects and audio passes are accepted. Finalize re-reads and validates " +
				"the complete candidate, then promotes only candidate-owned track labels " +
				"and archives the session.";
		}
		else if (finalization?.Report != null)
		{
			AutoEditing.Iteration.Contracts.Assembly.FinalSessionReport report =
				finalization.Report;
			AiFinalSummary =
				"Completed " + report.CompletedUtc.LocalDateTime.ToString("g") +
				" - promotion " + report.PromotionId +
				" - " + report.PlacementCount + " placement(s), " +
				report.TimelineDuration.TotalSeconds.ToString("0.0") +
				"s - plan " + ShortHash(report.FinalPlanSha256) +
				" - promoted snapshot " +
				ShortHash(report.PromotedSnapshotSha256) + "." +
				(report.SessionUsage == null
					? ""
					: " Usage: " +
						report.SessionUsage.TotalTokens.ToString("N0",
							CultureInfo.InvariantCulture) +
						" tokens across " + report.ModelUsage.Count +
						" model/backend aggregate(s).") +
				(report.Archive == null
					? ""
					: " Archive: " + report.Archive.ArchivePath +
						" (" + ShortHash(report.Archive.Sha256) + ").");
		}
		else if (finalization?.Intent != null)
		{
			AiFinalSummary =
				"Promotion " + finalization.Intent.PromotionId +
				" has a durable pre-mutation intent. Completion evidence is pending.";
		}
		else
		{
			AiFinalSummary = "";
		}
		AiRollbackSummary = finalization?.Rollback != null
			? "Rollback completed and restored candidate snapshot " +
				ShortHash(finalization.Rollback.RestoredSnapshotSha256) + "."
			: finalization?.RollbackPending == true
				? "A companion currently owns this session. A concurrent rollback launch is disabled until that process releases its runtime lease."
			: finalization?.RollbackInterrupted == true
				? "The previous rollback attempt was interrupted before its canonical result was written. No companion owns the session, so a verified retry is available."
			: CanRollbackAiPromotion
				? "Rollback is available while the recovery bundle and live project evidence still match."
				: finalization?.Intent != null && finalization.Recovery == null
					? "Promotion recovery is pending; rollback becomes available after a valid recovery bundle is written."
					: "";

		if (HasAiPolishReview)
			AiReviewGuidance =
				"Review the exact versioned plan or its complete rendered preview. Skipping " +
				"a rendered pass restores and verifies the accepted baseline before continuing.";
		else if (HasAiFinalReview)
			AiReviewGuidance =
				"Finalization is explicit and recoverable. It validates the complete live candidate before changing candidate-owned labels.";
		else if (HasAiCompletedReport)
			AiReviewGuidance =
				"The montage is promoted and archived. Rollback is offered only while the durable recovery evidence remains valid.";
	}

	private static string ShortHash(string value)
	{
		return string.IsNullOrWhiteSpace(value)
			? "(missing)"
			: value.Substring(0, Math.Min(12, value.Length));
	}

	private void PopulateLegacyIterationRows(WorkbenchUiProjection projection)
	{
		foreach (WorkbenchUiIteration iteration in projection.Iterations)
		{
			AutoEditing.Iteration.Contracts.Iterations.EditDecisionRecord decision =
				iteration.Snapshot.Decisions.FirstOrDefault();
			AiIterations.Add(new WorkbenchIterationRow
			{
				Number = iteration.Snapshot.Iteration,
				Title = "Iteration " + iteration.Snapshot.Iteration,
				Status = iteration.Snapshot.Iteration == projection.CurrentIteration
					? "Current"
					: "Completed",
				Summary = iteration.Snapshot.Candidate == null
					? "No candidate plan"
					: iteration.Snapshot.Candidate.Montage.Placements.Count +
						" placements",
				Confidence = decision == null
					? ""
					: decision.Confidence.ToString("P0"),
				Decision = decision == null
					? "No decision record."
					: decision.Summary,
				Rationale = string.Join(
					", ",
					decision == null ? new string[0] : decision.EvidenceIds)
			});
		}
	}

	private static string HumanizeAdjustment(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return "Timeline adjustment";
		return string.Concat(value.Select((character, index) =>
			index > 0 && char.IsUpper(character)
				? " " + character
				: character.ToString()));
	}

	private void AddQuickAiSteering(string instruction)
	{
		if (string.IsNullOrWhiteSpace(instruction)) return;
		AiSteeringText = string.IsNullOrWhiteSpace(AiSteeringText)
			? instruction
			: AiSteeringText.TrimEnd() + Environment.NewLine + instruction;
		AiSteeringStatus =
			"Direction prepared. Choose whether to revise this clip or accept and guide the next.";
	}

	private void RefreshAiUsage(WorkbenchUsageProjection usage)
	{
		AutoEditing.Iteration.Contracts.Sessions.InferenceUsageSummary session =
			usage == null ? null : usage.Session;
		AutoEditing.Iteration.Contracts.Sessions.InferenceUsageSummary lifetime =
			usage == null ? null : usage.Lifetime;
		if (session == null)
		{
			AiUsageModel = "No usage recorded";
			AiSessionUsage = "No calls in this session.";
			AiLifetimeUsage = "No lifetime usage for this model.";
			AiUsagePerformance = "";
			return;
		}
		AiUsageModel = string.IsNullOrWhiteSpace(session.Model)
			? session.HasMixedModels || session.HasMixedProviders
				? "Mixed inference session - lifetime totals require one exact backend and model"
				: "Unknown inference identity"
			: (string.IsNullOrWhiteSpace(session.Provider)
				? "Unknown backend"
				: session.Provider) + " / " + session.Model;
		AiSessionUsage = FormatUsage("Session", session);
		AiLifetimeUsage = lifetime == null
			? "No lifetime usage for this model."
			: FormatUsage("Lifetime", lifetime);
		double inferenceSeconds =
			(session.PromptMilliseconds + session.GenerationMilliseconds) / 1000.0;
		double cacheRate = session.PromptTokens <= 0
			? 0
			: 100.0 * session.CachedPromptTokens / session.PromptTokens;
		AiUsagePerformance =
			inferenceSeconds.ToString("N1", CultureInfo.InvariantCulture) +
			"s inference - " +
			(session.TimeToFirstTokenSampleCount > 0
				? (session.TimeToFirstTokenMilliseconds /
					session.TimeToFirstTokenSampleCount)
					.ToString("N0", CultureInfo.InvariantCulture) +
					"ms average first token - "
				: "first-token timing unavailable - ") +
			cacheRate.ToString("N1", CultureInfo.InvariantCulture) +
			"% prompt cache - " + session.RetryCount + " retries" +
			(session.ContextTokensPeak.HasValue
				? " - " + session.ContextTokensPeak.Value.ToString("N0", CultureInfo.InvariantCulture) +
					" peak context"
				: "");
	}

	private void RefreshAiRoughCut(WorkbenchUiProjection projection)
	{
		string selectedId = SelectedAiRoughCutCorrection?.CorrectionId;
		string selectedChunk = SelectedAiRoughCutChunk?.AbsolutePath;
		string selectedEvidence = SelectedAiRoughCutEvidence?.EvidenceId;
		string selectedFinding = SelectedAiRoughCutFinding?.FindingId;
		AiRoughCutCorrections.Clear();
		AiRoughCutChunks.Clear();
		AiRoughCutEvidence.Clear();
		AiRoughCutFindings.Clear();
		WorkbenchRoughCutProjection roughCut = projection?.RoughCut;
		bool reviewPhase = projection?.Assembly != null &&
			projection.Assembly.Phase is
				AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.RoughCutReview or
				AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.RoughCutCorrection;
		if (roughCut == null)
		{
			SelectedAiRoughCutCorrection = null;
			SelectedAiRoughCutChunk = null;
			SelectedAiRoughCutEvidence = null;
			SelectedAiRoughCutFinding = null;
			AiRoughCutSummary = "";
			HasAiRoughCutReview = false;
			AiReviewGuidance =
				"You may move, trim, change duration, or apply a constant speed in VEGAS. " +
				"Deletion, extra or duplicate events, and variable velocity must be resolved before continuing.";
			return;
		}
		foreach (WorkbenchRoughCutChunkProjection chunk in roughCut.Chunks)
			AiRoughCutChunks.Add(new WorkbenchRoughCutChunkRow
			{
				Title = "Render chunk " + chunk.Chunk.ChunkIndex,
				TimeRange = FormatTimeRange(
					chunk.Chunk.Start.TotalSeconds,
					(chunk.Chunk.Start + chunk.Chunk.Duration).TotalSeconds),
				StartSeconds = chunk.Chunk.Start.TotalSeconds,
				EndSeconds =
					(chunk.Chunk.Start + chunk.Chunk.Duration).TotalSeconds,
				AbsolutePath = chunk.AbsolutePath
			});
		foreach (WorkbenchRoughCutEvidenceProjection item in roughCut.Evidence)
			AiRoughCutEvidence.Add(new WorkbenchRoughCutEvidenceRow
			{
				EvidenceId = item.Evidence.EvidenceId,
				Title = item.Evidence.Kind + " · " + item.Evidence.EvidenceId,
				Detail = item.Evidence.Description,
				TimeLabel = item.Evidence.TimelineTimeSeconds.HasValue
					? FormatTimestamp(item.Evidence.TimelineTimeSeconds.Value)
					: "Whole rough cut",
				TimelineTimeSeconds = item.Evidence.TimelineTimeSeconds,
				AbsolutePath = item.AbsolutePath,
				IsImage = item.Evidence.MediaType.StartsWith(
					"image/", StringComparison.OrdinalIgnoreCase)
			});
		foreach (WorkbenchRoughCutFindingProjection item in roughCut.Findings)
			AiRoughCutFindings.Add(new WorkbenchRoughCutFindingRow
			{
				FindingId = item.Finding.FindingId,
				Title = item.Finding.Summary,
				Detail = item.Finding.Details,
				Severity = item.Finding.Severity + " · " + item.Finding.Source,
				TimeRange = item.Finding.StartSeconds.HasValue
					? FormatTimeRange(
						item.Finding.StartSeconds.Value,
						item.Finding.EndSeconds.Value)
					: "No bounded time range",
				Checkpoints = item.Finding.AffectedCheckpoints.Count == 0
					? "No checkpoint target"
					: "Checkpoint" +
						(item.Finding.AffectedCheckpoints.Count == 1 ? " " : "s ") +
						string.Join(", ", item.Finding.AffectedCheckpoints),
				EvidenceTrace = "Evidence: " +
					string.Join(", ", item.Finding.EvidenceIds),
				EvidenceIds = item.Finding.EvidenceIds.ToList(),
				CorrectionTrace = item.Corrections.Count == 0
					? "Advisory only · no supported correction proposed"
					: "Proposal" + (item.Corrections.Count == 1 ? ": " : "s: ") +
						string.Join(", ", item.Corrections.Select(correction =>
							correction.CorrectionId + " [" + correction.Operation + "]")),
				NavigateSeconds = item.Finding.StartSeconds ??
					item.Evidence.Select(evidence =>
						evidence.Evidence.TimelineTimeSeconds).FirstOrDefault(
							value => value.HasValue)
			});
		Dictionary<string, AutoEditing.Iteration.Contracts.Assembly.RoughCutCorrectionDecision>
			decisions = roughCut.Decisions.ToDictionary(
				item => item.CorrectionId, StringComparer.Ordinal);
		foreach (AutoEditing.Iteration.Contracts.Assembly.RoughCutCorrectionProposal
			correction in roughCut.Report.Corrections)
		{
			AutoEditing.Iteration.Contracts.Assembly.RoughCutCorrectionDecision decision;
			decisions.TryGetValue(correction.CorrectionId, out decision);
			bool terminal = decision != null &&
				(decision.Disposition ==
					AutoEditing.Iteration.Contracts.Assembly.RoughCutCorrectionDisposition.Applied ||
				decision.Disposition ==
					AutoEditing.Iteration.Contracts.Assembly.RoughCutCorrectionDisposition.Rejected);
			bool approved = decision != null &&
				decision.Disposition ==
					AutoEditing.Iteration.Contracts.Assembly.RoughCutCorrectionDisposition.ApprovedForReopen;
			AiRoughCutCorrections.Add(new WorkbenchRoughCutCorrectionRow
			{
				CorrectionId = correction.CorrectionId,
				Instruction = correction.Instruction,
				ExpectedOutcome = correction.ExpectedOutcome,
				Risk = correction.Risk,
				Scope = "Checkpoint" +
					(correction.TargetCheckpoints.Count == 1 ? " " : "s ") +
					string.Join(", ", correction.TargetCheckpoints),
				Status = decision == null ? "Pending" : decision.Disposition.ToString(),
				Operation = correction.Operation.ToString(),
				FindingTrace = "From: " + string.Join(", ", correction.FindingIds),
				CanApprove = reviewPhase && !terminal && !approved,
				CanReject = reviewPhase && !terminal,
				CanApply = reviewPhase && approved &&
					projection.Assembly.Phase ==
						AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.RoughCutCorrection,
				IsTerminal = terminal
			});
		}
		SelectedAiRoughCutCorrection = AiRoughCutCorrections
			.FirstOrDefault(item => string.Equals(
				item.CorrectionId, selectedId, StringComparison.Ordinal))
			?? AiRoughCutCorrections.FirstOrDefault(item => !item.IsTerminal)
			?? AiRoughCutCorrections.FirstOrDefault();
		SelectedAiRoughCutChunk = AiRoughCutChunks.FirstOrDefault(item =>
				string.Equals(item.AbsolutePath, selectedChunk,
					StringComparison.OrdinalIgnoreCase))
			?? AiRoughCutChunks.FirstOrDefault();
		SelectedAiRoughCutEvidence = AiRoughCutEvidence.FirstOrDefault(item =>
				string.Equals(item.EvidenceId, selectedEvidence,
					StringComparison.Ordinal))
			?? AiRoughCutEvidence.FirstOrDefault(item => item.IsImage);
		SelectedAiRoughCutFinding = AiRoughCutFindings.FirstOrDefault(item =>
				string.Equals(item.FindingId, selectedFinding,
					StringComparison.Ordinal))
			?? AiRoughCutFindings.FirstOrDefault();
		AiRoughCutSummary = roughCut.Report.Summary + " " +
			roughCut.Report.Findings.Count + " finding(s), " +
			roughCut.Report.Corrections.Count + " correction proposal(s). Model: " +
			roughCut.Report.ModelStatus + ".";
		AiReviewGuidance =
			"Review the complete synchronization-only rough cut. Approve a correction " +
			"before changing its listed checkpoints, or reject it with an optional note.";
		HasAiRoughCutReview = reviewPhase;
	}

	private void SelectEvidenceForFinding(WorkbenchRoughCutFindingRow finding)
	{
		if (finding == null || finding.EvidenceIds == null) return;
		WorkbenchRoughCutEvidenceRow evidence = AiRoughCutEvidence.FirstOrDefault(item =>
			item.IsImage && finding.EvidenceIds.Contains(
				item.EvidenceId, StringComparer.Ordinal));
		if (evidence != null) SelectedAiRoughCutEvidence = evidence;
	}

	private void JumpToRoughCutTime(double? seconds)
	{
		if (!seconds.HasValue) return;
		_ = _vegasCommands.ExecuteAsync(new SetCursorCommand
		{
			TimelineSeconds = seconds.Value
		});
	}

	private static string FormatTimestamp(double seconds)
	{
		TimeSpan time = TimeSpan.FromSeconds(seconds);
		return time.TotalHours >= 1
			? time.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
			: time.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
	}

	private static string FormatTimeRange(double start, double end) =>
		FormatTimestamp(start) + " – " + FormatTimestamp(end);

	private static string FormatUsage(
		string label,
		AutoEditing.Iteration.Contracts.Sessions.InferenceUsageSummary usage)
	{
		return label + ": " + usage.CallCount.ToString("N0", CultureInfo.InvariantCulture) +
			" calls - " + usage.PromptTokens.ToString("N0", CultureInfo.InvariantCulture) +
			" prompt (" + usage.CachedPromptTokens.ToString("N0", CultureInfo.InvariantCulture) +
			" cached) - " + usage.GeneratedTokens.ToString("N0", CultureInfo.InvariantCulture) +
			" generated - " + usage.TotalTokens.ToString("N0", CultureInfo.InvariantCulture) +
			" total";
	}

	private void RefreshAiProgress(
		AutoEditing.Iteration.Contracts.Sessions.EditSessionProgress progress)
	{
		if (progress == null)
		{
			AiProgressStage = "Idle";
			AiTokenSummary = "No progress heartbeat has been published.";
			AiPromptProgress = 0;
			IsAiModelProcessing = false;
			return;
		}
		AiProgressStage = progress.Stage + " - " +
			progress.Elapsed.ToString(@"hh\:mm\:ss");
		if (progress.IsTokenEstimate)
		{
			AiPromptProgress = 0;
			string provider = string.IsNullOrWhiteSpace(progress.Provider)
				? "Model"
				: progress.Provider;
			string activity;
			if (!progress.HasReceivedFirstToken)
				activity = "waiting for first token";
			else
			{
				double idleSeconds = progress.LastActivityUtc.HasValue
					? Math.Max(
						0,
						(progress.UpdatedUtc -
							progress.LastActivityUtc.Value).TotalSeconds)
					: 0;
				activity =
					"streaming; last token " +
					(idleSeconds < 1
						? "just now"
						: Math.Floor(idleSeconds).ToString(
							"N0",
							CultureInfo.InvariantCulture) +
							"s ago");
			}
			AiTokenSummary =
				provider + " | " +
				progress.Message + " | about " +
				progress.PromptTokens.ToString(
					"N0",
					CultureInfo.InvariantCulture) +
				" text prompt tokens" +
				(progress.HasReceivedFirstToken
					? " | about " +
						progress.GeneratedTokens.ToString(
							"N0",
							CultureInfo.InvariantCulture) +
						" generated"
					: "") +
				" | " + activity;
			IsAiModelProcessing = progress.IsProcessing;
			return;
		}
		AiPromptProgress = progress.PromptTokens <= 0
			? 0
			: Math.Min(100, 100.0 * progress.PromptTokensProcessed / progress.PromptTokens);
		AiTokenSummary =
			progress.PromptTokensProcessed.ToString("N0", CultureInfo.InvariantCulture) +
			" / " + progress.PromptTokens.ToString("N0", CultureInfo.InvariantCulture) +
			" prompt tokens - " +
			progress.GeneratedTokens.ToString("N0", CultureInfo.InvariantCulture) +
			" generated" +
			(progress.MaximumGeneratedTokens > 0
				? " / " + progress.MaximumGeneratedTokens.ToString("N0", CultureInfo.InvariantCulture) +
					" maximum"
				: "");
		IsAiModelProcessing = progress.IsProcessing;
	}

	private void RefreshSelectedAiIteration()
	{
		AiEvidence.Clear();
		IList<WorkbenchPreviewRevisionRow> previews =
			SelectedAiIteration?.PreviewRevisions ??
			new List<WorkbenchPreviewRevisionRow>();
		SelectedAiPreviewB = previews.LastOrDefault();
		SelectedAiPreviewA = previews.Count > 1
			? previews[previews.Count - 2]
			: null;
		if (SelectedAiIteration != null && !string.IsNullOrWhiteSpace(SelectedAiIteration.Rationale))
		{
			AiEvidence.Add(new WorkbenchEvidenceRow
			{
				Title = "Primary synchronization evidence",
				Detail = SelectedAiIteration.Rationale
			});
		}
		if (SelectedAiIteration != null)
		{
			foreach (WorkbenchEvidenceRow evidence in SelectedAiIteration.Evidence)
				AiEvidence.Add(evidence);
		}
		OnPropertyChanged("AiDecisionSummary");
		OnPropertyChanged("AiDecisionConfidence");
		OnPropertyChanged("AiPreviewUri");
		OnPropertyChanged("HasAiPreview");
		OnPropertyChanged("AiPreviewAUri");
		OnPropertyChanged("AiPreviewBUri");
		OnPropertyChanged("HasAiPreviewComparison");
		OnPropertyChanged("AiPreviewComparisonSummary");
	}

	private static Uri PreviewUri(WorkbenchPreviewRevisionRow preview) =>
		preview != null &&
		!string.IsNullOrWhiteSpace(preview.AbsolutePath) &&
		File.Exists(preview.AbsolutePath)
			? new Uri(preview.AbsolutePath, UriKind.Absolute)
			: null;

	private void RefreshCandidateTracks(WorkbenchUiIteration iteration)
	{
		AiCandidateTracks.Clear();
		if (iteration == null || iteration.Timeline == null) return;
		foreach (AutoEditing.Iteration.Contracts.Automation.CandidateTrackSnapshot track in
			iteration.Timeline.Tracks)
		{
			TimeSpan duration = TimeSpan.FromTicks(track.Events.Sum(item => item.TimelineDuration.Ticks));
			AiCandidateTracks.Add(new WorkbenchTrackRow
			{
				Name = track.Name,
				Content = track.Events.Count + " events",
				Duration = duration.ToString(@"mm\:ss\.f"),
				VisualWidth = Math.Max(40, Math.Min(300, duration.TotalSeconds * 8))
			});
		}
	}

	private void InitializeSteps()
	{
		Steps.Add(new WizardStepDefinition { Step = WizardStep.Sources, Number = "1", Title = "Sources", Subtitle = "Clips, song, SFX" });
		Steps.Add(new WizardStepDefinition { Step = WizardStep.SongAnalysis, Number = "2", Title = "Song map", Subtitle = "Review musical structure" });
		Steps.Add(new WizardStepDefinition { Step = WizardStep.SfxIndex, Number = "3", Title = "SFX index", Subtitle = "Validate templates" });
		Steps.Add(new WizardStepDefinition { Step = WizardStep.Analyze, Number = "4", Title = "Analyze", Subtitle = "Find candidates" });
		Steps.Add(new WizardStepDefinition { Step = WizardStep.Review, Number = "5", Title = "Review", Subtitle = "Confirm sync points" });
		Steps.Add(new WizardStepDefinition { Step = WizardStep.Effects, Number = "6", Title = "Effects", Subtitle = "Choose treatments" });
		Steps.Add(new WizardStepDefinition { Step = WizardStep.Drawer, Number = "7", Title = "Clip drawer", Subtitle = "Build from ready clips" });
		UpdateStepState();
	}

	private void InitializeEffectSelections()
	{
		EffectSelections.Add(new EffectSelectionRow
		{
			Name = "Screen pumps",
			Description = "A brief punch-in that boosts each kill and can carry nearby beats when no shot lands on them.",
			Incorporation = "Applied at every placed kill. One or two eligible beats between consecutive shots may receive a lighter pump so the rhythm remains visible without forcing another cut.",
			Availability = "AVAILABLE NOW",
			CanEnable = true,
			IsEnabled = true
		});
		EffectSelections.Add(new EffectSelectionRow
		{
			Name = "Flashes",
			Description = "A short brightness accent for impacts, fast drums, and energetic transitions.",
			Incorporation = "Planned for selected musical accents after a renderer and preset are available.",
			Availability = "PLANNED · NEEDS RENDERER"
		});
		EffectSelections.Add(new EffectSelectionRow
		{
			Name = "Camera shake",
			Description = "Controlled shake for high-impact moments; never intended as a constant treatment.",
			Incorporation = "Planned for sparse high-energy accents when a compatible renderer or OFX preset is available.",
			Availability = "PLANNED · NEEDS RENDERER"
		});
		EffectSelections.Add(new EffectSelectionRow
		{
			Name = "Editorial transitions",
			Description = "Transitions and title reveals driven by montage structure rather than every beat.",
			Incorporation = "Planned for section boundaries, introductions, cinematics, and closers.",
			Availability = "PLANNED · NEEDS RENDERER"
		});
	}

	private void NavigateToStep(object parameter)
	{
		if (parameter is WizardStep step) SetStep(step);
	}

	private bool CanNavigateToStep(object parameter)
	{
		if (IsBusy || !(parameter is WizardStep step)) return false;
		if (step == WizardStep.Sources || step == WizardStep.Effects || step == WizardStep.Drawer) return true;
		if (step == WizardStep.SongAnalysis) return ClipsFolderExists && SongExists && SfxRootExists;
		if (step == WizardStep.SfxIndex || step == WizardStep.Analyze) return SfxRootExists;
		return step != WizardStep.Review || _analysisBatch != null;
	}

	private RelayCommand AsyncCommand(string title, Func<CancellationToken, Task> action, Func<bool> canExecute)
	{
		return Command(async delegate { await RunBusyAsync(title, action); }, canExecute);
	}

	private async Task NextStepAsync()
	{
		if (CurrentStep == WizardStep.Sources)
		{
			SetStep(WizardStep.SongAnalysis);
			if (_songAnalysisDraft == null) ((RelayCommand)AnalyzeSongCommand).Execute(null);
		}
		else if (CurrentStep == WizardStep.SongAnalysis)
		{
			await RunBusyAsync("Committing song review", async token =>
			{
				await CommitSongReviewAsync(token);
				SetStep(WizardStep.SfxIndex);
			});
			if (CurrentStep == WizardStep.SfxIndex && SfxRootExists) ((RelayCommand)ValidateSfxCommand).Execute(null);
		}
		else if (CurrentStep == WizardStep.SfxIndex) SetStep(WizardStep.Analyze);
		else if (CurrentStep == WizardStep.Analyze) ((RelayCommand)AnalyzeCommand).Execute(null);
		else if (CurrentStep == WizardStep.Review) SetStep(WizardStep.Effects);
		else if (CurrentStep == WizardStep.Effects) SetStep(WizardStep.Drawer);
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
		OnPropertyChanged("IsSourcesStep"); OnPropertyChanged("IsSongAnalysisStep"); OnPropertyChanged("IsSfxStep"); OnPropertyChanged("IsAnalyzeStep"); OnPropertyChanged("IsReviewStep"); OnPropertyChanged("IsEffectsStep"); OnPropertyChanged("IsDrawerStep");
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
		SetStep(_analysisBatch.Items.Count == 0 ? WizardStep.Effects : WizardStep.Review);
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
			if (next < 0) { SetStep(WizardStep.Effects); } else { _reviewPosition = next; RefreshMarkers(); }
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
		DrawerView.Refresh();
		_ = LoadDrawerThumbnailsAsync();
		RefreshCommands();
	}

	private bool IsDrawerRowVisible(object item)
	{
		ClipDrawerRow row = item as ClipDrawerRow;
		if (row == null || string.IsNullOrWhiteSpace(DrawerFilter)) return true;
		string filter = DrawerFilter.Trim();
		return Contains(row.FileName, filter) || Contains(row.Player, filter) || Contains(row.Game, filter) || Contains(row.Map, filter) || Contains(row.Guns, filter) || Contains(row.DirectoryName, filter);
	}

	private static bool Contains(string value, string filter)
	{
		return !string.IsNullOrWhiteSpace(value) && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private void SetVisibleClipSelection(bool selected)
	{
		foreach (ClipDrawerRow row in DrawerView.Cast<ClipDrawerRow>()) row.IsSelected = selected && row.IsReady && row.FileExists;
	}

	private async Task LoadDrawerThumbnailsAsync()
	{
		foreach (ClipDrawerRow row in DrawerRows.Where((ClipDrawerRow item) => item.FileExists && item.Thumbnail == null).ToList())
		{
			try
			{
				System.Windows.Media.ImageSource thumbnail = await Task.Run(() => VideoThumbnailProvider.Load(row.FilePath));
				if (DrawerRows.Contains(row)) row.Thumbnail = thumbnail;
			}
			catch (Exception)
			{
				// Explorer does not provide thumbnails for every codec/container.
			}
		}
	}

	private async Task BuildFromLibraryAsync(CancellationToken token)
	{
		List<string> paths = DrawerRows.Where((ClipDrawerRow row) => row.IsSelected && row.IsReady).Select((ClipDrawerRow row) => row.FilePath).ToList();
		List<Clip> clips = new ShotReviewWorkflow().HydrateFromLibrary(paths);
		if (clips.Count == 0) throw new InvalidOperationException("Select at least one available ready clip.");
		EffectSelectionOptions effectSelection = EffectSelection;
		EditPlanningRequest planningRequest = new EditPlanningRequest
		{
			RequestId = Guid.NewGuid().ToString("N"),
			Clips = clips,
			SongPath = SongPath,
			EffectOptions = effectSelection
		};
		EditPlanDocument planDocument = await Task.Run(
			() => new AutomaticEditPlanner().CreatePlanAsync(planningRequest, token).GetAwaiter().GetResult(),
			token);
		PreparedMontage prepared = planDocument.Montage;
		foreach (MontageSongPlanningDiagnostic diagnostic in prepared.PlanningDiagnostics ?? new List<MontageSongPlanningDiagnostic>())
		{
			Logger.Log("Montage planning [" + diagnostic.Severity + "/" + diagnostic.Code + "]: " + diagnostic.Message);
		}
		foreach (MontageTimelineGap gap in prepared.TimelineGaps ?? new List<MontageTimelineGap>())
		{
			Logger.Log("Montage slot open for a cinematic or b-roll clip: " + gap.StartSeconds.ToString("0.000") + "s to " + gap.EndSeconds.ToString("0.000") + "s (" + gap.DurationSeconds.ToString("0.000") + "s).");
		}
		List<MontageSyncAssignment> assignments = prepared.SyncAssignments ?? new List<MontageSyncAssignment>();
		Logger.Log("Montage plan ready before VEGAS mutation: " + prepared.Placements.Count + " clips, " + assignments.Count + " kill anchors, mode " + (prepared.SongPlan?.Mode.ToString() ?? "LegacyPayload") + ".");
		foreach (MontageSyncAssignment assignment in assignments)
		{
			Logger.Log("  " + System.IO.Path.GetFileName(assignment.ClipPath) + " kill " + (assignment.KillIndex + 1) + " -> " + assignment.MusicEventId + " at " + assignment.TimelineTimeSeconds.ToString("0.000") + "s");
		}
		foreach (EffectTreatmentDiagnostic diagnostic in prepared.EffectTreatments?.Diagnostics ?? new List<EffectTreatmentDiagnostic>()) Logger.Log("Effect planning [" + diagnostic.Code + "]: " + diagnostic.Message);
		Logger.Log("Effect treatment plan " + (prepared.EffectTreatments?.PresetId ?? "legacy") + "@" + (prepared.EffectTreatments?.PresetRevision ?? 0) + ": " + (prepared.EffectTreatments?.Actions.Count ?? 0) + " actions (automatic and manual).");
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
		ResetProjectedSongEvents(Enumerable.Empty<string>());
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
		ResetProjectedSongEvents(Enumerable.Empty<string>());
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
			_selectedSongRegion = SongRegions.FirstOrDefault((SongRegionRow row) => row.Model.Id == selectedRegionId) ?? SongRegions.FirstOrDefault();
			RefreshSongEventFilter();
			OnPropertyChanged("SelectedSongEvent");
			OnPropertyChanged("HasSelectedSongEvent");
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
	public void Dispose() { _workbenchRefreshTimer.Stop(); _operationCancellation?.Cancel(); _vegasEvents.Changed -= HandleVegasHostChanged; _vegasEvents.Dispose(); Logger.SetSink(null); }
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
