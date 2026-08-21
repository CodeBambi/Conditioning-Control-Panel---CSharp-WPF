# SP-132 — record

Branch `lane/SP-132-goon-evidence`, base `79d08a27b` (wave-66 lock). Plan checkpoint at `d694e33d3`.

---

## 1. THE RUNG REACHED, in the packet's own words

The packet names three rungs and says they are not equivalent. Here is exactly where this stopped.

| Rung | Claim | Reached | Evidence class |
|---|---|---|---|
| **1** | **The origin SERVES** | **YES** | in-process HTTP GET at the bound origin, asserted on BYTES. No browser, no monitor, no lease |
| **2** | **The page LOADS and the handshake COMPLETES** — `init` + `manifest` answered and acknowledged | **YES** | a real WebView2 load on a real Windows desktop; the page's OWN object graph read back; `presentation-verified` pixels behind a UIA gate |
| **3** | **A duel is PLAYED** | **NO. Not attempted, not claimed, and OWED** | a human gate. No harness in this repository can drive one |

**Serving bytes is not loading a page, and loading a page is not playing a duel.** Rung 2 is
reached; rung 3 is owed to a person opening the Play page, pressing PRACTICE, and playing.

**Nobody has still played a duel.** What changed is that somebody — a machine, under a lease, with
the state confirmed before the shutter — has now SEEN the page.

---

## 2. Rung 1 — the origin serves

`client/tests/CcpClient.Tests/GoonServingTests.cs`, over a real `GoonParticipant` bound to the real
`payload/goon` build output, with an explicit temp data directory so the real profile is never
touched. **10 facts, all passing on the first run**, before the harness was touched at all.

| Fact | What it proves, on content rather than a status code |
|---|---|
| `TheDocument_IsRetrievedAtTheOrigin_WithItsRealBytes` | `index.html` byte-equal to disk, `text/html; charset=utf-8`, nosniff, and it really carries `<script type="module" src="./boot.js">` |
| `EveryRequiredBootFile_IsRetrievable_ByteForByte` | all three of `GoonServingRoots.RequiredFiles`; `boot.js` arrives WHOLE at >100 KB, so the body streams rather than a header answering |
| `AssetsBeyondTheDocument_AreServed_WithTheRightTypeAndTheRightBytes` | the packet's explicit requirement — `goon.css`, `ui/screens.css`, `package.json`, and the title logo checked by its **PNG magic number**, which an error page or an empty body cannot pass |
| `RangeRequests_AreHonoured_OnTheGoonOrigin` | 206 with the right 64 bytes and 416 on an unsatisfiable range — the stated reason the §4 class was reused unmodified |
| `ThePageUrlTheHostNavigatesTo_Serves` | **the exact string `GoonParticipant.PageUrl()` hands the WebView**, bridge-token query and all |
| `TheFourDeniedFiles_Answer415_AndTheyReallyExist` | the D258 four, each asserted PRESENT on disk first, so 415 is the allowlist refusing and not a 404 in disguise |
| `TheServedTrees415Set_IsDerivedFromTheWire_NotRestated` | the effective allowlist **derived from behaviour**: sweep by extension class, ask the server for one representative of each, partition on the status codes that come back |
| `Section4_Discipline_HoldsOnTheGoonOrigin` | 405 / 403 traversal / 404 / `/health` on THIS origin — a third, separately-constructed server |
| `TheBridgeInbox_IsTokenGuarded_AndServesJson` | wrong token 404 with a FIXED route class; right token serves JSON; missing cursor is a typed 400 |
| `TheUserMediaOrigin_IsBound_AndRefusesAnAbsentName` | the second origin the `manifest` frame's URLs point into is really bound |

**Board row 333 item (2) is closed in substance without touching `GoonPracticeTests.cs`** (which is
must-not-change). That row asks for the allowlist "read from its source rather than restated". The
sweep above is stronger than reading the table: it observes what the server DOES, so a change on
the server side moves an observed status code. The literal in `GoonPracticeTests` still exists and
is now cross-checked rather than removed.

**The derivation's shape, stated because it is not the obvious one.** There are **three** refused
extension CLASSES — `.webmanifest`, `.mjs`, and the EMPTY STRING — and the empty string covers TWO
files, so the comparison to the four D258 names is made over files and not over classes. Six of the
server's nine allowlist rows are exercised by this tree (`.css .html .js .json .mp3 .png`); the
goon tree carries no `.gif/.webm/.webp`, so three are unreachable through it, and the fact says so
rather than claiming "the allowlist works".

