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

- Step 1 plan review: pending

## Evidence matrix + budgets

(to be filled in Steps 3-4)

## Surprises

(to be filled during execution)
