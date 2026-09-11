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

**MUSIC-REVIEW-004 — Commit regions before montage planning.** A detected
song analysis whose regions are still proposed is not a reviewed song map and
must not be used to allocate montage sync points. The editor must review the
`AE|MUSIC_REGION` regions and commit the song review first. Advancing from the
song-map wizard step with **Next** performs that commit atomically before the
wizard changes step; the explicit commit button performs the same operation. This state is
reported once as an actionable validation error; it must not produce one
missing-region error for every detected musical event. Proposed musical events
may supplement sync capacity only after at least one reviewed region exists.

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

Because the montage playhead advances clip by clip, a clip boundary — and only a
clip boundary — may move the playhead forward to the start of the next reviewed
region it uses. Kills inside one clip share one continuous media event and
therefore one region. Without that forward move no clip could ever enter a later
region: the preceding clip would have to end within 2 ms of the boundary.

### EDIT-SYNC-004 — Not every beat must receive gameplay

**Implemented in planning.** Unused musical events are normal. Effect-only
events remain in the prepared plan and do not consume kills. The generated
timeline shows assigned sync and effect markers instead of every detected beat.

### EDIT-SYNC-005 — Region crossings extend the previous clip instead of cutting to black

**Implemented.** When the playhead moves forward into the next region, the
preceding clip covers the skipped span by playing its unused source footage after
the post-roll at cruise speed. Only that trailing segment grows, so no reviewed
kill moves and the montage stays gapless across contiguous regions. The extension
stops at the preceding clip's own region end and at the end of its footage.

Whatever the extension cannot cover stays uncovered — typically the width of an
`Unused` region the montage skips. That span is reported as a planning diagnostic
and carried on the plan as an editorial slot with its start, end, and the regions
on either side. It is an opportunity, not a defect: a cinematic, title, or b-roll
clip belongs there (see EDIT-VEL-004). Automatic gameplay planning never fills
it.

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

### EDIT-VEL-005 — Synchronization velocity is timeline structure

**Implemented for production and candidate montage materialization.** Every
placement's authoritative `SpeedProfile` is rendered to a VEGAS velocity
envelope as part of timeline synchronization, before audio or creative effect
treatments run. Disabling creative effects therefore disables only the
capability-bound visual treatment pass; it never disables playback-rate
decisions used to align source action with the song. Normal `1.0x` profiles are
also written explicitly so a freshly materialized candidate has observable
velocity evidence.

Every speed-profile point must map to a finite, positive, monotonically ordered
timeline-relative envelope point. VEGAS must accept every point. An unmappable
profile, rejected point, or envelope-write exception fails materialization
instead of leaving a candidate whose visible timing disagrees with the
canonical plan.

During live reconciliation, a constant envelope is authoritative. A video event
with no velocity envelope is actual VEGAS `1.0x` playback; it is recorded as a
change when the proposal expected another rate. Reconciliation never fills
missing live evidence with the proposed speed. Variable envelopes remain
unsupported during the synchronization pass and fail closed.

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

### EDIT-LLM-001 — Forensic style evidence is advisory and versioned

**Implemented for LLM planning.** Initial and revision prompts include the
versioned `forensic-cross-editor-v1` findings profile from
`LlmEditor/Prompts/editor-style-findings.v1.md`. The profile separates
replicated findings, limited evidence, editor-specific patterns, and
contradicted behaviors.

Deterministic editing constraints and the validated planning request remain
authoritative. The model must not turn forensic tendencies into invariants,
invent unavailable media or effects, or claim an unsupported renderer
capability. A baseline response remains one complete edit plan so global
ordering and musical structure can be evaluated before section-level planning
or targeted patch contracts are introduced.

The structural output example is presented before the actual request and is
explicitly non-authoritative. An LLM response retaining the example's
`SKELETON_FAKE_PLAN` diagnostic is rejected before VEGAS automation because it
is evidence that the model copied the contract fixture instead of making
editorial decisions.

### EDIT-LLM-002 — LLM planning requires the committed reviewed song map

**Implemented for LLM planning.** An LLM planning request includes the compact
`MontageSongPlanningInput` created by the same provider used by deterministic
montage planning. It supplies the authoritative song identity and duration,
reviewed regions, effective event times, classifications, editorial uses,
priorities, intensity, and locks.

The request also contains every detected musical event in the compact
`eventTimeline` positional table. Its declared columns are time, type, strength,
confidence, and review state. This complete lattice provides pulse, density,
accent, and structural context without repeating full editorial objects for
hundreds of detections. Numeric values are rounded to four decimal places for a
stable, token-efficient representation.

