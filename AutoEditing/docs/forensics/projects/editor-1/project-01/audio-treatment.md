# Audio Treatment — Editor 1 / Project 01

Evidence IDs indexed in [evidence-register.md](evidence-register.md). Source material: Editor 1's
original audio forensics (`project-audio-forensics.md`) plus this investigation's adversarial pass
(`audio-mix-verification.md`, `musical-alignment-analysis.md`, `kill-alignment-corpus.json`).

**This document does not infer the effective final mix solely from track-level gain** — master
and bus levels were separately inspected and are reported below rather than assumed.

## Music placement, gain, and fades

- One event, `Bad Omens - Glass Houses.mp3`, on Track 3.
- Timeline start: 23.556867s. Source offset: 23.556867s (the song's own absolute timebase is
  preserved — its first 23.556867s are simply not played, rather than the event being offset
  arbitrarily).
- Timeline end: 240.975488s, exactly the project end.
- Event fade-in: 3.086417s, Fast curve. Event normalization: off.
- Track-level gain: 0.707946 linear ≈ **-3.0 dB**.
- Event-level and track-level envelopes: **zero**, confirmed at both levels (extending a check
  that was already performed at bus/master level — see below).

**[E1-P01-AUD-001, DIRECT_PROJECT_OBSERVATION]**

## Track, bus, and master-level processing

- All 3 audio tracks (2/3/4) carry the same three track-level effects in order: `Track Noise
  Gate`, `Track EQ`, `Track Compressor`. **Parameter values for these three effects were not
  recoverable** — see [limitations.md](limitations.md).
- Track-fader linear gains: Track 2 (SFX/retained audio) 0.660693 ≈ **-3.6 dB**; Track 3 (music)
  and Track 4 (whoosh) 0.707946 ≈ **-3.0 dB** each.
- `Project.MasterBus` and `Project.VideoBus` both confirmed to have **zero effects and zero
  envelopes**.
- **This investigation's adversarial pass additionally confirmed zero envelopes at track level on
  all 5 tracks** (extending the already-confirmed bus/master-level zero). **Conclusion: no
  ducking/volume-automation mechanism exists anywhere in this project, at any stage of the signal
  chain.** Any perceived "ducking" of the music under gunfire in this project would be
  psychoacoustic masking from simultaneous loud transients, not an authored volume reduction.

**[E1-P01-AUD-002, DIRECT_PROJECT_OBSERVATION]**

## Original gameplay-audio retention

Track 2 has 143 total events: 125 sourced from a weapon-sound library MP3 (see below) and **18
sourced directly from original gameplay/cinematic MP4 files**:

- Duration range: 0.700700–2.268933s; median 1.234567s.
- 17 of 18 remain grouped to another event.
- None are normalized. None have event-level audio effects.
- **Every one of the 18 starts at exactly the same timestamp (0ms delta) as, and shares its source
  file with, a Track-1 video event carrying no event-level video effects** (the "no-effects"
  family in [effects-and-presets.md](effects-and-presets.md)).
- No retained-audio start is within one project frame of a marker (median nearest-marker distance
  1.051050s) — these are not marker-locked impacts.

**[E1-P01-AUD-003, DIRECT_PROJECT_OBSERVATION]**

**More precise characterization (this investigation)**: of the 24 Track-1 "no-effects" video
events, **18/24 (the ones sourced from raw/unedited DVR gameplay folders, `cines`/`map cines`)
pair 1:1 with one of these 18 retained-audio events. The other 6/24 (sourced from the curated
`Opener`/`Quad`/`Closer` folders) get no paired retained audio at all.** This is a clean,
deterministic rule tied to source-folder origin, not a loose or unexplained minority pattern.
**Purpose, well-supported by this rule:** the editor kept the original in-game audio (ambient
sound, footsteps, in-engine foley) synced to raw, unprocessed gameplay footage that isn't part of
the curated highlight pipeline, rather than layering a replacement weapon sound onto it.
**[E1-P01-AUD-004, DIRECT_PROJECT_OBSERVATION]**

This is strong evidence against a blanket "delete all original clip audio" characterization of this
project — source audio is selectively retained for a specific, identifiable category of footage.

## Replacement weapon sounds

