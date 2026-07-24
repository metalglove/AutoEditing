# Audio Treatment — Editor 2 / Project 01

Evidence IDs indexed in [evidence-register.md](evidence-register.md). This document does not
infer the effective final mix solely from track-level gain — master/bus levels were separately
checked and are reported below rather than assumed.

## Music placement, gain, and fades

Track 6, source `03. Temptation.flac`, split into **5 discrete segments** rather than one
continuous event (a structural difference from Editor 1's single-event music track):

| Event | Start | Length | Fade in | Fade out |
|---|---:|---:|---|---|
| `t6_e0` | 0.000s | 70.583s | 0s, Fast | 0.01s, Slow |
| `t6_e1` | 70.583s | 1.375s | 0s, Fast | 0.01s, Slow |
| `t6_e2` | 71.958s | 30.000s | 0.01s, Fast | 0.01s, Slow |
| `t6_e3` | 101.958s | 1.292s | 0.01s, Fast | 0.01s, Slow |
| `t6_e4` | 103.250s | 82.167s | 0.01s, Fast | **12.125s, Slow** |

All 5 events: `normalize=false`, `gain=1`. **[E2-P01-AUD-001, DIRECT_PROJECT_OBSERVATION]**

**The segment boundaries at 70.583s and 101.958s land exactly on the solid-color flash-cluster
timestamps** (see [timeline-structure.md](timeline-structure.md)) — the music itself is cut/spliced
at the same instants as the visual strobe accent, not just visually accented independently. This
is a genuine, directly-observed audio-visual co-editing pattern not present in Editor 1's project
(whose music track had zero internal splits). **[E2-P01-AUD-002, DIRECT_PROJECT_OBSERVATION]**

**The final segment's 12.125-second fade-out exactly matches the length of the last Track-0/1
solid-color event** (`Solid Color 13`/`14`, both 12.125s at 173.292s) — the music's fade-out is
timed to a specific visual element's duration, not an arbitrary round number.
**[E2-P01-AUD-003, DIRECT_PROJECT_OBSERVATION]**

**Fast-in/Slow-out fade curve convention matches Editor 1's project exactly** (Editor 1's music and
whoosh events also used Fast-in/Slow-out) — a genuine cross-project match candidate.
**[E2-P01-AUD-004, DIRECT_PROJECT_OBSERVATION]**

## Track, bus, and master-level processing

- Tracks 5 and 6 (both audio) carry the same three track-level effects in order: `Track Noise
  Gate`, `Track EQ`, `Track Compressor` — parameter values not recoverable (same classic-effect API
  limitation documented for Editor 1's project). Track volume for both: 1.0 linear (0dB) — no
  track-fader attenuation on either audio track, a genuine difference from Editor 1's project
  (which used -3.0dB and -3.6dB track-fader attenuation on its audio tracks).
  **[E2-P01-AUD-005, DIRECT_PROJECT_OBSERVATION]**
- `Project.MasterBus`/`Project.VideoBus`: 0 effects, 0 envelopes each (same finding as Editor 1's
  project). **No ducking mechanism exists at track, bus, or master level** — no envelopes were
  found anywhere in this project either. **[E2-P01-AUD-006, DIRECT_PROJECT_OBSERVATION]**

## Retained/native gameplay audio (Track 5, 107 events)

Unlike Editor 1's project (which used one fixed weapon-SFX-library excerpt reused 125 times,
individually pitch-shifted and reverbed), **Editor 2's Track 5 reuses audio directly from the same
video files that appear on the main picture track** — every one of the 17 curated-highlight source
files (`opener.mp4`, the `Glovali - *`/`NEW Glovali - *` files, `5on head.mp4`) also appears as a
Track-5 audio source, with per-file counts roughly tracking (not exactly matching) their Track-4
picture-event counts. **This is a structurally different SFX/audio-layering strategy**: rather than
replacing gunshot audio with a processed library excerpt, Editor 2 appears to duplicate/re-layer
the game's own native audio from the same source files onto a separate track (likely for
independent gain/EQ/compression processing distinct from the picture-track's embedded audio, given
the dedicated Noise Gate/EQ/Compressor chain on this track). **[E2-P01-AUD-007,
DIRECT_PROJECT_OBSERVATION]** The exact relationship (same exact sub-clip in-and-out points as the
corresponding picture event, vs. an independently-selected excerpt from the same file) was **not
verified event-by-event** this pass — flagged as an open item in
[limitations.md](limitations.md).

## Hit-accent sample (`SA-B 50 Hit.mp3`, 7 uses, on Track 5)

- **7/7 (100%) start at exactly 0ms delta from both a timeline marker and a Track-4 video event
  start** — even tighter than Editor 1's 97.6%-within-30ms figure for its (much larger, 125-event)
  replacement-SFX corpus. **[E2-P01-AUD-008, DIRECT_PROJECT_OBSERVATION]**
- All 7 events: `normalize=false`, `gain=1`, no event-level effects.
- All 7 are clustered within an 8-second span (125.292-133.292s+1.0s length ≈ 133.3s) — a rapid
  multi-hit burst (see [velocity-findings.md](velocity-findings.md)).
- This is a single short accent sample (not processed per-instance the way Editor 1's weapon
  excerpt was individually pitch-shifted/reverbed) — a mechanically simpler approach for achieving
  hit-emphasis, used far more sparingly (7 times vs. 125).

## First, middle, and final-kill audio patterns

Not established — the 7 hit-accent instances are too few and too tightly clustered in one burst to
support a first/middle/final analysis within this pass. Analogous to Editor 1's finding that
source-level data alone couldn't establish per-position audio variation, though for a different
underlying reason here (small sample, not classic-effect API opacity).

## Whooshes / transition sounds

**No dedicated whoosh-style transition sample was identified in this project's media list** —
unlike Editor 1's project (24 uses of one `woosh2.mp3` file). This is a genuine structural
difference, not a gap: the media inventory contains no additional unaccounted-for audio file that
would fill this role. **[E2-P01-AUD-009, DIRECT_PROJECT_OBSERVATION]**

## Same-source excerpt evidence

The `SA-B 50 Hit.mp3` accent is a single fixed sample reused without per-instance processing
(no per-event effects on any of its 7 instances) — simpler than Editor 1's single-excerpt-plus-
per-event-processing approach, but the same underlying "reuse one sample across the hit corpus"
principle. **[E2-P01-AUD-010, DIRECT_PROJECT_OBSERVATION]**

## Unsupported or unavailable audio parameters

- `Track Noise Gate`/`EQ`/`Compressor` exact settings on Tracks 5-6: unrecoverable (same API
  limitation as Editor 1's project).
- Peak/integrated loudness, stereo placement: **not measured** this pass — no audio render/stem
  extraction was performed for Editor 2's project (reduced scope; Editor 1's package included a
  music-track solo render and loudness measurement, which was not repeated here given time
  constraints — see [limitations.md](limitations.md)).
- Whether Track 5's audio is genuinely independent-in/out-point excerpts vs. exact picture-track
  duplicates: not verified (see above).
