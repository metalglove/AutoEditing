# Effect preset architecture

This document defines the proposed first-class preset system for automatic and
manual visual treatment. It is a design contract, not a claim that the system is
implemented. The normative behavior remains in `editing-rules.md`; when this
design becomes production behavior, code, deterministic tests, and that
rulebook must change together.

## Goals and boundaries

A preset describes editorial intent independently from the VEGAS mechanism used
to render it. A build must be deterministic, capability-aware, explainable, and
safe to repeat. It must also remain useful when the user does not review the song
map manually.

The system has four deliberately separate layers:

1. **Preset source**: versioned JSON expressing policy and effect recipes.
2. **Resolved preset**: inheritance and user overrides compiled into one valid
   immutable definition.
3. **Effect treatment plan**: concrete actions selected for this song, seed,
   reviewed metadata, clip placement, and capability snapshot.
4. **VEGAS renderer**: translates each selected implementation into timeline
   mutations and records the actual result.

Do not put `ScriptPortal.Vegas` types, localized plugin names, or live plugin
objects in the first three layers.

## Identity, versions, and schema

Preset identity and document compatibility are different concerns:

- `id`: stable reverse-DNS-style identity, for example
  `autoediting.sniper.conservative`.
- `revision`: monotonic preset content revision, for example `3`. A built-in
  change increments this without changing the ID.
- `schemaVersion`: integer JSON contract version. This changes only when the
  serialized shape or interpretation changes.
- `displayName`: mutable UI label; never used as identity.

References use `id@revision`. A selection may pin an exact revision or request
`latest-compatible`. Every compiled plan records the exact resolved revision,
schema version, source hashes, compiler version, seed, and capability snapshot
ID. This makes an old plan reproducible even after a preset is edited.

Unknown fields should be preserved when round-tripping through a JSON DOM, but
unknown effect kinds, operators, or future schema versions must fail validation
with a useful message. Silently ignoring editorial behavior is unsafe.

Suggested envelope:

```json
{
  "schemaVersion": 1,
  "id": "autoediting.sniper.conservative",
  "revision": 1,
  "displayName": "Sniper — Conservative",
  "extends": ["autoediting.base.clean@1"],
  "seedPolicy": "song-and-preset",
  "policies": {},
  "recipes": {},
  "fallbackChains": {}
}
```

## Built-ins, user presets, and overrides

Ship immutable built-ins as embedded resources so a normal installation always
has known-good presets:

- `autoediting.none`: no visual effects; manual assignments still explain that
  they could not render under this selection.
- `autoediting.base.clean`: restrained shared defaults and safety limits.
- `autoediting.sniper.conservative`: sparse flash/pump, rare shake, structural
  accents at strong musical changes.
- `autoediting.sniper.punchy`: denser strong-hit treatment while retaining
  cooldowns and repetition limits.

