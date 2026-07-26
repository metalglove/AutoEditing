# LLM editor skeleton

`AutoEditing.LlmEditor` is the out-of-process planning and revision boundary for
an LLM-driven editor. It targets .NET 8 and has no reference to VEGAS or the WPF
host. Its local provider adapter uses the OpenAI-compatible HTTP protocol rather
than a model-vendor SDK.

The `fake` command proves the versioned planning-file exchange:

```powershell
dotnet run --project LlmEditor/AutoEditing.LlmEditor.csproj -- `
  plan `
  --request LlmEditor/Fixtures/skeleton-request.json `
  --output skeleton-plan.json `
  --planner fake
```

The `fake` planner deterministically creates one normal-speed placement from the
first fixture clip. Its output is labeled `skeleton.fake`; it is not an
LLM-authored edit and there is intentionally no VEGAS import command.

A local OpenAI-compatible server, such as vLLM, can exercise a single candidate
generation:

```powershell
$env:AUTOEDITING_LLM_ENDPOINT = "http://127.0.0.1:8000/v1/"
$env:AUTOEDITING_LLM_MODEL = "the-served-model-name"
dotnet run --project LlmEditor/AutoEditing.LlmEditor.csproj -- `
  plan `
  --request LlmEditor/Fixtures/skeleton-request.json `
  --output candidate-plan.json `
  --planner local
```

`AUTOEDITING_LLM_API_KEY` is optional. The output still passes the shared plan
validator, must preserve the request ID, may reference only requested clips, and
cannot overwrite an existing file.

## Iterative editing

One-shot generation is a diagnostic primitive, not the intended editing
workflow. `EditIterationOrchestrator` models the main loop:

1. generate and validate a candidate plan;
2. ask an `IEditPlanReviewer` to materialize it in isolated working state, run
   deterministic checks, render previews, and analyze those previews;
3. accept the candidate or send critique and visual evidence to the planner;
4. generate a complete replacement plan and validate it again;
5. stop on acceptance or a configured iteration limit.

The current implementation includes the orchestration contract and deterministic
tests. A VEGAS-backed reviewer and preview analyzer are not implemented yet.
Those components belong outside this project and will communicate using files
and DTOs. The local inference adapter supports text plus image data URLs, which
allows contact sheets or sampled render frames to accompany revision feedback.
Raw video ingestion remains provider-specific; the portable baseline will use
timestamped frames/contact sheets and objective render metrics.

## Edit workbench

The iteration coordinator publishes `EditIterationSnapshot` values through an
`IEditIterationObserver`. This is the boundary for a separate workbench window.
The window can show:

- the current candidate as a visual timeline;
- differences from the preceding candidate;
- preview frames and render locations;
- validation and critic findings;
- concise decision records with category, confidence, and evidence IDs; and
- whether an iteration was accepted or sent back for revision.

Reviewer feedback can also carry explicit steering instructions into the next
revision, such as locking an opener, prohibiting an effect, preserving a sync
point, or asking for a calmer section. These instructions are visible inputs,
not invisible prompt state.

Decision records are short, structured explanations backed by inspectable
evidence. The system must not depend on or attempt to expose a model's private
chain-of-thought. Durable trace persistence and the actual workbench UI remain
to be implemented.

Run the contract and determinism checks with:

```powershell
dotnet run --project LlmEditor/AutoEditing.LlmEditor.csproj -- --self-test
```

## Safety boundary

- Planning requests and plans contain DTO data only.
- Structural validation is VEGAS-independent.
- File, audio, SFX, and host checks still run inside the VEGAS-side resource
  preflight before any project cleanup or mutation.
- The CLI refuses to overwrite an existing output file.
- No arbitrary C#, script text, prompt, or model response can be dispatched to
  VEGAS.

Semantic media summaries, editor-style retrieval, preview rendering, approval,
and production critic implementations are later milestones.
The quality-first local model candidates and benchmark are documented in
[Local multimodal model recommendations](../docs/local-multimodal-models.md).
