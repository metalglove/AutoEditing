# Effects and Presets — Editor 1 / Project 01

Distinguishes mechanically recoverable presets from visually verified effects, per this package's
evidence-quality requirements. Evidence IDs indexed in [evidence-register.md](evidence-register.md).
Use [media-less-vegas-project-value.md](../../../media-less-vegas-project-value.md) as the general
methodology reference for the mechanical-recoverability-versus-visual-verification distinction
applied throughout this document.

**Note on terminology**: this document does not label any effect chain a "screen pump" from
parameter names alone. Where the final rendered result was verified (via controlled ablation and
rendered-frame pixel comparison), the exact mechanical driver is named directly. Where it was not,
the mechanical treatment is described without a perceptual label attached.

## Effect-chain order and instance counts (project-wide)

| Effect | Instances | Scope |
|---|---:|---|
| `S_Shake` | 277 | 2 track-level (Track 1) + up to 2 per event |
| `Bump Map` | 224 | event |
| `S_Glow` | 224 | event |
| `S_BlurMoCurves` | 224 | event |
| `Pitch Shift` | 125 | event (audio) |
| `eFX_Reverb (VST2, 64-bit)` | 125 | event (audio) |
| `S_Flicker` | 29 | 2 track-level + up to 1 per event |
| `S_DistortRGB` | 26 | event |
| `Track Compressor` | 3 | track (one per audio track) |
| `Track Noise Gate` | 3 | track |
| `Track EQ` | 3 | track |
| `Black and White` | 1 | event |

