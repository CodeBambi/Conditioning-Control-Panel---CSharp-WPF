# SP-117 — plan: the three-way census, committed before one line of product code

Branch `lane/SP-117-last-three`, base `2a991d61`. Twelve modules run; three rack rows remain.
This document is the inventory SP-112 §1 set the standard for. **Every citation below was opened
in the shipping tree and read**, because SP-113 found `AppSettings.cs` citations wrong by ~530
lines and in the wrong path — and that path error reproduces here, so it is fixed first.

---

## 0. THE PATH CORRECTION, before any claim rests on it

**There is no `ConditioningControlPanel/Models/AppSettings.cs`.** The only `AppSettings.cs` in
this repository is `ConditioningControlPanel/CCP.Core/Models/AppSettings.cs` (6969 lines), and
the shipping WPF app reaches it by **project reference**
(`ConditioningControlPanel/ConditioningControlPanel.csproj:52`,
`<ProjectReference Include="CCP.Core\CCP.Core.csproj" />`) while excluding the `CCP.Core\**`
directory from its own globs (`:10`). So the settings model is simultaneously **inside the
`CCP.*` tree on disk** and **shipping product code by reference**. Citations of the form
`AppSettings.cs:NNN` in this port's source are line-correct against that file; only the implied
path is wrong. Every `AppSettings.cs` line below is verified against
`ConditioningControlPanel/CCP.Core/Models/AppSettings.cs`.

---

## 1. VISUALS — and the census says it is not a module, which is exactly why it can ship

### 1.1 The shipping service, and its real surface

**There is no service.** `grep -rn "VisualsService" ConditioningControlPanel --include=*.cs`
returns nothing. Visuals is a rack row whose panel is a **settings page for another module**.

| what | where | verified |
|---|---|---|
| rack entry | `Views/Tabs/StudioTabView.xaml.cs:496` — `Add("visuals", "👁", null, "Visuals", "section_visuals", HostVisuals, PanelVisuals, "Visuals", null)` | yes — the last argument is the dot predicate and it is **`null`** |
| why no dot | `:494-495` — "Visuals has no single master toggle - the dashboard card is deliberately neutral too (MainWindow.Presets.cs:800). A dot that cannot be wired honestly is omitted." | comment verified; **its own citation does not** — see §1.5 |
| host | `Views/Tabs/StudioTabView.xaml:223` (`ScrollViewer x:Name="HostVisuals"`), `:226` (`<feat:VisualsFeatureControl x:Name="PanelVisuals"/>`) | yes |
| panel | `Features/VisualsFeatureControl.xaml` (146 lines) + `.xaml.cs` (120 lines) | yes |
| panel says so itself | `VisualsFeatureControl.xaml:12-14` — "No art ... and no enable pill: Visuals is the one module with no master toggle at all" | yes |

The whole code-behind is five load/save pairs against `App.Settings.Current` and nothing else
(`VisualsFeatureControl.xaml.cs:35-118`). No timer, no window, no thread, no device.

### 1.2 Every capability it needs — the five dials, and who reads them

| # | control | XAML | setting | field/prop | default | clamp | **who reads it** |
|---|---|---|---|---|---|---|---|
| 1 | `SliderSize` 50..250 | `:54-56` | `ImageScale` | `AppSettings.cs:839` / `:846-850` | 100 | `Math.Clamp(v,50,250)` `:849` | `FlashService.cs:484`, `:620`, `:656`, `:1002`, `:1965` → `CalculateGeometry` `:2290-2315` |
| 2 | `SliderOpacity` 10..100 | `:70-72` | `FlashOpacity` | `:853` / `:856-861` | 100 | `Math.Clamp(v,10,100)` `:859` | `FlashService.cs:2072` `maxAlpha`, applied `:2108-2117` |
| 3 | `SliderFade` 0..100 | `:86-88` | `FadeDuration` | `:892` / `:893-897` | 40 | `Math.Clamp(v,0,200)` `:896` | **NOBODY — see §1.4** |
| 4 | `SliderDuration` 1..30 | `:102-104` | `FlashDuration` | `:925` / `:926-930` | 5 | `Math.Clamp(v,1,30)` `:929` | `FlashService.cs:1034` → lifetime `:1073` |
| 5 | `ChkAudio` | `:124-126` | `FlashAudioEnabled` | `:899` / `:900-904` | true | — | `FlashService.cs:1037` → `duration = PlaySound(...)` `:1042` |

