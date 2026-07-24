# Adversarial Verification Report — "montage 4" Forensic Analysis

## Purpose and method

This report closes out the adversarial verification pass: a 15-part instruction to actively try to
**falsify**, not confirm, every headline conclusion from the earlier forensic analysis and its
Codex-relayed follow-up priorities, reconcile with Codex's independently-produced reports
(`AutoEditing/docs/forensics/*.md`, read in full before this pass began), and produce 12 new
deliverables. The full machine-readable reconciliation — every claim labeled Confirmed / Confirmed
narrower scope / Revised / Falsified / Still unresolved — is in `reconciled-final-findings.json`.
This document is the narrative walkthrough of how that reconciliation was reached.

**Safety constraints maintained throughout**: `Untitled.veg`/`Untitled.veg.bak` were never opened by
any script this session; every automation run targeted the disposable `Untitled.relinked.veg` copy
(SHA-256 recorded in `effect-ablation-results.md`); no script called `SaveProject` except the one
deliberate relink-and-save step performed earlier in the investigation; every dialog-handling
decision erred toward killing a stuck process over blind-clicking an unidentified dialog (see the
real incident documented in `automation-safety-audit.md`); every generated script is preserved at
`C:\VEGAS\scripts\`.

## What was falsified

Two genuine, headline-level falsifications came out of this pass, both worth internalizing:

1. **"No multi-kill clustering pattern exists."** This was wrong because it checked spacing only
   within the 25-event impact population, ignoring the ordinary-family events around them. The
   correct analysis (source-run grouping, independently discovered by Codex via a different method
   and replicated/generalized here) shows 18/24 multi-event source-runs follow a clean escalation
   pattern: a run of ordinary-treated events culminating in an impact-treated cut at or near the
   run's end. The lesson generalizes beyond this one finding: **checking a subset in isolation, and
   concluding "no pattern" from that subset, is a real methodological trap** — worth remembering for
   any future structural analysis of this kind.
2. **"All 125 weapon-SFX events have normalize=false."** Generalized from one non-representative
   sampled event. 118/125 are actually normalized. Already caught and fixed earlier in the session
   (before the formal adversarial pass began), carried into the final reconciliation for
   completeness.

## What was confirmed — with narrower scope than originally stated

Several "clean" claims turned out to be real but oversold in scope:

- The "byte-identical preset" claim (both recipes) is true at the *template* level but not at the
  literal byte level across every sampled instance — full-corpus signature hashing found 7 distinct
  signatures (duration-variants of the same templates, plus rare accents), not 2.
- The "41% crossfade rate" was a real number with no explained mechanism. It decomposes cleanly:
  100% of source-change boundaries crossfade, ~30-35% of same-source ordinary-run boundaries
  crossfade, and ordinary→impact escalation points are *always* a hard cut (0/21, zero exceptions).
  The crossfade mechanism itself turned out to be VEGAS's automatic behavior from dragging clips
  into alignment (100% Smooth/Smooth curves, 98%+ exact-symmetric fade lengths) — not a hand-authored
  transition palette.
- "18 retained-audio events" is now a precisely deterministic rule, not a loosely-understood
  minority: all 18 pair 1:1 with raw-DVR-sourced "none"-family video clips; the other 6 "none"-family
  clips (curated bridge footage) get no paired audio at all.
- "The ordinary recipe is imperceptible" is correct for casual, normal-speed viewing but was
  overstated as an objective claim — a controlled on/off ablation found a real, measurable 28%
  edge-energy loss and brightness lift, visible directly in fine HUD-text legibility.

## What was newly confirmed this pass (the user's 6 explicitly-flagged priorities — all complete)

1. **Track-vs-event effect interaction**: event-level FX dominates/masks track-level ambient FX on
   strong (impact) events; track-level FX is proportionally more visible on weak (ordinary) events.
2. **Crossfade predictor analysis**: source-change is the primary predictor (100% vs 30-35% for
   same-source); mechanism is automatic, not authored (above).
3. **Independent musical alignment**: song tempo ~115-120 BPM, converging from two independent
   methods (audio-onset detection on a freshly rendered solo track vs. marker-grid ratio analysis);
   markers sit on a half-time (every-2nd-beat) grid; a real, useful falsification alongside this —
   no single global tempo+phase grid holds across the full 240s, meaning naive fixed-grid beat
   extrapolation would drift in any future automation.
4. **Retained gameplay-audio purpose**: precisely characterized as above (raw-DVR clips keep native
   audio; curated bridge clips don't).
5. **Cinematic/connective-clip pairing**: raw/connective footage sits between an impact hit and the
   next setup 68% of the time, always crossfades in/out (the opposite rule from the impact hard-cut
   rule), is preceded by a whoosh 86% of the time, and stays within-section 91% of the time.
6. **Render ablation**: Motion Blur toggle is the dominant blur driver; DistortRGB is the dominant
   chromatic-separation driver; Shake1 >> Shake2 in magnitude; extended to the ordinary recipe this
   pass (Finding above).

## What remains genuinely unresolved

- **OFX keyframe time-domain reference frame** (event-relative vs. timeline-absolute) — Part 3's
  fixture-testing engineering task was not attempted this pass; deprioritized below the six
  explicit priorities given the time already invested. See `ofx-time-domain-verification.md` for
  the honest accounting and recommended follow-up.
- **Missing-plugin exact chain location** — structurally re-verified as absent from every
  Track/Event Effects collection in the exported schema, which by elimination is consistent with
  the "used as transition" classification but not proof of it, because no inspector version ever
  exported VEGAS's `Transition` object.
- **GPU vs. CPU render differences** — not tested.

## A real incident worth carrying forward: the stuck dialog

While building the Part-7 ablation, a script with a genuine compile error (`TrackEvent` doesn't
expose `.Effects` directly — needs a cast to `VideoEvent` first, a pattern already solved once
earlier in the session in `ablation-render.cs` and silently reintroduced in a new script) produced
a real, unidentifiable modal dialog. The automation correctly did *not* blind-click it — it hung
until timeout, and the process was killed instead. A second run of the identical buggy script (after
closing a redundant concurrent VEGAS instance) exited cleanly instead of hanging, which is most
consistent with the launcher's title-only window matching (`FindWindow(null, "VEGAS Pro 20.0")`)
behaving ambiguously when more than one VEGAS window shares that exact title — a real, now-evidenced
instance of exactly the gap `docs/vegas-integration-probe.md` already designed around (PID-scoped
window matching, refuse-to-launch-if-any-instance-exists). Full writeup, comparison against the
probe doc, and concrete next steps: `automation-safety-audit.md`.

## Bottom line

The reference project's core editorial grammar holds up well under adversarial re-testing: a
deliberate two-tier effect system (subtle-but-real ordinary treatment vs. a stylized, exaggerated
motion-blur impact hit), a consistent hard-cut-on-escalation / soft-crossfade-elsewhere rule, tight
(97.6% within 30ms) musical alignment of kills to markers, and a coherent connective-footage
placement rule around kill highlights. The two real falsifications (multi-kill clustering,
weapon-SFX normalization) were both cases of drawing a corpus-wide conclusion from too narrow a
slice of the data — a specific, nameable failure mode worth remembering for future forensic passes.
Genuine gaps (OFX time-domain, exact missing-plugin location, GPU/CPU parity) are flagged rather
than guessed at. See `reconciled-final-findings.json` for the complete, itemized ledger and the
closing should-change-now / remain-experimental / requires-unavailable-plugins /
requires-more-reference-projects / should-not-be-generalized breakdown.
