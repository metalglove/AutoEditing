using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Core.Domain.Clip;
using Core.Domain.Logging;
using ScriptPortal.Vegas;

namespace Core.Domain.Audio;

public sealed class ShotReviewWorkflow
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

	public void CalibrateSfx(Vegas vegas, string sfxRoot)
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

	public void SaveCalibration(Vegas vegas, string sfxRoot)
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
		ShotAnalysisSidecar shotAnalysisSidecar = ShotAnalysisSidecar.Load(clipsFolder);
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
			MonoAudio monoAudio = AudioLoader.LoadMono(clip.FilePath);
			clip.DurationSeconds = monoAudio.DurationSeconds;
			ClipShotAnalysis clipShotAnalysis = shotAnalysisSidecar.FindValid(clip.FilePath, templateFingerprint);
			List<ShotEvent> list2 = ((clipShotAnalysis == null) ? shotDetector.DetectShots(monoAudio, clip.Gun, sfxTemplateCatalog, sfxRoot) : clipShotAnalysis.Events);
			shotAnalysisSidecar.Put(clip.FilePath, templateFingerprint, list2);
			analysisBatch.Items.Add(new AnalysisItem
			{
				Index = i,
				Clip = clip,
				Events = list2
			});
			int num = list2.Count((ShotEvent e) => e.Confidence >= 0.82);
			Logger.Log(Path.GetFileName(clip.FilePath) + ": " + num + " high-confidence and " + (list2.Count - num) + " review-candidate shot matches.");
		}
		shotAnalysisSidecar.Save(clipsFolder);
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
						((BaseList<Marker>)(object)project.Markers).Add(new Marker(Timecode.FromSeconds(itemCursor + @event.SourceConfirmationTimeSeconds), "AE|" + text2 + text + "|" + item.Index + "|" + text3 + "|" + @event.TemplateId));
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
		((BaseList<Marker>)(object)vegas.Project.Markers).Add(new Marker(vegas.Transport.CursorPosition, "AE|" + outcome.ToString() + "|" + array[2] + "|manual"));
	}

	public List<Core.Domain.Clip.Clip> CaptureReviewedMarkers(Vegas vegas, string clipsFolder, string sfxRoot)
	{
		SfxTemplateCatalog sfxTemplateCatalog = SfxTemplateCatalog.Load(sfxRoot);
		List<Core.Domain.Clip.Clip> list = new ClipParser().ParseAllClips(clipsFolder);
		ShotAnalysisSidecar shotAnalysisSidecar = ShotAnalysisSidecar.Load(clipsFolder);
		foreach (Region item in ((IEnumerable<Region>)vegas.Project.Regions).Where((Region r) => ((Marker)r).Label != null && ((Marker)r).Label.StartsWith("AE|CLIP|")).ToList())
		{
			string[] array = ((Marker)item).Label.Split(new char[1] { '|' }, 4);
			if (array.Length < 4 || !int.TryParse(array[2], out var result) || result < 0 || result >= list.Count)
			{
				continue;
			}
			Core.Domain.Clip.Clip clip = list[result];
			double start = Seconds(((Marker)item).Position);
			double end = start + Seconds(item.Length);
			List<ShotEvent> list2 = new List<ShotEvent>();
			foreach (Marker item2 in ((IEnumerable<Marker>)vegas.Project.Markers).Where((Marker m) => Seconds(m.Position) >= start && Seconds(m.Position) <= end).ToList())
			{
				if (TryReviewedOutcome(item2.Label, result, out var outcome))
				{
					list2.Add(ShotEvent.Reviewed(Seconds(item2.Position) - start, outcome));
				}
			}
			clip.DurationSeconds = Seconds(item.Length);
			clip.ShotEvents = list2.OrderBy((ShotEvent e) => e.SourceConfirmationTimeSeconds).ToList();
			sfxTemplateCatalog.ValidateForGun(sfxRoot, clip.Gun);
			shotAnalysisSidecar.Put(clip.FilePath, sfxTemplateCatalog.RelevantFingerprint(clip.Gun), clip.ShotEvents);
		}
		shotAnalysisSidecar.Save(clipsFolder);
		return list;
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

	private static bool TryReviewedOutcome(string label, int index, out ShotOutcome outcome)
	{
		outcome = ShotOutcome.Candidate;
		if (string.IsNullOrEmpty(label))
		{
			return false;
		}
		string[] array = label.Split('|');
		int result;
		return array.Length >= 3 && array[0] == "AE" && int.TryParse(array[2], out result) && result == index && Enum.TryParse<ShotOutcome>(array[1], ignoreCase: true, out outcome) && (outcome == ShotOutcome.Hit || outcome == ShotOutcome.Headshot || outcome == ShotOutcome.Miss);
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
