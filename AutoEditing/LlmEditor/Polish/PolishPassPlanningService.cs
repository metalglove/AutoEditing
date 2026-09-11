using AutoEditing.Iteration.Contracts.Assembly;
using Core.Domain.Audio.SongAnalysis;
using Core.Domain.Editing;
using Core.Domain.Planning;

namespace AutoEditing.LlmEditor.Polish;

internal sealed class PolishPassPlanningService
{
	private readonly Func<DateTimeOffset> clock;

	public PolishPassPlanningService(Func<DateTimeOffset>? clock = null)
	{
		this.clock = clock ?? (() => DateTimeOffset.UtcNow);
	}

	public EffectsPassPlan PlanEffects(
		string sessionId,
		int revision,
		string roughCutSha256,
		EditPlanDocument acceptedRoughCut,
		PolishRendererCapabilities? capabilities = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
		ArgumentNullException.ThrowIfNull(acceptedRoughCut);
		EditPlanDocumentValidator.ValidateAndNormalize(acceptedRoughCut);
		capabilities ??= new PolishRendererCapabilities();
		EffectsPassPlan result = new()
		{
			SessionId = sessionId,
			PlanId = $"effects-{revision:D4}",
			Revision = revision,
			BaseRoughCutSha256 = roughCutSha256,
			CreatedUtc = clock(),
			Capabilities = capabilities
		};
		if (!capabilities.ScreenPump)
		{
			result.Diagnostics.Add(
				"Screen-pump rendering is unavailable; no visual actions were planned.");
			PolishPassContractValidator.Validate(result);
			return result;
		}

		List<EffectTreatmentAction> requested =
			(acceptedRoughCut.Montage.EffectTreatments?.Actions ??
				new List<EffectTreatmentAction>())
			.Where(item => item != null)
			.OrderBy(item => item.TimeSeconds)
			.ThenBy(item => item.EventId, StringComparer.Ordinal)
			.ToList();
		foreach (EffectTreatmentAction treatment in requested)
		{
			if (treatment.Type != EditorialUse.ScreenPump)
			{
				result.Diagnostics.Add(
					$"{treatment.Type} at {treatment.TimeSeconds:0.###} s was retained as " +
					"unsupported intent and not placed in the executable effects pass.");
				continue;
			}
			AddPump(result, acceptedRoughCut, treatment.EventId, treatment.TimeSeconds,
				treatment.Intensity, treatment.DurationSeconds,
				string.IsNullOrWhiteSpace(treatment.RecipeId)
					? "native.pump.explicit"
					: treatment.RecipeId,
				string.IsNullOrWhiteSpace(treatment.Reason)
					? "Explicit supported screen-pump treatment."
					: treatment.Reason);
		}

		foreach (MontageSyncAssignment assignment in
			acceptedRoughCut.Montage.SyncAssignments
				.OrderBy(item => item.TimelineTimeSeconds)
				.ThenBy(item => item.ClipPath, StringComparer.OrdinalIgnoreCase)
				.ThenBy(item => item.KillIndex))
		{
			if (result.Actions.Any(item =>
				Math.Abs(item.TimelineTimeSeconds - assignment.TimelineTimeSeconds) <=
					0.001 &&
				string.Equals(item.PlacementPath, Path.GetFullPath(assignment.ClipPath),
					StringComparison.OrdinalIgnoreCase)))
				continue;
			AddPump(result, acceptedRoughCut, assignment.MusicEventId,
				assignment.TimelineTimeSeconds, 0.82, 0.24,
				"native.pump.impact",
				$"Supported impact pump for reviewed kill {assignment.KillIndex + 1}.");
		}
		if (result.Actions.Count == 0)
			result.Diagnostics.Add(
				"No reviewed kill synchronization or explicit supported pump was available.");
		PolishPassContractValidator.Validate(result);
		return result;
	}

