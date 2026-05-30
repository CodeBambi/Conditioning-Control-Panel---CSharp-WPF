# In-App Tutorial / Help System — Recon & Design Context

> **Status:** Recon + proposed design only. No feature code written.
> **Goal:** Add a "?" affordance on complex features that opens a small popup playing a
> short **muted, looping** video clip, with a "watch the full tutorial" link out to the
> hosted website.
> **Date:** 2026-05-30

This document maps the existing infrastructure relevant to that goal, proposes a design
that reuses it, and flags open questions and architectural friction. All paths are
absolute under `C:\Projects\Conditioning-Control-Panel---CSharp-WPF\ConditioningControlPanel`.

---

## 0. TL;DR

The app already has **most of the scaffolding** for this feature:

- A **help content system** (`HelpContentService` + `HelpContent` model + `HelpTooltipBuilder`)
  with ~55 topics keyed by `SectionId`, already wired to "?" buttons across the UI.
- A **reusable `FeatureCard` control** (`Features/FeatureCard.xaml.cs`) that *already* renders a
  per-card "?" button (`BtnHelp`) bound to a `HelpSectionId` dependency property and shows a rich
  tooltip. This is the single best hook point.
- A **borderless popup window** (`Features/FeaturePopupWindow.xaml`) used as the card → detail
  pattern, plus a `Popup`+`UserControl` pattern (`FeatureSettingsPopup`) for anchored panels.
- A **lightweight looping video/GIF player** already proven in production: `MiniPlayerWindow`
  (LibVLC `VideoView` with an `EndReached` re-`Play` loop + `Mute = true`; GIF via XamlAnimatedGif
  `RepeatBehavior.Forever`).
