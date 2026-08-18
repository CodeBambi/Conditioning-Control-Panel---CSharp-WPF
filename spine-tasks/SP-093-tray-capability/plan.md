# SP-093 plan (Review Level 3 — plan checkpoint)

Branch `lane/SP-093-tray-capability`, worktree
`C:\Code\Conditioning-Control-Panel---CSharp-WPF\.claude\worktrees\agent-a145b63aad7d3ff1e`,
base `94fb5d14`.

## Step 1 — the absence, confirmed by me

`rg -i "tray|notifyicon|notify-icon|minimize.to.tray|StatusNotifier|AppIndicator"` over
`client/src` returns **no tray code**. Every hit is an unrelated substring (`stray`,
`betrayal`, `carriesStrayLink`) except one:

- `client/src/CcpClient.Desktop/Features/Intake/IntakeHostWindow.axaml.cs:131` —
  *"Plain MainWindow minimize (explicitly NOT tray tuck; records the prior state so a
  maximized panel comes back maximized)"*, with `DuckOwner`/`RestoreOwner` at :134/:148.

That is the precedent I must not duplicate differently: the port already has a documented
plain-minimize fallback with prior-state restore. My `Unavailable` answer routes callers to
*that*, it does not invent a second one.

WPF side (the parity owed): `Services/Chaos/DtrhHostService.cs:156` calls
`mw.MinimizeToTrayForChaos()` at DTRH launch and `:998` calls `mw.ShowFromTray()` at close.
Those land in `MainWindow/MainWindow.RemoteControl.cs:1517` and `:1554`, both delegating to
`Services/Notifications/TrayIconService.cs` — `MinimizeToTray()` at `:145` is
`_mainWindow.Hide(); Show();` (window gone from the taskbar **plus** an icon made visible),
`ShowWindow()` at `:160` hides the icon, re-shows, `EnsureOnScreen()`, `SetForegroundWindow`,
`Activate()`. The icon itself is `System.Windows.Forms.NotifyIcon` (`:19`, `:42`) with an
icon-source fallback chain at `:48-91` ending in `SystemIcons.Application`.

## Step 2 — what Avalonia 12.1.1 actually offers (verified, not guessed)

