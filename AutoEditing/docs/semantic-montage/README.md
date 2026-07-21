# Semantic Montage Assistant

This documentation set defines the product direction for AutoEditing: a guided
VEGAS Pro assistant that aligns meaningful gameplay events with meaningful music
events, then generates editable timeline treatments around those alignments.

The central design rule is:

> Synchronize semantic events, not clip boundaries.

## Documents

1. [Product brief](product-brief.md) — product decision, users, principles, scope,
   and success criteria.
2. [Research and assumptions](research-and-assumptions.md) — evidence levels,
   current hypotheses, and the validation plan.
3. [MVP requirements](mvp-requirements.md) — the first end-to-end product slice,
   prioritized requirements, and acceptance criteria.
4. [Domain and retiming model](domain-and-retiming-model.md) — semantic entities,
   persistence boundaries, and the source-time/timeline-time solver contract.
5. [VEGAS feasibility matrix](vegas-feasibility-matrix.md) — what is documented,
   what the repository implements, and what still requires a VEGAS Pro 20 probe.

The larger vision includes section-aware planning, visual impact, sound design,
transition assistance, and editorial quality suggestions. Those ideas remain
valid, but they are deliberately outside the first MVP until the core
anchor-to-anchor workflow has been validated.

## Relationship to the current repository

The current working pipeline is:

```text
parse → detect beats → detect shot candidates → review → plan → build timeline
```

The intended evolution is:

```text
analyze → verify semantic events → propose alignments → approve
        → solve retiming → generate native VEGAS data → revise or remove
```

Existing detectors remain useful. They produce candidates; they do not make the
final editorial decision. Existing `ShotEvent`, `BeatGrid`, `SpeedProfile`, and
`ClipPlacement` types should be adapted incrementally behind the generalized
model rather than replaced in one migration.

## Status language

These documents use four feasibility states:

- **Implemented** — present in this repository, though VEGAS-host behavior may
  still require a smoke test.
- **Documented** — supported by API documentation or prior research, but not yet
  proven by this application in VEGAS Pro 20.
- **Probe required** — plausible, but exact runtime API behavior or identifiers
  must be inspected in VEGAS Pro 20.
- **Deferred** — intentionally outside the current delivery target.