Store user documents under `%LOCALAPPDATA%\AutoEditing\effect-presets\`. Never
edit an embedded built-in. “Customize” creates a user preset with a new user ID
that extends the built-in. A lightweight override document may retain the
built-in ID plus its pinned base revision only for application-local settings;
exported/shareable presets should have their own ID.

Resolution order is:

1. embedded built-in base(s);
2. inherited user base(s), in listed order;
3. selected preset;
4. project/song override;
5. build-session override.

Later layers win for scalar values. Maps merge by stable key. Lists do not merge
implicitly: each list field declares `replace`, `append`, or `removeById`.
Inheritance cycles, duplicate stable keys, missing pinned bases, and incompatible
schema versions are validation errors.

Composition should be explicit. For example, a “clean color” recipe pack can be
composed with a “sniper rhythm” policy pack, but two packs defining the same
recipe key require an explicit override. This prevents load-order surprises.

## Policy model

Policies select *whether and where* treatment occurs; recipes define *how* it
looks. A region policy is keyed by `MusicRegionType` and may override the default
policy. Relevant selectors include event type, editorial use, strength range,
downbeat/bar position, whether an event is also a gameplay anchor, kill ordinal,
multi-kill role, and distance to a region boundary.

Each policy category (`visualAccent`, `timing`, `structural`, `title`) defines:

- enabled effect recipe candidates and weighted selection;
- maximum actions per region and per minute;
- minimum seconds and optional minimum beats since the category last fired;
- per-recipe cooldown;
- maximum consecutive uses of the same recipe;
- exclusion windows around other categories;
- minimum event strength/priority;
- probability or deterministic score threshold;
- intensity and duration ranges;
- region density multiplier.

An explicit region entry inherits the preset default and overrides only named
fields. `Unused` is disabled by default. Suggested defaults are low density in
Intro, Outro, Cinematic, and Breakdown; moderate in BuildUp and Action; and
slightly higher in Climax. A region multiplier may raise density but must not
bypass absolute safety maxima.

Cooldown evaluation uses effective event time (including reviewed timing offset)
and operates in a stable order: effective time, event ID, then recipe ID.
Category and recipe cooldowns are evaluated before density quotas. Rejected
candidates remain in plan diagnostics.

## Parameters and deterministic variation

Recipe parameters use typed definitions rather than an unstructured dictionary:
`double`, `integer`, `boolean`, `choice`, `color`, `point2D`, and `duration`.
Numeric definitions contain allowed renderer bounds plus an editorial range:

```csharp
public sealed class NumericParameterRule
{
    public double Min { get; init; }
    public double Max { get; init; }
    public double? QuantizeStep { get; init; }
    public VariationDistribution Distribution { get; init; }
}
```

Validation rejects NaN/infinity, reversed ranges, values outside renderer-safe
bounds, and unknown choice values. Intensity is normalized to `[0,1]`; each
recipe maps it onto parameter curves. This lets the policy say “0.7 intensity”
without knowing an OFX parameter name.

Variation must not use process-global `Random` or enumeration order. Derive a
separate pseudorandom stream for each decision from a stable hash of:

```text
schemaVersion | presetId | revision | buildSeed | songFingerprint |
regionId | eventId | category | recipeId | decisionName
```

The build seed defaults to a stable song-and-preset seed, can be regenerated in
the UI, and is stored in the plan. Adding an unrelated event must not perturb
existing decisions. Quantize final values before serialization so .NET/runtime
differences do not create noisy plans.

## Manual precedence

Manual metadata remains authoritative and is not rewritten by automatic
planning. Precedence is:

1. `IntentionallyUnused` suppresses automatic treatment at that event.
2. A locked, user-chosen assignment is mandatory unless impossible or unsafe.
3. An unlocked user-chosen assignment is selected before automatic candidates.
4. A user-chosen assignment suppresses automatic choices in the same category
   at that event.
5. Suggested assignments join automatic candidates and remain subject to
   policy, cooldown, density, and capabilities.
6. Pure automatic inference has the lowest priority.

Manual treatment bypasses aesthetic density and repetition limits, but never
renderer safety limits, invalid timing bounds, or missing capabilities. It also
participates in later cooldown calculations so automatic effects do not crowd
it. If a manual recipe cannot render, resolve its declared fallback chain. If no
fallback is viable, emit a visible error diagnostic for locked assignments and a
warning for unlocked assignments; never silently substitute a different
editorial category.

An assignment should eventually support an optional recipe ID and parameter
overrides while keeping `EditorialUse` as its semantic role:

```csharp
public sealed class EditorialAssignment
{
    public EditorialUse Use { get; set; }
    public EditorialAssignmentOrigin Origin { get; set; }
    public string RecipeId { get; set; }       // optional
    public Dictionary<string, JsonToken> ParameterOverrides { get; set; }
}
```

## Capabilities and fallback chains

Capabilities are discovered, normalized, and snapshotted before planning. The
planner must not repeatedly query live VEGAS state. The snapshot contains:

- VEGAS product/version and scripting API version;
- project frame size, frame rate, pixel format, and stereoscopic mode;
- supported native mechanisms (event opacity/compositing, generated media,
  pan/crop keyframes, velocity envelopes, transitions);
- every video/audio plugin's stable class ID when available, provider, version,
  OFX/non-OFX status, normalized and display names;
- available presets by plugin;
- OFX parameter descriptors: stable key, type, animatable status, legal range,
  choices, and default;
- discovery timestamp and a canonical content hash (`SnapshotId`).

Localized display names are aliases, not stable identity. Recipe requirements
use stable IDs and parameter keys where VEGAS exposes them. Capability matching
returns `Supported`, `Degraded`, or `Unsupported`, with reasons.

A recipe contains ordered implementations and each implementation declares its
requirements. Example screen-pump chain:

1. native event pan/crop keyframes;
2. supported OFX transform plugin and required animatable parameters;
3. no-op with diagnostic.

Fallback chains belong to the recipe/preset, not hard-coded `catch` blocks.
Fallbacks must preserve the semantic role. A flash may fall back from a generated
white overlay to event opacity/compositing; it must not become shake. `no-op` is
always the terminal fallback and is recorded, not treated as rendered success.
Renderer failure after capability resolution may try the next implementation
only if the first mutation can be rolled back or rendering is staged before
timeline commit.

Snapshot persistence can live at
`%LOCALAPPDATA%\AutoEditing\capabilities\vegas-{major}-{hash}.json`. Refresh on
VEGAS version/plugin inventory change, on user request, or after a renderer
reports a stale capability. Plans embed or reference the exact snapshot and
retain the subset of capabilities used.

## Explainable compiled plan

`EffectTreatmentPlan` should be immutable after compilation and should contain
both selected and rejected decisions. A concrete API direction:

```csharp
public sealed record EffectTreatmentPlan(
    EffectPlanIdentity Identity,
    IReadOnlyList<CompiledEffectAction> Actions,
    IReadOnlyList<EffectDecision> Decisions,
    IReadOnlyList<EffectPlanDiagnostic> Diagnostics);

