# SP-132 — plan checkpoint (Review Level 3, BEFORE any product edit)

Branch `lane/SP-132-goon-evidence`, base `79d08a27b` (wave-66 lock), own worktree
`.claude/worktrees/agent-ae614e0827c72c4a0`. Pin **2547 unit / 152 headless**. `floor.json` never opened.

Nothing under `client/` has been edited at this checkpoint. This file is the only change in the tree.

---

## 0. What I read before planning

`spine-tasks/SP-130-goon-practice-host/record.md` in full (its §5 evidence-class table and §7 blockers
set the standard this packet has to match), the PROMPT, and then the source:
`Features/Goon/{GoonParticipant,GoonServingRoots,GoonDoors,GoonLaunch,GoonProtocol}.cs`,
`GoonHostWindow.axaml(.cs)`, `Features/Dtrh/LoopbackServer.cs`, `Program.cs`, `App.axaml.cs`,
`Views/MainWindow.axaml.cs`, `Views/Pages/PlayPage.axaml(.cs)`, `Lifecycle/HarnessEntryPoints.cs`,
`tools/verify/capture.ps1`, `tools/verify/checks.json`, `tools/verify/CcpVerify/*`,
`tests/CcpClient.Tests/{IntakeServingTests,GoonPracticeTests,RackPresentationTests,HarnessEntryPointGuardTests,HarnessEntryPointGateTests}.cs`,
and — as read-only evidence — `Resources/web/goon/{index.html,goon.css,boot.js,bridge.js,ui/screens/title.js,ui/screens.css}`.

---

## 1. Rung by rung: what each piece of work targets, and the evidence class it produces

| # | Work | Rung | Evidence class produced | What it will NOT prove |
|---|---|---|---|---|
| 1 | `GoonServingTests.cs` — real HTTP GETs at the bound goon origin | **1 — the origin SERVES** | in-process socket + byte assertion. No browser, no monitor, no lease | that any browser fetched them; that a page parsed them |
| 2 | `--goon-demo` through all four sites | none by itself | a launch path, not evidence | nothing. It is a dial, and it is BLOCKED (§4) |
| 3 | Goon probe line on `GoonHostWindow` + `goon-page` surface in `capture.ps1`/`checks.json`, real headed capture | **2 — the page LOADS, the handshake COMPLETES** | `presentation-verified` for the pixel; the handshake half is proved by the page's OWN object graph read back through `InvokeScript` before any pixel is taken | **that a duel is played.** Rung 3 is a human gate, is not attempted, and is named as owed |

**The three rungs are not equivalent and I will not blur them.** Serving bytes is not loading a page;
loading a page is not playing a duel.

---

## 2. Rung 1 — the in-process GET (FIRST, banked before the harness is touched)

New file, the only new test file the scope allows: `client/tests/CcpClient.Tests/GoonServingTests.cs`.
Fixture shape copied from `IntakeServingTests` (real `LoopbackServer`, `HttpClient`,
`LoopbackListenerRegistry.RegisterLoopbackServer`, `TestContext.Current.CancellationToken`), but over the
**real `GoonParticipant`** against the **real `payload/goon` build output**, with an explicit temp
`dataDirectory` so `CompositionRoot.DefaultSettingsPath()` and the real profile are never touched.

Facts (assert on CONTENT, never a status code alone):

1. **The document serves with its real bytes.** `GET /dtrh/index.html` -> 200, `text/html; charset=utf-8`,
   body **byte-equal to the file on disk**, and contains `src="./boot.js"` (`index.html:96`).
2. **The three required boot files are retrievable**, byte-equal: `index.html`, `boot.js` (133 KB — proves
   the body really streams, not just headers), `bridge.js` (`GoonServingRoots.RequiredFiles`).
3. **Assets that are not the document serve** — the packet's explicit requirement: `goon.css`
   (`text/css`), `ui/screens.css`, `assets/goon_game_logo.png` (200, `image/png`, PNG magic bytes
   `89 50 4E 47` — a content assertion, not a length one). The logo is the file
   `ui/screens/title.js:28` names, so it is on the path the capture will photograph.
4. **The URL the host actually navigates to serves.** `GET participant.PageUrl()` verbatim, bridge-token
   query and all, -> 200 + the index bytes. The `/dtrh/` route class is the reused server's, not a DTRH
   page (`GoonParticipant.cs` PageUrl).
5. **The refused files answer 415 AT THE WIRE, and they exist.** All four D258 files —
   `manifest.webmanifest`, `vendor/mp4-muxer/mp4-muxer.mjs`, `vendor/fflate/LICENSE`,
   `vendor/mp4-muxer/LICENSE` — asserted `File.Exists` first (so 415 is a REFUSAL, not a 404) and then
   415 with the body `refused: extension outside the pinned allowlist` (`LoopbackServer.cs:446-450`).
