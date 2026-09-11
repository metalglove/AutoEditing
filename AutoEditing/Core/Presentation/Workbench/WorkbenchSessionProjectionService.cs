using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Iterations;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.Iteration.Contracts.Steering;
using Newtonsoft.Json.Linq;

namespace Core.Scripts;

internal sealed class WorkbenchSessionProjectionService
{
	private readonly string _sessionsRoot;
	private readonly string _telemetryRoot;

	public WorkbenchSessionProjectionService()
		: this(
			Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"AutoEditing", "automation", "sessions"),
			Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"AutoEditing", "telemetry", "models"))
	{
	}

	internal WorkbenchSessionProjectionService(string sessionsRoot, string telemetryRoot)
	{
		if (string.IsNullOrWhiteSpace(sessionsRoot))
			throw new ArgumentException("A sessions root is required.", "sessionsRoot");
		if (string.IsNullOrWhiteSpace(telemetryRoot))
			throw new ArgumentException("A telemetry root is required.", "telemetryRoot");
		_sessionsRoot = Path.GetFullPath(sessionsRoot);
		_telemetryRoot = Path.GetFullPath(telemetryRoot);
	}

	public WorkbenchUiProjection ReadLatest()
	{
		if (!Directory.Exists(_sessionsRoot))
			return WorkbenchUiProjection.Empty("No AI editing sessions have been created.");

		string sessionRoot = Directory.EnumerateDirectories(_sessionsRoot)
			.Where(path => File.Exists(Path.Combine(path, "manifest.json")))
			.OrderByDescending(path => File.GetLastWriteTimeUtc(Path.Combine(path, "manifest.json")))
			.FirstOrDefault();
		if (sessionRoot == null)
			return WorkbenchUiProjection.Empty("No AI editing sessions have been created.");

		EditSessionManifest manifest = ContractSerializer.Deserialize<EditSessionManifest>(
			File.ReadAllText(Path.Combine(sessionRoot, "manifest.json")));
		List<WorkbenchUiIteration> iterations = new List<WorkbenchUiIteration>();
		string iterationsRoot = Path.Combine(sessionRoot, "iterations");
		if (Directory.Exists(iterationsRoot))
		{
			foreach (string directory in Directory.EnumerateDirectories(iterationsRoot)
				.OrderBy(path => path, StringComparer.Ordinal))
			{
				string snapshotPath = Path.Combine(directory, "snapshot.json");
				if (!File.Exists(snapshotPath)) continue;
				EditIterationSnapshot snapshot =
					ContractSerializer.Deserialize<EditIterationSnapshot>(File.ReadAllText(snapshotPath));
				CandidateTimelineSnapshot timeline = ReadOptional<CandidateTimelineSnapshot>(
					Path.Combine(directory, "timeline.json"));
				iterations.Add(new WorkbenchUiIteration(snapshot, timeline));
			}
		}

		return new WorkbenchUiProjection(
			sessionRoot,
			manifest.SessionId,
			manifest.State.ToString(),
			ReadFailureReason(sessionRoot, manifest.State),
			manifest.CurrentIteration,
			iterations,
			ReadOptional<EditSessionProgress>(Path.Combine(sessionRoot, "progress.json")),
			ReadUsage(sessionRoot),
			ReadAssembly(sessionRoot),
			ReadRoughCut(sessionRoot),
			ReadPolish(sessionRoot),
			ReadFinalization(sessionRoot));
	}

	private static string ReadFailureReason(
		string sessionRoot,
		EditSessionState state)
	{
		if (state != EditSessionState.Failed) return "";
		string eventsPath = Path.Combine(sessionRoot, "events.ndjson");
		if (!File.Exists(eventsPath)) return "";
		string[] lines = File.ReadAllLines(eventsPath);
		for (int index = lines.Length - 1; index >= 0; index--)
		{
			if (string.IsNullOrWhiteSpace(lines[index])) continue;
			try
			{
				JObject entry = JObject.Parse(lines[index]);
				if (!string.Equals(
						entry.Value<string>("EventType"),
						"state-changed",
						StringComparison.Ordinal))
					continue;
				JObject payload = entry["Payload"] as JObject;
				if (!string.Equals(
						payload?.Value<string>("state"),
						EditSessionState.Failed.ToString(),
						StringComparison.Ordinal))
					continue;
				return payload?.Value<string>("reason") ?? "";
			}
			catch
			{
				// A malformed trailing diagnostic must not hide older durable failure evidence.
			}
		}
		return "";
	}

	public IReadOnlyList<WorkbenchIncompleteSession> ReadIncompleteSessions()
	{
		if (!Directory.Exists(_sessionsRoot))
			return new WorkbenchIncompleteSession[0];
		List<WorkbenchIncompleteSession> sessions = new List<WorkbenchIncompleteSession>();
		foreach (string sessionRoot in Directory.EnumerateDirectories(_sessionsRoot))
		{
			string manifestPath = Path.Combine(sessionRoot, "manifest.json");
			if (!File.Exists(manifestPath)) continue;
			try
			{
				EditSessionManifest manifest = ContractSerializer.Deserialize<EditSessionManifest>(
					File.ReadAllText(manifestPath));
				if (!string.Equals(
						Path.GetFileName(sessionRoot),
						manifest.SessionId,
						StringComparison.Ordinal))
					continue;
				if (IsTerminal(manifest.State)) continue;
				AssemblySessionState assembly = ReadOptional<AssemblySessionState>(
					Path.Combine(sessionRoot, "assembly", "state.json"));
				if (assembly != null &&
					!string.Equals(
						assembly.SessionId,
						manifest.SessionId,
						StringComparison.Ordinal))
					continue;
				sessions.Add(new WorkbenchIncompleteSession(
					sessionRoot,
					manifest.SessionId,
					manifest.State,
					manifest.Revision,
					manifest.UpdatedUtc,
					assembly == null ? 0 : assembly.Checkpoint,
					assembly == null ? 0 : assembly.StateRevision,
					assembly == null ? "" : assembly.Phase.ToString(),
					assembly == null ? "" : assembly.Status,
					AssemblyRuntimeLease.IsHeld(sessionRoot)));
			}
			catch
			{
				// A partially-written or foreign directory is not offered as recoverable.
			}
		}
		return sessions
			.OrderByDescending(value => value.UpdatedUtc)
			.ThenBy(value => value.SessionId, StringComparer.Ordinal)
			.ToList();
	}

	private static bool IsTerminal(EditSessionState state) =>
		state == EditSessionState.Accepted ||
		state == EditSessionState.Completed ||
		state == EditSessionState.Cancelled;

	private WorkbenchUsageProjection ReadUsage(string sessionRoot)
	{
		InferenceUsageSummary session = ReadOptional<InferenceUsageSummary>(
			Path.Combine(sessionRoot, "usage-summary.json"));
		InferenceUsageSummary lifetime = null;
		if (session != null &&
			!session.HasMixedModels &&
			!session.HasMixedProviders &&
			!string.IsNullOrWhiteSpace(session.Model))
		{
			string lifetimePath = Path.Combine(
				_telemetryRoot,
				IdentityKey(session.Provider, session.Model),
				"usage-summary.json");
			lifetime = ReadOptional<InferenceUsageSummary>(lifetimePath);
		}
		return new WorkbenchUsageProjection(session, lifetime);
	}

	internal static WorkbenchAssemblyProjection ReadAssembly(string sessionRoot)
	{
		string assemblyRoot = Path.Combine(sessionRoot, "assembly");
		AssemblySessionState state = ReadOptional<AssemblySessionState>(
			Path.Combine(assemblyRoot, "state.json"));
		AssemblySketch sketch = ReadLatestRevision<AssemblySketch>(
			Path.Combine(assemblyRoot, "sketch", "revisions"));
		List<WorkbenchAssemblyCheckpoint> checkpoints = new List<WorkbenchAssemblyCheckpoint>();
		string checkpointsRoot = Path.Combine(assemblyRoot, "checkpoints");
		IReadOnlyList<SectionMilestoneRenderManifest> sectionMilestones =
			ReadSectionMilestones(assemblyRoot);
		if (Directory.Exists(checkpointsRoot))
		{
			foreach (string directory in Directory.EnumerateDirectories(checkpointsRoot)
				.OrderBy(path => path, StringComparer.Ordinal))
			{
				int checkpoint;
				if (!int.TryParse(Path.GetFileName(directory), out checkpoint) || checkpoint < 1)
					continue;
				ClipStepDecision proposal = ReadLatestRevision<ClipStepDecision>(
					Path.Combine(directory, "proposals"));
				IReadOnlyList<AssemblyProposalRejection> proposalRejections =
					ReadRevisionHistory<AssemblyProposalRejection>(
						Path.Combine(directory, "rejections"));
				bool accepted = File.Exists(Path.Combine(directory, "accepted-plan.json"));
				TimelineAdjustmentDelta adjustment = ReadOptional<TimelineAdjustmentDelta>(
					Path.Combine(directory, "adjustment-delta.json"));
				IReadOnlyList<WorkbenchCheckpointPreview> previews =
					ReadPreviewAttempts(sessionRoot, directory);
				WorkbenchCheckpointPreview latestPreview =
					previews.LastOrDefault();
				CheckpointPreviewArtifact preview =
					latestPreview == null ? null : latestPreview.Artifact;
				CheckpointReviewReport previewReview =
					latestPreview == null ? null : latestPreview.Review;
				string clipPath = proposal != null && proposal.Clip != null
					? proposal.Clip.MediaPath
					: SketchClipPath(sketch, checkpoint);
				checkpoints.Add(new WorkbenchAssemblyCheckpoint(
					checkpoint,
					clipPath,
					accepted,
					state != null && state.Checkpoint == checkpoint,
					proposal,
					adjustment,
					preview,
					previewReview,
					previews,
					sectionMilestones.Where(value =>
						value.CompletedCheckpoint == checkpoint).ToList(),
					proposalRejections));
			}
		}

		int total = state != null && state.TotalClips > 0
			? state.TotalClips
			: sketch == null || sketch.ClipOrder == null ? 0 : sketch.ClipOrder.Count;
		HashSet<string> projectedPaths = new HashSet<string>(
			checkpoints.Select(value => value.ClipPath)
				.Where(value => !string.IsNullOrWhiteSpace(value)),
			StringComparer.OrdinalIgnoreCase);
		List<string> unplannedPaths = (state == null
				? Enumerable.Empty<string>()
				: state.RemainingClipPaths ?? new List<string>())
			.Concat(sketch == null || sketch.ClipOrder == null
				? Enumerable.Empty<string>()
				: sketch.ClipOrder.OrderBy(value => value.Order)
					.Select(value => value.Clip == null ? "" : value.Clip.MediaPath))
			.Where(value => !string.IsNullOrWhiteSpace(value))
			.Where(value => !projectedPaths.Contains(value))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		for (int checkpoint = 1; checkpoint <= total; checkpoint++)
		{
			if (checkpoints.Any(value => value.Checkpoint == checkpoint)) continue;
			string clipPath = state != null &&
				state.Checkpoint == checkpoint &&
				!string.IsNullOrWhiteSpace(state.CurrentClipPath)
				? state.CurrentClipPath
				: unplannedPaths.FirstOrDefault() ?? SketchClipPath(sketch, checkpoint);
			unplannedPaths.RemoveAll(value => string.Equals(
				value, clipPath, StringComparison.OrdinalIgnoreCase));
			checkpoints.Add(new WorkbenchAssemblyCheckpoint(
				checkpoint,
				clipPath,
				false,
				state != null && state.Checkpoint == checkpoint,
				null,
				null,
				null,
				null));
		}
		checkpoints = checkpoints.OrderBy(value => value.Checkpoint).ToList();
		WorkbenchAssemblyCheckpoint current = state == null
			? checkpoints.FirstOrDefault(value => !value.IsAccepted)
			: checkpoints.FirstOrDefault(value => value.Checkpoint == state.Checkpoint);
		ClipStepDecision currentProposal = current == null ? null : current.Proposal;
		TimelineAdjustmentDelta latestAdjustment = checkpoints
			.Where(value => value.TimelineAdjustment != null)
			.OrderByDescending(value => value.Checkpoint)
			.Select(value => value.TimelineAdjustment)
			.FirstOrDefault();
		AssemblyReconciliationConflict conflict = state == null || state.Checkpoint < 1
			? null
			: ReadOptional<AssemblyReconciliationConflict>(Path.Combine(
				checkpointsRoot,
				state.Checkpoint.ToString("D4"),
				"reconciliation",
				"current-conflict.json"));
		if (conflict != null)
			AssemblyReconciliationConflictValidator.Validate(conflict);
		return new WorkbenchAssemblyProjection(
			state,
			sketch,
			currentProposal,
			checkpoints,
			checkpoints.Where(value => value.IsAccepted).ToList(),
			current,
			checkpoints.Where(value => !value.IsAccepted &&
				(current == null || value.Checkpoint >= current.Checkpoint)).ToList(),
			latestAdjustment,
			conflict,
			ReadQuarantinedActions(assemblyRoot),
			HasPendingActionExecution(assemblyRoot));
	}

	private static bool HasPendingActionExecution(string assemblyRoot)
	{
		string root = Path.Combine(assemblyRoot, "action-executions");
		if (!Directory.Exists(root)) return false;
		return Directory.EnumerateFiles(root, "*.started.json")
			.Any(started => !File.Exists(Path.Combine(
				root,
				Path.GetFileName(started).Replace(
					".started.json",
					".completed.json"))));
	}

	private static T ReadLatestRevision<T>(string revisionsRoot) where T : class
	{
		if (!Directory.Exists(revisionsRoot)) return null;
		string path = Directory.EnumerateFiles(revisionsRoot, "*.json")
			.OrderByDescending(value => Path.GetFileName(value), StringComparer.Ordinal)
			.FirstOrDefault();
		return path == null ? null : ReadOptional<T>(path);
	}

	private static IReadOnlyList<T> ReadRevisionHistory<T>(string revisionsRoot)
		where T : class
	{
		if (!Directory.Exists(revisionsRoot)) return Array.Empty<T>();
		return Directory.EnumerateFiles(revisionsRoot, "*.json")
			.OrderBy(value => Path.GetFileName(value), StringComparer.Ordinal)
			.Select(ReadOptional<T>)
			.Where(value => value != null)
			.ToList();
	}

	private static string SketchClipPath(AssemblySketch sketch, int checkpoint)
	{
		if (sketch == null || sketch.ClipOrder == null) return "";
		AssemblyClipIntent intent = sketch.ClipOrder
			.FirstOrDefault(value => value.Order == checkpoint);
		return intent == null || intent.Clip == null ? "" : intent.Clip.MediaPath;
	}

	private static CheckpointReviewReport ReadPreviewReview(
		string sessionRoot,
		CheckpointPreviewArtifact preview)
	{
		if (preview == null ||
			string.IsNullOrWhiteSpace(preview.ReviewReportRelativePath))
			return null;
		string relative = SafeRelativePath.Validate(
			preview.ReviewReportRelativePath,
			"ReviewReportRelativePath");
		string root = Path.GetFullPath(sessionRoot)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string path = Path.GetFullPath(Path.Combine(
			root, relative.Replace('/', Path.DirectorySeparatorChar)));
		if (!path.StartsWith(
			root + Path.DirectorySeparatorChar,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"Checkpoint review path escaped the session directory.");
		return ReadOptional<CheckpointReviewReport>(path);
	}

	private static IReadOnlyList<WorkbenchCheckpointPreview> ReadPreviewAttempts(
		string sessionRoot,
		string checkpointDirectory)
	{
		string previewsRoot = Path.Combine(checkpointDirectory, "previews");
		if (!Directory.Exists(previewsRoot))
			return Array.Empty<WorkbenchCheckpointPreview>();
		List<WorkbenchCheckpointPreview> result =
			new List<WorkbenchCheckpointPreview>();
		foreach (string attemptDirectory in
			Directory.EnumerateDirectories(previewsRoot)
				.OrderBy(value => Path.GetFileName(value), StringComparer.Ordinal))
		{
			CheckpointPreviewArtifact artifact =
				ReadOptional<CheckpointPreviewArtifact>(
					Path.Combine(attemptDirectory, "preview-artifact.json"));
			if (artifact == null) continue;
			CheckpointPreviewContractValidator.Validate(artifact);
			result.Add(new WorkbenchCheckpointPreview(
				artifact,
				ReadPreviewReview(sessionRoot, artifact)));
		}
		return result;
	}

	private static IReadOnlyList<SectionMilestoneRenderManifest>
		ReadSectionMilestones(string assemblyRoot)
	{
		string root = Path.Combine(
			assemblyRoot,
			"milestones",
			"sections");
		if (!Directory.Exists(root))
			return Array.Empty<SectionMilestoneRenderManifest>();
		List<SectionMilestoneRenderManifest> result =
			new List<SectionMilestoneRenderManifest>();
		foreach (string path in Directory.EnumerateFiles(
			root,
			"current.json",
			SearchOption.AllDirectories))
		{
			SectionMilestoneRenderManifest manifest =
				ReadOptional<SectionMilestoneRenderManifest>(path);
			if (manifest == null) continue;
			SectionMilestoneRenderContractValidator.Validate(manifest);
			result.Add(manifest);
		}
		return result
			.OrderBy(value => value.CompletedCheckpoint)
			.ThenBy(value => value.SectionId, StringComparer.Ordinal)
			.ToList();
	}

	private static IReadOnlyList<WorkbenchQuarantinedActionDiagnostic> ReadQuarantinedActions(
		string assemblyRoot)
	{
		string dispositionsRoot = Path.Combine(assemblyRoot, "action-dispositions");
		if (!Directory.Exists(dispositionsRoot))
			return new WorkbenchQuarantinedActionDiagnostic[0];
		List<WorkbenchQuarantinedActionDiagnostic> diagnostics =
			new List<WorkbenchQuarantinedActionDiagnostic>();
		foreach (string path in Directory.EnumerateFiles(
			dispositionsRoot, "*.quarantined.*.json"))
		{
			try
			{
				AssemblyActionDispositionProjection disposition =
					ContractSerializer.Deserialize<AssemblyActionDispositionProjection>(
						File.ReadAllText(path));
				diagnostics.Add(new WorkbenchQuarantinedActionDiagnostic(
					disposition.ActionFile,
					disposition.Reason,
					disposition.Detail,
					disposition.RecordedUtc,
					Path.GetFileName(path)));
			}
			catch (Exception exception)
			{
				diagnostics.Add(new WorkbenchQuarantinedActionDiagnostic(
					"",
					"unreadable-disposition",
					exception.GetType().Name,
					File.GetLastWriteTimeUtc(path),
					Path.GetFileName(path)));
			}
		}
		return diagnostics
			.OrderByDescending(value => value.RecordedUtc)
			.ThenBy(value => value.ArtifactName, StringComparer.Ordinal)
			.ToList();
	}

	internal static string IdentityKey(string provider, string model)
	{
		using (SHA256 sha = SHA256.Create())
			return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
					(provider ?? "") + "\n" + model)))
				.Replace("-", "").ToLowerInvariant();
	}

	public EditSteeringDirective SubmitSteering(
		string sessionRoot,
		int currentIteration,
		string instruction)
	{
		if (string.IsNullOrWhiteSpace(sessionRoot) || !Directory.Exists(sessionRoot))
			throw new InvalidOperationException("There is no active AI editing session.");
		if (string.IsNullOrWhiteSpace(instruction))
			throw new ArgumentException("A steering instruction is required.", "instruction");

		EditSteeringDirective directive = new EditSteeringDirective
		{
			DirectiveId = Guid.NewGuid().ToString("N"),
			Kind = EditSteeringKind.Prefer,
			Instruction = instruction.Trim(),
			ApplicableFromIteration = Math.Max(1, currentIteration + 1),
			IsActive = true
		};
		string directory = Path.Combine(sessionRoot, "steering");
		Directory.CreateDirectory(directory);
		string finalPath = Path.Combine(directory, directive.DirectiveId + ".json");
		string temporaryPath = finalPath + ".tmp";
		File.WriteAllText(temporaryPath, ContractSerializer.Serialize(directive));
		File.Move(temporaryPath, finalPath);
		return directive;
	}

	public AssemblyAction SubmitAssemblyAction(
		string sessionRoot,
		string sessionId,
		int checkpoint,
		long expectedStateRevision,
		AssemblyActionKind kind,
		string instruction,
		string targetId = "",
		AssemblySteeringScope steeringScope =
			AssemblySteeringScope.CurrentClip)
	{
		if (string.IsNullOrWhiteSpace(sessionRoot) || !Directory.Exists(sessionRoot))
			throw new InvalidOperationException("There is no active AI assembly session.");
		if (string.IsNullOrWhiteSpace(sessionId))
			throw new InvalidOperationException("The active AI assembly session has no session ID.");
		if (checkpoint <= 0)
			throw new InvalidOperationException("The active AI assembly checkpoint is invalid.");
		if (expectedStateRevision <= 0)
			throw new InvalidOperationException("The AI assembly state revision is not available yet.");
		if (!Enum.IsDefined(typeof(AssemblyActionKind), kind))
			throw new ArgumentOutOfRangeException("kind", kind, "Unknown assembly action.");
		if (!Enum.IsDefined(typeof(AssemblySteeringScope), steeringScope))
			throw new ArgumentOutOfRangeException(
				"steeringScope",
				steeringScope,
				"Unknown assembly steering scope.");
		string normalizedInstruction = (instruction ?? string.Empty).Trim();
		string normalizedTargetId = (targetId ?? string.Empty).Trim();
		if (normalizedInstruction.Length > 16384)
			throw new ArgumentException(
				"The assembly instruction cannot exceed 16,384 characters.",
				"instruction");
		if (normalizedTargetId.Length > 256)
			throw new ArgumentException(
				"The assembly action target cannot exceed 256 characters.",
				"targetId");

		AssemblyAction action = new AssemblyAction
		{
			SessionId = sessionId,
			Checkpoint = checkpoint,
			ExpectedStateRevision = expectedStateRevision,
			Kind = kind,
			TargetId = normalizedTargetId,
			Instruction = normalizedInstruction,
			SteeringScope = steeringScope
		};
		string directory = Path.Combine(sessionRoot, "assembly", "actions");
		Directory.CreateDirectory(directory);
		AssemblyAction existing = FindAssemblyAction(
			directory,
			sessionId,
			checkpoint,
			expectedStateRevision);
		if (existing != null)
			return ResolveRepeatedAssemblyAction(existing, action);

		string finalPath = Path.Combine(
			directory,
			"checkpoint-" + checkpoint.ToString("D6") +
			"-revision-" + expectedStateRevision.ToString("D19") + ".json");
		string temporaryPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			using (FileStream stream = new FileStream(
				temporaryPath,
				FileMode.CreateNew,
				FileAccess.Write,
				FileShare.None,
				4096,
				FileOptions.WriteThrough))
			using (StreamWriter text = new StreamWriter(stream, new UTF8Encoding(false)))
			{
				text.Write(ContractSerializer.Serialize(action));
				text.Flush();
				stream.Flush(true);
			}
			try
			{
				File.Move(temporaryPath, finalPath);
				return action;
			}
			catch (IOException)
			{
				if (!File.Exists(finalPath)) throw;
				AssemblyAction raced = ContractSerializer.Deserialize<AssemblyAction>(
					File.ReadAllText(finalPath));
				return ResolveRepeatedAssemblyAction(raced, action);
			}
		}
		finally
		{
			if (File.Exists(temporaryPath))
				File.Delete(temporaryPath);
		}
	}

	private static AssemblyAction FindAssemblyAction(
		string directory,
		string sessionId,
		int checkpoint,
		long expectedStateRevision)
	{
		List<AssemblyAction> matching = new List<AssemblyAction>();
		string dispositions = Path.Combine(
			Path.GetDirectoryName(directory),
			"action-dispositions");
		foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
		{
			if (Directory.Exists(dispositions) &&
				Directory.EnumerateFiles(
					dispositions,
					Path.GetFileName(path) + ".quarantined.*.json")
					.Any())
				continue;
			try
			{
				AssemblyAction action = ContractSerializer.Deserialize<AssemblyAction>(
					File.ReadAllText(path));
				if (string.Equals(action.SessionId, sessionId, StringComparison.Ordinal) &&
					action.Checkpoint == checkpoint &&
					action.ExpectedStateRevision == expectedStateRevision)
					matching.Add(action);
			}
			catch
			{
				// The companion quarantines malformed action files.
			}
		}
		return matching
			.OrderBy(action => action.CreatedUtc)
			.ThenBy(action => action.ActionId, StringComparer.Ordinal)
			.FirstOrDefault();
	}

	private static AssemblyAction ResolveRepeatedAssemblyAction(
		AssemblyAction existing,
		AssemblyAction requested)
	{
		if (existing.Kind == requested.Kind &&
			existing.SteeringScope == requested.SteeringScope &&
			string.Equals(
				existing.TargetId ?? string.Empty,
				requested.TargetId ?? string.Empty,
				StringComparison.Ordinal) &&
			string.Equals(
				existing.Instruction ?? string.Empty,
				requested.Instruction ?? string.Empty,
				StringComparison.Ordinal))
			return existing;

		throw new InvalidOperationException(
			"Checkpoint " + requested.Checkpoint +
			" revision " + requested.ExpectedStateRevision +
			" already has a queued " + existing.Kind +
			" action. Wait for the workbench to process it before choosing another action.");
	}

	private static T ReadOptional<T>(string path) where T : class
	{
		return File.Exists(path)
			? ContractSerializer.Deserialize<T>(File.ReadAllText(path))
			: null;
	}

	internal static WorkbenchRoughCutProjection ReadRoughCut(string sessionRoot)
	{
		string root = Path.Combine(sessionRoot, "assembly", "rough-cut");
		WorkbenchRoughCutPointer pointer = ReadOptional<WorkbenchRoughCutPointer>(
			Path.Combine(root, "current-report.json"));
		if (pointer == null || string.IsNullOrWhiteSpace(pointer.ReportId))
			return null;
		string auditRoot = Path.Combine(root, "audits", pointer.ReportId);
		RoughCutAuditReport report = ReadOptional<RoughCutAuditReport>(
			Path.Combine(auditRoot, "report.json"));
		if (report == null) return null;
		RoughCutAuditContractValidator.Validate(report);
		List<RoughCutCorrectionDecision> decisions = new List<RoughCutCorrectionDecision>();
		string decisionsRoot = Path.Combine(auditRoot, "decisions");
		if (Directory.Exists(decisionsRoot))
			foreach (string path in Directory.EnumerateFiles(
				decisionsRoot, "*.json", SearchOption.AllDirectories))
			{
				RoughCutCorrectionDecision decision =
					ReadOptional<RoughCutCorrectionDecision>(path);
				if (decision != null)
				{
					RoughCutAuditContractValidator.Validate(decision, report);
					decisions.Add(decision);
				}
			}
		IReadOnlyList<RoughCutCorrectionDecision> latest = decisions
			.GroupBy(value => value.CorrectionId, StringComparer.Ordinal)
			.Select(group => group
				.OrderByDescending(value => value.DecidedUtc)
				.ThenByDescending(value => value.Disposition)
				.First())
			.OrderBy(value => value.CorrectionId, StringComparer.Ordinal)
			.ToList();
		RoughCutEvidenceReference renderEvidence = report.Evidence.Single(item =>
			item.Kind == RoughCutEvidenceKind.FullRender);
		string renderManifestPath = ResolveSessionArtifact(
			sessionRoot, renderEvidence.RelativePath);
		RoughCutRenderManifest renderManifest =
			ContractSerializer.Deserialize<RoughCutRenderManifest>(
				File.ReadAllText(renderManifestPath));
		RoughCutRenderContractValidator.Validate(renderManifest);
		IReadOnlyList<WorkbenchRoughCutChunkProjection> chunks =
			renderManifest.Chunks
				.OrderBy(item => item.ChunkIndex)
				.Select(item => new WorkbenchRoughCutChunkProjection(
					item,
					ResolveSessionArtifact(sessionRoot, item.OutputRelativePath)))
				.ToList();
		IReadOnlyList<WorkbenchRoughCutEvidenceProjection> evidence =
			report.Evidence
				.OrderBy(item => item.TimelineTimeSeconds ?? double.MaxValue)
				.ThenBy(item => item.EvidenceId, StringComparer.Ordinal)
				.Select(item => new WorkbenchRoughCutEvidenceProjection(
					item,
					ResolveSessionArtifact(sessionRoot, item.RelativePath)))
				.ToList();
		IReadOnlyList<WorkbenchRoughCutFindingProjection> findings =
			report.Findings
				.OrderByDescending(item => item.Severity)
				.ThenBy(item => item.StartSeconds ?? double.MaxValue)
				.ThenBy(item => item.FindingId, StringComparer.Ordinal)
				.Select(item => new WorkbenchRoughCutFindingProjection(
					item,
					evidence.Where(reference =>
						item.EvidenceIds.Contains(
							reference.Evidence.EvidenceId,
							StringComparer.Ordinal)).ToList(),
					report.Corrections.Where(correction =>
						correction.FindingIds.Contains(
							item.FindingId,
							StringComparer.Ordinal)).ToList()))
				.ToList();
		return new WorkbenchRoughCutProjection(
			report, latest, renderManifest, chunks, evidence, findings);
	}

	private static WorkbenchPolishProjection ReadPolish(string sessionRoot)
	{
		string root = Path.Combine(sessionRoot, "assembly", "polish");
		if (!Directory.Exists(root)) return null;
		WorkbenchPolishPassProjection effects =
			ReadPolishPass(sessionRoot, PolishPassKind.Effects);
		WorkbenchPolishPassProjection audio =
			ReadPolishPass(sessionRoot, PolishPassKind.Audio);
		return effects == null && audio == null
			? null
			: new WorkbenchPolishProjection(effects, audio);
	}

	private static WorkbenchPolishPassProjection ReadPolishPass(
		string sessionRoot,
		PolishPassKind pass)
	{
		string passName = pass == PolishPassKind.Effects ? "effects" : "audio";
		string passRoot = Path.Combine(
			sessionRoot, "assembly", "polish", passName);
		string statesRoot = Path.Combine(passRoot, "states");
		if (!Directory.Exists(statesRoot)) return null;
		string statePath = Directory.EnumerateFiles(statesRoot, "*.json")
			.OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
			.FirstOrDefault();
		PolishPassStateRecord state =
			statePath == null ? null : ReadOptional<PolishPassStateRecord>(statePath);
		if (state == null) return null;
		PolishPassContractValidator.Validate(state);
		string revisionRoot = Path.Combine(
			passRoot, "revisions", state.PlanRevision.ToString("D4"));
		EffectsPassPlan effects = pass == PolishPassKind.Effects
			? ReadOptional<EffectsPassPlan>(Path.Combine(revisionRoot, "plan.json"))
			: null;
		AudioPassPlan audio = pass == PolishPassKind.Audio
			? ReadOptional<AudioPassPlan>(Path.Combine(revisionRoot, "plan.json"))
			: null;
		if (effects == null && audio == null)
			throw new InvalidDataException(
				"The current " + passName + " plan revision is missing.");
		if (effects != null) PolishPassContractValidator.Validate(effects);
		if (audio != null) PolishPassContractValidator.Validate(audio);
		PolishPassApproval approval = ReadOptional<PolishPassApproval>(
			Path.Combine(revisionRoot, "approval.json"));
		if (approval != null && effects != null)
			PolishPassContractValidator.Validate(
				approval, effects, state.PlanSha256);
		if (approval != null && audio != null)
			PolishPassContractValidator.Validate(
				approval, audio, state.PlanSha256);
		PolishPassMaterialization materialization =
			ReadOptional<PolishPassMaterialization>(
				Path.Combine(revisionRoot, "materialization.json"));
		if (materialization != null)
			PolishPassContractValidator.Validate(materialization);
		PolishPassPreviewManifest preview =
			ReadOptional<PolishPassPreviewManifest>(
				Path.Combine(revisionRoot, "preview", "manifest.json"));
		if (preview != null)
			PolishPassContractValidator.Validate(preview);

		string hashPrefix = state.PlanSha256.Substring(0, 16);
		AcceptedPolishPass accepted = ReadOptional<AcceptedPolishPass>(
			Path.Combine(passRoot, "revisions", "accepted-" + hashPrefix + ".json"));
		if (accepted != null) PolishPassContractValidator.Validate(accepted);
		RejectedPolishPassPreview rejected =
			ReadOptional<RejectedPolishPassPreview>(
				Path.Combine(passRoot, "revisions", "rejected-" + hashPrefix + ".json"));
		if (rejected != null) PolishPassContractValidator.Validate(rejected);
		PolishBaselineRestoration restoration =
			ReadLatestRestoration(passRoot, hashPrefix);
		IReadOnlyList<WorkbenchPolishPreviewChunkProjection> chunks =
			(preview == null
				? Enumerable.Empty<RoughCutRenderChunk>()
				: preview.Chunks.OrderBy(item => item.ChunkIndex))
			.Select(item => new WorkbenchPolishPreviewChunkProjection(
				item,
				ResolveSessionArtifact(sessionRoot, item.OutputRelativePath)))
			.ToList();

		return new WorkbenchPolishPassProjection(
			pass,
			state,
			effects,
			audio,
			approval,
			materialization,
			preview,
			chunks,
			accepted,
			rejected,
			restoration);
	}

	private static PolishBaselineRestoration ReadLatestRestoration(
		string passRoot,
		string hashPrefix)
	{
		string root = Path.Combine(passRoot, "restorations", hashPrefix);
		if (!Directory.Exists(root)) return null;
		string receipt = Directory.EnumerateFiles(
				root, "receipt.json", SearchOption.AllDirectories)
			.OrderByDescending(value => value, StringComparer.Ordinal)
			.FirstOrDefault();
		string intent = Directory.EnumerateFiles(
				root, "intent.json", SearchOption.AllDirectories)
			.OrderByDescending(value => value, StringComparer.Ordinal)
			.FirstOrDefault();
		string path = receipt ?? intent;
		PolishBaselineRestoration restoration =
			path == null ? null : ReadOptional<PolishBaselineRestoration>(path);
		if (restoration != null)
			PolishPassContractValidator.Validate(restoration);
		return restoration;
	}

	private static WorkbenchFinalizationProjection ReadFinalization(
		string sessionRoot)
	{
		string root = Path.Combine(sessionRoot, "finalization");
		if (!Directory.Exists(root)) return null;
		FinalizationPromotionIntent intent =
			ReadOptional<FinalizationPromotionIntent>(
				Path.Combine(root, "promotion-intent.json"));
		FinalizationRecoveryBundle recovery =
			ReadOptional<FinalizationRecoveryBundle>(
				Path.Combine(root, "recovery-bundle.json"));
		FinalSessionReport report = ReadOptional<FinalSessionReport>(
			Path.Combine(root, "final-session-report.json"));
		FinalArtifactArchiveReceipt archive =
			ReadOptional<FinalArtifactArchiveReceipt>(
				Path.Combine(root, "archive-receipt.json"));
		RollbackCandidatePromotionResult rollback =
			ReadOptional<RollbackCandidatePromotionResult>(
				Path.Combine(root, "rollback-result.json"));
		string attemptsRoot = Path.Combine(root, "rollback-attempts");
		string latestRollbackIntent = Directory.Exists(attemptsRoot)
			? Directory.EnumerateFiles(attemptsRoot, "*.intent.json")
				.OrderByDescending(File.GetLastWriteTimeUtc)
				.ThenByDescending(Path.GetFileName, StringComparer.Ordinal)
				.FirstOrDefault()
			: null;
		bool runtimeLeaseHeld = AssemblyRuntimeLease.IsHeld(sessionRoot);
		bool rollbackPending = rollback == null && runtimeLeaseHeld;
		bool rollbackInterrupted = rollback == null &&
			latestRollbackIntent != null &&
			!runtimeLeaseHeld;
		if (intent == null && recovery == null && report == null &&
			archive == null && rollback == null &&
			!rollbackPending && !rollbackInterrupted)
			return null;
		return new WorkbenchFinalizationProjection(
			intent,
			recovery,
			report,
			archive,
			rollback,
			rollbackPending,
			rollbackInterrupted,
			runtimeLeaseHeld);
	}

	private static string ResolveSessionArtifact(
		string sessionRoot,
		string relativePath)
	{
		string root = Path.GetFullPath(sessionRoot);
		string result = Path.GetFullPath(Path.Combine(root, relativePath));
		string prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(),
				StringComparison.Ordinal)
			? root
			: root + Path.DirectorySeparatorChar;
		if (!result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"A rough-cut workbench artifact escaped its session directory.");
		return result;
	}
}

