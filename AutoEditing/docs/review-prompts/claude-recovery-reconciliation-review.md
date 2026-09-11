# Independent review brief: crash recovery and VEGAS reconciliation

## Purpose

Please perform a rigorous, read-only architectural and correctness review of
AutoEditing's LLM-driven, clip-by-clip VEGAS assembly workflow. Concentrate on
the boundary where:

1. the human may manually change the candidate timeline in VEGAS;
2. the companion consumes a durable workbench action;
3. the process may crash at any instruction boundary;
4. the process resumes without duplicating inference, losing accepted work, or
   rematerializing over unknown manual edits; and
5. supported timeline edits are adopted while unsupported edits become explicit
   conflicts.

This is not a style review. Look for concrete data-loss, replay, stale-state,
identity, hash-binding, ordering, and time-of-check/time-of-use defects.

## Repository and normative contract

Repository root:

```text
C:\Users\mario\sources\AutoEditing
```

Solution root:

```text
C:\Users\mario\sources\AutoEditing\AutoEditing
```

The normative, code-independent behavior contract is:

```text
AutoEditing/docs/editing-rules.md
```

Pay particular attention to:

- `EDIT-LLM-004`
- `EDIT-LLM-005`
- `EDIT-LLM-006`
- `EDIT-LLM-008`
- `EDIT-LLM-013`
- `EDIT-VEL-005`

If code, tests, and the rulebook disagree, report it as a defect. Do not assume
the document or code is automatically correct.

## Primary files to review

### Durable action and recovery boundary

```text
AutoEditing/Iteration.Contracts/Assembly/AssemblyAction.cs
AutoEditing/LlmEditor/Assembly/AssemblyActionStore.cs
AutoEditing/LlmEditor/Assembly/AssemblyActionExecutionStore.cs
AutoEditing/LlmEditor/Assembly/AssemblyRecoveryService.cs
AutoEditing/LlmEditor/Assembly/AssemblyCoordinator.cs
AutoEditing/LlmEditor/Assembly/AssemblyArtifactStore.cs
AutoEditing/LlmEditor/Program.cs
```

### Live timeline evidence and reconciliation

```text
AutoEditing/Iteration.Contracts/Automation/CandidateTimelineSnapshot.cs
AutoEditing/Iteration.Contracts/Automation/CandidateTrackSnapshot.cs
AutoEditing/Iteration.Contracts/Automation/CandidateEventSnapshot.cs
AutoEditing/Iteration.Contracts/Assembly/CandidateMaterializationBaseline.cs
AutoEditing/Iteration.Contracts/Assembly/TimelineAdjustmentDelta.cs
AutoEditing/LlmEditor/Assembly/AssemblyTimelineReconciler.cs
AutoEditing/LlmEditor/Assembly/AssemblyReconciliationConflictService.cs
AutoEditing/Vegas/Interaction/Adapters/CandidateReview/CandidateSnapshotReader.cs
```

### Later lifecycle replay boundaries

```text
AutoEditing/LlmEditor/Polish/PostRoughCutPolishCoordinator.cs
AutoEditing/LlmEditor/Finalization/PostPolishFinalizationCoordinator.cs
AutoEditing/LlmEditor/Finalization/FinalizationService.cs
```

### Deterministic coverage

```text
AutoEditing/LlmEditor/Assembly/AssemblyActionExecutionStoreSelfTests.cs
AutoEditing/LlmEditor/Assembly/AssemblyRecoverySelfTests.cs
AutoEditing/LlmEditor/Assembly/AssemblyReconciliationConflictSelfTests.cs
AutoEditing/LlmEditor/AssemblyWorkflowSelfTests.cs
AutoEditing/LlmEditor/PolishPassSelfTests.cs
AutoEditing/LlmEditor/FinalizationSelfTests.cs
```

## Required invariants

Evaluate the implementation against every invariant below.

### Action consumption

