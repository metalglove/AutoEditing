# Representative Moments — Editor 1 / Project 01

Indexes every validated representative moment and frame capture produced across this
investigation. Evidence IDs indexed in [evidence-register.md](evidence-register.md). All frame
files referenced below are preserved, unmodified, at their original `C:\VEGAS\` paths (not copied
into this repository — see `AutoEditing/docs/vegas-integration-probe.md` for why source-media and
capture artifacts stay outside version control).

## Captured and confirmed (this investigation)

These 16 frames were actually captured via VEGAS's `SaveSnapshot` API and directly viewed as part
of this investigation, at `C:\VEGAS\representative-frames\`.

### `E1-P01-VIS-006` — Pre-song black field

- **Timestamp:** 10.0s. **Track/event:** none (before first video content).
- **Source filename:** n/a.
- **Why selected:** confirms nothing exists on screen before the first video event.
- **Structural evidence:** project's first Track-1 event starts at 26.643s; song event starts at
  23.557s.
- **Visual evidence:** `00_pre-song-blackfield.png` — completely blank canvas.
- **Interpretation:** consistent with a deliberate ~23–26s cold open (song audio starts before any
  picture).
- **Structural confidence:** high. **Visual confidence:** high (directly confirmed).
  **Semantic confidence:** medium (the "deliberate cold open" framing is an inference; the data
  only proves the timing gap exists).
- **Remaining ambiguity:** whether the gap was a deliberate creative choice vs. incidental.

### `E1-P01-VIS-007` — First video event

- **Timestamp:** 26.8s. **Track/event:** `t1_e0`.
- **Source filename:** raw/connective-footage DVR capture (see [timeline-structure.md](timeline-structure.md)
  for folder taxonomy), RIFE frame-interpolated.
- **Why selected:** first video event of the edit; carries the intro-flicker family (see
  [effects-and-presets.md](effects-and-presets.md) Family D).
- **Visual evidence:** `01_cinematic-bridge_t1_e0.png` — shows the project's own title card text
  over gameplay footage. A faint vertical scratch line and scattered dust specks are visible,
  confirming the Track-0 Screen-blend overlay renders as expected (subtle, not overwhelming).
- **Structural confidence:** high. **Visual confidence:** high. **Semantic confidence:** medium.
- **Remaining ambiguity:** none for the overlay-visibility claim; the broader "establishing shot"
  framing used in an earlier pass of this investigation is not strongly supported (the raw/
  connective folder tier is not an environment-only b-roll category — see
  [timeline-structure.md](timeline-structure.md)).

### `E1-P01-VIS-008` — No-effects counterexample

- **Timestamp:** 35.4s. **Track/event:** `t1_e2`.
- **Why selected:** one of the 24/276 Track-1 events with zero per-event effects.
- **Visual evidence:** `02_no-effects-counterexample_t1_e2.png` — plain, unmodified gameplay
  footage.
- **Structural/visual confidence:** high.

### `E1-P01-VIS-001` — Ordinary-family cut (see effects-and-presets.md for the full ablation)

- **Timestamp:** 35.90s/36.05s/36.35s (pre/at/post). **Track/event:** `t1_e3`.
- **Visual evidence:** `03_opener-dip_t1_e3_{pre,at,post}.png` — all three sharp at normal viewing
  resolution; the controlled on/off ablation (documented in
  [effects-and-presets.md](effects-and-presets.md)) later found a real, measurable but subtle
  28%-edge-energy-loss softening at this same event.
- **Visual confidence:** high for "reads as sharp at casual viewing"; the ablation refines this to
  "measurably softened but not consciously obvious."

### `E1-P01-VIS-002` — Impact-family cut (see effects-and-presets.md for the full ablation)

- **Timestamp:** 43.10s/43.28s/43.55s/43.90s (pre/at/post/settled). **Track/event:** `t1_e16`.
- **Source filename:** `Opener 01.mp4`.
- **Visual evidence:** `04_impact-distortRGB_t1_e16_{pre,at,post,settled}.png`. The "at" frame
  (~53ms after cut) shows a strong directional motion-blur smear with visible chromatic/RGB
  fringing; "pre" shows the game's own "ONE SHOT, ONE KILL" banner; "settled" (~0.67s later) shows
  the game's own "KILLED: [player]" confirmation text, fully sharp again.
- **Interpretation:** this is direct visual proof the cut lands on an actual in-game confirmed
  kill — one of the few moments in this investigation where the "kill" label is fully supported by
  both structural and rendered visual evidence, not inferred from timing alone.
- **Structural confidence:** high. **Visual confidence:** high. **Semantic confidence:** high (rare
  — the in-game kill-confirmation text is direct evidence, not circumstantial).

### `E1-P01-VIS-004` — Monochrome accent

- **Timestamp:** 116.9s. **Track/event:** `t1_e113`.
- **Visual evidence:** `05_black-and-white_t1_e113.png` — confirmed genuinely desaturated/grayscale.
- **Structural/visual confidence:** high. **Semantic confidence:** low (n=1, no selection rule
  established).

### `E1-P01-VIS-009` — Closer "hit, beat, hit" triple

- **Timestamps/events:** `t1_e272` (234.6s, hit1), `t1_e273` (235.6s, beat — ordinary family),
  `t1_e274` (236.35s/236.6s, hit2 at/post).
- **Source filename:** `Closer 01.mp4` (all three).
- **Visual evidence:** `06_closer-triple_*.png`. The `t1_e274` frames both still show heavy blur at
  +47ms and +300ms after cut — this event is short enough (0.834s) that the effect's decay window
  covers most of its visible duration.
- **Interpretation (project-internal pattern, not a general multi-kill rule)**: this is the closest
  analog to a "hit/beat/hit" punctuation found via direct frame inspection; it is a
  closer-section-specific device confirmed by this sample, not evidence of a project-wide
  first/middle/final-kill recipe on its own (see [velocity-findings.md](velocity-findings.md) for
  the broader, more general escalation pattern found later via source-run analysis).
- **Structural confidence:** high. **Visual confidence:** high. **Semantic confidence:** medium.

### `E1-P01-VIS-010` — Final event

- **Timestamp:** 237.25s. **Track/event:** `t1_e275`, the last Track-1 event.
- **Visual evidence:** `07_final-event_t1_e275.png`.
- **Interpretation:** confirmed via direct query (not visual inspection alone) to have no
  replacement-gun audio at its start — evidence this is a trimmed tail of the preceding event, not
  an independent kill (see [effects-and-presets.md](effects-and-presets.md) FX-003).

## Additional captured evidence: effect ablation frames (13+1 variants, `t1_e16`/`t1_e3`)

`C:\VEGAS\ablation-frames\` (14 PNGs + `ablation-log.txt` + `ablation-metrics.json`) and
`C:\VEGAS\ordinary-ablation-frames\` (3 PNGs + log) — the controlled on/off comparisons summarized
in [effects-and-presets.md](effects-and-presets.md) `E1-P01-VIS-001`/`E1-P01-VIS-002`. Method:
`Effect.Bypass` and OFX boolean-parameter toggling, in-memory only, never saved — see
[project-profile.md](project-profile.md) for the "whether the project was mutated" record.

## Additional captured evidence: track-vs-event ablation frames

`C:\VEGAS\track-vs-event-frames\` (8 PNGs + log) — 2 events × 4 track/event-FX-bypass combinations,
underlying `E1-P01-VIS-003` in [effects-and-presets.md](effects-and-presets.md).

## Planned but not yet executed against this project (Editor 1's validation plan)

Editor 1's `representative-moments-validation.md` proposed a 12-moment set with a disposable-copy
ablation matrix (8 variants: A–H, covering event-FX-only, track-FX-only, individual
effect-disable, velocity-neutralization, and audio-muting bypass combinations) and a frame-strip
comparison protocol. Several of its target events overlap with the captured set above (`t1_e16`,
`t1_e113`); several do not (`t1_e51`/`t1_e55`/`t1_e60` multi-kill first/middle/final comparison,
`t1_e80`, `t1_e175`, `t1_e203`, `t1_e255`, `t1_e272`/`t1_e274` under Editor 1's own timestamps,
which differ slightly from this investigation's re-derived timestamps for the same events — not
reconciled, see [limitations.md](limitations.md)).

**This plan was not executed as a full disposable-copy ablation matrix in this investigation.**
The specific ablations that *were* executed (impact-family full ablation, ordinary-family on/off
ablation, track-vs-event interaction) answer several of the plan's concrete hypotheses (the
Motion-Blur-driven blur mechanism, the track-vs-event interaction question) but not all of them
(e.g., variant G "neutralize velocity only" and variant H "mute replacement gun and whoosh
separately" were not run). This is recorded as `PRODUCTION_RULE_CANDIDATE`-blocking work, not
silently dropped — see [evidence-register.md](evidence-register.md) and
[limitations.md](limitations.md).

## Acceptance criteria not yet met

Per Editor 1's own acceptance criteria for this validation plan: "at least one first/middle/final
same-source sequence is compared" via rendered frames has **not** been executed (the
`t1_e51`/`t1_e55`/`t1_e60` triple was proposed but not captured); "the actual screen-pump producer
is demonstrated by ablation" **has** been met for the impact family (Motion Blur toggle, confirmed)
but not independently for the ordinary family's `S_BlurMoCurves` Z-Dist component in isolation
(the ordinary-family ablation tested `S_BlurMoCurves` and `S_Glow` bypassed together, not
separately).
