# Task: SP-067 — The StopAsync completion race: a cancelled heartbeat that reports Completed

## Mission

`AsyncLifecycleTests.Heartbeat_SkipsProjectionUntilBound_ThenFlowsThroughBoundary` has now failed **twice** — SP-055 and SP-066 run 0 — with `Assert.IsType` expecting `OperationOutcome.Cancelled` and getting `Completed` at `AsyncLifecycleTests.cs:203`. Both times it fired under a diff that touched **no lifecycle code**, so it is a real race in the product's shutdown path, not test noise. It was deliberately **not** added to `allowedSkips`: quarantining it is the exact abuse the SP-066 admission rule bans.

**Fix it at the source.** This is the first row in five waves that is a defect in the *product*, not scaffolding around the tests — and it binds the async-lifecycle fault policy that the whole SP-004 contract rests on.

The row's acceptance, verbatim: *reproduce with a bounded loop, name the actual mechanism in the StopAsync completion path (why a cancelled heartbeat can report `Completed`), fix at the source. Do NOT weaken the assertion, widen a tolerance, or quarantine it.*

**Binding framings:**

(a) **The orchestrator's mechanism reading is a HYPOTHESIS to confirm or refute with a captured RED — never a finding to copy.** The reading is: `HeartbeatParticipant.TickLoopAsync` (`client/src/CcpClient.Desktop/Lifecycle/Participants.cs:84-108`) is

```csharp
while (!token.IsCancellationRequested) { ...tick...; await Task.Delay(_interval, token); }
return OperationOutcome.Completed.Instance;   // :108
```

so it has **two** exit paths. The usual one: cancellation lands *during* `Task.Delay`, which throws `OperationCanceledException`, which `AsyncOperationOwner.RunAsync` maps to `Cancelled` (`OperationRegistry.cs`, the `catch (OperationCanceledException) when (token.IsCancellationRequested)` arm). The defective one: cancellation lands *after* the `Delay` completes but *before* the `while` re-check — or before the loop's very first check — so the loop exits **normally** and returns `Completed`. That post-loop `return` is reachable **only** when the token is cancelled, which makes `Completed` there wrong by construction. **Confirm this yourself before changing a line, and if the evidence refutes it, say so and follow the evidence** — a fix that lands on an unreproduced hypothesis is a guess with a commit message.

(b) **The correct shape already exists in this repository, twice. Reuse it; do not invent a third.** `StatusTickerParticipant.TickLoopAsync` (`client/src/CcpClient.Desktop/Features/StatusTickerParticipant.cs:150-152`) ends:

```csharp
// Typed terminal outcome: observing the token at the loop check is Cancelled too —
// identical semantics to the OCE path RunAsync maps (async contract §2).
return token.IsCancellationRequested
    ? OperationOutcome.Cancelled.Instance
    : OperationOutcome.Completed.Instance;
```

and `AvatarAnimationEngine.LoopAsync` returns `Cancelled` at **every** exit including its post-loop return. `HeartbeatParticipant` — the older SP-003 site — never received this. The defect is a **single-site divergence from an already-correct in-repo pattern**, which is why the row is Size S.

(c) **The contract is the authority and it is unambiguous. Cite it; do not paraphrase it.** `client/docs/async-lifecycle-fault-contract.md` §2 line 25: *"`Cancelled` — the owner's generation was cancelled (teardown or owner stop); the operation observed the token and terminated. Not an error."* And §3 rule 4: *"In-flight operations observe the generation token and terminate with the typed `Cancelled` outcome."* A heartbeat that leaves its loop **because it observed the token** is the literal definition of `Cancelled`. The product code contradicts its own contract; the test was right both times. **Re-read both lines in the file yourself and quote them in the record** — do not trust this packet's transcription.

(d) **Why the committed test is NOT a flake, and this is the crux of the whole packet.** After the fix, *both* exit paths return `Cancelled`, so a test that stops the participant and asserts `Cancelled` is **deterministically green whichever path it takes**. Before the fix, the two paths **disagree**, which is precisely why the existing test is intermittent. So: use a **bounded loop** (framing e) to capture the RED, and commit a test that **does not depend on winning a race** — it asserts the outcome rule, not the interleaving. A committed test that needs a specific interleaving to pass is a future flake and is banned here.

