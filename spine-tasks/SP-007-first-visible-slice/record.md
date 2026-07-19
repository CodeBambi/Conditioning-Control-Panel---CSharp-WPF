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

(To be filled as work proceeds.)

## Engine reviews

(To be filled: `spine_review_step` presence/absence per row T-2.)