- A **bundled-asset pipeline** in the `.csproj` for shipping disk-copied content
  (`Resources\sounds`, `Resources\DeeperDemos`, etc.) that a `Resources\tutorial_videos\` folder
  would slot into directly.
- A **localization system** (`{loc:Str key}` markup extension over flat JSON, 9 languages) that
  makes **localized text-overlay captions ~50× cheaper than locale-specific clips**.
- A **settings-persistence pattern** (`AppSettings` + `SettingsService`) for first-run / "seen"
  flags (`Welcomed`, `DismissedNotificationKeys`, etc.) that a `SeenHelpTips` set would copy.

**The cheapest build:** extend the existing "?" affordances to open a small popup that plays a
bundled muted-looping clip (reusing `MiniPlayer`'s loop logic), overlay a localized caption
(`{loc:Str}`), and add a "Watch full tutorial" button using the existing
`Process.Start(... UseShellExecute = true)` link pattern. Almost nothing new is structural.

---

## 1. File / Flow Map

### 1.1 Existing help content system

| File | Role |
|------|------|
| `Models\HelpContent.cs` | Data model: `SectionId`, `Icon`, `Title`, `WhatItDoes`, `Tips`, `HowItWorks`. **No media field yet.** |
| `Services\HelpContentService.cs` (~1222 lines) | Static registry of ~55 `HelpContent` topics. API: `GetContent(id)`, `HasContent(id)`, `GetAllSectionIds()`. |
| `Services\HelpTooltipBuilder.cs` (~162 lines) | `Build(HelpContent, FrameworkElement host) → ToolTip`. Builds a styled dark/pink tooltip (header, "What it does", tips, "How it works"), MaxWidth 360. Resolves `HelpTooltipStyle`/`PinkBrush` by walking the logical tree up into the owning Window. |

`HelpContent` (current shape — `Models\HelpContent.cs:1`):
```csharp
public class HelpContent {
    public string SectionId { get; set; } = "";
    public string Icon { get; set; } = "?";
    public string Title { get; set; } = "";
    public string WhatItDoes { get; set; } = "";
    public List<string> Tips { get; set; } = new();
    public string HowItWorks { get; set; } = "";
    public bool HasTips => Tips?.Count > 0;
    public bool HasHowItWorks => !string.IsNullOrEmpty(HowItWorks);
}
```
Topic IDs already present include: `FlashImages`, `Visuals`, `Video`, `MiniGame`, `Subliminals`,
`Sessions`, `SessionDetails`, `KeywordTriggers`, `ScreenOcr`, `WebcamGames`, `GazeMinigame`,
`FocusGaze`, `BlinkTrainer`, `Modding`, `Haptics`, `Companions`, etc. — see
`HelpContentService.GetAllSectionIds()`.

### 1.2 "?" affordance — the existing pattern

Two existing entry points already render a "?" tied to a `SectionId`:

**(a) `HelpButtonStyle` + `SetHelpContent` (Settings/section headers).**
- Style: `MainWindow.xaml:826` (`HelpButtonStyle`, 18×18, `Cursor="Help"`, `?` glyph,
  `ToolTipService.ShowDuration=60000`).
- Wire-up: `MainWindow.xaml.cs:16997`
  ```csharp
  private void SetHelpContent(Button helpButton, string sectionId) {
      var content = Services.HelpContentService.GetContent(sectionId);
      helpButton.ToolTip = Services.HelpTooltipBuilder.Build(content, this);
  }
  ```
- ~40 named help buttons exist (`HelpBtnFlash`, `HelpBtnVideo`, `HelpBtnKeywordTriggers`, …).

**(b) `FeatureCard.BtnHelp` (dashboard "mosaic" tiles) — the richest hook.**
- `Features\FeatureCard.xaml.cs:38` — `HelpSectionIdProperty` dependency property.
- `Features\FeatureCard.xaml.cs:175` `RefreshHelpTooltip()` builds the tooltip from
  `HelpContentService.GetContent(id)` and **auto-hides `BtnHelp` when the id is null/unknown**.
- `Features\FeatureCard.xaml.cs:221` `OnClick` already swallows clicks that originate inside
  `BtnHelp`, so the "?" is click-isolated from the card body — exactly what a click-to-open-video
  affordance needs.

### 1.3 The dashboard mosaic (card → popup) flow

- `MainWindow.xaml:~2582` `VelvetFeatureGrid` — the dashboard grid of `FeatureCard` tiles. Each
  card sets `HelpSectionId="FlashImages"` etc. (`MainWindow.xaml:~2602`).
- Clicking a card raises `FeatureCard.Click` → opens `Features\FeaturePopupWindow.xaml`, a
  borderless `Window` (520×640, `WindowStartupLocation="CenterOwner"`, pink 1px border, draggable
  titlebar with icon + title + ✕) whose `ContentHost` `ContentControl` hosts a feature-specific
  `UserControl` from `Features\*FeatureControl.xaml` (e.g. `FlashFeatureControl`, `VideoFeatureControl`).
- This is the canonical "open a detail surface for a feature" flow and is where in-context help video
  most naturally lives.

### 1.4 Existing tutorial/onboarding system (separate, heavier)

There is already a guided **coach-mark tour** system — distinct from the lightweight "?" help and
**not** what this feature needs, but worth knowing it exists (and reusing its persistence ideas):

| File | Role |
|------|------|
| `Models\TutorialStep.cs` | Coach-mark step model: target element name, spotlight position, advance triggers (`OnButtonClick`, `OnSliderAtLeast`, …), follow-up buttons. |
| `Services\TutorialService.cs` | State machine. `Start(TutorialType)`, `Next/Previous/Skip`, `StepChanged`/`TutorialCompleted` events. Enumerates many `TutorialType`s (FullTour, Settings, Companion, Deeper, DeeperEditor*, Awareness…). **Does NOT persist completion** — every restart, tours are "new" again. |
| `Services\TutorialEventBus.cs` | Cross-window event broker for multi-step tours (e.g. Deeper editor part 1 → part 2). |
| `TutorialOverlay.xaml(.cs)` | Topmost transparent spotlight window: dims screen, cuts a hole around the target, shows a card with nav buttons. |

**Recommendation:** keep the new video-help feature *separate* from `TutorialService` — the spotlight
tour is overkill for "hover a ? and watch a 5s loop." Borrow its **localization keys convention** and
add the **persistence it lacks** (see §8).

### 1.5 Reusable popup / floating-UI patterns

| Pattern | Example | Shape | Best for |
|---------|---------|-------|----------|
| Borderless detail `Window`, CenterOwner | `Features\FeaturePopupWindow.xaml` | `Window`, pink border, titlebar+✕, `ContentHost` | Click-to-open feature detail (and full help video) |
| `Popup` + `UserControl`, anchored | `SessionEditorWindow.xaml:252` hosting `FeatureSettingsPopup` | WPF `Popup`, `Placement="Mouse"`/`PlacementTarget`, `PopupAnimation="Fade"`, `AllowsTransparency` | **Hover/anchored preview next to a "?" button** |
| Corner / center toast `Window` | `AchievementPopup`, `PinkRushPopup`, `QuestCompletePopup`, `RoadmapStepPopup`, `AnnouncementPopup` | borderless `Window`, `Topmost`, `ShowActivated=False`, manual 300 ms opacity fade | Transient notifications (not this feature) |

No `Adorner` or UWP `Flyout` usage exists; everything is either a `Window` or a WPF `Popup`. The
**`Popup` + `UserControl`** pattern (`FeatureSettingsPopup`) is the right match for a **hover preview**
anchored to a "?"; the **`FeaturePopupWindow`** pattern is the right match for a **click-to-open**
larger help panel.

Shared theme tokens (all `DynamicResource`, mod-overridable): `PinkBrush`, `SurfaceBgBrush`,
`DarkerBgBrush`, `ElevatedSurfaceBrush`, `GlassBorderBrush`, `TransparentPinkBrush`, `DangerBrush`
(defined in `Resources\Theme\Brushes.xaml` / `Colors.xaml`, merged in `App.xaml`).

### 1.6 Inline media playback (lightest looping-clip path)

| File | Tech | Looping? | Muted? |
|------|------|----------|--------|
| `Services\VideoService.cs` (~3146 lines) | **LibVLCSharp** (`VideoView` + `MediaPlayer`); `MediaElement` only as codec fallback | No (ends at `EndReached`) | `mediaPlayer.Mute = true` available |
| `MiniPlayerWindow.xaml(.cs)` (~366 lines) | LibVLC `VideoView` **+ XamlAnimatedGif** | **Yes** — `EndReached` re-`Play` (`:109`); GIF `RepeatBehavior.Forever` (`:149`) | **Yes** — `Mute = true` (`:92`) | 
| `Services\DualMonitorVideoService.cs` | LibVLC memory-render to `WriteableBitmap` | No | — |
| `Services\Deeper\*` / `EnhancementPlayerWindow` | WebView2 (HTML5 video) | — | heavyweight |

**Packages (`.csproj`):** `LibVLCSharp.WPF 3.8.5`, `VideoLAN.LibVLC.Windows 3.0.21`,
`XamlAnimatedGif 2.3.0`, `Microsoft.Web.WebView2 1.0.2535.41`. `MediaElement` is built-in but
codec-dependent (fails on Windows N/KN), so it's a fallback, not the primary path.

**Lightest reusable mechanism — two options:**
1. **LibVLC `VideoView`** for `.mp4`/`.webm`: copy `MiniPlayerWindow`'s ~40-line load + loop +
   mute + dispose pattern, drop the seek/transport UI. Codec-independent. Reuses the
   already-initialized shared LibVLC instance (`VideoService` preloads it on a background thread).
2. **GIF via XamlAnimatedGif** for the very smallest clips: `<Image>` +
   `AnimationBehavior.SetSourceUri/SetAutoStart/SetRepeatBehavior(Forever)` — ~10 lines, no codec,
   no audio to mute. Already used in `MiniPlayerWindow`, `AvatarTubeWindow`, GazeMinigame.

> ⚠️ LibVLC `EndReached` fires on a native VLC thread — the loop **must** re-dispatch to the WPF
> dispatcher before `Stop()/Play()` (see `MiniPlayerWindow.xaml.cs:109`). Getting this wrong
> deadlocks/crashes; copy the existing handler verbatim.

### 1.7 Bundled vs downloaded assets (where tutorial clips ship)

The `.csproj` (`ConditioningControlPanel.csproj`) ships assets two ways:

- **Embedded WPF `<Resource>`** (in the `.exe`, addressed via `pack://application:,,,/Resources/...`):
  images, achievement/skill/feature icons, Twemoji SVGs (`.csproj:161–245`).
