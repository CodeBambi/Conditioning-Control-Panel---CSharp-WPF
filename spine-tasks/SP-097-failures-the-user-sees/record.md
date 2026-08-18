# SP-097 — record

Branch `lane/SP-097-failures-the-user-sees`, base `feat/crossplatform` @ `4e62c6fe`.
Review level 3. Plan checkpoint artifact: `spine-tasks/SP-097-failures-the-user-sees/plan.md`.

## 1. What was actually wrong (verified against the source, not the brief)

WPF wraps its two launch handlers whole and, on any throw, logs it and shows a blocking
`MessageBox` with `MessageBoxImage.Warning`:

* `MainWindow/MainWindow.Lab.cs:161-166` — `"Couldn't start Graded Intake:\n\n" + ex.Message`
* `MainWindow/MainWindow.Lab.cs:266-271` — `"Couldn't start Down the Rabbit Hole:\n\n" + ex.Message`
* `MainWindow/MainWindow.Lab.cs:333-338` — the identical catch on the Quick Start path

The port caught only around `ResolveAsync` (`Features/Dtrh/DtrhLaunch.cs`), and:

* `Views/Pages/PlayPage.axaml.cs:38-39` fired `_ = dtrh.FallInAsync();` — a throw past the gate
  became an **unobserved task exception**, surfacing only when the finalizer ran it into
  `TaskScheduler.UnobservedTaskException` (`Program.cs:313`). Logged at some later GC, invisible.
* `Views/Pages/IntakePage.axaml.cs:41` called `intake.Launch()` **synchronously**. **This
  contradicts the packet brief**, which said both pages "fire the launch as a discarded task".
  The intake's throw escaped the click handler into Avalonia's dispatcher — not an unobserved
  exception but an unhandled one. Same user-visible defect (nothing on screen), worse process
  outcome. **Resolution: trusted the source.** Both launchers now wrap their whole flow, so the
  fix covers both shapes; the discrepancy is recorded here and in §13's preamble.

## 2. The failure surface, and how it differs from a refusal

A second plate in the SP-094/SP-095 idiom, **not** a modal dialog, and deliberately not the
refusal band re-tinted. Reasoning and the rejected alternative are in `plan.md` and §13 D41.

Five independent differences, so no single later tidy-up collapses them:

| axis | refusal | failure |
|---|---|---|
| element | `GateBand` / `PassGate` | **`FaultBand`** (both pages) |
| classes | `lock-band`/`intake-gate` + `lock-plate` | **`fault-band` + `fault-plate`** |
| rim / headline colour | `#FFB47BFF` / `#FFD05CE8` | **`#FFF0A02E`** (amber — `MessageBoxImage.Warning`'s severity as colour) |
| scrim | `#A8120A1E` / `#CC0A0A14` | **`#C8140A02`** (still translucent: `PlayTabView.xaml:247-248`) |
| headline | LAB ONLY / COULD NOT VERIFY / This week's intake is done / COULD NOT DETERMINE YOUR PASS | **"Couldn't start Down the Rabbit Hole" / "Couldn't start Graded Intake"** — WPF's own words |

Plus a sixth, in prose: the body ends with *"This is a fault in the app, not a decision about your
account: nothing here was refused to you. The button still works, so you can try again."* The two
bands are **mutually exclusive** — raising either lowers the other, on the same press and across
presses — and the fault band is `IsHitTestVisible="False"` like the refusal band, so the retry
still arrives (WPF's dialog is dismissed and leaves a live card; that is the outcome ported).

## 3. The trap: nothing swallowed

* **Diagnostic** keeps type **and** message: `dtrh: launch from {entry} FAULTED ({Type}: {Message})`,
  `intake: launch FAULTED ({Type}: {Message})` — the port's own convention for a thrown subsystem
  (`Audio/SoundArbitration.cs:412`, `Companion/BarkPipeline.cs:493`), and WPF logs its exception too
  (`Lab.cs:163`, `:268`).
* **User** reads the type and the message — strictly WPF's disclosure plus the type.
* The exception object stays on the launcher as `LastFault`, so the detail survives even with no
  subscriber. `Faulted` carries the `Exception` itself, never a pre-rendered string.
* The **entitlement-resolution** catch deliberately keeps the old, narrower rule: type name only,
  and it stays a **refusal** (`Unavailable(tier-authority-fault)`), because the question asked was
  about the account and "could not be determined" is the honest answer there. §13 D42; pinned,
  including that the reader's secret-shaped message never reaches the log.
* A cancellation the caller asked for still propagates on both throw paths rather than being
  painted as a failure.
* `FallInAsync`/`QuickDropAsync` now return `Task<DtrhGateDecision?>`; **null = the flow faulted, no
  verdict was reached**. Same shape and same reason as `IntakeLaunch.Launch()`'s existing null. A
  fourth `DtrhGateDecision` case was deliberately NOT added — a fault is not a verdict about the
  account, and typing it as one is trap 2 at the type level.

## 4. The four never-executed paths, all four closed

| # | path | fact |
|---|---|---|
| 2 | `MainWindow.RequestApplicationExit`, no-lifetime branch | `ShellTrayHeadlessTests.TheMenusExitEntry_WithNoClassicLifetime_SaysSoAndShutsNothingDown` — invokes the REAL tray menu's `exit` item on a real `MainWindow`. **The lifetime precondition is asserted FIRST**, with a message saying why, so that if a headless host ever grows a classic lifetime the test fails instead of shutting the runner down. Asserts the diagnostic, that nothing shut down, and idempotence |
| 3 | `DtrhLaunch` `catch` -> `Unavailable(tier-authority-fault)` | `PlayPageHeadlessTests.WhenResolvingTheEntitlementTHROWS_ItStaysARefusal_AndLandsTheTierAuthorityFaultFallback` — a read seam that throws is the only input that makes the sealed `HostLoginEntitlement.ResolveAsync` throw. Asserts the reason code, the refusal band (not the fault plate), `LastFault` still null, and that the thrown message never reaches the log |
| 4 | `IntakePage`'s `RefusedSpent` / `RefusedNeedsAccount` arms | `AnAccountlessSeam_ReachesTheNeedsAccountArm_ThroughThePage` (injected `IIntakeEntitlementSource` reporting logged-out) and `ASpentWeek_ReachesTheSpentArm_ThroughThePage` (a free-account seam plus the real service's own `ConsumeForCompletedIntake` — WPF's sole spend site, `IntakePassService.cs:163-181`). Nothing stubs a decision: the real store, the real ISO week key and the real gate all participate |
| 5 | `DtrhLaunch.RestoreOwner` | `ShellTrayHeadlessTests.TheDtrhFlowENDING_RestoresTheShell_ThroughDtrhLaunchsOwnFunnel` — a real entitled FALL IN opens the REAL slot picker through the REAL default descent seam, and cancelling it (WPF's own back-out, `Lab.cs:246-247`) raises the real coordinator's `FlowEnded`, which must reach `RestoreOwner`. **Honest limit:** the duck is invoked directly, because its real trigger is `HostOpened` — a WebView2 host window no headless frame can present. SP-096 owned the duck; this is the restore half it left open |

## 5. Proving it bites (step 4)

Mutation: `Faulted?.Invoke(ex)` removed from BOTH launchers, leaving the log line intact — i.e. the
exact defect this packet closed, "logged and invisible", rather than a strawman.

Result: **3 failures, all three fault-surface facts** —
`PlayPageHeadlessTests.WhenTheDescentThrows_…`, `PlayPageHeadlessTests.AFailureLooksNothingLike…`,
`IntakePageHeadlessTests.WhenOpeningTheRunThrows_…` (21 run, 3 failed). Every other test stayed
green, which is the point: only the "the user saw it" claims depend on it.

Restored with `git checkout --` from commit `c3fc821b`, so the restoration is byte-identical by
construction. The mutation was never committed.

## 6. The thing this packet found that nobody was looking for

**A blank line in a wrapped `TextBlock` wedges the layout pass on these plates (Avalonia 12.1.1).**

The first version of the fault body used WPF's own `"\n\n"` spacing. The headless suite then
**hung** — not failed — inside the click that raised the band. Bisected to the text content, then to
the empty line: reproduces with a two-character body (`"A:\n\nB"`), with and without `LineHeight`,
and — decisively — **identically when written into the EXISTING refusal plate's `GateBandText`**. So
it is a property of the surface, not of this feature, and it was a live landmine in already-landed
code.

Response: `LaunchFaultText.Separator` is a single newline (the shape the landed refusal copy already
uses and which is proven safe), the reason is written at the constant, and
`LaunchFaultTextTests.NoStringAPlateCanRENDER_ContainsABlankLine_AndTheSetIsDERIVED_NeverHandListed`
guards it in the **unit** suite, because the failure it prevents is a hang and a hang is the worst
thing a suite can report. Recorded as §13 D43 with a close condition and the re-measurement to run.

**The first version of that guard was itself the defect it exists to prevent**, and code review
caught it. It hand-enumerated eight strings, **omitted `IntakePassGate.SpentMessageFormat`** — the
string the intake plate renders on six days in seven, and one my own new
`ASpentWeek_ReachesTheSpentArm_ThroughThePage` puts on screen — and sampled `UnverifiedMessage` at
one of eleven reason codes, while its comment claimed to cover *every* plate string. A guard that
claims total coverage and has a hole is worse than one that claims a sample, because the claim is
what stops the next person checking. It is the same defect as a capture harness with a hard-coded
door list that goes blind the moment a wave adds a door: **enumerating by hand is the bug**.

The set is now **derived**, in three sweeps, and the coverage claim is exactly this and no more:

1. **reflection** over every public `string` field and static get-only property of `DtrhGate`,
   `IntakePassGate` and `LaunchFaultText` — the three classes that own plate body copy;
2. **every message the gates can produce**: `DtrhGate.Decide` over every reason code discovered by
   reflecting `EntitlementReasonCodes` (plus an unknown code) and every `EntitlementTier`, and
   `IntakePassGate.Decide` over every `(IntakePassState, IntakePassReason)` pair across five day
   counts — which is where `SpentMessageFormat` is actually rendered, and which catches copy
   composed at runtime that exists as no constant at all;
3. the composed fault bodies: ordinary, clamped and empty-message.

~150 strings per run. The derivation is itself pinned: the test asserts by name that the sweep sees
`SpentMessageFormat`, `SpentMessageOneDay`, `TierRefusalMessage` and `Separator`, and asserts floors
on the member, reason-code and total counts, so an over-narrow filter fails loudly instead of
sweeping nothing. The leading/trailing-blank-line rule skips pure-whitespace joiners by a **derived**
test (`Trim().Length == 0`), not by naming `Separator`.

**Not claimed:** the band *titles* on either page. Those are separate `NoWrap` TextBlocks, the wedge
was measured on a **wrapped** one, and claiming coverage there would repeat exactly the mistake
above. Two of the four titles are the same `LaunchFaultText` constants the sweep already covers.

Bite check: injecting `\n\n` into `IntakePassGate.SpentMessageFormat` — the precise string the hand
list missed — reds the derived guard and names the member in the failure. Restored with
`git checkout --`; the mutation was not committed.

## 7. Floor

Pin (not read, not touched): **1117 unit / 62 headless**.
Declared delta (`floor-delta.json`): **+11 unit / +8 headless**.
Expected observed total: **1128 unit / 70 headless**.

The floor run therefore reports a total that does not match the pin. That is expected: observed
equals pin + declared delta. `client/tests/floor/floor.json` was never opened or edited.

## 8. What this work does NOT prove

Draw-level only. Nothing here is `presentation-verified`:

* not that the amber livery reads as "something broke" to a human eye, and not that the fault plate
  composites legibly over the hero card at real scaling — a headless frame cannot discharge either;
* not that the tray icon, its menu or the `Exit` entry behave on a real desktop — the exit fact
  covers the **no-lifetime** branch only, and the classic-lifetime branch (the one that really shuts
  the app down) is **untested here by design**, because running it would kill the test host;
* not that the shell's minimize and restore really leave and return a taskbar button;
* not that the DTRH or intake host windows present, boot, or render anything;
* no audio, no animation, no focus, no z-order, no window activation claim anywhere.