**Every one of the five belongs to Flash Images**, a module this port landed at SP-098/SP-100.

### 1.3 Which of the port's six landed capabilities covers it

**Overlay, and it is already consumed by this exact code path.** No capability folder needs a
line changed:

| dial | the port's existing seam | what is missing |
|---|---|---|
| ImageScale | `FlashGeometry.Size(..., int scalePercent)` — **already a parameter** (`Effects/FlashGeometry.cs:46-62`) | only the value: `FlashSurfacePresenter.cs:244` passes the constant `ImageScalePercent = 100` (`:81`) |
| FlashOpacity | `OverlaySurfaceRequest(placement, opacity, ClickThrough)` — **already a parameter** (`Overlay/OverlaySurfaceRequest.cs:47`) | only the value: `FlashSurfacePresenter.cs:264` passes `OpacityPercent / 100.0` with `OpacityPercent = 100` (`:84`) |
| FlashDuration | `OverlaySurfaceSet.Place(slot, request, frame, lifetime)` — **already a parameter** | only the value: `FlashSurfacePresenter.cs:265` passes `SurfaceLifetime`, a `static readonly` off the constant (`:90-91`) |

**The port's own source already names this debt, in as many words.**
`Persistence/SessionPresetDocument.cs:17-23`:

> "Every value here is a dial some effect actually consumes. WPF's flash block carries a dozen
> more (`FlashOpacity`, `ImageScale`, `FlashDuration`, `FadeDuration`, …) and every one of them
> describes how a flash is DRAWN. **The port draws no flash** …, so persisting them would write
> settings nothing reads … **They arrive with the surface that honours them.**"

**The surface arrived at SP-100. The dials never did.** That sentence is also now stale on its
face — the port DOES draw flashes — and correcting it is a divergence-ledger row, not a licence
to edit `Persistence/**` (out of File Scope; SP-101's one-document-per-module precedent applies,
`Session/PinkFilterPresetDocument.cs:11-16`).

**Second-order payoff, and the port set the condition for it itself.**
`Session/SessionParticipant.cs:302-306`: "WPF links five (`AppSettings.cs:2589-2621`); **flash
opacity**, master volume and subliminal volume have **no dial on any ported panel**, so they are
absent rather than present-and-inert (D93)." WPF's five ramp links are verified at
`MainWindow/MainWindow.StartStop.cs:506-510` (FlashOpacity, cap 100), `:512-518` (Spiral, 100),
`:520-524` (PinkFilter, **50**), `:526-533` (MasterVolume), `:535-539` (SubAudioVolume). Landing
a flash-opacity dial on a ported panel is the exact precondition D93 named for the **third** ramp
link.

### 1.4 What is precisely missing — and two dials that must be ABSENT, with the enumeration

**(a) `FadeDuration` is a DEAD DIAL in the shipping product.** Enumerated by `grep` over the
whole repository, every hit classified:

| site | kind |
|---|---|
| `Features/VisualsFeatureControl.xaml.cs:46,47` | writer→UI (load) |
| `:59` | change-notification filter |
| `:96` | writer→model |
| `CCP.Core/Models/AppSettings.cs:892-897` | the field and its clamp |
| `CCP.Core/Models/Preset.cs:68,178,202,226,253,292` | preset defaults |
| `:338` | preset→settings writer |
| `:456` | settings→preset reader |
| `CCP.Avalonia/Features/VisualsFeatureControl.axaml.cs:51,52,64,95` | the abandoned port's copy of the same panel |

**Zero readers that change anything on screen.** The fade the user actually sees is a
**hard-coded constant**: `FlashService.cs:2018` `private const double FADE_PER_SEC = 2.4`,
consumed at `:2073` `var fadeStep = FADE_PER_SEC * dt;` and applied at `:2110-2117`. The slider
is also the only one of the four sliders on the page **not** marked
`feat:SessionLock.Owned="True"` (`:87` against `:55`, `:71`, `:103`), and `SessionSettings`
carries `FlashOpacity` (`CCP.Core/Models/Session.cs:876`), `FlashScale` (`:878`) and
`FlashAudioEnabled` (`:881`) but **no** `FadeDuration`. Porting it would be the greyed dial this
port refuses (D93). **It ships ABSENT, with this table as the proof.**

