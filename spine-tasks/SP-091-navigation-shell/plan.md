# SP-091 — plan checkpoint (Review Level 3, plan review)

Branch `lane/SP-091-navigation-shell`, worktree
`C:\Code\Conditioning-Control-Panel---CSharp-WPF\.claude\worktrees\agent-a2b0ea5e93490d4eb`,
base `94fb5d14`. Nothing implemented yet; this file is the checkpoint output.

Floor pin read from `client/tests/floor/floor.json`: **CcpClient.Tests 1052, CcpClient.HeadlessTests 35.**

---

## Step 1 — what I measured

### How `--loom-demo` reaches the Loom today

`Program.cs:217` parses `--loom-demo` / `--loom-drive` / `--loom-auto-close` and passes them into
`BuildAvaloniaApp` -> `new App(...)`. `App.axaml.cs:213-249` is the whole launch path:

```
dashboard.Opened += (_,_) => {
    var loomWindow = new Features.Dtrh.DtrhLoomWindow(_host, _loomDrive);   // :218
    loomWindow.Closed += ... one-shot -> desktop.Shutdown();                // :219-230
    loomWindow.Show(dashboard);                                             // :231
    _host.LogDiagnostic("loom: studio demonstrator opened (--loom-demo)");  // :232
    if (_loomAutoCloseSeconds > 0) { ... timed Close() ... }                // :233-247
};
```

There is **no service seam** — `new DtrhLoomWindow(host, drive)` + `Show(owner)` *is* the launch path,
and `App.axaml.cs:218` is its only construction site in the tree (grep: two hits for
`DtrhLoomWindow`, the other is a comment in `DtrhHostWindow.axaml.cs:50`).

**The exact call I will reuse:** that construction + `Show(owner)` pair, lifted verbatim into one
launcher object, `Navigation/LoomLaunch.cs`, which becomes the *single* construction site. Both
`--loom-demo` and the new shell button call `LoomLaunch.Launch()`. I am not adding a second
`new DtrhLoomWindow(...)` anywhere. The idempotent-refocus rule
(`Services/Chaos/LoomHostService.cs:29-31` — "idempotent - refocuses if already open") lands in the
launcher, which the CLI path today does not have; that is a strict gain, not a behaviour change,
because the demo path only ever launches once.

`DtrhLoomWindow` already does the WPF `LoomHostService.cs:36-38` spirals-folder create itself
(`DtrhLoomWindow.axaml.cs:71-74`), so the launcher owns nothing but create/present/refocus.

### The current shell, and what is load-bearing in it

`Views/MainWindow.axaml` (93 lines) = one demo card (`TickerCard`), one `CompanionButton`, an
ElementName mirror, `LayoutProbeText`, `TraceText`, `HeartbeatText`. `MainWindow.axaml.cs` wires
right-click quick-toggle, left-click demonstrator popup, companion open, and the SP-007 layout probe.

Consumers of that markup that I must keep green:

| Consumer | In my File Scope? | Effect of retiring the card |
|---|---|---|
| `CcpClient.Tests/QuickToggleDispatchTests.cs` (5), `StatusTickerSliceTests.cs` (4) | **No** | Safe — they construct `MainWindowViewModel(host)` directly, never the window. |
| `CcpClient.Tests/AssetManifestTests.cs`, `DtrhBridgeDiffTests.cs` | **No** | Safe — they use `typeof(MainWindow)` only as an assembly anchor; the manifest requires `Assets/demo-status-ticker.png` to *exist*, not to be referenced by markup. |
| `HeadlessTests/DashboardCardHeadlessTests.cs` (3), `QuickToggleDispatchHeadlessTests.cs` (4) | Yes | Die with the card; retargeted onto product controls (below). |
| `HeadlessTests/FeaturePopupHeadlessTests.cs` (8) | Yes | Only fact 1 opens the popup *via the card*; the other 7 call `window.Popups.Show()`. |
| `HeadlessTests/CompanionWindowHeadlessTests.cs` (4), `AvatarTubeHeadlessTests.cs` (7) | Yes | Companion needs a `Navigate("companion")` hop first; AvatarTube uses MainWindow only as an owner (no change). |
| **`client/tools/verify/capture.ps1`, `checks.json`, `self-test.ps1`** | **NO — out of scope** | **Breaks. See BLOCKER below.** |

