# SP-091 — record

Branch `lane/SP-091-navigation-shell`, base `21e381fe` (the amended packet).
Plan checkpoint output: `plan.md` in this folder.

**Amendment status, stated precisely because the first version of this file overclaimed it.**
Amendment 2 was applied in the first commit. **Amendment 1 was applied INCOMPLETELY in the first
commit** and completed at code review: it widened the File Scope to `client/tools/verify/**`, the
whole directory, and I re-anchored only the three files that had come up in conversation
(`capture.ps1`, `checks.json`, `self-test.ps1`). **`capture-wslg.sh`, the Linux leg, was missed, and
my own commit broke it** — see "The missed file" below. The earlier line here reading "Both
orchestrator amendments were applied" was false while `capture-wslg.sh` was broken, and it was the
worse half of the miss: a wrong claim that a gate was discharged is what stops the next reader from
checking it.

---

## Step 1 — what `--loom-demo` did, and the call reused

`App.axaml.cs:213-249` (pre-change) was the whole launch path: `new DtrhLoomWindow(_host, _loomDrive)`
at `:218`, `loomWindow.Show(dashboard)` at `:231`. No service seam existed, and `:218` was the only
`DtrhLoomWindow` construction site in the tree.

That pair was lifted verbatim into `Navigation/LoomLaunch.cs`, which is now **the one construction
site**. Both callers go through it:

- the shell's `THE LOOM — weave your own spiral` button (`Views/Pages/StudioPage.axaml.cs`);
- `--loom-demo` (`App.axaml.cs`), which now calls `dashboard.Loom.Launch()`.

The launcher adds the WPF rule the CLI path never had: idempotent refocus, tracked by the field and
released on `Closed` (`Services/Chaos/LoomHostService.cs:29-31`, `:68-69`). All four demonstrator log
strings are byte-identical, and the demonstrator was re-run end to end (evidence below).

## Step 2 — the shell

Three doors, each with a working destination; everything else honestly absent. Justification and the
full divergence table are in `client/docs/wpf-surface-reachability.md` §9 (D1-D13).

| Door | Destination | Works because |
|---|---|---|
| Studio | rack row *Spiral Overlay* -> module panel -> the Loom studio window | ungated on the WPF path (`LoomHostService.cs:30-77`; rack entry `StudioTabView.xaml.cs:490` passes no tier, default `tier = 0` at `:548`) |
| Companion | page -> "Show companion" -> the real `CompanionWindow` | landed at SP-046; the two-hop shape is WPF's (`SettingsTabView.xaml:1864-1887`) |
| System | the live startup trace, typed capability states, heartbeat | live data from the real composition root |

## Steps 3-5 — files

**New** `Navigation/ShellRoute.cs`, `ShellRoutes.cs`, `ShellRouter.cs` (+ `ShellRouteBinding`),
`LoomLaunch.cs`; `Views/Pages/StudioPage`, `CompanionPage`, `SystemPage` (`.axaml` + `.axaml.cs`).

**Rewritten** `Views/MainWindow.axaml` (rail + page host + diagnostic footer; `:checked` pseudo-class
styling, no triggers) and `Views/MainWindow.axaml.cs`.

**Changed** `App.axaml.cs` (`--loom-demo` -> the launcher), `client/tools/verify/{capture.ps1,
checks.json,self-test.ps1,capture-wslg.sh}` (amendment 1 — the last of those four came at code
review, see below), `client/docs/wpf-surface-reachability.md` (§9 appended).

### The missed file: `capture-wslg.sh` (found at code review, not by me)

My first commit renamed the layout probe from `card WxH …` to `door <id> WxH …` and renamed the
harness surface/state tokens. The Windows leg was updated with it; the **Linux leg was not**, and
three things in it broke:

| Break | Was | Now |
|---|---|---|
| `:44-46` matched `card ([0-9.]+)x([0-9.]+) DIP @ scale …` | exits 1 with `FAIL: layout probe unreadable` **against a healthy app** — verbatim the broken-app-versus-broken-harness ambiguity amendment 1 cited when it overruled option (b) | matches `door <id> WxH DIP @ scale … @ screen x,y`, with the requested door's id in the pattern (one stderr line carries all three doors) |
| `:3,11-12,41` took `dashboard-card` and `unlit\|lit` | `checks.json` had renamed those, so even the surviving whole-window `dashboard` capture could not be evaluated — `CheckEvaluator.EvaluateCapture` throws on an unknown state | takes `dashboard\|rail-door` and `unselected\|selected`, and validates both arguments explicitly instead of falling through |
| `:25-29` seeded `{"statusTickerEnabled": true}` as the `lit` drive | lit nothing: the card that setting drove is gone | **no state is seeded at all.** On cold start the shell opens on Studio, so the Studio door is already `:checked` and Companion is not — both states are capturable with **zero input**, which is exactly what WSLg's no-input-automation limit (SP-007/SP-008 named gate) permits. The settings file is still removed for a deterministic start |

One thing was fixed beyond the rename, because it is the same defect class the amendment was written
about: `sleep 5` before the capture is gone, replaced by a bounded poll (40 s) for the app's own
layout-probe line, with an early failure if the process exits first. A fixed sleep encodes today's
startup cost as tomorrow's correctness condition, and this harness has already been bitten by exactly
that once.

### A FIFTH consumer was broken the same way: the publish gate (fixed under amendment 5)

Sweeping every consumer of the probe string (rather than only the directory I had been pointed at)
turned up one more, in a directory no amendment granted me:

**`client/tools/publish/matrix.ps1:75`**
```powershell
$probe = (Get-WindowTexts $window | Where-Object { $_ -like 'layout-probe: card*' }) -join ';'
if (-not $probe) { $p.Kill(); return @{ Ok = $false; Detail = 'layout-probe needle missing — no render evidence (killed)' } }
```
`card` never matches now, so **publish gate 1 kills a healthy app and reports "no render evidence"**
— the identical broken-app-versus-broken-harness ambiguity, one directory over, on the release gate.

- The fix is one token: `'layout-probe: card*'` -> `'layout-probe: door*'`.
- The Linux twin is **not** affected: `client/tools/publish/matrix.sh:91` greps the bare
  `'layout-probe:'` with no `card`, so it still matches. It was left alone.

I did not touch it when I found it: `client/tools/publish/**` was outside the File Scope amendment 1
had granted (`client/tools/verify/**` only), so it was reported rather than silently widened.
**Amendment 5 then added `client/tools/publish/**` for this single change, and the one token is now
applied**, with a comment naming why a stale needle here is worse than elsewhere: line 76 does not
merely fail, it `Kill()`s the app and reports `no render evidence`.

**Demonstrated, not argued** (scratch script, not committed), by applying `matrix.ps1`'s exact
`Get-WindowTexts` shape and both filters to the real running app:

```
OLD needle 'layout-probe: card*' -> NO MATCH  => gate 1 would Kill() a healthy app
NEW needle 'layout-probe: door*' -> MATCH     => gate 1 passes; probe = layout-probe: door studio 174.9x44.0 DIP @ scale 1.75 @ screen 165,267
graceful close exit code: 0
```

The last line also exercises gate 1's other assertion (CloseMainWindow -> graceful exit 0).
`matrix.ps1` parses clean (0 syntax errors, PowerShell `Parser::ParseFile`). **What this does NOT
cover: the publish matrix itself was never run here** — it publishes per-RID artifacts and this lane
has no publish output; only the gate-1 needle and close path were exercised, against a Debug build.

The demo card is gone. The SP-003/SP-006 startup-trace and capability proofs moved to the System page
and the SP-007 layout probe moved to the footer, now measuring every rail door — both shrank and
moved, neither was deleted.

## Step 6 — tests

