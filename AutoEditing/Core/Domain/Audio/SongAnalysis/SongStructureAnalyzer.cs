using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Domain.Audio.SongAnalysis;

public sealed class SongStructureAnalyzer
{
	private const int BeatsPerBar = 4;
	private const int BarsPerPhrase = 4;
	private const double SilenceRms = 0.0001;

	public SongAnalysis Analyze(MonoAudio audio, SongIdentity song)
	{
		if (audio == null)
		{
			throw new ArgumentNullException(nameof(audio));
		}
		if (song == null)
		{
			throw new ArgumentNullException(nameof(song));
		}
		SongAnalysis analysis = new SongAnalysis { Song = song };
		if (audio.DurationSeconds < 1.0 || RootMeanSquare(audio.Samples, 0, audio.Samples.Length) < SilenceRms)
		{
			AddUnusedRegion(analysis, audio.DurationSeconds);
			return analysis;
		}

		BeatGrid grid = new BeatDetector().DetectBeats(audio);
		analysis.TempoBpm = grid.Bpm;
		analysis.BeatPhaseSeconds = grid.FirstBeatOffsetSeconds;
		float[] onset = BeatDetector.ComputeOnsetEnvelope(audio.Samples);
		double hopSeconds = (double)BeatDetector.HopSize / audio.SampleRate;
		double[] beatStrengths = MeasureBeatStrengths(grid, onset, hopSeconds);
		double downbeatConfidence;
		int downbeatPhase = FindDownbeatPhase(beatStrengths, out downbeatConfidence);
		AddBeatEvents(analysis, grid, beatStrengths, downbeatPhase, downbeatConfidence);
		AddTransientEvents(analysis, onset, hopSeconds, grid.BeatIntervalSeconds);
		AddPhraseRegions(analysis, audio, grid);
		AddPhraseEvents(analysis);
		return analysis;
	}

	private static void AddBeatEvents(SongAnalysis analysis, BeatGrid grid, double[] strengths, int downbeatPhase, double downbeatConfidence)
	{
		for (int index = 0; index < grid.BeatTimesSeconds.Count; index++)
		{
			double time = grid.BeatTimesSeconds[index];
			double strength = strengths[index];
			analysis.Events.Add(CreateDetectedEvent(analysis.Song.ContentFingerprint, "beat", index, time, MusicEventType.Beat, strength, 0.55 + 0.4 * strength));
			if (index % BeatsPerBar == downbeatPhase)
			{
				analysis.Events.Add(CreateDetectedEvent(analysis.Song.ContentFingerprint, "downbeat", index, time, MusicEventType.Downbeat, strength, downbeatConfidence));
			}
			if (strength >= 0.72)
			{
				analysis.Events.Add(CreateDetectedEvent(analysis.Song.ContentFingerprint, "accent", index, time, MusicEventType.Accent, strength, 0.5 + 0.5 * strength));
			}
		}
	}

	private static MusicEvent CreateDetectedEvent(string fingerprint, string kind, int ordinal, double time, MusicEventType type, double strength, double confidence)
	{
		return new MusicEvent
		{
			Id = MusicAnalysisId.Create(fingerprint, kind, ordinal),
			TimeSeconds = time,
			Type = type,
			Strength = Clamp01(strength),
			Confidence = Clamp01(confidence),
			Origin = MusicAnalysisOrigin.Detected,
			ReviewState = MusicAnalysisReviewState.Proposed,
			DetectedTimeSeconds = time,
			DetectedType = type
		};
	}

	private static double[] MeasureBeatStrengths(BeatGrid grid, float[] onset, double hopSeconds)
	{
		double[] raw = new double[grid.BeatTimesSeconds.Count];
		double maximum = 0.0;
		for (int index = 0; index < raw.Length; index++)
		{
			int frame = (int)Math.Round(grid.BeatTimesSeconds[index] / hopSeconds);
			double value = 0.0;
			for (int offset = -1; offset <= 1; offset++)
			{
				int candidate = frame + offset;
				if (candidate >= 0 && candidate < onset.Length)
				{
					value = Math.Max(value, onset[candidate]);
				}
			}
			raw[index] = value;
			maximum = Math.Max(maximum, value);
		}
		if (maximum > 0.0)
		{
			for (int index = 0; index < raw.Length; index++)
			{
				raw[index] /= maximum;
			}
		}
		return raw;
	}

