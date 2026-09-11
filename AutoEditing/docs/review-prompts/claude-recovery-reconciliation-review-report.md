# Independent review report: crash recovery and VEGAS reconciliation

Scope reviewed: the durable assembly action/execution journal, per-clip
synchronization-pass materialization and reconciliation, the rough-cut audit
→ polish → finalization boundary, pause/resume/abandon handling across all
three phases, and the ten crash windows named in the review brief. Every
finding below is traced to exact source lines; nothing is inferred from
comments, tests-as-documentation, or the rulebook's prose alone.

## 1. Verdict: Conditionally safe

The per-clip synchronization pass — durable action claims, execution-start/
completion records, `TryRecoverClaimed` replay, and
`CandidateMaterializationBaseline`-bound reconciliation — is carefully built
and, for every crash window traced, correctly idempotent: recovery never
duplicates LLM inference, never re-applies a completed mutation, and reliably
turns unsupported live-timeline changes into durable, visible conflicts
*while a checkpoint is being reconciled inside that pass*.

It stops being safe once the workflow leaves that pass. The same
`AssemblyTimelineReconciler.Apply` entry point is reused at the three most
sensitive downstream trust boundaries — rough-cut→polish handoff, polish
baseline restoration, and final promotion — through a call signature that
omits the `CandidateMaterializationBaseline` argument. Without a baseline,
the reconciler only checks video-placement geometry (start/trim/duration/
speed); it never inspects tracks, mute/solo, gain, fades, transitions,
grouping, or effects. A human edit to any of those categories between
rough-cut acceptance and final promotion is promoted silently (**Blocker
B1**). This directly contradicts the review brief's required invariant list
and, in spirit, `EDIT-LLM-012`'s claim that finalization "reconciles it with
the complete final plan."

Beyond B1, no confirmed defect duplicates a monetary VEGAS mutation, loses an
accepted plan, or leaves recovery unable to pick a winner — several
brief-suspected mechanisms (stale-baseline pairing, renamed-track omission,
workspace-identity-by-phase-enum, hash-ordering instability, steering-
instruction durability) were traced end-to-end and found to already be
correct. The remaining findings are real but narrower: gain/grouping
evidence gaps, non-deterministic idempotency keys on a handful of cleanup
call sites, an orphaned-workspace cleanup gap, an audit-trail (not
functional-correctness) gap, and a structural asymmetry in how the
later phases handle a timeline that diverges outside of a checkpoint
review.

## 2. Blockers

### B1 — Baseline-less reconciliation is blind to every non-geometric category at the polish/finalization boundary

- **Severity:** Blocker.
- **Files/lines:**
  - `AutoEditing/LlmEditor/Assembly/AssemblyTimelineReconciler.cs:12-26` — the
    `baseline` parameter defaults to `null`. The entire
    `ValidateAgainstMaterializationBaseline` block (lines 146-215) — the
    *only* code that inspects track add/remove/rename, mute/solo, audio/song
    events, gain, fades, transitions, grouping (`GroupSignature`), and
    effects — runs only `if (baseline != null)`.
  - `AutoEditing/LlmEditor/Finalization/FinalizationService.cs:141` — primary
    promotion path calls the 2-arg `Apply(validatedPlan, snapshot)`.
  - `AutoEditing/LlmEditor/Finalization/FinalizationService.cs:228` — the
    finalization-retry path does the same.
  - `AutoEditing/LlmEditor/Polish/PostRoughCutPolishCoordinator.cs:841`
    (`RequireRoughCutLayoutAsync`) and `:815` (`ValidateRestoredSnapshot`) —
    same 2-arg call at rough-cut→polish entry and at polish-rejection
    baseline restoration.
- **Failing state sequence:** The sync pass finishes and the rough cut is
  accepted. Before or during polish/finalization, the editor — outside any
  reviewed action — mutes the `AE|Montage Song` track, adds a fade to a
  video event, groups two clips, or attaches an effect to an existing event.
  Video placement start/trim/duration/speed are untouched, so
  `delta.Changes.Count == 0` at `RequireRoughCutLayoutAsync`, and
  `FinalizationService.FinalizeAsync`'s `Apply` call raises nothing. The
  change rides silently into the promoted montage.
