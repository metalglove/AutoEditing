# Clip-by-clip workflow completion audit

Date: 2026-07-27

This document is the requirement-to-evidence index for
`llm-clip-by-clip-workflow-plan.md`. A checked item in the implementation plan
is not considered complete unless production behavior, deterministic evidence,
and the applicable editing rule agree.

## Phase evidence

| Phase | Production evidence | Deterministic evidence | Rule |
|---|---|---|---|
| 1. Correct synchronization core | `AssemblyCoordinator`, `AssemblyTimelineReconciler`, `ClipStepDecisionCompiler`, durable action and execution stores | `AssemblyWorkflowSelfTests`, `AssemblyActionStoreSelfTests`, `AssemblyActionExecutionStoreSelfTests`, `ClipStepDecisionCompilerSelfTests`, `AssemblyReconciliationConflictSelfTests` | EDIT-LLM-004, 005, 006, 013 |
| 2. Progressive planner | `LlmProgressiveAssemblyPlanner`, compact strict schemas, accepted-prefix context, semantic sketch and one-clip decisions | `ProgressiveAssemblyPlannerSelfTests`, `ProgressiveAssemblyWorkflowSelfTests` | EDIT-LLM-002, 003, 007 |
| 3. Human review and workbench | `ShotReviewViewModel`, `ShotReviewWindow.xaml`, `FileWorkbenchProjectionReader`, steering and reconciliation projections | `WorkbenchSessionProjectionSelfTests`, `WorkbenchProjectionSelfTests`, `AssemblySteeringDirectiveStoreSelfTests` | EDIT-LLM-004, 006, 014 |
| 4. Lifecycle and recovery | `AssemblyRecoveryService`, runtime lease, immutable artifacts, project/workspace checks, idempotent action journal, and shared post-sync abandon cleanup/reconciliation | `AssemblyRecoverySelfTests`, action-store tests, reconciliation-conflict tests, `CandidateAbandonCleanupSelfTests`, final-review direct/reflected abandon tests | EDIT-LLM-008 |
| 5. Checkpoint rendering | `CheckpointPreviewPipeline`, native VEGAS preview/frame adapters, immutable attempt store, section milestones | `CheckpointPreviewPipelineSelfTests`, `SectionMilestoneRenderSelfTests`, VEGAS render-policy tests | EDIT-LLM-009 |
| 6. Full rough-cut audit | `PostSyncRoughCutCoordinator`, evidence capture, strict multimodal audit, targeted reopen and recovery stores | `RoughCutAuditSelfTests` | EDIT-LLM-010 |
| 7. Effects and audio polish | `PostRoughCutPolishCoordinator`, separate versioned plans, exact approval, materialization baseline chain | `PolishPassSelfTests`, unsupported-change reconciliation tests | EDIT-LLM-011, 013 |
| 8. Final review and promotion | `PostPolishFinalizationCoordinator`, `FinalizationService`, promotion intent/recovery bundle/rollback | `FinalizationSelfTests` | EDIT-LLM-012, 013 |
| Cross-cutting observability | recording inference client, progress monitor, session/model usage stores, inference monitor and consequence-aware UI | inference, workbench projection, and iteration execution self-tests | EDIT-LLM-001, 003, 008 |

## Required scenario matrix

