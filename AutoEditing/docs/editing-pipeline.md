# Editing pipeline

This document is the code-level map of the current end-to-end montage build. It
is intended to let an agent understand the workflow and ownership boundaries
without reverse-engineering the implementation.

The normative behavior contract is [editing-rules.md](editing-rules.md). When
ordering, synchronization, retiming, audio, effect selection, or rendering
changes, production code, deterministic tests, and that rulebook must change
together. The fuller target design for reusable effect presets is
[effect-preset-architecture.md](effect-preset-architecture.md).

## Pipeline at a glance

```text
reviewed clip library + selected song
    -> hydrate selected ready clips
    -> load reviewed song sidecar, or detect a legacy beat grid
    -> adapt music events/regions into planning input
    -> order clips, allocate kills, and solve velocity profiles
    -> review effect policy in the Effects wizard stage
    -> derive and preview a deterministic effect-treatment plan
    -> package the complete VEGAS-independent PreparedMontage
    -> cross the BuildMontage command boundary
    -> validate files, placements, speed profiles, and SFX
    -> mutate VEGAS: video, audio, velocity, supported effects, markers
    -> log what was planned, rendered, skipped, or rejected
```

All planning completes before the command that mutates VEGAS. `PreparedMontage`
is the boundary object between those two phases.

## 1. Sources and clip review

The editor chooses source folders, a song, and an SFX-template root in
`ShotReviewViewModel`. Shot analysis proposes candidate events; the review
workflow requires every candidate to be classified or removed and at least one
reviewed `Hit` or `Headshot` before a clip can be marked ready.

Reviewed event data is persisted by `ShotAnalysisSidecar` and exposed as a
reusable library by `ClipSyncLibrary`. The clip drawer selects ready files.
`ShotReviewWorkflow.HydrateFromLibrary` reconstructs the selected domain
`Clip` objects immediately before build. A build with no selected, available
ready clips fails before planning.

Main owners:

- `ShotReviewViewModel`: source selection, drawer selection, and build workflow.
- `ShotReviewWorkflow`: VEGAS review operations and library hydration.
- `ClipParser`, `ClipSyncLibrary`, `ShotAnalysisSidecar`: clip identity and
  persisted reviewed events.
- `ClipValidator` and the review command handlers: review-time validation and
  VEGAS interaction.

## 2. Song analysis and review

`SongStructureAnalyzer` creates a versioned `SongAnalysis` containing song
identity, musical events, regions, provenance, review state, and editorial
metadata. `SongAnalysisStore` persists it beside the song. Re-analysis is
reconciled by `SongAnalysisReconciler` so stable reviewed or locked decisions
survive compatible detector output.

At build time `MontageSongPlanningInputProvider` loads the song audio and
computes its current identity:

- If a song-analysis sidecar exists, its fingerprint and duration must match
  the selected song. `SongAnalysisPlanningInputAdapter` then converts reviewed
  regions and all non-rejected events into `MontageSongPlanningInput`.
- If no sidecar exists, the current compatibility path runs `BeatDetector` and
  adapts the resulting `BeatGrid` with `BeatGridPlanningInputAdapter`.

A stale or mismatched sidecar is a hard failure; the build does not silently
fall back to beats. Errors produced while adapting reviewed events are also a
hard failure.

## 3. Editorial assignments and automatic suggestions

Each music event may carry independent `EditorialAssignment` values. A music
event can therefore be both a gameplay sync anchor and an effect/structural
cue. User-chosen assignments, suggested assignments, locks, priority,
intensity, and timing offset are stored in `EditorialMetadata`.

`SongAnalysisPlanningInputAdapter` classifies assignments for montage
allocation. Unassigned, eligible detected events are promoted to suggested
gameplay anchors so pressing **Build montage** can work without manually
assigning every sync point. Explicit gameplay anchors retain preference over
suggestions through planner scoring. Rejected events and intentionally unused
events do not become eligible gameplay anchors; unused regions are excluded.

`EditorialMetadataValidator` owns assignment consistency checks.

## 4. Clip ordering, kill allocation, and velocity

`MontagePreparationService` creates `MontagePlanner` using configured pre-roll,
post-roll, minimum velocity, and maximum velocity. `MontagePlanner`:

1. generates deterministic candidate clip orders;
2. keeps explicit opener clips first and closer clips last;
3. compares ordinary-clip variants instead of assuming filename or capture
   chronology;
4. creates one demand for every reviewed kill;
5. allocates each demand to a distinct, chronological, eligible gameplay
   anchor while respecting region boundaries, song boundaries, and velocity
   bounds;
6. chooses the best feasible plan, preferring explicit/high-priority anchors
   and less disruptive speed profiles;
7. builds `ClipPlacement`, `MontageSyncAssignment`, mapped shot events, and
   piecewise speed profiles; and
8. verifies the solved mapping before reporting a feasible plan.

