# SP-119 — plan (checkpoint 1, committed BEFORE the first product edit)

Branch `lane/SP-119-haptic-seam`, base `4746d513`.
Pin **2191 unit / 133 headless**. `client/tests/floor/floor.json` will not be opened.

SP-116 committed its protocol before its first measurement, SP-117 made that the standard and
SP-118 followed it. This file is that commit for SP-119: the seam, the ownership point, the gate
and the dependency cost, all written down before any product file changes.

---

## 0. THE CENSUS I RE-VERIFIED, because the packet says to and because two of its four claims
##    turned out to need qualifying

SP-117 §1 named four blockers. Every one was re-read against the shipping source on this machine
before anything below rested on it.

| # | SP-117's claim | verified? | what I found |
|---|---|---|---|
| 1 | a seventh capability folder | **yes** | `client/src/CcpClient.Desktop/` has exactly six capability folders today: `Overlay`, `Input`, `Audio`, `Video`, `Pointer`, `Glyph`. There is no `Haptics`. |
| 2 | a NuGet dependency the csproj does not carry | **yes, for ONE of the two providers** | `ConditioningControlPanel.csproj:60` is `<PackageReference Include="Buttplug" Version="5.0.1" />`. `ButtplugProvider.cs:6-7` imports `Buttplug.Client` / `Buttplug.Core.Messages`. **`LovenseProvider.cs:1-8` imports nothing but the BCL** (`System.Net.Http`, `System.Text.Json`). So the dependency blocks the Buttplug half and **not** the Lovense half. §4 states the cost exactly. |
| 3 | app-scope wiring in `Lifecycle/**` | **yes** | `App.xaml.cs:533` (static field), `:2060` (constructed at startup), `:2103-2105` (auto-connect), `:4406` (`ShutdownStop()` FIRST at exit), `:4524` (`Dispose()`). `grep App.Haptics ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs` → **0 hits**: never engine-started. |
| 4 | a premium gate | **yes, and there are TWO of them, not one** | the packet cites `MainWindow/MainWindow.Haptics.cs:484-503` (the enable checkbox). There is a SECOND, deeper gate: `Services/Haptics/Core/HapticMixer.cs:191-204` (`IsGateOpen`), evaluated once per 10 Hz tick, with an open→closed **all-stop** at `:253-262`. The two gates do not agree — see §3.2. |

**Also verified, and it changes the shape of the refusal:** SP-117 §1.2's measurement ("no listener
on 12345, 20010 or 30010") is a property of this machine. It is **not** the blocker and it must not
be what the refusal names.

---

## 1. THE SEAM, SHOWN AGAINST BOTH PROVIDERS

### 1.1 The trap, stated as a fact about upstream rather than as a worry

The packet's central trap is "a seam shaped around a provider nobody has integrated". **That
failure already happened in the shipping product, in the very interface the packet cites, and it is
readable in three lines:**

`Services/Haptics/IHapticProvider.cs:23-28` writes the contract for `PingAsync` in its own words:

> *"Verify the device is still reachable. IsConnected can lie when the OS routing table changes
> after connect (e.g. user enables a VPN), so call this before any operation that needs to confirm
> we can actually talk to the device."*

- `LovenseProvider.PingAsync` (`:163-186`) **honours it**: a real HTTP round trip on a 1500 ms
  `CancellationTokenSource`, returning `IsSuccessStatusCode`.
- `ButtplugProvider.PingAsync` (`:215-221`) **does not**: `return Task.FromResult(IsConnected);` —
  the cached field — with a comment that admits it (*"this legacy shim does not"*).

So upstream's own two-provider seam has a member that one of its two implementations answers from a
field the contract's own text says can lie. **That is the trap, realised, in the code this packet is
told to port.** Every seam decision below is therefore justified against BOTH implementations or it
is not made.

### 1.2 The eight places the two providers DISAGREE, each read out of source

