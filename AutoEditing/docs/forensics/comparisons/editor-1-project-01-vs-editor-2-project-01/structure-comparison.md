# Structure Comparison

## Track layout

| | Editor 1 | Editor 2 |
|---|---|---|
| Total tracks | 5 (1 overlay video, 1 main video, 3 audio) | 7 (4 accent/overlay video, 1 main video, 2 audio) |
| Main edit track events | 276 | 158 |
| Project duration | 240.98s | 185.42s |
| Video format | 1920×1080, 29.97fps, Float32Bit | 2560×1440, 24fps, Int8BitFullRange |

**Classification: Editor/project-specific.** Track count, resolution, and frame rate are basic
project-setup choices with no evidence either way that they reflect editor convention vs. one-off
project requirements. **[E1-P01-STR-001/STR-002 vs. E2-P01-STR-001/STR-002]**

## Coverage continuity

Editor 1: zero positive gaps across 275 adjacencies (fully continuous). Editor 2: one 1.375s gap.

**Classification: Contradicted.** Editor 1's "picture coverage is always continuous" reads as a
strong single-project finding, directly falsified as a cross-editor claim by Editor 2's one
counterexample. **[E1-P01-STR-013 vs. E2-P01-STR-007]**

## Crossfade / hard-cut rate

Editor 1: 114/275 (41%) overlap. Editor 2: 17/157 (10.8%) overlap.

**Classification: Contradicted** as a specific rate; **shared technique, different parameters**
as a mechanism (both projects' overlaps show the same VEGAS-automatic-crossfade curve/length
signature — see below). **[E1-P01-TRN-001/002 vs. E2-P01-TRN-001/002]**

## Automatic-crossfade mechanism

Editor 1: 114/114 (100%) `Smooth`/`Smooth`, 112/114 (98%) exact-symmetric fade lengths matching the
overlap duration. Editor 2: 16/17 (94%) `Smooth`/`Smooth` and symmetric (1 genuine asymmetric
exception).

**Classification: Shared mechanically and contextually.** Both projects' overlaps carry the
signature of VEGAS's automatic crossfade generation from dragging clips into alignment, not
hand-authored transition curves. This is now supported by two independent editors and is the
single strongest replicated structural finding in this comparison.
**[E1-P01-TRN-004 vs. E2-P01-TRN-004]**

## Hard-cut at escalation points

Editor 1: ordinary→impact escalation is always a hard cut (0/21 overlap, zero exceptions).
Editor 2: no equivalent escalation-tier structure exists to test against (see
[preset-comparison.md](preset-comparison.md)) — **insufficiently supported**, not contradicted,
since Editor 2 simply doesn't have a comparable two-tier system to check this rule against.

## Full-length texture overlay

Editor 1: one project-wide `Screen`-blend dirt/scratch overlay clip on a dedicated track. Editor 2:
no equivalent — texture is applied per-event via `S_FilmDamage`, and the one full-event blend-mode
track (`Lighten`) is a single outro bumper, not a persistent texture layer.

**Classification: Editor-specific / contradicted as a shared technique.**
**[E1-P01-TRN-007 vs. E2-P01-TRN-006/007]**

## Track-level ambient effects

Editor 1: main track carries both `S_Shake` and `S_Flicker` at track level. Editor 2: main track
carries only `S_Flicker` at track level (no track-level `S_Shake`).

**Classification: Shared technique, different parameters** (both use a track-level `S_Flicker`
ambient layer; only Editor 1 adds a track-level `S_Shake` on top).
**[E1-P01-STR-005 vs. E2-P01-FX-007]**

## Novel Editor 2 mechanism with no Editor 1 counterpart

The track-level Composite (opacity) envelope, clustered into isolated flicker bursts and paired
with a matching solid-color flash-clip pair, timed to music-segment cuts. Editor 1's project has
zero track-level envelopes of any kind. **Classification: Editor 2-specific.**
**[E2-P01-STR-006]**

## Cinematic/connective-footage source tiers

Both projects independently split their main-track sources into a "curated highlight clips" tier
and a "raw/connective DVR footage" tier, by filename pattern. **Classification: Shared technique,
different parameters** — the tiering concept replicates; what happens to each tier (Editor 1:
raw-footage events get no per-event effects and retain native audio; Editor 2: raw footage
predominantly gets `S_FilmDamage`) differs.
**[E1-P01-STR-003/FX-010 vs. E2-P01-STR-003/FX-005]**
