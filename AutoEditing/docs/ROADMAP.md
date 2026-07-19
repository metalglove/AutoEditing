# Auto Editor Roadmap — CoD Sniper Montages in VEGAS Pro 20

Self-contained plan for continuing this work in any session. Written 2026-07-19.
Companion doc: [vegas-scripting-effects-api.md](vegas-scripting-effects-api.md)
(verified VEGAS 20 scripting API research with sources; UNVERIFIED items are
marked there explicitly).

## Context — where the project stands

Pipeline (working, verified via harness against real clips):
**parse → beat detection → shot detection → plan → build timeline**

- Test data: `C:\Users\mario\Videos\edit` — 10 real MWIII clips + song
  `Traveller - Never Cared (2002).mp3` (detected 94.6 BPM, first beat 0.488s).
- `Core/Domain/Audio/` (AudioLoader, BeatDetector, ShotDetector) and
  `Core/Domain/Editing/MontagePlanner.cs` are deliberately **VEGAS-free** and are
  compiled directly into `Tools/AnalysisHarness` (net48 console) for testing
  without VEGAS. Harness also has `--debug-tempo <song>` and
  `--debug-shots <clip>` tuning commands.
- `TimelineBuilder`/`MontageOrchestrator` execute the plan in VEGAS
  (tracks, events, take offsets). Compiles; needs a first smoke run in VEGAS.
- `EffectsApplier` is still all placeholders (log-only).
- Naming: `Player - Game - Map - GUN TYPE [SEQ] [(notes)].mp4`. Placement is
  marked ONLY by `[OPENER]`/`[CLOSER]` filename prefixes. Type words like
  "Ender" (= game-ending kill, e.g. "nosc ender") are just clip type — do NOT
  infer placement from them (this was a bug, fixed).
- Build: `dotnet build Core/Core.csproj` and `dotnet build Tools/AnalysisHarness`
  from the `AutoEditing/` folder. Style gate: **no `var` keyword anywhere**
  (pre-build script fails the build). NAudio 2.2.1 DLLs are hint-path referenced
  from `AutoEditing/packages/` (gitignored; repopulate from
  `%USERPROFILE%\.nuget\packages` if missing).

Known limitation: ShotDetector finds loud shots/impacts, not kills — clips with
lots of firing over-count (e.g. a "Triple" clip detecting 12 transients). Only
the first-shot alignment is currently reliable. Phase 4 fixes this.

## The API fact that shapes everything

**Velocity envelopes never change `VideoEvent.Length`.** The event consumes
source media equal to the integral of the velocity curve over its fixed
timeline length. There is no built-in compensation API. Therefore:

- Timeline slot lengths stay beat-quantized (set by the planner, as today).
- The planner must solve "which source window + speed profile makes the kill
  land on beat N" by integrating the profile (trapezoid integrals for
  piecewise-linear velocity). Pure math → unit-test it in the harness.

API (verified): `Envelope env = new Envelope(EnvelopeType.Velocity);`
`videoEvent.Envelopes.Add(env);` points are `EnvelopePoint(Timecode, double y,
CurveType)` with y as a speed fraction: `1.0` = 100%, `0.0` = freeze,
negative = reverse, max `10.0`.

## Phase 0 — VEGAS smoke test (user, ~10 min)

Build, copy `Core.dll` to the VEGAS scripts folder (see README / `.vscode/copy-dll.ps1`),
run on the test folder. Verify: clips appear at planned beat positions and each
clip's **source window** matches the harness plan output (take offsets working).
First place to look if source positions are wrong: `Take.Offset` semantics in
`TimelineBuilder.PlaceClip`.

## Phase 1 — Velocity time-remapping (quickscope feel) ← START HERE

1. **Planner**: add a `SpeedProfile` (piecewise-linear velocity points in source
   time) to each `ClipPlacement`. Default profile: ~1.2× lead-in, ramp to ~0.35×
   just before the (first) kill, kill frame at the slow point ON the beat, snap
   back to 1× on the next beat. Enders/multis: deeper dip or beat-long freeze
   (y=0.0). Solve source-offset/consumption analytically; assert in harness that
   kills land on beats after warping.
2. **EffectsApplier**: real `ApplyVelocityEnvelope(videoEvent, profile)` writing
   the envelope points with sensible `CurveType` (smooth into the dip, fast out).
3. **Frame-rate note**: 60fps source at 0.35× ≈ 21 unique fps — fine at 30fps
   render, visible at 60. Keep dips short (2–4 beats). Later option (free, no
   plugin): ffmpeg `minterpolate` pre-render for hero clips.

