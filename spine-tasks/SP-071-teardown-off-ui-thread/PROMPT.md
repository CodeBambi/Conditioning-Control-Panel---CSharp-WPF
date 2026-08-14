# Task: SP-071 — Host close must not wait on a wedged native audio probe

## Mission

Wave 27 (SP-070) made the audio session-disable expire, and to do it safely it added `_initLock`, a lock
serializing every backend device call — because the recovery probe is the port's **first cross-thread
`IAudioBackend` access** and that seam has no internal synchronization. That lock was right and it stays.
**This packet fixes where the blocking moved.**

`SoundArbitration.Dispose` takes `_initLock` (`SoundArbitration.cs:1087-1091`) before
`_backend.Dispose()` (`:1093`), and `TeardownBarkPipeline` (`DtrhHostWindow.axaml.cs:255-262`) is called
from the host-window close handler (`:153`) — **on the UI thread**. The only situation in which a probe is
in flight is a **dead endpoint**, which is precisely the failure this feature exists for. So a wave whose
central constraint was "the play seam must never block on a native device call" reintroduced an unbounded
UI-thread block at **teardown**, in exactly the scenario it was built to survive. The DTRH host is a
non-modal child window (`DtrhLaunchCoordinator.cs:167` `window.Show(_owner)`), so this is not a
process-exit path where a hang is survivable — the whole app's dispatcher stops while a native init that
already failed to answer keeps not answering.

