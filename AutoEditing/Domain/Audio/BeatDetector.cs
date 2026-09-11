using System;

namespace Core.Domain.Audio;

public class BeatDetector
{
	internal const int FrameSize = 1024;

	internal const int HopSize = 512;

	private const double MinBpm = 60.0;

	private const double MaxBpm = 200.0;

	private const double MaxGridBpm = 150.0;

	public BeatGrid DetectBeats(MonoAudio audio)
	{
		double hopSeconds = 512.0 / (double)audio.SampleRate;
		float[] onsetEnvelope = ComputeOnsetEnvelope(audio.Samples);
		double num;
		for (num = EstimateBpm(onsetEnvelope, hopSeconds); num > 150.0; num /= 2.0)
		{
		}
		double num2 = 60.0 / num;
		double num3 = FindBeatPhase(onsetEnvelope, hopSeconds, num2);
		BeatGrid beatGrid = new BeatGrid
		{
			Bpm = num,
			FirstBeatOffsetSeconds = num3
		};
		for (double num4 = num3; num4 < audio.DurationSeconds; num4 += num2)
		{
			beatGrid.BeatTimesSeconds.Add(num4);
		}
		return beatGrid;
	}

	internal static float[] ComputeOnsetEnvelope(float[] samples)
	{
		int num = Math.Max(0, (samples.Length - 1024) / 512);
		float[] array = new float[num];
		for (int i = 0; i < num; i++)
		{
			int num2 = i * 512;
			float num3 = 0f;
			for (int j = 0; j < 1024; j++)
			{
				float num4 = samples[num2 + j];
				num3 += num4 * num4;
			}
			array[i] = num3;
		}
		float[] array2 = new float[num];
		for (int k = 1; k < num; k++)
		{
			float num5 = array[k] - array[k - 1];
			array2[k] = ((num5 > 0f) ? num5 : 0f);
		}
		NormalizeInPlace(array2);
		return array2;
	}

	private static void NormalizeInPlace(float[] values)
	{
		float num = 0f;
		for (int i = 0; i < values.Length; i++)
		{
			if (values[i] > num)
			{
				num = values[i];
			}
		}
		if (!(num <= 0f))
		{
			for (int j = 0; j < values.Length; j++)
			{
				values[j] /= num;
			}
		}
	}

	private static double EstimateBpm(float[] onsetEnvelope, double hopSeconds)
	{
		int num = (int)Math.Floor(0.3 / hopSeconds);
		int val = (int)Math.Ceiling(1.0 / hopSeconds);
		val = Math.Min(val, onsetEnvelope.Length / 2);
		if (val <= num)
		{
			return 120.0;
		}
		double[] array = new double[val + 1];
		for (int i = num; i <= val; i++)
		{
			double num2 = 0.0;
			for (int j = 0; j + i < onsetEnvelope.Length; j++)
			{
				num2 += (double)(onsetEnvelope[j] * onsetEnvelope[j + i]);
			}
			array[i] = num2 / (double)(onsetEnvelope.Length - i);
		}
		int num3 = num;
		double num4 = double.MinValue;
		for (int k = num; k <= val; k++)
		{
			double num5 = 60.0 / ((double)k * hopSeconds);
			double num6 = Math.Log(num5 / 130.0, 2.0);
			double num7 = Math.Exp(-0.5 * Math.Pow(num6 / 0.9, 2.0));
			double num8 = ((k * 2 <= val) ? (0.5 * array[k * 2]) : 0.0);
			double num9 = (array[k] + num8) * num7;
			if (num9 > num4)
			{
				num4 = num9;
				num3 = k;
			}
		}
		double num10 = num3;
		if (num3 > num && num3 < val)
		{
			double num11 = array[num3 - 1];
			double num12 = array[num3];
			double num13 = array[num3 + 1];
			double num14 = num11 - 2.0 * num12 + num13;
			if (Math.Abs(num14) > 1E-12)
			{
				num10 = (double)num3 + 0.5 * (num11 - num13) / num14;
			}
		}
		return 60.0 / (num10 * hopSeconds);
	}

	private static double FindBeatPhase(float[] onsetEnvelope, double hopSeconds, double beatIntervalSeconds)
	{
		int num = Math.Max(1, (int)Math.Round(beatIntervalSeconds / hopSeconds));
		double num2 = 0.0;
		double num3 = double.MinValue;
		for (int i = 0; i < num; i++)
		{
			double num4 = 0.0;
			for (double num5 = i; num5 < (double)onsetEnvelope.Length; num5 += beatIntervalSeconds / hopSeconds)
			{
				num4 += (double)onsetEnvelope[(int)num5];
			}
			if (num4 > num3)
			{
				num3 = num4;
				num2 = i;
			}
		}
		return num2 * hopSeconds;
	}
}
