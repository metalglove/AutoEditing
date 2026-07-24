# VEGAS Pro integration probe

Status: design validated against the locally installed VEGAS Pro 20 API surface.
Launching VEGAS and exercising a fixture remains an explicit opt-in step.

## Purpose

The integration probe closes the gap between deterministic domain tests and
behavior inside the real VEGAS host. It is intended to answer questions such as:

- Did the built extension load?
- Did `BuildMontage` create the expected tracks and events?
- Did a screen-pump action create valid Pan/Crop keyframes?
- Did those keyframes survive saving and reopening the project?
- Can VEGAS render or snapshot the affected frames?

The probe is not a general-purpose remote control for an editor's open project.
Every run owns a new VEGAS process and a disposable working directory.

## What was verified locally

The installed host is:

```text
C:\Program Files\VEGAS\VEGAS Pro 20.0\vegas200.exe
C:\Program Files\VEGAS\VEGAS Pro 20.0\ScriptPortal.Vegas.dll
```

`ScriptPortal.Vegas.dll` exposes the following relevant managed methods in this
installation:

```text
Vegas.OpenFile(string)
Vegas.OpenProject(string)
Vegas.NewProject()
Vegas.RunScriptFile(string[, string])
Vegas.RunScriptText(ScriptEngineType, string[, string])
Vegas.WaitForIdle()
Vegas.SaveProject(string)
Vegas.Render(RenderArgs)
Vegas.SaveSnapshot(...)
Vegas.Exit()

IVegasCOM.RunScriptFile(string scriptFile, string scriptArgs)
IVegasCOM.RunScriptText(string text, ScriptEngineType, bool, string scriptArgs)
IVegasCOM.WaitForIdle()
IVegasCOM.Exit()
```

The script context exposes:

```text
Script.File
Script.Directory
Script.RawArgs
Script.Args.ValueOf(name)
Script.Args.Exists(name)
```

VEGAS Pro's official help documents `/NOLOGO`, `/OPEN`, `/RUNSCRIPT` (also
`/SCRIPT`), `/SCRIPTARGS`, and `/CMDMODULE`. The exact VEGAS 20 invocation must
still receive a one-time smoke test because the currently published command-line
page describes the latest host, although these switches predate version 20.

The `IVegasCOM` GUID is present as an imported interface in
`ScriptPortal.Vegas.dll`, but no VEGAS application COM class is registered on
this machine. It is the backing interface used by the in-process managed
`Vegas` wrapper, not a supported `CreateObject`/out-of-process automation entry
point. A runner must enter through a VEGAS-hosted script.

## Recommended topology

```text
dotnet/PowerShell runner
  -> creates %TEMP%\AutoEditing.VegasProbe\<run-id>\
  -> copies fixture inputs into that directory
  -> writes request.json atomically
  -> starts its own vegas200.exe process
       /NOLOGO
       /OPEN "<disposable fixture.veg>"
       /SCRIPTARGS "request=<absolute request.json>"
       /RUNSCRIPT "<absolute VegasProbe.Script.dll>"
  -> VEGAS-hosted probe reads request.json
  -> probe invokes a production command or a read-only assertion
  -> probe saves only to the run directory
  -> probe writes result.json atomically
  -> probe sets the script exit flag or calls Vegas.Exit()
  -> runner validates result schema, process exit, and artifacts
```

Prefer one startup script that performs the whole scenario. Calling
`Vegas.RunScriptFile` from an already running script is useful for delegating to
the production `Core.dll`, but it remains synchronous and re-entrant inside the
same UI thread. `WaitForIdle` does not make script code asynchronous; it only
allows pending host work to settle.

The first implementation should use a precompiled .NET Framework 4.8 script
assembly. This avoids CodeDOM/compiler differences and lets it share typed
contracts with `Core`. Its entry point remains the standard:

```csharp
public sealed class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        string requestPath = Script.Args.ValueOf("request");
        // Validate, execute, write result, and exit in a finally block.
    }
}
```

Do not pass a JSON document on the command line. Pass only an absolute request
path. This avoids quoting limits and leaves a durable diagnostic artifact.

## Result handshake

Each run directory contains:

```text
request.json
request.ready
result.json.tmp
result.json
probe.log
fixture-input.veg
fixture-output.veg
fixture-reopened.veg       (optional)
snapshots\                 (optional)
render\                    (optional)
```

The runner writes `request.json.tmp`, renames it to `request.json`, then creates
`request.ready`. The host performs the same temporary-file-and-rename operation
for `result.json`. Both documents contain a random 128-bit `runId`; a result
with another ID is rejected.

Minimum result fields:

```json
{
  "schemaVersion": 1,
  "runId": "hex-guid",
  "status": "passed | failed | blocked",
  "startedUtc": "ISO-8601",
  "finishedUtc": "ISO-8601",
  "vegasVersion": "string",
  "projectInput": "absolute disposable path",
  "projectOutput": "absolute disposable path",
  "assertions": [],
  "artifacts": [],
  "warnings": [],
  "exception": null
}
```

