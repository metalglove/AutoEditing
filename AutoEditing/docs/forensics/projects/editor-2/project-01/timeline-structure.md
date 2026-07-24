# Timeline Structure — Editor 2 / Project 01

Direct structural measurements are separated from editorial interpretation throughout. Evidence
IDs indexed in [evidence-register.md](evidence-register.md).

## Track organization and compositing hierarchy

7 tracks, in two clear tiers:

- **Accent/overlay tier** (Tracks 0-3): two mirrored solid-color flash tracks (0, 1), a title-card
  track (2), and a single-event outro-bumper track (3, `CompositeMode=Lighten`).
- **Main edit tier**: Track 4 (video, 158 events, the dominant editorial content) and Tracks 5-6
  (audio: reused/native gameplay audio + hit accent; music).

No track is a compositing parent/child of another (`isCompositingChild`/`isCompositingParent` are
`false` throughout). **[E2-P01-STR-001, DIRECT_PROJECT_OBSERVATION]**

## Main gameplay track (Track 4)

158 events, reducing to **32 contiguous same-source runs** (21 with more than one event) after
grouping by source file — structurally comparable in shape to Editor 1's 44/46-run reduction from
a larger event count, though the absolute numbers differ. **[E2-P01-STR-002,
DIRECT_PROJECT_OBSERVATION]**

Sources are two tiers, mirroring (but not identical to) Editor 1's curated-vs-raw split:

- **Curated highlight clips**: `opener.mp4`, `5on head.mp4`, `Glovali - 5on 001/003/004/005/006/
  007 (mcpr to pistol 8mult)/X3 001.mp4`, `Glovali - 6on 001/002.mp4`, `Glovali - Quad MCPR to Quad
  SPX.mp4`, `Glovali - Quad X3 001/X4 001.mp4`, `NEW Glovali - 5on 008/009.mp4`, `NEW Glovali - 6on
  X3 001.mp4` — 17 distinct curated files.
- **Raw/connective footage**: 12 distinct `Call of Duty  Modern Warfare 2 (2022) [date] -
  [time].DVR.mp4` files (plus 2 reversed subclips of two of them), each used once or twice.

**[E2-P01-STR-003, DIRECT_PROJECT_OBSERVATION]**

## Solid-color flash pair (Tracks 0-1)

Tracks 0 and 1 each carry 7 `VEGAS Solid Color` events, at **exactly matching start times and
durations** in every case (e.g. both have events at 38.833s/1.250s, and a 4-event cluster at
70.583-72.084s with identical sub-splits). Each track uses a different color index per cluster
(Track 0: Colors 4/9/9/9/9/11/13; Track 1: Colors 6/10/10/10/10/12/14) — a genuine two-color
flash/strobe pair, not a single repeated clip. **[E2-P01-STR-004, DIRECT_PROJECT_OBSERVATION]**

**Visually confirmed** (`05_strobe_flash_burst.png`, captured at 70.7s, inside the 4-event cluster):
a near-white/light-gray full-frame flash with visible motion-blur streak artifacts and black
letterbox bars top/bottom, consistent with a deliberate strobe/flash accent rather than a rendering
artifact. **[E2-P01-VIS-001, DIRECT_PROJECT_OBSERVATION]**

**This flash cluster's timestamps line up exactly with**: (a) a burst of rapid opacity oscillation
in Track 4's Composite envelope (see below), and (b) a music-track segment boundary at the same
70.583s/71.958s timestamps (see [audio-treatment.md](audio-treatment.md)). This three-way
convergence (color flash + opacity strobe + music cut, all at the same instant) is strong,
directly-observed evidence of a single coordinated accent moment, not independent coincidence.
**[E2-P01-STR-005, DIRECT_PROJECT_OBSERVATION]**

## Track-4 Composite (opacity) envelope — a track-level flicker mechanism Editor 1's project does not have

