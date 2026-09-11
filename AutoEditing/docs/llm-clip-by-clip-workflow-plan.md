# LLM Clip-by-Clip Editing Workflow Plan

Status: Implementation and completion audit complete; Debug, Release, and
Deploy verified  
Last updated: 2026-07-27  
Scope: LLM-driven montage assembly, human steering, VEGAS interaction,
render/review iteration, effects, audio, recovery, and workbench UX.

## Implementation progress

Verified Phase 1 foundation currently present:

- [x] Assembly state is persisted with a monotonically increasing state
  revision.
- [x] Assembly commands identify the session, checkpoint, and expected state
  revision; stale or malformed commands are quarantined and pending commands
  are ordered deterministically.
- [x] Supported human timeline changes are reconciled into a structured
  `TimelineAdjustmentDelta`.
- [x] Revision inference receives the reconciled candidate evidence and the
  structured before/after adjustment data.
- [x] Missing expected events, unexpected added events, duplicate/ambiguous
  media matches, invalid source bounds, and variable velocity are explicitly
  rejected instead of silently ignored.
- [x] Synchronization speed profiles are rendered independently of creative
  effects, including explicit `1.0x` envelope evidence; a missing live envelope
  reconciles as actual `1.0x` rather than inheriting the proposed rate.
- [x] Reconciled prefixes and newly inferred plans are validated after accepted
  placements are merged.

Phase 1 verification completed:

- [x] Repeated identical commands are idempotent, contradictory commands are
  rejected, and concurrent consumers produce exactly one winner.
- [x] Multi-clip integration coverage includes post-merge conflicts and all
  reconciliation rejection categories.
- [x] Phase 1 builds cleanly across the .NET 8 companion and .NET Framework
  VEGAS UI, and all LLM, inference, and VEGAS automation self-tests pass.

Completed explicit UX and recovery work:

- [x] Durable placement identity beyond normalized media path.
- [x] Explicit workbench resolution choices for deletion, addition, and
  ambiguity instead of rejection-only handling.
- [x] Workbench presentation of detected adjustments, quarantined actions, and
  the consequences of each available action.

Verified Phase 2 progressive planning:

- [x] The initial inference returns a semantic `AssemblySketch`, including song
  sections, tentative order, reservations, alternatives, confidence, and
  uncertainty.
- [x] The sketch request contains the complete compact song-event lattice and
  the detailed legal synchronization anchors.
- [x] Normal planning and revision inference returns one `ClipStepDecision`
  rather than a complete executable montage.
- [x] Accepted-prefix context is derived from the actual canonical VEGAS
  placement and permits zero surviving syncs after a human adjustment.
- [x] Single-step decisions are compiled and validated deterministically
  against the accepted prefix, remaining media, reviewed events, song bounds,
  speed capabilities, and overlap rules.
- [x] Sketch, proposal revisions, adjustment deltas, and accepted plans are
  persisted per session and checkpoint.
- [x] A deterministic two-clip integration test covers revise, human-adjusted
  acceptance, next-clip planning, final acceptance, and artifact recovery.

Later phases are marked verified only by their explicit progress entries below.

Verified Phase 3 workbench foundation:

- [x] The workbench projects the active sketch, current proposal, accepted,
  current, and remaining checkpoints from append-only session artifacts.
- [x] The header shows the assembly phase, current clip, checkpoint count,
  model activity, and token usage.
- [x] The current proposal shows source bounds, speed, musical anchor,
  rationale, confidence, alternatives, and global assembly intent.
- [x] A non-destructive `Compare with VEGAS` action captures and displays
  proposed-versus-actual timing changes before acceptance or revision.
- [x] Quarantined commands are surfaced as workbench evidence instead of being
  hidden in session files.
- [x] Action labels state what they preserve or discard, final acceptance uses
  a single dynamic primary action, and common steering directions are available
  as quick actions.

Verified Phase 6 isolated rough-cut audit foundation:

- [x] Complete synchronization-pass evidence uses a durable manifest of
  contiguous render chunks bounded to twenty seconds each; completed chunks
  are resumable and collectively cover the full timeline.
- [x] Deterministic audit metrics and findings cover pacing discontinuities,
  action continuity, gaps, repeated maps/weapons/visual situations, region
  density, unused major musical events, and unfulfilled sketch reservations.
- [x] Multimodal audit uses a strict evidence schema and can only return
  evidence-citing findings plus checkpoint-targeted move, trim, duration, or
  constant-speed correction proposals. Unsupported creative ideas remain
  advisory.
- [x] Corrections require an explicit disposition, checkpoint reopening
  requires prior approval, and accepted rough-cut milestones bind the report,
  plan, and render evidence by hash.