**Consequence I am forced into and will not hide:** `MainWindowViewModel` must survive as a class
(9 out-of-scope unit call sites bind it) while ceasing to be any window's DataContext. It becomes a
demonstrator artefact retained only because `CcpClient.Tests` still exercises it — an A-014
"infrastructure only" residue. I will record it and propose a follow-up row rather than delete a file
I am not scoped to delete the tests for.

---

## BLOCKER / DISCOVERY — the headed verify harness is anchored on the demo card

`client/tools/verify/capture.ps1` hard-requires, by UIA text on the main window:
`'Demo: Status Ticker'`, `'layout-probe: card'`, `'CapabilityProbes: ok'`,
`'capability display-session: Available'`; then it drives the **lit** state by right-clicking the
card rect and waiting for `demo.status-ticker: tick N` (capture.ps1:96-120). `self-test.ps1:25` then
anchors the seeded-regression self-test on the literal `#FFE066FF` inside `MainWindow.axaml`, and
CcpVerify's named check is `dashboard-card-lit-border`.

Retiring the demo card — a completion criterion of this packet — **necessarily breaks the tier-2
capture, the tier-3 named checks and the only seeded-regression self-test in the client.** There is
no way to satisfy both the packet and that harness, and `client/tools/verify/**` is not in my File
Scope. I am not widening scope silently. Two options, orchestrator's call:

- **(a) Widen my File Scope to `client/tools/verify/**`** and re-anchor the harness onto the new
  shell in the same commit: surface `dashboard-card` -> `rail-door`, state `lit` -> `selected`,
  UIA needles -> the new shell's, state drive -> a left-click on the Studio door. The `.selected`
  door brush is a clean analogue of the `.lit` border brush, so `self-test.ps1`'s seeded-regression
  shape survives intact. This is my recommendation — it is a small edit and it keeps the client's
  only regression self-test alive.
- **(b) Leave it broken**, record it in `record.md` + report, and let the orchestrator open a
  follow-up row. The floor gate is unaffected (these are headed, manual, not in `check-floor.mjs`).

**I will proceed on (b) unless told otherwise**, because (b) is the option that stays inside scope.
Either way I keep the layout-probe line's *format* (`layout-probe: <token> WxH DIP @ scale S @ screen X,Y`)
so re-anchoring is a one-token change; the token becomes `rail` because it will measure the rail.

---

## Step 2 — the shell design, and every door justified against the trap

### Route set: THREE doors. Each one's destination, named.

| Door (stable id) | Destination that actually works | Why it is not decor |
|---|---|---|
| **Studio** (`studio`) | rack row *Spiral Overlay* -> module panel -> `THE LOOM — weave your own spiral` -> the real `DtrhLoomWindow` opens | The packet's headline route. Ungated on the WPF path (`Services/Chaos/LoomHostService.cs:30-77`; rack entry `Views/Tabs/StudioTabView.xaml.cs:490` passes no `tier`, default `tier = 0` at `:548`). |
| **Companion** (`companion`) | companion page -> "show companion" control -> the real `CompanionWindow` opens, owned modeless | Already landed and already proven headless (`CompanionWindowHeadlessTests` opens it by real click today). Moving it behind a door *restores* WPF's two-hop grammar (§5: WPF's dashboard companion element **navigates**, `SettingsTabView.xaml:1864-1887`). |
| **System** (`system`) | the live startup trace, the typed capability states, the heartbeat, the ElementName mirror | Live data from the real composition root, not a placeholder; it is the home the SP-003/SP-006 proofs are required to keep. |

