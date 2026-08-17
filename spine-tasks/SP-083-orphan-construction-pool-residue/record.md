# SP-083 — record

Packet: `spine-tasks/SP-083-orphan-construction-pool-residue/PROMPT.md` (supersedes SP-074).
Lane branch: `lane/SP-083-orphan-construction-pool-residue`, base `feat/crossplatform` at `cf9f7143`.
Review level 3. Plan: `.port/plans/SP-083-orphan-construction-pool-residue/plan-round-2.md` (APPROVED round 2).

Every `File.cs:line` below was opened in THIS tree. Where the packet's citation is stale I give the live number.

---

## 1. Census (Step 1)

### 1.1 Every path that reaches `OrphanSafePlayerFactory.Create`

Two product classes call `Create`, and both do nothing else with it
(`SoundFlowAudioBackend.cs:113-122`, `SoundFlowDtrhAudio.cs:107-116` — each delegates straight to
`_players.Create`).

| # | Seam call site | Reaches `Create` via | Under a lock? | Refusal handling | Recurs per session against a wedged endpoint? |
|---|---|---|---|---|---|
| 1 | `SoundArbitration.cs:602` (`PlaySfx`) | `SoundFlowAudioBackend.cs:122` | no | `catch (Exception ex)` `:603` → log + `SoundOutcome.Failed` | **yes**, once per sfx cue |
| 2 | `SoundArbitration.cs:815` (private `CreatePlayer` helper) | same | no | `catch (Exception ex)` `:817` → log + `SoundOutcome.Failed` | **yes**, once per `PlayVoice` (`:440`) and once per `PlayWhisper` (`:517`) |
| 3 | `SoundArbitration.cs:931` (`OnPacingFire`, queued voice) | same | **YES — inside `lock (_gate)` opened at `:908`** | `catch (Exception ex)` `:933` → log + `continue` | **yes**, once per queued line |
| 4 | `DtrhNativeEffects.cs:114` (`PlaySfx`) | `SoundFlowDtrhAudio.cs:116` | **YES — inside `lock (_gate)` opened at `:102`** | `catch (Exception ex)` `:115` → log + silent no-op | **yes**, once per DTRH sfx cue |
| 5 | `DtrhNativeEffects.cs:454` (`PlayWhisper`) | same | no | `catch (Exception ex)` `:456` → log + silent no-op | **yes**, once per DTRH whisper |

**The packet's list of five sites is CORRECT. Its line numbers are not.** `SoundArbitration.cs` moved
+54 lines when SP-073 landed (2026-08-15). Packet `:548 / :761 / :877` → live `:602 / :815 / :931`.
`DtrhNativeEffects.cs:114 / :454` are exact. Filed under §6.

**Re-derived rather than inherited:** the packet warned that SP-072's census found one more
construct-under-a-lock site than *its* packet predicted. I re-derived it here and the answer holds
at **two** (sites 3 and 4). Nothing was missed —
`grep -rn "CreatePlayer|OrphanSafePlayerFactory" client/src` returns no sixth product caller and
`SoundFlowAudioBackend.cs:24` is the only `IAudioBackend` implementation.

**All five catch `Exception` broadly**, verified at each site in this tree rather than inherited from
SP-072. That is why the cap refusal needs no caller change and no second exception type (§3.4).

### 1.2 Does anything already bound the outstanding count?

**Both packet candidates re-derived. I reach the same conclusion on both — the packet's Premise
section is CONFIRMED, not corrected, on this point.**

- **SP-070 session suppression does not bite.** `_audioDisabledForSession` (`SoundArbitration.cs:126`)
  is written in exactly two places: `RecordInitFailureLocked` (`:368`) and the success reset (`:342`).
  `RecordInitFailureLocked` has exactly three callers — `:312`, `:333`, `:412` — **all inside
  `Initialize` / the recovery probe, none on a construction path**. A construction timeout is caught
  at `:604` / `:817` / `:933` and never reaches it, so `ReadyLocked` (`:759`) keeps returning true for
  a device that initialised fine and then wedged inside `AssetDataProvider`. **Confirmed.**
- **The SFX pool cap does not bite.** `_sfxPool.Count >= _options.MaxSfxVoices`
  (`SoundArbitration.cs:592`, `DtrhNativeEffects.cs:104`) is checked **before** construction, and the
  pool only grows on a construction that **returned** (`SoundArbitration.cs:622`,
  `DtrhNativeEffects.cs:125`). Against a wedge the pool is permanently empty, so the cap never fires.
  **Confirmed.**