6. **The effective allowlist is DERIVED FROM BEHAVIOUR, not restated.** Sweep the output tree, group by
   extension, GET one representative per extension, and assert the served/refused partition equals the
   four named files. This closes the substance of board row 333 item (2) — *"the allowlist read from its
   source rather than restated"* — **without touching `GoonPracticeTests.cs`** (must-not-change): a
   change on the SERVER side now moves an observed status code, so it reds here. I will say plainly in
   the record that the literal in `GoonPracticeTests` still exists and is now cross-checked, not removed.
7. **§4 discipline holds on THIS origin** (it is a third, separately-constructed server): POST -> 405;
   traversal -> 403; unknown path -> 404; route outside `/dtrh/` -> 404; `/health` -> 200.
8. **The bridge inbox route is token-guarded**: wrong token -> 404 with the fixed route class; correct
   token `?after=0` -> 200 JSON. SP-130 records this route as present-but-unused; "unused" and
   "unguarded" are different facts.

Estimated ~10-14 `[Fact]`s. Exact count goes into `floor-delta.json` (`headless: 0`).

**No wall-clock waits anywhere**: every step is an awaited HTTP call. `TestWait` is not needed and no
`Thread.Sleep`/`Task.Delay`/tick poll will appear.

---

## 3. Rung 2 — the headed capture, SP-122's shape

### 3.1 Confirm state through UIAutomation BEFORE any pixel

`GoonHostWindow` gains ONE new UIA-readable diagnostic element (the `MainWindow.axaml.cs:288-294`
`ProbeLine` pattern, `AutomationId="GoonProbe"`, also logged once to diagnostics so the WSLg/stderr path
can read it later). Content-free. One line:

```
goon-probe: surface=embedded nav=success heartbeats=17 page ready=true solo=true name=Player
            canhost=false images=0 screen=title | page-rect 900.0x700.0 DIP @ scale 1.75 @ screen 121,223
```

Every field has a source, and **each is a failure this packet must be able to type** (trap 2):

| Field | Source | Typed failure it makes visible |
|---|---|---|
| `surface=` | `GoonHostWindow.Surface` | `payload-missing` / `unsupported` — a MISSING WEBVIEW2 RUNTIME lands here (`DtrhCapabilityProbes.ProbeEmbedded` returns `DependencyMissing`) and the harness refuses by name |
| `nav=` | `NativeWebView.NavigationCompleted.IsSuccess` | **a FAILED NAVIGATION** — the harness refuses instead of photographing an error page |
| `heartbeats=` | existing `_heartbeats` (`boot.js:2587` = one every 2000 ms) | a page that loaded and then wedged |
| `ready= solo= name= canhost= images= screen=` | **one `InvokeScript` evaluated when the page's own `boot ok` log frame arrives** (`boot.js:431`, emitted right after `openFirstScreen()`), reading `window.__gg.session` (`boot.js:2645-2664`) and `document.documentElement`'s `data-gg-screen` (`ui/router.js:222`) | the handshake did not complete |

**Why the page's object graph and not the host's own "I sent it".** `session.ready` is literally
*"init + manifest both in"* (`boot.js:185`) and is set only by `settle()` (`:415-418`). `session.solo`,
`identity.displayName` and `caps.canHost` are values THIS host computed and posted, read back out of the
page. That is what "answered and acknowledged" means, and it is why `BootMessagesSent` alone would not do
— that only proves the host spoke. If `boot ok` never arrives the probe reads `ready=false screen=-` and
the harness fails naming it: **loud, never a silent pass.**

### 3.2 The capture

`capture.ps1`: `-Surface` `ValidateSet` gains `goon-page`; `-State` gains `title`; `$statesFor` gains
`'goon-page' = @('title')`. Both additions are required by
`RackPresentationTests.EveryManifestSurfaceIsOneTheCaptureScriptAccepts` and
`...EveryManifestSurfaceStatePairIsOneTheCaptureScriptCanDrive`, which sweep the WHOLE manifest.

