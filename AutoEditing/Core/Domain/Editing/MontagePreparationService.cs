using System.Collections.Generic;
using Core.Domain;
using Core.Domain.Audio;
using Core.Domain.Clip;

namespace Core.Domain.Editing;

public sealed class MontagePreparationService
{
	public PreparedMontage Prepare(List<Core.Domain.Clip.Clip> reviewedClips, string songPath)
	{
		MonoAudio audio = AudioLoader.LoadMono(songPath);
		BeatGrid beats = new BeatDetector().DetectBeats(audio);
		ShotDetectionConfig config = ConfigurationManager.GetShotDetection();
		MontagePlanner planner = new MontagePlanner(config.PreRollSeconds, config.PostRollSeconds, config.MinVelocity, config.MaxVelocity);
		return new PreparedMontage { Placements = planner.PlanMontage(reviewedClips, beats, audio.DurationSeconds), Beats = beats };
	}
}