**Third candidate searched for and NOT found.** I looked for any latch a construction failure could
arm: `_tornDown` (only `Teardown` sets it), `_whisperBusy` (cleared on failure), the voice generation
tokens and `DtrhNativeEffects._voiceGeneration` (monotonic, not gates). None is armed by a
construction failure. I read all of `AudioSeams.cs:184-376` as it stood before this change: the
factory held `_lifecycle`, `_tornDown` and a per-call `ConstructionSlot`, and **no counter and no cap
of any kind**. The packet premise holds.

### 1.3 Maxima

- **Outstanding abandoned constructions per app session, before this change: UNBOUNDED.** One per
  cue, per factory, forever.
- **Live factory instances.** Three construction sites, all at the packet's exact lines:
  `SoundFlowAudioBackend` at `DtrhHostWindow.axaml.cs:213`, `SoundFlowDtrhAudio` at
  `DtrhHostWindow.axaml.cs:315`, `SoundFlowDtrhAudio` at `DtrhLoomWindow.axaml.cs:78`. Each `new`
  builds its own factory with its own `_lifecycle` / `_tornDown`.
  - **Simultaneously live: at most 3** (2 per open DtRH host + 1 loom).
  - **Cumulatively per session: UNBOUNDED.** `DescendAndOpenAsync` (`DtrhLaunchCoordinator.cs:135`)
    constructs a fresh `DtrhHostWindow` at `:151`, and is reached from `LaunchWithPickerAsync`
    (`:58`), `QuickStartAsync` (`:81`) — both user-invocable again every time `FlowEnded` fires
    (`:143`, `:165`, `:174`) — **and** from the watchdog relaunch at `:113`. A host can be opened,
    closed and opened again in one session. **This is what decides Decision B.**

### 1.4 The pool-starvation cost — what is and is NOT asserted

Asserted: a `Task.Run` worker blocked inside a native call is **not returned to the pool**, so each
abandonment permanently removes one worker, and the pool responds to blocked workers by injecting
additional threads over time rather than instantly — degrading every other `Task.Run` in the app, not
only audio.

**NOT asserted:** the packet's "roughly one per 0.5–1 s beyond `MinThreads`" injection *rate*. I did
not verify it against a source, and the packet says not to assert it unless confirmed.

---

## 2. Decisions

**DECISION A → LAND A BOUND.** §1.2 shows nothing bounds it and §1.3 shows one-per-cue growth with no
ceiling. The "already bounded" branch has no evidence to stand on.

**DECISION B → branch (ii): the PER-INSTANCE cap, landed, with the per-session remainder named.**
The gating question is census item 3, and the evidence says a host **can** be reopened
(`DtrhLaunchCoordinator.cs:58`, `:81`, `:113`), so a per-instance cap is not by itself a session
bound. Branch (i) — process-wide accounting — was rejected on three independent grounds, any one
sufficient:

1. **It changes live behaviour nobody asked for.** The two factories drive *different* engine/device
   instances. A process-wide cap lets a wedged loom sfx endpoint refuse bark *voice* on a healthy
   device — silence on an unrelated channel.
2. **Deterministic resettability is not reachable inside File Scope.** A `static` on
   `OrphanSafePlayerFactory<TPlayer>` is per closed generic type, so every `<OrphanPlayer>` test would
   share one counter across parallel collections — the cross-test-bleed class the `ProcessEnvCollection`
   board row exists for. The only fixes are a reset method on a product type (test-only product API)
   or re-scheduling all of `SoundArbitrationTests`. The packet says: if that is not reachable without
   widening File Scope, **(i) is off the table**.
3. An injected shared-counter abstraction would have exactly one concrete consumer shape.

**The remainder, named:** `opens × 2 × cap` (DtRH host: bark backend + native effects) `+ 1 × cap`
(loom, one-shot). Threads parked by a closed window's factory stay parked and are not re-counted by
the next factory — `Teardown` sets `_tornDown`, so a dead factory starts no further constructions.
This is **not** a WIP dodge: the per-instance cap removes the one-per-cue growth, which is the whole
of what the board row asks for. Owed as its own board row (§6).

---

## 3. What landed