---

## 3. Rung 2 — the page loads and the handshake completes

### 3.1 What was observed, verbatim from the run

```
goon handshake confirmed: goon-probe: surface=embedded nav=success heartbeats=2 pageread=yes
  ready=true solo=true name=Player canhost=false assetcache=false images=0 screen=title modal=open
  | page-rect 1058.9x789.7 DIP @ scale 1.75 @ screen 216,149
goon page log: boot: ready posted, build r17-20260806 (hosted), waiting for init + manifest
goon page log: init: Player solo=true mode=- net=via-host
goon page log: manifest: 0 images, 0 videos
goon page log: boot ok
CAPTURE: ...artifacts/windows-goon-page-first-run.png (1853x1382)
CAPTURE PASS
```

`AdapterInfo` from the same run: `Type = WebView2, Engine = Blink, Version = 151.0.4129.93`.

**Why `ready=true` is the acknowledgement and the host's own flag is not.** `session.ready` is
written in ONE place in the payload — `boot.js:418`, inside `settle()`, behind a guard at `:416`
that returns unless BOTH `gotInit` (`:322`) and `gotManifest` (`:354`) are set. It means the page
PARSED both frames. `GoonHostWindow.BootMessagesSent` is set at `GoonHostWindow.axaml.cs:529`
**before either message is dispatched**, so it would read true even if both dispatches faulted;
proving the host spoke would have proved nothing. `solo=true`, `name=Player` and `canhost=false`
are values THIS host computed and posted, read back out of the page, so they also rule out the
standalone frame the page synthesizes for itself (`bridge.js:473`).

**The exit handshake, unexercised before this packet, ran for real**: `end-run posted` →
`goon page log: exit-done (end-run)` → `exit-done received — closing` → `dashboard restored
(Normal)` → process exit 0.

### 3.2 The pixel evidence, and its class

`presentation-verified`: composited pixels read off the real Windows desktop through a
`DwmFlush`-fenced `CopyFromScreen`, under the machine-wide real-desktop lease, at the rect the
window's own probe published.

| Check | Region | Colour | Tol | Min | Real capture |
|---|---|---|---|---|---|
| `goon-page-backdrop` | 0.30, 0.02, 0.40, 0.035 | `#1C0B1F` | 6 | 0.98 | **1.0000** |
| `goon-page-explainer-card` | 0.35, 0.40, 0.05, 0.025 | `#1C0923` | 8 | 0.95 | **0.98913** |

### 3.3 WHAT `first-run` IS — the name has to be honest

The page's screen id is `title`. Over it sits the title screen's explainer card, which auto-opens
420 ms after the first mount (`ui/screens/title.js:157`, setting its own flag at `:137`). **That
card is what is photographed. The BARE title menu is a different state and is NOT captured here**;
it is not reachable from this harness without driving input INSIDE the page, and nothing here does.

**The determinism was measured, and my first explanation of it was WRONG.** The harness clears the
goon WebView2 profile — but a run with that clear deliberately skipped still reported `modal=open`.
What actually makes it deterministic is that `localStorage` is scoped per ORIGIN and this page is
served from an EPHEMERAL port, so every launch gets an empty store. The clear is now best-effort
and non-fatal (a real run failed hard when a WebView2 child still held a file in that directory
seconds after the previous run), and the comment in `capture.ps1` says what is true. **The
`modal=open` gate is the mechanism.**

---

## 4. Every check demonstrated to fail on a capture of the wrong state

All at the committed head `965ef0a29` (§4.3's last two rows at `08c3bf768`, the guard-hardening
commit, and re-run there in full). Three real wrong-state captures, at the same rect.

| # | Wrong state | How produced | `goon-page-backdrop` | `goon-page-explainer-card` |
|---|---|---|---|---|
| A | **payload-missing** | `payload/goon` RENAMED in the build output. **No source change at all** — the product's own typed honest surface | **0.0000** | **0.0000** |
| B | **the boot LOADER** | the `manifest` send seeded out of `SendBootMessages`, reverted after | **0.0000** | **0.0000** |
| C | **the DASHBOARD** | a real `capture.ps1 -Surface dashboard` capture — a different surface entirely | **0.0000** | **0.0000** |
| — | the real capture | | 1.0000 | 0.98913 |