	private static int FindDownbeatPhase(double[] strengths, out double confidence)
	{
		double[] scores = new double[BeatsPerBar];
		for (int phase = 0; phase < BeatsPerBar; phase++)
		{
			List<double> values = new List<double>();
			for (int index = phase; index < strengths.Length; index += BeatsPerBar)
			{
				values.Add(strengths[index]);
			}
			scores[phase] = values.Count == 0 ? 0.0 : values.Average();
		}
		int best = Array.IndexOf(scores, scores.Max());
		double second = scores.Where((double score, int index) => index != best).DefaultIfEmpty(0.0).Max();
		confidence = Clamp01(0.35 + Math.Max(0.0, scores[best] - second));
		return best;
	}

	private static void AddTransientEvents(SongAnalysis analysis, float[] onset, double hopSeconds, double beatInterval)
	{
		if (onset.Length < 3)
		{
			return;
		}
		float[] ordered = (float[])onset.Clone();
		Array.Sort(ordered);
		double threshold = Math.Max(0.35, ordered[(int)Math.Floor((ordered.Length - 1) * 0.9)]);
		double minimumSpacing = Math.Min(0.12, beatInterval / 4.0);
		double lastTime = -minimumSpacing;
		int ordinal = 0;
		for (int index = 1; index < onset.Length - 1; index++)
		{
			if (onset[index] < threshold || onset[index] < onset[index - 1] || onset[index] < onset[index + 1])
			{
				continue;
			}
			double time = index * hopSeconds;
			if (time - lastTime < minimumSpacing)
			{
				continue;
			}
			analysis.Events.Add(CreateDetectedEvent(analysis.Song.ContentFingerprint, "transient", ordinal++, time, MusicEventType.Transient, onset[index], 0.45 + 0.5 * onset[index]));
			lastTime = time;
		}
	}

	private static void AddPhraseRegions(SongAnalysis analysis, MonoAudio audio, BeatGrid grid)
	{
		double phraseDuration = grid.BeatIntervalSeconds * BeatsPerBar * BarsPerPhrase;
		double first = Math.Max(0.0, grid.FirstBeatOffsetSeconds);
		List<double> boundaries = new List<double> { 0.0 };
		for (double time = first + phraseDuration; time < audio.DurationSeconds; time += phraseDuration)
		{
			boundaries.Add(time);
		}
		boundaries.Add(audio.DurationSeconds);
		double[] energy = new double[boundaries.Count - 1];
		for (int index = 0; index < energy.Length; index++)
		{
			int start = Math.Max(0, (int)Math.Floor(boundaries[index] * audio.SampleRate));
			int end = Math.Min(audio.Samples.Length, (int)Math.Ceiling(boundaries[index + 1] * audio.SampleRate));
			energy[index] = RootMeanSquare(audio.Samples, start, end - start);
		}
		double maximum = energy.DefaultIfEmpty(0.0).Max();
		if (maximum > 0.0)
		{
			for (int index = 0; index < energy.Length; index++)
			{
				energy[index] /= maximum;
			}
		}
		double median = Percentile(energy, 0.5);
		double low = Percentile(energy, 0.3);
		double high = Percentile(energy, 0.75);
		for (int index = 0; index < energy.Length; index++)
		{
			double delta = index == 0 ? 0.0 : energy[index] - energy[index - 1];
			MusicRegionType type = ClassifyRegion(index, energy.Length, energy[index], delta, median, low, high);
			AddOrMergeRegion(analysis, boundaries[index], boundaries[index + 1], type, energy[index], delta, index);
		}
	}

	private static MusicRegionType ClassifyRegion(int index, int count, double energy, double delta, double median, double low, double high)
	{
		if (index == 0 && energy < median * 0.9)
		{
			return MusicRegionType.Intro;
		}
		if (index == count - 1 && energy < median * 0.85)
		{
			return MusicRegionType.Outro;
		}
		if (delta >= 0.16)
		{
			return MusicRegionType.BuildUp;
		}
		if (energy >= Math.Max(0.6, high))
		{
			return MusicRegionType.Climax;
		}
		if (energy <= Math.Min(0.3, low))
		{
			return energy <= 0.16 ? MusicRegionType.Cinematic : MusicRegionType.Breakdown;
		}
		return MusicRegionType.Action;
	}

