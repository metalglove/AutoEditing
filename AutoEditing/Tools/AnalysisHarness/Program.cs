using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Domain;
using Core.Domain.Audio;
using Core.Domain.Clip;
using Core.Domain.Editing;

namespace AnalysisHarness
{
    /// <summary>
    /// Runs the VEGAS-free part of the montage pipeline (parse, beat detection,
    /// shot detection, planning) against a folder of real clips and a song, and
    /// prints everything the pipeline decided. Usage:
    ///
    ///   AnalysisHarness.exe [clipsFolder] [songPath]
    ///
    /// Defaults to the test clips folder when no arguments are given.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
			if (args.Length == 1 && args[0] == "--self-test-song-analysis")
			{
				SongAnalysisSelfTests.RunAll();
				return 0;
			}
            if (args.Length == 2 && args[0] == "--debug-tempo")
            {
                DebugCommands.DebugTempo(args[1]);
                return 0;
            }
			if (args.Length == 2 && args[0] == "--debug-song")
			{
				DebugCommands.DebugSong(args[1], null);
				return 0;
			}
			if (args.Length == 3 && args[0] == "--export-song-analysis")
			{
				DebugCommands.DebugSong(args[1], args[2]);
				return 0;
			}
            if (args.Length == 2 && args[0] == "--debug-shots")
            {
                DebugCommands.DebugShots(args[1]);
                return 0;
            }

            string clipsFolder = args.Length > 0 ? args[0] : @"C:\VEGAS\edit";
            string songPath = args.Length > 1
                ? args[1]
                : Directory.GetFiles(clipsFolder, "*.mp3").FirstOrDefault();
            string sfxRoot = args.Length > 2 ? args[2] : ConfigurationManager.GetShotDetection().SfxRoot;

            if (!Directory.Exists(clipsFolder))
            {
                Console.Error.WriteLine($"Clips folder not found: {clipsFolder}");
                return 1;
            }

            Console.WriteLine("=== 1. Clip parsing ===");
            ClipParser parser = new ClipParser();
            List<Clip> clips = parser.ParseAllClips(clipsFolder);
            List<string> allFiles = Directory.GetFiles(clipsFolder, "*.mp4").ToList();
            Console.WriteLine($"Parsed {clips.Count} of {allFiles.Count} mp4 files.");
            foreach (string file in allFiles)
            {
                Clip clip = clips.FirstOrDefault(c => c.FilePath == file);
                if (clip == null)
                {
                    Console.WriteLine($"  REJECTED: {Path.GetFileName(file)}");
                    continue;
                }
                string flags = (clip.IsOpener ? " [OPENER]" : "") + (clip.IsCloser ? " [CLOSER]" : "");
                string notes = string.IsNullOrEmpty(clip.Notes) ? "" : $" notes='{clip.Notes}'";
                Console.WriteLine($"  {Path.GetFileName(file)}");
                Console.WriteLine($"    -> player={clip.PlayerName} game={clip.Game} map={clip.Map} gun={clip.Gun} type='{clip.ClipType}' seq={clip.SequenceNumber}{flags}{notes}");
            }

