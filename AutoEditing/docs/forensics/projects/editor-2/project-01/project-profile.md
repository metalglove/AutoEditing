# Project Profile — Editor 2 / Project 01

Canonical evidence key: `editor-2/project-01`. See [README.md](README.md) for the entry point and
[limitations.md](limitations.md) for inspection gaps and reduced-scope items referenced below.

## Identity

- **Original project title (preserved):** "Glovali Montage 5" (the project's own folder/file
  naming; the delivered video is `Glovali Montage 5 by [Editor 2].mp4` — the filename's original
  form directly names the editor and has been redacted here per the anonymization scope of this
  package; the render file itself is preserved unmodified on disk).
- **Editor identity:** referred to throughout this package as **Editor 2** / `editor-2`. The
  editor's real name/alias appeared in: the exported render's filename ("... by [redacted]"), an
  in-project title-generator clip's text ("[redacted] PRESENTS"), a bumper clip's filename
  ("[redacted]VISUALS_INTRO.mp4"), and a Windows user-profile path on the editor's own machine
  referenced by one now-removed offline media item. All instances have been replaced with
  `Editor 2`/`editor-2` throughout this package. No mapping between `Editor 2` and the real
  identity is recorded anywhere in this repository.
- **Recipient/subject the montage was made for:** `glovali`, the same individual the Editor 1
  project was made for (confirmed by the user). Not the editor's identity — preserved unchanged
  as forensic evidence, consistent with the anonymization scope of this task.

## Software and version

- VEGAS Pro 20.0, Build 411 — the same installation used for the Editor 1 investigation.

## Project duration and frame rate

