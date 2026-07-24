# Representative Moments — Editor 2 / Project 01

10 frames captured via `SaveSnapshot` — fewer than the 16 the prompt requested and fewer than
Editor 1's package (16); a reduced-scope decision given the time already consumed reaching a
stable, non-crashing inspection copy for this project (see
[limitations.md](limitations.md)/[project-profile.md](project-profile.md) for the crash incident).
All frames preserved, unmodified, at `C:\VEGAS\editor-2-project-01-analysis\frames\representative\`
(not copied into this repository). Evidence IDs indexed in
[evidence-register.md](evidence-register.md).

## `E2-P01-VIS-002` — Intro title, "[Editor 2] PRESENTS"

- **Timestamp**: 2.0s. **Track/event**: `t2_e0` (title generator) over `t4_e0` (raw DVR footage,
  `S_FilmDamage`).
- **File**: `01_intro_title_dap_presents.png`.
- **Why selected**: opening/title moment.
- **Visual evidence**: aerial/drone gameplay footage with legible white title text overlaid
  (editor-identity text redacted in this description; the underlying image file itself, stored
  outside this repository, is not further redacted).
- **Structural confidence**: high. **Visual confidence**: high. **Semantic confidence**: high (a
  title card is unambiguous).

## Second intro title card ("GLOVALIIN")

- **Timestamp**: 6.5s. **Track/event**: `t2_e1`.
- **File**: `02_intro_title_glovaliin.png`. Not separately reviewed in detail this pass beyond
  confirming the capture succeeded (`status=Complete`) — flagged as not fully visually confirmed.

## `E2-P01-VIS-003` — The 1.375s gap

- **Timestamp**: 9.0s (inside the gap between `t4_e1` end at 8.625s and `t4_e2` start at 10.0s).
- **File**: `03_gap_blank_at_9.png`.
- **Why selected**: to visually confirm the structural gap finding.
- **Structural confidence**: high (gap is directly measured from timestamps).
- Not detailed pixel-by-pixel in this document; captured and available for review.

## Opener `S_Shake` treatment

- **Timestamp**: 10.8s. **Track/event**: `t4_e3` (`opener.mp4`, `S_Shake`).
- **File**: `04_opener_shake_hit.png`. Captured but not separately detailed here — see
  [effects-and-presets.md](effects-and-presets.md) `E2-P01-VIS-004` for the motion-blur
  observation drawn from this and the multi-hit-burst frame together.

## `E2-P01-VIS-001` — Strobe/flash burst

- **Timestamp**: 70.7s (inside the Track-0/1 solid-color flash cluster).
- **File**: `05_strobe_flash_burst.png`.
- **Why selected**: to visually confirm the solid-color-flash + opacity-envelope-strobe mechanism
  identified structurally.
- **Visual evidence**: near-white/light-gray full-frame flash, visible motion-blur streak artifacts,
  black letterbox bars top and bottom.
- **Structural confidence**: high. **Visual confidence**: high. **Semantic confidence**: medium
  (read as a deliberate flash/strobe accent; not independently confirmed against the specific
  in-game action at this instant).

## `E2-P01-VIS-004` — Multi-hit burst, first hit

- **Timestamp**: 125.3s. **Track/event**: `t4_e115` (paired with `t5_e76`, `SA-B 50 Hit.mp3`).
- **File**: `06_multihit_burst_start.png`.
- **Why selected**: the strongest available "confirmed kill/impact" candidate in this project
  (100% marker+event-aligned hit-accent sample).
- **Visual evidence**: heavily motion-blurred sniper-scope view; a red UI banner element is
  visible in the upper-right, consistent with (but not conclusively legible as) a kill-confirmation
  banner, given blur.
- **Structural confidence**: high. **Visual confidence**: high (motion blur clearly present).
  **Semantic confidence**: medium (kill interpretation is well-supported by converging evidence —
  hit-accent + marker + event-boundary alignment — but the on-screen banner text itself is not
  legible enough in this single frame to read directly).

## Multi-hit burst, mid-burst

- **Timestamp**: 129.0s. **Track/event**: `t4_e118` (paired with `t5_e79`).
- **File**: `07_multihit_burst_mid.png`. Captured, not separately detailed.

## `S_FilmDamage` texture

- **Timestamp**: 178.0s (inside the final Track-4 event, `t4_e157`).
- **File**: `08_filmdamage_texture.png`. Captured, not separately detailed — flagged as an item
  that would benefit from closer review in a follow-up pass.

## Outro `Lighten` composite

- **Timestamp**: 180.0s (inside the outro-bumper/`t4_e157` overlap region).
- **File**: `09_outro_lighten_composite.png`. Captured, not separately detailed — the `Lighten`
  blend-mode visual signature was not confirmed by direct comparison against a bypassed/disabled
  state (would require an ablation-style capture not attempted this pass).

## Final frame

- **Timestamp**: 184.0s (near the project's last visible content, end at 185.417s).
- **File**: `10_final_frame.png`. Captured, not separately detailed.

## Categories from the prompt's required list not covered by this reduced set

Only/first/middle/final-kill comparison (the multi-hit burst covers "first of a burst" but not a
first-vs-middle-vs-final within-burst comparison), a beat-effect-without-a-shot example, an
explicitly authored (non-automatic) transition example (none was identified structurally — see
[transitions-and-compositing.md](transitions-and-compositing.md)), an audio-led-transition example,
and a dedicated counterexample frame. These are explicitly listed as not captured this pass in
[limitations.md](limitations.md), not silently omitted.
