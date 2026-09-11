using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Sessions;
using Newtonsoft.Json.Linq;

namespace AutoEditing.LlmEditor.RoughCut;

internal sealed class RoughCutAuditArtifactStore
{
	private readonly SessionPathResolver paths;
	private readonly AtomicFileWriter writer = new();
	private readonly Func<DateTimeOffset> clock;

	public RoughCutAuditArtifactStore(
		string sessionRoot,
		Func<DateTimeOffset>? clock = null)
	{
		paths = new SessionPathResolver(sessionRoot);
		this.clock = clock ?? (() => DateTimeOffset.UtcNow);
	}

	public string SaveReport(RoughCutAuditReport report)
	{
		RoughCutAuditContractValidator.Validate(report);
		string relative = ReportRelativePath(report.ReportId);
		WriteImmutable(relative, ContractSerializer.Serialize(report));
		writer.WriteText(
			paths.Resolve("assembly/rough-cut/current-report.json"),
			ContractSerializer.Serialize(new RoughCutCurrentReport
			{
				SessionId = report.SessionId,
				ReportId = report.ReportId,
				ReportSha256 = ReportHash(report),
				PlanSha256 = report.PlanSha256,
				UpdatedUtc = clock()
			}));
		return relative;
	}

	public RoughCutAuditReport? ReadCurrentReport()
	{
		string pointerPath = paths.Resolve("assembly/rough-cut/current-report.json");
		if (!File.Exists(pointerPath)) return null;
		RoughCutCurrentReport pointer =
			ContractSerializer.Deserialize<RoughCutCurrentReport>(
				File.ReadAllText(pointerPath));
		if (pointer.SchemaVersion != RoughCutCurrentReport.CurrentSchemaVersion ||
			string.IsNullOrWhiteSpace(pointer.SessionId) ||
			string.IsNullOrWhiteSpace(pointer.ReportId))
			throw new InvalidDataException(
				"The current rough-cut report pointer is invalid.");
		RoughCutAuditReport report = ReadReport(pointer.ReportId);
		if (!string.Equals(
				ReportHash(report), pointer.ReportSha256,
				StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(
				report.PlanSha256, pointer.PlanSha256,
				StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(
				report.SessionId, pointer.SessionId, StringComparison.Ordinal))
			throw new InvalidDataException(
				"The current rough-cut report pointer failed its identity check.");
		return report;
	}

	public RoughCutAuditReport ReadReport(string reportId)
	{
		string path = paths.Resolve(ReportRelativePath(reportId));
		if (!File.Exists(path))
			throw new FileNotFoundException("The rough-cut audit report does not exist.", path);
		RoughCutAuditReport report =
			ContractSerializer.Deserialize<RoughCutAuditReport>(File.ReadAllText(path));
		RoughCutAuditContractValidator.Validate(report);
		return report;
	}

	public void SaveCorrectionDecision(RoughCutCorrectionDecision decision)
	{
		RoughCutAuditReport report = ReadReport(decision.ReportId);
		RoughCutAuditContractValidator.Validate(decision, report);
		string json = ContractSerializer.Serialize(decision);
		string fingerprint = ContractHash.Compute(JToken.Parse(json))[..16];
		string relative =
			$"{AuditRoot(report.ReportId)}/decisions/{SafeId(decision.CorrectionId)}/" +
			$"{decision.DecidedUtc.UtcTicks:D19}-{fingerprint}.json";
		WriteImmutable(relative, json);
	}

	public IReadOnlyList<RoughCutCorrectionDecision> ReadLatestCorrectionDecisions(
		string reportId)
	{
		RoughCutAuditReport report = ReadReport(reportId);
		string directory = paths.Resolve($"{AuditRoot(reportId)}/decisions");
		if (!Directory.Exists(directory)) return Array.Empty<RoughCutCorrectionDecision>();
		RoughCutCorrectionDecision[] decisions = Directory.EnumerateDirectories(directory)
			.SelectMany(item => Directory.EnumerateFiles(item, "*.json"))
			.Select(path => ContractSerializer.Deserialize<RoughCutCorrectionDecision>(
				File.ReadAllText(path)))
			.Select(decision =>
			{
				RoughCutAuditContractValidator.Validate(decision, report);
				return decision;
			})
			.ToArray();
		foreach (IGrouping<string, RoughCutCorrectionDecision> group in decisions
			.GroupBy(item => item.CorrectionId, StringComparer.Ordinal))
		{
			DateTimeOffset latest = group.Max(item => item.DecidedUtc);
			if (group.Where(item => item.DecidedUtc == latest)
				.Select(item => item.Disposition)
				.Distinct()
				.Count() > 1)
				throw new InvalidDataException(
					"Conflicting rough-cut correction decisions share the latest timestamp.");
		}
		return decisions
			.GroupBy(item => item.CorrectionId, StringComparer.Ordinal)
			.Select(group => group
				.OrderByDescending(item => item.DecidedUtc)
				.ThenByDescending(item => item.Disposition)
				.First())
			.OrderBy(item => item.CorrectionId, StringComparer.Ordinal)
			.ToArray();
	}

	public RoughCutCheckpointReopenRequest CreateApprovedReopenRequest(
		string reportId,
		string correctionId)
	{
		RoughCutAuditReport report = ReadReport(reportId);
		RoughCutCorrectionProposal correction = report.Corrections.SingleOrDefault(item =>
			string.Equals(item.CorrectionId, correctionId, StringComparison.Ordinal))
			?? throw new InvalidOperationException(
				"The requested rough-cut correction does not exist.");
		RoughCutCorrectionDecision approval = ReadLatestCorrectionDecisions(reportId)
			.SingleOrDefault(item => string.Equals(
				item.CorrectionId, correctionId, StringComparison.Ordinal))
			?? throw new InvalidOperationException(
				"The rough-cut correction has not been explicitly approved.");
		string reportHash = ReportHash(report);
		DateTimeOffset now = clock();
		string requestId = "reopen-" + SafeId(correctionId) + "-" +
			reportHash[..16].ToLowerInvariant();
		string relative =
			$"{AuditRoot(reportId)}/reopen-requests/{SafeId(requestId)}.json";
		string existingPath = paths.Resolve(relative);
		if (File.Exists(existingPath))
		{
			RoughCutCheckpointReopenRequest existing =
				ContractSerializer.Deserialize<RoughCutCheckpointReopenRequest>(
					File.ReadAllText(existingPath));
			RoughCutAuditContractValidator.Validate(existing, report, approval);
			return existing;
		}
		RoughCutCheckpointReopenRequest request = new()
		{
			RequestId = requestId,
			SessionId = report.SessionId,
			ReportId = report.ReportId,
			ReportSha256 = reportHash,
			CorrectionId = correction.CorrectionId,
			TargetCheckpoints = correction.TargetCheckpoints.ToList(),
			ScopedInstruction = correction.Instruction,
			CreatedUtc = now
		};
		RoughCutAuditContractValidator.Validate(request, report, approval);
		WriteImmutable(relative, ContractSerializer.Serialize(request));
		return request;
	}

	public AcceptedRoughCutMilestone AcceptMilestone(
		string reportId,
		string acceptedBy)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(acceptedBy);
		RoughCutAuditReport report = ReadReport(reportId);
		AcceptedRoughCutMilestone? existing = ReadAcceptedMilestone(reportId);
		if (existing != null)
		{
			if (!string.Equals(existing.AcceptedBy, acceptedBy, StringComparison.Ordinal))
				throw new InvalidOperationException(
					"The rough-cut milestone was already accepted by another actor.");
			return existing;
		}
		RoughCutEvidenceReference fullRender = report.Evidence.Single(item =>
			item.Kind == RoughCutEvidenceKind.FullRender);
		AcceptedRoughCutMilestone milestone = new()
		{
			SessionId = report.SessionId,
			ReportId = report.ReportId,
			ReportSha256 = ReportHash(report),
			PlanSha256 = report.PlanSha256,
			FullRenderSha256 = fullRender.Sha256,
			AcceptedUtc = clock(),
			AcceptedBy = acceptedBy,
			CorrectionDecisions = ReadLatestCorrectionDecisions(reportId).ToList()
		};
		RoughCutAuditContractValidator.Validate(milestone, report);
		WriteImmutable(
			$"{AuditRoot(reportId)}/accepted-milestone.json",
			ContractSerializer.Serialize(milestone));
		return milestone;
	}

	public bool IsFullRenderEvidenceValid(RoughCutAuditReport report)
	{
		RoughCutAuditContractValidator.Validate(report);
		try
		{
			RoughCutEvidenceReference evidence = report.Evidence.Single(item =>
				item.Kind == RoughCutEvidenceKind.FullRender);
			string manifestPath = paths.Resolve(evidence.RelativePath);
			if (!File.Exists(manifestPath) ||
				!string.Equals(
					new SessionArtifactHasher().ComputeSha256(manifestPath),
					evidence.Sha256,
					StringComparison.OrdinalIgnoreCase))
				return false;
			RoughCutRenderManifest persisted =
				ContractSerializer.Deserialize<RoughCutRenderManifest>(
					File.ReadAllText(manifestPath));
			RoughCutRenderArtifactStore renderStore =
				new(paths.Root);
			RoughCutRenderManifest? valid =
				renderStore.ReadValidManifest(persisted.RenderId);
			return valid != null &&
				string.Equals(
					valid.PlanSha256,
					report.PlanSha256,
					StringComparison.OrdinalIgnoreCase) &&
				string.Equals(
					renderStore.EvidenceForManifest(valid).Sha256,
					evidence.Sha256,
					StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception exception) when (
			exception is IOException or InvalidDataException or
				InvalidOperationException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	public AcceptedRoughCutMilestone? ReadAcceptedMilestone(string reportId)
	{
		RoughCutAuditReport report = ReadReport(reportId);
		string path = paths.Resolve($"{AuditRoot(reportId)}/accepted-milestone.json");
		if (!File.Exists(path)) return null;
		AcceptedRoughCutMilestone milestone =
			ContractSerializer.Deserialize<AcceptedRoughCutMilestone>(File.ReadAllText(path));
		RoughCutAuditContractValidator.Validate(milestone, report);
		if (!string.Equals(
			milestone.ReportSha256,
			ReportHash(report),
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The accepted rough-cut milestone report hash does not match.");
		RoughCutEvidenceReference render = report.Evidence.Single(item =>
			item.Kind == RoughCutEvidenceKind.FullRender);
		if (!string.Equals(
			milestone.FullRenderSha256,
			render.Sha256,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"The accepted rough-cut milestone render hash does not match.");
		return milestone;
	}

	private void WriteImmutable(string relativePath, string content)
	{
		string path = paths.Resolve(relativePath);
		if (File.Exists(path))
		{
			if (string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
				return;
			throw new InvalidOperationException(
				"An immutable rough-cut artifact already exists at " + relativePath + ".");
		}
		writer.WriteText(path, content);
	}

	internal static string ReportHash(RoughCutAuditReport report) =>
		ContractHash.Compute(JToken.Parse(ContractSerializer.Serialize(report)));

	private static string ReportRelativePath(string reportId) =>
		$"{AuditRoot(reportId)}/report.json";

	private static string AuditRoot(string reportId) =>
		"assembly/rough-cut/audits/" + SafeId(reportId);

	private static string SafeId(string value)
	{
		if (string.IsNullOrWhiteSpace(value) ||
			value.Any(character =>
				!char.IsLetterOrDigit(character) &&
				character is not ('-' or '_' or '.')))
			throw new InvalidOperationException(
				"Rough-cut artifact identifiers may contain only letters, numbers, dash, underscore, or dot.");
		return value;
	}
}

internal sealed class RoughCutCurrentReport
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public string ReportId { get; set; } = "";
	public string ReportSha256 { get; set; } = "";
	public string PlanSha256 { get; set; } = "";
	public DateTimeOffset UpdatedUtc { get; set; }
}