`CcpVerify` exits 2 on A, B and C, naming `goon-page-backdrop` first.

**And the harness REFUSES A and B before any pixel:**

```
FAIL: the Goon host did not select the embedded surface: goon-probe: surface=payload-missing ...
FAIL: the Goon host window CLOSED ITSELF while this capture waited for the page. Last probe:
  ... ready=false ... screen=boot modal=closed ...
```

### 4.1 (C) IS THE DEMONSTRATION THAT CHANGED THE SHIPPED NUMBERS

The plan-gate review said to MEASURE (C) rather than rely on it. It was right, and the check was
wrong. The first draft pinned `goon-page-backdrop` at `#1B0B1E` **tolerance 8**, which cleared the
loader rule comfortably (distance 14) — **and scored 1.0000 on the real dashboard capture**, because
`dashboard-background` is `#141018`, only 7 away per channel. A check that accepts a photograph of a
different window is the "cannot fail on a wrong capture" defect this project has found fourteen
times, and **it would have shipped had the separation been assumed.**

The fix is not a smaller number, it is a rule: both tolerances now sit strictly below the distance
to the nearest colour ANY other check in the manifest declares, and
`NoGoonCheckAcceptsTheColourOfAnotherDeclaredState` asserts that over the manifest itself.

### 4.2 A fourth region was measured and REJECTED

The scrim at the left margin (0.02, 0.35, 0.12, 0.30) is perfectly flat and scores **1.0000 on the
real capture — and 1.0000 on the LOADER too, at every tolerance down to 4.** It cannot tell a page
that rendered from a page that never booted. It would have looked exactly like coverage.

### 4.3 The guards, and the edit each was watched red on

| Edit | What went red |
|---|---|
| `goon-page` removed from `capture.ps1`'s `-Surface` ValidateSet | `EveryGoonSurfaceAndState_IsOneTheCaptureScriptAcceptsAndCanDrive` |
| `first-run` removed from the `-State` ValidateSet only | same fact — **the half nothing guarded before**, which would otherwise fail at PowerShell parameter binding |
| `goon-page-backdrop` demoted to `draw-verified` | `EveryGoonCheck_ClaimsPresentationVerified` |
| its tolerance widened 6 -> 8 | `NoGoonCheckAcceptsTheColourOfAnotherDeclaredState`, naming `dashboard-background` |
| its tolerance widened to 16 | also `NoGoonCheckAcceptsTheColourOfTheLoaderItMustReject` |
| the `ready` gate DELETED (poll condition AND refusal) | `TheGoonCapture_ConfirmsTheHandshakeBeforeItReadsAnyPixel` |
| the `modal=open` gate deleted | same |
| the `nav=failed` refusal deleted | same |
| `CopyFromScreen` hoisted above the whole gate | same |
| `.webmanifest` added to the served tree's allowed set | `TheServedTrees415Set_IsDerivedFromTheWire_NotRestated` |
| the DTRH borrow reverted to SP-130's construction | `TheTwelveHotlinkedAssets_FallThroughToTheDtrhTree` |

**A SECOND GUARD THAT COULD NOT FAIL, found at code review.** The colour guard excluded every
`goon-` SURFACE rather than only same-state siblings — equivalent today, because there is one goon
state, but a future `goon-duel/played` check would have escaped the comparison silently, which is
the whole failure mode it exists for. Demonstrated both ways against a seeded
`goon-duel/played` check pinned 2 away from `goon-page-backdrop`: the **prefix rule PASSED** it, the
shipped **surface+state rule FAILS** it by name —
*"'goon-page-backdrop' (#1C0B1F, tolerance 6) accepts 'SEED-goon-duel-played' (#1E0D21) — they are
2 apart per channel"*. Both the seed and the reverted rule were removed.

**AND ONE THAT DID NOT RED, WHICH IS WHY THIS SECTION IS WORTH RUNNING RATHER THAN DESCRIBING.**
At `965ef0a29` the ordering guard needled the bare tokens (`ready=true`, `screen=title`). A seeded
run that **deleted both real gates still PASSED**, because the token survives inside the `Fail`
message that explains them. **A guard satisfiable by its own error text is the defect it exists to
catch, one level up.** Every needle now binds the PowerShell REFUSAL — `-notmatch 'ready=true'`,
`-match 'nav=failed'`, `-ne 'embedded'` — which prose about a refusal cannot satisfy, and all four
variants above red at `08c3bf768`.

