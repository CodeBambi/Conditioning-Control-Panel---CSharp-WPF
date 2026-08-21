# SP-137 — plan (Review Level 3 checkpoint, written BEFORE any product edit)

Base: `feat/crossplatform` @ `766be7ac0`. Worktree: `.claude/worktrees/agent-a4047a34f4dfbe4ad`.
No file under `client/src/**`, `client/tests/**` or `client/docs/**` has been touched at the time
this file was written. Only this file exists in the tree.

---

## 1. The universe, stated as a RULE

> **Every file whose path is under `client/src/CcpClient.Desktop/`, at any depth, excluding any
> path segment `bin` or `obj`.** No other exclusion. No hand list, no glob over a chosen subset,
> no "the files I remember".

**Size: 330 files.** Walked twice by two independent enumerations that agree exactly:
`find client/src/CcpClient.Desktop -type f -not -path "*/bin/*" -not -path "*/obj/*"` = 330, and
`git ls-files client/src/CcpClient.Desktop` = 330 (so there is no untracked file and no
`bin/`/`obj/` byte inside the tracked set — the tell the packet warned about, `libvlc` DLLs and
payload copies, cannot appear).

By extension: **303 `.cs`, 16 `.axaml`, 3 `.json`, 3 `.bmp`, 1 `.png`, 1 `.manifest`, 1 `.js`,
1 `.html`, 1 `.csproj`.** Four of those (the 3 `.bmp` + 1 `.png`) are image bytes and carry no
sentence; the text-bearing universe is therefore **326 files**.

### How the universe was read (so the extraction is a rule too)

The 303 `.cs` files were lexed — not grepped — into **string-literal spans** and **comment spans**
(regular, verbatim `@"..."`, raw `"""..."""` and `//` / `/* */` all handled separately), producing
**28,086 spans**. Grep alone cannot tell a sentence a user reads from a sentence a maintainer
reads, and that distinction is the whole of step 2. The 16 `.axaml` files were read separately for
rendered `Text=` attributes versus XML comments.

Counts over that lex:

| Set | Rule | Size |
|---|---|---|
| Claim-vocabulary string literals | literal matches `cannot / can still / is not / does not / no longer / never / there is no / this build / not in this build / unsupported / unavailable / opens no / grants no / has no`, length ≥ 40 | **403** |
| **Build-scope** claim string literals | literal contains `this build` or `this port` | **84** |
| **Build-scope** claims in COMMENTS | comment span contains `this build` or `this port` | **321** |
| `.axaml` rendered `Text=` carrying a claim | attribute value, not comment | **1** (`GoonHostWindow.axaml:56`) |

---

## 2. The classification, and exactly where I draw the line

**USER-FACING** = the literal's value can reach a person running the built app **without reading
source**, because a sink renders it. Decided by tracing the sink, never by tone. Sinks found:

- `Views/Pages/*Notices.cs` constants and `Describe*()` returns — bound into panel `TextBlock`s.
- `GoonDoors.Refused[*].Headline/Missing/OwnerGate/PageSaysInstead` — rendered row by row into the
  Goon host's refusal rail (`GoonHostWindow.axaml.cs:152-170`).
- Gate/fault text: `DtrhGate`, `HapticGate`, `IntakePassGate`, `Views/LaunchFaultText`.
- `CapabilityReason.Detail` strings from the platform factories — panels interpolate them verbatim
  (`VisualsPanelNotices.DescribeSurface`, `PointerPanelNotices`, `SystemPage.axaml.cs:41`). **This
  is why the eight "no Linux X backend" strings count as user-facing.**
- `.axaml` `Text=` literals.
- Protocol payloads the page renders (`GoonProtocol.BuildNetPostResult` `detail`).

**MAINTAINER** = `///` and `//` spans, and `LogDiagnostic(...)` strings. The diagnostic line is
maintainer-class because its only sink is `DebugLogSink` → `Console.Error`
(`Lifecycle/CompositionRoot.cs:13`, `ApplicationHost.cs:88`); no page in this client renders the
log. **Boundary stated plainly: a user who launches from a console can see stderr, so this is a
line I drew, not one the code draws.** I fix the first class; I count and report the second.

---

## 3. The audit result: 3 FALSE user-facing claims in 3 files

