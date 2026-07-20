# SP-013 — Prove feature-popup scrolling (demonstrator) — record

**Packet:** `spine-tasks/SP-013-popup-scrolling/PROMPT.md` — board row "Prove feature-popup scrolling" (P0).
**Framing (binding):** explicitly-labeled DEMONSTRATOR popup (SP-007 card pattern: really-functioning, superseded-by-first-real, owner may async-veto). Does NOT discharge manifest row W-04's exercise gate. Popup-LOCAL chrome only (A-005 trap). Height constants are WPF-parity, recorded pending-owner.

## WPF archaeology (READ-ONLY evidence, File.cs:line)

### `ConditioningControlPanel/Features/FeaturePopupWindow.xaml`

- `:5-6` — `Width=520 Height=640`, `MinWidth=420 MinHeight=360` (FIXED 520×640; WPF never sizes to content).
- `:7` — `WindowStartupLocation="CenterOwner"`.
- `:9-12` — `ResizeMode="NoResize"`, `WindowStyle="None"`, `AllowsTransparency="False"`, `ShowInTaskbar="False"`.
- `:96-101` — content host: `ScrollViewer` `VerticalScrollBarVisibility="Auto"`, `HorizontalScrollBarVisibility="Disabled"`, `Padding=16`.

### `ConditioningControlPanel/Features/FeaturePopupWindow.xaml.cs`

