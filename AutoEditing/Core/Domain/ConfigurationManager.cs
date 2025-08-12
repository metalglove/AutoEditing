using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Core.Domain
{
    /// <summary>
    /// Configuration manager for reading appsettings.json file using Newtonsoft.Json.
    /// </summary>
    public static class ConfigurationManager
    {
        private static AppSettings _config;
        private static readonly string ConfigFileName = "appsettings.json";
        private static readonly string LocalConfigFileName = "appsettings.local.json";

        static ConfigurationManager()
        {
            LoadConfiguration();
        }

        /// <summary>
        /// Loads the configuration from appsettings.json file and optionally merges with appsettings.local.json.
        /// </summary>
        private static void LoadConfiguration()
        {
            try
            {
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string configPath = Path.Combine(baseDirectory, ConfigFileName);
                string localConfigPath = Path.Combine(baseDirectory, LocalConfigFileName);
                
                _config = new AppSettings();
                
                // Load base configuration
                if (File.Exists(configPath))
                {
                    string baseContent = File.ReadAllText(configPath);
                    _config = JsonConvert.DeserializeObject<AppSettings>(baseContent) ?? new AppSettings();
                }

                // Merge with local configuration if it exists (this will override base values)
                if (File.Exists(localConfigPath))
                {
                    string localContent = File.ReadAllText(localConfigPath);
                    AppSettings localConfig = JsonConvert.DeserializeObject<AppSettings>(localContent);
                    
                    if (localConfig != null)
                    {
                        MergeConfigurations(_config, localConfig);
                    }
                }
            }
            catch (Exception)
            {
                // Fallback to empty config if parsing fails
                _config = new AppSettings();
            }
        }

        /// <summary>
        /// Merges local configuration into base configuration.
        /// </summary>
        private static void MergeConfigurations(AppSettings baseConfig, AppSettings localConfig)
        {
            if (localConfig == null) return;

            // Merge QuickTesting settings
            if (localConfig.QuickTesting != null)
            {
                if (baseConfig.QuickTesting == null)
                    baseConfig.QuickTesting = new QuickTestingConfig();

                if (!string.IsNullOrEmpty(localConfig.QuickTesting.ClipsFolder))
                    baseConfig.QuickTesting.ClipsFolder = localConfig.QuickTesting.ClipsFolder;
                
                if (!string.IsNullOrEmpty(localConfig.QuickTesting.SongPath))
                    baseConfig.QuickTesting.SongPath = localConfig.QuickTesting.SongPath;
                
                if (!string.IsNullOrEmpty(localConfig.QuickTesting.OutputFolder))
                    baseConfig.QuickTesting.OutputFolder = localConfig.QuickTesting.OutputFolder;
            }

            // Merge Logging settings
            if (localConfig.Logging != null)
            {
                if (baseConfig.Logging == null)
                    baseConfig.Logging = new LoggingConfig();

                if (!string.IsNullOrEmpty(localConfig.Logging.LogLevel))
                    baseConfig.Logging.LogLevel = localConfig.Logging.LogLevel;

                if (localConfig.Logging.LogFile != null)
                {
                    if (baseConfig.Logging.LogFile == null)
                        baseConfig.Logging.LogFile = new LogFileConfig();

                    if (!string.IsNullOrEmpty(localConfig.Logging.LogFile.Path))
                        baseConfig.Logging.LogFile.Path = localConfig.Logging.LogFile.Path;
                }
            }
        }

        /// <summary>
        /// Gets the log file path from configuration.
        /// </summary>
        public static string GetLogFilePath()
        {
            return _config?.Logging?.LogFile?.Path ?? 
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "autoediting.log");
        }

        /// <summary>
        /// Gets whether file logging is enabled.
        /// </summary>
        public static bool IsFileLoggingEnabled()
        {
            return _config?.Logging?.LogFile?.Enabled ?? true;
        }

        /// <summary>
        /// Gets the quick testing clips folder path.
        /// </summary>
        public static string GetQuickTestingClipsFolder()
        {
            return _config?.QuickTesting?.ClipsFolder ?? @"C:\Users\Downloads\TestClips\";
        }

        /// <summary>
        /// Gets the quick testing song path.
        /// </summary>
        public static string GetQuickTestingSongPath()
        {
            return _config?.QuickTesting?.SongPath ?? @"C:\Users\Downloads\testsong.mp3";
        }

        /// <summary>
        /// Gets the output folder path.
        /// </summary>
        public static string GetOutputFolder()
        {
            return _config?.QuickTesting?.OutputFolder ?? @"C:\Users\Downloads\Output\";
        }

        /// <summary>
        /// Reloads the configuration from file.
        /// </summary>
        public static void ReloadConfiguration()
        {
            LoadConfiguration();
        }

        /// <summary>
        /// Debug method to get all configuration keys and values.
        /// </summary>
        public static Dictionary<string, string> GetAllConfigurationValues()
        {
            var result = new Dictionary<string, string>();
            
            if (_config == null) return result;

            // Manually flatten the configuration for debugging
            if (_config.Logging?.LogFile != null)
            {
                result["Logging:LogFile:Enabled"] = _config.Logging.LogFile.Enabled.ToString();
                result["Logging:LogFile:Path"] = _config.Logging.LogFile.Path ?? "";
            }

            if (_config.Logging != null)
            {
                result["Logging:LogLevel"] = _config.Logging.LogLevel ?? "";
            }

            if (_config.QuickTesting != null)
            {
                result["QuickTesting:ClipsFolder"] = _config.QuickTesting.ClipsFolder ?? "";
                result["QuickTesting:SongPath"] = _config.QuickTesting.SongPath ?? "";
                result["QuickTesting:OutputFolder"] = _config.QuickTesting.OutputFolder ?? "";
            }

            return result;
        }
    }
}
