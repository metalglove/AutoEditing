# Forensic editing research

This directory contains project-specific evidence, cross-editor comparisons,
portable datasets, and methodology used to reason about montage-editing
behavior without treating one editor's choices as universal rules.

Only editor identities are anonymized. Project titles, media names, paths,
timestamps, effect names, parameter values, and other technical evidence remain
project-specific unless they contain an editor identity.

## Start here

- [Editor 1 / Project 01](projects/editor-1/project-01/README.md)
- [Editor 2 / Project 01](projects/editor-2/project-01/README.md)
- [Cross-editor comparison](comparisons/editor-1-project-01-vs-editor-2-project-01/README.md)
- [Portable data and artifact manifest](artifact-manifest.json)
- [macOS and Windows workflow](platform-workflow.md)
- [Forensic value of projects without source media](media-less-vegas-project-value.md)
- [Inspector data-quality requirements](project-inspector-data-quality.md)

## Evidence layers

```text
raw project/export observation
    -> project-specific evidence register
    -> adversarially reconciled project pattern
    -> cross-editor comparison
    -> candidate production rule
    -> code + deterministic tests + editing-rules.md
```

Project evidence does not become production behavior merely because it is
repeated many times in one timeline. The cross-editor comparison records which
patterns replicated, contradicted each other, or remain insufficiently tested.

The normative production contract is
[editing-rules.md](../editing-rules.md). Forensic documents may propose changes,
but they do not describe those proposals as implemented.

## Portable machine-readable data

Compact evidence lives under [`data/`](data/):

- [`data/editor-1/project-01/`](data/editor-1/project-01/) contains the
  reconciled findings, kill alignment, effect signatures, transition detail,
  musical-grid measurements, retained-audio analysis, and ablation metrics.
- [`data/editor-2/project-01/evidence-summary.json`](data/editor-2/project-01/evidence-summary.json)
  is a compact machine-readable projection of the Editor 2 evidence register.
- The cross-project
  [`cross-project-evidence-matrix.json`](comparisons/editor-1-project-01-vs-editor-2-project-01/cross-project-evidence-matrix.json)
  connects evidence identifiers from both projects.

The Markdown evidence registers remain authoritative when a compact derivative
and its source disagree.

## Reproduction tooling

Sanitized research scripts are under
[`AutoEditing/Tools/Forensics/`](../../Tools/Forensics/README.md). They are
preserved for auditability and future fixture work; they are not production
code and must not be run blindly against an original project.

## Raw artifacts intentionally excluded

The public repository does not contain:

- original `.veg` projects or backups;
- source gameplay, cinematics, music, SFX, or final renders;
- commercial plug-in installers or archives;
- multi-gigabyte project/source archives;
- full representative-frame collections;
- the complete 20-26 MB historical Editor 1 inspector exports;
- the complete approximately 7.7 MB Editor 2 inspector export.

The compact datasets retain the evidence most useful for reasoning and review.
The [artifact manifest](artifact-manifest.json) records included artifacts and
the classes of material intentionally kept outside Git.

## Adding another reference project

1. Assign the next neutral editor/project key.
2. Preserve the original project and inspect disposable copies.
3. Produce a standalone project evidence register before reading other
   editors' conclusions.
4. Record structural, visual, and semantic confidence independently.
5. Adversarially test and retain falsified or superseded claims.
6. Add only sanitized, legally suitable compact evidence and scripts.
7. Compare projects only after the standalone register is frozen.
8. Propose production changes separately; if accepted, update code, tests, and
   `docs/editing-rules.md` together.
