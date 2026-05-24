# Deeper UI Recon

Scope: read-only map of the existing Deeper feature shell (hub, editor, player, dialogs).
No code changes, no redesign proposals. All paths are repo-relative to `ConditioningControlPanel/`.

---

## 1. File inventory

### Main hub (lives inside the main shell, not a separate window)
- `MainWindow.xaml` — Deeper-related XAML
  - Tab-strip button: lines 1692–1707 (`BtnDeeper`, uses `TabButtonDeeper` / `TabButtonDeeperActive` styles defined lines 158–204).
  - Settings-tab section: lines 2846–2900 (`DeeperSection` border with `ChkEnableDeeper` toggle).
  - Browser-tab badge: lines 2962–2975 (`DeeperBrowserBadge`, `TxtDeeperBrowserBadge`) shown when an enhancement is bound to the embedded Browser tab.
  - Browser-tab "Enhance if possible" toggle: line 3018ff.
  - Deeper tab content: lines 8488–8690 (`DeeperTab` ContentControl, header / pitch / first-run welcome card / Library + Recent two-column grid).
- `MainWindow.xaml.cs` — Deeper-related code-behind
  - Tab activation: `BtnDeeper_Click` line 2277.
  - Welcome card buttons: `BtnDeeperWelcomeTour_Click` 2306, `BtnDeeperWelcomeDemo_Click` 2312, `BtnDeeperWelcomeDismiss_Click` 2318.
  - Header buttons: `BtnDeeperTutorial_Click` 2323, `BtnDeeperNewEnhancement_Click` 2418, `BtnDeeperOpenPlayer_Click` 2429, `BtnDeeperOpenLibraryFolder_Click` 2950.
  - Settings toggle: `ChkEnableDeeper_Changed` 2363.
  - List builders: `RefreshDeeperLibraryUI` 2593, `BuildDeeperEmptyHint` 2626, `BuildDeeperLibraryRow` 2638 (~120 lines), `BuildDeeperAutoTagsRow` 2769, `BuildDeeperMediaLine` 2815, `BuildDeeperRecentRow` 2883.
  - Browser-bind glue: `OnDeeperBrowserBound` 2445, `OnDeeperBrowserUnbound` 2460, `ToggleEnhanceIfPossible_Changed` 2473, `OnBrowserEnhanceMatchChanged` 2493.
  - Routing into editor/player: `OpenDeeperEditor` 2517, `OpenInDeeperPlayer` 2534, `OpenInDeeperEditorForMedia` 2551, `OpenDeeperFile` 2931, `HandlePendingFileOpen` 2572.
  - Library mutation glue: `OnDeeperLibraryChanged` 2579, `DeleteDeeperLibraryEntry`, `SubmitDeeperLibraryEntryAsync`, `IsCatalogueEligible` (referenced 2683/2698/2720, in same file).

### Editor window
- `Views/Deeper/DeeperEditorWindow.xaml` (1035 lines) — Window + Resources + Grid layout.
- `Views/Deeper/DeeperEditorWindow.xaml.cs` (4540 lines) — main code-behind: state, playback init, timeline, regions, haptic events, rule editor, save/load.
- `Views/Deeper/DeeperEditorWindow.Unified.cs` (894 lines, partial) — context menu, hero buttons, HypnoTube auto-fill, Creator lock toggle, non-haptic effect editor panels, `RebuildEffectVisuals` / `RebuildRuleVisuals`, `EffectColors` palette.
- `Views/Deeper/DeeperEditorWindow.ItemsList.cs` (413 lines, partial) — right-panel "Rules & Effects" overview: collapsible list, per-row delete bin.
- `Views/Deeper/DeeperEditorWindow.MultiSelect.cs` (737 lines, partial) — multi-select set, rubber-band drag-select, copy / cut / paste / select-all on timeline.

### Player window
- `Views/Deeper/EnhancementPlayerWindow.xaml` (265 lines).
- `Views/Deeper/EnhancementPlayerWindow.xaml.cs` (1612 lines).

### Dialogs
- `Views/Deeper/NewEnhancementDialog.xaml` (122 lines) + `.xaml.cs` (219 lines) — invoked by `BtnDeeperNewEnhancement_Click`.
- `Views/Deeper/UrlPromptDialog.xaml` (39 lines) + `.xaml.cs` (51 lines) — invoked from editor "Change media → URL" and player "Load URL".
- `Views/Deeper/GazePickerWindow.xaml` (84 lines) + `.xaml.cs` (280 lines) — transparent topmost picker invoked by editor when picking a `GazeTarget` / `GazeAvoid` rect.

### Models (read by editor / player / hub list builders)
- `Models/Deeper/Enhancement.cs` — top-level container.
- `Models/Deeper/EnhancementRule.cs`.
- `Models/Deeper/EnhancementTrigger.cs` (8 concrete triggers + `NeverFiringTrigger`).
- `Models/Deeper/EnhancementAction.cs` (`Seek`, `LoopRegion`, `Pause`, `PlayAudio`, `TriggerHaptic`, `TriggerEffect`, `ScreenShake`, `SetIntensity`, `NoOp`).
- `Models/Deeper/TimelineItem.cs` (unified item; `TimelineItemKind.Effect|Rule`; `EffectTypes`, `OverlayKinds`, `EffectActivation`).
- `Models/Deeper/HapticTrack.cs` (`HapticTrack`, `HapticEvent`, `IHapticPatternTarget`).
- `Models/Deeper/StockHapticPatterns.cs`.

### Services touched by the UI (read but not modified)
- `Services/Deeper/EnhancementLibrary.cs` (referenced as `App.EnhancementLibrary` from MainWindow).
- `Services/Deeper/EnhancementHostService.cs` (`App.DeeperHost`).
- `Services/Deeper/EnhancementAudioPlayer.cs` (`App.DeeperPlayer`).
- `Services/Deeper/EnhancementSerializer.cs` (used by `BuildDeeperRecentRow` to read media line of a recent file).
- `Services/Deeper/EnhancementAutoTagger.cs` (`TagHaptics`, `TagWebcam` constants used for hub chip glyphs at `MainWindow.xaml.cs:2785-2787`).
- `Services/Deeper/BrowserEnhanceBridge` / `BrowserVideoTimeSource` (browser-tab integration).
- `Services/Deeper/HtMetadataFetcher` (HypnoTube metadata auto-fill from the editor).

### Styles, resources, theme — what Deeper relies on
- `App.xaml` merges `Resources/Theme/Colors.xaml` and `Resources/Theme/Brushes.xaml`.
- `Resources/Theme/Colors.xaml:63-66` — `DeeperAccent` `#FF7B5CFF`, `DeeperAccentSoft` `#FFB7A5FF`, `DeeperAccentTransparent20` `#207B5CFF`, `DeeperAccentTransparent40` `#407B5CFF`.
- `Resources/Theme/Brushes.xaml:63-66` — matching SolidColorBrush keys (`DeeperAccentBrush`, `DeeperAccentSoftBrush`, `DeeperAccentTransparent20Brush`, `DeeperAccentTransparent40Brush`).
- Generic theme brushes also used: `PanelBgBrush`, `DarkerBgBrush`, `SurfaceBgBrush`, `TextLightBrush`, `TextMutedBrush`, `TextDimBrush`, `PanelAccentBrush`, `GlassBorderBrush`, `DangerBrush` (all from `Brushes.xaml`).

### Shared resources Deeper depends on — DO NOT BREAK
- `TabButtonDeeper` / `TabButtonDeeperActive` styles at `MainWindow.xaml:158-204` — only Deeper uses them, but they `BasedOn` `TabButton` / `TabButtonActive` which the whole nav strip uses. Editing the base style affects every tab.
- `AnimatedTabBackground` template (Deeper tab uses it via `Template="{StaticResource AnimatedTabBackground}"` line 8489) — shared with every other tab.
- `SectionHeader`, `SettingLabel`, `ToggleStyle` styles used in the Settings "Deeper Section" — shared with every other settings group.
- `WindowChromeHelper.ApplyDarkTitleBar` / `RestoreOwnerOnClose` are called in the editor and player constructors — shared util across all CCP windows.
- The Deeper editor's `Window.Resources` (`EditorMenuItem`, `TimelineCtxMenuItem`, `TimelineCtxMenu`, `EditorTextBox`, `EditorLabel`, `EditorComboBox`, `EditorComboBoxItem` at `DeeperEditorWindow.xaml:14-202`) are window-scoped and not shared.
- `TutorialEventBus` / `TutorialOverlay` integration in `DeeperEditorWindow.xaml.cs:198,205-258,287-300` — shared with the global tutorial system.

---

## 2. Visual hierarchy per surface

### 2.1 Hub (`MainWindow.xaml:8488-8690`, `DeeperTab` ContentControl on `Grid.Row="4"` of the main tab area)