Every assertion records `id`, `expected`, `actual`, `passed`, and enough track,
event, and timeline identity to diagnose the failure. The host catches
exceptions, unwraps `TargetInvocationException`, writes the complete exception
chain, then attempts a clean exit.

## Minimal seam in AutoEditing

The current `VegasScriptCommandExecutor` writes one fixed
`AutoEditing.vegas-command.json` beside the deployed assembly and invokes
`Core.dll` through `Vegas.RunScriptFile`. That is appropriate for the docked UI
but unsafe for integration runs because it is shared and can collide with an
interactive session.

Implement the probe seam in three small parts:

1. Extract command dispatch from `TryExecutePending(Vegas)` into
   `ExecuteEnvelope(Vegas, absoluteEnvelopePath)`. Keep the existing fixed-path
   overload unchanged for the UI.
2. Add an integration-only entry point that accepts `request=<path>`, requires
   the path to be inside the probe run directory, and dispatches the same
   production handler registry.
3. Add read-only query handlers under
   `VegasInteraction/Diagnostics/`, beginning with
   `InspectScreenPumpsQueryHandler`.

Do not teach the test runner to click the docked UI. Send the same
`BuildMontageCommand` that the UI sends, through the same handler registry, then
inspect the result through typed diagnostics. This tests the production
orchestrator and renderer without making WPF automation part of the contract.

The integration entry point must be built into a separate probe assembly or
guarded by an explicit `mode=integration-probe` argument. It must never become a
normal extension menu command.

## Screen-pump assertions

Structural verification is the primary oracle. For every expected pump:

1. Find the target `VideoEvent` by the production placement identifier, not only
   by its index.
2. Convert the requested absolute timeline time to event-relative time.
3. Read `videoEvent.VideoMotion.Keyframes` after rendering.
4. Identify baseline, peak, and recovery keyframes within a frame-rate-aware
   tolerance.
5. Assert every keyframe `IsValid()`.
6. Assert positions are within `[0, event.Length]` and monotonically ordered.
7. Calculate the polygon center, width, height, and area from `Bounds`.
8. Assert the peak viewport is smaller than the baseline viewport by the
   configured zoom ratio, within tolerance.
9. Assert the recovery bounds equal the baseline bounds within a small geometric
   tolerance.
10. Assert keyframe type/interpolation matches the renderer contract.

Also report:

- planned pumps;
- pumps with a target video event;
- rendered pumps;
- missing-target pumps;
- rejected keyframes;
- duplicate/near-duplicate keyframes;
- unexpected mutations to non-target events.

Run the assertions once immediately after `BuildMontage`, save to
`fixture-output.veg`, reopen that disposable output with `Vegas.OpenFile` or
`OpenProject`, reacquire all project objects, and run the same assertions again.
Never retain `Track`, `Event`, `Take`, or keyframe instances across a project
open; they become invalid COM-backed objects.

The saved-project pass is essential. An operation returning without an exception
does not prove that VEGAS persisted it.

## Fixture strategy

Start with one tiny deterministic fixture:

- 1920x1080 or 1280x720, 29.97 fps;
- one short locally generated video with obvious frame numbers;
- one short WAV click track;
- two or three reviewed kills at known source times;
- three planned pumps: kill A, an intermediate beat, and kill B;
- no third-party OFX;
- no missing media;
- all paths copied below the run directory.

Generate media before VEGAS starts, preferably with a pinned `ffmpeg` version,
or check a very small lossless fixture into a dedicated test-assets package.
The fixture request should build a new project inside VEGAS rather than copy a
user project. A project supplied for forensic inspection must first be copied
with its media to the run directory and is read-only by policy.

## Visual verification

Structural assertions are reliable enough for CI-like local checks. Visual
checks are secondary:

- `Vegas.SaveSnapshot` at baseline, peak, and recovery is cheaper and less
  codec-dependent than rendering.
- Compare the peak snapshot with the baseline using crop/scale-aware image
  metrics, not a raw pixel hash.
- Optionally render a 1-2 second selection around the pump through an explicitly
  configured renderer/template.
- Enumerate active renderers/templates inside VEGAS and fail as `blocked` if the
  configured template is absent. Never silently choose a different template.
- Set `RenderArgs.WaitForIdle = true`; accept only `RenderStatus.Complete`.

GPU drivers, decoder versions, resampling, color management, and encoder
templates can make pixels vary. Therefore image comparisons should use
tolerances and must not replace the keyframe assertions.

## Timeouts and liveness

Use separate deadlines:

- startup/result-file appearance: 90 seconds;
- non-render scenario: 3 minutes;
- snapshot scenario: 5 minutes;
- short render: 10 minutes.

The host updates `heartbeat.json` at phase boundaries, not from a background
thread that touches VEGAS. ScriptPortal objects are apartment/UI-thread-bound.
The runner records stdout/stderr if any, process state, window titles, and the
last heartbeat.

If a timeout occurs:

1. Capture process/window diagnostics and a desktop screenshot when permitted.
2. Request a graceful close only for the exact process ID started by this run.
3. Wait a short grace period.
4. Terminate that exact owned process only when the run was launched with
   `--allow-force-close`.
5. Never enumerate and kill every `vegas200` process.

