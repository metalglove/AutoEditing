# Audio Comparison

## Music track structure

Editor 1: one continuous music event for the whole edit. Editor 2: music split into 5 discrete
segments, with segment boundaries landing exactly on visual-flash-cluster timestamps.

**Classification: Contradicted as a structural choice**, but the underlying editorial intent
(align music treatment to visual accent moments) is arguably compatible in spirit — Editor 1
achieves alignment through a continuous event with markers/velocity/effects layered on top; Editor
2 achieves it by physically splitting the audio at accent points. **Insufficiently supported** to
say which (if either) generalizes.

## Fade curve convention

Both editors use **Fast-in / Slow-out** fade curves on their music events (Editor 1: also on its
whoosh events). **Classification: Shared technique.** This is a real, exact match on a specific,
nameable convention (not just "both use fades," but the specific fast/slow curve-type pairing).
**[E1-P01-AUD-001 vs. E2-P01-AUD-004]**

## Track-fader attenuation

Editor 1: -3.0dB (music), -3.6dB (SFX) track-fader attenuation. Editor 2: 0dB (no attenuation) on
both audio tracks.

**Classification: Contradicted.** Editor 1's -3dB figure was already flagged in its own package as
"reference-project evidence, not a safe universal default" — Editor 2's project confirms that
caution was warranted: zero attenuation is equally valid editorial practice.
**[E1-P01-AUD-001 vs. E2-P01-AUD-005]**

## Ducking / volume automation

Both projects: **zero envelopes at track, bus, and master level** — no ducking mechanism exists in
either project. **Classification: Shared (as an absence).** Two independent editors both achieving
their mix balance without any automated ducking is a real, if negative, convergence — it argues
against assuming a production montage tool needs a ducking feature to match observed editor
practice, though it does not prove ducking is never used by any editor.
**[E1-P01-AUD-002 vs. E2-P01-AUD-006]**

## Replacement/accent SFX mechanism

Editor 1: **one fixed source excerpt, reused 125 times, individually processed per event** via a
`Pitch Shift → Reverb` chain — sonic variety comes entirely from per-instance processing of one
sample.

Editor 2: **native gameplay audio reused directly from the same source files as the picture**
(107 events across 17 files) **plus one small, unprocessed hit-accent sample** (`SA-B 50 Hit.mp3`,
7 uses, zero per-event effects).

**Classification: Contradicted as a mechanism, shared as a principle.** Both editors solve
"how do I get consistent audio impact without a unique recording for every hit" by reusing a small
number of source assets — but the concrete techniques (per-instance signal processing of one
excerpt, vs. accepting an unprocessed accent sample plus native audio) are different enough that
neither should be treated as *the* AutoEditing default; both are candidate options.
**[E1-P01-AUD-006/012 vs. E2-P01-AUD-007/010]**

## Marker/event alignment of the primary hit/impact audio

Editor 1: 122/125 (97.6%) of replacement-gun events within 30ms of a marker. Editor 2: 7/7 (100%)
of hit-accent events at exactly 0ms delta from both a marker and a video-event boundary.

**Classification: Shared mechanically and contextually — the single strongest replicated finding
in this entire comparison.** Both editors, independently, place their primary hit/impact audio
cue in near-perfect (Editor 1) or exact (Editor 2) alignment with both the timeline marker grid and
a video cut. This is the best-supported candidate in this whole investigation for a
**should-change-now** AutoEditing production rule: align hit/impact SFX to markers and cuts
simultaneously. **[E1-P01-TIM-002 vs. E2-P01-AUD-008]**

## Whooshes / transition sounds

Editor 1: 24 uses of one dedicated whoosh sample, preceding transitions by ~2-3.5s. Editor 2: no
dedicated whoosh-style sample exists in the project at all.

**Classification: Editor 1-specific.** A whoosh-transition-cue convention is not universal even
across these two projects.
