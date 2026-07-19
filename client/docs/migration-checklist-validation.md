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
| 1 | Selector/pseudo-class state (no WPF triggers) | Card ring: `Border.feature-card:pointerover` + `Border.feature-card.lit` selectors in `MainWindow.axaml`; `lit` class toggled from operation state | pending | cheat sheet §Styling (triggers→pseudo-classes); https://docs.avaloniaui.net/docs/styling/pseudoclasses | pending |
| 2 | Compiled bindings incl. named/ancestor case | `x:DataType` view-model bindings + ElementName case `#TickText.Text` (window subtitle follows the LIVE tick text — a delta, not a constant) | pending | cheat sheet §Data binding (`#name` syntax, `CompiledBinding`); https://docs.avaloniaui.net/docs/basics/data/data-binding/compiled-bindings | pending |
| 3 | Compiled bindings PROVEN compiled (negative proof) | `AvaloniaUseCompiledBindingsByDefault=true` (csproj); throwaway build with one deliberately wrong path must FAIL with AVLN error, restore → green | pending | https://docs.avaloniaui.net/docs/basics/data/data-binding/compiled-bindings (compile-time validation) | pending |
| 4 | One direct `ICommand` (no `RoutedCommand`/`CommandBinding`) | `DemoToggleCommand` (hand-rolled `ICommand`) — the ONE command path for right-click quick-toggle AND keyboard toggle (A-004) | pending | cheat sheet §Commands (no RoutedCommand; bind commands directly) | pending |
| 5 | `IsVisible` layout intent (collapse removes layout) | Tick `TextBlock` `IsVisible="{Binding TickerVisible}"`; card height measurably shrinks when off (bounds delta) | pending | cheat sheet §Layout (`IsVisible=false` == WPF `Collapsed`, removed from layout) | pending |
| 6 | `avares://` asset (no pack URIs) | `Assets/demo-status-ticker.png` (`AvaloniaResource`), rendered via `<Image Source="avares://CcpClient.Desktop/Assets/demo-status-ticker.png"/>`; stream-open test + headed pixel observation | pending | cheat sheet gotcha 9 (`avares://AssemblyName/path`); https://docs.avaloniaui.net/docs/basics/user-interface/assets (AssetLoader) | pending |
| 7 | Pointer input (right-click quick-toggle) | `PointerPressed` handler, explicit right-button check (`PointerUpdateKind.RightButtonPressed`), `Handled=true` — WPF parity outcome (FeatureCard.xaml.cs:248-261) | pending | cheat sheet §Events (pointer events, routing, `e.Handled`) | pending |
| 8 | Keyboard input path | Focusable card + `KeyBinding` (Enter) → same `ICommand` | pending | cheat sheet §Commands (`KeyBinding` same concept) | pending |
| 9 | Scaling | Measured card bounds at 100% (Windows, 3 monitors all scale 1.0 — environment fact) and 150% (WSL2/X11 via official `AVALONIA_GLOBAL_SCALE_FACTOR=1.5`; source-verified `X11Screens.Scaling.cs` 12.1.0) | pending | cheat sheet §Platform services (`Screens.Primary.Scaling`); Avalonia 12.1.0 source `src/Avalonia.X11/Screens/X11Screens.Scaling.cs` | pending |
| 10 | Teardown | Mid-operation window close → operation cancelled with typed `OperationOutcome.Cancelled`, settings flushed, exit 0 | pending | startup-shutdown-contract §6; async-lifecycle-fault-contract §6 | pending |

## Carve-outs and gates (named, never claimed)

- **Left-click settings popup:** carved out (A-005 per-window contract; dashboard/feature rows own it).
  No left-click handler exists on the card; a wired no-op would be a capability lie.
- **Linux Wayland:** WSLg is XWayland-only (SP-006 session-probe facts); `Avalonia.Wayland` opt-in is
  open owner question §5.1 (architecture-proposal). Wayland evidence is a named gate — this task
  records X11 session facts only and never fakes Wayland.
- **Windows 150% scaling:** all three monitors on this machine report scale 1.0 (recorded fact);
  `TopLevel.RenderScaling` is get-only in 12.1.0 (source-verified) and no supported Windows override
  exists. The 150% measurement is delivered on WSL2/X11 (official env override); Windows-150% remains
  a named manual gate for the verification-harness row.
- **`demo.status-ticker` is a demonstrator**, explicitly labeled; the first real feature card
  supersedes it in a later dashboard row. "One real toggleable feature card" was interpreted as
  *really-toggling demonstrator card* (owner may async-veto).