- 125 of Track 2's 143 events use one file, `Modern Warfare 2 - All Weapons Showcase (Reloads,
  Sounds, Animations).mp3`, all at the **identical source offset**, 1120.185732 seconds — i.e. one
  fixed extracted sound, reused 125 times.
- **This source file is, per direct confirmation from the person the montage was made for, simply
  Editor 1's personal sample library for extracting individual gun sounds — a practical storage
  convenience, not itself an editorial technique to model.** The per-event processing applied to
  each of the 125 extracted instances (below) is the actual editorial mechanism.
- All 125 have `playbackRate: 1` (no time-stretch).
- 119 events are 0.850125 seconds long; the other 6 range 0.674407–1.084417s.
- All use Fast fade-in / Slow fade-out curve labels; 109/125 fade out across their complete
  duration; median fade-out 0.850125s, median fade-in 0s.

**[E1-P01-AUD-005, DIRECT_PROJECT_OBSERVATION]**

### Pitch and reverb (SFX layering mechanism)

Every one of the 125 replacement-weapon events carries exactly two per-event effects, in order:
`Pitch Shift` → `eFX_Reverb (VST2, 64-bit)`. **This is the confirmed mechanism for gun-to-gun sonic
variety**: one fixed source sample, individually processed per event. **The exact pitch-shift
amount and reverb settings per event are not recoverable** — these are classic (non-OFX) VEGAS/VST
effects, and `Effect.Keyframes.Preset` (the only field that would expose the configured value) has
no getter in the `ScriptPortal.Vegas` API. **[E1-P01-AUD-006, DIRECT_PROJECT_OBSERVATION /
E1-P01-LIM-002 for the recoverability gap]**

### Normalization (corrected finding)

**118 of 125 replacement-weapon events ARE normalized** (`normalizeGain` ≈ 2.331107 linear ≈
+7.35 dB); only 7 are not. An earlier pass of this investigation incorrectly claimed all 125 were
unnormalized, generalizing from one non-representative sampled event that turned out to belong to
the retained-audio category, not the replacement-weapon category. **[E1-P01-AUD-007,
DIRECT_PROJECT_OBSERVATION — supersedes an earlier incorrect claim, see
evidence-register.md]**

### Fade `gain` field (clarified mechanism)

The exported per-event `Fade.Gain` field is **a static, event-level output trim, identical on both
`fadeIn` and `fadeOut` for a given event** (confirmed across all 125 weapon-SFX events and all 276
Track-1 events) — **not** a fade-curve-specific value. It correlates exactly with each event's
normalize/normalizeGain state. **[E1-P01-AUD-008, DIRECT_PROJECT_OBSERVATION]**

## SFX/marker/cut/velocity alignment

**Extended from an initial n=25 sample to the full n=125 corpus (this investigation's adversarial
pass, `kill-alignment-corpus.json`):**

- **122/125 (97.6%) start within 30ms of a timeline marker; median delta 0ms** (effectively
  frame-exact).
- 3 confirmed counterexamples (`t2_e103` at 180.897s, `t2_e104` at 181.765s, `t2_e105` at
  184.067s), independently cross-validated by Editor 1's separate analysis, which found the same
  three timestamps as marker-alignment exceptions.
- 97.6% of gun events show a "fast entry" velocity-phase signature at their start (velocity phase
  index 0), extending Editor 1's smaller-sample finding to the full corpus.

**[E1-P01-TIM-002, DIRECT_PROJECT_OBSERVATION]**

Editor 1's original count (n=125, schema-1 export): 122/125 replacement sounds start within 1ms of
a marker, main-video event boundary, and velocity-envelope point simultaneously; 123/125 within one
project frame of a velocity point. Replacement-event *ends* generally do not align to markers
(median nearest-marker distance 0.200925s). **[E1-P01-TIM-003, DIRECT_PROJECT_OBSERVATION]**

## First, middle, and final-kill audio patterns

**Not established from audio data alone.** Editor 1's audio-forensics report is explicit: "The data
does not establish that this replacement layer uses different samples or recipes for first,
middle, and final kills. It establishes the opposite at the source level: all replacement events
use the same source position. Any variation must come from gain, Pitch Shift, Reverb, track
processing, or layering" — none of which are recoverable through the current tooling.
**[E1-P01-AUD-009, DIRECT_PROJECT_OBSERVATION]**

## Whoosh / transition sounds

- Track 4: 24 events, 100% sourced from one file, `woosh2.mp3`. 23 are 3.960552s; one is
  3.585798s. All begin at source offset zero. Every whoosh fades out across its complete event.
  Fast fade-in / Slow fade-out curve labels throughout.
- Only 2/24 starts are within 1ms of a marker or video boundary (3/24 within one frame) — whooshes
  are not frame-locked to a cut.
- **23/24 whooshes precede the nearest subsequent source-file change by approximately 1.85–3.49
  seconds** (Editor 1's count). This investigation's independent re-derivation on the
  raw/connective-footage subset specifically found **19/22 (86%)** of raw/connective-clip entries
  are preceded by a whoosh in the same 1.85–3.49s window (median ≈2.2s).
- The final whoosh (232.111956s in Editor 1's count) has no nearby subsequent source-file change —
  likely an outro treatment, not a mid-edit transition cue.

**[E1-P01-AUD-010, DIRECT_PROJECT_OBSERVATION]**. **Interpretation** (repeated within this project,
not established as a general convention): a whoosh functions as a lead-in transition cue
specifically for connective-footage cutaways, anticipating the cut rather than landing on it.
**[E1-P01-AUD-011, EDITOR_1_PROJECT_PATTERN]**

## Same-source excerpt evidence (single-sample reuse)

Confirmed directly: all 125 replacement-weapon events share one identical source offset into one
identical file — the clearest possible evidence of single-excerpt reuse with per-event processing,
rather than a library of many distinct gunshot recordings. **[E1-P01-AUD-012,
DIRECT_PROJECT_OBSERVATION]**

## Unsupported or unavailable audio parameters

- `Pitch Shift` and `eFX_Reverb` exact per-event settings: unrecoverable (classic/VST effect,
  `ScriptPortal.Vegas` API limitation).
- `Track Noise Gate` / `Track EQ` / `Track Compressor` parameter values on any of the 3 audio
  tracks: unrecoverable, same reason.
- Peak/integrated loudness, stereo placement, and reverb-tail-overlap behavior: **not measured**
  in this investigation beyond a single whole-track peak/RMS figure for the music (see
  [limitations.md](limitations.md)) — Peak **-2.47 dBFS**, whole-track RMS **-11.64 dBFS**, zero
  clipped samples, from a solo render of the music track. No SFX-solo or full-mix render was
  produced. **[E1-P01-AUD-013, DIRECT_PROJECT_OBSERVATION (music-track loudness only)]**
