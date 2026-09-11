using System.Security.Cryptography;
using AutoEditing.Iteration.Contracts.Assembly;

namespace AutoEditing.LlmEditor.RoughCut;

internal sealed class RoughCutAuditService
{
	private readonly DeterministicRoughCutAnalyzer deterministic;
	private readonly IRoughCutMultimodalAuditor? multimodal;
	private readonly Func<DateTimeOffset> clock;

	public RoughCutAuditService(
		IRoughCutMultimodalAuditor? multimodal = null,
		DeterministicRoughCutAnalyzer? deterministic = null,
		Func<DateTimeOffset>? clock = null)
	{
		this.multimodal = multimodal;
		this.deterministic = deterministic ?? new DeterministicRoughCutAnalyzer();
		this.clock = clock ?? (() => DateTimeOffset.UtcNow);
	}

	public async Task<RoughCutAuditReport> RunAsync(
		string sessionRoot,
		RoughCutAuditInput input,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionRoot);
		VerifyEvidence(sessionRoot, input.Evidence);
		RoughCutAuditReport report = deterministic.Analyze(input, clock());
		VerifyFullRenderManifest(sessionRoot, input, report);
		if (multimodal == null)
		{
			report.ModelStatus = "skipped";
			report.Diagnostics.Add(
				"Multimodal audit was not configured; deterministic metrics remain available.");
			RoughCutAuditContractValidator.Validate(report);
			return report;
		}

		try
		{
			LlmRoughCutAuditContribution model = await multimodal.AuditAsync(
				sessionRoot, input, report, cancellationToken);
			report.ModelId = model.ModelId;
			report.ModelStatus = "completed";
			report.Summary = report.Summary + " Multimodal review: " + model.Summary;
			foreach (RoughCutAuditFinding finding in model.Findings)
				report.Findings.Add(finding);
			foreach (RoughCutCorrectionProposal correction in model.Corrections)
				report.Corrections.Add(correction);
			RoughCutAuditContractValidator.Validate(report);
			return report;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			report.ModelStatus = "failed-deterministic-fallback";
			report.Diagnostics.Add(
				"Multimodal audit failed; no model-authored finding was adopted. " +
				exception.GetType().Name + ": " + exception.Message);
			RoughCutAuditContractValidator.Validate(report);
			return report;
		}
	}

	private static void VerifyEvidence(
		string sessionRoot,
		IReadOnlyList<RoughCutEvidenceReference> evidence)
	{
		string root = Path.GetFullPath(sessionRoot);
		string prefix = root.EndsWith(Path.DirectorySeparatorChar)
			? root
			: root + Path.DirectorySeparatorChar;
		foreach (RoughCutEvidenceReference item in evidence ??
			throw new InvalidOperationException("Rough-cut evidence is required."))
		{
			string path = Path.GetFullPath(Path.Combine(root, item.RelativePath));
			if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"A rough-cut evidence path escaped the session directory.");
			if (!File.Exists(path))
				throw new FileNotFoundException("Rough-cut evidence is missing.", path);
			using FileStream stream = File.OpenRead(path);
			string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
			if (!string.Equals(actual, item.Sha256, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"Rough-cut evidence failed its SHA-256 check: " + item.EvidenceId);
		}
	}

	private static void VerifyFullRenderManifest(
		string sessionRoot,
		RoughCutAuditInput input,
		RoughCutAuditReport report)
	{
		RoughCutEvidenceReference evidence = report.Evidence.Single(item =>
			item.Kind == RoughCutEvidenceKind.FullRender);
		if (!string.Equals(
			evidence.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Complete rough-cut evidence must be a render manifest.");
		string root = Path.GetFullPath(sessionRoot);
		RoughCutRenderManifest manifest =
			AutoEditing.Iteration.Contracts.Serialization.ContractSerializer
				.Deserialize<RoughCutRenderManifest>(
					File.ReadAllText(Path.Combine(root, evidence.RelativePath)));
		RoughCutRenderContractValidator.Validate(manifest);
		if (!string.Equals(manifest.SessionId, input.SessionId, StringComparison.Ordinal) ||
			!string.Equals(
				manifest.PlanSha256, report.PlanSha256, StringComparison.OrdinalIgnoreCase) ||
			manifest.TimelineStart != input.Timeline.TimelineStart ||
			manifest.TimelineEnd != input.Timeline.TimelineEnd ||
			!string.Equals(
				manifest.Workspace.ToString(),
				input.Timeline.Workspace.ToString(),
				StringComparison.Ordinal))
			throw new InvalidOperationException(
				"The complete render manifest does not match the audited session, plan, workspace, or timeline.");
		foreach (RoughCutRenderChunk chunk in manifest.Chunks)
		{
			string path = Path.GetFullPath(Path.Combine(root, chunk.OutputRelativePath));
			string prefix = root.EndsWith(Path.DirectorySeparatorChar)
				? root
				: root + Path.DirectorySeparatorChar;
			if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
				!File.Exists(path))
				throw new InvalidOperationException(
					"A complete rough-cut render chunk is missing or outside the session.");
			using FileStream stream = File.OpenRead(path);
			string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
			if (!string.Equals(hash, chunk.Sha256, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"A complete rough-cut render chunk failed its SHA-256 check.");
		}
	}
}