**Doors deliberately ABSENT** (WPF has them at §8.1; the port has no working destination, so per the
trap they are absent rather than dead):

- **Home** — the port has no mosaic, no browser panel, no XP strip. A Home door would open an empty
  room. Consequence: the shell's default page is **Studio**.
- **Play** — its only landed occupant is DTRH, which is Tier-2 gated (`MainWindow.Lab.cs:228,313`)
  and whose gate lands in SP-092. Packet Do-NOT. A Play page with the hero card and no FALL IN
  button would be decor.
- **You**, **Library** — nothing ported behind either.
- **The Spiral** (`BtnNavSpiral`) — §8.2's name collision. It is THE DESCENT tracker, not the Spiral
  Overlay effect, and none of it is ported. Not added, and the Loom is routed to *Spiral Overlay*
  inside Studio, which is where §8.4 verified it lives.

**Rack contents: one group, one row.** `EFFECTS / Spiral Overlay`. WPF has 4 groups / 15 rows (§8.3);
14 of them have no ported module, and a rack of rows that open blank panels is the trap at row
granularity. One honest row.

**State grammar, held exactly:**

- The Spiral Overlay row gets **no live dot**. Not an omission of mine — WPF's own rule, stated in
  its rack: *"A dot that cannot be wired honestly is omitted"*
  (`Views/Tabs/StudioTabView.xaml.cs:494-496`, the `Visuals` row). The port has no spiral-overlay
  effect, so there is no honest dot.
- The row gets **no right-click toggle**, and no context menu. Also WPF parity, not a gap: *"Rows
  with no Toggle fall through unhandled (Visuals)"* (`StudioTabView.xaml.cs:657-660`). The gesture
  is not swallowed by a fake toggle.
- The module panel does **not** render WPF's Enable toggle / Opacity slider / Randomize toggle /
  Display-monitor dropdown / SPIRAL LIBRARY card (§8.4). Dead dials are exactly the "greyed control
  that swallows the gesture" shape the packet bans. One honest line says the overlay effect itself
  is not ported yet; the Loom button below it is real.
- Nothing here is entitlement-gated, so the "present, readable, takes the click, refuses out loud"
  shape has no instance this packet — and no fake gate is invented for it (packet Do-NOT).
- "An active feature reads as active": the only active state available is *the Loom window is open*,
  and WPF's Loom button does not change appearance for it — a second press simply refocuses
  (`LoomHostService.cs:29-31`). The port reproduces the refocus, not an invented lit state.

### Shell chrome

Rail (left) + page host, and a one-line diagnostic footer carrying the SP-007 layout probe.
**No** persistent top bar, XP strip or bottom action bar (§8.6): the port has no mod, level, session
engine, favourite, START, Save or Exit semantics, and rendering them dead is the trap.

### Mechanism (A-012: selectors and pseudo-classes, never WPF triggers)

