using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Domain.Audio;
using Core.Domain.Audio.SongAnalysis;

namespace AnalysisHarness
{
    /// <summary>
    /// Diagnostic commands for tuning the detectors against real media:
    ///   --debug-tempo &lt;songPath&gt;   ranks tempo candidates from the autocorrelation
    ///   --debug-shots &lt;clipPath&gt;   prints the loudest envelope peaks with attack stats
    /// </summary>
    internal static class DebugCommands
    {
		public static void DebugSong(string songPath, string outputPath)
		{
			MonoAudio audio = AudioLoader.LoadMono(songPath);
			SongIdentity identity = SongIdentity.FromFile(songPath, audio.DurationSeconds);
			SongAnalysis analysis = new SongStructureAnalyzer().Analyze(audio, identity);
			Console.WriteLine(Path.GetFileName(songPath));
			Console.WriteLine($"Duration: {audio.DurationSeconds:F2}s");
			Console.WriteLine(analysis.TempoBpm.HasValue
				? $"Tempo: {analysis.TempoBpm:F2} BPM, phase {analysis.BeatPhaseSeconds:F3}s"
				: "Tempo: none (short or effectively silent audio)");
			Console.WriteLine();
			Console.WriteLine("Regions:");
			foreach (MusicRegion region in analysis.Regions)
			{
				Console.WriteLine($"  {region.StartSeconds,7:F2}-{region.EndSeconds,7:F2}s  {region.Type,-10} energy={region.Energy.GetValueOrDefault():F2} delta={region.EnergyDelta.GetValueOrDefault():+0.00;-0.00;0.00} confidence={region.Confidence.GetValueOrDefault():F2}");
			}
			Console.WriteLine();
			Console.WriteLine("Event summary:");
			foreach (IGrouping<MusicEventType, MusicEvent> group in analysis.Events.GroupBy((MusicEvent item) => item.Type).OrderBy((IGrouping<MusicEventType, MusicEvent> group) => group.Key))
			{
				Console.WriteLine($"  {group.Key,-16} {group.Count(),4}  first=[{string.Join(", ", group.Take(8).Select((MusicEvent item) => item.TimeSeconds.ToString("F2")))}]");
			}
			if (!string.IsNullOrWhiteSpace(outputPath))
			{
				new SongAnalysisStore().Save(outputPath, analysis);
				Console.WriteLine();
				Console.WriteLine("Exported: " + Path.GetFullPath(outputPath));
			}
		}

        public static void DebugTempo(string songPath)
        {
            MonoAudio audio = AudioLoader.LoadMono(songPath);
            double hopSeconds = (double)BeatDetector.HopSize / audio.SampleRate;
            float[] onsetEnvelope = BeatDetector.ComputeOnsetEnvelope(audio.Samples);

            int minLag = (int)Math.Floor(60.0 / 220.0 / hopSeconds);
            int maxLag = (int)Math.Ceiling(60.0 / 55.0 / hopSeconds);

            double[] correlation = new double[maxLag + 1];
            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double sum = 0.0;
                for (int i = 0; i + lag < onsetEnvelope.Length; i++)
                {
                    sum += onsetEnvelope[i] * onsetEnvelope[i + lag];
                }
                correlation[lag] = sum / (onsetEnvelope.Length - lag);
            }

            // Local maxima of the correlation, strongest first.
            List<KeyValuePair<double, double>> peaks = new List<KeyValuePair<double, double>>();
            for (int lag = minLag + 1; lag < maxLag; lag++)
            {
                if (correlation[lag] >= correlation[lag - 1] && correlation[lag] >= correlation[lag + 1])
                {
                    peaks.Add(new KeyValuePair<double, double>(60.0 / (lag * hopSeconds), correlation[lag]));
                }
            }