Verified by reflecting the packaged assemblies out of
`C:\Users\Micha\.nuget\packages\avalonia*\12.1.1\lib\net10.0\` and by a live construction
probe. Findings:

| Type | Assembly (version) | Note |
|---|---|---|
| `Avalonia.Controls.TrayIcon` | Avalonia.Controls **12.1.1.0** | public; `Icon`/`ToolTipText`/`IsVisible`/`Menu`/`Command`, `Clicked` event, `Dispose()`, attached `TrayIcon.Icons` |
| `Avalonia.Platform.ITrayIconImpl` | Avalonia.Controls 12.1.1.0 | `SetIcon`, `SetToolTipText`, `SetIsVisible`, `MenuExporter`, `OnClicked` — **every mutator returns `void`** |
| `Avalonia.Platform.IWindowingPlatform.CreateTrayIcon()` | Avalonia.Controls 12.1.1.0 | the only factory |
| `Avalonia.Win32.TrayIconImpl` + `Avalonia.Win32.Interop.NOTIFYICONDATA` | Avalonia.Win32 12.1.1.0 | **internal** — Avalonia's Windows tray is Shell_NotifyIcon |
| `Avalonia.FreeDesktop.DBusTrayIconImpl`, `StatusNotifierItemDbusObj`, `DBus.StatusNotifierWatcher` | Avalonia.FreeDesktop 12.1.1.0 | **internal** — Linux SNI/DBus exists in the framework |
| `Avalonia.X11.XEmbedTrayIconImpl`, `Avalonia.X11.SystrayRequest` | Avalonia.X11 12.1.1.0 | **internal** — XEmbed fallback |

**The partial-support finding, reproduced live.** `TrayIcon`'s only public constructor is
parameterless; the `TrayIcon(ITrayIconImpl)` overload is non-public, and no member exposes
the impl or an availability flag. Constructing it with **no windowing platform registered
at all** does not throw:

```
constructed with NO windowing platform: OK, type=Avalonia.Controls.TrayIcon
IsVisible set to true, reads back: True
disposed cleanly
```

So `TrayIcon.IsVisible == true` is a stored `StyledProperty`, not an observation, and it
reads `true` while **no icon exists anywhere on the machine**. That is this packet's named
trap sitting inside the framework type. Recorded as a finding, not routed around: it is the
reason the capability cannot take `Avalonia.Controls.TrayIcon` as its truth source, because
neither the app nor a test can ask that object whether an icon was placed.

## Step 2b — the mechanism and the instrument, validated empirically first

Before designing anything I ran a throwaway Win32 probe on this box (scratchpad, not
committed). Result:

```
sizeof(NOTIFYICONDATAW) = 976        (the V4 layout; the shell accepted it)
Shell_TrayWnd           = 65922      (this machine has a notification area)
MODIFY before ADD (uid 1) = False
ADD    (uid 1)            = True
MODIFY after ADD (uid 1)  = True
MODIFY never-added uid 9  = False
DELETE (uid 1)            = True
MODIFY after DELETE       = False
```

`Shell_NotifyIcon(NIM_MODIFY, …)` is therefore a genuine **existence oracle** for an
`(hWnd, uID)` pair, with both negative controls holding (never-added id → False; after
delete → False). That is the effect probe Step 4/Step 6 need, and it is independent of
whatever the product code claims.

## Step 3 — the typed capability

Reuses SP-006's shape as instructed: `CcpClient.Desktop.Capabilities.CapabilityState`
(`Available(detail)` / `Unavailable(CapabilityReason(code, detail))` / `Faulted`). No new
state type is invented, and the runtime-capability-contract §2 rule 2 line is respected —
a platform check selects a *backend*, it never yields `Available`; `Available` is returned
only after `Shell_NotifyIcon` really accepted the icon and the round-trip confirmed it.

New files under `client/src/CcpClient.Desktop/Tray/`:

- `TrayReasonCodes.cs` — `tray-mechanism-absent` (this build has no tray backend that can
  drive this platform), `tray-mechanism-refused` (the mechanism exists, was asked, said no —
  e.g. `Shell_NotifyIcon` returned FALSE in a session with no notification area),
  `tray-owner-window-failed` (the prerequisite hidden owner window could not be created),
  `tray-presence-disposed`. The first two are the pair the packet demands.
  (Codes live here, not in `Capabilities/CapabilityReasonCodes.cs`, because that file is
  outside my File Scope. Precedent: `Features/Dtrh/DtrhCapabilityProbes.cs:41,52,72` already
  uses feature-local code strings. Flagged in `record.md`.)
- `TrayIconRequest.cs` — `ToolTip` (clamped to the 127-char `szTip` budget).
- `ITrayPresence.cs` — `Place(TrayIconRequest) : CapabilityState`,
  `Remove() : CapabilityState`, `bool IsPlaced`, `event EventHandler Activated`, `IDisposable`.
- `Win32TrayPresence.cs` — the real backend (below).
- `UnsupportedTrayPresence.cs` — the honest typed refusal. Never claims placement, `IsPlaced`
  is permanently false, `Remove()` also returns `Unavailable`, `Activated` never fires.
- `TrayPresenceFactory.cs` — `Create()` and `CreateFor(TrayHostPlatform)` so both branches are
  reachable deterministically from a test on either OS.

## Step 4 — Windows implementation

`Win32TrayPresence` over `Shell_NotifyIconW`, the same mechanism Avalonia's own
`Avalonia.Win32.TrayIconImpl` uses (verified above) and the same one WPF reaches through
`NotifyIcon` (`TrayIconService.cs:19`):

- a hidden **top-level** owner window (`WS_POPUP`, never shown, `WS_EX_TOOLWINDOW`) — not
  `HWND_MESSAGE`, because a message-only window cannot receive the `TaskbarCreated` broadcast
  the icon needs to survive an Explorer restart;
- `NIM_ADD` with `NIF_MESSAGE|NIF_ICON|NIF_TIP`; icon source = the process image's own icon
  via `ExtractIconExW`, falling back to `LoadIconW(IDI_APPLICATION)` — the same fallback shape
  as WPF `TrayIconService.cs:67-91`, and the state's `detail` names which source was used;
- **the claim is self-verified**: after `NIM_ADD` succeeds the backend re-asks the shell with
  `NIM_MODIFY`; if either call returns FALSE it returns
  `Unavailable(tray-mechanism-refused, …)` and leaves `IsPlaced` false. No path returns
  `Available` without the shell having said yes twice;
- `Remove()` → `NIM_DELETE`; `Dispose()` → delete + `DestroyWindow` + `UnregisterClass`;
- left-click / double-click on the icon arrives as a `WM_APP+1` callback and is republished as
  `Activated`; `TaskbarCreated` re-adds the icon;
- `Diagnostics` exposes `OwnerWindow`, `IconId`, `CallbackMessage` so a test can probe the
  effect from outside the product;
- asked to run off Windows it returns `Unavailable(tray-mechanism-absent, …)` and P/Invokes
  nothing, so the suite is portable rather than crashing.

## Step 5 — non-Windows

`TrayPresenceFactory` selects `UnsupportedTrayPresence` on Linux/macOS/unknown, with a reason
detail that names the real route (`Avalonia.FreeDesktop.DBusTrayIconImpl` /
`StatusNotifierItem` over DBus, `Avalonia.X11.XEmbedTrayIconImpl` for XEmbed, and the Wayland
"no XEmbed systray at all" fact) and the exact manual gate. **No Linux code claims success.**
`record.md` carries the BLOCKED line with the gate a Linux box must run; nothing is skipped to
hide it.

## Step 6 — proving it bites

Scratch-mutate `Win32TrayPresence` to return `Available` without calling `NIM_ADD`, run the
unit project, confirm red, restore byte-identically (`git diff --stat` empty + `git status`
clean), re-run green. Mutation never committed. Evidence goes in `record.md`.

## Tests — `client/tests/CcpClient.Tests/Tray*` (pure logic, no Avalonia runtime)

The heart of every effect test is one assertion shape:

```csharp
Assert.Equal(run.MachineHasNotificationArea, run.ShellSawIconAfterPlace);
```

The left side is a machine fact the **test** establishes independently
(`FindWindowW("Shell_TrayWnd", null) != 0`); the right side is the shell's own answer to
"does this (hWnd, uID) icon exist". A backend that claims success without placing reds; a
degenerate backend that always refuses also reds. Neither branch is a skip.

Planned facts (8), all with assertions at statement depth 0 and **no** `OperatingSystem.Is`,
`Environment.GetEnvironmentVariable`, `File.Exists`, early `return;`, or nested-only
assertions in the fact bodies — so no new entry is needed in
`client/tests/floor/vacuous-shape-ledger.json` (which is outside my File Scope). No wall-clock
token from `TestTimingGuardTests.ForbiddenTokens` appears anywhere.

1. the existence oracle answers **no** for an icon that was never placed (instrument control);
2. placing is confirmed by the shell, and the backend's claim equals the shell's answer;
3. removing is confirmed by the shell, and the backend stops claiming placement;
4. disposing leaves no icon and no owner window;
5. the icon's click notification becomes an `Activated` event (synthetic post + bounded
   `PeekMessage` pump — proves the routing, **not** that a human click works);
6. the unsupported backend refuses with a code and never claims placement;
7. `CreateFor(Linux)` refuses with `tray-mechanism-absent` and names SNI/DBus;
8. `CreateFor(Windows)` selects the Win32 backend.

`floor-delta.json`: `unit: 8, headless: 0` (final number confirmed against the gate).

## Wiring

**None.** Per A-014 this is infrastructure only: nothing is registered, no window's
close/minimize behaviour changes, `App.axaml.cs` / `Views/**` / `Features/**` are untouched.
DTRH's minimize-to-tray parity is **not** delivered by this packet.

## What this plan will not prove

Compile-and-unit-test proves no rendering, no composited pixels, no real mouse click, no
Explorer-restart recovery, and nothing at all about Linux. No `presentation-verified` claim
is made anywhere.