- **Disk-copied `<Content CopyToOutputDirectory>`** (next to the exe, addressed via
  `AppDomain.CurrentDomain.BaseDirectory`): `Resources\sounds\**`, `Resources\sub_audio\**`,
  `Resources\AwarenessPresets\**`, `Resources\DeeperDemos\**`, `Resources\Models\**`, `Spirals\**`
  (`.csproj:272–308`), plus a post-publish `CopyContentAfterPublish` target (`.csproj:327–341`) to
  survive single-file publish (`ExcludeFromSingleFile=true`).

**Video must go the disk-copied route** (LibVLC/MediaElement open files by path, not pack URIs).
A new `Resources\tutorial_videos\` folder added to the disk-copied ItemGroup is the exact pattern
the sounds/Deeper demos already use:
```xml
<Content Include="Resources\tutorial_videos\**\*">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
  <ExcludeFromSingleFile>true</ExcludeFromSingleFile>
</Content>
```
Loaded at runtime via:
```csharp
var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "Resources", "tutorial_videos", "calibration_intro.mp4");
```
Optionally route through `Services\ModResourceResolver.cs` so mods can override clips (same as
sounds/icons resolve today).

**Line in the sand:** bundled = anything under `Resources\`/`assets\` shipped by the csproj.
User/downloaded = anything under `App.EffectiveAssetsPath` (default
`%APPDATA%\ConditioningControlPanel\assets`, e.g. user `videos`, `images`, content packs). Tutorial
clips are **bundled**, so they're version-locked to the build and never collide with user content.

### 1.8 External link handling (the "watch full tutorial" button)

Canonical "open in default browser" pattern (used widely):
```csharp
Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
```
- Hyperlink handler: `MainWindow.xaml.cs:~18207` `HandleHyperlinkClick` (wraps the above, sets
  `e.Handled = true`, logs failures).
- Also used in `AvatarTubeWindow.xaml.cs:~3147`, with an HTTPS-only guard in some call sites.
- `Services\BrowserService.cs:946` `Navigate(url)` is the **in-app WebView2** path (blocks
  `javascript:`/`file:`/`data:`, forces HTTPS) — use this only if a clip's full tutorial should
  open inside the embedded BambiCloud browser instead of the external browser.

**No central URL constants file.** Known hosts scattered in code: `https://cclabs.app/...`
(policies), `https://app.cclabs.app/catalogue`, `https://bambicloud.com/`, Discord/Patreon/Linktree.
The remote-control docs site is `https://cclabs.app/remote/`. The full-tutorial links should get a
single new constant (e.g. `TutorialBaseUrl = "https://cclabs.app/docs/tutorials"`, **URL TBD**),
ideally a static on `App` so all "?" surfaces share it.

