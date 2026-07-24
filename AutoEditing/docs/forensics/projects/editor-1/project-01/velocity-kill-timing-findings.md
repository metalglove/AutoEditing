# Velocity and kill-timing forensics

Source: `C:\VEGAS\project-inspection.json` (inspector v1). This is a
read-only analysis of one project. Values are timeline seconds unless noted.

## Executive finding

The strongest observable synchronization signal is the replacement gun event,
not the minimum velocity point. In 122 of 125 cases, the replacement gun event
starts exactly at a cut between two main-track video events from the same source.
In 122 of 125 cases it is also within one 29.97-fps frame of a project marker.

For the dominant construction, a kill is therefore most plausibly placed at:

`marker = replacement-gun start = outgoing event end = incoming event start`

This is observed/calculated evidence for the timeline construction and a
high-confidence inference for kill placement. Frame inspection is still required
to prove that the visible kill occurs there.

The recurring velocity shape is a fast–slow–fast valley *between* kill
boundaries. The outgoing half accelerates from a short slow plateau into the
next kill; after that kill, the newly split incoming event begins fast and then
decelerates into another brief plateau. This explains how “fast into the shot,
slow after the shot” can be achieved even though each individual four-point
envelope looks like a symmetric fast–slow–fast recipe.

## Direct observations

- 277 video events; 275 contain velocity envelopes.
- 125 replacement gun-audio events, all using the same source offset
  (`1120.185732` seconds).
- 123/125 replacement gun events lie in main-track events with velocity.
- 122/125 start at an exact same-source video-event boundary (tolerance 2 µs).
- 122/125 are within one frame of a marker; almost all are within 1 µs.
- 123/125 align to a velocity point within 1.36 ms.
- For 107/125 replacements, the aligned point is velocity-point index 3.
- 188 velocity envelopes contain four points.
- The most common four-point curve sequence is
  `Fast > Smooth > Slow > Fast` (96/188), followed by
  `Fast > Smooth > Slow > Smooth` (45/188).

## Dominant four-point velocity recipe

| Measurement | P25 | Median | P75 |
|---|---:|---:|---:|
| Starting speed | 2.184x | 2.763x | 3.053x |
| Low speed | 0.500x | 0.500x | 0.500x |
| Ramp from starting speed to low | 151 ms | 151 ms | 261 ms |
| Low plateau | 168 ms | 209 ms | 317 ms |
| Ramp from low to terminal speed | 165 ms | 226 ms | 363 ms |
| Terminal speed | 2.474x | 3.000x | 3.053x |
| Tail after final point | 0 ms | 0 ms | 0 ms |

The point values and times are observed. Calling this an exact rendered
source-time curve is premature because the inspector does not export enough
information to reproduce VEGAS curve interpolation mathematically.

The median replacement starts 584 ms after its containing event starts and
exactly at its end. Relative to the low plateau, it occurs:

- 428 ms after the low plateau begins (median);
- 224 ms after the low plateau ends (median).

Thus, for ordinary split segments, the shot/audio boundary is normally at the
terminal fast point, not at the start of the 50% plateau.

## Same-source multi-kill sequences

Grouping consecutive replacements by associated source-media path yields 24
source sequences, all containing multiple replacements:

- sequence length range: 3–10 kills;
- total: 125 replacements;
- first: 24; middle: 77; final: 24.

This grouping is more defensible than grouping solely by inter-shot duration,
but it remains an inference: a repeated source file could conceivably contain
multiple editorial clips.

| Position | N with usable velocity | Median start speed | Median low speed | Median terminal speed | Median low-entry → gun | Median low-exit → gun |
|---|---:|---:|---:|---:|---:|---:|
| First | 23 | 1.895x | 0.500x | 3.000x | 727 ms | 378 ms |
| Middle | 76 | 2.329x | 0.500x | 3.000x | 399 ms | 209 ms |
| Final | 24 | 2.329x | 0.500x | 3.000x | 433 ms | 211 ms |

The first kill of a source sequence has a materially longer approach from the
low plateau than middle/final kills. Middle and final medians are very similar.
There is no strong evidence in these velocity values that final kills receive a
distinct terminal-speed recipe. Any special final-kill treatment is more likely
to be in effects, audio, event duration, or following cinematic structure.

## Counterexamples

Three replacement-gun events are not exact main-track event boundaries:

1. `180.897383` and `181.764916` are two gun events inside `t1_e205`
   (`Quad X2 007.mp4`). That video event has no velocity envelope and neither
   gun event has a nearby project marker. These may be deliberately natural/raw
   double kills, but that requires visual confirmation.
2. `184.067216` is on a marker and near velocity-point index 2 inside
   `t1_e206`, about 551 ms before its end. This is a marked kill without the
   otherwise dominant split-at-kill construction.

One additional kill-like boundary at `155.955800` starts replacement audio and
lands on the final fast velocity point, but the nearest marker is 162 ms later
(`156.118002`). This is strong evidence that not every replacement shot is
forced onto a marker.

These counterexamples are important: automation should model the dominant
recipe as a preset/strategy, not as an unconditional invariant.

## Confidence-labelled conclusions

- **Observed directly:** replacement-gun starts, marker times, event boundaries,
  source paths, envelope points, values, and point curve labels.
- **Calculated:** alignment counts and timing distributions above.
- **Inferred with high confidence:** most intended kills are the replacement-gun
  start/cut/marker boundary.
- **Inferred with high confidence:** the usual inter-kill motion is fast after a
  kill, decelerating to a short 50% valley, then accelerating into the next kill.
- **Inferred with medium confidence:** same-source replacement runs represent
  multi-kill source clips.
- **Speculative:** the unmarked, unretimed pair at 180.897/181.765 is retained
  for natural visual pacing or an artistic exception.

## What inspector v2 should add

1. A source-frame/sample position at every velocity point and gun event.
2. Exact evaluation of the velocity envelope between points, including curve
   interpolation.
3. A rendered or extracted frame immediately before/at/after each candidate
   kill boundary.
4. Explicit transition/crossfade data at split boundaries.
5. OFX keyframes correlated to the same absolute timestamps.
6. A stable clip/family annotation rather than inferring sequences from path.

The reproducible analysis is in
[`forensics-velocity-kills.js`](../../../../../Tools/Forensics/editor-1/project-01/forensics-velocity-kills.js);
its detailed machine-readable output is
[`velocity-kills-output.json`](../../../data/editor-1/project-01/velocity-kills-output.json).