(e) **The deterministic route to the defective `return` is the zero-tick path, and nothing in the suite covers it.** Start the participant and stop it *immediately*: the owner's CTS is cancelled before the `Task.Run` body reaches its first `while` check, so the loop body never runs and control falls **straight** to the post-loop `return` — the defect, with no timing window at all. Every existing loop-outcome test (`StatusTickerSliceTests.cs:40,69,89,157`, `AvatarAnimationEngineTests` `StopAndAssertCancelledAsync`, and the named red at `AsyncLifecycleTests.cs:203`) stops the participant only **after** it has ticked, so all three sites are unbound on the zero-tick path. Note honestly that "immediately" is still scheduling-dependent in the other direction (the loop may get one iteration in and exit via the OCE path) — which is exactly why it is **green either way after the fix** and why the RED probe is a bounded loop rather than a single shot.

(f) **Sweep every owned-operation loop, not just the reported one — the report names a symptom, the row wants the class.** The authoring scan found exactly **three** `while (!token.IsCancellationRequested)` loop bodies returning `Task<OperationOutcome>` (`Participants.cs:86`, `StatusTickerParticipant.cs:126`, `AvatarAnimationEngine.cs:378`), **zero** `while (true)` loops, and **12** `Task<OperationOutcome>`-returning methods in `client/src/**`. **Re-derive this yourself**; treat the numbers as a magnitude to reconcile against, not as input. Every owned operation whose loop can exit by observing the token must return `Cancelled` on that path. Report the disposition of **each** `Task<OperationOutcome>` method you examined, including the ones that are correct and why.

(g) **Verify nothing depends on the old wrong value BEFORE you change it.** The fix flows through `OperationRegistry.Complete` → `SetOutcome` → `AsyncOperationOwner.LastOutcome`, so a test or product path asserting `Completed` after a heartbeat teardown would flip. There are **49** `OperationOutcome.Completed` assertions across `client/tests/**`; an authoring grep found none tied to the heartbeat, but that is a starting point, not a clearance. Grep `LastOutcome`, `Completion`, and `Completed` against every heartbeat and teardown path and state the result. If something *does* depend on it, that is a finding to report — not a reason to soften the fix.

(h) **Never weaken, quarantine, or allowlist.** Do not touch the assertion at `AsyncLifecycleTests.cs:203` except to keep it exactly as strict. Do not add any test to `allowedSkips`. Do not add a retry, a tolerance, a `Task.Delay` sleep, or a "wait for it to settle" to make a red go away. If you cannot fix it, report that honestly and leave it red — a named red is worth more than a green that lies.

(i) **THE FIVE PINNED SKIPS ARE CORRECT. DO NOT "FIX" THEM.** `client/tests/floor/floor.json` pins **5** fully-qualified names in `allowedSkips`; on this Windows machine exactly **2** of them skip (`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`, `ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps` — both Linux-gated) and the other 3 execute. Before SP-066 those tests early-`return`ed and were counted as **passes**, so the old "0 skipped" floor scored vacuity as green. **Driving the skip count back to 0 regresses the honesty SP-066 landed.** Expected on this machine: `900 unit / 35 headless / 2 skipped`.

(j) **Floor discipline (`floor.json` `bumpRule`).** Floor at authoring: **900 unit / 35 headless / 2 skipped on Windows, build 0W/0E** (SP-066, integrate `29950e9b`). This packet **adds** facts. Bump `total` in the **same commit** as the tests that move it, with the reason in the message. Never widen, disable, or special-case the floor to make one of your own steps pass. Do not touch `allowedSkips`, `admissionRule`, or `skipSemantics`.

(k) **If the named flake `ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery` fires in any run, record it by name with the run number and TRX path.** It guards a privacy boundary and has its own row; never retry it away, never quarantine it, never list it.

(l) **Never export `CCP_DATA_ROOT` process-wide for a suite run** (`client/docs/port-workflow.md:204`). It makes the SP-057 pin skip and the floor goes blind — the vacuous-green class SP-062 closed.

(m) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, or `client/docs/upstream-sync.md`.** Name intended filings in `record.md`; the orchestrator reconciles at land.

(n) **Product code is in scope — narrowly.** Unlike SP-066, this packet edits `client/src/**`. That licence covers the loop-exit outcome sites and nothing else: no opportunistic refactor, no renaming, no "while I was in there". Every product line you touch must be traceable to framing (a), (b), or (f).

## Dependencies