internal sealed class WorkbenchUiProjection
{
	public WorkbenchUiProjection(
		string sessionRoot,
		string sessionId,
		string state,
		string failureReason,
		int currentIteration,
		IReadOnlyList<WorkbenchUiIteration> iterations,
		EditSessionProgress progress,
		WorkbenchUsageProjection usage,
		WorkbenchAssemblyProjection assembly,
		WorkbenchRoughCutProjection roughCut,
		WorkbenchPolishProjection polish,
		WorkbenchFinalizationProjection finalization)
	{
		SessionRoot = sessionRoot;
		SessionId = sessionId;
		State = state;
		FailureReason = failureReason ?? "";
		CurrentIteration = currentIteration;
		Iterations = iterations;
		Progress = progress;
		Usage = usage;
		AssemblyDetails = assembly;
		Assembly = assembly == null ? null : assembly.State;
		RoughCut = roughCut;
		Polish = polish;
		Finalization = finalization;
	}

	public string SessionRoot { get; }
	public string SessionId { get; }
	public string State { get; }
	public string FailureReason { get; }
	public int CurrentIteration { get; }
	public IReadOnlyList<WorkbenchUiIteration> Iterations { get; }
	public EditSessionProgress Progress { get; }
	public WorkbenchUsageProjection Usage { get; }
	public AssemblySessionState Assembly { get; }
	public WorkbenchAssemblyProjection AssemblyDetails { get; }
	public WorkbenchRoughCutProjection RoughCut { get; }
	public WorkbenchPolishProjection Polish { get; }
	public WorkbenchFinalizationProjection Finalization { get; }