```
ContentControl  DeeperTab  Template=AnimatedTabBackground  Visibility=Collapsed
└─ Grid  Margin=28,20  MaxWidth=1080  HorizontalAlignment=Center
   ├─ Grid.RowDefinitions
   │  ├─ Row 0  Height=Auto    (Header)
   │  ├─ Row 1  Height=Auto    (Pitch + BETA notice)
   │  ├─ Row 2  Height=Auto    (DeeperWelcomeCard, first-run only)
   │  └─ Row 3  Height=*       (Library + Recent two-column)
   ├─ Grid  Row 0  Margin=0,0,0,18
   │  ├─ Col 0  Auto  TextBlock 🌊  FontSize=34
   │  ├─ Col 1  *     StackPanel
   │  │  ├─ StackPanel Horizontal
   │  │  │  ├─ TextBlock deeper_empty_title  FontSize=22  Bold
   │  │  │  └─ Border  (BETA badge)  Padding=7,2
   │  │  └─ TextBlock deeper_empty_pitch  MaxWidth=700
   │  └─ Col 2  Auto  StackPanel Horizontal
   │     ├─ Button BtnDeeperTutorial      (transparent, outlined)
   │     ├─ Button BtnDeeperOpenPlayer    (transparent20 fill, outlined)
   │     └─ Button BtnDeeperNewEnhancement (solid accent, hover-soft, custom template)
   ├─ StackPanel  Row 1  Margin=0,0,0,20
   │  ├─ TextBlock deeper_empty_features  (one-line blurb)
   │  └─ Border CornerRadius=6 Padding=10,7  Background=Transparent20  Border=Transparent40
   │     └─ TextBlock deeper_beta_notice  (wrap)
   ├─ Border  Row 2  DeeperWelcomeCard  Visibility=Collapsed  CornerRadius=10  Padding=22,18  Margin=0,0,0,18
   │  └─ Grid
   │     ├─ Col 0  Auto  TextBlock ✨  FontSize=34
   │     └─ Col 1  *     StackPanel
   │        ├─ TextBlock  welcome title  FontSize=16  Bold
   │        ├─ TextBlock  welcome body
   │        └─ StackPanel Horizontal
   │           ├─ Button BtnDeeperWelcomeTour       (solid accent)
   │           ├─ Button BtnDeeperWelcomeDemo       (transparent20 fill)
   │           └─ Button BtnDeeperWelcomeDismiss    (transparent)
   └─ Grid  Row 3   (two-column: Library | gutter | Recent)
      ├─ Col 0  *
      ├─ Col 1  20  (fixed gutter)
      ├─ Col 2  *
      ├─ Border DeeperLibraryCard  Col 0  CornerRadius=10  Padding=18,14
      │  └─ DockPanel LastChildFill
      │     ├─ DockPanel  Dock=Top
      │     │  ├─ TextBlock  deeper_section_library  (Left)
      │     │  ├─ TextBlock  TxtDeeperLibraryCount   (Right)
      │     │  └─ Button    BtnDeeperOpenLibraryFolder 📁  22×20  (Right)
      │     └─ ScrollViewer  VerticalScrollBarVisibility=Auto
      │        └─ StackPanel DeeperLibraryList   (rows appended in code)
      └─ Border DeeperRecentCard  Col 2  CornerRadius=10  Padding=18,14
         └─ DockPanel LastChildFill
            ├─ TextBlock  deeper_section_recent   (Dock=Top)
            └─ ScrollViewer  VerticalScrollBarVisibility=Auto
               └─ StackPanel DeeperRecentList
```

Each library row (`BuildDeeperLibraryRow`, `MainWindow.xaml.cs:2638-2765`):
```
Border  CornerRadius=6  Padding=12,8  Margin=0,0,0,6   (click → OpenDeeperFile)
└─ StackPanel
   ├─ DockPanel  titleRow  LastChildFill=true
   │  ├─ TextBlock icon (🎬/🎵)   (Dock=Left)
   │  ├─ Button   🗑 deleteBtn    (Dock=Right)
   │  ├─ Button   📤 submitBtn    (Dock=Right, only if HT-eligible + auth token)
   │  └─ TextBlock entry.Name     (fills the LastChild slot)
   ├─ TextBlock "Creator: …"      (only if Creator set)
   ├─ DockPanel  BuildDeeperMediaLine  (icon + status dot + filename/host)
   ├─ TextBlock  filename basename
   └─ WrapPanel  auto-tag chips (only if any)
```

Recent row (`BuildDeeperRecentRow`, line 2883) is shorter — display name, optional media line, full path.

### 2.2 Editor window (`Views/Deeper/DeeperEditorWindow.xaml`, 864×1180, MinHeight=672, MinWidth=980)

```
Window  Background=DarkerBgBrush
└─ Grid                                          (Root layout)
   ├─ RowDefinitions
   │  ├─ Row 0  Height=Auto   (Menu / title strip)
   │  ├─ Row 1  Height=Auto   (Linked Files strip)
   │  ├─ Row 2  Height=*      (Main 2-column body)
   │  └─ Row 3  Height=Auto   (Validation / save strip)
   │
   ├─ Border  Row 0   (menu strip)  Padding=10,4  Background=PanelBg
   │  └─ DockPanel
   │     ├─ Menu  Dock=Left    →  "File" MenuItem (Save / Save As / Export / Close)
   │     ├─ TextBlock TxtTitle Dock=Left
   │     ├─ TextBlock TxtDirty (●) Dock=Left
   │     └─ Button   BtnEditorHelp 26×22  Dock=Right
   │
   ├─ Border  Row 1   (linked files strip)  Padding=14,8
   │  └─ Grid   (two equal columns)
   │     ├─ Col 0 *  (project JSON)
   │     │  └─ Grid 4-col / 2-row
   │     │     ├─ Row 0:  📄 | TxtLinkedJsonName (*)  | 📁 BtnLinkedJsonOpenFolder | BtnLinkedJsonSwap
   │     │     └─ Row 1:  TxtLinkedJsonPath (spans 1-3)
   │     ├─ Col 1 20  (gutter)
   │     └─ Col 2 *  (linked media)
   │        └─ Grid 5-col / 2-row
   │           ├─ Row 0:  TxtLinkedMediaIcon | TxtLinkedMediaStatus ✓ | TxtLinkedMediaName (*) | BtnLinkedMediaChange | BtnLinkedMediaClear
   │           └─ Row 1:  TxtLinkedMediaPath  (spans 1-4)
   │     └─ Popup MediaChangePopup  (file vs URL chooser, attached to BtnLinkedMediaChange)
   │
   ├─ Grid  Row 2   (main body)
   │  ├─ ColumnDefinitions
   │  │  ├─ Col 0 *            (preview + timeline)
   │  │  └─ Col 1 320 (fixed)  (right side panel)
   │  │
   │  ├─ Grid  Col 0
   │  │  ├─ RowDefinitions
   │  │  │  ├─ Row 0 *            (preview pane fills remaining space)
   │  │  │  └─ Row 1 160 (fixed)  (timeline — FIXED HEIGHT)
   │  │  ├─ Border  Row 0  Background=#0A0A14  Margin=10,10,10,5  (preview pane)
   │  │  │  └─ Grid PreviewHost
   │  │  │     ├─ vlc:VideoView VideoPreview  (Collapsed unless local video)
   │  │  │     ├─ wv2:WebView2 BrowserPreview (Collapsed unless remote URL)
   │  │  │     ├─ StackPanel Horizontal zoom pills (BtnPreviewZoomOut/In, only with WebView visible)
   │  │  │     ├─ Canvas WaveformCanvas  (audio mode)  → Path WaveformPath
   │  │  │     └─ StackPanel PreviewPlaceholder  (icon + title + source text)
   │  │  └─ Border  Row 1  CornerRadius=6  Margin=10,5,10,10  Padding=10,8  (timeline)
   │  │     └─ Grid
   │  │        ├─ RowDefinitions Auto / *
   │  │        ├─ DockPanel Row 0  Transport row LastChildFill=true
   │  │        │  ├─ Btn BtnPlayPause   Dock=Left  34×28
   │  │        │  ├─ TxtCurrentTime / "/" / TxtTotalTime   Dock=Left
   │  │        │  ├─ Btn BtnPreview        Dock=Right
   │  │        │  ├─ Btn BtnAddRuleHero    Dock=Right
   │  │        │  ├─ Btn BtnAddEffectHero  Dock=Right
   │  │        │  ├─ TextBlock scrub hint  Dock=Right
   │  │        │  └─ Btn BtnZoomOut / TxtZoomLevel / Btn BtnZoomIn  Dock=Right
   │  │        └─ ScrollViewer TimelineScroll  Row 1  Horizontal=Auto Vertical=Disabled
   │  │           └─ Canvas TimelineCanvas  Background=#15151F  Cursor=Hand
   │  │              ├─ Line PlayheadLine                                      (ZIndex=100)
   │  │              ├─ Rectangle[]  region visuals (added/removed in code)    (top half lane)
   │  │              ├─ Rectangle[]  haptic visuals  (added/removed in code)   (bottom half lane)
   │  │              ├─ Ellipse/Rectangle[] effect visuals (RebuildEffectVisuals, ZIndex=10)
   │  │              └─ Line + Polygon + Rectangle (rule pins, RebuildRuleVisuals, ZIndex 9-11)
   │  │              + ContextMenu TimelineCtxMenu   (right-click attaches)
   │  │
   │  └─ Border  Col 1  Background=PanelBg  BorderThickness=1,0,0,0  (right side panel)
   │     └─ ScrollViewer Vertical=Auto Padding=14,12
   │        └─ StackPanel    (everything stacks vertically — variable height)
   │           ├─ TextBlock "Metadata"  Bold accent
   │           ├─ Label + TextBox  TxtMetaName
   │           ├─ DockPanel Label + ToggleButton 🔒 BtnCreatorLockToggle
   │           ├─ TextBox  TxtMetaCreator
   │           ├─ Label + TextBox TxtMetaRemixer
   │           ├─ Label + TextBox TxtMetaDescription   Height=68 wrap
   │           ├─ Label + TextBox TxtMetaTags
   │           ├─ Label + TextBox TxtMetaLicense
   │           ├─ Separator
   │           ├─ StackPanel ItemsListSection  Margin=0,0,0,12
   │           │  ├─ ToggleButton ItemsListToggle  (Chevron / "Rules & Effects" / count)
   │           │  └─ Border ItemsListContent
   │           │     └─ Grid
   │           │        ├─ TextBlock TxtItemsListEmpty
   │           │        └─ ScrollViewer ItemsListScroll  MaxHeight=240
   │           │           └─ ItemsControl LstAllItems   (rows from BuildItemListRow)
   │           ├─ TextBlock "Selected Item"  Bold accent
   │           ├─ TextBlock SelectedPlaceholder  (italic, shows when nothing selected)
   │           ├─ StackPanel RegionEditor  Visibility=Collapsed
   │           │  ├─ Label + TxtRegionId  (read-only)
   │           │  ├─ Label + TxtRegionLabel
   │           │  ├─ Grid (Start / End side-by-side)
   │           │  ├─ Color row: swatch + TxtRegionColor + WrapPanel RegionColorSwatches (6 swatches)
   │           │  └─ Btn Delete  (Danger)
   │           ├─ StackPanel HapticEventEditor  Visibility=Collapsed
   │           │  ├─ Label + TxtHapticTrackId
   │           │  ├─ Grid Start / Duration
   │           │  ├─ Slider SliderHapticIntensity + TxtHapticIntensityValue
   │           │  ├─ ComboBox CmbHapticPattern
   │           │  ├─ StackPanel CurveEditorPanel  Visibility=Collapsed
   │           │  │  └─ Border + Canvas CurveCanvas Height=120
   │           │  └─ Grid: Btn Test (accent) / Btn Delete (danger)
   │           ├─ StackPanel RuleEditor  Visibility=Collapsed
   │           │  ├─ DockPanel: TxtRuleHeader + Delete button
   │           │  ├─ CheckBox ChkRuleEnabled
   │           │  ├─ Label + "?" help button + ComboBox CmbTriggerType
   │           │  ├─ StackPanel TriggerFields  (built in code — rect picker, time field, region id…)
   │           │  ├─ Label + "?" + ComboBox CmbActionType
   │           │  ├─ StackPanel ActionFields  (built in code)
   │           │  ├─ Label + "?" + ComboBox CmbRuleRegion
   │           │  ├─ Label + TxtRuleCooldown
   │           │  └─ StackPanel RuleRegionDetails  Visibility=Collapsed
   │           │     ├─ Separator
   │           │     ├─ TxtRuleBandLabel
   │           │     ├─ Grid Start / End  (read-only Consolas)
   │           │     └─ Color row: swatch + TxtRuleBandColor + WrapPanel RuleBandColorSwatches
   │           ├─ StackPanel FlashEffectEditor       Visibility=Collapsed
   │           ├─ StackPanel BubbleEffectEditor      Visibility=Collapsed
   │           ├─ StackPanel SubliminalEffectEditor  Visibility=Collapsed
   │           └─ StackPanel OverlayEffectEditor     Visibility=Collapsed
   │
   └─ Border  Row 3   (validation strip)  Padding=14,8
      └─ DockPanel LastChildFill=true
         ├─ Btn BtnEditorSave    Dock=Right (solid accent)
         ├─ Btn BtnEditorExport  Dock=Right (transparent20)
         └─ TxtValidationSummary  (fills LastChild)
```

