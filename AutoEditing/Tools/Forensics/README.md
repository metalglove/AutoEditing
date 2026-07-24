# VEGAS forensic research tools

These scripts reproduce or explain portions of the Editor 1 / Project 01 and
Editor 2 / Project 01 investigations.

They are archived research tools, not production extension code.

## Layout

- [`editor-1/project-01/`](editor-1/project-01/) contains inspector revisions,
  relinking, reflection, capture, audio-render, and effect-ablation scripts.
- [`editor-2/project-01/`](editor-2/project-01/) contains its inspector,
  structure/audio queries, frame capture, relinking, media-removal fixtures,
  and the second autonomous runner.

## Safety

- Read a script completely before running it.
- Work only on a disposable project copy.
- Never change an original `.veg`, `.veg.bak`, or source-media file.
- Confirm the expected project hash and path.
- Do not reuse media-removal scripts outside their documented fixture.
- Do not blindly accept or dismiss dialogs.
- Only terminate VEGAS processes launched by the current run.
- Save, close, reopen, and inspect again before treating a relink or mutation
  as verified.

The scripts retain project-specific paths and assumptions because those are
part of their forensic provenance. Only editor identities were anonymized.

## Platform

The C# scripts use ScriptPortal.Vegas and require the corresponding Windows
VEGAS environment. The PowerShell autonomous runners also depend on Windows UI
and process behavior. JavaScript files query exported JSON and can be reviewed
or adapted on other platforms, but their referenced input paths may be
Windows-specific.

See the [platform workflow](../../docs/forensics/platform-workflow.md) and
[artifact manifest](../../docs/forensics/artifact-manifest.json).