The normal approach to a kill remains at or above normal playback, the kill
lands during the accelerated approach, and the slow-motion dip is a short
post-kill treatment. `EffectsApplier.ApplyVelocityEnvelope` later translates
the solved `SpeedProfile` into a real VEGAS velocity envelope; it does not
invent the speed plan.

Insufficient anchors, impossible bounds, invalid clip windows, or failed
mapping verification make the plan infeasible. `MontagePreparationService`
throws before any VEGAS mutation.

## 5. Effect-treatment planning

Effects are not an implicit side effect of **Build montage**. Before opening
the clip drawer, the wizard presents an explicit **Effects** stage. This is the
editor-facing boundary between musical/clip intent and effect planning:

1. choose conservative automatic treatment or no automatic effects;
2. enable the treatment families that may be inferred;
3. choose the overall treatment intensity;
4. review how enabled effect families will be incorporated; and
5. continue to build with that exact configuration.

The stage labels renderer capability separately from editorial intent.
`ScreenPump` is currently supported and creates native VEGAS pan/crop
keyframes. Flash, shake, editorial speed change, transitions, title reveal,
cinematic transition, and color treatment remain modeled or planned and must
not be presented as effects that will render. Unsupported manual assignments
can remain in the proposal for explainability, but carry an unsupported status
and later produce an explicit skip diagnostic.

The preview is read-only with respect to VEGAS. It can report event time,
treatment, recipe/reason, intensity, duration, origin, and current support.
Final target-clip resolution happens after montage placement, so a previewed
action may still be skipped when it has no generated clip at its event time or
when it conflicts with custom pan/crop state.

When a reviewed `SongAnalysis` exists,
`AutomaticEffectTreatmentPlanner` produces an `EffectTreatmentPlan`. With the
legacy beat-grid fallback it currently produces an empty treatment plan.

The current default policy is `AutomaticEffectTreatmentPreset`. It is
deterministic for a fixed seed and event identity. It:

- carries supported manual assignments into the plan;
- combines stored suggestions with conservative event-type defaults;
- proposes screen pumps and speed changes at drops, screen pumps at build hits,
  sampled flashes at accents, sampled shakes at transients, and structural
  treatments at phrase boundaries;
- lets a manual treatment suppress automatic treatments in the same category;
- suppresses automation in unused regions; and
- applies per-category cooldowns, per-region density limits, repetition limits,
  region-density scaling, and bounded duration/intensity variation.

Every accepted action records event ID, time, type, intensity, duration,
origin, and reason. Every suppression records an `EffectTreatmentDiagnostic`.

After clips are placed, `PlacementAwareEffectTreatmentPlanner` augments that
general musical treatment plan with treatments that require knowledge of the
actual montage:

- every assigned reviewed kill receives an impact screen pump;
- a one- or two-beat pocket between consecutive kills can receive lighter
  screen pumps; and
- longer gaps remain untreated by this recipe for later editorial decisions.

This placement-aware pass guarantees that required kill pumps target generated
video rather than merely existing at an unrelated song timestamp.
This is a planning policy, not proof that VEGAS can render the treatment.

The reviewed Effects-stage configuration is an input to this planner. Disabled
automatic families produce no inferred actions, and the overall intensity
scales generated treatment strength within the bounds in `EDIT-FX-007`.
Reopening or changing the stage recompiles the proposal deterministically; it
does not reuse stale preview rows.

## 6. Presets and capability resolution

Current implementation:

- `AutomaticEffectTreatmentPreset` is an in-code policy object with tunable
  seed, spacing, density, repetition, duration, and region scaling.
- The planner compiles that policy into an explainable `EffectTreatmentPlan`.
- `MontageOrchestrator.RenderKind` maps modeled editorial uses to renderer
  kinds.
- `VegasEditorialEffectRenderer` reports success or a precise unsupported
  reason. Unsupported work is never claimed as rendered.
- `ScreenPump` is the first and currently only actual editorial visual
  renderer. It creates native pan/crop keyframes around the cue, and safely
  skips an event that already has custom pan/crop keyframes.
- Velocity envelopes are also real, but are the renderer for the montage speed
  plan rather than an implementation of the planned `SpeedChange` treatment.
- `Flash`, `Shake`, `CutOrTransition`, `CinematicTransition`, and
  `TitleReveal` are modeled and marked, but currently unsupported by the
  renderer. `SpeedChange` has no editorial renderer mapping.

Planned architecture:

- stable, versioned preset identity and persistence;
- built-in and user presets with overrides;
- explicit capability discovery for installed/native VEGAS effects;
- fallback chains resolved before mutation;
- parameter schemas, migrations, provenance, and regeneration ownership; and
- UI for choosing, inspecting, and overriding the compiled preset.

Those planned pieces are specified in
[effect-preset-architecture.md](effect-preset-architecture.md); they must not be
described as implemented until code, tests, and the normative rules agree.

## 7. The `PreparedMontage` boundary

`MontagePreparationService.Prepare` is the final domain-side coordinator. It
returns a `PreparedMontage` containing:

- `Placements`: source windows, timeline positions, mapped shot events, and
  speed profiles;