- **Why existing guards/tests don't prevent it:** The per-placement loop in
  `Apply` (lines 27-120) only enumerates `Video`-kind track events and only
  compares start/trim/duration/speed for placements already expected to
  exist; it never enumerates audio tracks. The full track/event walk that
  *would* catch this is entirely gated behind an optional baseline these
  three call sites never supply. `EditPlanDocumentValidator` cannot help
  either — mute/gain/fade/group state isn't part of `EditPlanDocument` at
  all. Neither `FinalizationSelfTests.cs` nor `PolishPassSelfTests.cs`
  contains any assertion involving `Mute`, `Solo`, `Gain`, `Fade`,
  `GroupSignature`, or `Effects` (confirmed by direct search — the only
  `PolishPassSelfTests.cs` hits are unrelated `PolishPassKind.Effects` enum
  references). By contrast, `AssemblyWorkflowSelfTests.cs:172-252`
  (`TestMaterializationBaselineRejectsUnsupportedChanges`) proves this exact
  logic works correctly *when a baseline is supplied* — confirming the fix
  is a wiring problem, not a missing capability.
- **Correction:** Capture a durable "post-acceptance baseline" — structurally
  identical to `CandidateMaterializationBaseline` — at rough-cut acceptance,
  from the same snapshot already taken in
  `AssemblyCoordinator.PublishCheckpointAsync`. Persist it once and thread it
  through `RequireRoughCutLayoutAsync`, `ValidateRestoredSnapshot`, and both
  `FinalizationService` call sites so all three invoke the 4-arg `Apply`
  overload. This reuses `ValidateAgainstMaterializationBaseline` rather than
  inventing a new comparator.
- **Deterministic regression-test outline:** Given an accepted rough cut and
  its baseline snapshot, mutate a cloned live snapshot to (a) mute the song
  track, (b) add a fade to a video event, (c) attach an effect to a video
  event, (d) add an unrelated new audio track, (e) group two video events.
  Assert `RequireRoughCutLayoutAsync` and `FinalizationService.FinalizeAsync`
  each require recovery / throw for every one of (a)-(e), not only for
  start/trim/duration/speed changes.

## 3. High-risk findings

### H1 — Per-event audio gain envelopes are never captured; only a track-level scalar is smuggled into event `Gain`

- **File/line:**
  `AutoEditing/Vegas/Interaction/Adapters/CandidateReview/CandidateSnapshotReader.cs:86`
  — `Gain = trackEvent.Track is AudioTrack audioTrack ? audioTrack.Volume :
  1.0`. `ReadVelocity` only reads envelope `Type == 202` (playback rate),
  never a volume/gain envelope. `CandidateEventSnapshot.Gain` is a single
  scalar with no field for a gain curve.
- **Failure scenario:** An editor adds a volume-automation envelope (e.g. a
  manual fade-out) to an accepted SFX or song event without changing the
  track's overall `Volume`. `Gain` compares equal before/after (both read the
  unchanged track scalar), so even the baseline-bound reconciler
  (`CompareDouble(..., "gain", ...)`, `AssemblyTimelineReconciler.cs:252`)
  reports no change — a real gap in the *protected* sync pass, not only the
  B1 paths. Separately, video events always report `Gain = 1.0` (never read
  from VEGAS), making the parallel "event gain" check at
  `AssemblyTimelineReconciler.cs:259` permanently a no-op for video — harmless
  today, but dead code that could mislead a future maintainer into believing
  video gain is monitored.
- **Why tests don't catch it:** `AssemblyWorkflowSelfTests.cs:222` builds a
  `CandidateEventSnapshot` by hand and asserts the *reconciler's comparison*
  fires — it never exercises `CandidateSnapshotReader`, which requires the
  live VEGAS COM API and has no adapter-level seam/test in this suite.
- **Correction:** Capture per-event volume/gain envelope points on
  `AudioEvent`, parallel to how velocity envelopes are captured for
  `VideoEvent`, and compare the envelope rather than a scalar smuggled from
  the track.
