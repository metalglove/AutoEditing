# Forensic workflow on macOS and Windows

The repository is intentionally useful on macOS without pretending that VEGAS
can be automated or rendered there.

## Work that can be done on macOS

- Review and revise project-specific findings.
- Query compact JSON evidence and the cross-project evidence matrix.
- Compare parameter families, timing distributions, and confidence labels.
- Improve evidence schemas, preset taxonomy, and proposed rule language.
- Review the forensic scripts and inspector implementation.
- Write VEGAS-free domain logic and deterministic tests where the installed
  .NET tooling supports the target framework.
- Prepare issues, prompts, implementation designs, and documentation.
- Validate JSON, Markdown links, privacy rules, and documentation consistency.

The compact data under `docs/forensics/data` is the preferred input for agents
that do not have access to the Windows research workstation.

## Work that requires Windows and VEGAS

- Open, relink, save, or inspect `.veg` projects.
- Resolve ScriptPortal.Vegas APIs and COM behavior.
- Enumerate installed OFX/VST/native plug-ins.
- Verify missing-media and missing-plugin dialogs.
- Render representative frames, stems, or effect ablations.
- Confirm Pan/Crop, Track Motion, transition, velocity, and effect-key behavior
  after save/reopen.
- Run unattended VEGAS integration probes.
- Validate whether stored effect parameters create the expected perception.

The scripts under `Tools/Forensics` are Windows/VEGAS research tools. Many have
hard-coded fixture paths because exact provenance matters. Review and adapt a
copy before running them. Never point a mutation or relinking script at an
original project.

## Build expectations

`Core` targets the Windows VEGAS extension environment and references VEGAS
assemblies. A macOS agent should not interpret inability to build or execute
that extension as a code defect without reproducing it on the supported Windows
toolchain.

The domain and planning code is intentionally VEGAS-free, but the current
analysis harness targets .NET Framework. It may require Mono or a future
cross-platform test project on macOS. Until that exists, Windows build and
self-test results remain the authoritative executable validation.

## Recommended Mac-to-Windows loop

1. On macOS, change hypotheses, schemas, pure planning logic, tests, or docs.
2. Keep every editing-behavior change synchronized across production code,
   deterministic tests, and `docs/editing-rules.md`.
3. Push a branch and clearly list the Windows/VEGAS validations still required.
4. On Windows, build `Core` and `AnalysisHarness`.
5. Run the deterministic self-tests.
6. For VEGAS behavior, run a disposable fixture or inspection copy.
7. Commit only sanitized evidence and compact derived data.

## Evidence that cannot be revalidated on macOS

A macOS agent may reason from committed structural evidence but must preserve
the recorded limitation when the corresponding raw project, source media,
render, or installed plug-in is unavailable. Parameter presence proves what was
stored; it does not always prove the visible result or contextual editorial
intent.
