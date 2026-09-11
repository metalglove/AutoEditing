using System;
using System.IO;

namespace Core.Domain.Logging;

public static class Logger
{
	private static Action<string, bool> _sink;

	private static readonly object FileLock = new object();

	private static string _logFilePath;

	private static bool _isInitialized = false;

	private static void Initialize()
	{
		if (_isInitialized)
		{
			return;
		}
		try
		{
			if (ConfigurationManager.IsFileLoggingEnabled())
			{
				_logFilePath = ConfigurationManager.GetLogFilePath();
				string directoryName = Path.GetDirectoryName(_logFilePath);
				if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
				File.WriteAllText(_logFilePath, string.Empty);
			}
			_isInitialized = true;
		}
		catch (Exception ex)
		{
			_logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autoediting.log");
			try
			{
				File.WriteAllText(_logFilePath, "Logger initialization error: " + ex.Message + Environment.NewLine);
			}
			catch
			{
			}
		}
	}

	public static void SetSink(Action<string, bool> sink)
	{
		_sink = sink;
		Initialize();
	}

	public static void Log(string message)
	{
		Initialize();
		string text = "[DEBUG] " + message;
		Publish(text, isError: false);
		if (string.IsNullOrEmpty(_logFilePath))
		{
			return;
		}
		try
		{
			lock (FileLock)
			{
				File.AppendAllText(_logFilePath, text + Environment.NewLine);
			}
		}
		catch
		{
		}
	}

	private static void Publish(string message, bool isError)
	{
		Action<string, bool> sink = _sink;
		if (sink == null)
		{
			return;
		}
		try
		{
			sink(message, isError);
		}
		catch
		{
		}
	}

	public static void LogError(string message, Exception ex = null)
	{
		Initialize();
		string text = ((ex != null) ? FormatException(message, ex) : message);
		string text2 = "[ERROR] " + text;
		Publish(text2, isError: true);
		if (string.IsNullOrEmpty(_logFilePath))
		{
			return;
		}
		try
		{
			lock (FileLock)
			{
				File.AppendAllText(_logFilePath, text2 + Environment.NewLine);
			}
		}
		catch
		{
		}
	}

	private static string FormatException(string message, Exception exception)
	{
		string text = message;
		for (Exception ex = exception; ex != null; ex = ex.InnerException)
		{
			string text2 = ex.GetType().Name + " (0x" + ex.HResult.ToString("X8") + "): " + ex.Message;
			if (!text.Contains(text2))
			{
				text = text + " --> " + text2;
			}
		}
		if (!string.IsNullOrEmpty(exception.StackTrace))
		{
			text = text + Environment.NewLine + exception.StackTrace;
		}
		return text;
	}
}
