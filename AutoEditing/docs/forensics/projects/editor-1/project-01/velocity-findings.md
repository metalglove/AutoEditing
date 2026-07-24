# Velocity Findings — Editor 1 / Project 01

Distinguishes stored project data, derived timeline timing, render-confirmed behavior, and
semantic interpretation of gameplay action, per the evidence-quality separation required for this
package. Evidence IDs indexed in [evidence-register.md](evidence-register.md).

**A note on terminology used in this document:** a timeline boundary is called a "kill,"
"scope-out," or "impact" only where visual or audio evidence supports that interpretation
(specifically, the boundary coincides with a replacement-gun-audio event and/or a rendered frame
showing a game-native kill-confirmation banner). Elsewhere this document says "event boundary,"
"velocity point," or "effect start," which is the language the underlying data actually supports.

## Exact observed velocity-envelope families (stored project data)

275/276 Track-1 events carry a `Velocity` envelope (range -1..10, neutral 1). Curve-shape counts
(Editor 1's classification, values below 0.8 = slow, 0.8–1.2 = normal, above 1.2 = fast):

| Shape | Events | Interpretation |
|---|---:|---|
| `fast → slow → slow → fast` | 147 | dominant closed impact ramp |
| `fast → slow → slow` | 36 | no fast recovery inside the event |
| `fast → slow → slow → slow` | 18 | sustained slow exit |
| `fast → normal → slow → fast` | 14 | connective/native-speed variant |
| `fast → slow` | 8 | short terminal treatment |
| 5-point `fast → slow → slow → fast → fast` | 7 | closed ramp, explicit fast tail |
| 5-point `fast → fast → slow → slow → fast` | 7 | explicit fast approach and recovery |

**[E1-P01-VEL-001, DIRECT_PROJECT_OBSERVATION]**

This investigation's independent full-corpus pass (`inferred-editing-strategies.md` §5) reports a
consistent picture using a slightly different point-count grouping: 4-point curves dominate
(188/275 = 68%), 3-point (44) and 2-point (14) likely correspond to shorter events without room for
a full plateau, and 5–6 point curves (29) likely correspond to multi-stage events.
**[E1-P01-VEL-002, DIRECT_PROJECT_OBSERVATION]**

## Velocity-point values and curve types

- **Plateau (minimum) value:** median 0.5×, mean 0.52×, range 0.4×–1.0×, n=275. This confirms a
  ~50% slow-motion plateau as the corpus-wide norm (a single early-sampled event at 0.7× was not
  representative). **[E1-P01-VEL-003, DIRECT_PROJECT_OBSERVATION]**
- **Entry/exit speed:** wide range (0.4×–6.2×), median ~2.87× entry / ~2.76× exit — both
  comfortably above 1×, meaning the "fast" ends of the ramp genuinely exceed normal speed rather
  than merely returning to 1.0×. **[E1-P01-VEL-004, DIRECT_PROJECT_OBSERVATION]**
- The largest single rounded-value cluster in Editor 1's independent count is only 27 events:
  `2.8 → 0.5 → 0.5 → 3.0`. Common entry values cluster around ~2.2×, ~3.0×, and ~3.1×, with a
  recurring 0.5× plateau. This supports a **parameterized velocity family** (fast approach, short
  slow plateau, optional fast recovery), not one single hard-coded envelope.
  **[E1-P01-VEL-005, DIRECT_PROJECT_OBSERVATION]**

## Entry ramps, fast-approach segments, and slow-motion plateaus

Sample envelope (first opener event, `t1_e0`, start 26.643s):

```
t=0.000s   value=3.05x   curve=Fast    (fast entry)
t=0.833s   value=0.70x   curve=Smooth  (drop into slow plateau)
t=2.955s   value=0.70x   curve=Slow    (plateau holds ~2.1s)
t=4.137s   value=2.47x   curve=Smooth  (ramp back up, overshooting 1x)
```

**[E1-P01-VEL-006, DIRECT_PROJECT_OBSERVATION]**. This single-event sample is not representative
of the corpus-wide plateau value (see VEL-003 above, which corrects the plateau figure this sample
implied).

## Recovery behavior

Terminal-family velocity shapes (the events sharing the effect chain documented in
[effects-and-presets.md](effects-and-presets.md) as the "distortion/impact family") skew toward
non-recovering shapes: Editor 1's clustering found 11 four-point `fast/slow/slow/slow` and 6
three-point `fast/slow/slow` events as the leading shapes for this family, versus the montage-wide
closed-ramp default. **[E1-P01-VEL-007, DIRECT_PROJECT_OBSERVATION]**

## Event splits associated with velocity changes

Not directly established — velocity-envelope shape was not cross-tabulated against event-split
boundaries beyond the family-level clustering above. Flagged as a genuine gap in
[limitations.md](limitations.md).

## Kill/impact relationships

**Plateau value does not distinguish an "impact" cut from an "ordinary" cut.** The impact-family
events (n=25/26, using replacement-gun-audio + marker + effect-chain identity as the "impact"
classifier — see [effects-and-presets.md](effects-and-presets.md)) have plateau median 0.5×, mean
0.492×; the non-impact population (n=250) has plateau median 0.5×, mean 0.523× — statistically
indistinguishable. **This means velocity depth and the impact effect treatment are independent
editorial decisions in this project, not a single coupled "kill recipe."**
**[E1-P01-VEL-008, DIRECT_PROJECT_OBSERVATION]**

## Scope-in and scope-out relationships where visually established

**Not established.** No representative frame or render in this investigation specifically confirms
a scope-in/scope-out moment aligned to a velocity-envelope point. This is a genuine gap, not a
negative finding — see [limitations.md](limitations.md).

## First, middle, and final-kill patterns

Editor 1's structural clustering explicitly found the exported standard-chain velocity parameters
for a sampled first event (`t1_e51`) and mid-sequence event (`t1_e55`) in the same same-source run
were **identical** despite their different editorial position — i.e., stored velocity data alone
does not distinguish first/middle position within a run. **[E1-P01-VEL-009,
DIRECT_PROJECT_OBSERVATION]**

This investigation's adversarial verification pass found a related but distinct pattern at the
*effect-family* (not velocity) level: grouping all 276 Track-1 events into 46 contiguous
same-source runs, 24 runs have more than one event, and **18/24 (75%) follow a "long run of
ordinary-family-treated events, escalating to impact-family treatment at or immediately before the
run's last event" pattern.** This is a real, repeated pattern **within this one project**, found
via source-run grouping (not velocity-envelope analysis) — it should not be read as a
velocity-curve finding, and it is not yet established whether "first/middle/final" position
independently modulates velocity shape. **[E1-P01-VEL-010, EDITOR_1_PROJECT_PATTERN]**

An earlier pass of this same investigation concluded "there is no multi-kill clustering pattern in
this project" — that conclusion is **retracted** (see [evidence-register.md](evidence-register.md)
for the falsification record) because it checked spacing only within the narrow impact-family
population, not the full same-source-run context that VEL-010 above uses.

## Negative results — patterns tested but not found

- **No relationship was found between impact/ordinary effect-family membership and velocity
  plateau depth** (VEL-008 above) — tested directly across the full population, not inferred from
  a small sample.
- Editor 1's clustering explicitly notes: "24 main-track events without effects still have
  velocity envelopes" — i.e., "visually clean" (no event-level effect chain) does not imply
  "unretimed." **[E1-P01-VEL-011, DIRECT_PROJECT_OBSERVATION]**
- One standard-chain event has no velocity envelope at all (1/276) — a genuine, unexplained
  exception, not folded into the "275/276 have velocity" statistic silently.
  **[E1-P01-VEL-012, DIRECT_PROJECT_OBSERVATION]**

## Out-of-range or retained envelope points; differences between stored and effective curves

Not specifically audited for the Velocity envelope type in this investigation (the corresponding
audit was performed for **OFX effect keyframes**, not Velocity envelope points — see
[effects-and-presets.md](effects-and-presets.md) and [limitations.md](limitations.md) for the OFX
time-domain gap). Whether any Velocity envelope point lies outside its owning event's visible
duration was not checked. Flagged as an open item.
