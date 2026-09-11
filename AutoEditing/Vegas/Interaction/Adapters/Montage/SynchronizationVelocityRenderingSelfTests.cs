using System;
using Core.Domain.Editing;

namespace Core.Scripts;

internal static class SynchronizationVelocityRenderingSelfTests
{
	public static void Run()
	{
		VelocityEnvelopeRenderPlan accelerated =
			VelocityEnvelopeRenderPlan.Create(new SpeedProfile(new[]
			{
				new SpeedProfilePoint(2, 1.5),
				new SpeedProfilePoint(5, 1.5)
			}));
		Assert(accelerated.Points.Count == 2,
			"The velocity renderer dropped an authoritative synchronization point.");
		AssertClose(accelerated.Points[0].TimelineOffsetSeconds, 0,
			"The source-window start did not map to the event start.");
		AssertClose(accelerated.Points[1].TimelineOffsetSeconds, 2,
			"The constant-speed source window did not map to its expected duration.");
		AssertClose(accelerated.Points[0].Speed, 1.5,
			"The accelerated synchronization rate changed during render planning.");

		VelocityEnvelopeRenderPlan normal =
			VelocityEnvelopeRenderPlan.Create(new SpeedProfile(new[]
			{
				new SpeedProfilePoint(0, 1),
				new SpeedProfilePoint(2, 1)
			}));
		Assert(normal.Points.Count == 2 &&
			Math.Abs(normal.Points[0].Speed - 1) < 0.000001 &&
			Math.Abs(normal.Points[1].Speed - 1) < 0.000001,
			"A 1.0x synchronization profile was omitted instead of being rendered " +
			"as observable timeline evidence.");

		Assert(!MontageBuildContext.Candidate(
				new AutoEditing.Iteration.Contracts.Automation.CandidateWorkspaceId
				{
					SessionId = "velocity-policy-test",
					Iteration = 1,
					Nonce = "candidate"
				},
				applyEffects: false,
				includeSong: false,
				includeSfx: false)
			.ApplyEffects,
			"The candidate fixture unexpectedly enabled creative effects.");
	}

	private static void AssertClose(double actual, double expected, string message)
	{
		if (Math.Abs(actual - expected) > 0.000001)
			throw new InvalidOperationException(message);
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}
}
