# Editor style findings profile

Profile ID: `forensic-cross-editor-v1`

Status: advisory evidence for planning, not a replacement for validated editing
rules or renderer capability checks.

Evidence base: anonymized forensic comparison of Editor 1 Project 01 and
Editor 2 Project 01. Evidence IDs refer to the registers under
`docs/forensics/`.

## How to use this profile

1. Obey hard planning constraints and the current request before this profile.
2. Use replicated findings as strong stylistic guidance when the current media
   and song support them.
3. Treat optional findings as candidates, not obligations.
4. Do not invent plugins, effects, source media, or capabilities.
5. Do not generalize editor-specific or contradicted findings.
6. Prefer a coherent, restrained edit over applying every observed technique.

## Replicated high-confidence findings

### STYLE-SYNC-001 — Couple primary impact audio, musical markers, and cuts

Place the principal hit/impact audio cue at the same editorial moment as the
chosen timeline marker and video-event boundary wherever the source material
permits it. This alignment occurred in 122/125 Editor 1 events within 30 ms and
7/7 Editor 2 events exactly. Evidence: E1-P01-TIM-002, E2-P01-AUD-008.

### STYLE-TRN-001 — Prefer simple automatic crossfades when overlap is intended

When a transition calls for overlap, prefer a symmetric overlap with VEGAS
Smooth/Smooth automatic-crossfade behavior instead of an unnecessarily bespoke
transition curve. Both projects used this mechanism consistently. The frequency
of crossfades is not shared: Editor 1 used them much more often than Editor 2.
Evidence: E1-P01-TRN-004, E2-P01-TRN-004.

### STYLE-AUD-001 — Do not assume automated ducking

Neither reference project used track, bus, or master volume envelopes for
ducking. Do not introduce ducking merely to imitate the references. This does
not prohibit ducking when explicitly requested or justified by current audio.
Evidence: E1-P01-AUD-002, E2-P01-AUD-006.

## Shared tendencies with limited evidence

### STYLE-VEL-001 — Impact retiming may use a fast/slow/fast shape

Both editors used above-normal entry and exit speeds around an approximately
0.5x slow plateau. Editor 1 has corpus-level evidence; Editor 2 currently has
only one detailed velocity sample. Treat 0.5x as a candidate neighborhood, not
a fixed value. Curve topology is editor-specific: Editor 1 predominantly used
a four-point single dip while Editor 2 predominantly used a seven-point double
dip. Evidence: E1-P01-VEL-001/002/003, E2-P01-VEL-001/002/003.

### STYLE-AUD-002 — Music fades may use fast-in/slow-out curves

Both projects used the same fast-in/slow-out fade-curve pairing for music.
Apply only where a fade is editorially needed. Evidence: E1-P01-AUD-001,
E2-P01-AUD-004.

### STYLE-FX-001 — Shake with motion blur is a recurring hit treatment

Both projects used Sapphire Shake with motion blur enabled and independently
used a motion-blur length near 0.8 on the first or only Shake instance. This is
capability-gated and based on two projects. A zoom pulse is not part of the
shared finding: Editor 1 used one and Editor 2 did not. Evidence:
E1-P01-FX-007, E2-P01-FX-002.

### STYLE-SRC-001 — Distinguish highlight and connective footage

Both projects separated curated highlight material from raw/connective
footage, but treated the tiers differently. Use semantic source tiers to inform
pacing and placement; do not infer a universal effect treatment for either
tier. Evidence: E1-P01-STR-003/FX-010, E2-P01-STR-003/FX-005.

## Editor-specific patterns available only through explicit style selection

- Editor 1: a two-tier ordinary/impact effect vocabulary, persistent
  Screen-blend texture overlay, dedicated transition whoosh, approximately
  -3 dB track attenuation, and predominantly four-point single-dip velocity.
- Editor 2: flatter Shake/FilmDamage vocabulary, track-opacity flicker bursts
  paired with solid-color flashes, zero-dB track faders, and predominantly
  seven-point double-dip velocity.

Do not blend these into a supposed universal style. Apply them only when the
request explicitly selects the corresponding editor profile and the required
renderer capabilities exist.

## Findings that must not become invariants

- Picture coverage need not be perfectly continuous.
- Crossfade frequency is not shared across the projects.
- A two-tier ordinary/impact effect system is not universal.
- No fixed track-fader attenuation value is supported across editors.
- A zoom pulse is not required for a hit effect.
- Dedicated whooshes are not universal.
- Persistent overlay texture and per-event FilmDamage are alternative,
  editor-specific mechanisms rather than one shared recipe.
- Velocity-curve topology is stylistic rather than universal.

## Planning preference

Use the findings to construct one globally coherent baseline plan. Establish a
clear arc across intro, build, peak, and resolution where those song regions
exist. Reserve the strongest clips, densest synchronization, and most salient
supported treatments for structural peaks. Avoid repetitive effect stacking,
and leave room for rendered review to justify later revisions.
