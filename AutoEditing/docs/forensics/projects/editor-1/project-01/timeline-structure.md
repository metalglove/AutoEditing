# Timeline Structure — Editor 1 / Project 01

This document separates direct structural measurements from editorial interpretation. Evidence IDs
below are indexed in [evidence-register.md](evidence-register.md). Source material: Editor 1's
own project-structure forensics (originally `project-structure-forensics.md`) and this
investigation's adversarial-verification pass (`cinematic-pairing-analysis.md`,
`inferred-editing-strategies.md`).

## Track organization and compositing hierarchy

The project contains 5 tracks. Track 0 (video) is a single 210.8-second, full-length dirt/scratch
overlay clip using `CompositeMode=Screen` at `compositeLevel=1` — a global finishing layer, not a
per-event treatment. Track 1 (video, `CompositeMode=SrcAlpha`, standard) is the main edit and
carries essentially all editorial event structure. Tracks 2–4 are audio (SFX/retained audio, music,
whooshes respectively). Neither video track is an `isCompositingChild`/`isCompositingParent` of the
other — this is a simple two-layer composite (overlay over main picture), not a nested compositing
hierarchy. **[E1-P01-STR-001, DIRECT_PROJECT_OBSERVATION]**

Project pixel format is `Float32Bit` — all compositing (including the Screen-blend overlay and any
Glow/BlurMoCurves math) happens at high internal precision rather than 8-bit integer.
**[E1-P01-STR-002, DIRECT_PROJECT_OBSERVATION]**

## Main gameplay and cinematic tracks

Track 1 (276 events) is the main gameplay edit. Its events are sourced from two structurally
distinct asset tiers (re-derived directly from exported media folder paths):

- **Curated highlight clips**: `1 - openers` (3 files), `2 - middle` (17 files, `Quad 001`–`Quad
  017.mp4`), `3 - single single collat section` (3 files), `4 - closers` (1 file). These carry the
  project's per-event effect treatments (see [effects-and-presets.md](effects-and-presets.md)).
- **Raw/connective footage**: `cines` (2 files) and `map cines` (20 files), both raw or
  RIFE-frame-interpolated DVR gameplay captures. 22 Track-1 events are sourced from these two
  folders. **[E1-P01-STR-003, DIRECT_PROJECT_OBSERVATION]**

There is no dedicated "establishing shot"/environment-only b-roll category in this project — the
raw/connective folders contain player-gameplay captures, not empty-scene cinematography.
**[E1-P01-STR-004, DIRECT_PROJECT_OBSERVATION]**

## Global texture and effect layers

- Track 0's Screen-blend overlay (above) is the only track-level *compositing* treatment.
- Track 1 additionally carries two **track-level, always-on** Sapphire effects: `S_Shake`
  (Amplitude 0.06, Frequency 1.5, Tilt Rand Amp 5 — all far below their respective defaults) and
  `S_Flicker` (Amplitude 0.14, Wave Freq 27.16, Rand Freq 22). Neither has any animated parameter.
  **[E1-P01-STR-005, DIRECT_PROJECT_OBSERVATION]**
- `TrackMotion` (`HasMotionData`/`HasGlowData`/`HasShadowData`) is confirmed `false` on both video
  tracks — the project does not use VEGAS's track-level 2D/3D motion compositing tool anywhere.
  **[E1-P01-STR-006, DIRECT_PROJECT_OBSERVATION]**

## Event segmentation and source ordering

The main edit's body order is **not** chronological or filename order. The 20 distinct curated
"middle"-section gameplay sources appear in this timeline order (source filenames preserved as
recorded):

`Quad X2 001`, `Quad X2 005`, `Quad 005 (single single collat)`, `Glovali - Quad 003 (single single
collat)`, `Quad X2 002`, `Quad 014`, `Quad 003`, `Quad X2 004`, `Quad 005`, `Quad 011`, `Quad 001`,
`Quad 002`, `Quad 006`, `Quad 007`, `Quad X2 006`, `Quad X2 007`, `Quad 008`, `Quad 004`,
`Quad X2 003`, `Quad 009`.

This directly supports "ordinary gameplay is arranged editorially, not chronologically," in this
project. What criterion Editor 1 used to choose this order cannot be established from project
structure alone (would need musical-structure correlation, rendered-frame content, or interview
evidence — none available). **[E1-P01-STR-007, DIRECT_PROJECT_OBSERVATION]**

276 Track-1 events reduce to only 44 contiguous same-source runs after grouping. Curated-highlight
sources are split far more aggressively (opener/middle/closer sources: 188+45+21 = 254 events from
24 source files) than raw/connective sources (almost always single-event runs).
**[E1-P01-STR-008, DIRECT_PROJECT_OBSERVATION]**

## Opener, body, and closer structure

