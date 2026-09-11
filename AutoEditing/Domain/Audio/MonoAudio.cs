namespace Core.Domain.Audio;

public class MonoAudio
{
	public float[] Samples { get; set; }

	public int SampleRate { get; set; }

	public double DurationSeconds => (double)Samples.Length / (double)SampleRate;
}
