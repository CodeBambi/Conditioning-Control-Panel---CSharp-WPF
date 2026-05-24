# Mission 3 — Deeper Player redesign + theming cleanup

Status: code complete, builds clean (0 errors, 124 pre-existing warnings),
runtime smoke check still owed (user-driven; the dev build is locked by
the running app while these edits land).

---

## Commits

1. `Mission 3: add theme resources for player + gaze picker chrome`
   — 8 new color/brush pairs in Colors.xaml + Brushes.xaml.

2. `Mission 3: player redesign (header pill, file strip, mini-timeline,
   structured event log)` — XAML rewrite + Mission3.cs partial + edits
   to existing player code-behind + DeeperEditorWindow.LoadedFilePath
   + MainWindow.OpenDeeperEditorFromPlayer + en.json keys.

3. `Mission 3: GazePicker theming + editor hex promotion + violet
   pointer comments + delete dead loc keys` — bundled cleanup
   (single commit because each piece is small and they touch a
   coherent surface: theming + dead-code).

4. `Mission 3: translation-debt log entry + mission report` — docs.

---

## Smoke check

Owed (couldn't run in this session — file locks from the running app).
Manual check plan:

| Step | Expected |
| --- | --- |
| Open empty player from hub | Header shows "Empty" pill, no Source pill, no Open-in-editor, no mini-timeline. |
| Load any enhancement | File context strip populates with media icon, name, creator·source·counts. Source pill appears. Open-in-editor button appears. Mini-timeline appears with region bands + rule pins. |
| Press play | Status pill flips to "Live" with solid accent fill. Mini-timeline playhead moves. |
| Playhead enters a region | "Now: [label]" overlay appears top-right of media pane, color matches region. |
| Event log filter pills | Clicking Actions/Engine/Errors filters the list; counts update live. Single-select (can't deselect; can re-click to toggle off without re-checking another). |
| Clear button | Empties the list, counts go to zero. |
| Collapse chevron | Hides the scroll viewer (header + filter strip stay visible). Re-click expands. |
| Open in editor (header) | Routes through MainWindow.OpenDeeperFile. If an editor is already open for the same .ccpenh.json, that one gets focus (no duplicate window). |
| Change → popover | Opens with: pick file / load URL / create new (only when no enh loaded for the current media) / unload (only when an enh is loaded). |
| Resize down to 504×640 | Header / file strip / mini-timeline / transport all stay legible without clipping. |
| Editor → BtnPreview | Constructor signature unchanged; player opens with the editor-preview tag. |
| Interactive tutorial follow-up card → Open Deeper Player | `OpenDeeperPlayerWithLastSavedEnhancement` still launches via the no-arg constructor + LoadEnhancementFile. Confirm it lands at the new redesigned player with the just-saved enhancement loaded. |
| GazePicker (editor rule editor → pick on video) | Renders with new theme brushes (backdrop, toolbar, handle fill, handle stroke). Drag/resize behaves unchanged. Done/Cancel return the same way. |

---

## Decisions and deviations from spec

### Event ingestion path: (a) structured at intake

`OnHostActionLogged` (ActionLogged → Action category) and
`OnHostDiagnostic` (Diagnostic → Engine, or Error if the line mentions
fail/error/rejected) are now two separate handlers. The engine surface
itself is untouched — both events still emit plain strings; we just
classify by source instead of pattern-matching one shared handler's
strings.

### Rule label color tinting: deferred

The spec called for the rule label inside an Action row to be tinted by
trigger family (amber TimeReached / violet haptic+region / teal gaze+
blink+attention). The engine's `ActionLogged` format
(`"t=12.34s  effect flash for 2000ms"` from
`RecordingActionDispatcher`) doesn't carry a rule id or trigger type
back, so we can't map a firing → its rule without changing the engine.
For v1 we ship category-only coloring (icon tint by Action/Engine/Error).
The DataTemplate has the `RuleLabel` / `RuleLabelBrush` properties wired
already, so the moment the engine starts emitting structured action
records the tinting works without further XAML changes.

### "Open in editor" jump-to-rule from event log: deferred

Same root cause. The link is wired (button visible only when an entry
has a non-null RuleId; Tag carries the id), but no row currently
populates `RuleId`. Documented here so the next mission that touches the
engine knows the consumer is ready.

### Mini-timeline seek

Audio-mode click-to-seek works (routes through `_player.Seek`). Video
mode is read-only — seeking through the WebView2 JS bridge right after
navigation is race-prone, and the existing player has no Seek call
against `_videoSource` either. Ship read-only for v1.

### TxtAudioPath + BtnPickAudio kept (preserved x:Names)

The "audio path also folds into the Change strip popover" line in the
spec — implemented by putting AudioFileRow (same x:Name) inside the
Change popover. `ShowMediaPaneFor` still toggles its visibility, the
audio path picker still works from inside the popover.

### Status row dropped, TxtStatus preserved

The separate Row 5 status border (TxtStatus + TxtEnhMetadata) is gone.
TxtStatus moved into a thin header band at the top of the event log
border so engine state messages (load-failed, webcam prompts) still have
a visible home. TxtEnhMetadata moved into the file context strip's
inline meta line — we override its text in `RefreshFileContextStrip`
with the formatted creator·source·counts, so any code path still
writing into `TxtEnhMetadata.Text` (e.g. external diagnostics) keeps
working.

### Old `_events` ObservableCollection removed

Replaced by the typed `_logEntries` + `ICollectionView` filter wrapper
in `Mission3.cs`. `AppendEvent(string)` is gone — call sites (only the
two `On*` handlers) now call `IngestActionLine` / `IngestDiagnosticLine` /
`IngestErrorLine`. Cap stays at 30 entries.

### Fullscreen button: skipped

Spec marked it optional. The existing dblclick-on-video fullscreen path
in `EnsureVideoBrowserReadyAsync` already handles enter+exit through
`ContainsFullScreenElementChanged`. Adding a chrome button would
duplicate that flow without adding new capability — left alone.

### Editor + player hardcoded hex promotion

Done for the editor's preview pane bg (#0A0A14), waveform/curve canvas
bg (#10000020), menu-item hover (#3D3D60), zoom-overlay backdrop
(#A0000000). The timeline canvas bg (#15151F) already routed through
`DeeperLaneCanvasBrush` after Mission 1; no second pass needed there.

### Violet sources

- `Enhancement.cs` `Region.Color` default: literal `#7B5CFF` retained
  with a comment pointer to `Colors.xaml` (serialized field, can't be
  a DynamicResource).
- `DeeperEditorWindow.Unified.cs` `EffectColors[Haptic]`: same
  treatment — comment pointer added.
- `DeeperEditorWindow.xaml.cs:1965` haptic visual stroke: PROMOTED to
  a runtime `FindResource("DeeperAccentBrush")` lookup, with a fallback
  to the canonical hex if the resource isn't loaded.
- `GazePickerWindow.xaml` rect stroke + handle stroke: PROMOTED to
  `DynamicResource DeeperAccentBrush`. The 8 raw hex values that were
  in the file (stroke, fill, handle fill, handle stroke, toolbar bg,
  backdrop, hint color, cancel border) all became DynamicResource
  lookups across either existing theme brushes (DeeperAccent /
  DeeperAccentTransparent40 / DeeperAccentSoft) or the 4 new
  GazePicker* brushes shipped in commit 1.

### Dead localization keys

`deeper_tutorial_coming_soon_local_video` and
`deeper_tutorial_coming_soon_local_audio` deleted from en.json + the 8
non-en locale files (mechanical sed, JSON structure preserved).

---

## Open questions

- **Rule-id propagation from engine.** Adding a single `string ruleId`
  field on `ActionLogged`'s payload (today a `Action<string>`, would
  need to become `Action<string, string?>` or a dedicated record type)
  would unlock both the rule-label tinting and the "Open in editor"
  jump-to-rule link without further player work. Out of scope for this
  mission.

- **Mini-timeline density.** With 50+ rules + 20 regions on a 1-hour
  enhancement, the 32px Canvas gets visually dense. We don't draw
  haptic events on the mini-timeline today (the editor does). If
  feedback says the mini-timeline is too noisy or too quiet, the
  filter knob is which `Enhancement` lists to walk in `RebuildMiniTimeline`.
  Currently: regions + TimeReached rule pins; haptic events skipped to
  stay below the visual budget.

- **Status pill flash hint.** Spec says "green when playing." We use
  the existing `DeeperAccentBrush` (violet) instead of inventing a
  green — keeps the palette coherent and lets the same pill double as
  the "Loaded but paused" indicator with just a soft variant. If user
  testing wants a distinctly green "live now" cue, add a `DeeperLive`
  color token next to `DeeperAccent` in Colors.xaml.