All product code in `client/src/CcpClient.Desktop/Audio/AudioSeams.cs`, inside
`OrphanSafePlayerFactory<TPlayer>`. No new lock, no new wait, no `CancellationToken`, no dedicated
thread, no static, no new exception type, no new seam, no new test helper class.

| clause | what | where (live) |
|---|---|---|
| **C1** | refuse at the cap, typed, **before any `Task.Run`** | `AudioSeams.cs:284-295` |
| **C2** | count the abandonment, exactly once, on the caller thread | `AudioSeams.cs:373` |
| **C3** | settle in the construct `finally` — the pool thread is released HERE | `AudioSeams.cs:300-304` |
| state | `DefaultMaxOutstandingAbandoned = 4`, `_maxOutstandingAbandoned` (clamped `Math.Max(1, …)`), `_outstandingAbandoned`, `ConstructionSlot.Accounting` | `:214`, `:262`, `:232` |
| observable | `public int OutstandingAbandonedConstructions => Volatile.Read(ref _outstandingAbandoned);` | `:270` |
| helpers | `CountAbandoned` (CAS `Uncounted→Counted`), `SettleAccounting` (`Exchange`→`Settled`, decrement only if it was `Counted`) | `:428`, `:442` |
| doc | orphan-invariant **clause 6**; `PlayerConstructionTimeoutException` doc widened to name both refusals | `:181-192`, `:140-153` |

`Accounting` is a **second, independent** latch on the slot. It is deliberately not overloaded onto
`State`: the pool THREAD is released when `_construct` returns, the PLAYER is disposed later and
behind `_lifecycle` — two lifetimes that end at different moments must not share one latch. Nothing
reads `State` to decide accounting and nothing reads `Accounting` to decide disposal.

Exactly-once in all four interleavings:

| interleaving | abandon side | construct-`finally` side | net |
|---|---|---|---|
| construction still parked when the caller abandons | CAS `0→1` wins → **+1** | later `Exchange` returns `1` → **−1** | 0 when the thread returns |
| construction already returned (budget met, `Monitor.TryEnter` failed) | CAS fails (already `Settled`) | `Exchange` already returned `0` | **0 — nothing parked, nothing counted** |
| construction faults after abandonment | **+1** | `finally` still runs → **−1** | 0 |
| ordinary construction, never abandoned | never called | `Exchange` returns `0` | **counter never touched** |

**The LOAD-BEARING log order at `AudioSeams.cs:374-380` is UNMOVED.** C2 inserts one statement between
`slot.Abandoned = true` and the log; the log still precedes the `if (task.IsCompletedSuccessfully)`
check, which is the arming dependency
`Construction_CompletionRacesAbandonment_DisposedExactlyOnce` rendezvous on. That pin is green and
unmodified.

**The five existing orphan facts are unchanged and green.** None conflicts with this mechanism. The
harness hook added for SP-083 is invoked unconditionally but armed by no existing test, and it
enqueues no `Events` entry — so the class's only exact-sequence assertion
(`Assert.Equal(["construct-returned", "attached"], h.Events.ToArray())`) is untouched.

### The three FORBIDDEN overflow behaviours, and why this design cannot reach them

- **Caller waits again** — C1 is `Volatile.Read` + compare + `throw`. No `Wait`, `Join`, `lock`,
  `Monitor`, `SpinWait`, `Sleep` on the new path; the refusal returns before `Task.Run` is reached.
  The two existing bounded waits are untouched.
- **Skip/defer orphan disposal** — nothing was added to `DisposeOrphan`, `SpawnDisposer`, the P4
  continuation, or the `State` latch. F4 pins that disposal still happens with the counter already at
  zero.
- **`CancellationToken` faking a bounded count** — none created, passed or observed. The decrement is
  the delegate's own `finally`, which can only run when the native call actually returns; a token
  could not make it run earlier, so a token could not fake a bounded reading.

---

## 4. The five facts

All in `client/tests/CcpClient.Tests/SoundArbitrationTests.cs`, driving the **existing**
`OrphanHarness` (extended, not duplicated).

