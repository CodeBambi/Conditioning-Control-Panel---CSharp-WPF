# SP-095 — plan checkpoint (before any product edit)

Branch `lane/SP-095-remaining-doors`, worktree
`C:\Code\Conditioning-Control-Panel---CSharp-WPF\.claude\worktrees\agent-a14b9761280f25c7f`,
base `fbf61b17`.

## The verdict: ONE door, not three

| Surface | Verdict | Why |
|---|---|---|
| **Graded Intake** | **DOOR** | WPF exposes it as a rail entry AND as a Play-page card; both NAVIGATE to a destination page whose one button launches |
| **Chaos tunnel** | **NO DOOR** | WPF has no entry point either. It is a backdrop rendered *under* the classic Chaos run, spawned by `ChaosModeService`, gated on a settings checkbox. Nothing navigates to it |
| **AvatarTube** | **NO DOOR** | WPF never opens the companion from a dashboard gesture; the port's Companion door already answers the reachability question, and the port's AvatarTube window is a self-declared demonstrator |

---

## 1. Graded Intake — earns a door

### WPF evidence (re-read, not taken from the packet)

- Rail sub-entry: `MainWindow/MainWindow.xaml:811-812` — `<Button x:Name="BtnNavGradedIntake" Tag="GradedIntake" … Click="BtnNavGradedIntake_Click" ToolTip="{loc:Str tab_gradedintake}">`.
  `tab_gradedintake` = **"Graded Intake"** (`Localization/Languages/en.json:802`).
- Handler: `MainWindow/MainWindow.TabNavigation.cs:947` — `ShowTab("gradedintake")`. It **navigates**; it opens nothing.
- The entry lives under the **Play** door: `NavDoorMap` at `MainWindow.TabNavigation.cs:600-601`
  — `("play", "play", { "play", "deeper", "exclusives", "gradedintake", "lockdown", … })`.
- A **second signpost**, the Play page's Graded Intake card: `Views/Tabs/PlayTabView.xaml:1007-1010`
  ("Every state navigates to ShowTab(\"gradedintake\") - a Spent user has to be able to read why"),
  button `BtnPlayGradedIntake` → `Views/Tabs/PlayTabView.Cards.cs:83` → `ShowTab("gradedintake")`.
  Again: **navigates, never launches** — §1's rule at card granularity.
- The destination page: `Views/Tabs/GradedIntakeTabView.xaml`. Title literal **"Graded Intake"**
  (`:60`, deliberately not localized), sub-line `:67-68`
  *"A banded descent that reads how you answer - and how long you take - and drafts a personalised session from it."*,
  section head **"Begin a run"** `:126`, blurb `:128-129`, and the page's **one** launch button
  `BtnStartIntake`, content `✨ Begin Intake` (`:154-158`).
- The launcher: `MainWindow/MainWindow.Lab.cs:108-167` → `Services.Quiz.IntakeHostService.Launch(...)`.

So Graded Intake is a **two-hop WPF surface with a real destination page and exactly one launcher on
it** — the same shape the port already reproduces for the Loom (Studio door → rack row → button) and
DTRH (Play door → hero card → FALL IN). It earns a door.

### Why a rail DOOR and not a card on the port's Play page

WPF reaches the gradedintake **tab**; the Play card is a signpost to it, not a host for it. The port
cannot mount an Intake page behind a card, because `ShellRouteBinding.ValidateOrThrow` refuses a
mounted page that no door reaches (`Navigation/ShellRouter.cs:88-94`). And putting the launch button
straight on the port's Play page would make the Play card a **launcher**, which is precisely the rule
WPF states verbatim at `MainWindow/MainWindow.Presets.cs:1036`. So: door → page → one button.

The port's rail has no sub-entries, so WPF's sub-entry becomes a top-level door. That is a
**divergence**, recorded, not parity.

### What I will NOT port, and why (recorded as divergences)

