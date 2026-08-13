# Port Status (as of 2026-08-13, ninth export — laptop)

## Wave 23 LANDED (2026-08-13, integrate `29950e9b`; floor 898/35/0 → **900 unit / 35 headless / 2 NAMED skips, 0W/0E**)

- **SP-066 LANDED — board row 49 PART (1). BOTH HALVES OF ROW 49 ARE NOW LANDED; the row stays WIP pending owner ratification only, and no claimable row-49 work remains.** The land was CLEAN: batch `20260813T052334`, 1 task, 0 failures, zero recovery cycles.
- **THE FLOOR NOW READS 2 SKIPS AND THAT IS STRICTLY STRONGER, NOT A LOOSENING.** Five tests used to hit a hidden early `return` on the wrong OS and were counted as PASSES — the old "0 skipped" floor was measuring vacuity as green. They now `Assert.Skip*` and are pinned by fully-qualified NAME in `floor.json` `allowedSkips`, so a skip that is not on that list fails the contract NAMING the test. Arithmetic reconciles exactly: 898 = 896 passed + the 2 Linux-gated conversions now visible; +2 new guard facts = **900 total / 898 passed / 2 skipped** on a Windows box. **Do not "restore" the 0-skip floor.**
- **Both permanent bans verified ABSENT from `allowedSkips` at land** (this was the first thing checked, per the wave-22 handoff): the SP-057 pin `DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault` and the named privacy flake `ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery`. The 5 listed names are all OS-gated with their executing machine class named in-file.
- **Commit ORDER verified, not just end state:** the `{passed,skipped}` → `{total, allowedSkips[]}` schema change (`055b937f`) landed BEFORE any `Assert.Skip` conversion (`0113c9fc`). That ordering is what stopped the packet from reddening its own contract and reaching for the cheap fix of widening the pin.
- **Delivered:** `VacuousShapeDetector.cs` (one lexical surface shared by inventory and guard so they cannot drift); `client/tests/floor/vacuous-shape-ledger.json` verdicting **all 78 sites** (67 not-vacuous, 5 platform-skip-converted, 6 fixed, **0 deleted, 0 residual**); `VacuousShapeGuardTests` failing `file:line` for any detected site missing from the ledger, both directions + shape-set equality, captured RED from a probe that was then proven removed. Zero assertions weakened, zero tolerances widened, zero quarantines, **zero `client/src/**` changes — this wave closed NO product capability.**
- **A NAMED RED fired and was NOT retried away:** `AsyncLifecycleTests.Heartbeat_SkipsProjectionUntilBound_ThenFlowsThroughBoundary` (Cancelled vs Completed, `AsyncLifecycleTests.cs:203`) in the worker's run 0 — **second recorded occurrence** (first: SP-055 record.md:162). Both times it fired under a diff touching no lifecycle code, so it is a real race in the StopAsync completion path. **Filed as a P1 board row.** Allowlisting it would have been exactly the quarantine abuse the new admission rule bans.
- **Named limits (carried on the row, do not let them dissolve):** the detector is **LEXICAL** → **runtime vacuity is NOT detected** (helper-hoisted assertions read as absent; a loop over a possibly-empty collection reads as asserting); the guard binds **only enumerated shapes**, and `Assert.All`/expression-lambda bodies (21 uses) were **observed and deliberately left out** → own board row; **`allowedSkips` records intent nothing verifies** — the admission rule and both bans are TEXT → own S row to make the ban half mechanical; ledger reasons are unchecked judgment; **T-17's induced-skip auditor RUN undelivered** → T-17 stays OPEN on that residual; **Linux unproven** (zero WSL distros).
- **"78 sites" is SURFACE-RELATIVE, not absolute:** the detector was refined twice mid-packet (class attribution by brace range; guarding-brace depth making try/using/lock transparent, which moved `assertions-all-nested` 45→22). Quote the number with that qualifier or not at all.
- **`client/tools/port-audit-prompt.md` IS NOW READ BY TWO TESTS.** Editing it is a code change requiring a verified run — never a docs drive-by, and never a post-verification reconciliation edit (that is the wave-18 red-base class). The land consult flagged this specifically and the reconciliation honored it.
- **Land consult (solo Opus 5, capped at 200 words): APPROVE-WITH-CONDITIONS, all discharged.** Capping the reply worked again — clean surfacing, no truncation, third consecutive wave. It contributed one row I had not planned (the mechanical ban test).
- **Rows filed at this land:** AsyncLifecycle StopAsync race (P1); `Assert.All` unenumerated shape (P2); `allowedSkips` bans-are-text (P2, S). **Next unused task ID: SP-067.** Claimable next: the three rows just filed, row 50 (injected timeout BUDGETS, 4th occurrence of the timing class — raising the 800 ms budget on `LoopbackOllamaProviderTests.Truncated_PrefixCut_NeverSurfaced_TypedUnavailable` stays BANNED), T-17's auditor run, the named privacy flake, and the standing product queue. **Owner question raised in the digest: five queue rows now exist only to harden the test suite — keep going, or return to WPF parity features?**

## Wave 23 authoring phase (2026-08-13 — SP-066 authored + launched, superseded by the land above)

