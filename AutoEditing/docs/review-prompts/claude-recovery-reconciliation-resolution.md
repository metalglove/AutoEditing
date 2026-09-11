# Recovery and reconciliation review resolution

Date: 2026-07-27  
Source review: `claude-recovery-reconciliation-review-report.md`

## Release disposition

The blocker is closed. The workflow now persists and enforces a complete
baseline chain:

1. rendered rough-cut review baseline;
2. explicitly accepted rough-cut baseline;
3. accepted effects baseline;
4. accepted audio baseline used by final promotion.

Each comparison is checkpoint-, exact-plan-, workspace-, snapshot-content-,
and stage-bound. This permits the intended effects and audio mutations while
rejecting unrelated human divergence at every later handoff.

## Finding disposition

- **B1 — closed.** Polish entry, rejected-pass restoration, primary
  finalization, and finalization recovery all reconcile against the appropriate
  complete baseline.
- **H1 — closed for the VEGAS object model used here.** Candidate snapshots
  now capture audio-track scalar gain and every point of the track-scoped
  volume envelope. Reconciliation compares both. Event fades and event effects
  remain independently captured.
- **H2 — closed.** Reconciliation rejects a baseline whose plan SHA-256 does
  not match the exact plan being compared.
- **H3 — retained intentionally for persisted-session compatibility.**
  Integrity fields continue hashing the exact serialized bytes written to the
  artifact. `ContractHash` remains the canonical semantic-object hash used by
  promotion validation. Migrating persisted assembly hashes requires an
  explicit schema/version migration rather than an in-place algorithm swap.
- **H4 — no correctness change.** VEGAS event groups are flat membership sets,
  so a sorted member signature represents the available topology. Durable
  placement identities prevent the legacy path-collision case for new
  candidates.
- **H5 — closed.** Every live mutating cleanup replay uses a stable
  action/session/checkpoint-derived idempotency scope.
- **H6 — closed.** Recovery cleans the superseded accepted-checkpoint workspace
  before materializing the next checkpoint.
- **H7 — closed.** Recovered and advanced-replay actions receive an accurate
  recovered/consumed disposition.
- **H8 — closed.** Action evidence validates session and checkpoint iteration,
  while baseline/recovery comparison continues to validate the complete
  workspace identity including nonce.
- **H9 — explicit fail-closed product decision.** Post-acceptance divergence
  enters durable `NeedsRecovery`. After the accepted candidate is restored or
  the rough cut is reopened and re-audited, retry proceeds from persisted
  evidence. It is not silently adopted as a polish change.

## Added deterministic coverage

Coverage now includes mismatched plan hashes; mute and solo; track gain and
volume automation; fades and transitions; grouping; event effects; added and
removed tracks; audio-pass materialization replay; stable cleanup/recovery
behavior; recovered action disposition; workspace iteration mismatch; and
finalization against the accepted final-polish baseline.

Debug and Release verification pass the complete solution and every
deterministic harness. The Deploy build is installed, and hashes of the
ProgramData extension, LocalAppData LLM companion, shared contracts, and
inference monitor match their build outputs.
