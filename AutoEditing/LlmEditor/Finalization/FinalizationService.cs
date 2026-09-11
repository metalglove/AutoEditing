using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.LlmEditor.Assembly;
using AutoEditing.LlmEditor.Automation;
using AutoEditing.LlmEditor.Sessions;
using AutoEditing.LlmEditor.RoughCut;
using Core.Domain.Planning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AutoEditing.LlmEditor.Finalization;

internal sealed class FinalizationRequest
{
	public string SessionId { get; init; } = "";
	public string RequestId { get; init; } = "";
	public string ProjectFingerprint { get; init; } = "";
	public CandidateWorkspaceId Workspace { get; init; } = new();
	public EditPlanDocument FinalPlan { get; init; } = new();
	public CandidateMaterializationBaseline? AcceptedCandidateBaseline { get; init; }
	public bool RenderFinalPreview { get; init; }
}

internal sealed class FinalizationResult
{
	public FinalSessionReport Report { get; init; } = new();
	public FinalizationRecoveryBundle Recovery { get; init; } = new();
}

internal sealed class FinalRenderContext
{
	public string SessionRoot { get; init; } = "";
	public string SessionId { get; init; } = "";
	public CandidateWorkspaceId Workspace { get; init; } = new();
	public CandidateTimelineSnapshot Snapshot { get; init; } = new();
	public EditPlanDocument Plan { get; init; } = new();
	public string FinalPlanSha256 { get; init; } = "";
}

internal interface IFinalRenderHook
{
	Task<FinalRenderArtifact> RenderAsync(
		FinalRenderContext context,
		CancellationToken cancellationToken);
}

internal interface IFinalizationExecutor
{
	Task<FinalizationResult> FinalizeAsync(
		FinalizationRequest request,
		CancellationToken cancellationToken = default);
}

internal interface IFinalizationClock
{
	DateTimeOffset UtcNow { get; }
}

internal sealed class SystemFinalizationClock : IFinalizationClock
{
	public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Performs the final validation/promotion transaction and emits all material
/// required to inspect or reverse it. It never deletes candidate or unrelated
/// timeline content; the host promotion operation is rename-only.
/// </summary>
internal sealed class FinalizationService : IFinalizationExecutor
{
	private readonly string sessionRoot;
	private readonly string archiveRoot;
	private readonly SessionPathResolver paths;
	private readonly IVegasAutomationClient automation;
	private readonly IFinalRenderHook? renderHook;
	private readonly IFinalizationClock clock;
	private readonly AtomicFileWriter writer = new();

