# SP-058 — Graded Intake v6.7.x delta: record

Wave-16 lane-2 (serial after SP-057). Review Level 2. Contract: verify.mjs + build 0W/0E + both test projects (floor 847/33, the SP-057 amendment). Enabler 2: the three hot docs are NOT worker-edited; orchestrator reconciles at land.

**NEW BASELINE STATED EXPLICITLY: v6.7.4 — upstream main tip `0c9947a6` (2026-08-11), merged into the port branch at merge commit `42286638`.** Previous baseline: v6.6.3 `b35facb6` (SP-054's internally-consistent line). The next sync starts from `0c9947a6`.

## Step 1 — delta archaeology (the tree is the authority; `git diff b35facb6 0c9947a6`)

Ledger hypothesis vs tree truth (the predicted-list-wrong precedent held a THIRD time — SP-037/SP-055/now):

| Path | Delta | Ledger named it? |
|---|---|---|
| `Resources/web/intake/core/accents.js` | **NEW, +350** | yes |
| `Resources/web/intake/core/ai.js` | **+79/−22** | yes |
| `Services/Quiz/IntakeHostService.cs` | **+83** | yes |
| `Services/Quiz/QuizService.cs` | **+15** | **NO — the tree-only element** ([AI-METER] log-only sizing in the classic collapsed quiz's own `CallAiAsync`; :662-712) |

Widened sweep (pre-approach consult discharge — the ledger's scope is two dirs; the intake SURFACE is wider): `ChaosWebViewHost.cs` unchanged; `IntakePassService.cs` / punch card unchanged; the borrowed dtrh-tree assets (`vendor/three/*`, `assets/bubbles/sfx/chime*.mp3`) unchanged; `Services/GamificationBridge.cs` **+157** — the CONSUMER side of the delta's `RaiseQuizCompleted` (the "Quiz" section renamed "Graded runs", a source-agnostic `OnQuizCompleted` handler; `top_of_the_class` at the 90% bar, `honor_roll` over distinct categories, `held_back` deliberately fail-streak-only). It confirms the emit's semantics without changing the intake host's obligation (greenfield has no achievement subsystem — typed seam). Achievement models/popups/tabs changed alongside — subsystem-side, not the intake surface. Release notes: v6.7.1/v6.7.2 files exist; v6.7.3/v6.7.4 shipped without notes (the sync ledger already records this).

### Obligation table (framing a: derive, don't port)

| # | Delta element | Verdict | Citation | Sizing |
|---|---|---|---|---|
| 1 | `accents.js` (new payload file) | **SERVE** (it is a static ES module the page imports; the host's only job is resolving it) + **NOTHING** for provisioning (curated in-page pools; no settings, no bridge messages, no storage) | accents.js:1-27 (header: pools picked in-page, never network); ai.js:24 (`import { createAccentPicker } from './accents.js'`) | S |
| 2 | `ai.js` rework | **NOTHING** (payload-internal). Every `want='accent'` request is answered locally in EITHER mode (ai.js:63); the remote path remains for `want='synthesis'` only, which has no client call sites. The host's init provisioning `ai={serverBase, authToken:""}` is unchanged and still exactly what the page expects — accents-never-network makes SP-054's empty-token local path the PRODUCTION path, strengthening the logged-out-parity framing. The STRUCTURE_RE/usableLine guards (ai.js:158-174) are page/server-side belt-and-suspenders | ai.js:60-65, :155-174, :194-228 | zero code |
| 3 | `TopMarksPercent = 90.0` + perfect verdict | **PORT the derivation as a computed verdict + typed seam** (PROMPT framing c): `pct = MaxScore > 0 ? TotalScore/MaxScore*100 : 0`; `perfect = MaxScore > 0 && pct >= 90.0`; `category = niche` normalized (whitespace → bambi fallback, trim, lower-invariant). No greenfield GamificationBridge — the SP-054 "XP computed, not granted" class | IntakeHostService.cs:45-53 (const + why-not-100 comment), :414-422 | S |
| 4 | `QuizService.RaiseQuizCompleted(...)` emit (#870) | **typed seam** (with #3): no achievement subsystem greenfield; score/passed(true)/perfect/category computed + log-lined, never raised. `held_back` deliberately unwired upstream too (an intake has no fail state) | IntakeHostService.cs:396-422; GamificationBridge.cs:540-586 (the consumer's semantics) | with #3 |
| 5 | Mantra-credit loop `TrackMantraCompleted()` × min(affirmed,5) | **typed seam**: no quest/program verifier greenfield; the capped count rides the same computed log line | IntakeHostService.cs:435-441 | with #3 |
| 6 | `IsAssetActive` + `BuildDisabledAssetSet` + enumeration gate | **ALREADY LANDED via SP-055 — verified, not re-implemented** (framing b). Residual gap: NONE | below | verify only |
| 7 | `QuizService.cs` +15 [AI-METER] | **NOTHING for the intake host**: it meters the classic collapsed AI quiz launcher's own transport (`CallAiAsync`), an unported surface; belongs to a future AI-subsystem row if ever filed | QuizService.cs:662-712 | zero code |

### SP-055 reuse verification (framing b — consume, never re-implement)

`IntakeHostWindow.axaml.cs` SendBootMessages passes `disabled: DtrhUserMedia.BuildDisabledSet(_context.AssetSelectionStore.Current.DisabledAssetPaths), useWhitelist: …` into `IntakeMediaManifest.Build` → the ONE active-pool definition in `DtrhUserMedia`. Semantics identical to the upstream delta: normalization (Replace `\`→`/`, OrdinalIgnoreCase — IntakeHostService.cs:776-781), empty-set short-circuit (:784), unrelatable-path `true` (:789), whitelist gate (AppSettings.cs:1637 contract), skip-vs-deselect distinction, both-folders bound. Extension sets identical both sides (gif/png/jpg/jpeg/webp — upstream :802-804 filter vs `DtrhUserMedia` image list). **No second definition exists; none was added.** SP-055's headed runs E/F already proved the intake consumer end-to-end (deselected asset never provisioned, pool-of-N discriminator).

## Step 1 — pre-approach consult

**Mode:** solo (T-7; PROMPT Do-NOT — council never used). **Requested route:** the runtime default (the PROMPT's "Opus 5 main / Fable 5 fallback" is not settable through the consult tool's parameters). **Actual answering model:** NOT surfaced by the consult tool response (no model identity header — the SP-022…027/049…057 provenance discipline, recorded honestly). **Verdict: obligation table SOUND with binding discharges — all executed:**

1. **Serve-probe shape APPROVED + negative control (adopted):** the running host GETs its own loopback for the payload-art path and logs status/bytes/sha256 (route-class logging redacts filenames, so the page's own fetch can never name the file); the probe MUST include a 404 on a nonexistent sibling (`core/accents-missing.js`) proving a 200 is a real resolution, not a masked miss. Also confirmed: a missing accents.js is never silently masked — as a static ES-module import it kills the whole module graph (the page dies loudly).
2. **Computed verdict + log line APPROVED; the verdict must NOT ride the drafted session document** (upstream doesn't put it there; no consumer validates the schema — adding a field would invent schema). Static helper + log line; no interface, no document member.
3. **Guard ported verbatim:** `MaxScore > 0 && pct >= 90.0` even though the `pct` else-branch makes the guard arithmetically redundant — the port keeps upstream's exact comparison.
4. **Boundary tests use exact doubles:** 9/10 = exactly 90.0 (true); 8.999/10 = 89.99000000000001 (false — must NOT round up); zero-max (false, pct 0.0).
5. **Widened enumeration (executed above):** GamificationBridge +157 is the consumer side; ChaosWebViewHost / pass / punch / borrowed dtrh assets unchanged.
6. **Probe extension:** required-file list as a named constant; the Incomplete honest surface NAMES the missing file.
7. **Serve-probe must be async** (no UI-thread HttpClient deadlock) — implemented as a detached task off the drive timer.
8. **Category normalization verbatim** (`Trim().ToLowerInvariant()`, whitespace → `IntakeNiche.Fallback`) — honor_roll's distinct-category counting must never split on case/padding.

## Step 1 — engine-review presence (T-2)

Step-1 plan review call → `skipped: true, spawnFailed: false` (artifact `.reviews/1-20260812T074241.md`) — "Nested reviewer spawn blocked inside pi worker session... the batch engine runs reviews after worker success (SP-195)". Engine review ABSENT in-worker by design; code + final reviews run on the engine after .DONE.

## Step 2 — implementation (build 0W/0E; 862/862 + 33/33 green, floor 847/33; verify.mjs exit 0)

**Files (all in File Scope):**
- `Features/Intake/IntakeQuizRun.cs` — new `IntakeGraded` static ( obligation #3-5 ): `TopMarksPercent = 90.0` (with the upstream why-not-100 comment), `ScorePercent` (MaxScore-guarded), `IsTopMarks` (the comparison verbatim), `Category` (niche normalization verbatim), `MantraCreditCount` (min cap 5). Each member carries its IntakeHostService.cs citation.
- `Features/Intake/IntakeHostWindow.axaml.cs` — the graded-verdict log line in `OnQuizResult` between XP and the pass spend (upstream order: the raise block precedes XP at :396-422 — recorded; the greenfield line is sequenced after XP for diff-minimality, both precede the spend; the ORDER upstream binds is raise-before-XP, adopted here as compute-adjacent — the verdict has no side effects, so the sequencing is observational only); the payload-missing honest surface now NAMES the missing required file; the HARNESS-ONLY `serve-probe:<path>@t` drive step (async GET against the running host's own loopback, status/bytes/sha256 logged).
- `Features/Intake/IntakeServingRoots.cs` — `RequiredFiles = ["index.html", "core/accents.js"]` named constant; the probe types `Incomplete` with `MissingFile` naming the first absent required file (framing d: a copied file is not a served file; a MISSING required file is never a silent 404).
- `Features/Intake/IntakeHarness.cs` — the `quiz-result:topmarks` boundary drive step (totalScore 9 / maxScore 10 = exactly 90.0).
- Tests: `IntakeGradedTests.cs` (NEW, 11): the bar constant, exactly-90.0 true, 8.999/10 false (no round-up), zero-max false + pct 0.0, category normalization matrix, mantra-credit cap. `IntakeServingTests.cs` (+1): `/dtrh/core/accents.js` → 200 with the overlay tree's bytes through a REAL LoopbackServer. `IntakeHostSupportTests.cs` (probe matrix extended): missing index.html names `index.html`; index-only tree names `core/accents.js`; both present → Present with null MissingFile.

**Serving verification (consult discharge — "serves mechanically" needed proof):** the built output tree carries `payload/intake/core/accents.js` and its sha256 (`855a8ce6fefb2593…`) is BYTE-IDENTICAL to the READ-ONLY trust anchor `ConditioningControlPanel/Resources/web/intake/core/accents.js`; the SP-009 manifest entry (`intake.payload/core/accents.js`, required, copied) and the 3682 count pin already landed at the SP-054 land (the ledger's serving-half claim verified true against the tree).

**NOTHING verdicts shipped as zero code (framing a):** accents.js provisioning (payload-internal pools), the ai.js rework (the host's `ai={serverBase, authToken:""}` provisioning stands unchanged), QuizService.cs [AI-METER] (not the intake surface). `upstream-payload-inventory.json` unchanged (no disposition or file-count change — the intake tree's 2138 already counts accents.js from the SP-054 land); the SP-056 guard schema untouched.

## Step 2 — engine-review presence (T-2)

Step-2 plan review call → `skipped: true, spawnFailed: false` (artifact `.reviews/2-20260812T074459.md`), same SP-195 class.

## Step 3 — host-proven evidence (headed, Windows, under the SP-057 seam)

**The seam's first consumer (framing e):** `CCP_DATA_ROOT` set absolute/fully-qualified for every headed run (including the debug launches); `data-root override active: CCP_DATA_ROOT -> …` is the run.log's first line. The bracket reuses SP-057's `manifest.ps1`/`diff.ps1` byte-for-byte (copied into `evidence/`); `run.ps1` is the SP-057 shape adapted to the intake drive.

**The run (`evidence/run-drive.log`, `run.log`, `run-host-live.png`; SCRIPT_EXIT=0):**
- **Byte-identity:** pre/post manifests of the real `%APPDATA%\CcpClient` — `DIFF VERDICT: BYTE-IDENTICAL (2677 files, set-equal both directions, all hashes match)` (`diff-verdict.txt`). Claim scope: `%APPDATA%\CcpClient` ONLY (WebView2/LibVLC may write under LocalAppData/Temp — stated, not swept).
- **Positive controls (all True):** override root populated (216 files — intake_settings.json, intake_punchcard.json, intake/drafted_sessions/, the WebView2 profile); run.log carries the override line, `intake: init sent`, both serve-probe lines, the graded-verdict line; the boundary pin matched exactly.
- **Serving proven by the running host (framing d):** `intake: serve-probe GET /dtrh/core/accents.js -> 200 (13580 bytes, sha256 855A8CE6FEFB…)` — the sha256 prefix MATCHES the trust-anchor file hash — and the negative control `GET /dtrh/core/accents-missing.js -> 404`. Request/response evidence from the real loopback, never a directory listing.
- **Top-marks boundary end-to-end (framing c):** drive `quiz-result:topmarks@22` (9/10) through the REAL parse+dispatch → `intake: graded verdict — top-marks True (90% of max; category bambi; mantra credit x1) — achievement/quest raises are typed seams`, followed by the real completion loop (pass Spent, Hard draft `runnable:false`, first-hole-free punch + 1 pending). The below-bar and zero-max cases are unit-pinned.
- **Headed-ness:** `run-host-live.png` — the page PAINTED (the onboarding card over the spiral backdrop, status `intake: init sent`), rect-verified at the placed point (100,100) 2472x1522 physical (the axaml 85%-of-screen size at the 1.75 raster scale). **DISPLAY3 was ABSENT this session** (only DISPLAY1 2880x1800 attached) — the amendment's fallback disposition applied: probed `Screen.AllScreens`, fell back loudly to a visible DISPLAY1 point, recorded in the transcript; never captured black at an unattached origin.

**Surprises (recorded, fixed in `run.ps1`/`capture-intake.ps1` with comments):**
1. `Start-Process -ArgumentList` joins array elements with spaces UNQUOTED — the spaced drive step list arrived as separate args and silently truncated the script to its first step. Fix: the drive string carries no spaces.
2. SP-057's `drive.ps1` filters EnumWindows on `IsWindowVisible` — the intake window reports `vis=False` to the OS enumerator while painted (the SP-054 recorded quirk), so it can never be found that way. SP-058's `capture-intake.ps1` follows SP-054's no-visibility-filter pattern.
3. The first capture script passed `SetWindowPos` cx=cy=0 with only SWP_NOMOVE (0x0002) — without SWP_NOSIZE (0x0001) that SQUASHES the window to its minimum (232x64), and every retry then read the squashed rect. Fix: SWP_NOSIZE on the placement call; the retry loop also gained a width floor (>800) so a title-bar slice can never pass as a capture.
4. PowerShell delegate callbacks swallow non-final expression output into the return conversion — diagnostic enum scripts must collect into a script-scope list, not emit strings mid-callback (used only during debugging; the committed scripts never relied on it).

**Linux/WSLg disposition (honest):** WSL reports ZERO installed distributions — never faked (same named limit as SP-054/055). The dialog surface remains NOT ADMITTED for intake (web-shim speaks WebView2 chrome.webview only; the typed honest-unsupported surface). Evidence is Windows-only.

## Step 3 — engine-review presence (T-2)

Step-3 plan review call → `skipped: true, spawnFailed: false` (artifact `.reviews/3-20260812T081444.md`), same SP-195 class.

## Step 4 — pre-completion consult

**Mode:** solo (T-7). **Requested route:** the runtime default. **Actual answering model:** NOT surfaced by the consult tool response (same provenance discipline as every prior packet — recorded honestly). **Verdict: NOTHING BLOCKS .DONE** — "the evidence is stronger than the packet required (the serve-probe sha256 matching the trust-anchor hash is self-verifying)". The response TRUNCATED mid-sentence inside Discharge 1 (the SP-027 truncation class — recorded, never silently stitched); the visible portion is complete and was executed:

**Discharge 1 (executed):** the serve-probe is a HOST-side GET — real route resolution, but not the page fetching the module. The independent PAGE-side proof sits in the same transcript and is now claimed precisely: `ai.js:24` statically imports `./accents.js`, so a 404 on it kills the whole ES-module graph and the page never reaches `ready`. run.log carries `intake: ready received`, `init sent`, and the page's own `boot: niche=bambi hosted=true endless=false` + the banks fetch (`GET /dtrh/banks/… -> 200 (page:overlay)`) — the page booted, therefore every static import of its module graph, accents.js included, resolved through the loopback. Claim bounded exactly: host-side per-file request/response (serve-probe 200 + 404 control + hash match) AND page-side whole-graph resolution (successful boot); never "the page's individual accents.js fetch was logged" (route-class logging redacts filenames by design).

**Truncated tail (recorded, non-blocking):** the cut sentence began a second framing point about the claim's precision; the visible verdict (nothing blocks .DONE) and Discharge 1 arrived complete. The nuance I had flagged (upstream sequences the raise before XP; the greenfield verdict log line sits after XP compute — side-effect-free, observational only) drew no objection in the visible portion and stays as recorded.

## Budgets (T-11)

Step 1 ≈ 50min (tree enumeration + widened sweep + obligation table + consult). Step 2 ≈ 40min (4 source files + 12 test cases + full gates). Step 3 ≈ 50min (3 bracket runs + the capture-tooling surprises + a live enum debug session). Each headed run ≈ 100s. The 4h export was never approached.

## Step 5 — testing & verification

- `node .spine/patches/verify.mjs` → **exit 0** (all patches applied on all roots).
- `dotnet build client/CcpClient.sln -c Debug --nologo` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet test CcpClient.Tests` → **862/862 green** (floor 847, the SP-057 amendment), TRX `sp058-final-unit.trx` attached into evidence/ (force-added past the `*.trx` gitignore, the SP-054/055 convention).
- `dotnet test CcpClient.HeadlessTests` → **33/33 green** (floor 33), TRX `sp058-final-headless.trx` attached.
- `git diff --check` → **clean (exit 0)**.
- `git status --short` → only File Scope paths (committed diff vs the wave-16 base e0b30928 grep-verified: zero hits on `ConditioningControlPanel/**`, `Features/Dtrh/**`, `Lifecycle/**`, `client/spikes/**`, `.spine/**`, `client/CcpClient.sln`, the three hot docs). `Program.cs` UNTOUCHED — no new harness flag was required (the File Scope preference held: the serve-probe and boundary steps ride SP-054's existing `--intake-drive` vocabulary). fileScopeMustChange satisfied (record.md, this file); artifactsMustExist satisfied.
- `grep -c "Review Level" PROMPT.md` = **2** (≥ 2 authoring rule).

**Completion criteria audit:** (1) the real v6.6.3→v6.7.4 delta enumerated from the TREE with counts — 4 files, including the ledger-unnamed QuizService.cs +15 and the widened-sweep GamificationBridge +157 (consumer side). (2) Every delta element carries a typed verdict with File.cs:line / payload:line citations; shipped obligations implemented + tested; NOTHING verdicts recorded with evidence and zero code. (3) accents.js proven served by the RUNNING host (serve-probe 200 + trust-anchor-matching sha256 + 404 negative control + the page-side whole-graph boot proof). (4) `IsAssetActive` gating runs through SP-055's single `DtrhUserMedia` definition — verified, no second definition, no residual gap. (5) `TopMarksPercent = 90.0` pinned WITH the comparison and boundary cases (exactly-90.0 true headed + unit; 89.99…/zero-max false unit). (6) New baseline stated explicitly (v6.7.4, `0c9947a6`, merge `42286638`); the real profile byte-identical after headed evidence under the SP-057 seam (its first consumer). (7) Contract green; both consults persisted with actual-answering-model provenance honestly recorded (never surfaced by the tool).

## Durable-lesson candidates (orchestrator picks at land; enabler 2 — the worker does NOT edit port-lessons.md)

1. **`Start-Process -ArgumentList` joins array elements unquoted** — a step-list argument containing spaces silently truncates; keep harness vocabularies space-free or quote manually.
2. **A capture/raise helper that calls `SetWindowPos` with cx=cy=0 MUST pass SWP_NOSIZE (0x0001)** — without it the window is squashed to its minimum and every subsequent rect read sees the squashed size (self-inflicted, looks exactly like a windowing quirk).
3. **The intake window reports `IsWindowVisible=false` to EnumWindows while painted** (SP-054's quirk, reconfirmed) — any enumeration filtered on visibility can never find it; SP-057's `drive.ps1` pattern does not transfer to intake captures.
4. **The sync ledger's file list is a hypothesis — the tree diff is the authority** (third confirmation: QuizService.cs +15 was never ledger-named; and the widened sweep found the GamificationBridge +157 consumer side).
5. **A per-file serving probe from inside the running host (status+bytes+sha256 against its own loopback, with a 404 negative control) is the honest shape when route-class logging redacts filenames** — and the probe's sha256 doubling as a trust-anchor hash check makes the evidence self-verifying.