- `SongPlan`: reviewed-song or legacy-beat planning input;
- `Beats`: present for the legacy path;
- `SyncAssignments`: explainable kill-to-music allocation;
- `PlanningDiagnostics`: planner information, warnings, and errors; and
- `EffectTreatments`: actions plus suppression diagnostics.

`ShotReviewViewModel.BuildFromLibraryAsync` prepares this object on a worker
thread and logs the plan before sending `BuildMontageCommand` through
`VegasCommandClient`. `BuildMontageCommandHandler` runs on the VEGAS command
side. This command is the mutation boundary: domain planning must not depend on
live VEGAS objects.

## 8. Validation and failure behavior

Before timeline construction, `PreparedMontageValidator.ValidateAndNormalize`:

- requires a non-null request and an existing, decodable song;
- normalizes nullable collections for compatibility;
- requires at least one placement;
- requires every source clip to exist;
- requires a speed profile with at least two points;
- rejects non-finite, non-positive, negative, or overlapping placements; and
- validates the configured SFX catalog for every used gun.

Planning and validation failures are exceptions and stop the build before the
orchestrator starts mutation. During mutation, missing media/streams and VEGAS
API failures also surface as build failures. Timeline placement logs the
specific clip before rethrowing.

Editorial rendering is deliberately softer: no target clip, unsupported
capability, conflicting pan/crop state, or a rejected VEGAS effect is logged
and the effect remains represented by its marker. The rest of the montage
continues. This distinction prevents optional treatment capability from
invalidating an otherwise sound edit.

## 9. VEGAS timeline rendering

`BuildMontageCommandHandler` validates the payload, then calls
`MontageOrchestrator.BuildPreparedMontage` with effects enabled.

`TimelineBuilder`:

- matches project video properties to the first source clip when possible;
- creates the `AE|Montage Clips` video track;
- imports each source video;
- creates events at planned timeline starts and lengths; and
- applies planned source take offsets.

`MontageOrchestrator` then applies every placement's real velocity envelope and
passes treatment actions to `VegasEditorialEffectRenderer`. At present only
`ScreenPump` can return rendered success for an editorial visual action.

## 10. Audio

`MontageAudioBuilder` replaces source gameplay audio by creating generated
montage audio tracks:

- the selected song is placed from time zero on `AE|Montage Song` at volume
  `0.5`;
- confirmed kills select a gun SFX template by explicit template ID, then
  outcome, then configured fallback;
- SFX confirmation offsets align the template transient to the mapped kill;
- overlapping sounds are distributed across as many
  `AE|Montage Gun SFX N` tracks as needed, at volume `0.6`; and
- first, intermediate, final, and single kills receive different bounded tail
  and fade treatments.

No source gameplay audio event is created by `TimelineBuilder`.

## 11. Markers and logging

`TimelineBuilder.AddMontageMarkers` preserves the plan on the VEGAS timeline:

- `AE|SYNC` for assigned reviewed-song anchors;
- `AE|EFFECT` for effect-only song events;
- `AE|BEAT` for the legacy beat-grid path;
- `AE|EFFECT:<type>` for every planned treatment; and
- `AE|Hit`, `AE|Headshot`, and related shot outcomes.

Roles at the same time are merged into one `+`-joined label. Treatment markers
are added regardless of renderer support, making planned-but-unrendered intent
visible.

`ShotReviewViewModel` logs planning diagnostics, every kill-to-anchor
assignment, effect suppression diagnostics, and action count before mutation.
`MontageOrchestrator` logs effect targeting, rendered/skipped status and reason,
and final placement count. `Logger` is the shared logging owner.

## Ownership summary

| Stage | Primary owner(s) |
| --- | --- |
| Clip review and ready selection | `ShotReviewViewModel`, `ShotReviewWorkflow` |
| Clip metadata and reviewed events | `ClipParser`, `ClipSyncLibrary`, `ShotAnalysisSidecar` |
| Song detection, persistence, reconciliation | `SongStructureAnalyzer`, `SongAnalysisStore`, `SongAnalysisReconciler` |
| Planning-input selection/adaptation | `MontageSongPlanningInputProvider`, `SongAnalysisPlanningInputAdapter`, `BeatGridPlanningInputAdapter` |
| Ordering, allocation, retiming | `MontagePlanner` |
| Automatic effect policy | `AutomaticEffectTreatmentPreset`, `AutomaticEffectTreatmentPlanner` |
| Domain-side preparation | `MontagePreparationService`, `PreparedMontage` |
| Command boundary and validation | `BuildMontageCommandHandler`, `PreparedMontageValidator` |
| Video event creation | `TimelineBuilder` |
| Velocity rendering | `EffectsApplier` |
| Editorial effect rendering | `VegasEditorialEffectRenderer` |
| Audio generation | `MontageAudioBuilder` |
| End-to-end VEGAS mutation | `MontageOrchestrator` |
| Markers and operational diagnostics | `TimelineBuilder`, `Logger` |
