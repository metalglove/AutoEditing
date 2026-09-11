using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Iterations;
using AutoEditing.LlmEditor.Inference;
using AutoEditing.LlmEditor.Iteration;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor;

internal static class CandidateReviewerSelfTests
{
	public static void Run(EditPlanningRequest request, EditPlanDocument plan)
	{
		const string evidenceId = "preview-frame@00:01.000";
		const string response = """
			{
			  "accepted": false,
			  "summary": "The opening cut lands after the visible impact.",
			  "findings": [{
			    "findingId": "finding-001",
			    "code": "SYNC_LATE",
			    "severity": "error",
			    "message": "Move the cut earlier.",
			    "evidenceIds": ["preview-frame@00:01.000"]
			  }],
			  "decisions": [{
			    "decisionId": "decision-001",
			    "category": "sync",
			    "summary": "Revise impact timing.",
			    "confidence": 0.9,
			    "evidenceIds": ["preview-frame@00:01.000"]
			  }],
			  "steeringInstructions": ["Move the opening impact cut 120 ms earlier."]
			}
			""";
		StubClient client = new(response);
		VisualEvidence evidence = new()
		{
			DataUrl = "data:image/png;base64,AA==",
			Description = evidenceId
		};
		EditIterationFeedback feedback = new LlmCandidateReviewer(client)
			.ReviewAsync(
				plan,
				Timeline(),
				new[] { evidence },
				1,
				CancellationToken.None)
			.GetAwaiter().GetResult();

		Assert(!feedback.IsAccepted &&
			feedback.Findings.Single().Severity == EditReviewSeverity.Error &&
			feedback.Decisions.Single().Confidence == 0.9 &&
			feedback.SteeringInstructions.Single().Contains("120 ms", StringComparison.Ordinal),
			"The structured candidate review was not mapped correctly.");
		Assert(client.Requests.Single().JsonSchemaName == "candidate_edit_review" &&
			client.Requests.Single().JsonSchema?.Contains("\"additionalProperties\": false",
				StringComparison.Ordinal) == true,
			"The candidate reviewer did not request strict structured output.");
		Assert(client.Requests.Single().VisualEvidence.Single() == evidence &&
			client.Requests.Single().UserPrompt.Contains(evidenceId, StringComparison.Ordinal),
			"The preview evidence was not supplied to the multimodal review request.");
		Assert(client.Requests.Single().UserPrompt.Contains(
				plan.Montage.Placements[0].Clip.FilePath,
				StringComparison.Ordinal),
			"The review prompt did not contain the edit plan.");
		Assert(client.Requests.Single().UserPrompt.Contains(
				"AE|LLM|session-a|0001|nonce-a",
				StringComparison.Ordinal),
			"The review prompt did not contain the materialized timeline snapshot.");

		ExpectFailure(
			() => new LlmCandidateReviewer(new StubClient(
					response.Replace(evidenceId, "invented-evidence", StringComparison.Ordinal)))
				.ReviewAsync(plan, Timeline(), new[] { evidence }, 1, CancellationToken.None)
				.GetAwaiter().GetResult(),
			"Review output was allowed to invent evidence.");
		ExpectFailure(
			() => new LlmCandidateReviewer(new StubClient("```json\n" + response + "\n```"))
				.ReviewAsync(plan, Timeline(), new[] { evidence }, 1, CancellationToken.None)
				.GetAwaiter().GetResult(),
			"Review output with non-JSON framing was accepted.");
		ExpectFailure(
			() => new LlmCandidateReviewer(new StubClient(
					response.Replace(
						"\"accepted\": false",
						"\"accepted\": true",
						StringComparison.Ordinal)))
				.ReviewAsync(plan, Timeline(), new[] { evidence }, 1, CancellationToken.None)
				.GetAwaiter().GetResult(),
			"An accepted review with an error finding was accepted.");
	}

	private static CandidateTimelineSnapshot Timeline() => new()
	{
		Workspace = new CandidateWorkspaceId
		{
			SessionId = "session-a",
			Iteration = 1,
			Nonce = "nonce-a"
		},
		TimelineStart = TimeSpan.Zero,
		TimelineEnd = TimeSpan.FromSeconds(4),
		Tracks = new[]
		{
			new CandidateTrackSnapshot
			{
				Index = 0,
				Name = "Candidate Video",
				MediaKind = "video"
			}
		}
	};

	private static void ExpectFailure(Action action, string message)
	{
		try { action(); }
		catch { return; }
		throw new InvalidOperationException(message);
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private sealed class StubClient : ITextGenerationClient
	{
		private readonly string response;

		public StubClient(string response) => this.response = response;

		public List<TextGenerationRequest> Requests { get; } = new();

		public Task<TextGenerationResult> GenerateAsync(
			TextGenerationRequest request,
			CancellationToken cancellationToken)
		{
			Requests.Add(request);
			return Task.FromResult(new TextGenerationResult
			{
				Text = response,
				Model = "test-reviewer"
			});
		}
	}
}