| # | question | `ButtplugProvider.cs` | `LovenseProvider.cs` | what the seam must do |
|---|---|---|---|---|
| D1 | what is on the other end | a **WebSocket** to `ws://127.0.0.1:12345` (`:27`, `:83`) into Intiface Central | **HTTP** to `http://127.0.0.1:20010` (`:21`), `POST /command` (LAN, `:83`) or `GET /command?...` (Connect, `:89`) | **neither is a driver.** Both are clients of a separate server process the user installs. The seam must never present itself as "talking to a toy". |
| D2 | who owns the STOP | **the client**: `Task.Delay(durationMs, token)` on a pool thread then `StopAsync` on every device (`:309-331`) | **the server**, quantized: `timeSec = Math.Max(1, durationMs / 1000)` (`:232-233`) — LAN mode; **nobody**, in Connect mode (`:242-243`: *"The device maintains vibration until next command"*) | the seam **must not** take a `durationMs` and promise a stop. It is **level-set + an explicit all-stop**. |
| D3 | can a sub-second pulse be expressed | yes — any `durationMs` | **no**: `Math.Max(1, …/1000)` floors every LAN duration at **one second** | see D2. A duration parameter would be unrepresentable in one of the two. |
| D4 | intensity resolution | `0..1` as `DeviceOutput.Vibrate.Percent(x)`, mapped by the client onto the feature's own step range (`:268`) | quantized to **0..20**, and levels 1-2 are deliberately unreachable: `i = 0` for `intensity <= 0.05`, else `3 + (int)((intensity - 0.05) / 0.95 * 17)`, clamped (`:200-204`) | the seam carries **0..1 only**. Quantization is the PROVIDER's, which is exactly what upstream's own v2 contract says (`Core/HapticContracts.cs:36-38`, `:70-82`). |
| D5 | does every command reach the wire | yes — no throttle | **no**: in continuous mode (`durationMs < 500`) a command inside 200 ms of the last one **returns without sending** (`:207-219`) | the seam may not promise delivery per call. `SetOutputs` returns a typed outcome and unchanged-send suppression is named as the provider's. |
| D6 | how many things are addressable | **every** device with a Vibrate output; one intensity fanned across every such feature (`:101-115`, `:264-278`) | **one** `_toyId` — and it is overwritten per toy in `ParseToys` (`:132`), so only the LAST discovered toy is addressable | the seam is **keyed by device AND by actuator index**. Without the key it is Lovense-legacy-shaped; without the index a two-motor device cannot be driven. Upstream repaired exactly this in v2 (`HapticContracts.cs:31-39`: *"Index disambiguates same-type motors (Edge=2 vibes, Lapis=3)"*). |
| D7 | what "connect failed" means | `false` for "server reachable, zero devices" (`:132-138`) **and** `false` from the catch for "no server" (`:140-145`) | the same collapse: `false` at `:116-117` for no toys, `false` at `:119-124` for a failed request | **the seam splits them.** `Unavailable(server unreachable)` and `DependencyMissing(a device)` are different typed states, and neither is the port's actual answer today. |
| D8 | is `IsConnected` trustworthy | a cached field (`:35-41`), and `PingAsync` returns it | a cached field (`:31`), and `PingAsync` really asks | **the seam has no `IsConnected` at all.** There is one way to ask and it is defined as touching the wire. A provider cannot answer it from a stale bool because there is no bool to answer it from. |

### 1.3 The seam that follows, and what each provider would need to implement it

```
Haptics/IHapticSink.cs

  HapticProviderRoute Route { get; }                                   // None in this build
  Task<HapticServerObservation> ObserveAsync(CancellationToken)         // D8: one way to ask, never cached
  Task<CapabilityState>        ConnectAsync(CancellationToken)          // D7: three answers, not two
  Task<CapabilityState>        SetOutputsAsync(string deviceKey,
                                   IReadOnlyList<HapticOutput>, CancellationToken)   // D2/D4/D6: level-set, keyed
  Task<CapabilityState>        StopAllAsync()                           // D2: the stop is its own verb
  CapabilityState?             LastOutcome { get; }
```

- **Buttplug would need:** the `Buttplug` 5.0.1 package (§4), a `ButtplugWebsocketConnector` to
  `ws://127.0.0.1:12345`, `StartScanningAsync` + the 2 s discovery wait (`ButtplugProvider.cs:89-95`),
  `HasOutput(OutputType.Vibrate)` to build the device list, and — because Buttplug outputs **latch**
  (`ButtplugProviderV2.cs:31-34`) — nothing at all for the hold. Its `StopAllAsync` is
  `device.StopAsync()` per device (`:280-284`). It would need **no keep-alive**.
