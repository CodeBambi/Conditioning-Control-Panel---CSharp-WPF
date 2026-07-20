# SP-014 record — replace card-title quick-toggle dispatch

## Step 1 — Archaeology of the title-keyed mechanism

### The mechanism being REPLACED (first-attempt, READ-ONLY evidence)

Title-keyed dispatch lives in the first Avalonia attempt, not the greenfield client:

- `ConditioningControlPanel/CCP.Avalonia/Views/Tabs/SettingsTabView.axaml.cs:371-462` —
  `OnFeatureCardToggleRequested` runs `switch (card.Title)` (line 382) where every case
  compares against `LocalizationManager.Instance.Get("feature_title_*")` — **dispatch keyed
  on localized display text**. Any title change (language switch, rewording, casing) falls
  to `default: return` (line ~456) and the card silently stops toggling.
- Same file, `:476-478` — `OnFeatureCardTestRequested` repeats the localized-title
  comparison for the mandatory-video test action.
- `ConditioningControlPanel/CCP.Avalonia/Features/FeatureCard.axaml.cs:368-386` —
  `OnPointerPressed`: plain right-click raises `ToggleRequestedEvent` ONLY when
  `!Shift && !CanTest`; **cards with `CanTest` open a context menu on plain right-click**
  (`OpenContextMenu()`, `:398-418`, localized menu items). This is the banned
  plain-right-click context-menu substitution.
- Owner report + lesson record: `client/docs/first-attempt-lessons.md` §"UI code presence
  is not interaction parity" (lines 57-67) — "some quick toggles do not work" VERIFIED.

### WPF ground truth (READ-ONLY, File.cs:line)

- Card construction/registration: `ConditioningControlPanel/Views/Tabs/SettingsTabView.xaml:299-376`
  — 12 `FeatureCard`s declared with `x:Name`; 10 toggleable with `x:FieldModifier="internal"`
  (CardFlash, CardVideo, CardSubliminal, CardSpiral, CardLockCard, CardPinkFilter,
  CardMindWipe, CardBubblePop, CardBouncingText, CardBubbleCount); CardVisuals/CardSystem
  stay private — never referenced by dispatch code.
- Right-click path: `ConditioningControlPanel/Features/FeatureCard.xaml.cs:248-258`
  (`OnRightClick`) — swallows right-clicks originating inside `BtnHelp` (:250-252),
  `if (IsLocked) return;` (:255), `e.Handled = true`, raises `ToggleRequestedEvent`.
  **No ContextMenu anywhere in the file.** Left-click twin: `OnClick` :239-246 (same
  help-button swallow, raises `ClickEvent`).
- Dispatch wiring: `ConditioningControlPanel/MainWindow/MainWindow.xaml.cs:559-561` —
  grid-level `AddHandler(FeatureCard.ToggleRequestedEvent, OnFeatureCardToggleRequested)`.
- The dispatch itself: `ConditioningControlPanel/MainWindow/MainWindow.Presets.cs:797-826` —
  keys on **reference identity** (`card == SettingsTab.CardFlash` …), flips the persisted
  flag, live-starts/stops the service when `App.IsEngineRunning`, `App.Settings.Save()`,
  then `RefreshFeatureCardActiveStates()`. Non-toggleable cards hit
  `else return; // Visuals / System cards have no single on/off toggle.` (:818).
- Ring: `MainWindow.Presets.cs:778-793` `RefreshFeatureCardActiveStates` — `IsActive` from
  settings flags; `FeatureCard.xaml.cs:230-237` `ApplyActiveState` —
  `showActive = IsActive && !IsLocked` (locked suppresses the ring even if the flag is on).
- Exception classes:
  - **Locked:** right-click is a no-op (`FeatureCard.xaml.cs:255`); ring suppressed
    (:230-237). Product note: `MainWindow.UiUpdates.cs:638-645` — "velvet-mosaic dashboard
    cards are never locked anymore" (IsLocked forced false; the locked path is
    code-supported, product-inactive).
  - **Help:** clicks/right-clicks originating inside `BtnHelp` are swallowed before any
    event is raised (`FeatureCard.xaml.cs:239-246`, `:250-252`); the button opens the help
    popover only (`RefreshHelpTooltip` → `HelpPopover.Attach`, :195-212).
  - **Visuals/System:** no dispatch entry — `else return` (`MainWindow.Presets.cs:818`);
    ring stays neutral (`MainWindow.Presets.cs:792-793` comment). Left-click still opens
    their popups (`MainWindow.Presets.cs:893-896`, `:943-946`).

### First-attempt lesson dispositions for this mechanism

`client/docs/first-attempt-lessons.md` §"UI code presence is not interaction parity"
(lines 57-67) already disposes this exact mechanism:

- **REJECT** implementation-by-markup (declared handlers as evidence) — ACCEPTED here:
  dispatch is proven by falsifiable tests + headed input, not handler presence.
- **REJECT** localized-title dispatch — this row's center; the greenfield replacement keys
  on a stable card ID end-to-end with a title-mutation negative test.
- **REJECT** plain-right-click context-menu substitution (`CanTest` branch) — contract
  requires plain right-click toggles immediately; negative proof in Step 3.
- **ADAPT** WPF's interaction outcomes using stable feature commands — ADAPTED as the
  stable-ID command path (WPF's reference-identity switch is WPF mechanics; the *outcome*
  — identity, never display text — is what ports).

Architecture authority: `client/docs/architecture.md` A-004 (stable feature identities +
one quick-toggle command path; titles/themes never participate; smallest command surface,
no speculative framework) and capability-inventory §Feature-card interaction.

### Greenfield pre-state (what SP-014 changes)

- `client/src/CcpClient.Desktop/Views/MainWindow.axaml` — card Border `TickerCard` with
  `KeyBinding Gesture="Enter" Command="{Binding ToggleCommand}"` (no parameter); title is a
  hardcoded AXAML literal.
- `MainWindow.axaml.cs` — right-click `PointerPressed` → `vm.ToggleCommand.Execute(null)`;
  left-click → SP-013 popup. No title keying exists, but no stable-ID keying either: the
  path is hard-wired to the one card with NO dispatch key at all. SP-014 makes the stable
  ID the explicit dispatch key end-to-end (falsifiably: title strings must NOT resolve).
- `StatusTickerParticipant.FeatureId = "demo.status-ticker"` already exists as the stable ID.

### Pre-approach consult (solo; requested Fable 5 — ACTUAL answering model recorded per T-7 rule)

**Verdict: design approved — proceed; the Execute(parameter)→TryToggle shape is the honest
minimal form.** Guards applied:

1. `KeyBinding.CommandParameter={Binding CardId}` should resolve like the headed-proven
   `Command` binding, but a silently-broken parameter binding would make Enter a silent
   no-op (unknown/null → swallow). The headless `KeyPress(Enter)` toggle test is therefore
   LOAD-BEARING, not optional.
2. No keyless bypass: `vm.Toggle()` (internal) becomes the action the dispatch resolves
   to; no test may invoke it directly as a toggle path — all toggle assertions route
   through `ToggleCommand.Execute(id)` or `dispatch.TryToggle(id)` so "one command path"
   stays literally true in the suite.
3. Silent no-op on unknown/null ID is WPF parity (`else return`, Presets.cs:818) — the
   contract doc must state that equivalence explicitly so it isn't read as swallowed error.
4. CardTitle mutation is the fair stand-in for the language-switch failure mode because it
   is asserted at BOTH layers: the VM source of the displayed text AND (headless) the
   rendered TextBlock text.
