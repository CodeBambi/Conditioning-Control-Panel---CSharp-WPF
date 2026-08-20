# SP-117 — record

Branch `lane/SP-117-last-three`, base `2a991d61`.
Floor: pin **2067 unit / 121 headless**; observed **2123 unit / 125 headless**, zero failures;
declared **+56 unit / +4 headless** (`floor-delta.json`). 2067 + 56 = 2123 and 121 + 4 = 125,
confirmed by `node client/tests/floor/sum-deltas.mjs --check --packets SP-117-last-three`. The floor
run therefore REPORTS a violation against the pin, which is the expected shape: the orchestrator sums
the deltas and applies one bump. Two skips, both pre-existing
(`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); none added, none widened.
Build: 0 errors, 0 warnings. `client/tests/floor/floor.json` was never opened.

> **The census is this packet's primary output and it was committed first**, at
> `spine-tasks/SP-117-last-three/plan.md`, commit `2c6da52c`, before a single product edit. SP-116
> committed its protocol before its first measurement and that ordering is now the standard. §1
> below is the summary; the inventory itself is in `plan.md` and is not restated here.

---

## 1. THE THREE-WAY CENSUS, and the one correction that had to come first

**Before any claim rested on it:** there is no `ConditioningControlPanel/Models/AppSettings.cs`. The
only `AppSettings.cs` in the repository is `ConditioningControlPanel/CCP.Core/Models/AppSettings.cs`
(6969 lines), which the shipping WPF app reaches by **project reference**
(`ConditioningControlPanel.csproj:52`) while excluding the directory from its own globs (`:10`). So
the settings model is simultaneously inside the `CCP.*` tree on disk and shipping product code by
reference. That is the trap SP-113 hit as "the wrong path", and every `AppSettings.cs` line in this
packet is verified against that file.

| | **Visuals** | **Haptics** | **Scheduler** |
|---|---|---|---|
| the shipping service | **NONE.** `grep -rn "VisualsService"` over the shipping tree returns nothing | `Services/Haptics/**` — **9193 lines over 21 files**, plus `MainWindow/MainWindow.Haptics.cs` (1091) and `Views/Tabs/HapticsTabView.xaml` (1640) | four methods on `MainWindow` (`MainWindow.StartStop.cs:562-696`). `CCP.Core/Services/Scheduler/SchedulerService.cs` exists and the shipping app does not use it |
| its real surface | a settings page: five load/save pairs over `App.Settings.Current` (`Features/VisualsFeatureControl.xaml.cs:35-118`). No timer, no window, no thread | `IHapticProvider` (`Services/Haptics/IHapticProvider.cs:8-29`) plus a mixer, a device manager, patterns and a temperament | a 30 s `DispatcherTimer` (`MainWindow.xaml.cs:206`, `:616-620`) started after a 60 s grace (`:623-635`), stopped at `MainWindow.WindowChrome.cs:166` |
| lifetime | none — it owns nothing | **APP-scoped**: `App.xaml.cs:533`, `:2060`, auto-connect `:2103-2105`, `:4406`, `:4524`. **Never engine-started**: `grep App.Haptics MainWindow/MainWindow.StartStop.cs` → 0 hits | **APP-scoped and runs when nothing is running** |
| what it does | writes `ImageScale`, `FlashOpacity`, `FadeDuration`, `FlashDuration`, `FlashAudioEnabled` | reacts to other modules — **99 call sites across 29 shipping files** | calls **`StartEngine()` / `StopEngine()`** plus `_trayIcon.MinimizeToTray()` and a notification (`:601-637`) |
| every capability it needs | the OVERLAY, and only through the Flash Images module that already consumes it | a loopback **WebSocket** client (`ws://127.0.0.1:12345`, `ButtplugProvider.cs:27`, `:83`) or **HTTP** client (`http://127.0.0.1:20010`, `LovenseProvider.cs:21`, `:244`), plus a third-party server process and a physical toy | local wall-clock day-of-week and time-of-day (`:642-696`), session start/stop from outside a session, tray minimize, tray notification |
| which of the six covers it | **overlay — already landed and already consumed by this path** | **NONE. Not one.** All six are display and peripheral capabilities on this machine's own screen and keyboard | **NONE, and it needs none of them** |
| precisely what is missing | **only the values.** `FlashGeometry.Size(…, scalePercent)` (`Effects/FlashGeometry.cs:46`), `OverlaySurfaceRequest(…, opacity, …)` (`Overlay/OverlaySurfaceRequest.cs:47`) and `OverlaySurfaceSet.Place(…, lifetime)` were **already parameters**; `FlashSurfacePresenter.cs:244/264/265` was handing them constants (`:75`, `:81`, `:84`) | a seventh capability folder, a NuGet dependency the csproj does not carry (`CcpClient.Desktop.csproj:24-42`), an app-scope wiring point in `Lifecycle/**`, and a premium gate | an app-lifetime owner in `Lifecycle/**`, and a LOCAL-time seam `ISessionClock` does not have (`Session/SessionClock.cs:17-25` is `UtcNow` + `Schedule`) |
| verdict | **SHIPS** | **REFUSED** | **REFUSED, and the refusal is the finding** |

### 1.1 Scheduler — SP-108's belief held, and is now evidence

`SchedulerTimer_Tick` enters the window while `!_isRunning` and calls **`StartEngine()`**
(`:612-618`); leaves it while `_isRunning && _schedulerAutoStarted` and calls **`StopEngine()`**
(`:625-631`). A session module runs *under* the engine; this one drives it. **It does not belong to
the effect spine, so "the effect spine is complete" and "the Scheduler row is unported" are both
true at once**, and its correct home is a board row against `Lifecycle/**`. The port's own rack
comment already said this (`Views/Pages/StudioPage.axaml:283-284`) and can now cite evidence for it.

Its predicate is exact and is recorded for whoever takes it: seven per-day booleans on
`DateTime.Now.DayOfWeek` (`:648-659`); `TimeSpan.TryParse` of `SchedulerStartTime`/`SchedulerEndTime`
with fallbacks **16:00** and **22:00** (`:663-675`); and `endTime < startTime ? (now >= start || now
< end) : (now >= start && now < end)` (`:679-690`) — an overnight wrap, half-open at the end.

**The clock gap is real and was deliberately not taken.** `ISessionClock` could grow a local-time
seam inside this packet's File Scope, but it is consumed by all twelve landed modules and both
presenters, so widening it is an equivalence claim over a symbol with more consumers than this
packet can discharge by name — and it would not unblock the row anyway, because the app-lifetime
owner is still out of scope.

### 1.2 Haptics — a named limit, measured, and it is NOT the blocker

`netstat -ano -p tcp` on this machine at 2026-08-20: **no listener on 12345, 20010 or 30010**; the
only loopback listeners are 11434, 15292 and 51703. No toy attached. **That is a named limit, never
a blocker and never a skip** — it does not stop a port being written, it stops any run of it earning
`Available`, and the honest artefact would be a typed refusal plus a `WIP`/`BLOCKED` board row naming
"connect an Intiface Central or Lovense Connect server and one device" as the manual gate.

**The blocker is the four File Scope violations**, and the device is a footnote to them. Neither
provider is a driver: both are clients of a separate server the user installs.

---

## 2. WHAT SHIPPED, and why the census is what made it writable

**Visuals is the Flash Images module's DRAW dials on their own page** — which is exactly what it is
upstream. Three live dials (size, opacity, duration), two declared absences, no enable, no dot.

**The port had written this debt down twice and neither note had been actioned:**

- `Persistence/SessionPresetDocument.cs:17-23` — "every one of them describes how a flash is DRAWN.
  The port draws no flash …, so persisting them would write settings nothing reads … **They arrive
  with the surface that honours them.**" **The surface arrived at SP-100.** That sentence is also now
  stale on its face and is corrected in the ledger rather than in `Persistence/**`, which is out of
  scope.
- `Session/SessionParticipant.cs` — "flash opacity, master volume and subliminal volume have **no
  dial on any ported panel**, so they are absent rather than present-and-inert (D93)." This row is
  that panel, so **D93 partially closes on its own stated condition** rather than by waiver.

### 2.1 The two dials that must be ABSENT, with the enumeration that proves it

This is the half of the census that matters most, because both look like ordinary port debt and
neither is.

**Fade is a DEAD DIAL in the shipping product.** Every reference in the whole repository was
enumerated and classified: `Features/VisualsFeatureControl.xaml.cs:46,47` (load), `:59` (change
filter), `:96` (write); `CCP.Core/Models/AppSettings.cs:892-897` (field + clamp);
`CCP.Core/Models/Preset.cs:68,178,202,226,253,292` (defaults), `:338` (preset→settings), `:456`
(settings→preset); and the abandoned port's copy of the same panel. **Zero readers that change
anything on screen.** The fade a user actually sees is `private const double FADE_PER_SEC = 2.4`
(`Services/Flash/FlashService.cs:2018`), spent at `:2073` and applied at `:2110-2117`. Two
corroborating details found in the same pass: it is the only slider on that page **not** marked
`SessionLock.Owned` (`:87` against `:55`, `:71`, `:103`), and `SessionSettings` carries
`FlashOpacity`, `FlashScale` and `FlashAudioEnabled` (`CCP.Core/Models/Session.cs:876,878,881`) and
no fade at all. **Porting it would have shipped a slider that moves nothing** — §9 D7's greyed
control in its purest form. D172.

**Link-to-audio cannot ship because the port's flash is silent.** Its whole meaning is
`FlashService.cs:1037-1042`: when a flash has a sound, play it and let **the sound's length replace
the duration**. `FlashImagesEffect.Deliver` hands paths to a surface and raises an event; nothing in
the module reaches `Audio/**`. Upstream's own comment calls `FlashDuration` the duration "when audio
is disabled" (`AppSettings.cs:925`), so the port's always-on duration path IS upstream's
audio-link-off path. D173.

**Both absences are on the PAGE, not only in this record** (`VisualsAbsenceState`), on the precedent
SP-111, SP-113 and SP-115 set for a half-ported row.

### 2.2 A citation in the SHIPPING source that does not verify

`StudioTabView.xaml.cs:494-495` justifies the missing dot with "the dashboard card is deliberately
neutral too (`MainWindow.Presets.cs:800`)". Checked: `MainWindow/MainWindow.Presets.cs:800` is inside
`ShowMediaDropChoiceDialog`; the only `Visuals` in that file is `:41`, a comment about help buttons;
and `grep -rn "visuals" ConditioningControlPanel/MainWindow/*.cs` finds no dashboard card at all.
**The behavioural fact the port needs does not depend on it** — the dot-predicate argument at `:496`
is literally `null`, read directly — but the stale citation is recorded so the next reader does not
chase it. D171.

---

## 3. THE DOT — there is none, and that is the ported decision

The packet asked for "a rack row and a truthful dot". **The truthful outcome here is no dot**, and
it is upstream's own, in upstream's own words: "A dot that cannot be wired honestly is omitted"
(`StudioTabView.xaml.cs:494-495`), with `null` passed where every other row passes a predicate
(`:496`).

The reasoning is SP-112 §4's, applied: **the eight landed meanings are properties of CAPABILITIES,
not of rows.** Visuals consumes no capability of its own. The only dot it could carry would be the
Flash Images row's dot, repeated two inches to its left — an eighth copy of an existing fact, not a
new one. And it has no enable, so there is no `Off` for it to be in.

`RowVisuals` is therefore the only rack row whose `Grid` has one child, `VisualsModulePanel` is the
only panel with no enable checkbox, and both absences are asserted rather than left implicit:
`TheRackIsInWpfsOrder…` now also asserts `RowVisuals` carries **no `Ellipse` at all**, and
`TheVisualsRowOpensAPanelWithNOENABLEBOX…` asserts the panel contains **no `CheckBox` at all**. The
row also takes **no right-click quick toggle**, which is upstream's own unhandled case (`:659`)
rather than an omission: the gesture flips an enable and this row has none.

---

## 4. THE THREE LANDED SURFACES, proven unharmed rather than assumed

**All six capability folders are byte-identical to base.** `git diff --stat` over
`client/src/CcpClient.Desktop/{Overlay,Input,Audio,Video,Pointer,Glyph}` is empty. The overlay was
**consumed**, through the seam it already exposed: `OverlaySurfaceRequest`'s opacity parameter and
`OverlaySurfaceSet.Place`'s lifetime parameter were already there, and `FlashGeometry.Size` has taken
a `scalePercent` since SP-100.

**Nothing new goes on screen and nothing new takes focus.** This packet adds no window, no surface,
no presence and no thread. It changes three numbers inside a request the flash presenter was already
making. That is why there is no coexistence run here and why one would prove nothing: there is no
second rectangle to be disjoint from.

**Twelve landed modules' facts pass unchanged in SEMANTICS.** Three headless facts moved because
their PRECONDITION moved, and each is a strengthening:

| fact | what changed | why it is not a weakening |
|---|---|---|
| `TheRackIsInWpfsOrder_AndEveryRowWithAPortedEffectBehindItCarriesADot` | the row list gains `RowVisuals` in WPF's sixth position | plus a NEW assertion that the row carries no dot. The method name always said "every row **with a ported effect behind it**"; Visuals is the first row with none |
| `TheRackNowHasTWOGROUPS_AndTheSecondOneHoldsAModuleThatDrawsNothing` | the same row list | the group list, the group order and the ramp's dot are untouched |
| `TheRampsOwnLinkSwitchesWriteItsDocument_AndOnlyTheTwoDialsThePortReallyHas` → `…AndOnlyTheThreeDialsThePortReallyHas` | the flash link switch is asserted PRESENT and writing, and the dial asserted to be in the built composition | the two audio links are still asserted ABSENT, by the same mechanism, in the same fact. **D93's rule was never relaxed — its precondition was discharged for exactly one of the three** |

No landed assertion was relaxed, reworded to be weaker, or deleted. The unit suite was **2067 at
base and 2067 after every product change**, before a single new test was written — recorded because
it is the equivalence evidence for the presenter mutation, not a formality.

---

## 5. THE DEFECT THIS PACKET FOUND IN LANDED CODE, and repaired

`_bouncingTextPreset` (SP-115) was created at `SessionParticipant.cs:167` and appears in **none** of
`StartAsync`, `LogIfDegraded`, `StopAsync` or `FlushAsync`. `PersistenceStore.Load` runs **only** from
`StartAsync` (`Persistence/PersistenceStore.cs:179-190`), so the document was never read from disk
and never written to it: **every dial a user set on the Bouncing Text panel was silently gone at the
next launch.**

It was found while adding a twelfth store to those same four lists. Fixed in the same commit, with
the reason at the line, and pinned by
`THEBOUNCINGTEXTDIALSSurviveARestartToo_AndBeforeSp117TheyDidNot` — which fails against base. Leaving
one store out of four lists while editing all four is exactly how the next one gets missed. D178.

---

## 6. PROVING IT BITES — 60 mutations, three rounds, **ZERO survivors**

Every conjunct, clamp, constant, predicate and wiring line this packet added or moved was mutated
one at a time by `spine-tasks/SP-117-last-three/sweep.mjs` — which lives inside this packet's folder
and writes only inside it (SP-112's rule, after a previous wave's driver wrote three levels above its
own root into the shared checkout). The raw logs are beside this record
(`sweep-round1.log`, `sweep-round2.log`, `sweep-round3.log`) and every count below is taken from
them. The driver normalises for MATCHING and writes each mutant back in the file's OWN line endings
— the tree is CRLF and the needles are LF, which is exactly what silently skipped 27 of SP-112's
hardest cases — restores each file byte-identically, and asserts `git status --porcelain client/src`
is empty at the end. **It was, on all three rounds.**

**The books:** 60 distinct mutations; 57 (round 1) + 2 (round 2) + 1 (round 3) = **60 caught**;
**0 survive**; 0 not patched, so no needle was silently skipped.

**Rounds 4 and 5 add no mutations to those books and must not be counted into them.** Both re-run
mutations round 1 already caught, as checks on the DRIVER rather than on the code: round 4 re-ran
M-x while its compile behaviour was being measured, and round 5 re-ran M-x and M-at through the
compile-gated driver. Each has its own durable log. A run whose evidence is deleted is
indistinguishable from a run that never happened — SP-112 was corrected on exactly that point — so
they are logged and excluded rather than logged and quietly added.

### Round 1 — 57 caught, 3 survived, 0 not patched

### Round 2 — the three survivors, each closed by a fact rather than argued away: 2 caught, 1 survived

- **M-ao — `anyLink` dropping the flash link** (`RampPanelNotices.cs`). A real hole. Every case that
  exercised that predicate linked the spiral or the tint, so a user who had linked **only** flash
  opacity would have been told "nothing is linked to it yet, so it would run for the whole session
  and change nothing" while their ramp was about to drive a real dial. Closed by
  `ARampLinkedToNothingBUTTHEFLASHOpacityIsStillLinked_WhichSp117sThirdLinkMadeReachable`.
- **M-bg — the panel reloading the size slider from a CONSTANT instead of the document**
  (`StudioPage.axaml.cs`). A real hole, and the sneakiest of the three: at boot the document holds
  the defaults, so the mutant is invisible until the second reload, at which point the user's
  sliders silently snap back to WPF's defaults. Closed inside
  `MovingAVisualsDial_WritesItsDocument_AndReachesTheVERYNEXTFLASHSPLACEMENT` by driving the
  product's OWN reload path (the rack's right-click → `LoadDialsFromPreset`) rather than a test hook.
- **M-at — `SessionParticipant` building the presenter WITHOUT the dials.** The most load-bearing
  hole in the packet: every other fact drives a presenter the test itself built, so dropping
  `draw: Visuals.Draw` left every dial correct in isolation and completely inert in the product.

### Round 3 — M-at, after correcting the DRIVER's own routing: 1 caught, 0 survived

**Round 2 reported M-at as a survivor for a reason that was mine, not the code's, and it is recorded
rather than quietly re-run.** The mutation was routed to the HEADLESS suite, and its closing fact —
`THECOMPOSITIONROOTReallyHandsTheDialsToTheProductPresenter` — has to live in the pure-logic project,
because it needs the **real** product presenter and the headless boot supplies a double. So the
driver ran a suite that could not see the fact and reported "survived" for a mutation that was
already caught. The route is corrected in the driver with the reason at the line, and round 3 is the
re-run with a durable log. A sweep whose own routing can manufacture a survivor is a sweep that can
also manufacture a false clean, which is why this is in the record rather than in a comment.

### The SECOND false-clean channel, named because the paragraph above obliges it

The paragraph above argues that a sweep whose routing can manufacture a survivor can also
manufacture a false clean. **There is a second channel of exactly that kind in this driver and an
earlier draft of this record left it unstated, which is that standard applied unevenly.**

`sweep.mjs`'s `runSuite` decides `CAUGHT` from a **non-zero exit code**, and `dotnet test` exits
non-zero for reasons that are not a failing assertion: **a mutant that does not COMPILE**, a
`--filter` that matches no test, a crashed test host, or the 15-minute timeout. Every one of those
registers as a catch, so the exit code alone cannot distinguish "a fact noticed" from "nothing was
ever measured".

**The bound that makes rounds 1-4 sound anyway, measured rather than argued:**

- All 60 mutations are **type-preserving** — each swaps a constant, a member, an operand or a whole
  statement for another that type-checks — so none can produce a compile error by construction.
- **No project under `client/` sets `TreatWarningsAsErrors` or `WarningsAsErrors`** (`grep` over
  every `*.csproj`, `*.props` and `*.targets` there returns nothing), so a warning cannot become an
  error.
- The one mutation that could plausibly still have failed to build is **M-x**, which removes the
  sole `Changed?.Invoke()` (`FlashDraw.cs:160`) and orphans the event declared at `:131`. It was
  applied by hand and built: **`CS0067` is a WARNING, `Build succeeded`, 0 errors.** Its `CAUGHT` is
  therefore a real assertion failure — `TheDialsWriteThroughToTheDocumentAndRaiseChanged`'s
  `Assert.Equal(3, changes)` — and not a build failure wearing a catch's clothes.
- The filter channel is bounded differently: every round's log shows a non-zero passing count from
  the same filters, so no filter in this sweep matched zero tests.

**The driver now closes the largest of those channels rather than only describing it.** `compiles()`
builds the product project before the suite runs and reports **`NOT COMPILED`** as its own outcome,
the way `NOT PATCHED` already is, and the round summary carries a fourth column for it. **It was
added after round 3 and therefore did NOT gate rounds 1-4** — that is why the bound above is stated
rather than skipped. Round 5 re-ran M-x and M-at through the compile-gated driver as a check on the
driver itself: both remain `CAUGHT`, `0 not compiled` (`sweep-round5.log`). **The remaining
channels — empty filter, crashed host, timeout — are UNCLOSED and are named here rather than left
for a reader to find.**

### The composition-root fact creates no window

Deliberately and by construction rather than by luck:
`FlashSurfacePresenter.Show` takes the reading FIRST, and `ShowOne` gives up at the undecodable
frame **before** `OverlaySurfaceSet.Acquire` is ever reached. It asserts `SurfacesShown == 0` so
that property is pinned rather than assumed.

### No equivalence claim is made anywhere in this sweep

`port-workflow.md`'s equivalence rule was adopted after five false claims in four waves, each the
same shape: a true proposition about one consumer of the mutated symbol, generalised to "no input
distinguishes". **This packet asserts no equivalent mutants at all** — there are none to assert,
because every mutation was caught. Where a survivor appeared, it was closed by a fact or (once) by
correcting the driver; none was dispositioned by argument.

The consumer enumeration was still done, before the mutation and not after, because three of the
mutated symbols are consumed by landed code: `FlashSurfacePresenter.ImageScalePercent`
(`FlashEndToEndObservations.cs:150,153`), `.SurfaceLifetime`
(`FlashSurfacePresenterTests.cs:59,61,76,78,129,151`), `.OpacityPercent` (the presenter itself
only — the other `OpacityPercent` tokens in the tree belong to `PinkFilterPresetDocument`,
`SpiralPresetDocument`, `BouncingTextPresentation` and `AiCommandEnvelope.FlashImage`, which are
different symbols), and the `FlashSurfacePresenter` constructor
(`SessionParticipant.cs:210`, `FlashEndToEndObservations.cs:160`,
`FlashSurfacePresenterTests.cs:418,467`). Every one keeps compiling and passing because the three
constants kept their values as DEFAULTS and the new parameter is optional — and the empirical form
of that claim is the one worth having: **the unit suite was 2067 at base and 2067 after every
product change, before a single new test was written.**

---

## 7. FILES CHANGED

**Product — new:** `Session/VisualsPresetDocument.cs` (the three dials with WPF's clamps, and no
`Enabled` member), `Effects/FlashDraw.cs` (the reading, its arithmetic, its validating boundary, and
`VisualsDials`), `Views/Pages/VisualsPanelNotices.cs` (the panel's four sentences).

**Product — changed:** `Effects/FlashSurfacePresenter.cs` (a `Func<FlashDraw>?` read once per
`Show`; the three constants stay as the DEFAULTS), `Effects/IntensityDial.cs` (`FlashOpacityDial`),
`Effects/IntensityRampEffect.cs` (`SetLinkFlashOpacity` + the `IsLinked` arm),
`Session/IntensityRampPresetDocument.cs` (`LinkFlashOpacity`), `Session/SessionParticipant.cs` (the
store, the dials owner, the presenter's supplier, the ramp's third dial, and §5's repair),
`Views/Pages/RampPanelNotices.cs` (`anyLink` gains the third link), `Views/Pages/StudioPage.axaml`
plus `.axaml.cs` (the dot-less row in WPF's sixth position, the enable-less panel, three sliders, the
ramp's third switch).

**All six capability folders are byte-identical to base.**

**Tests — new:** `VisualsModuleTests.cs` (**54** cases, the last being the sweep's M-at closer).
**Tests — changed:** `NonDrawingEffectSpineTests.cs` (**+1** — the ramp's third dial, held, driven,
clamped at the ceiling and restored), `StudioSurfaceNoticeTests.cs` (**+1** — the sweep's M-ao
closer), `StudioRackHeadlessTests.cs` (**+4** new cases, plus the M-bg closer folded into one of
them and three landed facts updated per §4, all at **0** count change).

54 + 1 + 1 = **56** unit, **4** headless — the declared delta.

**Sweep artefacts, inside this packet's folder:** `sweep.mjs`, `sweep-round1.log`,
`sweep-round2.log`, `sweep-round3.log`.

**Docs:** `client/docs/wpf-surface-reachability.md` (§SP-117, **D171-D178**).

---

## 8. WHAT THIS WORK DOES NOT PROVE

**FIRST, because it is this record's largest claim and the unjoined half of it reads as something
false.** §1.1 says Scheduler is not a session module and this record elsewhere says the effect spine
is complete. **The joined, scoped, qualified form is the only one that may be carried forward:**
thirteen of the rack's fifteen rows are ported; the two that remain are **not session-scoped effect
modules** — Scheduler calls `StartEngine()` / `StopEngine()` from a 30 s timer that runs when nothing
is running (`MainWindow/MainWindow.StartStop.cs:601-637`), and Haptics is app-scoped and never
engine-started (`App.xaml.cs:533`, `:2060`, `:2103-2105`; zero hits for `App.Haptics` in
`MainWindow/MainWindow.StartStop.cs`). **So the session effect SPINE is complete and the PRODUCT is
not at parity.**

**That qualification is load-bearing rather than pedantic. Haptics is a SINK the spine drives, and
none of the thirteen ported modules has a haptic limb.** The shipping product's ported-module code
calls the haptic service from **eight** sites in three of them: `FlashService.cs:1453`, `:1480`,
`:1516` (all three spawn arms) and `:1915` (the click); `VideoService.cs:2580`, `:4585`, `:6580`;
`SubliminalService.cs:230`. So the absence subtracts from thirteen modules, not from one unported
row. Recorded as **D179** in the ledger, which the ledger had carried only as one item inside three
other modules' reward lists (D68, D145, D161) — **and not for Flash Images or Mandatory Video at
all**, including Flash, the module this packet just extended. Unqualified, "the effect spine is
complete" reads as behavioural parity, which is exactly what `task-board.md:24` was filed to prevent.

- **Nothing here proves a human saw a flash at any size, opacity or duration.** No headed capture was
  taken; `presentation-verified` is untouched. Every measurement stops at the request this process
  handed an overlay double, or at the document behind it.
- **The composited half of the opacity dial is unmeasured.** The fact asserts the request's `Opacity`
  and the `Alpha` byte the overlay would hand the OS. Whether `LWA_ALPHA` 102 looks like 40 % to a
  person on a real display is a headed claim and is not made.
- **No real overlay window was created by any fact in this packet.** SP-100's and SP-112's real
  desktop runs are unchanged and were not re-run here; this packet's presenter facts drive recording
  doubles.
- **Nothing measures cadence or timing.** Every stagger and every lifetime is advanced by hand on the
  injected clock, so a flash that stayed up for twice its dialled duration in wall-clock terms would
  satisfy every check here.
- **The Visuals row was never opened by a human.** The headless facts drive real input on real
  controls in a real visual tree; that is `draw-verified` and no more.
- **Linux is unproven** and unchanged: the overlay factory still refuses in type there, so on Linux
  these three dials describe pictures nothing draws — which the panel's surface line says verbatim,
  from the capability's own answer.
- **Haptics and Scheduler are censused, not attempted.** Nothing in this packet is evidence about
  either beyond §1's inventory, and neither refusal was tested by trying.
- **Concurrency is single-threaded.** A dial moved on the UI thread while the ramp drives the same
  dial from the clock's pool thread is not covered; `PersistenceStore.Mutate` takes its own lock, but
  the interleaving is not exercised.