LLM planning fails before inference when this context is absent, legacy-only,
uncommitted, stale, or contains planning errors. The model must shape the
montage across the reviewed regions and use only supplied musical events for
synchronization; it must not invent replacement beat times or musical event
IDs. Rejected timeline rows are context only and cannot become synchronization
targets. Only detailed entries in `events` are eligible anchors. Initial and
revision prompts retain both layers of the compact song map.

### EDIT-LLM-003 — Models author compact decisions, not renderer DTOs

**Implemented for LLM planning.** The model response is constrained by a strict
JSON schema to clip path, source window, constant speed, primary kill-to-music
sync, optional additional syncs, and short diagnostics. The model does not
author canonical clip metadata, timeline starts or ends, transformed shot
events, speed-profile totals, `songPlan`, effect renderer data, or the final
`PreparedMontage`.

The deterministic decision compiler resolves zero-based confirmed-kill indices,
derives timeline starts from reviewed effective music-event times, calculates
all placement fields, copies the authoritative reviewed song plan, and creates
canonical `MontageSyncAssignment` records. It rejects unknown response
properties, unavailable clips, duplicate clips or music events, invalid source
windows, disabled speed changes, non-gameplay or intentionally-unused musical
events, misaligned additional syncs, and overlapping placements.

A complete baseline places every selected request clip exactly once and covers
every usable reviewed song region that contains an eligible gameplay anchor.
This prevents a model from returning a short intro fragment while describing it
as the complete montage.

### EDIT-LLM-004 — Assembly is checkpointed and human-authoritative

**Implemented for the synchronization pass.** The sync pass is modeled as a
durable clip-by-clip assembly. After a clip is materialized, the candidate
workspace remains present in VEGAS and enters `AwaitingHumanReview`. The user
may revise the current clip, accept the current timeline and continue, reset the
current clip, or finish the sync pass. Creative effects remain outside this
assembly phase. Deterministic confirmation-aligned gunshot SFX are materialized
with each checkpoint so the editor can judge the actual impact relationship;
they are synchronization evidence, not an AI-authored audio-treatment pass.

Finishing before the final selected clip is an explicit destructive-boundary
choice, never an implicit planner decision. The workbench states the number of
selected clips that will remain unused and requires warning confirmation. The
current live VEGAS timing is reconciled and accepted first; no later clip is
planned or materialized. The `SyncPassComplete` state retains the unused clip
paths, and the mandatory rough-cut audit reports unused media and uncovered
reviewed musical structure before effects or audio may begin.

The live candidate timeline is authoritative for supported human adjustments.
Before acceptance or revision, the reconciler reads the current candidate video
events and adopts changes to timeline start, source trim, duration, and constant
playback rate. It produces a structured `TimelineAdjustmentDelta` containing
the before and after values. Invalidated sync assignments are explicitly
recorded in that delta rather than silently retained. Revision inference
receives both the reconciled candidate evidence and the structured delta, so the
model can respond to the editor's actual timing changes rather than a generic
statement that the timeline changed.

Playback rate is read from the live VEGAS velocity envelope. An absent envelope
means the event is actually playing at `1.0x`; it never inherits the proposal's
rate during reconciliation. This allows deleting an envelope to be a deliberate,
observable human correction while preventing a missing render from being
misreported as the planned non-1x speed. See EDIT-VEL-005 for the independent,
fail-closed materialization rule.

The editor may also request a non-destructive comparison. Comparison reads the
current VEGAS candidate and persists the structured adjustment delta for
inspection without accepting, resetting, revising, or otherwise mutating the
candidate plan. Completing a comparison publishes a new
`AwaitingHumanReview` state revision. The consumed read-only action therefore
cannot occupy the reviewed revision or block a subsequent comparison, preview,
revision, reset, or acceptance action.

Each newly materialized candidate video event receives a deterministic
workspace-owned placement ID in its VEGAS event name. The ID binds the
workspace, original placement ordinal, and a digest of the normalized source
path, and survives direct movement, trimming, duration, and speed changes.
VEGAS event metadata is assigned only after the event has been attached to its
candidate track and its media take has been initialized; VEGAS 20 rejects
metadata writes on detached `TrackEvent` objects. Any failure during take or
identity initialization removes the partially attached event and fails
materialization.
Reconciliation resolves the exact ID before comparing media and timing.
Path-only matching remains a compatibility path for persisted candidates made
before placement IDs existed.

