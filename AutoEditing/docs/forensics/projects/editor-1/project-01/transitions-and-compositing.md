# Transitions and Compositing — Editor 1 / Project 01

Evidence IDs indexed in [evidence-register.md](evidence-register.md). Source material: Editor 1's
structural forensics (`project-structure-forensics.md`) plus this investigation's adversarial pass
(`crossfade-predictor-analysis.md`, `cinematic-pairing-analysis.md`).

## Hard cuts versus crossfades

The main-video timeline (Track 1) is fully continuous: **zero positive gaps** between any of the
275 adjacencies across 276 events. **114/275 (41%) overlap** (soft crossfade); the remaining
161/275 are exact butt cuts at export precision. **[E1-P01-TRN-001, DIRECT_PROJECT_OBSERVATION]**

**This investigation's adversarial pass decomposed the 41% figure into its actual predictors**
(an earlier pass reported it as one undifferentiated statistic):

| Predictor | n | % overlapping | Median overlap |
|---|---:|---:|---:|
| Source file changes | 45 | **100.0%** | 83.4ms |
| Same source file (internal treatment splits) | 230 | **30.0%** | 33.4ms |
| Ordinary-family → impact-family escalation within a run | 21 | **0.0%** (always a hard cut) | n/a |

**[E1-P01-TRN-002, DIRECT_PROJECT_OBSERVATION]**. Source change is the dominant predictor of
whether a crossfade exists at all; the ordinary→impact escalation point is a 100%-consistent
exception in the opposite direction (zero overlaps across 21 confirmed instances, zero
counterexamples).

## Crossfade durations and curve types

Editor 1's overlap-duration statistics by adjacency type:

| Adjacency | Count | Median | Interquartile range | Maximum |
|---|---:|---:|---:|---:|
| Same source | 69 | 0.033s | 0.033–0.050s | 0.117s |
| Source change | 45 | 0.083s | 0.067–0.117s | 0.367s |

All exported fade curves involved are labeled `Smooth`. **[E1-P01-TRN-003,
DIRECT_PROJECT_OBSERVATION]**

**This investigation's direct per-pair fade-detail extraction** (114 overlapping Track-1 boundary
pairs, comparing each side's stored `fadeIn`/`fadeOut` length and curve against the measured
overlap duration):

- **114/114 (100%) are `Smooth`/`Smooth`** on both sides — zero curve-type variation anywhere.
- **112/114 (98.2%) have exactly matching, symmetric fade lengths equal to the overlap duration**
  on both sides.
- Overlap lengths cluster hard at round values: 0.05s (65/114), 0.1s (24/114), with a thin tail to
  0.367s — consistent with drag-snap increments, not typed/authored numeric values.

**Conclusion, mechanism-level (this investigation)**: this is the signature of VEGAS's **automatic
crossfade generation** from dragging one clip to overlap an adjacent one on the same track — not a
deliberately hand-authored transition-curve palette. **No explicit `Transition` plugin object was
found governing these 114 overlaps** — VEGAS's `Transition` object was never exported by any
inspector version (see [limitations.md](limitations.md)), so this conclusion rests on the absence
of curve/length variation, not on a direct read of a Transition object's absence.
**[E1-P01-TRN-004, DIRECT_PROJECT_OBSERVATION]**

## Same-source treatment splits vs. source-change overlaps

All 45 source-change adjacencies overlap (100%); only 69/230 same-source adjacencies overlap
(30%). Source changes receive a blend systematically; internal treatment splits usually remain
invisible butt cuts. **[E1-P01-TRN-005, DIRECT_PROJECT_OBSERVATION]**

## Cinematic bridges (raw/connective footage)

See [timeline-structure.md](timeline-structure.md) for the full structural placement analysis. The
transition-specific findings:

- **100% of the 22 raw/connective-clip boundaries crossfade in (21/21 measurable) and out (22/22)**
  — the exact opposite rule from the ordinary→impact hard-cut rule above.
- **19/22 (86%) are preceded by a whoosh 1.85–3.49 seconds ahead** (median ≈2.2s).