- **Lovense would need:** no package, an `HttpClient` with the loopback-only certificate exception
  (`LovenseProvider.cs:41-52`), `GetToys` parsing for the device list (`:127-152`), and — because the
  LAN API's `timeSec` expires — **a keep-alive refresh or short-`timeSec` repeats**, which is
  precisely the provider's choice upstream's v2 contract leaves open
  (`HapticContracts.cs:70-73`). Its `StopAllAsync` is one `Function/Stop` POST (`:261-262`).

**The seam therefore fits both, and it fits them differently in the one place they really differ —
the hold.** That difference lives entirely inside an implementation and never in the interface. This
is also the shape upstream itself converged on with both providers in hand: `IHapticProviderV2`
(`HapticContracts.cs:105-137`) is `SetOutputsAsync(deviceId, outputs, ct)` + `StopAllAsync()` +
a `PingAsync` whose doc *requires* the wire — level-set, keyed, stop-as-its-own-verb.

### 1.4 What `Unavailable` will say, and what it will NOT say

`UnadmittedHapticSink` refuses with `haptic-no-admitted-provider` and a detail that names:
the two upstream routes and their transports; that **both are clients of a separate server process**;
what each would need here; and that the shipping app's own message for the other failure ("No devices
found. Connect your device in Intiface first." — `ButtplugProvider.cs:135`) is **not** what this is.

**It will never say "no device found."** A refusal that named a missing toy would be false: this
build has no client with which to look. The device gate exists as a separate, clearly downstream
constant (`HapticSinkFactory.DeviceManualGate`) and is labelled as unreachable-until-admission.