- **Task:** SP-066 (landed `29950e9b`) — the floor wrapper, the name-anchored `allowedSkips` pin, and the honesty rules this packet must not regress.
- SP-004 / SP-003 — `client/docs/async-lifecycle-fault-contract.md` and the `OperationRegistry` / `AsyncOperationOwner` / `HeartbeatParticipant` machinery this packet fixes.

## Context to Read First

- `client/src/CcpClient.Desktop/Lifecycle/Participants.cs` — `HeartbeatParticipant.TickLoopAsync`, the defect site (`:84-108`)
- `client/src/CcpClient.Desktop/Lifecycle/OperationRegistry.cs` — `AsyncOperationOwner.RunAsync`'s OCE→`Cancelled` mapping, `Complete`/`SetOutcome`/`LastOutcome`, and the generation rules
- `client/src/CcpClient.Desktop/Features/StatusTickerParticipant.cs:126-152` — **the correct shape to reuse** (framing b)
- `client/src/CcpClient.Desktop/Features/AvatarTube/AvatarAnimationEngine.cs:378+` — the second correct site; returns `Cancelled` at every exit
- `client/docs/async-lifecycle-fault-contract.md` §2 and §3 — the authority (framing c); read the lines, do not trust this packet's quotes
- `client/tests/CcpClient.Tests/AsyncLifecycleTests.cs:177-204` — the named red
- `client/tests/CcpClient.Tests/StatusTickerSliceTests.cs:40-90,154-157` and `client/tests/CcpClient.Tests/AvatarAnimationEngineTests.cs` (`StopAndAssertCancelledAsync`) — the existing loop-outcome bindings, and the shape your new zero-tick facts should match
- `client/tests/CcpClient.Tests/TestWait.cs` — the shared wait/budget helper; **add no waits outside it** (SP-063 discipline)
- `client/tests/floor/floor.json` and `client/tests/floor/check-floor.mjs` — the floor, its `bumpRule`, and the 5 pinned skip names (framings i, j)
- `client/docs/port-workflow.md` — §Verification floor and the `CCP_DATA_ROOT` rule at :204
- `docs/constitution.md` — standing orders

## File Scope