Reconciliation requires exactly one live video event for each expected
placement. A deleted expected event, an unexpected added event, an ID now
pointing at different media, or multiple events sharing an expected identity
creates an immutable conflict artifact and enters `ReconciliationConflict`.
The artifact records the exact issue, live event candidates, and only the
resolution choices that are safe for that snapshot. Nothing is silently
ignored, restored, excluded, or adopted.

Every conflict permits exact proposal restoration and defer-and-pause. A
deleted current event may be excluded only when the remaining expected prefix
reconciles exactly and at least one accepted placement remains. A live event
may be adopted only by explicit candidate ID when its media is one of the
reviewed remaining clips, its source bounds and constant velocity are
representable, and it deterministically replaces an unavailable current clip
or becomes an additional clip. Adoption/exclusion updates the semantic sketch,
persists the choice, and rematerializes canonical workspace-owned events before
continuing. Foreign media, already-used media, indistinguishable duplicates,
unsafe overlap, out-of-bounds timing, and variable velocity are not offered for
adoption; those cases retain restore/defer and any independently safe choice.
Variable velocity remains unsupported during this pass and is rejected rather
than reduced to an invented constant rate.

Restore, exclusion, and adoption first write one deterministic conflict-bound
resolution intent. Candidate identity binds placement identity, media,
timeline and source ranges, and velocity evidence. Immediately before an
initial exclusion or adoption, the coordinator re-reads VEGAS; a changed
conflict invalidates the consumed choice and requires a new selection.
Interruption after intent persistence replays that exact intent, with
idempotent sketch mutation, until cleanup, canonical rematerialization,
snapshot reconciliation, and accepted-plan persistence verify the outcome.
`ReconciliationConflict` remains the durable assembly phase until that
verification completes. Restore completes after its rebuilt proposal
reconciles; exclusion/adoption complete only after their resolved plan is
durably accepted. The workbench asks for consequence-specific confirmation
before any of these three rebuilding choices.

### EDIT-LLM-005 — Reconciled and merged timelines are revalidated

**Implemented for the synchronization pass.** The deterministic plan validator
runs after live timeline reconciliation and again after a reconciled prefix is
merged into the candidate. It also runs after a newly inferred revision has had
the previously accepted prefix restored. Media ranges, placement overlap,
ordering, synchronization, and plan invariants therefore apply to the actual
combined result, not only to the model's unmerged response.

When revising the current clip, the existing candidate workspace is cleaned up
only after the replacement plan and its preserved accepted prefix pass
validation. A failed reconciliation or invalid merged plan leaves the prior
workspace available for inspection and correction.

### EDIT-LLM-013 — Synchronization reconciliation is baseline-bound

**Implemented for the synchronization pass.** Immediately after each
deterministic candidate materialization, the companion reads the candidate back
from VEGAS and persists an exact, hashed materialization baseline for that
checkpoint. The baseline's checkpoint, plan SHA-256, workspace identity, and
snapshot SHA-256 must all match the comparison; a baseline for another plan is
rejected before any live change can be adopted. A later comparison, revision,
or acceptance may adopt only video
timeline start, source trim, event duration, and a constant velocity envelope.

Track additions/removals or identity changes, mute/solo changes, song or other
audio-event edits, track gain, event gain, or track volume-automation changes, fades,
transitions, grouping/linkage,
event effects, and ambiguous event identities are unsupported synchronization
changes. They produce an explicit reconciliation conflict with the observed
difference. They are never ignored, inferred from the proposed plan, or erased
by rematerialization. Recovery requires the same baseline; if it is missing,
the editor must reset the proposal to establish a new baseline before live
changes can be adopted.

The exact timeline that produced the reviewed complete rough-cut render is
captured again at explicit rough-cut acceptance and persisted as the accepted
rough-cut baseline. Polish entry compares against it. Each explicitly accepted
effects and audio pass then records the next complete candidate baseline;
rejected-pass restoration compares against the appropriate prior stage, and
finalization compares against the accepted audio-stage baseline. Every baseline
remains bound to the unchanged rough-cut placement plan, workspace, and exact
snapshot evidence. Unsupported track or event changes between stages therefore
fail closed instead of riding into promotion. Post-acceptance divergence enters
`NeedsRecovery`; the editor must restore the relevant accepted candidate or
reopen and re-audit the rough cut.

### EDIT-LLM-014 — Steering is explicit, suffix-only, and durable

**Implemented for progressive synchronization.** Editorial direction selects
one explicit scope: `CurrentClip`, `NextClip`, `RemainingSection`, or
`GlobalRemainder`. Current-clip direction is valid only with a revision and is
not persisted beyond that proposal. Future direction is valid only when the
current checkpoint is accepted.

