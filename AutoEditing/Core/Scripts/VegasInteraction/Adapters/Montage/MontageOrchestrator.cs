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
			ApplyEffects(videoEvents);
		}
		timelineBuilder.AddMontageMarkers(vegas, prepared.Placements, prepared.Beats);
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

	private static void ApplyEffects(Dictionary<ClipPlacement, VideoEvent> videoEvents)
	{
		EffectsApplier effectsApplier = new EffectsApplier();
		foreach (KeyValuePair<ClipPlacement, VideoEvent> videoEvent in videoEvents)
		{
			effectsApplier.ApplyVelocityEnvelope(videoEvent.Value, videoEvent.Key.SpeedProfile);
			effectsApplier.AddNameTag(videoEvent.Value, videoEvent.Key.Clip.PlayerName + " - " + videoEvent.Key.Clip.ClipType);
			effectsApplier.ApplyColorCorrection(videoEvent.Value);
		}
	}
}
