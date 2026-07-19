# VEGAS Pro 20 Scripting API for Automated Montage Effects

Research notes on the `ScriptPortal.Vegas` .NET scripting API (the namespace used by VEGAS Pro 14+, including
Pro 20 — older scripts used `Sony.Vegas`). Compiled from web search/fetch against the official MAGIX/Boris FX
scripting docs, the vegascreativesoftware.info forum (now migrated/redirected to forum.borisfx.com — most old
thread URLs 301-redirect to the forum homepage and their content could not be recovered), JetDV scripting
tutorials, and public GitHub script repositories.

Target: .NET Framework 4.8, code style matches the existing codebase (no `var`).

Every section lists its source(s). Anything not directly confirmed in a fetched page is marked **UNVERIFIED**.

---

## 1. Velocity envelopes on video events

**Confirmed via:** official VEGAS scripting API docs, vegas-magazine.com, multiple vegascreativesoftware.info
forum threads (via search snippets — original pages now redirect).

### API shape

- `Envelope` is constructed with an `EnvelopeType`. `EnvelopeType.Velocity` is the "video event velocity" type
  (as opposed to `Volume`/`Pan` for audio, `Composite`/`FadeToColor` for video track, `MotionBlurLength` /
  `VideoSupersampling` for the video bus track, `TransitionProgress` for transitions).
- `Envelope` is added to a `VideoEvent` via the `Envelopes` collection (inherited from `TrackEvent`).
- Points are `EnvelopePoint(Timecode x, Double y, CurveType curveType)` (or without curve type), added to
  `envelope.Points`.
- `Envelope.Min` / `Max` / `Neutral` expose the legal value range for the given envelope type; `ValueAt(Timecode)`
  reads the interpolated value at any position (including positions with no explicit point).

```csharp
// Add a velocity envelope and speed the clip up to 200% at 1s, back to 100% at 2s.
Envelope velocityEnvelope = new Envelope(EnvelopeType.Velocity);
videoEvent.Envelopes.Add(velocityEnvelope);

velocityEnvelope.Points.Add(new EnvelopePoint(Timecode.FromSeconds(0), 1.0, CurveType.Linear));
velocityEnvelope.Points.Add(new EnvelopePoint(Timecode.FromSeconds(1), 2.0, CurveType.Smooth));
velocityEnvelope.Points.Add(new EnvelopePoint(Timecode.FromSeconds(2), 1.0, CurveType.Linear));
```

### Value scale (confirmed)

The `Y` value is a **fraction of normal speed**, not a raw percent:

| Y value | Meaning |
|---|---|
| `1.0` | Neutral / 100% (normal forward speed) — this is the default/neutral value |
| `0.0` | Freeze frame (0%) |
| up to `10.0` | Maximum forward speed, 1000% (10x) |
| negative, down to `-1.0` | Reverse playback; `-1.0` is 100% speed in reverse (real-time backwards). Forum sources note reverse tops out around real-time, not a symmetric -10x. |

So `0..3` in the task prompt's assumption maps correctly to `0%..300%` as fractional `0.0..3.0`; negative values
do mean reverse.

### `CurveType` (a.k.a. `EnvelopePointCurveType` in some doc versions) between points

```
Invalid, Sharp (cubic sharp fade), Slow (logarithmic slow fade), None (hold/step),
Linear, Fast (logarithmic fast fade), Smooth (cubic smooth fade)
```

### THE GOTCHA — velocity does not change event length

This is confirmed by multiple independent sources (VEGAS help text quoted on Creative COW/vegas-magazine.com,
and forum discussion of the missing "compensate frames" feature):

> "Each video event in your project has a specific duration that is not changed by velocity envelopes. Changing
> the velocity or playback rate does not change the length of the event."

Concretely: the **event's timeline length (`VideoEvent.Length`) is fixed** by the script/user. The velocity
envelope only changes *which source frames are consumed to fill that fixed timeline duration* — i.e. the amount
of source media consumed is the **integral of the velocity envelope over the event's timeline duration**. If you
raise velocity without shortening the event, you will run out of source media before the event ends (VEGAS shows
a "notch" mark on the event where the source runs out, and holds the last frame or loops depending on settings);
if you lower velocity, the source is not fully consumed, and the tail is simply not played back.

**There is no automatic "compensate frames" scripting helper in the public API.** Scripts that want a velocity
ramp to consume an exact amount of source must calculate the desired event length themselves:

- For a **constant** velocity `v` (fraction, e.g. `2.0` = 2x), source consumed over event length `L` is
  `sourceConsumed = L * v`. To play back a known `sourceLength` of media at velocity `v`, set
  `videoEvent.Length = sourceLength / v` **before or after** applying the envelope (envelope points are
  time-relative to the event, so set event length first, then add points).
- For a **ramp** (the beat-synced case), the amount of source consumed is the *integral* of `v(t)` over
  `t = 0..L`. For a linear ramp from `v0` to `v1` across the whole event, `sourceConsumed = L * (v0 + v1) / 2`.
  Scripts typically pick the desired `sourceConsumed` (e.g. "next beat is 0.5s away, use however much source
  fits") and solve for `L` given the ramp shape, or iteratively trim/extend the event and re-check the "source
  exhausted" notch position (not directly exposed as a script property — UNVERIFIED whether there is a scripted
  way to read the notch position other than computing the integral yourself from `Take.Offset` / media length).