## Phase 2 — Impact effects, all procedural, no plugins

Gate this phase on the **probe script**: add a mode that dumps
`vegas.VideoFX` / `vegas.Generators` / `vegas.Transitions` plugin names + UIDs
and the `VideoMotion` keyframe API surface (reflection) to the log. One VEGAS
run resolves every UNVERIFIED item in the API doc; hardcode nothing before that.

- **Punch-in zoom on beats**: `VideoEvent.VideoMotion` keyframes — scale ~108%
  for 2–3 frames on the beat, return. (Exact keyframe/vertex API: see probe.)
- **Camera shake at kills**: same API — burst of small random position/rotation
  keyframes over ~8 frames, decaying amplitude. Procedural, no BCC.
- **White flash on cuts**: 2–3 frame Solid Color generator events with fades on
  an overlay track (generator UID via probe).
- **Transitions on section changes**: verified pattern
  `videoEvent.FadeIn.Transition = new Effect(plugInNode)` over an event overlap.
  Use sparingly (e.g. every 8th cut / section boundaries), not every cut.

## Phase 3 — Polish

- **Nametags**: "Titles & Text" generator; its `Text` OFX parameter takes **RTF**
  — stage the string in a `System.Windows.Forms.RichTextBox`, assign `.Rtf` to
  the `OFXStringParameter.Value`. Show player + gun + type for ~2s per clip.
- **Grade**: track-level Color Curves, or VEGAS LUT Filter with a user `.cube`.
- **Render button**: verified `Renderer`/`RenderTemplate`/`RenderArgs` +
  `Vegas.Render()` pattern → queue an MP4 render from the script UI.

## Free plugins — conclusion: skip (researched, mostly dead ends)

- HitFilm Ignite Express: dead (site gone; broken on modern VEGAS).
- RGBurst / DataStorm (RGB-split/glitch OFX): paid.
- VEGAS 20 bundle (Continuum Primatte Studio): a keyer; not montage material.
- Everything in Phases 1–3 needs zero plugins. Optional later experiment: fake
  chromatic aberration with 3 stacked channel-isolated copies offset a few px.

## Phase 4 — Detection v2: per-gun sound templates + hitmarker matching

User has offered to record gun sounds. Design:

- Templates in e.g. `C:\Users\mario\Videos\edit\templates\<GUN> 01.wav`
  (filename gives the gun; 2–3 variants per gun: indoor/outdoor). The parser
  already knows each clip's gun → match only that gun's templates.
- Keep current transient detector as a **high-recall candidate finder** (lower
  thresholds), then **verify** each candidate: compare log-spectral envelopes of
  attack + ~150ms early decay against the template (cosine similarity), max over
  templates. Ignore the reverb tail — that's what varies indoor vs outdoor; the
  attack spectrum is the stable signature. Raw waveform cross-correlation would
  NOT be robust; spectral-envelope matching is.
- **Bigger win — ask user for MWIII hitmarker / kill-confirmed UI sounds**:
  game-UI audio is identical every time (acoustics-independent) and marks
  KILLS, not shots. This fixes over-counting directly and enables per-kill beat
  sync (every kill on a beat via Phase 1 velocity dips, not just the first).
- Tuning workflow: `--debug-shots` baselines are recorded in
  `.claude` project memory (shot-detection-tuning); current constants:
  RMS 40ms/10ms, attack lookback 80ms (must exceed RMS window smear),
  peak > 3.5× median, > 0.30× max, rise ≥ 35% over 80ms, merge 0.35s.

## Phase 5 — Musical structure

- Cut on **downbeats** (bars = 4 beats), not arbitrary beats.
- Song sections via energy over 4-bar windows: intro/build/drop. Place best
  clips (most kills, `7mult`, `ender` types) at drops; opener clip at intro.
- Weight slot length by clip type; consider "same game" sequence adjacency.

## Execution model

Order: **0 → 1 → probe → 2 → 3 → 4 → 5**.

Delegation (user preference): orchestrate with the main session; delegate
implementation/research to subagents — the user wants the **codex plugin**
(`openai/codex-plugin-cc`) used for task delegation once installed (install via
`/plugin marketplace add openai/codex-plugin-cc` in the interactive CLI; it was
not available in the previous environment). Until then: general-purpose
subagents with harness-verified acceptance criteria.

Verification per phase: harness output for anything VEGAS-free; one quick VEGAS
run by the user for timeline/effect changes (only the user can see the
timeline). Never mark VEGAS-side work verified without that run.
