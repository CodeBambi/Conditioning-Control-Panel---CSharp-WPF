# SP-097 — plan checkpoint

Branch `lane/SP-097-failures-the-user-sees`, base `4e62c6fe`.

## 1. The failure surface

**A second, differently-liveried plate in the same idiom — not a modal dialog, and not the
refusal band.**

WPF's surface is a blocking `MessageBox` with `MessageBoxImage.Warning`, title
`"Down the Rabbit Hole"` / `"Graded Intake"`, body `"Couldn't start …:\n\n" + ex.Message`
(`MainWindow/MainWindow.Lab.cs:164-165`, `:269-270`, `:336-337`). Three reasons the port does not
import the modal:

* the port has no dialog service at all, so a modal here would be a new window class whose
  every user-visible claim (modality, activation, focus return, z-order) is **presentation**-class
  and undischargeable by this packet;
* the port's landed idiom for "the page must say something out loud" is the SP-094/SP-095 plate,
  and a second idiom on the same card would make the surface answer in two grammars;
* WPF's dialog is *dismissed* and then the card is live again. A hit-test-transparent band gives
  the same "you can press again" outcome without a modal loop.

### How a failure is visibly distinct from a refusal

The refusal band already means *"we could not determine your entitlement."* The fault surface
differs on **five** independent axes, so no single later edit can collapse them:

| | Refusal (SP-094/095) | Failure (this packet) |
|---|---|---|
| Element | `GateBand` / `PassGate` | **`FaultBand`** (a different Border, on both pages) |
| Style classes | `lock-band` / `intake-gate` + `lock-plate` | **`fault-band` + `fault-plate`** |
| Rim + title colour | tier-2 violet `#FFB47BFF` / `#FFD05CE8` | **warning amber `#FFF0A02E`** (WPF's `MessageBoxImage.Warning` severity, carried across as colour rather than a glyph) |
| Scrim | `#A8120A1E` / `#CC0A0A14` | **`#C8140A02`** (amber-black; still translucent, so the card reads through — `PlayTabView.xaml:247-248`) |
| Headline | "LAB ONLY" / "COULD NOT VERIFY" / "This week's intake is done" | **"Couldn't start Down the Rabbit Hole" / "Couldn't start Graded Intake"** — WPF's own words |

Body text: WPF's shape (`headline + ":\n\n" + detail`) with the **exception type prefixed** to the
message, plus one port line that says in words what the colour says: *"This is a fault in the app,
not a decision about your account."* The refusal wording ("Tier 2 perk", "upgrade your pledge",
"could not verify your entitlement", "You've taken your free Graded Intake") is asserted absent
from it, and vice versa.

Both bands are mutually exclusive: raising the fault band hides the refusal band and a later
decision hides the fault band, so the two never appear together.

### The trap: nothing is swallowed

* the **diagnostic** carries `ex.GetType().Name` **and** `ex.Message` — the port's own convention
  everywhere outside the entitlement path (`Audio/SoundArbitration.cs:412`,
  `Companion/BarkPipeline.cs:493`);
* the **user-facing text** carries the type *and* the message, which is strictly WPF's disclosure
  plus the type. Clamped to 400 chars (the plate lives inside a card; an unclamped multi-line
  message re-creates the §10 D23 illegibility defect). Recorded as a divergence;
* the exception object itself is retained on the launcher as `LastFault`, so nothing is lost
  in-process and "not swallowed" is assertable;
* the entitlement-resolution catch stays **type-only** and stays a *refusal* — that throw really
  is "could not determine your entitlement", and its message can carry a bearer
  (`DtrhLaunch.cs` existing comment; `HostLoginEntitlement.cs` AskAuthorityAsync). Recorded.

### Where the catch goes

WPF wraps the **whole handler**, so the port wraps the whole of
`DtrhLaunch.GateThenDescendAsync` (after `GateArrivals++`) and the whole of
`IntakeLaunch.Launch()`. That covers the coordinator construction, the gate call, the
`Decided` render itself and the descent/open — not just the descent.

`FallInAsync`/`QuickDropAsync` become `Task<DtrhGateDecision?>`: **null = no decision was taken
because the launch faulted**, which is the exact shape and doc-comment `IntakeLaunch.Launch()`
already uses for "no decision because a live run was refocused". A fourth `DtrhGateDecision`
case is deliberately NOT added — a fault is not a gate verdict, and typing it as one is trap 2 at
the type level.

## 2-5. The four never-executed paths — all four taken

2. **`RequestApplicationExit`** — headless: invoke the real tray menu's `exit` item on a real
   `MainWindow`, assert the no-classic-lifetime diagnostic line and that nothing shut down.
3. **`catch -> Unavailable(tier-authority-fault)`** — a `IHostAuthTokenReader` whose `Read()`
   throws makes the real sealed `HostLoginEntitlement.ResolveAsync` throw; assert
   `RefusedUnverified(tier-authority-fault)`, the message-free diagnostic, and that the user sees
   the refusal band (not the fault band).
4. **`IntakePage.RefusedSpent` / `RefusedNeedsAccount`** — driven through the page: an injected
   `IIntakeEntitlementSource` with `IsLoggedIn == false` reaches NeedsLogin; the same seam with
   `IsLoggedIn == true` plus a real `ConsumeForCompletedIntake()` on the real pass service reaches
   Spent. No stubbed decision either time.
5. **`RestoreOwner`** — real FALL IN -> real picker -> `Tray.Duck()` -> `picker.Close()` raises the
   coordinator's real `FlowEnded`, which must reach `RestoreOwner` -> `ShellTray.Restore()`.
   The duck is invoked directly because `HostOpened` needs a WebView2 host window no headless
   frame can present; the restore half — the packet's actual ask — is fully real.

## Files

`Features/Dtrh/DtrhLaunch.cs`, `Features/Intake/IntakeLaunch.cs`,
`Views/Pages/PlayPage.axaml{,.cs}`, `Views/Pages/IntakePage.axaml{,.cs}`,
`Views/MainWindow.axaml` (two style blocks), new `Views/LaunchFaultText.cs`,
tests in both projects, `client/docs/wpf-surface-reachability.md` (divergences only),
`spine-tasks/SP-097-failures-the-user-sees/{plan.md,record.md,floor-delta.json}`.

## Discrepancies already found (before writing code)

* **The orchestrator brief says `IntakePage` "fires the launch as a discarded task".** It does
  not: `BeginIntakeButton.Click += (_, _) => intake.Launch();` is a **synchronous** call
  (`IntakePage.axaml.cs:41`), so a throw escapes the click handler into Avalonia's dispatcher —
  not an unobserved task exception but an unhandled one. Same user-visible defect (nothing is
  shown), worse process outcome. The fix is the same and covers both.
* **"Record divergences from D40 onward"** — D40 already exists, recorded at the wave-39 land by
  the SP-096 final review. This packet starts at **D41**.
