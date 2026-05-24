# Deeper Tutorials Recon

Map of every tutorial that targets a Deeper surface. Use the Element Reference Index (section 4) before renaming any control: any element listed there has at least one tutorial step that will silently miss its target the moment the name changes (see section 6 for failure-mode specifics).

All paths are relative to `ConditioningControlPanel/` unless noted absolute.

---

## 1. File inventory

### Definitions (step lists)
- `Services/TutorialService.cs`
  - Enum `TutorialType` (lines 12-33) — every Deeper-touching tutorial has an enum value here.
  - `GetStepsForTutorial(...)` dispatcher: lines 99-124.
  - Deeper-touching step factories:
    - `CreateDeeperSteps()` — lines 1395-1467 (Hub tour, 7 steps).
    - `CreateDeeperEditorSteps()` — lines 1473-1548 (Editor coachmarks, 8 steps).
    - `CreateDeeperEditorInteractiveHTSteps()` — lines 1558-1573 (HT Part 1, 1 step).
    - `CreateDeeperEditorInteractiveHTPart2Steps()` — lines 1579-1796 (HT Part 2, 16 steps).
    - `CreateDeeperEditorInteractiveLocalAudioSteps()` — lines 1805-1840 (Audio Part 1, 2 steps).
    - `CreateDeeperEditorInteractiveLocalAudioPart2Steps()` — lines 1847-2001 (Audio Part 2, 15 steps).
    - `CreateDeeperEditorInteractiveLocalVideoSteps()` — lines 2007-2037 (Video Part 1, 2 steps).
    - `CreateDeeperEditorInteractiveLocalVideoPart2Steps()` — lines 2044-2211 (Video Part 2, 16 steps).
    - Shared follow-up card builder `BuildInteractiveDoneCard(...)` — lines 2217-2258.
    - Post-completion player launcher `OpenDeeperPlayerWithLastSavedEnhancement()` — lines 2267-2296.
- Narrative strings (verbatim text user sees) — `Localization/Languages/en.json`:
  - Hub tab: `deeper_tut_tab_*` keys at lines 384-397.
  - Editor coachmarks: `deeper_tut_ed_*` keys at lines 399-414.
  - HT walkthrough: `deeper_itut_ht_*` keys at lines 2893-2927.
  - Audio walkthrough: `deeper_itut_audio_*` keys at lines 2928-2957.
  - Video walkthrough: `deeper_itut_video_*` keys at lines 2958-2989.
  - Same keys mirrored in `de.json / es.json / fr.json / ja.json / ko.json / pt-BR.json / ru.json / zh-CN.json`.

### Infrastructure
- `Models/TutorialStep.cs` (whole file, 1-68) — step DTO, enums `TutorialStepPosition` and `TutorialAdvanceTrigger`.
- `Services/TutorialEventBus.cs` (whole file, 1-24) — static event bus + `PendingPart2Tutorial` handoff + `LastSavedEnhancementPath`.
- `Services/TutorialService.cs:35-226` — orchestration: `Start / Next / Previous / Skip / Complete`, `ConfigureCallbacks`, `ApplyCallbacksToSteps`.
- `TutorialOverlay.xaml` (whole file, 1-335) — visual scrim, step text panel, navigation buttons, follow-up card area.
- `TutorialOverlay.xaml.cs` (whole file, 1-1248) — render loop, spotlight region, advance subscriptions, Win32 region click-through.
- `App.xaml.cs:265` — `public static TutorialService Tutorial`.
- `App.xaml.cs:978` — `Tutorial = new TutorialService();` (constructed during splash 0.85 stage).

### Triggers (entry points)
- Hub: `MainWindow.xaml:8530-8538` — `BtnDeeperTutorial`.
- Hub: `MainWindow.xaml:8609-8614` — `BtnDeeperWelcomeTour` (inside `DeeperWelcomeCard`).
- Hub: `MainWindow.xaml.cs:2306-2310` — `BtnDeeperWelcomeTour_Click`.
- Hub: `MainWindow.xaml.cs:2323-2326` — `BtnDeeperTutorial_Click`.
- Hub: `MainWindow.xaml.cs:2356-2361` — `StartDeeperTabTutorial()` (calls `ShowTab("deeper")` then `StartTutorial(TutorialType.Deeper)`).
- Editor: `Views/Deeper/DeeperEditorWindow.xaml:239-243` — `BtnEditorHelp`.
- Editor: `Views/Deeper/DeeperEditorWindow.xaml.cs:287-308` — `_editorTutorialOverlay`, `BtnEditorHelp_Click`, `StartEditorTutorial()`.
- Editor first-run auto-launch: `DeeperEditorWindow.xaml.cs:192-285` (`DeeperEditorWindow_Loaded`), gated by `!settings.HasSeenDeeperEditorIntro` at line 271.
- Editor Part-2 dispatch: `DeeperEditorWindow.xaml.cs:200-259` — picks up `TutorialEventBus.PendingPart2Tutorial`, ~800ms timer, then creates a fresh overlay.
- NewEnhancementDialog: `Views/Deeper/NewEnhancementDialog.xaml:81-107` — `BtnLocalVideoTutorial`, `BtnLocalAudioTutorial`, `BtnTryHypnoTubeTutorial`.
- NewEnhancementDialog: `NewEnhancementDialog.xaml.cs:106-185` — three `*Tutorial_Click` handlers and the shared `StartInteractiveTutorial(part1, part2)` launcher.
- NewEnhancementDialog: `NewEnhancementDialog.xaml.cs:210-213` — `BtnCreate_Click` queues `TutorialEventBus.PendingPart2Tutorial = _pendingPart2Tutorial.Value` only after source validation passes.
- Hub help-menu route into the Deeper tab is the broader `MainTutorialOverlay` in `MainWindow.xaml:11399-11680`; that menu has no Deeper button (it lists Settings/Presets/Progression/Achievements/Companion/Patreon/Awareness/Avatar/Modding only). FullTour also does not visit any Deeper element — see section 8.
- Bus emit sites that drive Deeper interactive tutorials:
  - `DeeperEditorWindow.Unified.cs:148` — `EffectAdded` (haptic AddHapticEventAt path).
  - `DeeperEditorWindow.Unified.cs:182` — `EffectAdded` (non-haptic effect path).
  - `DeeperEditorWindow.Unified.cs:293` — `RuleAdded` (AddRuleAt completion).
  - `DeeperEditorWindow.xaml.cs:198` — `WindowLoaded:DeeperEditorWindow` (legacy retarget signal; Part-2 now uses `PendingPart2Tutorial` instead).
  - `DeeperEditorWindow.xaml.cs:3875-3876` — `LastSavedEnhancementPath = path; Emit("FileSaved")` inside the save-to-disk success branch.

### State / persistence
- `Models/AppSettings.cs:3578` — `HasSeenDeeperTab` (drives the BtnDeeper pulse, not a tutorial flag per se).
- `Models/AppSettings.cs:3584` — `HasSeenDeeperWelcome` (controls `DeeperWelcomeCard` visibility).
- `Models/AppSettings.cs:3587` — `HasSeenDeeperEditorIntro` (auto-launch gate for `CreateDeeperEditorSteps`).
- `Models/AppSettings.cs:3590` — `HasSeenDeeperHTInteractiveTutorial` (set when user clicks `BtnTryHypnoTubeTutorial`; nothing reads it back yet).
- No migration / version-reset code touches these flags — `AppSettings.cs` only migrates non-tutorial things (`MigrateFromContentModeToMod` at line 924, `MigratedFlashClickableDecoupling` at line 1208).

---

## 2. Tutorial inventory

