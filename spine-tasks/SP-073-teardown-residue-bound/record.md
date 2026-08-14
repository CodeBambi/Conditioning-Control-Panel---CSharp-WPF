# SP-073 — Bound the teardown give-up residue across AUTOMATIC host closes

Branch `lane/sp-073-teardown-residue-bound`; commits `20d5b9cc` (mechanism + facts) and
`8162afda` (this record). The packet names base `37af332e`, but the actual parent of `20d5b9cc`
is **`7615c654`** — the lane's worktree was garbage-collected while it sat idle at the plan
checkpoint with nothing written, so the implementation committed onto `feat/crossplatform` in the
shared tree and the orchestrator recovered it non-destructively onto the lane branch afterwards.
Recorded because "which branch did this land on" is not inferable from the packet.

Review Level 3 (plan reviewed and revised before any product edit; the plan review found two
blocking defects in the first design — both are recorded in §3 and §4 because the shipped
mechanism is the answer to them; the final review found two more, corrected in §4 item 5 and §9
as documentation only, with no executable change).

## 1. Census: every path that reaches `SoundArbitration.Dispose`

The sole product call site is one line: `DtrhHostWindow.axaml.cs:258` (`_barkArbitration?.Dispose()`
in `TeardownBarkPipeline`), reached only from the window's `Closing` handler at `:153`. The only
construction site is `DtrhHostWindow.axaml.cs:214`. `BarkPipeline` holds the reference
(`Companion/BarkPipeline.cs:159,183`) and never disposes it; every other mention in `client/src`
is a comment (`DtrhProtocol.cs:200`, `Audio/AudioSeams.cs:18,82,145,179,387`).

| # | Trigger (all via `DtrhHostWindow.Closing:153` → `:258`) | Class | Recurs in ONE app session? |
|---|---|---|---|
| 1 | `DtrhLaunchCoordinator.cs:104` `dead.CloseForRecovery()` (watchdog Relaunch) | automatic | once per `DtrhWatchdog` instance |
| 2 | `DtrhLaunchCoordinator.cs:120` `HostWindow?.CloseForRecovery()` (watchdog Exhausted) | automatic | once per watchdog, only after #1 |
| 3 | `DtrhHostWindow.axaml.cs:777` forced close, 1200 ms exit-done wait elapsed | automatic | once per window (`DtrhExitFlow._closed`) |
| 4 | `DtrhHostWindow.axaml.cs:1037` page `exit-done` | automatic | once per window |
| 5 | `DtrhHostWindow.axaml.cs:1059` page `boot-error` | automatic | once per window |
| 6 | `DtrhHostWindow.axaml.cs:667` `_dialog.Closing` → `Close()` (Linux dialog surface) | automatic | once per window. **Not in the packet's list** |
| 7 | User closes the host window (title-bar X) | user | once per window |
| 8 | Lifetime/owner shutdown closes owned windows (`App.axaml.cs:153`; host is `Show(_owner)`-owned, `DtrhLaunchCoordinator.cs:167`) | automatic | once per app session |
| 9 | `App.axaml.cs:171` `--dtrh-autoclose` timed close | harness-only | once |

**The packet's five are all real; the list is the wrong unit of accounting.** Per window instance
there is at most ONE effective `Dispose`: the graceful branch returns before teardown with
`e.Cancel = true` (`:131-140`), `TeardownBarkPipeline` nulls the field (`:262`), and
`SoundArbitration.Dispose` early-returns on `_tornDown`. In m2 test mode no arbitration is built
at all (`:207-211`). So **residue count = number of host windows that ran `InitBarkPipeline` and
then closed**, and recurrence is governed by window CREATION (`DtrhLaunchCoordinator.cs:151`,
reachable from `:72` picker, `:85` Quick Start, `:113` relaunch), not by close paths.

