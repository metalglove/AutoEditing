using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace Core.Domain.Audio
{
    /// <summary>
    /// Mono PCM audio samples for analysis (beat/shot detection).
    /// </summary>
    public class MonoAudio
    {
        public float[] Samples { get; set; }
        public int SampleRate { get; set; }

        public double DurationSeconds
        {
            get { return (double)Samples.Length / SampleRate; }
        }
    }

    /// <summary>
    /// Decodes audio from media files (MP3, MP4, WAV, M4A, AAC) into mono PCM
    /// samples using Windows Media Foundation. Works for both songs and the
    /// audio track of video clips.
    /// </summary>
    public static class AudioLoader
    {
        public static MonoAudio LoadMono(string filePath)
        {
            using (MediaFoundationReader reader = new MediaFoundationReader(filePath))
            {
                ISampleProvider provider = reader.ToSampleProvider();
                int channels = provider.WaveFormat.Channels;
                int sampleRate = provider.WaveFormat.SampleRate;

                List<float> mono = new List<float>((int)(reader.TotalTime.TotalSeconds * sampleRate) + sampleRate);
                float[] buffer = new float[channels * 4096];
                int samplesRead;
                while ((samplesRead = provider.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i + channels <= samplesRead; i += channels)
                    {
                        float sum = 0f;
                        for (int c = 0; c < channels; c++)
                        {
                            sum += buffer[i + c];
                        }
                        mono.Add(sum / channels);
                    }
                }

                return new MonoAudio { Samples = mono.ToArray(), SampleRate = sampleRate };
            }
        }

        /// <summary>
        /// Returns the media duration in seconds without decoding the full file.
        /// </summary>
        public static double GetDurationSeconds(string filePath)
        {
            using (MediaFoundationReader reader = new MediaFoundationReader(filePath))
            {
                return reader.TotalTime.TotalSeconds;
            }
        }
    }
}
