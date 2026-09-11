using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Diff;
using AutoEditing.Iteration.Contracts.Evidence;
using AutoEditing.Iteration.Contracts.Iterations;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.Iteration.Contracts.Steering;

namespace AutoEditing.LlmEditor.Workbench;

public sealed record WorkbenchSession(
	string SessionId,
	EditSessionState State,
	long Revision,
	int CurrentIteration,
	string AcceptedPlanHash,
	IReadOnlyList<WorkbenchIteration> Iterations,
	IReadOnlyList<EditSteeringDirective> Steering,
	IReadOnlyList<WorkbenchDiagnostic> Diagnostics);

public sealed record WorkbenchIteration(
	int Number,
	DateTimeOffset CreatedUtc,
	string CandidateHash,
	int PlacementCount,
	TimeSpan Duration,
	IReadOnlyList<EditDecisionRecord> Decisions,
	IReadOnlyList<EditReviewFinding> Findings,
	IReadOnlyList<EditEvidenceReference> Evidence,
	CandidateTimelineSnapshot? Timeline,
	EditPlanDiff? Diff,
	WorkbenchIterationStatus Status);

public sealed record WorkbenchIterationStatus(
	bool HasCandidate,
	bool IsMaterialized,
	bool HasPreviewEvidence,
	bool HasBlockingFindings,
	bool IsCurrent,
	bool IsAccepted);

public sealed record WorkbenchDiagnostic(string Artifact, string Message);

public interface IWorkbenchProjectionReader
{
	WorkbenchSession Read(string sessionRoot);
}

public interface IWorkbenchSteeringSink
{
	EditSteeringDirective Submit(string sessionRoot, WorkbenchSteeringSubmission submission);
}

public sealed record WorkbenchSteeringSubmission(
	EditSteeringKind Kind,
	string Instruction,
	IReadOnlyList<string>? TargetIds = null,
	TimeSpan? TimelineStart = null,
	TimeSpan? TimelineEnd = null,
	int? ApplicableFromIteration = null);