- [x] Rendering, schema, fallback, persistence, targeted-reopen, and milestone
  behavior have deterministic self-test coverage.
- [x] Final render is gated by an exact accepted-plan/live-timeline
  reconciliation; drift cannot be rendered under a stale plan hash.
- [x] Render reuse verifies every chunk file and SHA-256; missing or corrupt
  outputs are quarantined and rerendered rather than trusted.
- [x] Rough-cut subphases, the active correction, and the phase preceding pause
  are durable. Claimed actions are journaled before consumption and replayed
  idempotently after a crash; saved reports and accepted milestones resume
  directly at their last completed boundary.
- [x] The rough-cut coordinator acquires the exclusive runtime lease itself
  when no lease is supplied by its caller.
- [x] Coordinator/workbench hook: after the last sync checkpoint is accepted,
  capture the complete timeline, invoke `RoughCutFullRenderService`, generate
  targeted VEGAS snapshot evidence, invoke `RoughCutAuditService`, persist with
  `RoughCutAuditArtifactStore`, and remain in rough-cut review until all
  correction decisions are terminal and the milestone is accepted.
- [x] Evidence sampling covers overview, both sides of joins,
  confirmed-action/sync moments,
  reviewed major musical events, and section boundaries within the nine-frame
  VEGAS capture limit; the UI does not mislabel snapshots as decoded frames.
- [x] The workbench provides ordered chunk playback, timestamped snapshot
  inspection and VEGAS navigation, bounded finding review, evidence citations,
  and finding-to-correction traceability.

Verified Phase 5 checkpoint preview foundation:

- [x] `Render checkpoint preview` is an explicit non-mutating assembly action.
- [x] The renderer keeps the complete active placement and adds up to two
  seconds of available context on each side without exceeding the existing
  twenty-second checkpoint-preview limit.
- [x] Numbered attempts persist timeline, render-window, timing-sidecar,
  timing-visualization, contact-sheet sampling, render hash, and reviewer
  evidence.
- [x] The multimodal reviewer returns observations and suggestions only; its
  output cannot directly change or accept the timeline.
- [x] Render failure, render timeout, cancellation, and post-render reviewer
  failure remain distinct, inspectable outcomes.
- [x] VEGAS captures five real isolated-candidate PNG frames at deterministic
  sample times and supplies them with the timing PNG to the image-capable local
  reviewer.
- [x] An embedded workbench preview player shows the selected checkpoint render;
  preview attempt
  status and evidence are already projected in the checkpoint details.

Verified Phase 7 isolated polish-pass foundation:

- [x] Effects and audio/SFX use separate versioned plans bound by SHA-256 to
  the accepted rough cut; audio additionally binds to the exact accepted
  effects revision.
- [x] Executable visual capability is explicitly limited to native VEGAS
  screen pumps. Unsupported visual intent remains diagnostic and is never
  presented as rendered.
- [x] The audio plan contains the song action and one deterministic action for
  every reviewed confirmed kill. The VEGAS adapter preflights the calibrated
  template catalogue and applies the existing alignment, gain, tail, and fade
  rules.
- [x] Exact-revision approval is required before either pass mutates VEGAS.
  Applied-action results and the post-pass timeline snapshot are persisted, and
  a partially applied pass cannot be accepted.
- [x] Each applied pass requires complete preview evidence split into chunks no
  longer than twenty seconds before final pass acceptance.
- [x] Rejecting a rendered preview is terminal for that exact revision and
  records that candidate-baseline restoration is required before a higher
  revision may apply. A disabled/no-op pass still follows approve, unchanged
  snapshot, preview, and accept rather than being silently skipped.
- [x] Plans, approvals, append-only state transitions, materialization results,
  previews, and accepted-pass records are immutable artifacts under
  `assembly/polish`.
- [x] Deterministic self-tests cover capability filtering, approval gates,
  materialization, bounded previews, immutable revisions, effects-to-audio
  sequencing, and final pass acceptance.
- [x] Coordinator/workbench hook: enter `PolishPassWorkflow` after the accepted
  rough-cut milestone and project `assembly/polish` state, action summaries,
  preview chunks, and approve/reject controls in the workbench.

Verified Phase 8 isolated finalization foundation:

- [x] Final validation re-reads and reconciles the complete candidate before
  promotion; an optional final render hook must return hash-verified evidence.
- [x] Promotion is project-identity- and candidate-snapshot-bound, changes only
  candidate-owned track labels, rejects final-name collisions, and removes all
  candidate-only labels.
- [x] A pre-mutation intent and complete recovery bundle support rollback only
  when project identity and promoted timeline evidence still match.
- [x] Final session reporting includes per-model and session usage, cache-token
  totals, final plan and snapshot hashes, optional render evidence, artifact
  inventory, and a SHA-256-addressed external archive.
