# Editing and effects rulebook

This is the normative editing contract for AutoEditing. It is intentionally
readable without opening the code. Production behavior, deterministic tests,
and this document must change together.

Status terms:

- **Implemented**: produces or directly controls the generated VEGAS timeline.
- **Modeled**: persisted and available to planning, but not visually rendered.
- **Planned**: design direction only.

## Clip selection and ordering

### EDIT-ORDER-001 — Ordinary clips are not chronological

**Implemented.** Map name, filename sequence number, and source-directory order
do not define montage chronology. Ordinary clips may be reordered to improve
musical fit and reduce retiming distortion.

The planner evaluates deterministic order families based on:

- number of confirmed kills and natural multi-kill span;
- shortest first-kill lead first;
- longest first-kill lead first.

Each order is fully allocated against the music. The feasible result with the
strongest explicit/editorial anchors and least normal-speed distortion wins.

### EDIT-ORDER-002 — Only explicit opener and closer roles constrain position

**Implemented.** A filename beginning with `[OPENER]` remains before ordinary
clips. A filename beginning with `[CLOSER]` remains after ordinary clips. If no
such prefixes exist, every clip is reorderable.

Kill order inside a single source clip remains chronological because those kills
share one continuous media event.

## Music synchronization

### EDIT-SYNC-001 — One confirmed kill consumes one unique anchor

**Implemented.** Confirmed kills map to unique, strictly increasing musical
times. Allocation is global rather than greedy and verifies every rendered kill
within 2 ms of its assigned musical time.

### EDIT-SYNC-002 — Explicit anchors take precedence

**Implemented.** Explicit `GameplayAnchor` assignments are preferred whenever
velocity and region constraints permit them. Otherwise-unassigned, non-rejected
events supplement capacity as automatic suggestions.

Suggested priority, highest first:

1. drop;
2. build hit;
3. accent;
4. phrase boundary;
5. manual sync point;
6. downbeat;
7. transient;
8. ordinary beat.

Assignments to another role and `IntentionallyUnused` points are not automatic
gameplay candidates.

### EDIT-SYNC-003 — Regions are hard placement boundaries

**Implemented.** Events in an `Unused` region are excluded. A placed clip must
fit inside the reviewed region containing its selected anchors; the kill marker
alone being inside the region is insufficient. Timing offsets are applied before
allocation and cannot silently move locked decisions outside valid bounds.

### EDIT-SYNC-004 — Not every beat must receive gameplay

**Implemented in planning.** Unused musical events are normal. Effect-only
events remain in the prepared plan and do not consume kills. The generated
timeline shows assigned sync and effect markers instead of every detected beat.

## Velocity and retiming

### EDIT-VEL-001 — Normal gameplay never becomes slow motion

**Implemented.** Pre-kill and ordinary footage never run below `1.0x`. Sparse
anchors are not solved by stretching gameplay. The allocator chooses another
anchor or reports insufficient capacity.

### EDIT-VEL-002 — The kill occurs during accelerated approach

**Implemented.** Preferred cruise is at least `1.2x`, with a normal configured
maximum of `2.0x`. The kill is deliberately kept visible only briefly while the
approach remains fast.

### EDIT-VEL-003 — Slow motion is a short post-kill treatment

**Implemented.** The sub-100% speed (`0.35x` by default) is reserved for a short
dip after confirmation:

- fast delay: approximately 0.005–0.030 source seconds;
- ramp down: approximately 0.10–0.18 source seconds;
- slow hold: approximately 0.035–0.11 source seconds;
- ramp back: approximately the ramp-down duration.

Durations vary deterministically per kill and compress when too little source
footage is available. VEGAS uses a smooth downward curve and a fast recovery
curve. The final post-kill tail resolves around `1.0x`.

### EDIT-VEL-004 — Artistic long-form slow motion requires editorial intent

**Planned.** Introductions, outros, cinematics, or specifically reviewed musical
passages may eventually authorize longer slow motion. The automatic gameplay
planner does not infer this from sparse beats or low energy alone.

## Audio

### EDIT-AUDIO-001 — Generated gameplay audio is replaced

**Implemented.** Source clip audio is not placed. The montage song is placed on
its own track at 50% volume. Gun/hit SFX are aligned to confirmation using the
template's stored confirmation offset and play on 60%-volume tracks.