| Passage | Timeline range | Duration | Markers | Material |
|---|---:|---:|---:|---|
| Prelude | 26.643–35.986s | 9.343s | 1 | 3 raw/connective sources |
| Explicit opener | 35.986–69.303s | 33.317s | 27 | `Opener 01/02/03`, separated by raw/connective clips |
| Gameplay body | 69.303–222.889s | 153.586s | 113 | 20 curated + 17 raw/connective sources |
| Explicit closer | 222.889–237.454s | 14.565s | 13 | `Closer 01` |

The opener is not one monolithic clip; its observed sequence is: 3 raw/connective shots → `Opener
01` (14 events) → 1 raw/connective shot → `Opener 02` (17 events) → 1 raw/connective shot →
`Opener 03` (14 events) → 1 raw/connective shot leading into the body.
**[E1-P01-STR-009, DIRECT_PROJECT_OBSERVATION]**

## Gameplay-to-cinematic (raw/connective) relationships

**Repeated pattern within this project** (`cinematic-pairing-analysis.md`, this investigation):
across all 22 raw/connective-footage events, **15/22 (68%) sit directly between an
impact-treated clip and the next ordinary-treated clip**, forming a
buildup → impact → connective-breath → buildup pattern. **20/22 (91%) precede an
ordinary-family clip; 20/22 (91%) stay within the same montage section** rather than bridging
between opener/middle/closer sections. **[E1-P01-STR-010, EDITOR_1_PROJECT_PATTERN]**

Editor 1's earlier structural analysis (`project-structure-forensics.md`) independently found: of
19 gameplay-source transitions in the body, 15 are mediated by a raw/connective clip; 4 are direct
gameplay-to-gameplay transitions (named explicitly: `Quad 005 (single single collat)` →
`Glovali - Quad 003`; `Glovali - Quad 003` → `Quad X2 002`; `Quad X2 004` → `Quad 005`; `Quad 001`
→ `Quad 002`). Raw/connective clips are common but not mandatory.
**[E1-P01-STR-011, EDITOR_1_PROJECT_PATTERN]**

Most raw/connective-clip starts are **not** marker-aligned (only 1/22 within one project frame in
Editor 1's sample); the following curated clip more often is. This supports a repeated,
project-internal pattern: the connective clip begins before the musically-anchored moment and
overlaps it, handing the marker-aligned boundary to the incoming featured clip.
**[E1-P01-STR-012, EDITOR_1_PROJECT_PATTERN]**

## Cut, overlap, and crossfade behavior

See [transitions-and-compositing.md](transitions-and-compositing.md) for the full breakdown. Summary:
the main-video timeline has zero positive gaps between adjacent events (fully continuous coverage);
114/275 adjacencies (41%) overlap (soft crossfade), the remaining 161 are exact butt cuts.
**[E1-P01-STR-013, DIRECT_PROJECT_OBSERVATION]**

## Marker and region relationships

- 150/154 markers (97.4%, Editor 1's count) lie within one project frame of at least one Track-1
  event start or end.
- 146/276 event starts (52.9%) are marker-aligned; 135/276 event ends (48.9%) are marker-aligned.
- This project does **not** support "every clip begins on a marker" — it supports the narrower
  claim that markers are used heavily for segmentation and treatment timing, while some sequence
  entrances are anticipated by an overlapping connective clip instead.
  **[E1-P01-STR-014, DIRECT_PROJECT_OBSERVATION]**
- Independently, this investigation's musical-alignment analysis (rendering the song track solo
  and running onset detection against the actual audio, cross-checked against the marker-interval
  grid) found the 154 markers sit on a **half-time (every-2nd-beat) grid at an estimated ~115–120
  BPM**, not a per-beat or per-bar grid, and that **no single fixed-phase, fixed-BPM grid can be
  extrapolated across the full 240s runtime** (local residual 71ms/13.6% of a beat; global-phase-fit
  residual 229ms). **[E1-P01-TIM-001, DIRECT_PROJECT_OBSERVATION]**

## Nested projects and prerendered sections

Not found. 0 nested-project media references were identified in either inspector pass.
**[E1-P01-STR-015, DIRECT_PROJECT_OBSERVATION]**

## Negative findings that contradict expected montage conventions

- **No Pan/Crop (VideoMotion) animation exists anywhere in the project.** Every one of 276
  Track-1 events carries exactly 1 static VideoMotion keyframe. Whatever visual "punch" effect
  exists in this project's cuts does not come from a Pan/Crop zoom mechanism.
  **[E1-P01-STR-016, DIRECT_PROJECT_OBSERVATION]**
- A single **reversed subclip** was found and used exactly once (`Call of Duty  Modern Warfare 2
  (2022) 2022-10-28 - 15-26-10-11-DVR_1.mp4 - subclip 1 (reversed)`, sourced from 2560×1440/60fps
  cinematic footage). This is a deliberate reverse-playback technique, distinct from anything
  captured by effect-chain analysis. **[E1-P01-STR-017, DIRECT_PROJECT_OBSERVATION]**
- No masks, chroma keys, or generated/text media were found anywhere in the project.
  **[E1-P01-STR-018, DIRECT_PROJECT_OBSERVATION]**