Future direction is persisted immutably before the accepted plan is written.
`NextClip` is included in exactly one subsequent planning context.
`RemainingSection` is included only for later clip intents with the same
semantic section ID. `GlobalRemainder` is included for every later planning
context. All scopes affect only the unaccepted suffix; accepted placements are
never sent back for rewriting. A contradictory replay, a current-clip
instruction attached to acceptance, or direction submitted after the final
clip fails closed.

### EDIT-LLM-006 — Assembly actions target one persisted state revision

**Implemented for the synchronization pass.** Each published assembly state has
a monotonically increasing state revision. Accept, revise, reset, and finish
actions carry the session ID, checkpoint, expected state revision, creation
time, and unique action ID. An action is eligible only for the exact session,
checkpoint, and state revision that the editor reviewed.

Pending actions are append-only and ordered deterministically by creation time
and action ID. An atomic checkpoint-and-revision claim permits exactly one
winning action. Durable disposition records mark commands as consumed or
quarantined while retaining inspectable copies. Malformed, wrong-session,
stale-checkpoint, stale-state, and conflicting actions are quarantined instead
of being applied or repeatedly blocking a newer valid action.

### EDIT-LLM-007 — Progressive assembly planning is semantic and prefix-aware

**Implemented for LLM planning contracts and inference.** Progressive planning
starts with one loose global `AssemblySketch`. The sketch records the editorial
thesis, song sections, tentative clip order, sync strategy, structural
reservations, uncertainties, alternative clips, rationales, and confidence. It
receives the complete reviewed compact song map, including both detailed legal
sync events and the full positional `eventTimeline` lattice. The tentative order
is guidance rather than a lock: a clip step may choose any remaining clip when
its rationale explains the choice. Every clip intent must reference a section ID
declared in the same sketch (never a song-region ID), and every clip
reference-ID/media-path pair must exactly match the authoritative input. An
invalid sketch response is never persisted as the active sketch and never
changes VEGAS. The planner supplies the exact deterministic diagnostic and asks
for a complete corrected sketch, with a maximum of three total attempts; all raw
inference exchanges remain inspectable. The rejected sketch is placed before
the final repair diagnostic in the next prompt, so a repeated bad value cannot
become the most recent instruction through prompt recency. Before internal
sketch consistency is checked, the planner performs an aggregate comparison
against the authoritative request. An unknown returned reference ID paired with
exactly one supplied media path is treated as a transcription alias: the path's
canonical reference ID replaces it in the clip intent and in matching
alternative and reservation references. This deterministic correction does not
fuzzy-match IDs or paths. A known ID paired with another clip's path, an unknown
path, or an alias that identifies multiple supplied paths still fails closed.
The raw model response remains unchanged in inference records. A single repair
response lists every missing, duplicated, invented, or path-mismatched clip,
every invalid alternative, and every duplicate or undeclared section reference.
Missing clips are identified by stable reference ID and filename so a secondary
symptom cannot hide the actionable root cause.

Each subsequent inference authors exactly one `ClipStepDecision`: one stable
clip reference, one source window, constant speed, primary and optional
additional kill-to-music syncs, alternatives, rationale, and confidence. Strict
JSON schemas reject unknown output fields. Stable clip references are derived
from normalized media paths; sync references must name supplied reviewed music
events. Every clip-step prompt supplies authoritative, compact source-media
timing for every remaining clip: duration plus ordered confirmed-kill muzzle and
confirmation times. Source windows are expressed only in source-media seconds;
every selected kill confirmation must lie inside the chosen source window.
Source ranges, kill indices, request identity, remaining-clip membership,
nearby-event scope, selected-kill containment, and the speed-change setting are
revalidated after inference. A decision that excludes one of its selected kills
is rejected before any VEGAS timeline mutation. Every rejected proposal revision
and its exact deterministic diagnostic remain durable and inspectable. The
coordinator automatically asks the configured model to repair a rejected
proposal, including the diagnostic and the constant-speed synchronization
equation, for at most three total proposal attempts. Only a proposal that
compiles into a valid complete prefix may be materialized. Exhausting the bounded
repair budget fails the checkpoint without changing VEGAS. When speed changes
are disabled, the decision must remain at `1.0x`.

The AI synchronization workflow enables the supported constant-speed decision
even when the automatic-effects UI preset is otherwise conservative. This does
not enable variable velocity or creative speed effects. A `1.0x` decision
remains correct when retiming is not editorially justified; a non-`1.0x`
constant is used only to fit supplied source-action spacing to supplied reviewed
music-event spacing.

