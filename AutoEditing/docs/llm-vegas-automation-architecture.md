# LLM–VEGAS automation architecture

This document records the implemented process and ownership boundaries for the
progressive LLM editing workflow. Editing behavior itself is normative in
`docs/editing-rules.md`.

## Process boundary

The .NET 8 companion owns llama.cpp communication, semantic planning,
multimodal review, workflow coordination, recovery, and durable session
artifacts. The .NET Framework extension is the only process allowed to invoke
VEGAS APIs. `AutoEditing.Iteration.Contracts` carries versioned DTOs between
them.

```text
VEGAS workbench <-> durable session artifacts <-> LLM companion
                                                    |
                                             typed spool jobs
                                                    |
                                           Core automation broker
                                                    |
                                        allow-listed VEGAS handlers
```

The workbench reads and writes session artifacts; it never calls the LLM
transport or VEGAS COM from a background process. No model response, script
source, COM object, or arbitrary command name crosses the automation boundary.

## Durable session and job transport

Each session has an immutable normalized planning request, semantic sketch,
revisioned clip proposals, accepted plans, exact-state actions, action
dispositions and execution records, timeline snapshots, render evidence,
polish artifacts, inference conversations and usage, and finalization evidence.
Writers use temporary files followed by atomic replacement. Immutable artifacts
are content-checked before reuse.

One exclusive runtime lease owns the workflow. Workbench actions target the
exact session, checkpoint, and state revision that was displayed. A durable
claim chooses one action; a durable execution record is written before the
consumed disposition, so process loss cannot permanently consume a button click
without a replayable operation.

The automation client writes payload-hashed, deadline-bound, project-bound jobs
to a typed spool. The in-process broker validates and dispatches one allow-listed
operation at a time on the VEGAS command queue. Mutating idempotency keys include
the logical operation attempt: retrying one interrupted action reuses its key,
while a later deliberate reset or rebuild receives another key. Readback and
reconciliation verify the live result before a review state is published.

## Candidate ownership

All work occurs in one exact candidate workspace:

```text
AE|LLM|{session}|{iteration}|{nonce}|...
```

New video events also receive a deterministic workspace-owned placement ID.
Cleanup matches the complete workspace identity and may not use a broad `AE|`
prefix. Unrelated tracks and events are never part of candidate cleanup,
materialization, polish, promotion, or rollback.

The selected song is materialized during synchronization and reused only when
the later audio pass verifies its exact path, start, and gain. Effects and SFX
are not applied during the synchronization pass.

## Progressive assembly

```text
reviewed request
  -> semantic assembly sketch
  -> one clip-step decision
  -> deterministic compile and validation
  -> preflight, materialize, snapshot readback
  -> human compare / preview / adjust / accept / revise / reset
  -> reconcile the live timeline
  -> next clip
```

The model authors semantic intent and one compact `ClipStepDecision`, not VEGAS
renderer DTOs or a complete replacement montage. The deterministic compiler
restores the accepted prefix and derives canonical timing, sync, speed, and
placement data.

Supported manual move, trim, duration, and constant-speed changes become
structured adjustment evidence. Missing, extra, ambiguous, foreign,
out-of-bounds, and variable-velocity events create a durable conflict. The
editor may explicitly restore, safely exclude, adopt one validated known
remaining event, or defer and pause. Mutating resolutions are intent-first,
read back the live timeline, and replay after interruption.

## Render and review stages

A checkpoint preview is an explicit non-mutating action. The renderer isolates
the candidate, captures bounded video and PNG evidence, and restores cursor,
selection, loop, mute, and solo state in `finally`.
Numbered attempts are immutable and remain selectable as revision A and B.
Accepting the final clip in a semantic song section also renders that complete
accepted section in contiguous, hash-verified chunks.

After the final sync checkpoint, the companion renders contiguous chunks for
the complete rough cut, computes deterministic continuity/pacing findings, and
optionally asks the multimodal model for evidence-citing observations.
Corrections are checkpoint-scoped proposals and require human approval plus a
new live reconciliation; the model cannot mutate the timeline.

Effects and audio are later versioned passes:

```text
accepted rough cut
  -> effects plan -> approve -> apply -> complete preview -> accept
  -> audio/SFX plan -> approve -> apply -> complete preview -> accept
  -> final review
```

Every pass binds exact prior-state hashes. Rejected rendered passes restore and
verify the accepted baseline before a higher revision proceeds. Missing or
corrupt cached render chunks are quarantined and regenerated.

The baseline chain starts with the complete VEGAS snapshot captured at explicit
rough-cut acceptance, not only placement geometry. Accepted effects and audio
passes each record the next complete snapshot. Checkpoint, rough-cut plan hash,
workspace identity, and snapshot hash are enforced at polish entry,
stage-appropriate restoration, and finalization; track state, audio volume
automation, fades, transitions, grouping, and effects cannot silently cross
those boundaries.

## Promotion and rollback

Finalization re-reads and reconciles the complete live candidate. Promotion is a
short rename-only transaction over the exact candidate-owned tracks; it does
not rebuild or delete unrelated timeline content. A pre-mutation intent and
hash-bound recovery bundle are durable before completion is reported.
The final-review action carries an explicit render-before-promotion target.
That target is included in the finalization request hash, so recovery cannot
change the choice.

The completed report includes final plan and snapshot hashes, model/session
usage, artifact inventory, and an external SHA-256-addressed archive. Explicit
rollback is available only while the same project and promoted timeline evidence
still match, and must reproduce the original candidate snapshot exactly.

## Local inference and observability

The supported local backend is llama.cpp `llama-server` through OpenAI-compatible
chat completions with schema-constrained JSON and image inputs. Server
capabilities and context size are probed at session start. Built-in model tools
remain disabled.

All prompts, streamed response text, parsed results, errors, timing data
(including measured time to first streamed token), prompt and generated tokens,
cached prompt tokens, context peaks, and retries are persisted. Session totals
detect mixed inference identities; lifetime totals are partitioned by the exact
backend-and-model pair. The separate Inference Monitor presents these records as
a live conversation. The VEGAS workbench projects editorial decisions,
operation-specific progress and evidence, not private model chain-of-thought.