- **SP-066 = board row 49 PART (1)** — the vacuous-SHAPE sweep, the half SP-065 did not cover — **plus T-17 as a bounded doc-only final step**. Row 49 flipped with the authoring note; T-17 annotated as riding but NOT closing. **Next unused task ID: SP-067.**
- **The framing that decided the wave:** the floor now pins 898 passing facts without being able to tell whether any of them *assert* anything. A test that conditionally `return`s before its only assertion reports Passed and is now pinned as a permanent green fixture — so the just-landed machinery makes the unswept half *worse*, not better. That is the argument for doing it now rather than deferring it behind product work.
- **Deliverable shape:** a committed, executable shape detector over both test projects; a ledger with a disposition verdict + reason for EVERY silencable site (`not-vacuous` / `platform-skip-converted` / `fixed` / `deleted` / `residual`); a guard failing `file:line` on a NEW unclassified site; and `floor.json` moved from `{passed, skipped}` to `{total, allowedSkips[]}` so expected skips are pinned by fully-qualified NAME. That last part **also closes SP-065's own named counts-not-identity limit.** Zero product code (`client/src/**` in `fileScopeMustNotChange`).
- **THE TRAP TO WATCH AT LAND:** `allowedSkips` is a new quarantine temptation. Two names are banned from it in the packet and in `## Do NOT` — the **SP-057 pin** (a skip there means someone exported `CCP_DATA_ROOT` process-wide, the vacuous `896/1` green SP-062 closed) and the **named flake** `ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery` (privacy boundary; reproduce and fix at source). **At land, read `floor.json`'s `allowedSkips` before anything else and check both names are absent and every listed entry names the machine class where it does execute.**
- **Order is load-bearing and is the second land check:** the schema change (Step 2) must land BEFORE any `Assert.Skip` conversion (Step 3). Converting first reddens the packet's own contract and the cheap way out is widening the pin — the exact failure this row exists to prevent, reproduced by its own fix.
- **Honest non-claims to expect in the record (do not let them be dropped):** the detector is **lexical**, so assertions hoisted into helpers read as absent and a loop over an empty collection reads as asserting — **runtime vacuity is NOT detected** (mitigated additively with `Assert.NotEmpty`, never by weakening); the guard binds only enumerated shapes; `allowedSkips` records intent that nothing mechanically verifies; **T-17's induced-skip auditor RUN is not delivered** (the edit and a prompt pin are); Linux unproven.
- **Decomposition consult (solo, Opus 5): first call reasoning-only (5th occurrence), narrow re-ask capped at 150 words surfaced cleanly.** This is now a reliable technique, not luck — same result as wave 22. Verdicts: single lane; ship the guard ("the row's exact analyzer surface, not scope creep"); name-based allowlist over platform-conditional pins ("encodes machine facts into a committed pin") and over forbidding `Assert.Skip` ("contradicts the row's own acceptance"); T-17 may ride but "may not expand".
- Base floor at launch: **898 unit / 35 headless / 0 skipped, 0W/0E**. Packet is **Size L**, launched with `SPINE_WORKER_PI_TIMEOUT_MS=14400000`; each step bounded under 2h. MCP 0/3 connected (cached only) — named limit, no AXAML here. Zero WSL distros → Linux stays a standing named gate. 9 local patches verified applied on both roots BEFORE authoring (the per-checkout `skill-floor-wrapper-testcommand` patch is what puts the wrapper mandate in the packet template — a fresh clone must run `apply.mjs && verify.mjs` first).

## Wave 22 LANDED (2026-08-13, integrate `09b4b639`; floor 897/35/0 → **898 unit / 35 headless / 0 skipped, 0W/0E**)

