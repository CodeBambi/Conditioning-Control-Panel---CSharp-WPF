# SP-093 — A tray capability, honest on the platform that cannot have one yet

**Branch** `lane/SP-093-tray-capability` · **base** `94fb5d14` · **worktree**
`C:\Code\Conditioning-Control-Panel---CSharp-WPF\.claude\worktrees\agent-a145b63aad7d3ff1e`
**Review Level** 3 · **Status** infrastructure only (A-014) — see §Wiring.

---

## Step 1 — the absence, confirmed independently

`rg -i "tray|notifyicon|notify-icon|minimize.to.tray|StatusNotifier|AppIndicator"` over
`client/src` returned **no tray code**. Every hit was an unrelated substring (`stray`,
`betrayal`, `carriesStrayLink`) except one, the precedent the packet names:

- `client/src/CcpClient.Desktop/Features/Intake/IntakeHostWindow.axaml.cs:131` —
  *"Plain MainWindow minimize (explicitly NOT tray tuck; records the prior state so a maximized
  panel comes back maximized)"*, implemented at `:134` (`DuckOwner`) and `:148` (`RestoreOwner`,
  "the single restore funnel … Maximized comes back Maximized, else Normal + Activate").

That is the fallback this capability's `Unavailable` routes callers to. It is not duplicated
differently here — nothing in `Tray/` minimizes or restores a window.

**The parity owed (WPF).** `Services/Chaos/DtrhHostService.cs:156` calls
`mw.MinimizeToTrayForChaos()` when the DTRH window opens and `:998` calls `mw.ShowFromTray()`
when it closes. Those resolve to `MainWindow/MainWindow.RemoteControl.cs:1517` and `:1554`,
both delegating to `Services/Notifications/TrayIconService.cs`:

- `:145-158` `MinimizeToTray()` = `_mainWindow.Hide()` **plus** `Show()` (the icon made visible).
  Both halves, always. The balloon fires only on the first-ever minimize (`:152-157`), and the
  DTRH path deliberately takes the no-notification variant (`MainWindow.RemoteControl.cs:1515-1517`).
- `:160-184` `ShowWindow()` = hide the icon, `Show()`, `WindowState = Normal`, `EnsureOnScreen()`,
  `SetForegroundWindow`, `Activate()`.
- `:19,42` the icon itself is a WinForms `NotifyIcon`; icon-source fallback chain at `:48-91`
  ending in `SystemIcons.Application` (`:91`).

## Step 2 — what Avalonia 12.1.1 actually offers (verified, never guessed)

