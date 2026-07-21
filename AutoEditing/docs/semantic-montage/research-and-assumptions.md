# Research and Assumptions

## Purpose

This document separates observed editing conventions from editorial hypotheses
and committed product behavior. It prevents tutorial examples from becoming
hard-coded definitions of good editing.

## Evidence levels

### Observation

A dated, attributable example from a tutorial, editor interview, or observed
workflow.

Example: an editor used a fast approach, slowed before discharge, and accelerated
after confirmation.

### Pattern

A convention repeated across multiple independent observations. A pattern should
record sample size, style, publication period, game, source frame rate, and
relevant plugin dependencies.

### Editorial hypothesis

An explanation that can guide design but is not directly proven.

Example: deceleration before a shot creates anticipation while acceleration
removes dead time.

### Product requirement

Testable application behavior justified by the problem, regardless of whether a
particular tutorial used it.

Example: the system previews source consumption before applying a velocity
profile.

## Current working hypotheses

The following are credible starting points, not validated frequency claims:

- The most useful synchronization primitive is a selected gameplay event aligned
  with a selected music event.
- Discharge, hitmarker, kill confirmation, and killfeed update can all serve as a
  primary anchor depending on style and source material.
- Scope start, full scope, bolt cycle, and movement exit are useful secondary
  anchors.
- A fast approach, brief anticipation, impact, and release is a reusable timing
  family, but its values should remain editable.
- Impact is multimodal: music, game audio, added audio, motion, zoom, and flash
  may reinforce the same moment.
- Treating every beat or every clip identically usually reduces variation.
- Hard cuts are a first-class creative choice.

The product brief depends only on the first hypothesis. The remaining hypotheses
inform later presets and recommendations and must not block the core MVP.

## Tutorial values

Values such as `50%`, `500%`, or “six frames before the shot” are observations
only. Presets must record:

- reference frame rate;
- whether an offset is stored in frames or time;
- conversion and rounding policy;
- minimum visible duration;
- supported speed range;
- required source handles;
- interpolation assumptions.

Presets are versioned implementation mechanisms. They are not scoring objectives
and do not prove that an edit is good.

## Research dataset

Record every source as structured data instead of relying on prose summaries:

```text
TutorialObservation
- sourceUrl
- creator
- publicationDate
- dateObserved
- VEGAS version
- game and mode
- montage style
- source and project frame rate
- clip context
- chosen gameplay anchor
- chosen music anchor
- velocity control points
- timing offsets
- curve types
- visual effects
- audio layers
- transition type
- plugin dependencies
- direct observation or researcher inference
- notes
```

Derived claims should identify the observations that support them. When evidence
is sparse, use language such as “candidate convention” rather than “commonly.”

## Validation studies

### Workflow observation

Observe editors creating or revising real montages. Record:

- how they find the hero event;
- which event they choose as the anchor and why;
- number of frame-level corrections;
- velocity curve and handle decisions;
- when they reject synchronization in favor of readability;
- revision and removal behavior;
- whether generated timeline structure remains understandable.

### MVP usability study

Compare manual VEGAS editing with the guided workflow on the same clips. Measure
time, anchor error, corrections, failed mappings, and subjective control.

### Preset study

Only after the solver works, test a small number of clearly distinct profiles.
Do not begin with a large style library. Record acceptance and adjustment rates
to determine whether the profiles are genuinely different and useful.

## Claims the MVP must not make

Without a defined measurable rule, the application must not state as fact that:

- action is unreadable;
- a montage lacks breathing room;
- intensity is emotionally flat;
- a clip was used too early;
- audio masking is unacceptable;
- an effect is dated or excessive.

Later versions may surface these as configurable heuristics or informational
suggestions. The UI must label them accordingly and explain the measured signal.