Drive, in order — **all real input, no flag needed** (this is why §4's blocker does not block rung 2):

1. Take the machine-wide lease (`%TEMP%/ccp-real-desktop.lease`) — unchanged, it already wraps the launch.
2. Launch, poll to a deadline for the shell, raise topmost — unchanged.
3. Click the **Play** rail door (`Get-DoorRect`), `Assert-Route play`.
4. Click **`GoonPracticeButton`** (a real `Button`, so a real UIA peer with `AutomationId`).
5. Poll to a deadline for a SECOND top-level window of the same pid whose Name matches `Goon Game`
   (the dashboard is minimized by the duck at that point — `GoonHostService.cs:20-23` parity), raise it
   topmost.
6. **Read `GoonProbe` and assert every field**, refusing by name on each wrong value.
7. `Assert-Inside` the page rect against the goon window rect (the SP-122 clipping rule).
8. Park the cursor off every interactive surface, `DwmFlush`, `CopyFromScreen` the page rect.
9. Close the goon window first (it cancels its own close once for the `end-run`/`exit-done` handshake,
   `boot.js:2437-2465`), poll for it to go, then close the dashboard; assert exit 0. Release the lease.

**The microphone residual (D250) is walked around, deliberately.** No menu item is ever clicked, so the
voice screen is never reached. The harness additionally fails if any UIA element inside the goon window
carries the standalone word `Allow` — WebView2's own permission bar wording, absent from every string
this window renders. **Named limit, not a claim:** whether WebView2 surfaces its prompt into the host UIA
tree at all is unverified, so this is a best-effort detector; if a prompt appears in the PNG it goes into
the record as evidence, exactly as the packet requires.

### 3.3 `checks.json`

`goon-page` / `title`, `presentation-verified`. Region: a `region-color` rect in the **top strip, centred**
— the body radial gradient (`goon.css:56-58`, `#2a1030` at stop 0) with no title-card content over it. The
expected colour and both fractions will be **MEASURED off the real captures and then pinned**, never read
out of the CSS — SP-122's rule, and the reason for it is that a gradient's value at a sampled rect is not
its stop colour. The tolerance will be set strictly below the distance to the nearest OTHER real state of
that rect (the loader's flat `#0d0716`, `goon.css:225-229`, is 29/9/26 away — the red channel alone is
29, so a tolerance in the low teens rejects it by a wide margin), and I will state both fractions.

**If the two states do not separate by enough to make a check bite, I will not ship the check.** A check
that cannot fail is the defect this project has found fourteen times, and shipping one here would be the
fifteenth.

### 3.4 Every check demonstrated to fail on a capture of the WRONG state

Three demonstrations, all at the committed head, all with the SHA, all reverted:

| # | The wrong state, and how it is produced | What must go red |
|---|---|---|
| A | **`payload/goon` renamed in the build output.** No source seed at all: the product's own typed `payload-missing` surface renders (`GoonHostWindow.Begin`), flat `#0b0b16` + the honest text, at the same rect | `capture.ps1` refuses at `surface=payload-missing` **before any pixel**; and the PNG taken of that rect scores ~0.000 against `goon-page/title` |
| B | **The `manifest` send suppressed in `SendBootMessages`** (one seeded line, reverted). The page then never settles (`boot.js:415-418`), the loader stays and at 45 s becomes the loader FAILURE | `capture.ps1` refuses at `ready=false screen=boot`; the loader PNG scores ~0.000 against `goon-page/title`. **This is the demonstration that matters most: it is the "silently photographed an error page" outcome, produced on purpose and caught** |
| C | An existing non-goon capture (`windows-dashboard-unselected.png`) run through `CcpVerify --surface goon-page --state title` | non-zero exit naming the check |

For A and B the PNG is produced by a throwaway capture script in the scratchpad (same launch/rect, without
the `GoonProbe` assertion) so the committed harness keeps its refusal intact; the script's text goes into
`record.md`. If the reviewer prefers a first-class second state in `$statesFor` instead, say so and I will
build it — I did not, because neither wrong state is drivable deterministically from the harness without
either a new harness flag (out of scope, §4) or destructive build-output edits inside the drive path.

---

## 4. BLOCKER — `--goon-demo` needs exactly one line outside File Scope

This is SP-130 §7.1's root cause recurring, and I am reporting it rather than crossing it.

The flag's four sites are all in scope: `Program.cs` parse (beside `--intake-demo` at `:230`),
`Program.cs` -> `BuildAvaloniaApp` call (`:252-256`), `BuildAvaloniaApp` -> `new App(...)` (`:321-336`),
and `App.axaml.cs` (field + ctor param + the `dashboard.Opened` launch, mirroring `_intakeDemo` at
`:283-320`).

**The fifth site is not.** `HarnessEntryPointGuardTests.EveryStartupFlagLiteral_IsClassified_AndTheGateRidesTheRealEntryPoint`
sweeps every `"--..."` string literal under `client/src/**` and reds any not classified in
`client/src/CcpClient.Desktop/Lifecycle/HarnessEntryPoints.cs`. Adding the flag therefore requires:

```csharp
new("--goon-demo", EntryPointDisposition.Demo),
```

one line in the Class-2 DEMO block of that file — **which is outside this packet's File Scope.**

- Classifying it `Demo` (not `Harness`) matches every sibling `*-demo` flag and does **not** move
  `HarnessEntryPointGateTests.HarnessRegistryCount_IsPinnedAtEight`, which pins the eight Class-1 entries
  by name. I checked. The diff is one line and one line only.
- There is no in-scope way to write the flag without that literal, and writing it obfuscated to dodge a
  guard would be routing around a finding.

**Request: a grant for that one line.** Without it the honest outcome is the same as SP-130's — the flag
stays unbuilt and the record says so — because **a partially wired flag is a worse outcome than none**.
Note that rung 2 does **not** depend on it: the capture drives the real user path (Play door -> PRACTICE),
which is better evidence than a flag.

---

## 5. What I will do if the page does not boot

Named in advance, so there is no room to improvise a pass:

| Outcome | Response |
|---|---|
| WebView2 runtime absent | `surface=unsupported` in the probe; capture REFUSES by name; **rung 2 is not claimed**, the capability state is quoted verbatim into the record, and rung 1 still lands |
| `NavigationCompleted.IsSuccess=false` | `nav=failed`; capture REFUSES; the failure code goes in the record as a finding. **No retry loop, no workaround** |
| Page loads but never settles (`ready=false`) | capture REFUSES; the loader-failure text and the host's diagnostic transcript go in the record. This is a **finding about the page or the handshake and is worth more than a workaround** |
| A permission prompt appears | recorded as evidence — it is the D250 residual made visible — with the PNG kept |
| A stale-profile-lock (0x800700AA) | the product already recovers once (`GoonHostWindow.BeginEmbedded`); if it recurs, reported, not retried around |
| No interactive desktop / lease contended | capture refuses (it already does); rung 2 reported as **not reached**, rung 1 still landed |

In every one of those rows the packet outcome is *"rung 1 reached, rung 2 not reached, here is exactly
why"*. **Swallowing any of them is the failure mode this packet exists to avoid.**

---

## 6. Guards, and the edit each must red on

New facts also go in `GoonServingTests.cs` (the only new test file in scope; they are pure logic — reading
`checks.json` and `capture.ps1` off disk, the `RackPresentationTests` precedent):

| Guard | Reds on |
|---|---|
| every `goon-` check claims `presentation-verified` | demoting one to `draw-verified` (which is what would let a headless frame claim it) |
| the goon surface+state is one `capture.ps1` accepts AND can drive | removing `goon-page` from either `ValidateSet` or `$statesFor` |
| the goon check's tolerance is strictly less than its distance to the loader's `#0d0716` | widening the tolerance past the colour it exists to reject |
| `capture.ps1`'s goon phase asserts the probe BEFORE `CopyFromScreen` | moving the pixel read above the state confirmation |
| the four denied files 415 at the wire (§2.5/2.6) | adding an extension to `LoopbackServer`'s allowlist, or a new upstream extension |

Each will be watched RED at the committed head and then reverted, with the SHA, in `record.md`.

---

## 7. Discrepancies and discoveries already found (before any edit)

1. **`client/docs/verification-harness.md:50`** says *"The eight checks today are: …"* and enumerates them.
   A goon check makes that line stale. **The doc is outside File Scope** and no test loads it at runtime
   (verified: every reference to it in `client/tests/**` is a doc comment). Reported, not edited.
2. **`SP-130` record §7.2.2**: `client/docs/upstream-payload-inventory.json` types `goon` as `not-ported`
   and this packet makes it *served and loaded*. Still outside scope; still held by the orchestrator.
3. **Board row 333 item (2)** asks for the allowlist "read from its source". Reading
   `LoopbackServer`'s `private static readonly Mime` by reflection was considered and rejected in favour
   of §2.6's behavioural derivation, which is strictly stronger: it observes what the server DOES.
4. **`GoonDoors.cs`'s refusal text will not be touched.** I read it in full first, as instructed. Nothing
   in this plan needs it changed.
5. `capture.ps1` currently finds its window with `FindFirst(Children, pid)`, which is ambiguous the moment
   a process has two top-level windows. The goon phase will look the window up by NAME; the existing
   phases are left exactly as they are.

## 8. Divergences

`client/docs/wpf-surface-reachability.md`, **D265-D274 only**, appended to the SP-130 block. Candidates:
the origin-serves evidence and what it is not; the probe seam (upstream has no such line); the
harness-driven real-input path vs upstream's; the `--goon-demo` blocker; the wrong-state demonstrations;
and whatever rung 2 turns up. Ids will not stray outside the range.

## 9. Floor

`spine-tasks/SP-132-goon-evidence/floor-delta.json`, `unit: <exact count>`, `headless: 0`. Expect the run
to report **2547 + unit**, which is pin + delta and is not a failure. `floor.json` will not be opened.

---

**STOPPING HERE for the plan verdict, per Review Level 3. No file under `client/` has been touched.**
The one decision I need back is §4: **grant the one line in `HarnessEntryPoints.cs`, or `--goon-demo`
stays unbuilt and the record says so.**