- [x] Deterministic contract and companion self-tests cover mapping safety,
  label removal, identity binding, reporting, archival, and rollback.
- [x] Coordinator/workbench hook: after final review acceptance, invoke
  `FinalizationService`, publish the completed report, and expose the explicit
  rollback command while the recovery bundle remains valid.

Completed cross-phase workflow and UX work:

- [x] Scoped steering is durable and explicitly limited to the current clip,
  next clip, remaining song section, or all remaining clips.
- [x] Every checkpoint preview attempt remains selectable for A/B playback;
  section-completion milestones render the entire accepted section in verified
  chunks no longer than twenty seconds.
- [x] Final review offers an explicit render-before-promotion choice. The
  choice is part of the state-revision-bound finalization action and survives
  restart without changing meaning.
- [x] Rough-cut acceptance records a plan-, workspace-, and snapshot-bound
  complete timeline baseline. Accepted effects and audio passes advance that
  baseline chain. Polish entry, stage restoration, and finalization reject
  unsupported track, audio automation, fade, transition, grouping, or effect
  divergence against the appropriate accepted stage.
- [x] Pending action execution disables stale workbench review controls,
  recovered actions repair their consumed disposition, and superseded
  checkpoint workspaces are cleaned with stable idempotency keys.

## 1. Purpose

This document is the durable implementation plan for evolving the existing LLM
editing prototype into an editor-like, clip-by-clip workflow.

The intended experience is:

1. The client supplies the song and footage.
2. Analysis identifies song structure, musical events, footage events, and
   relevant learned editing patterns.
3. The LLM forms a loose mental assembly for the complete montage.
4. The LLM proposes and materializes one clip at a time.
5. The human reviews or adjusts that clip directly in VEGAS.
6. The system detects those adjustments and either accepts them or supplies them
   to the LLM for another proposal.
7. Once all clips are synchronized, the system reviews the complete rough cut.
8. Effects and audio are designed in later, separate passes.
9. Intermediate results are rendered and reviewed where visual or auditory
   evidence is needed.
10. The accepted candidate is promoted to a finished montage without disturbing
    unrelated timeline content.

This plan is intentionally broader than a single implementation task. Each phase
has its own exit criteria and should remain usable as a reference after context
from the original design discussion is no longer available.

## 2. Repository contract

`docs/editing-rules.md` is the normative, code-independent editing rulebook.

Any implementation change affecting montage ordering, synchronization
allocation, velocity, audio treatment, effect selection, or effect rendering
must update all three of the following in the same change:

1. production code;
2. deterministic tests;
3. `docs/editing-rules.md`.

Do not document modeled or proposed effects as implemented. Preserve existing
rule IDs where possible and introduce new IDs for genuinely new behavior.

## 3. Target workflow

```text
Create or resume session
  |
  v
Analyze song, clips, and style references
  |
  v
Generate a loose assembly sketch
  |
  v
Propose one clip for the next musical window
  |
  v
Validate and materialize the proposal in VEGAS
  |
  v
Editor plays or renders the section
  |
  v
Detect manual timeline changes
  |
  +--> Accept and continue
  |
  +--> Ask AI to revise the current clip
  |
  +--> Reset to the last proposal
  |
  +--> Pause and resume later
  |
  v
Repeat until synchronization assembly is complete
  |
  v
Audit and render the complete rough cut
  |
  v
Effects pass
  |
  v
Audio and SFX pass
  |
  v
Preview and revision loop
  |
  v
Promote candidate to final montage
```

The global plan guides the edit, but only one clip decision becomes executable
at a time.

## 4. Architectural principles

### 4.1 Separate editorial intent from execution

The LLM should make editorial decisions. Deterministic code must compile,
validate, and execute them. The LLM must not emit arbitrary VEGAS API calls.

### 4.2 VEGAS is the authoritative editing workspace

Once the editor adjusts a proposal in VEGAS, the system must explicitly detect,
classify, persist, and either adopt or reject each change. It must not silently
restore deleted work or ignore additions.

### 4.3 Accepted work is stable

An accepted checkpoint is immutable unless the editor explicitly reopens it.
Planning the next clip must not alter accepted placements.

### 4.4 Every transition is recoverable

LLM requests, raw responses, parsed responses, validation results, timeline
snapshots, adjustment deltas, renders, and user commands must be persisted.

### 4.5 Effects follow synchronization

The first pass establishes clip order, trims, pacing, and synchronization.
Effects, SFX, mixing, and final polish are separate passes operating on an
accepted rough cut.

### 4.6 The UI must explain consequences

Every action must make clear what it preserves, replaces, accepts, or sends to
the LLM. Empty panels and indefinite waiting states are not acceptable.

## 5. Core planning contracts

