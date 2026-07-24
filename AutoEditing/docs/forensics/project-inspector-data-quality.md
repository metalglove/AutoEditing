# VEGAS project inspector data-quality audit

## Scope

This audit treats the two inspector exports as data products rather than as
editorial evidence:

- `C:\VEGAS\project-inspection.json` (schema/inspector 1.0)
- `C:\VEGAS\project-inspection-v2.json` (schema/inspector 2.0)
- their corresponding raw diagnostic logs

No inspector source was present under `C:\VEGAS`, so implementation advice
below is inferred from the output contract and ScriptPortal behavior. The v2
file was generated while this audit was in progress and is included because it
fixes some important v1 defects.

## Executive assessment

The v2 export is useful for exploratory forensics, especially OFX recipe
discovery. It is not yet suitable as a lossless interchange format or an
automated oracle.

| Area | v1 | v2 | Assessment |
|---|---:|---:|---|
| Events | 445 | 445 | Structurally complete |
| Unique `inspectorId` values | 445 | 445 | Reliable within one export |
| Unique serialized native event IDs | **3** | 445 | v1 irreversibly loses identity; v2 fixes it |
| OFX parameter records | 46,924 | 46,924 | Very large and highly duplicated |
| Animated OFX parameters | 0 | 1,028 | v2 fixes animation detection |
| OFX keyframes | 0 | 2,525 | v2 extracts useful typed values |
| Event-effect keys outside event span | n/a | **686** | Time semantics must not be trusted yet |
| Non-OFX audio-effect parameters | 0 | 0 | Pitch/reverb and track processing remain opaque |
| Contextual diagnostics | 0 | 0 | 259 failures cannot be attributed |
| Reported unavailable/invalid plug-ins | 0 | 0 | Cannot prove that no dependencies are missing |

The most urgent work is not adding more fields. It is making every value
traceable to a scope, time basis, read status, and validation result.

## Confirmed defects and risks

### DQ-01: v1 corrupts native event identity

The v1 file serializes `EventID` as a JSON number. Values such as
`3458764513820545723` exceed JavaScript's safe integer limit
(`9,007,199,254,740,991`). Across 445 events, only three distinct parsed values
remain. Any join, grouping, or reference based on v1 `eventID` is invalid.

V2 correctly serializes IDs as decimal strings and has 445 unique values.
`groupEventIDs` are strings as well. This policy must apply to every VEGAS,
media, take, group, marker, plug-in, and native COM identifier regardless of
its apparent size.

### DQ-02: OFX keyframe time basis is asserted, not established

V2 calls each key time `eventRelativeOrRawSeconds` and derives
`assumedTimelineAbsoluteSeconds`. This correctly signals uncertainty, but
downstream analysis can easily mistake the derived value for fact.

Of 2,525 event-level OFX keyframes:

- 25 have negative raw time;
- 661 occur after their containing event's visible length;
- 686 total (27.2%) therefore fall outside the event span.

Examples include a 0.45-second event with an Amplitude recovery key at 1.1011
seconds. These may be valid keyframes retained outside trimmed event bounds,
media-relative keys, or a VEGAS/OFX time convention. They are not necessarily
bad project data. They do show that `event.start + rawTime` is not automatically
an *effective visible timeline time*.

V2 should emit separate concepts:

- `rawTime` and `rawTimeUnit`;
- `timeBasis`: `event`, `media`, `project`, or `unknown`;
- `derivedTimelineTime`, only when the basis is known;
- `isWithinVisibleEvent`;
- `isEffectiveOnTimeline`;
- `derivationMethod` and `derivationConfidence`.

For trimmed keys, retain them but do not count them as visible treatment
evidence until evaluated at the event boundary or confirmed by a render.

### DQ-03: diagnostic messages discard all context

Both exports contain 259 identical failures:

`SafeStr failed: Error HRESULT E_FAIL has been returned from a call to a COM component.`

There is no property name, object scope, track/event/effect identity, native
type, HRESULT field, or stack location. The pattern strongly matches one
failing string read per video event or similar repeated object property, but
that cannot be proven from the log. All five track names and all 445 event names
are null, yet there are only 259 failures, so the message cannot safely be
assigned to all null names.

Diagnostics must be structured records embedded in (or shipped alongside) the
export:

```json
{
  "severity": "warning",
  "code": "COM_PROPERTY_READ_FAILED",
  "scope": "event",
  "inspectorId": "t1_e42",
  "propertyPath": "event.Name",
  "nativeType": "ScriptPortal.Vegas.VideoEvent",
  "hresult": "0x80004005",
  "exceptionType": "COMException",
  "message": "Error HRESULT E_FAIL ...",
  "fallbackUsed": null
}
```

Record a per-field state of `read`, `notApplicable`, `unsupported`,
`unavailableDependency`, or `readError`. A null alone is ambiguous.

### DQ-04: media state changed between runs without enough provenance

The v1 diagnostic reports many sources at their original `E:\Fiverr\...` paths
as offline, logs a relink to `C:\VEGAS\...`, then immediately reports
`RefreshNeeded()` failure and “STILL OFFLINE.” Nevertheless, v1 JSON says every
media/take is online. V2 opens `Untitled.relinked.veg`, reports
`VerifyMedia: total=51 offline=0`, and also reports every source online.

This is consistent with relinking being saved and becoming effective after
reopen, but the exports do not establish that chain. The inspection itself also
mutated project state in v1 (`isModified: true`), whereas v2 inspected a saved
copy (`isModified: false`).

For each media object v2 should retain:

- `originalProjectPath`;
- `resolvedPath`;
- `pathKind`: filesystem, generated, subclip, nested project, or other virtual;
- `existsOnDisk` where applicable;
- `mediaOffline`, `videoStreamOffline`, and `audioStreamOffline`;
- `decodeProbe`: not-run, success, or failure;
- relink attempt/result and save/reopen generation.

One distinct take path looks like a filename but is actually a virtual reversed
subclip (`...mp4 - subclip 1 (reversed)`). A plain filesystem existence test
would incorrectly call it missing. Virtual media must be identified before path
validation.

### DQ-05: absent plug-ins cannot be represented

All 1,264 enumerated effects report valid and available, despite the known
incomplete plug-in environment. This does **not** establish that the project has
no missing effects. VEGAS may omit unresolved effects from the normal chain,
surface a placeholder through another API, or load only the effects that remain
installed.

The inspector needs two complementary inventories:

1. Enumerated live effect objects, as today.
2. Project dependency evidence: unavailable/placeholder nodes if exposed,
   plug-in registry inventory, load/open warnings, and project-generated missing
   plug-in notices.

Use `presenceStatus`: `loaded`, `disabled`, `invalid`, `placeholder`,
`referencedButUnavailable`, or `unknown`. Never map “not enumerated” to “not
used.”

### DQ-06: audio processing remains metadata-only

V2 successfully identifies 125 Pitch Shift and 125 VST2 eFX_Reverb event
instances, plus Noise Gate, EQ, and Compressor on each of three audio tracks.
It exports no parameter values, automation, program/bank data, or opaque state
for any non-OFX audio effect. Consequently, claims about pitch, reverb, EQ, or
dynamics settings cannot be derived from this export.

For VST/DirectX/native audio effects, try in order:

1. supported typed parameter/program APIs;
2. preset/program name and current program index;
3. parameter-index/name/display/value tuples;
4. serialized opaque plug-in state with a hash (and blob only in a separate
   binary sidecar if legally and technically safe);
5. explicit `parameterReadStatus: unsupported`.

Track and bus gain also need a complete signal path. Current track `volume`
values alone do not establish perceived attenuation.

### DQ-07: “equals default” appears semantically unreliable

Sample keys with value `0` and parameter default `0` are exported with
`equalsDefault: false`. This may be an inspector comparison bug, an inaccessible
default, a type/coercion issue, or a plug-in-specific default. Export
`defaultReadStatus`, `defaultValue`, and comparison tolerance, then calculate
equality downstream. Do not serialize a confident Boolean if either operand was
not read successfully.

### DQ-08: the schema duplicates static OFX metadata excessively

The 25.8 MB v2 export repeats full definitions for 46,924 parameter instances.
For example, S_Shake appears on 276 events and contributes 20,976 parameter
records. This increases diff noise, parse cost, and the chance that static
metadata diverges between instances.

Normalize into:

- `pluginDefinitions`;
- `parameterDefinitions`, keyed by plug-in ID and parameter stable ID;
- `effectInstances`;
- `parameterStates` containing only value/animation overrides;
- `keyframes`.

