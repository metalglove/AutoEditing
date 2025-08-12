using System;
using System.IO;
using System.Windows.Forms;

namespace Core.Domain.Logging
{
    /// <summary>
    /// Logger class for logging messages to a RichTextBox control.
    /// </summary>
    /// <remarks>
    /// This class provides static methods to log debug and error messages to a RichTextBox.
    /// It should be initialized with a RichTextBox instance using SetLogger method.
    /// </remarks>
    /// <example>
    /// Logger.SetLogger(myRichTextBox);
    /// Logger.Log("This is a debug message.");
    /// Logger.LogError("This is an error message.", new Exception("Sample exception"));
    /// </example>
    ///
    public static class Logger
    {
        private static RichTextBox _logBox;
        private static string _logFilePath;
        private static bool _isInitialized = false;

        /// <summary>
        /// Initializes the logger with configuration settings.
        /// </summary>
        private static void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                if (ConfigurationManager.IsFileLoggingEnabled())
                {
                    _logFilePath = ConfigurationManager.GetLogFilePath();
                    
                    // Ensure the directory exists
                    string logDirectory = Path.GetDirectoryName(_logFilePath);
                    if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
                    {
                        Directory.CreateDirectory(logDirectory);
                    }

                    // Clear the log file on each run
                    File.WriteAllText(_logFilePath, string.Empty);
                }
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                // Fallback logging in case configuration fails
                _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autoediting.log");
                try
                {
                    File.WriteAllText(_logFilePath, $"Logger initialization error: {ex.Message}{Environment.NewLine}");
                }
                catch { /* Silent fail if we can't even write to fallback */ }
            }
        }

        public static void SetLogger(RichTextBox logBox)
        {
            _logBox = logBox;
            Initialize();
        }

        public static void Log(string message)
        {
            Initialize(); // Ensure logger is initialized
            
            string logMsg = "[DEBUG] " + message;
            // Log to UI
            if (_logBox != null)
            {
                _logBox.Invoke((Action)(() =>
                {
                    _logBox.SelectionStart = _logBox.TextLength;
                    _logBox.SelectionLength = 0;
                    _logBox.SelectionColor = System.Drawing.Color.Yellow;
                    _logBox.AppendText(logMsg + Environment.NewLine);
                    _logBox.SelectionColor = _logBox.ForeColor;
                }));
            }
            // Log to file
            if (!string.IsNullOrEmpty(_logFilePath))
            {
                try
                {
                    File.AppendAllText(_logFilePath, logMsg + Environment.NewLine);
                }
                catch { /* Optionally handle file IO errors */ }
            }
        }

        public static void LogError(string message, Exception ex = null)
        {
            Initialize(); // Ensure logger is initialized
            
            string errorMsg = ex != null ? $"{message}: {ex.Message}" : message;
            string logMsg = "[ERROR] " + errorMsg;
            // Log to UI
            if (_logBox != null)
            {
                _logBox.Invoke((Action)(() =>
                {
                    _logBox.SelectionStart = _logBox.TextLength;
                    _logBox.SelectionLength = 0;
                    _logBox.SelectionColor = System.Drawing.Color.Red;
                    _logBox.AppendText(logMsg + Environment.NewLine);
                    _logBox.SelectionColor = _logBox.ForeColor;
                }));
            }
            // Log to file
            if (!string.IsNullOrEmpty(_logFilePath))
            {
                try
                {
                    File.AppendAllText(_logFilePath, logMsg + Environment.NewLine);
                }
                catch { /* Optionally handle file IO errors */ }
            }
        }
    }
}