Notes on the layout fight:
- Right side panel is fixed at `Width=320` (`DeeperEditorWindow.xaml:383`). The metadata block plus all the per-selection editors are siblings inside a single vertical `StackPanel`, so the `ItemsListSection` (rules+effects overview) is constrained between metadata above and the selected-item editor below. Only `ItemsListScroll` has its own `MaxHeight=240` (line 649); the rest stack on whatever height the StackPanel produces and rely on the outer `ScrollViewer` (line 556) to scroll the entire side panel.
- Timeline is hard-pinned at `Height=160` (`DeeperEditorWindow.xaml:390`). Region lane is `h/2` (top half); haptic lane is `h/2 + 2` … `h - 2` (bottom half); effect dots at `y = h - 18`; effect segments at `y = h - 22`; rule pins span the full height. With h=160, that means region lane is ~80px tall, haptic lane ~78px, then effect dots and segments overlap the haptic lane at y≈138 and y≈142 (see `DeeperEditorWindow.Unified.cs:388-389,428-429`).

### 2.3 Player window (`Views/Deeper/EnhancementPlayerWindow.xaml`, 768×900, MinHeight=504, MinWidth=640)

```
Window  Background=DarkerBgBrush
└─ Grid Margin=18,16
   ├─ RowDefinitions
   │  ├─ Row 0  Auto   (header)
   │  ├─ Row 1  Auto   (audio file row — audio mode only)
   │  ├─ Row 2  Auto   (enhancement file row)
   │  ├─ Row 3  *      (media pane: waveform OR webview)
   │  ├─ Row 4  Auto   (transport)
   │  ├─ Row 5  Auto   (status)
   │  └─ Row 6  Auto   (event log)
   ├─ StackPanel Row 0  Horizontal
   │  ├─ TextBlock 🌊 22pt
   │  └─ TextBlock deeper_player_title  18pt SemiBold
   ├─ Grid Row 1 AudioFileRow  3 cols (label 100 | path * | btn Auto)
   ├─ Grid Row 2  6 cols / 2 rows
   │  Row 0: Label | TxtEnhPath | Pick | Load URL | Create New | Unload
   │  Row 1: TxtEnhSource (badge: From library / Embedded / Sidecar / Url / Manual)
   ├─ Grid Row 3
   │  ├─ Border AudioPane  Height=80  Background=#15151F
   │  │  └─ Canvas WaveformCanvas  →  Path WaveformPath  +  Line PlayheadLine
   │  └─ Border VideoPane  Visibility=Collapsed  Background=Black
   │     └─ Grid
   │        ├─ wv2:WebView2 VideoBrowser
   │        ├─ TextBlock TxtVideoStatus  (centered, "loading…", IsHitTestVisible=False)
   │        └─ StackPanel zoom pills  (BtnZoomOut / BtnZoomIn, ZIndex=10)
   ├─ DockPanel Row 4 LastChildFill=true  (transport)
   │  ├─ Btn BtnPlayPause ▶  Dock=Left  38×32 accent fill
   │  ├─ Btn Stop ⏹         Dock=Left  32×32
   │  ├─ TxtCurrent / "/" / TxtTotal  Dock=Left
   │  ├─ StackPanel VolumePanel       Dock=Right  (🔊 + SliderVolume 120px)
   │  ├─ Btn BtnEyeTracking           Dock=Right
   │  └─ Btn BtnPictureInPicture      Dock=Right  Visibility=Collapsed
   ├─ Border Row 5  CornerRadius=4  Padding=12,10
   │  └─ StackPanel
   │     ├─ TxtStatus      (engine state line)
   │     └─ TxtEnhMetadata (creator / tags one-liner)
   └─ Border Row 6  CornerRadius=4  Padding=12,8  Margin=0,8,0,0
      └─ StackPanel
         ├─ TextBlock "Event log"  Bold accent
         └─ ScrollViewer MaxHeight=120
            └─ ItemsControl LstEvents
               DataTemplate → TextBlock {Binding} Consolas 10pt NoWrap
```

### 2.4 NewEnhancementDialog (`Views/Deeper/NewEnhancementDialog.xaml`, 410×540, WindowStyle=None, no resize)

```
Border  BorderBrush=DeeperAccentBrush  Thickness=1  Radius=0
└─ Grid Margin=22
   ├─ 9 rows (Auto × 7, *, Auto)
   ├─ Row 0: TextBlock title  18pt Bold accent
   ├─ Row 1: TextBlock subtitle muted
   ├─ Row 2: Label "Media type"
   ├─ Row 3: StackPanel Horizontal  RbVideo (default) + RbAudio
   ├─ Row 4: Label "Source"
   ├─ Row 5: Grid 2-col  TxtSource (*) + BtnBrowse
   ├─ Row 6: TextBlock source hint
   ├─ Row 7: StackPanel
   │  ├─ TextBlock tutorial header
   │  └─ StackPanel Horizontal
   │     ├─ BtnLocalVideoTutorial  (transparent outline)
   │     ├─ BtnLocalAudioTutorial  (transparent outline)
   │     └─ BtnTryHypnoTubeTutorial (filled20 + accent outline)
   └─ Row 8: StackPanel Horizontal Right-aligned
      ├─ Btn Cancel  (PanelAccent fill)
      └─ Btn BtnCreate  (solid accent)
```

### 2.5 UrlPromptDialog (190×500, no resize)

```
StackPanel Margin=20,16
├─ TextBlock label
├─ TextBox TxtUrl  Background=White Foreground=Black
├─ TextBlock TxtError  Red  Visibility=Collapsed
└─ DockPanel LastChildFill=false
   ├─ Btn "Load" Dock=Right  IsDefault=true  (solid accent)
   └─ Btn Cancel  Dock=Right  IsCancel=true   (transparent outline)
```

### 2.6 GazePickerWindow (transparent, topmost, fills the editor's PreviewHost rect)

```
Window  WindowStyle=None  AllowsTransparency=true  Topmost=true  Background=Transparent
└─ Grid
   ├─ Border  Background=#40000000   (semi-opaque backdrop tint)
   ├─ Canvas PickCanvas Background=Transparent  (drag area, full size)
   │  ├─ Rectangle RectShape  Stroke=#FF7B5CFF Fill=#357B5CFF  Visibility=Collapsed
   │  └─ Rectangle[] 8 handles HandleNW/N/NE/E/SE/S/SW/W  12×12 white fill, accent stroke
   └─ Border  Top center toolbar  Background=#E61C1C35  Border=#FF7B5CFF  Radius=6
      └─ DockPanel LastChildFill=false
         ├─ TextBlock TxtHint    Dock=Left
         ├─ TextBlock TxtCoords  Dock=Left  Consolas
         ├─ Btn Done    Dock=Right  filled accent
         └─ Btn Cancel  Dock=Right  outline
```

---

## 3. Control inventory per surface

### 3.1 Hub controls (Deeper tab inside MainWindow)

| Name | Type | Purpose / state | Handler |
| --- | --- | --- | --- |
| `BtnDeeper` | Button (tab strip) | Switches `DeeperTab.Visibility=Visible`, hides other tabs | `BtnDeeper_Click` (MainWindow.xaml.cs:2277) |
| `BtnDeeperTutorial` | Button | Launches the editor coachmark tutorial | `BtnDeeperTutorial_Click` (2323) |
| `BtnDeeperOpenPlayer` | Button | Opens `EnhancementPlayerWindow` empty | `BtnDeeperOpenPlayer_Click` (2429) |
| `BtnDeeperNewEnhancement` | Button (custom template) | Opens `NewEnhancementDialog`, then editor | `BtnDeeperNewEnhancement_Click` (2418) |
| `DeeperWelcomeCard` (Border) | First-run welcome card; collapsed once dismissed via setting | n/a | — |
| `BtnDeeperWelcomeTour` / `Demo` / `Dismiss` | Buttons | Tour, open bundled demo, dismiss card | `BtnDeeperWelcome*_Click` (2306/2312/2318) |
| `DeeperLibraryCard` (Border) | Library list container | n/a | — |
| `TxtDeeperLibraryCount` | TextBlock | "{n} enhancements" | set by `RefreshDeeperLibraryUI` |
| `BtnDeeperOpenLibraryFolder` | Button 📁 | Opens library folder in Explorer | `BtnDeeperOpenLibraryFolder_Click` (2950) |
| `DeeperLibraryList` | StackPanel | Code-populated rows (`BuildDeeperLibraryRow`) | — |
| `DeeperRecentList` | StackPanel | Code-populated recent rows (`BuildDeeperRecentRow`) | — |
| `ChkEnableDeeper` (Settings tab) | CheckBox | Enable feature globally | `ChkEnableDeeper_Changed` (2363) |
| `DeeperBrowserBadge` / `TxtDeeperBrowserBadge` (Browser tab) | Border + TextBlock | Active-enhancement badge above embedded browser | set by `OnDeeperBrowserBound/Unbound` |
| `ToggleEnhanceIfPossible` (Browser tab) | ToggleButton | Auto-enhance matching pages | `ToggleEnhanceIfPossible_Changed` |

