# Sniper montage effects: VEGAS 20 capability and editorial research

Research date: 2026-07-23.

This document informs effect planning and preset design. It does **not** claim
that an effect is implemented by AutoEditing. `editing-rules.md` remains the
normative statement of implemented editing behavior.

## Evidence labels

- **Verified (official/API):** stated by VEGAS or the official scripting API.
- **Verified (project):** observed in this repository's code or existing
  research, but not necessarily smoke-tested inside VEGAS Pro 20.
- **Verified (upstream):** stated by the effect project's owner.
- **Inference:** an editorial or engineering recommendation that still needs a
  VEGAS 20 visual/smoke test.
- **Unverified compatibility:** the format or vendor suggests compatibility,
  but the exact Windows binary has not been proven in this VEGAS 20 install.

## Executive recommendation

Build the first automatic treatment library from native, reversible timeline
operations:

1. Pan/Crop punch-in (screen pump) at an important kill.
2. A 1–2 frame white/color overlay flash for a stronger kill or drop.
3. The existing velocity treatment.
4. A very short native dissolve, fade, or hard cut at structural boundaries.
5. Optional native blur/glow only after runtime capability discovery.
6. Procedural shake via Pan/Crop keyframes after the punch-in path is proven.

This stack expresses most of the recognizable sniper-montage vocabulary without
requiring a plug-in. Third-party OFX should be an optional enhancement selected
only after discovery and a host smoke test. A preset must always have a native
fallback and ultimately a no-effect fallback.

## Ranked effect candidates

Scores are relative: 5 is best. Dependency risk is scored inversely, so 5 means
low risk.

| Rank | Treatment | Montage value | Automation feasibility | Low dependency risk | Native fallback | Recommendation |
|---:|---|---:|---:|---:|---:|---|
| 1 | Pan/Crop punch-in / screen pump | 5 | 5 | 5 | 5 | First visual effect to ship |
| 2 | Generated overlay flash / impact frame | 5 | 5 | 5 | 5 | Ship with strict frequency limits |
| 3 | Velocity ramp and kill dip | 5 | 5 | 5 | 5 | Retain as the timing foundation |
| 4 | Hard cut / short dissolve | 4 | 5 | 5 | 5 | Default transition vocabulary |
| 5 | Pan/Crop micro-shake | 4 | 4 | 5 | 5 | Add after stable Pan/Crop probing |
| 6 | Native Gaussian/linear/radial blur pulse | 4 | 3 | 5 | 4 | Capability-gated; useful on movement |
| 7 | Native glow / brightness pulse | 3 | 3 | 5 | 5 | Prefer flash overlay when unavailable |
| 8 | RGB split / chromatic aberration | 3 | 2 | 2 | 3 | Optional signature accent, not baseline |
| 9 | GL Transition preset | 3 | 3 | 4 | 5 | Structural cuts only, never every kill |
| 10 | Large whip/spin/distortion transition | 2 | 2 | 3 | 5 | Rare, high-energy boundary treatment |

### Why punch-in and flash rank first

A punch-in preserves the player's aim and the kill frame while making the
impact spatially legible. It can be produced with event Pan/Crop keyframes and
does not depend on a named OFX parameter. A flash can be produced as a short
generated solid event composited above gameplay; it likewise avoids plug-in
identity and parameter instability. Both remain editable as ordinary VEGAS
timeline data. **Inference:** these are safer automatic defaults than blur,
shake, or chromatic effects because they are brief, easy to understand, and
easy to remove.

## Standard VEGAS facilities and likely useful effects

### Facilities confirmed by official documentation

