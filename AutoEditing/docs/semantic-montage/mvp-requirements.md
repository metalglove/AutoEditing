# MVP Requirements

## Scope

The MVP is one thin, end-to-end workflow: verify a gameplay anchor, pair it with
a music marker, solve one velocity treatment, preview it, write it to VEGAS, and
regenerate or remove it.

Priorities use:

- **P0** — required to validate the product.
- **P1** — important after the P0 path is stable.
- **Later** — deliberately outside the MVP.

## User journey

1. Analyze a clip and song using the existing detectors.
2. Review candidate gameplay events and correct or create the primary anchor.
3. Review beat markers and select a target music event.
4. Create an alignment with an optional offset.
5. Select the initial velocity profile.
6. Preview the proposed event start, source window, envelope points, and warnings.
7. Commit the treatment as native VEGAS timeline data.
8. Reopen the treatment, adjust it, regenerate it, or remove it.

## Functional requirements

### P0 — event verification

**MVP-1 — Candidate preservation**  
The system shall preserve detector origin and confidence when a shot candidate is
shown for review.

Acceptance:

- A detected candidate remains distinguishable from a manually created event.
- Changing its time records that a user adjusted it.
- Verification does not discard its original detector confidence.

**MVP-2 — Primary gameplay anchor**  
The user shall be able to verify exactly one primary gameplay anchor for the
selected treatment.

Acceptance:

- The anchor is stored in source time with frame-accurate project display.
- The user can create, move, reclassify, or reject it.
- Commit is unavailable until a primary anchor exists.

The initial supported types are `Discharge`, `Hitmarker`, and `KillConfirm`.
Existing reviewed `ShotEvent` values may be adapted to these types during
migration.

### P0 — music selection and alignment

**MVP-3 — Selectable music event**  
The system shall expose detected beat-grid entries as selectable music events.

Acceptance:

- Each event has a stable ID, timeline time, type, origin, and confidence where
  applicable.
- The user can create or move a manual marker.
- The MVP may treat all detected entries as `Beat`; downbeat and section analysis
  are not required.

**MVP-4 — Explicit alignment**  
The user shall be able to align the verified gameplay anchor to the selected
music event with an optional signed offset.

Acceptance:

- The offset records both value and unit.
- A frame offset uses project frames; a time offset uses milliseconds.
- The calculated target timeline time is visible before generation.
- User-approved alignments can be locked against incidental reproposal.

### P0 — retiming

**MVP-5 — Versioned velocity profile**  
The system shall ship one versioned velocity profile with explicit control-point
semantics and operational limits.

Acceptance:

- Profile timing is defined relative to the anchor or event boundary in timeline
  time after solving.
- The profile declares a reference frame rate and conversion policy for any
  frame-based values.
- Preset defaults are distinguishable from VEGAS/API limits.

**MVP-6 — Retiming solution**  
The system shall solve event placement and source consumption before mutating the
VEGAS project.

Acceptance:

- The verified source anchor maps to the alignment target within half a project
  frame mathematically.
- The result contains event start, duration, source in/out, timeline envelope
  points, anchor mapping, and warnings.
- The solver rejects non-monotonic forward profiles in the MVP.
- The solver rejects mappings outside available source media and reports the
  missing pre- or post-handle duration.
- Solver behavior is unit-testable without VEGAS.

**MVP-7 — Preview**  
The UI shall show the proposed mapping before commit.

Acceptance:

- It shows source in/out, timeline start/end, target anchor, maximum/minimum
  velocity, and all blocking warnings.
- Commit is disabled for infeasible solutions.

### P0 — VEGAS generation and lifecycle

**MVP-8 — Native generation**  
The system shall create or update a VEGAS video event, take offset, length, and
velocity envelope from the approved solution.

Acceptance:

- The generated anchor lands within one project frame in a VEGAS Pro 20 smoke
  test.
- Source media is not modified.
- Generated envelope points remain editable in VEGAS.
- The operation uses one meaningful undo transaction if supported as expected by
  the host.

**MVP-9 — Provenance**  
The system shall persist enough data to explain and reproduce a treatment.

Acceptance:

- Stored data includes treatment ID, source clip identity, gameplay and music
  event IDs, alignment, preset ID/version, parameter overrides, generated object
  references, and tool schema version.
- The first implementation may use a project-adjacent metadata file.
- A missing or incompatible metadata file does not corrupt the VEGAS project.

**MVP-10 — Individual regeneration and removal**  
The user shall be able to regenerate or remove one generated treatment without
rebuilding unrelated events.

Acceptance:

- Regeneration preserves the locked alignment unless the user changes it.
- Removal targets only objects owned by the selected treatment.
- Diverged or missing generated objects produce a repair warning rather than
  deleting an ambiguous timeline object.

### P1 — workflow quality

**MVP-11 — Explain proposal**  
Show the selected anchors, required retiming, and any handle or speed warnings in
plain language.

**MVP-12 — Multiple verified events**  
Store secondary gameplay events even though the first velocity treatment uses one
primary anchor.

**MVP-13 — Basic deterministic QC**  
Report invalid source mappings, missing media, anchor deviation, duplicate owned
objects, and velocities outside configured limits.

## Non-functional requirements

**NFR-1 — Determinism.** The same inputs, settings, preset version, and project
frame rate shall produce the same mathematical solution.

**NFR-2 — Frame accuracy.** Event display and generated mappings shall define
their rounding policy and use the project timebase at the VEGAS boundary.

**NFR-3 — Recoverability.** Analysis, solve, persistence, or generation failure
shall not leave partially registered treatment metadata.

**NFR-4 — Responsiveness.** Frame stepping and marking must remain interactive;
long-running analysis must not block the VEGAS UI thread.

**NFR-5 — Compatibility visibility.** Unsupported VEGAS operations shall fail
with an actionable message and shall not silently downgrade the treatment.

**NFR-6 — Schema migration.** Persisted metadata shall carry a schema version and
reject unknown breaking versions safely.

## Deferred requirement groups

- music phrase, section, energy, and transient-importance analysis;
- automatic clip-to-section assignment and narrative escalation;
- visual impact profiles;
- sound enhancement and ducking;
- transition recommendation;
- optical flow;
- qualitative editorial QC;
- render automation.

## MVP exit criteria

The MVP is complete only after:

1. solver tests cover constant, linear-ramp, boundary, rounding, and insufficient-
   handle cases;
2. a real VEGAS Pro 20 smoke test confirms envelope creation and anchor accuracy;
3. one treatment survives save/reopen through the chosen metadata strategy;
4. that treatment can be regenerated and removed without changing an unrelated
   event;
5. the workflow is exercised on multiple real clips with recorded timing and
   correction results.

