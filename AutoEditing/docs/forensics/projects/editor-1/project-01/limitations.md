# Limitations — Editor 1 / Project 01

For each limitation: what is unknown, why, which conclusions it affects, and what would resolve
it. Evidence IDs indexed in [evidence-register.md](evidence-register.md). General inspector
engineering guidance (test plans, schema recommendations) lives in
[project-inspector-data-quality.md](../../../project-inspector-data-quality.md) (kept outside this
project package as general methodology); this document records only the **project-01-specific**
consequences of those gaps.

## Missing media

- 48/51 media items were offline on first open (paths stored from Editor 1's own machine). All 48
  were relinked and the relink was confirmed to persist across save/reopen. **Affects:** none of
  the conclusions in this package depend on unrelinked media. **Resolved:** yes, via
  `Untitled.relinked.veg`.

## Relinked media / project-copy mutations

- All analysis after the initial relink operated against `Untitled.relinked.veg`, a disposable
  copy. The original `Untitled.veg`/`Untitled.veg.bak` were never opened or modified.
- **Why this matters:** any conclusion that depended on VEGAS's own project-open-time
  media-resolution behavior (e.g., exact codec/decode fidelity) should be understood as observed
  against the relinked copy, not the editor's original file locations.
- **Resolves via:** re-verification against the original file locations was not possible (media not
  available at those paths on the inspection machine) — this is a structural limitation of
  conducting this analysis on a different machine than the one Editor 1 used.

## Missing plugins

Four plugin references could not be verified visually (Red Giant Universe "Stylize Glitch," Boris
FX Continuum "Vector Blur Dissolve" and "Damaged TV," and one identified only by class GUID
`b47a199b-15e2-4836-bd39-419904b5d292`). **Affects:** [effects-and-presets.md](effects-and-presets.md)
and [transitions-and-compositing.md](transitions-and-compositing.md) cannot describe their rendered
appearance. **Would resolve via:** installing the missing plugins on the inspection machine and
re-rendering the affected events/transitions.

## Plugin placeholders / enumeration gaps

- All 1,264 enumerated effect instances (Editor 1's count) report as valid and available, despite
  the confirmed-incomplete plugin environment. **This does not prove no effects are missing** — VEGAS
  may omit unresolved effects from the normal enumeration entirely, and the inspector never
  distinguishes `loaded`/`disabled`/`invalid`/`placeholder`/`referencedButUnavailable`/`unknown`
  presence states.
- **This investigation's structural re-search** (searching all Track-level and Event-level
  `Effects` entries in the v3 export for any of the 4 known-missing identifiers) found **zero
  matches anywhere** — consistent with, but not proof of, the belief that at least one (Vector Blur
  Dissolve) lives on a `Transition` object rather than a Track/Event effect chain.
  **Affects:** the "used as transition" classification remains an inference by elimination, not a
  direct structural confirmation. **Would resolve via:** extending the inspector to export VEGAS's
  `Transition` object (never attempted in any of v1/v2/v3).

## Inspector API limitations

- **Classic (non-OFX) effect parameters are effectively write-only for reading exact configured
  values.** `Pitch Shift`, `eFX_Reverb (VST2, 64-bit)`, `Track Noise Gate`, `Track EQ`, and `Track
  Compressor` are all classic effects. `Effect.CurrentPreset` returns `null` everywhere in this
  project (custom, non-preset values). `Effect.Keyframes` exists but `Keyframe.Preset` — the only
  field identifying *what* a keyframe switches to — has no getter at all in the installed
  `ScriptPortal.Vegas.dll` (v20.0.0.411). Confirmed: 0/143 Track-2 events and 0/9 track-level
  EQ/Compressor/Gate instances have any classic keyframes at all — so even if `Preset` were
  readable, there is nothing time-varying to read here, but the **static configured values remain
  permanently unrecoverable**.
- **Affects:** [audio-treatment.md](audio-treatment.md) (exact Pitch Shift/Reverb amounts per
  event, exact EQ/Compressor/Gate settings) and, indirectly, any attempt to fully reconstruct the
  final audio mix's loudness path.
- **Would resolve via:** reverse-engineering by ear/spectral comparison against a real render (not
  attempted), or a future VEGAS SDK/API version exposing these parameters.

## Unknown OFX time basis

- `OFXKeyframe.Time` is exported both as a raw value and as an assumed timeline-absolute value
  (`eventStart + raw`), but **no source (SDK docs, forum posts, or the reflected API itself)
  confirms which frame of reference `Time` actually uses.**
- **Direct evidence this matters**: the impact-family's first `S_Shake.Amplitude` has a stored
  keyframe at raw time `-0.116783` (negative — before its own event's nominal start) that appears
  identically across every sampled instance of that family. This is decisive evidence that raw key
  time cannot be blindly treated as event-relative visible time.
- Of 2,525 event-level OFX keyframes in the v2 export (Editor 1's count), 25 have negative raw
  time and 661 occur after their containing event's exported visible length — 686 total (27.2%)
  fall outside the nominal event span.
- **Affects:** every stated keyframe time in [effects-and-presets.md](effects-and-presets.md) —
  all such times are reported as *stored* values, not confirmed *visible/effective* timeline
  positions.
- **Would resolve via:** the controlled fixture-testing plan that was explicitly scoped but **not
  attempted** in this investigation (build a fresh disposable project, apply split/trim/copy/move
  operations to test events, read keyframe times before/after each operation, compare against
  VEGAS's own UI display). This was deprioritized as a substantial standalone engineering task,
  below the six angles this investigation's requester explicitly flagged as highest priority.

## Negative keyframes / keys beyond visible event duration

Same evidence as above (686/2,525 keys outside event span). **Not yet resolved** whether these are
valid retained keys outside a trimmed event, media-relative keys, or a different VEGAS/OFX time
convention.

## Unsupported audio parameters

See "Inspector API limitations" above — same underlying gap.

## Unknown bus/master processing

- `Project.MasterBus` and `Project.VideoBus` were confirmed to have zero effects and zero
  envelopes — this specific question **is resolved**, not open.
- What remains open: the exact parameter values of the three track-level effects (`Noise Gate`,
  `EQ`, `Compressor`) present on all 3 audio tracks, which do affect final loudness/tone but
  whose settings are unrecoverable (see above).

## Anything not visually confirmed

- The exact chain location of the 4 missing plugins.
- Whether GPU vs. CPU rendering produces meaningfully different pixels for this project's effect
  chain — not tested (would require two renders under different acceleration settings).
- Whether `S_Flicker`'s contribution to the impact-family visual result is real — a single static
  frame cannot separate "no effect" from "sampled near a low point in its own animated curve."
- Scope-in/scope-out timing relative to velocity-envelope points (see
  [velocity-findings.md](velocity-findings.md)) — no representative frame specifically targets
  this.
- The `t1_e51`/`t1_e55`/`t1_e60` first/middle/final same-source-sequence comparison proposed in
  Editor 1's validation plan (see [representative-moments.md](representative-moments.md)) — not
  captured.
- Editor 1's proposed ablation variants G (velocity-neutralized) and H (audio-muted) — not run.

## Anything inferred only from filenames

- The distinction between "curated highlight" and "raw/connective footage" source tiers (see
  [timeline-structure.md](timeline-structure.md)) is derived from folder-path naming
  (`1 - openers`/`2 - middle`/`3 - single single collat section`/`4 - closers` vs. `cines`/`map
  cines`), not from visual content classification. This is flagged as a filename-based inference
  in every document that relies on it.
- Whether raw/connective-footage sources genuinely depict cinematic/establishing content (vs.
  ordinary gameplay setup shots) was Editor 1's own explicitly-labeled medium-confidence inference,
  not independently re-verified by rendered-frame review in this investigation beyond the single
  `t1_e0` frame.

## Mismatch between project state and final render

- The separately-existing rendered file `montage 4.mp4` was **never independently verified** to be
  frame-time-aligned with the inspected `.veg` project. All visual verification in this
  investigation used `SaveSnapshot` captures taken directly from the live project, not frames
  extracted from `montage 4.mp4`. Any comparison between the two would need this alignment step
  first.

## Claims that cannot be reproduced in the current VEGAS environment

- Rendering with the 4 missing plugins present (not installed on the inspection machine).
- Exact `Pitch Shift`/`Reverb`/`EQ`/`Compressor` parameter reproduction (API limitation, not an
  environment/licensing issue).

## Open reconciliation item: Family A effect membership

[effects-and-presets.md](effects-and-presets.md) flags a discrepancy between Editor 1's original
4-effect-chain description of the "ordinary" family (`S_Shake → S_BlurMoCurves → Bump Map →
S_Glow`, implying `S_Shake` is live) and this investigation's direct per-event inspection of
`t1_e3`, which found `S_Shake` and `Bump Map` explicitly `Bypass=true` on that specific event (only
`S_BlurMoCurves` and `S_Glow` non-bypassed). Both observations are reported as directly read from
the project; they have not been reconciled against each other (e.g. by checking whether bypass
state varies across the 187+36 instances of this family, which was not tested).

## Cross-project scope reminder

Nothing in this document — or anywhere in this package — should be read as evidence about any
other project, editor, or the sniper-montage genre generally. A second reference project prompt
already exists (`AutoEditing/docs/forensics/claude-project-5-analysis-prompt.md`, project
identity/editor not yet established) for exactly this reason: two projects are the minimum needed
before any cross-project claim can be tested.
