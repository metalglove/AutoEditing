using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Domain.Audio;
using Core.Domain.Clip;
using Core.Domain.Logging;
using ScriptPortal.Vegas;

namespace Core.Domain.Editing;

public class MontageOrchestrator
{
	public sealed class PreparedMontage
	{
		public List<ClipPlacement> Placements { get; set; }

		public BeatGrid Beats { get; set; }
	}

	public void CreateMontage(Vegas vegas, string clipsFolder, string songPath)
	{
		List<Core.Domain.Clip.Clip> reviewedClips = LoadReviewedClips(clipsFolder);
		CreateMontage(vegas, reviewedClips, songPath, applyEffects: true);
	}

	public void CreateMontage(Vegas vegas, List<Core.Domain.Clip.Clip> reviewedClips, string songPath, bool applyEffects)
	{
		PreparedMontage prepared = PrepareMontage(reviewedClips, songPath);
		BuildPreparedMontage(vegas, prepared, songPath, applyEffects);
	}

	public PreparedMontage PrepareMontage(List<Core.Domain.Clip.Clip> reviewedClips, string songPath)
	{
		MonoAudio monoAudio = AudioLoader.LoadMono(songPath);
		BeatGrid beats = new BeatDetector().DetectBeats(monoAudio);
		ShotDetectionConfig shotDetection = ConfigurationManager.GetShotDetection();
		MontagePlanner montagePlanner = new MontagePlanner(shotDetection.PreRollSeconds, shotDetection.PostRollSeconds, shotDetection.MinVelocity, shotDetection.MaxVelocity);
		List<ClipPlacement> placements = montagePlanner.PlanMontage(reviewedClips, beats, monoAudio.DurationSeconds);
		return new PreparedMontage
		{
			Placements = placements,
			Beats = beats
		};
	}

	public void BuildPreparedMontage(Vegas vegas, PreparedMontage prepared, string songPath, bool applyEffects)
	{
		TimelineBuilder timelineBuilder = new TimelineBuilder();
		Dictionary<ClipPlacement, VideoEvent> videoEvents = timelineBuilder.BuildTimeline(vegas, prepared.Placements, songPath);
		if (applyEffects)
		{
			ApplyEffects(videoEvents);
		}
		timelineBuilder.AddMontageMarkers(vegas, prepared.Placements, prepared.Beats);
		Logger.Log("Montage created from reviewed markers; placed " + prepared.Placements.Count + " clips.");
	}

	public void CreateQuickMontage(Vegas vegas, string clipsFolder, string songPath, bool skipValidation = false)
	{
		CreateMontage(vegas, clipsFolder, songPath);
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
