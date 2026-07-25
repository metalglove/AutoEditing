namespace Core.Domain;

public class ShotDetectionConfig
{
	public string SfxRoot { get; set; }

	public double PreRollSeconds { get; set; } = 1.25;

	public double PostRollSeconds { get; set; } = 0.75;

	public double MinVelocity { get; set; } = 0.35;

	public double MaxVelocity { get; set; } = 2.0;
}
