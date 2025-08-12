namespace Core.Domain
{
    /// <summary>
    /// Configuration classes for deserializing appsettings.json
    /// </summary>
    public class AppSettings
    {
        public LoggingConfig Logging { get; set; }
        public QuickTestingConfig QuickTesting { get; set; }
    }

    public class LoggingConfig
    {
        public LogFileConfig LogFile { get; set; }
        public string LogLevel { get; set; }
    }

    public class LogFileConfig
    {
        public bool Enabled { get; set; }
        public string Path { get; set; }
    }

    public class QuickTestingConfig
    {
        public string ClipsFolder { get; set; }
        public string SongPath { get; set; }
        public string OutputFolder { get; set; }
    }
}
