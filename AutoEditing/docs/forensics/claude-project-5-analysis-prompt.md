# Claude prompt - Editor 2 / Project 01 forensic analysis

Use this prompt only after the Editor 1 / Project 01 evidence package and
adversarial verification are complete.

---

Analyze the second reference project as **Editor 2 / Project 01**. Use the
corrected inspector, evidence discipline, and adversarial methodology developed
during the Editor 1 / Project 01 investigation.

## Public attribution and evidence preservation

This repository is public. Anonymize only the editor's identity:

- use `Editor 2` in prose;
- use `editor-2` in filesystem-safe identifiers;
- do not write the editor's real name or identifying editor aliases into the
  repository;
- do not create a real-name-to-Editor-2 mapping in the repository.

Do not otherwise anonymize or generalize the evidence. Preserve original project
titles, paths, source filenames, track/event names, music/SFX names, timestamps,
effect/preset names, metadata, screenshots, diagnostics, inspector fields, and
technical identifiers unless they contain the editor's identity.

## Project

Original VEGAS project:

`C:\VEGAS\Glovali Montage 5\Glovali Montage 5\Untitled.veg`

Recorded original state before analysis:

- Size: 1,246,776 bytes
- Last modified: April 11, 2023, 17:17:40

The completed reference package for later comparison is:

`AutoEditing/docs/forensics/projects/editor-1/project-01/`

Do not inspect that package for conclusions until the standalone Editor 2
analysis has been frozen. It may be consulted initially only for shared schema,
tooling, evidence-field definitions, and validation procedures.

## Primary objective

Conduct a blind, independent forensic investigation of Editor 2 / Project 01,
then adversarially test its conclusions. Only after its standalone evidence
package is frozen may you compare it with Editor 1 / Project 01.

The order is mandatory:

```text
Blind Editor 2 inspection
    -> freeze standalone factual findings
    -> adversarially test Editor 2 interpretations
    -> freeze corrected Editor 2 evidence register
    -> compare E1-P01 with E2-P01
    -> identify agreements, differences, and contradictions
    -> propose, but do not implement, production rules
```

Do not assume the projects share effect presets, velocity recipes, kill timing,
audio treatment, transition behavior, cinematics, musical grids, clip ordering,
or authorship conventions.

## Safety

- Never modify or overwrite the original project, backup, or source media.
- Record original size, timestamps, and a cryptographic hash before opening it.
- Create uniquely named inspection and relinking copies.
- Relink only a disposable copy; save, close, and reopen before inspection.
- Save all research output outside the original project directory.
- Log every launched process, dialog, automation action, timeout, and recovery.
- Never blindly click an unknown or unmatched dialog.
- Do not terminate a VEGAS process the analysis did not launch.
- Preserve scripts and commands required to reproduce the investigation.
- Document automation incidents as evidence; do not conceal them.
- Confirm that no VEGAS process remains after each unattended run.

## Private raw-output root

Create:

`C:\VEGAS\editor-2-project-01-analysis\`

Recommended structure:

```text
C:\VEGAS\editor-2-project-01-analysis\
  raw\
  reports\
  frames\
  renders\
  scripts\
  diagnostics\
  fixtures\
  comparison\
