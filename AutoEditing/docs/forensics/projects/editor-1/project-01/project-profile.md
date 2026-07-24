# Project Profile — Editor 1 / Project 01

Canonical evidence key: `editor-1/project-01`. This document records the identity, provenance,
and inspection state of the reference project. See [README.md](README.md) for the entry point and
[limitations.md](limitations.md) for the full list of inspection gaps referenced below.

## Identity

- **Original project title (preserved):** "montage 4" (the project's own internal folder/file
  naming; the delivered video is `montage 4.mp4`).
- **Original absolute media root (stored in the `.veg`, from the editor's own machine):**
  `E:\Fiverr\SuperB Montages\glovali\...`. This path is preserved as forensic evidence — it is not
  the editor's real name and is not redacted. "SuperB Montages" is the Fiverr gig/seller brand
  name under which the work was delivered.
- **Editor identity:** referred to throughout this package as **Editor 1** / `editor-1`. The
  editor's real name/alias appeared in local folder naming on the inspection machine
  (`...montage 4 by [editor's alias]...`) and has been replaced with `Editor 1`/`editor-1`
  everywhere in this repository. No mapping between `Editor 1` and the real identity is recorded
  anywhere in this repository.
- **Recipient/subject the montage was made for:** appears in the project's own title card
  ("GLOVALI PRESENTS") and in the original media path above as `glovali`. This is **not** the
  editor's identity — it is the person/channel the montage was produced for — and per the
  anonymization scope of this task, is preserved unchanged as forensic evidence.

## Software and version

- VEGAS Pro 20.0, Build 411 (`vegas200.exe`, `ScriptPortal.Vegas.dll` v20.0.0.411).
- Inspector/export schema versions used across the investigation: v1 (`inspectorVersion: "1.0"`),
  v2 (`"2.0"`), v3 (`"3.0"`, current/most complete — adds `Project.MasterBus`/`VideoBus` export and
  classic-effect `Keyframes`).

## Project duration and frame rate

- Project video: 2560×1440, 29.97003 fps, field order `UpperFieldFirst`, pixel format
  `Float32Bit`, render quality `Best`, no output rotation.
- Timeline length: 240.975488 seconds (project `lengthSeconds`).
- Main edit (Track 1) content range: 26.643283s–237.454437s (approx., per source-derived event
  sums); project has unused head/tail space beyond the edited content.

## Track and event counts

| Track | Type | Events | Role (as established by evidence, see [timeline-structure.md](timeline-structure.md)) |
|---|---|---:|---|
| 0 | Video | 1 | Full-length dirt/scratch/film-damage overlay, `CompositeMode=Screen` |
| 1 | Video | 276 | Main edit |
| 2 | Audio | 143 | Replacement weapon SFX (125) + retained gameplay audio (18) |
| 3 | Audio | 1 | Music (*Bad Omens – Glass Houses*) |
| 4 | Audio | 24 | Whoosh/transition stingers (`woosh2.mp3`) |

- 154 timeline markers, all with an empty `label` field. 0 regions.

## Source-media roles and counts

- 51 total media items referenced by the project.
- Folder taxonomy (re-derived directly from exported media paths this pass, see
  [timeline-structure.md](timeline-structure.md) for the full breakdown):
  - `montage 4 by [Editor 1]/cines` (2 files) — RIFE frame-interpolated raw DVR captures.
  - `montage 4 by [Editor 1]/map cines` (20 files) — raw, unedited DVR gameplay captures.
  - `montage 4/1 - openers` (3 files: `Opener 01/02/03.mp4`) — curated highlight clips.
  - `montage 4/2 - middle` (17 files: `Quad 001`–`Quad 017.mp4`, including `Glovali - Quad 003
    (single single collat).mp4`) — curated highlight clips.
  - `montage 4/3 - single single collat section` (3 files) — curated highlight clips.
  - `montage 4/4 - closers` (1 file: `Closer 01.mp4`) — curated highlight clip.
  - Project root / unnamed folder (2 files): `Modern Warfare 2 - All Weapons Showcase (Reloads,
    Sounds, Animations).mp3` (weapon-SFX sample library) and `y2mate.com - 4K FREE Dirt vintage
    film dust scratches damage 35mm footage overlay after effects premiere HD_1080p.mp4` (the
    Track-0 overlay source).
  - `montage 4` root: `Bad Omens - Glass Houses.mp3` (music), `woosh2.mp3` (whoosh source).
- One virtual/generated media item: a reversed subclip,
  `Call of Duty  Modern Warfare 2 (2022) 2022-10-28 - 15-26-10-11-DVR_1.mp4 - subclip 1
  (reversed)`, sourced from 2560×1440/60fps cinematic footage, used exactly once.
- 0 generated media (no title/text generators), 0 image sequences.

## Marker and region counts

- 154 markers, 0 regions (confirmed, see above).

## Available, relinked, and missing media

- On first open on the inspection machine, 48 of 51 media items were offline (paths stored from
  the editor's original machine, `E:\Fiverr\SuperB Montages\glovali\...`).
- All 48 were relinked by filename match under `C:\VEGAS\montage 4 by [Editor 1]\` and its
  `montage 4` subfolder, and the fix was saved to a new disposable file,
  `Untitled.relinked.veg`. This save/reopen persistence was independently verified: `VerifyMedia:
  total=51 offline=0` on a fresh open with no relink code running.
- The original `Untitled.veg` and `Untitled.veg.bak` were never modified or saved over at any
  point in the investigation — see [limitations.md](limitations.md) and the safety notes in
  `AutoEditing/docs/vegas-integration-probe.md`.

## Available and missing plugins

- **Installed and confirmed readable:** Sony/Magix stock (`Track EQ`, `Track Compressor`, `Track
  Noise Gate`, `Pitch Shift`, `eFX_Reverb (VST2, 64-bit)`) and Boris FX Continuum's Sapphire line
  (`S_Shake`, `S_Flicker`, `S_BlurMoCurves`, `S_Glow`, `S_DistortRGB`, `Bump Map`, `Black and
  White`).
- **Referenced but not installed on the inspection machine** (dialog warning on every project
  open):
  - Red Giant Universe "Stylize Glitch" (`{Svfx:com.redgiantsoftware.Universe_Stylize_Glitch_OFX}`)
    — 2 uses.
  - Boris FX Continuum "Vector Blur Dissolve" (`{Svfx:com.borisfx:BCC_Vector_Blur_Dissolve}`) —
    3 uses, believed (not structurally confirmed — see [limitations.md](limitations.md)) to be
    used as a transition.
  - Boris FX Continuum "Damaged TV" (`{Svfx:com.borisfx:BCC4Damaged_TV}`) — 1 use.
  - One additional effect identified only by class GUID
    `b47a199b-15e2-4836-bd39-419904b5d292` — unidentified use.
  - A structural re-search this pass across every Track-level and Event-level `Effects` entry in
    the v3 export found **zero matches** for any of these four identifiers — see
    [limitations.md](limitations.md) for why this doesn't fully resolve their chain location.

## Inspector/export versions and inspection-copy provenance

- Three full raw JSON exports exist: `project-inspection.json` (v1), `project-inspection-v2.json`
  (v2), `project-inspection-v3.json` (v3, current/canonical, ~25.8MB). All three are preserved
  unmodified at `C:\VEGAS\`.
- v1 has a confirmed identity defect: native `EventID` values were serialized as JSON numbers,
  exceeding JavaScript's safe-integer range, collapsing 445 events to 3 distinct parsed ID values.
  v2 and v3 correctly serialize IDs as decimal strings (445/445 and 276/276 unique respectively for
  their event populations).
- All automation scripts used to produce every export and every derived analysis are preserved at
  `C:\VEGAS\scripts\` (not copied into this repository — see
  [evidence-register.md](evidence-register.md) for per-finding provenance).

## Whether the final render was compared visually

- Partially. 16 representative still frames were captured directly from the project via VEGAS's
  `SaveSnapshot` scripting API (not from the separately-existing rendered
  `montage 4.mp4` file, whose exact frame-time alignment with the inspected project was never
  independently verified — see [limitations.md](limitations.md)).
- A full audio-synced short-segment render (`Project.Render` with a `RenderTemplate`) was
  attempted only once, for audio-only purposes (the song track rendered solo to WAV for
  independent musical analysis) — no synced audio+video render segment was produced.

## Whether the project was saved or mutated during inspection

- The original `Untitled.veg`/`Untitled.veg.bak` were never opened, modified, or saved by any
  script across the entire investigation.
- One deliberate, explicit save occurred: the relink fix was saved to a new path,
  `Untitled.relinked.veg` (not the original file). All subsequent inspection, ablation, and
  rendering work operated against this disposable copy.
- Every ablation script that temporarily toggled effect `Bypass` state or OFX boolean parameters
  restored the original values before exiting and logged `Project.IsModified` (confirmed `true`
  in-memory, confirming a real change was made, but the project was never saved in that state).

## Known limitations of the inspection environment

Summarized here; full detail in [limitations.md](limitations.md):

- No ffmpeg, Python, or other audio-DSP tooling was available on the inspection machine; audio
  analysis relied on VEGAS's own render pipeline plus a hand-rolled WAV/PNG decoder.
- Classic (non-OFX) effect parameters (`Pitch Shift`, `eFX_Reverb`, track `EQ`/`Compressor`/`Noise
  Gate`) are not readable through the `ScriptPortal.Vegas` scripting API beyond plugin identity —
  their exact configured values are permanently unrecoverable through this tooling.
- The OFX keyframe time reference frame (event-relative vs. timeline-absolute) was never
  conclusively resolved via controlled fixture testing.
- VEGAS's `Transition` object (as distinct from simple event `FadeIn`/`FadeOut` curves) was never
  exported by any inspector version, leaving the missing-plugin "used as transition" classification
  unconfirmed structurally.
