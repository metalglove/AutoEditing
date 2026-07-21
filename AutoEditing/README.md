# AutoEditing

AutoEditing is a work-in-progress VEGAS Pro 20 extension for building Call of
Duty sniper montages from reviewed gameplay sync points.

The project currently combines local audio analysis, a docked shot-review
workflow, reusable clip metadata, beat-aware montage planning, and native VEGAS
timeline generation. Its longer-term direction is a guided semantic montage
assistant: the editor verifies meaningful gameplay events, aligns them with
music events, and lets the tool perform the repetitive timeline and retiming
work.

> Status: active prototype. The analysis and planning layers can be exercised
> outside VEGAS. Timeline and velocity behavior still require continued smoke
> testing in VEGAS Pro 20 before the extension should be considered production
> ready.

## Current workflow

The docked **AutoEditing Shot Review** extension presents a five-step wizard:

1. **Choose sources** — select a clips folder, song, and weapon SFX-template
   folder.
2. **SFX index** — index or validate the per-gun reference sounds used by shot
   detection.
3. **Analyze clips** — reuse already reviewed clips and detect candidate events
   for new or changed media.
4. **Review** — classify candidates as hit, headshot, or miss; adjust markers on
   the VEGAS timeline; add missed events; and mark completed clips ready.
5. **Clip drawer** — select reusable ready clips and build a montage.

The implemented pipeline is approximately:

```text
parse filenames
→ load or detect shot candidates
→ review and persist sync points
→ detect the song beat grid
→ assign reviewed kills to sequential beats
→ solve bounded speed profiles
→ create VEGAS tracks, events, take offsets, markers, and velocity envelopes
```

Detected candidates are suggestions. A clip cannot become ready until every
candidate is classified or removed and at least one reviewed hit or headshot is
present.

## What is implemented

### Clip ingestion and persistence

- Filename parsing for player, game, map, gun, play type, sequence, and notes.
- `[OPENER]` and `[CLOSER]` placement prefixes.
- A reusable clip sync library keyed by a lightweight content signature, with a
  fast path using file path, size, and last-write time.
- Template fingerprints that invalidate stale analysis after relevant SFX
  references change.
- Stored review state and reviewed hit, headshot, and miss events.
- User preferences for onboarding and known clip directories.

### Audio analysis

- Windows Media Foundation/NAudio decoding to mono PCM.
- Tempo and beat-grid detection using an onset envelope and autocorrelation.
- Per-gun SFX-template catalogs and calibration/indexing.
- High-recall shot candidate detection followed by template matching.
- Diagnostic commands for tempo and shot-detector tuning.

### Review UI

- A WPF wizard hosted inside a VEGAS `DockableControl`.
- Temporary `AE|` tracks, regions, and markers for clip review.
- Candidate reclassification, gun correction, deletion, cursor-based manual
  markers, marker refresh after timeline adjustment, and jump-to-marker actions.
- In-memory review drafts for outcome, gun, manual additions, and deletions,
  committed together when a clip is marked ready.
- Reuse of ready clips without laying every source clip out again.
- Progress, cancellation, logging, and onboarding UI.

### Planning and timeline generation

- Sequential beat assignment for all reviewed kills in a selected clip.
- Configurable pre-roll, post-roll, and velocity bounds.
- Piecewise-linear source/time mapping with bounded speed solving.
- Mathematical verification that planned reviewed kills land within 2 ms of
  their assigned beats before VEGAS generation.
- VEGAS video/audio track creation, media import, event placement, take offsets,
  song placement, and montage markers.
- Native velocity-envelope generation from the solved speed profile.

The planner and mapping mathematics are VEGAS-free so they can be tested in the
console harness.

## Not complete or not yet fully verified

- End-to-end timeline construction, take-offset behavior, velocity-envelope
  quantization, and undo behavior need more real VEGAS Pro 20 testing.
- Shot detection can still produce false positives and depends on good per-gun
  SFX templates plus human review.
- Beat detection produces a beat grid, not yet a complete musical model of
  downbeats, phrases, sections, or energy.
- Shake, name tags, color correction, and transitions remain logging/placeholding
  methods; they do not yet create the advertised visual treatments.
- Persisted reviewed shot events are more specialized than the planned general
  `GameplayEvent`/`MusicEvent` semantic model.
- Regenerating and removing an individual provenance-owned treatment is part of
  the planned MVP, not the current implementation.
- Optical flow, automatic narrative construction, learned clip ranking, sound
  enhancement, advanced transitions, grading, and render automation are
  deferred.

## Repository layout

```text
AutoEditing.sln
Core/
  Domain/
    Audio/       audio loading, beat detection, SFX templates, shot review
    Clip/        parsing, validation, reusable sync library
    Editing/     planning, speed mapping, timeline generation, effects adapter
    Logging/     application logging
  Scripts/       VEGAS extension entry point, dock host, WPF view and view model
  appsettings.json
Tools/
  AnalysisHarness/  VEGAS-free console runner and detector diagnostics
docs/
  ROADMAP.md
  vegas-scripting-effects-api.md
  semantic-montage/ current product, MVP, domain, research, and feasibility docs
```