Preserve chain order and instance identity. This should make exports
deterministically diffable without discarding information.

## Exact v2/v3 inspector changes

1. Serialize all native IDs as strings; use `inspectorId` as the export-local
   primary key and explicit foreign keys for relationships.
2. Add `exportId`, UTC timestamp, project file hash, project save generation,
   inspector source/version hash, locale, and a capabilities matrix.
3. Replace generic safe getters with `TryRead(scope, id, propertyPath, getter)`
   returning both value and structured read status.
4. Preserve raw time values and name the API type that produced them. Derive
   timeline positions only through a tested per-scope conversion.
5. Mark whether each key is visible/effective in the trimmed event.
6. Export effect scope (`media`, `event`, `track`, `bus`, `output`) and a stable
   effect instance ID separately from chain index.
7. Inventory unavailable dependencies separately from live effect enumeration.
8. Add typed non-OFX audio parameter extraction or an explicit unsupported
   record.
9. Distinguish filesystem media from subclips/generated/virtual media before
   checking existence.
10. Normalize static plug-in/parameter definitions and sort all collections by
    stable deterministic keys.
11. Add top-level `completeness` counters: attempted/read/unsupported/error for
    every object and field family.
12. Never relink during the inspection pass. Make relinking an explicit prior
    phase against a disposable copy, save, close, reopen, then inspect.

## Required validation tests

### Schema and identity

- Export 445+ synthetic events whose IDs exceed `2^53`; parse in .NET,
  JavaScript, and Python and assert exact round-trip string equality.
- Assert uniqueness and referential integrity for inspector IDs, event IDs,
  group IDs, effect instance IDs, parameter IDs, media IDs, and take references.
- Validate against a checked-in JSON Schema; reject unknown schema major
  versions.
- Export the same unchanged project twice and require semantic equality after
  removing run metadata.

### Time-basis fixtures

Create a disposable event that starts at 10 seconds, is trimmed at both ends,
and has OFX keys:

- before the visible event;
- exactly at event start;
- inside the event;
- exactly at event end;
- after the visible event.

Assert raw time, declared basis, derived timeline time, and visible/effective
flags against VEGAS UI observations. Repeat for event, track, and media FX. Save,
close, reopen, and assert persistence.

### COM diagnostics

- Force one known failing property read and assert that the diagnostic contains
  its property path and object ID.
- Assert that every null/omitted inspected field has a read-status reason.
- Group diagnostic counts by code/property and fail the inspector test if an
  unclassified generic `SafeStr failed` message remains.

### Media lifecycle

- Open a fixture with online, offline, relinked, generated, subclip, reversed
  subclip, image-sequence, and audio-only media.
- Inspect before relink; relink a copy; save; close; reopen; inspect again.
- Assert original path, resolved path, path kind, file existence, stream state,
  and decode probe independently.

### Plug-in availability

- Create one native effect, one installed OFX effect, and one effect whose
  plug-in is then removed/disabled for the test environment.
- Assert the missing dependency remains represented with a non-loaded status.
- Verify that the inspector distinguishes bypassed, disabled, invalid, missing,
  and successfully loaded effects.

### OFX and audio parameters

- Use Boolean, integer, double, 2D, color, choice, string, and custom OFX
  parameters, with animated and static cases.
- Assert typed values, interpolation, defaults, min/max finiteness, and
  keyframe order.
- Create known Pitch Shift/Reverb and track EQ/compressor settings; either assert
  exact exported values or assert a precise unsupported capability record.

### Visual oracle

For at least one pump recipe, compare:

1. structural keyframe assertions;
2. save/reopen structural equality;
3. frames rendered before, at, and after the predicted peak.

This is the only reliable way to establish the correct OFX time basis when API
semantics or trimmed out-of-range keys remain ambiguous.

## Safe downstream-use rules

Until these issues are fixed:

- use only v2 string `eventID` or `inspectorId` for joins;
- treat `assumedTimelineAbsoluteSeconds` as an unverified hypothesis;
- filter or separately label keys outside visible event spans;
- treat null names as unknown, not blank;
- do not infer absence of missing plug-ins from the all-available inventory;
- do not infer pitch, reverb, EQ, gate, or compressor settings;
- treat v2 media as successfully resolved after reopen, but preserve the v1
  relink failure history in any provenance statement.