### 5.1 `AssemblySketch`

The initial LLM response should be a semantic strategy rather than a complete
executable `EditPlanDocument`.

It should contain:

- a summary of the montage concept;
- opening, buildup, peak, and ending intent;
- intended energy and pacing arc;
- the role of each reviewed song section;
- tentative clip order;
- high-value clips reserved for peaks or resolution;
- preferred musical anchors;
- alternatives;
- uncertainties and confidence.

Illustrative shape:

```json
{
  "schemaVersion": 1,
  "sessionId": "edit-...",
  "strategy": {
    "summary": "Build from isolated shots into sustained streaks.",
    "pacingArc": "Measured opening, accelerating middle, strongest closer.",
    "openingIntent": "Establish the player and weapon clearly.",
    "peakIntent": "Reserve the strongest multi-kill for the largest rise.",
    "endingIntent": "Finish on a readable final confirmation."
  },
  "songSections": [
    {
      "regionId": "region-...",
      "purpose": "build",
      "desiredEnergy": 0.65,
      "preferredClipTraits": ["continuous action", "triple"]
    }
  ],
  "tentativeClipOrder": [
    {
      "clipId": "clip-...",
      "role": "opening",
      "reason": "Readable first engagement.",
      "confidence": 0.78,
      "alternativeClipIds": ["clip-..."]
    }
  ],
  "reservations": [],
  "uncertainties": []
}
```

The sketch may evolve after accepted checkpoints, but changes must be versioned
and must never silently rewrite the accepted prefix.

### 5.2 `ClipStepDecision`

Each planning step should return one executable placement only.

It should reference stable clip and song-event IDs instead of copying complete
clip objects and the complete song analysis into the response.

It should contain:

- session, checkpoint, and revision identifiers;
- selected clip ID;
- source window;
- target timeline window;
- selected musical event IDs;
- synchronization mapping;
- expected playback-rate treatment during the sync pass;
- concise rationale;
- considered alternatives;
- confidence and warnings.

Illustrative shape:

```json
{
  "schemaVersion": 1,
  "sessionId": "edit-...",
  "checkpointId": "checkpoint-004",
  "checkpointVersion": 4,
  "clipId": "clip-...",
  "sourceWindow": {
    "startSeconds": 3.2,
    "endSeconds": 8.7
  },
  "timelinePlacement": {
    "startSeconds": 12.4,
    "songEventIds": ["event-..."]
  },
  "syncPoints": [],
  "reasoningSummary": "Align the first confirmed shot to the section rise.",
  "alternatives": [],
  "confidence": 0.82
}
```

### 5.3 `TimelineAdjustmentDelta`

The VEGAS reconciler should deterministically compare the materialized proposal
with the current timeline.

It should record:

- the proposal and current timeline revisions;
- changes to start, duration, trim, synchronization, tracks, audio, or effects;
- deleted expected events;
- unexpected added events;
- ambiguous identities;
- supported and unsupported changes;
- optional human explanation.

Illustrative shape:

```json
{
  "checkpointId": "checkpoint-004",
  "baseRevision": 4,
  "changes": [
    {
      "kind": "SourceTrimChanged",
      "before": 3.2,
      "after": 3.55
    },
    {
      "kind": "TimelineStartChanged",
      "before": 12.4,
      "after": 12.52
    }
  ],
  "unsupportedChanges": [],
  "editorIntent": null
}
```

This delta must be passed to the LLM when revising the current clip. A textual
statement that an adjustment occurred is insufficient.

## 6. Component responsibilities

| Component | Responsibility |
| --- | --- |
| LLM planner | Editorial strategy and one-clip proposals |
| Decision compiler | Resolve stable IDs into domain objects |
| Validator | Reject overlaps, invalid media ranges, impossible sync, and broken invariants |
| VEGAS adapter | Apply and inspect timeline state |
| Timeline reconciler | Compute explicit human adjustment deltas |
| Renderer | Produce bounded checkpoint and milestone previews |
| Multimodal reviewer | Evaluate rendered evidence and return observations |
| Session coordinator | Persist and enforce state transitions |
| Workbench UI | Explain state, evidence, differences, and available actions |
| Inference monitor | Display full conversations, live generation, latency, and token usage |

## 7. Persisted session state machine

Use explicit persisted states:

```text
Initializing
Analyzing
Sketching
AwaitingClipProposal
ValidatingProposal
MaterializingProposal
AwaitingHumanReview
RevisingProposal
AcceptingCheckpoint
RenderingCheckpoint
AuditingRoughCut
EffectsPlanning
EffectsReview
AudioPlanning
FinalReview
Completed

Paused
Recovering
Failed
Cancelled
```

Every transition must record:

