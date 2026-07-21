# VEGAS Pro Feasibility Matrix

## Purpose

This matrix separates API documentation, repository implementation, and observed
VEGAS Pro 20 behavior. “Documented” is not equivalent to “verified in this
application.” See [the existing API research](../vegas-scripting-effects-api.md)
for source notes and detailed examples.

| Capability | Evidence | Repository status | Required next proof | MVP |
|---|---|---|---|---|
| Create tracks, events, and takes | Documented | Implemented in `TimelineBuilder` | VEGAS Pro 20 timeline smoke test | P0 |
| Set event start and length | Documented | Implemented | Confirm project-frame rounding and final placement | P0 |
| Set `Take.Offset` | Documented | Implemented | Confirm source-window semantics on real clips | P0 |
| Create velocity envelope | Documented | Placeholder generation | Probe min/max/neutral and create a visible envelope | P0 |
| Add velocity envelope points | Documented | Not complete | Confirm curve names, point ordering, and duplicate-time behavior | P0 |
| Read/interpolate envelope values | Documented | Not integrated | Compare solver values with host behavior | P1 |
| Automatic event-length compensation | Documented as unavailable | Solver work exists | Confirm no host-side compensation occurs | P0 constraint |
| Group changes in `UndoBlock` | Documented by examples | Not established for full workflow | Confirm one undo restores the pre-treatment project | P0 |
| Stable custom ID on a timeline event | Unknown | Not implemented | Probe available metadata; assume unavailable until proven | P0 design risk |
| Project-adjacent metadata file | Application-controlled | Sidecar/library patterns exist | Save/reopen/regenerate experiment | P0 |
| Observe arbitrary timeline edits | Uncertain | Not implemented | Probe events and define manual refresh/repair fallback | P1 |
| Pan/Crop keyframes | Documented at broad level | Placeholder | Reflect exact VEGAS Pro 20 members and run zoom probe | Later |
| Procedural shake via Pan/Crop | Exact calls unverified | Placeholder | Runtime reflection and visual smoke test | Later |
| Add built-in OFX effects | Documented | Placeholder | Enumerate plugin IDs and parameters at runtime | Later |
| Solid-color flash generator | Exact ID unverified | Not implemented | Enumerate generator and parameter IDs | Later |
| Place and route SFX events | Broadly documented | Not implemented as treatment system | Track, bus, gain, and timing probe | Later |
| Render automation | Documented | Not implemented | Enumerate renderers/templates in host | Later |

## P0 probe script

Before implementing host mutation, add a small diagnostic mode that records:

1. VEGAS version and project frame rate.
2. Velocity envelope minimum, maximum, and neutral values.
3. Available `CurveType` values.
4. The result of creating an event with a known take offset, length, and three-
   point velocity envelope.
5. Envelope points read back from the event.
6. Source and timeline positions before and after one undo.
7. Any usable event/take identifiers before and after project save/reopen.

The probe should create its objects on clearly named temporary tracks and group
the action in one undo transaction. Cleanup should be explicit and must not
target unrelated user objects.

## Required VEGAS smoke-test fixture

Use a source clip with:

- known constant frame rate;
- visible frame numbers or another unambiguous frame-level reference;
- sufficient pre- and post-anchor handles;
- a manually recorded source anchor time;
- a simple forward-only velocity curve.

Verify:

- event project start;
- take offset;
- event duration;
- envelope point positions and values;
- visual source frame at the target music marker;
- behavior after project save/reopen;
- one-step undo;
- regeneration and removal without altering a neighboring event.

## Persistence strategy for the MVP

Use a project-adjacent, schema-versioned metadata file unless the probe reveals a
reliable native custom-metadata mechanism. Native marker names may aid diagnosis,
but names alone are not safe ownership identifiers because users can edit them.

Generated-object resolution should use multiple signals, for example:

- persisted treatment ID;
- normalized media identity;
- track role/name;
- approximate event position;
- take offset and event length;
- an optional generated marker label.

If resolution is ambiguous, stop and request repair selection. Never remove an
object merely because its name resembles a generated name.

## Feasibility gates

The MVP may proceed through pure domain work before VEGAS probing, but it must not
claim completion until these gates pass:

1. **Mapping gate:** solver integration matches the quantized VEGAS envelope
   within one project frame.
2. **Mutation gate:** generation creates native editable data without partial
   project changes on failure.
3. **Undo gate:** one user action can restore the previous state.
4. **Identity gate:** a treatment can be found safely after save/reopen or is
   reported as ambiguous without mutation.
5. **Lifecycle gate:** regeneration and removal affect only owned objects.