`client/tests/CcpClient.HeadlessTests/NavigationShellHeadlessTests.cs`, 11 facts, all driving real
headless input on real controls from a cold composition-root boot with **no CLI flags**:

1. `ColdStart_NoArguments_DoorThenRowThenButton_ReachesTheLoomHost` — the headline. Leaves Studio
   first (so the door click is a real navigation), then door -> row -> button; asserts the concrete
   `DtrhLoomWindow` reaches the launch seam, is `Loom.Current`, and is titled "The Loom".
2. `LoomButton_PressedTwice_RefocusesInsteadOfOpeningASecondStudio` — `LoomHostService.cs:29-31`.
3. `CompanionDoor_TwoRealClicks_OpenTheRealCompanionWindow`.
4. `SystemDoor_RendersTheLiveStartupTraceAndCapabilityStates` — every registered capability's typed
   line, with a non-vacuity assertion on the loop.
5. `TheRail_DeclaresExactlyTheDeclaredRoutes_AndNoDtrhDoor` — the anti-decoration pin.
6. `SelectedDoor_ResolvesTheSelectedBrush_ThroughTheCheckedPseudoClass`.
7. `PageHost_SwapsContent_AndTheOutgoingPageLeavesTheVisualTree`.
8. `RackRow_SpaceWhenFocused_OpensTheSameModulePanel`.
9. `DoorLabelMutation_LeavesRoutingIntact_AndTheLabelNeverResolves`.
10. `RightClickOnTheRackRow_OpensNoMenu_AndSelectsNothing`.
11. `ElementNameMirror_FollowsTheLiveHeartbeatText` (via `TestWait` only).

`client/tests/CcpClient.Tests/NavigationRouteTableTests.cs`, 3 pure-logic guards: stable unique ids
plus the mechanical no-DTRH-door boundary; `Navigate` on null/empty/unknown/wrong-case/current
changes nothing and returns false (with a positive control); and the door-with-no-page /
page-with-no-door / duplicate-id refusals.

### The 8 retired headless facts, and where their proofs went

Amendment 4 asked for this to be visible rather than reading as dropped.

| Retired | Was | Now |
|---|---|---|
| `DashboardCardHeadlessTests.Card_Toggle_AppliesLitClass_AndStyleResolvesBorderBrush` | conditional class -> style-resolved brush on the demo card | fact 6, on the rail door's `:checked` pseudo-class |
| `DashboardCardHeadlessTests.Card_ArrangedBounds_GrowWithLoadBearingIsVisible` | load-bearing layout change | fact 7, on the page swap (with arranged bounds) |
| `DashboardCardHeadlessTests.ElementNameMirror_FollowsLiveTickText` | compiled ElementName binding vs a changing source | fact 11, on the live heartbeat |
| `QuickToggleDispatchHeadlessTests.EnterKey_WhenFocused_TogglesSameOperation_AsRightClick` | keyboard reaches the same destination as the pointer | fact 8, Space on the rack row |
| `QuickToggleDispatchHeadlessTests.TitleMutation_RendersMutatedTitle_AndRightClickStillToggles` | stable id vs display text | fact 9, on the door label |
| `QuickToggleDispatchHeadlessTests.PlainRightClick_NoContextMenu_ExistsOrOpens` | right-click opens no menu | fact 10, on the rack row |
| `QuickToggleDispatchHeadlessTests.RightClickPress_OnCardBody_TogglesThroughStableIdPath` | the right-click quick-toggle itself | **no product analogue.** The port has no toggleable effect (divergence D6). `QuickToggleDispatchTests` (5 facts, `CcpClient.Tests`) still covers the dispatch class |
| `FeaturePopupHeadlessTests` fact 1's card gesture | left-click on the card opened the popup | **gesture retired with the card.** The fact was kept, not deleted: it now drives `Popups.Show()` and keeps all the W-04 chrome assertions |

### Floor

