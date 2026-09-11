using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Assembly;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor;

internal static class CheckpointPreviewPipelineSelfTests
{
	public static void Run(
		string testRoot,
		EditPlanningRequest request,
		EditPlanDocument candidate)
	{
		string sessionRoot = Path.Combine(testRoot, "checkpoint-preview");
		Directory.CreateDirectory(sessionRoot);
		string sessionId = "preview-session";
		string clipPath = candidate.Montage.Placements.Single().Clip.FilePath;
		AssemblyClipReference clip = new()
		{
			ReferenceId = AssemblyReferenceIds.ForClipPath(clipPath),
			MediaPath = clipPath
		};
		AssemblySketch sketch = new()
		{
			RequestId = request.RequestId,
			EditorialThesis = "Keep the shot readable and make the impact musical.",
			Sections = new List<AssemblySectionIntent>
			{
				new()
				{
					SectionId = "section-1",
					EditorialRole = "opening",
					EnergyDirection = "build",
					PacingIntent = "readable",
					Rationale = "Establish the first action."
				}
			},
			ClipOrder = new List<AssemblyClipIntent>
			{
				new()
				{
					Order = 1,
					Clip = clip,
					SectionId = "section-1",
					EditorialRole = "opening",
					Rationale = "Clear source action.",
					Confidence = .8
				}
			},
			SyncStrategy = new AssemblySyncStrategy
			{
				Density = "sparse",
				Rationale = "Use the strongest musical event."
			}
		};
		ClipStepDecision decision = new()
		{
			RequestId = request.RequestId,
			StepIndex = 1,
			Clip = clip,
			SourceWindow = new AssemblySourceWindow
			{
				StartSeconds = candidate.Montage.Placements[0].SourceOffsetSeconds,
				EndSeconds = candidate.Montage.Placements[0].SourceOffsetSeconds +
					candidate.Montage.Placements[0].LengthSeconds
			},
			PrimarySync = new AssemblySyncDecision
			{
				MusicEventId = "music-1",
				KillIndex = 0
			},
			Rationale = "Align the confirmed action with the accent.",
			Confidence = .8
		};
		CandidateWorkspaceId workspace = new()
		{
			SessionId = sessionId,
			Iteration = 1,
			Nonce = "assembly"
		};
		CandidateTimelineSnapshot timeline = new()
		{
			Workspace = workspace,
			TimelineStart = TimeSpan.Zero,
			TimelineEnd = TimeSpan.FromSeconds(
				Math.Max(6, candidate.Montage.Placements[0].TimelineEndSeconds + 2)),
			Tracks = new[]
			{
				new CandidateTrackSnapshot
				{
					Name = workspace.OwnershipPrefix + "|video",
					MediaKind = "video"
				}
			}
		};
		TestWindowBounds(candidate, timeline);
		TestCompletedAttempt(
			sessionRoot, sessionId, sketch, decision, candidate, timeline);
		TestRenderFailure(
			sessionRoot, sessionId, sketch, decision, candidate, timeline);
		TestReviewFailure(
			sessionRoot, sessionId, sketch, decision, candidate, timeline);
		TestTimeout(
			sessionRoot, sessionId, sketch, decision, candidate, timeline);
		TestAutomationTimeout(
			sessionRoot, sessionId, sketch, decision, candidate, timeline);
		TestReviewTimeout(
			sessionRoot, sessionId, sketch, decision, candidate, timeline);
		TestCancellation(
			sessionRoot, sessionId, sketch, decision, candidate, timeline);
	}

	private static void TestWindowBounds(
		EditPlanDocument candidate,
		CandidateTimelineSnapshot timeline)
	{
		CheckpointPreviewWindow window = CheckpointPreviewPipeline.CreateWindow(
			candidate.Montage.Placements.Single(), timeline);
		Assert(window.PlacementStart ==
			TimeSpan.FromSeconds(candidate.Montage.Placements[0].TimelineStartSeconds),
			"Checkpoint preview moved the placement start.");
		Assert(window.PlacementEnd ==
			TimeSpan.FromSeconds(candidate.Montage.Placements[0].TimelineEndSeconds),
			"Checkpoint preview truncated the active placement.");
		Assert(window.ContextBefore <= TimeSpan.FromSeconds(2) &&
			window.ContextAfter <= TimeSpan.FromSeconds(2) &&
			window.End - window.Start <= TimeSpan.FromSeconds(20),
			"Checkpoint preview context is not bounded.");
	}

