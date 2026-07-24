# Limitations — Editor 2 / Project 01

For each limitation: what is unknown, why, which conclusions it affects, and what would resolve
it. Evidence IDs indexed in [evidence-register.md](evidence-register.md).

## Automation incident: reproducible VEGAS crash

**What happened**: opening a relinked copy of this project (with all 46-48 media items resolvable/
online, including a ~3.9GB `.avi` file) caused VEGAS Pro to crash with an unmanaged exception
(`0xe0434352`, fault in `KERNELBASE.dll`) before any script could execute. This was **reproduced
twice independently** (two separately-rebuilt relink passes, same crash both times), ruling out
relink-data corruption as the cause. Windows Event Log crash records around the same time window
were checked but showed only an unrelated, already-recurring crash in a VEGAS background update
service (`Service_rel_u_x64_vc16.exe`/`Qt5CoreMx64Qt5.15.1.dll`), not the actual `vegas200.exe`
crash — the specific fault details for the `vegas200.exe` crash itself were not further isolated
(no `gpu_video_x64.log`/`vegas_script_x64.log` was written for either crash, indicating the crash
occurred very early in project load, before those subsystems logged anything).

**Why it's unknown**: no automated diagnostic captured the exact trigger. The leading hypothesis
(size/age/codec of the `.avi` file causing a hardware-detection or decode-path crash during
project load) was not proven, only supported circumstantially (the crash reproduced with the file
resolvable; a load of the same project with the file offline/unresolvable did not crash, only
loaded slowly).

**What was done**: at the user's direction, the single Track-4 event referencing this file (an
8-second segment) was removed from a disposable copy, along with the one genuinely-unavailable
media reference (see below), before relinking and inspecting. This resolved the crash.

**Which conclusions it affects**: any finding about the removed 8-second segment's content or
treatment is unavailable (it was excised before inspection). All other findings in this package are
unaffected, since they come from the remaining 157/158 Track-4 events, none of which reference the
removed file.

**What would resolve it**: installing VEGAS with updated codec packs, testing the `.avi` file in
isolation, or obtaining a VEGAS crash dump for full stack-trace analysis — none attempted here.

## Missing media

One media item (a raw DVR capture originally stored under the editor's own
`C:\Users\[Editor 2, redacted]\Videos\...` folder) has no local copy available. Its single
timeline reference was removed (at the project owner's explicit direction) rather than left
offline. **Affects**: nothing else in this package depends on this file; it is not referenced by
any surviving Track-4 event.

## The original `Untitled.veg` was directly edited by the project owner

Recorded transparently in [project-profile.md](project-profile.md): pre-edit SHA-256
`903707faf5135b4805cc7ff2a1ae1c4daa40d91441e864cbd7dea615f307c7f2` (preserved intact in
`Untitled.veg.bak`), post-edit SHA-256
`4954c30b8a48e23970917d448c8f8dfbfca0593810756a690644ccbbc473d9ba`. This was a deliberate,
transparent action by the project owner (not automation) to remove the one genuinely-missing media
reference, done in direct response to a recurring dialog interruption during automated inspection
attempts. **Affects**: strict "never modify the original" provenance no longer holds for
`Untitled.veg` specifically (though it does still hold for `Untitled.veg.bak`, which remains
byte-identical to the pre-edit state). All automated work after this point operated on further
disposable copies, never on `Untitled.veg` directly.

## Reduced-scope items (time-constrained, explicitly not silently dropped)

Relative to the depth of the Editor 1 investigation, the following were **not** attempted this
pass, primarily because a large share of the available time was consumed diagnosing and working
around the crash above:

- **No controlled effect ablation** (bypass on/off comparison with pixel-metric analysis) for
  `S_Shake` or `S_FilmDamage` — visual confirmation rests on unablated representative frames only.
- **No track-vs-event effect-interaction test** analogous to Editor 1's Part-8 ablation.
- **No independent musical/tempo analysis via audio render** — the music track was not rendered
  solo, no onset-detection or tempo-estimation was performed. The marker-density observation
  (`E2-P01-TIM-001`) is structural only, not audio-verified.
- **No full crossfade-predictor breakdown** (percentage of same-source vs. source-change
  adjacencies that overlap) — only raw counts were computed.
- **No cinematic/connective-footage pairing analysis** analogous to Editor 1's
  `cinematic-pairing-analysis.md`.
- **Only 10 of the requested 16 representative moments were captured**, and several of those 10
  were not individually reviewed in detail (captured and confirmed to exist, not visually
  described).
- **No OFX keyframe time-domain fixture test** — same unresolved status as Editor 1's project.
- **No audio loudness/stem-render analysis** for Track 5 or Track 6.
- **The `SA-B 50 Hit.mp3` burst was not cross-checked against velocity-envelope shape** — a
  specific, named adversarial test from the prompt (Phase 13: "ordinary versus strong effect
  visibility") that remains open.
- **No full same-source-run escalation-pattern check** (Editor 1's Part-4/adversarial-pass finding
  that 18/24 runs show an ordinary→impact escalation) was replicated for Editor 2's 21 multi-event
  runs.

Each of these is a legitimate candidate for a follow-up pass, not a claim resolved by omission.

## Inspector API limitations (same as Editor 1's project)

- Classic (non-OFX) effect parameters (`Track Noise Gate`/`EQ`/`Compressor`) are not readable
  beyond plugin identity, for the same `ScriptPortal.Vegas` API reason documented for Editor 1's
  project.
- OFX keyframe time-domain (event-relative vs. timeline-absolute) remains asserted, not proven,
  for the same reason as Editor 1's project.

## Anything inferred only from filenames

- The curated-highlight-vs-raw/connective-footage source-tier split (see
  [timeline-structure.md](timeline-structure.md)) is inferred from filename patterns
  (`Glovali - */NEW Glovali - */opener.mp4` vs. `Call of Duty Modern Warfare 2 (2022) [date] -
  [time].DVR.mp4`), not independently verified by content review beyond the captured frames.
- Whether Track 5's audio events are exact duplicates of the corresponding Track-4 picture events'
  in/out points, or independently selected excerpts, was inferred from same-source-file matching,
  not verified event-by-event.

## Mismatch between project state and final render

The separately-existing rendered file was not frame-aligned or compared against the inspected
project (same limitation as Editor 1's package, not attempted here either).

## Cross-project scope reminder

Nothing in this document, or anywhere in this package, is evidence about any project or editor
other than Editor 2 / Project 01. See
[../../../comparisons/editor-1-project-01-vs-editor-2-project-01/README.md](../../../comparisons/editor-1-project-01-vs-editor-2-project-01/README.md)
for the cross-editor comparison, which is explicit about what two data points can and cannot
establish.