| Tutorial (TutorialType) | Launch | Persistence flag | Steps | Teaches |
| --- | --- | --- | --- | --- |
| `Deeper` | `BtnDeeperTutorial_Click` (`MainWindow.xaml.cs:2323`) **or** `BtnDeeperWelcomeTour_Click` (`MainWindow.xaml.cs:2306`) → `StartDeeperTabTutorial()` → `MainWindow.xaml.cs:22391` `StartTutorial(TutorialType.Deeper)` | None (no "seen" flag; the welcome-card visibility is gated by `HasSeenDeeperWelcome` but the tour itself is replayable). | 7 | Walks the Deeper tab: pitch, Player, New Enhancement, Library, Recent, Export-as-Media, done. |
| `DeeperEditor` | `BtnEditorHelp_Click` (`DeeperEditorWindow.xaml.cs:289`) **or** auto-launch on first editor open when `!HasSeenDeeperEditorIntro` (`DeeperEditorWindow.xaml.cs:266-284`). | `HasSeenDeeperEditorIntro` — read & flipped at `DeeperEditorWindow.xaml.cs:271-274`. Manual replay via `BtnEditorHelp` ignores the flag. | 8 | Editor coachmarks: timeline / preview / metadata / rules / selected panel / save+validate. |
| `DeeperEditorInteractiveHT` (Part 1) | `BtnTryHypnoTubeTutorial_Click` (`NewEnhancementDialog.xaml.cs:128`) → `StartInteractiveTutorial(part1, part2)` (`NewEnhancementDialog.xaml.cs:167`). | `HasSeenDeeperHTInteractiveTutorial` is **set** at `NewEnhancementDialog.xaml.cs:152` but **never read** — replay is unconditional. | 1 | Click Create with the pre-filled HypnoTube URL. |
| `DeeperEditorInteractiveHTPart2` | Handed off via `TutorialEventBus.PendingPart2Tutorial` (queued `NewEnhancementDialog.xaml.cs:212`, consumed `DeeperEditorWindow.xaml.cs:207-253`, started after ~800ms timer). | Same flag as Part 1; never read. | 16 | End-to-end: metadata → preview → +Effect (haptic) → +Rule (TimeReached→screen_shake) → Save. |
| `DeeperEditorInteractiveLocalAudio` (Part 1) | `BtnLocalAudioTutorial_Click` (`NewEnhancementDialog.xaml.cs:117`). | None (no persistence flag exists for audio/video tutorials). | 2 | Browse for .mp3/.wav → Create. |
| `DeeperEditorInteractiveLocalAudioPart2` | `PendingPart2Tutorial` handoff, same machinery as HT. | None. | 15 | Audio editor walkthrough; rule is TimeReached → `pause`. |
| `DeeperEditorInteractiveLocalVideo` (Part 1) | `BtnLocalVideoTutorial_Click` (`NewEnhancementDialog.xaml.cs:106`). | None. | 2 | Browse for video file → Create. |
| `DeeperEditorInteractiveLocalVideoPart2` | `PendingPart2Tutorial` handoff. | None. | 16 | Video editor walkthrough; rule is AttentionLost → screen_shake. |

Also relevant but **not Deeper-specific**:
- `FullTour` (`TutorialService.cs:230-303`) does not target any Deeper element.
- `GettingStarted`, `Settings`, `Presets`, `Progression`, `Achievements`, `Companion`, `Patreon`, `Avatar`, `Modding`, `Awareness` — none reference Deeper elements.

### Subscribing element lookups for each tutorial
- All step targeting goes through `TutorialOverlay.FindElementByName(_targetWindow, step.TargetElementName)` (`TutorialOverlay.xaml.cs:899-924`), which first calls `FrameworkElement.FindName(name)` then walks the visual tree comparing `element.Name`.
- The interactive Part-2 tutorials run against the editor window (the overlay is constructed with `new TutorialOverlay(this, App.Tutorial)` where `this` is `DeeperEditorWindow`), so name lookups happen inside the editor's name scope.

---

## 3. Step-by-step walkthrough per tutorial

Narrative text is shown **verbatim** (`Loc.Get("…")` → en.json). Line numbers point at the step definition in `TutorialService.cs`.

### Tutorial: `Deeper` — Hub tab tour (7 steps)

| # | Id | Title | Body | Target | Lookup | Conditions |
|---|---|---|---|---|---|---|
| 1 | `dp_intro` | "Welcome to Deeper" | "Deeper lets you wire haptics, gaze triggers, and timed cues into any audio or video. The file you create (.ccpenh.json) is portable — anyone running CCP can play it.\n\nThis tour takes about a minute." | (Center card) | — | `RequiresTab="deeper"` — `ApplyCallbacksToSteps` (`TutorialService.cs:154-190`) wires `_showDeeper` which calls `ShowTab("deeper")` (`MainWindow.xaml.cs:22408`). |
| 2 | `dp_player` | "The Player" | "The player runs an enhancement against a piece of media. Pick the audio, pick the enhancement, hit play — haptics and rules fire on the timeline.\n\nIf an enhancement file sits next to your audio with the same name, it loads automatically." | `BtnDeeperOpenPlayer` | `step.TargetElementName = "BtnDeeperOpenPlayer"` | RequiresTab="deeper". |
| 3 | `dp_new` | "New Enhancement" | "Pick a media source (local file or URL) and a type (audio or video). The editor opens with the timeline ready to go." | `BtnDeeperNewEnhancement` | `"BtnDeeperNewEnhancement"` | RequiresTab="deeper". |
| 4 | `dp_library` | "Your Library" | "Everything in your Deeper folder shows up here. Double-click to edit, right-click for play/reveal/delete.\n\nThe bundled \"Welcome to Deeper\" demo is here on first launch — open it to see a working example with regions, haptics, and rules." | `DeeperLibraryCard` | `"DeeperLibraryCard"` | RequiresTab="deeper". |
| 5 | `dp_recent` | "Recent Files" | "Last few files you opened. Includes anything outside your library too." | `DeeperRecentCard` | `"DeeperRecentCard"` | RequiresTab="deeper". |
| 6 | `dp_export` | "🆕 Export as Media" | "You can now bundle an enhancement directly into a copy of an MP4, MP3, or WAV. Find it in the editor under File → Export as Media… (Ctrl+E), or the button next to Save.\n\nThe exported file plays normally in any player — and CCP loads the effects automatically when you open it back in the Deeper player. No more sidecar .ccpenh.json files: hand someone the media and they get the enhancement for free." | (Center card) | — | RequiresTab="deeper". No element highlighted. |
| 7 | `dp_done` | "You're set" | "Open the demo from the library and poke at it — the editor's tour will walk you through that side too. You can re-run this tour anytime from the 🎓 Tutorial button." | (Center card) | — | RequiresTab="deeper". |

### Tutorial: `DeeperEditor` — Editor coachmarks (8 steps)

Runs against the `DeeperEditorWindow` (overlay is built with `this` as target at `DeeperEditorWindow.xaml.cs:300`). No `RequiresTab` since the editor is a separate window. All steps Manual-advance (default).

