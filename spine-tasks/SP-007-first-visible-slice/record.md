# SP-007 — validate official migration checklist in first visible slice: record

**Task:** task-board row 6 (P0). **Worker:** kimi-coding/k3. **Date:** 2026-07-19.

---

## Design decisions (ratified by pre-approach consult)

- **Demonstrator interpretation:** "one real toggleable feature card" = a really-toggling DEMONSTRATOR card with stable ID `demo.status-ticker`, explicitly labeled as a demonstrator in the UI (A-004 stable identities). It is NOT named after a real WPF feature and implies no backend beyond its own tick operation. The first real feature card supersedes it in a later dashboard row. Owner may async-veto.
- **Left-click settings popup CARVED OUT** (A-005 per-window contract, deferred to dashboard/feature rows): no left-click handler is wired at all — a no-op would be a claim. The exclusion is recorded here and in the validation doc.
- **Wayland gate:** row-6 acceptance literally demands Linux-Wayland, but WSLg is XWayland-only (SP-006 session-probe facts) and proposal §5.1 Wayland opt-in is an open owner question. This task delivers Windows + WSL2/X11 acceptance with Linux-Wayland as a NAMED, DOCUMENTED gate; no Wayland backend opt-in, no acceptance-text narrowing. §5.1 surfaces in the board's "Decisions needed" list.
- **No schema bump for `DemoSettings.StatusTickerEnabled`:** unknown-member tolerance means an older file loads with default `false`; the store's `[JsonExtensionData]` preserves the member for older builds. DemoSettings' "not a feature model" doc comment stays honest because this is a labeled demonstrator.
- **No `Avalonia.Headless.XUnit`** (row 7's admission decision, proposal §6): interaction evidence comes from headed UIA on Windows + recorded WSLg observation; unautomatable items are named manual gates.
- **No new packages:** hand-rolled INPC view-model + hand-rolled `ICommand`; CommunityToolkit.Mvvm stays deferred (packet bans admissions outright).

## Pre-approach consult (solo Fable 5, 2026-07-19)

Full design outline submitted (toggle/restore/ring design, hand-rolled VM+ICommand, ElementName named-binding case, evidence plan incl. RenderScaling scaling probe, WSL2/WSLg plan, MCP-unavailability expectation). **First reply truncated mid-answer (b)** — known Fable behavior; missing (c)/(d) recovered with a pointed follow-up per SP-005/SP-006 precedent. Truncation labeled.

Verdict text (received, condensed): **"design is sound overall — proceed, with four corrections."**

(a) Toggle/restore/ring — three fixes, ALL APPLIED:
1. **`SetEnabled` must be idempotent** — `owner.Begin()` on an already-enabled ticker cancels the live generation and starts a second tick loop (re-entrant double-toggle race). Guard both directions like `HeartbeatParticipant.StartAsync`.
2. **Restore-start must use the skip-until-bound pattern** — restored-ON starts the tick loop in phase 3, before the phase-4 bind; the loop must check `_ui.IsBound &&` exactly as `HeartbeatParticipant.TickLoopAsync` does, or the restored-ON path throws on first post.
3. **"Operation state" must be the owner, proven by the outcome** — a `bool Lit` set by the toggle handler rebuilds the persisted-flag lie one level up. Derive lit from `owner.IsLive(generation)`; tests assert the full chain toggle-off → owned completion completes `OperationOutcome.Cancelled` → ring unlit. Tick text advancing = headed proof the operation is real; typed Cancelled = proof stopping is real.
Minor: toggle path order `SetEnabled` → `Mutate` → `Save()` (ring responds immediately; Save not awaited on UI thread). No schema bump needed — record explicitly (done above).

(b) Hand-rolled INPC + ICommand: **"correct, and not actually a judgment call"** — the packet bans any package admission, so CommunityToolkit.Mvvm is out regardless; a ~15-line RelayCommand is the right size. (Reply truncated here; no contrary content received.)

(c) — follow-up, received complete: `#Root.Title` (ElementName) satisfies the packet's literal wording ("`ElementName` or `$parent[...]`"); cheat sheet maps WPF `ElementName` → `#name` as an equal citizen. **BUT `Title` is static — observed text can't distinguish a resolving binding from a hardcoded literal.** Applied: the named/ancestor case binds to something that CHANGES at runtime (window subtitle bound to the card's live tick TextBlock via ElementName `#TickText.Text`), so a delta proves resolution.

(d) — follow-up, received complete: **the compiled-binding item itself is the biggest markup-presence risk** (`x:DataType` + running app proves nothing; reflection bindings render identically). Closing observation: with `AvaloniaUseCompiledBindingsByDefault=true`, seed one deliberately wrong binding path, record the AVLN compile FAILURE, restore, record green build — the negative proof. Applied (throwaway-build observation in Step 4). Runner-up risk: `avares://` asset ("Image tag present" ≠ rendered) — closed by stream-open test + headed pixel observation.

No rejection of the overall outline. Skipped: nothing requested was declined.

## WPF quick-toggle parity digest (wpf-parity; outcomes only, no mechanics)

From `ConditioningControlPanel/Features/FeatureCard.xaml.cs:248-261` and `ConditioningControlPanel/MainWindow/MainWindow.Presets.cs:797-825` (VERIFIED by direct read):

- Plain right-click anywhere on an unlocked, toggleable card body IMMEDIATELY reverses the feature's enabled state — no popup, no context-menu choice. Right-clicks originating inside the help button are swallowed (help never toggles).
- When the engine is running, quick-toggle also starts/stops the actual service; flipping only the persisted flag is an inert-UI failure (owner's words encoded in the code comment).
- The flag is saved immediately and the card's lit/unlit border updates immediately (settings-change notification path; result stays correct across language/theme/mod changes and app restart).
- Dispatch is by card OBJECT identity, never by localized title — the first attempt's title-string dispatch and CanTest context-menu substitution are the REJECT lessons this slice avoids (first-attempt-lessons.md:59-67,161).
- Locked cards ignore quick-toggle; `Visuals`/`System` are neutral (no aggregate on-state). This slice has one demonstrator card only; locked/neutral semantics are later dashboard rows.

## v12 research (avalonia-research protocol)

All pages fetched 2026-07-19 and verified current (A-012 freshness: migration index April 2026, cheat sheet June 2026):

- Migration index: https://docs.avaloniaui.net/docs/migration/wpf/ — conceptual shifts confirmed.
- Cheat sheet: https://docs.avaloniaui.net/docs/migration/wpf/cheat-sheet — §Styling (triggers→pseudo-classes, `Button.primary:pointerover`), §Data binding (`#name`, `$parent[Type]`, compiled bindings), §Commands (no RoutedCommand, direct `ICommand`, `KeyBinding` same concept), §Layout (`IsVisible=false` == WPF Collapsed, removed from layout; WPF Hidden → Opacity=0), §Events (pointer events, tunnel for Preview*, `e.Handled` same), §Platform services (`TopLevel.Screens`), gotcha 9 (`avares://AssemblyName/path`).
- Deeper: https://docs.avaloniaui.net/docs/styling/pseudoclasses (selector syntax, `PseudoClasses.Set` is protected → use style `Classes` from the VM for the lit state), https://docs.avaloniaui.net/docs/basics/data/data-binding/compiled-bindings (x:DataType + global csproj flag, compile-time validation), https://docs.avaloniaui.net/docs/basics/user-interface/assets (`AssetLoader.Open(new Uri("avares://..."))`).
- **Scaling — source-verified (12.1.0):** `TopLevel.RenderScaling` is get-only (`src/Avalonia.Controls/TopLevel.cs:514`); Win32 scaling comes from `GetDpiForMonitor` (no override); X11 honors the official `AVALONIA_GLOBAL_SCALE_FACTOR` env override (`src/Avalonia.X11/Screens/X11Screens.Scaling.cs:206-211`) and `Xft.dpi` via `XrdbScalingProvider`. This machine's three monitors ALL report scale 1.0 (GetDpiForMonitor, recorded). Plan: 100% measured on Windows; 150% measured on WSL2/X11 via `AVALONIA_GLOBAL_SCALE_FACTOR=1.5`; Windows-150% named manual gate.
- Local ref-assembly XML docs (`~/.nuget/packages/avalonia/12.1.0/ref/net10.0/*.xml`) are the API-signature authority during implementation.

## Steps 2–5 evidence

### Implementation summary
- `Features/StatusTickerParticipant.cs` — the demonstrator (stable ID `demo.status-ticker`). Phase-3 start applies the restored flag (store starts earlier in registration order — restore-then-start IS the ordering). `SetEnabled` is idempotent both directions (double-toggle guard, consult fix 1). The tick loop is an SP-004 owned operation: `owner.Begin()` + `RunAsync("status-tick")`, skip-until-bound + stale-check-inside-post discipline copied from `HeartbeatParticipant` (consult fix 2), terminal outcome explicitly `Cancelled` when the token is observed at the loop check (a test caught `Completed`-on-toggle-off — fixed). `IsOperationLive` = the ring authority (owner generation liveness; consult fix 3).
- `Views/MainWindowViewModel.cs` — hand-rolled INPC + one hand-rolled `ICommand` (no packages; `CanExecuteChanged` inert via empty add/remove to keep 0 warnings). Toggle order: `SetEnabled` → `Mutate` → `Save()` (never awaited on UI thread). `TickerLit`/`TickerVisible` derive from `IsOperationLive`, never the flag.
- `Views/MainWindow.axaml` — dashboard: card (`Border.feature-card` selectors: base / `:pointerover` / `.lit` / `.lit:pointerover`; conditional class `Classes.lit="{Binding TickerLit}"`), KeyBinding Enter, `avares://` PNG image, load-bearing `IsVisible` tick row, `#TickText.Text` ElementName mirror, layout-probe line; retained SP-003/004/006 trace/capability/heartbeat surface below (shrunk, not deleted). NO left-click handler (carve-out).
- `Views/MainWindow.axaml.cs` — right-click quick-toggle (PointerPressed + RightButtonPressed + Handled), layout probe (`LayoutUpdated` → DIP bounds + `RenderScaling` + `PointToScreen`, invariant-culture text; logged once via `host.LogDiagnostic`).
- `Persistence/DemoSettings.cs` — `StatusTickerEnabled` added, NO schema bump (unknown-member tolerance; recorded decision).
- `Lifecycle/CompositionRoot.cs` — ticker registered AFTER the store (factory restructured to hold the store reference).
- `Lifecycle/ApplicationHost.cs` — `LogDiagnostic` (content-free diagnostic surface for the harness).
- `Assets/demo-status-ticker.png` — generated 64×64 neon-ring glyph (PowerShell System.Drawing, one-time on Windows).

### Test output — Windows (contract testCommand)
Build: **0 Warning(s), 0 Error(s)** (net10.0, SDK 10.0.302). Tests: **85/85 passed** (78 SP-003…006 intact incl. updated participant-count assertions + 7 new slice tests: toggle on/off with typed Cancelled + tick advance + ring-from-operation, idempotence/no-second-generation, toggle-before-start throws, file-content persistence incl. post-teardown file re-read, restart-restore through the real composition root + mid-operation teardown Cancelled, capability surface intact (never not-probed), avares stream + PNG magic).

### Test output — WSL2 Linux (in-packet gate)
Ubuntu 26.04, SDK 10.0.110, native `~/ccp-sp007` copy (never /mnt/e). Build **0W/0E** (after fixing 4 xUnit1051 analyzer warnings — `TestContext.Current.CancellationToken`); tests **85/85 passed**. Session facts for the run: `WAYLAND_DISPLAY=wayland-0`, `DISPLAY=:0`, `XDG_SESSION_TYPE` empty (WSLg = Wayland session with X11 via XWayland; no Wayland backend claim), kernel 6.6.114.1-microsoft-standard-WSL2.

### Compiled-binding negative proof (validation item 3)
Seeded `Classes.lit="{Binding TickerLitDOESNOTEXIST}"` → build FAILED `AVLN2000: Unable to resolve property or method of name 'TickerLitDOESNOTEXIST' on type 'CcpClient.Desktop.Views.MainWindowViewModel'`; restored → 0W/0E. Compiler involvement is proven, not inferred from `x:DataType` presence (consult point d applied).

### Headed Windows smoke (2026-07-19, `headed-smoke.ps1`, SMOKE PASS)
Real input (SendInput mouse_event, SendKeys), UIA text/bounds reads, pixel observations:
- capability surface + card + layout probe render; SP-003/004/006 texts intact.
- Probe: `card 488.0x77.0 DIP @ scale 1` (this machine: all 3 monitors scale 1.0 via GetDpiForMonitor — recorded environment fact).
- avares asset: 32×32 image bounds (UIA).
- unlit `#3A2F3E` 966px → `:pointerover` `#6B5B73` 960px (real mouse move) → lit `#E066FF` 958px.
- right-click quick-toggle: tick appears and ADVANCES 2→6 (real operation, UIA-observed).
- `IsVisible` load-bearing: card 77 → 102 DIP on toggle-on, exactly 77 again on toggle-off.
- ElementName mirror followed the live tick (`…tick 7`).
- keyboard path: click-to-focus + Enter toggled OFF (bounds reverted) and ON (tick 10).
- teardown mid-operation: exit 0; settings file contained `"statusTickerEnabled": true` (flush).
- restart: operation running from restored flag (tick 9→13), lit ring restored (958px), exit 0.

### Headed WSLg observation (2026-07-19; X11 session facts, NO Wayland claim)
- Session facts: `WAYLAND_DISPLAY=wayland-0`, `DISPLAY=:0` — the app's own capability surface renders "linux wayland session with X11 offered via XWayland … session facts only — not a claim about the selected Avalonia backend" (observed in the capture).
- Renders for real: XGetImage captures `artifacts/wslg-x11-scale1-window.bmp` / `wslg-x11-scale15-window.bmp` — card + asset + demonstrator label + full trace + advancing heartbeat (`tick 1404`) all visible.
- Scaling measured: default run `card 488.0x71.0 DIP @ scale 1`, X window 520×680; `AVALONIA_GLOBAL_SCALE_FACTOR=1.5` run `card 488.0x70.7 DIP @ scale 1.5`, X window 780×1020 (= ×1.5 exactly). Card DIP height differs from Windows (71 vs 77 — font metrics; honest platform difference).
- NOT observable there (named, not claimed): UIA tree (Avalonia Linux exposes no UIA — interaction evidence is Windows-headed + unit-level), graceful close (no xdotool/wmctrl, no passwordless sudo; instance signal-terminated — named manual gate), RAIL pixel capture from the Windows side (GDI/PrintWindow produce black bitmaps — observed; Linux-side XGetImage used instead).

### A-013 MCP advisory (bounded, redacted)
The avalonia MCP IS connected in this worker session (53 tools). Sent ONE small redacted AXAML fragment (card + selectors + bindings only; no repo paths/secrets/proprietary code):
- `ValidateXaml` (strict): heuristic pass ("compiled bindings with x:DataType - excellent"). **Accepted:** nothing new — consistent with the real compiler's verdict (which is the authority and actually ran). Its `.axaml` extension tip already satisfied.
- `AnalyzePerformance` (xaml): **REJECTED** — self-contradictory output: "Invalid XAML syntax - cannot analyze performance" AND "Score: 90/100" in the same report (fragment without a Window root breaks its XML parser, yet it still emits an authoritative-looking score). The exact A-013 failure mode; recorded as evidence for the MCP-audit row.
- `GetServerInfo`: "AvaloniaUI MCP Server v1.0". Version-pin note: upstream pins Avalonia 11.3.1 heuristics (A-013); nothing version-specific was adopted. Real v12 validation = the 12.1.0 compiler (0W/0E + the AVLN2000 negative proof).

### K3 visual verification (app-visual-verification, targeted level 2)
Reviewed `windows-uia-scale1-card-unlit.png` and `windows-uia-scale1-card-lit.png` against the dashboard-design contract. **VERDICT: PASS.** Definite defects: none. Matches: dark dashboard surface; unlit card = dark/unlit border with readable artwork; lit card = neon accent border + restrained glow; demonstrator label explicit; tick row present only when lit; no clipping/truncation; hierarchy clear. Interaction gates screenshots cannot prove were covered by the headed smoke (toggle, keyboard, teardown). WSLg captures additionally reviewed: same grammar holds under X11 at both scales.

### Surprises
1. **Avalonia exposes no UIA automation peers for Border/StackPanel/Grid on this build** (only Window/Text/Image appear) — `AutomationProperties.Name` on the card is useless for UIA. The layout probe (app-measured bounds + `PointToScreen`) became the card locator; recorded here so later rows don't burn time rediscovering it.
2. **The app opens UNACTIVATED behind existing windows**; UIA reads it fine but pixel captures read the occluder (first run "verified" the terminal — false-positive pixel match within tolerance!). `SetForegroundWindow` is foreground-locked from a background process; the window is fully covered so clicks can't raise it. Working harness pattern: `SetWindowPos(HWND_TOPMOST)` on `proc.MainWindowHandle` (UIA's `NativeWindowHandle` is a bogus huge value on this build). Pixel checks then verify content themselves.
3. **WSLg RAIL windows are invisible to GDI `CopyFromScreen`/`PrintWindow`** (black bitmaps, CAPTUREBLT included). Linux-side `XGetImage` via python3 ctypes + libX11 captures real content; no extra packages needed.
4. `pkill -f "dotnet run"` matches the invoking `bash -lc` command line itself — self-kill (exit 15) twice. Use `pgrep -f "CcpClient[.]Desktop"` bracket patterns.
5. WSL `/tmp` did not persist a helper script across wsl invocations (VM teardown); helpers live in `~/`.

### Known accepted gaps
- Windows-150% scaling: named manual gate (all monitors 1.0; RenderScaling get-only; no supported override). 150% measured on WSL2/X11 instead.
- WSLg graceful close: named manual gate (no xdotool/wmctrl; no passwordless sudo). Teardown contract proven by unit tests (both platforms) + Windows headed exit 0.
- WSLg interaction (right-click/keyboard): not automated there — Windows-headed + unit evidence; WSLg observation is render/scale/session-facts only, recorded as such.

## Engine reviews
`spine_review_step` called after Steps 1, 2, 3 (plan): all **skipped=true, reviewLevel=0, spawnFailed=false** — seventh consecutive batch (SP-001…SP-007) with zero engine reviews; T-2 remains open. Fable solo consults are the active quality gate per the packet.

## Pre-completion consult (solo Fable 5, 2026-07-19)

Full as-built summary submitted (implementation, evidence set, named gates, surprises, board plan). Verdict received complete (no truncation): **"the slice is sound and the evidence set is honest — proceed to land, with two small annotations and one board correction check. No rework required."**

(a) **Residual capability-lie audit: none found.** Scaling split legitimate (real measured bounds at both factors; Windows-150% a named gate, not a claim). WSLg observation correctly scoped to render/scale/session-facts. Two annotations, BOTH APPLIED:
1. **Keyboard path was click-focused, not Tab-focused** — the toggle keystroke is real, but focus arrived via pointer. Tab-reachability untested; keyboard toggle itself observed. Focus traversal design belongs to the dashboard rows.
2. **The stderr probe line says `scale 1` on the 1.5 run** — the one-shot first-layout log races the scaling override; the LIVE window text (read from the XGetImage capture: `@ scale 1.5`) is the observation. Recorded here explicitly so the log line is not misread later.

(b) **Layout probe: KEEP** — it exists because Avalonia exposes no UIA peers for Border/Grid/StackPanel (observed platform fact); it is the only reproducible card locator and the scaling observations depend on it. Slice tooling, not scope creep. **Supersedure note (applied):** the real dashboard row replaces it with a proper automation-peer strategy (a custom control with real peers) and the probe line leaves the window then. `ApplicationHost.LogDiagnostic` stays (one method, stated privacy constraint).

(c) **Board plan satisfies reconciliation** — with one correction the advisor verified: the §5.1 Wayland opt-in question is NOT in Decisions-needed (the display-backends entry is adjacent but different), so it must be ADDED, not just checked. Row → `WIP`, evidence cites this record, names the Linux-Wayland gate in the row text itself, never narrows the original acceptance wording, never `DONE`.

### Annotations applied (per consult)
- Tab-reachability untested (pointer-acquired focus; keystroke itself real) — (a)1.
- stderr `scale 1` vs live `scale 1.5` first-layout race — (a)2.
- Layout-probe supersedure to the dashboard rows — (b).

## Board reconciliation
`client/docs/task-board.md` row "Validate official migration checklist in first visible slice" → **WIP** with evidence citing this record and naming the Linux-Wayland gate (never `DONE`; owner ratification + Wayland gate remain). "Decisions needed" gained the §5.1 Wayland opt-in question (was absent — pre-completion consult verified). No other docs needed edits; no durable surprise beyond those already recorded above (port-lessons candidates: no-UIA-peers + occluded-window capture pattern — left in this record for the port-lessons owner row, not appended here).
