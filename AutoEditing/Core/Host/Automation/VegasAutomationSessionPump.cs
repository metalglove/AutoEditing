using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Host.Automation
{
	/// <summary>
	/// Manually driven discovery coordinator. It performs no polling of its own;
	/// the VEGAS host decides when it is safe to call PumpOnceAsync.
	/// </summary>
	internal sealed class VegasAutomationSessionPump
	{
		private readonly string _sessionsRoot;
		private readonly TimeSpan _runningLease;
		private readonly VegasAutomationHandler _handler;
		private readonly IVegasAutomationClock _clock;
		private readonly SemaphoreSlim _pumpGate = new SemaphoreSlim(1, 1);

		public VegasAutomationSessionPump(
			string sessionsRoot,
			TimeSpan runningLease,
			VegasAutomationHandler handler,
			IVegasAutomationClock clock = null)
		{
			if (string.IsNullOrWhiteSpace(sessionsRoot))
				throw new ArgumentException("An automation sessions root is required.", nameof(sessionsRoot));
			if (runningLease <= TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(runningLease));
			_sessionsRoot = Path.GetFullPath(sessionsRoot);
			_runningLease = runningLease;
			_handler = handler ?? throw new ArgumentNullException(nameof(handler));
			_clock = clock ?? new SystemVegasAutomationClock();
		}

		public async Task<bool> PumpOnceAsync(CancellationToken cancellationToken)
		{
			await _pumpGate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				foreach (string sessionRoot in DiscoverSessionRoots())
				{
					VegasAutomationConfiguration configuration =
						new VegasAutomationConfiguration(sessionRoot, _runningLease);
					VegasAutomationJobStore store =
						new VegasAutomationJobStore(configuration, _clock);
					store.RecoverStaleClaims();
					VegasAutomationDispatcher dispatcher =
						new VegasAutomationDispatcher(store, _handler, _clock);
					if (await dispatcher.TryProcessNextAsync(cancellationToken).ConfigureAwait(false))
						return true;
				}
				return false;
			}
			finally
			{
				_pumpGate.Release();
			}
		}

		private IReadOnlyList<string> DiscoverSessionRoots()
		{
			if (!Directory.Exists(_sessionsRoot))
				return Array.Empty<string>();
			return Directory.EnumerateDirectories(_sessionsRoot, "*", SearchOption.TopDirectoryOnly)
				.Select(Path.GetFullPath)
				.Where(IsDirectChild)
				.OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
				.ToArray();
		}

		private bool IsDirectChild(string path) =>
			string.Equals(
				Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
				_sessionsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
				StringComparison.OrdinalIgnoreCase);
	}
}