	public static WorkbenchUiProjection Empty(string state) =>
		new WorkbenchUiProjection("", "", state, "", 0, new WorkbenchUiIteration[0], null,
			new WorkbenchUsageProjection(null, null), null, null, null, null);
}

internal sealed class WorkbenchRoughCutProjection
{
	public WorkbenchRoughCutProjection(
		RoughCutAuditReport report,
		IReadOnlyList<RoughCutCorrectionDecision> decisions,
		RoughCutRenderManifest renderManifest,
		IReadOnlyList<WorkbenchRoughCutChunkProjection> chunks,
		IReadOnlyList<WorkbenchRoughCutEvidenceProjection> evidence,
		IReadOnlyList<WorkbenchRoughCutFindingProjection> findings)
	{
		Report = report;
		Decisions = decisions;
		RenderManifest = renderManifest;
		Chunks = chunks;
		Evidence = evidence;
		Findings = findings;
	}

	public RoughCutAuditReport Report { get; }
	public IReadOnlyList<RoughCutCorrectionDecision> Decisions { get; }
	public RoughCutRenderManifest RenderManifest { get; }
	public IReadOnlyList<WorkbenchRoughCutChunkProjection> Chunks { get; }
	public IReadOnlyList<WorkbenchRoughCutEvidenceProjection> Evidence { get; }
	public IReadOnlyList<WorkbenchRoughCutFindingProjection> Findings { get; }
}

