# Editor 2 / Project 01

Canonical evidence key: `editor-2/project-01`. Original project title: **"Glovali Montage 5"**.
Produced by a freelance editor referred to throughout this repository only as **Editor 2**
(`editor-2`) — their real name/alias (which appeared in an exported render's filename, an
in-project title-card's text, a bumper clip's filename, and a Windows user-profile path) has been
removed and no mapping to it is recorded anywhere in this repository. The montage was made for
`glovali`, the same recipient as Editor 1 / Project 01 — not the editor's identity, preserved
unchanged.

## Investigation method: blind, then compared

This investigation was conducted **blind** — analyzed independently of Editor 1 / Project 01's
substantive findings, per the mandatory phase order in
`AutoEditing/docs/forensics/claude-project-5-analysis-prompt.md`. The Editor 1 package was
consulted beforehand only to match document structure, evidence-ID schema, and methodology — not
its conclusions. The standalone Editor 2 evidence register was frozen before any cross-editor
comparison was written. See
[`../../../comparisons/editor-1-project-01-vs-editor-2-project-01/README.md`](../../../comparisons/editor-1-project-01-vs-editor-2-project-01/README.md)
for the comparison, produced only after this freeze.

## What this package is, and is not

Project-specific evidence, scoped to this one project. **Nothing here is a universal
sniper-montage convention** — even where a finding replicates across both Editor 1 and Editor 2,
two data points establish a repeated pattern worth testing further, not a proven convention.

## Inspection status

- **Original files**: `Untitled.veg` was directly edited once by the project owner (not by
  automation) to remove one genuinely-unavailable media reference; `Untitled.veg.bak` was never
  touched and remains at the project's pristine original state. Full provenance in
  [project-profile.md](project-profile.md) and [limitations.md](limitations.md).
- **A reproducible VEGAS crash** was encountered and documented as a genuine automation incident
  (not concealed) — see [limitations.md](limitations.md).
- **10 representative frames** captured directly via `SaveSnapshot` (fewer than the requested 16 —
  a reduced-scope decision given the time spent resolving the crash above).
- **No controlled effect ablation, no independent musical/tempo audio render, no full
  crossfade-predictor breakdown** were performed this pass — explicitly listed as reduced-scope
  items in [limitations.md](limitations.md), not silently dropped.

A compact machine-readable projection of the evidence register is available at
[`../../../data/editor-2/project-01/evidence-summary.json`](../../../data/editor-2/project-01/evidence-summary.json).
Sanitized reproduction scripts are under
[`../../../../../Tools/Forensics/editor-2/project-01/`](../../../../../Tools/Forensics/editor-2/project-01/).

## Document index

| Document | Covers |
|---|---|
| [project-profile.md](project-profile.md) | Identity, VEGAS version, duration/frame rate, track/event/media/marker counts, plugin availability (none missing), inspection-copy provenance, the crash incident |
| [timeline-structure.md](timeline-structure.md) | Track organization, the solid-color flash pair, the track-level opacity-envelope strobe, intro/outro structure, the coverage gap |
| [velocity-findings.md](velocity-findings.md) | Velocity-envelope families (7-point double-dip shape dominates), the multi-hit burst |
| [effects-and-presets.md](effects-and-presets.md) | `S_Shake`/`S_FilmDamage` mechanisms, effect-chain distribution, zero missing plugins |
| [audio-treatment.md](audio-treatment.md) | Music segmentation tied to the flash cluster, native-audio reuse on Track 5, the hit-accent sample |
| [transitions-and-compositing.md](transitions-and-compositing.md) | Hard-cut/crossfade rate (10.8%, much lower than Editor 1's 41%), the `Lighten`-blend outro |
| [representative-moments.md](representative-moments.md) | The 10 captured frames with confidence labels |
| [evidence-register.md](evidence-register.md) | Every claim, stable ID, classification, traceability; proposed promotions |
| [limitations.md](limitations.md) | The crash incident, reduced-scope items, inspector gaps |

## Strongest findings (project-specific, not universal rules)

- This project's dominant per-event treatment is a single `S_Shake` instance (Amplitude+Frequency
  decaying, Motion Blur forced on, `Mo Blur Length=0.8`) applied to 78% of main-track events — a
  materially simpler vocabulary than a separate two-tier ordinary/impact system.
- This project uses a genuinely new mechanism not found in Editor 1's project: a track-level
  Composite (opacity) envelope that flickers in isolated bursts, paired with a matching two-color
  solid-color flash clip pair, timed to exact music-segment cut points — a coordinated
  audio-visual strobe accent, directly visually confirmed.
- This project's hit-accent sample (`SA-B 50 Hit.mp3`, 7 uses) is 100% aligned (0ms delta) to both
  a marker and a video-event boundary, clustered into one rapid 8-second multi-hit burst.
- This project has a materially lower crossfade rate (10.8% vs. Editor 1's 41%) and, notably, a
  genuine coverage gap (1.375s) that Editor 1's project did not have.
- Zero missing plugins, zero ducking mechanism, zero dedicated whoosh-transition sample — three
  clean structural negatives.

## Repeated patterns within this project (not yet cross-project)

- The solid-color-flash / opacity-strobe / music-cut three-way convergence at the same instants
  (observed at two separate timestamp clusters).
- `S_FilmDamage` applied predominantly to raw/connective footage rather than curated highlights.

## Findings replicated across Editor 1 and Editor 2 (see the comparison package for detail)

VEGAS automatic-crossfade signature (Smooth/Smooth, symmetric length matching overlap); zero
bus/master-level ducking mechanism in either project; `Mo Blur Length=0.8` on the Shake-based hit
effect in both; Fast-in/Slow-out fade convention on both projects' music.

## Findings that differ or fail to replicate

Two-tier ordinary/impact effect system (Editor 1 only); full-length texture-overlay track (Editor
1 only, `Screen` blend vs. Editor 2's per-event `S_FilmDamage` and `Lighten`-blend outro);
continuous zero-gap picture coverage (Editor 1 only — Editor 2 has one gap); dominant velocity
shape (4-point single-dip vs. 7-point double-dip); crossfade rate (41% vs. 10.8%); track-fader
attenuation (present in Editor 1, absent in Editor 2); replacement-SFX mechanism (processed
single-excerpt library in Editor 1 vs. reused native audio + one small hit-accent sample in Editor
2); presence of missing plugins (4 in Editor 1, 0 in Editor 2).

## Known gaps

See [limitations.md](limitations.md) for the full list, including the VEGAS crash incident, the
user-edited original file, and every reduced-scope analysis item.