Clip-step prompts discourage whole-source default windows. A single synchronized
kill normally uses a compact two-to-four-second action window with readable
setup and recovery. Windows longer than six seconds require an explicit
multi-kill, opener/closer, or song-structure rationale. These are editorial
guidelines rather than invented timing: deterministic validation still requires
all selected confirmed kills to be contained by the exact supplied source
times.

The next-clip prompt receives the sketch, remaining clips, nearby reviewed song
context, style findings, and an immutable summary of the accepted prefix.
Accepted-prefix summaries describe the canonical materialized placement rather
than the model proposal: timeline and source bounds, actual constant speed,
surviving syncs, and whether a human adjusted it. Surviving syncs may be empty
when reconciliation invalidated the proposed primary sync. A current-clip
revision additionally receives the structured `TimelineAdjustmentDelta` and
one scoped instruction. This rule covers the contract and LLM planner; durable
coordination and timeline materialization are separate workflow responsibilities.
When accepting a clip completes a sketched song section, that accepted section
is rendered as a durable review milestone before the temporary candidate is
cleaned up. If clips remain, a successful milestone render returns through the
reviewing state before next-clip revision begins; rendering success must never
turn into a terminal session failure merely because another synchronization
checkpoint follows. If synchronization is complete, the workflow remains in
rendering while it hands off to the mandatory whole-rough-cut review.

### EDIT-LLM-010 — Rough-cut audit is evidence-backed and correction-gated

**Implemented in the post-synchronization workflow.** A completed
synchronization plan is reviewed as one rough-cut milestone before effects or
audio are considered. Complete render
evidence is represented by an immutable manifest of contiguous chunks no longer
than twenty seconds each. The chunks must cover the accepted synchronization
timeline exactly, without gaps or overlaps. This keeps the existing bounded
VEGAS preview operation safe while retaining evidence for the whole assembly.
Interrupted renders reuse already persisted, matching chunk results.
Before any chunk is rendered, the live candidate video events are reconciled
against a clone of the exact accepted plan. Any timing, trim, duration, speed,
sync, missing-event, duplicate-event, or unexpected-event delta blocks the
render; evidence may never be labeled with a plan hash that no longer describes
the VEGAS timeline. Cached chunks and completed manifests are reusable only
while every referenced media file still exists and its bytes match the recorded
SHA-256. Missing, stale, or corrupt cache artifacts are moved to the session
quarantine and the affected chunks are rendered again. Acceptance revalidates
the report's complete-render manifest and every chunk; if that evidence was
lost after audit, acceptance is withheld while a fresh render and audit are
created.

The deterministic audit records placement-duration statistics, timeline gaps,
join-duration ratios, action proximity at joins, repeated adjacent maps and
visual situations, long repeated-weapon runs, per-region placement and sync
density, unused reviewed major musical events, and unfulfilled sketch
reservations. These measurements are signals for review, not permission to
silently reorder or trim accepted clips. Deterministic findings remain available
when multimodal inference is unavailable or invalid.

Multimodal audit uses a strict JSON schema. Its PNG evidence consists of
hash-verified VEGAS snapshots captured from the same isolated candidate
workspace as the full-render chunks; snapshots are authoritative only for their
listed timeline instants and must not be described as decoded render frames.
Sampling targets the opening/midpoint/closing overview, both sides of joins,
confirmed-action
or accepted-sync moments, reviewed major music events, and reviewed section
boundaries, with at most nine ordered interior samples. Every model finding must
cite supplied evidence IDs. Every correction must reference known findings and
explicit existing checkpoint numbers, and must declare exactly one supported
operation: move, trim, duration, or constant speed. Substitution, reordering,
deletion, duplication, variable velocity, effect, and audio ideas remain
advisory findings without correction proposals. Model output may propose a
supported correction but cannot mutate the timeline or reopen an accepted
checkpoint.

Each correction receives an explicit editor disposition. A targeted checkpoint
reopen request can be created only after that correction is approved, and must
preserve its exact approved checkpoint scope and instruction. The rough-cut
milestone can be accepted only when every proposed correction is terminally
marked either applied or rejected. The milestone binds the accepted plan,
audit report, and complete-render manifest by SHA-256.

The live coordinator enters full-render review immediately after the final
synchronization checkpoint. It captures up to nine targeted interior PNG
snapshots from the isolated candidate workspace plus the authoritative timeline
snapshot. These visual and timing artifacts are hash-verified and supplied to
the multimodal audit. The workbench exposes ordered full-render chunks for
playback, timestamped snapshots with VEGAS navigation, bounded findings with
their cited evidence, and proposal-to-finding traceability alongside operation,
scope, expected outcome, risk, and disposition.