internal sealed class WorkbenchRoughCutChunkProjection
{
	public WorkbenchRoughCutChunkProjection(
		RoughCutRenderChunk chunk,
		string absolutePath)
	{
		Chunk = chunk;
		AbsolutePath = absolutePath;
	}

	public RoughCutRenderChunk Chunk { get; }
	public string AbsolutePath { get; }
}

internal sealed class WorkbenchRoughCutEvidenceProjection
{
	public WorkbenchRoughCutEvidenceProjection(
		RoughCutEvidenceReference evidence,
		string absolutePath)
	{
		Evidence = evidence;
		AbsolutePath = absolutePath;
	}

	public RoughCutEvidenceReference Evidence { get; }
	public string AbsolutePath { get; }
}

internal sealed class WorkbenchRoughCutFindingProjection
{
	public WorkbenchRoughCutFindingProjection(
		RoughCutAuditFinding finding,
		IReadOnlyList<WorkbenchRoughCutEvidenceProjection> evidence,
		IReadOnlyList<RoughCutCorrectionProposal> corrections)
	{
		Finding = finding;
		Evidence = evidence;
		Corrections = corrections;
	}

	public RoughCutAuditFinding Finding { get; }
	public IReadOnlyList<WorkbenchRoughCutEvidenceProjection> Evidence { get; }
	public IReadOnlyList<RoughCutCorrectionProposal> Corrections { get; }
}