- session ID;
- checkpoint ID and version, when applicable;
- previous and next state;
- timestamp;
- triggering user or system command;
- associated inference request and response IDs;
- validation result;
- diagnostic or error details.

Commands must include the expected checkpoint version. Stale or conflicting
commands must be quarantined and displayed in diagnostics instead of crashing
or blocking the session.

## 8. Detailed checkpoint behavior

### 8.1 Create the initial sketch

The initial request receives:

- compact clip catalogue;
- complete compact song-event timeline;
- detailed events for eligible anchors;
- reviewed song regions;
- style findings from analyzed editor projects;
- normative editing rules;
- user direction and enabled capabilities.

The response is an `AssemblySketch` only.

### 8.2 Request a clip proposal

The per-clip request receives:

- current assembly sketch;
- compact summary of the accepted prefix;
- remaining clips;
- the next relevant song window and nearby musical events;
- eligible source events for likely clips;
- previous checkpoint outcome;
- hard constraints and available renderer capabilities;
- user steering scoped to the current step.

The response is one `ClipStepDecision`.

### 8.3 Validate before mutation

Before touching the current candidate:

1. Compile stable references.
2. Validate media existence and ranges.
3. Validate against the accepted prefix.
4. Validate song boundaries and synchronization assignments.
5. Validate supported playback-rate behavior.
6. Produce a clear validation report.

Only after successful validation may the old candidate be replaced.

### 8.4 Materialize

Materialization should:

- retain all accepted events in place;
- create or replace only the active candidate;
- assign durable placement metadata;
- preserve unrelated non-AutoEditing timeline objects;
- render the authoritative synchronization speed profile before audio or
  creative treatment work, even when creative effects are disabled;
- keep creative effects and SFX disabled during synchronization;
- snapshot VEGAS state immediately afterwards.

### 8.5 Accept and continue

When the editor accepts:

1. Read actual VEGAS state.
2. Calculate the adjustment delta.
3. Resolve deletions, additions, and ambiguities.
4. Validate the actual combined timeline.
5. Persist the accepted checkpoint immutably.
6. Update the sketch using the actual result.
7. Determine the next musical window.
8. Request one new clip decision.
9. Materialize only the new candidate.

### 8.6 Revise current clip

When the editor requests a revision:

1. Read actual VEGAS state.
2. Calculate and display the adjustment delta.
3. Capture optional textual or quick-action guidance.
4. Send the actual delta, current proposal, and guidance to the LLM.
5. Request a replacement for the current clip only.
6. Validate it against the accepted prefix.
7. Replace only the current candidate after validation.

The editor must be able to choose whether manual changes are:

- constraints to preserve;
- examples of the intended direction;
- temporary experiments to discard.

### 8.7 Reset

Reset restores the exact persisted proposal revision. It must not reconstruct an
approximation by rerunning the planner.

### 8.8 Finish synchronization

On the last checkpoint, the primary action should read:

`Accept clip and finish sync assembly`

An early-finish option may exist but must display unused clips, uncovered song
regions, and relevant warnings before confirmation.

## 9. VEGAS timeline reconciliation

### 9.1 Stable identity

Do not rely only on media paths. Persist:

- session ID;
- placement ID;
- checkpoint ID;
- clip ID;
- event role;
- proposal revision.

Where VEGAS does not expose durable custom metadata, maintain a companion
identity map using event signatures and explicit ambiguity handling.

### 9.2 Change classification

Reconciliation must distinguish:

- event moved;
- source trim changed;
- duration changed;
- constant speed changed;
- velocity envelope changed;
- expected event deleted;
- unexpected event added;
- track changed;
- linked audio changed;
- audio fade changed;
- transition added or changed;
- effect added or changed;
- event identity ambiguous.

No category may be silently ignored.

A missing velocity envelope is not unknown or permission to reuse the proposed
rate: it is observed normal VEGAS playback at `1.0x`. A constant live envelope
is adopted exactly, while a variable envelope fails closed until the workflow
supports deterministic piecewise velocity. Candidate materialization writes
even a normal `1.0x` envelope, so newly built proposals provide explicit
readback evidence and any later envelope removal is visible as a human change.

### 9.3 Resolution UX

For missing, extra, or ambiguous events, the implemented workbench allows the
editor to:

- explicitly adopt one exact known remaining-media event as the unavailable
  current clip or as an additional clip when deterministic validation permits;
- restore and rematerialize the exact AI proposal;
- exclude a deleted current clip only when the remaining prefix reconciles
  exactly and remains non-empty;
- defer resolution and pause without discarding the conflict artifact.

Foreign media, already-used media, identical ambiguous duplicates,
out-of-bounds source ranges, variable velocity, and candidates that would make
the combined plan invalid are deliberately not adoptable. These cases remain
visible and require restoration, deferral, or another separately safe choice;
the system never guesses.