## Prerequisites

- Windows
- VEGAS Pro 20 installed at the standard location
- .NET Framework 4.8 developer tooling
- Visual Studio 2019/2022, or the .NET/MSBuild tooling needed to build `net48`

`Core.csproj` references:

- `ScriptPortal.Vegas.dll` from the VEGAS Pro 20 installation;
- NAudio Core and Wasapi 2.2.1;
- Newtonsoft.Json 13.0.3.

Audio analysis is Windows-only because it uses Windows Media Foundation through
NAudio.

## Build and deploy

Run commands from this directory (`AutoEditing/`):

```powershell
dotnet build Core/Core.csproj --configuration Debug
dotnet build Tools/AnalysisHarness/AnalysisHarness.csproj --configuration Debug
```

Building `Core` invokes `.vscode/deploy-extension.ps1`, which copies the extension
and runtime dependencies to:

```text
Documents\Vegas Application Extensions
```

Close VEGAS before building so its loaded assemblies do not block deployment.
Restart VEGAS after deployment, then open:

```text
View → Extensions → AutoEditing Shot Review
```

The post-build deployment uses `ContinueOnError`, so a successful compilation
does not necessarily mean deployment succeeded. Read the build output for the
deployment result.

## Configuration

Defaults live in `Core/appsettings.json`. Create
`Core/appsettings.local.json` from `Core/appsettings.local.json.example` for
machine-specific overrides; the local file is intentionally ignored by Git.

Relevant settings include:

```json
{
  "QuickTesting": {
    "ClipsFolder": "C:/VEGAS/edit",
    "SongPath": "",
    "OutputFolder": "C:/VEGAS/edit/Output"
  },
  "ShotDetection": {
    "SfxRoot": "C:/VEGAS/sounds/MWIII Snipers SFX",
    "PreRollSeconds": 1.25,
    "PostRollSeconds": 0.75,
    "MinVelocity": 0.35,
    "MaxVelocity": 2.0
  }
}
```

The UI can override source paths for the active workflow.

## Clip naming convention

The current parser accepts:

```text
PlayerName - Game - Map - GUN [TYPE...] [SEQUENCE] [(notes)].mp4
```

Examples:

```text
Glovali - MWIII - Dome - MORS 6ON 001.mp4
Glovali - MWIII - Greece - XRK QUAD.mp4
[OPENER]Glovali - MWIII - Rio - KATT 5ON X2 001 (Triple).mp4
```

- `GUN` is the first word in the final details section.
- `TYPE` is the remaining play description.
- `SEQUENCE` is an optional zero-padded counter.
- Parenthesized text is stored as notes.
- Only `[OPENER]` and `[CLOSER]` control special placement. A word such as
  `Ender` inside the play type does not imply montage placement.

The older dash-separated gun/type convention remains supported by the parser.

## Analysis harness

The harness runs parsing, beat detection, shot detection, and planning without
launching VEGAS:

```powershell
dotnet build Tools/AnalysisHarness/AnalysisHarness.csproj
Tools/AnalysisHarness/bin/Debug/net48/AnalysisHarness.exe <clips-folder> <song-path> <sfx-root>
```

Arguments after the clips folder are optional when suitable configuration or an
MP3 in the clips folder supplies the missing value.

Diagnostics:

```powershell
Tools/AnalysisHarness/bin/Debug/net48/AnalysisHarness.exe --debug-tempo <song-path>
Tools/AnalysisHarness/bin/Debug/net48/AnalysisHarness.exe --debug-shots <clip-path>
```

Use the harness for detector tuning and planner verification. It cannot validate
VEGAS timeline behavior.

## Development rules

- The main project targets `net48` and uses file-scoped namespaces where the
  configured compiler supports them.
- The repository style gate disallows the `var` keyword; use explicit types.
- Keep analysis and planning logic independent of `ScriptPortal.Vegas` whenever
  possible so it remains harness-testable.
- Treat existing uncommitted changes as user work and avoid unrelated rewrites.
- Do not describe VEGAS-host behavior as verified until it has been observed in
  an actual VEGAS run.

## Documentation

- [Semantic montage documentation](docs/semantic-montage/README.md) — current
  product direction, evidence policy, MVP requirements, retiming model, and
  feasibility gates.
- [Development roadmap](docs/ROADMAP.md) — historical/current implementation
  sequence; some sections predate the semantic-montage documentation.
- [VEGAS scripting and effects API notes](docs/vegas-scripting-effects-api.md) —
  researched API behavior and explicitly unverified gaps.
- [VEGAS interaction contract](docs/vegas-interaction-contract.md) — required
  command/query/event architecture and extension rules for agents and contributors.

When documents disagree, use the semantic-montage MVP documents for product
scope and verify implementation claims against the current code.

## License

This repository is currently described as educational and personal-use work; no
standalone license file is present. VEGAS Pro and its scripting API are products
and trademarks of their respective owner.
