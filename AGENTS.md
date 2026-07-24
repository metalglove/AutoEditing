# Repository agent instructions

## Editing behavior is a synchronized contract

`AutoEditing/docs/editing-rules.md` is the normative, code-independent editing
rulebook. Any change to montage ordering, sync allocation, velocity, audio
treatment, effect selection, or effect rendering must update all three in the
same change:

1. production code;
2. deterministic tests;
3. `AutoEditing/docs/editing-rules.md`.

Do not describe a modeled or planned effect as implemented. Preserve the
document's rule IDs where possible, and add a new ID when introducing a new
behavior. If code and documentation disagree, treat this as a defect to resolve,
not permission to silently choose one.
