using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AutoEditing.Iteration.Contracts.Automation;

namespace Core.Host.Automation
{
	internal static class VegasProjectIdentity
	{
		public static VegasHostIdentity Create(
			string machineName,
			int processId,
			string vegasVersion,
			string projectPath)
		{
			if (string.IsNullOrWhiteSpace(machineName))
				throw new ArgumentException("A machine name is required.", nameof(machineName));
			if (processId <= 0)
				throw new ArgumentOutOfRangeException(nameof(processId));

			string normalizedPath = NormalizePath(projectPath);
			string stableIdentity = string.IsNullOrEmpty(normalizedPath)
				? "unsaved\n" + machineName.ToUpperInvariant() + "\n" +
					processId.ToString(System.Globalization.CultureInfo.InvariantCulture)
				: "saved\n" + machineName.ToUpperInvariant() + "\n" +
					normalizedPath.ToUpperInvariant();
			return new VegasHostIdentity
			{
				MachineName = machineName,
				ProcessId = processId,
				VegasVersion = vegasVersion ?? "",
				ProjectPath = normalizedPath,
				ProjectFingerprint = Sha256(stableIdentity)
			};
		}

		private static string NormalizePath(string projectPath)
		{
			if (string.IsNullOrWhiteSpace(projectPath)) return "";
			return Path.GetFullPath(projectPath.Trim());
		}

		private static string Sha256(string value)
		{
			using (SHA256 hash = SHA256.Create())
			{
				byte[] bytes = hash.ComputeHash(new UTF8Encoding(false).GetBytes(value));
				return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
			}
		}
	}
}