```

Do not mix raw Editor 2 output with Editor 1 output.

## Public evidence package

Create:

`AutoEditing/docs/forensics/projects/editor-2/project-01/`

Required documents:

- `README.md`
- `project-profile.md`
- `timeline-structure.md`
- `velocity-findings.md`
- `effects-and-presets.md`
- `audio-treatment.md`
- `transitions-and-compositing.md`
- `representative-moments.md`
- `evidence-register.md`
- `limitations.md`

Use stable evidence identifiers:

- `E2-P01-STR-*` for structure;
- `E2-P01-TIM-*` for timing;
- `E2-P01-VEL-*` for velocity;
- `E2-P01-FX-*` for effects;
- `E2-P01-AUD-*` for audio;
- `E2-P01-TRN-*` for transitions;
- `E2-P01-VIS-*` for visual evidence;
- `E2-P01-LIM-*` for limitations.

Classify each significant conclusion as exactly one of:

- `DIRECT_PROJECT_OBSERVATION`
- `EDITOR_2_PROJECT_PATTERN`
- `CROSS_PROJECT_HYPOTHESIS`
- `PRODUCTION_RULE_CANDIDATE`

Track status independently:

- `confirmed`
- `hypothesis`
- `falsified`
- `superseded`
- `unresolved`

When a conclusion changes, retain the old evidence entry and link it through
`supersedes`, `supersededBy`, and `falsificationReason`.

## Phase 1 - Preflight and provenance

Before opening the project:

1. Hash and record the original `.veg` and backup, if present.
2. Record timestamps and sizes.
3. Inventory nearby media without modifying it.
4. Record installed VEGAS version and build.
5. Inventory relevant OFX, VST, DirectX, native effects, fonts, and codecs.
6. Create an inspection copy.
7. Start automation and diagnostic logs before launching VEGAS.

Capture missing-media dialogs, missing-plugin warnings, generated-media errors,
font warnings, codec failures, OFX load failures, relink candidates, and project
conversion notices.

Produce private raw artifacts:

- `reports/preflight.md`
- `diagnostics/project-open-diagnostics.json`
- `diagnostics/dependency-inventory.json`

Summarize them publicly in `project-profile.md` and `limitations.md`.

## Phase 2 - Corrected structural export

Reuse the latest corrected inspector and autonomous VEGAS process. Export:

- project video/audio settings, duration, buses, resampling, motion blur, and
  compositing configuration;
- tracks, hierarchy, gain, pan, compositing, motion, FX, envelopes, routing, and
  mute/solo state;
- events, groups, timeline ranges, source/take paths, offsets, playback rates,
  fades, overlaps, transitions, velocity, Pan/Crop, gain, normalization, FX,
  and media state;
- effect scope and chain order;
- stable plugin, effect-instance, and parameter identifiers;
- typed static and animated values;
- raw keyframe time, API source type, candidate time basis, interpolation,
  derived timeline time, visible-range classification, and derivation
  confidence;
- markers, regions, labels, generated media, nested projects, subclips, and
  reversed media;
- structured diagnostics with scope, object ID, property path, HRESULT,
  exception, fallback, and read status;
- loaded, disabled, bypassed, invalid, placeholder, and referenced-but-missing
  dependencies where the API exposes them;
- non-OFX audio parameter or explicit unsupported-capability records.

Serialize all native identifiers as strings.

Produce:

- `raw/project-inspection-v1.json`
- `diagnostics/project-inspection-diagnostics.json`
- `reports/inspection-coverage.md`

## Phase 3 - Inspector validation and time-domain fixture

Before interpreting project keyframes, verify:

- identifier uniqueness and referential integrity;
- native-ID precision;
- media existence separately from decode success;
- relinks surviving save/close/reopen;
- virtual media distinguished from filesystem paths;
- missing dependencies represented rather than inferred absent;
- every inspected null or omission has a read-status reason;
- deterministic repeat exports of an unchanged inspection copy.

Build the previously unresolved OFX time-domain fixture unless it is technically
impossible. Use a disposable event starting away from zero, trimmed at both
ends, with keys before, at, within, at the end of, and after the visible event.
Test event-, media-, and track-level effects. Save, close, reopen, inspect, and
render representative frames.

Do not transfer a fixture result to every plugin scope unless the evidence
supports that generalization.

If the fixture cannot be completed, record the attempted implementation,
failure, diagnostics, and exact claims that remain blocked.

## Phase 4 - Blind standalone factual report

Analyze Editor 2 / Project 01 without reading Editor 1's substantive findings.
Report facts about:

- project structure, tracks, events, media, markers, and regions;
- duration, occupancy, gaps, cuts, overlaps, and crossfades;
- structural sections, titles, openers, closers, cinematics, and overlays;
- source ordering and source runs;
- music, gameplay audio, SFX, buses, and gain;
- velocity, playback rate, event splitting, and source mapping;
- Pan/Crop, Track Motion, compositing, event/track FX, and preset signatures;
- missing dependencies and unsupported inspector capabilities.

Freeze the initial factual ledger before conducting comparison-driven tests.

Produce:

- `raw/editor-2-project-01-factual-findings.json`
- an initial `evidence-register.md`
- the factual portions of the public project documents.

## Phase 5 - Semantic runs, kills, and impacts

Build the kill/impact corpus from converging evidence:

- visible hit or kill confirmation;
- scope state, recoil, muzzle flash, and source action;
- replacement and native weapon audio;
- markers and regions;
- velocity points and phases;
- cuts and event boundaries;
- effect starts, peaks, and recovery;
- musical roles;
- same-source semantic runs.

Classify candidates as:

- confirmed kill;
- probable kill;
- possible kill;
- musical impact without a visible kill;
- non-kill accent;
- unknown.

Do not use individual events as the only unit of analysis. Build same-source
semantic runs and identify each kill's position within its run. Test first,
middle, final, and only-kill roles. Report both event-level and run-level results
with denominators and counterexamples.

For every candidate record timeline/source time, evidence signals, relevant
deltas, velocity phase, effect family, musical role, section, run ID, run
position, confidence, and ambiguity.

Produce:

- `raw/editor-2-project-01-kill-corpus.json`
- `reports/editor-2-project-01-kill-timing.md`

## Phase 6 - Velocity and retiming

Recover Editor 2 velocity families without importing Editor 1 assumptions:

- speed and source action at the kill;
- approach, scope-in, fast-entry, and pre-impact behavior;
- slow-motion start relative to kill and scope-out;
- plateau speed, duration, and variation;
- exit and return to normal speed;
- source-to-timeline mapping;
- event split boundaries;
- run-position, section, and artistic variation;
- clips or regions intentionally left at normal speed;
- counterexamples.

Cluster complete signatures, not isolated envelope values. Report samples,
distributions, triggers, representative events, visual validation, and
confidence.

Explicitly test whether slow motion is confined to post-kill recovery or is also
used for introductions, outros, cinematics, setup, or other artistic purposes.
Do not treat the Editor 1 result as the expected answer.

Produce:

- `velocity-findings.md`
- `raw/editor-2-project-01-velocity-clusters.json`

## Phase 7 - Effects, presets, and render ablation

Cluster complete event and track effect chains by:

- scope and chain order;
- plugin and parameter IDs;
- static and animated values;
- keyframes and interpolation;
- time basis and visible range;
- motion-blur state;
- random seeds;
- event duration and truncation;
- track/event interaction;
- context, section, and semantic-run position.

Do not equate similar names with identical presets. Identify byte-identical and
near-identical stored configurations separately.

For every common or important treatment, render:

- full chain;
- each effect disabled independently;
- important parameters neutralized;
- track FX disabled;
- event FX disabled;
- no-effects baseline;
- CPU/GPU variants when practical and relevant.

Measure scale/displacement, blur, shake, RGB separation, luminance, glow,
flicker, recovery, and visibility during normal playback. Test both an ordinary
treatment and the strongest impact/escalation treatment.

Separate:

- stored mechanical recipe;
- rendered visual result;
- perceived editorial role;
- inferred contextual trigger.

Produce:

- `effects-and-presets.md`
- `raw/editor-2-project-01-effect-signatures.json`
- `reports/editor-2-project-01-effect-ablation.md`
- frames under `frames/effects/`.

## Phase 8 - Independent musical analysis

Analyze the song without using project markers as ground truth. Estimate:

- tempo using at least two independent methods;
- tempo changes and confidence;
- full-time, half-time, and double-time interpretations;
- beats, downbeats, bars, phrases, transients, drum fills, vocal entries,
  drops, breakdowns, climaxes, and outro;
- local timing deviations and flexible grids.

Then correlate project markers, kills, effects, cuts, cinematics, whooshes,
flashes, titles, and transitions with the independent analysis.

Do not force a rigid global grid. Test whether alignment is global, half-time,
section-specific, phrase-based, transient-based, or deliberately loose.

Produce:

- `reports/editor-2-project-01-musical-alignment.md`
- `raw/editor-2-project-01-song-event-correlations.json`

## Phase 9 - Audio and retained-source rules

Inspect:

- music event/track/bus/master gain and fades;
- gain and normalization on every SFX event;
- replacement weapon sounds and layered variants;
- retained and removed gameplay audio;
- source-media role and origin;
- pitch, reverb, EQ, compression, gating, and automation;
- whooshes, risers, impacts, tails, stereo placement, loudness, and headroom;
- whether overlapping sounds require multiple tracks;
- whether any ducking mechanism exists at event, track, bus, or master level.

If plugin parameters are inaccessible, render isolated stems and analyze
waveforms, spectra, and loudness. Do not infer parameter values that the API
cannot expose.

Derive and test candidate source-audio retention rules. Check whether retention
is predicted by raw gameplay/DVR media, curated bridges, cinematics, section,
kill role, or another factor.

Produce:

- `audio-treatment.md`
- `raw/editor-2-project-01-audio-correlations.json`

## Phase 10 - Cuts, automatic crossfades, and compositing

Distinguish:

- hard cuts;
- event overlaps;
- automatic VEGAS crossfades;
- explicit transition plugins;
- same-source treatment splits;
- source-change transitions;
- cinematic bridges;
- track compositing and global texture layers.

For every crossfade and hard cut, calculate candidate predictors including
source continuity, source change, semantic-run position, effect escalation,
cinematic adjacency, musical role, event duration, and section.

Do not interpret an overlap as an authored transition merely because VEGAS
renders a crossfade automatically. Determine which editorial placement decision
created the overlap and whether a separate transition was authored.

Produce:

- `transitions-and-compositing.md`
- `raw/editor-2-project-01-cut-transition-corpus.json`

## Phase 11 - Structure, ordering, and cinematics

Determine:

- whether opener/closer metadata is honored;
- whether body clips are chronological, filename-ordered, or freely assigned;
- how footage is allocated to musical sections;
- how cinematics relate to preceding and following gameplay;
- whether map, weapon, palette, motion, composition, or source identity predicts
  cinematic pairing;
- cinematic duration, placement, effects, source audio, and whoosh behavior;
- intro/title and outro behavior;
- multi-song or multi-part behavior.

Analyze cinematic pairing at the source/run level, not only as isolated
timeline events.

Produce:

- `timeline-structure.md`
- `raw/editor-2-project-01-structural-sections.json`

## Phase 12 - Representative proof

Select at least 16 representative moments covering:

- opening and title;
- ordinary treatment;
- strongest impact;
- only-, first-, middle-, and final-kill cases when present;
- a beat effect without a shot;
- cinematic entry and exit;
- hard cut;
- automatic crossfade;
- authored transition if any;
- retained source audio;
- removed/replaced source audio;
- audio-led transition;
- counterexample;
- closer and final frame.

For each, preserve timestamp, track/event/source identity, structural evidence,
before/at/after frames, short low-resolution render, audio evidence,
interpretation, structural/visual/semantic confidence, and ambiguity.

Produce:

- `representative-moments.md`
- frames under `frames/representative/`;
- renders under `renders/representative/`.

## Phase 13 - Adversarial verification

Before comparing editors, assign adversarial tests that attempt to falsify each
major Editor 2 conclusion from different angles.

At minimum test:

- event-level versus same-source-run conclusions;
- alternative tempo and phase interpretations;
- automatic crossfade mechanics versus authored intent;
- event FX versus track FX interaction;
- stored keyframes versus visible render behavior;
- retained-audio predictors and counterexamples;
- cinematic pairing alternatives;
- ordinary versus strong effect visibility;
- normalization, gain, and ducking claims;
- missing-plugin and inspector-coverage explanations.

For every falsification:

- retain the original claim;
- mark it `falsified` or `superseded`;
- issue a new evidence ID for the corrected claim;
- record the failed unit of analysis or assumption;
- back-port the correction into affected narrative documents.

Freeze a reconciled Editor 2 evidence ledger before cross-editor comparison.

Produce:

- `raw/reconciled-editor-2-project-01-findings.json`
- `reports/editor-2-project-01-adversarial-verification.md`
- final project-specific public documents.

## Phase 14 - Cross-editor comparison

Only now read the substantive Editor 1 / Project 01 package. Compare evidence
register entries and representative proof rather than narrative impressions.

Classify candidate behavior as:

- shared mechanically and contextually;
- shared technique with different parameters;
- Editor 1-specific;
- Editor 2-specific;
- project/section-dependent;
- plugin/capability-dependent;
- contradicted;
- insufficiently supported.

Compare:

- source-run and multi-kill escalation;
- kill position within velocity curves;
- post-kill recovery and artistic slow motion;
- ordinary and strong effect families;
- screen pumps and effects on beats without shots;
- effect peak, SFX, marker, cut, and kill alignment;
- music grid and half/full-time interpretation;
- retained source audio and replacement SFX;
- music attenuation and ducking;
- whooshes and structural lead-ins;
- hard cuts, automatic crossfades, and authored transitions;
- cinematics, titles, ordering, and opener/closer behavior;
- preset reuse and track/event finishing.

Two projects can establish replicated evidence across two editors. They cannot
establish a universal montage convention.

Create:

`AutoEditing/docs/forensics/comparisons/editor-1-project-01-vs-editor-2-project-01/`

Include:

- `README.md`
- `rule-comparison.md`
- `preset-comparison.md`
- `velocity-comparison.md`
- `audio-comparison.md`
- `structure-comparison.md`
- `cross-project-evidence-matrix.json`

## Phase 15 - AutoEditing implications

Separate recommendations into:

1. replicated candidate defaults;
2. optional style presets;
3. editor/project-specific presets;
4. capability-gated plugin presets;
5. native VEGAS fallbacks;
6. experimental rules;
7. rules requiring more projects;
8. behaviors contradicted or unsafe to generalize.

For every proposal record:

- supporting E1 and E2 evidence IDs;
- sample sizes and counterexamples;
- mechanical recipe;
- contextual trigger;
- confidence by structural, visual, and semantic evidence;
- required capability;
- fallback;
- parameter range;
- suppression and conflict rules;
- additional validation still required.

Do not modify production code or general editing rules. List proposed promotions
for later human review.

## Evidence discipline

For every conclusion distinguish:

- direct observation;
- calculated correlation;
- high/medium/low-confidence inference;
- speculation;
- insufficient evidence.

Always provide sample size, denominator, project scope, counterexamples, missing
dependencies, inspector limitations, render-validation state, and applicable
evidence IDs.

Use precise language such as:

- "Editor 2 / Project 01 contains..."
- "The inspector exported..."
- "The rendered ablation shows..."
- "This supports..."
- "This suggests, but does not prove..."
- "This remains unresolved because..."

Do not write:

- "Editors always..."
- "Professional montages use..."
- "Every kill requires..."
- "This is the standard workflow..."

## Required final answers

1. What is Editor 2 / Project 01's ordinary visual treatment?
2. What is its strongest impact or escalation treatment?
3. Where does the kill sit in each velocity family?
4. When does slow motion start and end relative to kill and scope-out?
5. Does only/first/middle/final position matter within semantic runs?
6. How are effects used on beats without shots?
7. Is musical alignment rigid, half-time, phrase-based, or locally flexible?
8. How are cinematics selected and paired?
9. What predicts hard cuts, automatic crossfades, and authored transitions?
10. How is gun audio constructed and layered?
11. Which source audio is retained, and what predicts retention?
12. What is the complete music/SFX gain relationship, including ducking?
13. Which findings survived adversarial verification?
14. Which initial Editor 2 findings were falsified or superseded?
15. Which Editor 1 findings independently replicate?
16. Which Editor 1 findings fail to replicate?
17. Which recipes can be recovered mechanically but lack contextual confidence?
18. What can AutoEditing safely consider after two independent editors?

## Final privacy and integrity checks

Before finishing:

1. Confirm the original `.veg`, backup, and source media were not modified.
2. Confirm no VEGAS process remains.
3. Run `git diff --check`.
4. Verify internal Markdown links and unique evidence IDs.
5. Search tracked repository changes for the Editor 2 real name and aliases.
6. Replace only those identity occurrences; preserve all other evidence.
7. Confirm no private identity mapping exists in the repository.
8. Confirm Editor 2 findings were frozen before cross-editor comparison.
9. Report any unresolved privacy, provenance, inspector, plugin, or render
   limitations.

## Final response

Report:

1. Private raw artifacts and public documents created.
2. Original-project integrity verification.
3. Editor-name anonymization verification.
4. Strongest direct Editor 2 observations.
5. Strongest repeated Editor 2 project patterns.
6. Falsified or superseded findings.
7. Findings replicated across Editor 1 and Editor 2.
8. Findings that differ or conflict.
9. Proposed production-rule and preset promotions.
10. Items still blocked by time-domain, plugin, media, render, or inspector
    limitations.
