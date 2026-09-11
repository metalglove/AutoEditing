using AutoEditing.Iteration.Contracts.Assembly;

namespace AutoEditing.LlmEditor.Assembly;

internal static class AssemblySteeringDirectiveStoreSelfTests
{
	public static void Run(string testRoot)
	{
		string sessionRoot = Path.Combine(testRoot, "scoped-steering");
		Directory.CreateDirectory(sessionRoot);
		const string sessionId = "scoped-steering-session";
		AssemblySketch sketch = new()
		{
			RequestId = "request",
			EditorialThesis = "test",
			Sections = new List<AssemblySectionIntent>
			{
				new() { SectionId = "section-a", RegionId = "a", EditorialRole = "build", EnergyDirection = "up", PacingIntent = "steady", Rationale = "test" },
				new() { SectionId = "section-b", RegionId = "b", EditorialRole = "peak", EnergyDirection = "up", PacingIntent = "fast", Rationale = "test" }
			},
			ClipOrder = new List<AssemblyClipIntent>
			{
				Intent(1, "section-a"),
				Intent(2, "section-a"),
				Intent(3, "section-b")
			},
			SyncStrategy = new AssemblySyncStrategy
			{
				Density = "sparse",
				PreferredMusicalTypes = new List<string> { "Impact" },
				Rationale = "test"
			}
		};
		AssemblySteeringDirectiveStore store = new(sessionRoot);
		store.SaveAcceptedDirection(
			Action(
				"next",
				sessionId,
				1,
				AssemblySteeringScope.NextClip,
				"tighten only the next clip"),
			sketch,
			1);
		store.SaveAcceptedDirection(
			Action(
				"section",
				sessionId,
				1,
				AssemblySteeringScope.RemainingSection,
				"preserve this section's restraint"),
			sketch,
			1);
		store.SaveAcceptedDirection(
			Action(
				"global",
				sessionId,
				1,
				AssemblySteeringScope.GlobalRemainder,
				"avoid repeated maps"),
			sketch,
			1);

		string checkpointTwo =
			store.DescribeApplicable(sessionId, sketch, 2);
		Assert(
			checkpointTwo.Contains("[next clip] tighten only the next clip",
				StringComparison.Ordinal) &&
			checkpointTwo.Contains(
				"[remaining section] preserve this section's restraint",
				StringComparison.Ordinal) &&
			checkpointTwo.Contains(
				"[global remainder] avoid repeated maps",
				StringComparison.Ordinal),
			"Checkpoint two did not receive all three applicable scopes.");
		string checkpointThree =
			store.DescribeApplicable(sessionId, sketch, 3);
		Assert(
			!checkpointThree.Contains("tighten only", StringComparison.Ordinal) &&
			!checkpointThree.Contains("section's restraint", StringComparison.Ordinal) &&
			checkpointThree.Contains("avoid repeated maps", StringComparison.Ordinal),
			"One-shot or section-scoped direction leaked into another section.");

		AssertThrows<InvalidOperationException>(
			() => store.SaveAcceptedDirection(
				Action(
					"current",
					sessionId,
					1,
					AssemblySteeringScope.CurrentClip,
					"revise this"),
				sketch,
				1),
			"Current-clip direction was incorrectly persisted for future planning.");
		AssertThrows<InvalidDataException>(
			() => store.SaveAcceptedDirection(
				Action(
					"global",
					sessionId,
					1,
					AssemblySteeringScope.GlobalRemainder,
					"different content"),
				sketch,
				1),
			"A contradictory replay replaced an immutable steering directive.");
		AssertThrows<InvalidOperationException>(
			() => store.SaveAcceptedDirection(
				Action(
					"after-final",
					sessionId,
					3,
					AssemblySteeringScope.GlobalRemainder,
					"unused"),
				sketch,
				3),
			"Future direction was accepted after the final clip.");
	}

	private static AssemblyClipIntent Intent(int order, string sectionId) =>
		new()
		{
			Order = order,
			SectionId = sectionId,
			EditorialRole = "test",
			Rationale = "test",
			Confidence = 1,
			Clip = new AssemblyClipReference
			{
				ReferenceId = "clip-" + order,
				MediaPath = "fixtures/clip-" + order + ".mp4"
			}
		};

	private static AssemblyAction Action(
		string id,
		string sessionId,
		int checkpoint,
		AssemblySteeringScope scope,
		string instruction) =>
		new()
		{
			ActionId = id,
			SessionId = sessionId,
			Checkpoint = checkpoint,
			ExpectedStateRevision = checkpoint,
			Kind = checkpoint == 3
				? AssemblyActionKind.FinishSyncPass
				: AssemblyActionKind.AcceptTimelineAndContinue,
			SteeringScope = scope,
			Instruction = instruction,
			CreatedUtc = new DateTimeOffset(
				2026, 7, 27, 10, checkpoint, 0, TimeSpan.Zero)
		};

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