Library row controls (built in `BuildDeeperLibraryRow`):
- Title text (entry.Name), media icon 🎬/🎵.
- 🗑 delete button.
- 📤 catalogue submit button (only shown if `IsCatalogueEligible`; disabled if no auth token).
- Auto-tag chips (📳 haptics, 📷 webcam) via `BuildDeeperAutoTagsRow`.
- Media-source line with status dot (✓ exists / ⚠ missing / 🌐 URL) via `BuildDeeperMediaLine`.

### 3.2 Editor controls

Menu strip (Row 0):

| Name | Type | Purpose | Handler |
| --- | --- | --- | --- |
| File menu | Menu/MenuItem | Save / Save As / Export Enhanced / Close | `MenuSave_Click` (3810), `MenuSaveAs_Click` (3820), `MenuExportEnhanced_Click` (3888), `MenuClose_Click` (3973) |
| `TxtTitle` | TextBlock | Project title (`enhancement.Metadata.Name` fallback to filename) | `UpdateTitle()` |
| `TxtDirty` (●) | TextBlock | Unsaved-changes glyph; Visible when `_isDirty` | `MarkDirty()` |
| `BtnEditorHelp` (?) | Button | Re-runs editor coachmarks | `BtnEditorHelp_Click` (289) |

Linked files strip (Row 1):

| Name | Purpose | Handler |
| --- | --- | --- |
| `TxtLinkedJsonName` / `TxtLinkedJsonPath` | Project file display | — |
| `BtnLinkedJsonOpenFolder` 📁 | Opens project folder | `BtnLinkedJsonOpenFolder_Click` (4045) |
| `BtnLinkedJsonSwap` | Swap to a different JSON | `BtnLinkedJsonSwap_Click` (4063) |
| `TxtLinkedMediaIcon` / `TxtLinkedMediaStatus` / `TxtLinkedMediaName` / `TxtLinkedMediaPath` | Media file display + ✓/⚠ status | — |
| `BtnLinkedMediaChange` + `MediaChangePopup` | Pops sub-menu for local vs URL | `BtnLinkedMediaChange_Click` (4095), `BtnChangeMediaLocal_Click` (4100), `BtnChangeMediaUrl_Click` (4116) |
| `BtnLinkedMediaClear` | Detach media | `BtnLinkedMediaClear_Click` (4220) |

Preview pane (Row 2 / Col 0 / Row 0): `VideoPreview` (LibVLC `VideoView`), `BrowserPreview` (WebView2), `WaveformCanvas` + `WaveformPath`, `PreviewPlaceholder` group (`TxtPlaceholderIcon`, `TxtPlaceholderTitle`, `TxtPlaceholderSource`), zoom pills `BtnPreviewZoomOut` / `BtnPreviewZoomIn`.

Timeline transport (Row 2 / Col 0 / Row 1 / Row 0):

| Name | Purpose | Handler |
| --- | --- | --- |
| `BtnPlayPause` | Toggles playback (text ▶ / ⏸) | `BtnPlayPause_Click` (1032) |
| `TxtCurrentTime` / `TxtTotalTime` | Playhead readout | `PlayheadTimer_Tick` |
| `BtnPreview` | Opens standalone Player window with this enhancement | `BtnPreview_Click` (3619) |
| `BtnAddRuleHero` | "+ Rule" at playhead (TimeReached default) | `BtnAddRuleHero_Click` (Unified.cs:129) |
| `BtnAddEffectHero` | "+ Effect" at playhead (defaults to Haptic 5s) | `BtnAddEffectHero_Click` (Unified.cs:122) |
| `BtnZoomOut` / `BtnZoomIn` / `TxtZoomLevel` | Horizontal zoom 1.0–16.0 (`SetZoom`) | `BtnZoomIn_Click` (1189), `BtnZoomOut_Click` (1190); also `TimelineScroll_PreviewMouseWheel` (1214) with Ctrl |

Timeline canvas:
- `TimelineCanvas` is a custom-built `Canvas` (no virtualization). Rules/regions/effects/haptics are dynamically added `Rectangle`/`Ellipse`/`Line`/`Polygon` shapes. Lists tracked: `_regionVisuals`, `_hapticVisuals`, `_effectVisuals`, `_ruleVisuals`. ContextMenu `TimelineCtxMenu` attached on right-click with audio-mode filtering (`ApplyAudioModeToContextMenu`).
- Handlers: `TimelineCanvas_MouseLeftButtonDown` (1225), `TimelineCanvas_MouseRightButtonDown` (Unified.cs:53), `TimelineCanvas_MouseMove` (1250), `TimelineCanvas_MouseLeftButtonUp` (1377), `TimelineCanvas_SizeChanged` (1118), `TimelineScroll_SizeChanged` (1131).

Right side panel — Metadata fields:

| Name | Bound to | Notes |
| --- | --- | --- |
| `TxtMetaName` | `enhancement.Metadata.Name` | `MetadataField_TextChanged` |
| `TxtMetaCreator` + `BtnCreatorLockToggle` (🔒) | `Metadata.Creator` + `_creatorLocked` | Lock prevents HT auto-fill overwrite |
| `TxtMetaRemixer` | `Metadata.Remixer` | hand-set by remixer |
| `TxtMetaDescription` | `Metadata.Description` | 68px multiline |
| `TxtMetaTags` | `Metadata.Tags` | comma-separated |
| `TxtMetaLicense` | `Metadata.License` | free text |

Items list section (rules+effects overview):

| Name | Purpose |
| --- | --- |
| `ItemsListToggle` (ToggleButton) | Expand/collapse |
| `ItemsListChevron` | ▼/▶ glyph |
| `TxtItemsListCount` | Total count display |
| `ItemsListContent` (Border) → `ItemsListScroll` (ScrollViewer, `MaxHeight=240`) → `LstAllItems` (ItemsControl) | Rows built by `BuildItemListRow` (DeeperEditorWindow.ItemsList.cs:174) — color swatch + type label + detail subtitle + time + 🗑 delete |
| `TxtItemsListEmpty` | Italic placeholder when no items |

Selected-item editor groups (visibility-toggled by `UpdateSelectedSidePanel` / `UpdateSelectedSidePanelForEffect`):

- `SelectedPlaceholder` (TextBlock, italic)
- `RegionEditor` (StackPanel): `TxtRegionId` (Consolas read-only), `TxtRegionLabel`, `TxtRegionStart` + `TxtRegionEnd`, `RegionColorSwatch` (Border) + `TxtRegionColor`, `RegionColorSwatches` (WrapPanel, 6 swatches), Delete button.
- `HapticEventEditor` (StackPanel): `TxtHapticTrackId`, `TxtHapticStart` + `TxtHapticDuration`, `SliderHapticIntensity` + `TxtHapticIntensityValue`, `CmbHapticPattern`, `CurveEditorPanel` (`CurveCanvas` 120px tall, `BtnResetCurve`), `BtnTestHaptic` (filled accent) + `BtnDeleteHaptic` (danger).
- `RuleEditor` (StackPanel): `TxtRuleHeader` + Delete button, `ChkRuleEnabled`, `CmbTriggerType` + `BtnTriggerHelp` ? + `TriggerFields` (code-built — e.g. rect picker, time field), `CmbActionType` + `BtnActionHelp` ? + `ActionFields`, `CmbRuleRegion` + `BtnRegionHelp` ?, `TxtRuleCooldown`, sub-panel `RuleRegionDetails` with `TxtRuleBandLabel`, `TxtRuleBandStart`/`TxtRuleBandEnd` (read-only), `RuleBandColorSwatch` + `TxtRuleBandColor` + `RuleBandColorSwatches`.
- `FlashEffectEditor` (StackPanel): `TxtFlashDuration` + Delete.
- `BubbleEffectEditor` (StackPanel): `TxtBubbleWindow`, `SliderBubbleIntensity`, Delete.
- `SubliminalEffectEditor` (StackPanel): `TxtSubliminalText`, `TxtSubliminalDuration`, Delete.
- `OverlayEffectEditor` (StackPanel): `CmbOverlayKind` (Pink Filter / Spiral / Brain Drain), `TxtOverlayDuration`, `SliderOverlayOpacity`, Delete.

Validation strip (Row 3): `TxtValidationSummary` (errors/warnings count), `BtnEditorExport` (transparent20), `BtnEditorSave` (solid accent).

Context menu (`TimelineCtxMenu`, right-click timeline): "Add Effect" submenu (Haptic / Flash / Bubble / Subliminal / Overlay), "Add Rule" submenu (TimeReached / RegionEntered / RegionExited / GazeTarget / GazeAvoid / AttentionLost / BlinkDetected / MouthOpen — last five hidden in audio mode by `ApplyAudioModeToContextMenu`).

### 3.3 Player controls

