# Velocity Findings — Editor 2 / Project 01

Distinguishes stored project data, derived timeline timing, render-confirmed behavior, and
semantic interpretation, per this package's evidence-quality requirements. A timeline boundary is
called a "kill" or "impact" only where corroborating evidence (visible kill-confirmation UI,
hit-accent-sample alignment) supports that interpretation. Evidence IDs indexed in
[evidence-register.md](evidence-register.md).

## Exact observed velocity-envelope families (stored project data)

155/158 Track-4 events carry a `Velocity` envelope. Point-count distribution:

| Points | Events |
|---|---:|
| 1 | 1 |
| 2 | 2 |
| 3 | 1 |
| 4 | 16 |
| 5 | 5 |
| 6 | 1 |
| **7** | **125 (81%)** |
| 8 | 4 |

**The 7-point shape dominates overwhelmingly** — a materially different distribution from Editor
1's project, where a 4-point shape dominated (68%). **[E2-P01-VEL-001, DIRECT_PROJECT_OBSERVATION]**

## Velocity-point values and curve types

Representative 7-point envelope (`t4_e3`):

```
t=0.000s   value=3.0x   curve=Fast
t=0.195s   value=0.5x   curve=Smooth
t=0.377s   value=0.5x   curve=Slow
t=0.673s   value=2.0x   curve=Fast
t=1.024s   value=0.5x   curve=Smooth
t=1.111s   value=0.5x   curve=Slow
t=1.306s   value=3.0x   curve=Smooth
```

**This is a double-dip shape**: speed drops to the same 0.5x plateau value **twice** within one
event, with an intermediate fast (2.0x) peak between the two dips, before a final fast (3.0x) exit.
This is structurally different from Editor 1's project, whose dominant shape was a single
fast-in/slow-plateau/fast-out ramp with no intermediate re-acceleration. **[E2-P01-VEL-002,
DIRECT_PROJECT_OBSERVATION]** Not yet established whether this double-dip shape is universal
across all 125 seven-point instances or whether the specific values/timing vary — a single
representative sample was inspected in detail, not the full 125-instance corpus (reduced scope,
see [limitations.md](limitations.md)).

**Entry/exit values in this sample**: 3.0x entry, 3.0x exit (symmetric) — both well above 1x,
consistent with Editor 1's finding that entry/exit speeds genuinely exceed normal playback rather
than merely returning to it. **Plateau value 0.5x matches Editor 1's project's plateau value
exactly** — a genuine cross-project convergence candidate. **[E2-P01-VEL-003,
DIRECT_PROJECT_OBSERVATION]**

## Recovery behavior

Not established across the full corpus in this pass — the single detailed sample recovers fully
(returns to 3.0x, well above neutral) by its final point. Whether this holds for the minority
4-6-point shapes (26/155, 17%) was not separately checked. Flagged as a gap in
[limitations.md](limitations.md).

## Event splits associated with velocity changes

Not directly cross-tabulated against event-split boundaries in this pass — same reduced-scope
status as the equivalent item in Editor 1's package.

## Kill/impact relationships

**Not established with the same rigor as Editor 1's project.** The one clean semantic anchor found
this pass is the `SA-B 50 Hit.mp3` accent sample (7 uses, 100% aligned to both a marker and a
Track-4 event start at 0ms delta — see [audio-treatment.md](audio-treatment.md)) — these 7 moments
are the strongest "kill/impact" candidates in the corpus. Whether their velocity-envelope shape
differs from the surrounding 148 non-hit-accent events was **not checked** this pass (a direct,
valuable adversarial test that remains open — see [limitations.md](limitations.md)).

## Scope-in/scope-out relationships where visually established

**Not established.** `06_multihit_burst_start.png` (captured at 125.3s, the first of the 7
hit-accent moments) shows a heavily motion-blurred sniper-scope view with a red UI banner element
visible (consistent with, but not conclusively proving, a kill-confirmation banner given blur
obscures its text) — this is suggestive visual corroboration of a kill moment at this timestamp,
not a frame-by-frame scope-in/scope-out determination. **[E2-P01-VIS-004,
DIRECT_PROJECT_OBSERVATION, semantic confidence: medium]**

## First, middle, and final-kill patterns / multi-kill escalation

The 7 `SA-B 50 Hit.mp3` accents land on 7 consecutive-ish Track-4 events (`t4_e115` through
`t4_e120`, then `t4_e122` — skipping `t4_e121`) within an 8-second span (125.3-133.3s) — a genuine,
tightly-clustered rapid multi-hit burst, directly analogous in shape to what Editor 1's project
required source-run analysis to uncover indirectly. Here it is directly observable via the
hit-accent-sample alignment alone. **[E2-P01-VEL-004, DIRECT_PROJECT_OBSERVATION]**

Whether this burst follows an escalation pattern (ordinary treatment building to a stronger
effect at the burst) analogous to Editor 1's 18/24-run finding was **not checked** in this pass —
flagged as a valuable, not-yet-executed adversarial test in [limitations.md](limitations.md).

## Negative results — patterns tested but not found

- No relationship between hit-accent timing and velocity-envelope shape was tested (see above —
  genuinely not checked, not a negative result).
- Unlike Editor 1's project (where impact-family velocity plateau was statistically
  indistinguishable from ordinary-family plateau, a real tested negative result), no equivalent
  direct statistical test was run here in the time available.

## Out-of-range or retained envelope points; stored vs. effective curves

Not audited for the Velocity envelope type in this pass, consistent with Editor 1's package. The
OFX keyframe out-of-range question (see [effects-and-presets.md](effects-and-presets.md)) was
checked for `S_Shake`/`S_FilmDamage` parameters, not for Velocity envelope points specifically.