internal sealed class WorkbenchPolishProjection
{
	public WorkbenchPolishProjection(
		WorkbenchPolishPassProjection effects,
		WorkbenchPolishPassProjection audio)
	{
		Effects = effects;
		Audio = audio;
	}

	public WorkbenchPolishPassProjection Effects { get; }
	public WorkbenchPolishPassProjection Audio { get; }
}

internal sealed class WorkbenchPolishPassProjection
{
	public WorkbenchPolishPassProjection(
		PolishPassKind pass,
		PolishPassStateRecord state,
		EffectsPassPlan effectsPlan,
		AudioPassPlan audioPlan,
		PolishPassApproval approval,
		PolishPassMaterialization materialization,
		PolishPassPreviewManifest preview,
		IReadOnlyList<WorkbenchPolishPreviewChunkProjection> previewChunks,
		AcceptedPolishPass accepted,
		RejectedPolishPassPreview rejected,
		PolishBaselineRestoration restoration)
	{
		Pass = pass;
		State = state;
		EffectsPlan = effectsPlan;
		AudioPlan = audioPlan;
		Approval = approval;
		Materialization = materialization;
		Preview = preview;
		PreviewChunks = previewChunks;
		Accepted = accepted;
		Rejected = rejected;
		Restoration = restoration;
	}