- **SP-065 LANDED — board row 49 PART (2) only; the row stays WIP because part (1) (the vacuous-SHAPE enumeration sweep) is UNSWEPT and still claimable.** The floor is now machinery: `client/tests/floor/check-floor.mjs` owns both `dotnet test` invocations, discovers test projects from the sln, writes TRX outside the worktree, and fails the CONTRACT on an unexpected skip / off-floor count / bad counter category / stale result file. Pin: `client/tests/floor/floor.json` (898 + 35, 0 skipped). `FloorWrapperGuardTests` walks `spine-tasks/*/PROMPT.md` and reddens any packet with ID >= SP-065 that calls `dotnet test` outside the wrapper.
- **The land was CLEAN — no recovery** (contrast wave 21's `GitignoredDirtyWorktree` scramble). Batch `20260813T032810`: 1 task, 0 failures, gate approved after my own verification, integrate `09b4b639`, batch completed and archived.
- **All three pre-launch land obligations discharged.** (1) Merged tree verified THROUGH the wrapper in scratch worktree `land-verify-w22`: 0W/0E build + **3 consecutive greens** (898/898, 35/35, 0 skipped, exit 0), then the decisive check — verified tree SHA `ec157d0e` **byte-identical** to the integrated tip, `git diff` EMPTY. Wrapper left ZERO gitignored-dirty entries. (2) `.spine/patches/manifest.json` gained `skill-floor-wrapper-testcommand` (project-tree-only, like the other 3 skill patches), applied + `verify.mjs` OK on both roots. (3) Auditor's bare `dotnet test` FILED as new board row T-17, not fixed inline.
- **Consult caught two real errors this phase (solo Opus 5, clean surfacing, ~400-word cap held).** It corrected my false premise that the skill patches hit the machine-global engine root — they are `engine: false`, project-tree-only, so there was no global blast radius and the obligation was unambiguous; and it predicted the anchor collision before I hit it. It also called out a sloppy `grep -icE "warning|error"` build check that proved nothing (re-derived properly: 0 Warning(s) / 0 Error(s)). **Lesson: cap the reply and ask narrowly — it worked where three prior calls truncated.**
- **T-3 gate-evidence class, 5th occurrence, NEW variant:** the gate's `diff-stat.txt` is a TWO-DOT diff, so base-side commits appear as worker deletions. It showed `port-status.md -1` and `CONTEXT.md -8`, i.e. it looked like the worker had reverted the land obligations. Three-dot diff disproved it. Do not panic-reject on a two-dot diff-stat.
- **FRESH-MACHINE / FRESH-CLONE STEP (new, easy to miss):** the `skill-floor-wrapper-testcommand` patch is `engine:false`, so it lives in the **per-checkout** `.pi/npm` tree, not the machine-global engine root. On the desktop or any fresh clone, run `node .spine/patches/apply.mjs && node .spine/patches/verify.mjs` BEFORE authoring a packet — otherwise the authoring template silently lacks the wrapper mandate and `FloorWrapperGuardTests` reddens the next lane instead.
- **Next unused task ID: SP-066.** Claimable next: row 49 part (1) (vacuous-SHAPE sweep, size M+), row 50 (injected timeout BUDGETS — 4th occurrence of the timing class), the two new rows filed this land (T-17 auditor wrapper, named flake), and the standing product queue.

## Wave 22 authoring phase (2026-08-13 — SP-065 authored + launched, nothing landed)

- **SP-065 = board row 49 PART (2) ONLY** — the mechanical skip/count check that fails the CONTRACT instead of a human comparing two numbers. Part (1) (vacuous-SHAPE enumeration sweep) is untouched and stays claimable; the row's own sizing note says do not let (1) delay (2). Row flipped OPEN → WIP with the authoring note. **Next unused task ID: SP-066.**
- **Deliverable shape:** wrapper at `client/tests/floor/check-floor.mjs` owns BOTH `dotnet test` invocations (the contract testCommand no longer calls them directly), writes results OUTSIDE the worktree, fails closed on missing/unparseable/stale/empty/absent results, and pins per-project `passed` + `skipped` exactly. Both verdicts must be demonstrated (induced skip → RED, clean run → GREEN) plus count drift in both directions. **Zero product code** — `client/src/**` is in `fileScopeMustNotChange`.
- **Three traps verified empirically at authoring, all now packet framings.** (1) `client/tools/` is gitignored by the bare `tools/` rule at `.gitignore:168` — the 117 files there are force-added, so a new wrapper written there passes in-lane and is ABSENT from the merged tree. `client/tests/**` is clean. (2) `*.trx` (`.gitignore:91`) and `TestResults/` (`.gitignore:90`) inside a lane worktree would make every future lane unmergeable-until-cleaned, turning SP-064's one-off recovery into a permanent tax on every land. (3) Wiring an exact pin into the packet's own testCommand turns its own later steps red — pin last, or bump in the same commit as each count change.
- **Half-install closed by a guard, not a note** (consult directive, the one verdict line that surfaced): `.spine/patches/manifest.json` stays out of worker scope and the orchestrator applies the packet-template edit AT LAND; the packet ships a guard that walks `spine-tasks/*/PROMPT.md` and fails with `file:line` when any packet with ID >= SP-065 calls `dotnet test` outside the wrapper. **If the land-time template edit is forgotten, the NEXT lane goes red on its own contract** — that is the intended catch point, and it is the orchestrator's obligation at the SP-065 land.
- **Consult tool surfacing is degrading and should be treated as a known instrument limit:** three solo Opus-5 calls — call 1 reasoning only, call 2 one line then truncated mid-sentence, call 3 clean. Same class as waves 17 and 21. Ask narrowly and cap the reply length when a verdict matters; record what surfaced and never stitch a verdict out of reasoning.
- **LAND OBLIGATIONS (pre-launch consult, solo Opus 5) — the land phase is a fresh session and must inherit these:** (1) **verify the merged tree THROUGH the wrapper** (`node client/tests/floor/check-floor.mjs` as the decisive check in the scratch worktree, exit 0, then `git diff` EMPTY vs the integrated tip) — landing this row by reading counts off a console would certify the fix using the exact method the fix abolishes, and a stale pin would ship green; (2) **update `.spine/patches/manifest.json`** so future packets inherit the wrapper in their testCommand (forgetting it is loud-but-wasteful: SP-065's guard reddens the next lane); (3) **file, do not fix inline** — the blind auditor `client/tools/port-audit-prompt.md` still runs bare `dotnet test` and so retains the detection path SP-065 replaces.
- Base floor at launch: **897 unit / 35 headless / 0 skipped, 0W/0E**. MCP 0/3 connected (cached only) — named limit, no AXAML in this packet. Zero WSL distros → Linux stays a standing named gate.

## Wave 21 (LANDED 2026-08-13, integrate `e8eab7c1`; floor 892/35/0 → **897 unit / 35 headless / 0 skipped**)

- **SP-064 LANDED (row 38 stays WIP pending owner ratification).** Harness-only entry points exit 3 naming `CCP_DATA_ROOT` when unset; gate in the real `Program.Main` between the SP-057 override read and composition-root construction; one registry + table-driven refusal pin + unclassified-literal guard (captured RED); profile BYTE-IDENTICAL over 2677 files after a refused run. **Behavior break: every headed evidence script must set `CCP_DATA_ROOT` or exit 3.**
- **The land was a recovery, not a clean pass.** Batch `20260813T010705` ended `failed`/`GitignoredDirtyWorktree` AFTER contract-verified + code-review APPROVE + final-review PASS. The `--diagnose` headline (`git rm -r --cached`) was DANGEROUS — 117 force-added tracked files (all of `client/tools/verify`, `port-loop.ps1`, `Tools/asset_gen`) would have been staged for deletion; spine's own event payload carried the correct advice. Recovery: preserve+hash the 8 gitignored TRX → surgical clean of unclassified dirt → remove lane `.pi/npm` → `force-merge` → `retry` + `resume` (reconcile-from-`.DONE`, no worker respawn). **Lane tip never moved from `571a240f`**, so the merged tree is exactly the reviewed artifact. Six lessons recorded in port-lessons.
- Orchestrator verified independently: fresh scratch worktree on the merged tree (build 0W/0E, 897/35/0), then re-verified the exact tree pushed AFTER the reconciliation commit (wave-18 rule). Gate evidence not trusted.
- Named limits carried, not closed: demo+auto-close hole open by decree; only the data root protected; guard scans `client/src/**/*.cs` literals only; non-`Program.Main` harness paths unprotected; **Linux unproven (zero WSL distros)**. Next unused task ID: **SP-065**. Next claimable: board row 49 part (2), which must pin 897/35/0.

## Wave 21 authoring phase (2026-08-13, superseded by the land above)

- **First phase run under the unattended loop** (`client/tools/port-loop.ps1`): the shell owns waiting, one fresh pi session per phase. This session did phase B only (reconcile → consult → author → launch detached → exit); it did NOT monitor the batch.
- **SP-064 = board row 38** (harness entry points must REFUSE to run unsealed). Gate in the real `Program.Main` path after the SP-057 override block and before composition-root construction; ONE registry for the classification; a guard test that fails on any unclassified startup flag literal; real-process proof both directions (refusal leaves `%APPDATA%\CcpClient` byte-identical under path-hashed manifests + SP-057 positive controls; plain unsealed launch still opens a window and exits 0).
- **Single lane on purpose:** the deliverable is a suite-wide pinned enumeration of entry points and every product slice here has added its own `--x-demo`/`--x-drive` flag, so a parallel lane is green alone and RED at merge (SP-054/SP-058 class). It also moves the exact-count floor that board row 49 part (2) would pin — that row is the successor.
- **Decomposition consult (solo, Opus 5) returned reasoning only; the final verdict text was not surfaced by the tool.** Recorded, never stitched; its guidance is carried in the packet framings.
- Base floor at launch: **892 unit / 35 headless / 0 skipped**. This wave ADDS facts — the worker states the new exact count. Next unused task ID: **SP-065**.

## Wave 20 (LANDED 2026-08-12, integrate `10c37650`; floor stays 892/35/0-skipped)

- **OWNER DECREE 2026-08-12: "Just increase the amount of budgets by a lot! So it does not happen again."** It supersedes SP-059's "raising a budget is the banned fix" **for board row 49 only** (owner = authority order #1). Batch `20260812T221746` was ABORTED mid-Step-1 the moment the decree landed (its packet's central acceptance was the vetoed fix); its completed sweep was preserved as `prior-step1/` input to verify.
- **SP-063 LANDED (row WIP):** one shared FINITE constant `TestWait.InjectedBudget = 60 s` for budgets that must not decide outcomes; the 2 timeout-SUBJECT tests keep 800 ms marked + pinned; 3 inert assignments deleted; 1 guard token + captured RED. Two deviations from a literal decree reading, both surfaced to the owner: don't raise timeout-subject budgets (fixes nothing, slows the suite); never `Timeout.InfiniteTimeSpan` (unbounded hang on a suite with no per-test timeout).
- **Residual named on the row:** a bigger number lengthens the fuse; it does not remove the time dependence. The deterministic alternative was set aside by decree, not refuted.
- Next: wave 21 = board row 38 (harness entry points must REFUSE to run unsealed when `CCP_DATA_ROOT` is unset). Next unused task ID: **SP-064**.

## Wave 19 (LANDED 2026-08-12, integrate `7518c6a4`; floor stays 892/35)

- **SP-062 LANDED (row WIP).** Loud `Assert.SkipWhen` ×2 + positive control (`891 passed / 1 skipped`, TRX `NotExecuted`); isolation fixed by **co-location** into `ProcessEnvCollection`, probe-proven both ways (cross-collection ran concurrently in 65 ms = `DisableParallelization` is dead here; a deliberately-RED cross-class handshake deadlocked = intra-collection sequentiality is real). 20 greens at 892/0 + 35/0 across two trees, 2 cold first-ever builds. Next unused task ID: **SP-063**.
- **MY wave-18 land shipped a RED base:** flipping `upstream-payload-inventory.json` to `served` without the guard-required `evidence` field. Rule now in port-lessons: a disposition flip carries every field the guard keys on, and the land's LAST action is a full-suite run on the tree actually being pushed. Also fixed in-lane: a 9-site `AiProviderLab` record-before-response ordering race.
- **Gate evidence lied in both directions (T-3):** the integrate gate's `test-output.txt` was a BASE-tree run in the main checkout showing 2 failures that were not the lane's. Land rode my own merged-state verification; the decisive check is `git diff` EMPTY between the verified scratch merge and the integrated tip.
- **Single lane by consult (solo):** the row changes what "passing" means suite-wide — a second lane contaminates the 10-green scheduling-pressure measurement and races the floor count. Successors: board row 38 (harness entry points refuse unsealed) and row 49 (injected timeout BUDGETS, 4th occurrence). Row 49's site `LoopbackOllamaProviderTests.Truncated_PrefixCut_NeverSurfaced_TypedUnavailable` is carried in SP-062 BY NAME as a known out-of-scope cold red; raising its 800 ms budget stays banned.
- Session posture: git clean and in sync with origin at `f3a1192b`; MCP 1/3 (avalonia-docs connected; avalonia-live `fetch failed`; avalonia-ui not connected); no MonitorCreate/LoopList tools → batch watcher is a background pi Agent on `spine wait`. Next unused task ID after this wave: **SP-063**.
- Board reconcile at launch: row "Wire companion memory into prompt context" status cell corrected OPEN→WIP (landed SP-047 work must never read as claimable).

Branch: `feat/crossplatform` @ wave-17 integrate `eb1f60d4`. **35+ land commits ahead of `origin/feat/crossplatform` and NOT pushed** (waves 13-17). Do not `reset --hard origin` on this machine — that would destroy them.

## Live state (2026-08-12)

- Waves 1-18 landed. Floor: **892 unit / 35 headless**. Next unused task ID: **SP-062**. Next work: **SP-062 = loud skip + fixture process-env isolation** (filed at the SP-061 land: the SP-057 pin can now pass vacuously).
- **Wave 17 LANDED** — batch `20260812T115820`: **SP-059** timing discipline (waits converted + `TestWait` helper + pinned guard + 10/10 greens; **claim narrowed at land: injected timeout BUDGETS are not swept** — fourth-occurrence P1 row filed; constitution line applied by the orchestrator) and **SP-060** Her Room/Awareness audit (38-row table + 12 owner questions; **row stays OPEN — an audit is not a decree**).
- **Standing traps learned this wave (full text in `client/docs/port-lessons.md`):** never `git clean -fdX` a lane before `contract.verified ok` (it deletes the T-14-staged `.pi/npm` and breaks `verify.mjs`); "cold" means a fresh checkout, not a rebuild in place; `spine wait` returns instantly once any lane is terminal-failed; `tasklist /FI` lies about live PIDs — use `Get-Process -Id`.
- **Wave-17 consult correction (solo, Opus 5):** the Chaos tunnel backdrop that wave-16 queued for lane-1 was front-run by the timing row and moves to **wave 18, alone** — the tunnel lane writes the repo's most wall-clock-hungry test class, and a deadline-literal guard landing beside new deadline literals reproduces the SP-058 merge-time failure on purpose.
- **This machine (laptop):** no `MonitorCreate`/`LoopList` tools in this pi session — the batch monitor is a background pi Agent blocking on `spine wait` instead. engram MCP not registered here; 0/3 MCP servers connected at session start (`avalonia-live` cached only).

## New since the first export (2026-08-04, second export state)

- **Resume reconciliation executed (laptop):** waves 1–3 verified; the desktop's parked wave-4 batch `20260722T152755` lane commits NEVER travelled (desktop-local) — wave 4 is a FRESH execution of the packets, not a resume.
- **SP-037 authored + LANDED** (v6.6.3 manifest-drift repair, floor-repair precondition): empirical sweep +7/−1 (board hypothesis said +4 — sweep vindicated), copied-count 1538→1544, floor restored **466/466 + 29/29**; substitution-norm land (engine review chain absent — see park below); row WIP, owner ratifies. Next unused ID = **SP-038**.
- **WAVE 4 LANDED 2026-08-04 (integrate `8efd60b4`; floor now 492/492 + 29/29):** SP-035 (LoopbackOllamaProvider — first real provider; native api/chat; retry default OFF + 5-min WPF-observed timeout via consult corrections; LAB 26/26 Windows; WSL zero-distros named limit) + SP-036 (bounded MCP admission; avalonia-live PROVISIONAL with binding `CCP_MCP=1` condition; Sentry empirically LIVE = de-facto option 3, owner question OPEN; redact-BEFORE-calling binding run-wide). First production wave on the billing-header fix — all engine review spawns green. T-14 filed (lane re-patch recurrence). **Next: SP-038 = AI c3 (moderation boundary, read admission §8 c3 first) + lane partner TBD.**
- **WAVE 5 LANDED 2026-08-04 (integrate `f4eea79e`; floor now 516/516 + 29/29):** SP-038 (c3 moderation boundary — coverage-honesty inventory + tripwire, escalation interactive-only session-scoped, ZERO policy values invented) + SP-039 (T-14 hook — lanes now pre-staged with the main checkout's PATCHED .pi/npm at creation; named gate armed: next wave zero mid-task verify reds). T-15 filed (c2 lab harness hardening — zombie test-host flake class root-caused at T-3). **Next: SP-040 = AI c4 (memory; §4 rule 5 binds moderation-gated persist) + lane partner TBD.**
- **WAVE 4 PARKED (pause protocol, both-routes-failed branch):** anthropic fresh-subprocess route DOWN account-wide (`400 "extra usage"` — engine reviewer spawn + manual `pi -p` probes, opus-5 AND fable-5; in-session consult UNAFFECTED). SP-035+SP-036 stay pending + 2026-08-04-amended (Ollama present on laptop; WSL zero-distros named limit; consult rewire; SP-036 three-seat subject). Owner action: restore spawn capacity (claude.ai/settings/usage) or explicitly accept reduced assurance. Full state: `.spine/handoff.md`. **PARK LIFTED ~15:30 same day: request-shape defect (missing billing-header system[0], hermes #48176), fixed by `__PI_BILLING_HEADER_FIX__` — see incidents file.**
- **Engine restored on laptop:** global pi-spine re-pinned 2.12.2 → admitted 2.10.0 (BOTH settings.json + npm package.json exact), 12 patches applied, verify.mjs green.
- **Laptop bootstrap fixes (durable, in incidents file):** `git config --global core.hidedotfiles false` (hidden-.git EPERM class) + `pi.exe` shim (Node 24 cannot spawn the cmd/shell shims).

## Pause state

~~Parked 2026-08-04 ~13:10 UTC~~ **PARK LIFTED same day ~15:30 local** — the anthropic-400 was a pi request-shape defect (missing `x-anthropic-billing-header` system[0], hermes-agent #48176), fixed by local patch `__PI_BILLING_HEADER_FIX__` on the nested pi-ai (`pi -p` 200 on opus-5 AND fable-5). Wave 4 launches. Re-check the patch after any pi upgrade (npm wipes it).

## Board honesty rule

Landed rows stay WIP until the owner ratifies them. 18 rows were flipped WIP→DONE only with RATIFIED decree citation placed in evidence cells. Never flip without it.

## Landed

- **Run closed out 2026-07-21** — SP-001…SP-020 all landed (19 product/tooling rows,
  ALL WIP pending owner ratification). T-1 CLOSED (durable spine patch mechanism
  delivered by SP-020 and proven on the real tree via post-land reinstall gate).
- **SP-023** (2026-07-21) — DTRH host slice b1, FIRST product-implementation slice.
  FIRST GATE PROVEN: invokeCSharpAction page→host works on NativeWebDialog (WSLg
  transcript) — admission's named risk retired. Host shell: Windows embedded WebView2 12.0.1.
- **SP-024** (2026-07-21) — DTRH host b2: slots/picker/protocol v1. Three save slots =
  4 PersistenceStore<T> instances on SP-005 machinery (index + 3 slots, each its OWN
  named AsyncOperationOwner).
- **SP-025** (2026-07-21) — DTRH host b3: SFX/freeze/tint/video. Backends LIVE-FEED
  admitted (SoundFlow 1.4.1 nupkg-verified + LibVLCSharp 3.10.0 /
  VideoLAN.LibVLC.Windows 3.0.23.1; Linux distro libvlc 3.0.23-1).
  SoundFlow deadlock lesson + rect-persistence binding recorded.
- **SP-026** (2026-07-22) — DTRH host b4: progression/payout/Loom/media. Progression
  rides b2 slot documents — schema stays v1 additive-only, NO parallel meta file.
  Floor 366/29.
- **SP-027** (2026-07-22) — DTRH host b5, FINAL slice. **The b1–b5 slice cut is
  COMPLETE; the DTRH host row is fully sliced and stays WIP with consolidated named
  limits.** Watchdog/exit/injection + ESC forensics delivered.
- **SP-028** (2026-07-22) — T-5 local anchor-patch (parallelism enabler 1).
  `t5-reviews-autoclean` manifest patch: delete .reviews/ inside commitLaneWorktree
  AFTER verdict recording. T-5 CLOSED-by-patch; base install patched.
- **WAVE 1** (2026-07-22, first 2-lane batch, orch tip bff8f037) — SP-029 quips
  arbitration q1 (Audio/SoundArbitration.cs — SP-017 channel ownership verbatim,
  refcounted ducking with panic release-all) + SP-030 admission.
  T-5 post-land gate FAILED (row reopened).
- **WAVE 2** (2026-07-22, integrate 6e1b2f81) — **FIRST AUTO-GATE LAND in project
  history** (engine ran its own merges + opened its own gate).
  SP-031 (T-5 anchor re-base): **the SP-028 premise was FALSIFIED with 3 independent
  proofs** — two-root truth. SP-032: quips q2.
- **WAVE 3** (2026-07-22, integrate 2f77c934) — second consecutive auto-gate land.
  SP-033: AI companion c1 — AiOperationPipeline (SP-004 owned ops); provider seam
  switch = generation invalidation + cancel + stale-drop. SP-034: probe
  (Review-Level authoring defect recorded honestly). **T-5 gate DISCHARGED**
  (full-chain proof on SP-033). T-13 stall-detector DONE.

## Staged / next (wave 4)

- **SP-035** — AI companion slice c2: loopback Ollama provider.
- **SP-036** — audit and admit bounded Avalonia MCP use (A-01...).

## Pause state

2026-07-22 ~16:30 UTC the owner invoked the pause protocol: "Consult fable 5 has
hit limit, pause all work and prepare save spot." Work resumed 2026-08-04 (this
session: git repair + push + this memory export).

## Board honesty rule

Landed rows stay WIP until the owner ratifies them. 18 rows were flipped WIP→DONE
only with RATIFIED decree citation placed in evidence cells. Never flip without it.

## Wave 6 (2026-08-04, integrate 6255a643; floor now 537/537 + 29/29)

- **SP-040 (c4 memory):** AiMemoryStore on SP-005 machinery (own owner; null-on-disk retention discipline; consent placeholder Denied; append-NEVER strengthening; explicit-clear with 3 consult hardenings; named non-claim: persists+clears, context consumption = c7). Row WIP — c5 = awareness next.
- **SP-041 (T-15 lab harness):** ctor ODE race root-caused w/ deterministic repro; fresh-instance-per-bind; leak self-check (static registry + assembly fixture); 5 consecutive greens; zero assertion changes. Row WIP — owner ratifies.
- **T-14 NAMED GATE DISCHARGED → row CLOSED:** hook fired all lanes; lane-1's first red-free contract in 6 packets. Fresh lanes now arrive pre-patched (keep MAIN checkout patched — the hook copies whatever main carries).
- **T-16 filed** (DTRH cap-timer flake class). Next: SP-042 = AI c5 (awareness) + partner TBD.
- **Owner decrees encoded (2026-08-04):** improve-freely mandate (no 1:1 copy anywhere; observable-outcome parity only; improvements a must); use all resources actively ALWAYS (MCP seats within SP-036 rules); hermes caps 5000→10000 (config, restart-effective); avalonia-live verified end-to-end (27 tools; laptop headed-evidence substitute for UI work).

## Wave 7 (2026-08-04, integrate 49c4af7b; floor now 564/564 + 29/29)

- **SP-042 (c5 awareness):** typed consent (NotGiven placeholder; residual bool door + retirement condition in row); 4-class cooldown registry (extend-not-shrink; 10-vs-90 owner question verbatim); packaging under consent through c3 boundary (zero transmission on block); keyword routing owned ops (canned keyword-path-only; refusal drops); title capability Windows-probed. Row WIP — c6 = command execution next.
- **SP-043 (T-16 cap-timer determinism):** REAL 15s SEGMENT_SEC on ManualClock; pre-existing ISoundClock seam; 10 consecutive zero-red runs; row DONE (with T-15, consistency ruling).
- **T-15 + T-16 BOTH DONE** (tooling rows discharge on evidence; owner async-veto standing).
- **Named limits carried on the AI row:** Reserved→Wired flip (c6 owns, coverage test explicitly in File Scope); bool-overload retirement condition; badge-accuracy headed = c7.
- Next: SP-044 = AI c6 (command execution; none-admitted default; provable scope = canary + verdict round-trips + NotExecuted/ConsentGated).

## Wave 8 (2026-08-04, integrate b1a5b5f8; floor now 581/581 + 29/29)

- **SP-044 (c6 command execution):** AiCommandExecutor — generation-first per-command check (SP-019 limit 7 discharged); FromPolicy single consent source; none-admitted default + WPF divergence verbatim; type-level zero-execution + canary silence; Reserved flip LANDED; bool-door retirement blocked honestly (6 files, 3 out-of-scope — all-or-nothing condition recorded; assigned to c7). Row WIP.
- **SP-045 (ManualClock hygiene):** done, grep-proven zero assertion/wall-clock changes.
- **First ZERO-recovery wave** — no merge-stage T-5 cycles at all (T-14 hook + T-15 harness era).
- **Next: SP-046 = c7 companion UI (FIRST UI SLICE)** — improve-don't-clone decree + avalonia-live evidence + A-013 advisory; carries the bool-overload retirement.

## Wave 9 (2026-08-05, integrate 4479689a; floor now 601/601 + 33/33)

- **SP-046 (c7 companion UI):** owned modeless CompanionWindow on the REAL typed pipeline; badge truth type-computed; status from capability state; refusal bubble; memory-clear control (default-No + file deletion); consent/cooldown surfaces; panic-quiet + RE-ARM; bool-door RETIRED; avalonia-live carried the WH-class discharge (windowId silent-drop quirk recorded); K3 review PASS. **The c1–c7 slice cut is COMPLETE; the AI row's acceptance is NOT** (remaining limits on the row: Linux halves, Fallback type-level, reserved moderation rows, memory-not-consumed, none-admitted commands, §9.2 ×7, owner ratification).
- **New row: memory→prompt context (OPEN)** — the real functional gap (WPF: full dialogue history per request).
- **Next: phase-scope re-derivation consult** before further authoring (claimable inventory: prompt-context, dashboard-surface question, DTRH payload-location decision; rest owner-gated/excluded).
- Real Ollama 0.32.5 now runs on the laptop (SP-019 limit 1 stale).

## Wave 10 (2026-08-05, integrate 10f087b9; floor now 614/614 + 33/33)

- **SP-047 (memory->prompt context):** c4 store consumed (consent-gated read; wire-proven; read-gating ≠ deletion). ANTI-OVERCLAIM: recall stays owner-gated (Denied placeholder + session-only; WPF-true tension verbatim). Row WIP.
- **SP-048 (DTRH payload location):** b1's oldest open condition DISCHARGED ON WINDOWS (ratified copy-beside-exe; published boot from a MOVED dir; matrix 18/18). Publish footprint owner fact: 899 MB publish dir / 380 MB payload / 117.5 MB exe. Linux publish named limit.
- Consent-scope divergence is a board named limit now (startup load regardless of consent + ungated ReadRecent; retirement condition recorded).
- Packet-template patch `skill-trx-failure-names` added (TRX logger mandated on full-suite runs).
- Next: SP-049 = Loom studio promotion (v6.6.3 delta; dual archaeology — v6.6.3 payload changes AND b4's landed DtrhLoom).

## Wave 11 (2026-08-05, integrate 7a26a661; floor now 629/629 + 33/33)

- **SP-049 (Loom studio promotion, first v6.6.3 delta):** DtrhLoomWindow (WPF LoomHostService sibling); loom-reveal end-to-end; gifenc save round trip (byte-deterministic ×8); rack-pane limit DISCHARGED AS DRIVEN (painted screenshot = residual laptop-scale limit, zero-code-change discharge condition on a matched-scale machine); boon_pick chain fix (b3 text corrected; ChaosSfx audit row filed — full cue→chain map unaudited); dashboard entry-points row filed (reachability debt).
- **Next: SP-050 = host-obligation audit** across remaining v6.6.3 deltas (Brain Drain + Brain Melt, FX overhaul, Hourglass, Bottomless Fall, NUX, Weekly Intake Pass) — enumerate per-delta client obligations instead of blind feature packets.
- Ten consecutive auto-gate lands; four consecutive zero-recovery waves.

## Wave 13 (2026-08-11, integrate 6507361b; floor now 683/683 + 33/33)

- **SP-052 (b4 ownership-gate defects FIXED):** durMax 7200/1200 at persist AND deal (main's exact shape); Endless knob complete end-to-end; clamp matrix + five-point round-trips green; b4 tests updated+strengthened. Row WIP. Recovery: kimi-403 kill → days-parked → retry/resume both tasks (stale-failure-blocks-merge lesson).
- **SP-053 (reduced-motion probe): VERDICT = INHERITANCE HOLDS on Windows WebView2 151.0.4129.72** (engine-version-scoped; honoring mechanism not built; re-check = runtime version change). Row DONE-with-named-limits. Linux unproven.
- **OWNER INCIDENT: Run A wrote the real %APPDATA%/CcpClient profile** (APPDATA= doesn't redirect .NET GetFolderPath) — slot-1 index restored to WPF fallback defaults, purchases to []; post-run file showed the slot was unused (0 runs/0 sparks). P1 isolation row filed (real seam or backup/restore + m2test declared-fixture discipline); interim rule = backup-before-run.
- Next: SP-054 = Graded Intake web-core host (L, wave to itself).

## UPSTREAM BASELINE MOVED: v6.6.3 → v6.7.4 (2026-08-11, merge `42286638`)

The WPF reference tree on `feat/crossplatform` is no longer v6.6.3. 403 upstream commits merged
(938 files, +221k/−13k); client build 0W/0E and 683/683 green after the merge; `client/**` untouched.
**Everything about the delta is in `client/docs/upstream-sync.md`** (per-item obligations + evidence),
and the recurring procedure is the project skill `wpf-upstream-sync`.

- **New product surfaces (own rows):** Goon Game 1v1 duels (`Services/GoonGame/` + 184-file `web/goon/`
  payload), FYP desktop feed + ghost mode, Her Room companion redesign + Awareness (RECONCILE against
  the port's own c1–c7 companion), Trainer Card profile + wardrobe, Haptics v2 (SET-not-choice provider
  flags + schema-3 migration).
- **P0 parity drift on LANDED port code:** upstream now honors Assets-tree **deselection** in DTRH
  pools (`DtrhAssetManifest.EnumerateActive()`) and Graded Intake (`IntakeHostService.IsAssetActive`);
  the port's pools predate it (#762 #798 #619).
- **SP-054 was in flight at merge time and was NOT retargeted** — its v6.6.3 baseline stays internally
  consistent; the v6.7.x intake delta (new `intake/core/accents.js` +350, `ai.js` +79) is a follow-up row.
- **Guard gap found:** the client asset-manifest parity test gives ZERO signal for upstream payload
  trees the client doesn't ship yet (a 184-file tree appeared, suite stayed green).
- Merge-conflict rule: the WPF tree tracks `main` exactly (`--theirs`); `CCP.Core/` + `CCP.Avalonia.*`
  are abandoned first-attempt residue that manufacture delete/modify conflicts forever.

## Wave 14 (2026-08-11, integrate `6ce1e2ae`; floor now 795/795 + 33/33)

- **SP-054 Graded Intake web-core host LANDED** (row WIP): window + full typed bridge vocabulary (6 out / 12 in; `ping`/`payload-state` pinned never-emitted by refutation) + ISO-week pass machine + punch card (first hole free) + pure-function profiler + drafting sink (`runnable:false` per the degraded contract) + shared loom-save write path. Six headed runs incl. one added by its own consult to discharge the no-spend obligation literally (sha256 byte-identical before/after an abort on an EXISTING store). Privacy headed-verified (empty token → local stub, mic OFF, subject id local-only, media null).
- **Cross-merge drift (the land's real finding):** the v6.7.4 sync added `intake/core/accents.js` while SP-054 was in flight → each side green alone, **merged state RED**. Fixed at land (manifest entry + count tripwire 3681→3682 with the reason). Rule folded into the `wpf-upstream-sync` skill: test `base + merge orch` in a scratch worktree before approving any land that overlaps a sync.
- **Provider-500 recovery:** worker + watcher died in the same window (second occurrence of that pattern); Steps 1-2 were committed, so `salvageable:false` was a clean-worktree artifact. Read the lane BRANCH before trusting it.
- Next: wave 15 = SP-055 (P0 one active-pool definition honoring asset deselection) + SP-056 (upstream payload-tree guard).

## Wave 15 (2026-08-12, integrate `de53393a`; floor now 833/833 + 33/33)

- **SP-055 (P0 deselection contract) LANDED** (row WIP): ONE active-pool definition in `DtrhUserMedia` consumed by **three** grep-verified consumers (DTRH manifest, intake media manifest, fire-payload video pool — the row predicted two). Upstream semantics verbatim: normalization = `FlashService.GetMediaFiles`, empty-set short-circuit, unrelatable path → `true`, `UseAssetWhitelist` gate, skip-vs-deselect distinct, both-folders bound. Persisted `AssetSelectionDocument` empty until an Assets tree exists (no speculative UI).
- **SP-056 (§D guard) LANDED**: `client/docs/upstream-payload-inventory.json` (7 trees, honest dispositions) + a guard that FAILS on an unlisted/stale tree (RED demo proves it bites on `goon`). **Every future sync must update the inventory — the suite stays red until it does.**
- **Enumeration finding:** `tunnel` + `vendor` (upstream's opaque WebView2 three.js backdrop below every Topmost window, `Chaos/ChaosTunnelService.cs`) were never covered by the DTRH host row's completed b1–b5 slice cut → own P1 row + a **ratification qualifier** on the DTRH row (it cannot be ratified DONE until that resolves or the tunnel is decreed out of scope).
- Next: wave 16 candidates — Graded Intake v6.7.x delta (accents provisioning + ai.js rework + TopMarksPercent), profile-isolation seam (APPDATA trap), tunnel backdrop surface, or the big v6.7 surfaces (Goon/FYP/Her Room/Trainer Card/Haptics v2) which need decomposition first.
