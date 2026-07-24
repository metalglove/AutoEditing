# Forensic value of a VEGAS project without source media

A `.veg` project can retain most of the mechanically useful information needed
to recover editing presets even when its referenced clips are unavailable.
Missing media lowers the confidence of semantic and visual conclusions; it does
not make the project structurally useless.

## Three separate kinds of evidence

- The `.veg` records **what the editor configured and when**.
- The source media explains **why that treatment was chosen for that shot**.
- The final render shows **what the stored configuration produced**.

These evidence types answer different questions and must not be treated as
interchangeable.

## Information commonly retained by the project

Subject to VEGAS version, plug-in availability, and inspector capabilities, a
media-less project may preserve:

- event placement, duration, overlap, source offset, and playback rate;
- cuts, crossfades, transition duration, and transition parameters;
- velocity-envelope points, curve types, and event segmentation;
- Pan/Crop, Track Motion, opacity, and compositing keyframes;
- event-, media-, track-, bus-, and output-level effect-chain order;
- OFX parameter values, animation keys, and interpolation;
- audio-event placement, gain, fades, routing, and envelopes;
- track hierarchy, names, mute/solo state, and compositing relationships;
- markers, regions, labels, generated media, and text;
- nested-project references and repeated effect configurations.

This can be sufficient to recover exact or candidate recipes for screen pumps,
shake tiers, flashes, impact stacks, velocity ramps, fades, transitions, and SFX
placement. Repeated identical configurations can also reveal preset reuse.

Some plug-ins store state opaquely or fail to instantiate when unavailable.
Absence from an inspected live effect chain is therefore not proof that an
effect was never used.

## What cannot be established from the project alone

Without the source pixels or a corresponding render, the project generally
cannot prove:

- the visible kill, scope-in, scope-out, or confirmation frame;
- whether an effect complements or obscures the underlying motion;
- whether apparent motion originates in the clip or the applied treatment;
- why one clip was assigned to a particular musical anchor;
- whether velocity changes align correctly with the action inside the source;
- the visible result of masks, chroma keys, blends, or source-dependent effects;
- whether a named or offline event contains gameplay, a cinematic, or a
  prerendered composite when surrounding metadata is inconclusive.

For example, the project may prove that a speed ramp starts 18 frames before an
impact boundary and a pump peaks at that boundary. It does not by itself prove
that the boundary is the visible kill or that the recovery begins after
scope-out.

## Value by research question

| Research goal | Value without source clips |
|---|---|
| Recover effect chains, parameters, and candidate presets | Very high |
| Recover timeline, transition, and track structure | High |
| Recover velocity curve shapes | High |
| Measure treatment timing relative to cuts and retained music | High |
| Determine treatment timing relative to in-game action | Low to medium |
| Infer clip-selection and ordering rationale | Low |
| Visually validate the treatment | Low without a final render |

A matching final render materially increases the value of a media-less project.
It permits timeline-to-output comparison even though source-to-output
transformation and source-frame semantics remain partially unknown. Retained
music and SFX further improve beat, impact, and audio-treatment analysis.

## Acquisition and evidence policy

For preset research, prefer this minimum bundle:

1. the `.veg` project;
2. its corresponding final render;
3. retained music and SFX when legitimately supplied;
4. an explicit VEGAS version and plug-in dependency list.

Original gameplay media is most important when converting a recovered mechanical
recipe into a contextual automation rule such as “begin recovery after
scope-out.” It is less essential when the goal is simply to recover a stored
effect chain and its keyframes.

Record separate confidence for:

- project/listing provenance;
- archive and dependency completeness;
- mechanical preset recovery;
- timeline-time interpretation;
- visual validation;
- semantic interpretation of the shot.

Do not promote a recovered preset into a general editing rule solely because its
parameters are present. Cross-project repetition, a matching render, or
source-action evidence is needed to justify when and why the preset should be
applied.

## Public attribution

Private reference-project authors must be identified in repository material only
as **Editor 1** and **Editor 2**. Their real identities are local knowledge and
must not appear in tracked documentation, issues, commits, pull requests, test
fixtures, or exported example data.