| Name | Purpose | Handler |
| --- | --- | --- |
| `TxtAudioPath` + `BtnPickAudio` | Audio-mode media picker | `BtnPickAudio_Click` (140) |
| `TxtEnhPath` + `BtnPickEnhancement` | Pick .ccpenh.json | `BtnPickEnhancement_Click` (210) |
| `BtnLoadUrl` | Opens `UrlPromptDialog`, loads remote enhancement | `BtnLoadUrl_Click` (224) |
| `BtnCreateNewEnhancement` | Hands current media to editor | `BtnCreateNewEnhancement_Click` (351) |
| `BtnUnloadEnhancement` | `_host.Unload()` | `BtnUnloadEnhancement_Click` (222) |
| `TxtEnhSource` | Discovery-source badge (From library / Embedded / Sidecar / Url / Manual) | set in `OnHostLoaded` |
| `AudioPane` → `WaveformCanvas` / `WaveformPath` / `PlayheadLine` | Waveform scrub | `WaveformCanvas_MouseLeftButtonDown` |
| `VideoPane` → `VideoBrowser` (WebView2) + `TxtVideoStatus` + zoom pills | Video preview | `BtnZoomIn_Click` (563) / `BtnZoomOut_Click` (564), `OnVideo*` lifecycle handlers |
| `BtnPlayPause` | Play / pause | `BtnPlayPause_Click` (428) |
| Stop ⏹ | Stop | `BtnStop_Click` (479) |
| `TxtCurrent` / `TxtTotal` | Time readout | `UiTimer_Tick` |
| `SliderVolume` (+ 🔊) | 0–100, default 80 | `SliderVolume_ValueChanged` |
| `BtnEyeTracking` | Toggle webcam tracking | `BtnEyeTracking_Click` (500) |
| `BtnPictureInPicture` | PiP for embedded video | `BtnPictureInPicture_Click` (585), Visibility=Collapsed by default |
| `TxtStatus` + `TxtEnhMetadata` | Engine status + creator/tags line | set by `OnHostLoaded` / `OnHostLoadFailed` |
| `LstEvents` (ItemsControl) bound to `_events` (ObservableCollection<string>, max 30) | Live log of dispatched effects/rules | `AppendEvent` (1531) called from `OnHostActionLogged` (1521) |

### 3.4 Dialog controls

NewEnhancementDialog: `RbVideo` / `RbAudio` (RadioButtons grouped `MediaType`), `TxtSource`, `BtnBrowse`, three tutorial buttons (`BtnLocalVideoTutorial`, `BtnLocalAudioTutorial`, `BtnTryHypnoTubeTutorial`), Cancel + `BtnCreate`.

UrlPromptDialog: `TxtUrl`, `TxtError`, "Load" (default), Cancel (cancel button).

GazePickerWindow: `PickCanvas`, `RectShape`, 8 named resize `Handle*` rectangles, `TxtHint`, `TxtCoords`, Done + Cancel.

---

## 4. Interaction map

### 4.1 Hub

- Click any `DeeperLibraryList` row → `OpenDeeperFile(entry.FilePath)` → `App.EnhancementLibrary.Open` → `OpenDeeperEditor(enhancement, path)` (MainWindow.xaml.cs:2517) opens a non-owned-modal `DeeperEditorWindow` and refreshes the hub list on close.
- 🗑 on a row → `DeleteDeeperLibraryEntry(entry)` with confirm.
- 📤 on a row (HT video + auth token) → `SubmitDeeperLibraryEntryAsync`.
- 📁 button → opens the library folder in Explorer.
- Recent-list row → same `OpenDeeperFile` path.
- Welcome card buttons mutate `App.Settings?.Current.HasSeenDeeperWelcome` (or similar) and collapse the card.
- Hub library auto-refreshes on `App.EnhancementLibrary.LibraryChanged` (MainWindow.xaml.cs:2579) only when the Deeper tab is visible.
- The header CTA buttons each open the relevant window directly; there is no in-tab editor or player surface.

### 4.2 Editor — rule add / edit / delete / select

- **Add via timeline right-click**: `TimelineCanvas_MouseRightButtonDown` (Unified.cs:53) captures `_rightClickSeconds` and seeks the playhead to the click, then opens `TimelineCtxMenu`. "Add Rule → …" calls `AddRuleAt(triggerType, _rightClickSeconds)` (Unified.cs:220). For non-`TimeReached` rules, this also creates a companion `Region` and wires `rule.RegionConstraint` + `Trigger.RegionId` to it. The new rule is `SelectRule`ed so its editor opens.
- **Add via hero button**: `BtnAddRuleHero_Click` (Unified.cs:129) → `AddRuleAt(TriggerTypes.TimeReached, _currentSeconds)`. Same path but always TimeReached.
- **Add via legacy `BtnAddRule_Click`** (DeeperEditorWindow.xaml.cs:2472): no XAML button currently references this name; appears to be a dead path left from the pre-unified design (defaults to `GazeTarget` for video, `TimeReached` for audio).
- **Select**: right-side `LstAllItems` (Rules & Effects overview) row click → `SelectRule(rule)` via `OnSelect` lambda set in `BuildItemListRow`. Timeline rule pins → wider transparent hit `Rectangle` (`BuildRulePin`, Unified.cs:865-883) → `SelectRule(rule)`. Band-style rules: clicking the band's region rect routes to `SelectRule(rule)` via `FindRuleByRegionConstraint` (xaml.cs:1837).
- **Edit**: `PopulateRuleEditor` (xaml.cs:2524) rebuilds `CmbTriggerType` + `CmbActionType` + `CmbRuleRegion` + `TriggerFields` + `ActionFields` + `PopulateRuleBandDetails`. Each field change calls `MarkDirty`, `ScheduleValidation`, possibly `RebuildRuleVisuals` / `RebuildRegionVisuals`.
- **Delete**: `BtnDeleteRule_Click` (xaml.cs:2494). Also `DeleteRuleViaList` (ItemsList.cs:284) — same flow but also drops the companion region if no other rule references it. `Delete` key with a single selected rule routes via `_selectedEffect`/`_selectedHaptic`/`_selectedRegion` branches; multi-selection (`_selectionSet.Count > 1`) routes through `DeleteSelection` (MultiSelect.cs:337).

### 4.3 Editor — region propagation

- Timeline `Rectangle` per region (`BuildRegionVisual`, xaml.cs:1745) — fill ARGB(80, color), stroke = color, height = canvas/2, top half lane.
- Click on a region: if `FindRuleByRegionConstraint(region.Id)` returns a rule, `SelectRule` is called (so the user sees the full rule editor with the "Region details" sub-panel). Orphan regions fall into `SelectRegion(region)` and surface the standalone `RegionEditor` panel.
- Drag the body of a region rect → `DragRegion` mode (move). Drag the edges (within `EdgeResizePx = 6.0` of left/right) → `ResizeRegionStart` / `ResizeRegionEnd`. Cursor feedback set in `RegionRect_MouseMove`.
- Region label/color/start/end edits in the side panel push live updates and `RebuildRegionVisuals`.

### 4.4 Editor — rules/effects list scrolling, sort, filter

- No sort UI. List is auto-sorted by `Start` time ascending (`CollectItemRows`, ItemsList.cs:157-165). Items without a meaningful start time sink to bottom.
- No grouping. Rules / haptic events / non-haptic effects are interleaved by time.
- No filter UI. Disabled rules render with dimmed `TextMutedBrush` foreground in `BuildRuleRow` (the legacy summary path, xaml.cs:2362), but disabled state isn't surfaced on items-list rows.
- Scroll: `ItemsListScroll` has `MaxHeight=240` — beyond that height, list scrolls inside the side panel. The outer side-panel `ScrollViewer` also scrolls when total side-panel height exceeds the window.
- Selection highlight: row background flips to `DeeperAccentTransparent20Brush` and border to `DeeperAccentBrush` (ItemsList.cs:178-184). Highlight is rebuilt by `RefreshItemsList()` after any selection change.

### 4.5 Editor → player communication

- `BtnPreview_Click` (xaml.cs:3619) constructs `new EnhancementPlayerWindow(App.DeeperPlayer, App.DeeperHost, _enhancement, "editor-preview")` with the in-memory enhancement. The player marshals via the shared singletons `App.DeeperPlayer` (EnhancementAudioPlayer) and `App.DeeperHost` (EnhancementHostService).
- There is no live "send updates to running preview" channel — the editor opens a snapshot in a new window. Closing the player doesn't push anything back.
- The editor also calls `HealEmptyTriggerRegionIds()` (xaml.cs:3652) right before launching the player to repair an in-memory inconsistency that older versions left behind.
- The editor's own preview is the in-window `PreviewHost` (VLC / WebView2 / waveform). It does not host the `EnhancementEngine` — the comment at line 118 explicitly says the editor stopped running the engine in-place; only the standalone Player does.

### 4.6 Editor — keyboard shortcuts (`DeeperEditorWindow_KeyDown`, xaml.cs:4273)

| Key | Behavior | Guarded by |
| --- | --- | --- |
| Space | Play/pause | not in a TextBox |
| Home | Seek to 0 | not in a TextBox |
| R | Create region [now, now+5s] | not in a TextBox, has media |
| H | Create haptic event at playhead | not in a TextBox, has media |
| Delete | Delete current selection (multi → bulk; else region/haptic/effect) | not in a TextBox |
| Escape | `SelectNothing()` | not in a TextBox |
| Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z | Undo / Redo | not in a TextBox |
| Ctrl+C / Ctrl+X / Ctrl+V | Copy / Cut / Paste selection | not in a TextBox |
| Ctrl+A | Select all on timeline | not in a TextBox |
| Ctrl+S / Ctrl+Shift+S | Save / Save As | always |
| Ctrl+E | Export Enhanced | not in a TextBox |

Drag handlers in `TimelineCanvas_MouseMove` (xaml.cs:1250) cover scrub, region create, region move/resize, haptic move/resize, effect segment move/resize, rubber-band selection. Drag start: `TimelineCanvas_MouseLeftButtonDown` — `Shift+click` for region create, plain click for rubber-band (drag) OR scrub (no drag).

Double-click: not handled on timeline. Ctrl+Wheel on `TimelineScroll` → horizontal zoom with cursor anchor (`TimelineScroll_PreviewMouseWheel`).

### 4.7 Editor — window resize behavior

