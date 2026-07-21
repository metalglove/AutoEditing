# Review workspace UX

This document describes the intended editor-facing workflow for reviewing song
structure and choosing reusable clips. It distinguishes the product direction
from behavior already available in the VEGAS extension.

## Design principles

- Start with meaningful groups, then reveal individual events or clips.
- Keep VEGAS as a focused preview, not the database or the primary browser.
- Preserve selection context across the visual overview, inspector, and VEGAS
  cursor.
- Make the common action work on the first click.
- Keep diagnostics available without permanently consuming editing space.

## Song map

The song map is region-first because a song can contain hundreds of detected
beats and transients. The normal flow is:

1. Scan the horizontal region navigator.
2. Select an intro, build-up, action, climax, breakdown, cinematic, outro, or
   unused region.
3. Review all sync points inside that region.
4. Assign independent anchor, visual, and timing purposes where useful.
5. Save the reviewed map and project only the active layer into VEGAS.

Implemented now:

- horizontal, color-coded region cards with time range, energy, and inclusion;
- selected-region filtering and layered views;
- bidirectional event-row/timeline selection and deletion;
- editorial assignment data for anchor, visual, and timing purposes;
- atomic persistence through the VEGAS interaction boundary.

Still to improve:

- replace the wide sync-point grid with a compact master/detail inspector;
- show friendly display labels instead of serialized enum names;
- add bulk include, reject, assign, lock, and clear actions;
- display saved, saving, dirty, and validation states explicitly;
- show validation errors inline and disable save while invalid;
- add a zoomable graphical event lane beneath the region overview;
- ensure only the chosen region/layer is projected into VEGAS by default.

## Visual clip drawer

The clip drawer should behave like a media browser, not a spreadsheet. The
primary surface should be a responsive card grid populated from one or more
selected clip directories. Each card should include:

- a representative thumbnail, ideally near the first reviewed kill;
- filename or concise semantic title;
- gun, map, player, play type, and kill-count badges;
- ready, changed, missing, or analysis-needed state;
- opener/closer designation;
- a directly clickable **Use** checkbox.

Selecting a card should open a detail inspector with reviewed kills, notes,
source metadata, and actions to preview, re-analyze, or return to event review.
Filters should include directory, readiness, gun, map, player, opener/closer,
and free-text search. Bulk actions should include select all ready, select none,
and invert selection.

Current status: the drawer lists reusable clips and defaults ready clips to
selected, with first-click checkbox behavior. Directory thumbnail browsing,
card layout, filters, bulk actions, and the detail inspector are not yet
implemented. Thumbnail extraction must run away from the VEGAS host thread and
use a bounded disk or memory cache; VEGAS object access must remain serialized
through the CQRS boundary.

## Wizard navigation

The numbered sidebar should be directly clickable. A completed step is always
navigable; a future step is enabled only when its prerequisites are satisfied.
Disabled steps should explain the missing prerequisite. Back and Next remain
available for a guided first run.

Current status: the sidebar communicates progress, while Back and Next remain
the reliable navigation path. Direct guarded step navigation is planned.

## Selection and editing

One selection should be reflected everywhere:

```text
region or clip card
    -> compact list row
    -> detail inspector
    -> VEGAS cursor / focused projection
```

Checkboxes and combo boxes must activate on the first click without requiring
the row to be selected first. Selection and editing are separate interactions.
This behavior exists for the clip drawer's **Use** checkbox and should be
applied consistently to every editable grid.

## Activity log

The activity log should be collapsed by default into a status strip showing the
latest message and warning/error counts. It expands on demand and automatically
on errors, but not for routine debug output. Clear and cancel remain available
inside the expanded panel.

Current status: progress, cancellation, and logging exist, but the log occupies
permanent workspace. Collapsing, severity badges, and error-triggered expansion
are planned.

## Effect-treatment inspector

When versioned treatment presets are implemented, a selected editorial event
should preview the resolved recipe and fallback before timeline generation, for
example:

```text
Kill anchor
Velocity: PostKillShort v1
Visual: ScreenPumpMedium v1
SFX: GunImpactFast v1
Fallback: Native Pan/Crop
```

This is a future consumer of editorial assignments, not currently applied UI.
