# Rule Comparison

Every candidate editorial/technique rule proposed by either project's evidence register, checked
against the other project. Classification values as defined in [README.md](README.md).

| Candidate rule | Editor 1 evidence | Editor 2 result | Classification |
|---|---|---|---|
| Hard cut at every treatment-escalation point, never a crossfade | 0/21 overlap, zero exceptions | No comparable escalation-tier structure exists to test | Insufficiently supported (E2 side) |
| VEGAS automatic crossfade (Smooth/Smooth, symmetric length) explains most overlaps | 114/114 match | 16/17 match, 1 exception | **Shared mechanically and contextually** |
| Continuous, gapless picture coverage | 0/275 gaps | 1/157 gap found | **Contradicted** |
| Two-tier ordinary/impact effect system | 224 ordinary + 26 impact, cleanly separated | No comparable two-tier split; one dominant single-effect treatment instead | **Contradicted** |
| `Mo Blur Length` below Sapphire's documented realistic default on the hit effect | 0.8 / 2.0 | 0.8 | **Shared technique, different parameters** (exact match on the 0.8 value) |
| Zoom/`Z Dist` animation as part of the hit-effect signature | Yes, ~10% pulse | No — `Z Dist` static | **Contradicted** |
| Project-wide persistent texture-overlay track | Yes (`Screen`-blend clip) | No (per-event `S_FilmDamage` instead) | **Contradicted** |
| Track-level ambient `S_Flicker` | Yes | Yes | **Shared** |
| Track-level ambient `S_Shake` (in addition to Flicker) | Yes | No | **Editor 1-specific** |
| Single fixed source excerpt reused for hit/impact SFX | Yes, with per-instance processing | Partially — native audio reuse + one small unprocessed accent sample | **Shared technique, different parameters** |
| Hit/impact SFX aligned to markers and cuts simultaneously | 97.6% within 30ms | 100% at 0ms | **Shared mechanically and contextually** (strongest replicated finding) |
| No ducking/volume-automation mechanism | Confirmed absent | Confirmed absent | **Shared (as an absence)** |
| Dedicated whoosh/transition-cue sample | Yes, 24 uses | None found | **Editor 1-specific** |
| Fast-in/Slow-out fade convention on music | Yes | Yes | **Shared** |
| Track-fader attenuation on audio (non-zero dB) | Yes (-3.0/-3.6dB) | No (0dB) | **Contradicted** |
| Missing/unavailable plugin dependencies | 4 unavailable | 0 unavailable | **Plugin/capability-dependent**, not an editor-technique finding |
| Dominant velocity curve shape (4-point single-dip) | 68% of events | Not found — 7-point double-dip dominates instead (81%) | **Contradicted** |
| Velocity plateau ≈0.5x | n=275, mean 0.52 | n=1 detailed sample, 0.5x | **Shared technique, different parameters (unconfirmed at scale for E2)** |
| Curated-vs-raw/connective source-tiering by filename pattern | Yes | Yes | **Shared technique, different parameters** (tiering concept shared; per-tier treatment differs) |
| Connective-footage placement rule (after impact, before setup, crossfaded both sides) | Yes, well-evidenced | Not tested this pass | **Insufficiently supported** (E2 side reduced scope) |

## Reading this table

12 of 19 candidate rules are **Contradicted** or **Editor-specific** — the majority. Only 6 are
**Shared** in some form, and of those, only 2 (the automatic-crossfade mechanism, and hit/impact
SFX marker+cut alignment) are shared with high confidence on both sides. This is exactly the
"replicated core, divergent style" pattern summarized in [README.md](README.md) — the honest
takeaway from two data points is that very little should be treated as universal, and even the
strongest replicated items should be framed as "worth defaulting to," not "proven correct."
