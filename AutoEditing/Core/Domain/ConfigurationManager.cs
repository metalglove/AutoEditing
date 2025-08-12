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
        /// Looks for config files in the same directory as the executing assembly.
        /// </summary>
        private static void LoadConfiguration()
        {
            try
            {
                // Get the directory where the current assembly (Core.dll) is located
                string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
                string configPath = Path.Combine(assemblyDirectory, ConfigFileName);
                string localConfigPath = Path.Combine(assemblyDirectory, LocalConfigFileName);
                
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
        /// Local values will override base values when they are not null or empty.
        /// </summary>
        private static void MergeConfigurations(AppSettings baseConfig, AppSettings localConfig)
        {
            if (localConfig == null) return;

            // Merge QuickTesting settings
            if (localConfig.QuickTesting != null)
            {
                if (baseConfig.QuickTesting == null)
                    baseConfig.QuickTesting = new QuickTestingConfig();

                if (!string.IsNullOrWhiteSpace(localConfig.QuickTesting.ClipsFolder))
                    baseConfig.QuickTesting.ClipsFolder = localConfig.QuickTesting.ClipsFolder;
                
                if (!string.IsNullOrWhiteSpace(localConfig.QuickTesting.SongPath))
                    baseConfig.QuickTesting.SongPath = localConfig.QuickTesting.SongPath;
                
                if (!string.IsNullOrWhiteSpace(localConfig.QuickTesting.OutputFolder))
                    baseConfig.QuickTesting.OutputFolder = localConfig.QuickTesting.OutputFolder;
            }

            // Merge Logging settings
            if (localConfig.Logging != null)
            {
                if (baseConfig.Logging == null)
                    baseConfig.Logging = new LoggingConfig();

                // Merge LogLevel
                if (localConfig.Logging.LogLevel != null)
                {
                    if (baseConfig.Logging.LogLevel == null)
                        baseConfig.Logging.LogLevel = new LogLevelConfig();

                    if (!string.IsNullOrWhiteSpace(localConfig.Logging.LogLevel.Default))
                        baseConfig.Logging.LogLevel.Default = localConfig.Logging.LogLevel.Default;
                }

                // Merge LogFile settings
                if (localConfig.Logging.LogFile != null)
                {
                    if (baseConfig.Logging.LogFile == null)
                        baseConfig.Logging.LogFile = new LogFileConfig();

                    if (!string.IsNullOrWhiteSpace(localConfig.Logging.LogFile.Path))
                        baseConfig.Logging.LogFile.Path = localConfig.Logging.LogFile.Path;
                    
                    // For boolean values, always take the local value if the local config has this section
                    baseConfig.Logging.LogFile.Enabled = localConfig.Logging.LogFile.Enabled;
                }
            }
        }

        /// <summary>
        /// Gets the log file path from configuration.
        /// If not configured, defaults to a Logs folder relative to the assembly location.
        /// </summary>
        public static string GetLogFilePath()
        {
            if (!string.IsNullOrWhiteSpace(_config?.Logging?.LogFile?.Path))
            {
                return _config.Logging.LogFile.Path;
            }
            
            // Default to Logs folder relative to the assembly location
            string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
            return Path.Combine(assemblyDirectory, "Logs", "autoediting.log");
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
                if (_config.Logging.LogLevel != null)
                    result["Logging:LogLevel:Default"] = _config.Logging.LogLevel.Default ?? "";
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