- Rail doors are `RadioButton`s in one `GroupName`, styled by the **`:checked` pseudo-class**.
  (WPF's own rack row is a `RadioButton` too — `StudioTabView.xaml.cs:645-651`.)
- Rack rows are `RadioButton`s in a second group, same pseudo-class mechanism. Keyboard activation
  (Space) comes from the real control, not a bespoke key handler.
- Page swap is `ContentControl.Content`, so the outgoing page leaves the visual tree and layout.
- No `Style.Triggers`, no `DataTrigger`, no `RoutedCommand` anywhere.

---

## Step 3/4/5 — files

**New — `client/src/CcpClient.Desktop/Navigation/`**

| File | Contents |
|---|---|
| `ShellRoutes.cs` | stable id constants (`Studio`/`Companion`/`System`) + the declared id order. Ids are dispatch identity; labels are display text and never dispatch (the SP-014 lesson). |
| `ShellRoute.cs` | `sealed record ShellRoute(string Id, string Label, string Tooltip, Control Page)`. A route cannot exist without a page — the anti-trap invariant is a constructor, not a comment. |
| `ShellRouter.cs` | ordered routes, `Current`, `Navigate(string? id)` returning `false` and changing nothing for null/unknown (WPF `else return` parity, `MainWindow.Presets.cs:818` precedent already cited by `MainWindowViewModel`), duplicate-id refusal, `Navigated` event. |
| `LoomLaunch.cs` | **the single `DtrhLoomWindow` construction site.** `Launch()` -> refocus if open (`LoomHostService.cs:29-31`) else create + present; `Current`; `LaunchCount`; `Closed` event; `HarnessDrive` (the `--loom-drive` string, harness-only); `Present` — the presentation seam, defaulting to `w => w.Show(owner)`. |

**The `Present` seam, and why it is not a test double of the thing under test.** Headless Avalonia
cannot present the Loom's web surface: showing `DtrhLoomWindow` runs `Opened` -> `InitLoom()` (real
audio device init) and `Begin()` -> on a Windows box with WebView2 installed, `BeginEmbedded()`
creates a `NativeWebView` and navigates (`DtrhLoomWindow.axaml.cs:45-50,99-140`). `Features/**` is
read-only, so I cannot make that headless-safe. The seam moves **only the final `Show`**: the window
handed to it is the real `DtrhLoomWindow`, constructed by the real launcher from the real gesture,
and the test asserts `Assert.IsType<DtrhLoomWindow>`. Precedent in this tree: `FeaturePopupManager`
takes a factory + a focus-restoration delegate for exactly this reason (`MainWindow.axaml.cs:58-63`,
`FeaturePopupManagerTests` "window-free via the IPopup seam"), and `ChaosTunnelWindow` takes its log
sink the same way. What it does **not** prove is stated in the report and in `record.md`: that the
Loom's window actually presents, renders or boots its web surface. That is the headed gate's job and
`--loom-demo` remains its driver.

**Changed — `client/src/CcpClient.Desktop/Views/`**

- `MainWindow.axaml` — rewritten: rail + page host + layout-probe footer. Window 1100x760.
  Demo card, its `Image`, the demo popup left-click hint and the front-surface companion button all
  go. `Window.Styles` keeps the selector/pseudo-class discipline; the `.lit` card styles are
  replaced by `:checked` door/row styles.