---

## 5. THE FINDING: twelve assets the page hotlinks were all 404 (D266)

**Nobody had loaded the page, so nobody had heard this.** The page reported it itself, on the host's
own transcript:

```
goon page log: warn: [GG audio] asset failed: /dtrh/assets/bubbles/sfx/Pop2.mp3 (HTTP 404)
```

The goon page hotlinks **twelve** assets out of the DTRH tree by ABSOLUTE path, and says so in its
own words: *"HOTLINKED, not copied. Resources/web is the page origin's root under WebView2 and every
harness mounts /dtrh/ at that exact prefix"* (`ui/audio.js:43-46`).

- **six audio cues** — `ui/audio.js:112` declares `DTRH_SFX_DIR = '/dtrh/assets/bubbles/sfx/'`, resolved at `:203`, `:219`, `:221`, `:225`, `:230`, `:235`: `Pop.mp3`, `Pop2.mp3`, `Pop3.mp3`, `chime1.mp3`, `chime2.mp3`, `GG.mp3`
- **six images** — `exec/fx.css:271` (the bubble sprite) and `:304-308` (five effect icons)

SP-130 passed the goon tree as BOTH roots so that *"nothing else is reachable on this origin"*, and
all twelve answered 404. **User-observable in a practice duel**: bubbles are one of the eight
element kinds this build exercises, and their sprite, their five effect icons, the pop and the win
chime were blank or silent.

**Fixed, with the precedent it already had.** `GoonParticipant` now uses the `IntakeParticipant`
overlay-first BORROW verbatim (`IntakeParticipant.cs:94-100`): goon tree as overlay, dtrh tree as
payload fallback.

### 5.1 WHAT THE FIX GIVES UP, AT ITS REAL SIZE

**Measured, not characterised — and the first draft of this record did not measure it.**

| | |
|---|---|
| dtrh payload files | **1542** |
| shadowed by the goon tree (`index.html`, `boot.js`, `bridge.js`) | **3** |
| **newly reachable at `/dtrh/*` on the goon origin** | **1539** |
| of those, actually SERVE | **1537** |
| of those, answer 415 (`.md`) | **2** |

The fallback admits **the whole DTRH payload tree**, not the twelve files that motivated it, and
SP-130's *"nothing else is reachable on this origin"* is deliberately given up. **One line reverts
it.**

**My earlier wording here said only "this is not a new admission surface".** That sentence is true
about the MECHANISM and reads far narrower than the fact, and nowhere did I state 1539 or pin it.
**That is this packet's own standard turned on me: I asserted the surface was not new without
measuring the surface.** It is now measured, stated, and pinned by
`TheBorrowsAdmissionSurface_IsMeasured_AndBoundedToTheDtrhPayloadTree`, which is deliberately
brittle — a change to that number is an admission surface that moved, not a constant to update.

**Why the trade is still right, beside the number rather than instead of it:**

- the mechanism is **verbatim** the intake origin's, which has borrowed these same `bubbles/sfx`
  files since SP-054 (`IntakeServingTests.The_Borrow_Falls_Through_To_The_Dtrh_Tree`)
- this construction is **narrower than that one** in one respect: `/media/*` keeps the goon tree's
  own root (`GoonServingRoots.MediaRoot`) rather than the dtrh one
- every §4 control is re-asserted on this origin — GET-only, MIME allowlist + 415, traversal
  refusal, nosniff — and the `.md` files in that tree still 415
- **no user media is admitted at all**: `/umedia/*` keeps its own root on its own port
- the fallback root is **located by what does not resolve through it** — a file at the build-output
  root and a file in a sibling payload tree both 404, so the root is `payload/dtrh` and neither
  `AppContext.BaseDirectory` nor `payload/`

**SP-130's stated property is deliberately given up, and one line reverts it if the reviewer
disagrees.** Both directions are pinned at the wire: `TheTwelveHotlinkedAssets_FallThroughToTheDtrhTree`
and `TheGoonTreeStillShadows_TheBorrowedFallback` — the second because both trees carry
`index.html`, and a fallback that won there would serve the DTRH game from the goon origin, which
is worse than a missing pop sound and would still answer 200.

---

## 6. Two defects THIS PACKET INTRODUCED, both found only by running it