**(b) `FlashAudioEnabled` cannot ship, because the port's flash makes no sound.** Its whole
meaning is `FlashService.cs:1037-1042`: when a flash has a sound file, play it and let **the
sound's own length replace the duration**. The port's `FlashImagesEffect.Deliver`
(`Effects/FlashImagesEffect.cs:157-161`) hands paths to a surface and raises an event; there is
no sound path, no pool of flash audio and no call into `Audio/**` anywhere in the flash module.
A checkbox here would move nothing. **ABSENT, D93, and the panel says so.**

### 1.5 The citation in the SHIPPING source that does not verify

`StudioTabView.xaml.cs:494-495` justifies the missing dot with "the dashboard card is
deliberately neutral too (`MainWindow.Presets.cs:800`)". Checked: `MainWindow/MainWindow.Presets.cs:800`
is inside `ShowMediaDropChoiceDialog` (a three-choice media-drop dialog); the only occurrence of
`Visuals` in that file is `:41`, a comment about **help buttons**; and `grep -rn "visuals"` over
`ConditioningControlPanel/MainWindow/*.cs` finds no dashboard card at all. **The behavioural fact
the port needs is verified directly and does not depend on that citation:** the dot predicate
argument at `:496` is literally `null`. Recorded so the next reader does not chase it.

### 1.6 VERDICT — **VISUALS SHIPS.** It needs nothing that does not exist.

Three live dials, three parameters that are already parameters, one landed capability consumed
and not edited, two dials declared absent with their enumeration attached, no dot (upstream's own
decision, ported), and one ramp link that D93 said was blocked only on this row existing.

---

## 2. HAPTICS — refused, with an inventory

### 2.1 The shipping service and its real surface

| what | where | verified |
|---|---|---|
| rack entry | `Views/Tabs/StudioTabView.xaml.cs:519-528`; the row is `tier: 1` at `:528` — the rack's **one paid module** | yes |
| the row has **no host**, only a panel | `:519` passes `PanelHaptics` then `null, null` where every other row passes host+panel | yes |
| panel | `Views/Tabs/HapticsTabView.xaml` (**1640** lines) + `.xaml.cs` (215) | yes |
| panel wiring | `MainWindow/MainWindow.Haptics.cs` (**1091** lines) | yes |
| premium gate | `MainWindow.Haptics.cs:484-503` — enabling with `App.Patreon?.HasPremiumAccess != true` **reverts the box** and shows a message | yes |
| the service | `Services/Haptics/**` — **9193 lines** over 21 files | yes (`wc -l`) |
| lifetime | **APP-scoped, not session-scoped**: `App.xaml.cs:533` static, `:2060` constructed at startup, `:2103-2105` auto-connect at startup, `:4406` `ShutdownStop()`, `:4524` `Dispose()` | yes |
| **never engine-started** | `grep "App.Haptics" MainWindow/MainWindow.StartStop.cs` → **zero hits** | yes |
| how it is driven | **reactively, by other modules**: 99 call sites of `App.Haptics` across **29** shipping files | yes |

`IHapticProvider` (`Services/Haptics/IHapticProvider.cs:8-29`) is the device contract:
`ConnectAsync`, `DisconnectAsync`, `VibrateAsync(intensity, durationMs)`, `StopAsync`,
`PingAsync`, plus `IsConnected`, `ConnectedDevices` and three events.

### 2.2 The device stack, named exactly

| provider | transport | evidence |
|---|---|---|
| Buttplug / Intiface | **WebSocket to `ws://127.0.0.1:12345`**, via the `Buttplug` NuGet package 5.0.1 | `Services/Haptics/ButtplugProvider.cs:6-7` (`using Buttplug.Client`), `:27` (the URL), `:83` (`new ButtplugWebsocketConnector(new Uri(_serverUrl))`); v2 at `ButtplugProviderV2.cs` (885 lines) |
| Lovense | **HTTP to `http://127.0.0.1:20010`** (Connect PC) or a phone on the LAN | `Services/Haptics/LovenseProvider.cs:20-21`, `:82-89` (`GetToys`), `:244` (the vibrate URL); v2 at `LovenseProviderV2.cs` (1673 lines) |
| Mock | in-process | `MockHapticProvider.cs`, `Core/MockProviderV2.cs` |

**Neither is a device driver.** Both are clients of a **separate server process** the user
installs and runs; the app never touches BLE.

### 2.3 Which of the port's six landed capabilities covers it