- `Width=1180` `Height=864` `MinWidth=980` `MinHeight=672`.
- Right side panel is fixed at `Width=320` (Col 1 of the main body). Resizing the window only changes Col 0's `*`.
- The timeline row is `Height=160` (fixed). Vertical resize feeds entirely into the preview row (`Height=*`).
- Preview pane has no minimum; very short windows clip the preview.
- Side panel uses its own `ScrollViewer` (line 556), so anything that overflows 320px wide × available height scrolls vertically.
- `TimelineCanvas_SizeChanged` (1118) and `TimelineScroll_SizeChanged` (1131) recompute the canvas width via `ApplyZoom` and rebuild visuals.
- The validation strip (Row 3) and linked-files strip (Row 1) are `Height=Auto`, so they always show but can stack their internal grid awkwardly if window is too narrow.

### 4.8 Player — controls & engine

- `BtnPlayPause_Click` (428) calls `_player.Play()` / `_player.Pause()` for audio, or scripts the WebView2 video element for video mode.
- `_uiTimer` (100ms `DispatcherTimer`) drives `UiTimer_Tick` — updates `TxtCurrent`, `TxtTotal`, the waveform playhead `PlayheadLine.X1/X2`, and `SliderVolume` if external state changed.
- Engine bind: `_host.Bind(timeSource, …)` happens on play (line 755, 930), `_host.UnbindEngine()` on stop (763, `UnbindEngineIfRunning`).
- Event log: `_host.ActionLogged` and `_host.Diagnostic` both feed `OnHostActionLogged` → `AppendEvent` inserts into `_events` ObservableCollection (cap 30). `LstEvents` is bound to `_events`, rendering each line via a `DataTemplate` `TextBlock` in Consolas 10pt NoWrap.
- Status: `_host.Loaded` → `OnHostLoaded` populates `TxtStatus`, `TxtEnhMetadata`, swaps `AudioPane` ↔ `VideoPane`, sets `TxtEnhSource`. `_host.LoadFailed` → `OnHostLoadFailed` formats failure into `TxtStatus`.

---

## 5. Styling and theming

Resource flow:
- `App.xaml` merges `Resources/Theme/Colors.xaml` (the actual `Color` values) and `Resources/Theme/Brushes.xaml` (`SolidColorBrush` keys that bind to those colors via `DynamicResource`). Deeper-specific colors at `Colors.xaml:63-66`, brushes at `Brushes.xaml:63-66`.
- `MainWindow.xaml` `Window.Resources` (`StaticResource` declarations near the top of the file) define `TabButton`, `TabButtonActive`, `TabButtonDeeper`, `TabButtonDeeperActive`, plus generic styles like `SectionHeader`, `SettingLabel`, `ToggleStyle`, `AnimatedTabBackground` (referenced as `Template`).
- `DeeperEditorWindow.xaml` `Window.Resources` define editor-local styles: `EditorMenuItem`, `TimelineCtxMenuItem`, `TimelineCtxMenu` (ContextMenu instance, `x:Shared="False"`), `EditorTextBox`, `EditorLabel`, `EditorComboBox`, `EditorComboBoxItem`. These exist because the WPF system defaults paint menus / popups / comboboxes with a light Aero-style background that breaks against the dark theme.
- `EnhancementPlayerWindow.xaml` `Window.Resources` define only `HeaderText` and `DimText`.
- Other Deeper windows (`NewEnhancementDialog`, `UrlPromptDialog`, `GazePickerWindow`) define no styles and rely entirely on inline + theme brushes.

Sources of color / spacing / size:
- The Deeper accent line: violet `#FF7B5CFF` at `Colors.xaml:63` with three derived variants (`Soft`, `Transparent20`, `Transparent40`). Used everywhere — borders, button fills, swatches, the timeline playhead, region default color (Region.cs:101 default `#7B5CFF`), GazePickerWindow stroke (hardcoded `#FF7B5CFF` at GazePickerWindow.xaml:29).
- Effect color palette (`EffectColors` dictionary, `DeeperEditorWindow.Unified.cs:35-43`) is independent: Haptic `#7B5CFF`, Flash `#FFC85C` (yellow-amber), Bubble `#5CC8FF` (cyan), Subliminal `#FF69B4` (CCP-classic pink), Overlay `#5CFFB7` (mint). These are not in the theme dictionary — hardcoded in the partial class.
- Region palette (`RegionPalette`, `DeeperEditorWindow.xaml.cs:62-65`) is yet a third hardcoded set of 6 hex strings: `#7B5CFF #FF69B4 #5CFFB7 #FFC85C #5CC8FF #FF7B5C`. Overlaps with EffectColors but is not the same list — `#FF7B5C` (peach) is only in the region palette, and the orange amber differs slightly.
- Rule pin color: `#FF8C00` (`DeeperEditorWindow.Unified.cs:788-789`), hardcoded, not in theme.
- Drag-create preview, rubber-band rectangle, selection highlights — all use `DeeperAccentBrush` via `FindResource`.

Inline-vs-resource inconsistencies / smells (file:line):
- `DeeperEditorWindow.xaml:394` preview pane background `#0A0A14` hardcoded.
- `DeeperEditorWindow.xaml:425` waveform canvas background `#10000020` hardcoded (and again at line 773 for the curve editor canvas).
- `DeeperEditorWindow.xaml:535` timeline canvas background `#15151F` hardcoded; same constant for `EnhancementPlayerWindow.xaml:131` (`AudioPane`).
- `DeeperEditorWindow.xaml:50,53,106` menu hover background `#3D3D60` hardcoded.
- `GazePickerWindow.xaml` uses raw hex `#FF7B5CFF`, `#357B5CFF`, `#FFB8A8FF`, `#E61C1C35`, `#40000000`, `#80FFFFFF`, `#A0000000` everywhere — no `DynamicResource`. Re-theming Deeper would not touch this window automatically.
- `EnhancementPlayerWindow.xaml:131,139,162,168` hardcodes black backgrounds (`#15151F`, `Black`, `#A0000000`).
- `UrlPromptDialog.xaml:17` `TxtUrl` is `Background=White Foreground=Black` (intentional per the user's memory `feedback_combobox_text_color.md`, but worth noting it doesn't follow the dark theme).
- `EditorComboBox` style at `DeeperEditorWindow.xaml:190` sets `Background=White Foreground=Black` for every editor combobox — same readability-driven choice, no shared style.
- `BtnDeeperNewEnhancement` (`MainWindow.xaml:8554`) re-implements its own `ControlTemplate` inline (lines 8555-8566) for the hover swap; other Deeper buttons use the inline `Background`/`BorderBrush` pattern.
- Cancel button in `NewEnhancementDialog.xaml:113` uses `PanelAccentBrush` (the global muted-button color), but every other Deeper "secondary" button uses `DeeperAccentTransparent20Brush` + accent foreground — visual language drift.

---

## 6. Pain points

### 6.1 Hub — Library + Recent are two parallel lists doing similar things

- Both lists render with the same row style (`Border` CornerRadius=6 with title, media-source line, file path); only Library rows additionally render the 📤 catalogue button and auto-tag chips. Recent rows literally re-read each JSON to render the same media-source dot.
- The Recent list has no de-duplication versus the Library — a file the user just saved into the library appears in both columns simultaneously. Source: `RefreshDeeperLibraryUI` (MainWindow.xaml.cs:2593-2624) builds both unconditionally with no overlap check.
- Both lists are vertical `StackPanel`s inside a `ScrollViewer`. No virtualization. With hundreds of enhancements this rebuilds hundreds of `Border`s per refresh (every time the tab activates or the library changes).
- The two-column layout splits horizontal space 50/50 (`Width="*"` × 2 with a fixed 20px gutter, MainWindow.xaml:8638-8642). On narrow windows each card is ~480px wide, but the title + 4 status lines + chips don't need that width — the card density is low.
- The "open folder" affordance (📁) exists only on the Library side. Recent has no equivalent.
- There is no in-list search, filter, or sort UI. With more than ~20 enhancements, finding one is purely a scroll exercise.
- The "BETA" badge (`MainWindow.xaml:8513-8522`) plus the `deeper_beta_notice` info box in Row 1 plus the `DeeperWelcomeCard` (Row 2 collapsed by default but available again from the Tour button) means the user can see three different "this is new" callouts at once on a freshly opened tab.

### 6.2 Editor sidebar — pixel/space budget

The right panel is `Width=320` (`DeeperEditorWindow.xaml:383`). Within it, a single vertical `StackPanel` (line 557) stacks:

1. "Metadata" header (line 559-561) — 13pt Bold + 10px bottom margin.
2. Six labeled metadata fields:
   - Name TextBox (default ~30px) + 8px margin from style.
   - Creator field with lock toggle DockPanel.
   - Remixer TextBox.
   - Description TextBox `Height=68` (line 592) + 8px margin = ~76px.
   - Tags TextBox.
   - License TextBox.
   Each label is `Margin=0,0,0,4` plus a `TextBox` style with `Margin=0,0,0,8`. Conservatively the metadata block consumes ~280–320px before the user touches anything.
3. Separator (`Margin=0,10,0,14`, line 605) — another ~25px.
4. `ItemsListSection` (rules+effects overview) collapsible. When expanded, `ItemsListScroll` caps at `MaxHeight=240` (line 649). When the user has more than ~6 rows it scrolls inside this 240px window. Plus the toggle header is ~30px, so this group costs ~270px when expanded.
5. "Selected Item" header (line 658) — ~25px.
6. The active editor (Region / Haptic / Rule / Flash / Bubble / Subliminal / Overlay). Rule editor in particular packs in:
   - Header row (line 810).
   - `ChkRuleEnabled` checkbox.
   - Trigger combo + ?-button row + `TriggerFields` (code-built — can be 1 line for `TimeReached` or 4+ fields for `GazeTarget` with rect inputs + "Pick on video…" + 3×3 preset grid).
   - Action combo + ?-button row + `ActionFields`.
   - Region constraint combo + ?-button row.
   - Cooldown TextBox.
   - `RuleRegionDetails` sub-panel (separator + 4 fields + color swatch + 6-color WrapPanel) — another ~180px when visible.
   Easily 350–500px for a band-style rule with a GazeTarget trigger.