By default, if any VEGAS process already exists, the runner refuses to start and
reports `blocked`. A future `--allow-parallel-instance` option may relax this,
but should not be the default because file associations, extension state, media
decoders, and modal windows are per interactive desktop and sometimes shared.

## Modal-dialog detection

There is no supported headless/modal API. The runner should enumerate top-level
Windows owned by its process ID with `EnumWindows` and record visible windows
whose class/title differ from the main VEGAS window. Treat an unexpected visible
owned window persisting longer than 10 seconds as a blocked run.

Known categories include licensing, missing media, offline media replacement,
plug-in errors, project upgrade prompts, codec dialogs, crash recovery, and save
confirmation. The probe must not automatically press buttons on unknown dialogs.
A later allowlist may handle a small set of proven harmless dialogs, but the
initial runner should capture diagnostics and stop.

Launching minimized reduces disruption but does not make VEGAS headless and does
not prevent modal dialogs.

## Safety and security contract

- Refuse an input `.veg` path unless it is below the run directory, or copy it
  there before launch.
- Canonicalize every request and output path; reject traversal, UNC paths, device
  paths, and reparse-point escapes.
- Never overwrite an existing file. Create a unique run directory and use
  create-new semantics.
- Never invoke `SaveProject()` without an explicit new path.
- Never run against an untitled or interactive user's project.
- Refuse symlinked/reparse-point media and output paths in the first version.
- Accept only a fixed command/query allowlist; do not accept script text or an
  arbitrary assembly path from request JSON.
- Quote every process argument with `ProcessStartInfo.ArgumentList` where
  available, or a tested Windows quoting routine on .NET Framework.
- Record hashes of the probe and production assemblies.
- Run under the interactive user, never as a Windows service or elevated task.
- Never dismiss licensing or security dialogs automatically.
- Clean up only the unique run directory and process ID created by the runner.
- Retain failed run artifacts by default.

VEGAS scripts have the full privileges of the current user. The request file is
therefore an internal trusted test protocol, not a remote API.

## Exact runner commands

Proposed commands after implementation:

```powershell
dotnet build AutoEditing/Tools/VegasIntegrationProbe/VegasIntegrationProbe.csproj

AutoEditing/Tools/VegasIntegrationProbe/bin/Debug/net48/VegasIntegrationProbe.exe `
  verify-screen-pumps `
  --vegas "C:\Program Files\VEGAS\VEGAS Pro 20.0\vegas200.exe" `
  --core "AutoEditing/Core/bin/Debug/Core.dll" `
  --fixture "AutoEditing/Tools/VegasIntegrationProbe/Fixtures/screen-pumps.json" `
  --artifacts "$env:TEMP\AutoEditing.VegasProbe"
```

The runner should log the actual invocation in an escaped, copyable form. The
expected host command is:

```powershell
& "C:\Program Files\VEGAS\VEGAS Pro 20.0\vegas200.exe" `
  /NOLOGO `
  /OPEN "<run-dir>\fixture-input.veg" `
  /SCRIPTARGS "request=<run-dir>\request.json" `
  /RUNSCRIPT "<absolute-path>\VegasIntegrationProbe.Script.dll"
```

If VEGAS 20's one-time switch smoke test shows ordering sensitivity, use
`/OPEN`, then `/SCRIPTARGS`, then `/RUNSCRIPT` as above and freeze that ordering
in a process-argument unit test.

## Phased delivery

### Phase 0: opt-in smoke probe

- Preflight executable/API versions and assert no VEGAS process is running.
- Launch a script that writes version/project information to `result.json`.
- Confirm `/SCRIPTARGS` parsing and clean `Vegas.Exit()`.
- No project mutation or render.

### Phase 1: structural fixture

- Create/copy the tiny fixture in a unique run directory.
- Invoke the production `BuildMontageCommand`.
- Add `InspectScreenPumpsQuery`.
- Save, reopen, and repeat assertions.
- Make this a developer-invoked test, not part of every build.

### Phase 2: snapshot verification

- Save baseline/peak/recovery snapshots.
- Add tolerant image checks and an HTML/Markdown artifact summary.

### Phase 3: short render and broader recipes

- Pin a renderer/template by ID and validate availability.
- Render one short selection.
- Extend diagnostics to velocity envelopes, audio routing/SFX, flashes, shake,
  and transitions as those renderers become implemented.

### Phase 4: scheduled workstation verification

- Run serially on a dedicated licensed Windows workstation or interactive VM.
- Preserve logs and artifacts.
- Do not advertise this as true headless CI.

## Reliability conclusion

Automated structural verification is realistic and valuable. It would have
caught both the invalid unattached `VideoMotionKeyframe` ordering bug and a
"planned but not rendered" screen-pump regression.

True headless reliability is not realistic: VEGAS remains a licensed GUI
application with UI-thread-affine COM objects and possible modal dialogs.
Short, isolated, serial, disposable integration runs on an interactive Windows
desktop can nevertheless be dependable enough as an opt-in pre-PR or nightly
gate. The primary pass/fail oracle should be project structure after save/reopen;
snapshots and renders should provide supporting evidence.