Recorded because neither is visible to any headless frame, and both were in the probe that exists
to make the capture trustworthy.

1. **The probe FROZE.** It stopped re-reading the page once `ready=true` arrived, so every other
   field stayed at whatever it was in that instant. The explainer card opens 420 ms LATER, so the
   probe reported `modal=closed` while a card sat open on screen — and the harness then refused a
   perfectly good build for 90 s. **A probe that stops observing is a probe that lies**, and this
   one is what a headed capture decides on.
2. **The probe CRASHED THE APP.** `PointToScreen` throws for a detached visual, `LayoutUpdated`
   still fires during teardown, and the throw escaped the UI lifetime as
   `panic: System.ArgumentException: Visual does not belong to a visual tree`. Guarded on
   `VisualRoot`, the `FeaturePopupWindow.axaml.cs:288-292` precedent.

**And a third in the product, found at code review rather than by running it — the same class as
(1), one level further in.** `_pageStateInFlight` is released only INSIDE the continuation, and a
continuation only runs if a task was returned. **An `InvokeScript` that threw on the calling
thread** — a disposed adapter, a dead browser process — **would pin the flag at 1 and the probe
would never read again.** Fail-closed at the harness, which is why nothing caught it, but it is
precisely *"a probe that stops observing is a probe that lies"*. Now a `try/catch` that releases the
flag, records `pagestate-faulted=<type>` and republishes. **It has no automated coverage**: reaching
it needs a live `_web`, which means a real embedded browser, and no test in this repository builds
one. Stated rather than implied.

And a third, in the harness rather than the product: the poll loop **died with a raw `.NET`
exception** — *"The target element corresponds to UI that is no longer available"* — when the goon
window closed itself under it. That is what the page's own 45 s deadline does (`boot.js:113`) when
the handshake never completes: it posts `boot-error` and the host closes the window honestly. A raw
exception is a worse outcome than the failure it reports; it is now a typed refusal naming the
page's deadline. **Found by the seeded-loader demonstration, not by reasoning about it.**

---

## 7. `--goon-demo` — BUILT, through all four sites, plus a fifth (D265)

| Site | File |
|---|---|
| parse | `Program.cs`, beside `--intake-demo` |
| thread into `BuildAvaloniaApp` | `Program.cs` |
| thread into `new App(...)` | `Program.cs` |
| consume | `App.axaml.cs` — `dashboard.Opened += (_, _) => dashboard.Goon.Practice();` |
| **classify (the fifth)** | `Lifecycle/HarnessEntryPoints.cs` — **granted at the plan gate** |

**The parse lives in `Program.cs` on purpose.** Nothing mechanically requires it there rather than
in `App.axaml.cs`, and the D259 shape — a dial nothing can turn — would pass every test in the
suite. The flag reaches the SAME `GoonLaunch` the PRACTICE button reaches; no `MainWindow` edit was
needed because `MainWindow.Goon` is already public.

**It carries no `--goon-drive` and no `--goon-auto-close`, deliberately.** The headed evidence here
is taken through real input, which is stronger, and either modifier would be a `Harness` entry point
rather than a `Demo` one.

**The fifth site is SP-130 §7.1's root cause recurring**: `HarnessEntryPoints.cs` and
`assets.manifest.json` are both organised by CONCERN while packets are scoped by FEATURE, so a
feature-scoped File Scope will miss them every time.

---

### 7.1 `--goon-demo` HAS NO END-TO-END COVERAGE

Stated plainly because the chain being complete is not the same as the chain being exercised. The
flag is pinned only by CLASSIFICATION — `HarnessEntryPointGuardTests` proves the literal is
registered, and `HarnessEntryPointGateTests` proves a `Demo` entry is not refused without
`CCP_DATA_ROOT`. **No test and no capture ever launches with it.** `capture.ps1` deliberately drives
the real user path instead (Play door, then PRACTICE), which is the stronger evidence and the reason
the flag was not needed for rung 2 — but the consequence is that **the flag itself is exercised by
nothing**, and a break in the `Program.cs` -> `BuildAvaloniaApp` -> `new App(...)` -> `Practice()`
chain would be found by a person running it, not by this suite.

## 8. Discrepancies found, and how each was resolved

