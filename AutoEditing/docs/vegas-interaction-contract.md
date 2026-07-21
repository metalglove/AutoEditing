# VEGAS Interaction Contract

This document defines how AutoEditing code interacts with VEGAS Pro. Agents and
contributors must follow this contract when adding or changing VEGAS behavior.

The architecture and review workflow described here were verified by the user in
VEGAS Pro 20 on 2026-07-21.

## Core rule

Application and domain code must not manipulate `ScriptPortal.Vegas` objects
directly. Every interaction crosses one of three explicit boundaries:

```text
Command → mutate VEGAS
Query   → read VEGAS and return a DTO snapshot
Event   ← report that VEGAS host state changed
```

Only bootstrap code, interaction infrastructure, handlers, and internal VEGAS
adapters may reference `ScriptPortal.Vegas`.

## Why this boundary exists

VEGAS timeline objects are COM-backed and sensitive to execution context. A
persistent application extension previously attempted to replace markers from a
long-lived UI callback. VEGAS failed in `IMarkerCOM.Remove(UInt32 sessionID)` with
`E_UNEXPECTED`. Nested failures from `RunScriptFile` were also hidden behind a
`TargetInvocationException`.

The verified implementation now:

- keeps COM objects inside a short VEGAS execution context;
- sends immutable request DTOs across the boundary;
- serializes operations through the host command queue;
- runs expensive analysis and planning before entering VEGAS;
- returns query snapshots instead of COM wrappers;
- preserves the nested error produced by a failed script command;
- treats UI review rows as draft state and commits them together.

## Directory structure

```text
Core/Scripts/VegasInteraction/
├── Contracts/
├── Infrastructure/
├── Adapters/
│   ├── Media/
│   ├── Montage/
│   └── Review/
├── Montage/
│   └── OperationName/
│       ├── OperationNameCommand.cs
│       └── OperationNameCommandHandler.cs
└── Review/
    ├── OperationName/
    │   ├── OperationNameCommand.cs
    │   └── OperationNameCommandHandler.cs
    └── Snapshots/
```

Use one top-level type per file. Each operation has its own directory containing
its request and handler. Reusable result DTOs belong in a clearly named folder
such as `Snapshots` and also use one type per file.

## Commands

A command represents one logical mutation and implements `IVegasCommand`.

```csharp
internal sealed class SetCursorCommand : IVegasCommand
{
	public string CommandType => "SetCursor";
	public double TimelineSeconds { get; set; }
}
```

Command names are imperative and describe an outcome:

- `SetCursorCommand`
- `CommitClipReviewCommand`
- `BuildMontageCommand`

A handler performs the mutation synchronously in the VEGAS context:

```csharp
internal sealed class SetCursorCommandHandler
	: VegasCommandHandler<SetCursorCommand>
{
	public override string CommandType => "SetCursor";

	protected override void Execute(Vegas vegas, SetCursorCommand command)
	{
		vegas.Transport.CursorPosition =
			Timecode.FromSeconds(command.TimelineSeconds);
	}
}
```

The UI awaits the command client:

```csharp
await _vegasCommands.ExecuteAsync(
	new SetCursorCommand { TimelineSeconds = target });
```

Commands normally return no read model. If the caller needs current VEGAS state,
run a separate query after the command.

## Queries

A query reads VEGAS without changing it and implements `IVegasQuery<TResult>`.

```csharp
internal sealed class GetReviewClipSnapshotQuery
	: IVegasQuery<ReviewClipSnapshot>
{
	public string CommandType => "GetReviewClipSnapshot";
	public int ClipIndex { get; set; }
}
```

The query handler may enumerate COM-backed objects, but it must convert everything
to plain DTO data before returning:

```csharp
ReviewClipSnapshot snapshot = await _vegasQueries.QueryAsync(
	new GetReviewClipSnapshotQuery { ClipIndex = index });
```

A query must never mutate VEGAS. For example, moving the cursor does not belong
in `GetReviewClipSnapshotQuery`; it is a separate `SetCursorCommand`.

Query results must not contain:

- `Vegas`;
- `Project`;
- `Marker` or `Region`;
- `Track` or `TrackEvent`;
- `Media`, `Take`, or stream wrappers;
- any other `ScriptPortal.Vegas` object.

