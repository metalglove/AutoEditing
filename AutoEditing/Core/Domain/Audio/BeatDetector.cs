using System;
using System.Collections.Generic;

namespace Core.Domain.Audio
{
    /// <summary>
    /// A steady beat grid detected from a song: tempo plus the timestamp of
    /// every beat across the song duration.
    /// </summary>
    public class BeatGrid
    {
        public double Bpm { get; set; }
        public double FirstBeatOffsetSeconds { get; set; }
        public List<double> BeatTimesSeconds { get; set; } = new List<double>();

        public double BeatIntervalSeconds
        {
            get { return 60.0 / Bpm; }
        }
    }

    /// <summary>
    /// Detects tempo and beat positions from audio using an onset-energy
    /// envelope, autocorrelation for tempo, and a phase search for beat
    /// alignment. Assumes a steady tempo, which holds for typical montage
    /// soundtracks.
    /// </summary>
    public class BeatDetector
    {
        internal const int FrameSize = 1024;
        internal const int HopSize = 512;
        private const double MinBpm = 60.0;
        private const double MaxBpm = 200.0;

        /// <summary>
        /// Detected tempos above this get folded down an octave: fast songs are
        /// usually 8th-note aliases of a slower true tempo, and even for real
        /// fast tempos a half-time grid still lands every cut on a beat while
        /// giving the montage breathable pacing.
        /// </summary>
        private const double MaxGridBpm = 150.0;

        public BeatGrid DetectBeats(MonoAudio audio)
        {
            double hopSeconds = (double)HopSize / audio.SampleRate;
            float[] onsetEnvelope = ComputeOnsetEnvelope(audio.Samples);

            double bpm = EstimateBpm(onsetEnvelope, hopSeconds);
            while (bpm > MaxGridBpm)
            {
                bpm /= 2.0;
            }
            double beatIntervalSeconds = 60.0 / bpm;
            double offsetSeconds = FindBeatPhase(onsetEnvelope, hopSeconds, beatIntervalSeconds);

            BeatGrid grid = new BeatGrid { Bpm = bpm, FirstBeatOffsetSeconds = offsetSeconds };
            for (double t = offsetSeconds; t < audio.DurationSeconds; t += beatIntervalSeconds)
            {
                grid.BeatTimesSeconds.Add(t);
            }
            return grid;
        }

        /// <summary>
        /// Positive energy flux per hop: how much louder each frame is than the
        /// previous one. Percussive onsets (kicks, snares) show up as spikes.
        /// </summary>
        internal static float[] ComputeOnsetEnvelope(float[] samples)
        {
            int frameCount = Math.Max(0, (samples.Length - FrameSize) / HopSize);
            float[] energies = new float[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                int start = i * HopSize;
                float sum = 0f;
                for (int j = 0; j < FrameSize; j++)
                {
                    float s = samples[start + j];
                    sum += s * s;
                }
                energies[i] = sum;
            }

            float[] flux = new float[frameCount];
            for (int i = 1; i < frameCount; i++)
            {
                float rise = energies[i] - energies[i - 1];
                flux[i] = rise > 0f ? rise : 0f;
            }
            NormalizeInPlace(flux);
            return flux;
        }

        private static void NormalizeInPlace(float[] values)
        {
            float max = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > max)
                {
                    max = values[i];
                }
            }
            if (max <= 0f)
            {
                return;
            }
            for (int i = 0; i < values.Length; i++)
            {
                values[i] /= max;
            }
        }

        /// <summary>
        /// Tempo estimation: autocorrelation of the onset envelope over the
        /// plausible BPM lag range, weighted towards typical song tempos, with
        /// parabolic interpolation around the winning lag for sub-frame accuracy.
        /// </summary>
        private static double EstimateBpm(float[] onsetEnvelope, double hopSeconds)
        {
            int minLag = (int)Math.Floor(60.0 / MaxBpm / hopSeconds);
            int maxLag = (int)Math.Ceiling(60.0 / MinBpm / hopSeconds);
            maxLag = Math.Min(maxLag, onsetEnvelope.Length / 2);

            if (maxLag <= minLag)
            {
                return 120.0;
            }

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

            int bestLag = minLag;
            double bestScore = double.MinValue;
            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double bpm = 60.0 / (lag * hopSeconds);

                // Log-Gaussian preference centred near 130 BPM keeps the picker
                // from choosing half or double tempo when both correlate.
                double octaves = Math.Log(bpm / 130.0, 2.0);
                double weight = Math.Exp(-0.5 * Math.Pow(octaves / 0.9, 2.0));

                // Reward tempos whose double-length lag also correlates: a true
                // beat period repeats at every multiple.
                double harmonicSupport = lag * 2 <= maxLag ? 0.5 * correlation[lag * 2] : 0.0;

                double score = (correlation[lag] + harmonicSupport) * weight;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestLag = lag;
                }
            }

            double refinedLag = bestLag;
            if (bestLag > minLag && bestLag < maxLag)
            {
                double left = correlation[bestLag - 1];
                double centre = correlation[bestLag];
                double right = correlation[bestLag + 1];
                double denominator = left - 2.0 * centre + right;
                if (Math.Abs(denominator) > 1e-12)
                {
                    refinedLag = bestLag + 0.5 * (left - right) / denominator;
                }
            }

            return 60.0 / (refinedLag * hopSeconds);
        }

        /// <summary>
        /// Finds the beat phase: the offset (within one beat period) whose grid
        /// points line up with the strongest onsets.
        /// </summary>
        private static double FindBeatPhase(float[] onsetEnvelope, double hopSeconds, double beatIntervalSeconds)
        {
            int periodFrames = Math.Max(1, (int)Math.Round(beatIntervalSeconds / hopSeconds));
            double bestOffsetFrames = 0.0;
            double bestScore = double.MinValue;

            for (int offset = 0; offset < periodFrames; offset++)
            {
                double score = 0.0;
                for (double frame = offset; frame < onsetEnvelope.Length; frame += beatIntervalSeconds / hopSeconds)
                {
                    score += onsetEnvelope[(int)frame];
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    bestOffsetFrames = offset;
                }
            }

            return bestOffsetFrames * hopSeconds;
        }
    }
}
