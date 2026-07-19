# Migration checklist validation — first visible slice (SP-007)

Task-board row 6 / A-012: every item of the official WPF→Avalonia migration checklist is exercised
by the first visible slice (dashboard window + `demo.status-ticker` demonstrator card) with a
**named observation** — an item claimed from markup presence alone is a contract violation.

Status values: `pending` · `observed` · `manual-gate` (named, exact instructions, never claimed).

Official sources (all fetched 2026-07-19, verified current):
- Migration index: https://docs.avaloniaui.net/docs/migration/wpf/ (A-012: last updated April 2026)
- WPF→Avalonia cheat sheet: https://docs.avaloniaui.net/docs/migration/wpf/cheat-sheet (A-012: last updated June 2026)
- Deeper pages per row below.

| # | Checklist item | Where exercised | Named observation | Official citation | Status |
|---|---|---|---|---|---|
| 1 | Selector/pseudo-class state (no WPF triggers) | Card ring: `Border.feature-card`, `:pointerover`, `.lit`, `.lit:pointerover` selectors in `MainWindow.axaml`; `lit` is a conditional class bound to the OPERATION state | Headed Windows smoke (SendInput mouse move): border pixel counts — unlit `#3A2F3E` 966px → pointer-over `#6B5B73` 960px → lit `#E066FF` 958px. Each state delta observed in rendered pixels, not markup | cheat sheet §Styling; https://docs.avaloniaui.net/docs/styling/pseudoclasses; https://docs.avaloniaui.net/docs/styling/style-classes (conditional `Classes.lit="{Binding …}"`) | observed |
| 2 | Compiled bindings incl. named/ancestor case | `x:DataType` on the Window root + `Classes.lit`, `IsVisible`, `Text`, `Command` bindings; ElementName case `{Binding #TickText.Text}` (window mirror line) | The mirror text FOLLOWED the live tick: UIA-observed `ElementName mirror: demo.status-ticker: tick 7` while the tick advanced — a runtime delta a hardcoded literal cannot produce | cheat sheet §Data binding (`#name` syntax); https://docs.avaloniaui.net/docs/basics/data/data-binding/compiled-bindings | observed |
| 3 | Compiled bindings PROVEN compiled (negative proof) | `AvaloniaUseCompiledBindingsByDefault=true` (csproj) | Seeded `Classes.lit="{Binding TickerLitDOESNOTEXIST}"` → build FAILED with `AVLN2000: Unable to resolve property or method of name 'TickerLitDOESNOTEXIST' on type 'MainWindowViewModel'`; restored → 0W/0E. Compiler involvement proven | https://docs.avaloniaui.net/docs/basics/data/data-binding/compiled-bindings (compile-time validation) | observed |
| 4 | One direct `ICommand` (no `RoutedCommand`/`CommandBinding`) | `MainWindowViewModel.ToggleCommand` (hand-rolled `ICommand`) — the ONE command path (A-004) for right-click quick-toggle and the keyboard KeyBinding | Both input paths executed the same command: real SendInput right-click → tick 2→6 advanced; focused-card Enter → toggled off then on. No second dispatch path exists | cheat sheet §Commands (no RoutedCommand; bind commands directly) | observed |
| 5 | `IsVisible` layout intent (collapse removes layout) | Tick `TextBlock` `IsVisible="{Binding TickerVisible}"` inside the card | Measured card bounds delta: 77 → 102 DIP on toggle-on, reverts to exactly 77 on toggle-off (app-measured layout probe; Windows UIA cross-check). The row leaves layout; the card shrinks | cheat sheet §Layout (`IsVisible=false` == WPF `Collapsed`, removed from layout) | observed |
| 6 | `avares://` asset (no pack URIs) | `Assets/demo-status-ticker.png` (`AvaloniaResource` glob), `<Image Source="avares://CcpClient.Desktop/Assets/demo-status-ticker.png"/>` | (a) Unit test: `StandardAssetLoader.Open(uri)` returns a real stream with PNG magic bytes (89 50 4E 47…); (b) headed: 32×32 image bounds via UIA; (c) visible in the lit/unlit screenshots AND the WSLg XGetImage capture | cheat sheet gotcha 9; https://docs.avaloniaui.net/docs/basics/user-interface/assets (`AssetLoader.Open`) | observed |
| 7 | Pointer input (right-click quick-toggle) | `PointerPressed` handler on the card, explicit `PointerUpdateKind.RightButtonPressed` check, `e.Handled = true` — WPF parity outcome (FeatureCard.xaml.cs:248-261) | Real mouse_event right-down/up at card center (headed): tick text appeared (`tick 2`) and advanced (2→6); no popup/context menu appeared. WPF parity outcome reproduced | cheat sheet §Events (pointer events, routing, `e.Handled`) | observed |
| 8 | Keyboard input path | Focusable card + `KeyBinding Gesture="Enter"` → same `ICommand` | Real input: left-click focuses card body, `{ENTER}` → tick row vanished (bounds reverted to 77), second `{ENTER}` → tick 10 running again | cheat sheet §Commands (`KeyBinding` same concept) | observed |
| 9 | Scaling | App-measured layout probe (card DIP bounds + `RenderScaling`) + pixel sizes | 100% (Windows): `card 488.0x77.0 DIP @ scale 1`, UIA cross-check 488×77 px; all 3 monitors report scale 1.0 (GetDpiForMonitor — environment fact). 150% (WSL2/X11, official `AVALONIA_GLOBAL_SCALE_FACTOR=1.5`): `card 488.0x70.7 DIP @ scale 1.5`, X window 780×1020 px (= 520×680 × 1.5 exactly). DIP bounds scale-invariant; physical pixels scale exactly | cheat sheet §Platform services; Avalonia 12.1.0 source `src/Avalonia.X11/Screens/X11Screens.Scaling.cs:206-211` (env override), `src/Avalonia.Controls/TopLevel.cs:514` (RenderScaling get-only) | observed (Windows-150%: manual-gate) |
| 10 | Teardown | Single guarded teardown (SP-003/SP-004 contracts); mid-operation window close | Headed: ticker running (tick 10) → CloseMainWindow → exit 0, settings file contained `"statusTickerEnabled": true` (flush). Unit: owned completion terminates `OperationOutcome.Cancelled` through the real composition root | startup-shutdown-contract §6; async-lifecycle-fault-contract §6 | observed |

