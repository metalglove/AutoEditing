# Transitions and Compositing — Editor 2 / Project 01

Evidence IDs indexed in [evidence-register.md](evidence-register.md).

## Hard cuts versus crossfades

Track 4 (157 adjacencies across 158 events): **17 overlaps (10.8%), 139 hard cuts (88.5%), 1 gap
(0.6%)**. **[E2-P01-TRN-001, DIRECT_PROJECT_OBSERVATION]**

**This overlap rate (10.8%) is markedly lower than Editor 1's project (41%)** — a genuine,
directly-measured cross-project difference, not a methodological artifact (the same measurement
approach was used in both projects). **[E2-P01-TRN-002, DIRECT_PROJECT_OBSERVATION]**

**The one gap** (1.375s, between the intro sequence and the opener — see
[timeline-structure.md](timeline-structure.md)) is itself a counterexample to "continuous coverage"
as a cross-editor convention; Editor 1's project had zero gaps.

## Same-source vs. source-change overlap predictor

Of the 17 overlaps: **4 are same-source, 13 are source-change**. This is the same directional
pattern as Editor 1's project (source changes overlap more often than same-source splits), but
the absolute rate is much lower here given the small overlap count relative to 157 total
adjacencies. **A full predictor breakdown (percentage of same-source vs. source-change
adjacencies that overlap, as computed for Editor 1) was not computed this pass** — flagged as an
open follow-up. **[E2-P01-TRN-003, DIRECT_PROJECT_OBSERVATION]**

## Crossfade curve types and duration

**16 of 17 overlaps use `Smooth`/`Smooth` curves with exactly symmetric fade lengths matching the
overlap duration** — the same VEGAS-automatic-crossfade signature identified in Editor 1's project.
Overlap durations range 208.3ms-833.3ms (median ≈416.7ms) — **considerably longer, on average,
than Editor 1's overlaps** (which ranged 17-367ms, median 50ms). **[E2-P01-TRN-004,
DIRECT_PROJECT_OBSERVATION]**

**One genuine exception**: `t4_e126`→`t4_e127` has an asymmetric fade (prev `fadeOut.length=0`,
cur `fadeIn.length=0.208333s`) despite an overlap being present — the only case in this project
where the automatic-crossfade signature doesn't hold exactly. Preserved as a counterexample, not
discarded. **[E2-P01-TRN-005, DIRECT_PROJECT_OBSERVATION]**

## Cinematic/connective-footage bridges

Not separately analyzed with the same rigor as Editor 1's `cinematic-pairing-analysis.md` in this
pass (reduced scope — see [limitations.md](limitations.md)). The raw/connective DVR footage tier
is structurally identified (see [timeline-structure.md](timeline-structure.md) and
[effects-and-presets.md](effects-and-presets.md)), but its placement relative to hit-accent
moments and curated-clip boundaries was not systematically cross-tabulated the way Editor 1's 22
connective-clip corpus was.

## Global/local visual layers

- Tracks 0-1: solid-color flash pair, `CompositeMode` not separately overridden per-track from
  default (`SrcAlpha`) — the flash effect comes from the clip content and the Track-4 opacity
  envelope beneath it revealing/hiding it, not from an exotic blend mode on the flash tracks
  themselves.
- Track 3: single outro bumper event, `CompositeMode=Lighten` — a different blend mode from both
  Editor 1's project (`Screen`, on its full-length texture overlay) and this same project's other
  tracks. **[E2-P01-TRN-006, DIRECT_PROJECT_OBSERVATION]**
- No project-wide, full-length texture overlay analogous to Editor 1's Track-0 dirt/scratch clip
  exists in this project — texture (via `S_FilmDamage`) is applied per-event, not as a persistent
  track-level layer. **[E2-P01-TRN-007, DIRECT_PROJECT_OBSERVATION]**

## Track Motion / Pan-Crop

Not explicitly re-checked this pass via the same `TrackMotion`/`VideoMotion` fields used for
Editor 1's project — the raw export contains this data (same inspector) but a dedicated
zero-usage confirmation pass was not run. Flagged as an open item, not assumed either way.

## Masks and chroma keys

Not found in any effect chain inspected (`S_Shake`, `S_FilmDamage`, `S_Flicker`, `S_Glow` — none
are masking/keying effects). No dedicated mask/chroma-key effect type was observed anywhere in the
effect-chain distribution. **[E2-P01-TRN-008, DIRECT_PROJECT_OBSERVATION]**

## Nested or prerendered material

None found — no nested-project media references in the 46-item media pool.
**[E2-P01-TRN-009, DIRECT_PROJECT_OBSERVATION]**

## Which treatments are native vs. plugin-dependent

All effects used (`S_Shake`, `S_FilmDamage`, `S_Flicker`, `S_Glow`, `Track Noise Gate/EQ/
Compressor`) are Sapphire/Sony-Magix stock and confirmed available — **zero missing-plugin
dependencies**, unlike Editor 1's project. See [effects-and-presets.md](effects-and-presets.md).

## Negative results and exceptions preserved

- The `t4_e126`/`t4_e127` asymmetric-fade exception (above).
- The one Track-4 gap (above) — a direct counterexample to Editor 1's "always continuous" finding.
- The compound `S_Shake`+`S_FilmDamage` chain (2 instances total) is too rare to establish a
  reusable "escalation" rule the way Editor 1's 26-event impact family could.
