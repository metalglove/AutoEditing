# Comparison: Editor 1 / Project 01 vs. Editor 2 / Project 01

Produced only after Editor 2 / Project 01's standalone evidence register was frozen (blind
investigation completed first, per the mandatory phase order in
`AutoEditing/docs/forensics/claude-project-5-analysis-prompt.md`). This compares two independently
analyzed reference projects. **Two projects can establish that a pattern is replicated across two
editors. They cannot establish a universal montage convention** — every claim below is labeled
accordingly.

## Document index

- [rule-comparison.md](rule-comparison.md) — candidate editorial/technique rules, classified as
  shared / different-parameters / editor-specific / contradicted.
- [preset-comparison.md](preset-comparison.md) — effect-chain/preset mechanism comparison.
- [velocity-comparison.md](velocity-comparison.md) — velocity-envelope shape comparison.
- [audio-comparison.md](audio-comparison.md) — music/SFX/gain comparison.
- [structure-comparison.md](structure-comparison.md) — timeline/track/transition structure
  comparison.
- [cross-project-evidence-matrix.json](cross-project-evidence-matrix.json) — machine-readable
  matrix cross-referencing every comparison point to its Evidence IDs in both packages.

## Classification scheme used throughout

- **Shared mechanically and contextually** — same mechanism, same triggering context, in both
  projects.
- **Shared technique, different parameters** — same underlying mechanism, different concrete
  values.
- **Editor 1-specific** / **Editor 2-specific** — found in one project only.
- **Project/section-dependent** — appears to depend on project structure rather than editor
  identity (not strongly supported either way by only 2 projects).
- **Plugin/capability-dependent** — depends on plugin availability, which differed between the two
  inspection environments in this instance (Editor 1's project referenced 4 unavailable plugins;
  Editor 2's referenced none).
- **Contradicted** — one project's finding is directly falsified by the other's evidence.
- **Insufficiently supported** — neither project's evidence is strong enough to classify further.

## Headline result

The two projects share a small number of genuinely convergent mechanisms (the VEGAS
automatic-crossfade signature; the complete absence of any ducking mechanism; a `Mo Blur
Length=0.8` starting value on their respective Shake-based hit effects; a Fast-in/Slow-out music
fade convention) alongside a much larger number of project-specific or directly contradicting
choices (effect vocabulary complexity, crossfade rate, coverage continuity, dominant velocity
shape, SFX-replacement mechanism, track-fader attenuation, plugin dependency). **This pattern — a
few small, mechanically simple conventions replicating, while most higher-level editorial choices
differ — is itself the most defensible conclusion two projects can support.** It argues for
AutoEditing treating the replicated items as reasonable defaults while treating everything else as
editor/project-specific style, not as evidence of one universal sniper-montage grammar.

## Strongest candidates for cross-project comparison, now tested

| Candidate (from Editor 1's evidence register) | Result against Editor 2 |
|---|---|
| Automatic-crossfade mechanism (Smooth/Smooth, symmetric length) | **Replicated** — see [structure-comparison.md](structure-comparison.md) |
| Avoid fixed global beat-grid extrapolation | **Not tested** — no independent musical analysis was performed for Editor 2 (reduced scope) |
| Model the "ordinary" cut as a real, low-intensity treatment | **Not directly tested** — Editor 2 has no clearly analogous "ordinary vs. impact" split to compare against; see [preset-comparison.md](preset-comparison.md) |
| Single-source-sample + processing for gunshot SFX variety | **Contradicted in mechanism, not in principle** — Editor 2 reuses native audio plus one small unprocessed accent sample rather than one processed excerpt; see [audio-comparison.md](audio-comparison.md) |
| Connective-footage placement rule (after impact, before setup, crossfaded both sides) | **Not tested** — no equivalent connective-footage pairing analysis was performed for Editor 2 (reduced scope) |
| Do not infer a ducking feature | **Replicated (as an absence)** — neither project has one |
