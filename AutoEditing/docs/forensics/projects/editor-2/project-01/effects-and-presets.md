# Effects and Presets — Editor 2 / Project 01

Distinguishes mechanically recoverable presets from visually verified effects. Evidence IDs
indexed in [evidence-register.md](evidence-register.md). No effect chain is labeled a "screen
pump" or similar perceptual term without render confirmation; where confirmation wasn't obtained
this pass, the exact mechanical treatment is described instead.

## Effect-chain distribution (Track 4, 158 events)

| Chain | Events |
|---|---:|
| `S_Shake` alone | 124 (78%) |
| `S_FilmDamage` alone | 17 (11%) |
| (no event-level effects) | 11 (7%) |
| `S_Shake` → `S_Flicker` | 2 |
| `S_Shake` → `S_Flicker` → `S_Glow` | 1 |
| `S_Shake` → `S_Glow` | 1 |
| `S_Shake` → `S_FilmDamage` | 1 |
| `S_Flicker` alone | 1 |

**[E2-P01-FX-001, DIRECT_PROJECT_OBSERVATION]**

This is a materially simpler effect vocabulary than Editor 1's project, which used two clearly
distinguished 4-effect chains (an "ordinary" family and a separate, rarer "impact" family). Here,
**one single-effect treatment (`S_Shake` alone) covers the large majority (78%) of treated
events**, with no separate escalated multi-effect "impact" family at comparable scale — the
handful of compound chains (5 events total, 3%) are too few to constitute a distinct reusable
family the way Editor 1's 26-event impact family did.

## `S_Shake` mechanism (dominant treatment, 124/158 events)

**Stored/animated parameters** (representative event `t4_e3`, 1.333s long):

| Parameter | Stored keys |
|---|---|
| `Amplitude` | `0:1 → 0.417s:0` (decays to zero) |
| `Frequency` | `0:12 → 0.417s:0.1` (decays sharply) |

**Static (non-animated) parameters of note**: `Motion Blur = true` (default `false` — the same
static override pattern found in Editor 1's impact recipe), `Mo Blur Length = 0.8` (**identical
value to the first of Editor 1's two stacked `S_Shake` instances** — a genuine cross-project
match candidate, see [evidence-register.md](evidence-register.md)), `Z Dist = 1` (static, **not
animated** — unlike Editor 1's impact family, which did animate `Z Dist` 0.9→1.0 as a small zoom
pulse; no zoom mechanism is used here). **[E2-P01-FX-002, DIRECT_PROJECT_OBSERVATION]**

**Only one `S_Shake` instance per event** (never two stacked instances, unlike Editor 1's impact
family) — the shake/motion-blur decay is a single, simpler mechanism applied broadly rather than
a rare, escalated compound treatment. **[E2-P01-FX-003, DIRECT_PROJECT_OBSERVATION]**

**Visually confirmed**: `06_multihit_burst_start.png` (a Shake-treated event) shows heavy
directional motion blur consistent with the Motion-Blur-forced-true mechanism already established
for Editor 1's project — the same underlying Sapphire mechanism produces a visually similar result
here. **No controlled ablation (bypass on/off comparison) was run this pass** — this is inferred
from the parameter pattern plus one corroborating frame, not independently isolated the way
Editor 1's Part-1 ablation isolated Motion Blur specifically. Flagged as reduced-scope in
[limitations.md](limitations.md). **[E2-P01-VIS-004, DIRECT_PROJECT_OBSERVATION, visual
confidence: medium — not ablation-isolated]**

## `S_FilmDamage` mechanism (17/158 events, plus 1 compound instance)

A single Sapphire effect bundling film-grain/scratches/dust/hair simulation together with its
**own internal shake, flicker, and defocus sub-parameters** — mechanically distinct from Editor 1's
approach (a separate always-on Screen-blended overlay *clip* for texture, with `S_Shake` as an
entirely separate effect for motion). Representative event (`t4_e0`, the first Track-4 event,
0-5.333s):

- **Animated parameters**: `Shake Frequency` (0→2.449 over 0.764s), `Shake Jumpiness` (0→0.422),
  `Shake Random` (0.565→0), `Shake Always` (0.463→0) — a "settling" pattern (shake intensity
  building up, other shake-randomness parameters settling down) specific to this opening event.
- **Static parameters of note**: `Shake Amplitude=0.204`, `Shake Motion Blur=0.122`,
  `Flicker=0.258`, extensive grain/stain/dust/scratch/vignette parameters (48+ static values total)
  configured as a specific "look," not left at effect defaults.

**[E2-P01-FX-004, DIRECT_PROJECT_OBSERVATION]**

This effect is applied predominantly to **raw/connective DVR footage** (not the curated highlight
clips) — consistent in spirit with Editor 1's finding that raw/connective footage receives
different (there, no per-event effects at all) treatment than curated highlights, though the
specific mechanism differs (Editor 2 applies a dedicated texture/shake effect to this footage tier
rather than leaving it untreated). **Not fully cross-tabulated** (which specific sources get
`S_FilmDamage` vs. no treatment at all among the 11 no-effect events) — flagged as an open
follow-up. **[E2-P01-FX-005, EDITOR_2_PROJECT_PATTERN — single-project pattern, not yet
cross-validated]**

## Missing/unavailable plugins

**None found.** A structural scan of every Track-level and Event-level `Effects` entry across all
7 tracks found zero instances of `pluginAvailable: false`. All effects used are installed and
loaded successfully on the inspection machine. **[E2-P01-FX-006, DIRECT_PROJECT_OBSERVATION]**

## Track-level effects

Tracks 2 and 4 both carry a track-level `S_Flicker` effect (bypass=false). Unlike Editor 1's
project (which had both `S_Shake` and `S_Flicker` as always-on track-level effects on its main
track), Editor 2's main track (Track 4) has **only `S_Flicker` at track level, no `S_Shake`** — the
shake/motion-blur mechanism here is purely event-level. **[E2-P01-FX-007,
DIRECT_PROJECT_OBSERVATION]** Parameter values for these track-level `S_Flicker` instances were
not individually inspected in this pass (reduced scope).

## Per-candidate-preset structured record

| Field | `S_Shake` (dominant) | `S_FilmDamage` | Compound chains (5 events) |
|---|---|---|---|
| Chain order | Shake alone | FilmDamage alone | Shake + {Flicker, Glow, FilmDamage} in various combos |
| Scope | event | event | event |
| Parameters | Amplitude, Frequency, Motion Blur (static true), Mo Blur Length=0.8 | Shake Frequency/Jumpiness/Random/Always + 48 static grain/stain/dust params | combination of the above |
| Time basis | unresolved (assumedTimelineAbsoluteSeconds = eventStart+raw, not independently confirmed) | unresolved, same caveat | unresolved |
| Expected visual role | primary "hit" motion-blur treatment, applied broadly | texture/grain treatment for raw footage, occasionally with settling shake | rare stronger/compound accent |
| Visual verification status | Partially confirmed (1 corroborating frame, no ablation) | Not independently visually verified this pass | Not verified |
| Dependencies | Sapphire (installed) | Sapphire (installed) | Sapphire (installed) |
| Reproducibility | High (124 instances, single representative sampled in full detail) | Medium (17 instances, 1 sampled in full detail) | Low (n≤2 per exact combination) |
| Evidence IDs | FX-002, FX-003, VIS-004 | FX-004, FX-005 | FX-001 |