	public PolishPassKind Pass { get; }
	public PolishPassStateRecord State { get; }
	public EffectsPassPlan EffectsPlan { get; }
	public AudioPassPlan AudioPlan { get; }
	public PolishPassApproval Approval { get; }
	public PolishPassMaterialization Materialization { get; }
	public PolishPassPreviewManifest Preview { get; }
	public IReadOnlyList<WorkbenchPolishPreviewChunkProjection> PreviewChunks { get; }
	public AcceptedPolishPass Accepted { get; }
	public RejectedPolishPassPreview Rejected { get; }
	public PolishBaselineRestoration Restoration { get; }
}

internal sealed class WorkbenchPolishPreviewChunkProjection
{
	public WorkbenchPolishPreviewChunkProjection(
		RoughCutRenderChunk chunk,
		string absolutePath)
	{
		Chunk = chunk;
		AbsolutePath = absolutePath;
	}

	public RoughCutRenderChunk Chunk { get; }
	public string AbsolutePath { get; }
}

internal sealed class WorkbenchFinalizationProjection
{
	public WorkbenchFinalizationProjection(
		FinalizationPromotionIntent intent,
		FinalizationRecoveryBundle recovery,
		FinalSessionReport report,
		FinalArtifactArchiveReceipt archive,
		RollbackCandidatePromotionResult rollback,
		bool rollbackPending,
		bool rollbackInterrupted,
		bool runtimeLeaseHeld)
	{
		Intent = intent;
		Recovery = recovery;
		Report = report;
		Archive = archive;
		Rollback = rollback;
		RollbackPending = rollbackPending;
		RollbackInterrupted = rollbackInterrupted;
		RuntimeLeaseHeld = runtimeLeaseHeld;
	}

	public FinalizationPromotionIntent Intent { get; }
	public FinalizationRecoveryBundle Recovery { get; }
	public FinalSessionReport Report { get; }
	public FinalArtifactArchiveReceipt Archive { get; }
	public RollbackCandidatePromotionResult Rollback { get; }
	public bool RollbackPending { get; }
	public bool RollbackInterrupted { get; }
	public bool RuntimeLeaseHeld { get; }
	public bool CanRollback =>
		Recovery != null && Rollback == null && !RuntimeLeaseHeld;
}

