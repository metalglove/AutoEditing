using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace Core.Domain.Audio;

public static class AudioLoader
{
	public static MonoAudio LoadMono(string filePath)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Expected O, but got Unknown
		MediaFoundationReader val = new MediaFoundationReader(filePath);
		try
		{
			ISampleProvider val2 = WaveExtensionMethods.ToSampleProvider((IWaveProvider)(object)val);
			int channels = val2.WaveFormat.Channels;
			int sampleRate = val2.WaveFormat.SampleRate;
			List<float> list = new List<float>((int)(((WaveStream)val).TotalTime.TotalSeconds * (double)sampleRate) + sampleRate);
			float[] array = new float[channels * 4096];
			int num;
			while ((num = val2.Read(array, 0, array.Length)) > 0)
			{
				for (int i = 0; i + channels <= num; i += channels)
				{
					float num2 = 0f;
					for (int j = 0; j < channels; j++)
					{
						num2 += array[i + j];
					}
					list.Add(num2 / (float)channels);
				}
			}
			return new MonoAudio
			{
				Samples = list.ToArray(),
				SampleRate = sampleRate
			};
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	public static double GetDurationSeconds(string filePath)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Expected O, but got Unknown
		MediaFoundationReader val = new MediaFoundationReader(filePath);
		try
		{
			return ((WaveStream)val).TotalTime.TotalSeconds;
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}
}