- Project video: 2560×1440, **24 fps** (not 29.97, unlike Editor 1's project), field order
  `UpperFieldFirst`, pixel format **`Int8BitFullRange`** (not `Float32Bit`, unlike Editor 1's
  project), render quality `Best`, no output rotation.
- Timeline length: 185.416667 seconds — considerably shorter than Editor 1's project (240.98s).
- Ruler: `TimeAndFrames` format, 120 BPM / 4 beats-per-measure / Quarter beat value (same declared
  ruler BPM as Editor 1's project, though see [velocity-findings.md](velocity-findings.md) and
  [timeline-structure.md](timeline-structure.md) for why this is not treated as proof of the song's
  actual tempo).

## Track and event counts

7 tracks total (Editor 1's project had 5) — a genuinely different track layout, not assumed to
share Editor 1's structure:

| Track | Type | Events | Role (established in [timeline-structure.md](timeline-structure.md)) |
|---|---|---:|---|
| 0 | Video | 7 | Solid-color flash layer A |
| 1 | Video | 7 | Solid-color flash layer B (paired with Track 0) |
| 2 | Video | 2 | Intro title cards (track-level `S_Flicker`) |
| 3 | Video | 1 | Outro/closing bumper clip, `CompositeMode=Lighten` |
| 4 | Video | 158 | Main edit (track-level `S_Flicker` + 1 track-level Composite envelope) |
| 5 | Audio | 107 | Retained/reused native gameplay audio + 1 hit-accent sample |
| 6 | Audio | 5 | Music, split into 5 segments |

- 251 timeline markers, considerably denser than Editor 1's 154 (median inter-marker spacing
  0.667s vs. Editor 1's ~1.05s). 0 regions.
- `Project.MasterBus` and `Project.VideoBus`: 0 effects, 0 envelopes each (same finding as Editor 1
  — no hidden bus-level gain/effect chain in either project).

## Source-media roles and counts

46 media items in the final (cleaned, relinked) inspection copy — see
[limitations.md](limitations.md) for the two items removed before this count (one 3.9GB `.avi`
file removed after it triggered a reproducible VEGAS crash on load; one genuinely-unavailable
media item with no local copy). Notable categories not present at all in Editor 1's project:

- **8 `VEGAS Solid Color` generated clips** (Colors 4, 6, 9, 10, 11, 12, 13, 14) — Editor 1's
  project had zero generated media of any kind.
- **2 `VEGAS Titles & Text` generated clips** — an in-VEGAS title generator, used for the intro
  ("[Editor 2] PRESENTS" and "GLOVALIIN" — the second card's text does not reference the editor
  and is preserved as-is). Editor 1's equivalent title card was baked into source video, not
  VEGAS-generated.
- **2 reversed subclips** (Editor 1's project had exactly 1).
- One dedicated brand/bumper clip (`[Editor 2]VISUALS_INTRO.mp4`, redacted filename)  used as a
  **closing** bumper despite its "_INTRO" name — see [timeline-structure.md](timeline-structure.md).
- One dedicated hit-accent audio sample (`SA-B 50 Hit.mp3`), used 7 times — structurally analogous
  in role to Editor 1's weapon-SFX-library approach, but mechanically very different (a single
  short accent sample, not a per-event Pitch-Shift/Reverb-processed excerpt) — see
  [audio-treatment.md](audio-treatment.md).

## Marker and region counts

251 markers, 0 regions (see above).

## Available, relinked, and missing media

- On first open, the project's stored paths referenced `G:\Edits\Glovali Montage 5\...` (the
  editor's original machine — no `G:` drive exists on the inspection machine). Most media (30-31
  of 46-48, depending on which pass) auto-resolved via VEGAS's own same-folder search; 14 items
  under a `cines\` subfolder required explicit relinking by filename match.
- **One media item had no local copy at all**: a raw DVR capture originally stored under the
  editor's own `C:\Users\[Editor 2]\Videos\...` folder path (redacted) — genuinely unavailable,
  not attempted to be substituted. Its single reference was removed from the timeline (with the
  user's explicit confirmation) rather than left as a permanent missing-media prompt.
- **A second item, a ~3.9GB `.avi` file, triggered a reproducible VEGAS crash** when resolvable/
  online (confirmed twice, independent relink passes) — see [limitations.md](limitations.md) for
  the full incident record. Its single timeline reference (an 8-second event on Track 4) was
  removed at the user's explicit direction to allow the rest of the project to be inspected.
- **The original `Untitled.veg` was directly edited by the project owner during this
  investigation** (not by any automated script) to remove the genuinely-unavailable media
  reference above. This is a deliberate deviation from the "never modify the original" default and
  is recorded transparently: pre-edit SHA-256
  `903707faf5135b4805cc7ff2a1ae1c4daa40d91441e864cbd7dea615f307c7f2` (matching
  `Untitled.veg.bak`, which remains at this exact state and was never touched), post-edit SHA-256
  `4954c30b8a48e23970917d448c8f8dfbfca0593810756a690644ccbbc473d9ba`. All subsequent automated work
  (removing the `.avi` reference, relinking, inspection, frame capture) operated on further
  disposable copies of the post-edit state, never on `Untitled.veg` or `Untitled.veg.bak` directly.

## Available and missing plugins

**Zero missing/unavailable plugins found** — a structural scan of every Track-level and
Event-level `Effects` entry found no `pluginAvailable: false` instances anywhere. All effects used
(`S_Shake`, `S_FilmDamage`, `S_Flicker`, `S_Glow`, `Track Noise Gate`, `Track EQ`, `Track
Compressor`) are Sapphire/Sony-Magix stock, installed and loaded successfully. This differs from
Editor 1's project, which referenced 4 unavailable Red Giant/Boris FX plugins.

## Inspector/export versions and inspection-copy provenance

- One export produced this pass: `raw/project-inspection-v1.json` (schema `e2p01-1.0`, inspector
  reused verbatim from the Editor 1 investigation's v3 inspector with only output-path changes).
- Chain of disposable copies (all under `C:\VEGAS\Glovali Montage 5\Glovali Montage 5\`, none
  overwriting the original): `Untitled.inspect*.veg` (raw inspection copies) →
  `Untitled.clean.veg` (problem-media events removed + `RemoveUnusedMedia()`) →
  `Untitled.final2.veg` (relinked, superseded — retained the crash-triggering `.avi` reference) →
  the working copy used for the successful inspection and frame capture was `Untitled.clean.veg`
  after relinking its remaining offline items directly (see
  `raw/relink-result.txt`-equivalent diagnostic in `diagnostics/relink-result.txt`).
- All generated scripts preserved at `C:\VEGAS\editor-2-project-01-analysis\scripts\`.

## Whether the final render was compared visually

Partially. 10 representative still frames were captured directly via `SaveSnapshot` (see
[representative-moments.md](representative-moments.md)) — fewer than the 16 recommended by the
prompt and fewer than the Editor 1 package's 16, a reduced-scope decision recorded explicitly in
[limitations.md](limitations.md). The separately-existing rendered file
(`Glovali Montage 5 by [Editor 2].mp4`, redacted) was not frame-aligned or compared against the
inspected project.

## Whether the project was saved or mutated during inspection

- `Untitled.veg` was edited once, directly by the project owner (not by automation) — see above.
- `Untitled.veg.bak` was never opened, modified, or saved by anyone at any point.
- All automated relink/removal/inspection work operated on disposable copies and never saved back
  to `Untitled.veg` itself.

## Known limitations of the inspection environment

Summarized here; full detail in [limitations.md](limitations.md): a reproducible VEGAS crash tied
to one large media file; classic (non-OFX) effect parameters unreadable (same API limitation as
Editor 1); no independent musical/tempo analysis via audio render (reduced scope, time-constrained);
no controlled ablation/render testing of `S_Shake`/`S_FilmDamage` (reduced scope); the OFX
keyframe time-domain question remains unresolved (as it was for Editor 1); the 16-representative-
moment target was not fully met (10 captured).
