# Quick-toggle dispatch contract

**Status:** implemented on the demonstrator card (`demo.status-ticker`) — SP-014.
**Scope:** one card, one command path, contract + demonstrator evidence. Multi-card
acceptance awaits real feature cards (named limit below). Authority: architecture A-004;
capability-inventory §Feature-card interaction.

## The replaced mechanism

The first Avalonia attempt dispatched quick-toggle on **localized display text**:

- `ConditioningControlPanel/CCP.Avalonia/Views/Tabs/SettingsTabView.axaml.cs:371-462` —
  `switch (card.Title)` (line 382) with every case comparing against
  `LocalizationManager.Instance.Get("feature_title_*")`. A title change (language switch,
  rewording, casing) falls to `default: return` and the card silently stops toggling.
- `ConditioningControlPanel/CCP.Avalonia/Features/FeatureCard.axaml.cs:368-386` — cards
  with `CanTest` open a **context menu on plain right-click** instead of toggling
  (`OpenContextMenu()`, :398-418). Plain right-click must never open a menu.
- Owner report: some quick toggles did not work (`first-attempt-lessons.md` §"UI code
  presence is not interaction parity", VERIFIED). Dispositions: REJECT localized-title
  dispatch and context-menu substitution; ADAPT WPF's interaction outcomes via stable
  feature commands.

WPF ground truth dispatches on **reference identity**
(`MainWindow.Presets.cs:797-826`: `card == SettingsTab.CardFlash` …) — an identity, never
display text. The reference comparison is WPF mechanics; the *outcome* (dispatch keys on
identity) is what this contract ports.

## Contract

1. **Stable card ID is the ONLY dispatch key.** Every toggleable card carries a stable,
   non-localized identifier (demonstrator: `demo.status-ticker`,
   `StatusTickerParticipant.FeatureId`). Display titles, artwork, capitalization, and
   visual-tree position never participate in dispatch. Title text is bound display text
   (`CardTitle` — mutable; rewording/localization territory) and mutating it cannot affect
   resolution (title-mutation negative test).
2. **ONE command path.** Plain right-click on the card body, keyboard Enter (focused),
   and — when they exist — popup toggles, accessibility, and automation all execute the
   same command with the card's stable ID, resolved through the single
   `QuickToggleDispatch` map (`Features/QuickToggleDispatch.cs`). No per-gesture or
   per-surface switch statements.
3. **Immediate toggle.** A single plain right-click press reverses the state at once. No
   context menu, no confirmation, no intermediate choice. `e.Handled = true`.
4. **Ring from operation liveness.** The lit ring derives from the SP-004 operation
   authority (`IsOperationLive`), never the persisted flag (SP-007 rule).
5. **Persistence.** The flag mutates through the SP-005 store and saves; file-content
   proof (`"statusTickerEnabled": true`), restore-on-restart.
6. **Exception taxonomy** (contract-only in the client today — no such cards exist here;
   no fake cards synthesized):
   - **Locked cards** reject quick-toggle and suppress the active ring even if the flag is
     on. WPF: `FeatureCard.xaml.cs:255` (`if (IsLocked) return;`) and
     `FeatureCard.xaml.cs:230-237` (`showActive = IsActive && !IsLocked`). Product note:
     current WPF force-unlocks dashboard cards (`MainWindow.UiUpdates.cs:638-645` —
     code-supported, product-inactive).
   - **Help button** never toggles and never opens the popup: clicks originating inside it
     are swallowed before any card event is raised (WPF `FeatureCard.xaml.cs:239-246` and
     `:250-252`); it opens help only.
   - **Visuals/System** are neutral — no single enabled state, no dispatch entry, ring
     stays neutral. WPF: `MainWindow.Presets.cs:818` (`else return`) and `:792-793`.
   - **Unknown or neutral IDs** presented to the dispatch are a silent no-op — WPF
     `else return` parity (`MainWindow.Presets.cs:818`), not error swallowing.
7. **Click-region parity.** The toggle region is the whole card body except the help
   button (WPF `OnRightClick` fires from anywhere on the card). The title-text region is
   body region: right-clicking the displayed title dispatches identically (proven draw-level
   by clicking over the mutated title text).
8. **No plain-right-click context-menu substitution.** The card carries no `ContextMenu`/
   `ContextFlyout`; test/advanced actions (when they exist) use separate buttons or an
   explicit modified gesture and never consume plain right-click.

## Demonstrator evidence (SP-014)

- Unit (`CcpClient.Tests/QuickToggleDispatchTests.cs`): stable-ID resolution toggles the
  real SP-004 operation + SP-005 file-content persistence; **title-mutation negative
  test** (mutate `CardTitle` → dispatch still resolves via ID; old and mutated title
  strings never resolve); capitalization/whitespace/null near-misses never resolve;
  unknown/neutral IDs silent no-op; one-entry dispatch + command/dispatch convergence.
- Headless draw-level (`CcpClient.HeadlessTests/QuickToggleDispatchHeadlessTests.cs`):
  real routed right-click press toggles (immediate, no menu intermediate); focused
  `KeyPressQwerty(Enter)` toggles the same operation (load-bearing proof that
  `KeyBinding.CommandParameter={Binding CardId}` resolved); mutated title renders on the
  card while right-click over the title text still toggles; no `ContextMenu`/
  `ContextFlyout` exists or opens.
- Windows-headed smoke (`spine-tasks/SP-014-quick-toggle-dispatch/`): real SendInput
  right-click toggles, ring pixel flip, persistence file proof, **toggle while the SP-013
  modeless popup is open**, no-context-menu negative, title-region click parity.

## Named limits (row stays WIP)

- **Languages:** the client has NO localization system (A-014 honest absence; SP-009/SP-010
  verified). Language-switch invariance is not demonstrable; the falsifiable stand-in is
  the title-mutation negative test (mutating the displayed title cannot break dispatch).
- **Themes:** one theme exists; theme-invariance is unproven (dispatch touches no theme
  resources — the claim is structural, not exercised).
- **Exception classes:** locked/help/Visuals/System are CONTRACT-ONLY with WPF evidence
  above — none are demonstrable with one demonstrator card; no fake cards were synthesized.
- **Session:** no session concept exists in the client. "Live-starts/stops during a
  session" is proven as: real SP-004 operation liveness changes on toggle (SP-007 rule)
  + right-click toggle while the SP-013 modeless popup is open. The WPF-session sense
  (`App.IsEngineRunning` gating service start/stop) remains a named limit.
- **Multi-card acceptance:** one card, one entry. The full row acceptance (every card, all
  languages/themes, running session) awaits real feature cards and supersedes this
  demonstrator.
