using System.Collections.Generic;
using Core.Domain.Clip;

namespace Core.Domain.Audio;

// Keeps the legacy MontageOrchestrator entry point source-compatible while its
// storage is transparently backed by the central content-keyed library.
public sealed class ClipShotAnalysis
{
	public List<ShotEvent> Events { get; set; } = new List<ShotEvent>();
}

public sealed class ShotAnalysisSidecar
{
	private readonly ClipSyncLibrary _library;

	private ShotAnalysisSidecar(ClipSyncLibrary library)
	{
		_library = library;
	}

	public static ShotAnalysisSidecar Load(string clipsFolder)
	{
		return new ShotAnalysisSidecar(ClipSyncLibrary.Load());
	}

	public ClipShotAnalysis FindValid(string clipPath, string templateFingerprint)
	{
		ClipSyncEntry entry = _library.Find(clipPath, templateFingerprint);
		if (entry == null || entry.State != ClipSyncState.Ready)
		{
			return null;
		}
		return new ClipShotAnalysis { Events = new List<ShotEvent>(entry.Events) };
	}
}