            double maxCorrelation = peaks.Max(p => p.Value);
            Console.WriteLine("Tempo candidates (BPM, relative correlation):");
            foreach (KeyValuePair<double, double> peak in peaks.OrderByDescending(p => p.Value).Take(12))
            {
                Console.WriteLine($"  {peak.Key,7:F2} BPM   {peak.Value / maxCorrelation,6:P1}");
            }

            // How well do onsets align with the grid each candidate implies?
            BeatDetector detector = new BeatDetector();
            BeatGrid grid = detector.DetectBeats(audio);
            Console.WriteLine();
            Console.WriteLine($"Chosen: {grid.Bpm:F2} BPM, phase {grid.FirstBeatOffsetSeconds:F3}s");
            PrintGridFit(onsetEnvelope, hopSeconds, grid);
        }

        private static void PrintGridFit(float[] onsetEnvelope, double hopSeconds, BeatGrid grid)
        {
            double onBeat = 0.0;
            double offBeat = 0.0;
            int count = 0;
            foreach (double beat in grid.BeatTimesSeconds)
            {
                int frame = (int)Math.Round(beat / hopSeconds);
                int halfFrame = (int)Math.Round((beat + grid.BeatIntervalSeconds / 2.0) / hopSeconds);
                if (halfFrame >= onsetEnvelope.Length)
                {
                    break;
                }
                onBeat += onsetEnvelope[frame];
                offBeat += onsetEnvelope[halfFrame];
                count++;
            }
            Console.WriteLine($"Mean onset strength on beats: {onBeat / count:F4}, at half-beat offsets: {offBeat / count:F4}");
        }

        public static void DebugShots(string clipPath)
        {
            MonoAudio audio = AudioLoader.LoadMono(clipPath);
            float[] envelope = ShotDetector.ComputeRmsEnvelope(audio, out double hopSeconds);

            float[] sorted = (float[])envelope.Clone();
            Array.Sort(sorted);
            float median = sorted[sorted.Length / 2];
            float max = sorted[sorted.Length - 1];
            Console.WriteLine($"{clipPath}");
            Console.WriteLine($"Envelope frames: {envelope.Length}, hop {hopSeconds * 1000:F1}ms, median {median:F5}, max {max:F5} ({max / Math.Max(median, 1e-9f):F1}x median)");
            Console.WriteLine();
            Console.WriteLine("Top peaks (time, level, x-median, x-max, rise from 30/80/150ms before):");

            List<int> peakIndices = new List<int>();
            for (int i = 1; i < envelope.Length - 1; i++)
            {
                if (envelope[i] >= envelope[i - 1] && envelope[i] >= envelope[i + 1])
                {
                    peakIndices.Add(i);
                }
            }

            int lookback30 = Math.Max(1, (int)Math.Round(0.03 / hopSeconds));
            int lookback80 = Math.Max(1, (int)Math.Round(0.08 / hopSeconds));
            int lookback150 = Math.Max(1, (int)Math.Round(0.15 / hopSeconds));

            // Strongest peak per 0.35s cluster, top 20 overall.
            List<int> shown = new List<int>();
            foreach (int index in peakIndices.OrderByDescending(i => envelope[i]))
            {
                if (shown.Any(s => Math.Abs(s - index) * hopSeconds < 0.35))
                {
                    continue;
                }
                shown.Add(index);
                if (shown.Count >= 20)
                {
                    break;
                }
            }

            foreach (int index in shown.OrderBy(i => i))
            {
                float level = envelope[index];
                float before30 = envelope[Math.Max(0, index - lookback30)];
                float before80 = envelope[Math.Max(0, index - lookback80)];
                float before150 = envelope[Math.Max(0, index - lookback150)];
                Console.WriteLine($"  {index * hopSeconds,6:F2}s  {level:F5}  {level / Math.Max(median, 1e-9f),6:F1}x  {level / max,6:P0}  " +
                                  $"rise {(level - before30) / level,6:P0} / {(level - before80) / level,6:P0} / {(level - before150) / level,6:P0}");
            }
        }
    }
}
