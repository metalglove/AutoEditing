# Editor 1 / Project 01

Canonical evidence key: `editor-1/project-01`. Original project title: **"montage 4"** (the
editor's own internal project/folder naming). This is a forensic evidence package for one VEGAS
Pro 20 sniper-montage project, produced by a freelance editor referred to throughout this
repository only as **Editor 1** (`editor-1`) — their real name/alias has been removed and no
mapping to it is recorded anywhere in this repository. The person the montage was made for is a
separate individual, referred to by their own choice in this material as `glovali` (this is not
the editor's identity and is preserved unchanged, per the anonymization scope of this reorganization).

## What this package is, and is not

This package contains **project-specific evidence**, organized for eventual comparison with a
second reference project (a prompt for that second investigation already exists —
[`../../../claude-project-5-analysis-prompt.md`](../../../claude-project-5-analysis-prompt.md) — and is
kept as a future-work document, not part of this package, since its editor identity is not yet
established). **Nothing in this package should be read as a universal sniper-montage convention.**
Every finding is scoped to this one project (Direct Project Observation) or to a pattern that
repeats multiple times within this one project (Editor 1 / Project 01 Pattern). Claims that go
further than that are explicitly labeled as cross-project hypotheses or production-rule candidates,
neither of which is established yet.

## Inspection status

- **Original files never modified.** `Untitled.veg`/`Untitled.veg.bak` were never opened or saved
  by any script across the entire investigation. All work operated against a disposable relinked
  copy, `Untitled.relinked.veg`. See [project-profile.md](project-profile.md).
- **Three inspector export versions** exist (v1/v2/v3 schema), all preserved unmodified at
  `C:\VEGAS\` (raw research artifacts, not copied into this repository).
- **16 representative frames, 14 impact-ablation frames, 8 track/event-interaction frames, and 3
  ordinary-treatment ablation frames** were captured directly from the live project via VEGAS's
  scripting API and reviewed. See [representative-moments.md](representative-moments.md).
- **A one-track (music-only) solo render** was produced for independent audio analysis. No
  synced audio+video render segment and no full-mix or SFX-solo render were produced — see
  [limitations.md](limitations.md).
- Two independent forensic passes contributed to this package: Editor 1's own original structural/
  audio/OFX/clustering reports, and a separate adversarial-verification pass that actively
  attempted to falsify the first pass's conclusions and reconcile discrepancies. Both are cited
  throughout; where they disagreed, the resolution is recorded in
  [evidence-register.md](evidence-register.md).

## Document index

| Document | Covers |
|---|---|
| [project-profile.md](project-profile.md) | Identity, VEGAS version, duration/frame rate, track/event/media/marker counts, plugin availability, inspection-copy provenance |
| [timeline-structure.md](timeline-structure.md) | Track organization, compositing hierarchy, section structure, cinematic/connective-clip pairing, marker relationships, negative findings |
| [velocity-findings.md](velocity-findings.md) | Velocity-envelope families, plateau values, kill/impact relationships (or lack thereof), multi-kill escalation pattern |
| [effects-and-presets.md](effects-and-presets.md) | Effect-chain families (A–E), full-corpus preset-signature clustering, render-confirmed ablation results, per-preset structured records |
| [audio-treatment.md](audio-treatment.md) | Music/SFX placement and gain, replacement-weapon-SFX mechanism, retained-audio rule, whoosh transitions, marker/cut alignment |
| [transitions-and-compositing.md](transitions-and-compositing.md) | Hard cuts vs. crossfades and their predictors, overlay compositing, Track Motion/Pan-Crop (both unused), missing-plugin dependency |
| [representative-moments.md](representative-moments.md) | Indexed frame captures with structural/visual/semantic confidence per moment |
| [evidence-register.md](evidence-register.md) | Every claim in this package, with a stable ID, classification, and traceability; proposed promotions requiring cross-editor validation |
| [limitations.md](limitations.md) | Every known inspection/API/methodology gap, what it affects, and what would resolve it |

## Which conclusions are direct observations, and which are interpretations

The evidence register classifies every claim into exactly one of four tiers:

1. **Direct project observation** — read or measured directly from the project or a controlled
   render. The large majority of this package's content.
2. **Editor 1 / Project 01 pattern** — a pattern repeated multiple times within this one project
   (e.g. the ordinary→impact hard-cut rule, holding across 21/21 confirmed instances). Still scoped
   to this one project.
3. **Cross-project hypothesis** — proposed for testing against Editor 2; not yet tested.
4. **Production-rule candidate** — proposed for AutoEditing, with an explicit statement of what
   comparison or test is still required. See the "Proposed promotions" table in
   [evidence-register.md](evidence-register.md).

## Strongest findings (project-specific, not universal rules)

- This project contains no Pan/Crop animation anywhere; whatever visual impact its cuts have does
  not come from a zoom/punch-in mechanism.
- This project's "impact" visual treatment is render-confirmed to be driven primarily by a Motion
  Blur toggle on a Sapphire Shake effect (not by the effect's Z-Distance parameter, and not a
  screen-pump zoom), with a secondary RGB-distortion component. This project's "ordinary" cut
  treatment is measurably real (a ~28% edge-energy softening, render-confirmed) but subtle enough
  that it likely isn't consciously noticed at normal viewing speed.
- This project's crossfade behavior decomposes into three deterministic predictors: a source
  change always overlaps (100%, n=45); a same-source internal split overlaps about 30% of the time
  (n=230); an escalation from ordinary to impact treatment is always a hard cut (0%, n=21, zero
  exceptions). The crossfades themselves carry the signature of VEGAS's automatic
  overlap-based crossfade generation, not a hand-authored transition palette.
- This project's 125 replacement gunshot-SFX events all derive from one fixed source excerpt,
  individually processed per event by a `Pitch Shift → Reverb` chain — exact per-event values are
  not recoverable through the available scripting API, but the mechanism itself is fully confirmed.
- This project retains native gameplay audio specifically on raw/unedited DVR footage that
  receives no per-event visual effect treatment (18/24 such events, 100% deterministic by source
  folder), while curated highlight-clip footage without effects (6/24) gets no retained audio.
- This project has zero volume-automation envelopes anywhere (track, bus, or master level) — any
  perceived "ducking" of the music under gunfire in this project is psychoacoustic masking, not an
  authored mix technique.
- This project's timeline markers sit on an estimated ~115–120 BPM half-time beat grid, but no
  single fixed-tempo, fixed-phase grid can be extrapolated across the full 240-second runtime —
  local marker spacing is real but the song is not on a rigid global metronome.

## Repeated patterns within this project (not yet cross-project)

- Raw/connective footage clips are placed, 68% of the time, directly between an impact-treated
  clip and the setup portion of the next curated clip, and crossfade in and out 100% of the time
  (the opposite rule from the ordinary→impact hard cut) — see
  [timeline-structure.md](timeline-structure.md) and
  [transitions-and-compositing.md](transitions-and-compositing.md).
- 18 of 24 multi-event same-source runs in this project follow an escalation pattern: a run of
  ordinary-treated cuts culminating in an impact-treated cut at or near the run's end — see
  [velocity-findings.md](velocity-findings.md).
- Whoosh transition sounds precede 86% of connective-clip entries by roughly 2–3.5 seconds in this
  project, functioning as a lead-in cue rather than a beat-locked hit.

## Findings ready for comparison with Editor 2

See the "Proposed promotions" table in [evidence-register.md](evidence-register.md) for the full
list with required evidence IDs and validation steps.

## Known gaps

The OFX keyframe time-reference-frame question (event-relative vs. timeline-absolute) was never
conclusively resolved; the exact chain location of 4 referenced-but-missing plugins is unconfirmed;
exact Pitch Shift/Reverb/EQ/Compressor parameter values are permanently unrecoverable through the
current tooling; no full-mix or GPU-vs-CPU render comparison was produced. Full detail, and what
would resolve each, in [limitations.md](limitations.md).