internal static class WorkbenchPolishActionPolicy
{
	public static AssemblyActionKind Resolve(
		AssemblyPhase phase,
		bool accept,
		bool preview)
	{
		if (preview)
		{
			if (phase == AssemblyPhase.EffectsPreviewReview)
				return accept
					? AssemblyActionKind.AcceptEffectsPreview
					: AssemblyActionKind.SkipEffectsPreview;
			if (phase == AssemblyPhase.AudioPreviewReview)
				return accept
					? AssemblyActionKind.AcceptAudioPreview
					: AssemblyActionKind.SkipAudioPreview;
		}
		else
		{
			if (phase == AssemblyPhase.EffectsPlanReview)
				return accept
					? AssemblyActionKind.ApproveEffectsPlan
					: AssemblyActionKind.SkipEffectsPlan;
			if (phase == AssemblyPhase.AudioPlanReview)
				return accept
					? AssemblyActionKind.ApproveAudioPlan
					: AssemblyActionKind.SkipAudioPlan;
		}
		throw new InvalidOperationException(
			"The requested polish action does not match the durable review phase.");
	}
}

internal sealed class WorkbenchRoughCutPointer
{
	public int SchemaVersion { get; set; }
	public string SessionId { get; set; } = "";
	public string ReportId { get; set; } = "";
	public string ReportSha256 { get; set; } = "";
	public string PlanSha256 { get; set; } = "";
	public DateTimeOffset UpdatedUtc { get; set; }
}

internal sealed class WorkbenchAssemblyProjection
{
	public WorkbenchAssemblyProjection(
		AssemblySessionState state,
		AssemblySketch activeSketch,
		ClipStepDecision currentProposal,
		IReadOnlyList<WorkbenchAssemblyCheckpoint> checkpoints,
		IReadOnlyList<WorkbenchAssemblyCheckpoint> acceptedCheckpoints,
		WorkbenchAssemblyCheckpoint currentCheckpoint,
		IReadOnlyList<WorkbenchAssemblyCheckpoint> remainingCheckpoints,
		TimelineAdjustmentDelta latestTimelineAdjustment,
		AssemblyReconciliationConflict reconciliationConflict,
		IReadOnlyList<WorkbenchQuarantinedActionDiagnostic> quarantinedActions,
		bool hasPendingActionExecution)
	{
		State = state;
		ActiveSketch = activeSketch;
		CurrentProposal = currentProposal;
		Checkpoints = checkpoints;
		AcceptedCheckpoints = acceptedCheckpoints;
		CurrentCheckpoint = currentCheckpoint;
		RemainingCheckpoints = remainingCheckpoints;
		LatestTimelineAdjustment = latestTimelineAdjustment;
		ReconciliationConflict = reconciliationConflict;
		QuarantinedActions = quarantinedActions;
		HasPendingActionExecution = hasPendingActionExecution;
	}

	public AssemblySessionState State { get; }
	public AssemblySketch ActiveSketch { get; }
	public ClipStepDecision CurrentProposal { get; }
	public IReadOnlyList<WorkbenchAssemblyCheckpoint> Checkpoints { get; }
	public IReadOnlyList<WorkbenchAssemblyCheckpoint> AcceptedCheckpoints { get; }
	public WorkbenchAssemblyCheckpoint CurrentCheckpoint { get; }
	public IReadOnlyList<WorkbenchAssemblyCheckpoint> RemainingCheckpoints { get; }
	public TimelineAdjustmentDelta LatestTimelineAdjustment { get; }
	public AssemblyReconciliationConflict ReconciliationConflict { get; }
	public IReadOnlyList<WorkbenchQuarantinedActionDiagnostic> QuarantinedActions { get; }
	public bool HasPendingActionExecution { get; }
}

internal sealed class WorkbenchAssemblyCheckpoint
{
	public WorkbenchAssemblyCheckpoint(
		int checkpoint,
		string clipPath,
		bool isAccepted,
		bool isCurrent,
		ClipStepDecision proposal,
		TimelineAdjustmentDelta timelineAdjustment,
		CheckpointPreviewArtifact preview,
		CheckpointReviewReport previewReview,
		IReadOnlyList<WorkbenchCheckpointPreview> previews = null,
		IReadOnlyList<SectionMilestoneRenderManifest> sectionMilestones = null,
		IReadOnlyList<AssemblyProposalRejection> proposalRejections = null)
	{
		Checkpoint = checkpoint;
		ClipPath = clipPath ?? "";
		IsAccepted = isAccepted;
		IsCurrent = isCurrent;
		Proposal = proposal;
		TimelineAdjustment = timelineAdjustment;
		Preview = preview;
		PreviewReview = previewReview;
		Previews = previews ??
			(preview == null
				? new WorkbenchCheckpointPreview[0]
				: new[]
				{
					new WorkbenchCheckpointPreview(preview, previewReview)
				});
		SectionMilestones = sectionMilestones ??
			new SectionMilestoneRenderManifest[0];
		ProposalRejections = proposalRejections ??
			new AssemblyProposalRejection[0];
	}

	public int Checkpoint { get; }
	public string ClipPath { get; }
	public bool IsAccepted { get; }
	public bool IsCurrent { get; }
	public ClipStepDecision Proposal { get; }
	public TimelineAdjustmentDelta TimelineAdjustment { get; }
	public CheckpointPreviewArtifact Preview { get; }
	public CheckpointReviewReport PreviewReview { get; }
	public IReadOnlyList<WorkbenchCheckpointPreview> Previews { get; }
	public IReadOnlyList<SectionMilestoneRenderManifest> SectionMilestones { get; }
	public IReadOnlyList<AssemblyProposalRejection> ProposalRejections { get; }
}

internal sealed class WorkbenchCheckpointPreview
{
	public WorkbenchCheckpointPreview(
		CheckpointPreviewArtifact artifact,
		CheckpointReviewReport review)
	{
		Artifact = artifact ??
			throw new ArgumentNullException(nameof(artifact));
		Review = review;
	}

	public CheckpointPreviewArtifact Artifact { get; }
	public CheckpointReviewReport Review { get; }
}

internal sealed class WorkbenchQuarantinedActionDiagnostic
{
	public WorkbenchQuarantinedActionDiagnostic(
		string actionFile,
		string reason,
		string detail,
		DateTimeOffset recordedUtc,
		string artifactName)
	{
		ActionFile = actionFile ?? "";
		Reason = reason ?? "";
		Detail = detail ?? "";
		RecordedUtc = recordedUtc;
		ArtifactName = artifactName ?? "";
	}

	public string ActionFile { get; }
	public string Reason { get; }
	public string Detail { get; }
	public DateTimeOffset RecordedUtc { get; }
	public string ArtifactName { get; }
}

internal sealed class AssemblyActionDispositionProjection
{
	public string ActionFile { get; set; } = "";
	public string Outcome { get; set; } = "";
	public string Reason { get; set; } = "";
	public string Detail { get; set; } = "";
	public DateTimeOffset RecordedUtc { get; set; }
}

internal sealed class WorkbenchUsageProjection
{
	public WorkbenchUsageProjection(
		InferenceUsageSummary session,
		InferenceUsageSummary lifetime)
	{
		Session = session;
		Lifetime = lifetime;
	}

	public InferenceUsageSummary Session { get; }
	public InferenceUsageSummary Lifetime { get; }
}

internal static class WorkbenchConsequenceCopy
{
	public const string FinalizeMontage =
		"The companion first validates the complete live candidate against " +
		"the accepted plan and project identity. It then promotes only " +
		"candidate-owned track labels, writes the final report and external " +
		"artifact archive, and preserves a verified rollback path. Unrelated " +
		"timeline content is never relabeled.";
}