At default 864 window height: title bar ~30px + linked files ~74px + validation ~50px = ~155px chrome. That leaves ~710px for the main body. Side panel viewport is therefore ~710px and the side-panel content above wants ~280 + 25 + 270 + 25 + 450 ≈ 1050px. **The side panel needs to scroll constantly even at default size.** Source: `ScrollViewer` at line 556 with no `MaxHeight` and a child `StackPanel`.

Specific call-outs:
- `ItemsListScroll.MaxHeight=240` (line 649) means the rules+effects list never gets more than 6–8 visible rows regardless of available space. A user with 20 rules has to scroll inside a 240px window even when the window is maximized.
- The Selected-Item editor group sits **below** the items list. When the user clicks an item in `LstAllItems`, the editor that opens up to populate is below the fold by default — the user needs to scroll the side panel to find the fields they just opened. There is no auto-scroll-to-selected-editor in the code (verified by searching `BringIntoView` — no calls).
- `RegionEditor`, `HapticEventEditor`, `RuleEditor`, `FlashEffectEditor`, `BubbleEffectEditor`, `SubliminalEffectEditor`, `OverlayEffectEditor` are seven sibling StackPanels (lines 667, 720, 809, 936, 950, 969, 983). Only one is visible at a time, but they're all in the visual tree. `HideAllEditors()` (Unified.cs:572) sets each to `Collapsed`; the active branch flips one to `Visible`. The collapsed siblings still consume layout-engine cycles per measure pass.
- Multi-select case is unhandled in the sidebar: if `_selectionSet.Count > 1`, the panel still shows whichever single primary (`_selectedRule` / `_selectedRegion` / `_selectedHaptic` / `_selectedEffect`) was last set. There's no "3 items selected" summary view.

### 6.3 Editor timeline — visual language

Timeline canvas is a fixed `Height=160` (`DeeperEditorWindow.xaml:390`) Canvas with five layered shape types:

| Lane | Y range | Shape | Color source | Selection feedback |
| --- | --- | --- | --- | --- |
| Regions | 0 .. h/2 (~0–80) | `Rectangle` fill ARGB(80,…) + stroke | `region.Color` (per-region) | StrokeThickness 1→2 |
| Haptic | h/2+2 .. h-2 (~82–158) | `Rectangle` fill ARGB(130 or 180,…) | hardcoded `#FF7B5CFF` (Unified.cs:1950 — yes, "#FF" + DeeperAccent) | Fill alpha + stroke 1→2 |
| Effect dots (one-shot) | y = h-18 (~142), 12×12 | `Ellipse` | `EffectColors[type]` (Flash amber, Subliminal pink, etc) | white stroke 0→2 |
| Effect segments (ongoing) | y = h-22 .. h-4 (~138–156) | `Rectangle` fill ARGB(140,…) + stroke | `EffectColors[type]` | StrokeThickness 1→2 |
| Rule pins (TimeReached) | 0..h vertical line + small 12px polygon flag at top + 14px-wide invisible hit rect | `Line` dashed + `Polygon` + `Rectangle` (transparent) | hardcoded `#FF8C00` | adds white stroke when selected |

Problems:
- The two lanes (regions top half, haptics bottom half) **overlap with effect dots/segments** at y≈138–156, because effects render at the bottom of the canvas. A bubble effect sitting on top of a haptic event is visually a colored rectangle on top of another colored rectangle on top of a region.
- There is no axis label, no tick marks, no gridlines, no "lane" header text. The only spatial cue that "this row is haptics" is "it's in the bottom half." Once effect dots/segments add a third lane visually on top of the haptic lane, the lane identity collapses.
- Region labels are **never drawn on the band itself** — they're only in the `ToolTip` (`DeeperEditorWindow.xaml.cs:1769`). The user has to hover to find out which band is which.
- Rule pins are a different color (orange `#FF8C00`) than every other element, and they are the only thing that's both dashed-stroked AND has a flag glyph. No legend or hint anywhere on the surface explains the visual vocabulary.
- Selection state is encoded as 1px→2px stroke thickness changes for every shape type. Multi-select uses the same affordance. There's no checkbox, halo, or count badge.
- The playhead `PlayheadLine` is a 2px solid line in DeeperAccent — visually similar to a rule pin stroke (1.5px dashed in orange). The single hint is "solid violet vs dashed orange," which is hard at 1080p.
- Effect dots are 12×12 px on a 160px canvas. With dense timelines they merge into a row of indistinguishable colored dots at the bottom of the canvas with no time-ordered grouping (they just sit at their `Start * width / total`, so overlapping `Start` values stack on top of each other).
- Zoom is horizontal-only and is anchored on the cursor (`TimelineScroll_PreviewMouseWheel`, xaml.cs:1214). No vertical zoom, no fit-to-content. The canvas remains 160px tall regardless of how many lanes are populated.
- No mini-map or overview at the bottom showing the whole project when zoomed in.
- The `MaxZoom=16.0` cap (`DeeperEditorWindow.xaml.cs:94`) plus the canvas's `Cursor=Hand` everywhere means click-targets get crowded but never get bigger.

### 6.4 Player window — separation and event log

- The player is a completely separate `Window` (`Views/Deeper/EnhancementPlayerWindow.xaml`). It shares `App.DeeperHost` and `App.DeeperPlayer` singletons with the editor but has its own copy of the enhancement and its own UI state.
- Picking media and picking the .ccpenh.json are two separate file dialogs (Row 1 and Row 2). For audio mode the user can pick the audio first and rely on auto-discovery (`TryAutoLoadEnhancement` referenced at line 257) to find a sidecar / embedded / library match; for video mode the "Create new enhancement" button at line 101 lets the user hand the current media to the editor — but that means leaving the player, authoring, and coming back.
- The event log is a `TextBlock`-per-line `ItemsControl` (xaml:251-260) bound to an `ObservableCollection<string>` capped at 30 (xaml.cs:59-60, 1531-1534). Lines are plain strings emitted by the host: timestamp + action description. No filtering by severity, no grouping by rule, no jump-to-rule-in-editor, no clear button. With one long-running session the log scrolls a lot and the user has no way to find "when did rule #5 last fire."
- Both `_host.ActionLogged` and `_host.Diagnostic` feed the same `OnHostActionLogged` handler (xaml.cs:81-82) — so engine diagnostic noise mixes with effect-firing events.
- The video pane (`VideoPane` Border, lines 144-173) uses WebView2 and the audio pane uses a Canvas waveform. The two are mutually exclusive but live in the same `Grid.Row=3` so swapping is a visibility toggle, not a layout change. Both panes are always in the visual tree.
- Volume slider, eye-tracking button, PiP button, and zoom pills are scattered: zoom inside the VideoPane (overlay), volume on the right of the transport, eye-tracking also on the right of the transport. The transport row has no clear "primary controls" vs "secondary controls" grouping — all on a single horizontal DockPanel.
- The "discovery source" badge (`TxtEnhSource`, line 121) is good but is shoehorned into a `Grid.ColumnSpan=5` second row of the enhancement-file Grid, with its visibility toggled in code. Visually it's just a smaller dimmer text line below the path.
- No way in the player to see the rule list / timeline of the enhancement being played — only the live event log. The user has to open the editor to inspect what the engine is actually configured to do.

### 6.5 Cross-window state — architecture feel

- Editor and Player are independent `Window`s. The editor's `BtnPreview_Click` (xaml.cs:3619) instantiates a new `EnhancementPlayerWindow` and passes the in-memory enhancement. Closing the player does nothing to the editor; closing the editor doesn't close the player.
- They share `App.DeeperPlayer` (audio playback singleton) and `App.DeeperHost` (engine binder). The editor never binds the engine; the player does at play and unbinds at stop. So in practice there's only ever one engine-running window at a time, but UI-wise both windows can be open with no clear "owner" relationship in chrome.
- The editor *also* has its own `_browserSource` (`BrowserVideoTimeSource`) for HypnoTube preview (xaml.cs:48), which is a *second* WebView2 alongside the player's `VideoBrowser`. So a user previewing an HT video might have two WebView2 processes both rendering the same page.
- The hub (MainWindow tab) is a third surface. The library list lives there, but neither the editor nor the player can "go back to library" without the user manually switching to the MainWindow's Deeper tab. `OpenDeeperEditor` (MainWindow.xaml.cs:2517) sets `Owner = this` so closing the main window closes the editor; the editor's `Closed` handler refreshes the library list (line 2522). Otherwise there's no cross-window navigation.
- Two browser-bind paths exist: the editor's local preview (`BrowserPreview` WebView2 in the editor) and the MainWindow Browser tab's auto-enhance bridge (`App.BrowserEnhanceBridge`, MainWindow.xaml.cs:2483) which sets `DeeperBrowserBadge` on the main shell. They don't coordinate.
- The Gaze picker is a fourth window (`GazePickerWindow.xaml`), transparent and topmost, positioned over the editor's `PreviewHost`. It exists specifically because LibVLC's `VideoView` is a child HWND that wins WPF airspace (xaml.cs:3679-3682). It only matters for `GazeTarget` / `GazeAvoid` triggers.
- New-Enhancement and URL-Prompt dialogs are modal children of the hub/editor/player respectively. The new-enhancement dialog has its own "tutorial" buttons (BtnLocalVideoTutorial / BtnLocalAudioTutorial / BtnTryHypnoTubeTutorial — last is fully wired, first two are stubs per the comment at NewEnhancementDialog.xaml:73-75) — so there's also a fifth surface (the TutorialOverlay) that can appear on top of the editor after dialog dismissal.

---

## 7. Data model touchpoints

The editor reads/writes the entire `Enhancement` (`Models/Deeper/Enhancement.cs`):

