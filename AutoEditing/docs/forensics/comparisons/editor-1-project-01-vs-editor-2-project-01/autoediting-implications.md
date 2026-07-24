# AutoEditing Implications (Phase 15)

Per the prompt's explicit instruction: **this document does not modify production code or general
editing rules.** It lists proposed promotions for later human review, each citing supporting
evidence IDs from both packages, sample sizes/counterexamples, and required validation.

## 1. Replicated candidate defaults

| Proposal | E1 evidence | E2 evidence | Confidence | Required capability | Fallback |
|---|---|---|---|---|---|
| Align the primary hit/impact SFX event to both a timeline marker and a video-event boundary simultaneously | TIM-002 (97.6%, n=125) | AUD-008 (100%, n=7) | High (structural, both projects) | None — pure timeline placement | N/A |
| Generate crossfades via simple overlap-and-let-VEGAS-auto-blend, using Smooth curves, rather than authoring bespoke transition curves | TRN-004 (n=114) | TRN-004 (n=16/17) | High | Native VEGAS crossfade | N/A |
| Do not implement a track/bus/master ducking feature by default | AUD-002 | AUD-006 | High (as an absence in both) | N/A | If ducking is wanted, it must be designed fresh — neither reference project demonstrates a recipe |

## 2. Optional style presets

| Proposal | Evidence | Confidence | Notes |
|---|---|---|---|
| Fast-in/Slow-out fade curve on the music track | E1 AUD-001, E2 AUD-004 | Medium-high | Both projects use it, but it's a style choice, not load-bearing |
| `Mo Blur Length≈0.8` as a starting point for a Shake-based hit effect (below Sapphire's documented 1.0 realistic default) | E1 FX-007, E2 FX-002 | Medium | Real convergence on one value; still only 2 data points |
| Velocity plateau ≈0.5x | E1 VEL-003 (n=275), E2 VEL-003 (n=1) | Medium (E1) / Low (E2) | Treat as a reasonable default range, not a fixed constant |

## 3. Editor/project-specific presets (do not generalize)

| Item | Belongs to |
|---|---|
| Two-tier ordinary/impact effect system | Editor 1 only |
| Project-wide persistent texture-overlay clip (`Screen` blend) | Editor 1 only |
| `S_FilmDamage`-based per-event texture treatment | Editor 2 only |
| Track-level Composite-envelope opacity strobe + matching solid-color flash pair | Editor 2 only |
| Dedicated whoosh/transition-cue sample | Editor 1 only |
| Non-zero track-fader attenuation (-3dB-ish) | Editor 1 only; Editor 2 uses 0dB |
| 7-point double-dip velocity shape | Editor 2 only; Editor 1's dominant shape is a 4-point single-dip |

## 4. Capability-gated plugin presets

| Item | Requires | Fallback if unavailable |
|---|---|---|
| Editor 1's impact-family `S_DistortRGB` chromatic distortion | Sapphire (`S_DistortRGB`) | Native RGB-channel-offset effect, or skip |
| Editor 1's 4 unavailable Red Giant/Boris FX effects | Those specific plugins | Unknown — never render-verified, cannot recommend a fallback with confidence |
| Editor 2's `S_FilmDamage` | Sapphire (`S_FilmDamage`) | A native film-grain/vignette combo could approximate the texture component, but not the bundled shake/flicker sub-parameters |

## 5. Native VEGAS fallbacks

Not deeply explored in either investigation — flagged as a genuine gap. The clearest candidate is
the automatic-crossfade mechanism itself (item 1 above), which is already a native VEGAS behavior
requiring no plugin.

## 6. Experimental rules (require further validation before use)

| Rule | Why experimental |
|---|---|
| Connective-footage placement (after impact, before setup, crossfaded both sides, whoosh-preceded) | Well-evidenced in Editor 1 only; not tested against Editor 2 this pass |
| Curated-vs-raw source tiering with per-tier differential treatment | Both projects tier their sources, but *what* each tier gets differs completely — no shared per-tier rule can be proposed yet |
| Single fixed source-excerpt reuse for hit/impact SFX | Confirmed as a shared *principle* but not a shared *mechanism* — needs a third project to see which specific technique (if either) is more common |

## 7. Rules requiring more projects

Everything in [rule-comparison.md](rule-comparison.md) marked `Contradicted` or `Insufficiently
supported` — most notably: dominant velocity-curve shape, effect-vocabulary complexity
(two-tier vs. flat), crossfade rate, coverage continuity, track-fader attenuation convention, and
whether a whoosh-style transition cue is common practice. Two projects is not enough to resolve any
of these; a third and fourth reference project are needed before proposing defaults.

## 8. Behaviors contradicted or unsafe to generalize

- **"Picture coverage is always continuous"** — directly falsified by Editor 2's one gap. Do not
  build an AutoEditing invariant that assumes zero gaps are required.
- **"A two-tier ordinary/impact effect system is how sniper montages work"** — falsified; Editor 2
  achieves its effect with a flatter vocabulary. Do not hard-code a two-tier assumption.
- **Any specific track-fader dB value** — the two projects disagree (non-zero vs. zero); neither
  should be treated as correct.
- **The zoom/`Z Dist` pulse as part of "the" hit-effect signature** — present in Editor 1, absent
  in Editor 2. Do not assume a hit effect requires a zoom component.

## Summary status against the prompt's Phase-15 requirement

Every proposal above cites supporting evidence IDs from both packages, states sample size/
counterexamples where known, and states what further validation is required. No production code,
`docs/editing-rules.md`, `docs/editing-pipeline.md`, or `docs/effect-preset-architecture.md` was
modified as part of this work.
