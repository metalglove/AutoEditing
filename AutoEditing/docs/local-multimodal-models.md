# Local multimodal model recommendations

This document records the quality-first local-model strategy for the future
LLM-driven editor. It is an evaluation plan, not implemented editing behavior.

Target hardware:

- two NVIDIA RTX 5060 Ti GPUs with 16 GB VRAM each;
- no NVLink, so the cards do not behave like one transparent 32 GB device; and
- inference latency is secondary to editorial quality.

## Recommendation

Evaluate
[Qwen3-VL-30B-A3B-Instruct](https://huggingface.co/Qwen/Qwen3-VL-30B-A3B-Instruct)
first as the quality candidate. Run a supported 4-bit quantization across both
GPUs and measure the real memory, PCIe, context, and stability characteristics
before making it a dependency.

The model has approximately 31 billion total parameters with about 3 billion
active parameters per token. Quantized weights may fit across both cards, but
vision processing, KV cache, inference workspace, and the lack of NVLink make
this an empirical feasibility question rather than a guaranteed configuration.
Slow tensor-parallel inference is acceptable for the intended offline workflow.

Use these challengers:

1. [Gemma 4 12B](https://huggingface.co/google/gemma-4-12B) at 4-bit. It accepts
   text, image, video, and audio input and produces text, but its recently
   introduced serving path must be validated before adoption.
2. [InternVL 3.5 14B](https://huggingface.co/OpenGVLab/InternVL3_5-14B) at
   4-bit as a strong image/video reasoning alternative.
3. [MiniCPM-V 4.5](https://huggingface.co/openbmb/MiniCPM-V-4_5) at 4-bit as an
   efficient long-video challenger with aggressive visual-token compression.
4. [Qwen3-VL-8B-Instruct](https://huggingface.co/Qwen/Qwen3-VL-8B-Instruct) at
   4-bit as the quality, memory, and throughput baseline.

Do not select a model from vendor aggregate benchmarks alone. Choose it from
frozen tests built from the actual reviewed gameplay, music maps, forensic
style evidence, and edit-plan contract.

## System design

Do not ask one model call to understand all media and construct the complete
timeline. Separate perception, deterministic timing, planning, and criticism:

```text
clips
  -> timestamped visual analysis
  -> persistent clip summaries

song
  -> deterministic beat, section, energy, and event analysis

clip summaries + song map + retrieved editor examples
  -> candidate EditPlanDocument values
  -> structural validation
  -> editorial critic and revision
  -> approved plan
  -> VEGAS resource preflight and renderer
```

The same multimodal model may perform visual analysis and planning initially,
but those must remain separate requests with separate schemas. This lets later
experiments use the best visual model for perception and a different reasoning
model for planning without changing the VEGAS renderer.

## Video representation

“Video input” generally means that the serving stack decodes and samples frames.
It does not remove the need for deliberate temporal evidence selection.

For each clip, prepare:

- frames at shot boundaries;
- regular samples at 0.5, 1, and 2 frames per second for evaluation;
- denser timestamped sequences around detected action;
- crops around faces, subjects, weapons, text, and other important objects;
- existing reviewed shot events and their exact source timestamps; and
- technical metadata such as duration, resolution, frame rate, motion, and
  quality problems.

Prefer a small set of informative timestamped frames over large runs of
near-duplicate frames. Persist model-generated summaries so expensive visual
analysis is repeated only when the media, model, or analysis schema changes.

A clip-analysis record should cite its evidence:

```json
{
  "clipId": "clip-014",
  "events": [
    {
      "sourceTimeSeconds": 4.312,
      "description": "Player fires and the target is eliminated",
      "evidenceFrameIds": [
        "clip-014@4.250",
        "clip-014@4.375"
      ],
      "confidence": 0.94
    }
  ],
  "usableRanges": [],
  "cameraMotion": [],
  "subjects": [],
  "qualityProblems": [],
  "editorialPossibilities": []
}
```

The model must not invent sub-frame precision from sampled images. Exact event
times remain owned by reviewed markers and deterministic analysis.

## Audio strategy

Keep beat grids, transients, downbeats, musical regions, energy, and exact sync
times in the existing deterministic audio pipeline. Pass that information to
the planner as compact structured context.

Native model audio may later add semantic evidence such as:

- lyrics and vocal meaning;
- applause or crowd response;
- speech;
- gunshots, impacts, and environmental sounds; and
- the emotional character of a section.

It must not replace deterministic timing analysis. This keeps musical
synchronization reproducible and testable.

## Iterative evaluation

Model evaluation must measure revision quality, not only first-pass plan
quality. For every benchmark case, retain the candidate plan, deterministic
timeline report, timestamped preview frames or contact sheets, critic feedback,
and every revised plan. Score both the final result and whether successive
iterations measurably improve the failed criteria.

The portable inference path uses text plus sampled images. Native video input
can be evaluated as a model-specific challenger after the image-based loop is
reliable. Rendering and objective checks remain external tools; the model
receives their evidence and proposes another complete, versioned plan.

## Quality-first planning passes

Generate an edit through explicit passes:

1. **Select** — identify strong footage and reject unusable ranges.
2. **Structure** — assign footage to musical sections and narrative roles.
3. **Synchronize** — match visual events to eligible reviewed music events.
4. **Retime** — state editorial intent; deterministic code solves the speed
   profiles.
5. **Treat** — retrieve similar forensic examples and propose supported effects.
6. **Critique** — detect repetition, weak progression, unsupported operations,
   missed evidence, and rule violations.
7. **Revise** — produce another complete plan rather than an unstructured patch.
8. **Rank** — compare several valid candidates and retain the strongest.

Generate three to five candidates when practical. The critic must score explicit
criteria and cite the plan decisions or evidence responsible for each score.
Model preference never overrides structural validation or the normative editing
rules.

## Serving approach

Start with Linux, current NVIDIA drivers/CUDA, and
[vLLM](https://docs.vllm.ai/en/latest/) using its OpenAI-compatible API and
schema-constrained output.

- Use an available AWQ or GPTQ/Marlin quantization after confirming support for
  the GPUs and exact model version.
- Use JSON-schema constrained decoding, temperature zero for repeatability, and
  deserialize into the versioned planning contracts.
- Treat syntactically valid JSON as untrusted until domain validation passes.
- Pin the model, quantization, serving engine, prompts, frame-selection policy,
  and schemas in every benchmark result.
- Compare tensor parallelism across both cards with CPU offload where useful.
  Speed is secondary, but unstable execution and lossy context truncation are
  not acceptable.

SGLang is the secondary serving candidate. llama.cpp or Ollama may be useful for
GGUF experiments, especially with smaller models, but the production evaluation
should favor a server with reliable multimodal batching and constrained output.

## Benchmark

Create 30–50 frozen cases from representative reviewed projects:

- short action clips;
- dialogue or people-focused clips;
- low-motion and establishing footage;
- difficult quality cases;
- several one-to-five-minute sequences;
- deterministic song maps; and
- retrieved rule/example sets for each analysed editor.

Run identical inputs and planning passes against every model. Measure:

- JSON-schema success;
- structural-validator success without repair;
- unknown or hallucinated clip IDs;
- invalid source ranges and timecodes;
- visual-event recall against reviewed annotations;
- correct use of retrieved editor evidence and rule IDs;
- violations of ordering, sync, velocity, audio, and effect rules;
- editor preference in blind pairwise comparisons;
- number of critique/revision passes to an acceptable plan;
- peak VRAM per GPU, context use, PCIe utilization, and failures; and
- total time to the first acceptable plan.

Initial quality gates:

- 100% syntactically valid constrained JSON;
- at least 95% structural validation without repair;
- zero unknown clip IDs;
- zero model-authored unsupported VEGAS operations;
- every important decision cites source evidence or an editor-rule/example ID;
  and
- a clear blind human preference over the deterministic/fake baseline.

Only after a model passes these gates should the project consider LoRA or other
fine-tuning using accepted/rejected plan pairs.

## Deferred decisions

Do not yet commit the application to:

- one permanent model or provider;
- native audio reasoning;
- a particular quantization format;
- tensor parallelism versus CPU offload;
- automated plan approval;
- direct model-generated VEGAS commands; or
- fine-tuning.

The current `skeleton.fake` planner remains the only implemented LLM-editor
provider. Local-model work begins with a reproducible evaluation adapter, not by
placing model calls inside VEGAS.