## Carve-outs and gates (named, never claimed)

- **Left-click settings popup:** carved out (A-005 per-window contract; dashboard/feature rows own it).
  No left-click handler exists on the card; a wired no-op would be a capability lie. The headed
  keyboard-path click focuses the card only.
- **Linux Wayland:** WSLg is XWayland-only (SP-006 session-probe facts, re-observed this task:
  the app's own capability surface reports "linux wayland session with X11 offered via XWayland…
  session facts only — not a claim about the selected Avalonia backend"). `Avalonia.Wayland` opt-in
  is open owner question §5.1 (architecture-proposal). Wayland evidence is a named gate — this task
  records X11 session facts only and never fakes Wayland.
- **Windows 150% scaling:** all three monitors on this machine report scale 1.0 (recorded fact);
  `TopLevel.RenderScaling` is get-only in 12.1.0 (source-verified) and no supported Windows override
  exists. The 150% measurement is delivered on WSL2/X11 via the official env override.
  Windows-150% remains a named manual gate for the verification-harness row (row 7): set a
  monitor to 150% in Windows Settings, re-run `headed-smoke.ps1`, expect identical DIP bounds and
  1.5× pixel bounds.
- **WSLg graceful close:** no `xdotool`/`wmctrl` in the WSL distro (no passwordless sudo to
  install) — the WSLg instance was terminated by signal; graceful window-close on WSLg is a named
  manual gate. The teardown CONTRACT is proven by unit tests green on WSL2 (85/85 incl. the typed
  Cancelled teardown assertions) and the headed Windows exit-0 close.
- **WSLg pixel capture path:** RAIL surfaces are invisible to GDI/PrintWindow captures (black
  bitmaps — observed). Linux-side `XGetImage` (python ctypes, libX11) captured the real rendered
  window content.
- **`demo.status-ticker` is a demonstrator**, explicitly labeled; the first real feature card
  supersedes it in a later dashboard row. "One real toggleable feature card" was interpreted as
  *really-toggling demonstrator card* (owner may async-veto).
