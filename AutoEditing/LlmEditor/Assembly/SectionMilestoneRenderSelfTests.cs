using System.Security.Cryptography;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.LlmEditor.RoughCut;

namespace AutoEditing.LlmEditor.Assembly;

internal static class SectionMilestoneRenderSelfTests
{
	public static void Run(string testRoot)
	{
		string sessionRoot = Path.Combine(
			testRoot,
			"section-milestone-render");
		Directory.CreateDirectory(sessionRoot);
		FakeRenderer renderer = new(sessionRoot);
		SectionMilestoneRenderService service = new(
			"section-milestone-session",
			sessionRoot,
			renderer,
			clock: () => new DateTimeOffset(
				2026, 7, 27, 12, 0, 0, TimeSpan.Zero));
		CandidateWorkspaceId workspace = new()
		{
			SessionId = "section-milestone-session",
			Iteration = 3,
			Nonce = "candidate"
		};
		string planHash = new string('a', 64);
		SectionMilestoneRenderManifest first = service.RenderAsync(
				"build-section",
				3,
				workspace,
				TimeSpan.FromSeconds(5),
				TimeSpan.FromSeconds(50),
				planHash,
				CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(
			first.Chunks.Count == 3 &&
			first.Chunks[0].Duration == TimeSpan.FromSeconds(20) &&
			first.Chunks[1].Duration == TimeSpan.FromSeconds(20) &&
			first.Chunks[2].Duration == TimeSpan.FromSeconds(5),
			"A complete 45-second section was not rendered in bounded chunks.");
		SectionMilestoneRenderManifest replay = service.RenderAsync(
				"build-section",
				3,
				workspace,
				TimeSpan.FromSeconds(5),
				TimeSpan.FromSeconds(50),
				planHash,
				CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(
			renderer.CallCount == 3 &&
			replay.Chunks.Select(item => item.Sha256)
				.SequenceEqual(first.Chunks.Select(item => item.Sha256)),
			"A completed section milestone was rerendered during idempotent replay.");

		File.WriteAllText(
			Path.Combine(
				sessionRoot,
				first.Chunks[0].OutputRelativePath.Replace(
					'/', Path.DirectorySeparatorChar)),
			"corrupt");
		AssertThrows<InvalidDataException>(
			() => service.RenderAsync(
					"build-section",
					3,
					workspace,
					TimeSpan.FromSeconds(5),
					TimeSpan.FromSeconds(50),
					planHash,
					CancellationToken.None)
				.GetAwaiter().GetResult(),
			"A corrupt cached section milestone was silently reused.");
	}

	private sealed class FakeRenderer : IRoughCutChunkRenderer
	{
		private readonly string sessionRoot;

		public FakeRenderer(string sessionRoot)
		{
			this.sessionRoot = sessionRoot;
		}

		public int CallCount { get; private set; }

		public Task<RenderCandidatePreviewResult> RenderAsync(
			CandidateWorkspaceId workspace,
			TimeSpan start,
			TimeSpan duration,
			string outputRelativePath,
			string idempotencyKey,
			CancellationToken cancellationToken)
		{
			CallCount++;
			string path = Path.Combine(
				sessionRoot,
				outputRelativePath.Replace(
					'/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(
				path,
				idempotencyKey + "|" + start + "|" + duration);
			return Task.FromResult(new RenderCandidatePreviewResult
			{
				OutputRelativePath = outputRelativePath,
				Sha256 = HashFile(path),
				RenderedDuration = duration,
				RenderProfileId = "review-1080p"
			});
		}
	}

	private static string HashFile(string path)
	{
		using SHA256 sha = SHA256.Create();
		using FileStream stream = File.OpenRead(path);
		return Convert.ToHexString(sha.ComputeHash(stream))
			.ToLowerInvariant();
	}

	private static void AssertThrows<TException>(
		Action action,
		string message)
		where TException : Exception
	{
		try { action(); }
		catch (TException) { return; }
		throw new InvalidOperationException(message);
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}
}