Mutating conflict choices are crash-resumable operations. A deterministic
intent is written before rebuild or sketch mutation, candidate evidence is
re-read before the first adoption/exclusion attempt, and restart replays the
same conflict-bound intent. Sketch exclusion/reordering is idempotent, while
the conflict phase remains visible until canonical rematerialization,
reconciliation, and accepted-plan persistence verify completion.

Variable velocity should either be represented fully as deterministic,
piecewise velocity data or clearly rejected during the sync-only phase.

### 9.4 Post-merge validation

Validate after:

- reconciling human changes;
- preserving the accepted prefix;
- merging a new proposal;
- resetting a proposal;
- reopening an accepted checkpoint;
- resuming a session.

Never clean up the previous valid candidate until the replacement has passed
validation.

## 10. Rendering and multimodal review

### 10.1 Checkpoint preview

Allow a lightweight preview around the current edit:

- roughly two seconds before the new placement;
- the complete new placement;
- roughly two seconds after it;
- song and relevant source audio;
- timecode and metadata sidecar.

Persist:

- low-resolution preview video;
- contact sheet;
- waveform or beat-overlay visualization;
- synchronization metrics;
- timeline snapshot.

### 10.2 Review input

The multimodal reviewer receives:

- editorial intent;
- proposed sync points;
- rendered preview or sampled frames;
- audio and timing metrics;
- human adjustment delta;
- previous review observations.

It returns observations, confidence, and suggested changes. It does not directly
mutate the timeline.

### 10.3 Milestone renders

Render at minimum after:

- completing a song section;
- completing the synchronization assembly;
- completing effects;
- completing audio treatment;
- completing final revision.

The workbench should support side-by-side or A/B comparison of revisions.

## 11. Workbench UX

### 11.1 Layout

```text
+---------------------------------------------------------------+
| Session / phase / checkpoint / model / tokens / Pause         |
+---------------+----------------------------+------------------+
| Checkpoints   | Current clip workspace     | Evidence and     |
|               |                            | steering         |
| completed     | intent and timing          | rationale        |
| current       | proposed vs actual         | alternatives     |
| remaining     | timeline visualization     | render review    |
|               | preview player             | user direction   |
+---------------+----------------------------+------------------+
| Accept & next | Revise | Reset | Render preview | Pause        |
+---------------------------------------------------------------+
```

### 11.2 Persistent status header

Display:

- session name;
- current workflow phase;
- checkpoint number and total;
- current clip;
- llama.cpp connection state;
- model name;
- elapsed inference time;
- input, cached, and output tokens;
- estimated context utilization;
- Pause or Resume.

Use concrete states such as:

- Waiting for llama.cpp;
- Generating assembly sketch;
- Choosing clip 4;
- Validating proposal;
- Applying clip in VEGAS;
- Waiting for timeline review;
- Rendering checkpoint preview;
- Revising from editor adjustments.

The UI must never appear idle while a request is active.

### 11.3 Checkpoint navigator

Each checkpoint should show:

- clip thumbnail or identifying label;
- accepted, current, revised, warning, or error state;
- timeline range;
- confidence;
- manual-adjustment indicator;
- render availability.

Accepted checkpoints may be inspected without becoming editable. Reopening one
requires an explicit action and impact warning.

### 11.4 Current proposal

Lead with a plain-language editorial explanation, followed by:

- source range;
- timeline range;
- selected musical events;
- expected source events;
- proposed speed treatment;
- confidence;
- alternatives;
- validation warnings.

### 11.5 Proposed-versus-actual timeline diff

After a VEGAS change, display a comparison:

| Property | AI proposal | Current VEGAS |
| --- | ---: | ---: |
| Timeline start | 24.40s | 24.53s |
| Source in | 3.44s | 3.61s |
| Duration | 8.07s | 7.82s |
| First kill offset | 1.20s | 1.07s |

This exact structured difference should also be available to the LLM.

### 11.6 Steering

Provide freeform guidance plus quick actions:

- More setup before the first shot;
- Tighter pacing;
- Use a stronger clip;
- Align another kill;
- Preserve my timeline adjustment;
- Try the alternative anchor.

Guidance must have an explicit scope:

- current clip;
- next clip;
- remaining song section;
- global assembly sketch.

The UI should explain which existing work each scope can affect.

### 11.7 Errors and diagnostics

Primary error messages must explain the problem and available choices. Example:

> The current candidate event was deleted in VEGAS. Exclude this clip, restore
> the proposal, or adopt another event before continuing.

Offer:

- Retry;
- Inspect details;
- Restore last valid state;
- Pause session.

Raw exceptions and stack traces belong in the diagnostics view and persisted
logs, not in the primary workflow.