	public FinalizationService(
		string sessionRoot,
		string archiveRoot,
		IVegasAutomationClient automation,
		IFinalRenderHook? renderHook = null,
		IFinalizationClock? clock = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionRoot);
		ArgumentException.ThrowIfNullOrWhiteSpace(archiveRoot);
		this.sessionRoot = Path.GetFullPath(sessionRoot);
		this.archiveRoot = Path.GetFullPath(archiveRoot);
		string sessionPrefix =
			Path.TrimEndingDirectorySeparator(this.sessionRoot) +
			Path.DirectorySeparatorChar;
		if (string.Equals(
				this.sessionRoot,
				this.archiveRoot,
				StringComparison.OrdinalIgnoreCase) ||
			this.archiveRoot.StartsWith(
				sessionPrefix,
				StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException(
				"The final archive root must be outside the live session root.",
				nameof(archiveRoot));
		paths = new SessionPathResolver(this.sessionRoot);
		this.automation = automation ?? throw new ArgumentNullException(nameof(automation));
		this.renderHook = renderHook;
		this.clock = clock ?? new SystemFinalizationClock();
	}

	public async Task<FinalizationResult> FinalizeAsync(
		FinalizationRequest request,
		CancellationToken cancellationToken = default)
	{
		ValidateRequest(request);
		RequireBoundProject(request.ProjectFingerprint);
		string intentPath = paths.Resolve("finalization/promotion-intent.json");
		if (File.Exists(intentPath))
			return await ResumeFinalizationAsync(
				request,
				intentPath,
				cancellationToken).ConfigureAwait(false);

		string promotionId = "promotion-" + Guid.NewGuid().ToString("N");

		EditPlanDocument validatedPlan = ClonePlan(request.FinalPlan);
		EditPlanDocumentValidator.ValidateAndNormalize(validatedPlan);
		CandidateTimelineSnapshot snapshot =
			await automation.ExecuteAsync<GetCandidateSnapshotRequest, CandidateTimelineSnapshot>(
				VegasOperations.GetCandidateSnapshot,
				new GetCandidateSnapshotRequest { Workspace = request.Workspace },
				"finalize-snapshot-" + promotionId,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		ValidateHostIdentity(request.ProjectFingerprint);
		ValidateFinalCandidate(snapshot);

		// The final plan is made authoritative to the actual candidate once more.
		// Missing/extra/ambiguous events and unsupported velocity stop here.
		AssemblyTimelineReconciler.Apply(
			validatedPlan,
			snapshot,
			validatedPlan.Montage.Placements.Count,
			request.AcceptedCandidateBaseline);
		EditPlanDocumentValidator.ValidateAndNormalize(validatedPlan);
		string snapshotSha256 = SnapshotHash(snapshot);
		string finalPlanJson = EditPlanDocumentSerializer.SerializePlan(validatedPlan);
		string finalPlanSha256 = ContentHash(finalPlanJson);
		WriteImmutable(
			"finalization/final-plan.json",
			finalPlanJson,
			"final plan");

		FinalRenderArtifact? finalRender = null;
		if (request.RenderFinalPreview)
		{
			if (renderHook == null)
				throw new InvalidOperationException(
					"A final preview was requested but no final render hook is configured.");
			finalRender = await renderHook.RenderAsync(
				new FinalRenderContext
				{
					SessionRoot = sessionRoot,
					SessionId = request.SessionId,
					Workspace = request.Workspace,
					Snapshot = snapshot,
					Plan = validatedPlan,
					FinalPlanSha256 = finalPlanSha256
				},
				cancellationToken).ConfigureAwait(false);
			ValidateRenderArtifact(finalRender);
		}

		FinalizationPromotionIntent intent = new()
		{
			SessionId = request.SessionId,
			RequestId = request.RequestId,
			PromotionId = promotionId,
			CreatedUtc = clock.UtcNow,
			ExpectedProjectFingerprint = request.ProjectFingerprint,
			FinalPlanSha256 = finalPlanSha256,
			ValidatedCandidateSnapshot = snapshot,
			ValidatedCandidateSnapshotSha256 = snapshotSha256,
			PlannedTrackMappings = CandidatePromotionContract.Plan(
				snapshot,
				Array.Empty<string>()),
			FinalRenderRequested = request.RenderFinalPreview,
			FinalRender = finalRender
		};
		// Persisted before the first promotion mutation.
		WriteImmutable(
			"finalization/promotion-intent.json",
			ContractSerializer.Serialize(intent),
			"promotion intent");
		return await CompleteFinalizationAsync(
			request,
			intent,
			validatedPlan,
			resumingPersistedIntent: false,
			cancellationToken).ConfigureAwait(false);
	}

	private async Task<FinalizationResult> ResumeFinalizationAsync(
		FinalizationRequest request,
		string intentPath,
		CancellationToken cancellationToken)
	{
		FinalizationPromotionIntent intent =
			ContractSerializer.Deserialize<FinalizationPromotionIntent>(
				File.ReadAllText(intentPath));
		ValidatePersistedIntent(request, intent);
		string planPath = paths.Resolve("finalization/final-plan.json");
		if (!File.Exists(planPath))
			throw new InvalidDataException(
				"A persisted promotion intent has no canonical final plan.");
		string finalPlanJson = File.ReadAllText(planPath);
		if (!string.Equals(
			ContentHash(finalPlanJson),
			intent.FinalPlanSha256,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The canonical final plan no longer matches the promotion intent.");
		EditPlanDocument persistedPlan =
			EditPlanDocumentSerializer.DeserializePlan(finalPlanJson);
		EditPlanDocumentValidator.ValidateAndNormalize(persistedPlan);

		// Reconcile the caller's plan against the exact persisted live snapshot.
		// A retry therefore cannot silently switch to another final edit.
		EditPlanDocument requestedPlan = ClonePlan(request.FinalPlan);
		EditPlanDocumentValidator.ValidateAndNormalize(requestedPlan);
		AssemblyTimelineReconciler.Apply(
			requestedPlan,
			intent.ValidatedCandidateSnapshot,
			requestedPlan.Montage.Placements.Count,
			request.AcceptedCandidateBaseline);
		EditPlanDocumentValidator.ValidateAndNormalize(requestedPlan);
		if (!string.Equals(
			ContentHash(EditPlanDocumentSerializer.SerializePlan(requestedPlan)),
			intent.FinalPlanSha256,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"The finalization retry proposes a different final plan.");
		if (intent.FinalRender != null)
			ValidateRenderArtifact(intent.FinalRender);

		return await CompleteFinalizationAsync(
			request,
			intent,
			persistedPlan,
			resumingPersistedIntent: true,
			cancellationToken).ConfigureAwait(false);
	}

	private async Task<FinalizationResult> CompleteFinalizationAsync(
		FinalizationRequest request,
		FinalizationPromotionIntent intent,
		EditPlanDocument validatedPlan,
		bool resumingPersistedIntent,
		CancellationToken cancellationToken)
	{
		if (File.Exists(paths.Resolve("finalization/rollback-result.json")))
			throw new InvalidOperationException(
				"This promotion was explicitly rolled back. Start a new " +
				"finalization transaction instead of reusing its completed intent.");
		FinalizationRecoveryBundle recovery;
		string recoveryPath = paths.Resolve("finalization/recovery-bundle.json");
		if (File.Exists(recoveryPath))
		{
			recovery =
				ContractSerializer.Deserialize<FinalizationRecoveryBundle>(
					File.ReadAllText(recoveryPath));
			ValidateRecoveryBundle(intent, recovery);
		}
		else
		{
			if (resumingPersistedIntent)
				await RestorePrePromotionStateAsync(
					intent,
					cancellationToken).ConfigureAwait(false);
			PromoteCandidateResult promotion =
				await automation.ExecuteAsync<
					PromoteCandidateRequest,
					PromoteCandidateResult>(
					VegasOperations.PromoteCandidate,
					new PromoteCandidateRequest
					{
						PromotionId = intent.PromotionId,
						Workspace = intent.ValidatedCandidateSnapshot.Workspace,
						ExpectedCandidateSnapshotSha256 =
							intent.ValidatedCandidateSnapshotSha256
					},
					"finalize-promote-" + intent.PromotionId,
					cancellationToken: cancellationToken).ConfigureAwait(false);
			ValidateHostIdentity(intent.ExpectedProjectFingerprint);
			ValidatePromotionResult(intent, promotion);
			recovery = new FinalizationRecoveryBundle
			{
				PromotedUtc = clock.UtcNow,
				Host = automation.LastHost!,
				Promotion = promotion
			};
			WriteImmutable(
				"finalization/recovery-bundle.json",
				ContractSerializer.Serialize(recovery),
				"promotion recovery bundle");
		}

		FinalizationResult? completed =
			TryReadCompletedFinalization(request, intent, recovery);
		if (completed != null) return completed;

		FinalSessionReport report = BuildReport(
			request,
			validatedPlan,
			intent.FinalPlanSha256,
			intent.FinalRender,
			recovery);
		report.ArchivedArtifacts = BuildArtifactManifest();
		string reportPath = paths.Resolve("finalization/final-session-report.json");
		writer.WriteText(reportPath, ContractSerializer.Serialize(report));

		FinalArtifactArchiveReceipt archive = CreateOrReuseArchive(
			request.SessionId,
			intent.PromotionId);
		report.Archive = archive;
		writer.WriteText(
			paths.Resolve("finalization/archive-receipt.json"),
			ContractSerializer.Serialize(archive));
		writer.WriteText(reportPath, ContractSerializer.Serialize(report));
		return new FinalizationResult { Report = report, Recovery = recovery };
	}

	private async Task RestorePrePromotionStateAsync(
		FinalizationPromotionIntent intent,
		CancellationToken cancellationToken)
	{
		PromoteCandidateResult interrupted =
			BuildInterruptedPromotionRecovery(intent);
		RollbackCandidatePromotionResult restored =
			await automation.ExecuteAsync<
				RollbackCandidatePromotionRequest,
				RollbackCandidatePromotionResult>(
				VegasOperations.RollbackCandidatePromotion,
				new RollbackCandidatePromotionRequest
				{
					Promotion = interrupted
				},
				"finalize-resume-restore-" + intent.PromotionId,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		ValidateHostIdentity(intent.ExpectedProjectFingerprint);
		if (!string.Equals(
				restored.PromotionId,
				intent.PromotionId,
				StringComparison.Ordinal) ||
			!string.Equals(
				restored.RestoredSnapshotSha256,
				intent.ValidatedCandidateSnapshotSha256,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"Interrupted finalization recovery did not restore the exact " +
				"validated candidate snapshot.");
		WriteImmutable(
			"finalization/promotion-resume-restoration.json",
			ContractSerializer.Serialize(restored),
			"promotion resume restoration");
	}

	private FinalizationResult? TryReadCompletedFinalization(
		FinalizationRequest request,
		FinalizationPromotionIntent intent,
		FinalizationRecoveryBundle recovery)
	{
		string reportPath = paths.Resolve("finalization/final-session-report.json");
		string receiptPath = paths.Resolve("finalization/archive-receipt.json");
		if (!File.Exists(reportPath) || !File.Exists(receiptPath))
			return null;
		FinalSessionReport report =
			ContractSerializer.Deserialize<FinalSessionReport>(
				File.ReadAllText(reportPath));
		FinalArtifactArchiveReceipt receipt =
			ContractSerializer.Deserialize<FinalArtifactArchiveReceipt>(
				File.ReadAllText(receiptPath));
		ValidateCompletedReport(request, intent, recovery, report, receipt);
		return new FinalizationResult { Report = report, Recovery = recovery };
	}

	public async Task<RollbackCandidatePromotionResult> RollbackAsync(
		CancellationToken cancellationToken = default)
	{
		string bundlePath = paths.Resolve("finalization/recovery-bundle.json");
		PromoteCandidateResult promotion;
		string projectFingerprint;
		if (File.Exists(bundlePath))
		{
			FinalizationRecoveryBundle bundle =
				ContractSerializer.Deserialize<FinalizationRecoveryBundle>(
					File.ReadAllText(bundlePath));
			if (bundle.Host == null ||
				string.IsNullOrWhiteSpace(bundle.Host.ProjectFingerprint))
				throw new InvalidDataException(
					"The promotion recovery bundle has no project identity.");
			promotion = bundle.Promotion;
			projectFingerprint = bundle.Host.ProjectFingerprint;
		}
		else
		{
			string intentPath = paths.Resolve(
				"finalization/promotion-intent.json");
			if (!File.Exists(intentPath))
				throw new InvalidOperationException(
					"No recoverable candidate promotion is available for this session.");
			FinalizationPromotionIntent promotionIntent =
				ContractSerializer.Deserialize<FinalizationPromotionIntent>(
					File.ReadAllText(intentPath));
			promotion = BuildInterruptedPromotionRecovery(promotionIntent);
			projectFingerprint = promotionIntent.ExpectedProjectFingerprint;
		}
		CandidatePromotionContract.ValidateResult(promotion);
		RequireBoundProject(projectFingerprint);
		string rollbackAttemptId = "rollback-" + promotion.PromotionId;
		string rollbackResultRelative =
			"finalization/rollback-attempts/" +
			rollbackAttemptId + ".result.json";
		string rollbackResultPath = paths.Resolve(rollbackResultRelative);
		if (File.Exists(rollbackResultPath))
		{
			RollbackCandidatePromotionResult completed =
				ContractSerializer.Deserialize<RollbackCandidatePromotionResult>(
					File.ReadAllText(rollbackResultPath));
			if (!string.Equals(
					completed.PromotionId,
					promotion.PromotionId,
					StringComparison.Ordinal) ||
				!string.Equals(
					completed.RestoredSnapshotSha256,
					promotion.CandidateSnapshotSha256,
					StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					"The persisted rollback receipt targets different promotion state.");
			return completed;
		}
		string rollbackIntentRelative =
			"finalization/rollback-attempts/" +
			rollbackAttemptId + ".intent.json";
		string rollbackIntentPath = paths.Resolve(rollbackIntentRelative);
		FinalizationRollbackIntent intent;
		if (File.Exists(rollbackIntentPath))
		{
			intent =
				ContractSerializer.Deserialize<FinalizationRollbackIntent>(
					File.ReadAllText(rollbackIntentPath));
			if (!string.Equals(
					intent.RollbackAttemptId,
					rollbackAttemptId,
					StringComparison.Ordinal) ||
				!string.Equals(
					intent.PromotionId,
					promotion.PromotionId,
					StringComparison.Ordinal) ||
				!string.Equals(
					intent.ExpectedProjectFingerprint,
					projectFingerprint,
					StringComparison.Ordinal) ||
				!string.Equals(
					intent.ExpectedPromotedSnapshotSha256,
					promotion.PromotedSnapshotSha256,
					StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					"The persisted rollback intent targets different promotion state.");
		}
		else
		{
			intent = new FinalizationRollbackIntent
			{
				RollbackAttemptId = rollbackAttemptId,
				PromotionId = promotion.PromotionId,
				CreatedUtc = clock.UtcNow,
				ExpectedProjectFingerprint = projectFingerprint,
				ExpectedPromotedSnapshotSha256 =
					promotion.PromotedSnapshotSha256
			};
			WriteImmutable(
				rollbackIntentRelative,
				ContractSerializer.Serialize(intent),
				"rollback intent");
		}

		RollbackCandidatePromotionResult result =
			await automation.ExecuteAsync<
				RollbackCandidatePromotionRequest,
				RollbackCandidatePromotionResult>(
				VegasOperations.RollbackCandidatePromotion,
				new RollbackCandidatePromotionRequest
				{
					Promotion = promotion
				},
				"finalize-" + rollbackAttemptId,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		ValidateHostIdentity(projectFingerprint);
		if (!string.Equals(
			result.PromotionId,
			promotion.PromotionId,
			StringComparison.Ordinal) ||
			!string.Equals(
			result.RestoredSnapshotSha256,
			promotion.CandidateSnapshotSha256,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"VEGAS returned a rollback result that does not match the recovery bundle.");
		WriteImmutable(
			rollbackResultRelative,
			ContractSerializer.Serialize(result),
			"rollback result");
		writer.WriteText(
			paths.Resolve("finalization/rollback-result.json"),
			ContractSerializer.Serialize(result));
		return result;
	}

	private static PromoteCandidateResult BuildInterruptedPromotionRecovery(
		FinalizationPromotionIntent intent)
	{
		if (intent == null ||
			string.IsNullOrWhiteSpace(intent.PromotionId) ||
			intent.ValidatedCandidateSnapshot == null ||
			intent.PlannedTrackMappings == null ||
			intent.PlannedTrackMappings.Count == 0)
			throw new InvalidDataException(
				"The pre-mutation promotion intent is incomplete.");
		CandidateTimelineSnapshot promoted =
			ContractSerializer.Deserialize<CandidateTimelineSnapshot>(
				ContractSerializer.Serialize(intent.ValidatedCandidateSnapshot));
		foreach (CandidatePromotionTrackMapping mapping in
			intent.PlannedTrackMappings)
		{
			CandidateTrackSnapshot track = promoted.Tracks.SingleOrDefault(
				item => string.Equals(
					item.Name,
					mapping.CandidateName,
					StringComparison.Ordinal))
				?? throw new InvalidDataException(
					"The promotion intent rename map does not cover its snapshot.");
			track.Name = mapping.FinalName;
		}
		return new PromoteCandidateResult
		{
			PromotionId = intent.PromotionId,
			Workspace = intent.ValidatedCandidateSnapshot.Workspace,
			CandidateSnapshot = intent.ValidatedCandidateSnapshot,
			CandidateSnapshotSha256 =
				intent.ValidatedCandidateSnapshotSha256,
			PromotedSnapshot = promoted,
			PromotedSnapshotSha256 = SnapshotHash(promoted),
			TrackMappings = intent.PlannedTrackMappings
		};
	}

	private FinalSessionReport BuildReport(
		FinalizationRequest request,
		EditPlanDocument plan,
		string finalPlanSha256,
		FinalRenderArtifact? finalRender,
		FinalizationRecoveryBundle recovery)
	{
		IList<InferenceUsageRecord> usage = ReadUsageRecords();
		IList<FinalModelUsageReport> byModel = AggregateByModel(usage);
		InferenceUsageSummary total = AggregateSessionUsage(request.SessionId, usage);
		double duration = plan.Montage.Placements.Count == 0
			? 0
			: plan.Montage.Placements.Max(item => item.TimelineEndSeconds);
		return new FinalSessionReport
		{
			SessionId = request.SessionId,
			RequestId = request.RequestId,
			PromotionId = recovery.Promotion.PromotionId,
			CompletedUtc = clock.UtcNow,
			ProjectPath = recovery.Host.ProjectPath ?? "",
			ProjectFingerprint = recovery.Host.ProjectFingerprint ?? "",
			FinalPlanSha256 = finalPlanSha256,
			PromotedSnapshotSha256 = recovery.Promotion.PromotedSnapshotSha256,
			PlacementCount = plan.Montage.Placements.Count,
			TimelineDuration = TimeSpan.FromSeconds(duration),
			FinalRender = finalRender,
			ModelUsage = byModel,
			SessionUsage = total
		};
	}

	private IList<FinalArtifactEntry> BuildArtifactManifest()
	{
		return EnumerateStableSessionFiles()
			.Where(path => !string.Equals(
				paths.GetRelativePath(path),
				"finalization/final-session-report.json",
				StringComparison.OrdinalIgnoreCase))
			.Select(path => new FinalArtifactEntry
			{
				RelativePath = paths.GetRelativePath(path),
				LengthBytes = new FileInfo(path).Length,
				Sha256 = FileHash(path)
			})
			.OrderBy(item => item.RelativePath, StringComparer.Ordinal)
			.ToList();
	}

	private FinalArtifactArchiveReceipt CreateOrReuseArchive(
		string sessionId,
		string promotionId)
	{
		string directory = Path.Combine(archiveRoot, sessionId);
		Directory.CreateDirectory(directory);
		string destination = Path.Combine(directory, promotionId + ".zip");
		string receiptPath = paths.Resolve("finalization/archive-receipt.json");
		if (File.Exists(destination))
		{
			int entries;
			using (ZipArchive existing = ZipFile.OpenRead(destination))
				entries = existing.Entries.Count;
			FinalArtifactArchiveReceipt receipt = new()
			{
				ArchivePath = destination,
				CreatedUtc = File.GetCreationTimeUtc(destination),
				LengthBytes = new FileInfo(destination).Length,
				Sha256 = FileHash(destination),
				EntryCount = entries
			};
			if (File.Exists(receiptPath))
			{
				FinalArtifactArchiveReceipt persisted =
					ContractSerializer.Deserialize<FinalArtifactArchiveReceipt>(
						File.ReadAllText(receiptPath));
				if (!string.Equals(
						Path.GetFullPath(persisted.ArchivePath),
						Path.GetFullPath(receipt.ArchivePath),
						StringComparison.OrdinalIgnoreCase) ||
					persisted.LengthBytes != receipt.LengthBytes ||
					persisted.EntryCount != receipt.EntryCount ||
					!string.Equals(
						persisted.Sha256,
						receipt.Sha256,
						StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException(
						"The finalization archive no longer matches its receipt.");
				return persisted;
			}
			return receipt;
		}
		string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
		int count = 0;
		using (FileStream stream = new FileStream(
			temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
		using (ZipArchive archive = new ZipArchive(
			stream, ZipArchiveMode.Create, leaveOpen: false))
		{
			foreach (string path in EnumerateStableSessionFiles())
			{
				string relative = paths.GetRelativePath(path);
				ZipArchiveEntry entry = archive.CreateEntry(
					relative,
					CompressionLevel.Optimal);
				entry.LastWriteTime = File.GetLastWriteTimeUtc(path);
				using Stream input = File.OpenRead(path);
				using Stream output = entry.Open();
				input.CopyTo(output);
				count++;
			}
		}
		File.Move(temporary, destination);
		return new FinalArtifactArchiveReceipt
		{
			ArchivePath = destination,
			CreatedUtc = clock.UtcNow,
			LengthBytes = new FileInfo(destination).Length,
			Sha256 = FileHash(destination),
			EntryCount = count
		};
	}

	private static void ValidatePersistedIntent(
		FinalizationRequest request,
		FinalizationPromotionIntent intent)
	{
		if (intent == null ||
			string.IsNullOrWhiteSpace(intent.PromotionId) ||
			intent.CreatedUtc == default ||
			intent.ValidatedCandidateSnapshot == null ||
			intent.ValidatedCandidateSnapshot.Workspace == null ||
			intent.PlannedTrackMappings == null ||
			intent.PlannedTrackMappings.Count == 0 ||
			!string.Equals(intent.SessionId, request.SessionId, StringComparison.Ordinal) ||
			!string.Equals(intent.RequestId, request.RequestId, StringComparison.Ordinal) ||
			!string.Equals(
				intent.ExpectedProjectFingerprint,
				request.ProjectFingerprint,
				StringComparison.Ordinal) ||
			!SameWorkspace(
				intent.ValidatedCandidateSnapshot.Workspace,
				request.Workspace) ||
			intent.FinalRenderRequested != request.RenderFinalPreview)
			throw new InvalidDataException(
				"The persisted promotion intent is incomplete or belongs to a " +
				"different finalization request.");
		if (!string.Equals(
			SnapshotHash(intent.ValidatedCandidateSnapshot),
			intent.ValidatedCandidateSnapshotSha256,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The persisted promotion intent snapshot failed SHA-256 validation.");
	}

	private static void ValidateRecoveryBundle(
		FinalizationPromotionIntent intent,
		FinalizationRecoveryBundle recovery)
	{
		if (recovery == null ||
			recovery.PromotedUtc == default ||
			recovery.Host == null ||
			!string.Equals(
				recovery.Host.ProjectFingerprint,
				intent.ExpectedProjectFingerprint,
				StringComparison.Ordinal))
			throw new InvalidDataException(
				"The promotion recovery bundle has invalid project identity.");
		ValidatePromotionResult(intent, recovery.Promotion);
	}

	private void ValidateCompletedReport(
		FinalizationRequest request,
		FinalizationPromotionIntent intent,
		FinalizationRecoveryBundle recovery,
		FinalSessionReport report,
		FinalArtifactArchiveReceipt receipt)
	{
		if (report == null ||
			report.CompletedUtc == default ||
			!string.Equals(report.SessionId, request.SessionId, StringComparison.Ordinal) ||
			!string.Equals(report.RequestId, request.RequestId, StringComparison.Ordinal) ||
			!string.Equals(report.PromotionId, intent.PromotionId, StringComparison.Ordinal) ||
			!string.Equals(
				report.ProjectFingerprint,
				intent.ExpectedProjectFingerprint,
				StringComparison.Ordinal) ||
			!string.Equals(
				report.FinalPlanSha256,
				intent.FinalPlanSha256,
				StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(
				report.PromotedSnapshotSha256,
				recovery.Promotion.PromotedSnapshotSha256,
				StringComparison.OrdinalIgnoreCase) ||
			report.Archive == null ||
			!string.Equals(
				Path.GetFullPath(report.Archive.ArchivePath),
				Path.GetFullPath(receipt.ArchivePath),
				StringComparison.OrdinalIgnoreCase) ||
			report.Archive.LengthBytes != receipt.LengthBytes ||
			report.Archive.EntryCount != receipt.EntryCount ||
			!string.Equals(
				report.Archive.Sha256,
				receipt.Sha256,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The completed finalization report does not match its durable intent.");
		string archivePath = Path.GetFullPath(receipt.ArchivePath);
		string expectedArchivePath = Path.GetFullPath(Path.Combine(
			archiveRoot,
			request.SessionId,
			intent.PromotionId + ".zip"));
		if (!string.Equals(
			archivePath,
			expectedArchivePath,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The completed archive receipt points outside its finalization slot.");
		if (!File.Exists(archivePath) ||
			new FileInfo(archivePath).Length != receipt.LengthBytes ||
			!string.Equals(
				FileHash(archivePath),
				receipt.Sha256,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The completed finalization archive is missing or corrupt.");
		using ZipArchive archive = ZipFile.OpenRead(archivePath);
		if (archive.Entries.Count != receipt.EntryCount)
			throw new InvalidDataException(
				"The completed finalization archive entry count changed.");
	}

	private IEnumerable<string> EnumerateStableSessionFiles()
	{
		foreach (string path in Directory.EnumerateFiles(
			sessionRoot, "*", SearchOption.AllDirectories))
		{
			string relative = paths.GetRelativePath(path);
			if (string.Equals(
				relative, "assembly/runtime.lock", StringComparison.OrdinalIgnoreCase) ||
				path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
				continue;
			FileAttributes attributes = File.GetAttributes(path);
			if ((attributes & FileAttributes.ReparsePoint) != 0)
				throw new InvalidDataException(
					"Session archive refuses filesystem links: " + relative);
			yield return path;
		}
	}

	private IList<InferenceUsageRecord> ReadUsageRecords()
	{
		string path = Path.Combine(sessionRoot, "usage.ndjson");
		if (!File.Exists(path)) return new List<InferenceUsageRecord>();
		List<InferenceUsageRecord> records = new();
		int lineNumber = 0;
		foreach (string line in File.ReadLines(path))
		{
			lineNumber++;
			if (string.IsNullOrWhiteSpace(line)) continue;
			try
			{
				InferenceUsageRecord? record =
					JsonConvert.DeserializeObject<InferenceUsageRecord>(line);
				if (record == null || string.IsNullOrWhiteSpace(record.Model))
					throw new JsonException("Usage record has no model.");
				records.Add(record);
			}
			catch (Exception exception)
			{
				throw new InvalidDataException(
					$"Usage ledger line {lineNumber} is invalid.",
					exception);
			}
		}
		return records;
	}

	private static IList<FinalModelUsageReport> AggregateByModel(
		IList<InferenceUsageRecord> records)
	{
		return records
			.GroupBy(
				item => new { item.Model, Provider = item.Provider ?? "" },
				item => item)
			.OrderBy(group => group.Key.Model, StringComparer.Ordinal)
			.ThenBy(group => group.Key.Provider, StringComparer.Ordinal)
			.Select(group => new FinalModelUsageReport
			{
				Model = group.Key.Model,
				Provider = group.Key.Provider,
				CallCount = group.LongCount(),
				PromptTokens = group.Sum(item => (long)item.PromptTokens),
				CachedPromptTokens = group.Sum(item => (long)item.CachedPromptTokens),
				GeneratedTokens = group.Sum(item => (long)item.GeneratedTokens),
				TotalTokens = group.Sum(item => (long)item.TotalTokens),
				PromptMilliseconds = group.Sum(item => item.PromptMilliseconds),
				TimeToFirstTokenMilliseconds =
					group.Sum(item => item.TimeToFirstTokenMilliseconds),
				TimeToFirstTokenSampleCount =
					group.LongCount(item => item.TimeToFirstTokenMilliseconds > 0),
				GenerationMilliseconds = group.Sum(item => item.GenerationMilliseconds),
				ContextTokensPeak = group
					.Where(item => item.ContextTokensPeak.HasValue)
					.Select(item => item.ContextTokensPeak)
					.DefaultIfEmpty(null)
					.Max(),
				RetryCount = group.Sum(item => (long)item.RetryCount),
				Cost = SumCost(group),
				CostCurrency = CommonCurrency(group.Select(item => item.CostCurrency))
			})
			.ToList();
	}

	private static InferenceUsageSummary AggregateSessionUsage(
		string sessionId,
		IList<InferenceUsageRecord> records)
	{
		return new InferenceUsageSummary
		{
			Scope = "final-session",
			SessionId = sessionId,
			Model = records.Select(item => item.Model)
				.Distinct(StringComparer.Ordinal).Count() == 1
				? records[0].Model
				: null,
			Provider = records.Select(item => item.Provider ?? "")
				.Distinct(StringComparer.Ordinal).Count() == 1
				? records[0].Provider
				: null,
			HasMixedModels = records.Select(item => item.Model)
				.Distinct(StringComparer.Ordinal).Count() > 1,
			HasMixedProviders = records.Select(item => item.Provider ?? "")
				.Distinct(StringComparer.Ordinal).Count() > 1,
			UpdatedUtc = records.Count == 0
				? default
				: records.Max(item => item.TimestampUtc),
			CallCount = records.Count,
			PromptTokens = records.Sum(item => (long)item.PromptTokens),
			CachedPromptTokens = records.Sum(item => (long)item.CachedPromptTokens),
			GeneratedTokens = records.Sum(item => (long)item.GeneratedTokens),
			TotalTokens = records.Sum(item => (long)item.TotalTokens),
			PromptMilliseconds = records.Sum(item => item.PromptMilliseconds),
			TimeToFirstTokenMilliseconds =
				records.Sum(item => item.TimeToFirstTokenMilliseconds),
			TimeToFirstTokenSampleCount =
				records.LongCount(item => item.TimeToFirstTokenMilliseconds > 0),
			GenerationMilliseconds = records.Sum(item => item.GenerationMilliseconds),
			ContextTokensPeak = records
				.Where(item => item.ContextTokensPeak.HasValue)
				.Select(item => item.ContextTokensPeak)
				.DefaultIfEmpty(null)
				.Max(),
			RetryCount = records.Sum(item => (long)item.RetryCount),
			Cost = SumCost(records),
			CostCurrency = CommonCurrency(records.Select(item => item.CostCurrency))
		};
	}

	private static bool SameCurrency(IEnumerable<string?> currencies) =>
		currencies.Where(value => !string.IsNullOrWhiteSpace(value))
			.Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Count() <= 1;

	private static decimal? SumCost(
		IEnumerable<InferenceUsageRecord> records)
	{
		List<InferenceUsageRecord> values = records.ToList();
		if (!SameCurrency(values.Select(item => item.CostCurrency)))
			return null;
		List<decimal> costs = values
			.Where(item => item.Cost.HasValue)
			.Select(item => item.Cost!.Value)
			.ToList();
		return costs.Count == 0 ? null : costs.Sum();
	}

	private static string CommonCurrency(IEnumerable<string?> currencies)
	{
		List<string?> values = currencies
			.Where(value => !string.IsNullOrWhiteSpace(value))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Take(2)
			.ToList();
		return values.Count == 1 ? values[0]! : "";
	}

	private static void ValidateFinalCandidate(CandidateTimelineSnapshot snapshot)
	{
		if (snapshot == null)
			throw new InvalidDataException("VEGAS returned no final candidate snapshot.");
		if (snapshot.Workspace == null)
			throw new InvalidDataException(
				"The final candidate snapshot has no workspace identity.");
		if (snapshot.Warnings != null && snapshot.Warnings.Count > 0)
			throw new InvalidOperationException(
				"The final candidate snapshot contains unresolved warnings: " +
				string.Join("; ", snapshot.Warnings));
		CandidatePromotionContract.Plan(snapshot, Array.Empty<string>());
		if (snapshot.Tracks.Select(item => item.Name)
			.Distinct(StringComparer.Ordinal).Count() != snapshot.Tracks.Count)
			throw new InvalidOperationException(
				"The final candidate contains duplicate track identities.");
		foreach (CandidateEventSnapshot item in snapshot.Tracks.SelectMany(
			track => track.Events ?? Array.Empty<CandidateEventSnapshot>()))
		{
			if (item.TimelineStart < TimeSpan.Zero ||
				item.TimelineDuration <= TimeSpan.Zero ||
				item.SourceOffset < TimeSpan.Zero)
				throw new InvalidOperationException(
					"The final candidate contains an invalid event range.");
		}
	}

	private void ValidateRenderArtifact(FinalRenderArtifact artifact)
	{
		if (artifact == null || string.IsNullOrWhiteSpace(artifact.RelativePath) ||
			string.IsNullOrWhiteSpace(artifact.Sha256) ||
			string.IsNullOrWhiteSpace(artifact.ArtifactKind))
			throw new InvalidDataException(
				"The final render hook returned incomplete evidence.");
		string path = paths.Resolve(artifact.RelativePath);
		if (!File.Exists(path))
			throw new FileNotFoundException(
				"The final render hook did not produce its declared artifact.",
				path);
		FileInfo info = new(path);
		if (info.Length != artifact.LengthBytes ||
			!string.Equals(FileHash(path), artifact.Sha256, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The final render evidence does not match its declared length and SHA-256.");
		foreach (FinalRenderComponent component in
			artifact.Components ?? Array.Empty<FinalRenderComponent>())
		{
			string componentPath = paths.Resolve(component.RelativePath);
			if (!File.Exists(componentPath) ||
				new FileInfo(componentPath).Length != component.LengthBytes ||
				!string.Equals(
					FileHash(componentPath),
					component.Sha256,
					StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					"A final render component is missing or failed SHA-256 validation.");
		}
	}

	private void RequireBoundProject(string expected)
	{
		if (string.IsNullOrWhiteSpace(expected))
			throw new InvalidOperationException(
				"Finalization requires a persisted VEGAS project fingerprint.");
		if (!string.Equals(
			automation.ExpectedProjectFingerprint,
			expected,
			StringComparison.Ordinal))
			throw new InvalidOperationException(
				"The automation client is not bound to the finalization project's fingerprint.");
	}

	private void ValidateHostIdentity(string expected)
	{
		VegasHostIdentity host = automation.LastHost
			?? throw new InvalidDataException("VEGAS returned no host identity.");
		if (!string.Equals(
			host.ProjectFingerprint,
			expected,
			StringComparison.Ordinal))
			throw new InvalidOperationException(
				"The active VEGAS project changed during finalization.");
	}

	private static void ValidateRequest(FinalizationRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);
		ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestId);
		ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectFingerprint);
		ArgumentNullException.ThrowIfNull(request.Workspace);
		request.Workspace.Validate();
		ArgumentNullException.ThrowIfNull(request.FinalPlan);
		ArgumentNullException.ThrowIfNull(request.AcceptedCandidateBaseline);
		if (!string.Equals(
			request.RequestId, request.FinalPlan.RequestId, StringComparison.Ordinal))
			throw new InvalidOperationException(
				"The final plan request ID does not match the finalization request.");
	}

	private static void ValidatePromotionResult(
		FinalizationPromotionIntent intent,
		PromoteCandidateResult result)
	{
		CandidatePromotionContract.ValidateResult(result);
		if (!string.Equals(
			result.PromotionId, intent.PromotionId, StringComparison.Ordinal) ||
			!SameWorkspace(result.Workspace, intent.ValidatedCandidateSnapshot.Workspace))
			throw new InvalidDataException(
				"VEGAS returned a promotion for a different intent or workspace.");
		if (!string.Equals(
			result.CandidateSnapshotSha256,
			intent.ValidatedCandidateSnapshotSha256,
			StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(
			SnapshotHash(result.CandidateSnapshot),
			result.CandidateSnapshotSha256,
			StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(
			SnapshotHash(result.PromotedSnapshot),
			result.PromotedSnapshotSha256,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"VEGAS promotion evidence failed snapshot integrity validation.");
		if (result.TrackMappings.Count != intent.PlannedTrackMappings.Count ||
			result.TrackMappings.Zip(
				intent.PlannedTrackMappings,
				(actual, planned) =>
					string.Equals(
						actual.CandidateName,
						planned.CandidateName,
						StringComparison.Ordinal) &&
					string.Equals(
						actual.FinalName,
						planned.FinalName,
						StringComparison.Ordinal))
				.Any(matches => !matches))
			throw new InvalidDataException(
				"VEGAS promotion used a different track rename map.");
	}

	private static bool SameWorkspace(
		CandidateWorkspaceId left,
		CandidateWorkspaceId right) =>
		left != null && right != null &&
		string.Equals(left.SessionId, right.SessionId, StringComparison.Ordinal) &&
		left.Iteration == right.Iteration &&
		string.Equals(left.Nonce, right.Nonce, StringComparison.Ordinal);

	private static EditPlanDocument ClonePlan(EditPlanDocument plan) =>
		EditPlanDocumentSerializer.DeserializePlan(
			EditPlanDocumentSerializer.SerializePlan(plan));

	private static string SnapshotHash(CandidateTimelineSnapshot snapshot) =>
		ContractHash.Compute(JToken.FromObject(snapshot));

	private static string ContentHash(string content) =>
		Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(content)))
			.ToLowerInvariant();

	private static string FileHash(string path) =>
		Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
			.ToLowerInvariant();

	private void WriteImmutable(
		string relativePath,
		string content,
		string artifactName)
	{
		string path = paths.Resolve(relativePath);
		if (File.Exists(path))
		{
			if (string.Equals(
				File.ReadAllText(path),
				content,
				StringComparison.Ordinal))
				return;
			throw new InvalidOperationException(
				"The persisted " + artifactName +
				" already exists with different content.");
		}
		writer.WriteText(path, content);
	}
}

/// <summary>
/// Concrete optional final-render hook using the already bounded, resumable
/// full-timeline renderer. The returned artifact is the immutable manifest and
/// every chunk is retained as separately hash-verified component evidence.
/// </summary>
internal sealed class RoughCutFinalRenderHook : IFinalRenderHook
{
	private readonly string sessionRoot;
	private readonly SessionPathResolver paths;
	private readonly RoughCutFullRenderService renderer;

	public RoughCutFinalRenderHook(
		string sessionRoot,
		RoughCutFullRenderService renderer)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionRoot);
		this.sessionRoot = Path.GetFullPath(sessionRoot);
		paths = new SessionPathResolver(this.sessionRoot);
		this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
	}

	public async Task<FinalRenderArtifact> RenderAsync(
		FinalRenderContext context,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		if (!string.Equals(
			Path.GetFullPath(context.SessionRoot),
			sessionRoot,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"The final render hook was configured for another session.");
		(RoughCutRenderManifest manifest, RoughCutEvidenceReference evidence) =
			await renderer.RenderAsync(
				context.Snapshot,
				context.FinalPlanSha256,
				cancellationToken).ConfigureAwait(false);
		string manifestPath = paths.Resolve(evidence.RelativePath);
		List<FinalRenderComponent> components = new();
		foreach (RoughCutRenderChunk chunk in manifest.Chunks)
		{
			string path = paths.Resolve(chunk.OutputRelativePath);
			if (!File.Exists(path))
				throw new FileNotFoundException(
					"A completed final render chunk is missing.",
					path);
			string actual = new SessionArtifactHasher().ComputeSha256(path);
			if (!string.Equals(
				actual,
				chunk.Sha256,
				StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					"A completed final render chunk failed SHA-256 validation.");
			components.Add(new FinalRenderComponent
			{
				RelativePath = chunk.OutputRelativePath,
				Sha256 = actual,
				LengthBytes = new FileInfo(path).Length,
				TimelineStart = chunk.Start,
				Duration = chunk.Duration
			});
		}
		return new FinalRenderArtifact
		{
			ArtifactKind = "chunk-manifest",
			RelativePath = evidence.RelativePath,
			Sha256 = evidence.Sha256,
			LengthBytes = new FileInfo(manifestPath).Length,
			Duration = manifest.TimelineEnd - manifest.TimelineStart,
			RenderProfile = manifest.RenderProfileId,
			Components = components
		};
	}
}