Approving a proposal authorizes only its listed checkpoints; it does not mutate
the timeline. After the editor changes VEGAS and chooses Apply, the full live
timeline is reconciled and the structured delta must contain at least one
change and no changed clip outside that authorized scope. Invalid, empty, or
out-of-scope changes remain in the correction review state with an actionable
message. A valid change becomes the new accepted synchronization plan, triggers
a fresh full render and audit, and cannot bypass a subsequent human rough-cut
acceptance.

Rough-cut rendering, auditing, review, targeted correction, pause, and
acceptance are durable subphases. A rough-cut coordinator owns the exclusive
session runtime lease when its caller does not supply one. Consumed review
actions are written to an idempotent operation journal before their consumed
disposition is committed; an interrupted claimed or started operation is
replayed until a terminal result record exists. The active correction ID and
the phase preceding a pause are persisted separately from UI projection state.
On restart, a matching saved report resumes review without repeating inference,
a saved accepted milestone completes the workflow without asking for a second
acceptance, and a paused review resumes its exact review or correction subphase.

### EDIT-LLM-008 — Assembly lifecycle and recovery are explicit and identity-safe

**Implemented for the progressive synchronization pass.** Lifecycle commands
are selected from durable session state. Commands consumed by a live companion
are versioned assembly actions targeting the exact session, checkpoint, and
state revision shown under EDIT-LLM-006. Pausing is enabled only while a live
companion is waiting at a durable human-review boundary; it leaves the candidate
workspace intact and publishes a new `Paused` revision. An in-process resume
returns that same checkpoint to human review. Abandon removes only
candidate-owned workspace tracks, publishes `Abandoned`, and transitions the
workbench session to `Cancelled`.

The companion owns `assembly/runtime.lock` with an exclusive cross-process
lease for the complete start or resume run, including inference. A live paused
session consumes a versioned `ResumeSession` action. If that lease is absent,
the UI starts the `workbench-resume` command instead of writing an orphan
action. Pause is unavailable without a live lease. Abandoning a stopped session
uses `workbench-abandon`, which first reacquires the lease and validates project
ownership before removing candidate tracks and cancelling the session.
Post-accept and stopped-abandon cleanup operations use stable state/action-bound
idempotency keys. If acceptance completed before its prior checkpoint workspace
was cleaned up, resume removes that superseded workspace before materializing
the next checkpoint. A recovered execution also repairs the source action's
consumed disposition so the audit trail cannot later mislabel a successful
action as stale.
The same ownership rule applies after synchronization: abandoning rough-cut
review, either polish review, or final review removes only the recorded
candidate workspace before publishing the terminal state. Its cleanup key is
derived from the durable abandon action ID. If the companion stops after
cleanup or after publishing `Abandoned`/`Cancelled` but before completing the
action journal, restart verifies the reflected abandon, safely replays that
same cleanup key, repairs the terminal manifest, and completes the journal
without re-entering review or promotion.
Failed manifests remain recoverable; they are not hidden as terminal sessions.
A crash during initial sketch generation resumes at checkpoint one and reuses
the persisted request rather than requiring a new session.
Session IDs are limited to the same path-safe ASCII identifier grammar used by
candidate workspaces, so recovery commands cannot address a directory outside
the configured session root.

Every progressive session persists its normalized planning request and an
`AssemblySessionDescriptor` containing the session ID, request ID, request
SHA-256, song path, clip count, and captured VEGAS project identity. A restart
discovers only nonterminal manifests and reconstructs the current sketch,
current clip proposal, latest accepted plan, checkpoint, workspace, and state
revision from durable artifacts. A supplied request must match the persisted
request byte-for-byte after canonical serialization.

After the first successful VEGAS automation response, the saved project path
and fingerprint are captured. The automation client also pins that fingerprint
onto every subsequent request envelope in the same run and rejects a response
from another project. Resume and stopped-session abandon automation supply the
persisted fingerprint as a host precondition before reading or changing the
timeline. Saved projects keep a stable path-based fingerprint across VEGAS
processes. Unsaved projects use a process-bound fingerprint and intentionally
cannot be resumed after VEGAS restarts; the editor must save the project before
relying on cross-process recovery.

Before an in-place review is resumed, the live candidate snapshot must identify
the recorded workspace and pass the same reconciliation checks used for normal
acceptance. Request, project, workspace, deleted-event, extra-event, ambiguous
event, and unsupported-velocity differences produce explicit conflict or
divergence results. They do not silently rebuild, overwrite the live timeline,
or mark a recoverable session terminal. If an in-place workspace cannot be
proven safe, recovery stops for explicit editor resolution. Checkpoints
interrupted before reaching human review may clean up their candidate-owned
workspace and deterministically rematerialize the persisted proposal, but only
after recovery has validated the artifacts and project identity.

