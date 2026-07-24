# Preset / Effect-Chain Comparison

## Overall vocabulary complexity

Editor 1: two clearly distinguished multi-effect chains — an "ordinary" 4-effect family
(`S_Shake`→`S_BlurMoCurves`→`Bump Map`→`S_Glow`, 224/276 events) and a separate, rarer "impact"
4-effect family (two stacked `S_Shake` instances→`S_Flicker`→`S_DistortRGB`, 26/276 events).

Editor 2: one dominant single-effect treatment (`S_Shake` alone, 124/158 events) plus a separate
single-effect texture treatment (`S_FilmDamage` alone, 17/158), with only 5 rare compound-chain
events total — no distinct, comparably-sized escalated "impact" family.

**Classification: Contradicted as a shared structure.** Editor 1's two-tier ordinary/impact system
does not replicate in Editor 2, which uses a flatter, simpler vocabulary. This directly bears on
`E1-P01-FX-004`'s implicit assumption that a two-tier system is how sniper montages work — it does
not generalize even to this second data point.

## The Shake/Motion-Blur "hit" mechanism specifically

| | Editor 1 (impact family) | Editor 2 (dominant family) |
|---|---|---|
| `S_Shake` instances per event | 2 (stacked) | 1 |
| Animated parameters | Amplitude, Z Dist (both instances) | Amplitude, Frequency |
| `Motion Blur` (static override) | `true` (default `false`) | `true` (default `false`) |
| `Mo Blur Length` | 0.8 (instance 1), 2.0 (instance 2) | **0.8** |
| Zoom component | Yes — `Z Dist` 0.9→1.0, ~10% pulse | No — `Z Dist` static at 1 |

**Classification: Shared technique, different parameters**, with one striking exact match: **both
editors independently use `Mo Blur Length=0.8`** on the first/only `S_Shake` instance in their
respective hit-treatment recipes. Boris FX's own documentation (cited during the Editor 1
investigation's external-practice research, `C:\VEGAS\external-practice-comparison.md` — a raw
research artifact, not part of this repository) states Sapphire's realistic-motion-blur guidance
is "around 0.5 when processing on fields or 1.0 for frames," making 1.0 the documented
frame-mode default. Two independent editors landing on the same non-default value (0.8) — Editor
1 on its first of two stacked instances, Editor 2 on its only instance — is a genuine, specific
convergence — not proof of a universal constant, but a stronger candidate for cross-project
comparison than most other findings here. **[E1-P01-FX-007 vs. E2-P01-FX-002]**

Editor 1's impact recipe additionally carries a real (if small) zoom-pulse component (`Z Dist`);
Editor 2's does not use `Z Dist` at all for its dominant treatment. **Classification: Different
parameters** — the zoom aspect does not replicate.

## Texture/damage effects

Editor 1: a single, project-wide `Screen`-blend overlay *clip* (external dirt/scratch footage)
provides texture uniformly across the whole edit. Editor 2: `S_FilmDamage`, a dedicated Sapphire
plugin bundling grain/stain/dust/scratches *and* its own internal shake/flicker/defocus
sub-parameters, applied per-event to a subset of (mostly raw/connective) footage.

**Classification: Contradicted as a shared mechanism** — the two editors achieve a superficially
similar "damaged film" aesthetic through mechanically unrelated means (one persistent overlay clip
vs. one dedicated effect applied selectively).

## Missing/unavailable plugins

Editor 1: 4 referenced-but-unavailable plugins (Red Giant Universe, Boris FX Continuum ×2, one
unidentified). Editor 2: 0.

**Classification: Plugin/capability-dependent**, not evidence of editor convention — this
reflects what happened to be installed on each editor's original machine and doesn't bear on
either editor's technique preferences.

## Track-level ambient texture

See [structure-comparison.md](structure-comparison.md) — both projects use track-level
`S_Flicker`; only Editor 1 pairs it with track-level `S_Shake`.