- `:37-43` (ctor) — `PreviewKeyDown` → Escape path installed.
- `:45-58` — `OnPreviewKeyDown`: Escape → `Close()` + `e.Handled=true` (unless owner's panic-key capture owns the key).
- `:60-66` — title-bar `MouseLeftButtonDown` → `DragMove()` (try/catch).
- `:68-71` — `BtnClose_Click` → `Close()`. **Escape and button both terminate in `Window.Close()` — ONE close operation.**

### `ConditioningControlPanel/MainWindow/MainWindow.Presets.cs`

- `:846` — `_activeFeaturePopup` single field (one-at-a-time).
- `:852` — close-existing-before-new: `_activeFeaturePopup?.Close();`.
- `:854-857` — `new FeaturePopupWindow(...) { Owner = this }`.
- `:858-871` — `Closed` handler: clears the field; **focus restoration** — if owner minimized → `WindowState.Normal`, then `Activate()` (try/catch for shutdown).
- `:873` — `popup.Show()` — modeless ("Non-modal so bubbles and other interactions keep working").

### Behavior contract (VERIFIED labels per wpf-parity)

| Contract point | Verdict |
|---|---|
| Owned, modeless, `ShowInTaskbar=false` | VERIFIED (Presets.cs:854/873, XAML:12) |
| Non-resizable, borderless custom chrome | VERIFIED (XAML:9-10) |
| Title-bar drag, Escape ≡ close-button = one `Close()` | VERIFIED (xaml.cs:45-71) |
| One-at-a-time, close-existing-before-new | VERIFIED (Presets.cs:846-853) |
| Focus restoration on close (unminimize + Activate) | VERIFIED (Presets.cs:858-871) |
| Min 420×360, default 520×640 | VERIFIED (XAML:5-6) |
| CenterOwner = owner's monitor | manifest §6 constraint 4 (SP-012) |
| Short content compact / tall capped in owner working area | capability-inventory §Feature-popup behavior — WPF fixed 640 does NOT do this; this is the owner-approved capability contract superseding the WPF mechanic (wpf-parity: port the outcome) |
| Scroll paths: wheel, trackpad/touch, keyboard focus, scrollbar controls, thumb; horizontal disabled; nested chaining | capability-inventory §Feature-popup behavior |
| Observable evidence = changing `Extent`/`Viewport`/`Offset` | capability-inventory §Dashboard acceptance evidence |

## avalonia-research (v12, pinned 12.1.0 verified against local ref XML + release/12.1.0 source tag)

Pinned baseline: `Avalonia 12.1.0` on `net10.0` (`client/src/CcpClient.Desktop/CcpClient.Desktop.csproj`).

- **Owned modeless show:** `Window.Show(Window owner)` — "Shows the window as a child of owner" ([api-docs](https://api-docs.avaloniaui.net/docs/M_Avalonia_Controls_Window_Show_1); 12.1.0 XML `M:Avalonia.Controls.Window.Show(Avalonia.Controls.Window)`). `Owner` lives on `WindowBase` in v12 (`P:Avalonia.Controls.WindowBase.Owner`).
- **Chrome:** v12 renamed the API — `Window.WindowDecorations` (enum `WindowDecorations { None, BorderOnly, Full }`, default `Full`; 12.1.0 XML). **`SystemDecorations` in 12.1.0 is `[Obsolete("Use WindowDecorations instead.")]`** — shim at `src/Avalonia.Controls/Window.cs:395-400` (release/12.1.0 tag). The api-docs "latest" site still presents `SystemDecorations` as current — pinned-package check is the authority (avalonia-research §3). Use `WindowDecorations="None"`.
- **Taskbar/resize:** `WindowBase.ShowInTaskbar`, `Window.CanResize` (12.1.0 XML).
- **Title-bar drag:** `Window.BeginMoveDrag(PointerPressedEventArgs)` (12.1.0 XML `M:Avalonia.Controls.Window.BeginMoveDrag`).
- **Screens:** `WindowBase.Screens` → `Avalonia.Controls.Screens` (v12 namespace, NOT `Avalonia.Platform`) with `ScreenFromWindow(WindowBase)` (12.1.0 XML). `Screen.WorkingArea` = "actual working-area **pixel-size**" (physical pixels), `Screen.Bounds` pixels, `Screen.Scaling` factor (12.1.0 XML `Avalonia.Platform.Screen`). ⇒ working-area DIP = `WorkingArea / Scaling`.
- **Placement:** `WindowStartupLocation { Manual, CenterScreen, CenterOwner }` (12.1.0 XML). `WindowBase.Position` is `PixelPoint` (physical). `TopLevel.ScalingChanged` event; `WindowBase.PositionChanged` event (12.1.0 XML).
- **ScrollViewer:** `Extent`/`Viewport`/`Offset` properties + `ScrollChanged` event ("changes to scroll position, extent, or viewport size") (12.1.0 XML + [api-docs](https://api-docs.avaloniaui.net/docs/T_Avalonia_Controls_ScrollViewer)).
- **Keyboard bring-into-view:** `ScrollViewer.BringIntoViewOnFocusChange` attached property, **default `true`** (`ScrollViewer.cs` release/12.1.0: `RegisterAttached<ScrollViewer, Control, bool>(..., true)`).
- **Nested chaining:** `ScrollViewer.IsScrollChainingEnabled` attached property, **default `true`** (same source; presenter doc: "After a user hits a scroll limit on an element nested within another scrollable element [movement chains]").
- **Mouse wheel:** handled natively in `ScrollContentPresenter.OnPointerWheelChanged` (release/12.1.0 `ScrollContentPresenter.cs` — moves `Offset` by `e.Delta`).
- **Touch pan:** `ScrollGestureRecognizer` inside `ScrollContentPresenter` (release/12.1.0) — pan + inertia path exists in v12.
- **Mixed scale:** official `AVALONIA_GLOBAL_SCALE_FACTOR` env override honored on X11 (`src/Avalonia.X11/Screens/X11Screens.Scaling.cs:206-211`, SP-007 record :53/:93 — measured ×1.5 exact on WSLg).

## Pre-approach consult — 2026-07-20, solo, ACTUAL answering model: claude-fable-5 (provenance recorded; council unavailable per packet)

Full verdict text received; key rulings applied to the design:

1. **SizeToContent=Height trap (ACCEPTED — design changed):** SizeToContent resets to `Manual` after programmatic resize and setting `MaxHeight` after first arrange may not re-clamp; X11 sizing is async. ⇒ `SizeToContent=Manual`; popup measures content desired height at open and on variant switch, sets `Height` directly: `Height = max(MinHeight, min(desiredHeight, cap))` where `cap = min(640, 0.9 × workingAreaDIP_H)` (640 = WPF fixed-height parity; both constants PENDING-OWNER). `MaxHeight` set to the same cap as belt-and-braces.
2. **Position timing (ACCEPTED):** `Position` is physical pixels and the popup's real size is unknown until first layout ⇒ position in `Opened` after first layout using `FrameSize` (physical), center on owner rect, clamp into the owner screen's working area. All math in ONE coordinate space (physical pixels).
3. **Owner-monitor rule (ACCEPTED):** cap from the OWNER's monitor at open; recompute only on owner-monitor change (owner `PositionChanged`) and DPI change (`ScalingChanged`) — never on popup drag (a "non-resizable" window must not visibly resize mid-drag).
4. **Scrollbar evidence (ACCEPTED):** Fluent ScrollBar auto-hide (`AllowAutoHide`) can collapse the bar mid-script ⇒ set `AllowAutoHide="False"` on the popup ScrollViewer (also WPF-parity: WPF bars don't auto-hide). Fluent theme has NO line buttons ⇒ "scrollbar controls" evidence = track click (page) + thumb drag; no faked button claims.
5. **Wheel routing (ACCEPTED):** WM_MOUSEWHEEL goes to the focused window, Avalonia hit-tests by cursor ⇒ script keeps cursor over popup content and popup active.
6. **Tab path (ACCEPTED):** loop Tab asserting the UIA-focused element name per step, bounded.
7. **Touch/trackpad (ACCEPTED):** probe `GetSystemMetrics(SM_DIGITIZER=92)` for `NID_INTEGRATED_TOUCH|NID_READY` + `SM_MAXIMUMTOUCHES`; precision touchpad needs HID enumeration. If absent ⇒ named manual gate, never faked.
8. **Offset assertions (ACCEPTED):** before/after probe reads, monotonic change, reached-bottom = `Offset.Y + Viewport.Height ≥ Extent.Height`.
9. **Clamp order (ACCEPTED):** `max(MinHeight, min(640, 0.9×waDIP))`; width also clamped (`min(520, waDIP_W)`, MinWidth guard) — WSLg at 1.5× gives ~512 DIP-wide working areas.
10. **Focus-restoration test seam (ACCEPTED):** manager exposes the restoration as an assertable seam (fake-able) for unit tests.
11. **Chrome locality (CONFIRMED):** title bar markup inline in `FeaturePopupWindow.axaml`, no UserControl extraction (A-005).

## Design (post-consult)

- `client/src/CcpClient.Desktop/Features/FeaturePopupWindow.axaml(.cs)` — contract-named. `WindowDecorations="None"`, `CanResize="False"`, `ShowInTaskbar="False"`, `WindowStartupLocation="Manual"`, `Width=520/MinWidth=420/MinHeight=360`, `SizeToContent=Manual`. Popup-LOCAL title bar (drag via `BeginMoveDrag`, close button) + window-level Escape `KeyBinding` → BOTH call one `ClosePopup()` operation. Owned modeless `Show(dashboard)`. Closed → focus restoration (unminimize-if-minimized + `Activate()`), assertable seam.
- `PopupPlacement` (pure, unit-tested): cap math + center-and-clamp math in physical pixels, guards `max(Min, min(default, cap))`.
- `FeaturePopupManager` (unit-tested): one-at-a-time, close-existing-before-new, Closed→null, focus-restoration seam.
- Synthetic content: variant switcher (TALL ~30 rows + final Button below fold / SHORT 3 rows / NESTED inner ListBox 200 DIP). UIA-readable scroll-probe TextBlock reporting live `Extent`/`Viewport`/`Offset` (ScrollChanged) — same pattern as the SP-007 layout probe.
- Evidence: headed Windows task script on capture.ps1 helpers (launch/SetWindowPos-raise/UIA/CopyFromScreen + mouse_event wheel/drag + SendKeys Tab/Escape); touch probe first (absent ⇒ named manual gate); mixed scale via `AVALONIA_GLOBAL_SCALE_FACTOR=1.5`; K3 visual review; WSLg = render + capping + geometry session facts only (no input automation, SP-008 limit).

## Engine-review presence (T-2)

- Step 1 plan review: **ABSENT** — `spine_review_step` returned `skipped=true` (`nested_spawn_blocked`, by design SP-195; `spawnFailed=false`). Engine runs code+final reviews post-.DONE.
- Step 2 plan review: **ABSENT** — same (`skipped=true`, `spawnFailed=false`).

## Step 2 implementation notes (2026-07-20)

- Files: `Features/PopupPlacement.cs` (pure math), `Features/FeaturePopupManager.cs` (one-at-a-time + focus-restoration seam), `Features/FeaturePopupWindow.axaml(.cs)` (contract-named deliverable), `Features/SyntheticPopupContent.cs` (tall/short/nested), MainWindow.axaml.cs left-click wiring. Tests: `PopupPlacementTests` (17 cases), `FeaturePopupManagerTests` (4), `FeaturePopupHeadlessTests` (8 draw-level interaction tests). 139 unit + 11 headless green on Windows.
- Command-path deviation (honest): packet lists "command-path close" under unit tests; the Escape/click ROUTING is Avalonia runtime behavior, so the equivalence test lives in `FeaturePopupHeadlessTests.Escape_And_CloseButton_BothClose_ThroughTheOnePath` (real KeyPress + real mouse click). The unit seam would be vacuous.
- Surprises (see below): both found BY the headless tests.

## Surprises

1. **Avalonia 12.1.0 `ScrollViewer.Padding` clips the content tail.** With `Padding="16"` on the ScrollViewer, the presenter arranged the content 32 DIP shorter than its desired size (stack desired 1225, bounds 1193) — the final control was UNREACHABLE at max offset (extent 1193 < content bottom 1225). Measured in a throwaway headless debug test, fixed by moving the padding onto the inner `ContentControl` (extent then = 1225+32 = 1257, correct). Durable port lesson candidate — orchestrator's harvest call (port-lessons.md is outside this packet's File Scope).
2. **`KeyBinding` in `Window.KeyBindings` does not inherit DataContext** — a compiled `{Binding CloseCommand}` on it never resolved (silent no-op), found by the headless Escape test. Replaced with a tunnel `OnKeyDown` override, which is ALSO the WPF shape (`PreviewKeyDown`, xaml.cs:45). The card's `Border.KeyBindings` (SP-007) works because the binding targets the Border's own DataContext inheritance.
3. **ScrollViewer without `Background` does not hit-test its empty regions** — wheel events over content gaps never reached the presenter (hit falls to the window Border, outside the scroller's bubble path). `Background="Transparent"` on the scroller is required for wheel-anywhere scrolling (headless test caught it: wheel worked over ListBox items, not over the tall StackPanel).

## Step 3 — Windows-headed evidence matrix (2026-07-20, `popup-evidence.ps1`, log + PNGs in `evidence/`)

Headed run on this workstation (3 monitors, all scale 1.0 per SP-007 record). Real input via mouse_event/SendKeys/UIA Invoke; observable scrolling via the app's UIA scroll-probe (changing Extent/Viewport/Offset, never screenshots alone). Full log: `evidence/windows-headed-evidence.log`. **EVIDENCE PASS** — 25 PASS gates, 1 named GATE, graceful close exit 0.

| Gate | Result |
|---|---|
| TALL geometry | scrollbar present (w=16); popup {182,202 520x640} inside owner WA {0,0,1920,1032}; extent 948 > viewport 504; final starts below fold |
| A. mouse wheel | offsets 100→200→300→400→444 (monotonic, stable at bottom); final-in-viewport true; offset+viewport = extent |
| B. keyboard focus (Tab) | focus trail close-button → variants → toggle rows 5…30 → **popup final control**; bring-into-view moved offset to 428; final-in-viewport true |
| C. scrollbar track | 1 page-down click → offset 444 (page = viewport); final reached |
| D. thumb drag | real drag on the thumb → offset 0 → 444; final reached |
| E. trackpad/touch | **NAMED MANUAL GATE** — see below |
| Close paths | Escape closed (keyboard side); title-bar button closed via real click at the button's UIA rect (button side) — ONE operation |
| Focus restoration | after Escape close, GetForegroundWindow = dashboard hwnd (W-04) |
| SHORT | extent 224 ≤ viewport 224, popup height 360 DIP (compact, WPF min — not the 640 fixed) |
| NESTED | inner list scrolled itself (inner-offset → 1400 while outer 0), then chained (outer → 100 → 122); final reached |
| Secondary monitor (Windows-headed only) | dashboard moved to DISPLAY1 WA {-1440,-1469,1440x2512}; popup opened at {-1380,-1389} INSIDE that working area on DISPLAY1 — owner-monitor, never primary-by-default; negative-origin monitor handled |

**Trackpad/touch:** digitizer PROBED — this box HAS a 2-point integrated touch digitizer (SM_DIGITIZER=0xCD), so the path was attempted for real with OS-level `InjectTouchInput`: **err=87 (ERROR_INVALID_PARAMETER) across parameter variations in 4 of 5 attempts; the one accepted batch (156 injections, 12 pans) produced NO app-visible scrolling (offset 0 → 0)** — automation cannot produce this evidence on this workstation. NAMED MANUAL GATE: physical touch-pan on the touch monitor. No touchpad device present (0 found) — trackpad specifically also gates.

**Mixed scaling:** `AVALONIA_GLOBAL_SCALE_FACTOR` is honored on X11 ONLY (SP-007 record :53 — Win32 scaling comes from GetDpiForMonitor); all monitors here are scale 1.0. Mixed-scale geometry/capping evidence therefore lands in the Step-4 WSLg run (1.5× session facts); Windows-150% inherits SP-007's named manual gate.

**K3 visual review (4 PNGs):** popup-tall-top / popup-tall-scrolled-bottom / popup-short-compact / popup-nested-scrolled — dark grammar, pink chrome, title bar + close button, variant switcher, correct thumb positions (top/bottom), inner-list own scrollbar, compact short state without scrollbar, probe texts readable. **K3 PASS** on all four states.

**A-013 MCP advisory (redacted AXAML):** `ValidateXaml` strict → PASS (first run caught MY redaction dropping `xmlns:x` — a fair catch; resent, passed). `AnalyzePerformance` → **REJECTED**: emitted "❌ Invalid XAML syntax - cannot analyze performance" AND "Score: 90/100 🏆 Excellent" in ONE response — SP-007's exact self-contradictory failure mode, second occurrence; the 12.1.0 compiler + headless suite remain the authority.

## Surprises (headed phase)

4. **The popup opens UNACTIVATED in the normal z-band** (Windows foreground-lock; the script process has no foreground rights) — the foreground terminal sat ABOVE the popup and silently ate every click/wheel aimed at it (clicks "worked", foreground changed, but to the terminal's hwnd). SP-007's SetWindowPos(HWND_TOPMOST) raise had to be applied to the POPUP after every open, not just the dashboard. Also: owned windows NEST under the owner in the UIA tree, so a naive `RootElement.Children` popup search either misses the popup or (descendants search) returns the DASHBOARD element (its subtree contains the popup's probe text) — `Get-Popup` must exclude the window carrying `layout-probe`.
5. **PowerShell `$Matches` entries are strings** — `$Matches[4] + [double]$Matches[1]*...` string-CONCATENATED ("76"+244 → 76244) and sent clicks off-screen. Cast the coordinate groups to `[int]` BEFORE arithmetic (capture.ps1's Get-CardRect already did this; I deviated and paid for it).
6. **Fluent ScrollViewer template hosts horizontal AND vertical ScrollBars** — `OfType<ScrollBar>().FirstOrDefault()` can return the disabled HORIZONTAL one (collapsed, 0x0 at the scroller origin). Filter by `Orientation == Vertical`.
7. **InjectTouchInput on this workstation:** init succeeds, then err=87 on most attempts; one run accepted 156 injections with zero app-visible effect. Unreliable-for-automation; named manual gate.

## Evidence matrix + budgets

(filled in Steps 3-4)
