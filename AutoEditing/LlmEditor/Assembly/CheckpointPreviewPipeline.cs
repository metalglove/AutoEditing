using System.IO.Compression;
using System.Text;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.LlmEditor.Automation;
using Core.Domain.Editing;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Assembly;

internal interface ICheckpointPreviewRenderer
{
	Task<RenderCandidatePreviewResult> RenderAsync(
		CandidateWorkspaceId workspace,
		CheckpointPreviewWindow window,
		int checkpoint,
		int attempt,
		CancellationToken cancellationToken);

	Task<CaptureCandidatePreviewFramesResult> CaptureFramesAsync(
		CandidateWorkspaceId workspace,
		IReadOnlyList<TimeSpan> timelineTimes,
		int checkpoint,
		int attempt,
		CancellationToken cancellationToken);
}

internal sealed record CheckpointPreviewFrameEvidence(
	TimeSpan TimelineTime,
	string RelativePath,
	string FullPath,
	string Sha256);

internal interface ICheckpointMultimodalReviewer
{
	Task<CheckpointReviewReport> ReviewAsync(
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
		CancellationToken cancellationToken);
}

internal interface ICheckpointPreviewPipeline
{
	Task<CheckpointPreviewArtifact> RenderAndReviewAsync(
		int checkpoint,
		AssemblySketch sketch,
		ClipStepDecision decision,
		EditPlanDocument candidate,
		CandidateTimelineSnapshot timeline,
		TimelineAdjustmentDelta? adjustment,
		CancellationToken cancellationToken);
}

internal sealed class VegasCheckpointPreviewRenderer : ICheckpointPreviewRenderer
{
	private readonly IVegasAutomationClient automation;
	private readonly string renderProfileId;

	public VegasCheckpointPreviewRenderer(
		IVegasAutomationClient automation,
		string renderProfileId = "review-1080p")
	{
		this.automation = automation ?? throw new ArgumentNullException(nameof(automation));
		this.renderProfileId = string.IsNullOrWhiteSpace(renderProfileId)
			? throw new ArgumentException("A preview render profile is required.", nameof(renderProfileId))
			: renderProfileId;
	}

	public Task<RenderCandidatePreviewResult> RenderAsync(
		CandidateWorkspaceId workspace,
		CheckpointPreviewWindow window,
		int checkpoint,
		int attempt,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(workspace);
		CheckpointPreviewContractValidator.Validate(window);
		string relativePath =
			CheckpointPreviewArtifactStore.PreviewRelativePath(checkpoint, attempt);
		return automation.ExecuteAsync<RenderCandidatePreviewRequest, RenderCandidatePreviewResult>(
			VegasOperations.RenderCandidatePreview,
			new RenderCandidatePreviewRequest
			{
				Workspace = workspace,
				Start = window.Start,
				Duration = window.End - window.Start,
				RenderProfileId = renderProfileId,
				OutputRelativePath = relativePath
			},
			$"checkpoint-{checkpoint:D4}-preview-{attempt:D4}",
			cancellationToken: cancellationToken);
	}

	public Task<CaptureCandidatePreviewFramesResult> CaptureFramesAsync(
		CandidateWorkspaceId workspace,
		IReadOnlyList<TimeSpan> timelineTimes,
		int checkpoint,
		int attempt,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(workspace);
		ArgumentNullException.ThrowIfNull(timelineTimes);
		return automation.ExecuteAsync<
			CaptureCandidatePreviewFramesRequest,
			CaptureCandidatePreviewFramesResult>(
			VegasOperations.CaptureCandidatePreviewFrames,
			new CaptureCandidatePreviewFramesRequest
			{
				Workspace = workspace,
				TimelineTimes = timelineTimes.ToList(),
				OutputDirectoryRelativePath =
					$"assembly/checkpoints/{checkpoint:D4}/previews/{attempt:D4}/frames"
			},
			$"checkpoint-{checkpoint:D4}-frames-{attempt:D4}",
			cancellationToken: cancellationToken);
	}
}

internal sealed class CheckpointPreviewPipeline : ICheckpointPreviewPipeline
{
	private static readonly TimeSpan PreferredContext = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(20);