| id | test | what it observes |
|---|---|---|
| **F1** | `Construction_AtTheOutstandingCap_FurtherCreatesRefusedWithoutStartingAConstruction` | cap 2, **3 creates** (one past the cap), driven SEQUENTIALLY. `ConstructCount == 2` — the third started no pool thread; `Outstanding == 2`; `AbandonmentLines == 2`; `CapRefusalLines == 1`; `AttachCount == 0`; `thrown[2]` is `PlayerConstructionTimeoutException` with `"cap"` in its message |
| **F2** | `Construction_ParkedConstructionsReturn_OutstandingDropsToZero_AndConstructionIsAdmittedAgain` | THE DECREMENT. `Outstanding == 2` **while both constructions are demonstrably parked**; the count read **from inside the still-parked constructions** via `InsideWedgedConstruct` (two readings, one of them `2`); release; `Outstanding == 0`; a further create is **admitted** (`ConstructCount == 3`, `CapRefusalLines == 0`) |
| **F3** | `Construction_Ordinary_NeverTouchesTheOutstandingCount_NoCapLine_NoLogLine` | NEGATIVE CONTROL. `Outstanding == 0` before and after; **`Assert.Equal(0, Assert.Single(OutstandingInsideWedge))` — read from INSIDE the ordinary construction while the caller is still in `task.Wait`**; `AttachCount == 1`; `ConstructCount == 1`; `CapRefusalLines == 0`; `LogLines == 0` (invariant clause 5's "zero new log lines", asserted rather than assumed); same object returned |
| **F4** | `Construction_AbandonedConstructionReturns_CountDropsAtTheNativeReturn_NotAtOrphanDisposal` | WHERE the release lives. One abandonment (`Outstanding == 1`, `DisposeCount == 0`); release the construct gate; wait for `"dispose-entered"` — the disposer is now parked INSIDE `dispose`, holding `_lifecycle`; assert `Outstanding == 0` **while `DisposeCount` is still 0**. The count tracks the THREAD, not the object |
| **F5** | `Construction_AbandonedThenFaults_CountStillDrops_CapNeverRefusesForever` | cap 1; one abandonment fills it; a second create is refused (`CapRefusalLines == 1`, `ConstructCount == 1`); release the gate with `ConstructThrows` armed so `_construct` **throws**; `Outstanding == 0`; a further create is **admitted** (`ConstructCount == 2`). Binds the `finally` specifically |

Harness extensions (all in `OrphanHarness`): `ConstructCount` (the one-shot `ConstructStarted` latch
cannot count N — the packet named this blocker), `InsideWedgedConstruct` + `OutstandingInsideWedge`
(SP-073's `InsideWedgedInit` / `InsideWedgedEnumerate` shape), `DisposeGate` +
`Events.Enqueue("dispose-entered")`, `ConstructThrows`, `CapRefusalLines`, `LogLines`, `Outstanding`,
and `outstanding=` / `constructs=` in `State()`. `ConstructGate` stays one shared gate — correct here,
because F1/F2 want all N released together. `LastPlayer` was left alone rather than churned; these
facts do not need all N players.

Timing discipline: only `TestWait.UntilSync` / `TestWait.InjectedBudget` and `ManualResetEventSlim`
signals. The one short pinned literal is the **existing** `ConstructionBudget` (200 ms) — reused, not
new. No `Thread.Sleep`, no bare `Task.Delay`, no `DateTime` / `Environment.TickCount64` poll.
Vacuous-shape guard: every new `[Fact]` has assertions at guarding depth 0 (no assertion sits inside
a loop body), no early `return`, no `Assert.Skip*`, no platform/env/filesystem predicate.
`client/tests/floor/vacuous-shape-ledger.json` was not touched and needs no entry.

---

## 5. Revert matrix — EXECUTED, not predicted

Method: each mutation applied to `AudioSeams.cs` **alone**; full solution rebuilt (`0W/0E` every
time); the ten `SoundArbitrationTests.Construction_*` tests run (5 existing SP-072 orphan facts + the
5 new SP-083 facts). Between reverts the file was restored from a saved pristine copy and verified
**byte-identical by `git hash-object` = `d1494294558c5d934473b3f4709ca10ca4016a3b`** — checked after
every restore, six times.

| # | mutation of ONE clause | predicted | **MEASURED red** | which |
|---|---|---|---|---|
| **R1** | delete the C1 cap-check block in `Create` | F1, F5 | **2** | F1, F5 |
| **R2** | delete `CountAbandoned(slot);` (C2) | F1, F2, F4, F5 | **4** | F1, F2, F4, F5 |
| **R3** | delete `SettleAccounting(slot);` from the construct `finally` (C3) | F2, F4, F5 | **4** | F1, F2, F4, F5 |
| **R4** | **move** `SettleAccounting` out of the `finally` into `DisposeOrphan`, **pinned position: after `SafeDispose(player)`, inside `lock (_lifecycle)`** | F4, F5 | **2** | F4, F5 |
| **R5** | replace the `try/finally` with a straight-line `SettleAccounting` after `_construct(...)` returns | F5 | **1** | F5 |
| **R6** | **move** `CountAbandoned(slot)` to just before `Task.Run` (count EVERY in-flight construction) | F3 | **1** | F3 |
| — | baseline, no mutation | 0 | **0** | 10/10 pass |

**Every clause C1/C2/C3 reds at least one fact when reverted alone. Every fact reds under at least
one revert. Every revert row reds at least one fact.** No revert reddened any of the five existing
SP-072 orphan facts, in any row.

**One measured/predicted divergence, recorded as measured (SP-079 precedent — a wrong prediction is
recorded wrong, not conformed):** R3 was predicted to red 3 facts and reddened **4**. F1 also reds,
because F1 ends by releasing the wedge and waiting for `Outstanding == 0` so it does not leak parked
stand-ins past the test; with C3 deleted the count never falls and that wait fails with
`TIMING-VERDICT:CONDITION-NEVER-TRUE` after the full window. That is a genuine red on a real
observation, not a spurious one — it simply means F1 also observes the decrement in its teardown.
R1/R2/R4/R5/R6 matched their predictions exactly.

**Round-1 misprediction, owed by the plan and recorded here.** Round 1's matrix claimed R6 reds F1
and did not red F3. Both halves were wrong, the round-2 review caught it, and **execution confirms
the round-2 correction**: R6 reds **F3 only**, at the round-2 addition itself —
`SoundArbitrationTests.cs:1493`, `Assert.Equal(0, Assert.Single(h.OutstandingInsideWedge))`,
`Expected: 0 / Actual: 1`. Under R6 with cap 2, F1's three creates still give `ConstructCount == 2`,
`Outstanding == 2` and one cap line, so every F1 assertion holds — F1 passed under R6 in the
executed run, exactly as the corrected plan said. The root was that F3 had no *during* observation,
so the placement of the count (abandoned-only vs. every in-flight construction) was a load-bearing
clause with no biting fact. It has one now.

**The one thing with no revert row is the public property `OutstandingAbandonedConstructions`,
deliberately:** reverting it is a compile error, not a bite, and every one of the five facts exercises
it.

---

## 6. Out of File Scope — FILED for the orchestrator, not fixed

1. **Board-row citation drift.** The row is at **`client/docs/task-board.md:129`** ("Wedged-construction
   pool-thread residue…"), **not `:125`** as the packet and `spine-tasks/CONTEXT.md:177` both say;
   `:125` is THE DESCENT row. Owed at land: that row should cite **`AudioSeams.cs:237` at the time of
   filing** (`Task.Run`) as the residue mechanism, replacing the `ISoundClock.Schedule` /
   `AudioSeams.cs:133-137` paraphrase — that is `SystemSoundClock`, a different and correctly-disposed
   mechanism (three live uses, all one-shot, all disposed). The packet's `AudioSeams.cs:236` and
   `:183` were each one low against the pre-change tree (live `:237` and `:184`).
2. **Stale `SoundArbitration.cs` citations in the packet** (`:548 / :761 / :877` → live
   `:602 / :815 / :931`), caused by SP-073 landing +54 lines. Items 1–2 are instances of the
   citation-drift class already on the board at `:128` (T-19).
3. **`SpawnDisposer`'s P3 pool thread is not counted by this counter.** `AudioSeams.cs:413` spawns a
   `Task.Run` whose `DisposeOrphan` takes `_lifecycle`; behind a wedged native teardown that worker
   parks too. Pre-existing, not a regression, and it belongs to the *disposal/teardown* residue family
   (SP-073's row) — but the two now sit adjacent and only one is counted. I confirmed the packet's
   claim that `SpawnDisposer`'s own `task.Wait()` returns immediately: it is only reached when
   `task.IsCompletedSuccessfully`, so it is **not** a second parked thread.
4. **`_gate` held across a wedged construction** at `SoundArbitration.cs:929-937` and
   `DtrhNativeEffects.cs:112-123`. After the cap fires the refusal is immediate, so the gate hold
   drops to ~0 for cues past the cap — recorded as a consequence, not a tested claim. Out of File
   Scope; SP-073 owns `SoundArbitration.cs` this wave.
5. **The Decision B per-session remainder** (§2) — owed as its own board row.

Nothing under `client/docs/`, `client/tests/floor/floor.json`, `client/src/.../SoundArbitration.cs`,
`ConditioningControlPanel/`, `.spine/`, `.pi/` or `.claude/` (other than this lane's worktree
directory) was edited.

---

## 7. Documentation owed at land — RECORDED, NOT EDITED (SP-059 precedent)

`client/docs/async-lifecycle-fault-contract.md:62`, rule 7, currently states this residue as a fact:
*"When a caller's bounded wait for a resource construction expires, the construction keeps running."*
That sentence stays true; what it now needs is the bound. Proposed appended wording:

> **Because the abandoned work keeps an OS thread that nothing can interrupt, the number of
> constructions abandoned and still running must itself be bounded.** The factory counts them and, at
> its cap, refuses a further construction with the same typed no-player outcome **before any thread is
> taken** — never by making the caller wait again, and never by skipping the orphan's disposal. The
> count is released **the instant the native call returns, whatever its outcome** — never at disposal,
> or a faulted construction would refuse forever. Only ABANDONED constructions are counted: an
> ordinary in-flight construction must never consume the bound, or a burst of healthy cues would
> silence a working device. The bound is **per factory instance**; a session that opens repeated hosts
> retains each instance's residue, which is a named limit rather than a session bound. Added at the
> SP-083 land.

Also for the orchestrator: board row `client/docs/task-board.md:129` (not `:125`) should cite
`AudioSeams.cs`'s `Task.Run` as the residue mechanism (§6 item 1). **The SP-071 / SP-072 / SP-073 board
rows were not touched, closed or claimed.**

---

## 8. Carried conditions — discharge

The four non-blocking round-1 suggestions carried into the approved plan, plus the fifth
"verified-for-the-record" item.

1. **F1's drive order must be sequential, or the cap check races. F2 needs the same discipline.**
   **DISCHARGED.** Both facts drive `StartCreate` one at a time and observe each caller's typed
   outcome (`TestWait.UntilSync(() => done.IsSet, …)` then `thread.Join()`) before the next `Create`
   begins. The reason is stated in F1's own comment ("SEQUENTIAL BY CONSTRUCTION … started
   concurrently, the third could reach the cap check a whole budget before the first two abandoned
   and all three would construct — a flake, not a bound"), and F2 cites it back.
2. **The cap is SOFT and the plan does not name it.** **DISCHARGED.** Named in the product code at
   the C1 comment (`AudioSeams.cs:284-288`: K callers concurrently past the read can all construct, so
   the real bound is `cap + concurrently-in-flight callers` per factory) and again in §9 below as a
   named limit alongside the per-session remainder. Sites 3 and 4 serialize under their own `_gate`,
   so only sites 1/2/5 can overlap.
3. **R4 is under-specified — pin the intended position.** **DISCHARGED.** R4 was executed with
   `SettleAccounting(slot)` placed **after `SafeDispose(player)`, inside `lock (_lifecycle)`** in
   `DisposeOrphan`, which is the position that makes F4's red meaningful. Pinned in the matrix row.
   Measured 2 red (F4, F5), as the suggestion predicted for that position.
4. **`SoundArbitration.cs:252` is not a counter.** **DISCHARGED.** Verified in this tree:
   `SoundArbitration.cs:252` is `public bool TeardownThreadOutstanding => Volatile.Read(ref _teardownThread) is { IsAlive: true };`
   — a reference read behind a bool, correct as the "outstanding X" *vocabulary* precedent in this
   subsystem and nothing more. The *counter* shape citation is `AiOperationPipeline.cs:68`,
   `public int SendAttempts => Volatile.Read(ref _sendAttempts);`, which is what
   `OutstandingAbandonedConstructions` copies. Both verified by reading the lines.
5. **The "verified, so round 2 need not re-derive" list.** Re-derived here anyway where it feeds a
   claim in this record: the five call sites and their broad `catch (Exception)` (§1.1), the live
   `SoundArbitration.cs` line corrections (§6), the board row at `task-board.md:129` (§6), the floor
   pin at 1028 / 35 (§9), both non-bounds (§1.2), the reopen evidence for Decision B (§1.3), and the
   accounting latch's exactly-once behaviour in all four interleavings (§3).

---

## 9. Verification and floor reconciliation

Build and gate run as **separate** commands, build immediately before the gate, both through the slot
semaphore:

```
node client/tools/gate/with-slot.mjs --slots 3 -- dotnet build client/CcpClient.sln -c Debug --nologo
node client/tools/gate/with-slot.mjs --slots 3 -- node client/tests/floor/check-floor.mjs
```

- **Build: `0 Warning(s), 0 Error(s)`.**
- **Pin, read from `client/tests/floor/floor.json`: `CcpClient.Tests` = 1028, `CcpClient.HeadlessTests` = 35.**
- **Declared delta (`floor-delta.json`): unit +5, headless 0.**
- **Observed: `CcpClient.Tests` 1033 (0 failed, 1031 passed, 2 skipped), `CcpClient.HeadlessTests` 35
  (35 executed, 35 passed, 0 skipped).**
- **Reconciles: 1028 + 5 = 1033, and 35 + 0 = 35.** The wrapper reports a pin mismatch on the unit
  project; that is the designed multi-lane state — the pin is bumped at land from the summed deltas,
  never by a lane. `client/tests/floor/floor.json` was not edited.
- The two skips are the OS-gated Linux legs already in `allowedSkips`
  (`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`,
  `SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`). The SP-057 pin
  (`DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault`) did **not** skip;
  `CCP_DATA_ROOT` was never exported.

---

## 10. HONESTY — what this does NOT prove

1. **The real native `AssetDataProvider` wedge is never exercised.** Every fact here runs against the
   recording `OrphanHarness` fake. No audio device is inducible on this machine and the real
   `SoundFlowAudioBackend` / `SoundFlowDtrhAudio` have zero headless coverage. What is proven is that
   the FACTORY bounds, counts and releases correctly against a construction that parks and returns on
   command. That the real SoundFlow constructor actually parks the way the fake does is inherited from
   SP-025's dump evidence, not re-proven here.
2. **Linux is unproven.** Zero WSL distros on this machine; nothing here ran on Linux. The mechanism
   is pure managed `Interlocked` with no platform surface, so there is no reason to expect divergence
   — but "no reason to expect" is not evidence, and this is not a Linux-verified claim.
3. **The cap is SOFT, and the real per-factory bound is `cap + concurrently-in-flight callers`.** C1
   is a read-compare-throw with no claim, so K callers concurrently inside `Create` while
   `Outstanding == cap - 1` can all pass the check and all start a construction. K is small in
   practice (sites 3 and 4 serialize under their own `_gate`; only sites 1/2/5 can overlap), and no
   fact here pins K. It is a bound and it removes the one-per-cue growth; it is not a hard ceiling.
4. **Decision B branch (ii): the per-session remainder is real and NOT bounded by this change.** A
   session that opens and closes the DtRH host repeatedly retains `opens × 2 × cap + 1 × cap` parked
   threads in the worst case. Threads parked by a closed window's factory stay parked forever. Owed
   as a board row (§6 item 5).
5. **`SpawnDisposer`'s P3 disposer threads are NOT counted** by this counter (§6 item 3). Behind a
   wedged native teardown those park too. Pre-existing and adjacent, but now only one of the two
   adjacent residues is counted, and that asymmetry is real.
6. **The cap value 4 is a chosen literal, not a proof.** The rationale (the healthy path never counts
   at all, so any non-zero count already means blown budgets; reaching 4 costs at least 8
   caller-seconds; 3 live factories × 4 stays inside one processor-count's worth of pool workers on
   the 8-core reference machine) is an argument, not evidence. No fact pins 4 — the facts pin the
   *behaviour* at an injected cap of 1 or 2.
7. **The thread-pool injection RATE is deliberately not asserted** (§1.4). Blocked workers are not
   returned; how fast the runtime compensates was not verified against a source.
8. **The refusal is not distinguishable from the budget expiry by TYPE**, only by message. That is a
   deliberate reuse decision (§3.4, all five callers catch `Exception` and embed the message). Any
   future caller that wants to branch on the two would have to match on text, which is a real
   consequence of this choice.
9. **Nothing here proves the user-visible outcome** — that a wedged endpoint no longer degrades the
   rest of the app. That would need a real device and a real wedge. What is proven is the mechanism
   the outcome rests on.
