# Forensic synthesis and implementation priorities — moved

This synthesis's evidence (tiered evidence ranking, OFX recipe recovery, contradictions resolved,
and candidates for AutoEditing) was entirely about one reference project and has been reorganized
as project-specific evidence into the Editor 1 / Project 01 package:

- [`AutoEditing/docs/forensics/projects/editor-1/project-01/README.md`](projects/editor-1/project-01/README.md)
  (entry point and strongest-findings summary)
- [`AutoEditing/docs/forensics/projects/editor-1/project-01/effects-and-presets.md`](projects/editor-1/project-01/effects-and-presets.md)
- [`AutoEditing/docs/forensics/projects/editor-1/project-01/audio-treatment.md`](projects/editor-1/project-01/audio-treatment.md)
- [`AutoEditing/docs/forensics/projects/editor-1/project-01/evidence-register.md`](projects/editor-1/project-01/evidence-register.md)
  (the "Highest-confidence candidates for AutoEditing" section is now the "Proposed promotions"
  table there, with required evidence IDs and validation steps)

Most of this document's "Questions for Claude's continuing inspection" (OFX time-domain
resolution, screen-pump ablation, marker-vs-kill correlation, missing-plugin inventory) were
subsequently investigated in an adversarial-verification pass; the outcomes (including two
falsified prior conclusions) are recorded in
[`evidence-register.md`](projects/editor-1/project-01/evidence-register.md) and
[`limitations.md`](projects/editor-1/project-01/limitations.md), which also lists which questions
remain genuinely open (notably: the OFX keyframe time-basis fixture test was never built).

The general methodology guidance this document referenced — the distinction between mechanical
recoverability, timeline-time interpretation, and visual/semantic confidence — remains at
[`media-less-vegas-project-value.md`](media-less-vegas-project-value.md), which is not
project-specific and is kept in place.

This report was entirely specific to one reference project (Editor 1 / Project 01) and has not
been re-verified against any other project. It is kept here only as a redirect so existing links
do not break.
