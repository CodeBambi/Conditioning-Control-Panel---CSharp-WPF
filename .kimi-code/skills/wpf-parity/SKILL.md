---
name: wpf-parity
description: "Feature archaeology and parity discipline for the WPF-to-Avalonia port. Use this skill whenever you need to know what a feature does in the original WPF head, when porting or changing ANY ported behavior, when merging main into feat/crossplatform, when updating the parity matrix or task board, or when the two heads behave differently. Also use it when the user asks 'does X work like WPF', 'what is missing', or 'keep track of what exists in WPF'."
---

# wpf-parity

## The bar

- **1:1 behavioral parity with WPF on Windows is the floor, not the ceiling.** Every ported feature must do what the WPF head does, and must be at least as fast and smooth. A port that is heavier or laggier is a defect even if it works.
- **UX may look different; behavior may not.** Visual improvements are welcome (see `dashboard-design`), but timings, triggers, settings semantics, focus behavior, multi-monitor behavior, and event ordering must match.
- Degradation is allowed ONLY on Linux/macOS/Android for inherently platform-limited features (hooks, wallpaper, WebView2, WASAPI), and must degrade gracefully, never trap input or crash.
- No permanent stubs. "Builds and looks right" is not done: the first port shipped inert UI (dead START button, dead overlays). A feature is ported when it is exercised end-to-end in the running app.

## Map of the WPF head (where behavior lives)

The WPF head is code-behind-heavy, no MVVM (ViewModels/ has one file). Knowing the layout saves hours:

| Area | Location |
|---|---|
| Main window | `ConditioningControlPanel/MainWindow/` - one giant partial class across ~45 files (`MainWindow.Presets.cs`, `.Browser.cs`, `.Assets.cs`, `.StartStop.cs`, `.UiUpdates.cs`, `.TabNavigation.cs`, ...). `MainWindow.xaml` itself is 127KB |
| Dashboard | `Views/Tabs/SettingsTabView.xaml` (the "settings" tab IS the home dashboard): `VelvetFeatureGrid`, 12 `FeatureCard`s + center logo. Cards: Flash, Visuals, Video, Subliminal, Spiral, LockCard, PinkFilter, MindWipe, BubblePop, BouncingText, System, BubbleCount |
| Feature cards | `Features/FeatureCard.xaml(.cs)` (IsActive ring), `Features/*FeatureControl.xaml` (per-feature settings UIs hosted in `FeaturePopupWindow`) |
| Services | `Services/` - flash `Flash/FlashService.cs`; video `Video/VideoService.cs` (+Dual/Mirror/Wallpaper); subliminal+bouncing text `Subliminal/`; spiral/pink/brain-drain overlays `Notifications/OverlayService.cs`; lock card+mind wipe+brain drain services `LockCard/`; bubbles `BubbleService.cs` (230KB); chaos `Chaos/ChaosModeService.cs` + `Chaos/` UI dir; audio ducking `AudioService.cs`; webcam `Webcam/`; gaze `Tracking/`; haptics `Haptics/`; progression/quests/achievements `Progression/`; companion `../AvatarTube/` + `Companion/`; browser `../MainWindow/MainWindow.Browser.cs` + `Browser/`; Deeper `Deeper/`; sessions `Session/SessionEngine.cs`; keyword triggers `KeywordTriggerService.cs` + `ScreenOcrService`; tutorial `TutorialService.cs` |
| Service access | ~90 static properties on `App` (`App.Flash`, `App.Video`, ...) in `App.xaml.cs` (152KB) |
| Settings | `CCP.Core/Models/AppSettings.cs` (shared with the port since the section-19.4 collapse) |
| Themes/mods | `CCP.Core/Models/BuiltInMods.cs`, `Services/ModService.cs`, `MainWindow.xaml.cs` `RefreshThemeAwareElements()` |

Folder names lie sometimes: `OverlayService` is under `Notifications/`, `MindWipeService`/`BrainDrainService` under `LockCard/`.

**Sliced reads are mandatory** for the big files. Non-exhaustive list of 100KB+ offenders: AppSettings.cs 192KB (yes, the file step 1 sends you to), BubbleService 230KB, WebcamTrackingService 186KB, ChaosModeService 172KB, VideoService 155KB, App.xaml.cs 152KB, ChaosHubWindow.xaml.cs 144KB, ProfileSyncService 144KB, BuiltInMods.cs 134KB, AvatarTubeWindow.Speech.cs 133KB, MainWindow.xaml 127KB, FlashService 116KB, TutorialService 116KB, MainWindow.Browser.cs 114KB, MainWindow.xaml.cs 110KB, MainWindow.UiUpdates.cs 106KB. Grep for the member you need, then Read the enclosing range. Task-board ledger lines are also extremely long; Read that file in <=45-line slices.