public sealed record CompiledEffectAction(
    string ActionId,
    string EventId,
    string RegionId,
    TimeSpan EffectiveTime,
    EditorialUse Role,
    string RecipeId,
    string ImplementationId,
    EffectTreatmentOrigin Origin,
    double Intensity,
    TimeSpan Duration,
    IReadOnlyDictionary<string, EffectParameterValue> Parameters,
    IReadOnlyList<string> CapabilityEvidence,
    EffectDecisionTrace Trace);

public interface IEffectPresetResolver
{
    ResolvedEffectPreset Resolve(EffectPresetReference reference,
        IReadOnlyList<EffectPresetPatch> overlays);
}

public interface IEffectTreatmentCompiler
{
    EffectTreatmentPlan Compile(EffectTreatmentContext context,
        ResolvedEffectPreset preset, VegasCapabilitySnapshot capabilities);
}

public interface IEffectTreatmentRenderer
{
    EffectRenderReport Render(EffectTreatmentPlan plan);
}
```

Each decision trace states candidate source, matched policy/rule IDs, sampled
values, quota/cooldown state, selected fallback, and concise human explanation.
Diagnostics use stable codes and severity. The plan sorts actions by effective
time, category, event ID, then action ID. `ActionId` is derived deterministically
from the decision identity.

Planning must be a pure VEGAS-free operation. Validation and compilation finish
before timeline cleanup or mutation. The renderer returns per-action status:
`Rendered`, `RenderedFallback`, `SkippedNoOp`, or `Failed`, plus the actual
implementation and changed timeline objects.

## Persistence and migration

Keep preset files separate from `appsettings.json`; presets are documents, not
flat application settings. User preferences store only the selected reference,
seed policy, and last editor state. Song/project state may store a pinned preset
reference and a sparse override.

Use an explicit migration pipeline:

```csharp
public interface IEffectPresetMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    JObject Migrate(JObject source, MigrationLog log);
}
```

Load into a JSON DOM, validate the envelope, apply one-version migrations in
sequence, validate against the target schema, then deserialize. Save a backup
before replacing a user file, write to a temporary file, and atomically replace.
Built-ins are never migrated on disk. Unsupported future versions open
read-only with an export option.

Migration tests use committed golden fixtures for every supported version and
verify idempotence, semantic equivalence, unknown-field preservation, and useful
failure messages. A compiled plan records migration notices so an unexpected
change is explainable.

## UI workflow

The normal Build Montage path should need no song-map review:

1. Choose preset (`Sniper — Conservative` by default), intensity/density summary,
   and deterministic variation seed.
2. Show capability status: full, degraded with named fallbacks, or unavailable.
3. “Preview treatment plan” shows timeline markers/cards without rendering.
4. Each card explains why it was selected or omitted and permits disable,
   semantic-role change, recipe choice, intensity, and lock.
5. “Customize preset” creates a user-derived preset; song-only edits remain a
   sparse project override.
6. Build compiles again from the saved inputs, compares plan hash to preview,
   and displays any capability changes before mutation.

The preset editor should expose friendly density/cooldown controls first and an
advanced recipe/parameter editor second. Include Validate, Reset inherited
value, Duplicate, Import, Export, and Restore built-in revision. Never expose
localized plugin/preset strings as the only identifier.

## Deterministic test strategy

VEGAS-free unit tests:

- schema validation, inheritance order, list operators, cycles, and missing
  bases;
- migrations and golden JSON round trips;
- stable variation vectors and independence from input enumeration;
- every region override, density quota, category/recipe cooldown, exclusion
  window, and repetition rule;
- manual/locked/intentionally-unused precedence;
- candidate tie-breaking and stable action IDs;
- exact decision traces and stable diagnostic codes;
- capability matching and each fallback branch;
- serialization and plan hash stability.

Property tests should assert intensity/range bounds, no automatic action in
`Unused`, no quota/cooldown violations, and identical plans for shuffled inputs.

Adapter contract tests use synthetic capability snapshots and a fake renderer.
VEGAS integration smoke tests run once per supported major version against a
small fixture project, inventory plugins, render one action per implementation,
and verify keyframes/effects/presets plus rollback behavior. Missing optional
plugins must produce a fallback/no-op report, not a failing test installation.

Snapshot regression fixtures should be sanitized and committed for representative
VEGAS versions/locales. UI tests cover default build without map review,
degraded-capability warnings, create-derived-preset, project overrides, and plan
explanations.

## Incremental implementation

1. **Contracts and fixtures**: introduce schema v1 DTOs, built-in resource
   catalog, validation, preset reference, stable hashing, and JSON fixtures.
   Preserve current automatic behavior behind a compatibility built-in.
2. **Resolution**: implement inheritance/composition, user storage, sparse
   overlays, migration pipeline, and immutable `ResolvedEffectPreset`.
3. **Pure compiler**: replace policy embedded in
   `AutomaticEffectTreatmentPreset`/planner with rule IDs, deterministic streams,
   precedence, quotas, and full decision traces. Keep output VEGAS-free.
4. **Capability inventory**: implement and persist
   `VegasCapabilitySnapshot`; add inspection UI and refresh/staleness behavior.
5. **Recipe resolution**: compile semantic actions to concrete implementation
   and parameter values using requirements and fallback chains.
6. **Renderers**: implement the safest native mechanisms first (opacity overlay,
   pan/crop pump, velocity where authorized), with staged mutation and action
   reports; add OFX renderers only from verified parameter descriptors.
7. **Workflow UI**: preset picker, preview/explanation, derived preset editor,
   song overrides, and capability warnings.
8. **Rollout**: make conservative automatic treatment the default only after
   deterministic and VEGAS smoke coverage. Retain `autoediting.none` and a
   preference to disable treatment.

At each behavior-changing step, update production code, deterministic tests, and
`editing-rules.md` in the same change. Do not mark a recipe implemented until a
VEGAS integration fixture confirms that it creates the intended timeline state.