| # | Id | Title | Body | Target | Lookup | Conditions |
|---|---|---|---|---|---|---|
| 1 | `de_intro` | "The Editor" | "Two big things on screen: the preview + timeline on the left, and the side panel on the right. Everything you build for an enhancement happens here.\n\n💡 Big idea: an enhancement turns a piece of media into something interactive — gaze, blinks, attention, or just timestamps can fire haptics, jump the playhead, loop a region, anything." | (Center card) | — | None. |
| 2 | `de_timeline` | "Timeline" | "Click or drag to seek. Shift-drag the top half to mark a new region. Press R to drop a 5-second region at the playhead, H for a haptic event.\n\nThe top lane (R) is regions; the bottom lane (H) is haptic events.\n\n💡 Example: an MP3 with a bass drop at 1:20 — drop a haptic event at 1:20 with a sharp pattern, then watch your viewers squirm right when it hits." | `TimelineCanvas` | `"TimelineCanvas"` | None. |
| 3 | `de_preview` | "Preview Mode" | "Hit Preview and the engine runs your enhancement live as the media plays — you can feel haptics and watch rules fire before you save.\n\nNo webcam? B = blink, M = mouth, A = attention-lost. The mouse stands in for gaze inside the preview." | `BtnPreview` | `"BtnPreview"` | None. |
| 4 | `de_metadata` | "Metadata" | "Name, creator, description, tags — what shows up in the library tile when someone installs your file." | `TxtMetaName` | `"TxtMetaName"` | None. |
| 5 | `de_rules` | "Rules" | "Each rule is a trigger → action pair. Time-based triggers work on any media. Gaze, blink, mouth-open, and attention-lost need video and a webcam.\n\nUse + to add one. Cooldowns stop a noisy trigger from firing every frame.\n\n💡 Examples:\n• Gaze on bottom-right corner → loop the current region. They have to look there to make it stop.\n• Attention lost (face away from camera 10s) → seek back to the start. Stay focused, or start over.\n• Region entered \"Deepen\" → trigger a slow-build haptic pattern. Synced reinforcement on a part of the audio." | `RulesList` | `"RulesList"` | None. |
| 6 | `de_selected` | "Selected" | "Click anything on the timeline (a region, a haptic event) or any rule above, and its fields land here for editing." | `SelectedPlaceholder` | `"SelectedPlaceholder"` | None. |
| 7 | `de_save` | "Save & Validate" | "Validation runs on every keystroke — the strip at the bottom tells you what's broken. Save (Ctrl+S) writes to disk; the player and library hot-reload the file." | `TxtValidationSummary` | `"TxtValidationSummary"` | None. |
| 8 | `de_done` | "Have fun" | "That's the whole editor. You can pop this tour again from the ? button next to the title.\n\n💡 A good first project: open the bundled demo, change a haptic pattern, save, and play it in the player to feel the difference.\n\nOr start from scratch — pick an MP3 you love, drop haptic events on the beats, share the .ccpenh.json with anyone running CCP. They get the same experience, synced to the same audio." | (Center card) | — | None. |

### Tutorial: `DeeperEditorInteractiveHT` (Part 1, 1 step)

Runs against the `NewEnhancementDialog` (overlay built `_activeTutorialOverlay = new TutorialOverlay(this, App.Tutorial)` at `NewEnhancementDialog.xaml.cs:178`). Pre-condition set by the handler at lines 128-161: `RbVideo.IsChecked = true; TxtSource.Text = <hypnotube TikTok URL>`.

| # | Id | Title | Body | Target | Lookup | Advance | Conditions |
|---|---|---|---|---|---|---|---|
| 1 | `iht_create` | "Sample HypnoTube TikTok ready" | "Video is selected and a working URL is pasted into Source. Click Create to open the editor." | `BtnCreate` | `"BtnCreate"` | `OnButtonClick` | — |

### Tutorial: `DeeperEditorInteractiveHTPart2` (16 steps)

Runs against `DeeperEditorWindow`. Started by `DeeperEditorWindow_Loaded` ~800ms after the editor loads when `TutorialEventBus.PendingPart2Tutorial == DeeperEditorInteractiveHTPart2` (`DeeperEditorWindow.xaml.cs:207-253`).

| # | Id | Title | Body | Target | Lookup | Advance | Conditions |
|---|---|---|---|---|---|---|---|
| 1 | `iht_metadata` | "HypnoTube auto-filled your metadata" | "Title, creator, description and tags came from the page. Notice the lock next to Creator — that is the auto-fill lock. Click Next to continue." | `TxtMetaName` | `"TxtMetaName"` | Manual | — |
| 2 | `iht_lock` | "Auto-locked Creator field" | "When the URL is HypnoTube, Creator is auto-set and locked. Click the lock to override; click again to relock. Don't click it now — just observe. Click Next to continue." | `BtnCreatorLockToggle` | `"BtnCreatorLockToggle"` | Manual | — |
| 3 | `iht_play` | "Try the preview" | "The video loaded in the embedded browser. Click play to start it for a few seconds. (If the browser hasn't initialized yet, use Skip step.)" | `BtnPlayPause` | `"BtnPlayPause"` | `OnButtonClick` | `AllowManualSkip = true` |
| 4 | `iht_pause` | "Pause it" | "Pause around 5 seconds — we'll add an effect at the playhead. Click pause." | `BtnPlayPause` | `"BtnPlayPause"` | `OnButtonClick` | `AllowManualSkip = true` |
| 5 | `iht_addeffect` | "Add your first Effect" | "Click + Effect to drop a Haptic at the playhead. Effects are point-in-time stimuli — vibration, flash, bubbles, subliminal text, or screen overlays." | `BtnAddEffectHero` | `"BtnAddEffectHero"` | `OnEvent` "EffectAdded" | Emitted from `DeeperEditorWindow.Unified.cs:148,182`. |
| 6 | `iht_intensity` | "Set the vibration strength" | "The slider is at 100% by default. Drag it down to about half — anywhere between 30% and 70%. This controls how strong the haptic feels." | `SliderHapticIntensity` | `"SliderHapticIntensity"` | `OnSliderAtLeast` `[0.3,0.7]` | — |
| 7 | `iht_pattern` | "Pick a different vibration pattern" | "Open the Pattern dropdown and pick any pattern (Throb, Wave, Climax, etc). The pattern shapes how the haptic ramps up and down - you'll feel it when you hit Test next." | `CmbHapticPattern` | `"CmbHapticPattern"` | `OnSelectionEquals` (empty value → any change) | `AllowManualSkip = true` |
| 8 | `iht_test` | "Test the haptic" | "Click Test to feel the pattern on your connected haptic device. If you don't have one connected, click Skip step." | `BtnTestHaptic` | `"BtnTestHaptic"` | `OnButtonClick` | `AllowManualSkip = true` |
| 9 | `iht_addrule` | "Now add your first Rule" | "Click + Rule to add a TimeReached rule at the playhead. Rules react to events (time, gaze, attention, blink) and run actions (pause, seek, fire effects)." | `BtnAddRuleHero` | `"BtnAddRuleHero"` | `OnEvent` "RuleAdded" | Emitted `DeeperEditorWindow.Unified.cs:293`. |
| 10 | `iht_ruletime` | "Make the rule fire at 15 seconds" | "Type 15 in the Time (s) field. The rule will fire when the playhead crosses 15s during playback." | `TutorialTriggerTimeField` | `"TutorialTriggerTimeField"` (**dynamic name** — `DeeperEditorWindow.xaml.cs:2815` calls `AssignNameToLastTextBox(TriggerFields, "TutorialTriggerTimeField")` when the trigger field grid is rebuilt) | `OnTextEquals` "15" | — |
| 11 | `iht_ruleaction` | "Choose what the rule does" | "Open the Action dropdown and pick screen_shake. A short description of each action appears below the dropdown." | `CmbActionType` | `"CmbActionType"` | `OnSelectionEquals` "screen_shake" | `MatchByTag = true` |
| 12 | `iht_actionintensity` | "Set the shake intensity" | "Type 0.7 in the Intensity field below. At playback time, when the rule fires, the screen will shake at 70% strength." | `TutorialActionIntensityField` | `"TutorialActionIntensityField"` (**dynamic name** — `DeeperEditorWindow.xaml.cs:2863` calls `AssignNameToLastTextBox(ActionFields, "TutorialActionIntensityField")`) | `OnTextEquals` "0.7" | — |
| 13 | `iht_save` | "Save your enhancement" | "You have 1 Effect and 1 Rule. Click Save to write your .ccpenh.json file to disk." | `BtnEditorSave` | `"BtnEditorSave"` | `OnButtonClick` | — |
| 14 | `iht_savedialog` | "Pick a filename" | "The Save dialog has appeared. Use the suggested name (Title — Creator) or rename it, then click Save in the dialog. We'll wait here until the file is written." | (Center card) | — | `OnEvent` "FileSaved" | `AllowManualSkip=true`, `BlockBackgroundClicks=false`. Bus emit at `DeeperEditorWindow.xaml.cs:3876`. |
| 15 | `iht_done` | "Your first enhancement is saved!" | "Your .ccpenh.json now contains: a Haptic effect at 5s + a TimeReached rule at 15s that triggers a screen-shake. To play it later, open the Deeper Player from the Deeper tab and load this file. The effect and rule will fire at their configured times. You can also share the .ccpenh.json with other CCP users." | (Follow-up card) | — | `IsFollowUpCard=true` with 3 actions: Open File Location (`explorer /select`), Open Deeper Player (`OpenDeeperPlayerWithLastSavedEnhancement()`), Done. All three call `App.Tutorial?.Skip()`. |