## Feature archaeology workflow (extracting the behavioral contract)

When you need "what does WPF do for X":

1. **Settings first**: find the feature's flags/values in `CCP.Core/Models/AppSettings.cs`. They name the semantics (enabled flags such as `FlashEnabled`, `SpiralEnabled`; timings; probabilities).
2. **Service**: locate the service (table above), Grep for the settings it reads, its public methods, its events, and its timers. Note event names and ordering.
3. **UI**: find the `Features/*FeatureControl` and dashboard card wiring (`MainWindow.Presets.cs`: `RefreshFeatureCardActiveStates`, `ShowFeaturePopup`, `OnFeatureCardToggleRequested`).
4. **Windows/overlays**: if the feature draws on screen, find its window classes and their input model (see `overlay-clickthrough`).
5. Write the contract down as: inputs (settings), triggers (timers/events), visible behavior (what/where/how long), input behavior (click-through? clickable? focus?), multi-monitor behavior, and edge cases. That contract is what the Avalonia side must reproduce, and what belongs in a task-board row or parity-matrix item.

## The living trackers (external memory - keep them true)

| Doc (all under `ConditioningControlPanel/docs/`) | Role | Update rule |
|---|---|---|
| `avalonia-ui-parity-matrix.md` | Per-screen verification checklist | Flip `[ ]` to `[x]` ONLY after exercising the item end-to-end in the running app, side by side with WPF, matching function, look, and speed, and record the evidence in the row. "Renders" is not verified. OWNER RULING 2026-07-02: all pre-existing `[x]` marks are VOID (the port was hand-made; the 2026-06-23 sweep is not trusted); the matrix is being re-earned from a full reset under `skia-rebuild-goal.md` WS0 |
| `avalonia-migration-task-board.md` | Live work queue | New gaps become rows; claims are append-only ledger rows (see `port-plan`) |
| `crossplatform-rebuild-plan.md` section 1A | Phase-level status snapshot | Update when a phase materially changes |
| `crossplatform-rebuild-plan.md` section 19 | Sync-from-main workflow + backlog | Record deferred UI work per merge |

Docs lag code (they are updated in batches). When a doc contradicts the code, trust the code, then fix the doc.

## Sync-from-main workflow (WPF keeps shipping; the port must keep up)

When `main` merges into `feat/crossplatform`:

1. `git diff --stat <prev>..<new> -- ConditioningControlPanel/` to list what changed.
2. Bucket each file:
   - **Localization JSON**: auto-syncs (CCP.Core Content-links `Localization/Languages/*.json`). No action.
   - **Models**: since the section-19.4 collapse, `CCP.Core/Models` is the single source referenced by BOTH heads; model drift is dead. If a merge tries to resurrect a WPF-side `Models/` file, that is a conflict artifact, not a real change.
   - **Portable service logic**: port into the Core service.
   - **UI changes**: port if that screen is already ported; otherwise add a task-board backlog row.
   - **WPF-only infra** (installer, Velopack remnants, win-only glue): no action.
3. Build the slnf + run Core tests.
4. Record deferrals in the task board section 19.3-style backlog.

**Merge trap:** modify/delete conflict resolutions can silently drop main's deltas (it happened to `Quest.cs` and `AppSettings.cs`; both had to be re-applied by hand). After any merge with modify/delete conflicts, diff the affected files against main's version explicitly.

## Gap hunting

- Stub floor: `grep -rinE "TODO|stub|not ported|not wired|placeholder|NotImplemented|No-?op" ConditioningControlPanel/CCP.Avalonia --include=*.cs` - treat as a floor, not a ceiling.
- The ceiling is a full click-through of every feature in the running app; unnamed gaps only show up when exercised.
- Behavioral diffs found while doing anything else get logged as task-board rows immediately (external memory beats transcript memory).

## Related skills

- `port-feature` - the implementation workflow that consumes the contract you extract here
- `port-plan` - claims, lanes, and sequencing before you start
- `port-audit` - periodic whole-port health sweep
- `dashboard-design` - what may legitimately differ (visual language) vs what may not (behavior)