**This is also the first capability in the port whose refusal is not about the platform.** Both
providers are loopback network clients; neither is a Win32 API. So the factory does **not** switch on
`OperatingSystem.Is*` — it switches on whether any route is ADMITTED, and today the admitted-route
list is empty. That makes the refusal a property of the BUILD (SP-109's Brain Drain precedent),
identical on Windows and Linux, and it is earned by a predicate rather than stipulated.

---

## 2. THE APP-SCOPE OWNERSHIP POINT

`Haptics/HapticParticipant.cs` implements the existing `IBackgroundParticipant` and is registered in
`CompositionRoot.DefaultParticipants`. **No second lifetime model.** This is SP-118 §3's discipline
applied to a sink instead of a driver.

| property needed | mechanism already in the port | WPF's counterpart |
|---|---|---|
| constructed once, at composition, starting nothing | participant constructors are cheap (SP-003 §4.4) | `App.xaml.cs:2060` |
| the auto-connect attempt is a phase-3 act, not a constructor act | `StartAsync` | `App.xaml.cs:2103-2105` |
| the all-stop reaches the toys **before** anything is torn down | the **reserved pre-drain head slot** in `CompositionRoot.Build` — it runs before generations are cancelled and before any participant stops | `App.xaml.cs:4401-4407`: *"Haptics FIRST and synchronously (bounded ~2s) … cannot be left to Haptics.Dispose()"* |
| the sink is disposed after that | `StopAsync` in reverse registration order | `App.xaml.cs:4524` |
| its one setting reaches disk | the same reserved pre-drain slot | `App.Settings.Save()` on the gate path |

**Registration position: FIRST among the feature participants is wrong and LAST is wrong.** It goes
**last**, after the scheduler, for one reason that is testable: participant stop is reverse order, so
last-registered stops first, and the sink must be stopped before the session that would (one day)
drive it. The pre-drain slot makes the all-stop earlier still — which is upstream's ordering exactly.

**Ordering is a FACT, not a comment:** a test builds a root whose participant list is a recording
participant plus the haptic participant, drives `ShutdownAsync()`, and asserts the all-stop's
sequence number is lower than the recording participant's stop.

---

## 3. THE PREMIUM GATE — consuming `Entitlement/**` without widening it

### 3.1 What is consumed, and what is not touched
`Entitlement/**` is **byte-identical to base** at the end of this packet (asserted by
`git diff --stat`). `Haptics/HapticGate.cs` is a pure function
`EntitlementOutcome -> HapticGateDecision` built on `EntitlementOutcome.Match`, which does not
compile with a branch missing. Three decisions, and the two refusals are different TYPES:

- `Allow(tier)` — an authority confirmed a pledge at or above the bar.
- `RefusedNotEntitled(message)` — an authority answered about this account: no pledge.
- `RefusedUnverified(reasonCode, message)` — **nothing was decided about this user.**

**The bar is TIER 1, not tier 2.** WPF gates haptics on `HasPremiumAccess`
(`MainWindow.Haptics.cs:487`), and `HasPremiumAccess` is `CurrentTier >= PatreonTier.Level1`
(`Services/Account/PatreonService.cs:134`), which is `EntitlementTier.Supporter`. `DtrhGate` is
`EntitlementTier.Lab`. Same folder, same three answers, a different bar — that is what "consume it"
means here.

**This build's authority is `UnconfiguredTierSource`, so this door refuses everyone too, and it
refuses through `RefusedUnverified` — never `RefusedNotEntitled`.** That is a third door in the same
state as the two DTRH doors, with the same cause and the same owner decision behind it. It does not
widen the entitlement surface: no new producer of `NotEntitled` exists, and no code in this packet
maps an `Unavailable` onto a refusal message that says "you are not a patron". A mechanical fact
holds that, exactly as `DtrhGateTests` does for the DTRH wording.

### 3.2 A DISCREPANCY IN THE SHIPPING SOURCE, recorded rather than smoothed over

Upstream's two haptic gates use **different predicates**:

- checkbox (`MainWindow.Haptics.cs:487`): `App.Patreon?.HasPremiumAccess != true` — **one term.**
- mixer (`HapticMixer.cs:200-201`): `(App.Patreon?.HasPremiumAccess ?? false) || App.DailyFree?.IsFreeToday("haptics") == true` — **two terms**,
  and the rail lockband uses the two-term `TierGate.RequiresPremium(…, "haptics")` (`MainWindow.PremiumRail.cs:573`).

`"haptics"` is in `DailyFreeService.OverridableKeys` (`:49`) and **cut from the rotation `Pool`**
(`:40`, owner 2026-08-11), so it can only ever be free on a day the SERVER names. On such a day the
rail chip unlocks and the mixer opens **while the enable checkbox still refuses**. The port
implements term 1 only, the same way `DtrhGate` implements one of `RequiresLab`'s two terms (D24),
and records both the source discrepancy and the divergence.

### 3.3 Where the gate BITES — and it is two places, because upstream's is

1. **The enable toggle** (the ported surface). The order is upstream's and is load-bearing:
   `MainWindow.Haptics.cs:487-497` **returns before** `HapticCfg.Enabled = isEnabled` at `:499`, so a
   refused tick reverts the box and **writes nothing**. A headless fact drives the real control and
   asserts the document was not written.
2. **The output path.** `HapticMixer.IsGateOpen` is evaluated per tick and the open→closed
   transition **drops everything and stops the toys once** (`:253-262`). The port's participant holds
   the resolved outcome, and applying a non-allowing outcome after an allowing one owes an
   `StopAllAsync`. Divergence: the port resolves the entitlement **once at phase 3** (a DPAPI read
   plus an authority call is not a 10 Hz operation), so a pledge that lapses mid-run is noticed at
   the next launch, not within 100 ms.

---

## 4. THE DEPENDENCY DECISION'S EXACT COST — the line this packet stops at

**The csproj is not edited. Nothing below is a request; it is the priced menu.**

| option | csproj cost | code cost | what it buys | what it costs |
|---|---|---|---|---|
| **A. Admit Buttplug** | **one line**: `<PackageReference Include="Buttplug" Version="5.0.1" />` in `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj:24-42` | **one new file**, `Haptics/ButtplugHapticSink.cs`, implementing the §1.3 seam | every Intiface-supported device (that is the whole point of Intiface), on Windows and Linux alike, since the package is pure .NET over a WebSocket | a third-party package on the port's supply chain, unverified here for net10.0 TFM compatibility and for licence (the shipping tree's own note says BSD-3, `ButtplugProviderV2.cs:17-19`, and that is **read, not verified against the nupkg on this machine**) |
| **B. Admit Lovense** | **ZERO** — `System.Net.Http` + `System.Text.Json` are BCL | **one new file**, `Haptics/LovenseHapticSink.cs`, plus a URL/mode setting surface and the keep-alive of D2 | Lovense toys only, through Lovense Connect or Lovense Remote | a per-vendor client; the keep-alive is real work; the LAN one-second `timeSec` floor (D3) is a permanent behaviour limit |
| **C. Admit Buttplug WITHOUT the package** | zero | **a redesign, not a file**: a hand-written Buttplug **message-spec v4** client over `System.Net.WebSockets.ClientWebSocket` — handshake, `OutputCmd`, per-feature capability model, device add/remove, spec-version tracking | avoids the supply-chain question | the port would own a wire protocol whose spec is versioned by someone else; `ButtplugProviderV2.cs:13-27` is the shipping tree's account of how much that spec moved between v3 and v4 |
| **D. Admit nothing** | zero | zero | the port stays honest and ships this capability's refusal | haptics remains unported; the thirteen effect modules stay silent to it |