| # | Required scenario | Deterministic evidence |
|---:|---|---|
| 1 | Manual trim followed by revision | `AssemblyWorkflowSelfTests.TestMultiClipReviseAndAccept` and progressive revision tests |
| 2 | Manual trim followed by acceptance | `AssemblyWorkflowSelfTests.TestMultiClipReviseAndAccept` |
| 3 | Manual timeline move followed by acceptance | `AssemblyWorkflowSelfTests.TestMultiClipReviseAndAccept` |
| 4 | Expected event deletion | `AssemblyWorkflowSelfTests.TestMissingExtraAndDuplicateEvents` |
| 5 | Unexpected event addition | `AssemblyWorkflowSelfTests.TestMissingExtraAndDuplicateEvents`; explicit adopt/exclude coverage in `AssemblyReconciliationConflictSelfTests` |
| 6 | Ambiguous event identity | `AssemblyWorkflowSelfTests.TestMissingExtraAndDuplicateEvents` and durable-identity coverage |
| 7 | Accepted placement conflicts with next proposal | `AssemblyWorkflowSelfTests.TestAcceptedAdjustmentConflictIsRejected` |
| 8 | Duplicate button clicks | concurrent exactly-once and idempotent replay tests in both assembly action stores |
| 9 | Stale checkpoint command | `AssemblyActionStoreSelfTests.TestMalformedAndStaleActionsDoNotBlockValidAction` |
| 10 | Contradictory commands for one state version | `AssemblyActionStoreSelfTests.TestConflictingActionsForOneStateAreQuarantined` |
| 11 | Markdown-fenced JSON | `ProgressiveAssemblyPlannerSelfTests` fenced semantic-sketch response |
| 12 | Malformed or incomplete JSON | `ProgressiveAssemblyPlannerSelfTests` malformed fenced and empty-object cases |
| 13 | Semantically invalid parseable JSON | `ProgressiveAssemblyPlannerSelfTests` wrong-checkpoint clip decision; compiler semantic tests |
| 14 | llama.cpp timeout or disconnect | `LlamaCppInferenceSelfTests` transient retry and permanent-failure cases; preview automation timeout classification |
| 15 | Companion restart during generation | `AssemblyRecoverySelfTests.TestCreatingSketchRecovery` performs a real resume, regenerates only the missing sketch, plans one clip, and reaches a durable review action |
| 16 | VEGAS restart during human review | `AssemblyRecoverySelfTests` reconstructs the exact persisted proposal/revision and validates stable saved-project identity before coordinator resume |
| 17 | Resume with a diverged timeline | `AssemblyRecoverySelfTests` workspace/timeline divergence; reconciliation resolution restart tests |
| 18 | Final checkpoint acceptance | progressive coordinator completion tests |
| 19 | Early sync completion with unused media | `ProgressiveAssemblyWorkflowSelfTests.TestEarlySyncCompletion` and `WorkbenchSessionProjectionSelfTests.TestEarlySyncCompletionPresentation` |
| 20 | Project reopened after sync completion | `AssemblyRecoverySelfTests.TestSyncPassCompleteRecovery` resumes directly at mandatory rough-cut rendering without rematerialization |
| 21 | Render cancellation and render failure | `CheckpointPreviewPipelineSelfTests.TestCancellation` and `TestRenderFailure` |
| 22 | Reopening an accepted checkpoint | approved/deferred correction and workflow-recovery cases in `RoughCutAuditSelfTests` |
| 23 | Final candidate promotion and rollback | `FinalizationSelfTests`, including interrupted promotion recovery and unsupported live-timeline changes |

## Independent recovery and reconciliation review

The independent Claude review is preserved in
`review-prompts/claude-recovery-reconciliation-review-report.md`. Its B1
blocker and H1-H9 findings are resolved or explicitly dispositioned in
`review-prompts/claude-recovery-reconciliation-resolution.md`.

Most importantly, every rough-cut-to-polish, rejected-pass restoration, and
finalization reconciliation now supplies a complete stage-appropriate
`CandidateMaterializationBaseline`. Reconciliation is therefore not limited to
video geometry: it also protects track membership and state, event grouping,
fades, effects, transitions, gain, and volume automation.

## Verification result

The final code state passed all release gates:

1. Debug solution build and every deterministic harness through `verify.ps1`:
   passed with zero warnings and errors.
2. Release solution build and every deterministic harness through
   `verify.ps1`: passed with zero warnings and errors.
3. Deploy build and installation: passed with zero warnings and errors.
4. `git diff --check`: passed.
5. Installed ProgramData and LocalAppData assembly hashes equal the Deploy
   outputs for the extension bootstrap, Core, Domain, AutomaticEditor, VEGAS
   adapter, shared iteration contracts, LLM editor, and inference monitor.
