# Evidence Register — Editor 2 / Project 01

Canonical evidence key: `editor-2/project-01`. Classification values match the Editor 1 package:
`DIRECT_PROJECT_OBSERVATION`, `EDITOR_2_PROJECT_PATTERN`, `CROSS_PROJECT_HYPOTHESIS`,
`PRODUCTION_RULE_CANDIDATE`. This investigation was conducted **blind** to Editor 1's substantive
findings — the Editor 1 package was consulted only for schema/document-structure/methodology
before this register was frozen; the "Proposed promotions" table below (and any explicit
cross-project comparisons elsewhere) were added only after that freeze, per the mandatory phase
order in `claude-project-5-analysis-prompt.md`.

## Structure (STR)

| ID | Claim | Scope | Classification | Obs/Inf | Struct. | Visual | Semantic | Cross-proj? | Prod-rule? |
|---|---|---|---|---|---|---|---|---|---|
| STR-001 | 7-track layout, two tiers (accent/overlay + main edit), no compositing parent/child | project | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | No | No |
| STR-002 | Track 4: 158 events, 32 source-runs (21 multi-event) | Track 4 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (candidate) | No |
| STR-003 | Two source tiers: 17 curated files, 12 raw/connective DVR files | Track 4 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | Medium (filename-based) | Yes | No |
| STR-004 | Tracks 0/1: matching 7-event solid-color flash pair, exact shared timestamps | Tracks 0-1 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | No | No |
| STR-005 | 3-way convergence: color flash + opacity strobe + music cut at same instant | Tracks 0,1,4,6 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | Medium | Yes | No |
| STR-006 | Track-4 Composite envelope, 141 points, clustered bursts not continuous | Track 4 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | No (new mechanism) | No |
| STR-007 | 1.375s gap in Track-4 coverage — contradicts Editor 1's "always continuous" finding | Track 4 | DIRECT_PROJECT_OBSERVATION | Observation | High | High (VIS-003) | n/a | Yes (falsifies a candidate universal) | No |
| STR-008 | Outro: bumper clip Lighten-composited exactly over final Track-4 event's range | Tracks 3,4 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | No | No |
| STR-009 | Bumper clip filename says "INTRO" but is used as outro | project | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | Medium | No | No |

## Timing (TIM)

| ID | Claim | Scope | Classification | Obs/Inf | Struct. | Visual | Semantic | Cross-proj? | Prod-rule? |
|---|---|---|---|---|---|---|---|---|---|
| TIM-001 | 251 markers, median 0.667s spacing — denser than Editor 1's 154/~1.05s | project | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a (not audio-verified) | Yes | No |

## Velocity (VEL)

| ID | Claim | Scope | Classification | Obs/Inf | Struct. | Visual | Semantic | Cross-proj? | Prod-rule? |
|---|---|---|---|---|---|---|---|---|---|
| VEL-001 | 7-point velocity shape dominates (81%), vs Editor 1's 4-point dominant (68%) | Track 4 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (contradiction) | No |
| VEL-002 | Double-dip shape: two 0.5x plateaus per event with intermediate re-acceleration | Track 4 | DIRECT_PROJECT_OBSERVATION | Observation | High (1 sample detailed) | n/a | n/a | Yes (contradiction w/ E1 single-dip) | No |
| VEL-003 | Plateau value 0.5x matches Editor 1's project's plateau value | Track 4 | DIRECT_PROJECT_OBSERVATION | Observation | Medium (1 sample) | n/a | n/a | Yes (match) | No |
| VEL-004 | 7 hit-accent events cluster into an 8s rapid multi-hit burst | Track 4/5 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | Medium | Yes | Yes (experimental) |

## Effects and presets (FX)