Verified by reflecting the packaged assemblies from
`C:\Users\Micha\.nuget\packages\avalonia*\12.1.1\lib\net10.0\` (metadata reader for internal
types, live reflection for public surface) and by constructing the type.

**The type I used for the design decision, and its version:**
`Avalonia.Controls.TrayIcon`, assembly **Avalonia.Controls 12.1.1.0** (package `Avalonia`
12.1.1, the client's pinned baseline). Members: `Icon` (`WindowIcon`), `ToolTipText`,
`IsVisible`, `Menu` (`NativeMenu`), `Command`/`CommandParameter`, attached `TrayIcon.Icons`,
`Clicked` event, `Dispose()`.

Supporting surface, same version:

| Type | Assembly | Visibility |
|---|---|---|
| `Avalonia.Platform.ITrayIconImpl` (`SetIcon`, `SetToolTipText`, `SetIsVisible`, `MenuExporter`, `OnClicked`) | Avalonia.Controls 12.1.1.0 | public |
| `Avalonia.Platform.IWindowingPlatform.CreateTrayIcon()` | Avalonia.Controls 12.1.1.0 | public |
| `Avalonia.Win32.TrayIconImpl`, `Avalonia.Win32.Interop.NOTIFYICONDATA` | Avalonia.Win32 12.1.1.0 | **internal** |
| `Avalonia.FreeDesktop.DBusTrayIconImpl`, `StatusNotifierItemDbusObj`, `DBus.StatusNotifierWatcher` | Avalonia.FreeDesktop 12.1.1.0 | **internal** |
| `Avalonia.X11.XEmbedTrayIconImpl`, `Avalonia.X11.SystrayRequest` | Avalonia.X11 12.1.1.0 | **internal** |

### The partial-support finding (recorded, not routed around)

**Avalonia 12.1.1 can place a tray icon on all three platforms, and cannot tell you whether it
did.** Three facts, each verified:

1. `ITrayIconImpl.SetIcon` / `SetToolTipText` / `SetIsVisible` all return `void`. There is no
   success signal anywhere on the platform interface.
2. `TrayIcon`'s only **public** constructor is parameterless; the `TrayIcon(ITrayIconImpl)`
   overload is non-public and no member exposes the impl, so a caller cannot inspect whether a
   backend was even obtained.
3. Reproduced live against the 12.1.1 assemblies with **no windowing platform registered at
   all**:

   ```
   constructed with NO windowing platform: OK, type=Avalonia.Controls.TrayIcon
   IsVisible set to true, reads back: True
   disposed cleanly
   ```

   No throw. `IsVisible` is a stored `StyledProperty`, so it reads back `true` while **no icon
   exists anywhere on the machine**.

That is this packet's named trap sitting inside the framework type: a capability built on
`TrayIcon.IsVisible` would report success in the one situation where nothing happened at all.
So the backend takes the mechanism it can interrogate — `Shell_NotifyIcon`, which is the same
call Avalonia's own `Avalonia.Win32.TrayIconImpl` makes and the same one WPF reaches through
`NotifyIcon` (`TrayIconService.cs:19,42`). Nothing about Avalonia is worked around silently;
the framework's inability to confirm placement is the reason, and it is written into
`Win32TrayPresence`'s class comment where the next reader will hit it.

### The instrument, validated before any of it was designed

A throwaway Win32 probe (scratchpad, not committed) on this Windows 11 box:

```
sizeof(NOTIFYICONDATAW) = 976        (V4 layout; the shell accepted it)
Shell_TrayWnd           = 65922      (this session has a notification area)
MODIFY before ADD (uid 1) = False
ADD    (uid 1)            = True
MODIFY after ADD (uid 1)  = True
MODIFY never-added uid 9  = False
DELETE (uid 1)            = True
MODIFY after DELETE       = False
```

`Shell_NotifyIcon(NIM_MODIFY)` is therefore a genuine **existence oracle** for an `(hWnd, uID)`
pair, with both negative controls holding. `TrayCapabilityTests.TheShellOracle_SaysNoForAnIconThatWasNeverPlaced`
re-runs the never-added leg on every suite run, so an oracle that degenerated into "always true"
fails the suite instead of silently certifying everything built on it.

## Step 3 — the typed capability

`client/src/CcpClient.Desktop/Tray/`:

| File | What |
|---|---|
| `ITrayPresence.cs` | `Place(TrayIconRequest) : CapabilityState`, `Remove() : CapabilityState`, `bool IsPlaced`, `event Activated`, `IDisposable` |
| `TrayReasonCodes.cs` | `tray-mechanism-absent`, `tray-mechanism-refused`, `tray-owner-window-failed`, `tray-nothing-placed`, `tray-presence-disposed` |
| `TrayIconRequest.cs` | tooltip, clamped to the shell's 127-char `szTip` budget, clamping surfaced not swallowed |
| `Win32TrayPresence.cs` | the real Windows backend + `TrayNativeHandles` |
| `UnsupportedTrayPresence.cs` | the honest typed refusal |
| `TrayPresenceFactory.cs` | `TrayHostPlatform` selection + the Linux manual-gate constant |

States are SP-006's `CcpClient.Desktop.Capabilities.CapabilityState` (`Available(detail)` /
`Unavailable(CapabilityReason(code, detail))`), as instructed — no parallel state type invented.
The pair the packet demanded is `tray-mechanism-absent` ("the platform has no tray mechanism
**this build can drive**, and nothing was attempted") versus `tray-mechanism-refused` ("the
mechanism exists, was really invoked, and said no" — with the failing call and the Win32
last-error in the detail). `tray-owner-window-failed` is the finer refusal where the shell never
got asked because its prerequisite failed.

`runtime-capability-contract.md` §2 rule 2 (platform checks may never yield `Available`) is
obeyed literally: the platform switch in `TrayPresenceFactory` only ever *selects a backend* or
*refuses*; the only code in the tree that constructs `CapabilityState.Available` for the tray is
`Win32TrayPresence`, after the shell answered yes twice.

## Step 4 — the Windows implementation, and how placement is proven

`Win32TrayPresence` over `Shell_NotifyIconW`:

- a hidden **top-level** owner window (`WS_POPUP`, never shown, `WS_EX_TOOLWINDOW`), deliberately
  not `HWND_MESSAGE`: a message-only window cannot receive the `TaskbarCreated` broadcast, which
  is the only signal that an Explorer restart wiped the icon;
- `NIM_ADD` with `NIF_MESSAGE|NIF_ICON|NIF_TIP`; icon source = the process image's own icon via
  `ExtractIconExW`, falling back to `LoadIconW(IDI_APPLICATION)` — the same fallback shape as WPF
  `TrayIconService.cs:67-91`, and the `Available` detail names which source was used;
- **the claim is never the return value**: after `NIM_ADD` succeeds the backend asks the shell
  again with `NIM_MODIFY`. If either call fails it deletes any half-state, leaves `IsPlaced`
  false, and returns `Unavailable(tray-mechanism-refused, …)`;
- `Remove()` is symmetric — `NIM_DELETE`, then a confirming `NIM_MODIFY` that must now **fail**
  before removal is claimed. A delete the shell reported as successful while the icon is still
  there is reported as a refusal, not a success;
- `Dispose()` deletes the icon, destroys the window, unregisters the class, and records
  `TeardownDiagnostic` if it could not (wrong thread — `DestroyWindow` is thread-affine — or a
  refused destroy). Dispose does not throw and does not pretend;
- left-click and double-click both raise `Activated` (WPF admits single-click deliberately,
  `TrayIconService.cs:113-119`: *"clicking the tray icon does nothing" reads as the app being
  gone*); `TaskbarCreated` re-adds the icon and drops the placement claim if the re-add does not
  confirm;
- asked to run off Windows it returns `Unavailable(tray-mechanism-absent, …)` and P/Invokes
  nothing.

### What the Windows test actually proves

The tests never ask the backend whether it worked. `TrayShellProbe` re-declares every P/Invoke
independently (its own `Shell_NotifyIconW`, its own window class, its own `IsWindow`), so
"the product claims an icon" and "the shell holds an icon" are produced by two separate code
paths. Each effect fact then compares three things at statement depth 0, with no conditional and
no skip:

```
Assert.True(run.ShellSawIconAfterPlace  == run.MachineHasNotificationArea, …)
Assert.True(run.ClaimedAvailableOnPlace == run.ShellSawIconAfterPlace,     …)
```

`MachineHasNotificationArea` is `FindWindowW("Shell_TrayWnd", null) != 0`, established by the
test. On this machine it is **True** (proven by the Step 6 failure messages below), so these
facts really did exercise the Windows placement path; they did not pass through a vacuous
no-tray branch. A backend that claims success without placing fails the second comparison; a
backend that degenerates into always refusing fails the first.

Concretely proven, on this machine, by the suite: an icon that the Windows shell itself confirms
it holds after `Place`; the same icon confirmed **gone** after `Remove`; confirmed gone after a
`Dispose` with no explicit `Remove`, with the hidden owner window confirmed destroyed; and the
shell's click notification confirmed to arrive as exactly one `Activated` event.

## Step 5 — the non-Windows answer, and the Linux gate

`TrayPresenceFactory.CreateFor(Linux)` returns `UnsupportedTrayPresence` with
`tray-mechanism-absent` and a detail that names the route and refuses to round it off: Linux
**does** have a tray mechanism and **Avalonia 12.1.1 even ships backends for it**
(`Avalonia.FreeDesktop.DBusTrayIconImpl` + `StatusNotifierItemDbusObj` for StatusNotifierItem
over DBus, `Avalonia.X11.XEmbedTrayIconImpl` for XEmbed — both internal). This build has not
implemented or verified one, so the honest report is "nothing was attempted", never "not
supported".

### BLOCKED — the exact manual gate a Linux box must run

> **BLOCKED (Linux tray placement).** On a real Linux desktop session with a status-notifier
> host (GNOME + AppIndicator extension, KDE Plasma, or XFCE) — **not WSLg** — place the icon and
> prove it three ways:
> 1. `busctl --user list` shows an `org.kde.StatusNotifierWatcher` owner;
> 2. `busctl --user get-property org.kde.StatusNotifierWatcher /StatusNotifierWatcher org.kde.StatusNotifierWatcher RegisteredStatusNotifierItems`
>    lists **this process's** item, and stops listing it after `Remove()`;
> 3. a human confirms the icon is visible in the panel and that clicking it raises `Activated`.
>
> **Why this machine cannot discharge it:** the port's own WSLg observation is that
> `_NET_CLIENT_LIST` is absent on the XWayland root (`client/docs/port-lessons.md:52`,
> `client/docs/window-behavior-manifest.md:251`), so there is no reliable way to prove a panel
> icon exists from here. Wayland additionally has no XEmbed systray at all, and GNOME needs an
> AppIndicator extension for SNI to be hosted — so the gate must state which desktop it ran on.

The gate text is not only in this file: it is `TrayPresenceFactory.LinuxManualGate`, embedded in
the refusal itself, so it travels to whoever hits the `Unavailable` at runtime.
`TheLinuxSelection_RefusesWithTheMechanismAbsentCode_AndCarriesTheManualGate` pins that it is
still there. **No test is skipped anywhere in this packet** — the 2 skips in the floor run are
the two pre-existing Linux-only entries (`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`,
`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`), untouched by me.

macOS and unknown platforms get their own refusals; nothing returns a silent no-op.

## Step 6 — proving the tests bite (the check that decides whether this packet is real)

Scratch mutation in `Win32TrayPresence.Place`, inserted after the owner window and icon were
prepared and before the shell was ever called:

```csharp
// SP-093 STEP 6 SCRATCH MUTATION — NOT FOR COMMIT. Reports success without placing an icon.
_placed = true;
return new CapabilityState.Available("mutation: claims placement without calling Shell_NotifyIcon at all");
```

**Result: 4 of 9 facts red** (`Failed: 4, Passed: 5, Skipped: 0, Total: 9`), naming the effect
directly:

- `PlacingTheIcon_IsConfirmedByTheShellItself_NotByTheBackendsOwnSayso` —
  *"this session has a notification area = True, but after Place the shell holding the icon =
  False. … Backend said: Available: mutation: claims placement without calling Shell_NotifyIcon
  at all"*
- `DisposingThePresence_LeavesNoIconAndNoOwnerWindowBehind` — *"placement was not real before the
  dispose leg started (notification area = True, shell held icon = False)"*
- `TheShellsClickNotification_BecomesAnApplicationActivationEvent` — *"the click leg needs a
  really-placed icon first (notification area = True, shell held icon = False)"*
- `RemovingTheIcon_TakesItOutOfTheNotificationAreaForReal` — `IsPlaced` stayed true after a
  `NIM_DELETE` the shell refused.

Those messages are also the proof that `MachineHasNotificationArea == True` here, i.e. that the
green run exercises the real path.

**Restored byte-identically and verified.** `git checkout --` the one file, then:

```
staged blob:   6f7a0dc7c091ac0c5e0b906570143a23a39d0ac2  client/src/.../Win32TrayPresence.cs
worktree blob: 6f7a0dc7c091ac0c5e0b906570143a23a39d0ac2
mutation marker count: 0
git diff --stat: (empty)
```

Re-run after restore: `Failed: 0, Passed: 9, Skipped: 0`. **The mutation was never committed.**

## Step 7 — what a caller does when the tray is `Unavailable`

**Fall back to a plain window minimize that keeps the taskbar button** — exactly the shape the
port already ships and documents at `Features/Intake/IntakeHostWindow.axaml.cs:131`, including
its record-the-prior-state restore (`:134` / `:148`: a maximized panel comes back maximized,
otherwise Normal + Activate).

**Why that beats hiding the window.** WPF's tuck is two things at once
(`TrayIconService.cs:145-148`): `Hide()` — which removes the window from the taskbar — **plus**
a visible icon that brings it back. Taking the first half without the second leaves the user
with a running application that is not in the taskbar, not in Alt-Tab, and has no icon anywhere:
the app is, from where they are standing, gone, and their only recovery is Task Manager. A plain
minimize is a *smaller* behaviour than WPF's and it is visibly smaller — the window is still on
the taskbar, one click away — which is why it is the correct honest degradation and hiding is
not. The port already reached that same conclusion once, in Intake, and wrote it down.

This is enforced structurally, not just advised: `ITrayPresence` has no `Hide the window` member
and `UnsupportedTrayPresence` has no path that reports success, so a caller cannot get half of
the tuck out of this capability by accident.

## Wiring: deliberately none (A-014 infrastructure only)

**Nothing is registered and no window's behaviour changed.** `App.axaml.cs`, `Views/**`,
`Navigation/**`, `Entitlement/**`, `Features/**` are untouched — SP-091 owns the shell files in
this wave. **DTRH's minimize-to-tray parity is NOT delivered by this packet**; it is delivered
when a later packet consumes this capability, and the board row's acceptance ("the Windows half
proven headed — window leaves the taskbar on launch and returns on close, captured") is
therefore **not discharged here**. No `presentation-verified` claim is made anywhere.

## Gate results

| Gate | Result |
|---|---|
| `dotnet build client/CcpClient.sln -c Debug` | **0 Warning(s), 0 Error(s)** |
| `node client/tests/floor/check-floor.mjs` | unit **observed 1061** vs **pin 1052** → **+9 = my declared delta**. `Failed: 0, Passed: 1059, Skipped: 2`. Headless **35/35**, no drift → delta 0 |

Both gates were run through `node client/tools/gate/with-slot.mjs --slots 3 -- …`. The floor
"FAILED" line is the expected multi-lane behaviour: observed == pin + declared delta, and the
orchestrator applies the sum at land. `floor.json` was never opened or edited; the pin figure
above is quoted from the gate's own output.

`spine-tasks/SP-093-tray-capability/floor-delta.json` declares `unit: 9, headless: 0`.

The 9 facts (`client/tests/CcpClient.Tests/TrayCapabilityTests.cs`):

1. `TheShellOracle_SaysNoForAnIconThatWasNeverPlaced` (the instrument's own negative control)
2. `PlacingTheIcon_IsConfirmedByTheShellItself_NotByTheBackendsOwnSayso`
3. `RemovingTheIcon_TakesItOutOfTheNotificationAreaForReal`
4. `DisposingThePresence_LeavesNoIconAndNoOwnerWindowBehind`
5. `TheShellsClickNotification_BecomesAnApplicationActivationEvent`
6. `TheRefusingBackend_ReportsAReasonAndNeverClaimsAnIconIsPlaced`
7. `TheLinuxSelection_RefusesWithTheMechanismAbsentCode_AndCarriesTheManualGate`
8. `TheBackendSelection_GivesTheRealOneOnlyToTheOnePlatformThisBuildCanDrive`
9. `TheTooltip_IsClampedToTheShellsBudgetBeforeItReachesTheMarshaller`

No new entry was needed in `client/tests/floor/vacuous-shape-ledger.json` (outside File Scope):
every fact body asserts at statement depth 0 with no early `return;`, no `OperatingSystem.Is`,
no `Environment.GetEnvironmentVariable` and no `File.Exists` — the platform predicates live in
the helpers, where they select what to measure and never silence an assertion. No token from
`TestTimingGuardTests.ForbiddenTokens` appears; the message pump is bounded by iteration count,
not by wall clock.

## Spec-versus-code discrepancies and scope notes

1. **The packet frames Linux as "StatusNotifierItem/AppIndicator over DBus, and Wayland has no
   XEmbed tray at all" — accurate, but it understates what the framework already has.** Avalonia
   12.1.1 ships `Avalonia.FreeDesktop.DBusTrayIconImpl` and `Avalonia.X11.XEmbedTrayIconImpl`
   (internal). The Linux blocker is therefore **not** "no mechanism exists"; it is (a) no
   verified backend in this build, (b) no way through Avalonia's public surface to confirm a
   placement even if there were, and (c) no Linux box here to run the gate. Recorded rather than
   improvised around; the refusal detail says exactly this.
2. **New reason codes live in `Tray/TrayReasonCodes.cs`, not in
   `Capabilities/CapabilityReasonCodes.cs`.** The contract says codes are additive and land with
   their consumer row, but `Capabilities/**` is outside this packet's File Scope. Precedent for
   feature-local codes already exists at `Features/Dtrh/DtrhCapabilityProbes.cs:41,52,72`.
   Flagged for the orchestrator in case consolidation is wanted later.
3. **The packet's Step 2 says to record "the exact type and version you used".** The type whose
   behaviour decided the design is `Avalonia.Controls.TrayIcon` / Avalonia.Controls **12.1.1.0**;
   the mechanism the backend actually drives is `Shell_NotifyIconW` (shell32), for the reason in
   §Step 2. Both are named, in the record and in the source comment.
4. No File Scope was widened. Changed files: `client/src/CcpClient.Desktop/Tray/**` (7 new),
   `client/tests/CcpClient.Tests/Tray*` (3 new), `spine-tasks/SP-093-tray-capability/**`.

## What this work does NOT prove

- **Nothing headed.** No composited pixel was captured. The suite proves the shell holds a
  notification-area entry; it does **not** prove a human can see the icon, that its bitmap
  rendered, where it landed (Windows 11 hides new icons in the overflow by default), or that a
  real mouse click on it works. Fact 5 posts the shell's own click notification and pumps the
  queue — that is routing, not interaction.
- **Nothing about the window.** No window leaves or returns to the taskbar in this packet, so no
  part of the WPF DTRH parity (`DtrhHostService.cs:156,998`) is discharged.
- **Nothing about Linux.** It compiles for `linux-x64` and refuses truthfully there. That is not
  support; the BLOCKED gate above is the only thing that would settle it.
- **Explorer-restart recovery is implemented but unverified.** The `TaskbarCreated` re-add path
  requires a live message pump and an actual Explorer restart; neither is exercised by the suite.
- **Cross-thread teardown is reported, not exercised.** `TeardownDiagnostic` covers the
  wrong-thread `Dispose` case; the suite only asserts it stays null on the clean path.