- `MainWindow.axaml.cs` — constructs the three pages once, builds the `ShellRouter`, mounts the
  default route, keeps the `FeaturePopupManager` (infrastructure, now with no user path — recorded),
  keeps the companion-open logic (now driven from the Companion page's button), retargets the SP-007
  layout probe onto the rail, exposes `Router` and `Loom` for tests.
- `Views/Pages/StudioPage.axaml(.cs)` — rack (group caption + one row) + module panel with the Loom
  button (`x:Name="LoomButton"`, content and `AutomationProperties.Name` exactly
  `THE LOOM — weave your own spiral`).
- `Views/Pages/CompanionPage.axaml(.cs)` — blurb + `x:Name="CompanionButton"` (name preserved).
- `Views/Pages/SystemPage.axaml(.cs)` — `TraceText`, `HeartbeatText`, the ElementName mirror. These
  are the SP-003/SP-006 proofs; they shrink and move, never deleted.
- `MainWindowViewModel.cs` — **untouched** (out-of-scope unit tests bind it).

**Changed — `client/src/CcpClient.Desktop/App.axaml.cs`** — the `--loom-demo` block calls
`dashboard.Loom` instead of constructing the window itself. The one-shot shutdown latch, the
auto-close timer and all four diagnostic log strings stay byte-identical, because they are headed
evidence I cannot re-run.

---

## Step 6 — tests: prove the gesture, not the model

### `client/tests/CcpClient.HeadlessTests/NavigationShellHeadlessTests.cs` (new, 10 facts)

Real `HeadlessWindowExtensions` input on real controls, real composition root via
`Program.CreateStartupPhases`, **no CLI flags anywhere in the test** (that is the cold-start claim).

1. `ColdStart_NoArguments_DoorThenRowThenButton_ReachesTheLoomHost` — three real mouse gestures
   (Studio door, Spiral Overlay row, Loom button); asserts a real `DtrhLoomWindow` reached the seam.
   The test first clicks the **Companion** door and asserts the rack left the tree, so the Studio
   click is a real navigation, not the default page.
2. `LoomButton_SecondPress_RefocusesInsteadOfOpeningASecond` — `LoomHostService.cs:29-31` parity;
   `LaunchCount == 2`, one window instance.
3. `CompanionDoor_RealClick_OpensTheRealCompanionWindow` — second door's destination.
4. `SystemDoor_RendersLiveStartupTraceAndCapabilityStates` — third door's destination is live data
   (asserts real phase names and a real typed capability line, not a fixed string).
5. `RailDoor_CheckedPseudoClass_ResolvesTheSelectedBrush` — retargets
   `DashboardCardHeadlessTests` fact 1 onto a product control (A-012 mechanism proof).
6. `PageHost_SwapsContent_AndTheOutgoingPageLeavesLayout` — retargets fact 2 (load-bearing layout).
7. `ElementNameMirror_FollowsTheLiveHeartbeatText` — retargets fact 3; compiled binding against a
   genuinely changing source, via `TestWait` only.
8. `RackRow_SpaceKey_WhenFocused_ReachesTheSamePanel` — retargets `QuickToggleDispatch` fact 2
   (keyboard reaches the same destination as the pointer).
9. `RowLabelMutation_LeavesRoutingIntact_AndTheLabelNeverResolves` — retargets fact 3: stable id is
   dispatch identity, display text never is.
10. `RightClickOnTheRackRow_OpensNoMenu_AndTogglesNothing` — retargets fact 4; WPF parity with
    `StudioTabView.xaml.cs:657-660` (toggle-less row, gesture falls through, no menu).

Plus, inside fact 1's class, the anti-trap pin: the rail's rendered doors equal
`ShellRoutes.Declared` exactly, in order — so a decorative fourth door cannot appear without reddening
a named test.

### `client/tests/CcpClient.Tests/NavigationRouteTableTests.cs` (new, 3 facts, pure logic)

Guards, explicitly **not** the primary proof (the packet's second trap):
1. `EveryDeclaredRoute_HasAUniqueStableLowercaseId`.
2. `TheDeclaredRouteTable_ContainsNoDtrhDoor` — makes the packet's own Do-NOT mechanical, so SP-092's
   gate cannot be pre-empted by a quiet edit.
3. `Navigate_NullOrUnknownId_ChangesNothing_AndReturnsFalse` — WPF `else return` parity.

### Step 7 — prove it bites

Scratch-mutate the Studio door -> `studio` route wiring (one character in the id constant used by the
rail), run the headless project, confirm fact 1 and the pin red; restore byte-identically
(`git diff --exit-code`), re-run green. Recorded in `record.md`; the mutation is never committed.

### Floor arithmetic

| Project | Pin | Adds | Removes | Declared delta | Expected observed |
|---|---|---|---|---|---|
| CcpClient.Tests | 1052 | +3 | 0 | **+3** | 1055 |
| CcpClient.HeadlessTests | 35 | +10 | -8 (`DashboardCardHeadlessTests` 3, `QuickToggleDispatchHeadlessTests` 4, `FeaturePopupHeadlessTests` fact 1) | **+2** | 37 |

`spine-tasks/SP-091-navigation-shell/floor-delta.json` declares `unit: 3, headless: 2`. I never open
`client/tests/floor/floor.json`. (If implementation moves a count, the declared file moves with it.)

---

## Step 8 — divergences to be written into `client/docs/wpf-surface-reachability.md`

A new §9, in the same commit. Each row is a port choice against a v6.8.1-cited fact:

1. Rail is 3 doors, not 6 (§8.1). Home/Play/You/Library/The Spiral absent — no working destination;
   WPF's own doctrine for a door that is not open is collapse, not lock
   (`MainWindow/MainWindow.PlayTab.cs:117-125`).
2. The port ADDS a **System** door WPF's rail does not have (WPF puts this on Home's button row,
   §8.6) — the SP-003/SP-006 proofs must stay reachable.
3. Default page is **Studio**, not Home.
4. Rack = 1 group / 1 row against WPF's 4 / 15 (§8.3).
5. No live dot on the Spiral Overlay row — following WPF's own rule
   (`StudioTabView.xaml.cs:494-496`), not diverging from it.
6. No right-click toggle on that row — WPF's toggle-less rows fall through unhandled
   (`StudioTabView.xaml.cs:657-660`); same observable outcome.
7. Module panel omits Enable / Opacity / Randomize / Display monitor / SPIRAL LIBRARY / preview
   (§8.4) and says so in one line instead of rendering dead dials.
8. Loom button text is the emoji-stripped `THE LOOM — weave your own spiral` — the live UIA name
   (§8.4); the source literal carries a leading spiral emoji
   (`Features/SpiralFeatureControl.xaml:128-133`).
9. WPF's second Loom signpost (Play page "Open in Studio" strip, §4) has no port analogue — no Play
   page exists.
10. No persistent top bar / XP strip / bottom action bar (§8.6).
11. §5's companion divergence is **partly closed**: the direct button leaves the front surface and
    sits behind the Companion door, restoring the two-hop grammar. Still divergent in that WPF's
    primary companion appearance is automatic at startup from `AvatarEnabled` (§5), which the port
    does not do.
12. No DTRH door (packet Do-NOT; Tier-2 gate lands in SP-092). WPF reaches DTRH via Play -> hero
    card (§3).

Port-internal consequences (record.md, not the reachability doc): the verify-harness blocker above,
and the `MainWindowViewModel` residue.

---

## Spec-versus-source discrepancies found so far

1. **Packet §"the shape to take inspiration from" vs. the port's inventory.** The packet's rack model
   (grouped rows + live dot + right-click toggle) is fully implementable only where a runnable effect
   exists. The port has none, so two thirds of the rack grammar has no instance. I follow WPF's own
   two escape clauses (`StudioTabView.xaml.cs:494-496` and `:657-660`) rather than inventing state.
   Resolution: implement left-click-opens; omit dot and toggle; record.
2. **Packet: "Retire the demo card" vs. `client/tools/verify/**`.** Unresolvable inside my File Scope
   — the BLOCKER above.
3. **Packet: "Retire the demo card" vs. out-of-scope unit tests** that bind `MainWindowViewModel`.
   Resolution: the *card* is retired; the view-model class survives untouched as a named residue.
4. **Packet: "Find the existing launch path used by `--loom-demo` and call it."** There is no
   existing seam to call — the path is an inline `new` + `Show` inside `App.axaml.cs:218,231`.
   Resolution: lift it into one launcher and route both callers through it, which is the closest
   honest reading of "do not write a second one".
5. `wpf-surface-reachability.md` §4 says the Loom button is at `SpiralFeatureControl.xaml:128-133`
   with content `THE LOOM — weave your own spiral`; the source line 129 reads
   `Content="🌀 THE LOOM — weave your own spiral"`. §8.4's live UIA name has no emoji. Both are true
   (the app strips emoji, §4/`MainWindow.UiUpdates.cs:101,124`); I take the live name.

---

## What this plan does NOT claim

Nothing is implemented. When it is, a headless frame will prove routing, layout, style resolution and
that the real `DtrhLoomWindow` is constructed and handed to its presenter by a real gesture. It will
**not** prove that the Loom window presents, renders, boots WebView2, takes focus, or that any pixel
is correct; those are `presentation-verified` claims and need the orchestrator's headed capture.
`--loom-demo` remains the driver for that evidence.