**[E1-P01-FX-001, DIRECT_PROJECT_OBSERVATION]** (Editor 1's original count, cross-verified by this
investigation's full-corpus signature pass — see below.)

## Full-corpus preset-signature clustering (this investigation, adversarial pass)

An earlier pass (both Editor 1's and this investigation's) characterized the project as having
essentially two exact, byte-identical presets applied uniformly. A full-corpus SHA1 signature hash
— computed over effect-chain order, plugin unique IDs, bypass state, Motion Blur toggle, and every
animated parameter's keyframe times/values/interpolation, across **all 276 Track-1 events** —
found **7 distinct signatures**, not 2:

| Signature group | Count | Description |
|---|---:|---|
| `9326fc860de1` | 187 | "ordinary" family, compact duration variant |
| `3a48ffaa6082` | 36 | "ordinary" family, long duration variant (same shape, ~2× keyframe timing) |
| `da39a3ee5e6b` | 24 | no event-level effects |
| `51bf6e855d1d` | 24 | "impact"/distortion family, canonical |
| `5fd5fe6bfa56` | 2 | intro-flicker-only |
| `5a77d2b298c1` | 1 | "ordinary" family + `Black and White` |
| `154b12097faf` | 1 | impact-family near-duplicate |
| `3848cc08cb28` | 1 | impact-family near-duplicate (`t1_e275`) |

**[E1-P01-FX-002, DIRECT_PROJECT_OBSERVATION]**. Corrected finding: each named recipe is one
reusable *template*, applied at more than one fixed duration — not one literal invariant preset
copy-pasted with zero variation, as an earlier pass claimed.

`t1_e275` (signature `3848cc08cb28`) was confirmed, via direct query, to have no replacement-gun
audio at its start — evidence that it is a **trimmed tail of a preceding event**, not an
independent kill moment. **[E1-P01-FX-003, DIRECT_PROJECT_OBSERVATION]**

## Family A — "ordinary" / common rhythmic-segment treatment

**Chain (event scope, in order):** `S_Shake → S_BlurMoCurves → Bump Map → S_Glow`.

**Stored parameters (mechanically recoverable, all instances):**

| Parameter | Stored keys (raw seconds : value) |
|---|---|
| `S_Shake.Amplitude` | `0:0`, `0.166834:0.2`, `1.101100:0` |
| `Bump Map.Intensity` | `0:0.05`, `1.101104:0` |

Two duration variants share this shape:

| Variant | Instances | `S_BlurMoCurves.Z Dist` | `S_Glow.Brightness` |
|---|---:|---|---|
| Compact | 187–188 (count differs by ±1 across the two independent counting passes) | `0:1`, `0.083417:0.96`, `0.934270:1` | `0:2`, `0.383718:0` |
| Long | 36 | `0:1`, `0.166834:0.96`, `1.868541:1` | `0:2`, `0.767436:0` |

**[E1-P01-FX-004, DIRECT_PROJECT_OBSERVATION]**

**Not a Pan/Crop-style zoom.** The stored zoom-like component is a modest ~4% inward `Z Dist` pulse
in `S_BlurMoCurves`; the `S_Shake` event instance is bypassed on these events in the sampled cases
this investigation directly re-examined (see the Part-7 ablation below) — Shake amplitude in the
table above is the *track-level* ambient contribution's interaction, not necessarily a live
per-event Shake in every instance. **This is flagged, not fully reconciled, between Editor 1's
count (S_Shake listed as part of the 4-effect Family A chain) and this investigation's direct
per-event inspection (which found `S_Shake` and `Bump Map` bypassed on the specific sampled event
`t1_e3`, with only `S_BlurMoCurves` and `S_Glow` actually active/non-bypassed)** — see
[limitations.md](limitations.md) for this open reconciliation item.
**[E1-P01-FX-005, DIRECT_PROJECT_OBSERVATION]**

### Visual verification (this investigation, controlled ablation)

A controlled on/off ablation was run on event `t1_e3` (bypassing `S_BlurMoCurves` and `S_Glow`,
identical timestamp, before/after/restore frames captured via `SaveSnapshot`):

| | Edge energy | R-G sep | G-B sep | Mean luma |
|---|---:|---:|---:|---:|
| ON (as authored) | 1.667 | 4.877 | 5.824 | 91.92 |
| OFF (bypassed) | 2.329 | 4.983 | 4.459 | 89.33 |

Edge-energy ratio 0.716 (28% sharpness loss when active), mean-luma delta +2.59 (brighter when
active). Directly viewing the two frames shows a real, visible softening of fine on-screen HUD
text in the "ON" frame. **Restore was verified exact** (`meanAbsDiffRestoredVsOn = 0.000`).

**Conclusion, narrower than an earlier "reads as a plain hard cut" claim**: this treatment produces
a real, measurable, non-negligible softening-plus-glow pulse on every cut it's applied to. It is
subtle relative to Family B below (~28% edge-energy loss vs. Family B's 60–65%) and unlikely to be
*consciously* noticed at normal viewing speed, but it is not a no-op.
**[E1-P01-VIS-001, DIRECT_PROJECT_OBSERVATION]**

### Context evidence (Editor 1's clustering)

- 212/223 sampled instances have a marker-aligned edge; 222/223 have a velocity envelope; only
  95/223 begin with replacement-gun audio. This family is broader than "kill preset" — it reads as
  a reusable rhythmic-segment treatment applied to most split gameplay segments, not exclusively to
  kills. **[E1-P01-FX-006, DIRECT_PROJECT_OBSERVATION → interpretation is EDITOR_1_PROJECT_PATTERN]**

## Family B — "impact"/distortion/terminal treatment

**Chain (event scope, in order):** two stacked `S_Shake` instances → `S_Flicker` → `S_DistortRGB`.

**Stored parameters (mechanically recoverable):**

- First `S_Shake.Amplitude`: `-0.116783:1.1`, `0:0.7 (Fast)`, `0.250251:0` — note the **negative
  raw keyframe time**, decisive evidence that raw OFX key time cannot be blindly treated as
  event-relative visible time (see [limitations.md](limitations.md)).
- First `S_Shake.Z Dist`: `0:0.9`, `0.250251:1` (a genuine ~10% inward zoom pulse, recovering over
  ~250ms — the clearest stored zoom component found anywhere in the project).
- Second `S_Shake.Amplitude`: `0:0.2 (Slow)`, `0.517183:0.04`.
- `S_DistortRGB.Amount`: `0:0.068 (Slow)`, `0.500501:0`.
- `S_Flicker.Amplitude`: usually `0:0.503401`, `1.868535:0`.
- Both `S_Shake` instances have `Motion Blur` explicitly forced `true` (a static, non-animated
  override) with `Mo Blur Length` **0.8** (first instance) and **2.0** (second instance), identical
  across every sampled instance to at least 5-decimal precision.

**[E1-P01-FX-007, DIRECT_PROJECT_OBSERVATION]**

### Visual verification (this investigation, full ablation, 13 variants + reference)

A controlled ablation on event `t1_e16` (a confirmed impact-family event) — toggling `Bypass` and
the `Motion Blur` boolean on each of the 4 effects independently and in combination, 13 named
variants + 1 ordinary-family reference frame, all snapshotted at the identical post-cut timestamp
via `SaveSnapshot`, metrics computed with a hand-rolled PNG decoder (edge-energy proxy, R-G/G-B
channel-separation proxy, mean luma):

| Variant | Edge energy (×baseline) | R-G sep | G-B sep |
|---|---:|---:|---:|
| Full original chain (baseline) | 1.00× | 19.83 | 29.57 |
| Both `Motion Blur` off | 1.96× | 26.35 | 35.29 |
| Both `S_Shake` disabled | 2.32× | 26.73 | 34.25 |
| `S_DistortRGB` disabled | 1.18× | 15.78 | 26.90 |
| `S_DistortRGB` in isolation | 1.99× | 25.82 | 27.82 |

**Findings, directly render-confirmed (not inferred from parameter names):**

- **The `Motion Blur` boolean toggle on `S_Shake` is the dominant blur driver** — disabling it
  alone (both instances) very nearly matches disabling `S_Shake` entirely (1.96× vs 2.32× baseline
  edge energy). The blur is specifically the Motion Blur rendering path, not an incidental
  side-effect of the Shake animation.
- **`S_DistortRGB` is the dominant chromatic-separation driver** — isolating it alone raises R-G
  separation above baseline (25.82 vs 19.83); removing it lowers R-G separation ~20% but the blur
  signature substantially survives (1.18× edge energy, i.e. still recognizable without it).
- **No measurable zoom/scale change** was found across any of the 14 captured frames — consistent
  with the stored `Z Dist` 0.9→1.0 pulse (a 10% nudge) being too subtle to register as a
  perceptible zoom at this frame resolution/metric.
- The first `S_Shake` instance dominates over the second (disabling instance 1 alone: 1.60× edge
  energy; instance 2 alone: 1.16×) — additive, not redundant, but clearly unequal contribution.
- Whether `S_Flicker` visibly contributes could **not** be established from single-frame
  comparison (its own `Amplitude` parameter is itself animated, so a static-frame test cannot
  separate "no effect" from "sampled near a low point in its own curve").

**[E1-P01-VIS-002, DIRECT_PROJECT_OBSERVATION]**

**Conclusion**: the signature visual result of this family is **a motion-blur hit (driven
specifically by the Motion Blur toggle) as the dominant, load-bearing component, with RGB
chromatic distortion as a real but secondary layered embellishment** — a compound recipe, not an
equal-weight ensemble, and not a Pan/Crop zoom. This is a render-confirmed conclusion, not a
parameter-name inference.

### Track-level vs. event-level interaction (this investigation)

A separate ablation (2 events × 4 track/event-FX bypass combinations) found: **event-level effects
dominate/mask the track-level ambient `S_Shake`/`S_Flicker` on strong (impact-family) events; the
track-level ambient effects are proportionally more visible on weak (ordinary-family) events.**
**[E1-P01-VIS-003, DIRECT_PROJECT_OBSERVATION]**

### Context evidence (Editor 1's clustering)

- 25/26 sampled instances begin on both a marker and a replacement-gun-audio event; 19/26 end a
  contiguous same-source run. This supports (does not by itself prove) a kill/terminal-accent
  role, but "always the final kill" is false — 7 events are not at a source-run boundary, and one
  has neither exact marker nor replacement-audio alignment.
  **[E1-P01-FX-008, DIRECT_PROJECT_OBSERVATION]**

## Family C — untreated / raw-connective segments

24 Track-1 events (excluding the Track-0 overlay) have no event-level effects. 20/24 are both the
start and end of their own source run; only 1/24 starts with replacement-gun audio. Sources are
predominantly raw DVR-style filenames rather than the curated `Opener`/`Quad`/`Closer` files.
**[E1-P01-FX-009, DIRECT_PROJECT_OBSERVATION]**

**More precise characterization (this investigation)**: of these 24 "no-effects" events, **18/24
pair 1:1 (0ms start delta, identical source file) with a retained-native-audio event on Track 2**
— see [audio-treatment.md](audio-treatment.md). The remaining **6/24 are sourced from the curated
`Opener`/`Quad`/`Closer` folders** and get no paired audio at all. This is a clean, deterministic
split by source-folder origin, not a loosely-understood minority category.
**[E1-P01-FX-010, DIRECT_PROJECT_OBSERVATION]**

## Family D — intro-flicker

2 events, both the first two Track-1 events, each ~4.2–4.3 seconds (much longer than typical
treated segments), animated `S_Flicker.Amplitude` only, no replacement-gun audio.
**[E1-P01-FX-011, DIRECT_PROJECT_OBSERVATION]**

## Family E — monochrome accent

1 event (`t1_e113`, at 116.316s), the ordinary-family chain plus a trailing `Black and White`
effect. Confirmed, via representative-frame capture, to render as genuinely desaturated/grayscale.
One occurrence — insufficient to derive a reusable selection rule.
**[E1-P01-FX-012, DIRECT_PROJECT_OBSERVATION / E1-P01-VIS-004, DIRECT_PROJECT_OBSERVATION for the
visual confirmation]**

## Preset names

`EffectPreset.Name` returns `(Default)` for every OFX effect checked in this project — VEGAS/
Sapphire's convention for "not currently on a named preset," not a data gap.
**[E1-P01-FX-013, DIRECT_PROJECT_OBSERVATION]**

## Missing-plugin limitations affecting this document

See [project-profile.md](project-profile.md) and [limitations.md](limitations.md) for the full
missing-plugin inventory. None of the 4 missing/unavailable plugins were found in any Track-level
or Event-level `Effects` collection in the exported schema — by elimination consistent with (not
proof of) the belief that at least one (Boris FX "Vector Blur Dissolve") is attached via a
`Transition` object, which no inspector version has ever exported.
**[E1-P01-LIM-001, DIRECT_PROJECT_OBSERVATION]**

## Candidate native VEGAS fallbacks

Not evaluated in this investigation. Flagged as a gap requiring dedicated design work, not
attempted here.

## Per-candidate-preset structured record

For every named family above (A–E), the following fields are recorded per the required schema:

| Field | Family A (ordinary) | Family B (impact) | Family C (untreated) | Family D (intro-flicker) | Family E (monochrome) |
|---|---|---|---|---|---|
| Chain order | Shake→BlurMoCurves→BumpMap→Glow | Shake→Shake→Flicker→DistortRGB | (none) | Flicker | ordinary chain + B&W |
| Scope | event | event | n/a | event | event |
| Parameters | see FX-004 | see FX-007 | n/a | Amplitude | see FX-004 |
| Keyframes | see FX-004 | see FX-007 | n/a | animated Amplitude | see FX-004 |
| Time basis | unresolved (see limitations.md) | unresolved, negative-key evidence present | n/a | unresolved | unresolved |
| Expected visual role | rhythmic-segment treatment | kill/terminal accent | clean connective bridge | opening specialization | one-off accent |
| Visual verification status | Confirmed (VIS-001) | Confirmed (VIS-002) | Not separately verified | Not verified | Confirmed (VIS-004) |
| Dependencies | Sapphire (installed) | Sapphire (installed) | n/a | Sapphire (installed) | Sapphire (installed) |
| Reproducibility | High (stable signature, 2 duration variants) | High (stable signature, FX-002) | n/a | Low sample size (n=2) | Low sample size (n=1) |
| Evidence IDs | FX-004, FX-005, FX-006, VIS-001 | FX-007, FX-008, VIS-002, VIS-003 | FX-009, FX-010 | FX-011 | FX-012, VIS-004 |