- VEGAS effects, transitions, media generators, Pan/Crop, and Track Motion can
  all be keyframed; intermediate values are interpolated. The UI exposes
  Linear, Fast, Slow, Smooth, Sharp, and Hold curves
  ([VEGAS keyframe animation](https://help.magix-hub.com/video/vegas/en/content/topics/keyframes.htm)).
- Transitions operate at event edges or overlaps. VEGAS documents transition
  presets and, from VEGAS Pro 20, an animatable Transition Progress envelope
  ([VEGAS transitions](https://help.magix-hub.com/video/vegas/en/content/topics/creatingtransitions.htm)).
- The same official transition page documents the built-in **GL Transition**
  integration, around 50 presets, editable GLSL, and an initial-release
  GPU/OpenCL interoperability warning. That makes GL Transition valuable but
  not dependable enough to be the only renderer for a preset.
- Official VEGAS help explicitly names the **Vegas Gaussian Blur** plug-in
  ([screen-capture workflow](https://help.magix-hub.com/video/vegas/en/content/topics/faq/faq_usingscreencaptures.htm)).

### Built-in names: discovery is required

The existing repository research identifies useful shelf names including
Gaussian Blur, Linear Blur, Radial Blur, Glow, Brightness and Contrast, Color
Curves, Black and White, Lens Flare, TV Simulator, LUT Filter, and Titles &
Text. Some names are version-, package-, or locale-sensitive. Treat these as
**candidate display names**, not durable identifiers.

At runtime, recursively enumerate `Vegas.VideoFX`, `Vegas.Transitions`, and
`Vegas.Generators`, recording at least:

- `PlugInNode.Name`, `Group`, `UniqueID`, `IsDisabled`, `IsAutomatable`,
  `IsOFX`;
- every `PlugInNode.Presets` entry by name;
- after creating a disposable effect instance, `Effect.IsOFX`, its presets,
  and each OFX parameter's name, type, animation support, and range where
  exposed.

The official API defines `PlugInNode` as a hierarchy with `Name`, `Group`,
`UniqueID`, `IsDisabled`, `IsAutomatable`, `IsOFX`, and `Presets`. It marks
class IDs deprecated in favor of `UniqueID`
([VEGAS scripting API: PlugInNode](https://help.magix-hub.com/video/vegas/22/en/content/topics/external/vegasscriptapi.html)).
Therefore a stored capability should resolve by unique ID first, then an
explicit alias list—not by a single English display name.

### ScriptPortal automation feasibility

**Verified (official/API):**

- `Effects.AddEffect(PlugInNode)` creates an effect in an effects collection.
- `Effect.Presets` enumerates available presets; `Effect.Preset` assigns the
  first keyframe's preset by name; `Effect.CurrentPreset` accepts an
  `EffectPreset`.
- `Effect.IsOFX` and `Effect.OFXEffect` expose the OFX instance.
- `OFXEffect.Parameters` and `FindParameterByName` expose parameters.
- Typed OFX parameters such as `OFXDoubleParameter` support
  `SetValueAtTime(Timecode, value[, interpolation])`.

These members are in the
[official VEGAS scripting API summary](https://help.magix-hub.com/video/vegas/22/en/content/topics/external/vegasscriptapi.html).

**Engineering inference:** preset-by-name is useful for user-authored VEGAS
presets, but is not a sufficient portable contract. Names can change, presets
can be absent, and parameter schemas can differ across versions. The robust
resolution order is:

1. required unique plug-in ID plus parameter schema/version match;
2. unique plug-in ID plus a discovered VEGAS preset;
3. known aliases plus verified parameter mapping;
4. native procedural fallback;
5. no visual effect, with a structured diagnostic.

Never guess an OFX parameter by ordinal position. Cache the discovery result by
VEGAS version, plug-in unique ID, and plug-in version/info, then invalidate it
when that fingerprint changes.

## Open-source and permissively licensed options

### GL Transitions — best external creative source, native VEGAS integration

Status: **Verified (official VEGAS) as integrated; individual preset behavior
still needs a VEGAS 20 probe.**

VEGAS officially describes GL Transitions as an open-source initiative built
into VEGAS, with presets and custom GLSL. It avoids installing an arbitrary OFX
binary and is the best source of optional modern transitions. Risks are GPU
interoperability, shader/preset availability, and visually excessive presets.
Use only at phrase/region boundaries and fall back to a dissolve or hard cut.
See [VEGAS transitions](https://help.magix-hub.com/video/vegas/en/content/topics/creatingtransitions.htm)
and the [GL Transitions project](https://gl-transitions.com/).

### Natron openfx-misc

Status: **Verified (upstream) open source and Windows-buildable;
unverified compatibility with VEGAS Pro 20.**

The project is GPL-2.0-or-later and contains useful primitives including
`FrameBlendOFX`, `TimeBlurOFX`, `ColorCorrectOFX`, `GradeOFX`, channel
operations, transforms, blurs, and generators. Upstream says it is primarily
developed for Natron, may work with other OFX hosts, and offers VS2017 Windows
artifacts via AppVeyor rather than an official versioned source release
([openfx-misc README](https://github.com/NatronGitHub/openfx-misc)).

Risk is high for product distribution: no stable release train, artifacts come
from CI or Natron bundles, many effects assume Natron host features, and GPL
obligations must be reviewed before redistributing binaries. Installing the
bundle does not prove VEGAS compatibility. Recommendation: optional developer
experiment only; do not make it a default user dependency.

### Natron openfx-gmic

Status: **Verified (upstream) as part of Natron's Windows distribution;
unverified compatibility with VEGAS Pro 20.**

Natron states that its Windows distribution includes openfx-gmic alongside
openfx-misc and other bundles
([Natron repository](https://github.com/NatronGitHub/Natron)).
G'MIC offers many stylized filters, but the large surface, GPL ecosystem,
render cost, parameter instability, and lack of a clear VEGAS support statement
make it a poor automation dependency. Recommendation: exclude from the initial
product preset catalog.

### Effects Town

Status: **Verified (upstream) permissive source and Windows x86-64 OFX;
unverified compatibility with VEGAS Pro 20.**

Effects Town describes its plug-ins as permissively open source, Windows
x86-64, and intended for OpenFX hosts. Its current effects (electric-field
diagrams, watercolor texture, Blender Filmic helper) are not particularly
useful for sniper impacts
([Effects Town](https://www.effects.town/)). It is evidence that permissive OFX
projects exist, but not a recommended montage dependency today.

### The OpenFX SDK itself

The OpenFX standard and sample plug-ins are open source and explicitly describe
VEGAS as an OFX host
([Academy Software Foundation OpenFX](https://github.com/AcademySoftwareFoundation/openfx)).
This verifies a shared interface, **not** universal host compatibility. If a
custom effect becomes necessary, a tiny purpose-built OFX with a stable
parameter schema is more controllable than depending on an unversioned pack,
but it creates a C++ build, signing, packaging, GPU/CPU, color-space, and crash
surface. It should come only after native effects prove insufficient.

### Options deliberately rejected

- Frei0r is open source and cross-platform, but it uses a different plug-in API;
  VEGAS does not natively host Frei0r. No direct automation path.
- Old HitFilm Ignite Express downloads, abandoned binaries, and “free” editions
  without source are not genuinely open-source dependencies.
- Paid suites such as Sapphire, Continuum, NewBlue, Universe, and RSMB may be
  excellent artist tools, but cannot be an assumed default and need
  license/capability-specific adapters.

## Editorial vocabulary for sniper montages

The external guidance is consistent on the basics: sync shots to musical beats,
use velocity/speed ramps for flow, and use transitions and grading selectively
([VideoProc gaming montage guide](https://www.videoproc.com/video-editor/how-to-make-a-gaming-montage.htm)).
The exact treatment rules below are **editorial inference**, intended for visual
testing rather than claims of universal taste.

### Screen pump / punch-in

Use a rapid scale-in centered near the reticle or kill subject, then a slightly
slower return. Keep the displacement small enough that HUD and target remain
readable. A stronger variant can overshoot once. Avoid applying it to every
shot in a multi-kill sequence; use the first, last, or musically strongest kill.

Suggested starting shape at 60 fps:

- 2–3 frames before impact: 100%;
- impact: 106–112%;
- 3–6 frames after: 102–104%;
- 8–12 frames after: 100%.

Scale those durations in musical time and clamp to available event handles.

### Flash / impact frame

Use a white or grade-tinted solid overlay at impact. Prefer a 1-frame strong
flash or 2-frame quick decay. Reserve a full-white frame for a drop, final kill,
or unusually strong transient. A lower-opacity warm/cool flash can distinguish
song palettes. Never cover information needed to understand the kill.

### Shake

Use 2–4 alternating Pan/Crop translations with decaying magnitude; optionally
add tiny rotation. Keep motion under a few percent of frame width and guarantee
coverage by scaling in. Shake belongs on rare high-energy impacts, explosions,
or the closing kill—not routine body shots. The native fallback is punch-in
without translation.

### Blur

Directional/radial blur works best during fast approach, whip motion, or a
structural transition. Reduce blur at the kill frame so the shot reads. A
one-sided “blur into the cut” is safer than blurring the entire impact. Fall
back to ordinary crossfade/punch-in when the built-in plug-in or parameter map
is unavailable.

### Glow / brightness

Pulse briefly on luminous footage or a musical accent. Glow plus flash easily
clips highlights, so presets should choose one dominant luminance effect.
Fallback is a low-opacity generated overlay.

### RGB split / chromatic aberration

Use as a signature micro-accent for glitches, bass hits, or an exceptional kill,
not as persistent grading. A native implementation could duplicate the event
into color-isolated layers with 1–3 pixel offsets, but that increases timeline
complexity and compositing assumptions. Prefer an optional verified OFX adapter;
otherwise omit it.

### Impact hold

A one-frame duplicate/hold or very short freeze can reinforce a major shot, but
it changes perceived timing and can fight the existing velocity dip. It should
be modeled as a timing treatment with explicit source availability, never
silently added by a visual-effect renderer.

### Transitions

Hard cuts remain the default for kills and fast phrases. Use short dissolves
for calmer boundaries; use zoom/whip/GL transitions only between clips or
regions when motion direction and musical structure support them. Official
VEGAS documentation notes that transitions require overlap/media handles, so
transition planning must validate available source before timeline mutation.

## Restraint by song region

| Song context | Automatic treatment budget | Appropriate vocabulary |
|---|---|---|
| Intro / sparse verse | Low | Clean cuts, gentle grade, occasional slow push; preserve anticipation |
| Build / rising energy | Medium | Increasing velocity, selective blur, smaller pumps; avoid spending the strongest flash early |
| Drop / chorus | High but sparse | Strong kill pump, 1-frame flash, optional shake on the primary accent |
| Dense multi-kill passage | Medium per passage, low per kill | Let shot rhythm lead; treat first/last/strongest kill, not every shot |
| Breakdown / bridge | Low | Longer holds, clean dissolves, minimal shake/flash |
| Final chorus / climax | High | Strongest validated treatment stack with repetition limits |
| Outro | Low | Clean closer, fade, title/credit; no random residual accents |

Recommended global guards (**inference**):

- no more than one strong luminance effect within roughly one musical beat;
- no consecutive identical strong presets unless deliberately forming a motif;
- one dominant visual effect per kill, plus velocity/audio—not a full stack;
- reserve the highest intensity tier for a small fraction of selected anchors;
- effects must not shift the actual kill sync point;
- `None` is a valid and common result;
- deterministic selection from song region, anchor strength, clip semantics,
  recent effect history, capability set, and a saved seed.

## Capability and smoke-test checklist

Before declaring any VEGAS-side effect implemented:

1. Enumerate the actual VEGAS 20 plug-in tree and preset names on the target
   machine.
2. Save the unique IDs and parameter schemas as a diagnostic artifact.
3. Create a short disposable project containing one labeled instance per
   candidate effect at low/medium/high intensity.
4. Verify frame positions, interpolation, event handles, compositing order,
   preview performance, render output, undo behavior, and reopen persistence.
5. Repeat with a missing optional plug-in and confirm deterministic fallback.
6. Do not install or redistribute GPL/open-source binaries automatically.
7. Mark support by exact capability fingerprint, not merely “VEGAS supports
   OFX.”

## Practical rollout order

1. Native punch-in and generated flash presets.
2. Native micro-shake reusing the proven Pan/Crop abstraction.
3. Runtime discovery report for built-in Video FX, transitions, generators,
   presets, and OFX parameters.
4. Built-in blur/glow adapters resolved by unique ID and schema.
5. GL Transition presets at structural boundaries with hard-cut fallback.
6. Only then, an opt-in lab for openfx-misc or a small custom OFX.

This order maximizes visible montage value while keeping the baseline portable,
editable, testable, and independent of the user's installed plug-ins.