- Simpler/common workaround seen in forum discussion: build the velocity ramp, then manually resize the event
  (`videoEvent.Length = ...`) and snap it to where the source media ends — scripts replicate the manual
  "snap to notch" behavior by computing available source length (`take.Length` minus `take.Offset`) divided by
  the *average* envelope value over the ramp.

**Sources:**
- [VEGAS Pro Scripting API Summary](https://help.magix-hub.com/video/vegas/22/en/content/topics/external/vegasscriptapi.html) — `Envelope`, `EnvelopeType`, `EnvelopePoint`, `CurveType` class members.
- [Velocity Envelope – The different automation envelopes of VEGAS Pro](https://vegas-magazine.com/velocity-envelope/) — numeric range (-100%..1000%), "does not change the length of the event", audio unaffected.
- [Velocity Envelope duration (Creative COW)](https://creativecow.net/forums/thread/velocity-envelope-duration/) and [Velocity Envelope Conundrum (vegascreativesoftware.info, redirected)](https://www.vegascreativesoftware.info/us/forum/velocity-envelope-conundrum-o--69063/) — "compensate frames" is not automatic; must resize/snap manually.
- [Is it possible to force VEGAS to automatically recalculate clip length when using event velocity? (Creative COW)](https://creativecow.net/forums/thread/is-it-possible-to-force-vegas-automatically-recalc/) — confirms no built-in auto-recalculation.
- Forum snippets on neutral=1.0, freeze=0.0, reverse down to -1.0: threads "maximum velocity down script", "How to Set Event Velocity Envelopes w/Numbers?", "Curved Velocity Envelopes?" (all vegascreativesoftware.info, titles found via search; original content pages now redirect to https://forum.borisfx.com/ and could not be fetched directly).
- [Reading the Value of Any Point on an Envelope from a Script in Vegas – JETDV Scripts](https://www.jetdv.com/2022/08/29/reading-the-value-of-any-point-on-an-envelope-from-a-script-in-vegas/) — confirms `ValueAt()` pattern exists for reading envelope values at arbitrary frames (code itself is behind a video/not extractable as text).

---

## 2. Pan/Crop keyframing (`VideoMotion` / `VideoMotionKeyframe`)

**Confirmed via:** official scripting FAQ (tracks/events page), multiple forum thread search snippets.

### API shape

- `VideoEvent.VideoMotion.Keyframes` is a `VideoMotionKeyframes` collection of `VideoMotionKeyframe`.
- Each keyframe has:
  - `Position` (`Timecode`, relative to the **event start**, not the timeline)
  - `Bounds` — a `VideoMotionBounds` polygon representing the four corners of the pan/crop viewport
    (`TopLeft`, `TopRight`, `BottomRight`, `BottomLeft` vertices), plus `Center`
  - `Smoothness` (float) — interpolation smoothness at that keyframe
  - `Type` (`VideoKeyframeType`) — interpolation type (e.g. `Linear`, `Fast`, `Slow`, `Smooth`, `Hold`/`Sharp` —
    exact enum member list **UNVERIFIED**, only `Fast` confirmed by name in a track-motion example)
  - `Selected` (bool)
  - Convenience scale/position properties were referenced in search snippets as `PositionX`, `PositionY`,
    `Width`, `Height`, `RotationZ`, `RotationOffsetX/Y`, `OrientationZ` — these look like they may belong to the
    3D **TrackMotion** keyframe type rather than the 2D VideoMotion (pan/crop) keyframe type; the docs summary
    page listed them under `VideoMotionKeyframe` but the worked TrackMotion forum example (section 6) uses the
    same property names on a `TrackMotionKeyframe`. Treat the exact property set on `VideoMotionKeyframe` vs
    `TrackMotionKeyframe` as **UNVERIFIED** at the granular level — the reliable, confirmed way to manipulate a
    `VideoMotionKeyframe`'s rectangle is via its methods, not direct scale/position setters:
  - **Methods (confirmed, this is "the best way to manipulate the bounds rectangle"):** `MoveBy(...)`,
    `ScaleBy(...)`, `RotateBy(...)`, taking offsets (a `VideoMotionVertex` for `MoveBy` per one snippet).

```csharp
// Beat-synced punch-in zoom: start at 100%, punch in to 130% over 4 frames at t=1s.
VideoMotionKeyframe startKey = videoEvent.VideoMotion.Keyframes[0];
startKey.Type = VideoKeyframeType.Fast;

VideoMotionKeyframe punchKey = new VideoMotionKeyframe(Timecode.FromSeconds(1));
videoEvent.VideoMotion.Keyframes.Add(punchKey);
// Scale the bounds rectangle in toward its own center (punch-in).
punchKey.ScaleBy(0.7, 0.7);
punchKey.Smoothness = 0.0f;
```

```csharp
// Procedural camera shake: jitter position every 2 frames for the event duration.
Random rng = new Random();
Timecode frameLength = Timecode.FromFrames(2);
for (Timecode t = Timecode.FromFrames(0); t < videoEvent.Length; t += frameLength)
{
    VideoMotionKeyframe shakeKey = new VideoMotionKeyframe(t);
    videoEvent.VideoMotion.Keyframes.Add(shakeKey);
    double dx = (rng.NextDouble() - 0.5) * 20.0; // pixels
    double dy = (rng.NextDouble() - 0.5) * 20.0;
    shakeKey.MoveBy(new VideoMotionVertex(dx, dy));
    shakeKey.Smoothness = 0.0f; // sharp jitter, not eased
}
```
(The `VideoMotionVertex` constructor signature and exact `MoveBy` overload are **UNVERIFIED** — only referenced
in a search-result paraphrase of a GitHub "Camera Shake Script", whose actual page 301-redirected and could not
be fetched. A GitHub camera-shake script is reported to exist but its raw source was not retrievable in this
session — link seen only via search result title, not a fetchable URL.)

**Sources:**
- [VEGAS Pro Scripting API Summary](https://help.magix-hub.com/video/vegas/22/en/content/topics/external/vegasscriptapi.html) — `VideoMotionKeyframe` property list (`Position`, `Smoothness`, `Type`, `Selected`, and the scale/position property names noted above).
- Official scripting FAQ tracks/events page (`scriptfaq_tracksevents.htm`) — confirms `VideoEvent.VideoMotion.Keyframes` collection and that `MoveBy`/`ScaleBy`/`RotateBy` are "the best way to manipulate the bounds rectangle."
- Forum thread titles found (content not recoverable — 301 redirects to forum.borisfx.com homepage): "Camera Shake Script", "Script to ZOOM easily (for software tutorials production)", "How can i move keyframes via scripts.", "Keyframe offset strange results", "Copying Pan/Crop keyframes question", "Keyframe Script For Artificial Smooth Camera Movement in Vegas" — all at vegascreativesoftware.info, all UNVERIFIED beyond their titles.
- [Scripting 3D track motion (Creative COW)](https://creativecow.net/forums/thread/scripting-3d-track-motion/) — see section 6, useful cross-reference for the keyframe property names.

---

## 3. Adding video FX to events/tracks; keyframing OFX parameters

**Confirmed via:** official scripting API docs, search-result code fragments from vegascreativesoftware.info
threads, and one GitHub script (`PushBlur.cs`) fetched directly.

### Finding a plugin and adding it as an effect

```csharp
// By name:
PlugInNode plugin = Vegas.VideoFX.GetChildByName("Brightness and Contrast");
// By unique ID (more robust across locales/renames):
PlugInNode plugin2 = Vegas.VideoFX.GetChildByUniqueID("Some-Unique-ID-String");

Effect effect = videoEvent.Effects.AddEffect(plugin);   // confirmed real-world usage (GitHub PushBlur.cs)
// Equivalent form seen in another snippet:
// Effect effect = new Effect(plugin); videoEvent.Effects.Add(effect);

effect.Preset = "My Preset Name";   // presets settable by name
```

Verified real code (from GitHub, `KLEEEEEER/Vegas-push-blur-transition/PushBlur.cs`):

```csharp
PlugInNode linearBlur = VegasPans.GetPlugin(myVegas, Config.pluginName);
using (UndoBlock undo = new UndoBlock("Add plugin to first clip"))
{
    Effect effect = videoEvent.Effects.AddEffect(linearBlur);
    effect.Preset = Config.pluginPreset;
}
```

Track-level effects work the same way through `VideoTrack.Effects` / `AudioTrack.Effects` (both inherit
`Effects` from the `Track`/`BusTrack` base per the API summary). `Effects` also exists on `Media` (the source
media pool item) and the `Project` itself (four levels: Event, Track, Media, Project) per a JetDV tutorial title
— the tutorial's actual code could not be extracted (page returned only prose, code appears to be embedded as
an image or in a downloadable script file, not as page text).

### OFX parameter keyframing

Effects that are OFX plugins expose an `OFXEffect` with a parameter dictionary/collection, indexable by name and
castable to the specific typed parameter class:

```csharp
OFXEffect ofx = effect.OFXEffect;
OFXDoubleParameter amplitude = (OFXDoubleParameter)ofx["Amplitude"];
amplitude.IsAnimated = true;
amplitude.SetValueAtTime(Timecode.FromFrames(4), 0.00);
amplitude.SetValueAtTime(Timecode.FromFrames(8), 1.00);

// Adjusting interpolation of an existing keyframe:
foreach (OFXKeyframe kf in amplitude.Keyframes)
{
    kf.Interpolation = OFXInterpolation.Smooth; // exact enum name UNVERIFIED
}
```

Parameter type classes confirmed in the official API summary: `OFXBooleanParameter`, `OFXIntegerParameter`,
`OFXDoubleParameter`, `OFXInteger2DParameter`, `OFXInteger3DParameter`, `OFXDouble2DParameter`,
`OFXDouble3DParameter`, `OFXRGBParameter`, `OFXRGBAParameter`, `OFXStringParameter`, `OFXChoiceParameter`,
`OFXCustomParameter`, `OFXPushButtonParameter`, `OFXRangeParameter` — all sharing a base pattern of
`GetValueAtTime(Timecode)` / `SetValueAtTime(Timecode, value)` and a `Keyframes` collection.

**Important caveat found in the official FAQ (older revision):** one FAQ page explicitly stated *"specific
parameters of video effects are not accessible to scripts; scripts can only set preset values."* This appears to
be **outdated / superseded** — the OFX parameter classes above are documented in the current API summary and
JetDV's tutorial series ("Keyframing OFX Effect Parameters in Vegas Pro", 2021) is specifically about
per-parameter keyframing, and only applies to **OFX-based** effects (VEGAS's newer built-in FX and most modern
third-party plugins are OFX; some legacy/non-OFX effects genuinely only expose `Preset` string to scripts).
Treat non-OFX legacy effects as preset-only; treat OFX effects as fully keyframable per-parameter.

### Useful built-in VEGAS 20 FX for montages (confirmed to exist)

Confirmed present in VEGAS's Video FX shelf (current versions, name may vary slightly by locale):
**Brightness and Contrast**, **Color Curves**, **Glow**, **Lens Flare**, **TV Simulator**, and **LUT Filter**
("VEGAS LUT Filter" — supports importing `.cube` LUTs, present since VEGAS Pro 15 and confirmed still present/
documented in current (v22+) help pages, so it is present in Pro 20 as well).

**Sources:**
- [VEGAS Pro Scripting API Summary](https://help.magix-hub.com/video/vegas/22/en/content/topics/external/vegasscriptapi.html) — `Effects`/`AddEffect`, OFX parameter type list, `GetValueAtTime`/`SetValueAtTime`/`Keyframes`.
- [KLEEEEEER/Vegas-push-blur-transition — PushBlur.cs (GitHub)](https://github.com/KLEEEEEER/Vegas-push-blur-transition/blob/master/PushBlur.cs) — real, fetched source showing `Effects.AddEffect(plugin)` + `effect.Preset`.
- [Keyframing OFX Effect Parameters in Vegas Pro – JETDV Scripts](https://www.jetdv.com/2021/07/11/keyframing-ofx-effect-parameters-in-vegas-pro/) — tutorial exists and describes `IsAnimated`/`SetValueAtTime` pattern (confirmed via search snippet quoting `amplitude.IsAnimated = true; amplitude.SetValueAtTime(...)`; the live page itself only rendered prose, not the code block, when fetched).
- [Add Effects to Events, Tracks, Media, and the Project in Vegas Pro – JETDV Scripts](https://www.jetdv.com/2021/08/16/add-effects-to-events-tracks-media-and-the-project-in-vegas-pro/) — confirms the four levels effects can be added at (code not extractable from the fetched page).
- [LUT Filter (official help)](https://help.magix-hub.com/video/vegas/22/en/content/topics/8-design/fxplugins/lut%20filter.htm) and [Vegas Pro/Video Effects/LUT Filter (fan wiki)](https://logo-editing.fandom.com/wiki/Vegas_Pro/Video_Effects/LUT_Filter) — LUT Filter present since v15, still documented in v22 docs.
- Built-in FX name list (Color Curves, Glow, Lens Flare, TV Simulator) — [VEGAS Pro: Video Effects (fan wiki)](https://logo-editing.fandom.com/wiki/Vegas_Pro/Video_Effects), cross-referenced against search snippets of official docs.
- OFXKeyframe `Interpolation` property and `OFXInterpolation` enum name: **UNVERIFIED** — inferred from a search-snippet paraphrase ("you can modify interpolation by iterating through the keyframes collection and setting the `Interpolation` property"), exact enum member names not confirmed.

---

## 4. Media generators (Solid Color, Titles & Text)

**Confirmed via:** official scripting FAQ, forum search snippets.

### Creating a generator event

```csharp
PlugInNode generatorPlugin = Vegas.Generators.GetChildByName("Sony Titles & Text");
// (Older/renamed variants seen in different doc snippets: "Titles &Text", "VEGAS Titles & Text" —
//  exact current display name for VEGAS Pro 20 is UNVERIFIED; GetChildByUniqueID is the robust option.)

Media media = Media.CreateInstance(Vegas.Project, generatorPlugin);
// or with a starting preset:
// Media media = Media.CreateInstance(Vegas.Project, generatorPlugin, "My Preset");

VideoEvent titleEvent = new VideoEvent(startTimecode, lengthTimecode);
videoTrack.Events.Add(titleEvent);
titleEvent.Takes.Add(new Take(media.GetVideoStreamByIndex(0)));
```

### Solid Color generator (for white flash frames)

- Confirmed unique ID string (from a search-result paraphrase, not independently re-verified against the SDK
  header): `{Svfx:com.vegascreativesoftware:solidcolor}` — **treat this exact string as UNVERIFIED**, re-check
  against `Vegas.Generators` enumeration at runtime before relying on it; `GetChildByName("Solid Color")` (or
  the current localized name) is the safer approach.
- Preset indices reported in a forum snippet: preset `0` = white, `1` = black, `2` = red, `3` = green, etc. —
  **UNVERIFIED**, and fragile (index-based presets can shift between versions). Prefer setting the color via the
  OFX RGBA parameter directly once the effect is created, e.g.:

```csharp
OFXEffect ofx = media.Generator.OFXEffect; // exact property path UNVERIFIED
OFXRGBAParameter colorParam = (OFXRGBAParameter)ofx["Color"]; // param name UNVERIFIED, likely "Color" or "Colour 1"
colorParam.Value = new OFXRGBAValue(1.0, 1.0, 1.0, 1.0); // white, full alpha — type name UNVERIFIED
```
Given the low confidence on the exact parameter name/type for Solid Color, a script generating white-flash
frames should enumerate `ofx.Parameters` (or equivalent) at runtime and log names before hardcoding.

### Titles & Text — the "Text" parameter (confirmed pattern, RTF-based)

Confirmed pattern across two independent forum-thread search snippets:

```csharp
OFXEffect ofx = titleEvent.Effects[0].OFXEffect; // or media.Generator.OFXEffect
OFXStringParameter textParam = (OFXStringParameter)ofx.FindParameterByName("Text");

System.Windows.Forms.RichTextBox rtfBox = new System.Windows.Forms.RichTextBox();
rtfBox.Rtf = textParam.Value;      // read current value (it's RTF)
rtfBox.Text = "My Caption";        // set plain text
// ... apply font/size/color via rtfBox.SelectionFont, rtfBox.SelectionColor, etc. before pulling Rtf back out
textParam.Value = rtfBox.Rtf;      // write back — the parameter's Value is an RTF string
```

So: the "Text" parameter is confirmed to be an `OFXStringParameter` found via `FindParameterByName("Text")`
(or possibly indexer `ofx["Text"]`, both patterns appeared in different snippets), and its `Value` is a **plain
RTF string** — the documented workaround for setting font/size/color is to stage the formatting in a
`System.Windows.Forms.RichTextBox` (set `SelectionFont`/`SelectionColor` over a selection) and then copy
`rtfBox.Rtf` back into `textParam.Value`, rather than there being a separate structured Font/Size/Color OFX
parameter. One snippet (about detecting off-screen text) additionally mentioned the "Text" parameter's value can
also be represented/parsed as an XML/paragraph structure in some contexts — this alternate XML representation is
**UNVERIFIED** in detail (which API surface exposes XML vs RTF was not confirmed with source code).

**Sources:**
- Official scripting FAQ tracks/events page — `vegas.Generators.GetChildByName("Titles &Text")`, confirms text string of text generators is one of the OFX-accessible parameters (superseding an older FAQ note claiming generator text was not scriptable at all).
- Forum thread "How do you edit text within the script? C# Vegas 14" (vegascreativesoftware.info, content only recoverable via search snippet — direct fetch 301-redirects) — `FindParameterByName("Text")`, RichTextBox + RTF round-trip pattern.
- Forum thread "Detect when 'Sony Titles & Text' OFXEffect text is Off-Screen?" (same redirect situation) — corroborates `OFXStringParameter tparm = (OFXStringParameter)ofx.FindParameterByName("Text")`, `rtfText.Rtf = tparm.Value`.
- Forum thread "Adding Solid Black Color to Vegas Pro script help" (2026, vegascreativesoftware.info — also redirects on fetch) — title only, content not recoverable; UID/preset-index details above came from an earlier search-engine paraphrase of forum content, not a directly fetched page, hence flagged UNVERIFIED.
- [Vegas Media Generator - SOLID COLOR - Tutorials - Boris FX Forum](https://forum.borisfx.com/t/vegas-media-generator-solid-color/22885) and [Vegas Media Generator - COLOR GRADIENT](https://forum.borisfx.com/t/vegas-media-generator-color-gradient/22860) — confirmed to exist (found via search) as the current (post-migration) home for this content, but not fetched for code content in this session.

---

## 5. Transitions (crossfades)

**Confirmed via:** official scripting FAQ tracks/events page, Creative COW forum thread, search-result snippets.

### Overlap = crossfade

A crossfade in VEGAS is simply two events overlapping on the same track; no special "crossfade object" is
needed to get the default dissolve — just move/trim events so they overlap:

```csharp
// eventA ends where eventB starts; overlap by 15 frames to crossfade.
Timecode overlap = Timecode.FromFrames(15);
eventB.Start -= overlap;   // pull eventB left so it overlaps eventA's tail
```

When two video events overlap, VEGAS auto-creates a default cross-dissolve. `TrackEvent` exposes `FadeIn` and
`FadeOut` (confirmed: "Every TrackEvent object has FadeIn and FadeOut properties which give you objects that
control the event's ASR [Attack, Sustain, Release]"). `Fade.Curve` sets the fade curve
(`evnt.FadeIn.Curve = CurveType.Fast;`), and for the **leading** event's trailing edge in an overlap, the
**trailing** event's `ReciprocalCurve` property designates the curve applied to the leading event.

### Assigning a specific transition plugin (VEGAS Zoom, Flash, Cross Effect, etc.)

Confirmed pattern (consistent across three independent sources):

```csharp
PlugInNode transitionPlugin = Vegas.Transitions.GetChildByName("Cross Effect");
// other example names seen in snippets: "VEGAS Dissolve", "VEGAS Slide", "Dissolve"
// exact catalog names for "VEGAS Zoom" / "Flash" transitions in v20 are UNVERIFIED — enumerate
// Vegas.Transitions at runtime to get exact current names.

Effect transitionFx = new Effect(transitionPlugin);
videoEvent.FadeIn.Transition = transitionFx;     // apply to the incoming edge of the overlap
transitionFx.Preset = "Additive Dissolve";       // presets settable by name, same as regular FX
```

`Fade.RemoveTransition()` removes an assigned transition (confirmed member name from API summary, semantics
inferred).

Once assigned, the transition's OFX parameters can be keyframed the same way as any other OFX effect (section 3)
— `EnvelopeType.TransitionProgress` also exists as an envelope type specifically for driving transition
progress from an envelope instead of/alongside its own timing, though no worked script example was found
(**UNVERIFIED** exact usage).

**Sources:**
- Official scripting FAQ tracks/events page (`scriptfaq_tracksevents.htm`) — `FadeIn`/`FadeOut`, `Fade.Curve`, `ReciprocalCurve`, and the exact code:
  ```csharp
  PlugInNode plugIn = vegas.Transitions.GetChildByName(plugInName);
  Effect fadeInFx = new Effect(plugIn);
  videoEvent.FadeIn.Transition = fadeInFx;
  fadeInFx.Preset = "Additive Dissolve";
  ```
- [script for insert transition (Creative COW)](https://creativecow.net/forums/thread/script-for-insert-transition/) — corroborating pattern: `var fx = new Effect(plugIn); ev.FadeIn.Transition = fx;`, transitions enumerated via `Vegas.Transitions`, applied conditionally when `ev.MediaType == MediaType.Video`.
- [VEGAS Pro Scripting API Summary](https://help.magix-hub.com/video/vegas/22/en/content/topics/external/vegasscriptapi.html) — `Fade.Transition` property, `Fade.RemoveTransition()`, `EnvelopeType.TransitionProgress`, `EnvelopePoint.MaxTransitionProgress`.
- [Finding All Transitions in the Project in Vegas Pro – JETDV Scripts](https://www.jetdv.com/2024/09/16/finding-all-transitions-in-the-project-in-vegas-pro/) — tutorial exists (title only confirmed; page content not extractable).
- Specific transition display names "VEGAS Zoom" / "Flash" for v20: **UNVERIFIED** — not found in any fetched source; only generic names like "Cross Effect", "Dissolve", "VEGAS Dissolve", "VEGAS Slide" appeared in snippets.

---

## 6. Track-level: track FX, track motion, motion blur, supersampling

**Confirmed via:** Creative COW forum thread (fetched directly), official API summary.

### Track FX

Same `Effects` collection pattern as events — `VideoTrack.Effects.AddEffect(plugin)` (both `VideoTrack` and
`AudioTrack` inherit `Effects` from the `Track`/`BusTrack` base, per the API summary).

### Track motion (3D)

Confirmed real code (fetched from a Creative COW thread on "Scripting 3D track motion"):

```csharp
VideoTrack myTrack = (VideoTrack)track;
myTrack.CompositeMode = CompositeMode.SrcAlpha3D;

BaseTrackMotionKeyframe endKeyframe = myTrack.TrackMotion.InsertMotionKeyframe(new Timecode(1000));
endKeyframe.OrientationZ = newRotation;
endKeyframe.OrientationY = 90;
endKeyframe.PositionX = posX;
endKeyframe.PositionY = posY;
endKeyframe.Width = picWidth;
endKeyframe.Height = newHeight;
endKeyframe.VideoKeyframeType = VideoKeyframeType.Fast;

BaseTrackMotionKeyframe startKeyframe = myTrack.TrackMotion.MotionKeyframes[0];
```

This confirms `TrackMotion.InsertMotionKeyframe(Timecode)`, the `MotionKeyframes` indexed collection, and that
track motion keyframes are distinct from event-level `VideoMotionKeyframe` (pan/crop) even though they share
some property names (`PositionX/Y`, `Width`, `Height`).

### Motion blur length / video supersampling (video bus track envelopes)

Confirmed to exist as envelope types (`EnvelopeType.MotionBlurLength`, `EnvelopeType.VideoSupersampling`) in the
official API summary, and confirmed as UI features accessible via right-click → Insert/Remove Envelope on the
video bus track. **Scriptability specifics are UNVERIFIED**: no source found showing how a script obtains a
reference to "the video bus track" object to add these envelopes to (it's not an ordinary entry in
`Project.Tracks` in the same way a normal video/audio track is, or at least no confirmed accessor was found).
Given the envelope type exists, the mechanism is presumably
`Envelope busEnvelope = new Envelope(EnvelopeType.MotionBlurLength); someBusTrackObject.Envelopes.Add(busEnvelope);`
but the identity/accessor of `someBusTrackObject` was not confirmed — flag as **UNVERIFIED** and validate with
`Vegas.Project.Video` / a dedicated bus-track property at implementation time.

**Sources:**
- [Scripting 3D track motion (Creative COW)](https://creativecow.net/forums/thread/scripting-3d-track-motion/) — directly fetched, code above is verbatim from that thread.
- [VEGAS Pro Scripting API Summary](https://help.magix-hub.com/video/vegas/22/en/content/topics/external/vegasscriptapi.html) — `EnvelopeType.MotionBlurLength`, `EnvelopeType.VideoSupersampling` listed as enum members; `TrackMotion`/`TrackMotionKeyframeList`/`TrackMotionScaleFactors` class names.
- [Using the Video Bus Track (HelpMax)](http://vegaspro.helpmax.net/en/using-vegas-software/using-markers-regions-and-commands/using-the-video-bus-track/) — confirms Motion Blur Amount / Video Supersampling are inserted via right-click on the video bus track in the UI; does not cover scripting.
- Bus track script accessor: **UNVERIFIED**, no source found.

---

## 7. Render automation (`Renderer` / `RenderTemplate`)

**Confirmed via:** GitHub script fetched directly in full (`shiregator/VegasProScripts/Event Batch Render.cs`).
This is real, working, verified code (dated 2006 but uses the same `Renderer`/`RenderTemplate`/`RenderArgs` API
that is still current — confirmed present in scripts using `ScriptPortal.Vegas`).

```csharp
using ScriptPortal.Vegas;

// Enumerate renderers and their templates to find the one you want (e.g. "MAGIX AVC/AAC (MP4)"):
Renderer targetRenderer = null;
RenderTemplate targetTemplate = null;
foreach (Renderer renderer in Vegas.Renderers)
{
    foreach (RenderTemplate template in renderer.Templates)
    {
        if (template.IsValid() && template.Name == "Internet 1080p, 25 fps")
        {
            targetRenderer = renderer;
            targetTemplate = template;
        }
    }
}

// Queue and run a render of the whole project:
RenderArgs args = new RenderArgs();
args.OutputFile = @"C:\Output\montage.mp4";
args.RenderTemplate = targetTemplate;
// For a selection/region instead of the whole project:
// args.Start = someRegion.Position;
// args.Length = someRegion.Length;
// args.UseSelection = true; // renders the current timeline selection instead

RenderStatus status = Vegas.Render(args);
if (status == RenderStatus.Failed)
{
    throw new ApplicationException("Render failed: " + args.OutputFile);
}
```

Confirmed members from the fetched source: `Renderer` (with `.FileTypeName`, `.Templates`, `.ClassID`, and
well-known `CLSID_*` static GUIDs for identifying specific renderers e.g. `Renderer.CLSID_SfWaveRenderClass`),
`RenderTemplate` (`.Name`, `.IsValid()`, `.VideoStreamCount`, `.AudioStreamCount`, `.AudioChannelCount`,
`.FileExtensions`, `.TemplateID`, `.Index`), `RenderArgs` (`.OutputFile`, `.RenderTemplate`, `.Start`, `.Length`,
`.UseSelection`), `myVegas.Render(args)` returning `RenderStatus` (`Complete`, `Canceled`, `Failed`).

To specifically target MP4 output, filter `Vegas.Renderers` for the one whose `FileTypeName`/extension is
`.mp4` (VEGAS 20's default MP4 renderer is MAGIX AVC/AAC — exact `FileTypeName` string to match should be
confirmed at runtime by enumeration rather than hardcoded, since it can vary by version/locale).

**Sources:**
- [shiregator/VegasProScripts — Event Batch Render.cs (GitHub, raw source fetched in full)](https://github.com/shiregator/VegasProScripts/blob/master/Event%20Batch%20Render.cs) — primary, high-confidence source; code excerpted above is adapted from this real script.
- [VEGAS Pro Scripting API Summary](https://help.magix-hub.com/video/vegas/22/en/content/topics/external/vegasscriptapi.html) — confirms `Renderer`/`Renderers`/`RenderTemplate`/`RenderTemplates`/`RenderArgs`/`RenderStatus`/`RenderStatusEventArgs`/`RenderModeEventArgs` class names exist in the current API.
- [Updated!! BATCH RENDER Script Is Here! (vegascreativesoftware.info)](https://www.vegascreativesoftware.info/us/forum/updated-batch-render-script-is-here-download-it-test-it-share-it--142682/) — confirms this exact script pattern is the community-standard batch-render approach, still current (thread references VEGAS 21).

---

## 8. Free plugins usable with VEGAS Pro 20 (OFX)

| Plugin | Status (as researched, mid-2026) | Notes |
|---|---|---|
| **Boris FX Continuum units bundled with VEGAS Pro 20 purchase** | **Confirmed, currently offered.** | VEGAS 20 Pro/Post purchase entitles the owner to a free license for **Continuum Primatte Studio** (a keying unit). A separately-named "VEGAS Movie Expansion Pack" bundle additionally includes free Continuum **Image Restoration**, **Particles**, and **Film Style** units (~50+ plugins). Since Boris FX now owns VEGAS outright, check `support.borisfx.com` / in-app "Get More" for the current free-with-purchase entitlement, as bundling has reportedly changed over time. |
| **HitFilm Ignite Express** (FXhome) | **Effectively dead — do not rely on it.** | Was a free ~90-plugin OFX pack. `fxhome.com` no longer resolves (DNS failure when fetched directly in this session). Multiple forum threads report Ignite Express (released 2017) does **not** work in VEGAS Pro 17+ / 22 — only the paid "Ignite Pro" line kept receiving compatibility updates (v4.1+) for newer VEGAS versions. FXhome was acquired by Artlist in 2021; no evidence found of Ignite Express being kept current for VEGAS Pro 20. |
| **RGBurst** (RGB split / chromatic aberration OFX, alestemple.net) | **Paid, not free.** $39.95. Compatible with VEGAS Pro 15+. | Confirmed via the vendor's own store page. |
| **DataStorm** (10-module glitch OFX pack, alestemple.net) | **Paid, not free.** $49.95. | Confirmed via the vendor's own store page; compatible with VEGAS Pro 15+, GPU-accelerated (OpenCL). |
| Any dedicated free RGB-split/glitch/shake OFX pack | **UNVERIFIED / not found.** | No currently-live, genuinely free (not free-trial/watermarked) third-party OFX pack for glitch/RGB-split/shake effects was located. VEGAS's own built-in FX (Glow, Lens Flare, TV Simulator, Color Curves — see section 3) plus procedural `VideoMotion` keyframe shake (section 2) are the reliable free/no-extra-download options. |
| **VEGAS's own built-in OFX FX** (Brightness/Contrast, Color Curves, LUT Filter, Glow, Lens Flare, TV Simulator, etc.) | **Confirmed free/bundled** — ships with VEGAS Pro itself, no download needed. | See section 3 sources. |

**Bottom line for the montage tool:** the only genuinely free, currently-working effect sources confirmed for
VEGAS Pro 20 in 2026 are (a) VEGAS's own built-in OFX video FX, and (b) whatever Continuum units currently ship
free with a VEGAS 20 license — verify the current entitlement via `support.borisfx.com` since bundling has
changed hands (FXhome → Artlist; Boris FX acquired VEGAS itself) and could change again. Do not build a
dependency on Ignite Express, RGBurst, or DataStorm without the user explicitly purchasing/installing them.

**Sources:**
- [Does Ignite from FX home still work in VEGAS PRO 20? (vegascreativesoftware.info, found via search, direct fetch redirects)](https://www.vegascreativesoftware.info/us/forum/does-ignite-from-fx-home-still-work-in-vegas-pro-20--136893/)
- `fxhome.com` — direct fetch failed with DNS resolution error (`getaddrinfo ENOTFOUND fxhome.com`) at time of research, corroborating reports that FXhome's site is down/gone.
- [I love the Boris FX Continuum Units plugins for VEGAS Pro 19/20! Can I upgrade to the full suite? – Boris FX support](https://support.borisfx.com/hc/en-us/articles/10032902600589-I-love-the-Boris-FX-Continuum-Units-plugins-for-VEGAS-Pro-19-20-Can-I-upgrade-to-the-full-suite) — confirms free Continuum Primatte Studio license with VEGAS 20 purchase, and the three-unit Movie Expansion Pack bundle (Image Restoration, Particles, Film Style).
- [vfx.borisfx.com/vegas20units](https://vfx.borisfx.com/vegas20units) — corroborates the same bundle details.
- [alestemple.net/store/ofx-plugins.html](https://www.alestemple.net/store/ofx-plugins.html) — directly fetched; confirms RGBurst ($39.95) and DataStorm ($49.95) are paid, not free, with stated VEGAS Pro 15+ compatibility.
- [RGBurst — Boris FX Forum thread](https://forum.borisfx.com/t/rgburst-rgb-channel-split-chromatic-aberration-ofx-plugin-for-vegas-pro/23820), [New OFX Plugin: DataStorm — Boris FX Forum thread](https://forum.borisfx.com/t/new-ofx-plugin-datastorm-10-glitch-effects-for-vegas-pro/22283)
- `support.borisfx.com/hc/.../How-do-I-get-my-free-Boris-FX-plugins` — returned HTTP 403 when fetched (likely bot-blocked); **not independently confirmed**, listed here for reference only, treat its specific content as UNVERIFIED.

---

## Summary of biggest UNVERIFIED gaps

1. Exact `OFXKeyframe.Interpolation` enum name/members for OFX parameter keyframes.
2. Exact `VideoMotionVertex` constructor signature and full `MoveBy`/`ScaleBy`/`RotateBy` overload list for
   procedural camera shake.
2b. Which properties (`PositionX/Y`, `RotationZ`, etc.) actually live on `VideoMotionKeyframe` vs
   `TrackMotionKeyframe` — evidence suggests these confirmed-named properties belong to `TrackMotionKeyframe`
   (3D track motion, section 6), while 2D pan/crop `VideoMotionKeyframe` is manipulated via `Bounds` +
   `MoveBy`/`ScaleBy`/`RotateBy` instead.
3. Solid Color generator's exact unique-ID string and OFX color-parameter name/type.
4. Whether the Titles & Text "Text" parameter is purely RTF, or also has an alternate structured XML
   representation, and if so which API exposes it.
5. Exact display names for "VEGAS Zoom" / "Flash" transitions as cataloged in `Vegas.Transitions` for v20.
6. How to obtain a script reference to the video bus track in order to add `MotionBlurLength` /
   `VideoSupersampling` envelopes to it.
7. Whether Boris FX's current free-with-VEGAS-20-purchase Continuum entitlement is still active/unchanged as of
   mid-2026 (the support article describing "how do I get my free Boris FX plugins" could not be fetched, HTTP
   403).

For all seven, the recommended approach before shipping automation that depends on them is to write a small
throwaway VEGAS script that enumerates the relevant collection (`Vegas.VideoFX`, `Vegas.Generators`,
`Vegas.Transitions`, an effect's `OFXEffect.Parameters`, etc.) and prints names/IDs to a log file, exactly as the
JetDV "Reading Available Effects, Transitions, Generated Media, and Renderers" tutorial's `ReadGUID.cs` script
does — rather than trusting hardcoded names/UIDs pulled from forum snippets.
