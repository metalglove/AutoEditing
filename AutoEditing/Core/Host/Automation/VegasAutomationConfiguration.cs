using System;
using System.IO;

namespace Core.Host.Automation
{
	internal sealed class VegasAutomationConfiguration
	{
		public VegasAutomationConfiguration(string sessionRoot, TimeSpan runningLease)
		{
			if (string.IsNullOrWhiteSpace(sessionRoot))
				throw new ArgumentException("A session root is required.", nameof(sessionRoot));
			if (runningLease <= TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(runningLease));

			SessionRoot = Path.GetFullPath(sessionRoot);
			RequestsDirectory = ChildDirectory(SessionRoot, "ipc", "requests");
			RunningDirectory = ChildDirectory(SessionRoot, "ipc", "running");
			ResponsesDirectory = ChildDirectory(SessionRoot, "ipc", "responses");
			RunningLease = runningLease;
		}

		public string SessionRoot { get; }
		public string RequestsDirectory { get; }
		public string RunningDirectory { get; }
		public string ResponsesDirectory { get; }
		public TimeSpan RunningLease { get; }

		private static string ChildDirectory(string root, params string[] parts)
		{
			string path = root;
			foreach (string part in parts)
				path = Path.Combine(path, part);
			string fullPath = Path.GetFullPath(path);
			string boundary = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				+ Path.DirectorySeparatorChar;
			if (!fullPath.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("Automation directories must remain under the session root.");
			return fullPath;
		}
	}
}