Structural cause: the client builds a whole second miniaudio engine per host window
(`DtrhHostWindow.axaml.cs:213-219`, whose own comment says the app-wide lift is a future row),
where WPF's audio service is app-wide and never torn down per DtRH host.

## 2. `_relaunchSpent`: proven per watchdog instance, and it never bounded the residue

- Exactly one write exists (`DtrhWatchdog.cs:141`); grep over `client/**/*.cs` returns only the
  declaration `:55`, the accessor `:67`, the read `:139` and that write. `MarkLive` (`:71-76`) and
  `MarkDead` (`:93`) do not reset it, and window recreation cannot: the instance is a coordinator
  field (`DtrhLaunchCoordinator.cs:26`), passed into each window at `:151`. **Open, close, open
  again on the same coordinator does NOT refresh the relaunch budget** — the latch holds, over the
  wrong quantity. It gates census rows 1-2 only; rows 3-9 and the launch paths are ungated by it.
- WPF parity: `DtrhHostService.cs:39` `private static bool _relaunchedOnce`, set once at `:945`,
  never reset — per app session there, and still only about relaunches. The same file proves
  launches recur: `Launch` is idempotent only while active (`:71-73`), `DisposeAll` clears `_host`,
  and the Lab doors call it on every press (`MainWindow/MainWindow.Lab.cs:237-253`, `:318-322`).
  The only session latch on those doors is `BootFailedThisSession` (`:67`), armed only on a WebGL
  boot-error.

## 3. Maximum outstanding backgrounded teardowns per app session, and the decision-rule branch

- **Today's wiring: 2.** One initial window plus at most one watchdog relaunch, because
  `App.axaml.cs:137-206` launches once (guarded `dashboard.Opened`) and shuts the lifetime down at
  `FlowEnded`.
- **By the mechanisms in the DtRH/audio layer: unbounded, one per open/close cycle.**
  `DescendAndOpenAsync` has no per-session latch; nothing in `DtrhLaunchCoordinator`,
  `DtrhExitFlow`, `DtrhWatchdog` or `SoundArbitration` limits how many host windows a session
  builds.

