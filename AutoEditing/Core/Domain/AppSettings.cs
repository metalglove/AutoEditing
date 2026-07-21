namespace Core.Domain;

public class AppSettings
{
	public LoggingConfig Logging { get; set; }

	public QuickTestingConfig QuickTesting { get; set; }

	public ShotDetectionConfig ShotDetection { get; set; }
}