1. **The weekly-pass gate.** WPF's `BtnStartIntake_Click:119-146` refuses when
   `App.IntakePass.CanStartIntake` is false, and `RefreshGradedIntakeGate` (`MainWindow.Lab.cs:368-434`)
   paints a hit-testable curtain (`GradedIntakeTabView.xaml:191-192`, `IsHitTestVisible="True"`, and
   `GradedIntakeGatedContent.IsEnabled = open`) — the OPPOSITE grammar from the DTRH band.
   The port has `Features/Intake/IntakePassService.cs` with all four states, and
   `IntakeHostWindow.axaml.cs:546` really spends the pass on completion — but **`CanStartIntake()` has
   zero product callers**. I am not wiring it in this packet, and the reason is not laziness:
   WPF's refusal is a claim about an **account** ("Patrons retake it as often as they like",
   `en.json:25-26`) with an unlock route (`intake_gate_spent_cta` → App Info & Data). The port has no
   account, no patron path and no App Info & Data page, so the same refusal would be a claim about
   the *install*, with no way for anyone to lift it — the mirror image of §10 D24's reasoning
   ("inventing a second grant condition out of nothing would be a worse answer than a recorded gap").
   Recorded with its close condition instead.
2. **The AI-availability gate** (`MainWindow.Lab.cs:148-153`, `App.Ai.IsAvailable`). The port has no
   `App.Ai` on this path; SP-054 landed the intake as a running flow without it. A hardcoded refusal
   would make the door dead on every machine.
3. **The Play page's second signpost card** (`PlayTabView.xaml:1007-1010`). One signpost this packet;
   absence recorded, same class as §9 D9.

### Shape