Declared in `floor-delta.json`: **unit +3, headless +4**.
Observed: **CcpClient.Tests 1055** (pin 1052 + 3), **CcpClient.HeadlessTests 39** (pin 35 + 4).
0 failed; the 2 unit skips are the OS-gated `allowedSkips` entries. `floor.json` was never opened.

**This is +4 headless, not the +2 the coordinator ratified.** The arithmetic behind +2 assumed
`FeaturePopupHeadlessTests` fact 1 would be deleted with the card gesture it used. Keeping the fact
(its W-04 chrome assertions have no other home) makes the retirement 7 rather than 8. The declared
file states the number the gate actually observes; nothing was widened to reach it.

## Step 7 — prove it bites

Scratch mutation, one line in `MainWindow.axaml.cs`:
`_doors[ShellRoutes.Studio] = DoorStudio;` -> `= DoorCompanion;` (the Studio route wired to the wrong
door control).

- With the mutation: **6 failed / 33 passed** — including
  `ColdStart_NoArguments_DoorThenRowThenButton_ReachesTheLoomHost`, the pseudo-class fact, the page
  swap, the refocus fact, the keyboard fact and the right-click fact.
- Restored: `git checkout --` then `git diff --exit-code` returned **0 with a clean tree**
  (byte-identical), and the suite returned **39 passed / 0 failed**.
- The mutation was never committed.

## Step 8 — divergences

Written into `client/docs/wpf-surface-reachability.md` §9 as D1-D13, in the same commit as the code.
Per amendment 2, **D5 (no live dot) and D6 (no right-click toggle) are recorded as GAPS with the true
reason — the port has no spiral-overlay effect to report or flip — and explicitly not as parity.**
The row states that WPF's spiral entry *does* pass a state lambda
(`Views/Tabs/StudioTabView.xaml.cs:490-491`), that the `Visuals` exception at `:494-496` and `:659`
does not generalise to it, and names the condition that closes each.

---

## Evidence run (all on this Windows box, 2026-08-18)

