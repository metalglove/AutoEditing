using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain.Audio;
using Core.Domain.Editing;
using Core.Domain.Logging;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class MontageAudioBuilder
{
	private const float DefaultSongVolume = 0.5f;

	private const float DefaultGunSfxVolume = 0.6f;

	public void Build(Project project, List<ClipPlacement> placements, string songPath, string sfxRoot, SfxTemplateCatalog catalog)
	{
		AddSong(project, songPath);
		AddHitSounds(project, placements, sfxRoot, catalog);
	}

	private static void AddSong(Project project, string songPath)
	{
		Media media = project.MediaPool.AddMedia(songPath);
		if (media == (Media)null) throw new InvalidOperationException("Could not import song file.");
		AudioStream stream = media.GetAudioStreamByIndex(0);
		if (stream == (AudioStream)null) throw new InvalidOperationException("Song has no audio stream.");
		AudioTrack track = project.AddAudioTrack();
		((Track)track).Name = "AE|Montage Song";
		track.Volume = DefaultSongVolume;
		AudioEvent audioEvent = new AudioEvent(Timecode.FromSeconds(0.0), media.Length);
		((BaseList<TrackEvent>)(object)((Track)track).Events).Add((TrackEvent)(object)audioEvent);
		((BaseList<Take>)(object)((TrackEvent)audioEvent).Takes).Add(new Take((MediaStream)(object)stream));
	}

	private static void AddHitSounds(Project project, IEnumerable<ClipPlacement> placements, string sfxRoot, SfxTemplateCatalog catalog)
	{
		List<AudioTrack> tracks = new List<AudioTrack>();
		List<double> trackEndTimes = new List<double>();
		foreach (ClipPlacement placement in placements)
		{
			List<TimelineShotEvent> kills = placement.TimelineShotEvents.Where((TimelineShotEvent item) => item.SourceEvent.IsConfirmedKill).OrderBy((TimelineShotEvent item) => item.TimelineTimeSeconds).ToList();
			for (int killIndex = 0; killIndex < kills.Count; killIndex++)
			{
				TimelineShotEvent shot = kills[killIndex];
				SfxTemplate template = SelectTemplate(catalog, shot.SourceEvent, placement.Clip.Gun);
				if (template == null)
				{
					Logger.Log("No hit SFX template for " + (shot.SourceEvent.Gun ?? placement.Clip.Gun) + " at " + shot.TimelineTimeSeconds.ToString("F2") + "s.");
					continue;
				}
				AddTemplate(project, tracks, trackEndTimes, template, sfxRoot, shot.TimelineTimeSeconds, killIndex, kills.Count);
			}
		}
	}

	private static SfxTemplate SelectTemplate(SfxTemplateCatalog catalog, ShotEvent shot, string clipGun)
	{
		IReadOnlyList<SfxTemplate> templates = catalog.ForGun(shot.Gun ?? clipGun);
		SfxTemplate exact = templates.FirstOrDefault((SfxTemplate item) => string.Equals(item.Id, shot.TemplateId, StringComparison.OrdinalIgnoreCase));
		if (exact != null) return exact;
		SfxTemplate outcome = templates.FirstOrDefault((SfxTemplate item) => item.Type == shot.Outcome);
		if (outcome != null) return outcome;
		return templates.FirstOrDefault((SfxTemplate item) => item.Type == ShotOutcome.Hit || item.Type == ShotOutcome.Headshot)
			?? templates.FirstOrDefault((SfxTemplate item) => item.Type == ShotOutcome.Shot)
			?? templates.FirstOrDefault();
	}

	private static void AddTemplate(Project project, List<AudioTrack> tracks, List<double> trackEndTimes, SfxTemplate template, string sfxRoot, double confirmationTimelineSeconds, int killIndex, int killCount)
	{
		Media media = project.MediaPool.AddMedia(template.FullPath(sfxRoot));
		if (media == (Media)null) throw new InvalidOperationException("Could not import hit SFX: " + template.RelativePath);
		AudioStream stream = media.GetAudioStreamByIndex(0);
		if (stream == (AudioStream)null) throw new InvalidOperationException("Hit SFX has no audio stream: " + template.RelativePath);
		double desiredStart = confirmationTimelineSeconds - template.ConfirmationOffsetSeconds.GetValueOrDefault();
		double takeOffsetSeconds = Math.Max(0.0, -desiredStart);
		double eventStartSeconds = Math.Max(0.0, desiredStart);
		double mediaLengthSeconds = media.Length.ToMilliseconds() / 1000.0;
		GetFadeTreatment(killIndex, killCount, out double tailSeconds, out double fadeSeconds, out CurveType curve);
		double availableLengthSeconds = mediaLengthSeconds - takeOffsetSeconds;
		double desiredEndSeconds = confirmationTimelineSeconds + tailSeconds;
		double eventLengthSeconds = Math.Min(availableLengthSeconds, desiredEndSeconds - eventStartSeconds);
		if (eventLengthSeconds <= 0.0) return;
		int trackIndex = FindAvailableTrack(trackEndTimes, eventStartSeconds);
		if (trackIndex < 0)
		{
			AudioTrack newTrack = project.AddAudioTrack();
			tracks.Add(newTrack);
			trackEndTimes.Add(0.0);
			trackIndex = tracks.Count - 1;
			((Track)newTrack).Name = "AE|Montage Gun SFX " + (trackIndex + 1);
			newTrack.Volume = DefaultGunSfxVolume;
		}
		AudioTrack track = tracks[trackIndex];
		AudioEvent audioEvent = new AudioEvent(Timecode.FromSeconds(eventStartSeconds), Timecode.FromSeconds(eventLengthSeconds));
		((BaseList<TrackEvent>)(object)((Track)track).Events).Add((TrackEvent)(object)audioEvent);
		Take take = new Take((MediaStream)(object)stream);
		((BaseList<Take>)(object)((TrackEvent)audioEvent).Takes).Add(take);
		take.Offset = Timecode.FromSeconds(takeOffsetSeconds);
		audioEvent.FadeOut.Length = Timecode.FromSeconds(Math.Min(fadeSeconds, eventLengthSeconds));
		audioEvent.FadeOut.Curve = curve;
		trackEndTimes[trackIndex] = eventStartSeconds + eventLengthSeconds;
	}

	private static void GetFadeTreatment(int killIndex, int killCount, out double tailSeconds, out double fadeSeconds, out CurveType curve)
	{
		if (killCount <= 1)
		{
			tailSeconds = 0.65;
			fadeSeconds = 0.35;
			curve = CurveType.Smooth;
			return;
		}
		if (killIndex == killCount - 1)
		{
			tailSeconds = 0.7;
			fadeSeconds = 0.38;
			curve = CurveType.Smooth;
			return;
		}
		if (killIndex == 0)
		{
			tailSeconds = 0.55;
			fadeSeconds = 0.28;
			curve = CurveType.Fast;
			return;
		}
		tailSeconds = 0.4;
		fadeSeconds = 0.22;
		curve = CurveType.Sharp;
	}

	private static int FindAvailableTrack(IReadOnlyList<double> trackEndTimes, double eventStartSeconds)
	{
		for (int index = 0; index < trackEndTimes.Count; index++)
		{
			if (trackEndTimes[index] <= eventStartSeconds + 0.000001) return index;
		}
		return -1;
	}
}