- An action targets exactly one session, checkpoint, phase, and state revision.
- Exactly one action may win for a published state revision.
- A repeated identical action is idempotent.
- A contradictory action is quarantined or rejected, never substituted.
- A consumed action is durably journaled before any consequential work starts.
- Completion is bound to the same immutable action and exact result.
- Multiple incomplete executions fail closed; recovery never guesses a winner.

### Manual timeline evidence

- Compare, preview, revise, accept, and finish actions preserve the exact live
  candidate snapshot they observed.
- If the process crashes after consuming one of those actions, recovery either
  reuses identity-validated live evidence or fails closed.
- Recovery never cleans or rematerializes a workspace when unknown manual edits
  may exist.
- A persisted snapshot is workspace-, action-, and hash-bound and cannot be
  replaced with different evidence.
- A recovered revision does not issue a duplicate LLM request after the revised
  proposal was already persisted.

### Materialization baseline and reconciliation

- Every new materialization is followed by an exact read-back baseline.
- The baseline is checkpoint-, plan-, workspace-, and content-hash-bound.
- Reconciliation may adopt only:
  - video timeline start;
  - video source trim;
  - video event duration; and
  - constant video velocity.
- The following must never be silently ignored:
  - track add/remove/rename;
  - mute or solo;
  - song/audio event add/remove/move/trim/gain;
  - fades or transitions;
  - grouping/linkage;
  - event effects;
  - media replacement;
  - missing, unexpected, duplicate, or ambiguous events; and
  - variable velocity.
- Unsupported changes must reach a durable, visible reconciliation conflict.
- A missing or corrupt baseline must fail closed with a recoverable instruction.
- Reconciliation validates the actual combined plan after adopting supported
  changes.

### Lifecycle crash points

Reason explicitly about a crash:

1. before action consumption;
2. after consumption but before the execution-start record;
3. after execution start but before live evidence capture;
4. after evidence capture but before state mutation;
5. after state/artifact mutation but before execution completion;
6. after execution completion but before publishing the next state;
7. after the last checkpoint is accepted but before rough-cut handoff;
8. after polish approval is persisted but before materialization;
9. after polish materialization but before review-state publication;
10. after final promotion intent, promotion, final report, and action completion,
    separately.

For each unsafe boundary, identify the exact persisted files and state values
that produce the failure.

## Questions that deserve special scrutiny

1. Can a completed accepted-checkpoint action be marked reflected during startup
   before all secondary durable consequences (for example steering directives)
   are persisted?
2. Is reuse of a materialized workspace based on sufficient live identity and
   evidence, or only on a phase enum?
3. Can a stale materialization baseline be paired with a newer proposal?
4. Does the baseline hash validate the semantic content canonically enough for
   the serializer used?
5. Can track discovery omit a renamed owned track and make it appear as a safe
   empty candidate?
6. Are audio gain and event gain modeled distinctly enough to detect every
   relevant change?
7. Is `GroupSignature` stable and collision-resistant enough for the intended
   linkage check?
8. Can finalization find the exact action after it is already completed, without
   accepting an older or unrelated finalization attempt?
9. Are pause/resume/abandon actions journaled and replayed at every review
   boundary, including a deferred reconciliation conflict?
10. Do any operation keys contain random data where crash replay requires a
    stable idempotency key?

## Requested output

Return a Markdown report with these sections:

1. **Verdict** — safe, conditionally safe, or unsafe, with a short reason.
2. **Blockers** — defects that can lose work, overwrite manual edits, promote the
   wrong candidate, or make recovery nondeterministic.
3. **High-risk findings** — correctness defects without immediate destructive
   impact.
4. **Missing deterministic tests** — name the exact crash window and the
   assertions the test should make.
5. **Contract disagreements** — code/test/rulebook mismatches.
6. **Recommended patch order** — smallest safe sequence of changes.

For every finding, include:

- severity;
- exact file and line;
- the failing state sequence;
- why existing guards or tests do not prevent it;
- a concrete correction; and
- a deterministic regression-test outline.

Do not propose a broad rewrite unless you can demonstrate why a localized fix
cannot preserve the invariants. Do not treat planned or documented behavior as
implemented without tracing the executable path.