| Gate | Result |
|---|---|
| `dotnet build client/CcpClient.sln -c Debug` | **0 warnings, 0 errors** |
| `node client/tests/floor/check-floor.mjs` | 1055 / 39, 0 failed — `observed == pin + declared delta`, the designed state for a bound lane |
| `pwsh client/tools/verify/self-test.ps1` | **SELF-TEST PASS.** Seeded: `FAIL rail-door-selected-border - 0/918 pixels matched`, `FIRST FAILED CHECK: rail-door-selected-border`, exit 2. Restored: `PASS rail-door-selected-border - 888/918 (0.967)`, `ALL CHECKS PASSED` |
| `capture.ps1 -Surface rail-door -State unselected` + CcpVerify | `PASS rail-door-unselected-border - 896/918 (0.976)` |
| `capture.ps1 -Surface dashboard -State unselected` + CcpVerify | `PASS dashboard-background - 151386/154000 (0.983)` |
| `--loom-demo --loom-auto-close 12` (per-run `CCP_DATA_ROOT`, never exported) | studio opened, page reached ready, `dtrh: loom-list posted (0 spiral(s))`, auto-close fired, `loom: studio window closed — shutting down the lifetime`, **exit 0** |
| Headed UIA probe of the Studio route (scratch script, not committed) | Studio door -> Spiral Overlay row -> `BUTTON: 'THE LOOM — weave your own spiral' automationId='LoomButton'` |
| `bash -n client/tools/verify/capture-wslg.sh` | syntax OK. **This is a parse, not a run** — it proves nothing about behaviour |
| The re-anchored WSLg door regex, exercised in bash against a real probe line | `studio -> crop 253 355 306 77`, `companion -> crop 253 442 306 77`, `system -> crop 253 529 306 77` (306x77 device px, identical to the Windows leg's capture size); an unknown door id does not match; the OLD `card …` pattern is confirmed NOT to match, which is the reported break. **This exercises the pattern in isolation, not the script** |
| `capture-wslg.sh` executed end to end under WSLg | **NOT RUN — see Limits.** This lane has no Linux/X session. The two rows above are the whole of the Linux evidence and they do not add up to a run |
| `client/tools/publish/matrix.ps1` gate-1 needle, both tokens, against the live app | OLD `'layout-probe: card*'` -> NO MATCH (gate 1 would `Kill()` a healthy app); NEW `'layout-probe: door*'` -> MATCH; graceful close exit 0. `matrix.ps1` parses clean. **The publish matrix itself was not run** |

Two defects were found by the headed captures that no headless frame showed, and both were fixed:
rail doors sized to their labels (ragged rail, and a harness band whose pixel fraction depended on
label length) and the Studio page blurb overrunning its fixed-width column.

---

## Limits — what this does NOT prove

- **The Loom window's presentation is not proven by any test here.** The headless facts stop at the
  `LoomLaunch.Present` seam: they prove a real gesture constructs the concrete `DtrhLoomWindow` and
  hands it to the presenter. They do **not** prove it shows, renders, boots WebView2, takes focus or
  sits correctly in z-order. The `--loom-demo` run above is the evidence that it does all of that,
  and it is a headed run, not a test.
- **No `presentation-verified` claim is made from a headless frame.** The three CcpVerify checks are
  presentation-verified and were run headed on one machine, one DPI (1.75), one theme. Multi-DPI,
  multi-monitor and Linux/WSLg behaviour of this shell is unmeasured.
- **Linux is entirely unmeasured for this shell, and that now includes the harness leg I just
  rewrote.** Everything above ran on Windows. `capture-wslg.sh` was fixed by reading it against the
  product's actual probe output and by exercising its regex, its argument validation and its bash
  syntax — **it was never executed under WSLg from this lane, because this lane has no Linux/X
  session.** Its correctness is therefore argued, not demonstrated. The first WSLg run is the gate
  that closes it, and the specific things that run must confirm are: the layout probe reaches
  stderr on Linux (it uses `Environment.NewLine`, which is `\n` there, so the doors arrive on one
  joined line as on Windows), the bounded poll sees that line, `xgetimage.py` crops the door rect
  correctly at the X root coordinates the probe reports, and the two zero-input states really do
  paint `#3A2F3E` and `#E066FF`.
- **`MainWindowViewModel` is now an A-014 residue.** It is no longer any window's DataContext, but it
  cannot be deleted here: `CcpClient.Tests/QuickToggleDispatchTests.cs` (5) and
  `StatusTickerSliceTests.cs` (4) construct it, and those files are outside this packet's File Scope.
  The same is true of `FeaturePopupManager`, which the shell still owns but which no user gesture can
  now reach. Both are infrastructure-only until a follow-up row retires the demonstrator properly.
- **`client/tools/publish/matrix.ps1` is fixed (amendment 5) but `pwsh client/tools/publish/matrix.ps1`
  was never run here.** The gate-1 needle and the graceful-close path were demonstrated against the
  live app; the surrounding publish matrix (per-RID publish, artifact layout, the rest of its gates)
  was not exercised at all from this lane. `matrix.sh` was deliberately left untouched.
- **`client/docs/verification-harness.md` and the root `CLAUDE.md` still document the old
  `-Surface dashboard-card -State lit` invocation.** Neither file is in this packet's File Scope
  (amendment 1 widened it to `client/tools/verify/**` only, and `CLAUDE.md` is never editable by a
  lane), so both are now stale by exactly the rename this packet performed. No test reads either.
  **Named for the orchestrator to correct at land.**
- **The rack is one row and the shell is three doors.** DTRH, Graded Intake, the AvatarTube
  demonstrator and the Chaos tunnel backdrop remain reachable only by a CLI flag. That is the honest
  state after this packet, not the finished one; §9's closing note says so in the doc as well.
