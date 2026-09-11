# Inference monitor

`AutoEditing.InferenceMonitor` is a standalone Windows desktop application for
observing the LLM editor without attaching a debugger to VEGAS. It watches the
durable session store beneath:

```text
%LOCALAPPDATA%\AutoEditing\automation\sessions
```

The monitor shows sessions, individual planning/review requests, full system
and user prompt context, streamed assistant text, response metadata, token
usage, timings, errors, and visual-evidence references. It is read-only: closing
or crashing the monitor cannot stop inference or mutate a VEGAS timeline.

## Live response contract

The llama.cpp, OpenAI, and DeepSeek clients use OpenAI-compatible server-sent events
and request the final usage block. Each inference call creates:

```text
inference/exchanges/0001-<operation>/
  request.json
  assistant.partial.txt
  assistant.txt
  response.json
  error.json
```

`assistant.partial.txt` is append-only while generation is active.
`assistant.txt` is the exact finalized assistant message. `response.json`
contains backend and model identity, finish reason, token counts, prompt
processing time, measured time to first streamed assistant token, generation
time, retry count, and response ID. `error.json` appears when inference or downstream completion
handling fails. Truncated assistant text is retained even when the output-token
limit causes the edit operation to fail.

Visual media is not duplicated into `request.json`; the transcript stores its
description, media type, encoded size, and SHA-256 identity. The corresponding
preview remains a normal session artifact.

## Running

From Visual Studio, set `AutoEditing.InferenceMonitor` as the startup project.
It can run before VEGAS or before an AI edit; new sessions and tokens appear
automatically.

The Deploy configuration copies the application to:

```text
%LOCALAPPDATA%\AutoEditing\bin\InferenceMonitor
```

The monitor requires no model-provider credentials because it never calls a
model server directly. OpenAI API keys are held by Windows Credential Manager
and are never copied into the monitored session tree.

## Context and output budgets

The quality-first local defaults reserve a 65,536-token server context, allow up
to 32,768 generated tokens for a complete edit plan, and allow up to 8,192 for
each candidate review. They can be changed independently:

```text
AUTOEDITING_LLM_MIN_CONTEXT_TOKENS=65536
AUTOEDITING_LLM_PLAN_MAX_OUTPUT_TOKENS=32768
AUTOEDITING_LLM_REVIEW_MAX_OUTPUT_TOKENS=8192
```

For llama.cpp, the context size itself is owned by the server, not the
OpenAI-style request. Configure the server with at least the same context (for
example, `--ctx-size 65536`) and restart it. AutoEditing checks `/props` before
planning and reports the configured and required sizes when the server is too
small. OpenAI does not expose the llama.cpp health, props, or slot endpoints,
so those local-server probes are skipped; request/response streaming and durable
usage telemetry remain available.

Larger output limits are ceilings, not targets. Usage telemetry records the
actual generated tokens, allowing later evaluation of whether still larger
online models or contexts are economically reasonable.

## Usage identity and workbench progress

Lifetime usage is partitioned by the exact backend-and-model pair. Two providers
serving the same model name therefore never share a lifetime total. A session
that crosses a backend or model boundary is explicitly labeled as mixed; its
combined session totals remain visible, but the workbench does not attach those
totals to one model's lifetime figure.

The workbench's progress heartbeat is also active when a stopped session is
resumed by a recovery companion. With llama.cpp it combines server slot
counters with the durable assembly phase. With OpenAI it uses streamed response
progress, token usage blocks, and the same durable assembly phase. Visible
stages distinguish sketch generation, per-clip planning and materialization,
checkpoint/full-render work, rough-cut audit, polish previews, and final
validation. Monitoring failure is non-authoritative and cannot cancel
inference or mutate the timeline.

Remote OpenAI and DeepSeek requests publish a provider-neutral heartbeat once per second.
DeepSeek `reasoning_content` deltas count as live activity without being appended
to the model's final plan response.
Before output begins it shows that the request is still active and waiting for
the first token. While streaming, the workbench shows elapsed time, an
approximate text-only prompt-token count, approximate generated tokens, and
time since the most recent streamed token. These estimates are explicitly
replaced by the provider's exact final usage block when the response completes;
image-token usage is never guessed from base64 size.