**NONE. Not one.** Overlay, input, audio, video, pointer and glyph are all *display and
peripheral* capabilities on this machine's own screen and keyboard. Haptics needs a **loopback
network client** and a **third-party server**, which is a seventh capability with no relative in
the folder set.

### 2.4 Precisely what is missing

1. **A NuGet dependency** (`Buttplug` 5.0.1) that `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj`
   does not carry (`:24-42` is the whole package list; there is no Buttplug and no WebSocket
   client). Adding it needs a **csproj edit — outside this packet's File Scope** — and a
   dependency-admission checkpoint with both advisory seats (`client/docs/port-workflow.md`,
   "Mandatory checkpoints" #3).
2. **A capability folder that does not exist.** Every landed capability earns `Available` by
   asking the OS back. The haptics analogue is asking the *device* back, and
   `IHapticProvider.PingAsync` exists upstream for exactly that reason
   (`IHapticProvider.cs:23-28`: "IsConnected can lie when the OS routing table changes after
   connect"). That is a new folder, and **all six are closed to editing**.
3. **A place to live.** It is app-scoped in the shipping product, so its port would wire into
   `Lifecycle/CompositionRoot.cs` — also outside this File Scope.
4. **A premium gate.** `Entitlement/**` exists in the port but the gate is upstream's
   `App.Patreon.HasPremiumAccess`, and no ported row is gated today.

### 2.5 The named limit — measured, not assumed

**This machine has no haptic device stack running.** `netstat -ano -p tcp` at 2026-08-20 shows
**no listener on 12345, 20010 or 30010**; the only loopback listeners are 11434, 15292 and 51703.
A physical toy is also required and none is attached.

**That is a named limit, not a blocker and not a skip.** It does not stop a port of haptics from
being written; it stops any run of it from ever earning `Available`, which means the honest
artefact would be a capability that returns a typed refusal on this machine at every operation —
and a `WIP`/`BLOCKED` board row naming "connect an Intiface Central or Lovense Connect server and
one device" as the manual gate. **The blocker is items 1-4, all four of which are File Scope
violations. The device is a footnote to them.**

### 2.6 VERDICT

**REFUSED.** Cheapest path first: a new capability folder plus a new dependency plus an
app-scope wiring point, none of them writable from here.

---

## 3. SCHEDULER — refused, and the refusal is the finding SP-108 predicted

### 3.1 The shipping service and its real surface — SP-108's claim, verified line by line

There is a `Services/Scheduler/SchedulerService.cs`, but it is in `CCP.Core` and **the shipping
WPF app does not use it**: the scheduler the product runs is four methods on `MainWindow`.

| what | where | verified |
|---|---|---|
| rack entry | `Views/Tabs/StudioTabView.xaml.cs:535-537`, quick-toggle flips the panel's own enable box | yes |
| the timer | `MainWindow/MainWindow.xaml.cs:206` field, `:616-618` `new DispatcherTimer { Interval = 30 s }`, `:620` `Tick += SchedulerTimer_Tick` | yes |
| the 60 s grace | `MainWindow.xaml.cs:623-635` — `Task.Delay(60 s)` then `Start()` **and** `CheckSchedulerOnStartup()` | yes |
| stopped at | `MainWindow/MainWindow.WindowChrome.cs:166` | yes |
| startup check | `MainWindow/MainWindow.StartStop.cs:562-579` | yes |
| settings-change check | `:583-598`, called from `MainWindow/MainWindow.Settings.cs:368` | yes |
| the tick | `:601-637` | yes |
| the predicate | `:642-696` `IsInScheduledTimeWindow()` | yes |

### 3.2 What the tick actually does — the disqualifying detail, in the source

`SchedulerTimer_Tick` (`MainWindow.StartStop.cs:601-637`):

* enters the window while `!_isRunning` → `_trayIcon?.MinimizeToTray()`, tray notification,
  **`StartEngine()`** (`:612-618`)
* leaves the window while `_isRunning && _schedulerAutoStarted` → **`StopEngine()`**, notification
  (`:625-631`)
* outside the window → resets `_schedulerAutoStarted` and `_manuallyStoppedDuringSchedule` (`:634-636`)

**It runs when nothing is running, and its output is `StartEngine` / `StopEngine`.** A session
module runs *under* the engine. **SP-108's belief holds, and it is now verified rather than
inherited.** The port already asserts this in a comment that can now cite evidence:
`Views/Pages/StudioPage.axaml:283-284`.

`IsInScheduledTimeWindow` (`:642-696`) is: seven per-day booleans switched on
`DateTime.Now.DayOfWeek` (`:648-659`); `TimeSpan.TryParse` of `SchedulerStartTime` /
`SchedulerEndTime` with fallbacks **16:00** and **22:00** (`:663-675`); and the window test
`endTime < startTime ? (now >= start || now < end) : (now >= start && now < end)` (`:679-690`) —
an overnight wrap, half-open at the end.

### 3.3 Which of the port's six landed capabilities covers it

**NONE, and it needs none of them.** Scheduler draws nothing, plays nothing, and takes no input.
It is the only one of the three whose refusal is **not** about a missing capability.

### 3.4 Precisely what is missing

| need | port status |
|---|---|
| start/stop a session from outside one | `Session/SessionEngine.cs:131` `Start()`, `:163` `Stop()`, `:98` `Running` — **exists** |
| a tray notification | `Tray/ITrayPresence.cs:77` `ShowNotification(TrayNotification)` — **exists** (not one of the six) |
| minimize to tray | `Tray/**` — exists in some form; not surveyed further because item 5 already refuses the row |
| **local wall-clock day-of-week and time-of-day** | **MISSING.** `ISessionClock` offers `UtcNow` and `Schedule` only (`Session/SessionClock.cs:17-25`). The predicate is entirely `DateTime.Now` local time plus `DayOfWeek`; deriving it from `UtcNow` needs `TimeZoneInfo.Local`, a machine property no fact can pin, so the window boundaries would be untestable — a straight violation of the no-wall-clock rule the port has held since SP-059 |
| **an app-lifetime owner** | **MISSING, and out of scope.** The row is not a `SessionEffect`, so it does not belong on `SessionParticipant`; it belongs beside the shell in `Lifecycle/CompositionRoot.cs` — **outside this packet's File Scope** |

The clock gap is fixable inside `Session/**` in principle. It is **not** taken here: `ISessionClock`
is consumed by every one of the twelve landed modules and both presenters, so widening it is an
equivalence claim over a symbol with more consumers than this packet can discharge by name
(`port-workflow.md` §"The equivalence rule"). And it would not help, because the app-lifetime
owner still refuses the row.

### 3.5 VERDICT

**REFUSED, and the refusal is a POSITIVE finding.** Scheduler is not a session module; it is an
app-lifetime supervisor of the session engine. It does not belong to the effect spine at all, so
"the effect spine is complete" and "the Scheduler row is unported" are both true at once. Its
correct home is a board row against `Lifecycle/**`, not a rack row.

---

## 4. THE CHOICE

**Visuals.** It is the only one of the three that needs nothing the port does not already have,
and the only one whose File Scope is entirely inside this packet's. It also discharges a debt the
port's own source wrote down twice (`SessionPresetDocument.cs:17-23`,
`SessionParticipant.cs:302-306`).

**What ships:** a `Visuals` rack row in EFFECTS, in WPF's own position (sixth, after Pink Filter
— `StudioTabView.xaml.cs:484-496`), **with no dot**, whose panel holds three live dials that
reach the pictures the operating system is already confirmed to be holding; plus WPF's third ramp
link, `RampLinkFlashOpacity`, which D93 blocked only on this row's existence.

**What is declared absent, with §1.4's enumeration as the proof:** the fade slider (dead
upstream) and the link-to-audio checkbox (the port's flash is silent).

**Not touched:** all six capability folders, `floor.json`, the board, `client/tools/**`, and the
whole shipping tree.

## 5. RISKS TAKEN ON, named before starting

1. **`FlashSurfacePresenter` is consumed by a landed module and its landed facts.** Turning three
   constants into a read is a mutation of a symbol with existing consumers; every one gets
   enumerated by `grep` and discharged by name before any equivalence is claimed.
2. **Adding a third ramp dial changes `IntensityRampEffect`'s input list.** Landed ramp facts
   assert against a two-dial list. If any of them asserts the COUNT, the fact is a legitimate
   list-grew edit (the SP-105/106/108/109/111/112 precedent) and never a weakened assertion.
3. **A dial read at draw time, not at schedule time.** WPF reads `App.Settings.Current` inside
   `ShowImages` (`FlashService.cs:1028`) and inside `LoadImagesUntilAsync` (`:655-656`), i.e. at
   the moment the flash fires. The port must read at the same moment or a mid-session ramp write
   would not reach the next flash.