(Note: 16 step definitions in `TutorialService.cs`, but step 14 is the savedialog and step 15 is the follow-up card. The TutorialService array shows step indexes 0-15; the overlay shows "Step N of 16".)

### Tutorial: `DeeperEditorInteractiveLocalAudio` (Part 1, 2 steps)

| # | Id | Title | Body | Target | Lookup | Advance | Conditions |
|---|---|---|---|---|---|---|---|
| 1 | `iaud_browse` | "Pick an audio file" | "Audio is selected. Click Browse and choose any .mp3, .wav, .m4a, .flac or .ogg file from your computer. (If you cancel the picker, click Skip step.)" | `BtnBrowse` | `"BtnBrowse"` | `OnButtonClick` | `AllowManualSkip=true`, `BlockBackgroundClicks=false` (the picker is on top). |
| 2 | `iaud_create` | "Open the editor" | "Path looks good? Click Create to open the editor. If the field is still empty, click Browse first." | `BtnCreate` | `"BtnCreate"` | `OnButtonClick` | `BlockBackgroundClicks=false` |

Handler sets `RbAudio.IsChecked = true` (`NewEnhancementDialog.xaml.cs:119`).

### Tutorial: `DeeperEditorInteractiveLocalAudioPart2` (15 steps)

| # | Id | Title | Body | Target | Lookup | Advance | Conditions |
|---|---|---|---|---|---|---|---|
| 1 | `iaud_metadata` | "Name your enhancement" | "Local files don't auto-fill metadata like HypnoTube does - you fill it in. Type a name (e.g. 'My Mix'), then click Next." | `TxtMetaName` | `"TxtMetaName"` | Manual | — |
| 2 | `iaud_play` | "Try the preview" | "The waveform replaces the video preview when you load audio. Click play to start it for a few seconds." | `BtnPlayPause` | `"BtnPlayPause"` | `OnButtonClick` | `AllowManualSkip=true` |
| 3 | `iaud_pause` | "Pause around 5 seconds" | "Pause near 5s - we'll drop an effect at the playhead next. Click pause." | `BtnPlayPause` | `"BtnPlayPause"` | `OnButtonClick` | `AllowManualSkip=true` |
| 4 | `iaud_addeffect` | "Add your first Effect" | "Click + Effect to drop a Haptic at the playhead. Audio enhancements support Haptic, Bubble, Subliminal and Overlay effects (no Flash, since there's no video to flash over)." | `BtnAddEffectHero` | `"BtnAddEffectHero"` | `OnEvent` "EffectAdded" | — |
| 5 | `iaud_intensity` | "Set the vibration strength" | "Drag the slider down to about half - anywhere between 30% and 70%. This controls how strong the haptic feels on your connected device." | `SliderHapticIntensity` | `"SliderHapticIntensity"` | `OnSliderAtLeast` `[0.3,0.7]` | — |
| 6 | `iaud_pattern` | "Pick a different vibration pattern" | "Open the Pattern dropdown and pick any pattern. The pattern shapes how the haptic ramps up and down - you'll feel it when you hit Test next." | `CmbHapticPattern` | `"CmbHapticPattern"` | `OnSelectionEquals` (any) | `AllowManualSkip=true` |
| 7 | `iaud_test` | "Test the haptic" | "Click Test to feel the pattern on your connected haptic device. If you don't have one, click Skip step." | `BtnTestHaptic` | `"BtnTestHaptic"` | `OnButtonClick` | `AllowManualSkip=true` |
| 8 | `iaud_addrule` | "Now add your first Rule" | "Click + Rule to add a TimeReached rule at the playhead. Audio rules can react to time and to regions you draw on the timeline (no gaze, blink or attention triggers - those need video)." | `BtnAddRuleHero` | `"BtnAddRuleHero"` | `OnEvent` "RuleAdded" | — |
| 9 | `iaud_ruletime` | "Make the rule fire at 8 seconds" | "Type 8 in the Time (s) field. The rule will fire when the playhead crosses 8s during playback." | `TutorialTriggerTimeField` | `"TutorialTriggerTimeField"` (dynamic, see HT step 10) | `OnTextEquals` "8" | — |
| 10 | `iaud_ruleaction` | "Choose what the rule does" | "Open the Action dropdown and pick 'Pause playback'. The audio will halt at 8s - handy for forcing the listener to dwell on a specific bar of the track." | `CmbActionType` | `"CmbActionType"` | `OnSelectionEquals` "pause" | `MatchByTag=true`, `AllowManualSkip=true` |
| 11 | `iaud_save` | "Save your enhancement" | "You have 1 Effect and 1 Rule. Click Save to write your .ccpenh.json file to disk." | `BtnEditorSave` | `"BtnEditorSave"` | `OnButtonClick` | — |
| 12 | `iaud_savedialog` | "Pick a filename" | "The Save dialog has appeared. Use the suggested name or rename it, then click Save in the dialog. We'll wait here until the file is written." | (Center) | — | `OnEvent` "FileSaved" | `AllowManualSkip=true`, `BlockBackgroundClicks=false` |
| 13 | `iaud_done` | "Your audio enhancement is saved!" | "Your .ccpenh.json now contains: a Haptic effect at 5s + a TimeReached rule at 8s that pauses playback. Open the Deeper Player from the Deeper tab to play it. You can share the file with other CCP users - anyone who has the same audio file at the bound path can play your edit." | (Follow-up card) | — | Shared `BuildInteractiveDoneCard` (`TutorialService.cs:2217-2258`). |

