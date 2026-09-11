# LLM editing companion

`AutoEditing.LlmEditor` is the out-of-process planning and workflow coordinator
for the AI montage workbench. It targets .NET 8, does not reference VEGAS or
WPF, and talks to the extension only through versioned contracts and a durable
typed job spool.

The workbench supports `llama-server` from llama.cpp, the OpenAI API, or the
DeepSeek API through their chat-completions endpoints. Select the provider from **AI
Montage > Settings** in the VEGAS extension. Text and image evidence are
supported; model output is always treated as untrusted data and must pass the
same strict schema and deterministic editing validators regardless of provider.

```powershell
$env:AUTOEDITING_LLM_ENDPOINT = "http://127.0.0.1:8080/v1/"
$env:AUTOEDITING_LLM_MODEL = "the-served-model-name"
dotnet run --project LlmEditor/AutoEditing.LlmEditor.csproj -- --self-test
```

For OpenAI, enter the API key into the masked settings field. The key is stored
as a generic credential in Windows Credential Manager under
`AutoEditing/Inference/OpenAI`; it is not written to `inference.json`, session
artifacts, logs, prompts, or companion-process arguments. The field is cleared
after saving and the UI exposes only whether a credential exists.

DeepSeek uses a separate masked credential stored under
`AutoEditing/Inference/DeepSeek`. The configurator exposes only the current
documented V4 models: `deepseek-v4-pro` and `deepseek-v4-flash`. Both support
non-thinking mode or thinking mode at `high`/`max` effort. DeepSeek reasoning
chunks are observed for the workbench activity heartbeat but never mixed into
the final JSON plan. Its documented chat-completions API is text-only, so a
phase that requires image evidence fails explicitly instead of silently
discarding that evidence. See the [DeepSeek model table](https://api-docs.deepseek.com/quick_start/pricing/)
and [thinking-mode contract](https://api-docs.deepseek.com/guides/thinking_mode/).

Provider settings are stored at
`%LOCALAPPDATA%\AutoEditing\settings\inference.json`. Existing local
installations can continue using `.env`. `AUTOEDITING_LLM_PROVIDER` accepts
`llamacpp` or `openai`; when it is explicitly set, the generic endpoint, model,
and API-key variables override the saved provider configuration. An OpenAI
selection made in the UI deliberately ignores legacy local endpoint/model
values from `.env`, preventing accidental routing to the wrong server.

The shared client also keeps provider-specific sampling fields separate.
llama.cpp receives AutoEditing's configured temperature and deterministic seed.
OpenAI requests omit those local-only overrides and send the reasoning effort
selected in AI settings (`minimal`, `low`, `medium`, `high`, or `xhigh`). New
installations default to `gpt-5.6-luna` at `high`; the schema migration changes
only the previous `gpt-5.6-sol` default and preserves other explicitly selected
models.

## Editor-like workflow

The normal workbench is progressive rather than a one-shot plan:

1. persist the reviewed footage and committed song-analysis request;
2. ask the model for a semantic assembly sketch;
3. request and validate one `ClipStepDecision`;
4. materialize the growing candidate in a session-owned VEGAS workspace;
5. let the editor compare, preview, adjust, accept, reset, or revise that clip;
6. reconcile supported live timeline changes into canonical evidence;
7. continue until every selected clip is synchronized;
8. render and audit the complete rough cut;
9. review separate effects and audio/SFX passes;
10. explicitly promote the validated candidate and retain a rollback bundle.

The editor remains authoritative. Move, trim, duration, and constant-speed
changes are adopted only after reconciliation. Missing, extra, ambiguous,
foreign, out-of-bounds, or variable-velocity events enter an explicit conflict
workflow; they are never silently restored or accepted.

## Evidence and steering

Checkpoint previews and full-rough-cut evidence are rendered in bounded chunks.
All numbered checkpoint attempts remain available for A/B playback, and
finishing a semantic song section automatically creates a hash-verified
complete-section render.
The multimodal reviewer sees hash-verified timeline metadata and sampled VEGAS
images. Its observations may recommend a revision, but cannot mutate or accept
the timeline. The workbench exposes the proposal, rationale, confidence,
evidence, live comparison, conflicts, recovery state, polish plans, rendered
previews, token usage, final report, and rollback availability.

Steering is durable input to the next scoped planning request. The system stores
complete request/response conversations and usage records for the separate
Inference Monitor. It records concise evidence-backed decisions, not private
model chain-of-thought.

## Recovery and safety

- Session state, action claims, action execution, plans, renders, decisions,
  promotion intent, reports, and archives are durable artifacts.
- One runtime lease owns a session; exact state-revision actions prevent stale
  or duplicate button clicks from racing.
- VEGAS mutations use typed allow-listed operations, project fingerprints,
  candidate-workspace ownership, payload hashes, and operation-scoped
  idempotency keys.
- Model output cannot dispatch C#, script text, COM objects, or arbitrary VEGAS
  commands.
- Rough-cut acceptance persists a complete plan/workspace/snapshot-bound
  timeline baseline. Polish and finalization re-read live VEGAS and fail closed
  on track, audio automation, fade, grouping, effect, or placement divergence.
- Final review explicitly chooses whether to render and verify one more
  complete preview before promotion; the choice is durable across restart.
- Promotion renames only verified candidate-owned tracks. Rollback verifies the
  promoted evidence before restoring the candidate labels.

The older `plan --planner fake` and `plan --planner local` commands remain
useful as one-shot transport/schema diagnostics. Workbench launches use
`--planner configured`; `local` remains an accepted compatibility alias.

Run all companion self-tests with:

```powershell
dotnet run --project LlmEditor/AutoEditing.LlmEditor.csproj -- --self-test
```

See [the clip-by-clip workflow plan](../docs/llm-clip-by-clip-workflow-plan.md),
[the automation architecture](../docs/llm-vegas-automation-architecture.md), and
the normative [editing rulebook](../docs/editing-rules.md).
