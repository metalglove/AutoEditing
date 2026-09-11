using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Diff;
using AutoEditing.Iteration.Contracts.Evidence;
using AutoEditing.Iteration.Contracts.Iterations;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.Iteration.Contracts.Steering;
using AutoEditing.LlmEditor.Sessions;

namespace AutoEditing.LlmEditor.Workbench;

internal static class WorkbenchProjectionSelfTests
{
	public static void Run(string testRoot)
	{
		string root = Path.Combine(testRoot, "workbench");
		AtomicFileWriter writer = new();
		writer.WriteText(Path.Combine(root, "manifest.json"), ContractSerializer.Serialize(new EditSessionManifest
		{
			SessionId = "session-1",
			Revision = 4,
			State = EditSessionState.AwaitingUser,
			CurrentIteration = 2,
			CreatedUtc = DateTimeOffset.Parse("2026-07-26T10:00:00Z"),
			UpdatedUtc = DateTimeOffset.Parse("2026-07-26T10:05:00Z")
		}));
		EditIterationSnapshot snapshot = new()
		{
			SessionId = "session-1",
			Iteration = 2,
			CreatedUtc = DateTimeOffset.Parse("2026-07-26T10:04:00Z"),
			Candidate = new Core.Domain.Planning.EditPlanDocument(),
			CandidateHash = "candidate-two",
			Decisions = new[] { new EditDecisionRecord { DecisionId = "d1", Summary = "Keep cut.", Confidence = .8 } },
			Findings = new[] { new EditReviewFinding { FindingId = "f1", Severity = EditReviewSeverity.Warning } },
			Evidence = new[] { new EditEvidenceReference { EvidenceId = "e1", MediaType = "image/png" } }
		};
		writer.WriteText(Path.Combine(root, "iterations", "0002", "snapshot.json"),
			ContractSerializer.Serialize(snapshot));
		writer.WriteText(Path.Combine(root, "iterations", "0002", "timeline.json"),
			ContractSerializer.Serialize(new CandidateTimelineSnapshot
			{
				Workspace = new CandidateWorkspaceId { SessionId = "session-1", Iteration = 2, Nonce = "preview" }
			}));
		writer.WriteText(Path.Combine(root, "iterations", "0002", "diff.json"),
			ContractSerializer.Serialize(new EditPlanDiff { BeforeHash = "one", AfterHash = "candidate-two" }));
		writer.WriteText(Path.Combine(root, "iterations", "bad", "snapshot.json"), "{}");

		FileWorkbenchSteeringSink sink = new(() => Guid.Parse("11111111-1111-1111-1111-111111111111"));
		EditSteeringDirective submitted = sink.Submit(root, new(
			EditSteeringKind.Lock, " Keep opener ", new[] { "shot-1", "shot-1" }));
		if (submitted.ApplicableFromIteration != 3 || submitted.Instruction != "Keep opener" ||
			submitted.TargetIds.Count != 1)
			throw new InvalidOperationException("Workbench steering was not normalized.");

		WorkbenchSession result = new FileWorkbenchProjectionReader().Read(root);
		if (result.Iterations.Count != 1 || !result.Iterations[0].Status.IsCurrent ||
			!result.Iterations[0].Status.IsMaterialized ||
			!result.Iterations[0].Status.HasPreviewEvidence ||
			result.Iterations[0].Diff?.AfterHash != "candidate-two" ||
			result.Steering.Count != 1 ||
			!result.Diagnostics.Any(item => item.Artifact == "iterations/bad"))
			throw new InvalidOperationException("The workbench projection did not reproduce persisted state.");

		bool rejected = false;
		try
		{
			sink.Submit(root, new(EditSteeringKind.Prefer, "bad", TimelineStart: TimeSpan.FromSeconds(2)));
		}
		catch (ArgumentException) { rejected = true; }
		if (!rejected) throw new InvalidOperationException("An incomplete steering range was accepted.");
	}
}
