---
name: overlay-clickthrough
description: "Click-through, topmost, transparency, focus, and global-hook input handling for CCP's fullscreen overlays. The product requirement (team review 2026-07-09): PER-REGION click-through - only the theme color filter and the spiral are ambient 'tinted glass' the user works through; every other active layer (video, flash, subliminal, brain-drain, bouncing text, keyword highlight, bubbles) captures pointer input over the region it paints. Use this skill whenever you touch overlay windows, WS_EX_* ex-styles, P/Invoke on windows, Topmost behavior, mouse hooks, hit-testing, focus stealing, screen-capture exclusion, or any bug where clicks are blocked, leak through, or land on the wrong thing; and when planning Linux/macOS click-through."
---

# overlay-clickthrough

## The requirement

**Per-region click-through (team review 2026-07-09 - supersedes the earlier "all passive overlays are click-through" spec).** The compositor window is fullscreen, topmost, invisible to Alt-Tab, never stealing focus. Click-through is decided PER-REGION, not per-window: only the **theme color filter** and the **spiral** are ambient tinted glass - a screen region covered by only those two layers passes input to the apps behind, so the user keeps typing/clicking into their normal apps through it. **Every other active layer** (video, flash, subliminal, brain-drain, bouncing text, keyword highlight, bubbles, chaos FX) **captures pointer input over the region it paints**. Interactive elements (chaos bubbles, clickable flash images, lock cards, mandatory-video controls) additionally hit-test their geometry for on-click behavior. Getting this wrong either locks the user out of their desktop or lets attention/interactive effects leak clicks to the app underneath.

Mechanism unchanged: the per-monitor window stays `WS_EX_TRANSPARENT|LAYERED`; the compositor exposes a per-frame **capture mask** = union of non-ambient active layer regions (immutable snapshot), and the global mouse hook **swallows** clicks inside the mask and passes the rest.

Authoritative product spec: `ConditioningControlPanel/docs/crossplatform-rebuild-plan.md` section 7.4 (updated with this rule; two-level passthrough + degrade-gracefully off Windows still apply to the window and the ambient regions).

## The two-level rule (the single most important fact)

Click-through must be applied at BOTH levels, or it does not work:

1. **Framework level**: `IsHitTestVisible = false` (plus `Focusable = false`, `ShowActivated = false`, `ShowInTaskbar = false`).
2. **OS level (Windows)**: `GWL_EXSTYLE |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, applied **after the native handle exists**. In Avalonia that means the `Opened` event (`TryGetPlatformHandle()` returns null earlier); in WPF, `SourceInitialized`/`Loaded`.

Shipping only the framework level is the historic "pink filter blocks the whole desktop" bug.

## Canonical recipes (Avalonia head)

Window setup (see `CCP.Avalonia/Platform/AvaloniaOverlaySurface.cs` and `Chaos/ChaosBubbleHostOverlay.cs`):

```
WindowDecorations = WindowDecorations.None      // v12 rename (not SystemDecorations)
TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }
Background = Brushes.Transparent
Topmost = true; ShowInTaskbar = false; ShowActivated = false
Focusable = false; IsHitTestVisible = false
```

Ex-styles: call `ChaosWin32Helper.ApplyOverlayExStyles(window, transparent: true)` from the `Opened` handler (`CCP.Avalonia/Chaos/ChaosWin32Helper.cs`). It ORs `TOOLWINDOW|NOACTIVATE|LAYERED` and toggles `TRANSPARENT`. Interactive-but-unfocusable windows pass `transparent: false`.

The compositor window goes further (`CCP.Avalonia/Compositor/CompositorWindow.axaml.cs`, `ApplyNativeTransparency`): it re-applies the ex-styles on `Opened`, `Activated`, AND window-state changes, then flushes with `SetWindowPos(SWP_FRAMECHANGED)`. Copy that pattern for any long-lived fullscreen overlay.

## Hard-won reliability rules (violating any of these reintroduces a shipped bug)

1. **`WS_EX_TRANSPARENT` alone fails once the window becomes foreground** (for example when the main window minimizes and Windows activates the topmost overlay): it then captures all clicks and locks the desktop. `WS_EX_LAYERED + WS_EX_TRANSPARENT` is the reliable combination under Avalonia v12's GPU pipeline. Note the distinction: the LAYERED **style bit** is fine with GPU rendering; the `UpdateLayeredWindow` **API** is not. (`ConditioningControlPanel/docs/unified-compositor-engine-plan.md` line ~34 claims LAYERED was removed; the code keeps it. Trust the code.)
2. **Never `SetWindowSubclass` an Avalonia v12 HWND**: races Avalonia's wndproc management, intermittent native `0xC0000005`. `WM_NCHITTEST`/`WM_MOUSEACTIVATE` subclass tricks are redundant given `WS_EX_TRANSPARENT|WS_EX_NOACTIVATE`. (`AvaloniaBubbleWindow.Windows.cs` still has a best-effort one; it is a known latent risk, not a pattern to copy.)
3. **`SetWindowLong` alone may not take effect**: follow style changes with `SetWindowPos(..., SWP_FRAMECHANGED)`.
4. **Pooled/recycled windows keep stale ex-styles.** Rebuild `GWL_EXSTYLE` from a known base on every reuse; do not OR onto whatever is there (the "unpoppable bubble" bug: a recycled shell kept `WS_EX_TRANSPARENT` from its corpse phase).
5. **WPF layered windows give free per-pixel click-through on alpha-0 pixels; Avalonia windows do NOT.** A ported overlay that paints partial content must either shrink the window to the painted region (Avalonia `ChaosHudWindow` sizes itself to the HUD strip) or go fully `WS_EX_TRANSPARENT` with hook-based hit-testing. Never assume per-pixel behavior carried over.
6. **Focus variants exist on purpose:** `SubliminalService` keeps `TRANSPARENT|LAYERED` always but toggles `NOACTIVATE` because deliberate focus-steal is a feature there. Chaos interactive windows use `TOOLWINDOW|NOACTIVATE` with NO `TRANSPARENT` so they absorb clicks without stealing focus. Read the intent before "fixing" a window's style set.
7. **Free Desktop mode inverts topmost**: `ChaosWindowZ.DesktopMode` births overlays non-topmost and actively demotes. Never assume overlays are always topmost.
8. **Topmost churn deadlocks:** chaos overlay windows are created once per run and hide/unhide (layered-window churn mid-run deadlocks the render thread). Re-showing does not restore z-order, so every show path re-raises (`RaiseAboveVideo`).

## The three interaction models

1. **Static click-through** (ambient ONLY, per the 2026-07-09 rule): transparent forever. ONLY the **spiral** and the **pink/color tint** (theme color filter). Brain-drain, subliminals, and keyword highlights are NO LONGER static click-through - they moved to capture-by-default (model 3's mask): they capture pointer input over their painted region while active.
2. **Dynamic toggle** (per-window): flip `WS_EX_TRANSPARENT` at runtime. `FlashService.ApplyClickability` (per spawn, from `FlashClickable`), `ChaosOverlayWindow.SetClickThrough` (click-through during play, interactive during boon draft/results), `ChaosDvdOverlay(clickable)`. Avalonia ports mirror these (`CCP.Avalonia/Chaos/ChaosOverlayWindow.axaml.cs` `SetClickThrough`).
3. **Shared host + global mouse hook** (the UCE model): the window stays always-`WS_EX_TRANSPARENT|LAYERED`, but its INPUT POLARITY flipped 2026-07-09 from opt-in-capture to opt-out-ambient. A `WH_MOUSE_LL` hook feeds pointer events; the compositor builds a per-frame **capture mask** = union of every non-ambient active layer's painted region (immutable snapshot: `FlashLayer`/`BubbleLayer`/`SubliminalLayer`/`BrainDrainLayer` etc.). The hook **swallows** a click whose point falls inside the mask (region owned by a non-ambient effect) and **passes** it otherwise (ambient-only region = color filter/spiral, or bare desktop). Interactive layers still hit-test their own geometry for on-click behavior; non-interactive capturing layers (subliminal, brain-drain) simply block.

### Hook rules (model 3)

- **WPF** (`Services/Input/GlobalMouseHook.cs`): callbacks are `Func<Point,bool>` running synchronously on the hook thread; return true to SWALLOW the click. Hit-test only immutable snapshots (`ChaosClickDiscsSnapshot`, `ChaosBubbleCentersSnapshot`); never touch UI state from the hook thread.
- **Hold-to-defuse exception:** those bubbles' clicks must NOT be swallowed; `GetAsyncKeyState` never sees a swallowed low-level click and the bubble would instantly detonate.
- **Avalonia** (`CCP.Avalonia/Platform/AvaloniaMouseHook.cs`): event-based and today ALWAYS calls `CallNextHookEx`, so **it cannot yet swallow clicks** - popping a bubble in the Avalonia head also delivers the click to the app underneath. **DECIDED 2026-07-09 (team review): this is no longer an open question** - the hook MUST gain a WPF-style swallow path so the per-region capture mask works (swallow inside the mask, pass outside). Preserve WPF's hold-to-defuse no-swallow exception. Implementation is the task-board row 'Per-region UCE input mask + hook swallow'.
- `AvaloniaMouseHook` raises its events SYNCHRONOUSLY on the hook thread. `AvaloniaBubbleService` marshals to the UI thread via `Dispatcher.UIThread.Post` before hit-testing; `AvaloniaFlashService` handles the callback directly on the hook thread. Either way, keep hook-path work cheap and thread-safe.

## Topmost management

- WPF: `Chaos/ChaosWindowZ.cs` (BornTopmost/PinTopmost/DesktopMode, `RaiseTopmost` via `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)`, `RaiseAboveVideo` only while video plays). Avalonia port: `CCP.Avalonia/Chaos/AvaloniaChaosWindowZ.cs` (Topmost pulse + `SetWindowPos`).
- The WPF video attention-target re-asserts `HWND_TOPMOST` every ~32ms on a timer; mandatory-video windows are interactive but get `WS_EX_NOACTIVATE` during chaos runs.
- Avatar windows need `ShowActivated = false` plus `SWP_NOACTIVATE` or they steal focus.

## Screen-capture exclusion (and deliberate inclusion)

Some overlays call `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE /* 0x11 */)` so they do not appear in screenshots/streams: WPF `KeywordHighlightService` and `OverlayService` (brain-drain windows, self-capture avoidance); Avalonia `AvaloniaKeywordHighlightService` (conditional on `OcrHighlightVisibleInCapture`). Preserve those when porting.

The counter-example matters just as much: **`SubliminalService` deliberately sets `WDA_NONE`** (WPF `SubliminalService.cs` ~:760, with an explanatory comment) so subliminal cards DO show up in the user's screen recordings, and to clear stale exclusion bits on pooled/reused windows. Do not "fix" subliminals by adding exclusion; their visibility in capture is a product decision, and the OCR feedback loop filters CCP's own windows by rect instead.

## Cross-platform status and plan

`CCP.Avalonia/Platform/AvaloniaPlatformCapabilities.cs` hardcodes `SupportsClickThrough = IsWindows`. There is ZERO Linux/macOS click-through code in the repo today; overlays are gated off elsewhere so they do not trap input.

The seam to implement is `CCP.Core/Platform/IOverlaySurface.cs` `SetClickThrough(bool)`: no-op in `AvaloniaOverlaySurface`, Win32 in `CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs`, WPF mirror in `CCP.WindowsOnly/WpfOverlaySurface.cs`. A Linux/macOS implementation mirrors that shape:

- **X11**: XShape input region (`XShapeCombineRectangles` with an empty input region) or XFixes input shape.
- **Wayland**: compositor-specific; input regions via `wl_surface.set_input_region(empty)` where the compositor honors it.
- **macOS**: `NSWindow.ignoresMouseEvents = true` (+ `level` for topmost).

All three MUST go through the `avalonia-research` skill first: check whether current Avalonia v12 exposes input-passthrough natively before hand-rolling interop (the repo's knowledge cites v11-era discussions #11911/#13827; v12 may have moved). Per the plan, off-Windows click-through is out of scope until Windows parity holds, and features must degrade gracefully (disable the overlay, never trap input).

## Verification

- Manual: start the effect, then click, type, and drag in another app THROUGH the overlay; minimize the main window and verify the desktop still responds (the foreground-activation trap); Alt-Tab must not list overlays; pop a bubble and check whether the click leaked underneath (known gap, see above).
- Both heads: run WPF side-by-side as the behavior reference.
- After any ex-style change, re-test window-state transitions (minimize/restore, monitor changes) because styles are re-applied on those paths.

For the complete catalog of every P/Invoke call site in both heads (file:line), read `references/callsite-catalog.md` in this skill.

## Related skills

- `unified-compositor-engine` - the single-surface architecture this input model serves
- `avalonia-research` - mandatory before any new platform interop
- `wpf-parity` - extracting exact WPF input behavior per overlay