## 12. Persistence and recovery

Use an append-only session structure:

```text
sessions/{sessionId}/
  manifest.json
  state.json
  event-log.jsonl
  sketch/
  checkpoints/
    0001/
      proposal.json
      vegas-before.json
      vegas-after.json
      adjustment-delta.json
      validation.json
      request.json
      response.txt
      response.json
      preview/
  renders/
  usage/
  diagnostics/
```

Save the complete request and raw response before attempting response parsing.

On startup:

1. Discover incomplete sessions.
2. Verify project and media identity.
3. Compare persisted state with the current VEGAS timeline.
4. Offer Resume, Inspect, or Abandon.
5. Require conflict resolution when the project diverged.
6. Never silently start the workflow again from the beginning.

## 13. Reliable command handling

Replace GUID filename ordering with a durable command envelope:

- monotonic sequence number;
- creation timestamp;
- session ID;
- checkpoint ID and expected version;
- unique command ID;
- command kind and payload.

Use atomic claiming or file moves and maintain:

- pending;
- processing;
- processed;
- rejected;
- quarantined.

Only one command may win for a checkpoint version. Duplicate button clicks must
be idempotent. A stale action must be quarantined and surfaced without blocking
newer valid actions.

## 14. Usage and inference observability

The workbench and separate inference monitor should expose:

- full system and user prompts;
- raw generated text as it streams;
- parsed response;
- parse and validation failures;
- request latency and time to first token;
- prompt tokens;
- cached prompt tokens where reported;
- generated tokens;
- tokens per second;
- context-window utilization;
- retries and repair attempts;
- current-session totals;
- lifetime totals per model and backend.

Every request and response must remain exportable for comparison with external
models.

## 15. Post-synchronization phases

### 15.1 Rough-cut audit

Evaluate:

- pacing arc;
- accidental gaps or dead space;
- repeated maps, weapons, or visual situations;
- weak joins;
- unused major musical events;
- overly dense or sparse sections;
- whether reserved high-value clips fulfilled their intended roles.

Present proposed corrections individually and require approval before reopening
accepted checkpoints.

### 15.2 Effects pass

Effects operate on stable placement and event IDs. Each treatment must identify:

- placement ID;
- event or time range;
- supporting rule or style finding;
- effect type and parameters;
- intensity;
- renderer capability;
- rationale.

Only effects supported by the VEGAS renderer may be described as implemented.

### 15.3 Audio and SFX pass

Handle independently:

- source-audio retention;
- weapon-sound emphasis;
- music ducking;
- SFX selection and timing;
- fades and crossfades;
- loudness and clipping;
- audio continuity.

### 15.4 Final promotion

Provide an explicit `Promote candidate to montage` action that:

- validates the complete timeline;
- optionally renders a final preview;
- creates a recoverable backup;
- removes candidate-only styling and labels;
- preserves unrelated timeline objects;
- archives session artifacts;
- records a final session and usage summary.

## 16. Implementation phases

### Phase 1: Correctness foundation

Deliver:

- persisted state machine;
- checkpoint revisions and idempotent commands;
- actual timeline adjustment data in revision requests;
- validation after accepted-prefix merging;
- explicit missing, added, and ambiguous event handling;
- deterministic action ordering and stale-action quarantine;
- multi-clip integration tests;
- corresponding editing-rule updates.

Exit criterion:

Basic clip-by-clip work cannot silently lose, restore, or overwrite a human
adjustment.

### Phase 2: Progressive planning contracts

Deliver:

- `AssemblySketch`;
- `ClipStepDecision`;
- `TimelineAdjustmentDelta`;
- sketch-only initial inference;
- one-clip checkpoint inference;
- stable clip and song-event references;
- revised prompts and copyable prompt artifacts;
- schema, validation, and malformed-response tests.

Exit criterion:

Routine LLM responses no longer contain the complete executable montage.

### Phase 3: Workbench UX

Deliver:

- redesigned session header;
- checkpoint navigator;
- live progress and inference status;
- current proposal before acceptance;
- proposed-versus-actual timeline diff;
- scoped steering controls;
- actionable error states;
- accurate language for implemented rendering capability.

Exit criterion:

The editor can understand the current state, the LLM's proposal, detected
changes, and the consequences of every available action.

### Phase 4: Resume and recovery

Implementation status (2026-07-27):

- [x] Incomplete and failed nonterminal sessions are discovered with an
  observable live-companion lease state.
- [x] Live Pause/Resume actions are limited to a durable review boundary;
  stopped Resume and Abandon launch recovery companion commands instead of
  writing orphan actions.
- [x] Request, session, workspace, and VEGAS project identity are validated;
  the first successful host identity is pinned to later automation requests.