	private static void TestCompletedAttempt(
		string sessionRoot,
		string sessionId,
		AssemblySketch sketch,
		ClipStepDecision decision,
		EditPlanDocument candidate,
		CandidateTimelineSnapshot timeline)
	{
		CheckpointPreviewArtifact result = new CheckpointPreviewPipeline(
				sessionId,
				sessionRoot,
				new FakeRenderer(sessionRoot),
				new FakeReviewer())
			.RenderAndReviewAsync(
				1, sketch, decision, candidate, timeline, null,
				CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(result.Status == CheckpointPreviewStatus.Completed &&
			result.Attempt == 1,
			"A successful checkpoint preview was not completed as attempt one.");
		foreach (string path in new[]
		{
			result.TimelineSnapshotRelativePath,
			result.TimingSidecarRelativePath,
			result.TimingVisualizationRelativePath,
			result.ContactSheetManifestRelativePath,
			result.ReviewReportRelativePath
		})
			Assert(File.Exists(Path.Combine(
				sessionRoot,
				path.Replace('/', Path.DirectorySeparatorChar))),
				"Checkpoint preview evidence was not persisted: " + path);
		CheckpointContactSheetManifest contact =
			ContractSerializer.Deserialize<CheckpointContactSheetManifest>(
				File.ReadAllText(Path.Combine(
					sessionRoot,
					result.ContactSheetManifestRelativePath.Replace(
						'/', Path.DirectorySeparatorChar))));
		Assert(contact.SampleOffsets.Count == 5 &&
			contact.Status == "captured" &&
			contact.FrameRelativePaths.Count == 5,
			"The real VEGAS contact-sheet frame set is not explicit or deterministic.");
	}

	private static void TestRenderFailure(
		string sessionRoot,
		string sessionId,
		AssemblySketch sketch,
		ClipStepDecision decision,
		EditPlanDocument candidate,
		CandidateTimelineSnapshot timeline)
	{
		CheckpointPreviewArtifact result = new CheckpointPreviewPipeline(
				sessionId,
				sessionRoot,
				new ThrowingRenderer(),
				new FakeReviewer())
			.RenderAndReviewAsync(
				1, sketch, decision, candidate, timeline, null,
				CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(result.Attempt == 2 &&
			result.Status == CheckpointPreviewStatus.RenderFailed &&
			result.FailureCode == "RENDER_FAILED",
			"A render failure was not persisted as a retryable preview attempt.");
	}

	private static void TestReviewFailure(
		string sessionRoot,
		string sessionId,
		AssemblySketch sketch,
		ClipStepDecision decision,
		EditPlanDocument candidate,
		CandidateTimelineSnapshot timeline)
	{
		CheckpointPreviewArtifact result = new CheckpointPreviewPipeline(
				sessionId,
				sessionRoot,
				new FakeRenderer(sessionRoot),
				new ThrowingReviewer())
			.RenderAndReviewAsync(
				1, sketch, decision, candidate, timeline, null,
				CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(result.Attempt == 3 &&
			result.Status == CheckpointPreviewStatus.ReviewFailed &&
			result.FailureCode == "REVIEW_FAILED" &&
			result.PreviewSha256.Length == 64,
			"A reviewer failure discarded a valid rendered preview.");
	}

	private static void TestTimeout(
		string sessionRoot,
		string sessionId,
		AssemblySketch sketch,
		ClipStepDecision decision,
		EditPlanDocument candidate,
		CandidateTimelineSnapshot timeline)
	{
		CheckpointPreviewArtifact result = new CheckpointPreviewPipeline(
				sessionId,
				sessionRoot,
				new BlockingRenderer(),
				new FakeReviewer(),
				TimeSpan.FromMilliseconds(20))
			.RenderAndReviewAsync(
				1, sketch, decision, candidate, timeline, null,
				CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(result.Attempt == 4 &&
			result.Status == CheckpointPreviewStatus.RenderFailed &&
			result.FailureCode == "RENDER_TIMEOUT",
			"A preview timeout was not distinguished from a render failure.");
		CheckpointPreviewArtifact? current =
			new CheckpointPreviewArtifactStore(sessionRoot).ReadCurrent(1);
		Assert(current?.Attempt == 4,
			"The current checkpoint preview pointer did not advance atomically.");
	}

	private static void TestAutomationTimeout(
		string sessionRoot,
		string sessionId,
		AssemblySketch sketch,
		ClipStepDecision decision,
		EditPlanDocument candidate,
		CandidateTimelineSnapshot timeline)
	{
		CheckpointPreviewArtifact result = new CheckpointPreviewPipeline(
				sessionId,
				sessionRoot,
				new TimeoutRenderer(),
				new FakeReviewer())
			.RenderAndReviewAsync(
				1, sketch, decision, candidate, timeline, null,
				CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(result.Attempt == 5 &&
			result.Status == CheckpointPreviewStatus.RenderFailed &&
			result.FailureCode == "RENDER_TIMEOUT",
			"An automation TimeoutException was not classified as render timeout.");
	}

	private static void TestReviewTimeout(
		string sessionRoot,
		string sessionId,
		AssemblySketch sketch,
		ClipStepDecision decision,
		EditPlanDocument candidate,
		CandidateTimelineSnapshot timeline)
	{
		CheckpointPreviewArtifact result = new CheckpointPreviewPipeline(
				sessionId,
				sessionRoot,
				new FakeRenderer(sessionRoot),
				new TimeoutReviewer())
			.RenderAndReviewAsync(
				1, sketch, decision, candidate, timeline, null,
				CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(result.Attempt == 6 &&
			result.Status == CheckpointPreviewStatus.ReviewFailed &&
			result.FailureCode == "REVIEW_TIMEOUT" &&
			result.PreviewSha256.Length == 64,
			"A reviewer TimeoutException discarded captured preview evidence or " +
			"was not classified as review timeout.");
	}

	private static void TestCancellation(
		string sessionRoot,
		string sessionId,
		AssemblySketch sketch,
		ClipStepDecision decision,
		EditPlanDocument candidate,
		CandidateTimelineSnapshot timeline)
	{
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		try
		{
			new CheckpointPreviewPipeline(
					sessionId,
					sessionRoot,
					new BlockingRenderer(),
					new FakeReviewer())
				.RenderAndReviewAsync(
					1, sketch, decision, candidate, timeline, null,
					cancellation.Token)
				.GetAwaiter().GetResult();
			throw new InvalidOperationException(
				"A cancelled render did not propagate cancellation to its caller.");
		}
		catch (OperationCanceledException)
		{
			// Cancellation is observable to the coordinator while its evidence is
			// still durable for recovery and retry.
		}
		CheckpointPreviewArtifact? current =
			new CheckpointPreviewArtifactStore(sessionRoot).ReadCurrent(1);
		Assert(current?.Attempt == 7 &&
			current.Status == CheckpointPreviewStatus.Cancelled &&
			current.FailureCode == "PREVIEW_CANCELLED",
			"The durable preview pointer did not retain the distinct cancelled attempt.");
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private sealed class FakeRenderer : ICheckpointPreviewRenderer
	{
		private readonly string sessionRoot;

		public FakeRenderer(string sessionRoot) => this.sessionRoot = sessionRoot;

		public Task<RenderCandidatePreviewResult> RenderAsync(
			CandidateWorkspaceId workspace,
			CheckpointPreviewWindow window,
			int checkpoint,
			int attempt,
			CancellationToken cancellationToken)
		{
			string relative =
				CheckpointPreviewArtifactStore.PreviewRelativePath(checkpoint, attempt);
			string full = Path.Combine(
				sessionRoot,
				relative.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(full)!);
			byte[] bytes = { 0, 0, 0, 24, (byte)attempt };
			File.WriteAllBytes(full, bytes);
			return Task.FromResult(new RenderCandidatePreviewResult
			{
				OutputRelativePath = relative,
				Sha256 = Convert.ToHexString(
					System.Security.Cryptography.SHA256.HashData(bytes))
					.ToLowerInvariant(),
				RenderedDuration = window.End - window.Start,
				RenderProfileId = "test-profile"
			});
		}

		public Task<CaptureCandidatePreviewFramesResult> CaptureFramesAsync(
			CandidateWorkspaceId workspace,
			IReadOnlyList<TimeSpan> timelineTimes,
			int checkpoint,
			int attempt,
			CancellationToken cancellationToken)
		{
			List<CapturedCandidatePreviewFrame> frames = new();
			for (int index = 0; index < timelineTimes.Count; index++)
			{
				string relative =
					$"assembly/checkpoints/{checkpoint:D4}/previews/{attempt:D4}/" +
					$"frames/frame-{index + 1:D4}.png";
				string full = Path.Combine(
					sessionRoot,
					relative.Replace('/', Path.DirectorySeparatorChar));
				Directory.CreateDirectory(Path.GetDirectoryName(full)!);
				byte[] bytes = { 137, 80, 78, 71, (byte)index };
				File.WriteAllBytes(full, bytes);
				frames.Add(new CapturedCandidatePreviewFrame
				{
					TimelineTime = timelineTimes[index],
					OutputRelativePath = relative,
					Sha256 = Convert.ToHexString(
						System.Security.Cryptography.SHA256.HashData(bytes))
						.ToLowerInvariant()
				});
			}
			return Task.FromResult(new CaptureCandidatePreviewFramesResult
			{
				Frames = frames
			});
		}
	}

	private sealed class ThrowingRenderer : ICheckpointPreviewRenderer
	{
		public Task<RenderCandidatePreviewResult> RenderAsync(
			CandidateWorkspaceId workspace,
			CheckpointPreviewWindow window,
			int checkpoint,
			int attempt,
			CancellationToken cancellationToken) =>
			throw new InvalidOperationException("Renderer unavailable.");

		public Task<CaptureCandidatePreviewFramesResult> CaptureFramesAsync(
			CandidateWorkspaceId workspace,
			IReadOnlyList<TimeSpan> timelineTimes,
			int checkpoint,
			int attempt,
			CancellationToken cancellationToken) =>
			throw new InvalidOperationException("Renderer unavailable.");
	}

	private sealed class BlockingRenderer : ICheckpointPreviewRenderer
	{
		public async Task<RenderCandidatePreviewResult> RenderAsync(
			CandidateWorkspaceId workspace,
			CheckpointPreviewWindow window,
			int checkpoint,
			int attempt,
			CancellationToken cancellationToken)
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			throw new InvalidOperationException("Unreachable.");
		}

		public Task<CaptureCandidatePreviewFramesResult> CaptureFramesAsync(
			CandidateWorkspaceId workspace,
			IReadOnlyList<TimeSpan> timelineTimes,
			int checkpoint,
			int attempt,
			CancellationToken cancellationToken) =>
			throw new InvalidOperationException("Unreachable.");
	}

	private sealed class TimeoutRenderer : ICheckpointPreviewRenderer
	{
		public Task<RenderCandidatePreviewResult> RenderAsync(
			CandidateWorkspaceId workspace,
			CheckpointPreviewWindow window,
			int checkpoint,
			int attempt,
			CancellationToken cancellationToken) =>
			throw new TimeoutException("Automation deadline elapsed.");

		public Task<CaptureCandidatePreviewFramesResult> CaptureFramesAsync(
			CandidateWorkspaceId workspace,
			IReadOnlyList<TimeSpan> timelineTimes,
			int checkpoint,
			int attempt,
			CancellationToken cancellationToken) =>
			throw new TimeoutException("Automation deadline elapsed.");
	}

	private sealed class FakeReviewer : ICheckpointMultimodalReviewer
	{
		public Task<CheckpointReviewReport> ReviewAsync(
			string sessionId,
			int checkpoint,
			int attempt,
			AssemblySketch sketch,
			ClipStepDecision decision,
			EditPlanDocument candidate,
			CandidateTimelineSnapshot timeline,
			CheckpointTimingSidecar timing,
			string timingVisualizationPath,
			RenderCandidatePreviewResult preview,
			IReadOnlyList<CheckpointPreviewFrameEvidence> frames,
			CancellationToken cancellationToken) =>
			Task.FromResult(new CheckpointReviewReport
			{
				SessionId = sessionId,
				Checkpoint = checkpoint,
				Attempt = attempt,
				CreatedUtc = DateTimeOffset.UtcNow,
				Summary = "The proposed event and music timing are coherent.",
				Confidence = .8,
				Observations = new List<CheckpointReviewObservation>
				{
					new()
					{
						ObservationId = "timing-1",
						Category = "synchronization",
						Severity = "info",
						Message = "The assigned music and kill points are close.",
						Confidence = .8,
						EvidenceIds = new List<string> { "timing-visualization" }
					}
				}
			});
	}

	private sealed class ThrowingReviewer : ICheckpointMultimodalReviewer
	{
		public Task<CheckpointReviewReport> ReviewAsync(
			string sessionId,
			int checkpoint,
			int attempt,
			AssemblySketch sketch,
			ClipStepDecision decision,
			EditPlanDocument candidate,
			CandidateTimelineSnapshot timeline,
			CheckpointTimingSidecar timing,
			string timingVisualizationPath,
			RenderCandidatePreviewResult preview,
			IReadOnlyList<CheckpointPreviewFrameEvidence> frames,
			CancellationToken cancellationToken) =>
			throw new InvalidOperationException("Reviewer unavailable.");
	}

	private sealed class TimeoutReviewer : ICheckpointMultimodalReviewer
	{
		public Task<CheckpointReviewReport> ReviewAsync(
			string sessionId,
			int checkpoint,
			int attempt,
			AssemblySketch sketch,
			ClipStepDecision decision,
			EditPlanDocument candidate,
			CandidateTimelineSnapshot timeline,
			CheckpointTimingSidecar timing,
			string timingVisualizationPath,
			RenderCandidatePreviewResult preview,
			IReadOnlyList<CheckpointPreviewFrameEvidence> frames,
			CancellationToken cancellationToken) =>
			throw new TimeoutException("Reviewer request deadline elapsed.");
	}
}
