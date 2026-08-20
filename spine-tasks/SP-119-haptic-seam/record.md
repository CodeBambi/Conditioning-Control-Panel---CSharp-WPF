# SP-119 — record

Branch `lane/SP-119-haptic-seam`, base `4746d513`.
Floor: pin **2191 unit / 133 headless**; observed **2247 unit / 141 headless**, zero failures;
declared **+56 unit / +8 headless** (`floor-delta.json`). 2191 + 56 = 2247 and 133 + 8 = 141,
confirmed by `node client/tests/floor/sum-deltas.mjs --check --packets SP-119-haptic-seam`. The floor
run therefore REPORTS a violation against the pin, which is the expected shape: the orchestrator sums
the deltas and applies one bump. Two skips, both pre-existing
(`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); none added, none widened.
Build: 0 errors, 0 warnings (`check-warnings.mjs`, forced non-incremental, 4 projects).
`client/tests/floor/floor.json` was never opened.

> **The plan was committed before the first product edit**, at
> `spine-tasks/SP-119-haptic-seam/plan.md`, commit `8b88b5a4`. SP-116 committed its protocol before
> its first measurement, SP-117 made that the standard and SP-118 followed it. §1 below is the
> summary; the census verification and the priced dependency menu are in `plan.md` and are not
> restated in full.

---

## 0. THE HEADLINE — a capability that refuses on every platform, and why that is the honest answer

**Six capabilities in this port refuse on Linux and work on Windows.** This one refuses on both, and
the reason is not the platform: it is that **this build admits no provider CLIENT at all.**

Upstream's two haptic providers are a WebSocket client to `ws://127.0.0.1:12345`
(`Services/Haptics/ButtplugProvider.cs:27,83`) and an HTTP client to `http://127.0.0.1:20010`
(`Services/Haptics/LovenseProvider.cs:21,83,89`). **Neither is a driver. Both are clients of a
separate server process the user installs** — Intiface Central, Lovense Connect or Lovense Remote —
and nothing in either one is Windows-only. So `HapticSinkFactory` does not switch on
`OperatingSystem.Is*` at all. It asks whether any provider ROUTE is admitted, and
`AdmittedRoutes` is empty. The refusal is produced by reading that list, so the same expression stops
refusing the day a route is admitted; it was not written as a constant "no".

**Three things this packet is careful NOT to claim, stated at the top because each is a lie somebody
could reasonably read into a landed capability:**

1. **It is not a working feature.** No effect module sends anything to this sink (§5).
2. **It does not say "no device found."** That refusal would be false: there is no client here with
   which to look (§3).
3. **It does not decide the dependency.** §6 prices the decision; it does not make it.

---

## 1. THE CENSUS, RE-VERIFIED — and one of SP-117's four blockers needed qualifying

SP-117 §1 named four blockers and the packet says to verify them. All four hold; **one is narrower
than it reads**, and that narrowing is the whole of §6.

| # | SP-117's claim | verdict |
|---|---|---|
| 1 | a seventh capability folder | **holds.** Six existed (`Overlay`, `Input`, `Audio`, `Video`, `Pointer`, `Glyph`); `Haptics` is the seventh |
| 2 | a NuGet dependency the csproj does not carry | **holds for ONE of the two providers only.** `ConditioningControlPanel.csproj:60` is `Buttplug` 5.0.1 and `ButtplugProvider.cs:6-7` imports `Buttplug.Client`/`Buttplug.Core.Messages`. **`LovenseProvider.cs:1-8` imports nothing but the BCL** (`System.Net.Http`, `System.Text.Json`). The dependency blocks the Buttplug half and not the Lovense half |
| 3 | app-scope wiring in `Lifecycle/**` | **holds.** `App.xaml.cs:533`, `:2060`, `:2103-2105`, `:4406`, `:4524`; zero hits for `App.Haptics` in `MainWindow/MainWindow.StartStop.cs` |
| 4 | a premium gate | **holds, and there are TWO of them.** The packet cites the enable checkbox (`MainWindow/MainWindow.Haptics.cs:484-503`). The second is `Services/Haptics/Core/HapticMixer.cs:191-204` (`IsGateOpen`), evaluated once per 10 Hz tick, with an open→closed **all-stop** at `:253-262`. Both are ported (§4) |

**SP-117 §1.2's measurement is confirmed as a NAMED LIMIT and not the blocker.** No listener on
12345, 20010 or 30010 on this machine. That is a property of the machine; it does not stop a port
being written and it is not what any refusal in this packet names.

---

## 2. THE SEAM, JUSTIFIED AGAINST BOTH PROVIDERS

### 2.1 The trap is not hypothetical — it already happened, in the interface the packet cites

The packet's central trap is "a seam shaped around a provider nobody has integrated". **That failure
is readable in three places in the shipping source.**

`Services/Haptics/IHapticProvider.cs:23-28` writes its own contract for `PingAsync`:

> *"Verify the device is still reachable. IsConnected can lie when the OS routing table changes after
> connect (e.g. user enables a VPN), so call this before any operation that needs to confirm we can
> actually talk to the device."*

- `LovenseProvider.PingAsync` (`:163-186`) **honours it**: a real HTTP round trip on a 1500 ms
  `CancellationTokenSource`, returning `IsSuccessStatusCode`.
- `ButtplugProvider.PingAsync` (`:215-221`) **does not**: `return Task.FromResult(IsConnected);` —
  the cached field the contract's own text says can lie — with a comment admitting it.

**So upstream's own two-provider seam has a member one of its two implementations answers from a
field the contract forbids.** Every seam decision below is therefore justified against both or it is
not made.

### 2.2 The eight disagreements, and what each one forced

| # | question | Buttplug | Lovense | the seam's answer |
|---|---|---|---|---|
| D1 | what is on the other end | WebSocket into Intiface (`:27,83`) | HTTP into Connect/Remote (`:21,83,89`) | every refusal is worded from "another program", never "a device" |
| D2 | who ends a pulse | **the client**: `Task.Delay(durationMs)` then a per-device stop (`:309-331`) | **the server**: `timeSec = Math.Max(1, durationMs/1000)` (`:232-233`); in Connect mode **nobody** — *"the device maintains vibration until next command"* (`:242-243`) | **no duration in the seam.** `SetOutputsAsync` is level-set; `StopAllAsync` is its own verb |
| D3 | can a sub-second pulse be expressed | yes | **no** — floored at one whole second | as D2 |
| D4 | intensity resolution | `Percent(x)` onto the feature's step range (`:268`) | 0..20, levels 1-2 unreachable (`:200-204`) | **0..1 only**, clamped; quantization is the provider's |
| D5 | does every command reach the wire | yes | **no** — inside 200 ms in continuous mode it returns without sending (`:207-219`) | the seam promises a typed outcome, never delivery |
| D6 | what is addressable | every device with a Vibrate output; one value fanned across all (`:101-115`, `:264-278`) | **one** `_toyId`, overwritten per toy (`:132`) — only the LAST is addressable | **keyed by device AND actuator index** |
| D7 | what "connect failed" means | `false` for no-server AND for no-device (`:132-145`) | the same collapse (`:116-124`) | **three typed answers**, and the ADMISSION question is asked first |
| D8 | is `IsConnected` trustworthy | a cached field, and `Ping` returns it | a cached field, and `Ping` really asks | **there is no `IsConnected` and no `Ping`** |

### 2.3 The seam, and what each provider would need to implement it

```
Task<HapticServerObservation> ObserveAsync(ct)                         // D8
Task<CapabilityState>         ConnectAsync(ct)                          // D7
Task<CapabilityState>         SetOutputsAsync(deviceKey, outputs, ct)   // D2/D4/D6
Task<CapabilityState>         StopAllAsync()                            // D2
HapticProviderRoute Route ;  CapabilityState? LastOutcome
```

- **Buttplug would need** the package (§6), a `ButtplugWebsocketConnector` to `ws://127.0.0.1:12345`,
  `StartScanningAsync` plus the 2 s discovery wait (`:89-95`), `HasOutput(OutputType.Vibrate)` for
  the device list, and **no keep-alive at all**, because Buttplug outputs latch
  (`ButtplugProviderV2.cs:31-34`). Its all-stop is `device.StopAsync()` per device (`:280-284`).
- **Lovense would need** no package, an `HttpClient` whose certificate exception is loopback-only
  (`:41-52`), `GetToys` parsing (`:127-152`), and **a keep-alive refresh or short-`timeSec` repeats**,
  because the LAN command expires. Its all-stop is one `Function/Stop` POST (`:261-262`).

**The one place they really differ — the hold — lives entirely inside an implementation and never in
the interface.** That is also the shape upstream itself converged on once it had both providers in
hand: `IHapticProviderV2` is `SetOutputsAsync(deviceId, outputs, ct)` + `StopAllAsync()` + a
`PingAsync` whose doc REQUIRES the wire (`Services/Haptics/Core/HapticContracts.cs:105-137`), with
the hold explicitly *"provider's choice, documented in the provider"* (`:70-73`). The port did not
copy that file — the constitution forbids importing from either evidence tree — but arriving
independently at the same three verbs from the same two implementations is the strongest available
check that the seam is not shaped around one of them.

### 2.4 What the seam deliberately does NOT carry

- **No actuator KIND.** Upstream's v2 contract has eleven (`HapticContracts.cs:16-29`); both CITED
  providers drive exactly one — `ButtplugProvider` filters `OutputType.Vibrate` (`:62-66`) and
  `LovenseProvider` sends `Vibrate:{i}` (`:233`, `:244`). Admitting ten kinds this port has never
  read against a device would be shaping a seam around code nobody here has exercised.
- **No mixer, no routing matrix, no temperament, no DSP.** Those are 9193 lines of upstream service
  and they configure a connection this build cannot make.

---

## 3. THE REFUSAL, AND WHY IT IS NOT "NO DEVICE FOUND"

`HapticServerObservation.Classify()` is a five-arm ladder and **the arm ORDER is the whole design**:

```
!ClientAdmitted   -> Unavailable(haptic-no-admitted-provider)   <-- FIRST, always
!Asked            -> Unavailable(not-probed)
!ServerAnswered   -> Unavailable(haptic-server-unreachable)
DeviceCount == 0  -> DependencyMissing("a haptic device …", haptic-no-device)
else              -> Available
```

Because admission is asked first, **no combination of the other three fields can produce an answer
about a server this build cannot reach**, and no run of this build can reach the missing-device arm.
`ADMISSIONIsAskedFIRST_SoNoOtherFieldCanProduceAnAnswerAboutAServerWeCannotReach` pins exactly that
by handing the classifier an observation where everything else is perfect.

The vocabulary for a missing device EXISTS (`HapticReasonCodes.HapticNoDevice`, carrying upstream's
own two sentences verbatim) and is deliberately not what this build produces. A refusal saying "no
device found" would be false, and it would send a user to plug in a toy when the real problem is four
File-Scope violations away from them.

**`Confirmed` and `Available` are shown to agree rather than argued to.** All sixteen combinations of
the four booleans are enumerated in one fact; exactly one is `Available`.

**The device gate is written and is marked DOWNSTREAM.** `HapticSinkFactory.DeviceManualGate` carries
four steps and its first line says it *cannot be attempted until a provider client is admitted*, so
quoting it today cannot send anybody to fix the wrong thing.

---

## 4. THE PREMIUM GATE — consuming `Entitlement/**` without widening it

**`Entitlement/**` is byte-identical to base** (`git diff --stat` over the folder is empty).
`Haptics/HapticGate.cs` is a pure function `EntitlementOutcome -> HapticGateDecision` built on
`EntitlementOutcome.Match`, which does not compile with a branch missing.

**The bar is TIER 1, and that is the difference from the DTRH door.** `MainWindow.Haptics.cs:487`
reads `HasPremiumAccess`, which is `CurrentTier >= PatreonTier.Level1`
(`Services/Account/PatreonService.cs:134`) = `EntitlementTier.Supporter`; `DtrhGate.RequiredTier` is
`Lab`. Same folder, same three answers, a different bar — which is what "consume it" looks like. A
fact asserts the two gates disagree on the bar, so a later tidy cannot merge them.

**"I could not tell" never renders as "you are not a patron."** All ten `EntitlementReasonCodes`
produce `RefusedUnverified` with their own code, their own authored sentence, and a header and footer
saying nothing was decided about the user — and a mechanical fact holds that **no** unknown-answer
message may contain `DeniedMessage`, so a reason code added later cannot inherit the refusal wording.
This is the third door in this port that refuses every user pending the owner's bearer decision; it
adds no producer of `NotEntitled` and widens nothing.

**The gate bites in TWO places, because upstream's does.**

1. **The enable toggle.** The statement ORDER is upstream's and is load-bearing:
   `MainWindow.Haptics.cs` tests at `:487`, reverts the box at `:489`, tells the user at `:490-495`
   and **returns** at `:496`, so `HapticCfg.Enabled = isEnabled` at `:499` is reached only when the
   gate allowed. A refused tick therefore writes **nothing** — asserted both at the participant and
   through the REAL checkbox in the headless suite. Switching OFF is never gated (upstream's
   condition is `isEnabled && …`), so a lapsed pledge cannot trap somebody with a running toy.
2. **The output path.** `HapticMixer.cs:253-262` drops everything and stops the toys ONCE on the
   open→closed transition. Ported: applying a non-allowing decision after an allowing one owes an
   all-stop, exactly once, and applying the same closed decision again stops nothing further.

### 4.1 A DISCREPANCY IN THE SHIPPING SOURCE, recorded rather than smoothed over

Upstream's three haptic gates do not use the same predicate:

| site | predicate |
|---|---|
| the enable checkbox (`MainWindow.Haptics.cs:487`) | `App.Patreon?.HasPremiumAccess != true` — **one term** |
| the mixer (`HapticMixer.cs:200-201`) | `HasPremiumAccess ?? false \|\| DailyFree?.IsFreeToday("haptics") == true` — **two** |
| the premium rail's lockband (`MainWindow/MainWindow.PremiumRail.cs:573`) | `TierGate.RequiresPremium(…, "haptics")` — **two** (`Services/TierGate.cs:67-73`) |

`"haptics"` is in `DailyFreeService.OverridableKeys` (`:49`) and was **cut from the rotation `Pool`**
(`:40`, owner 2026-08-11), so the disagreement is reachable only on a day the SERVER names haptics —
on which the rail unlocks and the mixer opens **while the enable checkbox still refuses**. The port
implements term 1 only, the same way `DtrhGate` implements one of `RequiresLab`'s two terms (D24),
and both the source discrepancy and the port divergence are in the ledger (D197). Recorded so the
next reader does not "fix" one gate to match another without knowing they were different.

### 4.2 The divergence that is a real loss, stated rather than buried

Upstream evaluates its gate **once per 10 Hz tick**. The port resolves the entitlement **once, at
phase 3**, because a DPAPI read plus an authority call is not a 10 Hz operation. **A pledge that
lapses mid-run is noticed at the next launch, not within 100 ms.** D198.

---

## 5. D179 IS NOT CLOSED, AND THE DOT SAYS SO

**The thirteen ported effect modules are silent to this sink.** Upstream drives it from eight sites in
three of them — `Services/Flash/FlashService.cs:1453`, `:1480`, `:1516`, `:1915`;
`Services/Video/VideoService.cs:2580`, `:4585`, `:6580`; `Services/SubliminalService.cs:230` — and
`Effects/**` was closed to this packet. Giving those modules a haptic limb is a later packet.

**So the dot has only TWO reachable values, and the third one's absence is the finding:**

```
Off    = the enable is off, OR nothing in this build can reach a device
Armed  = the enable is on AND a device is really reachable
Live   = UNREACHABLE — it would have to mean something is being SENT, and nothing is
```

The second conjunct asks the SINK, so the dot is earned rather than read off the checkbox (SP-118's
D180 rule), and it is SP-109's fifth dot meaning — reach — over a resource in another process. A fact
drives a recording sink with a device and asserts the ceiling is `Armed` with zero output calls; the
headless suite asserts the real `Ellipse` carries neither class on every run of the shipping build.

**`Effects/**` is byte-identical to base**, along with all six existing capability folders,
`Entitlement/**`, `Scheduling/**`, `Persistence/**` and the csproj (`git diff --stat` over each is
empty). **A landed capability is not a working feature**, and the panel says so in words a user reads:
*"even with a device attached, nothing would move: no effect in this build sends anything to haptics
yet."*

---

## 6. THE DEPENDENCY DECISION'S EXACT COST — the line this packet stops at

**The csproj was not opened.** Nothing below is a request; it is the priced menu, and the price is
different for the two halves, which is the part SP-117's one-line summary could not carry.

| option | csproj cost | code cost | reach | what it costs |
|---|---|---|---|---|
| **A. Admit Buttplug** | **ONE line** — `<PackageReference Include="Buttplug" Version="5.0.1" />` into `CcpClient.Desktop.csproj:24-42` | **ONE file** — `Haptics/ButtplugHapticSink.cs` over the §2.3 seam | every Intiface-supported device, on Windows and Linux alike | a third-party package on the port's supply chain. **Unverified here:** its net10.0 TFM compatibility and its licence. The shipping tree's own note says BSD-3 and "pure .NET, one package containing Core + Client + WebSocket connector" (`ButtplugProviderV2.cs:17-19`) — that is READ, not verified against the nupkg on this machine, and the owner should treat it as a claim to check rather than a fact |
| **B. Admit Lovense** | **ZERO** — `System.Net.Http` + `System.Text.Json` are BCL (`LovenseProvider.cs:1-8`) | **ONE file** plus a URL/mode setting surface and the keep-alive of D2 | Lovense hardware only | a per-vendor client; the LAN one-second `timeSec` floor is a permanent behaviour limit (D3) |
| **C. Buttplug WITHOUT the package** | zero | **a REDESIGN, not a file**: a hand-written Buttplug **message spec v4** client over `System.Net.WebSockets.ClientWebSocket` — handshake, `OutputCmd`, the per-feature capability model, device add/remove, spec-version tracking | as A | the port would own a wire protocol somebody else versions. `ButtplugProviderV2.cs:13-27` is the shipping tree's own account of how much that spec moved between v3 and v4 |
| **D. Admit nothing** | zero | zero | none | haptics stays unported and the thirteen modules stay silent to it. **This packet lands D** |

**The one sentence the owner is owed:** *the Buttplug half costs exactly one `PackageReference` line
and one file; the Lovense half costs no package at all and one file plus a keep-alive; and refusing
the package does not block haptics — it blocks Buttplug specifically.*

---

## 7. THE APP-SCOPE OWNERSHIP POINT

`Haptics/HapticParticipant.cs` implements the existing `IBackgroundParticipant` and is registered
**last** in `CompositionRoot.DefaultParticipants`. **No second lifetime model was invented.**

| property | mechanism | WPF's counterpart |
|---|---|---|
| constructed once, starting nothing | participant constructors are cheap (SP-003 §4.4) | `App.xaml.cs:2060` |
| the connect attempt is a phase-3 act | `StartAsync` | `App.xaml.cs:2103-2105` |
| **the all-stop reaches the toys BEFORE anything is torn down** | the **reserved pre-drain HEAD slot** in `CompositionRoot.Build`, ahead of every flush — it completes before generations are cancelled and before any participant stops | `App.xaml.cs:4401-4407`: *"Haptics FIRST and synchronously (bounded ~2s) … This cannot be left to Haptics.Dispose() further down"* |
| the sink is disposed after that | `StopAsync`, reverse registration order | `App.xaml.cs:4524` |
| the one setting reaches disk | the same pre-drain slot | `App.Settings.Save()` on the gate path |
| the all-stop cannot be spent twice | a one-shot latch | upstream's own latch and its own reason (`HapticMixer.cs:172-174`, `:1122`) |

**The ordering is a FACT, not a comment**, and the fact is built so that only the head slot can make
it pass: the participants are registered **haptics first**, so reverse-order stop would run the
participant's own `StopAsync` LAST, and the recording participant's stop sequence is asserted to be
greater than the all-stop's. Deleting the head-slot call reds it (M-aw).

**Phase 3 connects to NOTHING, and that is a predicate rather than a constant.** With no route
admitted the participant never asks — `ConnectAttempts` stays 0 and the sink records zero calls —
because a product that opened a WebSocket to `ws://127.0.0.1:12345` with nothing able to speak the
protocol would be making a connection no user could benefit from. Upstream guards its own auto-connect
the same way and says why (`App.xaml.cs:2098-2105`, predicate at `:3487-3495`). The other half of the
predicate is executed too: with an admitted route the participant does connect and does record what it
found.

**`AutoConnect` is ABSENT rather than present-and-inert** (D93's rule): upstream's predicate is
`AutoConnect && HasRealHapticProviderEnabled()` and the second conjunct is false here for a reason no
setting can change, so a checkbox for the first would decide nothing.

---

## 8. PROVING IT BITES — 61 mutations, two rounds, **60 caught and ONE survivor**

Every conjunct, arm, clamp, constant, wording and wiring line this packet added was mutated one at a
time by `spine-tasks/SP-119-haptic-seam/sweep.mjs`, which lives inside this packet's folder and
writes only inside it (SP-112's rule). The driver normalises for MATCHING and writes each mutant back
in the file's OWN line endings — the tree is CRLF and the needles are LF, which is what silently
skipped 27 of SP-112's hardest cases — restores each file byte-identically, gates on a real compile
before running anything, and asserts `git status --porcelain client/src` is empty at the end. The raw
logs are beside this record and every count below is taken from them.

**The books:** 61 distinct mutations; 57 (round 1) + 3 (round 2) = **60 caught**; **1 survives**;
0 not patched; 1 NOT COMPILED in round 1, which was a fault in the DRIVER and is accounted for below
rather than counted as evidence about the code.

### Round 1 — 57 caught, 3 survived, 1 not compiled

### Round 2 — the three survivors plus the driver correction: 3 caught, 1 survived

- **M-av — `SinkState` classifying a hard-coded `NotAsked` instead of the real observation.** A real
  hole. Every fact about that expression drove a build with nothing to observe, so a capability line
  that could never change would have read as one that reports. Closed by
  `SINKSTATEReportsWhatTheSERVERSaid_NotAFixedSentence`, which drives a recording sink with three
  devices and asserts the detail names them.
- **M-ax — deleting the haptic flush from the reserved pre-drain slot.** A real hole, and the same
  one `SchedulerModuleTests` closes for its own settings: every other fact about the document either
  saved it explicitly or never restarted, so a switch a user flipped on the way out could have been
  silently lost. Closed by `ANUNSAVEDSettingStillReachesDiskThroughTheReservedPreDrainSlot` — dirty,
  never saved, real `ShutdownAsync`, read the file back.
- **M-ba — NOT COMPILED, and that verdict was MINE rather than the code's.** The replacement was not
  type-preserving, so no test was ever asked about it. Re-stated as a swap for a participant whose
  constructor really does type-check at that call site, and it is **CAUGHT** in round 2. A driver that
  can manufacture a NOT COMPILED can also manufacture a false clean, which is why this is in the
  record rather than in a comment.

### The ONE survivor, dispositioned **UNCOVERED** — never "equivalent"

**M-as — `ApplyEntitlementAsync` carrying `ex.Message` instead of `ex.GetType().Name`.**

The mutation is a real hazard and the suite does not discriminate it. The reason is exact and was
established by enumerating the consumers with `grep` **before** the disposition, which is the standing
rule: **the value it corrupts has no reader in this build.** It is written into an
`EntitlementReason.Detail`, and the two things that consume that outcome both read the CODE only —
`HapticGate.Decide` (`reason.Code`, and `Explain` is a switch over codes with authored sentences) and
`EntitlementOutcome.Describe()` (`"unavailable(" + reason.Code + ")"`, `EntitlementOutcome.cs:146-149`).
`grep -n "\.Detail" client/src/CcpClient.Desktop` returns no site that renders a haptic entitlement
detail to a log, a panel or a capability state.

**So it is not an equivalent mutant** — an authority whose exception message carried a bearer really
would put it in a string — **and it is not a hole a fact can close today without inventing a reader.**
Adding a property so one test could read it is the unexecuted-shape failure this packet already
removed once (the sink seam, §9). It is recorded as an obligation on the packet that first renders an
entitlement detail: **that packet must not render this one.**

The fact that DOES exist (`ANAUTHORITYThatTHROWSIsUNKNOWN_AndOnlyItsTYPENAMEIsCarried`) pins what is
observable — the gate's message and the log carry no part of the exception — and it says in its own
body what it does not cover.

### The false-clean channels, named

`runSuite` decides CAUGHT from a **non-zero exit code**, and `dotnet test` exits non-zero for reasons
that are not a failing assertion. `compiles()` closes the largest — a mutant that does not build is
reported as its own `NOT COMPILED` outcome, and round 1 produced exactly one, which is why M-ba is
discussed above rather than counted as a catch. **The remaining channels — an empty `--filter`, a
crashed host, the 15-minute timeout — are UNCLOSED**, and are bounded only empirically: every line in
both logs shows a non-zero passing count from the same filters (349, then 351, on the unit side; 63 on
the headless side), so no filter in this sweep matched zero tests.

### One thing the sweep caught that was not a mutation

Round 1's final line read `tree restored byte-identically: NO`. **The driver was innocent and the
commit was not.** A `docs(SP-119)` commit ran `git add -A` while the sweep had M-h applied and
captured that mutant — `DeviceCount == 0` became `DeviceCount < 0`, which would have made an empty
haptic server report `Available`. It is fixed in its own commit with the reason at the top, rather
than amended away, because the checkpoint that caught it is the one worth keeping visible.

---

## 9. FILES CHANGED

**Product — new (`Haptics/**`, the whole folder):** `HapticReasonCodes.cs` (six codes),
`IHapticSink.cs` (the seam, `HapticLevel`, `HapticOutput`, `HapticServerObservation` and its
classification), `UnadmittedHapticSink.cs`, `HapticSinkFactory.cs` (the admitted-route list, the gap,
the two priced routes, the device gate), `HapticGate.cs`, `HapticSettingsDocument.cs`,
`HapticParticipant.cs`. Plus `Views/Pages/HapticsPanelNotices.cs`.

**Product — changed:** `Lifecycle/CompositionRoot.cs` (the tenth participant, the entitlement moved
ahead of the participants factory so the participant can be handed it, the capability registration,
the pre-drain head slot and the flush), `Views/MainWindow.axaml.cs` (the owner reached off the host),
`Views/Pages/StudioPage.axaml` + `.axaml.cs` (the row in WPF's IMMERSION position, the dot, the
right-click, the one-control panel, the four notice lines).

**All six existing capability folders, `Effects/**`, `Entitlement/**`, `Scheduling/**`,
`Persistence/**`, `Tray/**` and the csproj are byte-identical to base.**

**Tests — new:** `HapticCapabilityTests.cs` (**22**), `HapticGateTests.cs` (**13**),
`HapticParticipantTests.cs` (**21**, the last two being the sweep's M-av and M-ax closers) — 56 unit;
`HapticsRowHeadlessTests.cs` (**8**) headless.
**Tests — changed at zero count:** `CompositionRootValidationTests.cs` and `IntegrationProofTests.cs`
(9 → 10 participants, plus new assertions that the tenth connects to nothing and holds the root's own
entitlement authority), `CapabilityTests.cs` (the registered-name list gains `haptic-sink`, plus the
state it reports), `SchedulerModuleTests.cs` (two `[^1]` index reads become `[^2]`, with the property
they are about — order relative to the session — asserted unchanged),
`StudioRackHeadlessTests.cs` (the row list gains `RowHaptics` in WPF's position, and the order fact
gains two assertions: this row HAS a dot and has NO effect behind it).

22 + 13 + 21 = **56** unit, **8** headless — the declared delta.

**Docs:** `client/docs/wpf-surface-reachability.md` (§SP-119, **D191-D202**),
`client/docs/verification-harness.md` (the haptic evidence class).
**Sweep artefacts, inside this packet's folder:** `sweep.mjs`, `sweep-round*.log`.

---

## 10. WHAT THIS WORK DOES NOT PROVE

**FIRST, and it is the largest claim in the packet: nothing here proves anything ever moved.** There
is no device, no server and no client. Every measurement stops at a typed refusal, at a control in a
visual tree, or at a document. `felt-verified` is not merely undischarged — **it is not dischargeable
by code on any platform at any depth of API**, because a haptic server reports what it believes it
commanded over Bluetooth and neither this process nor upstream's can tell a toy that vibrated from one
with a flat battery in the next room.

- **The STOP half of that is the half that matters.** Upstream's own reason is that an
  uncountermanded level outlives the process (`App.xaml.cs:4401-4404`). The port's ordering is
  asserted; whether a real device really stopped is not, and cannot be here.
- **No headed capture was taken.** `presentation-verified` is untouched. The headless facts drive real
  input on real controls in a real visual tree; that is `draw-verified` and no more.
- **Nothing was measured about the two wire protocols.** Neither `ObserveAsync` nor
  `SetOutputsAsync` has ever been implemented against a server here, so §2.3's "what each provider
  would need" is an analysis of upstream's source, not a build that was tried.
- **The throttles and quantizations are described, not modelled.** Lovense drops commands inside
  200 ms in continuous mode (`:207-219`) and floors LAN durations at a second (`:232-233`); the seam
  deliberately does not model either, so a future sink must honour them itself.
- **The entitlement is resolved once.** A pledge that lapses mid-run is unnoticed until relaunch
  (D198). The transition machinery is proved by driving decisions directly, not by a real authority
  changing its mind.
- **Linux is unproven, and for once it is not the interesting axis.** This capability refuses
  identically on both platforms. Nothing here says a Linux build would behave differently once a
  client is admitted — that is precisely what a Linux run of the device gate would have to establish.
- **Concurrency is single-threaded.** The gate transition and the all-stop are exercised in sequence
  on one thread. A gate closing on one thread while a teardown runs on another is not covered; the
  one-shot latch is `Interlocked` and that is reasoning, not a stress result.
- **The dot has never been seen by a person.** It is asserted as style-resolved classes on an
  `Ellipse` in a headless tree.