### 1.9 Localization (caption strategy)

| File | Role |
|------|------|
| `Localization\LocalizationManager.cs` | Singleton. `Get(key)`, indexer `this[key]`. Fallback chain: active lang → English → key. Hot-swap via `SetLanguage` firing `PropertyChanged("Item[]")` so all bindings re-evaluate live. |
| `Localization\LocExtension.cs` (`StrExtension`) | XAML markup ext: `{loc:Str key}` → OneWay binding to `LocalizationManager.Instance[key]`. |
| `Localization\Loc.cs` | Code-behind: `Loc.Get(key)`, `Loc.GetF(key, args…)`. |
| `Localization\Languages\*.json` | 9 flat key→string files (en, zh-CN, ja, ko, es, pt-BR, fr, de, ru). `en.json` ≈ 3,189 keys; already ~247 `tooltip_*`/`*_help_*`/`tut_*` keys. |

Active language stored in `AppSettings.Language` (`Models\AppSettings.cs:61`), initialized at
`App.xaml.cs:~938` via `LocalizationManager.Instance.Initialize(...)`.

**Cheaper path (strongly recommended): one neutral muted clip + localized text overlay.**
Adding a caption in 9 languages = 9 JSON entries (e.g. `help_caption_calibration_intro`), bound via
`{loc:Str ...}`, hot-swapping for free with the existing language dropdown. Locale-specific clips
would mean 9× the storage, 9× re-recording per edit, and audio/video sync headaches — and the clips
are **muted** anyway, so there's no spoken track to localize. Text overlay wins decisively.

---

## 2. Proposed Design — "?" Video Help Preview

A two-tier affordance that reuses the existing patterns end-to-end:

### Tier 1 — Hover preview (lightweight, anchored Popup)
- Reuse the **`Popup` + `UserControl`** pattern (`FeatureSettingsPopup`/`SessionEditorWindow.xaml:252`).
- New `HelpHoverPreview` `UserControl`: a small bordered card (`SurfaceBgBrush` + `PinkBrush`, the
  shared theme tokens) containing:
  - a muted looping clip surface (LibVLC `VideoView` or GIF `Image`, copied from `MiniPlayerWindow`),
  - a localized caption `TextBlock` bound to `{loc:Str help_caption_<id>}`,
  - a "Watch full tutorial ↗" `Button`/`Hyperlink`.
- Host it in a `<Popup PlacementTarget="{Binding ElementName=BtnHelp}" Placement="Bottom"
  PopupAnimation="Fade" StaysOpen="False">`.
- **Lifecycle:** start playback on `IsOpen=true`, **stop + dispose the player on `IsOpen=false`**
  (don't leave a LibVLC player decoding behind a closed popup). On `MouseEnter` of the "?" open;
  on `MouseLeave` (with a small grace timer so the user can move into the popup) close.

### Tier 2 — Click to open (larger panel, reuse FeaturePopupWindow)
- For the "play the loop bigger + read the steps" case, reuse **`FeaturePopupWindow`** (or a trimmed
  `HelpVideoWindow` modeled on it): titlebar icon+title (from `HelpContent.Icon`/`Title`), the clip,
  the localized longer caption, and the external-link button.
- This naturally co-locates with the dashboard card → popup flow already in place.

### Data model extension
Add optional media fields to `HelpContent` (`Models\HelpContent.cs`) — purely additive, no breakage:
```csharp
public string? ClipFile { get; set; }       // e.g. "tutorial_videos/calibration_intro.mp4"
public string? CaptionKey { get; set; }      // e.g. "help_caption_calibration_intro" -> {loc:Str}
public string? FullTutorialUrl { get; set; } // absolute https URL, or null to hide the button
public bool HasClip => !string.IsNullOrWhiteSpace(ClipFile);
```
Populate these on the existing entries in `HelpContentService` only for features that ship a clip.
`FeatureCard.RefreshHelpTooltip()` already hides the "?" when content is absent; mirror that so the
**video** affordance only appears when `HasClip` is true.

### Where each feature surface hooks in
| Feature | File | Hook | Notes |
|---------|------|------|-------|
| Dashboard tiles (incl. Flash, Video, Subliminal, mosaic features) | `Features\FeatureCard.xaml.cs` + `*FeatureControl.xaml` | **`BtnHelp` already exists**, click-isolated (`:221`). Extend its click to open Tier-1/2 video. | Single change here lights up *every* dashboard feature at once. |
| Timeline editor | `SessionEditorWindow.xaml:46` | **"?" already in titlebar** (`tooltip_how_to_use_the_session_editor`). Repoint it / add video. | Already wired for help. |
| Eye-tracking calibration | `WebcamCalibrationWindow.xaml:52`, `WebcamQuickRecalWindow`, `WebcamGazeTrackerWindow` | **No "?" yet.** Add a small `HelpButtonStyle` button in the top status `StackPanel`. | Full-screen UI — keep the affordance subtle/corner. `HelpContentService` already has `WebcamGames`/`GazeMinigame`/`FocusGaze`/`BlinkTrainer`. |
| Mosaic setup ("Velvet" dashboard grid) | `MainWindow.xaml:~2582` `VelvetFeatureGrid` | **No grid-level header/"?"** (only per-card). Add a section header row with one "?" for the grid concept. | Per-card help already covers individual features. |
| Rule/trigger editor (Keyword Triggers) | `MainWindow.xaml:~6848` header; `HelpBtnKeywordTriggers` | **"?" already present** (`HelpButtonStyle`, Tag `KeywordTriggers`). Extend to video. | Secondary: `AttentionTargetEditorDialog.xaml:20` title has **no "?"** — add one alongside the title. |
| Mod Creator | `ModCreatorWindow.xaml:140` | **"?" already in titlebar** (`tooltip_mod_creator_tutorial`). Extend to video. | `HelpContentService` has `Modding`. |

**Net:** of the six surfaces, four already have a "?" to extend; only calibration and the
mosaic-grid-as-a-whole need a new button (both trivially using `HelpButtonStyle`).

### Localization & assets
- Ship clips under `Resources\tutorial_videos\` via the disk-copied `<Content>` ItemGroup (§1.7).
- One neutral muted clip per feature; caption text per language as `help_caption_<id>` keys in the
  9 JSON files (§1.9). No locale-specific clips.

### External link
- One `App.TutorialBaseUrl` constant (URL TBD with the website owner); the button calls the existing
  `Process.Start(new ProcessStartInfo(url){UseShellExecute=true})` pattern (§1.8). Optionally route to
  the in-app WebView2 via `BrowserService.Navigate` if the docs should open inside the app.

### First-run / "seen" state (optional, for later dismissable hints)
- Follow the `AppSettings` + `SettingsService` pattern. Add:
  ```csharp
  [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
  public HashSet<string> SeenHelpTips { get => _seenHelpTips; set { _seenHelpTips = value ?? new(); OnPropertyChanged(); } }
  ```
  Check `!Current.SeenHelpTips.Contains(id)` to auto-pulse a first-run "?" ; `Add(id)` +
  `App.Settings.Save()` on dismiss. Persists to `settings.json` automatically (mirrors
  `DismissedNotificationKeys`, `Welcomed`, `LastSeenVersion`). `TutorialService` currently persists
  nothing, so this is the right home.

---

## 3. Reuse Inventory (build vs reuse)

**Reuse as-is / copy:**
- "?" button visuals: `HelpButtonStyle` (`MainWindow.xaml:826`) and `FeatureCard.BtnHelp`.
- Rich tooltip: `HelpTooltipBuilder.Build(...)` (keep for text-only topics).
- Looping muted player: `MiniPlayerWindow` load/loop/mute/dispose (`:92`, `:109`, `:149`, `:305`).
- Popup shells: `FeatureSettingsPopup`+`Popup` (hover), `FeaturePopupWindow` (click).
- Theme tokens: `Resources\Theme\Brushes.xaml`.
- Link open: `Process.Start(... UseShellExecute=true)`.
- Localization: `{loc:Str}` + JSON.
- Persistence: `AppSettings`/`SettingsService`.

**Net-new (small):**
- `HelpHoverPreview` UserControl (clip surface + caption + link), ~80–120 lines.
- Optional `HelpVideoWindow` (or reuse `FeaturePopupWindow` directly).
- `HelpContent` media fields + populating clip entries in `HelpContentService`.
- Extend `FeatureCard.BtnHelp` click + 2 new "?" buttons (calibration, mosaic header).
- `Resources\tutorial_videos\` csproj ItemGroup + the clips themselves.
- `help_caption_*` keys × 9 languages.
- `App.TutorialBaseUrl` constant.

---

## 4. Open Questions & Assumptions

1. **Hover vs click?** Hover (Tier 1) is lighter but full-screen calibration windows and the loop's
   resource cost argue for **click-to-open** there. Assumption: support both, default dashboard/editor
   "?" to hover-preview, calibration to click.
2. **Clip format & encoding budget.** mp4 (LibVLC, codec-independent, audio-capable-but-muted) vs GIF
   (tiny, no codec, no audio). Assumption: short low-res **mp4/webm via LibVLC** for fidelity; GIF for
   the absolute smallest. Need a per-clip size budget (these ship in every build/installer).
3. **Full-tutorial URL scheme.** Is there a per-feature docs page (`/docs/tutorials/calibration`) or
   one index page? Need the real base URL and slug convention from the website owner. (`cclabs.app`
   docs path is assumed, **not confirmed in code**.)
4. **External vs in-app browser** for the link — default OS browser (`Process.Start`) vs embedded
   WebView2 (`BrowserService.Navigate`). Assumption: external browser (matches most existing links).
5. **Caption styling** — overlaid on the clip vs below it. Overlay risks clipping long translations
   (German/Russian run long); below-the-clip is safer. Assumption: caption **below** the clip.
6. **Which features actually get a clip first?** The six named surfaces, or only the genuinely complex
   ones (timeline, calibration, mod creator, trigger editor)? Assumption: start with those four.
7. **Mod overrides for clips** — should `.ccpmod`s be able to replace tutorial clips
   (`ModResourceResolver`)? Probably no for v1; flag for later.
8. **Lab-gated features** — calibration/gaze live under the Lab tab; confirm the "?" should appear
   even when the feature is locked/behind Patreon gating.

---

## 5. Architecture Friction (things that fight this design)

1. **LibVLC threading.** `EndReached` fires on a native thread; the loop and disposal **must**
   marshal to the WPF dispatcher (`MiniPlayerWindow.xaml.cs:109`). Naïve looping will deadlock/crash.
   Mitigate by copying the proven handler verbatim.
2. **LibVLC init cost / lifetime.** The shared LibVLC instance is preloaded on a background thread by
   `VideoService`; spinning up many `VideoView`s for many "?" popups could be heavy. Mitigate: one
   reusable player, create on open / dispose on close, never keep a hidden looping player alive.
3. **Bundled clips inflate the installer.** Disk-copied content is excluded from single-file and
   copied verbatim into the installer (Inno Setup). Several clips × every release = real size growth.
   Mitigate: aggressive low-res/short loops, GIF where acceptable, strict per-clip budget.
4. **No central URL/config home.** Links are scattered; adding yet another inline URL repeats the
   anti-pattern. Mitigate: a single `App.TutorialBaseUrl` (or a small `HelpLinks` static).
5. **`HelpContent` is hand-authored C# in a 1,200-line service.** Adding media to ~55 entries grows
   it further; there's no data-file for help content. Acceptable for now, but note it doesn't scale to
   localized rich help bodies (only the *captions* are localized via JSON; the tooltip
   `WhatItDoes`/`Tips`/`HowItWorks` text is currently **English-only in C#** — pre-existing gap, not
   introduced here, but worth flagging if help should be fully localized).
6. **Two parallel "help" systems.** The coach-mark `TutorialService` and the `HelpContentService`
   tooltips already coexist. Adding a third (video popups) risks confusion. Mitigate: anchor the new
   feature firmly on `HelpContentService`/`HelpContent` (extend it), and treat `TutorialService` as
   out of scope.
7. **Full-screen calibration windows** have minimal chrome and `ShowActivated=False`/topmost
   behaviors; an anchored `Popup` may not layer cleanly over them. Mitigate: use click-to-open
   `Window` (Tier 2) there rather than an anchored `Popup`.
8. **MediaElement fallback gap.** If LibVLC ever fails to init, the `MediaElement` fallback needs
   Windows codecs (absent on N/KN editions) and has no built-in loop. A clip-only feature should fail
   *soft* (hide the video, keep caption + link) rather than error.

---

## 6. Key File Index (quick jump)

- Help content: `Models\HelpContent.cs`, `Services\HelpContentService.cs`, `Services\HelpTooltipBuilder.cs`
- "?" affordances: `MainWindow.xaml:826` (`HelpButtonStyle`), `MainWindow.xaml.cs:16997` (`SetHelpContent`), `Features\FeatureCard.xaml.cs:38/175/221`
- Card→popup flow: `MainWindow.xaml:~2582` (`VelvetFeatureGrid`), `Features\FeaturePopupWindow.xaml`, `Features\*FeatureControl.xaml`
- Looping player: `MiniPlayerWindow.xaml(.cs)` (`:92/:109/:149/:305`), `Services\VideoService.cs`
- Popup pattern: `SessionEditorWindow.xaml:252` + `FeatureSettingsPopup.xaml(.cs)`
- Coach-mark tour (out of scope ref): `Services\TutorialService.cs`, `TutorialOverlay.xaml(.cs)`, `Models\TutorialStep.cs`, `Services\TutorialEventBus.cs`
- Assets: `ConditioningControlPanel.csproj:161–341`, `Services\ModResourceResolver.cs`
- Links: `MainWindow.xaml.cs:~18207` (`HandleHyperlinkClick`), `Services\BrowserService.cs:946`
- Localization: `Localization\LocalizationManager.cs`, `Localization\LocExtension.cs`, `Localization\Loc.cs`, `Localization\Languages\*.json`
- Persistence: `Models\AppSettings.cs` (`Welcomed:157`, `DismissedNotificationKeys:1388`), `Services\SettingsService.cs`

### Feature-surface anchor lines
- Timeline editor "?": `SessionEditorWindow.xaml:46`
- Calibration header (no "?" yet): `WebcamCalibrationWindow.xaml:52`
- Mosaic grid (per-card "?" only): `MainWindow.xaml:~2582`
- Keyword/trigger "?": `MainWindow.xaml:~6856`; `AttentionTargetEditorDialog.xaml:20` (no "?")
- Mod Creator "?": `ModCreatorWindow.xaml:140`
