# Domain and Retiming Model

## Time domains

The model must never use an unqualified `time` field. Retiming crosses several
coordinate systems:

- **Media source time** — position in the original media.
- **Clip source time** — position relative to a selected logical clip range.
- **Event-local timeline time** — position relative to the VEGAS event start.
- **Project timeline time** — absolute position in the VEGAS project.
- **Music source time** — position in the music file, which may differ from its
  project position.

Every stored time must declare its domain and unit. Conversion to project frames
occurs explicitly at the VEGAS adapter boundary.

## Core entities

### SourceClip

```text
SourceClip
- id
- mediaIdentity
- mediaPath
- mediaSourceIn
- mediaSourceOut
- sourceFrameRate
- resolution
- tags
- rating
- gameplayEvents[]
```

`mediaIdentity` must not rely only on a mutable path. The persistence design
should combine normalized path with stable media properties or a content-derived
fingerprint appropriate to project size.

### GameplayEvent

```text
GameplayEvent
- id
- sourceClipId
- type
- mediaSourceTime
- role
- origin
- detectionConfidence
- verificationState
- originalDetectedTime
- userNotes
```

Initial types:

```text
Discharge
Hitmarker
KillConfirm
```

Future types may include `ScopeStart`, `ScopeFull`, `TargetAcquired`,
`KillfeedUpdate`, `BoltCycle`, `Reload`, and `MovementExit`.

`detectionConfidence` describes the detector. It must not be overwritten with
`1.0` merely because the user verified the event. Verification is a separate
field.

### MusicEvent

```text
MusicEvent
- id
- musicSourceId
- projectTimelineTime
- type
- origin
- detectionConfidence
- strength
- verificationState
- locked
```

The MVP supports `Beat` and `Manual`. Later versions may add downbeats, phrases,
sections, fills, vocal accents, and ranked transients.

### Alignment

```text
Alignment
- id
- gameplayEventId
- musicEventId
- desiredOffset
- offsetUnit
- targetProjectTimelineTime
- approved
- locked
```

An alignment is editorial intent. It does not imply that a particular velocity
profile can realize the mapping.

### VelocityProfileDefinition

```text
VelocityProfileDefinition
- id
- version
- referenceFrameRate
- points[]
- minimumVelocity
- maximumVelocity
- conversionPolicy
- roundingPolicy
```

Each point must specify:

```text
VelocityProfilePointDefinition
- timelineReference: EventStart | PrimaryAnchor | EventEnd
- signedOffset
- offsetUnit
- velocity
- interpolationToNext
```

These are profile constraints, not final VEGAS envelope points. Offsets relative
to the primary anchor become event-local timeline positions only as part of a
solution.

The forward-only MVP requires `velocity > 0`. Freeze and reverse introduce
non-invertible or multi-valued mappings and are deferred until their semantics
are designed explicitly.

### RetimingSolution

```text
RetimingSolution
- id
- alignmentId
- profileId
- profileVersion
- projectFrameRate
- eventProjectStart
- eventTimelineDuration
- mediaSourceIn
- mediaSourceOut
- envelopePoints[]
- anchorMappings[]
- feasibility
- warnings[]
```

An envelope point is final event-local timeline data:

```text
SolvedEnvelopePoint
- eventLocalTimelineTime
- velocity
- curveType
```

An anchor mapping records the proof:

```text
AnchorMapping
- gameplayEventId
- mediaSourceTime
- eventLocalTimelineTime
- projectTimelineTime
- targetProjectTimelineTime
- errorSeconds
```

### GeneratedTreatment

```text
GeneratedTreatment
- id
- alignmentId
- solutionId
- presetId
- presetVersion
- parameterOverrides
- generatedObjectReferences[]
- metadataSchemaVersion
- toolVersion
- generationTimestamp
- ownershipState
```

`ownershipState` distinguishes objects that still match generated provenance from
objects that appear to have been edited or replaced by the user.

## Retiming mathematics

Let `t` be event-local timeline time, `v(t)` be velocity as a source-seconds per
timeline-second ratio, and `s(t)` be consumed source time:

```text
s(t) = sourceIn + integral from 0 to t of v(u) du
```

For a linear velocity segment from `(t0, v0)` to `(t1, v1)`, source consumption
is the trapezoid area:

```text
deltaSource = (t1 - t0) × (v0 + v1) / 2
```

The verified gameplay event at source time `sAnchor` must satisfy:

```text
eventProjectStart + tAnchor = targetProjectTimelineTime
s(tAnchor) = sAnchor
```

Therefore:

```text
sourceIn = sAnchor - integral from 0 to tAnchor of v(u) du
eventProjectStart = targetProjectTimelineTime - tAnchor
```

Once the event-local envelope shape and `tAnchor` are known, the solver can
derive the required source window and project placement. It must then verify:

```text
mediaStart <= sourceIn
sourceOut <= mediaEnd
all envelope points are within event duration
velocity remains within profile and VEGAS limits
mapping is monotonic for the forward-only MVP
```

VEGAS does not change `VideoEvent.Length` to compensate for a velocity envelope.
The solution must set event length and take offset deliberately.

## Frames and rounding

Frame-based preset offsets are interpreted at the preset's reference frame rate,
converted to duration, then quantized according to the profile policy. User-entered
project-frame offsets are interpreted directly against the project timebase.

The mathematical solver should retain high-precision seconds. Quantization occurs
when producing VEGAS `Timecode` values. After quantization, the adapter must
recalculate and report anchor error rather than assuming it remains zero.

## Solver phases

1. Validate the alignment and profile definition.
2. Convert profile offsets to precise durations.
3. Construct the event-local, forward-only velocity curve.
4. Determine `tAnchor`, event duration, source in, and source out.
5. Validate media bounds and configured speed limits.
6. Quantize the proposed VEGAS points to the project timebase.
7. Re-integrate the quantized curve and calculate final anchor error.
8. Return a solution or structured infeasibility result without changing VEGAS.

## Structured warnings

Warnings must be machine-readable and carry severity:

```text
InsufficientPreHandle
InsufficientPostHandle
VelocityBelowProfileLimit
VelocityAboveProfileLimit
EnvelopePointOutsideEvent
NonMonotonicMapping
AnchorQuantizationError
MissingMedia
UnsupportedCurveType
```

Only deterministic conditions belong in the MVP solver. Editorial judgments such
as “unreadable action” remain outside this contract.

## Migration from current types

- `ShotEvent` becomes a detector/review representation that can be adapted to
  `GameplayEvent`. Preserve its muzzle and confirmation times during migration.
- `BeatGrid` produces initial `MusicEvent` entries rather than being discarded.
- `TimelineShotEvent` becomes an `AnchorMapping` projection.
- `SpeedProfile` currently uses source-space points and provides useful mapping
  mathematics. Introduce the explicit profile-definition/solution separation
  before expanding its responsibilities.
- `ClipPlacement` may temporarily consume a `RetimingSolution`; it should not
  independently infer mappings once the solver is authoritative.

This staged approach keeps the analysis harness useful and avoids a repository-
wide rewrite.
