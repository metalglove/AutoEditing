using System;
using System.IO;
using System.Windows.Forms;

namespace Core.Scripts
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

        public static void SetLogger(RichTextBox logBox)
        {
            _logBox = logBox;
        }

        public static void SetLogFile(string logFilePath)
        {
            _logFilePath = logFilePath;
            // Clear the log file on each run
            try
            {
                File.WriteAllText(_logFilePath, string.Empty);
            }
            catch { /* Optionally handle file IO errors */ }
        }

        public static void Log(string message)
        {
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