- [x] Divergence remains recoverable and is reported without silently
  overwriting the candidate timeline or terminating the session.
- [x] Restart coverage includes exclusive leases, initial-sketch interruption,
  exact checkpoint recovery, project conflicts, divergence retry, and companion
  command lines.

Deliver:

- incomplete-session discovery;
- Pause, Resume, and Abandon;
- VEGAS project identity checks;
- persisted-state versus live-timeline reconciliation;
- action quarantine and diagnostics;
- restart and interruption tests.

Exit criterion:

Restarting VEGAS, the companion process, or the machine does not lose accepted
work.

### Phase 5: Preview rendering

Deliver:

- bounded checkpoint renders;
- preview player;
- contact sheets and timing evidence;
- persisted render metadata;
- multimodal-review connection;
- render cancellation, failure, and timeout handling.

Exit criterion:

The human and multimodal reviewer can inspect the materialized result rather
than reasoning solely from timeline metadata.

### Phase 6: Complete-assembly audit

Deliver:

- full synchronization-pass render;
- continuity and pacing review;
- proposed corrections;
- targeted checkpoint reopening;
- accepted rough-cut milestone.

Exit criterion:

Synchronization completion produces a reviewed rough cut rather than merely a
populated timeline.

### Phase 7: Effects and audio

Deliver:

- versioned effects pass;
- versioned audio and SFX pass;
- capability-aware deterministic rendering;
- preview and approval flow;
- deterministic tests and editing-rule updates for each implemented behavior.

Exit criterion:

All claimed effects and audio treatments are rendered, inspectable, and
test-covered.

### Phase 8: Finalization

Deliver:

- candidate promotion;
- backup and rollback;
- final validation and optional final render;
- artifact archive;
- final session and model-usage report.

Exit criterion:

The workflow produces a recoverable finished montage.

## 17. Testing matrix

Every phase should include the applicable combination of:

- schema serialization tests;
- state-transition tests;
- validator tests;
- reconciler tests;
- prompt snapshot tests;
- fake-LLM integration tests;
- multi-checkpoint workflow tests;
- recovery tests;
- VEGAS adapter contract tests;
- rendering tests.

Required scenarios:

1. Manual trim followed by revision.
2. Manual trim followed by acceptance.
3. Manual timeline move followed by acceptance.
4. Expected event deletion.
5. Unexpected event addition.
6. Ambiguous event identity.
7. Accepted placement conflicting with the next proposal.
8. Duplicate button clicks.
9. Stale checkpoint command.
10. Contradictory commands for the same checkpoint version.
11. Markdown-fenced JSON.
12. Malformed or incomplete JSON.
13. Semantically invalid but parseable JSON.
14. llama.cpp timeout or disconnect.
15. Companion process restart during generation.
16. VEGAS restart during human review.
17. Resume with a diverged timeline.
18. Final checkpoint acceptance.
19. Early sync completion with unused media.
20. Project reopened after sync completion.
21. Render cancellation and render failure.
22. Reopening an accepted checkpoint.
23. Final candidate promotion and rollback.

## 18. Parallel implementation structure

After the shared state and document contracts are agreed, work can proceed in
four streams:

### Stream A: State, persistence, and recovery

- session state machine;
- command processing;
- artifact layout;
- resume and conflict handling.

### Stream B: LLM planning

- schemas;
- prompts;
- compiler and validation;
- llama.cpp requests;
- request/response persistence and usage data.

### Stream C: VEGAS integration

- durable identities;
- reconciliation;
- deterministic materialization;
- timeline snapshots;
- rendering.

### Stream D: Workbench and monitor UX

- checkpoint navigation;
- status and progress;
- proposed-versus-actual diff;
- steering;
- preview;
- diagnostics and inference inspection.

Integration should occur phase by phase. Parallel streams must not independently
invent conflicting session states or document schemas.

## 19. Completion definition

The workflow is complete only when:

- the initial LLM result is a meaningful global editorial sketch;
- each normal planning response concerns one clip;
- human VEGAS changes are explicitly detected and fed back;
- accepted work is stable and revalidated;
- all actions are versioned, idempotent, and recoverable;
- the workbench explains current progress and decisions;
- prompts, responses, usage, and evidence are inspectable;
- the system can render and review intermediate results;
- the completed synchronization pass is audited;
- effects and audio run as separate implemented passes;
- the final candidate can be safely promoted and rolled back;
- production behavior, deterministic tests, and `editing-rules.md` agree.

## 20. Recommended delivery boundary

Phases 1 through 4 form the minimum dependable clip-by-clip editor.

Phases 5 and 6 make the system genuinely observational and iterative.

Phases 7 and 8 complete the autonomous montage workflow.

Do not describe phases beyond the currently completed exit criterion as
implemented.
