using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Serialization;

namespace AutoEditing.LlmEditor.Assembly;

internal static class AssemblyActionStoreSelfTests
{
	public static void Run(string testRoot)
	{
		TestMalformedAndStaleActionsDoNotBlockValidAction(testRoot);
		TestConflictingActionsForOneStateAreQuarantined(testRoot);
		TestConcurrentConsumersReturnActionExactlyOnce(testRoot);
		TestClaimWithoutDispositionIsRedelivered(testRoot);
	}

	private static void TestMalformedAndStaleActionsDoNotBlockValidAction(
		string testRoot)
	{
		string sessionRoot = CreateSession(testRoot, "action-ordering");
		string actions = Actions(sessionRoot);
		File.WriteAllText(Path.Combine(actions, "000-malformed.json"), "{");
		Write(actions, "001-stale.json", new AssemblyAction
		{
			ActionId = "stale",
			SessionId = "session-a",
			Checkpoint = 1,
			ExpectedStateRevision = 2,
			Kind = AssemblyActionKind.ResetCurrentClip,
			CreatedUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
		});
		Write(actions, "002-valid.json", new AssemblyAction
		{
			ActionId = "valid",
			SessionId = "session-a",
			Checkpoint = 2,
			ExpectedStateRevision = 7,
			Kind = AssemblyActionKind.AcceptTimelineAndContinue,
			CreatedUtc = DateTimeOffset.Parse("2026-01-01T00:00:01Z")
		});

		AssemblyAction? consumed =
			new AssemblyActionStore(sessionRoot).TryConsume(2, "session-a", 7);

		Assert(consumed?.ActionId == "valid",
			"A malformed or stale action blocked the next valid chronological action.");
		Assert(Directory.EnumerateFiles(
				Path.Combine(sessionRoot, "assembly", "quarantined"),
				"*.malformed.json").Any(),
			"The malformed action was not quarantined for inspection.");
		Assert(Directory.EnumerateFiles(
				Path.Combine(sessionRoot, "assembly", "quarantined"),
				"*.stale-checkpoint.json").Any(),
			"The stale action was not quarantined for inspection.");
	}

	private static void TestConflictingActionsForOneStateAreQuarantined(
		string testRoot)
	{
		string sessionRoot = CreateSession(testRoot, "action-conflict");
		string actions = Actions(sessionRoot);
		Write(actions, "001-accept.json", new AssemblyAction
		{
			ActionId = "accept",
			SessionId = "session-a",
			Checkpoint = 3,
			ExpectedStateRevision = 11,
			Kind = AssemblyActionKind.AcceptTimelineAndContinue,
			CreatedUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
		});
		Write(actions, "002-reset.json", new AssemblyAction
		{
			ActionId = "reset",
			SessionId = "session-a",
			Checkpoint = 3,
			ExpectedStateRevision = 11,
			Kind = AssemblyActionKind.ResetCurrentClip,
			CreatedUtc = DateTimeOffset.Parse("2026-01-01T00:00:01Z")
		});
		AssemblyActionStore store = new(sessionRoot);

		AssemblyAction? first = store.TryConsume(3, "session-a", 11);
		AssemblyAction? second = store.TryConsume(3, "session-a", 11);

		Assert(first?.ActionId == "accept" && second == null,
			"More than one action was consumed for a single checkpoint revision.");
		Assert(Directory.EnumerateFiles(
				Path.Combine(sessionRoot, "assembly", "quarantined"),
				"*.conflicting-action.json").Any(),
			"The conflicting action was not quarantined.");
	}

	private static void TestConcurrentConsumersReturnActionExactlyOnce(
		string testRoot)
	{
		string sessionRoot = CreateSession(testRoot, "action-race");
		Write(Actions(sessionRoot), "action.json", new AssemblyAction
		{
			ActionId = "race",
			SessionId = "session-a",
			Checkpoint = 4,
			ExpectedStateRevision = 15,
			Kind = AssemblyActionKind.AcceptTimelineAndContinue
		});
		using ManualResetEventSlim start = new(false);
		Task<AssemblyAction?>[] consumers = Enumerable.Range(0, 8)
			.Select(_ => Task.Run(() =>
			{
				start.Wait();
				return new AssemblyActionStore(sessionRoot)
					.TryConsume(4, "session-a", 15);
			}))
			.ToArray();

		start.Set();
		Task.WaitAll(consumers);

		Assert(consumers.Count(task => task.Result != null) == 1,
			"Concurrent consumers returned the same assembly action more than once.");
	}

	private static void TestClaimWithoutDispositionIsRedelivered(string testRoot)
	{
		string sessionRoot = CreateSession(testRoot, "action-claim-recovery");
		AssemblyAction action = new()
		{
			ActionId = "claimed-before-crash",
			SessionId = "session-a",
			Checkpoint = 5,
			ExpectedStateRevision = 21,
			Kind = AssemblyActionKind.AcceptRoughCut,
			CreatedUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
		};
		Write(Actions(sessionRoot), "recover.json", action);
		string claimDirectory =
			Path.Combine(sessionRoot, "assembly", "action-claims");
		Directory.CreateDirectory(claimDirectory);
		File.WriteAllText(
			Path.Combine(claimDirectory, "000005-0000000000000000021.json"),
			ContractSerializer.Serialize(action));

		AssemblyAction? recovered = new AssemblyActionStore(sessionRoot)
			.TryRecoverClaimed(5, "session-a", 21);

		Assert(recovered?.ActionId == action.ActionId,
			"An action claimed immediately before a process crash was silently lost.");
	}

	private static string CreateSession(string testRoot, string name)
	{
		string root = Path.Combine(testRoot, name);
		Directory.CreateDirectory(root);
		return root;
	}

	private static string Actions(string sessionRoot)
	{
		string path = Path.Combine(sessionRoot, "assembly", "actions");
		Directory.CreateDirectory(path);
		return path;
	}

	private static void Write(
		string directory,
		string fileName,
		AssemblyAction action)
	{
		File.WriteAllText(
			Path.Combine(directory, fileName),
			ContractSerializer.Serialize(action));
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}
}