public sealed class WorkbenchIncompleteSession
{
	public WorkbenchIncompleteSession(
		string sessionRoot,
		string sessionId,
		EditSessionState state,
		long revision,
		DateTimeOffset updatedUtc,
		int checkpoint,
		long assemblyStateRevision,
		string assemblyPhase,
		string status,
		bool hasLiveConsumer)
	{
		SessionRoot = sessionRoot;
		SessionId = sessionId;
		State = state;
		Revision = revision;
		UpdatedUtc = updatedUtc;
		Checkpoint = checkpoint;
		AssemblyStateRevision = assemblyStateRevision;
		AssemblyPhase = assemblyPhase ?? "";
		Status = status ?? "";
		HasLiveConsumer = hasLiveConsumer;
		AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase parsedPhase;
		if (!Enum.TryParse(AssemblyPhase, false, out parsedPhase))
			parsedPhase = AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.Failed;
		_phase = parsedPhase;
		_lifecycle = AssemblyLifecyclePolicy.Evaluate(
			State,
			parsedPhase,
			Checkpoint,
			AssemblyStateRevision,
			HasLiveConsumer);
	}

	private readonly AssemblyLifecycleAvailability _lifecycle;
	private readonly AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase _phase;
	public string SessionRoot { get; }
	public string SessionId { get; }
	public EditSessionState State { get; }
	public long Revision { get; }
	public DateTimeOffset UpdatedUtc { get; }
	public int Checkpoint { get; }
	public long AssemblyStateRevision { get; }
	public string AssemblyPhase { get; }
	public string Status { get; }
	public bool HasLiveConsumer { get; }
	public bool CanResume => _lifecycle.CanResume;
	public bool CanPause => _lifecycle.CanPause;
	public bool CanAbandon => _lifecycle.CanAbandon;
	public string DisplayName => string.IsNullOrWhiteSpace(SessionId) ? "(unnamed session)" : SessionId;
	public string StateSummary =>
		State + (Checkpoint > 0 ? " · checkpoint " + Checkpoint : "") +
		(UpdatedUtc == default(DateTimeOffset)
			? ""
			: " · updated " + UpdatedUtc.LocalDateTime.ToString("g"));
	public string Consequence =>
		CanResume
			? HasLiveConsumer
				? "Resume wakes the paused companion at this exact checkpoint. " +
					(CanAbandon ? AbandonConsequence : "")
				: "Resume starts a new companion, verifies durable artifacts and the live VEGAS workspace, then continues from the saved checkpoint. " +
					(CanAbandon ? AbandonConsequence : "")
			: CanPause
				? "Pause is available at this durable review boundary and leaves the candidate workspace intact."
				: CanAbandon
					? AbandonConsequence
					: "Lifecycle actions become available at a durable review boundary.";

	public string AbandonConsequence
	{
		get
		{
			string scope;
			switch (_phase)
			{
				case AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.CreatingSketch:
				case AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.PlanningClip:
				case AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.RepairingProposal:
				case AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.MaterializingClip:
					scope =
						"cancels planning and removes any candidate-owned tracks created so far";
					break;
				case AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.ReconciliationConflict:
					scope =
						"discards the unresolved synchronization candidate and removes only its candidate-owned tracks";
					break;
				case AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.RoughCutReview:
				case AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.RoughCutCorrection:
				case AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.EffectsPlanReview:
				case AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.EffectsPreviewReview:
				case AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.AudioPlanReview:
				case AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.AudioPreviewReview:
				case AutoEditing.Iteration.Contracts.Assembly.AssemblyPhase.FinalReview:
					scope =
						"discards the unpromoted candidate, including accepted synchronization and candidate-only polish";
					break;
				default:
					scope = "removes only candidate-owned tracks";
					break;
			}
			return "Abandon " + scope +
				", preserves unrelated timeline content, and retains durable session artifacts for inspection.";
		}
	}
}

internal sealed class WorkbenchUiIteration
{
	public WorkbenchUiIteration(
		EditIterationSnapshot snapshot,
		CandidateTimelineSnapshot timeline)
	{
		Snapshot = snapshot;
		Timeline = timeline;
	}

	public EditIterationSnapshot Snapshot { get; }
	public CandidateTimelineSnapshot Timeline { get; }
}

public sealed class WorkbenchIterationRow
{
	public int Number { get; set; }
	public string Title { get; set; } = "";
	public string Status { get; set; } = "";
	public string Summary { get; set; } = "";
	public string Confidence { get; set; } = "";
	public string Decision { get; set; } = "";
	public string Rationale { get; set; } = "";
	public string PreviewPath { get; set; } = "";
	public IList<WorkbenchPreviewRevisionRow> PreviewRevisions { get; set; } =
		new List<WorkbenchPreviewRevisionRow>();
	public IList<WorkbenchEvidenceRow> Evidence { get; set; } =
		new List<WorkbenchEvidenceRow>();
}

public sealed class WorkbenchPreviewRevisionRow
{
	public int Attempt { get; set; }
	public string Label { get; set; } = "";
	public string AbsolutePath { get; set; } = "";
	public string Summary { get; set; } = "";
	public string Confidence { get; set; } = "";
}

public sealed class WorkbenchEvidenceRow
{
	public string Title { get; set; } = "";
	public string Detail { get; set; } = "";
}

public sealed class WorkbenchReconciliationCandidateRow
{
	public string CandidateId { get; set; } = "";
	public string MediaName { get; set; } = "";
	public string Timing { get; set; } = "";
	public string Constraint { get; set; } = "";
	public bool CanAdopt { get; set; }
	public string AdoptionMode { get; set; } = "";
}

public sealed class WorkbenchRoughCutCorrectionRow
{
	public string CorrectionId { get; set; } = "";
	public string Instruction { get; set; } = "";
	public string ExpectedOutcome { get; set; } = "";
	public string Risk { get; set; } = "";
	public string Scope { get; set; } = "";
	public string Status { get; set; } = "";
	public string Operation { get; set; } = "";
	public string FindingTrace { get; set; } = "";
	public bool CanApprove { get; set; }
	public bool CanReject { get; set; }
	public bool CanApply { get; set; }
	public bool IsTerminal { get; set; }
}

public sealed class WorkbenchRoughCutChunkRow
{
	public string Title { get; set; } = "";
	public string TimeRange { get; set; } = "";
	public double StartSeconds { get; set; }
	public double EndSeconds { get; set; }
	public string AbsolutePath { get; set; } = "";
}

public sealed class WorkbenchRoughCutEvidenceRow
{
	public string EvidenceId { get; set; } = "";
	public string Title { get; set; } = "";
	public string Detail { get; set; } = "";
	public string TimeLabel { get; set; } = "";
	public double? TimelineTimeSeconds { get; set; }
	public string AbsolutePath { get; set; } = "";
	public bool IsImage { get; set; }
}

public sealed class WorkbenchRoughCutFindingRow
{
	public string FindingId { get; set; } = "";
	public string Title { get; set; } = "";
	public string Detail { get; set; } = "";
	public string Severity { get; set; } = "";
	public string TimeRange { get; set; } = "";
	public string Checkpoints { get; set; } = "";
	public string EvidenceTrace { get; set; } = "";
	public IList<string> EvidenceIds { get; set; } = new List<string>();
	public string CorrectionTrace { get; set; } = "";
	public double? NavigateSeconds { get; set; }
}

public sealed class WorkbenchPolishActionRow
{
	public string Title { get; set; } = "";
	public string Target { get; set; } = "";
	public string Timing { get; set; } = "";
	public string Treatment { get; set; } = "";
	public string Rationale { get; set; } = "";
}

public sealed class WorkbenchPreviewChunkRow
{
	public int Number { get; set; }
	public string Label { get; set; } = "";
	public string AbsolutePath { get; set; } = "";
}

public sealed class WorkbenchTrackRow
{
	public string Name { get; set; } = "";
	public string Content { get; set; } = "";
	public string Duration { get; set; } = "";
	public double VisualWidth { get; set; } = 120;
	public Brush Color { get; set; } =
		new SolidColorBrush(System.Windows.Media.Color.FromRgb(83, 111, 219));
}