- **Test outline:** Materialize a checkpoint with a song/SFX event, add a
  volume envelope point to the live audio event without touching track
  `Volume`, and assert `ValidateAgainstMaterializationBaseline` raises an
  unsupported-change entry. (Needs a fake-VEGAS seam around
  `CandidateSnapshotReader` since it isn't independently testable today.)

### H2 — The stated "plan-hash-bound" baseline invariant is not actually enforced at the point of use

- **Files/lines:** `CandidateMaterializationBaseline.PlanSha256`
  (`Iteration.Contracts/Assembly/CandidateMaterializationBaseline.cs:18`) is
  persisted, and `AssemblyArtifactStore.ReadMaterializedBaseline`/
  `ReadAndValidateBaseline` validate its format and self-consistency with the
  stored snapshot hash — but nothing compares it against the plan currently
  being reconciled. `AssemblyTimelineReconciler.ValidateAgainstMaterialization
  Baseline` (`:146-215`) checks `baseline.Checkpoint` and workspace identity
  but never touches `baseline.PlanSha256`. `AssemblyCoordinator.
  ReconcileMaterializedTimeline` (`:1115-1131`), the sole sync-pass caller,
  reads the baseline and calls `Apply` without ever computing or comparing
  `PlanSha256(plan)` against it.
- **Why it isn't currently reachable:** Every live call path calls
  `SaveMaterializedBaseline` synchronously right after `MaterializeCandidate`,
  and the "reuse workspace" recovery path (`AssemblyRecoveryService.cs:
  217-223`) only skips re-materialization when the *same* persisted proposal
  that produced the baseline is being reused. Tracing all reachable call
  sites turned up no sequence today that pairs a stale baseline with a newer
  proposal.
- **Why it's still a real defect:** the review brief's own required
  invariant states the baseline must be "checkpoint-, plan-, workspace-, and
  content-hash-bound." Only three of four are actually checked; `PlanSha256`
  is persisted for audit but never validated — a defense-in-depth hole that a
  future change to `ReuseMaterializedWorkspace` computation, or a new
  materialization-skipping code path, could silently exploit with nothing to
  catch it.
- **Correction:** Add a `baseline.PlanSha256 != PlanSha256(plan)` check
  inside `ValidateAgainstMaterializationBaseline` (or its caller), matching
  the fail-closed posture already used for checkpoint/workspace mismatches.
- **Test outline:** Persist a baseline for plan A at checkpoint 1, then call
  `Apply` with a *different* plan B (same checkpoint) and that baseline;
  assert it throws instead of comparing B's placements against A's baseline
  snapshot.

### H3 — Two incompatible content-hashing schemes coexist for integrity-critical artifacts

- **Files/lines:** `Iteration.Contracts/Serialization/ContractHash.cs` is a
  purpose-built canonical hash — JSON-property-order-independent,
  culture-invariant, explicit `G17` double formatting — used only by
  `FinalizationService.SnapshotHash` (`:1090-1091`). Everywhere else that
  binds correctness-critical state — `AssemblyArtifactStore.ComputeSha256`
  (`:290-296`, used for `PlanSha256`/`SnapshotSha256`/`RequestSha256` on the
  materialization baseline and session descriptor),
  `AssemblyCoordinator.PlanSha256` (`:1479-1483`), and
  `AssemblyActionExecutionStore`'s hash normalization — hashes the raw string
  from `ContractSerializer.Serialize` (Newtonsoft, reflection member order)
  directly.
- **Risk:** Newtonsoft's default member order is stable for a given compiled
  type shape, so this works today, but it is not robust to a future
  property reorder/rename-preserving refactor of any hashed contract type —
  such a change silently changes every persisted hash's expected value on
  next build (fails closed, but confusingly), and more importantly means the
  canonical hasher the codebase already built is not protecting the exact
  artifacts this review is about.
- **Correction:** Route `PlanSha256`/`SnapshotSha256`/`RequestSha256` in
  `AssemblyArtifactStore` and `AssemblyCoordinator` through
  `ContractHash.Compute(...)` for consistency with `FinalizationService`.

### H4 — `GroupSignature` is a multiset join, not a topology-preserving signature

- **File/line:** `CandidateSnapshotReader.cs:100-119` (`ReadGroupSignature`)
  builds `"MediaType:identity"` per group member, sorted ordinally and
  joined with `|`; compared via ordinal string equality
  (`AssemblyTimelineReconciler.cs:269-271`).
- **Risk (low/medium):** two differently-structured groupings that happen to
  contain the same identity multiset could produce identical signatures. For
  events with a durable owned placement ID (the normal case per
  `EDIT-LLM-004`) this is very unlikely, since each ID embeds a unique
  placement ordinal and path digest — real-world exposure is narrow, limited
  to the legacy path-only identity fallback for foreign/pre-placement-ID
  events.
- **Correction:** low priority; if addressed, incorporate VEGAS's own group
  object identity/count instead of only a sorted member list.

### H5 — Inconsistent, non-deterministic idempotency-key discipline for the mutating `Cleanup` operation

- **Files/lines:** `AssemblyCoordinator.CleanupAsync` (`:1154-1168`)
  generates `$"assembly-{checkpoint:D4}-cleanup-{Guid.NewGuid():N}"` when
  called with no explicit `operationScope`. This affects: the post-accept
  cleanup call at `:610` (called right after `actionExecutions.Complete`);
  the entire legacy, non-progressive `RunAsync` path (`:619-620, 648-649,
  674`), which has no deterministic scoping anywhere; and `workbench-abandon`
  in `Program.cs:351`. Every sibling call site for revise/reset/conflict
  resolution deliberately passes a deterministic scope
  (`"revise-"+action.ActionId`, `"conflict-"+resolution.ResolutionId`)
  specifically so a crash-and-retry reuses the same idempotency key.
- **Risk:** if the process crashes mid-cleanup at one of these call sites and
  is retried, the retry uses a fresh random key, so the automation broker's
  replay/dedup mechanism (the same one `EDIT-LLM-008/010/011` rely on
  elsewhere) cannot recognize the retry as the same logical operation.
  Correctness then depends entirely on the VEGAS-side `CleanupCandidate`
  handler being naturally idempotent when invoked twice on an
  already-partially-cleaned workspace — plausible, but unverified, and
  inconsistent with the deliberate determinism used for every other
  operation of the same kind in the same class. The legacy `RunAsync` path is
  only ever instantiated from test files (confirmed via
  `grep "new AssemblyCoordinator("`), not from `Program.cs`, so that specific
  branch is a code-hygiene note rather than a live production gap; the other
  two sites are live.
- **Correction:** derive a deterministic scope for every `CleanupAsync` call
  (e.g. `"post-accept-" + action.ActionId`, or a stable phase/reason token)
  instead of mixing random and deterministic keys for the same operation
  kind.
- **Test outline:** simulate a crash mid-`CleanupAsync` at the post-accept
  call site and at `workbench-abandon`; restart and assert the retry uses a
  stable, recognizable key.

### H6 — Orphaned prior-checkpoint workspace after a crash between action-completion and cleanup

- **Files/lines:** if the process crashes after
  `actionExecutions.Complete(action, "timeline-accepted", ...)` but before
  the `CleanupAsync` call at `AssemblyCoordinator.cs:610`,
  `ReadPendingForRecovery` finds no pending action (the completion record
  already exists), so recovery proceeds straight to planning the next
  checkpoint. No code path in `AssemblyRecoveryService.Inspect` or
  `ContinueProgressiveAsync`'s startup handling detects or retroactively
  cleans the superseded checkpoint's candidate-owned VEGAS tracks.
- **Consequence:** not data loss, but stale `AE|LLM|...` tracks can
  accumulate across repeated crash cycles, risking a future collision with
  `EDIT-LLM-012`'s promotion-time requirement that only the exact
  candidate-owned tracks are renamed and that collisions with unrelated
  tracks are rejected.
- **Correction:** on recovery, if an accepted checkpoint exists and a
  lower-iteration workspace than the current one still exists, clean it up
  before proceeding.
- **Test outline:** accept a checkpoint, kill the process before cleanup,
  restart, and assert the old iteration's candidate tracks are gone before
  the next materialize.

### H7 — Action-disposition audit-trail gap on recovered/advance-replayed actions

- **File/line:** `AssemblyCoordinator.WaitForActionAsync` (`~:1218-1245`).
  When a pending execution-start record exactly matches, or "advance-replays"
  via `CanReplayAdvancedProgressiveAction`, the method returns `pending.Action`
  directly without ever calling back into `AssemblyActionStore` to write a
  "consumed" disposition for the original `assembly/actions/{id}.json` file.
- **Consequence:** that file remains undispositioned until a *later*
  checkpoint's `TryConsume` scan sees a checkpoint/state-revision mismatch
  and quarantines it with a misleading `"stale-checkpoint"` reason — even
  though the action was, in fact, successfully processed and completed. This
  does not cause double-processing (the execution store, not the disposition
  folder, is authoritative there), but it corrupts the human-facing audit
  trail (`assembly/action-dispositions/`, `assembly/consumed/`,
  `assembly/quarantined/`).
- **Correction:** when returning a recovered/advance-replayed pending action,
  also disposition its source file as consumed (or add an explicit
  "recovered" disposition bucket).
- **Test outline:** crash between execution-start and disposition-write,
  recover, complete the action, then assert its disposition record reads
  "consumed," not "quarantined."

### H8 — Inconsistent workspace-identity validation strength

- **Files/lines:** `AssemblyActionExecutionStore.ValidateWorkspace`
  (`~:451-465`) checks only `SessionId`. The conceptually equivalent checks
  in `AssemblyRecoveryService.ValidateWorkspace` (`~:413-426`) and
  `AssemblyTimelineReconciler.SameWorkspace` (`~:288-295`) additionally check
  `Iteration` and `Nonce`. `CandidateWorkspaceId` is fully deterministic from
  the checkpoint number in the current codebase, so this is low-probability
  today, but it's an inconsistency worth closing rather than relying on
  incidental determinism elsewhere.

### H9 — No durable "deferred conflict" counterpart exists for post-acceptance timeline divergence in polish/finalization

- **Files/lines:** `PostRoughCutPolishCoordinator.RequireRoughCutLayoutAsync`
  (`:828-846`), `RequireExactLiveSnapshotAsync` (`:848-867`),
  `ValidateRestoredSnapshot` (`:800-817`), and `FinalizationService.
  FinalizeAsync`'s reconcile call all funnel any divergence into
  `RequireRecovery()` (`PostRoughCutPolishCoordinator.cs:881-886`), which sets
  `NeedsRecovery` and throws, unwinding the entire coordinator method.
- **Contrast:** unlike `AssemblyCoordinator`'s `ReconciliationConflict` phase
  — a durable, resolvable state with its own restore/exclude/adopt/defer/
  pause/abandon options — there is no equivalent resolvable state here. The
  process must be restarted externally, or the editor must manually undo
  their VEGAS edit, and the entire `RunAsync`/`FinalizeAsync` re-invoked from
  scratch, hitting the same divergence check again.
- **Assessment:** `EDIT-LLM-010`'s "durable subphases" language covers
  rough-cut audit/review/correction/pause/acceptance, not layout-drift
  recovery specifically, so this may be intentional scope-narrowing rather
  than a contradiction — but it means the review brief's Q9 ("deferred
  reconciliation conflict" pattern) simply has no counterpart at this
  boundary, which is worth making an explicit design decision rather than an
  implicit gap.
- **Pause/resume/abandon itself, however, is solid:** both
  `PostRoughCutPolishCoordinator.HandleLifecycleAsync`/
  `ResumePersistedPauseAsync` (`:952-1124`) and
  `PostPolishFinalizationCoordinator.ResumePersistedPauseAsync`/`PauseAsync`
  (`:201-307`) journal Pause/Resume/Abandon through
  `AssemblyActionExecutionStore` with `TryRecoverClaimed`/`TryConsume`,
  mirroring `AssemblyCoordinator`'s pattern — traced by hand for a
  Begin-before-Paused-publish crash and a Paused-publish-before-Complete
  crash; both replay correctly exactly once.

## 4. Crash-boundary verdicts (per the brief's ten named windows)

| # | Boundary | Verdict | Basis |
|---|---|---|---|
| 1 | Before action consumption | Safe | Pending action file untouched; next poll re-evaluates it from scratch. |
| 2 | After consumption, before execution-start record | Safe | `TryClaimState` writes the claim file atomically via temp-file-then-`File.Move`; `beforeDisposition` (execution `Begin`) runs before the "consumed" disposition is recorded, so a crash here leaves the claim recoverable via `TryRecoverClaimed`. |
| 3 | After execution start, before live evidence capture | Safe | `ReadOrCaptureActionTimelineEvidenceAsync` re-snapshots and durably saves evidence on the next pass if none was persisted; snapshot capture is itself idempotent (content-hash compared on replay). |
| 4 | After evidence capture, before state mutation | Safe | Reconciliation/materialization re-runs deterministically from the persisted evidence; nothing mutates state before this point. |
| 5 | After state/artifact mutation, before execution completion | Safe | Accepted-plan/proposal files are written with `WriteImmutable`-style content-compare-or-throw semantics; recovery detects the mutation already happened (e.g. `acceptedCheckpoint >= pending.Action.Checkpoint`) and completes the execution as reflected rather than redoing the mutation. |
| 6 | After execution completion, before publishing the next state | Safe | `stateRevision`/phase publication is derived fresh from persisted artifacts on every recovery pass; no in-memory-only state is required to reach the next publish. |
| 7 | After last checkpoint accepted, before rough-cut handoff | Safe (transition itself) / see B1 | The `SyncPassComplete` → `Rendering` transition is republished from the persisted accepted plan; however, the *evidence* consumed just after this point at rough-cut entry is B1's baseline-less reconciliation. |
| 8 | After polish approval persisted, before materialization | Safe | `PolishPassStateRecord` is durably written to `Approved` **before** `actionExecutions.Complete` runs (`PostRoughCutPolishCoordinator.cs:195-198, 435-438`). A crash here leaves `.started.json` open but the plan state already `Approved`; recovery (`CompleteRecoveredPolishAction`, `:1020-1062`) detects `state.Status != AwaitingApproval` and completes the stale execution as `"recovered-persisted-polish-state"` without re-deciding, so materialization runs exactly once. Directly tested by `PolishPassSelfTests.cs:182-289` (`TestPersistedDecisionActionRestart`). |
| 9 | After polish materialization, before review-state publication | Safe by construction | `RunEffectsAsync`/`RunAudioAsync` are state-machine reconstructors that re-read `artifacts.ReadLatestState(...)` from disk on every loop iteration; a crash between a materialization write and the subsequent publish just re-enters the same branch on restart. Tested for the effects pass (`PolishPassSelfTests.cs:325-349`); **no equivalent test exists for the audio pass**, though it is the same code path (test gap, see §5). |
| 10a | Promotion intent written, not yet promoted | Safe | `FinalizationService` retry always resumes the one persisted promotion ID/request/fingerprint/workspace/hash set; never creates a second intent (confirmed via direct trace). |
| 10b | Promoted, report not yet written | Safe (service layer) | Idempotent report/archive reuse validated by identity, length, entry count, and SHA-256 before being trusted; a created-but-uncommitted archive is inspected and adopted rather than overwritten. |
| 10c | Report written, action not yet completed | Safe | `ReadUniqueExecution` fails closed (throws) if more than one durable execution of a given kind exists for a session, so a restart cannot accept an older or unrelated finalization attempt — directly answers brief Q8. |

## 5. Special-scrutiny questions from the brief

- **Q1** (steering directive durability): **Not a defect.**
  `AssemblySteeringDirectiveStore.SaveAcceptedDirection`
  (`AssemblyCoordinator.cs:544-547`) persists the accept action's steering
  instruction *before* `artifacts.SaveAcceptedPlan` and well before
  `actionExecutions.Complete`. The write is idempotent (keyed by
  `action.ActionId`, content-compared on replay); planning always re-reads it
  from disk, so nothing is lost to an in-memory-only variable.
- **Q2** (workspace reuse based on sufficient identity, not just a phase
  enum): **Not a defect.** Every production call site of
  `AssemblyTimelineReconciler.Apply` that matters for reuse supplies a
  baseline, and `ValidateAgainstMaterializationBaseline` checks full
  workspace identity (SessionId+Iteration+Nonce) before anything else.
- **Q3** (stale baseline paired with a newer proposal): **Not reachable
  today** (see H2's discussion) — but the invariant is unenforced, not
  actively guaranteed, so treat H2 as the actionable version of this
  question.
- **Q4** (baseline hash canonical enough for the serializer): the *snapshot*
  side is fine — `CandidateSnapshotReader` deterministically sorts tracks,
  events, velocity points, and effects before serialization/hashing, so no
  ordering-instability was found. The *broader* hashing-scheme question is
  H3 (two incompatible hashers coexist).
- **Q5** (track discovery omitting a renamed owned track): **Not a defect**
  for the traced video path — a rename produces "0 live matches," which
  `AssemblyTimelineReconciler` reports as a paired delete+add conflict rather
  than a cleaner "renamed" diagnostic (cosmetic only). **Untested** for the
  audio-track case and for `CandidatePromotionContract.Plan`'s behavior
  against a wholesale-renamed, apparently-empty workspace (see §6).
- **Q6** (audio/event gain modeled distinctly): **Defect** — see H1.
- **Q7** (`GroupSignature` stability/collision-resistance): **Defect (low/
  medium risk)** — see H4.
- **Q8** (finalization finds the exact action, not an older/unrelated one):
  **Not a defect** — `ReadUniqueExecution` fails closed on ambiguity; see
  crash boundary 10c above.
- **Q9** (pause/resume/abandon journaled at every review boundary, including
  a deferred conflict): **Partially answered.** Pause/resume/abandon
  journaling itself is rigorous everywhere it exists (sync pass, rough-cut
  audit/polish, finalization). But there is no "deferred conflict" state at
  all for post-acceptance layout divergence in polish/finalization — see H9.
- **Q10** (operation keys containing random data where crash replay needs
  stability): **Defect** — see H5. All other idempotency keys observed
  (`SnapshotAsync`'s `Guid.NewGuid()`-suffixed key, preflight/materialize
  keys) are either pure reads (a fresh key each time is harmless) or already
  deterministic; `CleanupAsync`'s default-scope branch is the one
  established mutating exception to the codebase's own pattern.

## 6. Missing deterministic tests

1. Rough-cut→polish and finalization tampering coverage for mute/solo, gain
   (track and per-event), fades/transitions, grouping, effects, and track
   add/remove — **absent** in both `FinalizationSelfTests.cs` and
   `PolishPassSelfTests.cs` (confirmed by search). Covers **B1**.
2. A test that persists baseline A, then calls `Apply` with a plan whose hash
   doesn't match `baseline.PlanSha256`, asserting rejection. Covers **H2**.
3. A test that adds a volume-envelope point to an audio event without
   changing track `Volume`, asserting the reconciler still flags it (needs a
   fake-VEGAS seam around `CandidateSnapshotReader`). Covers **H1**.
4. A test that renames every owned track in a workspace to a non-owned name
   and asserts the resulting workspace fails closed at materialize-preflight,
   cleanup, and finalization time for the **audio**-track case (the video
   case is already confirmed safe by trace, but untested), and that
   `CandidatePromotionContract.Plan` rejects a wholesale-renamed,
   apparently-empty workspace rather than treating it as pristine. Extends
   **Q5**.
5. A crash-and-retry test for `CleanupAsync` at the post-accept call site and
   at `workbench-abandon`, asserting a stable, recognizable idempotency key
   on retry. Covers **H5**.
6. A test that crashes between action-completion and cleanup, restarts, and
   asserts the superseded checkpoint's workspace is cleaned up before the
   next materialize. Covers **H6**.
7. A test that crashes between execution-start and disposition-write,
   recovers, completes the action, and asserts the disposition record reads
   "consumed" rather than "quarantined/stale-checkpoint." Covers **H7**.
8. **Coordinator-level** Pause/Resume/Abandon lifecycle tests for
   `PostRoughCutPolishCoordinator` and `PostPolishFinalizationCoordinator`.
   Currently `PolishPassSelfTests.cs` only tests the underlying
   `PolishPassWorkflow` (plan/decide/materialize/preview) — `HandleLifecycleAsync`,
   `ResumePersistedPauseAsync`, and `RequirePolishActionBinding` have **zero**
   direct coverage. `FinalizationSelfTests.cs` covers only `FinalizeMontage`
   action recovery — no `PauseSession`/`ResumeSession`/`AbandonSession` test
   exists for `PostPolishFinalizationCoordinator`. This is the single largest
   test-coverage gap found in the later-phase coordinators.
9. A crash-during-`Materializing` test for the **audio** polish pass — only
   the effects pass has one (`PolishPassSelfTests.cs:325-349`), and it is the
   same code path.
10. A test that actually triggers the `NeedsRecovery` fail-closed path (H9) —
    asserting both that it fails closed on genuine divergence and that a
    retry after external correction succeeds — and a test for
    `FinalizationService.RollbackAsync`'s hash-mismatch branch (only the
    matching-hash success path is currently tested).

## 7. Contract disagreements

- `EDIT-LLM-013` explicitly scopes its "never silently ignored" list to "the
  synchronization pass." The review brief's own "Materialization baseline
  and reconciliation" invariant list, and `EDIT-LLM-012`'s language that
  finalization "reconciles it with the complete final plan," read as broader
  than that. The code matches the *narrower* rulebook scoping — only the
  checkpoint-bound sync pass gets full baseline protection — and B1 is the
  direct consequence. Given the brief's framing ("supported timeline edits
  are adopted while unsupported edits become explicit conflicts," not
  qualified to the sync pass), this reads as the code under-delivering
  relative to stated intent rather than the rulebook being wrong. Recommend
  closing the gap in code (§8, step 1) rather than narrowing the brief's
  invariant.
