using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using Core.Domain.Editing;
using Core.Domain.Planning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class ApplyCandidateEffectsCommandHandler : IVegasCommandHandler
{
	public string CommandType => VegasOperations.ApplyCandidateEffects;

	public string Execute(Vegas vegas, string payloadJson)
	{
		ApplyCandidateEffectsCommand command =
			JsonConvert.DeserializeObject<ApplyCandidateEffectsCommand>(payloadJson);
		ApplyCandidateEffectsRequest request = command?.Request ??
			throw new InvalidOperationException("Effects-pass request is empty.");
		ValidateRequest(request);
		Dictionary<string, VideoEvent> videos = FindVideoEvents(
			vegas.Project, request.Workspace);
		List<PolishActionResult> results = new List<PolishActionResult>();
		VegasEditorialEffectRenderer renderer = new VegasEditorialEffectRenderer();
		Dictionary<VideoEvent, List<MotionKeyframeState>> before = videos.Values
			.Distinct()
			.ToDictionary(video => video, CaptureMotion);
		foreach (EffectsPassAction action in request.Effects.Actions)
		{
			if (!videos.TryGetValue(Path.GetFullPath(action.PlacementPath),
				out VideoEvent video))
				throw new InvalidOperationException(
					"Effects pass cannot find its exact candidate video placement: " +
					action.PlacementPath);
			EffectsPassAction nextOnEvent = request.Effects.Actions
				.Where(item => item.LocalTimeSeconds > action.LocalTimeSeconds + 0.0005 &&
					string.Equals(Path.GetFullPath(item.PlacementPath),
						Path.GetFullPath(action.PlacementPath),
						StringComparison.OrdinalIgnoreCase))
				.OrderBy(item => item.LocalTimeSeconds)
				.FirstOrDefault();
			EditorialEffectRenderResult rendered = renderer.Render(
				video,
				new EditorialEffectRenderAction(
					EditorialEffectRenderKind.ScreenPump,
					action.LocalTimeSeconds,
					action.Intensity,
					action.DurationSeconds,
					nextOnEvent == null
						? (double?)null
						: nextOnEvent.LocalTimeSeconds -
							ScreenPumpShape.AttackSeconds(nextOnEvent.DurationSeconds)));
			results.Add(new PolishActionResult
			{
				ActionId = action.ActionId,
				Outcome = rendered.Rendered
					? PolishActionOutcome.Applied
					: PolishActionOutcome.Rejected,
				Detail = rendered.Reason
			});
			if (!rendered.Rendered)
			{
				foreach (KeyValuePair<VideoEvent, List<MotionKeyframeState>> saved in before)
					RestoreMotion(saved.Key, saved.Value);
				foreach (PolishActionResult prior in results.Where(item =>
					item.Outcome == PolishActionOutcome.Applied))
				{
					prior.Outcome = PolishActionOutcome.Rejected;
					prior.Detail =
						"Rolled back because another action in the atomic effects pass failed.";
				}
				break;
			}
		}
		HashSet<string> reported = new HashSet<string>(
			results.Select(item => item.ActionId), StringComparer.Ordinal);
		foreach (EffectsPassAction action in request.Effects.Actions.Where(item =>
			!reported.Contains(item.ActionId)))
			results.Add(new PolishActionResult
			{
				ActionId = action.ActionId,
				Outcome = PolishActionOutcome.Rejected,
				Detail = "Not attempted because the atomic effects pass was rolled back."
			});
		PolishPassMaterialization materialization = new PolishPassMaterialization
		{
			SessionId = request.Effects.SessionId,
			Pass = PolishPassKind.Effects,
			PlanId = request.Effects.PlanId,
			PlanRevision = request.Effects.Revision,
			PlanSha256 = request.PlanSha256.ToLowerInvariant(),
			Workspace = request.Workspace,
			CompletedUtc = DateTimeOffset.UtcNow,
			FullyApplied = results.All(item =>
				item.Outcome == PolishActionOutcome.Applied),
			Actions = results,
			Snapshot = CandidateSnapshotReader.Read(vegas.Project, request.Workspace)
		};
		PolishPassContractValidator.Validate(materialization);
		return JsonConvert.SerializeObject(new ApplyCandidateEffectsResult
		{
			Materialization = materialization
		});
	}

	private static void ValidateRequest(ApplyCandidateEffectsRequest request)
	{
		request.Workspace?.Validate();
		if (request.Workspace == null || request.Plan == null || request.Effects == null)
			throw new InvalidOperationException(
				"Effects-pass workspace, rough cut, and plan are required.");
		EditPlanDocumentValidator.ValidateAndNormalize(request.Plan);
		PolishPassContractValidator.Validate(request.Effects);
		string documentHash = HashPlan(request.Plan);
		if (!string.Equals(request.Effects.BaseRoughCutSha256, documentHash,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Effects pass does not target the supplied accepted rough cut.");
		string effectHash = ContractHash.Compute(
			JToken.Parse(ContractSerializer.Serialize(request.Effects)));
		if (!string.Equals(request.PlanSha256, effectHash,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Effects-pass hash does not match the exact plan payload.");
		if (!request.Effects.Capabilities.ScreenPump &&
			request.Effects.Actions.Count != 0)
			throw new InvalidOperationException(
				"The requested effects renderer capability is unavailable.");

		Dictionary<string, ClipPlacement> placements = request.Plan.Montage.Placements
			.ToDictionary(item => Path.GetFullPath(item.Clip.FilePath),
				StringComparer.OrdinalIgnoreCase);
		List<ClipPlacement> ordered = request.Plan.Montage.Placements
			.OrderBy(item => item.TimelineStartSeconds)
			.ThenBy(item => item.TimelineEndSeconds)
			.ToList();
		foreach (EffectsPassAction action in request.Effects.Actions)
		{
			if (!placements.TryGetValue(Path.GetFullPath(action.PlacementPath),
				out ClipPlacement placement) ||
				action.TimelineTimeSeconds <
					placement.TimelineStartSeconds - 0.001 ||
				action.TimelineTimeSeconds >
					placement.TimelineEndSeconds + 0.001 ||
				Math.Abs(action.LocalTimeSeconds -
					(action.TimelineTimeSeconds - placement.TimelineStartSeconds)) >
					0.001)
				throw new InvalidOperationException(
					"An effects action is not bound to its canonical placement time.");
			ClipPlacement expectedTarget = ordered.FirstOrDefault(item =>
				action.TimelineTimeSeconds >= item.TimelineStartSeconds - 0.0005 &&
				action.TimelineTimeSeconds < item.TimelineEndSeconds - 0.0005);
			if (expectedTarget == null && ordered.Count != 0 &&
				Math.Abs(action.TimelineTimeSeconds -
					ordered[ordered.Count - 1].TimelineEndSeconds) <= 0.0005)
				expectedTarget = ordered[ordered.Count - 1];
			if (expectedTarget == null ||
				!string.Equals(Path.GetFullPath(expectedTarget.Clip.FilePath),
					Path.GetFullPath(action.PlacementPath),
					StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"An effects action violates the deterministic cut-target rule.");
			bool explicitPump =
				(request.Plan.Montage.EffectTreatments?.Actions ??
					new List<EffectTreatmentAction>())
				.Any(item =>
					item.Type == Core.Domain.Audio.SongAnalysis.EditorialUse.ScreenPump &&
					Math.Abs(item.TimeSeconds - action.TimelineTimeSeconds) <= 0.001 &&
					(string.IsNullOrWhiteSpace(item.EventId) ||
						string.Equals(item.EventId, action.MusicEventId,
							StringComparison.Ordinal)));
			bool reviewedKillPump = request.Plan.Montage.SyncAssignments.Any(item =>
				Math.Abs(item.TimelineTimeSeconds - action.TimelineTimeSeconds) <= 0.001 &&
				string.Equals(item.MusicEventId, action.MusicEventId,
					StringComparison.Ordinal));
			if (!explicitPump && !reviewedKillPump)
				throw new InvalidOperationException(
					"An effects action is not supported by explicit treatment intent " +
					"or a reviewed kill synchronization.");
		}
	}

	private static Dictionary<string, VideoEvent> FindVideoEvents(
		Project project,
		CandidateWorkspaceId workspace)
	{
		List<Track> tracks = CandidateWorkspaceDiscovery.FindOwnedTracks(project, workspace);
		List<VideoEvent> videos = tracks
			.Where(track => track.MediaType == MediaType.Video)
			.SelectMany(track => (IEnumerable<TrackEvent>)track.Events)
			.OfType<VideoEvent>()
			.ToList();
		Dictionary<string, VideoEvent> result =
			new Dictionary<string, VideoEvent>(StringComparer.OrdinalIgnoreCase);
		foreach (VideoEvent video in videos)
		{
			string path = video.ActiveTake?.MediaPath;
			if (string.IsNullOrWhiteSpace(path))
				throw new InvalidOperationException(
					"A candidate video event has no active media path.");
			path = Path.GetFullPath(path);
			if (result.ContainsKey(path))
				throw new InvalidOperationException(
					"Effects pass cannot resolve duplicate candidate media: " + path);
			result.Add(path, video);
		}
		return result;
	}

	private static string HashPlan(EditPlanDocument plan)
	{
		using (SHA256 sha = SHA256.Create())
			return BitConverter.ToString(sha.ComputeHash(
					new UTF8Encoding(false).GetBytes(
						EditPlanDocumentSerializer.SerializePlan(plan))))
				.Replace("-", "").ToLowerInvariant();
	}

	private static List<MotionKeyframeState> CaptureMotion(VideoEvent video) =>
		((IEnumerable<VideoMotionKeyframe>)video.VideoMotion.Keyframes)
			.Select(item => new MotionKeyframeState
			{
				Milliseconds = item.Position.ToMilliseconds(),
				Bounds = CloneBounds(item.Bounds),
				Type = item.Type
			})
			.ToList();

	private static void RestoreMotion(
		VideoEvent video,
		IReadOnlyList<MotionKeyframeState> saved)
	{
		VideoMotionKeyframes keyframes = video.VideoMotion.Keyframes;
		for (int index = keyframes.Count - 1; index >= 0; index--)
		{
			double milliseconds = keyframes[index].Position.ToMilliseconds();
			if (!saved.Any(item =>
				Math.Abs(item.Milliseconds - milliseconds) < 0.5))
				keyframes.Remove(keyframes[index]);
		}
		foreach (MotionKeyframeState state in saved)
		{
			VideoMotionKeyframe keyframe =
				((IEnumerable<VideoMotionKeyframe>)keyframes).FirstOrDefault(item =>
					Math.Abs(item.Position.ToMilliseconds() - state.Milliseconds) < 0.5);
			if (keyframe == null)
			{
				keyframe = new VideoMotionKeyframe(
					Timecode.FromMilliseconds(state.Milliseconds));
				keyframes.Add(keyframe);
			}
			keyframe.Bounds = CloneBounds(state.Bounds);
			keyframe.Type = state.Type;
		}
	}

	private static VideoMotionBounds CloneBounds(VideoMotionBounds bounds) =>
		new VideoMotionBounds(
			new VideoMotionVertex(bounds.TopLeft.X, bounds.TopLeft.Y),
			new VideoMotionVertex(bounds.TopRight.X, bounds.TopRight.Y),
			new VideoMotionVertex(bounds.BottomRight.X, bounds.BottomRight.Y),
			new VideoMotionVertex(bounds.BottomLeft.X, bounds.BottomLeft.Y));

	private sealed class MotionKeyframeState
	{
		public double Milliseconds { get; set; }
		public VideoMotionBounds Bounds { get; set; }
		public VideoKeyframeType Type { get; set; }
	}
}