Every other build-scope claim was adjudicated against the code. Adjudicated **TRUE** and left
alone: the 8 platform-arm strings (`CreateFor(Linux)` still returns an `Unsupported*` in all of
Audio/Glyph/Input/Overlay/Pointer/Tray/Video×2); the haptic **sink** strings
(`HapticSinkFactory.AdmittedRoutes` is still `[]`, `:27`); "no tiers page of its own yet" (none
exists); "no desktop read-back capability at all" (`PrintWindow`/`GetDC` appear only against the
port's OWN windows in `Glyph/**`, never the desktop); the scheduler's two-balloon absence (the
first-minimize balloon that does ship, `ShellTray.cs:258`, is a different balloon); Mandatory
Video's silent clips; Input's speech/multi-monitor absence; Bubble Count's strict/mercy absence;
the Goon Host/Join "no outbound network" claim (both `HttpClient` sites in the product are
loopback-only — `Ai/LoopbackOllamaProvider.cs:93` refuses a non-loopback endpoint pre-socket, and
`Features/Intake/IntakeHostWindow.axaml.cs:719` GETs the host's own origin under a harness flag).

### FALSE #1 — the packet's instance. `Features/Goon/GoonDoors.cs`, VoiceNotes `Missing`

BEFORE (verbatim, lines 105-108):

```
+ "The residual is WebView2's own prompt: the voice screen is reachable from the title "
+ "menu and its recorder asks the browser for the microphone directly "
+ "(ui/voice/recorder.js:591), so the browser can still ask you. Closing that needs a "
+ "PermissionRequested-deny hook (D250).",
```

False clause: **"so the browser can still ask you. Closing that needs a PermissionRequested-deny
hook"** — false on Windows, still true on Linux.

AFTER (proposed; lines 102-104 above it are kept BYTE-IDENTICAL):

```
+ "The residual is WebView2's own prompt: the voice screen is reachable from the title "
+ "menu and its recorder asks the browser for the microphone directly "
+ "(ui/voice/recorder.js:591). ON WINDOWS this host now answers that request itself, "
+ "DENY, so the prompt does not reach you — and when the hook cannot be installed it says "
+ "so rather than going quiet (D289). ON LINUX there is no permission hook to install at "
+ "all, so the browser still owns the question and can still ask you. On neither has a "
+ "real browser prompt ever been refused here: the deny is proved against a test double, "
+ "and the run that would show it is a headed one nobody has made (D250).",
```

Code that makes each new clause true:

| Clause | Proof |
|---|---|
| "ON WINDOWS this host now answers that request itself, DENY" | `Features/Goon/GoonHostWindow.axaml.cs:393` attaches `WebViewPermissionDeny.TryAttach(wv2.CoreWebView2, OnPermissionDecision)`; `Features/Dtrh/WebViewPermissionDeny.cs:262-278` writes `DenyState` (=2) through `put_State` (args slot 7) for every kind except `AutoplayKind` (=9). Microphone is kind 1. |
| "so the prompt does not reach you" | `WebViewPermissionDeny.cs:28-33` — `COREWEBVIEW2_PERMISSION_STATE_DEFAULT` is what prompts, so the DENY write IS the suppression. |
| "when the hook cannot be installed it says so" | `GoonHostWindow.axaml.cs:401-411` logs the typed `Unavailable(code, detail)` and names that the browser still owns the prompt. |
| "ON LINUX there is no permission hook to install at all" | `WebViewPermissionDeny.cs:118-124` — `!OperatingSystem.IsWindows()` returns `Unavailable("unsupported-platform", …)`; SP-135's own D292 records that Avalonia exposes no permission API on any backend. |
| "no real browser prompt ever been refused here … a headed one nobody has made" | SP-135's *"What SP-135 does NOT establish"*: every fact runs against a fake `ICoreWebView2` built in-process. |

The caveat is **not deleted** and D250 is **not** called closed; Linux is **not** claimed.

### FALSE #2 — `Views/Pages/PointerPanelNotices.cs:43` (`ScopeNotice`, rendered on the Pointer panel)

BEFORE: `… the XP, lucky roll, achievement and haptic a pop pays upstream (this build has no
progression system at all); …`

False since **SP-128** (`9057cfa5b`): `Features/Progression/GradedRunAwards.cs` ships, is
constructed in the intake composition (`Features/Intake/IntakeHostContext.cs:167-180`) and is
written on a graded run (`Features/Intake/IntakeQuizRun.cs:188`). This build has a progression
system; what it does not have is one a bubble pop pays into.

AFTER (parenthetical only): `(the only progression this build has is the graded-intake award
record, and a pop pays into none of it)`. The rest of the sentence — pop sound, XP, lucky roll,
achievement and haptic **for a pop** — stays, and is still true: no bubble-pop path references
`GradedRunAwards` and Bubble Pop is not among the ported haptic sites (D210 lists flash decay,
video background + stop, subliminal pulse, bounce tap; pop is site 4 = D212, unported).

### FALSE #3 — `Views/Pages/HapticsPanelNotices.cs`, two rendered strings

False since **SP-126** (D210), whose own row says *"`Views/**` was not touched"* — the exact
shape of this packet.

BEFORE `:44-46` (`DescribeLiveState`, Armed arm): `"… It will not go further than this in this
build — nothing sends anything to it yet."`
AFTER: `"… It will not go further than this in this build — the effect modules do drive the
haptic limb now, but the sink admits no provider route, so nothing has ever been sent."`

BEFORE `:121-125` (`DescribeAbsences`, last clause): `"And even with a device attached, nothing
would move: no effect in this build sends anything to haptics yet."`
AFTER: `"And the modules are no longer the reason nothing moves: the flash decay ladder, the video
background layer and its stop, the subliminal pulse and the bounce tap all command the haptic limb
now — every one of those commands ends at 'there is no device to address', because no provider
route is admitted at all."`

Proof: `Effects/FlashSurfacePresenter.cs:114`, `Effects/MandatoryVideoEffect.cs:70`,
`Effects/SubliminalsEffect.cs:83`, `Effects/BouncingTextField.cs:83` each hold an `IHapticLimb`;
`Lifecycle/CompositionRoot.cs:239-241` passes the **real** limb (`haptics: haptics.Limb`) into the
session, so this is the shipping composition and not a test seam; `Haptics/HapticSinkFactory.cs:27`
still has `AdmittedRoutes = []`.

**Plus three MAINTAINER doc comments in that same file** (`:13`, `:110`, `:114`) that assert the
same falsified fact. Normally reported, not fixed — but these are the doc comments *of the two
strings being corrected*, so leaving them makes the shipped refusal incoherent, which is the
exception the packet names. I will fix those three and say so.

### Counts

- Found FALSE, user-facing: **3 claims across 3 files** (4 string literals).
- Fixed: **3** (+3 maintainer doc comments, under the coherence exception).
- Reported-not-fixed: the **321** build-scope maintainer comment spans, minus the 3 above = **318**
  not individually adjudicated. Stated as a count with its reach, never as "all checked".

---

## 4. The guard, and what it cannot see

**Ship it, narrow and bound.** New file `client/tests/CcpClient.Tests/UserFacingClaimTests.cs`:
a **claim-to-code binding registry**, not a prose matcher. Three entries, each pairing a sentence
read **from the running product** with **one machine-evaluated fact about this repository**:

| Id | Sentence source (runtime) | Fact |
|---|---|---|
| `goon-voice-prompt` | `GoonDoors.For(GoonDoors.Refused, VoiceNotes).Missing` | `Features/Goon/GoonHostWindow.axaml.cs` contains `WebViewPermissionDeny.TryAttach(wv2.CoreWebView2` **and** `WebViewPermissionDeny.cs` contains the `!OperatingSystem.IsWindows()` → `unsupported-platform` arm |
| `pointer-progression` | `PointerPanelNotices.ScopeNotice` | the product assembly contains ≥1 type in namespace `CcpClient.Desktop.Features.Progression` (reflection — exact, no text) |
| `haptic-silence` | `HapticsPanelNotices.DescribeAbsences()` + `DescribeLiveState(Armed,…)` | ≥1 file under `client/src/CcpClient.Desktop/Effects/` (same universe rule, `bin`/`obj` excluded) contains `IHapticLimb` |

Each entry owns a `Verdict(bool buildCanDoIt, string sentence)`. Tests:

1. `VoiceNoteRefusal_SaysWhatTheDenyHookActuallyDoes`
2. `VoiceNoteRefusal_KeepsTheCrossingAndTheTightening` — pins the two sentences the packet requires
   to survive, so a later edit cannot quietly trade one false sentence for another
3. `ProgressionClaim_MatchesTheProgressionThatShips`
4. `HapticSilenceClaim_MatchesTheLimbSitesThatShip`
5. `EveryClaimBinding_IsNonVacuous` — re-runs all three verdicts with the fact **flipped** and
   requires each to fail. A binding that cannot red is decoration, and this fact refuses to let one
   exist.

**+5 unit, 0 headless.** No new headless test is needed: `GoonPracticeHeadlessTests:189-198`
already renders every refusal's text into the rail, so the corrected sentence is carried by an
existing visual-tree fact.

Shape constraints I must respect (`client/tests/floor/**` is must-not-change, so the
vacuous-shape ledger cannot absorb a new disposition): **no `return;`, no try/catch, no
platform/env/`File.Exists` predicate, and at least one top-level `Assert` before any loop** in
every one of the five.

### What this guard CANNOT see — named, not implied

1. **It cannot discover a fourth claim.** Its reach is the three rows above. I deliberately do
   NOT ship an enrolment matcher over the 403 candidate strings: a matcher that greps prose for
   "capability claim" would assert a reach it cannot enforce, which is the packet's trap 4 and
   `port-lessons.md` wave 62 (*"the fix is never a cleverer matcher"*). A phrase-anchored enrolment
   list over the 84 `this build`/`this port` literals was considered and rejected for the same
   reason: enrolment without a falsifier is a checksum of prose, and it would have caught **none**
   of the three defects found here, because nobody edited those sentences — the code moved.
2. **It cannot check a claim whose falsifier is not a fact about this repository's source.**
   Anything needing a real browser, a real Linux session, a headed prompt, a device or a server is
   outside it — which includes the whole residual D250 gate the corrected sentence names.
3. **Two of the three facts are syntactic token presence** (`WebViewPermissionDeny.TryAttach(...)`,
   `IHapticLimb`). A call site that exists but is dead, or one that is renamed, moves the fact
   without moving the truth. The reflection fact (`Features.Progression`) is exact.
4. **It checks the sentence, not the pixels.** A rail that stopped rendering `Missing` would leave
   every one of these five green.

---

## 5. Divergences

`client/docs/wpf-surface-reachability.md`, new `## SP-137` section, **D306 onward** (max id at the
head is **D295**; D296-D305 are left to sibling lanes). Rows: D306 the class and the universe rule;
D307 the three corrections with before/after; D308 the guard and its four blind spots; plus a
*"What SP-137 does NOT establish"* block. Divergence rows ONLY — no other edit to that document.

## 6. Floor

`spine-tasks/SP-137-claims-that-went-false/floor-delta.json` = `{ unit: 5, headless: 0 }`.
Pin 2599/152 → expected observed **2604 unit / 152 headless**. `floor.json` is never opened.

---

# AMENDMENT — after the plan review (written before the amended work was committed)

The plan above is kept as the checkpoint record. Four things in it changed at review, and one
sentence in it is **wrong**; all five are recorded here rather than silently rewritten above.

## A1. `plan.md:134` is FALSE and must not be carried anywhere

It says *"pop is site 4 = D212, unported"*. **D212 is the flash LUMINANCE layer**, not Bubble Pop.
Bubble Pop's upstream haptic is `Services/BubbleService.cs:951-980`, which is **outside the
eighteen-site `Services/{Flash,Video,Subliminal}` census entirely**. The conclusion it was
supporting — that a bubble pop pays no haptic in this build — is unaffected and independently true
(no `IHapticLimb` reference exists in any Bubble Pop file). The sentence is quarantined here and
appears in neither `record.md` nor any shipped string. Recorded because a packet about sentences
that went false may not leave a false sentence in its own plan.

## A2. FALSE #2 is DROPPED — `PointerPanelNotices.cs:43` is not edited

Adjudicated **TRUE with a precision nit**. "Progression system" has a settled meaning in this
codebase's vocabulary and it is the XP/level ladder (`BubblePopPresetDocument.cs:44`,
`BubbleCountPresetDocument.cs:26`, `EffectReasonCodes.cs:295`,
`BubblePopSurfacePresenter.cs:84`), and none of it exists: `IntakeHostWindow.axaml.cs:538-541`
computes completion XP and logs it *"computed, not granted (no XP store — typed seam)"*, and
`GradedRunAwards.AwardableIds` (`:165`) is closed to two intake-only ids. My original finding
rested on a **directory name**, `Features/Progression/`, rather than on the vocabulary — precisely
the over-literal reading that would have shipped a worse sentence than the one it replaced, since
this text sits in a Bubble Pop notice explaining that a pop pays nothing. Recorded as **D309**.

## A3. FALSE #3 is BIGGER than scoped — two false clauses, not one

The last line of `DescribeAbsences()` read *"even with a device attached, nothing would move: no
effect in this build sends anything to haptics yet"*. **Both halves are false.** Six live sites
fire the limb (`FlashSurfacePresenter.cs:314`, `MandatoryVideoEffect.cs:307`, `:340`, `:419`,
`SubliminalsEffect.cs:223`, `BouncingTextField.cs:237`), each incrementing `HapticLimb.Moments` and
posting a real envelope (`HapticLimb.cs:187-227`) — so the modules DO send; and with a route
admitted those sends would go out, so the counterfactual is wrong too. The true stop is one rung
out: `AdmittedRoutes` is `[]`, `Create()` returns `UnadmittedHapticSink`, and `Send` returns at
`EvaluationsWithNoDevice++` (`HapticLimb.cs:566-570`) without reaching `SetOutputsAsync`. The
sentence **named the wrong cause**, which is the exact defect its own file header cites upstream
for. Recorded as **D308**.

## A4. Citations corrected, non-selecting

`WebViewPermissionDeny.cs:115-121` (not `:118-124`) is the unsupported-platform arm; the headless
rail fact starts near `GoonPracticeHeadlessTests:194`.

## A5. Two edits OUTSIDE the packet's enumerated May-change list

`client/tests/CcpClient.HeadlessTests/GoonPracticeHeadlessTests.cs` and
`client/tests/CcpClient.HeadlessTests/HapticsRowHeadlessTests.cs` each pinned the **exact false
sentence** being corrected, so both went red the moment the product text changed. Neither is named
in the Contract's `fileScopeMustNotChange`. Their needles were **re-pointed, never deleted or
weakened** — the Goon fact gained assertions (1 → 3) and the haptics fact gained a
`DoesNotContain`. Test counts are unchanged. Reported in full rather than treated as routine.

## A6. Guard shape, after the review

Two bindings, not three (the progression binding went with A2). Five facts, so the floor delta is
unchanged at **+5 unit / 0 headless**. Needles are hoisted to class-level constants so no `[Fact]`
body carries a platform predicate. **A discrepancy worth recording:** the review warned that
`VacuousShapeDetector` flags `OperatingSystem.Is` *"including inside a needle string"*. Reading
`VacuousShapeDetector.Sanitize` (`:341-470`), plain, verbatim, raw and interpolated string literals
are **all blanked before analysis**, so a needle inside a literal is invisible to the detector. The
hoisting was kept anyway — it costs nothing and does not depend on which reading is right — but the
constraint as stated is stricter than the code enforces.


## A7. SUPERSEDED BY CODE REVIEW: the registry is THREE, and `haptic-silence` never existed

The table at `plan.md:183` and A6's "Two bindings, not three" are both **stale**, and are left in
place because this file is the checkpoint record rather than a live document. The landed registry is
**three** — `goon-voice-prompt`, `haptic-absence-line`, `haptic-armed-arm` — because a needle over
the two haptic strings JOINED is satisfied by the absence line while the Armed arm says anything at
all, which is how a self-refuting Armed sentence got through review once. **There is no binding
called `haptic-silence` in the landed code**; that id appears only in this plan and in the review
history. `record.md` §6 carries the real table, and §12.5 records that this count went false in four
artifacts at once — the packet's own defect class, third instance.

Also superseded: the flipped-fact "non-vacuity" assertion described in the original plan was
**logically entailed** by the assertion above it and could never red alone. It was removed rather
than kept for the look of coverage; `EveryClaimBinding_IsRegisteredAndAgreesWithTheCode` now pins
the registry's size and exact id list instead. Fact count is unchanged at five, so the floor delta
stands at **+5 unit / 0 headless**.