- `EDIT-LLM-011`'s audio-bound-to-effects-revision claim is, by contrast,
  genuinely and rigorously enforced in code: `RunAudioAsync`'s `Approved`
  branch (`PostRoughCutPolishCoordinator.cs:460-472`) re-reads the
  persisted effects state fresh and throws unless
  `Status == Accepted && PlanSha256 == effectsHash`, checked on every pass
  through the state including recovery replays — no disagreement here.
- `EDIT-LLM-012`'s rollback claim ("must reproduce the original candidate
  snapshot hash exactly; otherwise the rename is reversed... and the rollback
  fails") is correctly implemented at the C# service layer
  (`FinalizationService.RollbackAsync:483-512` validates the hash and throws
  *before* committing a completion receipt) — but the actual reversal action
  the rulebook describes is performed by the VEGAS-side host handler, which
  is outside this review's file set. This is flagged as **unverified**, not
  a confirmed disagreement or a confirmed pass.

## 8. Recommended patch order

1. **Close B1** — capture a rough-cut-acceptance baseline snapshot and thread
   it through `RequireRoughCutLayoutAsync`, `ValidateRestoredSnapshot`, and
   both `FinalizationService` call sites so all three use the 4-arg `Apply`
   overload. Smallest change that closes the actual promotion-time exposure,
   reusing already-correct logic rather than adding new comparators.