	private readonly string sessionId;
	private readonly CheckpointPreviewArtifactStore artifacts;
	private readonly ICheckpointPreviewRenderer renderer;
	private readonly ICheckpointMultimodalReviewer reviewer;
	private readonly TimeSpan timeout;
	private readonly Func<DateTimeOffset> clock;

	public CheckpointPreviewPipeline(
		string sessionId,
		string sessionRoot,
		ICheckpointPreviewRenderer renderer,
		ICheckpointMultimodalReviewer reviewer,
		TimeSpan? timeout = null,
		Func<DateTimeOffset>? clock = null)
	{
		this.sessionId = string.IsNullOrWhiteSpace(sessionId)
			? throw new ArgumentException("A session ID is required.", nameof(sessionId))
			: sessionId;
		artifacts = new CheckpointPreviewArtifactStore(sessionRoot);
		this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
		this.reviewer = reviewer ?? throw new ArgumentNullException(nameof(reviewer));
		this.timeout = timeout ?? TimeSpan.FromMinutes(15);
		if (this.timeout <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(timeout));
		this.clock = clock ?? (() => DateTimeOffset.UtcNow);
	}

	public async Task<CheckpointPreviewArtifact> RenderAndReviewAsync(
		int checkpoint,
		AssemblySketch sketch,
		ClipStepDecision decision,
		EditPlanDocument candidate,
		CandidateTimelineSnapshot timeline,
		TimelineAdjustmentDelta? adjustment,
		CancellationToken cancellationToken)
	{
		if (checkpoint < 1) throw new ArgumentOutOfRangeException(nameof(checkpoint));
		ArgumentNullException.ThrowIfNull(sketch);
		ArgumentNullException.ThrowIfNull(decision);
		ArgumentNullException.ThrowIfNull(candidate);
		ArgumentNullException.ThrowIfNull(timeline);
		ProgressiveAssemblyContractValidator.Validate(sketch);
		ProgressiveAssemblyContractValidator.Validate(decision);
		timeline.Workspace?.Validate();
		if (timeline.Workspace == null)
			throw new InvalidOperationException("A checkpoint preview requires a VEGAS workspace.");
		if (decision.StepIndex != checkpoint)
			throw new InvalidOperationException("The preview decision does not match the checkpoint.");

		ClipPlacement placement = candidate.Montage.Placements.SingleOrDefault(item =>
			string.Equals(item.Clip.FilePath, decision.Clip.MediaPath,
				StringComparison.OrdinalIgnoreCase))
			?? throw new InvalidOperationException(
				"The active checkpoint clip is not present exactly once in the candidate.");
		CheckpointPreviewWindow window = CreateWindow(placement, timeline);
		int attempt = artifacts.NextAttempt(checkpoint);
		string previewPath =
			CheckpointPreviewArtifactStore.PreviewRelativePath(checkpoint, attempt);
		CheckpointTimingSidecar timing = CreateTiming(
			checkpoint, attempt, window, placement, adjustment);
		string timelinePath = artifacts.SaveTimeline(checkpoint, attempt, timeline);
		string timingPath = artifacts.SaveTimingSidecar(checkpoint, attempt, timing);
		string visualizationPath = artifacts.SaveTimingVisualization(
			checkpoint, attempt, CreateTimingVisualization(timing));
		CheckpointContactSheetManifest contactSheet =
			CreateContactSheetManifest(previewPath, window);
		string contactSheetPath = artifacts.SaveContactSheetManifest(
			checkpoint,
			attempt,
			contactSheet);
		CheckpointPreviewArtifact artifact = new()
		{
			SessionId = sessionId,
			Checkpoint = checkpoint,
			Attempt = attempt,
			Status = CheckpointPreviewStatus.Rendering,
			StartedUtc = clock(),
			Workspace = timeline.Workspace,
			Window = window,
			RenderProfileId = "review-1080p",
			PreviewRelativePath = previewPath,
			TimelineSnapshotRelativePath = timelinePath,
			TimingSidecarRelativePath = timingPath,
			TimingVisualizationRelativePath = visualizationPath,
			ContactSheetManifestRelativePath = contactSheetPath
		};
		artifacts.SaveArtifact(artifact);

		using CancellationTokenSource renderDeadline =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		renderDeadline.CancelAfter(timeout);
		try
		{
			RenderCandidatePreviewResult rendered = await renderer.RenderAsync(
				timeline.Workspace, window, checkpoint, attempt, renderDeadline.Token);
			ValidateRenderedResult(rendered, previewPath, window);
			ValidateRenderedFile(rendered, artifacts);
			artifact.PreviewRelativePath = rendered.OutputRelativePath;
			artifact.PreviewSha256 = rendered.Sha256.ToLowerInvariant();
			artifact.RenderProfileId = rendered.RenderProfileId;
			CaptureCandidatePreviewFramesResult capturedFrames =
				await renderer.CaptureFramesAsync(
					timeline.Workspace,
					contactSheet.SampleOffsets
						.Select(offset => window.Start + offset)
						.ToList(),
					checkpoint,
					attempt,
					renderDeadline.Token);
			IReadOnlyList<CheckpointPreviewFrameEvidence> frameEvidence =
				ValidateCapturedFrames(
					capturedFrames, contactSheet, window, artifacts);
			contactSheet.FrameRelativePaths =
				frameEvidence.Select(item => item.RelativePath).ToList();
			contactSheet.Status = "captured";
			contactSheet.Diagnostic =
				"VEGAS SaveSnapshot captured isolated candidate PNG frames at the persisted sample times.";
			artifacts.SaveContactSheetManifest(checkpoint, attempt, contactSheet);
			try
			{
				using CancellationTokenSource reviewDeadline =
					CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				reviewDeadline.CancelAfter(timeout);
				CheckpointReviewReport review = await reviewer.ReviewAsync(
					sessionId,
					checkpoint,
					attempt,
					sketch,
					decision,
					candidate,
					timeline,
					timing,
					artifacts.Resolve(visualizationPath),
					rendered,
					frameEvidence,
					reviewDeadline.Token);
				CheckpointPreviewContractValidator.Validate(review);
				artifact.ReviewReportRelativePath =
					artifacts.SaveReviewReport(checkpoint, attempt, review);
				artifact.Status = CheckpointPreviewStatus.Completed;
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				artifact.Status = CheckpointPreviewStatus.ReviewFailed;
				artifact.FailureCode = "REVIEW_TIMEOUT";
				artifact.FailureMessage =
					"Preview rendering completed, but multimodal review exceeded the configured timeout.";
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (TimeoutException)
			{
				artifact.Status = CheckpointPreviewStatus.ReviewFailed;
				artifact.FailureCode = "REVIEW_TIMEOUT";
				artifact.FailureMessage =
					"Preview evidence was captured, but multimodal review exceeded its request deadline.";
			}
			catch (Exception exception)
			{
				artifact.Status = CheckpointPreviewStatus.ReviewFailed;
				artifact.FailureCode = "REVIEW_FAILED";
				artifact.FailureMessage = SafeMessage(exception);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			artifact.Status = CheckpointPreviewStatus.Cancelled;
			artifact.FailureCode = "PREVIEW_CANCELLED";
			artifact.FailureMessage = "Checkpoint preview was cancelled.";
		}
		catch (OperationCanceledException)
		{
			artifact.Status = CheckpointPreviewStatus.RenderFailed;
			artifact.FailureCode = "RENDER_TIMEOUT";
			artifact.FailureMessage =
				"VEGAS preview rendering exceeded the configured timeout.";
		}
		catch (TimeoutException)
		{
			artifact.Status = CheckpointPreviewStatus.RenderFailed;
			artifact.FailureCode = "RENDER_TIMEOUT";
			artifact.FailureMessage =
				"VEGAS preview evidence capture exceeded the automation deadline.";
		}
		catch (Exception exception)
		{
			artifact.Status = CheckpointPreviewStatus.RenderFailed;
			artifact.FailureCode = "RENDER_FAILED";
			artifact.FailureMessage = SafeMessage(exception);
		}
		artifact.CompletedUtc = clock();
		artifacts.SaveArtifact(artifact);
		if (artifact.Status == CheckpointPreviewStatus.Cancelled &&
			cancellationToken.IsCancellationRequested)
			cancellationToken.ThrowIfCancellationRequested();
		return artifact;
	}

	internal static CheckpointPreviewWindow CreateWindow(
		ClipPlacement placement,
		CandidateTimelineSnapshot timeline)
	{
		ArgumentNullException.ThrowIfNull(placement);
		ArgumentNullException.ThrowIfNull(timeline);
		TimeSpan placementStart = TimeSpan.FromSeconds(placement.TimelineStartSeconds);
		TimeSpan placementEnd = TimeSpan.FromSeconds(placement.TimelineEndSeconds);
		if (placementStart < timeline.TimelineStart || placementEnd > timeline.TimelineEnd)
			throw new InvalidOperationException(
				"The active placement is outside the materialized candidate timeline.");
		TimeSpan placementDuration = placementEnd - placementStart;
		if (placementDuration > MaximumDuration)
			throw new InvalidOperationException(
				"The active placement exceeds the twenty-second checkpoint preview limit.");

		TimeSpan before = Min(PreferredContext, placementStart - timeline.TimelineStart);
		TimeSpan after = Min(PreferredContext, timeline.TimelineEnd - placementEnd);
		TimeSpan overflow = placementDuration + before + after - MaximumDuration;
		if (overflow > TimeSpan.Zero)
		{
			TimeSpan reduceAfter = Min(after, TimeSpan.FromTicks((overflow.Ticks + 1) / 2));
			after -= reduceAfter;
			overflow -= reduceAfter;
			before -= Min(before, overflow);
		}
		CheckpointPreviewWindow window = new()
		{
			Start = placementStart - before,
			End = placementEnd + after,
			PlacementStart = placementStart,
			PlacementEnd = placementEnd,
			ContextBefore = before,
			ContextAfter = after
		};
		CheckpointPreviewContractValidator.Validate(window);
		return window;
	}

	private CheckpointTimingSidecar CreateTiming(
		int checkpoint,
		int attempt,
		CheckpointPreviewWindow window,
		ClipPlacement placement,
		TimelineAdjustmentDelta? adjustment)
	{
		List<CheckpointTimingPoint> points = new()
		{
			new()
			{
				PointId = "placement-in",
				Kind = "cut",
				TimelineTime = window.PlacementStart,
				Description = "Active clip starts."
			},
			new()
			{
				PointId = "placement-out",
				Kind = "cut",
				TimelineTime = window.PlacementEnd,
				Description = "Active clip ends."
			}
		};
		int beat = 0;
		foreach (double seconds in placement.AssignedBeatTimesSeconds
			.Where(value => value >= window.Start.TotalSeconds &&
				value <= window.End.TotalSeconds))
		{
			beat++;
			points.Add(new CheckpointTimingPoint
			{
				PointId = $"music-{beat:D2}",
				Kind = "music",
				TimelineTime = TimeSpan.FromSeconds(seconds),
				Description = "Assigned musical synchronization event."
			});
		}
		int kill = 0;
		foreach (double seconds in placement.TimelineKillTimesSeconds
			.Where(value => value >= window.Start.TotalSeconds &&
				value <= window.End.TotalSeconds))
		{
			kill++;
			points.Add(new CheckpointTimingPoint
			{
				PointId = $"kill-{kill:D2}",
				Kind = "kill",
				TimelineTime = TimeSpan.FromSeconds(seconds),
				Description = "Confirmed gameplay kill event."
			});
		}
		return new CheckpointTimingSidecar
		{
			SessionId = sessionId,
			Checkpoint = checkpoint,
			Attempt = attempt,
			Window = window,
			Points = points.OrderBy(item => item.TimelineTime).ToList(),
			TimelineAdjustment = adjustment
		};
	}

	private static CheckpointContactSheetManifest CreateContactSheetManifest(
		string previewPath,
		CheckpointPreviewWindow window)
	{
		TimeSpan duration = window.End - window.Start;
		return new CheckpointContactSheetManifest
		{
			SourcePreviewRelativePath = previewPath,
			SampleOffsets = new[] { .1d, .3d, .5d, .7d, .9d }
				.Select(ratio => TimeSpan.FromTicks((long)(duration.Ticks * ratio)))
				.Distinct()
				.ToList(),
			Status = "pending-capture",
			Diagnostic =
				"Frame sample times are persisted before VEGAS captures isolated candidate PNGs."
		};
	}

	private static IReadOnlyList<CheckpointPreviewFrameEvidence> ValidateCapturedFrames(
		CaptureCandidatePreviewFramesResult result,
		CheckpointContactSheetManifest manifest,
		CheckpointPreviewWindow window,
		CheckpointPreviewArtifactStore artifacts)
	{
		if (result == null || result.Frames == null ||
			result.Frames.Count != manifest.SampleOffsets.Count)
			throw new InvalidOperationException(
				"VEGAS returned an incomplete preview-frame set.");
		List<CheckpointPreviewFrameEvidence> evidence = new();
		for (int index = 0; index < result.Frames.Count; index++)
		{
			CapturedCandidatePreviewFrame frame = result.Frames[index];
			TimeSpan expectedTime = window.Start + manifest.SampleOffsets[index];
			if (frame.TimelineTime != expectedTime)
				throw new InvalidOperationException(
					"VEGAS returned a preview frame for an unexpected timeline time.");
			if (frame.Sha256?.Length != 64 ||
				frame.Sha256.Any(character => !Uri.IsHexDigit(character)))
				throw new InvalidOperationException(
					"VEGAS returned an invalid preview-frame SHA-256.");
			string fullPath = artifacts.Resolve(frame.OutputRelativePath);
			if (!File.Exists(fullPath))
				throw new FileNotFoundException(
					"VEGAS reported a preview frame that does not exist.",
					fullPath);
			string actualHash = Convert.ToHexString(
				System.Security.Cryptography.SHA256.HashData(
					File.ReadAllBytes(fullPath))).ToLowerInvariant();
			if (!string.Equals(
				actualHash, frame.Sha256, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"A captured preview-frame hash did not match.");
			evidence.Add(new CheckpointPreviewFrameEvidence(
				frame.TimelineTime,
				frame.OutputRelativePath,
				fullPath,
				actualHash));
		}
		return evidence;
	}

	private static byte[] CreateTimingVisualization(CheckpointTimingSidecar timing)
	{
		const int width = 1200;
		const int height = 360;
		const int left = 80;
		const int usable = 1040;
		double duration = (timing.Window.End - timing.Window.Start).TotalSeconds;
		int X(TimeSpan value) => Math.Clamp(
			left + (int)Math.Round(
				(value - timing.Window.Start).TotalSeconds / duration * usable),
			left,
			left + usable);
		byte[] pixels = new byte[width * height * 3];
		Fill(pixels, width, height, 0, 0, width, height, 32, 33, 36);
		Fill(pixels, width, height, left, 178, usable, 4, 154, 160, 166);
		int placementX = X(timing.Window.PlacementStart);
		int placementWidth = Math.Max(1, X(timing.Window.PlacementEnd) - placementX);
		Fill(pixels, width, height, placementX, 145, placementWidth, 70, 63, 81, 181);
		foreach (CheckpointTimingPoint point in timing.Points)
		{
			int x = X(point.TimelineTime);
			(byte Red, byte Green, byte Blue) color = point.Kind switch
			{
				"music" => (251, 188, 4),
				"kill" => (52, 168, 83),
				_ => (234, 67, 53)
			};
			Fill(
				pixels,
				width,
				height,
				Math.Max(0, x - 2),
				105,
				4,
				140,
				color.Red,
				color.Green,
				color.Blue);
		}
		// Fixed-position color legend; textual labels and exact timestamps remain
		// in timing.json so the raster is deterministic without a font dependency.
		Fill(pixels, width, height, 80, 300, 80, 20, 63, 81, 181);
		Fill(pixels, width, height, 190, 300, 80, 20, 251, 188, 4);
		Fill(pixels, width, height, 300, 300, 80, 20, 52, 168, 83);
		Fill(pixels, width, height, 410, 300, 80, 20, 234, 67, 53);
		return EncodePng(width, height, pixels);
	}

	private static void Fill(
		byte[] pixels,
		int width,
		int height,
		int x,
		int y,
		int rectangleWidth,
		int rectangleHeight,
		byte red,
		byte green,
		byte blue)
	{
		int right = Math.Min(width, x + rectangleWidth);
		int bottom = Math.Min(height, y + rectangleHeight);
		for (int row = Math.Max(0, y); row < bottom; row++)
		for (int column = Math.Max(0, x); column < right; column++)
		{
			int offset = (row * width + column) * 3;
			pixels[offset] = red;
			pixels[offset + 1] = green;
			pixels[offset + 2] = blue;
		}
	}

	private static byte[] EncodePng(int width, int height, byte[] pixels)
	{
		using MemoryStream output = new();
		output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
		byte[] header = new byte[13];
		WriteBigEndian(header, 0, (uint)width);
		WriteBigEndian(header, 4, (uint)height);
		header[8] = 8;
		header[9] = 2;
		WritePngChunk(output, "IHDR", header);
		using MemoryStream raw = new();
		for (int row = 0; row < height; row++)
		{
			raw.WriteByte(0);
			raw.Write(pixels, row * width * 3, width * 3);
		}
		byte[] compressed;
		using (MemoryStream buffer = new())
		{
			using (ZLibStream deflate = new(
				buffer, CompressionLevel.SmallestSize, leaveOpen: true))
			{
				byte[] rawBytes = raw.ToArray();
				deflate.Write(rawBytes, 0, rawBytes.Length);
			}
			compressed = buffer.ToArray();
		}
		WritePngChunk(output, "IDAT", compressed);
		WritePngChunk(output, "IEND", Array.Empty<byte>());
		return output.ToArray();
	}

	private static void WritePngChunk(
		Stream output,
		string type,
		byte[] data)
	{
		byte[] typeBytes = Encoding.ASCII.GetBytes(type);
		byte[] length = new byte[4];
		WriteBigEndian(length, 0, (uint)data.Length);
		output.Write(length);
		output.Write(typeBytes);
		output.Write(data);
		byte[] crcInput = new byte[typeBytes.Length + data.Length];
		Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
		Buffer.BlockCopy(data, 0, crcInput, typeBytes.Length, data.Length);
		byte[] crc = new byte[4];
		WriteBigEndian(crc, 0, Crc32(crcInput));
		output.Write(crc);
	}

	private static uint Crc32(byte[] data)
	{
		uint crc = 0xffffffff;
		foreach (byte value in data)
		{
			crc ^= value;
			for (int bit = 0; bit < 8; bit++)
				crc = (crc & 1) == 0
					? crc >> 1
					: (crc >> 1) ^ 0xedb88320;
		}
		return ~crc;
	}

	private static void WriteBigEndian(byte[] buffer, int offset, uint value)
	{
		buffer[offset] = (byte)(value >> 24);
		buffer[offset + 1] = (byte)(value >> 16);
		buffer[offset + 2] = (byte)(value >> 8);
		buffer[offset + 3] = (byte)value;
	}

	private static void ValidateRenderedResult(
		RenderCandidatePreviewResult result,
		string expectedPath,
		CheckpointPreviewWindow window)
	{
		if (result == null)
			throw new InvalidOperationException("VEGAS returned no preview result.");
		if (!string.Equals(result.OutputRelativePath, expectedPath, StringComparison.Ordinal))
			throw new InvalidOperationException("VEGAS returned a preview at an unexpected path.");
		if (string.IsNullOrWhiteSpace(result.RenderProfileId))
			throw new InvalidOperationException("VEGAS did not report the render profile.");
		if (result.Sha256?.Length != 64 ||
			result.Sha256.Any(character => !Uri.IsHexDigit(character)))
			throw new InvalidOperationException("VEGAS returned an invalid preview SHA-256.");
		if (result.RenderedDuration != window.End - window.Start)
			throw new InvalidOperationException("VEGAS returned an unexpected preview duration.");
	}

	private static void ValidateRenderedFile(
		RenderCandidatePreviewResult result,
		CheckpointPreviewArtifactStore artifacts)
	{
		string fullPath = artifacts.Resolve(result.OutputRelativePath);
		if (!File.Exists(fullPath))
			throw new FileNotFoundException(
				"VEGAS reported a preview video that does not exist.",
				fullPath);
		string actualHash = Convert.ToHexString(
			System.Security.Cryptography.SHA256.HashData(
				File.ReadAllBytes(fullPath))).ToLowerInvariant();
		if (!string.Equals(
			actualHash, result.Sha256, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"The rendered preview video hash did not match.");
	}

	private static string SafeMessage(Exception exception)
	{
		string message = exception.GetBaseException().Message;
		return string.IsNullOrWhiteSpace(message)
			? exception.GetType().Name
			: message.Length <= 1000 ? message : message.Substring(0, 1000);
	}

	private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
		left <= right ? left : right;
}