Note: the array length is 13 (the Audio Part 2 has 13 entries — the Audio flow skips HT's separate "creator-lock" step, so the count diverges from the HT walkthrough).

### Tutorial: `DeeperEditorInteractiveLocalVideo` (Part 1, 2 steps)

| # | Id | Title | Body | Target | Lookup | Advance | Conditions |
|---|---|---|---|---|---|---|---|
| 1 | `ivid_browse` | "Pick a video file" | "Video is selected. Click Browse and choose any .mp4, .webm, .mkv, .mov, .avi or .m4v file from your computer. (If you cancel the picker, click Skip step.)" | `BtnBrowse` | `"BtnBrowse"` | `OnButtonClick` | `AllowManualSkip=true`, `BlockBackgroundClicks=false` |
| 2 | `ivid_create` | "Open the editor" | "Path looks good? Click Create to open the editor. If the field is still empty, click Browse first." | `BtnCreate` | `"BtnCreate"` | `OnButtonClick` | `BlockBackgroundClicks=false` |

Handler sets `RbVideo.IsChecked = true` (`NewEnhancementDialog.xaml.cs:108`).

### Tutorial: `DeeperEditorInteractiveLocalVideoPart2` (14 steps)

| # | Id | Title | Body | Target | Lookup | Advance | Conditions |
|---|---|---|---|---|---|---|---|
| 1 | `ivid_metadata` | "Name your enhancement" | "Local files don't auto-fill metadata like HypnoTube does - you fill it in. Type a name (e.g. 'My Edit'), then click Next." | `TxtMetaName` | `"TxtMetaName"` | Manual | — |
| 2 | `ivid_play` | "Try the preview" | "The video loaded in the embedded VLC preview. Click play to start it for a few seconds." | `BtnPlayPause` | `"BtnPlayPause"` | `OnButtonClick` | `AllowManualSkip=true` |
| 3 | `ivid_pause` | "Pause around 5 seconds" | "Pause near 5s - we'll drop an effect at the playhead next. Click pause." | `BtnPlayPause` | `"BtnPlayPause"` | `OnButtonClick` | `AllowManualSkip=true` |
| 4 | `ivid_addeffect` | "Add your first Effect" | "Click + Effect to drop a Haptic at the playhead. Video enhancements support every effect type - Haptic, Flash, Bubble, Subliminal and Overlay." | `BtnAddEffectHero` | `"BtnAddEffectHero"` | `OnEvent` "EffectAdded" | — |
| 5 | `ivid_intensity` | "Set the vibration strength" | "Drag the slider down to about half - anywhere between 30% and 70%. This controls how strong the haptic feels on your connected device." | `SliderHapticIntensity` | `"SliderHapticIntensity"` | `OnSliderAtLeast` `[0.3,0.7]` | — |
| 6 | `ivid_pattern` | "Pick a different vibration pattern" | "Open the Pattern dropdown and pick any pattern. The pattern shapes how the haptic ramps up and down - you'll feel it when you hit Test next." | `CmbHapticPattern` | `"CmbHapticPattern"` | `OnSelectionEquals` (any) | `AllowManualSkip=true` |
| 7 | `ivid_test` | "Test the haptic" | "Click Test to feel the pattern on your connected haptic device. If you don't have one, click Skip step." | `BtnTestHaptic` | `"BtnTestHaptic"` | `OnButtonClick` | `AllowManualSkip=true` |
| 8 | `ivid_addrule` | "Now add your first Rule" | "Click + Rule. The default trigger is TimeReached at the playhead - but for video you can do much more. We'll change it next." | `BtnAddRuleHero` | `"BtnAddRuleHero"` | `OnEvent` "RuleAdded" | — |
| 9 | `ivid_ruletrigger` | "Switch the trigger to Attention Lost" | "Open the Trigger dropdown and pick 'Looks away (face leaves camera)'. This is video's superpower - it fires whenever the webcam tracker sees you look away. (Requires camera consent + a calibration when you actually run the enhancement.)" | `CmbTriggerType` | `"CmbTriggerType"` | `OnSelectionEquals` "attention_lost" | `MatchByTag=true`, `AllowManualSkip=true` |
| 10 | `ivid_ruleaction` | "Choose what the rule does" | "Open the Action dropdown and pick 'Shake the screen'. Look-away triggers a shake that snaps you back to attention. Cheeky." | `CmbActionType` | `"CmbActionType"` | `OnSelectionEquals` "screen_shake" | `MatchByTag=true`, `AllowManualSkip=true` |
| 11 | `ivid_actionintensity` | "Set the shake intensity" | "Type 0.7 in the Intensity field below. When you look away, the screen will shake at 70% strength." | `TutorialActionIntensityField` | `"TutorialActionIntensityField"` (dynamic) | `OnTextEquals` "0.7" | — |
| 12 | `ivid_save` | "Save your enhancement" | "You have 1 Effect and 1 Rule. Click Save to write your .ccpenh.json file to disk." | `BtnEditorSave` | `"BtnEditorSave"` | `OnButtonClick` | — |
| 13 | `ivid_savedialog` | "Pick a filename" | "The Save dialog has appeared. Use the suggested name or rename it, then click Save in the dialog. We'll wait here until the file is written." | (Center) | — | `OnEvent` "FileSaved" | `AllowManualSkip=true`, `BlockBackgroundClicks=false` |
| 14 | `ivid_done` | "Your video enhancement is saved!" | "Your .ccpenh.json now contains: a Haptic effect at 5s + an AttentionLost rule that screen-shakes when you look away. Open the Deeper Player from the Deeper tab to play it. Try looking away while it's running - you'll see the rule fire." | (Follow-up card) | — | Shared `BuildInteractiveDoneCard`. |

### Player window tutorials
**None.** `Views/Deeper/EnhancementPlayerWindow.xaml.cs` has zero references to `Tutorial`, `TutorialOverlay`, `TutorialEventBus`, or any tutorial-launching button. The player has no help button. The Hub tour's `dp_player` step (step 2) targets `BtnDeeperOpenPlayer` on the hub but never opens the player window. The interactive walkthroughs' follow-up cards offer "Open Deeper Player" which calls `OpenDeeperPlayerWithLastSavedEnhancement()` (`TutorialService.cs:2267-2296`) but no tutorial runs inside the player window after that.

---

## 4. Element reference index

Every UI element name that any Deeper-touching tutorial step references, plus the tutorials/step ids that touch it. Use this when renaming, moving, or deleting controls.

### Hub elements (MainWindow.xaml — Deeper tab)

| Element x:Name | Surface | Referenced by |
| --- | --- | --- |
| `BtnDeeperOpenPlayer` | Hub Deeper tab (MainWindow.xaml:8539) | `Deeper` step 2 (`dp_player`) |
| `BtnDeeperNewEnhancement` | Hub Deeper tab (MainWindow.xaml:8548) | `Deeper` step 3 (`dp_new`) |
| `DeeperLibraryCard` | Hub Deeper tab | `Deeper` step 4 (`dp_library`) |
| `DeeperRecentCard` | Hub Deeper tab | `Deeper` step 5 (`dp_recent`) |
| `BtnDeeperTutorial` | Hub Deeper tab (MainWindow.xaml:8530) | Entry point only — no step targets it. |
| `BtnDeeperWelcomeTour` | Hub Deeper tab welcome card (MainWindow.xaml:8609) | Entry point only — no step targets it. |
| `DeeperWelcomeCard` | Hub Deeper tab welcome card (MainWindow.xaml:8589) | Visibility controlled by `HasSeenDeeperWelcome`; no step targets it. |

### Editor window elements (DeeperEditorWindow.xaml)

| Element x:Name | Surface | Referenced by |
| --- | --- | --- |
| `BtnEditorHelp` | Editor titlebar (DeeperEditorWindow.xaml:239) | Entry point only — no step targets it. |
| `TimelineCanvas` | Editor center pane (DeeperEditorWindow.xaml:535) | `DeeperEditor` step 2 (`de_timeline`) |
| `BtnPreview` | Editor preview pane (DeeperEditorWindow.xaml:478) | `DeeperEditor` step 3 (`de_preview`) |
| `BtnPlayPause` | Editor preview controls (DeeperEditorWindow.xaml:462) | HT Part 2 steps 3-4 (`iht_play`, `iht_pause`); Audio Part 2 steps 2-3 (`iaud_play`, `iaud_pause`); Video Part 2 steps 2-3 (`ivid_play`, `ivid_pause`) |
| `TxtMetaName` | Editor metadata side panel (DeeperEditorWindow.xaml:564) | `DeeperEditor` step 4 (`de_metadata`); HT Part 2 step 1 (`iht_metadata`); Audio Part 2 step 1 (`iaud_metadata`); Video Part 2 step 1 (`ivid_metadata`) |
| `BtnCreatorLockToggle` | Editor metadata side panel (DeeperEditorWindow.xaml:570) | HT Part 2 step 2 (`iht_lock`) |
| `RulesList` | Editor side panel | `DeeperEditor` step 5 (`de_rules`) |
| `SelectedPlaceholder` | Editor selected-item panel (DeeperEditorWindow.xaml:662) | `DeeperEditor` step 6 (`de_selected`) |
| `BtnAddEffectHero` | Editor hero strip (DeeperEditorWindow.xaml:493) | HT Part 2 step 5 (`iht_addeffect`); Audio Part 2 step 4 (`iaud_addeffect`); Video Part 2 step 4 (`ivid_addeffect`) |
| `BtnAddRuleHero` | Editor hero strip (DeeperEditorWindow.xaml:486) | HT Part 2 step 9 (`iht_addrule`); Audio Part 2 step 8 (`iaud_addrule`); Video Part 2 step 8 (`ivid_addrule`) |
| `SliderHapticIntensity` | Editor haptic effect panel (DeeperEditorWindow.xaml:751) | HT Part 2 step 6 (`iht_intensity`); Audio Part 2 step 5 (`iaud_intensity`); Video Part 2 step 5 (`ivid_intensity`) |
| `CmbHapticPattern` | Editor haptic effect panel (DeeperEditorWindow.xaml:764) | HT Part 2 step 7 (`iht_pattern`); Audio Part 2 step 6 (`iaud_pattern`); Video Part 2 step 6 (`ivid_pattern`) |
| `BtnTestHaptic` | Editor haptic effect panel (DeeperEditorWindow.xaml:795) | HT Part 2 step 8 (`iht_test`); Audio Part 2 step 7 (`iaud_test`); Video Part 2 step 7 (`ivid_test`) |
| `CmbTriggerType` | Editor rule panel (DeeperEditorWindow.xaml:839) | Video Part 2 step 9 (`ivid_ruletrigger`) |
| `CmbActionType` | Editor rule panel (DeeperEditorWindow.xaml:855) | HT Part 2 step 11 (`iht_ruleaction`); Audio Part 2 step 10 (`iaud_ruleaction`); Video Part 2 step 10 (`ivid_ruleaction`) |
| `BtnEditorSave` | Editor footer (DeeperEditorWindow.xaml:1016) | HT Part 2 step 13 (`iht_save`); Audio Part 2 step 11 (`iaud_save`); Video Part 2 step 12 (`ivid_save`) |
| `TxtValidationSummary` | Editor footer (DeeperEditorWindow.xaml:1028) | `DeeperEditor` step 7 (`de_save`) |
| `TutorialTriggerTimeField` *(dynamic — assigned via `AssignNameToLastTextBox(TriggerFields, "TutorialTriggerTimeField")` at `DeeperEditorWindow.xaml.cs:2815`)* | Editor rule panel runtime grid | HT Part 2 step 10 (`iht_ruletime`); Audio Part 2 step 9 (`iaud_ruletime`) |
| `TutorialActionIntensityField` *(dynamic — assigned via `AssignNameToLastTextBox(ActionFields, "TutorialActionIntensityField")` at `DeeperEditorWindow.xaml.cs:2863`)* | Editor action panel runtime grid | HT Part 2 step 12 (`iht_actionintensity`); Video Part 2 step 11 (`ivid_actionintensity`) |

### Dialog elements (NewEnhancementDialog.xaml)

| Element x:Name | Surface | Referenced by |
| --- | --- | --- |
| `BtnBrowse` | NewEnhancementDialog source row (NewEnhancementDialog.xaml:60) | Audio Part 1 step 1 (`iaud_browse`); Video Part 1 step 1 (`ivid_browse`) |
| `BtnCreate` | NewEnhancementDialog footer (NewEnhancementDialog.xaml:115) | HT Part 1 step 1 (`iht_create`); Audio Part 1 step 2 (`iaud_create`); Video Part 1 step 2 (`ivid_create`) |
| `BtnTryHypnoTubeTutorial` | NewEnhancementDialog tutorials row (NewEnhancementDialog.xaml:99) | Entry point only. |
| `BtnLocalAudioTutorial` | NewEnhancementDialog tutorials row (NewEnhancementDialog.xaml:90) | Entry point only. |
| `BtnLocalVideoTutorial` | NewEnhancementDialog tutorials row (NewEnhancementDialog.xaml:81) | Entry point only. |
| `RbVideo` / `RbAudio` / `TxtSource` | NewEnhancementDialog | Pre-set by entry handlers; no step highlights them. |

### Player window elements
None referenced by any tutorial step.

---

## 5. Tutorial architecture (brief)

### Event bus (`TutorialEventBus`, `Services/TutorialEventBus.cs`)
- Single static event: `public static event EventHandler<string>? Event;`.
- `Emit(name)` invokes inside a `try { … } catch { }`. Subscribers receive on whatever thread emitted; `TutorialOverlay.OnBusEvent` re-marshals to the UI via `Dispatcher.BeginInvoke`.
- Three event names used by Deeper:
  - `"EffectAdded"` (`DeeperEditorWindow.Unified.cs:148,182`)
  - `"RuleAdded"` (`DeeperEditorWindow.Unified.cs:293`)
  - `"FileSaved"` (`DeeperEditorWindow.xaml.cs:3876`)
  - Also legacy: `"WindowLoaded:DeeperEditorWindow"` at `DeeperEditorWindow.xaml.cs:198` — kept "for any other listeners" per the code comment but Part 2 dispatch uses `PendingPart2Tutorial` instead.
- Two ambient state slots:
  - `LastSavedEnhancementPath` (string) — set right before `FileSaved` emit; consumed by the follow-up card's Open File Location / Open Deeper Player buttons.
  - `PendingPart2Tutorial` (`TutorialType?`) — set by `NewEnhancementDialog.BtnCreate_Click` after source validation, consumed once (`= null`) by `DeeperEditorWindow_Loaded`.

### Overlay rendering (`TutorialOverlay.xaml(.cs)`)
- Window: borderless, transparent, `Owner = MainWindow` (so it doesn't outrank Discord etc.), `ShowActivated="False"`, `WindowStyle="None"`, `AllowsTransparency="True"`.
- Covers the entire monitor that `MainWindow` is currently on (`GetMainWindowMonitorBoundsDip`, `TutorialOverlay.xaml.cs:406-429`) — DPI-corrected via `Screen.FromHandle` + `VisualTreeHelper.GetDpi`.
- Two-layer render:
  - `SpotlightCanvas` paints the dark dim. For Manual / Center / no-target steps it paints a full opaque rectangle (`DrawFullOverlay`, line 624). For element-targeted auto-advance steps it paints a `CombinedGeometry(Exclude)` punch-out around the target (`DrawSpotlightOverlay`, line 653), with a pink glow border + drop-shadow.
  - `TextPanel` (the side card) is a `Border` outside the canvas. Position calculated by `PositionTextPanel` (line 804): one of Top/Bottom/Left/Right relative to the target, centered along the perpendicular axis, clamped to screen bounds with auto-flip if it would overlap the target.
- Click-through to underlying UI: `ApplyWindowSpotlightRegion` (line 737) uses `SetWindowRgn(hwnd, region, true)` to physically remove the spotlight rect from the window's input region (alpha alone isn't enough on `AllowsTransparency=True` windows). Switched off for Manual steps; on for auto-advance steps and TextBox targets.
- Dim level: `0xA0` alpha over MainWindow, `0x70` over other (dialog/editor) windows (`DrawFullOverlay:632-634`). `BlockBackgroundClicks=false` drops dim to alpha=0 so OS file pickers remain interactive.

### Step lifecycle
- `TutorialService.Start(type)` (line 129): replaces `_currentSteps`, applies tab-switch callbacks, fires `TutorialStarted` + `StepChanged` for step 0.
- `Next()` (line 192) increments and re-invokes `OnActivate` + `StepChanged`; falls through to `Complete()` past the last step.
- `Skip()` (line 217) → `Complete()` (line 222) which sets `IsActive=false` and fires `TutorialCompleted`. The overlay then fades out (200 ms) and `Close()`s itself in `OnTutorialCompleted` (line 1235).
- Auto-advance subscriptions live on the overlay (`SubscribeAdvanceTrigger`, line 942):
  - `OnButtonClick` → triple-subscribes `PreviewMouseLeftButtonUp` + `Click` + parent-window `Closing` (with `DialogResult==true`). The first to win calls `AdvanceSync` which DEFERRS `Next()` to the next dispatcher tick.
  - `OnTextEquals` → `TextBox.TextChanged`; matched by `TextMatches` (case-insensitive, with numeric tolerance ±0.5 and substring fallback).
  - `OnSelectionEquals` → `Selector.SelectionChanged`; empty AdvanceValue means "any change advances". `MatchByTag=true` reads `ComboBoxItem.Tag` instead of `Content`.
  - `OnSliderAtLeast` → `Slider.PreviewMouseLeftButtonUp` (not `ValueChanged`, to avoid the initial track click satisfying the range).
  - `OnEvent` → handled inline in `OnBusEvent` / `HandleBusEventOnUi`.
- `_advanceFiredThisStep` guards once-per-step semantics so multi-subscription doesn't double-fire.
- Cross-window retargeting (`TargetWindowTypeName`): a step can declare it expects a different window; `UpdateStep` (line 451) and `HandleBusEventOnUi` (line 303) call `RetargetToWindow`. Not used by any Deeper tutorial today — Part 2 is dispatched by `DeeperEditorWindow_Loaded` creating a *new* overlay, not by retargeting.

### Animations / durations
- Fade in: 300 ms (`Loaded` handler at `TutorialOverlay.xaml.cs:108`).
- Fade out: 200 ms (`OnTutorialCompleted:1240`).
- Spotlight retry: 120 ms `DispatcherTimer` (`UpdateSpotlight:595`) if the target hasn't been measured yet (bounds 0,0 with width≤100).
- Part-2 startup delay: 800 ms after `DeeperEditorWindow_Loaded` (line 221) so layout settles and Part-1 deferred lambdas drain.

### Follow-up cards
- `step.IsFollowUpCard = true` swaps the Skip / Previous / Next button row for a stacked `FollowUpPanel` (3 buttons). Each button text is in `FollowUpButton{N}Text`; click invokes `FollowUpAction{N}(step)`. None of the buttons auto-call `Skip()` — every Deeper follow-up action explicitly calls `App.Tutorial?.Skip()` (see `BuildInteractiveDoneCard:2228-2256`).

---

## 6. Failure modes

What happens if a tutorial step's `TargetElementName` no longer matches any element in the target window?

**Behavior: silent degrade. No crash, no log warning.**

Code path: `TutorialOverlay.UpdateSpotlight` (line 550-622) calls `FindElementByName(_targetWindow, step.TargetElementName)` (line 574). The helper at line 899-924:
```csharp
private FrameworkElement? FindElementByName(DependencyObject? parent, string name)
{
    if (parent == null) return null;
    if (parent is FrameworkElement fe)
    {
        var found = fe.FindName(name) as FrameworkElement;
        if (found != null) return found;
    }
    try
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element && element.Name == name)
                return element;
            var result = FindElementByName(child, name);
            if (result != null) return result;
        }
    }
    catch { }
    return null;
}
```
- A missing/renamed element returns `null`.
- `UpdateSpotlight` line 575-580 then handles `targetElement == null` by drawing a full-screen dim with the text card **centered**, instead of spotlighting nothing. **No callout points at (0,0)** — that path is reserved for the "bounds not measured yet" retry (line 592).
- **No log line is written** when the lookup fails. The user sees a generic centered card with the step's title/body; the tour still advances on Next.

**However, for auto-advance steps the consequence is harsher**:
- `SubscribeAdvanceTrigger` (line 942-1039) does an early `if (string.IsNullOrEmpty(step.TargetElementName)) return;` and `if (target == null) return;` (lines 955-957). **No subscription is wired** if the target is missing.
- For `OnButtonClick / OnTextEquals / OnSelectionEquals / OnSliderAtLeast` steps, this means the step has **no way to auto-advance**. The user is stuck with no Next button (manual mode is off — see `BtnNext.Visibility` logic at `UpdateStep:498-501`).
- Recovery: if the step set `AllowManualSkip = true`, the "Skip step" button is visible (line 496). The interactive tutorials use this for the haptic Test / Play / Pause / Pattern steps but **not** for `OnEvent` steps (EffectAdded, RuleAdded, FileSaved). For an `OnEvent` step, if the emit site is gone too, the user must "Skip Tutorial" entirely.
- For `OnEvent` steps specifically, the subscription happens via the always-on `TutorialEventBus.Event` handler (line 65). The target element does not need to exist for the advance to fire — but the spotlight/card targeting will still degrade as above.

**Visibility=Collapsed targets**: `FindElementByName` returns the element regardless of visibility (it walks the visual tree). `GetElementBounds` (line 926-938) calls `element.PointToScreen(...)` and reads `ActualWidth/Height`. For a Collapsed element those are 0, so `bounds.Width <= 100 && bounds.X==0 && bounds.Y==0` triggers the retry-timer path (line 592-616), which will spin a 120 ms `DispatcherTimer` forever (each tick re-measures; never grows). The user sees the full-screen dim with the card centered (the timer's first tick action calls `DrawFullOverlay` then `DrawSpotlightOverlay(retryBounds, …)` against bounds that are still empty, painting essentially nothing). This is the worst observable failure: a stuck timer that never converges, plus no "skip step" button if the step doesn't allow manual skip.

**No try/catch around target rendering** — but every external call (`BringIntoView`, `PointToScreen`, `UpdateLayout`, `ApplyWindowSpotlightRegion`) is individually wrapped in try/catch (lines 587-588, 928-937, 751, 786-789). So no exception escapes to the dispatcher.

**Summary of upcoming-mission risk**:
- Renaming an element used by a Manual step → user sees a centered card instead of a spotlight; tutorial still works, just less helpful.
- Renaming an element used by an auto-advance step → user gets stuck unless `AllowManualSkip=true`.
- Setting an element's Visibility=Collapsed → potential infinite retry-timer plus a stuck step (worst case).
- Removing a bus emit (`EffectAdded` / `RuleAdded` / `FileSaved`) → corresponding `OnEvent` step strands the user; only Skip Tutorial recovers (the `iaud_savedialog` / `iht_savedialog` / `ivid_savedialog` steps have `AllowManualSkip=true`, but the `addeffect`/`addrule` steps do not).

---

## 7. State and persistence

| Tutorial | Settings flag | Where flag is read | Where flag is set | Replay entry point | Upgrade behavior |
| --- | --- | --- | --- | --- | --- |
| `Deeper` (hub tour) | None | — | — | `BtnDeeperTutorial` (always) + `BtnDeeperWelcomeTour` (always; also dismisses the welcome card). | N/A. |
| `DeeperEditor` (editor coachmarks) | `HasSeenDeeperEditorIntro` (`Models/AppSettings.cs:3587`) | `DeeperEditorWindow.xaml.cs:271` — checked on `_Loaded`; if false, auto-launches once. | `DeeperEditorWindow.xaml.cs:273-274` — set to true immediately and `App.Settings?.Save()` called before the overlay is shown. | `BtnEditorHelp` (`DeeperEditorWindow.xaml.cs:289-291`) — replays regardless of the flag. | Flag has `[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]` so it persists across upgrades. No migration code resets it. Users who upgrade do **not** re-see the auto-tour. |
| `DeeperEditorInteractiveHT` (Part 1 + Part 2) | `HasSeenDeeperHTInteractiveTutorial` (`Models/AppSettings.cs:3590`) | **Never read anywhere.** (Grep confirmed only one consumer, the writer itself.) | `NewEnhancementDialog.xaml.cs:152` — set + saved when user clicks `BtnTryHypnoTubeTutorial`. | `BtnTryHypnoTubeTutorial` — replay always works (flag is unconditional). | Same `DefaultValueHandling=Ignore` — persists; but nothing reads it, so functionally a no-op. |
| `DeeperEditorInteractiveLocalAudio` (Part 1 + Part 2) | None — no flag exists. | — | — | `BtnLocalAudioTutorial` — always replayable. | N/A. |
| `DeeperEditorInteractiveLocalVideo` (Part 1 + Part 2) | None — no flag exists. | — | — | `BtnLocalVideoTutorial` — always replayable. | N/A. |
| Welcome card itself | `HasSeenDeeperWelcome` (`AppSettings.cs:3584`) — gates `DeeperWelcomeCard.Visibility`. | `MainWindow.xaml.cs:2292` (`UpdateDeeperWelcomeCardVisibility`). | `MainWindow.xaml.cs:2300` (`DismissDeeperWelcomeCard`, called from Tour, Open Demo, and Got It buttons). | The card itself is the entry point; once dismissed there's no way to re-show it. | Persisted; not reset on upgrade. |
| Deeper tab pulse | `HasSeenDeeperTab` (`AppSettings.cs:3578`) | `MainWindow.xaml.cs:2280` (Deeper tab `_Shown`) | Same handler immediately flips it true. | None (one-shot). | Persisted. |

**Upgrade flow**: `AppSettings.cs` has no migration that resets any of these flags. `Migrate*` methods only touch ContentMode/Mod (`MigrateFromContentModeToMod` line 924) and the FlashClickable one-shot (`MigratedFlashClickableDecoupling` line 1208). Users who installed an earlier version with these flags set to true will not re-see auto-tours, even after a major version bump.

---

## 8. Open questions and dead-code findings

### Confirmation request from prior recon
**Confirmed: the prior UI-recon assertion was WRONG as of the current tree.** All three buttons on `NewEnhancementDialog` are wired and functional:
- `BtnLocalVideoTutorial_Click` (`NewEnhancementDialog.xaml.cs:106-112`) — sets `RbVideo`, calls `StartInteractiveTutorial(DeeperEditorInteractiveLocalVideo, DeeperEditorInteractiveLocalVideoPart2)`. Step lists exist at `TutorialService.cs:2007-2037` and `2044-2211`.
- `BtnLocalAudioTutorial_Click` (`NewEnhancementDialog.xaml.cs:117-123`) — sets `RbAudio`, calls `StartInteractiveTutorial(DeeperEditorInteractiveLocalAudio, DeeperEditorInteractiveLocalAudioPart2)`. Step lists at `TutorialService.cs:1805-1840` and `1847-2001`.
- `BtnTryHypnoTubeTutorial_Click` (`NewEnhancementDialog.xaml.cs:128-161`) — fills URL, calls `StartInteractiveTutorial(DeeperEditorInteractiveHT, DeeperEditorInteractiveHTPart2)`.

The "framework-ready stubs" comment in `NewEnhancementDialog.xaml:72-75` is **stale** — it predates the Local Audio / Local Video Part-1 + Part-2 implementations. The two "coming_soon" strings in en.json (`deeper_tutorial_coming_soon_local_video`, `deeper_tutorial_coming_soon_local_audio`, lines 2891-2892) are **dead** — no code references them. Confirmed via grep across the whole project.

### Other dead-code / inconsistencies found
1. **`HasSeenDeeperHTInteractiveTutorial` is write-only** (`AppSettings.cs:3590`, set at `NewEnhancementDialog.xaml.cs:152`). No code path reads it. Either intended to gate a future "first-time hint" (the comment at line 147 says so) or stale scaffolding.
2. **Legacy bus event `"WindowLoaded:DeeperEditorWindow"`** (`DeeperEditorWindow.xaml.cs:198`) — has no current subscriber. The Part-2 dispatch uses `PendingPart2Tutorial` directly; the comment ("kept for any other listeners") acknowledges this. The general `WindowLoaded:` plumbing in `TutorialOverlay.HandleBusEventOnUi:303-339` works for any future `TargetWindowTypeName` step but none of the Deeper tutorials use that machinery today.
3. **No Deeper-targeting step in any global tutorial.** `FullTour`, `GettingStarted`, `Settings`, `Presets`, `Progression`, `Achievements`, `Companion`, `Patreon`, `Avatar`, `Modding`, `Awareness` — none reference `BtnDeeper`, any Deeper element, or `RequiresTab="deeper"`. The MainWindow help-menu (`MainTutorialOverlay`, MainWindow.xaml:11399-11680) intentionally has no "Deeper" entry; the only menu entry that would reach Deeper would have to be added there.
4. **The `MainTutorialOverlay` help-menu** lists Settings/Presets/Progression/Achievements/Companion/Patreon/Awareness/Avatar/Modding only. No Deeper button — so users opening the global `?` menu never get directed to the Deeper tour. The only entry points to the Deeper tour are `BtnDeeperTutorial` (inside the Deeper tab itself) and `BtnDeeperWelcomeTour` (one-shot welcome card). Out of scope to fix but worth flagging.
5. **HT Part 2 step counter says "16"** but the array is 16 entries (steps 1-16 in the table above). Earlier I counted "step 14 is savedialog, step 15 is follow-up" — re-counting from `TutorialService.cs:1581-1795`: phase 2 (5 steps) + phase 3 (4 steps) + phase 4 (5 steps) + phase 5 (2 steps) + phase 6 (1 step) = 17. There's a discrepancy — recount in source if precise count matters. Audio Part 2 = 13 entries, Video Part 2 = 14 entries.
6. **`TutorialTriggerTimeField` / `TutorialActionIntensityField` are runtime-assigned x:Names.** These don't exist in any XAML file. They are stamped onto the last TextBox inside the dynamically-rebuilt TriggerFields/ActionFields grids by `AssignNameToLastTextBox` calls at `DeeperEditorWindow.xaml.cs:2815` and `:2863`. Renaming/restructuring the rule editor's trigger/action field grid (or changing which TextBox is "last") will silently break the tutorial. Grep for `AssignNameToLastTextBox` to find the helper and its callsites.

### Things to clarify with the user before any rename/cleanup mission
- Is the welcome-card / `HasSeenDeeperWelcome` flow considered a "tutorial" for the purposes of UI missions? It does no overlay highlighting but is the only first-run gate users meet for the Deeper feature.
- Should the dead `deeper_tutorial_coming_soon_local_*` localization keys be removed? They're now misleading if anyone greps for "coming soon" while assessing the feature surface.
- Should `HasSeenDeeperHTInteractiveTutorial` be wired up (to suppress the welcome card on second visit, say) or deleted?