	private static void AddOrMergeRegion(SongAnalysis analysis, double start, double end, MusicRegionType type, double energy, double delta, int ordinal)
	{
		MusicRegion previous = analysis.Regions.LastOrDefault();
		if (previous != null && previous.Type == type)
		{
			double previousDuration = previous.EndSeconds - previous.StartSeconds;
			double duration = end - start;
			previous.Energy = (previous.Energy.GetValueOrDefault() * previousDuration + energy * duration) / (previousDuration + duration);
			previous.EndSeconds = end;
			previous.DetectedEndSeconds = end;
			return;
		}
		analysis.Regions.Add(new MusicRegion
		{
			Id = MusicAnalysisId.Create(analysis.Song.ContentFingerprint, "region", ordinal),
			StartSeconds = start,
			EndSeconds = end,
			Type = type,
			Confidence = 0.55,
			Energy = energy,
			EnergyDelta = delta,
			Origin = MusicAnalysisOrigin.Detected,
			ReviewState = MusicAnalysisReviewState.Proposed,
			DetectedStartSeconds = start,
			DetectedEndSeconds = end,
			DetectedType = type
		});
	}

	private static void AddPhraseEvents(SongAnalysis analysis)
	{
		for (int index = 1; index < analysis.Regions.Count; index++)
		{
			MusicRegion current = analysis.Regions[index];
			double time = current.StartSeconds;
			analysis.Events.Add(CreateDetectedEvent(analysis.Song.ContentFingerprint, "phrase", index, time, MusicEventType.PhraseBoundary, Math.Abs(current.EnergyDelta.GetValueOrDefault()), 0.55));
			if (current.EnergyDelta.GetValueOrDefault() >= 0.16)
			{
				double beatInterval = analysis.TempoBpm.HasValue ? 60.0 / analysis.TempoBpm.Value : 0.0;
				double buildTime = Math.Max(0.0, time - beatInterval);
				analysis.Events.Add(CreateDetectedEvent(analysis.Song.ContentFingerprint, "build-hit", index, buildTime, MusicEventType.BuildHit, current.EnergyDelta.GetValueOrDefault(), 0.5 + current.EnergyDelta.GetValueOrDefault()));
				analysis.Events.Add(CreateDetectedEvent(analysis.Song.ContentFingerprint, "drop", index, time, MusicEventType.Drop, current.Energy.GetValueOrDefault(), 0.55 + current.EnergyDelta.GetValueOrDefault()));
			}
		}
	}

	private static void AddUnusedRegion(SongAnalysis analysis, double duration)
	{
		if (duration <= 0.0)
		{
			return;
		}
		analysis.Regions.Add(new MusicRegion
		{
			Id = MusicAnalysisId.Create(analysis.Song.ContentFingerprint, "region", 0),
			StartSeconds = 0.0,
			EndSeconds = duration,
			Type = MusicRegionType.Unused,
			Confidence = 1.0,
			Energy = 0.0,
			Origin = MusicAnalysisOrigin.Detected,
			ReviewState = MusicAnalysisReviewState.Proposed,
			DetectedStartSeconds = 0.0,
			DetectedEndSeconds = duration,
			DetectedType = MusicRegionType.Unused
		});
	}

	private static double RootMeanSquare(float[] samples, int start, int length)
	{
		if (length <= 0)
		{
			return 0.0;
		}
		double sum = 0.0;
		int end = Math.Min(samples.Length, start + length);
		for (int index = start; index < end; index++)
		{
			sum += samples[index] * samples[index];
		}
		return Math.Sqrt(sum / Math.Max(1, end - start));
	}

	private static double Percentile(double[] values, double percentile)
	{
		if (values.Length == 0)
		{
			return 0.0;
		}
		double[] ordered = (double[])values.Clone();
		Array.Sort(ordered);
		return ordered[(int)Math.Round((ordered.Length - 1) * percentile)];
	}

	private static double Clamp01(double value)
	{
		return Math.Max(0.0, Math.Min(1.0, value));
	}
}
