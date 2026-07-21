# Product Brief

## Decision

AutoEditing will evolve from a one-shot montage generator into a guided semantic
timeline assistant for Call of Duty sniper montages in VEGAS Pro.

The assistant automates repeatable mechanics while leaving ambiguous and
high-judgment editorial decisions with the editor. It must produce native,
legible, undoable VEGAS timeline data wherever the API permits.

## Problem

A sniper clip is not editorially defined by its start or end. Its important
content is a chain of events such as scope-in, discharge, hitmarker, kill
confirmation, killfeed update, and bolt cycle. Music likewise contains events of
different importance: sections, downbeats, fills, transients, vocal accents, and
pauses.

Simple automation that places every clip boundary or first detected transient on
a beat misses this structure. It can also mistake a loud non-kill transient for
the hero moment. Editors need a faster way to inspect candidates, confirm the
correct event, align it to music, and shape the surrounding time without giving
up control of the timeline.

## Target user

The first target user understands basic VEGAS editing and wants to shorten the
repetitive parts of montage creation. The MVP does not assume that the user knows
retiming mathematics or scripting APIs.

Beginners may benefit later from stronger recommendations, but the first release
optimizes for transparent control rather than automatic creative direction.

## Core workflow

```text
Import clips and music
→ analyze shot and beat candidates
→ verify one primary gameplay event per clip
→ select a music event
→ approve an alignment
→ preview a retiming solution
→ generate native VEGAS placement and velocity data
→ revise, regenerate, or remove the result
```

## Core concepts

```text
SourceClip
  └─ GameplayEvent ──┐
                     ├─ Alignment ─ RetimingSolution ─ GeneratedTreatment
MusicSource          │
  └─ MusicEvent ─────┘
```

- A detector creates a candidate event.
- A user may verify, adjust, reject, or manually create that event.
- An alignment expresses editorial intent.
- A retiming solution proves that the intent can be realized with the selected
  profile and available media.
- A generated treatment records what was written to VEGAS and how to reproduce
  or remove it.

## Product principles

1. **Semantic anchors first.** Align gameplay and music events, not arbitrary
   clip boundaries.
2. **Candidates are not decisions.** Confidence measures detection likelihood,
   not editorial importance.
3. **Human approval at ambiguity.** The user confirms the hero event and final
   alignment.
4. **Solve before mutation.** Preview feasibility, source consumption, and
   warnings before changing the timeline.
5. **Native editability.** Prefer VEGAS events, markers, envelopes, Pan/Crop
   keyframes, and audio events over opaque renders.
6. **Non-destructive operation.** Do not modify source media. Preserve handles
   and make generated changes removable.
7. **Deterministic generation.** Persist preset version, parameters, source
   anchors, and any random seed.
8. **No forced styling.** A hard cut or no additional effect is always valid.
9. **Frame-rate-aware timing.** Store time semantics explicitly; do not assume
   that six frames has the same perceived duration at every frame rate.
10. **Explain important outcomes.** State why an alignment was suggested or why
    a retiming request is infeasible.

## MVP outcome

The MVP succeeds when an editor can take detected candidates, verify the correct
gameplay anchor, pair it with a music marker, and generate a correct native
VEGAS placement and velocity envelope without manually calculating source-time
consumption.

The MVP is not a complete montage generator. It validates the product's most
important relationship:

```text
verified gameplay event ↔ approved music event
```

## Explicit non-goals for the MVP

- automatic narrative or full-song clip ordering;
- automatic kill recognition without review;
- automatic phrase and section interpretation;
- visual impact stacks, sound replacement, or transition recommendation;
- optical-flow generation;
- learned clip-quality ranking;
- grading and rendering automation;
- claims that software can objectively judge readability, breathing room, or
  emotional escalation.

These capabilities may be added after the anchor-to-anchor workflow has measured
value and stable VEGAS integration.

## Success measures

Measure the workflow on real projects:

- median time to verify one gameplay anchor;
- median time from verified anchor to generated alignment;
- percentage of proposed alignments accepted;
- percentage of retiming solutions accepted without curve edits;
- anchor error after generation, in project frames;
- rate of insufficient-handle or invalid-source mappings caught before commit;
- successful individual regeneration and removal rate;
- user-rated timeline legibility and creative control.

Initial targets should be established after a baseline manual-editing study, not
invented in advance.