Track 4 carries one track-level `Composite` envelope (range 0-1, neutral 1) with 141 points,
**clustered into isolated bursts at specific timestamps** (e.g. 27.58-28.38s, 38.75-39.92s,
68.41-78.63s, 109.25-109.54s, 130.59-131.80s, 141.30-148.31s) rather than varying continuously
across the whole timeline — between bursts the value sits flat at 1 (fully opaque). Within a
burst, opacity oscillates rapidly between 1.0 and roughly 0.6-0.77 in a sawtooth pattern (period
roughly 80-100ms per cycle). **[E2-P01-STR-006, DIRECT_PROJECT_OBSERVATION]** **This is a
genuinely new mechanism relative to Editor 1's project, which had zero track-level envelopes of
any kind anywhere.**

## Intro structure (Track 2 + Track 4 events 0-1 + the 1.375s gap)

- Track 4's first two events (0-8.625s combined) are raw/connective DVR footage carrying the
  `S_FilmDamage` effect (see [effects-and-presets.md](effects-and-presets.md)).
- Track 2's two title-card events span the same window: "[Editor 2] PRESENTS" (0-5.333s,
  editor-identity redacted from the original text) then "GLOVALIIN" (5.333-8.625s, the recipient's
  name — not redacted, per this package's anonymization scope).
- **Visually confirmed**: `01_intro_title_editor-2_presents.png` (captured at 2.0s) shows the
  "[Editor 2] PRESENTS"-equivalent text legibly rendered over aerial/drone gameplay footage.
  **[E2-P01-VIS-002, DIRECT_PROJECT_OBSERVATION]**
- **A genuine 1.375-second gap** exists in Track 4's coverage immediately after the intro (t4_e1
  ends at 8.625s, t4_e2 — the first `opener.mp4` event — starts at 10.0s). `03_gap_blank_at_9.png`
  (captured at 9.0s, inside this gap) — reviewed to confirm blank/black content during this
  interval. **This directly contradicts Editor 1's "picture coverage is always continuous, zero
  positive gaps" finding — Editor 2's project has at least one deliberate gap.**
  **[E2-P01-STR-007, DIRECT_PROJECT_OBSERVATION / E2-P01-VIS-003 for the visual confirmation]**

## Outro structure (Track 3 + final Track-4 events)

The last two Track-4 events are: `t4_e156` (173.292-177.375s, `NEW Glovali - 6on X3 001.mp4`,
compound `S_Shake` + `S_FilmDamage` chain — one of only 2 instances of this exact combination in
the whole project) and `t4_e157` (176.542-184.042s, a raw DVR capture, `S_FilmDamage` alone,
overlapping the previous event by 0.833s). Track 3's single event
(the redacted-filename bumper clip) occupies **exactly the same range as `t4_e157`**
(176.542-184.042s), composited on top via `CompositeMode=Lighten`. **[E2-P01-STR-008,
DIRECT_PROJECT_OBSERVATION]**

**Notable naming/usage mismatch**: the bumper clip's filename (redacted here, contains "INTRO")
is used as the **closing** bumper, not an opening one — the clip appears to be a
generic branded bumper reused at both ends of different projects, not something whose filename
reliably predicts its structural role. **[E2-P01-STR-009, DIRECT_PROJECT_OBSERVATION]**

## Marker relationships

251 markers, median inter-marker spacing 0.667s (min 0.167s, max 2.75s) — considerably denser than
Editor 1's 154 markers (median ~1.05s). Whether this reflects a genuinely faster song tempo, a
finer subdivision convention, or a different placement philosophy was **not independently
verified via audio analysis** in this pass (reduced scope — see
[limitations.md](limitations.md)). **[E2-P01-TIM-001, DIRECT_PROJECT_OBSERVATION]**

## Negative findings / counterexamples preserved

- The gap at 8.625-10.0s (above) directly contradicts a headline Editor 1 finding
  ("picture coverage is always continuous"). Not treated as evidence the two projects share this
  convention — treated as a genuine per-project difference.
- Track 4's asymmetric fade at `t4_e126`→`t4_e127` (prevFadeOut length 0, curFadeIn length
  0.208333s) is the one exception to an otherwise-universal symmetric-crossfade pattern — see
  [transitions-and-compositing.md](transitions-and-compositing.md).