**This is WPF parity, not an invention.** `5a168554` ("stop the UI thread joining a wedged render thread,
and name the next one") is upstream's pass over exactly this class for the v6.6.3 hang cluster
(`#775`/`#777`/`#779`/`#780`): the dispatcher stops mid-session, nothing is logged, the user kills the app.
Its remedy shape is the one to port: **bound the UI thread's waits, degrade instead of blocking, and name
the native calls that cannot be bounded.** It is also the port's own standing rule —
`async-lifecycle-fault-contract.md` §5 makes the UI dispatch boundary **post-only** *"so that no operation
can wait on the UI thread"*, and `IUiDispatch` has no awaitable method by construction.

**THE TRAP THAT DECIDES THE DESIGN, and it is not the obvious one.** The obvious fix — put a timeout on
the `_initLock` acquisition and continue when it expires — is **WRONG and dangerous**: continuing runs
`_backend.Dispose()` while a native init is still in flight, which is the concurrent-native-call class
`_initLock` exists to prevent, i.e. the process-fatal outcome, reached by the very code meant to make
teardown safer. **The fix is not a timeout on the lock; it is moving the teardown off the UI thread.** The
backend teardown runs on a background thread that may wait on `_initLock` for as long as it takes; the UI
caller gets a **bounded** wait with a typed give-up that **never touches the backend**. Exactly one thread
ever disposes the backend, and never concurrently with an in-flight native call.

**And the give-up must not leak.** The DTRH host can be closed and reopened without the process exiting, so
"we are dying anyway" is not available: after a give-up, the backgrounded teardown must still complete and
still dispose the backend exactly once, and a subsequent host open must not inherit a half-torn-down owner.

**Scope is site (1) only — this class has two more members and they are a different packet.**
`SoundFlowAudioBackend.CreatePlayer` (`:108` → `OffSyncContext.Run`, `AudioSeams.cs:150`) and
`SoundFlowDtrhAudio.CreatePlayer` (`:100`) block the calling thread — which SP-070 established can be the
UI thread — inside a native `AssetDataProvider` construction, unbounded. **Do not fix them here:** they
change a *synchronous* seam contract (`CreatePlayer` returns an `IAudioPlayer`), and bounding them creates
an **orphan** hazard — a late-completing construction adds itself to `MasterMixer` (a ghost play plus a
leak) and disposing it races device teardown. That is its own packet whose central acceptance is orphan
disposal. **This packet's census names them with verdicts and the orchestrator files the row at land.**

## Dependencies

- **Task:** SP-070 (landed `9e6498b6`) — `_initLock`, the recovery probe, and the floor this packet moves.
- SP-029 / SP-025 — the landed arbitration core and the SoundFlow backend seam.
- SP-004 — `async-lifecycle-fault-contract.md`, whose §5 post-only rule this restores in spirit.

## Context to Read First

- `client/src/CcpClient.Desktop/Audio/SoundArbitration.cs` — **the only product file.** `Dispose`
  (`:1070-1094`: the `_gate` block, the timer cancel, the `_initLock` acquisition at `:1087-1091`,
  `PanicReset()` and `_backend.Dispose()` at `:1092-1093`), `_initLock`'s comment block (`:106-115`),
  `RunRecoveryProbe`, `Initialize`/`InitializeCore` (the `_tornDown` early return that makes a post-teardown
  probe harmless), `PanicReset` (`:541`)
- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhHostWindow.axaml.cs:138-160` and `:255-262` — the close
  handler and `TeardownBarkPipeline`, **the UI-thread caller**. Note its store waits are already bounded
  (2s) — that is the shape of an existing local answer. **Read-only, out of File Scope**
- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhLaunchCoordinator.cs:160-170` — `window.Show(_owner)`:
  the host is a **non-modal child window**, so close is not process exit. **Read-only**
- `client/src/CcpClient.Desktop/Lifecycle/UiDispatch.cs` — the port's post-only UI boundary and the reason
  it is post-only (contract §5). **Read-only: this packet must not add an awaitable dispatch**
- `client/docs/async-lifecycle-fault-contract.md` §5 (UI dispatch boundary) and §2/§3.4 (typed outcomes,
  cancellation) — **read-only; if wording is owed, name it in `record.md` and do not edit the file**
- `client/tests/CcpClient.Tests/SoundArbitrationTests.cs` — `Make` (`:18`), `FakeBackend` (`:465`),
  `ManualClock` (`:551`), and SP-070's `Recovery_Teardown_NoProbeAfterDispose_Ever` (`:529`) which must
  stay green and unchanged in meaning
- `ConditioningControlPanel/` — WPF `5a168554` as behavioral evidence for the **remedy shape only**
  (bounded budgets, degrade instead of block, breadcrumb the unboundable). Its sites are WPF-specific
  (`WriteableBitmap.Lock`, `RunOnUISync` under a lock); **do not port its mechanics**
- `client/tests/floor/floor.json` and `client/tests/floor/check-floor.mjs` — the floor and its `bumpRule`
- `client/docs/port-workflow.md` — §Verification floor and the `CCP_DATA_ROOT` rule at `:204`
- `docs/constitution.md` — standing orders

## File Scope

- `client/src/CcpClient.Desktop/Audio/SoundArbitration.cs` — **the only product file**
- `client/tests/CcpClient.Tests/SoundArbitrationTests.cs`
- `client/tests/floor/floor.json` (count bump only)
- `spine-tasks/SP-071-teardown-off-ui-thread/**`
- **NOT in scope:** every other path under `client/src/**` — in particular `AudioSeams.cs`,
  `SoundFlowAudioBackend.cs`, `Features/Dtrh/**` and `Lifecycle/UiDispatch.cs` — plus `client/tools/**`,
  `client/spikes/**`, `ConditioningControlPanel/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`,
  `.pi/**`

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Audio/SoundArbitration.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/spikes/**`, `client/tools/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`, `.pi/**` |
| artifactsMustExist | `spine-tasks/SP-071-teardown-off-ui-thread/record.md` |

`check-floor.mjs` is `--no-build` by design, so the explicit `dotnet build` ahead of it is **not optional** —
standalone the wrapper measures the last build, not the working tree, and fails closed naming the wrong
cause (SP-065 land finding). `FloorWrapperGuardTests` binds every packet with ID >= SP-065: **never** call
`dotnet test` outside the wrapper.

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. Scored: blast radius 2 (teardown semantics of the app-wide
arbitration core, reached from a live window's close handler; a mistake here is the **process-fatal**
concurrent-native-call class), pattern novelty 2 (the port has no background-teardown handoff anywhere —
its only dispatch boundary is deliberately post-only, one-way), security 0, reversibility 0 → **Level 2**.
**T-2 heading format is load-bearing** — record engine-review presence/absence per call in `record.md`.
**Authoring rule (SP-034 defect): `grep -c "Review Level" PROMPT.md` >= 2 before launch.** Review Level 2
applies to every step below.

## Steps

### Step 1: Prove the block, census the class, then design the handoff

- [ ] Update STATUS.md before starting work
- [ ] **Prove the defect before fixing it:** write a bounded probe (not a committed test) in which a
      `FakeBackend` blocks inside `TryInit` while another thread calls `Dispose`, and **capture the RED** —
      `Dispose` does not return while the native call is parked. Save it under `evidence/`. **A fix without
      a captured pre-fix observation is the class SP-067 closed**
- [ ] Re-derive the caller chain yourself and record it: which thread calls `Dispose`, from where, and
      whether the process survives that window closing. Cite the lines you found, not the ones given above
- [ ] **CENSUS (evidence, not a fix): classify every blocking wait in `client/src/**`** — the ~30 sites
      matching `.Wait(` / `.Result` / `.GetAwaiter().GetResult()` / `.Join()`. Per site: file:line, the
      thread(s) that can reach it, bounded or unbounded, what it waits on (disk, native, in-process task),
      and the consequence if the wait never returns. **Fix nothing outside File Scope**; the census is the
      deliverable that lets the orchestrator file the follow-up rows. Name explicitly the two sites this
      packet deliberately leaves alone (`SoundFlowAudioBackend.CreatePlayer` → `OffSyncContext.Run`, and
      `SoundFlowDtrhAudio.CreatePlayer`) and **why they are a separate packet** (they change a synchronous
      seam contract and carry an orphan-disposal hazard)
- [ ] **Design the handoff before writing it, and write the invariant first:** exactly one thread ever
      disposes the backend; the backend is never disposed while a native call is in flight; the UI caller's
      wait is **bounded**; the give-up path **never touches the backend**; and after a give-up the
      backgrounded teardown still completes and still disposes exactly once. State how `Dispose` stays
      **idempotent** and what a second call does
- [ ] **State plainly why a timeout on `_initLock` is the wrong fix** (continuing past it runs
      `_backend.Dispose()` concurrently with a native init — the process-fatal class `_initLock` exists to
      prevent). If your design contains any path that proceeds to the backend after failing to acquire
      `_initLock`, **stop and report** — that is a stop condition, not a trade-off
- [ ] Decide what the UI-side budget is and justify the number from something in this repository (the
      existing 2s store waits in `TeardownBarkPipeline` are the local precedent), and where it lives
      (`SoundArbitrationOptions`, beside SP-070's knobs). **No wall clock in tests** — the injected
      `ISoundClock` and the test `ManualClock` exist
- [ ] Decide and record what happens to a **reopened host** after a give-up: the old owner is torn down and
      the new one constructs a fresh backend, so state what (if anything) can overlap, and whether two
      backends can exist momentarily
- [ ] **Pre-approach solo consult** (`mode: "solo"` — a bare `consult` hits the council-roster trap, T-7;
      Opus 5 main, Fable 5 fallback). Verdict + **ACTUAL answering model** in `record.md`. **The tool has
      repeatedly returned reasoning-only or mid-sentence-truncated verdicts (board row T-18) — ask narrowly,
      cap the reply length, record exactly what surfaced, and never stitch a verdict out of reasoning.** An
      unstitched non-verdict is a MISSING consult: re-ask it

### Step 2: Implement the handoff in one file

- [ ] `Dispose` does its UI-safe work as it does today (mark torn down under `_gate`, cancel the pending
      recovery timer, clear the in-flight flag) and hands the **backend teardown** to a background thread
- [ ] The UI caller waits **bounded** for that teardown and returns on expiry with a **typed, once-logged**
      give-up. **On the give-up path it must not touch `_backend` in any way**
- [ ] The background teardown takes `_initLock`, waits as long as the native call needs, and disposes the
      backend **exactly once** — including when the UI side already gave up
- [ ] `Dispose` remains **idempotent**: a second call neither disposes the backend twice nor starts a second
      teardown, and returns promptly
- [ ] `PanicReset`'s placement in teardown is decided deliberately, not by accident: state whether it stays
      on the calling thread (it stops and disposes **players**, which is not what `_initLock` guards) and
      why that is safe with the backend teardown in flight
- [ ] Everything SP-070 landed still holds: no recovery after teardown, no probe scheduled post-`Dispose`,
      the play seam still never takes `_initLock`, and the lock order stays one-way
- [ ] **Transition-only logging.** One line when teardown is handed off past the budget, one when the
      backgrounded teardown completes. Never a line per call. **Nothing new observed, persisted, logged as
      user data, or transmitted** — grep your own diff for new log/diagnostic/persist/network calls and show
      the result in the record
- [ ] No new dispatch primitive, no awaitable UI dispatch, no `SynchronizationContext.Current` capture
      (contract §5 rules 1-2, §5.1: re-admitting awaitable dispatch needs a real consumer and a re-answered
      deadlock question — this is not that packet)
- [ ] Summarize the `git diff` for the product file in the record; confirm no edit outside File Scope

### Step 3: Bind the behavior, one source at a time

- [ ] **The defect fact:** a backend parked inside `TryInit` + `Dispose` from another thread → `Dispose`
      returns within the budget. This is the pin that would have caught today's behavior
- [ ] **The safety fact (the one that matters most):** the backend is **not** disposed while the native call
      is in flight — assert the ordering directly (the fake records the moment its `TryInit` returns and the
      moment `Dispose` is called on it), not merely that "nothing threw"
- [ ] **The completion fact:** after the UI side gives up, the backgrounded teardown still runs and disposes
      the backend **exactly once** when the native call finally returns
- [ ] **Idempotence:** two `Dispose` calls → one backend dispose, one teardown, both return promptly
- [ ] **Negative control — ordinary teardown is unchanged:** with no probe in flight, `Dispose` disposes the
      backend exactly once with the same observable outcome as before this packet, and logs no give-up line
- [ ] SP-070's teardown fact (`Recovery_Teardown_NoProbeAfterDispose_Ever`) and every other landed
      arbitration fact stay green and **unchanged in meaning** — prove it with a per-file `git diff` summary
- [ ] **BITE TEST, one source at a time:** revert the off-thread handoff alone → the bounded-return pin goes
      red; revert the give-up's don't-touch-the-backend guard alone (or the single-dispose latch) → only its
      own pin. Capture each RED under `evidence/` naming the reverted line and confirming the others stayed
      green. **A shared revert is not acceptable evidence** (SP-067), and **check that each pin's fixture
      actually reaches the mechanism** — SP-070's single-flight pin passed with its guard reverted until its
      fixture was corrected
- [ ] No wall-clock waits, no `Thread.Sleep`, no `Task.Delay` in the committed facts. Cross-thread
      rendezvous uses explicit synchronization primitives, not timing. Add no waits outside `TestWait`
- [ ] Bump `floor.json` `total` in the **same commit** as the new facts, reason in the message.
      `allowedSkips`, `admissionRule`, `skipSemantics` untouched

### Step 4: Record + pre-completion consult

- [ ] `record.md`: the captured pre-fix RED; the caller chain re-derived with your own cites; **the full
      census table** with a verdict per site; the invariant and the design that satisfies it; why a lock
      timeout is the wrong fix; the budget with its in-repo justification; the reopened-host answer; the
      **bite matrix**; the floor bump with its reason; the run table with exact counts and skipped names;
      consults + **ACTUAL answering models**; engine-review presence per step; intended board filings (state
      them, set no row state) — including the row for the two `CreatePlayer` sites this packet leaves alone
- [ ] **Honesty cell — required.** At minimum: (1) what is proven is that the UI caller returns bounded and
      the backend is disposed exactly once and never concurrently — **not** that a real wedged native audio
      call behaves as the fake does (name the manual gate); (2) whether the give-up leaves any observable
      residue for a reopened host, stated plainly rather than assumed away; (3) which behavior was verified
      by execution vs by reading; (4) **Linux unproven** (zero WSL distros — do not fake a Linux run);
      (5) the two `CreatePlayer` sites remain unbounded on the UI thread after this packet — this closes one
      member of the class, not the class
- [ ] If the named flake (`ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery`) fired in
      any run, it is recorded by name with run number and TRX path, and was **not** retried away
- [ ] **Pre-completion solo consult**; verdict + actual model in `record.md`
- [ ] STATUS.md accurate before `.DONE`; intended board filings named per ENABLER 2 (set no state)

### Step 5: Testing & Verification

- [ ] Contract testCommand passes **through the wrapper** (`verify.mjs` exit 0, build 0W/0E, new exact unit
      count / 35 headless, skip set exactly the 2 Windows-observed pinned names)
- [ ] **3 consecutive full-suite greens**, >= 1 a **fresh-checkout first-ever build** ("cold" is a NEW
      worktree, not a rebuild in place). Per-run table: run, worktree, cold/warm, unit + headless counts,
      skipped names, TRX path
- [ ] **The cross-thread facts are run repeatedly** (>= 20 iterations of the teardown class, filtered) with
      zero flakes, and the count is stated — a concurrency pin that passes once proves less than one that
      passes twenty times
- [ ] The bite matrix is complete: each revert named with the pins it reddened and the pins that stayed green
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
- [ ] `git status --porcelain --ignored=matching -uall` shows **no new ignored artifact** produced by any run

## Completion Criteria

- The UI caller's `Dispose` returns within a bounded budget even with a native init parked, proven by a pin
  that captures today's behavior as RED first
- The backend is never disposed while a native call is in flight, and is disposed **exactly once** — after a
  give-up as well
- `Dispose` is idempotent and ordinary teardown is observably unchanged (negative control)
- No path proceeds to the backend after failing to acquire `_initLock`
- Every SP-070 fact stays green and unchanged in meaning; the play seam still never takes `_initLock`
- The census classifies every blocking wait in `client/src/**` with thread, boundedness, and consequence,
  and names the two `CreatePlayer` sites as a separate packet with the orphan hazard stated
- Each behavior is bound by its own revert, and each pin's fixture is shown to reach its mechanism
- Zero assertions weakened, zero tolerances widened, nothing quarantined, nothing added to `allowedSkips`
- `floor.json` `total` bumped in the same commit as the facts that moved it, reason in the message
- 3 consecutive full-suite greens at the stated exact counts, >= 1 fresh-checkout first-ever build, plus the
  repeated run of the cross-thread facts
- The record states plainly that this closes one member of the class, not the class

## Do NOT

- Put a timeout on the `_initLock` acquisition and then continue to `_backend.Dispose()` — that is the
  process-fatal concurrent-native-call class arriving through the safety fix. If your design does this,
  stop and report
- Remove `_initLock`, weaken it, or narrow what it serializes — the race it closes is real (SP-070)
- Touch `SoundFlowAudioBackend.CreatePlayer`, `OffSyncContext`, `SoundFlowDtrhAudio`, or any other file
  under `client/src/**` — the two `CreatePlayer` sites are a **separate packet** (orphan-disposal hazard)
- Add an awaitable UI dispatch, capture `SynchronizationContext.Current`, or otherwise re-open the §5
  post-only boundary
- Change the recovery machinery SP-070 landed: the cooldown, the streak, the single-flight guard, the
  refusal reasons, or the play seam's non-blocking property
- Change channel ownership, ducking, the voice queue, pacing, the SFX cap, or panic semantics
- Add a `Thread.Sleep`, `Task.Delay`, wall-clock read, or `DateTime.UtcNow` in product or test code; add no
  waits outside `TestWait`; do not write a concurrency test that depends on timing to be deterministic
- Log per call, log a file path or any user data, or add any observation, persisted field, diagnostic, or
  network call
- Weaken, retry, quarantine, or allowlist any test; add anything to `allowedSkips`; touch `admissionRule`,
  `skipSemantics`, or the 5 pinned names
- "Fix" the 2 Windows-observed skips or drive the skip count to 0 — **the asymmetry is correct**
- Call `dotnet test` outside `check-floor.mjs` (`FloorWrapperGuardTests` binds this packet)
- Export `CCP_DATA_ROOT` process-wide for a suite run — it skips the SP-057 pin and blinds the exact-count
  floor (the vacuous-green class SP-062 closed)
- Create any new file under `client/tools/` (`.gitignore:168` — it would pass in-lane and vanish from the
  merged tree)
- Claim a Linux result, a real-device result, or a headed result you did not produce
- Edit `client/docs/**` (including the contract docs — name owed wording in `record.md` instead),
  `docs/constitution.md`, `.spine/**`, `.pi/**`, `AGENTS.md`, `CLAUDE.md`; set any board row state
- Use `consult` council mode (T-7: solo only)
- Run `git clean -fdX` repo-wide in the lane — it deletes the T-14-staged `.pi/npm` that `verify.mjs` needs;
  clean **scoped** (`git clean -fdX -- client/`) and only after the contract passes
- Commit build output (`bin/`, `obj/`) or any results file

## Git Commit Convention

- `feat(SP-071): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `spine-tasks/SP-071-teardown-off-ui-thread/record.md`, `STATUS.md`
**Check If Affected:** `client/docs/async-lifecycle-fault-contract.md` (**read-only for this packet** — §5
is the post-only UI boundary this restores in spirit; if a teardown-handoff rule belongs in it, state the
exact wording in `record.md` as a finding for the orchestrator; do not edit it)
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md`,
`client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`

## Amendments

- 2026-08-14 (authoring, orchestrator): **wave 28 runs this row ALONE**, for the standing reason: any
  lane-mate that adds or removes a test collides on `floor.json` and the exact count — green alone, RED at
  merge (the SP-054/SP-058 class).
- 2026-08-14 (authoring, orchestrator): **decomposition consult (solo, Opus 5) — complete verdict on the
  first call under a 200-word cap** (8th consecutive wave the T-18 cap technique has held; recorded as a
  technique that works, never as evidence the tool is fixed). Verdict: **right class, wrong bundle — take
  site (1) alone and file sites (2)+(3) as one row.** Two substantive corrections, both encoded: **(1) the
  hazard I named (an orphaned player from a bounded `CreatePlayer`) is real but belongs to the OTHER
  packet**, whose central acceptance must be orphan disposal — a late-completing `AssetDataProvider` adds
  itself to `MasterMixer` (ghost play plus leak) and disposing it races device teardown; **(2) the hazard I
  did NOT name is in site (1) and decides the design** — a bounded `_initLock` wait that gives up and
  continues walks straight into `_backend.Dispose()` while a native init is still running, which is the
  process-fatal class `_initLock` exists to prevent, so the fix is **moving the teardown off the UI thread**
  (unbounded lock wait on a background thread; bounded UI-side wait with a typed give-up that never touches
  the backend). It also noted the census is cheap evidence worth including, and that the absent UI-hang
  watchdog is WPF's separate machinery, not this packet's.
- 2026-08-14 (authoring, orchestrator): **the advisor's checkable claims were verified before encoding, not
  trusted.** `Dispose` does take `_initLock` (`:1087-1091`) and then calls `_backend.Dispose()` (`:1093`);
  `TeardownBarkPipeline` (`DtrhHostWindow.axaml.cs:255-262`) is called from the close handler at `:153`, so
  the UI thread is the caller; and `DtrhLaunchCoordinator.cs:167` opens the host with `window.Show(_owner)`
  as a **non-modal child window**, so closing it is not process exit and "leak it, we are dying anyway" is
  genuinely unavailable. The port has **no** UI-hang watchdog (grep: no `HangWatchdog`/`UiHang` symbol),
  which is recorded as a fact, not as work owed here.
- 2026-08-14 (authoring, orchestrator): **the port's own contract already bans this shape.**
  `async-lifecycle-fault-contract.md` §5 makes the UI dispatch boundary post-only *"so no operation can wait
  on the UI thread"* and `IUiDispatch` deliberately has no awaitable method. The SP-070 teardown block is
  the letter-vs-spirit gap; this packet closes it without re-opening §5.1.
- 2026-08-14 (authoring, orchestrator): **Size M.** The product change is small and lives in one file; the
  weight is evidence — a captured pre-fix RED, a ~30-site census with per-site verdicts, an ordering fact
  that asserts the native call finished before the backend was disposed, an exactly-once fact after a
  give-up, an idempotence fact, a negative control, repeated cross-thread runs, and independent bite reverts.
- 2026-08-14 (authoring, orchestrator): **`spine preflight`'s `prelanded-file-scope` warning, if it fires,
  must not be obeyed.** It compares `fileScopeMustChange` against **`main`**, the still-shipping WPF branch
  with no `client/` tree, while the contract verifier uses `baseBranch` from `.spine/spine-config.json`.
  Following its hint would manufacture the contract-passes-on-docs-only class (SP-214/SP-457).
- 2026-08-14 (authoring, orchestrator): machine posture — laptop; **zero WSL distros, so Linux is a standing
  named gate** (do not fake a Linux run); **no real audio-endpoint death or wedged native call can be
  induced here** — the manual gate is named in the honesty cell, never simulated as evidence; **MCP not
  re-probed this phase** — a named limit, never a blocker. No AXAML in this packet, so the A-013 advisory
  step is not a gate. **`## Review Level: 2` heading present + grep-verified >= 2 (SP-034 authoring rule).**
- 2026-08-14 (authoring, orchestrator): **worker board-row obligation.** ENABLER 2 keeps `task-board.md` out
  of worker scope, so the row update is **budgeted into the land** by the orchestrator. Name your intended
  filings precisely in `record.md` — that text is what lands.
