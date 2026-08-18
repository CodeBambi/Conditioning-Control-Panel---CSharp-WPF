# SP-094 — plan checkpoint (written BEFORE the first product edit)

Branch `lane/SP-094-dtrh-route-and-gate`, worktree
`.claude/worktrees/agent-afeee60116cc3e607`, base `655397f0`.
Floor pin read from `client/tests/floor/floor.json`: **1090 unit / 39 headless**.

---

## 1. The route I will build

Four hops, the same shape SP-091 gave the Loom, and the same two-hop WPF grammar
(`MainWindow/MainWindow.Presets.cs:1007,1036` — tiles and doors navigate, one button on the
destination page launches):

```
rail door "Play"  ->  PlayPage  ->  FALL IN / Quick Drop  ->  gate  ->  DtrhLaunchCoordinator
   (DoorPlay)        (the hero card)                                    (LaunchWithPickerAsync /
                                                                         QuickStartAsync)
```

- `Navigation/ShellRoutes.cs`: add `Play = "play"`, declared **between Companion and System**
  (WPF rail order is Home, Studio, Companion, Play, You, Library — §8.1; System is the port's
  own door, D2, and stays last). Label `Play`, tooltip `Games, modes, and the deep end.`
  (the page's own live sub-line, §8.5).
- `Views/Pages/PlayPage.axaml{,.cs}`: sibling of `StudioPage`, mounted in `MainWindow` and
  reached by `DoorPlay` in the rail markup. `ShellRouteBinding.ValidateOrThrow` runs in both
  directions at composition, unchanged.
- `Features/Dtrh/DtrhLaunch.cs`: the gate + **the single construction site** for
  `DtrhLaunchCoordinator` (the `LoomLaunch` pattern). `--dtrh-demo` in `App.axaml.cs` stops
  constructing its own coordinator and uses this one.

### The card (WPF `Views/Tabs/PlayTabView.xaml`, live capture §8.5)

| Element | Port | Citation |
|---|---|---|
| Tier badge | `PRIME SUBJECT` (the observed wording, never "TIER 2") | `Controls/TierBadge.cs:21` ("the art carries the words"), `PlayTabView.xaml:409-411` (`Tier="2"`, top-left) |
| Title | `DOWN THE RABBIT HOLE` | `PlayTabView.xaml:426` — see discrepancy D-1 below |
| Blurb | verbatim, "it's right there. it's always been right there. …" | `PlayTabView.xaml:435` |
| `FALL IN` | pink primary, tooltip "you were always going to." | `PlayTabView.xaml:455,458` |
| `Quick Drop` | outlined, tooltip "skip the dollhouse. fall straight in with your saved settings" | `PlayTabView.xaml:468-469` |
| Announcements / 3D game checkboxes | **OMITTED** | see divergence list |
| Lock band | a refusal band: scrim `#A8120A1E` (~66%), 1px rim `#FFB47BFF`, corner 15, **`IsHitTestVisible="False"`** | `PlayTabView.xaml:244,248,251-258,260-262,508-512` |

## 2. The gate, and the trap that decides the packet

`Features/Dtrh/DtrhGate.cs` is a **pure function** over `EntitlementOutcome` (so the branch
proof lives in `CcpClient.Tests` with no Avalonia runtime), consumed by `DtrhLaunch` before
either coordinator call — gate FIRST, exactly where WPF puts it
(`MainWindow/MainWindow.Lab.cs:228` for FALL IN, `:313` for Quick Drop, both the first
statement in the handler).

It consumes the outcome through `EntitlementOutcome.Match`, which does not compile with a
branch missing. Three outcomes, three different observable results:

| Outcome | Port | Why |
|---|---|---|
| `Entitled(tier)` with `tier >= Lab` | **proceed** | `TierGate.RequiresLab` is a tier-2 bar (`Services/TierGate.cs:88-94`) |
| `Entitled(Supporter)` | refuse, **tier message** | a tier-1 patron IS an authority answer, and WPF refuses them with the same string |
| `NotEntitled` | refuse, **tier message**: "Down the Rabbit Hole is a Tier 2 perk - upgrade your pledge to unlock it." | `Services/TierGate.cs:128,133`; `en.json:4704` verbatim |
| `Unavailable(reason)` | refuse, **a DIFFERENT message** that says the port could not verify entitlement, names which part could not be told, and says in words that this is not a refusal | SP-092's whole point |

**`Unavailable` is the only branch a real user hits today** (the tier authority is
`UnconfiguredTierSource`, so a readable login yields `Unavailable(tier-authority-absent)` and
an absent shipping app yields `Unavailable(host-app-data-absent)`). It gets:

- its own per-reason-code explanation for all ten `EntitlementReasonCodes` values;
- its own **named** unit test and its own **named** headless test;
- a table-driven guard asserting that **no** reason code's message ever contains "not a
  patron", "no pledge" or "upgrade your pledge" — so a code added later cannot silently fall
  into the refusal wording.

**The card takes the click.** `FALL IN` and `Quick Drop` are never disabled, never greyed, in
any branch. The refusal band is `IsHitTestVisible="False"` (`PlayTabView.xaml:512` semantics)
and a second click after a refusal still arrives — asserted. WPF's one genuinely disabled part
is the checkbox pair (`MainWindow/MainWindow.PlayTab.cs:88-91`), and the port has no
checkboxes to disable.

**Refusal surface.** WPF raises an 8s Warning toast with a "See tiers" action opening App Info
& Data (`Services/TierGate.cs:133`). The port has no toast system and no App Info & Data page,
so the refusal prints into the band on the card, out loud, and is logged with
`EntitlementOutcome.Describe()` (class + reason CODE only). Divergence recorded.

## 3. THE TRAY QUESTION — I take **(b): do not tuck**, and here is why

WPF hides the main window into the tray the moment the DTRH window opens and restores it on
close (`Services/Chaos/DtrhHostService.cs:156` -> `MainWindow/MainWindow.RemoteControl.cs:1517`
-> `Services/Notifications/TrayIconService.cs:145-148`; restore at `DtrhHostService.cs:998`).

Option (a) is rejected on four grounds, in order of weight:

1. **The board already blesses (b), and the board outranks the packet.** Board row (P2, "The
   tray capability is narrower than WPF's tray"): *"Acceptance when taken: `ITrayPresence`
   grows a menu surface with at least the wake-the-companion and exit items, **or the tuck is
   deliberately not shipped until it can, recorded either way**."*
2. **(a) would still not be parity, so it buys a bigger partial, not a smaller gap.** WPF's
   tuck also fires a balloon on its first-ever invocation
   (`TrayIconService.cs:152-157` — the comment at `RemoteControl.cs:1515` says "No
   notification", the code says otherwise, and the code is the behaviour). `ITrayPresence` /
   `TrayIconRequest` have no balloon surface either. A menu-only (a) diverges on the balloon
   *and* leaves the true parity item — WPF's menu has "Show Dashboard", "Wake Bambi Up!",
   separator, "Exit" (`TrayIconService.cs:96-109`), and §5 records that wake item as one of the
   three ways a user reaches the companion — only half-built.
3. **Every user-visible claim (a) would make is a headed claim I am forbidden to make.**
   `TrackPopupMenu` needs a real click on a real icon; "the window left the taskbar and came
   back" is a composited-window fact. The board's own acceptance for the tray row is *"the
   Windows half proven headed … captured"*. I would be shipping a hide-the-window path whose
   only way back I cannot exercise — the exact stranding the packet disqualifies.
4. **The port would grow more bespoke Win32 that a filed board row already wants to shrink**
   (P2, "Linux tray should REUSE Avalonia's shipped DBus backend"). And `ITrayPresence` on
   Linux is `UnsupportedTrayPresence`, so (a) ships a Windows-only behaviour split.

**What the port does instead**, so the outcome WPF owes is still delivered: the shell is
**plain-minimized** when the DTRH host window opens and **restored to its prior state** when
that window closes — no `Hide()`, no tray, taskbar button kept the whole time. This is not
invented: it is the port's own landed precedent for exactly this situation,
`Features/Intake/IntakeHostWindow.axaml.cs:120-162` ("Plain MainWindow minimize (explicitly
NOT tray tuck)", prior state recorded, Maximized comes back Maximized), which shipped through
six headed runs at SP-054 with the intake host window owned by the same shell.

`ITrayPresence` is **not wired by this packet**, and that is stated rather than left implicit.

## 4. Divergences I will write into `wpf-surface-reachability.md` §9

| # | v6.8.1 fact | Port at SP-094 | Reason |
|---|---|---|---|
| D14 | Rail is six doors; §9 D1 recorded Play as ABSENT because it had no ported destination | Rail is **four** doors; Play lands | D1's condition is discharged for Play only — Home/You/Library/The Spiral stay absent |
| D15 | D12: "no DTRH door … closes at SP-092" | closed | the gate landed; the door is gated, not ungated |
| D16 | The band advertises the lock BEFORE the click ("Lab only", `PlayTabView.xaml:508-512`) | the band appears AFTER the refusing click and carries the refusal text | the port cannot know a tier without an async read; a band painted from a cached read would advertise a state it had not re-checked |
| D17 | Refusal is an 8s toast with a "See tiers" action opening App Info & Data | refusal is text on the card; no toast system, no App Info & Data page, and a "See tiers" button with nowhere to go is a dead control | |
| D18 | The card carries an Announcements and a 3D-game checkbox, disabled when gated | **absent** | neither setting is ported, and the 3D toggle selects a classic WPF descent the port does not have. Dead dials are the greyed control that swallows the gesture (the §9 D7 rule) |
| D19 | The title carries a NEW pill (`PlayTabView.xaml:432`) | absent | nothing about this surface is new in the port |
| D20 | Main window is tucked into the tray on open and restored on close | plain minimize + restore; **no tray, no balloon, no tray menu** | the tray decision above |
| D21 | `Unavailable` has no WPF analogue at all — WPF fails CLOSED when the Patreon service is null (`Services/TierGate.cs:88-94`), i.e. it renders "could not tell" as "no" | the port refuses with a distinct message that says it could not verify | this is a deliberate IMPROVEMENT on WPF, not parity, and is recorded as such |

## 5. Spec-vs-source discrepancies found at plan time

- **D-1 (title).** The packet points at §8.5, which records the hero title as `THE RABBIT
  HOLE`. The source string is `🐇 DOWN THE RABBIT HOLE` (`PlayTabView.xaml:426`), §3 of the
  same document records `DOWN THE RABBIT HOLE`, and in `play-page.jpg` the left of the title is
  occluded by the onboarding card and the badge (the visible glyphs are "…E RABBIT HO…").
  **Resolution: `DOWN THE RABBIT HOLE`, emoji-stripped per the §9 D8 rule.** The §8.5 reading
  is an occlusion artefact, not an observation that beats source.
- **D-2 (packet vs. code).** The packet says `HostLoginEntitlement` "is currently registered
  nowhere" and that I am its first consumer — confirmed. `Lifecycle/CompositionRoot.cs` is
  **outside my File Scope**, so I will construct the capability in the shell
  (`Views/MainWindow.axaml.cs`, in scope) and **not** register a capability probe for it. That
  registration is a separate packet; I will name it as unfinished rather than widen scope.
- **D-3 (an over-grant hole in the dependency I consume).** A filed board row (P2, "Two review
  findings the WIRING packet must not inherit") states its acceptance as *"the wiring packet
  … the entitlement `Entitled` arm rejects undefined tier values with a test that feeds it
  one"*. `Entitlement/**` IS in my File Scope. I will close it (`Enum.IsDefined` in the
  `Entitled` arm of `HostLoginEntitlement.AskAuthorityAsync`) with a test that feeds it
  `(EntitlementTier)0` and `(EntitlementTier)99`. It is the one hole in that capability that
  leans toward GRANTING paid content, and wiring the first consumer over it would be
  negligent. Recorded here as a deliberate, board-authorised scope item.

## 6. Tests, and how each gate branch is proven

`CcpClient.Tests` (pure, no Avalonia) — new `DtrhGateTests.cs`:

1. `Entitled(Lab)` proceeds; `Entitled(Supporter)` refuses with the tier message.
2. `NotEntitled` refuses with WPF's verbatim `en.json:4704` string.
3. **`Unavailable_TierAuthorityAbsent_…`** — the named test for the branch a user hits today:
   distinct message, names the code, never the tier wording.
4. Closure table: every `EntitlementReasonCodes` constant yields a distinct non-empty
   explanation and none of them carries refusal wording.
5. The undefined-tier over-grant (D-3), fed through the REAL `HostLoginEntitlement`.

`CcpClient.HeadlessTests` — new `PlayPageHeadlessTests.cs`, driven by **real headless input**
on the real controls from a cold `Program.CreateStartupPhases` boot with no CLI flags, with
the REAL `HostLoginEntitlement` in the loop (its reader and authority seams carry local
doubles — nothing stubs the OUTCOME, and no test reads the developer's real store):

1. Entitled: door -> FALL IN -> the **real** `DtrhSlotPickerWindow` opens (the real
   coordinator, the real picker; the descent stops there so no WebView2 host is constructed).
2. `NotEntitled`: nothing opens, band visible, band text is the tier message.
3. **`Unavailable`: nothing opens, band visible, band text is NOT the tier message** and says
   the port could not verify — the named test.
4. The card takes the click: both buttons `IsEnabled` in every branch, band
   `IsHitTestVisible == false`, and a second click after a refusal still arrives.
5. Quick Drop refuses on both refusal branches and does not open the picker.
6. `TheRail_…` in `NavigationShellHeadlessTests` updated for the fourth door.

**Prove it bites** (step 6): collapse `Unavailable` into the `NotEntitled` arm in a scratch
edit, confirm the named tests red, restore byte-identically (`git diff --exit-code`), rerun.
The mutation is never committed.

Then `pwsh client/tools/verify/self-test.ps1` (run, never edited), and
`node client/tests/floor/check-floor.mjs` — which will report `1090 + my declared unit delta`
and `39 + my declared headless delta`, declared in
`spine-tasks/SP-094-dtrh-route-and-gate/floor-delta.json`.

## 7. What this packet will NOT prove

Nothing here is `presentation-verified`. A headless frame does not verify composited pixels,
window activation, z-order, the minimize/restore actually leaving and returning on a real
desktop, the WebView2 host booting, or that a human can see the band. The tray is untouched
and its Windows and Linux halves both stay undischarged.
