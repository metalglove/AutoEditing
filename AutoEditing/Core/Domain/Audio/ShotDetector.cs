using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Domain.Audio
{
    /// <summary>
    /// Detects sniper shots in gameplay audio. A player's own shot is by far the
    /// loudest transient in a clip: the detector computes a short-time RMS
    /// envelope and keeps sharp peaks that stand well above the clip's ambient
    /// loudness.
    /// </summary>
    public class ShotDetector
    {
        private const double WindowSeconds = 0.04;
        private const double HopSeconds = 0.01;

        /// <summary>
        /// Minimum spacing between two distinct shots. Bolt-action snipers
        /// cannot fire faster than this; closer peaks belong to the same shot.
        /// </summary>
        private const double MinShotIntervalSeconds = 0.35;

        /// <summary>
        /// A peak must be at least this many times louder than the clip's median
        /// loudness to count as a shot.
        /// </summary>
        private const double PeakOverMedianFactor = 3.5;

        /// <summary>
        /// A peak must reach at least this fraction of the loudest moment in the
        /// clip. Filters out mid-loud game sounds in quiet clips.
        /// </summary>
        private const double PeakOverMaxFraction = 0.30;

        /// <summary>
        /// The envelope must rise by at least this fraction of the peak within
        /// the attack lookback — shots have a hard attack, music swells do not.
        /// The lookback must be longer than the RMS window, which smears the
        /// attack over its whole length.
        /// </summary>
        private const double MinAttackFraction = 0.35;

        private const double AttackLookbackSeconds = 0.08;

        public List<double> DetectShots(MonoAudio audio)
        {
            float[] envelope = ComputeRmsEnvelope(audio, out double hopSeconds);
            if (envelope.Length == 0)
            {
                return new List<double>();
            }

            float median = Percentile(envelope, 0.5);
            float max = envelope.Max();
            double floorThreshold = Math.Max(median * PeakOverMedianFactor, max * PeakOverMaxFraction);

            List<KeyValuePair<double, float>> candidates = new List<KeyValuePair<double, float>>();
            int attackLookback = Math.Max(1, (int)Math.Round(AttackLookbackSeconds / hopSeconds));

            for (int i = 1; i < envelope.Length - 1; i++)
            {
                bool isLocalMax = envelope[i] >= envelope[i - 1] && envelope[i] >= envelope[i + 1];
                if (!isLocalMax || envelope[i] < floorThreshold)
                {
                    continue;
                }

                float before = envelope[Math.Max(0, i - attackLookback)];
                bool hasHardAttack = envelope[i] - before >= envelope[i] * MinAttackFraction;
                if (!hasHardAttack)
                {
                    continue;
                }

                candidates.Add(new KeyValuePair<double, float>(i * hopSeconds, envelope[i]));
            }

            return MergeNearbyPeaks(candidates);
        }

        internal static float[] ComputeRmsEnvelope(MonoAudio audio, out double hopSeconds)
        {
            int window = Math.Max(1, (int)(audio.SampleRate * WindowSeconds));
            int hop = Math.Max(1, (int)(audio.SampleRate * HopSeconds));
            hopSeconds = (double)hop / audio.SampleRate;

            int frameCount = Math.Max(0, (audio.Samples.Length - window) / hop);
            float[] envelope = new float[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                int start = i * hop;
                float sum = 0f;
                for (int j = 0; j < window; j++)
                {
                    float s = audio.Samples[start + j];
                    sum += s * s;
                }
                envelope[i] = (float)Math.Sqrt(sum / window);
            }
            return envelope;
        }

        /// <summary>
        /// Collapses peaks closer together than the minimum shot interval,
        /// keeping the loudest of each cluster.
        /// </summary>
        private static List<double> MergeNearbyPeaks(List<KeyValuePair<double, float>> candidates)
        {
            List<KeyValuePair<double, float>> merged = new List<KeyValuePair<double, float>>();
            foreach (KeyValuePair<double, float> candidate in candidates.OrderBy(c => c.Key))
            {
                if (merged.Count > 0 && candidate.Key - merged[merged.Count - 1].Key < MinShotIntervalSeconds)
                {
                    if (candidate.Value > merged[merged.Count - 1].Value)
                    {
                        merged[merged.Count - 1] = candidate;
                    }
                }
                else
                {
                    merged.Add(candidate);
                }
            }
            return merged.Select(c => c.Key).ToList();
        }

        private static float Percentile(float[] values, double percentile)
        {
            float[] sorted = (float[])values.Clone();
            Array.Sort(sorted);
            int index = (int)(percentile * (sorted.Length - 1));
            return sorted[index];
        }
    }
}