- `Navigation/ShellRoutes.cs`: new route id **`intake`**, label **"Graded Intake"**, tooltip = WPF's
  page sub-line. Rail position: **after Play**, because that is where WPF puts it (inside the Play
  door's entry list); System stays last (§9 D2).
- `Views/Pages/IntakePage.axaml{,.cs}`: sibling of `StudioPage`/`PlayPage`, same styles, same idiom.
  Carries the "Begin a run" section and one `BeginIntakeButton`.
- `Features/Intake/IntakeLaunch.cs`: **the one construction site** for `IntakeLaunchCoordinator`,
  the `Navigation/LoomLaunch.cs` / `Features/Dtrh/DtrhLaunch.cs` pattern verbatim — a lazily built
  `Coordinator`, a `LaunchCount`, and one `Open` seam (default `c => c.Launch()`) so a headless frame
  can prove the route without presenting a real WebView2 host window. Same seam class as
  `DtrhLaunch.Descend`.
- `Views/MainWindow.axaml{,.cs}`: `DoorIntake` (`AutomationProperties.Name="Intake door"`, which is
  what `capture.ps1:140-143` derives its door set from), page mounted, `Intake` exposed.
- `App.axaml.cs`: `--intake-demo` stops building its own coordinator and uses
  `dashboard.Intake.Coordinator` — the SAME object the button drives. Harness options travel through
  MainWindow as `IntakeHarnessOptions`, exactly as `DtrhHarnessOptions` already does.

---

## 2. The chaos tunnel — NO door, and the stale comment gets corrected

`Features/Chaos/ChaosTunnelDemoDrive.cs:12-13` says *"the greenfield dashboard has no Chaos game
entry point — typed named limit"*, which reads as a gap awaiting a door. The WPF source says
otherwise:

- `ConditioningControlPanel/Chaos/ChaosTunnelService.cs:20` — **"The endless three.js 'rabbit hole'
  tunnel rendered UNDER the whole Chaos game."**
- `:22-32` — a single non-topmost fullscreen window that sits below every Topmost game window,
  `WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW` so it **cannot take focus and is not in Alt-Tab**.
- `:34`, `:58` — gated on `ChaosTunnelEnabled` only, **default OFF**.
- Every caller is the classic descent's own service, never a UI gesture:
  `Services/Chaos/ChaosModeService.cs:345` (Preload under the countdown), `:518` (Show at run start),
  `:3042` (zone hint), `:3246` (CloseActive).
- Its **only** user-facing control is a checkbox in the Chaos hub ("the Warren"):
  `Chaos/ChaosHubWindow.xaml.cs:1566` (read) and `:1667` (write) — a setting, not a destination.
  The hub itself is only reachable from inside the classic descent (`MainWindow/MainWindow.Lab.cs:262-264`).

So WPF has **no tunnel entry point either**. A rail door to the tunnel would be a port invention with
no WPF counterpart — SP-091's trap. The port has not ported classic Chaos Mode, so there is no run
for the backdrop to sit under, which makes `--tunnel-demo` the correct and only way to render it.
The comment gets **corrected** (WPF has no entry point either; the missing thing is the Chaos RUN,
not a door) rather than satisfied.

---

## 3. AvatarTube — NO door

- WPF's companion window's primary appearance is **not a gesture**: created at startup when
  `AvatarEnabled` is true, `MainWindow/MainWindow.xaml.cs:2912` → `InitializeAvatarTube`
  (`MainWindow/MainWindow.Companion.cs:145`), and `AvatarEnabled` defaults true.
- The explicit control is the **Companion page hero card's** eye toggle →
  `MainWindow/MainWindow.CompanionRoom.cs:82` `SetAvatarEnabled`, plus the tray's "Wake Bambi Up!".
  There is no dashboard gesture anywhere (survey §5).
- The port already has a **Companion door** whose page carries "Show companion"
  (`Views/Pages/CompanionPage.axaml`), which is the ported two-hop analogue — §9 D11's first half is
  already closed.
- `Features/AvatarTube/AvatarTubeDemonstratorWindow.axaml.cs:11-22` declares itself a
  **DEMONSTRATOR** ("superseded by the first real AvatarTube feature, owner may async-veto") and
  carries Mode / Talk / Pause / Pack / Attach harness controls. Wiring a door to it is the packet's
  named trap.

So no door. What stays open and gets recorded: the port's companion window renders **no avatar**
(the engine is landed but not mounted on a product surface), and the startup-appearance half of
D11 is still divergent. Both are content/feature gaps owned by a future AvatarTube row — not by a
rail door.

---

## Files I will touch (all inside File Scope)

| File | Change |
|---|---|
| `client/src/CcpClient.Desktop/Navigation/ShellRoutes.cs` | the `intake` route |
| `client/src/CcpClient.Desktop/Views/MainWindow.axaml` | `DoorIntake` |
| `client/src/CcpClient.Desktop/Views/MainWindow.axaml.cs` | mount + door map + `Intake` property |
| `client/src/CcpClient.Desktop/Views/Pages/IntakePage.axaml{,.cs}` | NEW page |
| `client/src/CcpClient.Desktop/Features/Intake/IntakeLaunch.cs` | NEW single construction site |
| `client/src/CcpClient.Desktop/App.axaml.cs` | `--intake-demo` through the same launcher |
| `client/src/CcpClient.Desktop/Features/Chaos/ChaosTunnelDemoDrive.cs` | correct the stale comment |
| `client/docs/wpf-surface-reachability.md` | §11, D25+ |
| `client/tests/CcpClient.HeadlessTests/IntakePageHeadlessTests.cs` | NEW |
| `client/tests/CcpClient.HeadlessTests/NavigationShellHeadlessTests.cs` | rail pin gains the fifth door |
| `spine-tasks/SP-095-remaining-doors/{plan,record}.md`, `floor-delta.json` | packet artifacts |

Not touched: `client/tools/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`,
`Features/Dtrh/**`, `Entitlement/**`, `Tray/**`, `ConditioningControlPanel/**`.

## Tests planned

`IntakePageHeadlessTests` (headless, real input, cold boot, no CLI args):
1. cold start → `DoorIntake` click navigates → `BeginIntakeButton` click reaches `IntakeLaunch` and
   hands it the ONE coordinator;
2. the door navigates and launches nothing by itself;
3. two presses reach the one launcher and the one coordinator (no second coordinator);
4. the page carries WPF's own words (title/blurb/button) and no invented gate copy.

`NavigationShellHeadlessTests.TheRail_DeclaresExactlyTheDeclaredRoutes_AndNoDtrhDoor` gains
`DoorIntake` in its pinned array — the existing anti-drift pin, widened by the door that landed.

Bite test (step 5): break `DoorIntake`'s page mount in a scratch edit, confirm only the intake facts
red, restore byte-identically, do not commit.