- `client/src/CcpClient.Desktop/Lifecycle/Participants.cs` (the fix)
- `client/src/CcpClient.Desktop/Features/StatusTickerParticipant.cs`, `client/src/CcpClient.Desktop/Features/AvatarTube/AvatarAnimationEngine.cs` (**only if** the sweep finds a real divergence — otherwise dispositioned in the record, unmodified)
- `client/tests/CcpClient.Tests/AsyncLifecycleTests.cs`, `client/tests/CcpClient.Tests/StatusTickerSliceTests.cs`, `client/tests/CcpClient.Tests/AvatarAnimationEngineTests.cs` (the zero-tick bindings)
- `client/tests/floor/floor.json` (count bump only — framing j)
- `spine-tasks/SP-067-stopasync-completion-race/**`
- **NOT in scope:** any other path under `client/src/**`, `client/tools/**`, `client/spikes/**`, `ConditioningControlPanel/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`, `.pi/**`

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Lifecycle/Participants.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/spikes/**`, `client/tools/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**` |
| artifactsMustExist | `spine-tasks/SP-067-stopasync-completion-race/record.md` |

`check-floor.mjs` is `--no-build` by design, so the explicit `dotnet build` ahead of it is **not optional** — standalone the wrapper measures the last build, not the working tree, and fails closed naming the wrong cause (SP-065 land finding). `FloorWrapperGuardTests` binds every packet with ID >= SP-065: **never** call `dotnet test` outside the wrapper.

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. Scored: blast radius 2 (the async-lifecycle contract underpins every background participant), novelty 0 (the fix pattern already exists in-repo twice), security 0, reversibility 0 → Level 1 by arithmetic, **raised to 2 deliberately**: this is the first product-code defect fix in five waves and it changes a terminal-outcome value that other components read. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in `record.md`. **Authoring rule (SP-034 defect): `grep -c "Review Level" PROMPT.md` >= 2 before launch.** Review Level 2 applies to every step below.

## Steps

### Step 1: Reproduce the race and name the mechanism — RED before any fix

- [ ] Update STATUS.md before starting work
- [ ] Read the contract lines in framing (c) **in the file** and quote them in `record.md` with their line numbers
- [ ] Build a **bounded-loop probe** that drives `HeartbeatParticipant` start→stop repeatedly (bounded iterations, no unbounded spin, no wall-clock sleep outside `TestWait`) and records the observed terminal outcome per iteration. Run it against the **unmodified** product code
- [ ] **Capture the RED**: at least one iteration reporting `Completed` where the contract requires `Cancelled`, saved under `evidence/` with the iteration count and the observed hit rate. This is the packet's core evidence — a fix without it is a guess
- [ ] **Name the mechanism precisely**: which exit path produced `Completed`, why the token was already cancelled at that point, and why the OCE path did not fire. Confirm or **refute** framing (a) explicitly; if the evidence refutes it, follow the evidence and say so
- [ ] State whether the zero-tick path of framing (e) reaches the defect deterministically, with the measurement that shows it
- [ ] **Pre-approach solo consult** (`mode: "solo"` — a bare `consult` hits the council-roster trap, T-7; Opus 5 main, Fable 5 fallback). Verdict + **ACTUAL answering model** in `record.md`. **The tool has repeatedly returned reasoning-only or mid-sentence-truncated verdicts (waves 17, 21, 22, 23) — ask narrowly, cap the reply length, record exactly what surfaced, and never stitch a verdict out of reasoning**

### Step 2: Fix at the source, and sweep the class

- [ ] Fix `HeartbeatParticipant.TickLoopAsync`'s post-loop return using the **existing in-repo shape** (framing b), with a comment citing the contract section — matching `StatusTickerParticipant.cs:150-152`
- [ ] Re-run the Step-1 probe against the fixed code: the `Completed` outcome must be **gone** across at least the iteration count that produced the RED. Save the GREEN beside the RED
- [ ] **Sweep** every `Task<OperationOutcome>`-returning method in `client/src/**` (framing f). Disposition **each** one in the record: correct-and-why, or divergent-and-fixed. Re-derive the counts; reconcile against framing (f)'s magnitudes and state the reconciliation
- [ ] **Framing (g) clearance**: grep `LastOutcome` / `Completion` / `Completed` against heartbeat and teardown paths; state what depends on the outcome value and confirm nothing regressed. Report any real dependency as a finding
- [ ] Zero behavior changed beyond the outcome value on the cancellation exit path — no renames, no refactors, no unrelated edits (framing n). Summarize the `git diff` per product file in the record

### Step 3: Bind the class so it cannot return

- [ ] Add a **zero-tick fact** at each of the three loop sites (framing e): start the participant, stop it immediately, assert the owned completion is `Cancelled`. These must be deterministic **either way after the fix** (framing d) — no interleaving dependency, no sleeps, no retries
- [ ] Each new fact states in a comment what breaks it (the defective `return Completed` shape returning), so a future reader knows it is a regression pin and not ceremony
- [ ] Confirm the existing loop-outcome tests still assert exactly what they asserted before — **zero assertions weakened, zero tolerances widened** (framing h); prove it with a per-file `git diff` summary in the record
- [ ] Bump `floor.json` `total` in the **same commit** as the new facts, reason in the message (framing j). `allowedSkips`, `admissionRule`, and `skipSemantics` untouched
- [ ] The named red `AsyncLifecycleTests.Heartbeat_SkipsProjectionUntilBound_ThenFlowsThroughBoundary` is byte-identical except where the packet explicitly justifies otherwise

### Step 4: Record + pre-completion consult

- [ ] `record.md`: the quoted contract lines; the bounded-loop probe with its RED and its post-fix GREEN (iterations + hit rates); the named mechanism with the confirm/refute verdict on framing (a); the sweep table with a disposition per `Task<OperationOutcome>` method and its magnitude reconciliation; the framing-(g) clearance; the new facts and what breaks each; the floor bump with its reason; the run table with exact counts and skipped names; consults + **ACTUAL answering models**; engine-review presence per step; intended board filings (state them, set no row state)
- [ ] **Honesty cell — required.** At minimum: (1) how many iterations the RED needed and therefore what the probe does and does **not** bound about the real-world hit rate; (2) that the zero-tick fact is deterministic **because both paths now agree**, not because the interleaving is controlled — so it pins the outcome rule, not the scheduler; (3) whether any `Task<OperationOutcome>` method was dispositioned by reading rather than by executing it; (4) that this closes **no product capability** — it removes a lying outcome from an existing one; (5) **Linux unproven** (zero WSL distros on this machine — do not fake a Linux run)
- [ ] If the named flake fired in any run, it is recorded by name with run number and TRX path, and was **not** retried away (framing k)
- [ ] **Pre-completion solo consult**; verdict + actual model in `record.md`
- [ ] STATUS.md accurate before `.DONE`; board row updated per ENABLER 2 rules (name filings only — set no state)

### Step 5: Testing & Verification

- [ ] Contract testCommand passes **through the wrapper** (`verify.mjs` exit 0, build 0W/0E, new exact unit count / 35 headless, skip set exactly the 2 Windows-observed pinned names)
- [ ] **5 consecutive full-suite greens**, >= 1 a **fresh-checkout first-ever build** ("cold" is a NEW worktree, not a rebuild in place — port-lessons 2026-08-12). More than the usual 3 **because this packet's subject is an intermittent race**: three greens on a race that historically fires once in ~15 runs bounds very little, and the record must say what 5 bounds too (framing: honesty cell item 1). Per-run table: run, worktree, cold/warm, unit + headless counts, skipped names, TRX path
- [ ] The previously-failing test is named in every run's TRX with `outcome="Passed"`
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
- [ ] `git status --porcelain --ignored=matching -uall` shows **no new ignored artifact** produced by any run (the wrapper writes outside the worktree — do not regress it)

## Completion Criteria

- The race is **reproduced with a captured RED against unmodified product code**, and the mechanism is named precisely with framing (a) confirmed or refuted on evidence
- The fix is at the source, uses the existing in-repo shape, and cites the contract section that requires it
- Every `Task<OperationOutcome>` method in `client/src/**` is dispositioned in the record — correct-and-why, or divergent-and-fixed
- The zero-tick path is bound by a deterministic fact at all three loop sites, each stating what breaks it
- Zero assertions weakened, zero tolerances widened, zero tests quarantined, nothing added to `allowedSkips`, the 5 pinned skip names untouched
- `floor.json` `total` bumped in the same commit as the facts that moved it, with the reason in the message
- 5 consecutive full-suite greens at the stated exact counts, >= 1 fresh-checkout first-ever build, the named test `Passed` in every TRX
- The record states what 5 greens on an intermittent race do **not** prove

## Do NOT

- Weaken, retry, quarantine, or allowlist the named red — or any other test (framing h)
- Add the failing test, or any test, to `allowedSkips`; touch `admissionRule`, `skipSemantics`, or the 5 pinned names (framings h, i, j)
- "Fix" the 2 Windows-observed skips or drive the skip count to 0 — that regresses SP-066's honesty (framing i)
- Land a fix without a captured RED against unmodified product code (framing a)
- Commit any test that depends on winning a race, or that needs a sleep, retry, or tolerance to pass (framing d)
- Add waits outside `TestWait`, or a timeout literal that trips SP-063's `"Timeout = TimeSpan."` guard token without a marker + pin
- Refactor, rename, or otherwise edit product code beyond the loop-exit outcome sites (framing n)
- Widen, disable, or special-case the floor to make one of your own steps pass; bump without stating the reason in the same commit (framing j)
- Call `dotnet test` outside `check-floor.mjs` (`FloorWrapperGuardTests` binds this packet)
- Export `CCP_DATA_ROOT` process-wide for a suite run (framing l)
- Create any new file under `client/tools/` (`.gitignore:168` — it would pass in-lane and vanish from the merged tree)
- Edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`; set any board row state
- Use `consult` council mode (T-7: solo only)
- Run `git clean -fdX` repo-wide in the lane — it deletes the T-14-staged `.pi/npm` that `verify.mjs` needs; clean **scoped** (`git clean -fdX -- client/`) and only after the contract passes
- Commit build output (`bin/`, `obj/`) or any results file

## Git Commit Convention

- `feat(SP-067): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `spine-tasks/SP-067-stopasync-completion-race/record.md`, `STATUS.md`
**Check If Affected:** `client/docs/async-lifecycle-fault-contract.md` (**read-only for this packet** — if the fix reveals the contract itself is wrong or ambiguous, report it in `record.md` as a finding for the orchestrator; do not edit it)
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`

## Amendments

- 2026-08-13 (authoring, orchestrator): **wave 24 runs this row ALONE.** Four other claimable rows exist (the `Assert.All` shape sweep, the mechanical `allowedSkips` ban test, T-17's auditor run, the named privacy flake) and all four are **suite-hardening**, deliberately held back. Two reasons, both binding: the owner was asked in the wave-23 digest for the **ratio** of suite-trustworthiness work to WPF parity work and has not answered — bundling those rows now would pre-empt a decision that is theirs; and mixing a product-code fix with test-tooling changes in one batch muddies the floor delta, so a count move could not be attributed cleanly to either.
- 2026-08-13 (authoring, orchestrator): **decomposition consult (solo, Opus 5), verdicts encoded.** (1) The mechanism reading was confirmed as correct **and** as something the worker must re-derive rather than inherit → framing (a). (2) Use the ternary matching `StatusTickerParticipant` for consistency even though the `Completed` branch is dead on that path → framing (b). (3) A near-zero interval is **not** true determinism — it only widens the odds; the honest deterministic route is the **zero-tick path** (token already cancelled before the first `while` check), and the bounded-loop probe stays the RED evidence while the committed test asserts the outcome rule → framings (d), (e). (4) A lexical guard over `Task<OperationOutcome>` loop bodies was **rejected as scope creep** for a Size-S row — lexical detectors have proven fragile here (SP-066's own named limit) — in favour of behavioral zero-tick facts at the three sites → Step 3. (5) Single lane. (6) Two checkable claims the advisor raised were **verified before encoding, not trusted**: the contract §2/§3.4 wording (read at `async-lifecycle-fault-contract.md:25` and `:40`) and whether anything depends on the heartbeat reporting `Completed` (authoring grep found nothing; the worker must still clear it → framing g).
- 2026-08-13 (authoring, orchestrator): **sweep magnitudes in framing (f) are orchestrator-measured, not authoritative** — a crude grep over `client/src/**` at authoring time. The worker re-derives. The same applies to the "49 `OperationOutcome.Completed` assertions" figure in framing (g).
- 2026-08-13 (authoring, orchestrator): floor at authoring is **900 unit / 35 headless / 2 skipped on Windows (5 names pinned), 0W/0E** (SP-066, `29950e9b`). Note the asymmetry deliberately: `allowedSkips` carries **5** fully-qualified names, of which 3 execute and 2 skip on a Windows box. A worker reading "2 skips" and finding 5 names has not found a defect.
- 2026-08-13 (authoring, orchestrator): **Size S.** The fix itself is ~3 lines at one site; the packet's weight is evidence — reproducing an intermittent race, sweeping the class, and 5 greens. Not split: the RED probe, the fix, and the binding facts are one semantic unit, and shipping them apart would leave either an unproven fix or a red pin with no fix.
- 2026-08-13 (authoring, orchestrator): **`spine preflight` warns "Pre-landed contract risk: SP-067 has fileScopeMustChange paths already changed on main" — it is a FALSE POSITIVE and its suggested redirect is ACTIVELY WRONG here. Do not act on it.** Proven at authoring: the preflight check hardcodes `main` as its comparison base (`validate-prompt.mjs:196`, `options.baseRef ?? "main"`, and the preflight caller passes none), but **`main` is the still-shipping WPF product branch and carries no `client/` tree at all** (`git ls-tree origin/main` → zero `client` entries). Every `client/**` path therefore reads as "changed on main" forever, so **every port packet with a product-code `fileScopeMustChange` will trip this warning permanently**. The contract verifier does **not** use that base — it uses `baseBranch` from `.spine/spine-config.json`, which is `feat/crossplatform`, and `git diff <prompt-intro-commit>..feat/crossplatform -- client/src/CcpClient.Desktop/Lifecycle/Participants.cs` is **empty**, so `isPrelandedFileScopeSatisfied` returns false and this contract is enforced for real. Following the warning's hint ("redirect fileScopeMustChange to delivery artifacts") would replace a real product-code requirement with a `record.md` requirement — i.e. it would **manufacture the vacuous contract** where a worker passes by writing only docs (the SP-214/SP-457 class). Same shape as T-12, where spine's headline remediation was dangerous in this repo. **`fileScopeMustChange` stays pointed at `Participants.cs`.**
- 2026-08-13 (authoring, orchestrator): machine posture — laptop; **zero WSL distros, so Linux is a standing named gate** (do not fake a Linux run); **MCP not re-probed this phase — treat as a named limit, never a blocker**. This packet touches no AXAML, so the A-013 advisory step is not a gate for it. **`## Review Level: 2` heading present + grep-verified >= 2 (SP-034 authoring rule).**
