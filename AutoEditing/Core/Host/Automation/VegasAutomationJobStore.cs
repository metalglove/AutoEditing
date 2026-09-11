using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;

namespace Core.Host.Automation
{
	/// <summary>
	/// Owns spool durability only. It never schedules work and never accesses VEGAS.
	/// A request is complete only after its producer atomically renames it to *.json.
	/// </summary>
	internal sealed class VegasAutomationJobStore(
        VegasAutomationConfiguration configuration,
        IVegasAutomationClock clock = null)
    {
		private readonly VegasAutomationConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
		private readonly IVegasAutomationClock _clock = clock ?? new SystemVegasAutomationClock();

        public void EnsureDirectories()
		{
			Directory.CreateDirectory(_configuration.RequestsDirectory);
			Directory.CreateDirectory(_configuration.RunningDirectory);
			Directory.CreateDirectory(_configuration.ResponsesDirectory);
		}

		public IReadOnlyList<string> GetPendingRequestPaths()
		{
			EnsureDirectories();
			return Directory.EnumerateFiles(_configuration.RequestsDirectory, "*.json", SearchOption.TopDirectoryOnly)
				.OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
				.ToArray();
		}

		public bool TryAcquireDispatchLease(out IDisposable lease)
		{
			EnsureDirectories();
			string leasePath = Path.Combine(_configuration.RunningDirectory, ".host-dispatch.lock");
			try
			{
				lease = new FileStream(
					leasePath,
					FileMode.OpenOrCreate,
					FileAccess.ReadWrite,
					FileShare.None,
					1,
					FileOptions.None);
				return true;
			}
			catch (IOException)
			{
				lease = null;
				return false;
			}
		}

		public bool TryClaim(string requestPath, out VegasAutomationClaim claim)
		{
			claim = null;
			string source = RequireDirectChild(requestPath, _configuration.RequestsDirectory);
			string name = Path.GetFileName(source);
			string destination = Path.Combine(_configuration.RunningDirectory, name);
			try
			{
				File.Move(source, destination);
			}
			catch (IOException)
			{
				// Another host/process claimed it, or a stale claim with the same name exists.
				return false;
			}

			string rawJson = File.ReadAllText(destination, Encoding.UTF8);
			VegasJobEnvelope envelope = ContractSerializer.Deserialize<VegasJobEnvelope>(rawJson);
			claim = new VegasAutomationClaim(name, destination, rawJson, envelope);
			return true;
		}

		public VegasJobResponse FindCompletedByIdempotencyKey(string idempotencyKey)
		{
			if (string.IsNullOrWhiteSpace(idempotencyKey))
				return null;
			EnsureDirectories();
			string path = IdempotencyPath(idempotencyKey);
			if (!File.Exists(path))
				return null;
			try
			{
				return ContractSerializer.Deserialize<VegasJobResponse>(
					File.ReadAllText(path, Encoding.UTF8));
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Newtonsoft.Json.JsonException)
			{
				// A damaged journal entry is not valid idempotency evidence.
				return null;
			}
		}

		public void Complete(VegasAutomationClaim claim, VegasJobResponse response)
		{
			if (claim == null) throw new ArgumentNullException(nameof(claim));
			if (response == null) throw new ArgumentNullException(nameof(response));
			EnsureDirectories();

			string responsePath = ResponsePath(response.JobId);
			// The single-file journal entry is the authoritative completed-operation
			// record. Write it first so a crash cannot cause the operation to repeat.
			string idempotencyPath = IdempotencyPath(claim.Envelope.IdempotencyKey);
			if (!File.Exists(idempotencyPath))
				WriteAtomic(idempotencyPath, ContractSerializer.Serialize(response));
			WriteAtomic(responsePath, ContractSerializer.Serialize(response));
			DeleteClaim(claim);
		}

		public void RetainInvalidClaim(
			string runningPath,
			string jobId,
			string rawJson,
			VegasJobResponse failure)
		{
			if (failure == null) throw new ArgumentNullException(nameof(failure));
			string responsePath = ResponsePath(jobId);
			WriteAtomic(responsePath, ContractSerializer.Serialize(failure));
			WriteAtomic(responsePath + ".request.invalid.json", rawJson ?? "");
			if (File.Exists(runningPath))
				File.Delete(runningPath);
		}

		public void RetainInvalidClaim(string requestName, string rawJson, VegasJobResponse failure)
		{
			string safeName = Path.GetFileName(requestName);
			string runningPath = Path.Combine(_configuration.RunningDirectory, safeName);
			if (string.IsNullOrEmpty(rawJson) && File.Exists(runningPath))
			{
				try
				{
					rawJson = File.ReadAllText(runningPath, Encoding.UTF8);
				}
				catch (IOException)
				{
					rawJson = "";
				}
			}
			RetainInvalidClaim(runningPath, failure?.JobId, rawJson, failure);
		}

		public int RecoverStaleClaims()
		{
			EnsureDirectories();
			IDisposable lease;
			if (!TryAcquireDispatchLease(out lease))
				return 0;
			using (lease)
			{
				int recovered = 0;
				DateTimeOffset cutoff = _clock.UtcNow - _configuration.RunningLease;
				foreach (string runningPath in Directory.EnumerateFiles(
					_configuration.RunningDirectory, "*.json", SearchOption.TopDirectoryOnly))
				{
					if (File.GetLastWriteTimeUtc(runningPath) > cutoff.UtcDateTime)
						continue;

					string name = Path.GetFileName(runningPath);
					string requestPath = Path.Combine(_configuration.RequestsDirectory, name);
					if (File.Exists(requestPath))
						continue;
					File.Move(runningPath, requestPath);
					recovered++;
				}
				return recovered;
			}
		}

		private string ResponsePath(string jobId)
		{
			string safeJobId = NormalizeIdentifier(jobId);
			return Path.Combine(_configuration.ResponsesDirectory, safeJobId + ".response.json");
		}

		private string IdempotencyPath(string idempotencyKey)
		{
			string digest = ContractHash.Compute(new Newtonsoft.Json.Linq.JValue(idempotencyKey));
			return Path.Combine(_configuration.ResponsesDirectory, digest + ".idempotency.json");
		}

		private static void WriteAtomic(string destination, string content)
		{
			string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
			File.WriteAllText(temporary, content ?? "", new UTF8Encoding(false));
			try
			{
				if (File.Exists(destination))
					File.Replace(temporary, destination, null);
				else
					File.Move(temporary, destination);
			}
			finally
			{
				if (File.Exists(temporary))
					File.Delete(temporary);
			}
		}

		private static void DeleteClaim(VegasAutomationClaim claim)
		{
			if (File.Exists(claim.RunningPath))
				File.Delete(claim.RunningPath);
		}

		private static string NormalizeIdentifier(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return "unknown-" + Guid.NewGuid().ToString("N");
			foreach (char character in value)
				if (!(char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.'))
					return "invalid-" + Guid.NewGuid().ToString("N");
			return value;
		}

		private static string RequireDirectChild(string path, string directory)
		{
			string fullPath = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
			string parent = Path.GetDirectoryName(fullPath);
			if (!string.Equals(parent, Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("The request path is outside the requests directory.");
			return fullPath;
		}
	}
}