	public AudioPassPlan PlanAudio(
		string sessionId,
		int revision,
		string roughCutSha256,
		string acceptedEffectsSha256,
		string songPath,
		EditPlanDocument acceptedRoughCut,
		PolishRendererCapabilities? capabilities = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
		ArgumentNullException.ThrowIfNull(acceptedRoughCut);
		EditPlanDocumentValidator.ValidateAndNormalize(acceptedRoughCut);
		capabilities ??= new PolishRendererCapabilities();
		AudioPassPlan result = new()
		{
			SessionId = sessionId,
			PlanId = $"audio-{revision:D4}",
			Revision = revision,
			BaseRoughCutSha256 = roughCutSha256,
			EffectsPassSha256 = acceptedEffectsSha256,
			CreatedUtc = clock(),
			Capabilities = capabilities
		};
		if (capabilities.SongTrack)
		{
			result.Song = new AudioPassSongAction
			{
				SongPath = Path.GetFullPath(songPath),
				TimelineStartSeconds = 0,
				TrackGain = 0.5
			};
		}
		else result.Diagnostics.Add("Song-track rendering is unavailable.");

		if (capabilities.ReviewedGunHitSfx)
		{
			foreach (ClipPlacement placement in acceptedRoughCut.Montage.Placements
				.OrderBy(item => item.TimelineStartSeconds)
				.ThenBy(item => item.Clip.FilePath, StringComparer.OrdinalIgnoreCase))
			{
				List<TimelineShotEvent> kills = placement.TimelineShotEvents
					.Where(item => item.SourceEvent.IsConfirmedKill)
					.OrderBy(item => item.TimelineTimeSeconds)
					.ToList();
				for (int index = 0; index < kills.Count; index++)
				{
					TimelineShotEvent kill = kills[index];
					result.Sfx.Add(new AudioPassSfxAction
					{
						ActionId = StableActionId("hit", placement.Clip.FilePath, index,
							kill.TimelineTimeSeconds),
						PlacementPath = Path.GetFullPath(placement.Clip.FilePath),
						ConfirmedKillIndex = index,
						ConfirmationTimeSeconds = kill.TimelineTimeSeconds,
						Gun = kill.SourceEvent.Gun ?? placement.Clip.Gun,
						Outcome = kill.SourceEvent.Outcome,
						PreferredTemplateId = kill.SourceEvent.TemplateId ?? "",
						TrackGain = 0.6
					});
				}
			}
			if (result.Sfx.Count == 0)
				result.Diagnostics.Add("No reviewed confirmed kills were available for SFX.");
		}
		else result.Diagnostics.Add("Reviewed gun/hit SFX rendering is unavailable.");

		PolishPassContractValidator.Validate(result);
		return result;
	}

	private static void AddPump(
		EffectsPassPlan result,
		EditPlanDocument plan,
		string musicEventId,
		double time,
		double intensity,
		double duration,
		string recipe,
		string reason)
	{
		List<ClipPlacement> ordered = plan.Montage.Placements
			.OrderBy(item => item.TimelineStartSeconds)
			.ThenBy(item => item.TimelineEndSeconds)
			.ToList();
		ClipPlacement? placement = ordered.FirstOrDefault(item =>
			time >= item.TimelineStartSeconds - 0.0005 &&
			time < item.TimelineEndSeconds - 0.0005);
		if (placement == null && ordered.Count != 0 &&
			Math.Abs(time - ordered[^1].TimelineEndSeconds) <= 0.0005)
			placement = ordered[^1];
		if (placement == null)
		{
			result.Diagnostics.Add(
				$"Screen pump at {time:0.###} s was omitted because it has no video placement.");
			return;
		}
		double local = Math.Max(0, time - placement.TimelineStartSeconds);
		result.Actions.Add(new EffectsPassAction
		{
			ActionId = StableActionId("pump", placement.Clip.FilePath,
				result.Actions.Count, time),
			PlacementPath = Path.GetFullPath(placement.Clip.FilePath),
			MusicEventId = musicEventId ?? "",
			TimelineTimeSeconds = time,
			LocalTimeSeconds = local,
			Intensity = Math.Max(0, Math.Min(1, intensity)),
			DurationSeconds = duration > 0 ? duration : 0.24,
			RecipeId = recipe,
			Reason = reason
		});
	}

	private static string StableActionId(string prefix, string path, int index, double time)
	{
		string name = Path.GetFileNameWithoutExtension(path)
			.Replace(' ', '-').Replace('_', '-').ToLowerInvariant();
		string safe = new(name.Where(character =>
			char.IsLetterOrDigit(character) || character == '-').ToArray());
		if (safe.Length > 24) safe = safe[..24];
		long milliseconds = (long)Math.Round(time * 1000);
		return $"{prefix}-{safe}-{index:D3}-{milliseconds:D8}";
	}
}