**The one-sentence answer the owner is owed:** *the Buttplug half costs exactly one `PackageReference`
line and one file; the Lovense half costs no package at all and one file plus a keep-alive; and
refusing the package does not block haptics, it blocks Buttplug specifically.* This packet lands D.

---

## 5. THE ROW, THE DOT, AND WHAT THIS PACKET IS NOT

**Haptics IS a rack row upstream** — `Views/Tabs/StudioTabView.xaml.cs:519-527`, LAST in the
IMMERSION group, after Brain Drain, `tier: 1` ("the rack's one paid module"), with a dot predicate
(`() => App.Settings?.Current?.Haptics?.Enabled`, `:520`) and an explicit right-click toggle that
flips the panel's own master box *"so MainWindow.ChkHapticsEnabled_Changed runs — including the
premium gate that reverts the box for a free account"* (`:521-525`). So this row **has** a dot, and
its gesture goes through the gate. Both are ported.

**The dot's meaning, earned rather than read off the checkbox** (SP-118's D180 discipline):

```
Off    = the enable is off, OR nothing in this build can reach a device
Armed  = the enable is on AND a device is really reachable
Live   = UNREACHABLE IN THIS BUILD, and that is D179 made visible
```

`Live` would mean "something is being sent". **Nothing ever is.** The thirteen ported effect modules
are silent to this sink — upstream calls it from eight sites in three of them (`FlashService.cs:1453`,
`:1480`, `:1516`, `:1915`; `VideoService.cs:2580`, `:4585`, `:6580`; `SubliminalService.cs:230`) and
`Effects/**` is CLOSED to this packet. So the dot cannot reach `Live`, a fact pins that, and the
panel says it in words.

**A landed capability is not a working feature.** Nothing in this packet makes a toy move, and
nothing in it gives any effect a haptic limb.

---

## 6. FILE PLAN (inside File Scope, and nothing else)

**New — `client/src/CcpClient.Desktop/Haptics/`:** `HapticReasonCodes.cs`, `IHapticSink.cs`
(the seam + `HapticOutput` + `HapticLevel` + `HapticServerObservation` + `HapticAdmission.Classify`),
`UnadmittedHapticSink.cs`, `HapticSinkFactory.cs`, `HapticGate.cs`, `HapticSettingsDocument.cs`,
`HapticParticipant.cs`.
**Changed:** `Lifecycle/CompositionRoot.cs` (the tenth participant, the sink seam, the capability
registration, the pre-drain head slot), `Views/MainWindow.axaml.cs` (the participant reached off the
host), `Views/Pages/StudioPage.axaml` + `.axaml.cs` (the row in WPF's position, the dot, the
right-click, the panel), `Views/Pages/HapticsPanelNotices.cs` (new).
**Tests:** `HapticCapabilityTests.cs`, `HapticGateTests.cs`, `HapticParticipantTests.cs` (unit);
`HapticsRowHeadlessTests.cs` (headless); plus zero-count edits to
`CompositionRootValidationTests.cs`, `IntegrationProofTests.cs`, `StudioRackHeadlessTests.cs`.
**Docs:** `client/docs/wpf-surface-reachability.md` (§SP-119, D191+),
`client/docs/verification-harness.md` (the haptic evidence class).
**Packet:** `record.md`, `floor-delta.json`, `sweep.mjs` + logs.

**Untouched, and asserted:** all six existing capability folders, `Effects/**`, `Entitlement/**`,
`Scheduling/**`, `Persistence/**`, the csproj, `floor.json`, both floor scripts, `task-board.md`.
