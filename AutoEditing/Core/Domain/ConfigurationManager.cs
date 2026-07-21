using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace Core.Domain;

public static class ConfigurationManager
{
	private static AppSettings _config;

	private static readonly string ConfigFileName;

	private static readonly string LocalConfigFileName;

	static ConfigurationManager()
	{
		ConfigFileName = "appsettings.json";
		LocalConfigFileName = "appsettings.local.json";
		LoadConfiguration();
	}

	private static void LoadConfiguration()
	{
		try
		{
			string location = Assembly.GetExecutingAssembly().Location;
			string directoryName = Path.GetDirectoryName(location);
			string path = Path.Combine(directoryName, ConfigFileName);
			string path2 = Path.Combine(directoryName, LocalConfigFileName);
			_config = new AppSettings();
			if (File.Exists(path))
			{
				string text = File.ReadAllText(path);
				_config = JsonConvert.DeserializeObject<AppSettings>(text) ?? new AppSettings();
			}
			if (File.Exists(path2))
			{
				string text2 = File.ReadAllText(path2);
				AppSettings appSettings = JsonConvert.DeserializeObject<AppSettings>(text2);
				if (appSettings != null)
				{
					MergeConfigurations(_config, appSettings);
				}
			}
		}
		catch (Exception)
		{
			_config = new AppSettings();
		}
	}

	private static void MergeConfigurations(AppSettings baseConfig, AppSettings localConfig)
	{
		if (localConfig == null)
		{
			return;
		}
		if (localConfig.QuickTesting != null)
		{
			if (baseConfig.QuickTesting == null)
			{
				baseConfig.QuickTesting = new QuickTestingConfig();
			}
			if (!string.IsNullOrWhiteSpace(localConfig.QuickTesting.ClipsFolder))
			{
				baseConfig.QuickTesting.ClipsFolder = localConfig.QuickTesting.ClipsFolder;
			}
			if (!string.IsNullOrWhiteSpace(localConfig.QuickTesting.SongPath))
			{
				baseConfig.QuickTesting.SongPath = localConfig.QuickTesting.SongPath;
			}
			if (!string.IsNullOrWhiteSpace(localConfig.QuickTesting.OutputFolder))
			{
				baseConfig.QuickTesting.OutputFolder = localConfig.QuickTesting.OutputFolder;
			}
		}
		if (localConfig.ShotDetection != null)
		{
			if (baseConfig.ShotDetection == null)
			{
				baseConfig.ShotDetection = new ShotDetectionConfig();
			}
			if (!string.IsNullOrWhiteSpace(localConfig.ShotDetection.SfxRoot))
			{
				baseConfig.ShotDetection.SfxRoot = localConfig.ShotDetection.SfxRoot;
			}
			if (localConfig.ShotDetection.PreRollSeconds > 0.0)
			{
				baseConfig.ShotDetection.PreRollSeconds = localConfig.ShotDetection.PreRollSeconds;
			}
			if (localConfig.ShotDetection.PostRollSeconds > 0.0)
			{
				baseConfig.ShotDetection.PostRollSeconds = localConfig.ShotDetection.PostRollSeconds;
			}
			if (localConfig.ShotDetection.MinVelocity > 0.0)
			{
				baseConfig.ShotDetection.MinVelocity = localConfig.ShotDetection.MinVelocity;
			}
			if (localConfig.ShotDetection.MaxVelocity > 0.0)
			{
				baseConfig.ShotDetection.MaxVelocity = localConfig.ShotDetection.MaxVelocity;
			}
		}
		if (localConfig.Logging == null)
		{
			return;
		}
		if (baseConfig.Logging == null)
		{
			baseConfig.Logging = new LoggingConfig();
		}
		if (localConfig.Logging.LogLevel != null)
		{
			if (baseConfig.Logging.LogLevel == null)
			{
				baseConfig.Logging.LogLevel = new LogLevelConfig();
			}
			if (!string.IsNullOrWhiteSpace(localConfig.Logging.LogLevel.Default))
			{
				baseConfig.Logging.LogLevel.Default = localConfig.Logging.LogLevel.Default;
			}
		}
		if (localConfig.Logging.LogFile != null)
		{
			if (baseConfig.Logging.LogFile == null)
			{
				baseConfig.Logging.LogFile = new LogFileConfig();
			}
			if (!string.IsNullOrWhiteSpace(localConfig.Logging.LogFile.Path))
			{
				baseConfig.Logging.LogFile.Path = localConfig.Logging.LogFile.Path;
			}
			baseConfig.Logging.LogFile.Enabled = localConfig.Logging.LogFile.Enabled;
		}
	}

	public static string GetLogFilePath()
	{
		if (!string.IsNullOrWhiteSpace(_config?.Logging?.LogFile?.Path))
		{
			return _config.Logging.LogFile.Path;
		}
		string location = Assembly.GetExecutingAssembly().Location;
		string directoryName = Path.GetDirectoryName(location);
		return Path.Combine(directoryName, "Logs", "autoediting.log");
	}

	public static bool IsFileLoggingEnabled()
	{
		return _config?.Logging?.LogFile?.Enabled ?? true;
	}

	public static string GetQuickTestingClipsFolder()
	{
		return _config?.QuickTesting?.ClipsFolder ?? "C:\\VEGAS\\edit";
	}

	public static string GetQuickTestingSongPath()
	{
		return _config?.QuickTesting?.SongPath ?? "C:\\VEGAS\\edit\\song.mp3";
	}

	public static ShotDetectionConfig GetShotDetection()
	{
		return _config?.ShotDetection ?? new ShotDetectionConfig
		{
			SfxRoot = "C:\\VEGAS\\sounds\\MWIII Snipers SFX"
		};
	}

	public static string GetOutputFolder()
	{
		return _config?.QuickTesting?.OutputFolder ?? "C:\\VEGAS\\edit\\Output\\";
	}

	public static void ReloadConfiguration()
	{
		LoadConfiguration();
	}

	public static Dictionary<string, string> GetAllConfigurationValues()
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		if (_config == null)
		{
			return dictionary;
		}
		if (_config.Logging?.LogFile != null)
		{
			dictionary["Logging:LogFile:Enabled"] = _config.Logging.LogFile.Enabled.ToString();
			dictionary["Logging:LogFile:Path"] = _config.Logging.LogFile.Path ?? "";
		}
		if (_config.Logging != null && _config.Logging.LogLevel != null)
		{
			dictionary["Logging:LogLevel:Default"] = _config.Logging.LogLevel.Default ?? "";
		}
		if (_config.ShotDetection != null)
		{
			dictionary["ShotDetection:SfxRoot"] = _config.ShotDetection.SfxRoot ?? "";
			dictionary["ShotDetection:PreRollSeconds"] = _config.ShotDetection.PreRollSeconds.ToString();
			dictionary["ShotDetection:PostRollSeconds"] = _config.ShotDetection.PostRollSeconds.ToString();
			dictionary["ShotDetection:MinVelocity"] = _config.ShotDetection.MinVelocity.ToString();
			dictionary["ShotDetection:MaxVelocity"] = _config.ShotDetection.MaxVelocity.ToString();
		}
		if (_config.QuickTesting != null)
		{
			dictionary["QuickTesting:ClipsFolder"] = _config.QuickTesting.ClipsFolder ?? "";
			dictionary["QuickTesting:SongPath"] = _config.QuickTesting.SongPath ?? "";
			dictionary["QuickTesting:OutputFolder"] = _config.QuickTesting.OutputFolder ?? "";
		}
		return dictionary;
	}
}