### EDIT-LLM-009 — Checkpoint previews are bounded, durable observations

**Implemented for progressive synchronization checkpoints.** `Render checkpoint
preview` is an explicit, versioned assembly action and does not accept, revise,
reset, or otherwise mutate the candidate plan. The renderer includes the whole
active placement with up to two seconds of available candidate-workspace context
on either side. Only surrounding context may be reduced to honor the existing
twenty-second preview safety limit. If the active placement itself exceeds that
limit, preview creation fails explicitly instead of truncating the placement.

Every preview request creates an immutable numbered attempt. Before rendering,
the system persists the exact timeline snapshot, render window, timing sidecar,
timing visualization, and contact-sheet sampling manifest. A completed attempt
also records the output-relative video path, render profile, duration, SHA-256,
and multimodal review report. Render failure, review failure, timeout, and
cancellation are distinct persisted outcomes; a valid rendered video is retained
when only the reviewer fails.

The current local inference transport accepts images rather than video. VEGAS
therefore captures five isolated-candidate PNG snapshots at deterministic
sample times using its native snapshot API. The checkpoint reviewer receives
those real frames, the timing visualization, editorial thesis, current clip
rationale, proposed synchronization points, timeline snapshot, preview
metadata, and latest human adjustment. It returns observations, confidence,
and suggested changes only. It cannot mutate the timeline or decide acceptance.

All valid numbered attempts remain projected in the workbench. The editor may
select any two persisted attempts as revision A and revision B and play them
side by side with each attempt's reviewer summary; the newest attempt does not
hide or overwrite older evidence.

When accepting the last clip in a semantic song section, the production
coordinator automatically renders the complete accepted range of that section.
Ranges longer than twenty seconds are divided into contiguous chunks no longer
than twenty seconds. Every chunk has a stable section/checkpoint/plan-bound
operation key and a verified output SHA-256. A complete manifest is written
only after all chunks validate, is reused idempotently after a crash, and is
projected alongside checkpoint preview revisions for playback and comparison.

### EDIT-LLM-012 — Final promotion is identity-bound, rename-only, and reversible

**Implemented in finalization contracts, live coordinator, workbench,
services, and VEGAS automation.** Finalization re-reads the live candidate,
reconciles it with the complete final plan, and validates that
plan before any promotion mutation. It must also match the exact accepted
final-polish baseline, including track identity, mute/solo, volume automation,
gain, fades, transitions, grouping, and event effects. Missing, extra,
ambiguous, out-of-range, or unsupported changes stop finalization. An optional final
render hook runs before promotion and must return an existing, length- and
SHA-256-verified session artifact; a requested render that fails validation
prevents promotion. The concrete VEGAS hook reuses the bounded full-timeline
renderer, so a long montage is represented by an immutable manifest of
contiguous chunks of at most twenty seconds. Finalization re-verifies every
declared chunk before accepting that manifest as final-render evidence.
The editor's render choice is an explicit `FinalizeMontage` target bound to the
reviewed state revision and finalization-request hash. Recovery cannot switch
between render-then-promote and promote-without-an-additional-render.

Promotion is bound to the persisted VEGAS project fingerprint and the SHA-256
of the validated live candidate snapshot. The complete promotion intent is
written before mutation. The VEGAS host may rename only the exact tracks owned
by that candidate workspace: video becomes `AE|Montage Clips`, song becomes
`AE|Montage Song`, and numbered SFX tracks become
`AE|Montage Gun SFX N`. Candidate-only `AE|LLM|...` labels must not survive.
Unknown candidate track roles and collisions with any unrelated project track
are rejected. No unrelated track or timeline event is deleted, renamed, or
rebuilt during promotion.

The pre-mutation intent and recovery bundle retain the before and promoted
snapshot evidence, both hashes, the complete rename map, and the project
identity. If the host stops during its short rename transaction, recovery
accepts a mix of unchanged candidate labels and completed final labels only
when every mapped track still matches the backed-up content. Rollback requires an automation
client bound to that same project and refuses to act if the promoted tracks
cannot be identified exactly, their content has diverged from the promoted
snapshot, or restoring a candidate name would collide with unrelated content.
A successful rollback must reproduce the original candidate snapshot hash
exactly; otherwise the rename is reversed back to the promoted state and the
rollback fails.