2. **Close H2** — add the missing `PlanSha256` equality assertion inside
   `ValidateAgainstMaterializationBaseline`. One-line, closes a real gap
   between the stated and enforced invariant.
3. **Close H5** — give every `CleanupAsync` call site a deterministic scope
   (post-accept cleanup, `workbench-abandon`).
4. **Close H7** — disposition the source action file when returning a
   recovered/advance-replayed pending action, to restore audit-trail
   accuracy.
5. **Close H6** — add orphaned-prior-checkpoint-workspace detection/cleanup
   to `AssemblyRecoveryService.Inspect` or `ContinueProgressiveAsync`'s
   startup path.
6. **Close H1** — extend `CandidateEventSnapshot`/`CandidateSnapshotReader`
   to capture audio volume-envelope points and compare them the same way
   velocity envelopes already are.
7. **H3/H4/H8/H9** — lower urgency (no reachable exploit found for any of
   these today), but land before further feature work touches these files,
   since they are exactly the "worked by accident" pattern that breaks
   silently under refactors. H9 in particular deserves an explicit product
   decision (extend the deferred-conflict pattern to polish/finalization, or
   document that layout divergence there is intentionally handled by full
   restart) rather than being left as an implicit gap.
8. Add the deterministic tests from §6 alongside each fix, in the same
   order — item 8 in that list (coordinator-level pause/resume/abandon
   tests for the two later-phase coordinators) is the single highest-value
   test addition independent of any code fix above, since it currently has
   zero coverage.

Do not attempt a broader rewrite of the reconciler or the action-journal
mechanism. The sync-pass-scoped machinery (claims, execution-start/
completion records, `TryRecoverClaimed`, idempotent proposal-revision reuse,
pause/resume/abandon journaling) is correct for every crash window traced in
this review and should be *extended* to the polish/finalization boundary
(step 1 above), not replaced.
