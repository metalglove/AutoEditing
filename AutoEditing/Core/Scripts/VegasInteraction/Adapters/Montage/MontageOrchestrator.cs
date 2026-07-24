using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Domain;
using Core.Domain.Audio;
using Core.Domain.Clip;
using Core.Domain.Logging;
using Core.Domain.Editing;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class MontageOrchestrator
{
	public void BuildPreparedMontage(Vegas vegas, PreparedMontage prepared, string songPath, bool applyEffects)
	{
		TimelineBuilder timelineBuilder = new TimelineBuilder();
		Dictionary<ClipPlacement, VideoEvent> videoEvents = timelineBuilder.BuildTimeline(vegas, prepared.Placements);
		ShotDetectionConfig shotDetection = ConfigurationManager.GetShotDetection();
		SfxTemplateCatalog sfxCatalog = SfxTemplateCatalog.Load(shotDetection.SfxRoot);
		new MontageAudioBuilder().Build(vegas.Project, prepared.Placements, songPath, shotDetection.SfxRoot, sfxCatalog);
		if (applyEffects)
		{
			ApplyEffects(videoEvents, prepared);
		}
		timelineBuilder.AddMontageMarkers(vegas, prepared);
		Logger.Log("Montage created from reviewed markers; placed " + prepared.Placements.Count + " clips.");
	}

	private static List<Core.Domain.Clip.Clip> LoadReviewedClips(string clipsFolder)
	{
		ShotDetectionConfig shotDetection = ConfigurationManager.GetShotDetection();
		SfxTemplateCatalog sfxTemplateCatalog = SfxTemplateCatalog.Load(shotDetection.SfxRoot);
		ShotAnalysisSidecar shotAnalysisSidecar = ShotAnalysisSidecar.Load(clipsFolder);
		List<Core.Domain.Clip.Clip> list = new ClipParser().ParseAllClips(clipsFolder);
		List<Core.Domain.Clip.Clip> list2 = new List<Core.Domain.Clip.Clip>();
		foreach (Core.Domain.Clip.Clip item in list)
		{
			if (sfxTemplateCatalog.ForGun(item.Gun).Count == 0)
			{
				Logger.Log("Excluded unsupported gun: " + item.Gun + " (" + Path.GetFileName(item.FilePath) + ")");
				continue;
			}
			sfxTemplateCatalog.ValidateForGun(shotDetection.SfxRoot, item.Gun);
			ClipShotAnalysis clipShotAnalysis = shotAnalysisSidecar.FindValid(item.FilePath, sfxTemplateCatalog.RelevantFingerprint(item.Gun));
			if (clipShotAnalysis == null)
			{
				throw new InvalidOperationException("Missing or stale reviewed analysis: " + item.FilePath);
			}
			if (clipShotAnalysis.Events.Any((ShotEvent e) => e.ReviewState != ShotReviewState.Reviewed))
			{
				throw new InvalidOperationException("Unreviewed Candidate markers remain: " + item.FilePath);
			}
			item.ShotEvents = clipShotAnalysis.Events;
			item.DurationSeconds = AudioLoader.GetDurationSeconds(item.FilePath);
			if (item.ConfirmedKills.Count == 0)
			{
				throw new InvalidOperationException("No reviewed Hit/Headshot marker: " + item.FilePath);
			}
			list2.Add(item);
		}
		if (list2.Count == 0)
		{
			throw new InvalidOperationException("No supported, reviewed clips are available.");
		}
		return list2;
	}

	private static void ApplyEffects(Dictionary<ClipPlacement, VideoEvent> videoEvents, PreparedMontage prepared)
	{
		EffectsApplier effectsApplier = new EffectsApplier();
		foreach (KeyValuePair<ClipPlacement, VideoEvent> videoEvent in videoEvents)
		{
			effectsApplier.ApplyVelocityEnvelope(videoEvent.Value, videoEvent.Key.SpeedProfile);
		}
		VegasEditorialEffectRenderer renderer = new VegasEditorialEffectRenderer();
		List<EffectTreatmentAction> treatments = prepared.EffectTreatments?.Actions ?? new List<EffectTreatmentAction>();
		int rendered = 0;
		int noClip = 0;
		int unsupportedOrRejected = 0;
		foreach (EffectTreatmentAction treatment in treatments)
		{
			KeyValuePair<ClipPlacement, VideoEvent>? target = FindEffectTarget(videoEvents, treatment.TimeSeconds);
			if (!target.HasValue)
			{
				noClip++;
				Logger.Log("Effect " + treatment.Type + " at " + treatment.TimeSeconds.ToString("0.000") + "s has no placed clip and was preserved as a marker only.");
				continue;
			}
			EditorialEffectRenderKind? kind = RenderKind(treatment.Type);
			if (!kind.HasValue)
			{
				unsupportedOrRejected++;
				Logger.Log("Effect " + treatment.Type + " at " + treatment.TimeSeconds.ToString("0.000") + "s has no renderer yet.");
				continue;
			}
			double localSeconds = Math.Max(0, treatment.TimeSeconds - target.Value.Key.TimelineStartSeconds);
			EditorialEffectRenderResult renderResult = renderer.Render(target.Value.Value, new EditorialEffectRenderAction(kind.Value, localSeconds, treatment.Intensity, treatment.DurationSeconds));
			if (renderResult.Rendered) rendered++;
			else unsupportedOrRejected++;
			Logger.Log((renderResult.Rendered ? "Rendered " : "Skipped ") + treatment.Type + " at " + treatment.TimeSeconds.ToString("0.000") + "s: " + renderResult.Reason);
		}
		Logger.Log(
			"Editorial effects summary: " + treatments.Count + " planned, " +
			rendered + " rendered, " + noClip + " without a clip, " +
			unsupportedOrRejected + " unsupported or rejected.");
	}

	private static KeyValuePair<ClipPlacement, VideoEvent>? FindEffectTarget(
		Dictionary<ClipPlacement, VideoEvent> videoEvents,
		double timelineSeconds)
	{
		const double epsilon = 0.0005;
		List<KeyValuePair<ClipPlacement, VideoEvent>> ordered = videoEvents
			.OrderBy(item => item.Key.TimelineStartSeconds)
			.ThenBy(item => item.Key.TimelineEndSeconds)
			.ToList();

		// Timeline events are half-open. A treatment on a cut belongs to the
		// incoming clip, where VEGAS can render both the peak and its recovery.
		for (int i = 0; i < ordered.Count; i++)
		{
			ClipPlacement placement = ordered[i].Key;
			bool startsHereOrEarlier = timelineSeconds + epsilon >= placement.TimelineStartSeconds;
			bool beforeEnd = timelineSeconds < placement.TimelineEndSeconds - epsilon;
			if (startsHereOrEarlier && beforeEnd) return ordered[i];
		}

		// Preserve a treatment at the absolute montage end on the outgoing clip.
		if (ordered.Count > 0)
		{
			KeyValuePair<ClipPlacement, VideoEvent> last = ordered[ordered.Count - 1];
			if (Math.Abs(timelineSeconds - last.Key.TimelineEndSeconds) <= epsilon)
				return last;
		}
		return null;
	}

	private static EditorialEffectRenderKind? RenderKind(Core.Domain.Audio.SongAnalysis.EditorialUse use)
	{
		if (use == Core.Domain.Audio.SongAnalysis.EditorialUse.ScreenPump) return EditorialEffectRenderKind.ScreenPump;
		if (use == Core.Domain.Audio.SongAnalysis.EditorialUse.Flash) return EditorialEffectRenderKind.WhiteFlash;
		if (use == Core.Domain.Audio.SongAnalysis.EditorialUse.Shake) return EditorialEffectRenderKind.Shake;
		if (use == Core.Domain.Audio.SongAnalysis.EditorialUse.CutOrTransition || use == Core.Domain.Audio.SongAnalysis.EditorialUse.CinematicTransition) return EditorialEffectRenderKind.Transition;
		if (use == Core.Domain.Audio.SongAnalysis.EditorialUse.TitleReveal) return EditorialEffectRenderKind.TitleReveal;
		return null;
	}
}