```
Enhancement
├─ Schema           string (constant "ccp-enhancement/v1")
├─ Version          int (constant 1)
├─ MediaType        string ("video" | "audio")  — gates trigger picker options
├─ MediaSource      string (file path or URL)   — drives PreviewHost mode
├─ Metadata         EnhancementMetadata
│  ├─ Name          string  ← TxtMetaName
│  ├─ Creator       string  ← TxtMetaCreator
│  ├─ Remixer       string? ← TxtMetaRemixer
│  ├─ Description   string  ← TxtMetaDescription
│  ├─ Tags          List<string>  ← TxtMetaTags
│  ├─ AutoTags      List<string>  (auto-detected at save by EnhancementAutoTagger; hub renders chips)
│  └─ License       string  ← TxtMetaLicense
├─ Regions          List<Region>
│  └─ Region
│     ├─ Id          string  ← TxtRegionId (read-only)
│     ├─ Start       double  ← TxtRegionStart
│     ├─ End         double  ← TxtRegionEnd
│     ├─ Label       string  ← TxtRegionLabel
│     └─ Color       string (hex, default "#7B5CFF")  ← TxtRegionColor + RegionColorSwatches
├─ HapticTracks     List<HapticTrack>
│  └─ HapticTrack
│     ├─ Id          string (default "primary")
│     └─ Events      List<HapticEvent>
│        └─ HapticEvent (IHapticPatternTarget)
│           ├─ Start         double  ← TxtHapticStart
│           ├─ Duration      double  ← TxtHapticDuration
│           ├─ Intensity     double 0..1  ← SliderHapticIntensity
│           ├─ PatternName   string? (stock by name)  ← CmbHapticPattern
│           ├─ CustomPattern List<double[]>? (curve keyframes [t_frac, intensity])  ← CurveCanvas
│           └─ Activation    EffectActivation? (Region | Duration)
├─ Rules            List<EnhancementRule>
│  └─ EnhancementRule
│     ├─ Trigger          EnhancementTrigger
│     │    GazeTargetTrigger      .Rect[4] (normalized x,y,w,h), .MinDwellMs
│     │    GazeAvoidTrigger       same
│     │    AttentionLostTrigger   .MinDurationMs
│     │    BlinkDetectedTrigger
│     │    MouthOpenTrigger
│     │    TimeReachedTrigger     .Time (seconds)
│     │    RegionEnteredTrigger   .RegionId
│     │    RegionExitedTrigger    .RegionId
│     │    NeverFiringTrigger     (round-trips unknown types)
│     ├─ Action           EnhancementAction
│     │    SeekAction             .Target ("time"|"region_start"|"region_end"), .Time?, .RegionId?
│     │    LoopRegionAction       .RegionId?
│     │    PauseAction
│     │    PlayAudioAction        .Path, .Volume, .DuckOtherAudio
│     │    TriggerHapticAction    .PatternName?, .CustomPattern? (IHapticPatternTarget)
│     │    TriggerEffectAction    (see EnhancementAction.cs)
│     │    ScreenShakeAction
│     │    SetIntensityAction
│     │    NoOpAction
│     ├─ RegionConstraint string? (region.Id)  ← CmbRuleRegion
│     ├─ CooldownMs       int     ← TxtRuleCooldown
│     └─ Enabled          bool    ← ChkRuleEnabled
└─ TimelineItems    List<TimelineItem>
   └─ TimelineItem (Effect-only path in the editor; Rule kind not used in the UI yet — see Open Questions)
      ├─ Id                string (8-char hex)
      ├─ Kind              TimelineItemKind (Effect | Rule)
      ├─ Start             double seconds
      ├─ Duration          double seconds
      ├─ Label             string?
      ├─ Color             string?
      ├─ EffectType        string? ("flash" | "bubble" | "subliminal" | "overlay" | "haptic")
      ├─ EffectIntensity   double 0..1  (SliderBubbleIntensity, etc)
      ├─ EffectDurationMs  int          (TxtFlashDuration, TxtSubliminalDuration, TxtOverlayDuration, TxtBubbleWindow)
      ├─ EffectActivation  EffectActivation? (Region | Duration; resolver default per type)
      ├─ EffectPatternName / EffectCustomPattern (haptic via TimelineItem path — not wired in current UI)
      ├─ EffectImagePath / EffectPlaySound (flash)
      ├─ EffectText        (subliminal)
      ├─ EffectOverlayKind / EffectOpacity (overlay; CmbOverlayKind, SliderOverlayOpacity)
      ├─ EffectMaxBubbles  (bubble)
      ├─ Trigger / Action / CooldownMs / Enabled  (Rule kind only — currently the UI uses the legacy Rule list instead)
```

Key UI ↔ model bindings observed:
- Region color: `TxtRegionColor` ↔ `Region.Color`; the swatch palette `RegionPalette` is hardcoded in code and rendered as a `WrapPanel` of `Border`s (xaml.cs:148-172). Click-on-swatch sets `TxtRegionColor.Text`, which then flows through `RegionField_TextChanged` → `Region.Color`.
- Haptic intensity: `SliderHapticIntensity.Value` (0..1) ↔ `HapticEvent.Intensity`.
- Haptic pattern: `CmbHapticPattern` lists `StockHapticPatterns.Names` plus a "Custom" sentinel (xaml.cs:140-146). Selecting Custom enables `CurveEditorPanel` which has 5 draggable keyframes (`CurveKeyframeCount = 5`, xaml.cs:107) → `HapticEvent.CustomPattern`.
- Rule trigger fields: `TriggerFields` StackPanel rebuilt by `BuildTriggerFields` (referenced at xaml.cs:2577) — code-generates the trigger-type-specific inputs (rect for gaze, time for TimeReached, region id combo for Region*).
- Rule action fields: `ActionFields` StackPanel similarly built by `BuildActionFields`.
- TimelineItem effect fields (Flash/Bubble/Subliminal/Overlay) live in the four bottom StackPanels with direct two-way text/slider binding via `EffectField_TextChanged` (Unified.cs:599).

---

## 8. Open questions

- **TimelineItem.Rule kind appears unused in the UI.** The model supports `TimelineItemKind.Rule` with `Trigger`/`Action`/`CooldownMs`/`Enabled` fields (TimelineItem.cs:148-160), but the editor still adds rules to `_enhancement.Rules` (the legacy list) via `AddRuleAt` (Unified.cs:253, 280). Effects use `TimelineItems`, rules use the legacy list. The comment at line 36 of `Enhancement.cs` says they "coexist during the additive-schema transition" — is this transition still in flight, or has the unified Rule kind been abandoned?

- **`BtnAddRule_Click` (DeeperEditorWindow.xaml.cs:2472) and `BuildRuleRow` (xaml.cs:2321) look orphaned.** Search results show no current XAML element references them. The comment at line 2312 of `RefreshRulesList` says "the standalone Rules section was removed in the unified-timeline redesign." But the `BuildRuleRow` method (which renders a different-looking row than `BuildItemListRow`) remains in the file, and `BtnAddRule_Click` defaults to `GazeTarget` for video, which contradicts the hero button's `TimeReached` default. Is this dead code, or is it reached via a code path I missed?

- **`UpdateLaneDivider()` is intentionally a no-op (xaml.cs:1799-1804).** The comment says the lanes were merged, but `RebuildHapticVisuals` still computes a `laneTop = h/2.0 + 2` and `RebuildRegionVisuals` still computes `laneH = h/2.0` — so visually two lanes are still implied, just without a divider line. Is the "merged" comment accurate, or is the divider drop a half-finished refactor?

- **The first-run welcome card (`DeeperWelcomeCard`) starts `Visibility=Collapsed`** (`MainWindow.xaml:8589`). The comment says it's flipped to Visible via code on first run, but the code path that does that wasn't surveyed. Where does `DeeperWelcomeCard.Visibility` get set? Possibly tied to a settings flag like `HasSeenDeeperWelcome` — worth confirming before redesign.

- **Three independent purple/violet color sources:**
  - `DeeperAccent` `#7B5CFF` (theme).
  - `Region.Color` default `#7B5CFF` (Region.cs:101, hardcoded).
  - `EffectColors[Haptic]` `#7B5CFF` (Unified.cs:38, hardcoded).
  - `GazePickerWindow` stroke `#FF7B5CFF` (xaml:29, hardcoded).
  - Haptic visual stroke `#FF7B5CFF` (xaml.cs:1950, hardcoded).
  All resolve to the same color today, but four places to update if the accent changes. Is the duplication intentional (to keep saved files stable) or a missed refactor?

- **Rule pins are orange (`#FF8C00`, Unified.cs:788-789)**, not in any palette. Was this an explicit visual-language choice to differentiate from the violet accent, or just a quick "pick a distinct color" pick?

- **`BtnCreateNewEnhancement` is a permanently-Collapsed button in the player** (`EnhancementPlayerWindow.xaml:108`). The handler (xaml.cs:351) exists; visibility appears to be set in code when context allows. Under what conditions does this become visible? The hub already has its own "+ New Enhancement" button so this seems like a "create from currently-playing media" affordance — but discoverability is poor.

- **`BtnPictureInPicture` is also permanently Collapsed by default** (`EnhancementPlayerWindow.xaml:223`). When does it become visible? Likely tied to WebView2 video mode, but the toggle path wasn't surveyed.

- **The editor has both `BtnPreview` (transport row) and `MenuExport` (File menu, Ctrl+E)** — and a separate `BtnEditorExport` in the validation strip. The validation-strip Export button uses `MenuExportEnhanced_Click` (xaml.cs:3888) for "Export Enhanced" (i.e. a packaged version), while `BtnPreview_Click` opens the Player against the in-memory project. Two save-adjacent buttons in the bottom strip (`BtnEditorSave`, `BtnEditorExport`) plus a Preview button in the transport row — is the user expected to mentally model "Preview = test live; Export = ship a bundle; Save = write JSON"?

- **NewEnhancementDialog's two stub tutorial buttons** (Local Video / Local Audio) per the XAML comment at lines 73-75 are "framework-ready stubs." Only HypnoTube is wired. Is this surface meant to be visible to end users now, or should it be hidden until the stubs are filled in?

- **The `Region.UnknownFields` / `EnhancementMetadata.UnknownFields` / `Enhancement.UnknownFields` JsonExtensionData dictionaries** preserve forward-compatibility fields, but there's no UI surface for them. Worth confirming none of the new design needs to surface these.

- **Multi-select selection state is not reflected in the side-panel editor.** With `_selectionSet.Count > 1` the panel keeps showing whichever primary was last active. Is "show the panel of the primary" intentional, or is a "N items selected" summary view a planned-but-not-built case?