            if (songPath == null || !File.Exists(songPath))
            {
                Console.Error.WriteLine("No song file found; stopping after parsing.");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("=== 2. Beat detection ===");
            Console.WriteLine($"Song: {Path.GetFileName(songPath)}");
            MonoAudio songAudio = AudioLoader.LoadMono(songPath);
            Console.WriteLine($"Duration: {songAudio.DurationSeconds:F1}s, sample rate: {songAudio.SampleRate}");
            BeatDetector beatDetector = new BeatDetector();
            BeatGrid beats = beatDetector.DetectBeats(songAudio);
            Console.WriteLine($"BPM: {beats.Bpm:F2}, first beat: {beats.FirstBeatOffsetSeconds:F3}s, beats: {beats.BeatTimesSeconds.Count}");
            Console.WriteLine($"First beats: {string.Join(", ", beats.BeatTimesSeconds.Take(8).Select(b => b.ToString("F2")))}");

            Console.WriteLine();
            Console.WriteLine("=== 3. Shot detection ===");
            Console.WriteLine($"SFX root: {sfxRoot}");
            SfxTemplateCatalog catalog = null;
            try
            {
                catalog = SfxTemplateCatalog.Load(sfxRoot);
                Console.WriteLine($"Loaded {catalog.Templates.Count} calibrated SFX templates.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Shot detection skipped (no usable catalog): {ex.Message}");
            }
            ShotDetector shotDetector = new ShotDetector();
            foreach (Clip clip in clips)
            {
                MonoAudio clipAudio = AudioLoader.LoadMono(clip.FilePath);
                clip.DurationSeconds = clipAudio.DurationSeconds;
                Console.WriteLine($"  {Path.GetFileName(clip.FilePath)}");
                if (catalog == null)
                {
                    Console.WriteLine($"    duration={clip.DurationSeconds:F1}s  (no catalog)");
                    continue;
                }
                if (catalog.ForGun(clip.Gun).Count == 0)
                {
                    Console.WriteLine($"    duration={clip.DurationSeconds:F1}s  no SFX templates for gun '{clip.Gun}'");
                    continue;
                }
                try
                {
                    clip.ShotEvents = shotDetector.DetectShots(clipAudio, clip.Gun, catalog, sfxRoot);
                    string shots = string.Join(", ", clip.ShotEvents.Select(e => $"{e.SourceMuzzleTimeSeconds:F2}({e.Outcome})"));
                    Console.WriteLine($"    duration={clip.DurationSeconds:F1}s shots={clip.ShotEvents.Count} at [{shots}]");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    duration={clip.DurationSeconds:F1}s  detection failed: {ex.Message}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== 4. Montage plan ===");
            // The planner only uses reviewed Hit/Headshot markers. VEGAS does that
            // review interactively; here we auto-accept the detected kills so the
            // VEGAS-free harness can still exercise planning.
            Console.WriteLine("(auto-accepting detected Hit/Headshot markers as reviewed)");
            foreach (Clip clip in clips)
            {
                foreach (ShotEvent shot in clip.ShotEvents.Where(e => e.IsConfirmedKill))
                {
                    shot.ReviewState = ShotReviewState.Reviewed;
                }
            }
            foreach (Clip clip in clips.Where(c => c.ConfirmedKills.Count == 0))
            {
                Console.WriteLine($"  (excluded, no confirmed kills: {Path.GetFileName(clip.FilePath)})");
            }
            List<Clip> planClips = clips.Where(c => c.ConfirmedKills.Count > 0).ToList();
            MontagePlanner planner = new MontagePlanner();
            List<ClipPlacement> placements = planner.PlanMontage(planClips, beats, songAudio.DurationSeconds);
            Console.WriteLine($"Beat interval: {beats.BeatIntervalSeconds:F3}s");
            foreach (ClipPlacement placement in placements)
            {
                Console.WriteLine($"  {placement.TimelineStartSeconds,7:F2}s - {placement.TimelineEndSeconds,7:F2}s  " +
                                  $"{placement.Clip.Gun} {placement.Clip.ClipType} #{placement.Clip.SequenceNumber} ({placement.Clip.Map})  " +
                                  $"source@{placement.SourceOffsetSeconds:F2}s len={placement.LengthSeconds:F2}s  " +
                                  $"kills@[{string.Join(", ", placement.TimelineKillTimesSeconds.Select(k => k.ToString("F2")))}]");
            }
            Console.WriteLine($"Total montage length: {(placements.Count > 0 ? placements.Last().TimelineEndSeconds : 0.0):F1}s of {songAudio.DurationSeconds:F1}s song.");

            return 0;
        }
    }
}
