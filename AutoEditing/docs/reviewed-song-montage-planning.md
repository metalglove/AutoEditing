# Reviewed-song montage planning

Montage preparation completes before any VEGAS timeline mutation. It consumes a
VEGAS-independent `MontageSongPlanningInput`, so allocation and velocity
feasibility remain testable without COM or an open project.

## Source selection

If a song-analysis sidecar exists, its fingerprint and duration must match the
selected audio. A stale sidecar is an error and must be regenerated; reviewed
decisions are never silently discarded in favor of fresh beat detection.

If no sidecar exists, the compatibility adapter converts explicit
`BeatGrid.BeatTimesSeconds` into legacy gameplay anchors. Uniform BPM/phase
generation is used only for an older grid without explicit times. A
`legacy-beat-grid-fallback` diagnostic makes this path visible.

## Eligible anchors

Only reviewed events assigned `GameplayAnchor` can receive a confirmed kill.
Flash, pump, shake, and speed-only events remain in the prepared plan without
consuming gameplay capacity. `IntentionallyUnused` events and events in an
`Unused` region are excluded.

The planner exposes otherwise-unassigned, non-rejected detected events as
automatic suggestions instead of treating the song as having zero capacity.
Drops, build hits, accents, phrase boundaries, manual points, and downbeats
receive progressively higher suggested priority than ordinary beats. Explicit
gameplay assignments are always preferred when feasible; suggestions fill the
remaining capacity. Assign an event to another role or mark it intentionally
unused to keep it out of automatic gameplay allocation.

Effective time is the reviewed event time plus its timing offset. Offsets outside
the song are invalid. Moving a locked event across a locked region boundary is
also invalid. An editorial lock preserves classification and timing through
re-analysis; it does not bind a point to a particular kill because stable
shot-to-music binding IDs are not yet part of the domain.

## Allocation and diagnostics

The planner orders clips and kills deterministically, then uses global dynamic
programming over chronological eligible anchors. A transition is allowed only
when the velocity model can span it within configured bounds. Scoring prefers
editorial priority, intensity, accelerated cruise timing, low retiming
distortion, and stable ordering.

Normal gameplay is never retimed below `1.0x`. The configured sub-100% velocity
is reserved for the short post-kill dip; it is not a general fallback for sparse
music anchors. If a target would require stretching ordinary footage into slow
motion, the allocator must choose another target or report insufficient
capacity. A final post-kill tail returns to `1.0x` playback.

The result contains placements, explicit kill-to-music-event assignments, and
structured diagnostics. Insufficient or unreachable capacity stops before a
`BuildMontageCommand` mutates VEGAS. Successful profiles retain the existing
two-millisecond anchor verification.

The VEGAS marker pass renders assigned gameplay anchors and effect-only events,
not every detected beat.
