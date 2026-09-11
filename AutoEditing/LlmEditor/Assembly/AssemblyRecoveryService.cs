using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.LlmEditor.Sessions;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Assembly;

internal sealed class AssemblyResumePlan
{
	public required AssemblyRecoverySummary Summary { get; init; }
	public required EditPlanningRequest Request { get; init; }
	public AssemblySketch? Sketch { get; init; }
	public required AssemblySessionState State { get; init; }
	public EditPlanDocument? AcceptedPlan { get; init; }
	public ClipStepDecision? CurrentProposal { get; init; }
	public CandidateTimelineSnapshot? CurrentTimeline { get; init; }
}

internal sealed class AssemblyRecoveryInspection
{
	public required AssemblyRecoverySummary Summary { get; init; }
	public AssemblyResumePlan? ResumePlan { get; init; }
}

internal sealed class AssemblyRecoveryService
{
	private readonly AtomicFileWriter writer = new();

	public IReadOnlyList<string> DiscoverIncompleteSessions(string sessionsRoot)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionsRoot);
		if (!Directory.Exists(sessionsRoot)) return Array.Empty<string>();
		List<(DateTimeOffset Updated, string SessionId)> discovered = new();
		foreach (string directory in Directory.EnumerateDirectories(sessionsRoot))
		{
			string manifestPath = Path.Combine(directory, "manifest.json");
			if (!File.Exists(manifestPath)) continue;
			try
			{
				EditSessionManifest manifest =
					ContractSerializer.Deserialize<EditSessionManifest>(
						File.ReadAllText(manifestPath));
				if (IsTerminal(manifest.State)) continue;
				if (!string.Equals(
						manifest.SessionId,
						Path.GetFileName(directory),
						StringComparison.Ordinal))
					continue;
				discovered.Add((manifest.UpdatedUtc, manifest.SessionId));
			}
			catch
			{
				// Corrupt sessions are inspectable by explicit ID but are not safe
				// automatic resume candidates.
			}
		}
		return discovered
			.OrderByDescending(item => item.Updated)
			.Select(item => item.SessionId)
			.ToArray();
	}

	public AssemblyRecoveryInspection Inspect(
		string sessionsRoot,
		string sessionId,
		EditPlanningRequest? expectedRequest = null,
		VegasHostIdentity? currentHost = null,
		CandidateTimelineSnapshot? currentTimeline = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionsRoot);
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
		EditSessionIdValidator.Validate(sessionId);
		string sessionRoot = Path.Combine(Path.GetFullPath(sessionsRoot), sessionId);
		AssemblyRecoverySummary summary = new()
		{
			SessionId = sessionId,
			Disposition = AssemblyRecoveryDisposition.Corrupt
		};
		if (!Directory.Exists(sessionRoot))
			return Complete(summary, AssemblyRecoveryDisposition.NotFound,
				"SESSION_NOT_FOUND", "The requested assembly session does not exist.");

		try
		{
			SessionPathResolver paths = new(sessionRoot);
			EditSessionManifest manifest = ReadRequired<EditSessionManifest>(
				paths.Resolve("manifest.json"));
			AssemblySessionState state = ReadRequired<AssemblySessionState>(
				paths.Resolve("assembly/state.json"));
			AssemblyArtifactStore artifacts = new(sessionRoot);
			AssemblyActionExecutionStore actionExecutions = new(sessionRoot);
			AssemblyActionExecutionStart? pendingAction =
				actionExecutions.ReadPendingForRecovery(sessionId);
			AssemblySessionDescriptor descriptor = artifacts.ReadSessionDescriptor()
				?? throw new InvalidDataException(
					"The session predates durable assembly recovery and has no assembly/session.json.");
			EditPlanningRequest request = artifacts.ReadRequest()
				?? throw new InvalidDataException("The persisted assembly request is missing.");

			summary.Checkpoint = state.Checkpoint;
			summary.StateRevision = state.StateRevision;
			ValidateSessionIdentity(sessionId, manifest, state, descriptor, summary);
			if (summary.Issues.Count > 0)
			{
				summary.Disposition = AssemblyRecoveryDisposition.Conflict;
				WriteSummary(paths, summary);
				return WithDisposition(summary, AssemblyRecoveryDisposition.Conflict);
			}
			if (IsTerminal(manifest.State) ||
				state.Phase == AssemblyPhase.Abandoned)
			{
				if (pendingAction?.Action.Kind == AssemblyActionKind.AbandonSession &&
					(state.Phase == AssemblyPhase.Abandoned ||
						manifest.State == EditSessionState.Cancelled))
					actionExecutions.Complete(
						pendingAction.Action,
						"recovered-reflected-abandon");
				AssemblyRecoveryInspection terminal = Complete(summary, AssemblyRecoveryDisposition.Terminal,
					"SESSION_TERMINAL",
					"The assembly session is already " + manifest.State + ".");
				WriteSummary(paths, summary);
				return terminal;
			}

			ValidateRequest(descriptor, request, expectedRequest, summary);
			ValidateProject(descriptor, currentHost, summary);
			if (summary.Issues.Count > 0)
			{
				summary.Disposition = AssemblyRecoveryDisposition.Conflict;
				WriteSummary(paths, summary);
				return WithDisposition(summary, AssemblyRecoveryDisposition.Conflict);
			}

			AssemblySketch? sketch = artifacts.ReadCurrentSketch();
			if (sketch == null && state.Phase != AssemblyPhase.CreatingSketch)
				throw new InvalidDataException("The current assembly sketch is missing.");
			summary.NeedsSketch = sketch == null;
			int effectiveTotalClips =
				sketch?.ClipOrder.Count ?? descriptor.TotalClips;
			if (effectiveTotalClips < 1)
				throw new InvalidDataException(
					"The recoverable semantic sketch has no remaining clips.");
			(int acceptedCheckpoint, EditPlanDocument? acceptedPlan) =
				artifacts.ReadLatestAcceptedPlan();
			summary.AcceptedCheckpoint = acceptedCheckpoint;
			pendingAction = ReconcilePersistedActionResult(
				actionExecutions,
				pendingAction,
				state,
				acceptedCheckpoint);
			if (pendingAction != null &&
				AssemblyCoordinator.RequiresManualTimelineEvidence(
					pendingAction.Action.Kind))
			{
				AssemblyActionTimelineEvidence? evidence =
					actionExecutions.ReadTimelineEvidence(
						pendingAction.Action.ActionId);
				bool actionStateAdvanced =
					pendingAction.Action.ExpectedStateRevision !=
						state.StateRevision ||
					pendingAction.Phase != state.Phase;
				if (evidence == null && actionStateAdvanced)
					throw new InvalidDataException(
						"An advanced pending progressive action has no durable " +
						"identity-validated timeline evidence. Recovery will not " +
						"rematerialize over unknown manual edits.");
				if (evidence == null && currentTimeline != null)
					evidence = actionExecutions.SaveTimelineEvidence(
						pendingAction.Action,
						currentTimeline);
			}
			if (acceptedPlan != null)
			{
				AssemblyReconciliationConflictService reconciliation =
					new(sessionRoot);
				for (int resolutionCheckpoint = 1;
					resolutionCheckpoint <= Math.Max(
						state.Checkpoint, acceptedCheckpoint);
					resolutionCheckpoint++)
					reconciliation.CompleteAcceptedResolutionIfVerified(
						resolutionCheckpoint, acceptedPlan);
			}
			if (acceptedCheckpoint == effectiveTotalClips && acceptedPlan != null)
			{
				summary.Checkpoint = acceptedCheckpoint;
				summary.FinalizeAcceptedPlan = true;
				summary.Disposition = AssemblyRecoveryDisposition.ReadyToResume;
				WriteSummary(paths, summary);
				return new AssemblyRecoveryInspection
				{
					Summary = summary,
					ResumePlan = new AssemblyResumePlan
					{
						Summary = summary,
						Request = request,
						Sketch = sketch,
						State = state,
						AcceptedPlan = acceptedPlan
					}
				};
			}
			int checkpoint = acceptedCheckpoint >= state.Checkpoint
				? acceptedCheckpoint + 1
				: state.Checkpoint;
			if (checkpoint == 0 && state.Phase == AssemblyPhase.CreatingSketch)
				checkpoint = 1;
			if (checkpoint < 1 || checkpoint > effectiveTotalClips)
				throw new InvalidDataException(
					$"Recovery checkpoint {checkpoint} is outside 1..{effectiveTotalClips}.");
			ClipStepDecision? proposal = artifacts.ReadCurrentProposal(checkpoint);
			bool reviewPhase = state.Phase is
				AssemblyPhase.AwaitingHumanReview or
				AssemblyPhase.ReconciliationConflict or
				AssemblyPhase.Paused;
			summary.ReuseMaterializedWorkspace =
				proposal != null && currentTimeline != null &&
				((pendingAction == null && reviewPhase) ||
					(pendingAction != null &&
						CanReuseLiveWorkspaceForPending(pendingAction) &&
						actionExecutions.ReadTimelineEvidence(
							pendingAction.Action.ActionId) != null));
			summary.Checkpoint = checkpoint;

			if (currentTimeline != null)
			{
				ValidateWorkspace(state.Workspace, currentTimeline.Workspace, sessionId, summary);
				if (summary.Issues.Count == 0 &&
					pendingAction == null &&
					proposal != null &&
					state.Phase != AssemblyPhase.ReconciliationConflict)
				{
					EditPlanDocument expected = new ClipStepDecisionCompiler()
						.Append(request, acceptedPlan, proposal)
						.CombinedPlan;
					try
					{
						CandidateMaterializationBaseline baseline =
							artifacts.ReadMaterializedBaseline(checkpoint)
							?? throw new InvalidDataException(
								"The exact post-materialization candidate baseline is " +
								"missing. Reset the proposal before recovery can adopt " +
								"timeline changes.");
						AssemblyTimelineReconciler.Apply(
							expected,
							currentTimeline,
							checkpoint,
							baseline);
					}
					catch (Exception exception) when (
						exception is InvalidOperationException ||
						exception is InvalidDataException)
					{
						Add(summary, "TIMELINE_DIVERGED", exception.Message);
					}
				}
			}
			if (summary.Issues.Count > 0)
			{
				summary.Disposition = AssemblyRecoveryDisposition.Diverged;
				WriteSummary(paths, summary);
				return WithDisposition(summary, AssemblyRecoveryDisposition.Diverged);
			}

			summary.Disposition = AssemblyRecoveryDisposition.ReadyToResume;
			WriteSummary(paths, summary);
			return new AssemblyRecoveryInspection
			{
				Summary = summary,
				ResumePlan = new AssemblyResumePlan
				{
					Summary = summary,
					Request = request,
					Sketch = sketch,
					State = state,
					AcceptedPlan = acceptedPlan,
					CurrentProposal = proposal,
					CurrentTimeline = currentTimeline
				}
			};
		}
		catch (Exception exception) when (
			exception is IOException ||
			exception is InvalidDataException ||
			exception is ArgumentException)
		{
			Add(summary, "SESSION_CORRUPT", exception.Message);
			summary.Disposition = AssemblyRecoveryDisposition.Corrupt;
			TryWriteSummary(sessionRoot, summary);
			return new AssemblyRecoveryInspection { Summary = summary };
		}
	}

	private AssemblyRecoveryInspection Complete(
		AssemblyRecoverySummary summary,
		AssemblyRecoveryDisposition disposition,
		string code,
		string message)
	{
		Add(summary, code, message);
		return WithDisposition(summary, disposition);
	}

	private AssemblyRecoveryInspection WithDisposition(
		AssemblyRecoverySummary summary,
		AssemblyRecoveryDisposition disposition)
	{
		summary.Disposition = disposition;
		return new AssemblyRecoveryInspection { Summary = summary };
	}

	private static void ValidateSessionIdentity(
		string sessionId,
		EditSessionManifest manifest,
		AssemblySessionState state,
		AssemblySessionDescriptor descriptor,
		AssemblyRecoverySummary summary)
	{
		if (!string.Equals(manifest.SessionId, sessionId, StringComparison.Ordinal) ||
			!string.Equals(state.SessionId, sessionId, StringComparison.Ordinal) ||
			!string.Equals(descriptor.SessionId, sessionId, StringComparison.Ordinal))
			Add(summary, "SESSION_ID_MISMATCH",
				"Manifest, assembly state, descriptor, and directory must use the same session ID.");
		if (state.StateRevision <= 0)
			Add(summary, "STATE_REVISION_INVALID",
				"The persisted assembly state revision is not resumable.");
	}

	private static AssemblyActionExecutionStart? ReconcilePersistedActionResult(
		AssemblyActionExecutionStore executions,
		AssemblyActionExecutionStart? pending,
		AssemblySessionState state,
		int acceptedCheckpoint)
	{
		if (pending == null) return null;
		if (pending.Action.Checkpoint != state.Checkpoint ||
			pending.Action.ExpectedStateRevision > state.StateRevision)
			throw new InvalidDataException(
				"The pending action execution does not belong to the recoverable " +
				"assembly checkpoint and state revision.");

		string? reflectedOutcome = pending.Action.Kind switch
		{
			AssemblyActionKind.AcceptTimelineAndContinue
				when acceptedCheckpoint >= pending.Action.Checkpoint =>
				"recovered-accepted-checkpoint",
			AssemblyActionKind.FinishSyncPass
				when acceptedCheckpoint >= pending.Action.Checkpoint =>
				"recovered-finished-sync-pass",
			AssemblyActionKind.PauseSession
				when state.Phase == AssemblyPhase.Paused =>
				"recovered-reflected-pause",
			AssemblyActionKind.ResumeSession
				when pending.Phase == AssemblyPhase.Paused &&
					state.Phase != AssemblyPhase.Paused =>
				"recovered-reflected-resume",
			AssemblyActionKind.AbandonSession
				when state.Phase == AssemblyPhase.Abandoned =>
				"recovered-reflected-abandon",
			_ => null
		};
		if (reflectedOutcome == null) return pending;
		executions.Complete(pending.Action, reflectedOutcome);
		return executions.ReadPendingForRecovery(pending.SessionId);
	}

	private static void ValidateRequest(
		AssemblySessionDescriptor descriptor,
		EditPlanningRequest persisted,
		EditPlanningRequest? expected,
		AssemblyRecoverySummary summary)
	{
		string persistedHash = AssemblyArtifactStore.RequestSha256(persisted);
		if (!string.Equals(
				descriptor.RequestId, persisted.RequestId, StringComparison.Ordinal) ||
			!string.Equals(
				descriptor.RequestSha256, persistedHash, StringComparison.OrdinalIgnoreCase))
			Add(summary, "PERSISTED_REQUEST_MISMATCH",
				"The persisted request does not match the session descriptor.");
		if (expected == null) return;
		if (!string.Equals(expected.RequestId, descriptor.RequestId, StringComparison.Ordinal) ||
			!string.Equals(
				AssemblyArtifactStore.RequestSha256(expected),
				descriptor.RequestSha256,
				StringComparison.OrdinalIgnoreCase))
			Add(summary, "REQUEST_CONFLICT",
				"The supplied planning request is not the request that created this session.");
	}

	private static void ValidateProject(
		AssemblySessionDescriptor descriptor,
		VegasHostIdentity? current,
		AssemblyRecoverySummary summary)
	{
		if (current == null) return;
		if (!string.IsNullOrWhiteSpace(descriptor.ProjectFingerprint) &&
			!string.Equals(
				descriptor.ProjectFingerprint,
				current.ProjectFingerprint,
				StringComparison.Ordinal))
			Add(summary, "PROJECT_FINGERPRINT_CONFLICT",
				"The open VEGAS project does not match the session project fingerprint.");
		if (!string.IsNullOrWhiteSpace(descriptor.ProjectPath) &&
			!string.Equals(
				Path.GetFullPath(descriptor.ProjectPath),
				Path.GetFullPath(current.ProjectPath),
				StringComparison.OrdinalIgnoreCase))
			Add(summary, "PROJECT_PATH_CONFLICT",
				"The open VEGAS project path does not match the session project.");
	}

	private static void ValidateWorkspace(
		CandidateWorkspaceId expected,
		CandidateWorkspaceId actual,
		string sessionId,
		AssemblyRecoverySummary summary)
	{
		if (expected == null || actual == null ||
			!string.Equals(expected.SessionId, sessionId, StringComparison.Ordinal) ||
			!string.Equals(actual.SessionId, expected.SessionId, StringComparison.Ordinal) ||
			actual.Iteration != expected.Iteration ||
			!string.Equals(actual.Nonce, expected.Nonce, StringComparison.Ordinal))
			Add(summary, "WORKSPACE_DIVERGED",
				"The materialized VEGAS workspace is not the workspace recorded by the session.");
	}

	private static T ReadRequired<T>(string path)
	{
		if (!File.Exists(path))
			throw new InvalidDataException("Required recovery artifact is missing: " + path);
		return ContractSerializer.Deserialize<T>(File.ReadAllText(path));
	}

	private void WriteSummary(SessionPathResolver paths, AssemblyRecoverySummary summary) =>
		writer.WriteText(
			paths.Resolve("assembly/recovery/latest.json"),
			ContractSerializer.Serialize(summary));

	private void TryWriteSummary(string sessionRoot, AssemblyRecoverySummary summary)
	{
		try { WriteSummary(new SessionPathResolver(sessionRoot), summary); }
		catch { }
	}

	private static void Add(
		AssemblyRecoverySummary summary,
		string code,
		string message) =>
		summary.Issues.Add(new AssemblyRecoveryIssue { Code = code, Message = message });

	private static bool IsTerminal(EditSessionState state) =>
		state is EditSessionState.Accepted or EditSessionState.Completed or
			EditSessionState.Cancelled;

	private static bool CanReuseLiveWorkspaceForPending(
		AssemblyActionExecutionStart pending) =>
		pending.Action.Kind is
			AssemblyActionKind.CompareCurrentTimeline or
			AssemblyActionKind.RenderCheckpointPreview or
			AssemblyActionKind.AcceptTimelineAndContinue or
			AssemblyActionKind.FinishSyncPass;
}