## Host events

`IVegasHostEventSource` converts VEGAS notifications into application-level event
data. The current source observes verified VEGAS Pro 20 events including marker,
cursor, project, and timeline changes.

Events announce facts or invalidate cached UI state. They must not secretly
perform mutations.

Examples:

- marker changes tell the review UI that a refresh may be needed;
- project close clears active review drafts;
- cursor changes may update navigation state later.

Avoid automatically querying on every high-frequency event. Coalesce, debounce,
or show an explicit refresh action when appropriate.

## Asynchrony and threading

The task-based API makes UI sequencing asynchronous, but it does not make VEGAS
COM calls parallel or background-safe.

```text
background thread: decode, detect, plan, solve, serialize
VEGAS context:     short query or mutation
background/UI:     consume DTO result and update presentation state
```

Never use `Task.Run` around a `Vegas`, `Project`, `Marker`, `Region`, `Track`, or
event operation. Never retain one of those objects in a ViewModel or domain
object.

The command client queues work through the host command mechanism. The script
executor then invokes the assembly through `RunScriptFile`, which establishes the
verified mutation context. Requests are serialized, and each request carries a
correlation ID.

The final COM mutation may briefly occupy the VEGAS host thread. Keep handlers
small. Perform audio analysis, content hashing, beat detection, planning, and
retiming mathematics before submitting the command.

## Handler and adapter responsibilities

Handlers define the application operation. Internal adapters contain reusable
VEGAS-specific mechanics.

```text
Handler
→ validate command
→ call pure domain service when needed
→ call internal VEGAS adapter
→ finish without leaking COM objects
```

Code under `Core/Domain` must not import `ScriptPortal.Vegas`. Pure preparation
belongs there, such as `MontagePreparationService` and `PreparedMontage`.

VEGAS implementations belong under `VegasInteraction/Adapters`, such as timeline
construction, effects application, media inspection, and review layout.

## Atomic review commit

The review DataGrid is authoritative draft state. Changing outcome or gun, adding
a manual event, or deleting a row does not mutate VEGAS markers immediately.

When the user marks a clip ready:

```text
review rows
→ ReviewMarkerSubmission DTOs
→ CommitClipReviewCommand
→ one bridged handler invocation
→ validate region and submitted events
→ persist the ready clip
→ remove temporary review objects
```

This avoids repeated COM marker replacement and guarantees that “mark ready” sees
one ordered review state.

## Error handling

The script executor writes command status and the complete nested exception to
the command envelope. When `RunScriptFile` throws `TargetInvocationException`,
the caller reads the failed envelope and reports the underlying exception.

Successful command files are deleted. Failed command files are retained for
diagnosis as:

```text
AutoEditing.vegas-command.json
```

Do not replace the detailed handler error with only the outer invocation error.
Add contextual stage names around multi-step VEGAS operations.

## Adding a new interaction

1. Decide whether the operation is a command, query, or event.
2. Create a workflow/operation directory.
3. Add one request type per file.
4. Add one handler per file.
5. Use DTO fields only; do not put COM objects in a request or result.
6. Register the handler in `VegasCommandHandlerRegistry`.
7. Put expensive preparation in a pure domain service.
8. Put reusable VEGAS mechanics in an internal adapter.
9. Call the command or query client from the UI.
10. Compile Core and the analysis harness.
11. Run the relevant workflow in VEGAS Pro before describing it as verified.

## Review checklist

Before accepting a VEGAS interaction change, verify:

- [ ] The ViewModel does not access `Vegas` or COM timeline objects.
- [ ] Domain code does not reference `ScriptPortal.Vegas`.
- [ ] A query has no side effects.
- [ ] A command represents one logical mutation.
- [ ] Requests and results contain DTO data only.
- [ ] Every operation and top-level type has a clear file location.
- [ ] The handler is registered.
- [ ] Expensive work happens outside the VEGAS execution context.
- [ ] Nested errors remain visible.
- [ ] Core and the analysis harness compile.
- [ ] The behavior has been smoke-tested in VEGAS Pro.

## Verified workflow

The user verified the revised VEGAS Pro 20 interaction path after deployment,
including the command/query/event architecture and atomic review workflow. Future
changes are unverified until their affected VEGAS operation is exercised again.
