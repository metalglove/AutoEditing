# Reviewed song-analysis model

The song-analysis domain is the persistence and planning boundary for musical
structure. It is deliberately independent of `ScriptPortal.Vegas`: analysis
produces proposals, the editor reviews them, and later planners consume only the
reviewed model. VEGAS markers and regions are projections of this data rather
than its source of truth.

## Core objects

`SongAnalysis` owns a fingerprinted `SongIdentity`, stable analysis ID, schema
version, timestamps, music events, and music regions.

A `MusicEvent` represents a point in the song. Supported initial types are beat,
downbeat, accent, transient, build hit, drop, phrase boundary, and manual sync
point. A `MusicRegion` represents a time range such as an intro, build-up,
action passage, climax, breakdown, cinematic passage, outro, or unused passage.

Both objects distinguish:

- their current editor-visible time and classification;
- their most recent detected time and classification;
- detected versus user-created origin;
- proposed, reviewed, or rejected state;
- small editorial metadata such as priority, lock state, and notes.

Changing a detected event therefore does not erase what the detector originally
proposed. More detailed effect and gameplay assignments belong to the later
editorial-sync-point model rather than this foundation.

## Stable identity and re-analysis

The BeatGrid compatibility adapter creates deterministic event IDs from the
song fingerprint and beat ordinal. General re-analysis uses
`SongAnalysisReconciler`:

1. Match new detector proposals to old detected objects by original type and a
   bounded time tolerance.
2. Reuse the existing stable ID.
3. Keep new detector evidence in the detected fields.
4. Preserve reviewed or locked editor times, classifications, and metadata.
5. Preserve manual objects and reviewed/locked objects missed by the new run.

Analyses for different content fingerprints cannot be reconciled.

## Persistence contract

`SongAnalysisStore` writes project-adjacent JSON sidecars using the suffix
`.autoediting.song-analysis.json`. Writes use a temporary file followed by an
atomic replacement. The store validates the schema, song identity, unique IDs,
and all event/region ranges before save and after load.

Schema version 1 is the first supported format. Unknown versions are rejected
with an explicit error rather than being interpreted as a compatible model.

## Verification

The VEGAS-free harness covers deterministic BeatGrid adaptation, JSON
round-tripping, schema rejection, and re-analysis reconciliation:

```powershell
Tools/AnalysisHarness/bin/Debug/net48/AnalysisHarness.exe --self-test-song-analysis
```

The next layer should expose this model through a song-review workflow. It must
continue to serialize all VEGAS access through the CQRS interaction boundary
described in [vegas-interaction-contract.md](vegas-interaction-contract.md).

## Initial structure proposals

`SongStructureAnalyzer` adds an intentionally explainable first analysis pass:

- onset strength is measured around every detected beat;
- the strongest repeating phase in groups of four proposes downbeats;
- strong local onset peaks propose accents and transients;
- RMS energy is summarized in four-bar phrase windows;
- relative energy and energy change propose intro, build-up, action, climax,
  breakdown, cinematic, and outro regions;
- region boundaries propose phrase events, while strong rises also propose a
  build hit and drop.

These are confidence-scored review candidates, not authoritative musical facts.
Short or effectively silent audio produces no invented tempo and is represented
as an `Unused` region.

Inspect a song without VEGAS:

```powershell
Tools/AnalysisHarness/bin/Debug/net48/AnalysisHarness.exe --debug-song <song-path>
```

Export the proposal as a validated sidecar:

```powershell
Tools/AnalysisHarness/bin/Debug/net48/AnalysisHarness.exe `
  --export-song-analysis <song-path> <output-json>
```