| Found | Resolution |
|---|---|
| The packet says to capture "the page actually rendered". The first capture caught the explainer card **by timing luck** — the raise-and-park sleeps happened to exceed the 420 ms auto-open | Made deterministic and named honestly: `-State first-run`, gated on `modal=open`. §3.3 |
| I claimed clearing the WebView2 profile is what makes it deterministic | **Measured and withdrawn.** A run with the clear skipped still reported `modal=open`; the ephemeral origin is the real reason. The clear is now best-effort and the comment says so |
| The plan proposed `boot.js:185` for `session.ready` | Corrected to `:166` at the plan gate; the fact was right, the number was not |
| The plan cited `title.js:28` for the logo | Corrected to `:31`; `:28` is the brand comment |
| The plan said no test reads `verification-harness.md` at runtime | **False, and the conclusion survives anyway.** `WarningGateGuardTests.cs:239` does `File.ReadAllText` on it — but asserts only that it contains the `check-warnings.mjs` invocation string, so the "eight checks today" enumeration at `:50` is unguarded and a goon check reds nothing. **That line is now stale and the doc is outside File Scope — reported, not edited** |
| `VacuousShapeGuardTests` flagged three new facts (`fs-predicate`, `assertions-all-nested`), and its ledger is in must-not-change | **Fixed the SHAPE, not the ledger**: existence predicates moved into named helpers that return the offending FILES, and each fact given a top-level assertion. The facts are stronger for it — a failure now names which file was missing instead of reporting a bare false |
| `SoundArbitrationTests` failed twice in one floor run | **Not mine and not reproducible**: 52/52 in isolation and green in the next full floor run. Load flakes from the concurrent headed runs. Reported rather than absorbed |
| `capture.ps1`'s `Get-Window` finds a window by process id ALONE, which is ambiguous the moment a process has two top-level windows | The goon phase looks up BY NAME and closes by its own handle; **the four landed captures' path is left exactly as it was**, because it cannot be re-verified from inside this packet. The orchestrator holds the row |

---

## 9. Floor

Pin **2547 unit / 152 headless**. Declared delta **+18 unit / +0 headless**
(`floor-delta.json`). Observed at the final run: **2565 unit / 152 headless** — that is
`2547 + 18` and `152 + 0`, exactly, with **zero named failures in either project**.

**The delta moved from 17 to 18 at code review**, when the admission surface (§5.1) was measured and
needed a fact rather than a sentence. The `_pageStateInFlight` fix added none, for the reason given
in §6.
**`client/tests/floor/floor.json` was never opened.** The warnings gate is 0 warnings / 0 errors
across all four projects, forced non-incremental.

---

## 10. Files changed

- **New:** `client/tests/CcpClient.Tests/GoonServingTests.cs` (17 facts)
- **Product:** `Features/Goon/GoonParticipant.cs` (the borrow, D266), `Features/Goon/GoonHostWindow.axaml(.cs)` (the probe seam, D267), `Features/Goon/GoonLaunch.cs` (class doc corrected — the flag it called unbuilt now exists), `Program.cs` + `App.axaml.cs` (`--goon-demo`), `Lifecycle/HarnessEntryPoints.cs` (**the one granted line**)
- **Harness:** `client/tools/verify/capture.ps1` (the `goon-page` phase), `client/tools/verify/checks.json` (two checks)
- **Docs:** `client/docs/wpf-surface-reachability.md` (D265-D274 only; **D259 superseded, never edited in place**)

---

## 11. WHAT THIS DOES NOT PROVE

- **A duel was not played.** Not attempted, not claimed, and owed to a person.
- **The bare title menu is uncaptured.** Only the first-run explainer state was photographed.
- **Linux is untouched.** The typed unsupported surface is still justified only by
  `DtrhCapabilityProbes.cs:35-43`; the WSLg/X11 run that would observe it was not made.
- **Nothing here exercises audio, the fullscreen echo, the duck/restore under a different window
  manager, the watchdog recovery path, or `net-post` refusal in front of a live page.** The twelve
  restored assets are proved to SERVE; that the page now plays the pop sound is **not** proved —
  hearing it is a human gate.
- **The D250 microphone residual is still open.** The capture walks around the voice screen
  deliberately. Its permission-prompt detector is best-effort: whether WebView2 projects that
  prompt into the host UIA tree at all is unverified, and the harness says so on every run.
- **The harness guards in `GoonServingTests` §6 are LEXICAL.** They prove the gate has not been
  deleted or moved below the capture. They cannot prove it still bites — §4 is what proves that,
  and only for the states listed there.
