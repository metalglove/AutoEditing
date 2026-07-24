# Velocity Comparison

## Dominant curve shape

Editor 1: 4-point curve dominates (68%) — a single fast-in / slow-plateau / fast-out ramp.
Editor 2: 7-point curve dominates (81%) — a **double-dip** shape (fast-in, plateau, re-accelerate,
plateau again, fast-out).

**Classification: Contradicted.** Neither project's dominant shape matches the other's. This is
one of the clearest pieces of evidence in this whole comparison that velocity-curve shape is
editor-specific style, not a shared convention. **[E1-P01-VEL-001/002 vs. E2-P01-VEL-001/002]**

## Plateau value

Editor 1: median plateau 0.5x — and notably p10/p25/median/p75 are **all exactly 0.500** across the
188 committed four-point events, i.e. a hard convention rather than a distribution centred near 0.5.
Editor 2: 0.5x in the one detailed sample.

**Classification: Shared technique, different parameters (possibly a genuine match, unconfirmed
at scale).** Both editors' velocity plateaus land on the same round half-speed value — but Editor
2's finding rests on a single detailed sample (not a full-corpus statistical check, unlike Editor
1's n=275 measurement), so this should be read as a promising lead, not a confirmed match.
**[E1-P01-VEL-003 vs. E2-P01-VEL-003]**

## Entry/exit speed

Editor 1: median entry **2.763x**, exit **3.000x** (n=188 four-point events, committed corpus) —
both well above 1x. Editor 2: 3.0x entry, 3.0x exit in the one detailed sample (symmetric). (An
earlier revision of this document cited "entry ≈2.87x, exit ≈2.76x" for Editor 1; those figures
came from a non-committed artifact and are superseded — see E1-P01-VEL-004.)

**Classification: Shared technique, different parameters.** Both editors use above-1x, overshoot-
style entry/exit speeds rather than returning cleanly to normal playback — the specific
convention (never just return to 1x) appears in both, even though exact values differ.

## Velocity and effect-family relationship (kill/impact coupling)

Editor 1: velocity plateau depth is statistically indistinguishable between impact-family and
ordinary-family events (0.492 vs. 0.523 mean) — velocity and the visual-effect escalation are
**independent** editorial decisions in that project.

Editor 2: **not tested** — no equivalent statistical comparison was run between the hit-accent-
aligned events and the surrounding corpus (reduced scope, see the Editor 2 package's
`limitations.md`). **Classification: Insufficiently supported** for Editor 2's side of this
comparison; not contradicted, simply unverified.

## Multi-kill / escalation pattern

Editor 1: 18/24 multi-event same-source runs show a "long ordinary run culminating in an
impact-treated final event" pattern (found via adversarial-pass source-run analysis).

Editor 2: the 7 hit-accent events form a tight rapid-fire burst (8 seconds, 7 hits) — a genuinely
different *shape* of multi-kill pattern (a rapid burst rather than an escalating single-run
build-up). Whether Editor 2's burst *also* shows an ordinary→escalation pattern within its own
run structure was **not tested** this pass.

**Classification: Insufficiently supported for direct comparison** — both projects show *some*
multi-kill clustering behavior, but the specific shapes observed so far (gradual escalation vs.
rapid burst) are not yet confirmed as the same phenomenon or as genuinely different ones.

## Where the kill sits inside the envelope

*(Added retroactively — these Editor 1 findings existed in
`projects/editor-1/project-01/velocity-kill-timing-findings.md` from the original investigation but
carried no evidence IDs, so they were absent from the first revision of this comparison. See
E1-P01 corrections log C-006.)*

Editor 1: the kill lands on the **terminal (last) velocity point**, at ~3.0x — *not* inside the
slow plateau. `pointIndex/points` = 3/4 ×106, 4/5 ×8, 5/6 ×3, 2/3 ×4. It also coincides with a
**cut**: 122/125 kills sit exactly at their video event's end, which (coverage being gapless) is
simultaneously the next event's start. This yields the identity `marker = hit-SFX start = outgoing
event end = incoming event start`. Because the project splits picture at every kill, the 0.5x valley
sitting "before" kill N+1 is the same valley sitting "after" kill N — the perceived result is "fast
after a kill, decelerate to a short 50% valley, accelerate into the next kill."
**[E1-P01-VEL-013, VEL-014]**

Editor 2: **not tested.** Editor 2's `limitations.md` lists this as a specific named open item —
"the `SA-B 50 Hit.mp3` burst was not cross-checked against velocity-envelope shape."

**Classification: Insufficiently supported** for cross-project purposes — high-confidence
structurally in Editor 1, entirely unmeasured in Editor 2. Note also that even in Editor 1 the
*visible* kill was never frame-verified at that boundary (kill placement is labeled a
high-confidence inference), and 4 counterexamples are documented — so this is a dominant strategy,
not an invariant.

## Curve types on the dip

Editor 1: the canonical four-point curve-type sequence is **`Fast > Smooth > Slow > Fast`** (96/188),
followed by `Fast > Smooth > Slow > Smooth` (45/188). The top four sequences — 180/188 (96%) — all
carry `Slow` at point 3 (the plateau exit). **[E1-P01-VEL-015]**

Editor 2: **not tested** — curve types were not extracted for Editor 2's envelopes this pass.

**Classification: Insufficiently supported** (Editor 2 side unmeasured).

## Dip timing

Editor 1 medians (n=188): entry ramp **151ms**, plateau **209ms**, exit ramp **226ms**, tail after
the final point 0ms. The exit ramp is roughly **1.5× the entry ramp** — a deliberate asymmetry, and
the opposite of what a "fast recovery" reading would predict. First-in-run kills have a materially
longer approach from the plateau (727ms) than middle (399ms) or final (433ms) kills, while terminal
speed does not distinguish run position. **[E1-P01-VEL-016, VEL-017]**

Editor 2: **not tested** — no equivalent timing distribution was computed (n=1 detailed sample only).

**Classification: Insufficiently supported** (Editor 2 side unmeasured). These are the most directly
implementable velocity numbers in either package, but they rest on one project.