### EDIT-AUDIO-002 — SFX may use multiple layers

**Implemented.** Overlapping gun sounds receive additional audio tracks instead
of being truncated or forced onto one occupied track.

### EDIT-AUDIO-003 — Gun tails depend on kill position

**Implemented.** Only kill: 0.65 s tail / 0.35 s smooth fade. First multi-kill:
0.55 s / 0.28 s fast fade. Middle: 0.40 s / 0.22 s sharp fade. Final: 0.70 s /
0.38 s smooth fade.

## Visual effects and transitions

### EDIT-FX-001 — Effect roles are independent of gameplay anchors

**Modeled.** Events may independently carry `Flash`, `ScreenPump`, `Shake`, or
`SpeedChange`, plus structural roles such as `CutOrTransition`, `TitleReveal`,
and `CinematicTransition`. They carry priority, intensity, offset, notes, origin,
and review-lock metadata.

### EDIT-FX-002 — Effect markers are preserved

**Implemented.** Assigned effect-only points survive planning and appear as
`AE|EFFECT` timeline markers. Same-time sync and effect roles are merged rather
than overwriting one another.

### EDIT-FX-003 — Rendering reports actual capability

**Partially implemented.** Screen pumps create real VEGAS pan/crop keyframes.
Flash, shake, speed-change, transition, title/name-tag, cinematic-transition,
and color-correction treatments are currently unsupported and must report that
status with a reason; they must never be falsely logged as applied.

A screen pump is centered on the treatment time. Its peak zoom ranges from
2.5% to 10% according to intensity. The baseline-to-peak-to-baseline keyframes
fit inside the event, using at most 0.12 s on either side. The renderer operates
only on newly generated montage events and composes multiple planned pumps on
their shared baseline pan/crop state.

Screen-pump plans use stable semantic recipe IDs. `native.pump.subtle` maps to
roughly 2.5–4.5% zoom, `native.pump.medium` to 4.5–7.5%, and
`native.pump.impact` to 7.5–10%. Build hits default to medium and drops to
impact. A manual intensity selects the corresponding tier.

### EDIT-FX-004 — The default preset is automatic but conservative

**Implemented in planning.** Building without reviewing every song-map event
still creates a deterministic treatment plan. It does not decorate ordinary
beats or downbeats. Automatic candidates are:

- drop in BuildUp, Action, or Climax: screen pump and speed change;
- build hit in BuildUp, Action, or Climax: screen pump;
- accent in BuildUp, Action, or Climax: selectively gated flash;
- phrase boundary: title reveal in an intro, cinematic transition in cinematic
  or outro regions, and a selectively gated cut elsewhere.

Intro, Breakdown, Cinematic, and Outro do not receive automatic impact visuals;
their longer-form artistic treatment remains structural or manually assigned.

The stable default seed is 173. Accent, phrase-boundary, intensity,
and duration variation is derived from that seed and the stable event ID, never
from runtime randomness. The same reviewed analysis and preset therefore
produce the same ordered actions.

The current built-in is identified as `autoediting.sniper.conservative@1` under
preset schema 1. Plans record the exact preset ID, revision, schema, and seed.
`autoediting.none@1` disables automatic inference while retaining manual
treatments. Preset inheritance, user JSON storage, capability snapshots, and UI
selection remain planned in `effect-preset-architecture.md`.

### EDIT-FX-005 — Manual treatment wins within its category

**Implemented in planning.** A user-chosen visual, speed, or structural
assignment is preserved and suppresses automatic suggestions in that same
category at the event. Independent categories remain eligible. The event's
timing offset and explicit intensity apply to the planned action.

`IntentionallyUnused` suppresses all treatment at an event. An `Unused` region
suppresses automatic treatment. Suppression is recorded as a diagnostic.

### EDIT-FX-006 — Cooldowns, density, and repetition limit automation

**Implemented in planning.** Default minimum spacing is 1.25 s between visual
accents, 3.0 s between structural treatments, and 4.0 s between speed changes.
Base per-region maxima are four visual, two structural, and one speed treatment.
Those maxima are multiplied by region density and rounded, with a minimum of
one:

- Intro, Outro, Cinematic: 0.45;
- Breakdown: 0.55;
- BuildUp: 0.75;
- Action and unmapped: 1.0;
- Climax: 1.15.

No automatic treatment type may occur more than twice consecutively.
Spacing-, density-, and repetition-based omissions are retained as diagnostics.

### EDIT-FX-007 — Treatment strength and duration remain bounded

**Implemented in planning.** Automatic intensity combines event strength,
stable variation, and region density, then clamps to 0–1. Manual treatment uses
the explicit event intensity when present, otherwise a deterministic moderate
value. Default duration ranges are:

| Treatment | Duration |
|---|---:|
| Flash | 0.07–0.10 s |
| Screen pump | 0.16–0.30 s, depending on subtle/medium/impact recipe |
| Shake | 0.18–0.30 s |
| Speed change | 0.35–0.55 s |
| Cut / transition | 0.30–0.50 s |
| Title reveal | 1.00–1.50 s |
| Cinematic transition | 0.65–1.00 s |

### EDIT-FX-008 — Unsupported rendering degrades to no effect

**Implemented.** Treatment planning is independent from VEGAS capability.
During rendering, an unsupported or unsafe treatment is skipped with an
explicit diagnostic. Failure to render an optional visual effect does not invent
another effect, corrupt existing custom keyframes, or misreport success.

### EDIT-FX-009 — Effects are reviewed before montage construction

**Implemented.** Effects are an explicit wizard stage before the clip drawer
and **Build montage**. The stage exposes the treatment policy that will be sent
to planning instead of hiding that policy behind the build button.

The editor can:

- choose the conservative automatic treatment preset or disable automatic
  effects;
- independently allow or suppress each treatment family exposed by the stage;
- choose an overall treatment intensity;
- see how each available or planned family would be incorporated; and
- distinguish treatments that VEGAS can currently render from treatments that
  are only modeled or planned.

The initial selection uses the conservative preset at normal intensity and
density. Renderer capability is shown separately from editorial intent. At
present, `ScreenPump` is the only supported editorial visual treatment, so
families without renderers are visible but disabled. Manual song-map treatments
remain preserved and are reported honestly when the renderer cannot apply them.

Changing the family selection, intensity, or density does not mutate the VEGAS
timeline. The exact reviewed configuration is included in montage preparation
when **Build montage** is pressed. Final treatment actions and clip targeting
are resolved during preparation and reported in the log before timeline
mutation.

Native screen-pump Pan/Crop keyframes are attached to the target event before
their bounds and interpolation are configured. VEGAS validates that geometry
against the owning event. If configuration fails or the attached keyframe
remains invalid, the partial keyframe is removed and the treatment is reported
as rejected rather than rendered.

### EDIT-FX-010 — Screen-pump rhythm follows placed kills

**Implemented.** When automatic screen pumps are enabled, every reviewed kill
assignment receives an impact screen pump after clip placement. These mandatory
kill pumps are not subject to the generic visual spacing limit and replace a
duplicate automatic pump at the same event or time. An explicit manual pump at
that point remains authoritative.

Between each pair of consecutive assigned kills, eligible `Beat`, `Downbeat`,
and `Accent` events strictly inside the interval may receive subtle pumps.
Sparse density permits at most one and normal or high density at most two.
This interstitial recipe is used only when the total eligible pocket contains
one or two events; a longer musical gap receives none. Selection is stable by
event time and ID. Pumps are
only planned when their timeline time falls within a generated video placement.
A kill assignment outside all placed video intervals is omitted with a
`kill-pump-outside-placement` diagnostic rather than being presented as
renderable. Disabling screen pumps or selecting `autoediting.none` disables this
placement-aware automatic pass.

## Safety and explainability

### EDIT-SAFE-001 — Planning completes before VEGAS mutation

**Implemented.** Capacity, media, speed profiles, song identity, SFX templates,
and prepared payload shape are validated before generated timeline cleanup.
Placement failures abort rather than silently producing a partial montage.

### EDIT-SAFE-002 — The plan is explainable

**Implemented in logs and data.** Prepared plans retain kill-to-music-event
assignments and structured diagnostics. The activity log prints the selected
mode and each kill-to-anchor mapping before the VEGAS command executes.
