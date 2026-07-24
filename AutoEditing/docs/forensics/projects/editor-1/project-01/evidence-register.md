# Evidence Register — Editor 1 / Project 01

Canonical evidence key: `editor-1/project-01`. Every significant conclusion across this package is
listed here with a stable ID, classification, and traceability. Classification values:

- `DIRECT_PROJECT_OBSERVATION` — read/measured directly from the project or a controlled render.
- `EDITOR_1_PROJECT_PATTERN` — a pattern repeated multiple times within this one project; not yet
  tested against a second editor/project.
- `CROSS_PROJECT_HYPOTHESIS` — proposed for comparison with Editor 2 once available; not yet tested.
- `PRODUCTION_RULE_CANDIDATE` — proposed for AutoEditing, with explicit remaining validation needs.

"Evidence source" abbreviations: **C:\VEGAS** = raw research artifacts under `C:\VEGAS\` (JSON
exports, scripts, frame captures — preserved unmodified, not copied into this repository);
**E1-orig** = Editor 1's own original forensic reports (now split into this package, originals
replaced with redirect stubs — see [README.md](README.md)).

## Structure (STR)

| ID | Claim (abbreviated) | Scope | Classification | Source | Obs/Inf | Struct. conf. | Visual conf. | Semantic conf. | Files/timestamps | Limitations | Cross-proj? | Prod-rule? |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| STR-001 | Track 0/1 two-layer composite, no nested hierarchy | project | DIRECT_PROJECT_OBSERVATION | C:\VEGAS project-inspection-v3.json | Observation | High | n/a | n/a | tracks[0,1] | none | No | No |
| STR-002 | Project pixel format Float32Bit | project | DIRECT_PROJECT_OBSERVATION | C:\VEGAS project-inspection-v3.json | Observation | High | n/a | n/a | `video` object | none | No | No |
| STR-003 | 22 events sourced from raw/connective folders (`cines`/`map cines`) | Track 1 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS folder-structure.js output | Observation | High | n/a | Medium (role inferred from folder name) | 22 t1_e* events | filename-based inference, see limitations.md | No | No |
| STR-004 | No environment-only b-roll category exists | Track 1 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS folder-samples.js output | Observation | High | Low (1 frame reviewed) | Medium | — | see limitations.md | No | No |
| STR-005 | Track-level static S_Shake/S_Flicker, unanimated | Track 1 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS project-inspection-v3.json | Observation | High | Medium (via ablation VIS-003) | Medium | — | none | No | No |
| STR-006 | TrackMotion confirmed unused, both video tracks | project | DIRECT_PROJECT_OBSERVATION | C:\VEGAS project-inspection-v3.json | Observation | High | n/a | n/a | — | none | No | No |
| STR-007 | Body order not chronological/filename order | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig project-structure-forensics.md | Observation | High | n/a | Low (selection rationale unknown) | 20 named sources | selection criterion unknown | Yes (candidate) | No |
| STR-008 | 276 events reduce to 44 same-source runs; curated sources split far more aggressively | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig + C:\VEGAS multikill-source-runs.js | Observation | High | n/a | n/a | — | none | Yes (candidate) | No |
| STR-009 | Opener structure: 3 cinematic + Opener01/02/03 interleaved with bridges | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig project-structure-forensics.md | Observation | High | n/a | n/a | 26.643–69.303s | none | No | No |
| STR-010 | 15/22 raw/connective clips sit impact→connective→ordinary; 91% same-section | Track 1 | EDITOR_1_PROJECT_PATTERN | C:\VEGAS cinematic-pairing-analysis.md | Observation | High | n/a | Medium | 22 raw/connective events | single-project only | Yes | Yes (experimental) |
| STR-011 | 15/19 body gameplay transitions mediated by a raw/connective clip; 4 direct exceptions | Track 1 | EDITOR_1_PROJECT_PATTERN | E1-orig project-structure-forensics.md | Observation | High | n/a | Medium | 4 named exceptions | single-project only | Yes | No |
| STR-012 | Bridges begin off-marker; incoming featured clip privileged at anchor | Track 1 | EDITOR_1_PROJECT_PATTERN | E1-orig project-structure-forensics.md | Observation | High | n/a | Medium | — | single-project only | Yes | No |
| STR-013 | 114/275 (41%) Track-1 adjacencies overlap | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig + C:\VEGAS crossfade-predictors.js | Observation | High | n/a | n/a | — | none | No | No |
| STR-014 | 150/154 markers within 1 frame of an event boundary; not "every clip on a marker" | project | DIRECT_PROJECT_OBSERVATION | E1-orig project-structure-forensics.md | Observation | High | n/a | n/a | — | none | No | No |
| TIM-001 | Markers sit on ~115–120 BPM half-time grid; no rigid global grid holds | project | DIRECT_PROJECT_OBSERVATION | C:\VEGAS musical-alignment-analysis.md | Observation | High | n/a | n/a | song-solo.wav, marker-grid-analysis.json | onset-detection is a weak standalone estimator, see file | Yes | Yes (should change now) |
| STR-015 | 0 nested-project media references | project | DIRECT_PROJECT_OBSERVATION | C:\VEGAS project-inspection-v3.json | Observation | High | n/a | n/a | — | none | No | No |
| STR-016 | No Pan/Crop animation anywhere (276/276 events, 1 static keyframe each) | Track 1 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS project-inspection-v3.json | Observation | High | n/a | n/a | — | none | Yes (candidate) | No |
| STR-017 | One reversed subclip found, used once | project | DIRECT_PROJECT_OBSERVATION | C:\VEGAS project-inspection-v3.json media list | Observation | High | n/a | n/a | reversed DVR subclip | none | No | No |
| STR-018 | No masks/chroma keys/generated media anywhere | project | DIRECT_PROJECT_OBSERVATION | C:\VEGAS project-inspection-v3.json | Observation | High | n/a | n/a | — | none | No | No |

## Timing (TIM)

| ID | Claim | Scope | Classification | Source | Obs/Inf | Struct. | Visual | Semantic | Files/timestamps | Limitations | Cross-proj? | Prod-rule? |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| TIM-001 | (see STR table above) | | | | | | | | | | | |
| TIM-002 | 122/125 gun events within 30ms of a marker (97.6%), n extended from 25 to 125 | Track 2/1 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS kill-alignment-corpus.json | Observation | High | n/a | Medium (3 confirmed counterexamples) | t2_e103/104/105 | none | Yes | Yes (should change now) |
| TIM-003 | 122/125 gun events within 1ms of marker+boundary+velocity point simultaneously (Editor 1's original n=125 count, schema-1) | Track 2/1 | DIRECT_PROJECT_OBSERVATION | E1-orig project-audio-forensics.md | Observation | High | n/a | Medium | 3 exceptions at 155.956/180.897/181.765s | none | Yes | Yes (should change now) |

## Velocity (VEL)

| ID | Claim | Scope | Classification | Source | Obs/Inf | Struct. | Visual | Semantic | Limitations | Cross-proj? | Prod-rule? |
|---|---|---|---|---|---|---|---|---|---|---|---|
| VEL-001 | 7 velocity curve-shape families, dominant `fast→slow→slow→fast` (147) | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig project-preset-clustering.md | Observation | High | n/a | n/a | none | Yes (candidate) | No |
| VEL-002 | 4-point curves dominate (188/275) in independent recount | Track 1 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS inferred-editing-strategies.md | Observation | High | n/a | n/a | grouping differs slightly from VEL-001 | No | No |
| VEL-003 | Plateau median 0.5x, mean 0.52x, n=275 | Track 1 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS inferred-editing-strategies.md | Observation | High | n/a | n/a | none | Yes (candidate) | No |
| VEL-004 | Entry/exit median ~2.87x/2.76x, both above 1x | Track 1 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS inferred-editing-strategies.md | Observation | High | n/a | n/a | none | Yes (candidate) | No |
| VEL-005 | Parameterized velocity family, not one hard envelope | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig project-preset-clustering.md | Observation | High | n/a | n/a | none | Yes (candidate) | Yes (experimental) |
| VEL-006 | Sample envelope shape, t1_e0, 4 points | Track 1 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS inferred-editing-strategies.md | Observation | High | n/a | n/a | superseded by VEL-003 for the plateau figure | No | No |
| VEL-007 | Terminal-family velocity skews non-recovering (11 four-point, 6 three-point leading shapes) | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig project-preset-clustering.md | Observation | High | n/a | n/a | none | No | No |
| VEL-008 | Velocity plateau does NOT distinguish impact from ordinary family (0.492 vs 0.523 mean, n=25/250) | Track 1 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS inferred-editing-strategies.md | Observation | High | n/a | n/a | none | Yes (candidate) | No |
| VEL-009 | Stored velocity params identical for first vs mid-sequence event in same run (t1_e51/t1_e55) | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig project-preset-clustering.md | Observation | High | Low (not rendered) | Low | not visually confirmed | Yes (candidate) | No |
| VEL-010 | 18/24 multi-event source-runs follow ordinary→impact escalation pattern | Track 1 | EDITOR_1_PROJECT_PATTERN | C:\VEGAS multikill-source-runs.js + inferred-editing-strategies.md §7 | Observation | High | n/a | Medium | single-project only | Yes | Yes (experimental) |
| VEL-011 | 24 no-effects events still have velocity envelopes | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig project-preset-clustering.md | Observation | High | n/a | n/a | none | No | No |
| VEL-012 | 1/276 standard-chain event has no velocity envelope at all | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig project-preset-clustering.md | Observation | High | n/a | n/a | unexplained exception | No | No |

## Effects and presets (FX)

| ID | Claim | Scope | Classification | Source | Obs/Inf | Struct. | Visual | Semantic | Limitations | Cross-proj? | Prod-rule? |
|---|---|---|---|---|---|---|---|---|---|---|---|
| FX-001 | Project-wide effect instance counts (12 effect types) | project | DIRECT_PROJECT_OBSERVATION | E1-orig project-inspection-ofx-findings.md | Observation | High | n/a | n/a | none | No | No |
| FX-002 | 7 distinct full-corpus signatures, not 2 (falsifies "byte-identical" claim) | Track 1 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS full-corpus-preset-signatures.json | Observation | High | n/a | n/a | none | No | No |
| FX-003 | t1_e275 has no gun audio at start; trimmed tail, not independent kill | Track 1/2 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS query | Observation | High | Medium | Medium | single query, not re-verified | No | No |
| FX-004 | Ordinary-family stored parameters, 2 duration variants | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig forensic-synthesis.md | Observation | High | n/a | n/a | none | Yes (candidate) | Yes (experimental) |
| FX-005 | Reconciliation gap: S_Shake/Bump Map bypass state discrepancy | Track 1 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS direct query vs E1-orig chain description | Observation | Medium (unreconciled) | n/a | n/a | see limitations.md | No | No |
| FX-006 | Family A broader than "kill preset" — only 95/223 begin with gun audio | Track 1 | DIRECT_PROJECT_OBSERVATION → interpretation is EDITOR_1_PROJECT_PATTERN | E1-orig project-preset-clustering.md | Observation | High | n/a | Medium | none | Yes | No |
| FX-007 | Impact-family stored parameters incl. negative keyframe, Motion Blur forced true | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig forensic-synthesis.md + C:\VEGAS ablation | Observation | High | High (via VIS-002) | Medium | negative-key time basis unresolved | Yes (candidate) | Yes (experimental) |
| FX-008 | 25/26 impact events begin on marker+gun audio; 19/26 end a source run; not universal | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig project-preset-clustering.md | Observation | High | n/a | Medium | 7 counterexamples preserved | Yes | No |
| FX-009 | 24 no-effects events, mostly single-event source runs | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig project-preset-clustering.md | Observation | High | n/a | n/a | none | No | No |
| FX-010 | 18/24 no-effects events pair with retained audio (raw folders); 6/24 (curated folders) don't | Track 1/2 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS retained-gameplay-audio-analysis.json | Observation | High | n/a | Medium | filename-based folder inference | Yes | Yes (experimental) |
| FX-011 | Intro-flicker family, 2 events, ~4.2-4.3s | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig project-preset-clustering.md | Observation | High | n/a | n/a | n=2, low sample | No | No |
| FX-012 | Monochrome accent, 1 event | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig + C:\VEGAS frame | Observation | High | High | Low | n=1 | No | No |
| FX-013 | EffectPreset.Name returns (Default) everywhere | project | DIRECT_PROJECT_OBSERVATION | E1-orig project-inspection-ofx-findings.md | Observation | High | n/a | n/a | none | No | No |

## Audio (AUD)

| ID | Claim | Scope | Classification | Source | Obs/Inf | Struct. | Visual | Semantic | Limitations | Cross-proj? | Prod-rule? |
|---|---|---|---|---|---|---|---|---|---|---|---|
| AUD-001 | Music placement/gain/fade, -3.0dB track, 3.086s fade-in, 0 envelopes | Track 3 | DIRECT_PROJECT_OBSERVATION | E1-orig project-audio-forensics.md | Observation | High | n/a | n/a | none | Yes (candidate) | Yes (experimental) |
| AUD-002 | 0 envelopes at track/bus/master everywhere; no ducking mechanism exists | project | DIRECT_PROJECT_OBSERVATION | C:\VEGAS audio-mix-verification.md | Observation | High | n/a | n/a | none | No | Yes (should not generalize as a technique to copy — it's an absence) |
| AUD-003 | 18 retained-audio events, structural profile | Track 2 | DIRECT_PROJECT_OBSERVATION | E1-orig project-audio-forensics.md | Observation | High | n/a | n/a | none | Yes | No |
| AUD-004 | 18/24 no-effects video events pair 1:1 with retained audio; deterministic by source folder | Track 1/2 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS retained-gameplay-audio-analysis.json | Observation | High | n/a | Medium | filename-based folder inference | Yes | Yes (experimental) |
| AUD-005 | 125 gun-SFX events, one source offset, duration/fade stats | Track 2 | DIRECT_PROJECT_OBSERVATION | E1-orig project-audio-forensics.md | Observation | High | n/a | n/a | none | Yes (candidate) | Yes (experimental) |
| AUD-006 | Pitch Shift → Reverb chain on all 125 gun events | Track 2 | DIRECT_PROJECT_OBSERVATION | E1-orig project-audio-forensics.md | Observation | High | n/a | n/a | exact values unrecoverable (LIM-002) | Yes | Yes (should change now, mechanism only) |
| AUD-007 | 118/125 gun events ARE normalized (+7.35dB) — corrects an earlier false claim | Track 2 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS audio-treatment-recipes.md correction | Observation | High | n/a | n/a | none | Yes (candidate) | No |
| AUD-008 | Fade.Gain is a static per-event trim, not a curve value | Track 2/1 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS direct field query | Observation | High | n/a | n/a | none | No | No |
| TIM-002/003 | (see Timing table) | | | | | | | | | | |
| AUD-009 | First/middle/final-kill audio recipe NOT established from source-level data | Track 2 | DIRECT_PROJECT_OBSERVATION | E1-orig project-audio-forensics.md | Observation | High | n/a | Low | classic-effect values unrecoverable | No | No |
| AUD-010 | Whoosh structural profile, 24 events, 1 file, precedes transitions by 1.85-3.49s | Track 4 | DIRECT_PROJECT_OBSERVATION | E1-orig project-audio-forensics.md | Observation | High | n/a | Medium | none | Yes (candidate) | Yes (experimental) |
| AUD-011 | Whoosh = lead-in cue for connective cutaways specifically | Track 4/1 | EDITOR_1_PROJECT_PATTERN | C:\VEGAS cinematic-pairing-analysis.md | Observation | High | n/a | Medium | single-project only | Yes | No |
| AUD-012 | Single-excerpt reuse confirmed for all 125 gun events | Track 2 | DIRECT_PROJECT_OBSERVATION | E1-orig project-audio-forensics.md | Observation | High | n/a | n/a | none | Yes | Yes (should change now, mechanism) |
| AUD-013 | Music track peak -2.47dBFS, RMS -11.64dBFS, no clipping | Track 3 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS song-loudness.js output | Observation | High | n/a | n/a | music track only, no SFX/full-mix render | No | No |

## Transitions (TRN)

| ID | Claim | Scope | Classification | Source | Obs/Inf | Struct. | Visual | Semantic | Limitations | Cross-proj? | Prod-rule? |
|---|---|---|---|---|---|---|---|---|---|---|---|
| TRN-001 | 114/275 (41%) overlap, 0 positive gaps | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig project-structure-forensics.md | Observation | High | n/a | n/a | none | Yes (candidate) | No |
| TRN-002 | Source-change=100%, same-source=30%, ordinary→impact=0% overlap | Track 1 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS crossfade-predictor-analysis.md | Observation | High | n/a | n/a | none | Yes | Yes (should change now) |
| TRN-003 | Overlap duration stats by adjacency type | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig project-structure-forensics.md | Observation | High | n/a | n/a | frame-rate-mixture caveat | No | No |
| TRN-004 | Crossfade mechanism = VEGAS automatic (100% Smooth/Smooth, 98% symmetric) | Track 1 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS crossfade-fade-detail.json | Observation | High | n/a | n/a | Transition object never exported, see LIM-001 | No | No |
| TRN-005 | Source-change overlap 100% vs same-source 30% | Track 1 | DIRECT_PROJECT_OBSERVATION | (duplicate of TRN-002, retained for provenance) | Observation | High | n/a | n/a | none | Yes | Yes (should change now) |
| TRN-006 | 100% crossfade in/out on raw/connective clips; 86% preceded by whoosh | Track 1/4 | EDITOR_1_PROJECT_PATTERN | C:\VEGAS cinematic-pairing-analysis.md | Observation | High | n/a | Medium | single-project only | Yes | Yes (experimental) |
| TRN-007 | Track-0 overlay: Screen blend, compositeLevel=1, visually confirmed subtle | project | DIRECT_PROJECT_OBSERVATION | E1-orig + C:\VEGAS representative frame | Observation | High | High | Medium | none | Yes (candidate) | No |
| TRN-008 | Track compositing modes: Track0=Screen, Track1=SrcAlpha | project | DIRECT_PROJECT_OBSERVATION | C:\VEGAS project-inspection-v3.json | Observation | High | n/a | n/a | none | No | No |
| TRN-009 | Track Motion confirmed unused | project | DIRECT_PROJECT_OBSERVATION | C:\VEGAS project-inspection-v3.json | Observation | High | n/a | n/a | none | No | No |
| TRN-010 | Pan/Crop confirmed unused, 276/276 events | Track 1 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS project-inspection-v3.json | Observation | High | n/a | n/a | none | Yes (candidate) | No |
| TRN-011 | No masks/chroma keys anywhere | project | DIRECT_PROJECT_OBSERVATION | C:\VEGAS project-inspection-v3.json | Observation | High | n/a | n/a | none | No | No |
| TRN-012 | No nested/prerendered material | project | DIRECT_PROJECT_OBSERVATION | C:\VEGAS project-inspection-v3.json | Observation | High | n/a | n/a | none | No | No |
| TRN-013 | RIFE upscaling + 1 reversed subclip = external/source-prep techniques | project | DIRECT_PROJECT_OBSERVATION | filename evidence | Observation | High | n/a | Medium | filename-based inference | No | No |
| TRN-014 | Preserved negative results/exceptions (11 unaligned ordinary events, etc.) | Track 1 | DIRECT_PROJECT_OBSERVATION | E1-orig project-structure-forensics.md | Observation | High | n/a | n/a | none | No | No |

## Visual evidence (VIS)

| ID | Claim | Scope | Classification | Source | Obs/Inf | Struct. | Visual | Semantic | Limitations | Cross-proj? | Prod-rule? |
|---|---|---|---|---|---|---|---|---|---|---|---|
| VIS-001 | Ordinary-family on/off ablation: 28% edge-energy loss, real not negligible | t1_e3 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS ordinary-ablation-metrics.json | Observation | High | High | Medium | single event, single timestamp | Yes | Yes (should change now) |
| VIS-002 | Impact-family 13-variant ablation: Motion Blur toggle dominant, DistortRGB secondary | t1_e16 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS ablation-metrics.json | Observation | High | High | Medium | single event | Yes | Yes (experimental) |
| VIS-003 | Event-FX dominates on impact events; track-FX more visible on ordinary events | t1_e16/t1_e3 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS track-vs-event-frames | Observation | High | High | Medium | 2 events only | Yes | No |
| VIS-004 | Black & White accent renders as genuine grayscale | t1_e113 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS representative-frames | Observation | High | High | Low | n=1 | No | No |
| VIS-005 | Track-0 overlay visibly subtle scratch/dust texture confirmed | t1_e0 | DIRECT_PROJECT_OBSERVATION | C:\VEGAS representative-frames | Observation | High | High | Medium | none | Yes (candidate) | No |
| VIS-006 through VIS-010 | See [representative-moments.md](representative-moments.md) for full per-moment record | various | DIRECT_PROJECT_OBSERVATION | C:\VEGAS representative-frames | Observation | High | High | varies | see file | varies | varies |

## Limitations (LIM)

| ID | Claim | Scope | Classification | Source | Limitations |
|---|---|---|---|---|---|
| LIM-001 | Missing-plugin chain location unconfirmed structurally (Transition object never exported) | project | DIRECT_PROJECT_OBSERVATION | C:\VEGAS missing-capabilities.md §1a | See [limitations.md](limitations.md) |
| LIM-002 | Pitch Shift/Reverb/EQ/Compressor exact values unrecoverable via ScriptPortal.Vegas API | project | DIRECT_PROJECT_OBSERVATION | E1-orig project-inspector-data-quality.md | See [limitations.md](limitations.md) |
| LIM-003 | 4 missing plugins' visual result unverifiable on this machine | project | DIRECT_PROJECT_OBSERVATION | project-profile.md | See [limitations.md](limitations.md) |

## Proposed promotions (candidates requiring cross-editor validation before production use)

Per the integration instructions, these are **not** silently added to `docs/editing-rules.md`,
`docs/editing-pipeline.md`, or `docs/effect-preset-architecture.md`. Each requires the stated
additional validation.

| Candidate | Evidence IDs | Additional validation required before implementation |
|---|---|---|
| Never crossfade an escalation from ordinary to impact treatment within a continuous shot — always hard cut | TRN-002, TRN-005 | Test against Editor 2 / Project 01; confirm the rule holds when a different effect vocabulary is used |
| Model the "ordinary" cut treatment as a real, low-intensity visual effect (not skip as imperceptible) | VIS-001 | Cross-project comparison; a perceptual study beyond pixel metrics would strengthen this |
| Avoid fixed-BPM/fixed-phase beat-grid extrapolation across a multi-minute timeline; track locally | TIM-001 | Test against a second project with a different song/tempo profile |
| Single-source-sample + per-event pitch-shift/reverb for gunshot SFX variety | AUD-006, AUD-012 | Confirmed as both this project's mechanism and independently-documented general sound-design practice (see `AutoEditing/docs/sniper-montage-effects-research.md` for external-practice research, kept as general methodology) — still requires a second reference project before treating as a default |
| Connective-footage placement rule (after impact, before next setup, same section, whoosh-preceded, crossfaded both sides) | STR-010, TRN-006, AUD-011 | Requires Editor 2 comparison — could be Editor 1's personal style rather than a genre convention |
| Retain native audio on raw/unedited gameplay footage that receives no effect treatment; replace audio only on curated/treated highlight clips | FX-010, AUD-004 | Requires Editor 2 comparison |
| Do not implement an inferred "ducking" feature based on this project | AUD-002 | Should not be generalized — this project simply doesn't use one; ducking would be a new AutoEditing capability, not a reproduction |

## Validation notes

- Every evidence ID above is unique within this register (verified by construction — no ID appears
  twice with a different claim).
- Every ID referenced from another document in this package (`project-profile.md`,
  `timeline-structure.md`, `velocity-findings.md`, `effects-and-presets.md`, `audio-treatment.md`,
  `transitions-and-compositing.md`, `representative-moments.md`, `limitations.md`) resolves to a
  row in this register.
- IDs are grouped by their evidence-register category prefix (STR/TIM/VEL/FX/AUD/TRN/VIS/LIM),
  matching the required `E1-P01-<CATEGORY>-<NNN>` format.