**Branch selected: land a bound.** The "2" is not produced by a mechanism but by the absence of a
second caller, in a demonstrator-only file (`--dtrh-demo`) outside this packet's File Scope; the
ported outcome (WPF's doors) contradicts it; and a fact pinning "2" could not be written inside
this File Scope at all, since the number lives in `App.axaml.cs` wiring that `SoundArbitrationTests`
cannot observe.

## 4. The mechanism, and the two defects the plan review caught

Shipped shape (all in `SoundArbitration.cs`):

1. `RunHandoffTeardown` (the Dispose-side thread body) publishes `_backendDisposeOwed` with
   `Interlocked.Exchange` (full fence) **before** attempting the lock, then takes `_initLock` with a
   **zero timeout**. On success it releases immediately and disposes outside the lock — SP-071's
   barrier-then-dispose shape, unchanged. On failure it **exits**; it never parks.
2. `DrainOwedBackendDisposalAfterRelease` runs in the `finally` of both `_initLock` scopes
   (`Initialize`, `EnumerateDevices`), **outside** their lock bodies, i.e. strictly after
   `Monitor.Exit` has returned. If a disposal is owed it starts a teardown thread to perform it.
3. `PerformBackendDisposeOnce` is the single disposal site: claimed by
   `Interlocked.CompareExchange` (exactly-once across both spawn paths), never entered while
   holding any lock, carries SP-071's `_teardownState` transition pair and the completion line
   byte-identically, and signals the caller.
4. The caller waits on the **disposal** (`Monitor.Wait(_teardownSignal, TeardownBudget)`) rather
   than on a thread, because with the handoff the thread exits long before the budget and a `Join`
   would report "done" while the backend was still owed. Budget, give-up text and CAS unchanged.
   `Monitor.Wait` rather than an event object so the permanently wedged path never materialises a
   kernel handle nobody disposes.
5. `TeardownThreadOutstanding` is derived from `_teardownThread is { IsAlive: true }` (field
   published before `Start`). **Its scope is narrower than a residue counter, and the final
   review corrected an earlier claim here that it "cannot drift from the real one".** Publishing
   before `Start` closes only the started-but-not-yet-published mode. It does not close the
   OVERWRITE mode: `_teardownThread` is a single slot written by both spawn paths and
   `_backendDisposeOwed` is never cleared, so every post-teardown `_initLock` release spawns a
   no-op thread that overwrites the slot. Concretely — `TryEnter` succeeds, the claim is taken,
   `_backend.Dispose()` then wedges (T1 alive), any `EnumerateDevices` call (public, no
   `_tornDown` guard — §10.2) releases and the drain spawns T2, T2 sees the claim taken and
   dies, and the property now reads **false with T1 still parked**. So the property is sound for
   the parked-on-`_initLock` residue this packet removes, which is the only case the suite reads
   it against, and it is **unsound for the wedged-`Dispose()` class §9 calls irreducible**. The
   source comments at the property and at `StartBackendTeardownThread` carry the same narrowing.
   The better shape — an early return in the drain when the claim is already taken, which also
   kills the post-disposal thread churn — was deliberately NOT taken here: it is a behavioural
   change after verification (the wave-18 red-base class) and would invalidate §5's matrix. The
   orchestrator files it as its own board row at land.

**Defect 1 (plan review, blocking): the release race.** The first design read the owed flag INSIDE
the lock body. Interleaving: holder P finishes its wedged native call, runs the drain, reads
owed == false, and is preempted **before** `Monitor.Exit`; `Dispose` then runs, the teardown thread
sets owed and its `TryEnter` fails because P still holds the lock, so it exits; P resumes and
releases with nobody left to drain. The native device is never disposed — the forbidden "skip
disposal" outcome, reached non-deterministically, presenting as an intermittent red on SP-071's
`SoundArbitrationTests.cs:721` ("never zero (leak)", the assertion inside `Teardown_GiveUp_BackgroundCompletes_ExactlyOneDispose_CompletionLogged` at `:702`) rather than a clean failure. *(Citation corrected at land: `:713` is the "teardown exceeds" count, not the leak assertion.)*

**The fix and its interleaving argument.** The owed read moved *after* `Monitor.Exit`. Invariant:
**every acquisition of `_initLock` either performs the owed disposal or checks the owed flag
strictly after its own release.** There are exactly three acquisition sites — `Initialize`,
`EnumerateDevices`, and the teardown thread's `TryEnter` — verified by full read of the file. If
the teardown thread's `TryEnter` succeeds it performs the disposal. If it fails, some thread held
the lock at an instant *after* the fenced owed write; that holder releases later, and its check —
which happens after its own `Monitor.Exit`, hence after that instant — must observe the flag and
perform the teardown. Termination is immediate, not inductive. The flag is never cleared and the
perform is claimed, so a redundant offer is a no-op. The only non-terminating case is "a holder
never releases", which is bit-for-bit today's outcome (SP-071's thread never acquired either) —
never worse, never a new leak.

**Defect 2 (plan review, blocking): the lock the first design newly nested was `_lifecycle`, not
`_gate`.** `SoundFlowAudioBackend.Dispose:126-138` routes into `OrphanSafePlayerFactory.Teardown`,
which opens `lock (_lifecycle)` (`AudioSeams.cs:324`), and `AudioSeams.cs:178-181` states in
writing that `_lifecycle` is a leaf and that SP-071's teardown thread "takes `_initLock`, releases
it, then reaches `_lifecycle` — never nested". Disposing from inside the lock body would have
falsified that sentence one wave after SP-072 wrote it.

**Resolution: both defects had one root — read and disposal inside the lock scope.** In the
shipped mechanism `_backend.Dispose()` is reached from exactly two sites, **neither holding any
lock**: the teardown thread after `Monitor.Exit` returns, and the drain-spawned teardown thread,
which never touches `_initLock` at all. `_initLock → _lifecycle` never nests and the sentence stays
true. Sink property shown rather than assumed: the bodies under `_lifecycle` are `Teardown`'s
delegate (`SoundFlowAudioBackend.cs:132-136`, `_device.Stop/Dispose` + `_engine.Dispose`, no
managed lock), `Create`'s attach block (`AudioSeams.cs:271-289`) and `DisposeOrphan`'s (`:364`) —
none takes another managed lock, so `_lifecycle` remains a leaf and no cycle exists.

**Why the drain spawns a thread instead of disposing inline** (accepted deviation from literal
Variant A, approved at plan review): the drain runs on whatever thread owned the lock — the
recovery probe's THREAD-POOL thread (`AudioSeams.cs:133-137`), or a future UI-thread caller, since
`Initialize` is called on the UI thread at `DtrhHostWindow.axaml.cs:220`. Disposing inline would
put a native device teardown on the UI thread: the SP-071 class by a new door, live in the code and
annotated as unreachable. One line instead makes a checkable sentence true on every path:
**`_backend.Dispose()` runs only ever on a named `SoundArbitrationTeardown` thread.** Variant B
(spawn a thread that takes a blocking `lock (_initLock)`) was rejected at plan review because
public `EnumerateDevices` has no `_tornDown` guard, so a queued enumerate could park that thread
exactly as before.

## 5. Revert matrix — one source at a time, tree restored byte-identically between runs

Baseline commit `20d5b9cc`; each revert was applied, built (0W/0E), run against
`--filter FullyQualifiedName~SoundArbitrationTests` (46 tests), then restored with
`git checkout --` (working tree verified clean before the next).

| Revert | Single source construct reverted | Red | Which |
|---|---|---|---|
| R1 | `RunHandoffTeardown`: bounded `Monitor.TryEnter(_initLock, TimeSpan.Zero)` + walk-away → pre-SP-073 blocking `Monitor.Enter` | **3 / 46** | F1, F2, F3 |
| R2 | `Initialize`'s post-release drain call deleted | **5 / 46** | F1, F2, and three EXISTING SP-071 pins (`Dispose_TwiceWhileTeardownParked`, `Teardown_ProbeParked_BackendDisposedOnlyAfterNativeCallReturns`, `Teardown_GiveUp_BackgroundCompletes_ExactlyOneDispose_CompletionLogged`) |
| R3 | `EnumerateDevices`'s post-release drain call deleted | **1 / 46** | F3 only |
| baseline | none | **0 / 46** | — |

Verdict tokens were `TIMING-VERDICT:CONDITION-NEVER-TRUE` in every case (real failures, not
starvation; worst scheduler slip 22 ms). R1's actor-state snapshot reads `outstanding=5/5` — the
residue count is exactly N per N wedged closes, which is the defect this packet exists to remove.
R2 is the strongest signal available that the handoff cannot silently skip a disposal: deleting the
drain converts the give-up into a permanent leak, and SP-071's own "never zero (leak)" pin at
`:721` catches it (the assertion; `:713` is the "teardown exceeds" count — citation corrected at land).

F3 also reds under R1 (a blocking acquire parks against a wedged enumerate too). That is broader
than predicted and is reported as observed.

## 6. Facts added

| Fact | Subject |
|---|---|
| `Teardown_RepeatedWedgedCycles_TeardownThreadsDoNotAccumulate` | **The bound.** Five sequential open/close cycles against permanently parked probes: zero instances with a teardown thread outstanding, one give-up line per close, every backend untouched while wedged; then, released one wedge at a time, every backend disposed exactly once by a teardown thread with one completion line each (disposal handed off, never skipped) |
| `Teardown_GiveUp_NoThreadOutstandingInsideTheWedgedInit_DisposedOnceAfterRelease` | **The inside-the-wedge read** (SP-072's `disposeCountAtTeardownEnd` shape): the residue is read ON the wedged thread, INSIDE the still-in-flight native init, which is the only window where "no teardown thread outstanding" means anything — after the release the drain spawns one. Plus the SP-071 pair and the disposing identity |
| `Teardown_GiveUp_DuringParkedEnumerate_DrainsAfterTheEnumerateReleases` | The second `_initLock` scope, so the enumerate-site drain is not vacuous |
| `Teardown_NoContention_DisposalNeverRunsOnTheCallerThread` | **SP-071 regression guard, NOT an SP-073 fact.** Pre-SP-073 code passes it, so it is deliberately absent from the revert matrix above. It exists because SP-073's uncontended branch sits one edit away from "we hold the lock, just dispose here", which is SP-071 reverted |

Fixture notes: `Make`/`ParkedProbe` gained an optional log sink (default unchanged, so every
existing fact still uses the shared list). The three SP-073 facts pass a `ConcurrentQueue` because
their release phase genuinely has two product threads logging at once, and the shared `_log` is an
unsynchronised `List<string>`. Cycle sequencing is enforced by signals (`TestWait` on each cycle's
own dispose-returned / dispose-count), never by assumption. No new wall-clock wait: `TestWait` only,
plus the SP-071 `GiveUpBudget` literal whose elapsing is the subject.

## 7. Floor

- Pin: **1018 unit / 35 headless**. Observed after this slice: **1022 unit / 35 headless**,
  0 failed, 2 named OS-gated skips (`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`,
  `SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`). `observed == 1018 + 4` = the declared
  delta. `client/tests/floor/floor.json` was NOT opened or edited; the delta is declared in
  `floor-delta.json` in this folder for the land to sum.
- **Provenance of each number.** The 1022 unit figure comes from the contract's `testCommand`,
  `node client/tests/floor/check-floor.mjs`. The 35 headless figure does NOT: the wrapper aborts
  on the unit project's pin mismatch and never reaches the headless project, so it was produced
  by a direct run,
  `dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo --no-build`
  (35 passed, 0 skipped). Named because a number attributed to a gate that did not produce it is
  the kind of claim this record exists to prevent.
- Build: 0 Warning(s), 0 Error(s).

## 8. Documentation Requirements

`client/docs/async-lifecycle-fault-contract.md` §5 rule 6 ends "Bound the *observation*, never the
lock." Wording believed owed (worker proposes, orchestrator applies at land — SP-059 precedent;
this packet did not edit the document):

> ...and the backgrounded portion must not hold an OS thread waiting on that lock. It publishes the
> owed disposal and exits; the thread holding the lock performs the teardown after its own release,
> at the same instant the parked thread would have. The owed flag must be read after the release,
> never inside the lock body: a holder that reads it inside can be preempted before releasing while
> the hand-off's bounded acquisition fails, and the resource is then never disposed at all.

`client/src/CcpClient.Desktop/Audio/AudioSeams.cs:178-181` is **out of File Scope and was not
edited**. No correction is owed there — the shipped mechanism keeps its claim true (§4). What is
owed is a clarification, because there are now two spawn paths for the named teardown thread and
the clause enumerates one:

> SP-071's teardown thread takes `_initLock`, releases it, then reaches `_lifecycle`; SP-073 adds a
> second teardown thread spawned by the post-release drain, which reaches `_lifecycle` without ever
> taking `_initLock` — never nested on either path.

## 9. Honesty: what this does NOT prove

- **The ordering argument is argued, not executed.** No test can force the preemption point in
  Defect 1's interleaving. The revert matrix pins the mechanism's effect (no residue; the disposal
  still lands exactly once); the fencing argument itself rests on reading
  `Interlocked.Exchange` / `Monitor.Exit` / `Volatile.Read` semantics plus the three-acquisition-site
  enumeration. F2's inside-the-wedge read is the closest executed approach to it.
- **No headed evidence of any kind.** Unit facts against recording fakes and a manual clock. Nothing
  here verifies interaction, rendering, audio output, focus, window behaviour or animation, and no
  headless frame is claimed. The real SoundFlow backend is never exercised by these tests.
- **The residue counts in §3 are structural**, derived from code. No running app was observed, no
  real thread counts or handle counts were measured.
- Census row 8 (lifetime shutdown cascading `Closing` onto owned windows) is taken from
  `App.axaml.cs:143-144`'s own comment and the SP-023 ping-pong note, **not from an observed run**.
  If that cascade does not raise `Closing`, row 8 falls out; rows 1-7 stand on read code and the
  verdict does not change.
- **One residue class remains and is irreducible**: if `_backend.Dispose()` itself wedges in native
  code, its teardown thread stays parked for the life of the process — one per instance, only for
  instances whose device teardown wedges, and never accumulating from the lock. A blocking native
  call consumes its thread by construction; the only alternatives are skipping the disposal
  (forbidden) or abandoning the device (a leak). Whether SoundFlow's real `Dispose` can wedge was
  not determined.
- **If a thread never releases `_initLock`, the backend is never disposed.** Identical to
  pre-SP-073 behaviour, stated rather than hidden.
- **A failed Dispose-side `teardown.Start()` leaves the disposal un-owed, and two log lines then
  lie.** `_backendDisposeOwed` is published INSIDE `RunHandoffTeardown`, and `Dispose` reaches
  that body only through `StartBackendTeardownThread(RunHandoffTeardown)`. If `Start()` throws
  (resource exhaustion), the body never runs, nothing is owed, no later `_initLock` release can
  drain, and the backend is never disposed — while the catch logs "disposal stays owed for the
  next lock release" (true on the drain path, false here) and the caller logs "teardown continues
  in background" (false here). **Not a regression:** pre-SP-073 the unguarded `Start()` throw
  propagated out of `Dispose` and was swallowed by `TeardownBarkPipeline`'s catch
  (`DtrhHostWindow.axaml.cs:258`), and the backend was likewise never disposed. FILED, NOT FIXED:
  publishing the flag in `Dispose` before the spawn is a two-line fix, but it is a behavioural
  change after verification and would invalidate §5's matrix for no gain on any path a test
  drives. The source comment at the catch site carries the same statement so the code does not
  read as if it were handled.
- Pre-existing and untouched: SP-071's own pins write to the unsynchronised `_log` from two threads
  during their release phase. That race predates this packet and is unchanged in kind; those tests
  were deliberately not modified.

## 10. Discoveries filed, not fixed (both out of File Scope)

1. **The wedged probe parks a THREAD-POOL thread.** `RunRecoveryProbe` is scheduled through
   `ISoundClock.Schedule`, whose real implementation is `new Timer(...)`
   (`client/src/CcpClient.Desktop/Audio/AudioSeams.cs:133-137`), so every wedged cycle also parks a
   pool thread indefinitely — arguably the worse half of the residue, addressed by neither SP-071
   nor this packet. Fixing it means changing the clock seam in `AudioSeams.cs`.
2. **Public `EnumerateDevices` has no `_tornDown` guard** (`SoundArbitration.cs:257-271` in the shipped file; `:219-225` was its pre-packet location — corrected at land, because a board row is filed from this citation): it calls
   the backend directly, so it can reach a disposed backend, and it can hold `_initLock` on a
   torn-down instance. This became load-bearing twice in this packet's review (it is why Variant B
   was rejected, and it is the reason the enumerate scope needs its own drain). It is a behaviour
   change to the public device layer, so it is filed rather than taken here.

No board row was opened, closed or edited by this packet; `client/docs/task-board.md` was not
touched.
