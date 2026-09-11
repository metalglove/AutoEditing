using System;
using System.IO;
using System.Linq;

namespace Core.Scripts;

internal static class PreviewRenderPlanningSelfTests
{
	public static void Run()
	{
		const string rendererId = "11111111-1111-1111-1111-111111111111";
		const string templateId = "22222222-2222-2222-2222-222222222222";
		PreviewRenderProfile profile = new PreviewRenderProfile(
			"review-1080p",
			rendererId,
			templateId,
			new[] { ".mp4" },
			20);
		PreviewRendererCapability capability = new PreviewRendererCapability(
			rendererId,
			"MAGIX AVC/AAC",
			new[]
			{
				new PreviewTemplateCapability(templateId, "Configured template", true, 1, new[] { ".mp4" })
			});
		string root = Path.Combine(Path.GetTempPath(), "AutoEditing", "session");
		PreviewRenderPlan plan = PreviewRenderPlanFactory.Create(
			root,
			"iterations/0001/artifacts/preview.mp4",
			10,
			40,
			12,
			10,
			profile,
			new[] { capability });
		Assert(plan.OutputPath.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase),
			"Output containment changed.");
		Assert(plan.Renderer.Template.TemplateId == templateId,
			"Stable template selection changed.");
		PreviewRenderProfile resolved = PreviewRenderProfileResolver.Resolve(
			"review-1080p",
			new[]
			{
				new PreviewRendererCapability(
					"33333333-3333-3333-3333-333333333333",
					"later renderer",
					new[]
					{
						new PreviewTemplateCapability(
							"44444444-4444-4444-4444-444444444444",
							"720p",
							true,
							1,
							new[] { ".mp4" })
					}),
				capability
			});
		Assert(resolved.RendererClassId == rendererId && resolved.TemplateId == templateId,
			"Profile resolution is not deterministic by stable renderer/template IDs.");

		ExpectFailure(
			() => PreviewRenderPlanFactory.ResolveContainedOutput(root, "../outside.mp4"),
			"A traversal output was accepted.");
		ExpectFailure(
			() => PreviewRenderPlanFactory.Create(
				root, "preview.mp4", 10, 40, 35, 10, profile, new[] { capability }),
			"An out-of-bounds preview was accepted.");
		ExpectFailure(
			() => PreviewRendererSelector.Select(
				profile,
				new[]
				{
					new PreviewRendererCapability(
						rendererId,
						"renderer",
						new[] { new PreviewTemplateCapability(templateId, "invalid", false, 1, new[] { ".mp4" }) })
				},
				".mp4"),
			"An invalid configured template was accepted.");
		ExpectFailure(
			() => PreviewRenderProfileResolver.Resolve("unknown", new[] { capability }),
			"An unknown render profile was accepted.");

		PreviewRenderStateSnapshot snapshot = new PreviewRenderStateSnapshot(
			5,
			2,
			3,
			true,
			new[]
			{
				new PreviewTrackState("track-user", "User", false, false),
				new PreviewTrackState("track-video", "Candidate video", true, false),
				new PreviewTrackState("track-song", "Candidate song", false, false)
			});
		PreviewRenderStatePlan statePlan = PreviewRenderStatePlanner.Create(
			snapshot,
			new[] { "track-video", "track-song" });
		Assert(statePlan.IsolateCandidate.Single(item => item.StableTrackKey == "track-user").Mute,
			"Non-candidate track was not isolated.");
		Assert(statePlan.IsolateCandidate
				.Where(item => item.StableTrackKey != "track-user")
				.All(item => !item.Mute && item.Solo),
			"Candidate tracks were not activated for isolated rendering.");
		Assert(ReferenceEquals(statePlan.Captured, snapshot) &&
			statePlan.RestoreTracks.SequenceEqual(snapshot.Tracks),
			"The exact captured state was not retained for restoration.");
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private static void ExpectFailure(Action action, string message)
	{
		try { action(); }
		catch { return; }
		throw new InvalidOperationException(message);
	}
}