**[E1-P01-TRN-006, EDITOR_1_PROJECT_PATTERN]** (repeated, internally-consistent pattern within this
one project; not cross-validated against a second project).

## Texture and overlay tracks

Track 0 (video): a single, full-length (210.8s) dirt/scratch/film-damage overlay clip,
`CompositeMode=Screen`, `compositeLevel=1`. Visually reconfirmed via a captured representative
frame: a faint vertical scratch line and scattered dust specks are genuinely visible over the
project's opening title card — subtle, not overwhelming, the expected result of Screen-blending a
mostly-black source (dust/scratches on black bleed through; black itself contributes nothing in
Screen mode). Its source clip is only 15.08 seconds long and is looped to cover the full 210.8s
span. **[E1-P01-TRN-007, DIRECT_PROJECT_OBSERVATION / E1-P01-VIS-005 for the visual confirmation]**

## Track compositing modes

Track 0: `Screen`, `compositeLevel=1`. Track 1: `SrcAlpha` (standard/default). Neither track is a
compositing parent/child of the other. **[E1-P01-TRN-008, DIRECT_PROJECT_OBSERVATION]**

## Track Motion

Confirmed unused: `HasMotionData`/`HasGlowData`/`HasShadowData` are all `false` on both video
tracks. **[E1-P01-TRN-009, DIRECT_PROJECT_OBSERVATION]**

## Pan/Crop

Confirmed unused across all 276 Track-1 events — every event has exactly 1 static VideoMotion
keyframe, zero animation. **[E1-P01-TRN-010, DIRECT_PROJECT_OBSERVATION]**

## Masks and chroma keys

None found anywhere in the project. **[E1-P01-TRN-011, DIRECT_PROJECT_OBSERVATION]**

## Nested or prerendered material

None found. **[E1-P01-TRN-012, DIRECT_PROJECT_OBSERVATION]**

## External application boundaries

- Several source clips carry filenames indicating RIFE frame-interpolation upscaling performed
  outside VEGAS before import (e.g. filenames containing `-2x-RIFE-RIFE3.9-120fps`) — source-prep,
  not a VEGAS-native treatment.
- One reversed subclip was created (likely via VEGAS's native reverse-subclip feature, given it
  appears as a VEGAS media-pool virtual item rather than a separately re-encoded file) — see
  [timeline-structure.md](timeline-structure.md).

**[E1-P01-TRN-013, DIRECT_PROJECT_OBSERVATION]**

## Which treatments are native to VEGAS vs. dependent on external/missing tools

- **Native, confirmed working on the inspection machine**: Sapphire (`S_Shake`, `S_Flicker`,
  `S_BlurMoCurves`, `S_Glow`, `S_DistortRGB`), `Bump Map`, `Black and White`, the auto-crossfade
  mechanism above, `Screen` compositing.
- **Dependent on plugins not installed on the inspection machine**: Red Giant Universe "Stylize
  Glitch" (2 uses), Boris FX Continuum "Vector Blur Dissolve" (3 uses, believed transition-scoped),
  Boris FX Continuum "Damaged TV" (1 use), and one effect identified only by class GUID
  `b47a199b-15e2-4836-bd39-419904b5d292`. Their visual result could not be verified. See
  [project-profile.md](project-profile.md) and [limitations.md](limitations.md).

**[E1-P01-LIM-003, DIRECT_PROJECT_OBSERVATION]**

## Negative results and exceptions preserved

- 11 "ordinary"-family events (Editor 1's count) have no marker-aligned edge at all — the family is
  not universally marker-locked.
- 5 "ordinary"-family events end a contiguous source run — terminal position does not imply the
  impact/distortion family will follow.
- 7 "impact"-family events are not at a source-run boundary — the family is not exclusively a
  run-terminal marker.
- One frame-level difference (0.0167s / 0.0168s at the Track-0 overlay's start/end vs. the main
  edit's start/end) may be frame rounding rather than deliberate offset — not resolved.

**[E1-P01-TRN-014, DIRECT_PROJECT_OBSERVATION]**
