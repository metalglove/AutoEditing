using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Core.Domain;
using Core.Domain.Audio;
using Core.Domain.Clip;
using Core.Domain.Logging;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class ShotReviewWorkflow
{
	public sealed class AnalysisItem
	{
		public int Index { get; set; }

		public Core.Domain.Clip.Clip Clip { get; set; }

		public List<ShotEvent> Events { get; set; }
	}

	public sealed class AnalysisBatch
	{
		public List<AnalysisItem> Items { get; set; } = new List<AnalysisItem>();
	}

	private const string Prefix = "AE|";

	public void CalibrateSfx(string sfxRoot)
	{
		SfxTemplateCatalog sfxTemplateCatalog = SfxTemplateCatalog.Discover(sfxRoot);
		foreach (SfxTemplate template in sfxTemplateCatalog.Templates)
		{
			MonoAudio audio = AudioLoader.LoadMono(template.FullPath(sfxRoot));
			double value = FindFirstAudibleOnset(audio);
			template.MuzzleOffsetSeconds = value;
			template.ConfirmationOffsetSeconds = value;
		}
		sfxTemplateCatalog.Save(sfxRoot);
		sfxTemplateCatalog.Validate(sfxRoot);
		Logger.Log("Indexed " + sfxTemplateCatalog.Templates.Count + " clean templates. Muzzle and sync anchors use the same first audible onset.");
	}

	public void SaveCalibration(string sfxRoot)
	{
		SfxTemplateCatalog sfxTemplateCatalog = SfxTemplateCatalog.Load(sfxRoot);
		sfxTemplateCatalog.Validate(sfxRoot);
		Logger.Log("SFX index is valid. Clean templates use their first audible onset for both anchors.");
	}

	private static double FindFirstAudibleOnset(MonoAudio audio)
	{
		if (audio == null || audio.Samples == null || audio.Samples.Length == 0)
		{
			throw new InvalidOperationException("Cannot index an empty SFX template.");
		}
		int num = Math.Max(1, audio.SampleRate / 500);
		double num2 = 0.0;
		for (int i = 0; i + num <= audio.Samples.Length; i += num)
		{
			num2 = Math.Max(num2, WindowRms(audio.Samples, i, num));
		}
		double num3 = Math.Max(0.002, num2 * 0.08);
		for (int j = 0; j + num <= audio.Samples.Length; j += num)
		{
			if (WindowRms(audio.Samples, j, num) >= num3)
			{
				return (double)j / (double)audio.SampleRate;
			}
		}
		return 0.0;
	}

	private static double WindowRms(float[] samples, int start, int length)
	{
		double num = 0.0;
		for (int i = 0; i < length; i++)
		{
			double num2 = samples[start + i];
			num += num2 * num2;
		}
		return Math.Sqrt(num / (double)length);
	}

	public AnalysisBatch AnalyzeClipAudio(string clipsFolder, string sfxRoot, Action<int, int, string> reportProgress, CancellationToken cancellationToken)
	{
		SfxTemplateCatalog sfxTemplateCatalog = SfxTemplateCatalog.Load(sfxRoot);
		List<Core.Domain.Clip.Clip> list = new ClipParser().ParseAllClips(clipsFolder);
		ClipSyncLibrary clipSyncLibrary = ClipSyncLibrary.Load();
		ShotDetector shotDetector = new ShotDetector();
		AnalysisBatch analysisBatch = new AnalysisBatch();
		for (int i = 0; i < list.Count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Core.Domain.Clip.Clip clip = list[i];
			reportProgress?.Invoke(i, list.Count, Path.GetFileName(clip.FilePath));
			if (sfxTemplateCatalog.ForGun(clip.Gun).Count == 0)
			{
				Logger.Log("Excluded unsupported gun: " + clip.Gun + " (" + Path.GetFileName(clip.FilePath) + ")");
				continue;
			}
			sfxTemplateCatalog.ValidateForGun(sfxRoot, clip.Gun);
			string templateFingerprint = sfxTemplateCatalog.RelevantFingerprint(clip.Gun);
			ClipSyncEntry existingEntry = clipSyncLibrary.Find(clip.FilePath);
			if (existingEntry != null && existingEntry.State == ClipSyncState.Ready)
			{
				Logger.Log(Path.GetFileName(clip.FilePath) + ": reused ready sync points from the central library; no timeline review needed.");
				continue;
			}
			bool candidateReusable = existingEntry != null && string.Equals(existingEntry.TemplateFingerprint, templateFingerprint, StringComparison.Ordinal);
			MonoAudio monoAudio = AudioLoader.LoadMono(clip.FilePath);
			clip.DurationSeconds = monoAudio.DurationSeconds;
			List<ShotEvent> list2 = (candidateReusable ? existingEntry.Events : shotDetector.DetectShots(monoAudio, clip.Gun, sfxTemplateCatalog, sfxRoot));
			clipSyncLibrary.Put(clip, templateFingerprint, list2, existingEntry == null ? ClipSyncState.Candidate : existingEntry.State);
			analysisBatch.Items.Add(new AnalysisItem
			{
				Index = i,
				Clip = clip,
				Events = list2
			});
			int num = list2.Count((ShotEvent e) => e.Confidence >= 0.82);
			Logger.Log(Path.GetFileName(clip.FilePath) + ": " + num + " high-confidence and " + (list2.Count - num) + " review-candidate shot matches.");
		}
		clipSyncLibrary.Save();
		reportProgress?.Invoke(list.Count, list.Count, "Analysis complete");
		return analysisBatch;
	}

	public void LayOutAnalysis(Vegas vegas, AnalysisBatch batch, Action<int, int, string> reportProgress, CancellationToken cancellationToken)
	{
		Project project = vegas.Project;
		Logger.Log("VEGAS layout: removing previous AE objects.");
		RunVegasStage("remove previous AE objects", delegate
		{
			CleanupGenerated(project);
		});
		VideoTrack video = null;
		AudioTrack audio = null;
		RunVegasStage("create analysis video track", delegate
		{
			video = project.AddVideoTrack();
			((Track)video).Name = "AE|Analysis Video";
		});
		RunVegasStage("create analysis audio track", delegate
		{
			audio = project.AddAudioTrack();
			((Track)audio).Name = "AE|Analysis Audio";
		});
		Logger.Log("VEGAS layout: analysis tracks created.");
		double num = 0.0;
		try
		{
			for (int num2 = 0; num2 < batch.Items.Count; num2++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				AnalysisItem item = batch.Items[num2];
				Core.Domain.Clip.Clip clip = item.Clip;
				reportProgress?.Invoke(num2, batch.Items.Count, "Placing " + Path.GetFileName(clip.FilePath));
				Logger.Log("VEGAS layout: placing " + Path.GetFileName(clip.FilePath) + ".");
				double itemCursor = num;
				PlaceFullClip(project, video, audio, clip, itemCursor);
				RunVegasStage("add review region for " + Path.GetFileName(clip.FilePath), delegate
				{
					//IL_0058: Unknown result type (might be due to invalid IL or missing references)
					//IL_0062: Expected O, but got Unknown
					((BaseList<Region>)(object)project.Regions).Add(new Region(Timecode.FromSeconds(itemCursor), Timecode.FromSeconds(clip.DurationSeconds), "AE|CLIP|" + item.Index + "|" + clip.FilePath));
				});
				RunVegasStage("add review markers for " + Path.GetFileName(clip.FilePath), delegate
				{
					//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
					//IL_00f5: Expected O, but got Unknown
					foreach (ShotEvent @event in item.Events)
					{
						string text = @event.Outcome.ToString();
						string text2 = ((@event.Confidence >= 0.82) ? "HighConfidence-" : "Candidate-");
						string text3 = @event.Confidence.ToString("P1", CultureInfo.InvariantCulture);
						string source = text3 + ";" + (@event.TemplateId ?? string.Empty);
						string gun = string.IsNullOrWhiteSpace(@event.Gun) ? clip.Gun : @event.Gun;
						((BaseList<Marker>)(object)project.Markers).Add(new Marker(Timecode.FromSeconds(itemCursor + @event.SourceConfirmationTimeSeconds), BuildMarkerLabel(text2 + text, item.Index, source, gun)));
					}
				});
				num += clip.DurationSeconds + 1.0;
			}
		}
		catch (OperationCanceledException)
		{
			CleanupGenerated(project);
			vegas.UpdateUI();
			throw;
		}
		RunVegasStage("refresh VEGAS UI", (Action)vegas.UpdateUI);
		Logger.Log("Clip analysis laid out. Candidate markers must be converted to Hit, Headshot, or Miss.");
	}

	public void LayOutSingleClip(Vegas vegas, AnalysisItem item)
	{
		Project project = vegas.Project;
		VideoTrack video = ((IEnumerable<Track>)project.Tracks).OfType<VideoTrack>().FirstOrDefault((VideoTrack track) => ((Track)track).Name == "AE|Analysis Video");
		AudioTrack audio = ((IEnumerable<Track>)project.Tracks).OfType<AudioTrack>().FirstOrDefault((AudioTrack track) => ((Track)track).Name == "AE|Analysis Audio");
		if (video == null)
		{
			video = project.AddVideoTrack();
			((Track)video).Name = "AE|Analysis Video";
		}
		if (audio == null)
		{
			audio = project.AddAudioTrack();
			((Track)audio).Name = "AE|Analysis Audio";
		}
		double cursor = NextAnalysisCursor(project);
		PlaceFullClip(project, video, audio, item.Clip, cursor);
		((BaseList<Region>)(object)project.Regions).Add(new Region(Timecode.FromSeconds(cursor), Timecode.FromSeconds(item.Clip.DurationSeconds), "AE|CLIP|" + item.Index + "|" + item.Clip.FilePath));
		foreach (ShotEvent shot in item.Events)
		{
			string outcome = shot.Outcome.ToString();
			if (shot.ReviewState != ShotReviewState.Reviewed)
			{
				outcome = (shot.Confidence >= 0.82 ? "HighConfidence-" : "Candidate-") + outcome;
			}
			string source = shot.Confidence.ToString("P1", CultureInfo.InvariantCulture) + ";" + (shot.TemplateId ?? string.Empty);
			string gun = string.IsNullOrWhiteSpace(shot.Gun) ? item.Clip.Gun : shot.Gun;
			((BaseList<Marker>)(object)project.Markers).Add(new Marker(Timecode.FromSeconds(cursor + shot.SourceConfirmationTimeSeconds), BuildMarkerLabel(outcome, item.Index, source, gun)));
		}
		vegas.UpdateUI();
	}

	private static void RunVegasStage(string stage, Action action)
	{
		try
		{
			action();
		}
		catch (Exception innerException)
		{
			throw new InvalidOperationException("VEGAS layout failed during: " + stage + ".", innerException);
		}
	}

	public void MarkAtCursor(Vegas vegas, ShotOutcome outcome)
	{
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Expected O, but got Unknown
		Region val = ((IEnumerable<Region>)vegas.Project.Regions).FirstOrDefault((Region r) => ((Marker)r).Label != null && ((Marker)r).Label.StartsWith("AE|CLIP|") && Seconds(vegas.Transport.CursorPosition) >= Seconds(((Marker)r).Position) && Seconds(vegas.Transport.CursorPosition) <= Seconds(((Marker)r).Position) + Seconds(r.Length));
		if (val == (Region)null)
		{
			throw new InvalidOperationException("Cursor is not inside an AE clip region.");
		}
		string[] array = ((Marker)val).Label.Split('|');
		string gun = new ClipParser().ParseClip(array[3])?.Gun;
		((BaseList<Marker>)(object)vegas.Project.Markers).Add(new Marker(vegas.Transport.CursorPosition, BuildMarkerLabel(outcome.ToString(), int.Parse(array[2], CultureInfo.InvariantCulture), "manual", gun)));
	}

	public List<Core.Domain.Clip.Clip> CaptureReviewedMarkers(Vegas vegas, string clipsFolder, string sfxRoot)
	{
		List<Core.Domain.Clip.Clip> list = new ClipParser().ParseAllClips(clipsFolder);
		foreach (Region item in ((IEnumerable<Region>)vegas.Project.Regions).Where((Region r) => ((Marker)r).Label != null && ((Marker)r).Label.StartsWith("AE|CLIP|")).ToList())
		{
			string[] array = ((Marker)item).Label.Split(new char[1] { '|' }, 4);
			int result;
			if (array.Length < 4 || !int.TryParse(array[2], out result) || result < 0 || result >= list.Count)
			{
				continue;
			}
			Core.Domain.Clip.Clip clip = list[result];
			double start = Seconds(((Marker)item).Position);
			double end = start + Seconds(item.Length);
			List<ShotEvent> list2 = new List<ShotEvent>();
			foreach (Marker item2 in ((IEnumerable<Marker>)vegas.Project.Markers).Where((Marker m) => Seconds(m.Position) >= start && Seconds(m.Position) <= end).ToList())
			{
				ShotOutcome outcome;
				string gun;
				if (TryReviewedOutcome(item2.Label, result, clip.Gun, out outcome, out gun))
				{
					list2.Add(ShotEvent.Reviewed(Seconds(item2.Position) - start, outcome, gun));
				}
			}
			clip.DurationSeconds = Seconds(item.Length);
			clip.ShotEvents = list2.OrderBy((ShotEvent e) => e.SourceConfirmationTimeSeconds).ToList();
		}
		return list;
	}

	public Core.Domain.Clip.Clip CaptureReviewedMarkersForClip(Vegas vegas, string clipsFolder, int clipIndex)
	{
		List<Core.Domain.Clip.Clip> clips = new ClipParser().ParseAllClips(clipsFolder);
		if (clipIndex < 0 || clipIndex >= clips.Count)
		{
			throw new ArgumentOutOfRangeException("clipIndex");
		}
		Region region = FindClipRegion(vegas.Project, clipIndex);
		if (region == null)
		{
			throw new InvalidOperationException("The clip is not present on the AE review timeline.");
		}
		Core.Domain.Clip.Clip clip = clips[clipIndex];
		double start = Seconds(((Marker)region).Position);
		double end = start + Seconds(region.Length);
		List<ShotEvent> events = new List<ShotEvent>();
		foreach (Marker marker in ((IEnumerable<Marker>)vegas.Project.Markers).Where((Marker item) => Seconds(item.Position) >= start && Seconds(item.Position) <= end))
		{
			ShotOutcome outcome;
			string gun;
			if (TryReviewedOutcome(marker.Label, clipIndex, clip.Gun, out outcome, out gun))
			{
				events.Add(ShotEvent.Reviewed(Seconds(marker.Position) - start, outcome, gun));
			}
		}
		clip.DurationSeconds = Seconds(region.Length);
		clip.ShotEvents = events.OrderBy((ShotEvent shot) => shot.SourceConfirmationTimeSeconds).ToList();
		return clip;
	}

	public void MarkClipReady(Vegas vegas, string clipsFolder, string sfxRoot, int clipIndex, IReadOnlyList<ReviewMarkerSubmission> reviewedMarkers)
	{
		Region region = RunVegasStage("find review region", () => FindClipRegion(vegas.Project, clipIndex));
		if (region == null)
		{
			throw new InvalidOperationException("The clip is not present on the AE review timeline.");
		}
		double start = Seconds(((Marker)region).Position);
		double end = start + Seconds(region.Length);
		if (reviewedMarkers == null)
		{
			throw new InvalidOperationException("Reviewed marker submission is missing.");
		}
		List<Core.Domain.Clip.Clip> clips = new ClipParser().ParseAllClips(clipsFolder);
		if (clipIndex < 0 || clipIndex >= clips.Count)
		{
			throw new ArgumentOutOfRangeException("clipIndex");
		}
		Core.Domain.Clip.Clip clip = clips[clipIndex];
		clip.DurationSeconds = Seconds(region.Length);
		clip.ShotEvents = reviewedMarkers.Select((ReviewMarkerSubmission marker) => CreateReviewedEvent(marker, start, end, clip.Gun)).OrderBy((ShotEvent shot) => shot.SourceConfirmationTimeSeconds).ToList();
		if (clip.ConfirmedKills.Count == 0)
		{
			throw new InvalidOperationException("A ready clip requires at least one reviewed Hit or Headshot marker.");
		}
		SfxTemplateCatalog catalog = SfxTemplateCatalog.Load(sfxRoot);
		ClipSyncLibrary library = ClipSyncLibrary.Load();
		library.Put(clip, catalog.RelevantFingerprint(clip.Gun), clip.ShotEvents, ClipSyncState.Ready);
		library.Save();
		RunVegasStage("remove ready clip from review timeline", () => RemoveClipFromTimeline(vegas, clipIndex));
	}

	private static T RunVegasStage<T>(string stage, Func<T> action)
	{
		try
		{
			return action();
		}
		catch (Exception innerException)
		{
			throw new InvalidOperationException("VEGAS operation failed during: " + stage + ".", innerException);
		}
	}

	private static ShotEvent CreateReviewedEvent(ReviewMarkerSubmission marker, double regionStart, double regionEnd, string primaryGun)
	{
		if (marker == null)
		{
			throw new InvalidOperationException("Reviewed marker submission contains an empty entry.");
		}
		if (marker.Outcome != ShotOutcome.Hit && marker.Outcome != ShotOutcome.Headshot && marker.Outcome != ShotOutcome.Miss)
		{
			throw new InvalidOperationException("Every submitted marker must be classified as Hit, Headshot, or Miss.");
		}
		if (marker.TimelineSeconds < regionStart - 0.001 || marker.TimelineSeconds > regionEnd + 0.001)
		{
			throw new InvalidOperationException("A submitted marker is outside the current clip region.");
		}
		string gun = string.IsNullOrWhiteSpace(marker.Gun) ? primaryGun : marker.Gun;
		ShotEvent reviewed = ShotEvent.Reviewed(Math.Max(0.0, marker.TimelineSeconds - regionStart), marker.Outcome, gun);
		reviewed.Confidence = Math.Max(0.0, Math.Min(1.0, marker.DetectionConfidence));
		reviewed.TemplateId = string.IsNullOrWhiteSpace(marker.TemplateId) ? (marker.Origin == ShotEventOrigin.UserMarked ? "manual" : null) : marker.TemplateId;
		reviewed.Origin = marker.Origin;
		return reviewed;
	}

	public void RemoveClipFromTimeline(Vegas vegas, int clipIndex)
	{
		Region region = FindClipRegion(vegas.Project, clipIndex);
		if (region == null)
		{
			return;
		}
		double start = Seconds(((Marker)region).Position);
		double end = start + Seconds(region.Length);
		foreach (Track track in ((IEnumerable<Track>)vegas.Project.Tracks).Where((Track item) => item.Name != null && item.Name.StartsWith("AE|")))
		{
			foreach (TrackEvent trackEvent in ((IEnumerable<TrackEvent>)track.Events).Where((TrackEvent item) => Seconds(item.Start) >= start - 0.001 && Seconds(item.Start) <= end).ToList())
			{
				((BaseList<TrackEvent>)(object)track.Events).Remove(trackEvent);
			}
		}
		foreach (Marker marker in ((IEnumerable<Marker>)vegas.Project.Markers).Where((Marker item) => MarkerIndex(item.Label) == clipIndex).ToList())
		{
			((BaseList<Marker>)(object)vegas.Project.Markers).Remove(marker);
		}
		((BaseList<Region>)(object)vegas.Project.Regions).Remove(region);
		vegas.UpdateUI();
	}

	public List<Core.Domain.Clip.Clip> HydrateFromLibrary(IEnumerable<string> paths)
	{
		ClipParser parser = new ClipParser();
		ClipSyncLibrary library = ClipSyncLibrary.Load();
		List<Core.Domain.Clip.Clip> clips = new List<Core.Domain.Clip.Clip>();
		foreach (string path in paths)
		{
			if (!File.Exists(path))
			{
				continue;
			}
			Core.Domain.Clip.Clip clip = parser.ParseClip(path);
			ClipSyncEntry entry = clip == null ? null : library.Find(path);
			if (clip != null && entry != null && entry.State == ClipSyncState.Ready)
			{
				clip.DurationSeconds = entry.DurationSeconds;
				clip.ShotEvents = new List<ShotEvent>(entry.Events);
				clips.Add(clip);
			}
		}
		return clips;
	}

	public static void CleanupGenerated(Vegas vegas)
	{
		CleanupGenerated(vegas.Project);
	}

	private static void CleanupGenerated(Project project)
	{
		foreach (Track item in ((IEnumerable<Track>)project.Tracks).Where((Track t) => t.Name != null && t.Name.StartsWith("AE|")).ToList())
		{
			((BaseList<Track>)(object)project.Tracks).Remove(item);
		}
		foreach (Marker item2 in ((IEnumerable<Marker>)project.Markers).Where((Marker m) => m.Label != null && m.Label.StartsWith("AE|")).ToList())
		{
			((BaseList<Marker>)(object)project.Markers).Remove(item2);
		}
		foreach (Region item3 in ((IEnumerable<Region>)project.Regions).Where((Region r) => ((Marker)r).Label != null && ((Marker)r).Label.StartsWith("AE|")).ToList())
		{
			((BaseList<Region>)(object)project.Regions).Remove(item3);
		}
	}

	private static bool TryReviewedOutcome(string label, int index, string primaryGun, out ShotOutcome outcome, out string gun)
	{
		outcome = ShotOutcome.Candidate;
		gun = primaryGun;
		if (string.IsNullOrEmpty(label))
		{
			return false;
		}
		string[] array = label.Split('|');
		int result;
		if (array.Length >= 5 && !string.IsNullOrWhiteSpace(array[4]))
		{
			gun = array[4];
		}
		return array.Length >= 3 && array[0] == "AE" && int.TryParse(array[2], out result) && result == index && Enum.TryParse<ShotOutcome>(array[1], ignoreCase: true, out outcome) && (outcome == ShotOutcome.Hit || outcome == ShotOutcome.Headshot || outcome == ShotOutcome.Miss);
	}

	public static string BuildMarkerLabel(string outcome, int index, string source, string gun)
	{
		return "AE|" + outcome + "|" + index.ToString(CultureInfo.InvariantCulture) + "|" + (source ?? string.Empty).Replace("|", "/") + "|" + (gun ?? string.Empty).Replace("|", "/");
	}

	private static bool IsCandidateLabel(string label, int index)
	{
		string[] parts = (label ?? string.Empty).Split('|');
		return parts.Length >= 3 && parts[0] == "AE" && MarkerIndex(label) == index &&
			(parts[1].StartsWith("Candidate-", StringComparison.OrdinalIgnoreCase) || parts[1].StartsWith("HighConfidence-", StringComparison.OrdinalIgnoreCase));
	}

	private static int MarkerIndex(string label)
	{
		string[] parts = (label ?? string.Empty).Split('|');
		int index;
		return parts.Length >= 3 && parts[0] == "AE" && int.TryParse(parts[2], out index) ? index : -1;
	}

	private static Region FindClipRegion(Project project, int clipIndex)
	{
		string prefix = "AE|CLIP|" + clipIndex.ToString(CultureInfo.InvariantCulture) + "|";
		return ((IEnumerable<Region>)project.Regions).FirstOrDefault((Region region) => ((Marker)region).Label != null && ((Marker)region).Label.StartsWith(prefix, StringComparison.Ordinal));
	}

	private static double NextAnalysisCursor(Project project)
	{
		double cursor = 0.0;
		foreach (Region region in ((IEnumerable<Region>)project.Regions).Where((Region item) => ((Marker)item).Label != null && ((Marker)item).Label.StartsWith("AE|CLIP|")))
		{
			cursor = Math.Max(cursor, Seconds(((Marker)region).Position) + Seconds(region.Length) + 1.0);
		}
		return cursor;
	}

	private static void PlaceFullClip(Project project, VideoTrack video, AudioTrack audio, Core.Domain.Clip.Clip clip, double cursor)
	{
		string fileName = Path.GetFileName(clip.FilePath);
		Media media = null;
		RunVegasStage("import media " + fileName, delegate
		{
			media = project.MediaPool.AddMedia(clip.FilePath);
		});
		if (media == (Media)null)
		{
			throw new InvalidOperationException("VEGAS returned no media for " + fileName + ".");
		}
		Timecode start = Timecode.FromSeconds(cursor);
		Timecode length = Timecode.FromSeconds(clip.DurationSeconds);
		VideoStream videoStream = null;
		RunVegasStage("read video stream " + fileName, delegate
		{
			videoStream = media.GetVideoStreamByIndex(0);
		});
		if (videoStream == (VideoStream)null)
		{
			throw new InvalidOperationException("No video stream found in " + fileName + ".");
		}
		VideoEvent videoEvent = null;
		RunVegasStage("construct video event " + fileName, delegate
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			videoEvent = new VideoEvent(start, length);
		});
		RunVegasStage("insert video event " + fileName, delegate
		{
			((BaseList<TrackEvent>)(object)((Track)video).Events).Add((TrackEvent)(object)videoEvent);
		});
		Take videoTake = null;
		RunVegasStage("construct video take " + fileName, delegate
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_0011: Expected O, but got Unknown
			videoTake = new Take((MediaStream)(object)videoStream);
		});
		RunVegasStage("insert video take " + fileName, delegate
		{
			((BaseList<Take>)(object)((TrackEvent)videoEvent).Takes).Add(videoTake);
		});
		AudioStream audioStream = null;
		RunVegasStage("read audio stream " + fileName, delegate
		{
			audioStream = ((IEnumerable)media.Streams).OfType<AudioStream>().FirstOrDefault();
		});
		if (!(audioStream == (AudioStream)null))
		{
			AudioEvent audioEvent = null;
			RunVegasStage("construct audio event " + fileName, delegate
			{
				//IL_000d: Unknown result type (might be due to invalid IL or missing references)
				//IL_0017: Expected O, but got Unknown
				audioEvent = new AudioEvent(start, length);
			});
			RunVegasStage("insert audio event " + fileName, delegate
			{
				((BaseList<TrackEvent>)(object)((Track)audio).Events).Add((TrackEvent)(object)audioEvent);
			});
			Take audioTake = null;
			RunVegasStage("construct audio take " + fileName, delegate
			{
				//IL_0007: Unknown result type (might be due to invalid IL or missing references)
				//IL_0011: Expected O, but got Unknown
				audioTake = new Take((MediaStream)(object)audioStream);
			});
			RunVegasStage("insert audio take " + fileName, delegate
			{
				((BaseList<Take>)(object)((TrackEvent)audioEvent).Takes).Add(audioTake);
			});
		}
	}

	private static double Seconds(Timecode value)
	{
		return value.ToMilliseconds() / 1000.0;
	}
}
