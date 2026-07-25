# LLM editor skeleton

`AutoEditing.LlmEditor` is the out-of-process planning boundary for a future
LLM-driven editor. It targets .NET 8 and has no reference to VEGAS, the WPF host,
or a model-provider SDK.

The initial command proves only the versioned planning-file exchange:

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

Live provider adapters, semantic media summaries, editor-style retrieval,
preview rendering, approval, and critic/revision loops are later milestones.
The quality-first local model candidates and benchmark are documented in
[Local multimodal model recommendations](../docs/local-multimodal-models.md).