| ID | Claim | Scope | Classification | Obs/Inf | Struct. | Visual | Semantic | Cross-proj? | Prod-rule? |
|---|---|---|---|---|---|---|---|---|---|
| FX-001 | Effect-chain distribution: single S_Shake dominates (78%), no separate impact family at scale | Track 4 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (contradiction w/ E1 two-tier system) | No |
| FX-002 | S_Shake: Amplitude+Frequency animated, Motion Blur static true, Mo Blur Length=0.8 (matches E1) | Track 4 | DIRECT_PROJECT_OBSERVATION | Observation | High | Medium | n/a | Yes (match on Mo Blur Length) | Yes (experimental) |
| FX-003 | Only 1 S_Shake instance per event (never 2 stacked, unlike Editor 1's impact family) | Track 4 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (contradiction) | No |
| FX-004 | S_FilmDamage: bundled shake/flicker/defocus + grain/stain/dust, distinct mechanism from E1's overlay-clip approach | Track 4 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (contradiction) | No |
| FX-005 | S_FilmDamage applied predominantly to raw/connective footage | Track 4 | EDITOR_2_PROJECT_PATTERN | Observation | Medium (not fully cross-tabulated) | n/a | Low | Yes | No |
| FX-006 | Zero missing/unavailable plugins anywhere | project | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (contradiction w/ E1's 4 missing plugins) | No |
| FX-007 | Track-4 track-level FX: only S_Flicker, no S_Shake (unlike Editor 1's Shake+Flicker pair) | Track 4 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (contradiction) | No |

## Audio (AUD)

| ID | Claim | Scope | Classification | Obs/Inf | Struct. | Visual | Semantic | Cross-proj? | Prod-rule? |
|---|---|---|---|---|---|---|---|---|---|
| AUD-001 | Music split into 5 segments (not 1 continuous event like Editor 1) | Track 6 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (contradiction) | No |
| AUD-002 | Music segment boundaries land exactly on flash-cluster timestamps | Track 6 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | No (new pattern) | No |
| AUD-003 | Final fade-out length (12.125s) matches final solid-color event's duration exactly | Track 6 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | No | No |
| AUD-004 | Fast-in/Slow-out fade convention matches Editor 1's project | Track 6 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (match) | Yes (experimental) |
| AUD-005 | Track 5/6 fader = 0dB (no attenuation), vs Editor 1's -3.0/-3.6dB | Tracks 5-6 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (contradiction) | No |
| AUD-006 | No ducking mechanism anywhere (0 envelopes at all levels) | project | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (match — both projects lack ducking) | Yes (should not generalize as a technique) |
| AUD-007 | Track 5 reuses native gameplay audio from the same source files as the picture, not a replacement-SFX library | Track 5 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | Low (exact relationship unverified) | Yes (contradiction w/ E1's replacement-library approach) | No |
| AUD-008 | SA-B 50 Hit.mp3: 7/7 (100%) at 0ms delta from marker+event | Track 5 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | Medium | Yes (stronger version of E1's 97.6% finding) | Yes (should change now) |
| AUD-009 | No dedicated whoosh-style transition sample exists in this project | project | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (contradiction) | No |
| AUD-010 | Single reused hit-accent sample, no per-instance processing (simpler than E1's pitch/reverb approach) | Track 5 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (partial match — same "reuse one sample" principle, different mechanism) | No |

## Transitions (TRN)

| ID | Claim | Scope | Classification | Obs/Inf | Struct. | Visual | Semantic | Cross-proj? | Prod-rule? |
|---|---|---|---|---|---|---|---|---|---|
| TRN-001 | 17/157 (10.8%) overlap, 139 hard cuts, 1 gap | Track 4 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (contradiction w/ E1's 41%) | No |
| TRN-002 | Overlap rate markedly lower than Editor 1's project | Track 4 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (contradiction) | No |
| TRN-003 | Same-source vs source-change overlap direction matches E1 (source-change overlaps more) | Track 4 | DIRECT_PROJECT_OBSERVATION | Observation | Medium (not fully quantified) | n/a | n/a | Yes (partial match) | No |
| TRN-004 | 16/17 overlaps: Smooth/Smooth, symmetric, matching VEGAS auto-crossfade signature (matches E1 mechanism) | Track 4 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (match) | Yes (should change now) |
| TRN-005 | One asymmetric-fade exception (t4_e126->e127) | Track 4 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | No | No |
| TRN-006 | Outro bumper uses Lighten blend (different from E1's Screen) | Track 3 | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (contradiction) | No |
| TRN-007 | No full-length texture-overlay track exists (texture is per-event via S_FilmDamage instead) | project | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | Yes (contradiction) | No |
| TRN-008 | No masks/chroma keys anywhere | project | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | No | No |
| TRN-009 | No nested/prerendered material | project | DIRECT_PROJECT_OBSERVATION | Observation | High | n/a | n/a | No | No |

## Visual evidence (VIS)

| ID | Claim | Scope | Classification | Obs/Inf | Struct. | Visual | Semantic | Cross-proj? | Prod-rule? |
|---|---|---|---|---|---|---|---|---|---|
| VIS-001 | Strobe-flash frame confirms near-white flash + motion-blur streaks + letterbox bars | t4~70.7s | DIRECT_PROJECT_OBSERVATION | Observation | High | High | Medium | No | No |
| VIS-002 | Intro title frame confirms legible title text over gameplay footage | t2/t4~2.0s | DIRECT_PROJECT_OBSERVATION | Observation | High | High | High | No | No |
| VIS-003 | Gap frame captured at 9.0s (inside the measured gap) | t4~9.0s | DIRECT_PROJECT_OBSERVATION | Observation | High | High | n/a | No | No |
| VIS-004 | Multi-hit-burst frame shows heavy motion blur + red UI banner element (kill-consistent) | t4~125.3s | DIRECT_PROJECT_OBSERVATION | Observation | High | High | Medium | Yes (corroborates E1's Motion-Blur mechanism) | No |

## Limitations (LIM)

| ID | Claim | Classification |
|---|---|---|
| LIM-001 | Reproducible VEGAS crash tied to a large media file; resolved by removing its one reference | DIRECT_PROJECT_OBSERVATION |
| LIM-002 | One genuinely-unavailable media item, reference removed at user's direction | DIRECT_PROJECT_OBSERVATION |
| LIM-003 | Original `Untitled.veg` directly edited by the project owner (not automation); `.veg.bak` unaffected | DIRECT_PROJECT_OBSERVATION |
| LIM-004 | No controlled ablation for S_Shake/S_FilmDamage this pass | (reduced scope) |
| LIM-005 | No independent musical/tempo audio analysis this pass | (reduced scope) |
| LIM-006 | Only 10/16 representative moments captured, several not individually reviewed | (reduced scope) |

## Proposed promotions (candidates requiring further cross-editor validation before production use)

| Candidate | Evidence IDs | Additional validation required |
|---|---|---|
| VEGAS automatic crossfade mechanism (Smooth/Smooth, symmetric length) generalizes across editors | E2 TRN-004, E1 TRN-004 | Confirmed identical mechanism in 2/2 editors now — still only 2 data points |
| Mo Blur Length=0.8 as a starting value for a Shake-based motion-blur hit | E2 FX-002, E1 (effect-ablation-results.md) | Coincidence or convention? Requires a 3rd project to distinguish |
| Do not implement a ducking feature based on either reference project | E2 AUD-006, E1 AUD-002 | Should not be generalized as "this is correct" — both projects simply lack the mechanism; 2/2 is suggestive but not proof no montage editor ever ducks music |
| Marker-aligned single-sample hit accents as a kill-emphasis technique | E2 AUD-008, E1 TIM-002 | Mechanism differs (single un-processed sample here vs. per-instance pitch/reverb in E1) — cannot yet propose one unified rule, only that "marker-align your hit accent" itself replicates |

## Validation notes

- Every evidence ID above is unique within this register.
- This register was frozen (in the sense of representing the standalone, blind Editor 2
  investigation) before any cross-editor comparison document was written — the "Proposed
  promotions" table above was added afterward and is clearly a comparison-stage addition, not part
  of the blind factual findings.