Finalization retry always resumes the one persisted promotion ID, request,
project fingerprint, workspace, reconciled final-plan hash, candidate-snapshot
hash, render choice, and rename map. It never creates a second promotion intent.
If a process stopped after the intent but before the recovery bundle, the host
first applies the hash-bound rollback protocol to normalize either unchanged,
partially renamed, or fully renamed tracks back to the validated candidate; it
then replays the original promotion with the original idempotency key. A
completed report and archive are reused only after their identities, lengths,
entry count, and SHA-256 evidence validate. A created archive whose receipt was
not yet committed is inspected and adopted rather than overwritten. Explicit
rollback is one stable promotion-bound transaction; after rollback, the old
finalization intent cannot be reported as completed or silently promoted again.

Finalization writes the canonical final plan, promotion intent, recovery bundle,
optional render evidence, per-model and whole-session token/latency/cache/cost
usage summaries, an artifact manifest, a final session report, and an external
ZIP archive with its own SHA-256 receipt. Runtime locks, temporary files, and
filesystem links are excluded or rejected rather than being presented as
durable archived evidence.

### EDIT-LLM-011 — Effects and audio are separate, capability-bound polish passes

**Implemented in the post-sync polish contracts, live coordinator, workbench,
and VEGAS automation adapters.** Effects and audio are never folded into clip
synchronization. Each is a separately versioned plan bound by SHA-256 to the
accepted rough cut. The audio revision is also bound to the exact accepted
effects revision, so audio cannot be planned against visual state that was
merely proposed or later replaced.

The executable visual capability is the native `ScreenPump` renderer only.
Every executable pump names its exact placement, timeline and local event time,
intensity, duration, recipe, and reason. Existing explicit supported pumps and
reviewed kill synchronization assignments may create actions. Flash, shake,
transition, cinematic-transition, title, color, or unknown intent is retained
as a diagnostic and never converted to a different treatment or reported as
rendered. A missing renderer produces no executable action.

The audio pass contains a song action at timeline zero with track gain `0.5`
and one gun/hit SFX action at gain `0.6` for every reviewed confirmed kill.
Before timeline mutation, the VEGAS adapter verifies that every action matches
the canonical accepted placement and confirmation time, that no action was
omitted or added, and that the calibrated template catalogue and media files
are available. Template selection, confirmation alignment, overlap-track
allocation, tails, fades, and fade curves use the existing implemented audio
rules. An existing owned SFX track or a nonmatching owned song blocks
accidental reapplication; replacement requires an explicit rollback path.

The synchronization pass deliberately keeps the selected song audible. When
the later audio pass requests that same song at timeline zero and gain `0.5`,
the adapter verifies and reuses the existing owned song event instead of
duplicating it. A different path, start, gain, multiple song events, or any
pre-existing owned SFX track stops the pass and requires explicit recovery.

An exact plan-revision approval is required before either adapter runs. The
materialization result lists every action as applied, rejected, unsupported, or
failed and includes a post-pass candidate snapshot. A pass is not eligible for
acceptance unless all executable actions applied successfully. The fully
applied result must then be rendered across the complete candidate timeline as
contiguous preview chunks no longer than twenty seconds. Final acceptance
binds the plan, materialization, and preview-manifest hashes. Plans, approvals,
append-only state transitions, materialization results, preview manifests, and
accepted-pass records are immutable under `assembly/polish`.

Persisted preview metadata is only a cache hint. Before reuse or acceptance,
every chunk's expected index, time range, safe output path, and actual file
SHA-256 must match. Invalid metadata, missing output, or corrupt bytes quarantine
both the untrusted metadata and media inside the session. The replacement uses
a fresh idempotency key so a broker cannot replay a stale response whose output
was lost. The complete manifest is reused only while all referenced files still
match; otherwise it is quarantined and the bounded missing evidence is rendered
again. Acceptance and rejection both fail closed if verified preview evidence
is unavailable.

Rejecting a rendered polish preview is also immutable and terminal for that
revision. Its rejection artifact binds the exact plan, materialization, and
preview hashes, records the actor and note, and explicitly requires restoration
of the accepted candidate baseline before a later revision is applied. The
coordinator writes a restoration intent before cleanup, re-materializes the
accepted rough cut, reapplies the accepted effects when restoring an audio
rejection, validates the restored snapshot, and only then writes a hash-bound
completion receipt. An interrupted restoration resumes the same persisted
attempt. A later plan must use a strictly higher revision. Disabling every
capability is represented as an explicit empty plan;
that no-op revision still requires approval, an unchanged post-apply snapshot,
a complete preview, and acceptance, so “skip this pass” is evidence-backed
rather than silent.
