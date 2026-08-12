# SP-059 — Timing discipline in tests: convert, sweep, guard (THIRD occurrence — encode)

**Task:** the suite that grades every land must be trustworthy. Convert the AI lab's
wall-clock waits, sweep and classify every wall-clock dependency suite-wide, add a guard
+ draft the standing order. Bar: 10 consecutive full-suite green runs captured to files,
zero assertions weakened, zero product behavior change.

## 1. The concrete defect

`client/tests/CcpClient.Tests/AiProviderLabIntegrationTests.cs:88-97` — `WaitForAsync`
polls `Environment.TickCount64 + 8000` (10 ms cadence) for `BytesReadSoFar > 0` /
`SendAttempts >= 1` over a loopback `HttpListener`; consumed at :128, :257, :283.
Sibling `WaitForRecordAsync` (:70-86) same shape, 8000 ms, 25 ms cadence, consumed at
:142, :167, :262, :293. On a cold/loaded machine 8000 ms is losable, and the failure
message (`timed out waiting for a real in-flight network operation`) cannot say whether
the condition failed or the machine was slow — that is why the class keeps returning
(T-15 SP-041, T-16 SP-043, now).

## 2. Reproduction attempt (deliberate, cold-start correlation)

| Attempt | Conditions | Result |
|---------|-----------|--------|
| 8 × filtered runs (`--filter AiProviderLabIntegrationTests`, 12 tests each) | first run was first-after-build (cold-ish); `--no-build` after | 8/8 green (~5 s each) |
| 1 × full unit suite (862 tests, normal parallel load) | first-after-build | green, 862/862 |
| 1 × full headless suite | same | green, 33/33 |

**Not reproduced on demand** (the SP-058 land observation was 2 reds in 6 on the merged
tree under orchestrator cold conditions). The mechanism is taken from the failure
message and the code (a losable 8000 ms wall-clock deadline), not invented. Evidence:
`evidence/baseline-unit.log`, `evidence/baseline-headless.log` (tail captures of the
baseline runs; full-run file discipline begins with the Step-4 chain).

## 3. Suite-wide sweep + classification (no sampling)

Grep axes: `Task.Delay`, `Thread.Sleep`, `Stopwatch`, `Environment.TickCount64`,
`DateTime.UtcNow/Now`, `DateTimeOffset.*Now`, `WaitAsync(TimeSpan`, `.Wait(TimeSpan`,
`CancellationTokenSource(` timeout ctors, `CancelAfter`, `SpinWait/SpinUntil`
(last three: zero hits). Every hit below; nothing omitted.

### CcpClient.Tests

| # | file:line | construct | waits for | class → action |
|---|-----------|-----------|-----------|----------------|
| 1 | AiProviderLabIntegrationTests.cs:70-86 `WaitForRecordAsync` | TickCount64+8000 poll (25 ms) | lab thread's record queue (real loopback actor) | **2 → helper** |
| 2 | AiProviderLabIntegrationTests.cs:88-97 `WaitForAsync` | TickCount64+8000 poll (10 ms) | provider `BytesReadSoFar`/`SendAttempts` over real socket | **2 → helper** |
| 3 | AiProviderLabIntegrationTests.cs:131,153,178,285 | TickCount64 elapsed asserts (upper 2000/3300/3500 ms; lower 900 ms) | subject IS elapsed time (bounded cancel, bounded drain, Retry-After honored) | **3 — keep, marker** |
| 4 | AiOperationContractTests.cs:364 | `Task.Delay(5 min, ct)` | token-observed stand-in for in-flight work; literal never elapses on the pass path | **3 — keep, marker** |
| 5 | AiOperationPipelineTests.cs:240,264 | `DateTime.UtcNow` elapsed asserts (<5 s) | subject: bounded panic drain | **3 — keep, marker** |
| 6 | AiOperationPipelineTests.cs:344-355 `WaitForAsync` | 200×10 ms poll, `Assert.Fail("condition not met within 2s")` | fake provider first call (IN-PROCESS) | **1 → deterministic TCS signal via helper** |
| 7 | AiProviderLab.cs:236 | `Task.Delay(1500)` (SlowOk) | the lab instrument IS a genuinely slow reply (subject) | **3 — keep, marker** |
| 8 | AiProviderLab.cs:265,300 | `Task.Delay(100)`×300 probe loops | client-gone detection; exits on disconnect; lab-side 30 s cap | **3 — keep, marker** |
| 9 | AiProviderLab.cs:330 | `_loop.Wait(2 s)` in Dispose | teardown join tripwire | **3 — keep, marker** |
| 10 | AsyncLifecycleTests.cs:57,255 | `Task.Delay(Timeout.Infinite[, token])` | never elapses (cooperative/uncooperative stand-ins) | **3 — keep, marker** |
| 11 | AsyncLifecycleTests.cs:205-217 `WaitForAsync` | TickCount64+2000 poll (10 ms) | heartbeat tick text (real 500 ms timer actor) | **2 → helper** |
| 12 | AvatarAnimationEngineTests.cs:167,169 | `Task.Delay(150)` negative settle | "no emits while paused" — a broken engine could only be MISSED (false-green), never false-red | **3 — keep, marker** |
| 13 | AvatarAnimationEngineTests.cs:401-408 `WaitForAsync` | 600×5 ms poll, undifferentiated throw | engine loop park conditions (in-process, real async) | **2 → helper** |
| 14 | AvatarAnimationEngineTests.cs:197,230,242,417 | `Completion.WaitAsync(5 s)` | terminal-outcome await (hang tripwire; cancellation is token-driven) | **3 — keep, marker** |
| 15 | CapabilityTests.cs:118 | `Task.Delay(Timeout.Infinite, token)` | never elapses | **3 — keep, marker** |
| 16 | CompanionViewModelTests.cs:110-119 `PumpEventually` | TickCount64+5000 `Thread.Sleep(10)` poll | dispatch post from a real running participant | **2 → helper (sync)** |
| 17 | CompanionViewModelTests.cs:263-266 | TickCount64+5000 `Thread.Sleep(10)` poll | fake provider `Calls > 0` behind async dispatch | **2 → helper (sync)** |
| 18 | DtrhInboxTests.cs:66 | `Task.Delay(100)` pre-enqueue hedge | NOTHING — `Inbox.PollAsync` registers synchronously before its first incomplete await (Inbox.cs:51-66: the lock block runs on the caller's thread) | **1 → DELETE** (proof below) |
| 19 | DtrhInboxTests.cs:76 | `Stopwatch` elapsed bounds (100–2000 ms) | subject: long-poll bounded timeout | **3 — keep, marker** |
| 20 | DtrhInboxTests.cs:89 | `Task.Delay(50)` pre-ReleaseAll hedge | NOTHING (same synchronous-registration proof) | **1 → DELETE** |
| 21 | DtrhInboxTests.cs:68,91 | `poll.WaitAsync(2 s)` | terminal tripwire on a must-arrive result | **3 — keep, marker** |
| 22 | DtrhLoopbackContractTests.cs:253 | `Task.Delay(50)` pre-enqueue over real HTTP | server poller registration — not observable client-side; assertions hold regardless of arrival order (a retained message is returned immediately by a late poll) | **1 → DELETE + dedicated server (below)** |
| 23 | DtrhLoopbackContractTests.cs:255 | `pending.WaitAsync(5 s)` | terminal tripwire | **3 — keep, marker** |
| 24 | LoopbackOllamaProviderTests.cs:54,72,182,228 | TickCount64 elapsed asserts | subject: bounded timeout / remote-probe-no-socket / fast cancel | **3 — keep, marker** |
| 25 | LoopbackOllamaProviderTests.cs:248-251 | TickCount64+5000 poll (10 ms) | `BytesReadSoFar > 0` over real socket | **2 → helper** |
| 26 | StatusTickerSliceTests.cs:58,149 | `Task.Delay(1200)` fixed | real 500 ms ticker advancing (fixed wait where a poll returns at the first tick) | **2 → helper** |
| 27 | AiMemoryPromptAssemblyTests.cs:328 | `_serve.Wait(5 s)` in Dispose | teardown join tripwire | **3 — keep, marker** |
| 28 | TeardownFlushTests.cs:102 | `releaseWrite.Wait(30 s)` inside the wedged-writer hook | subject instrument (a wedged write); bound exists so the TEST cannot hang | **3 — keep, marker** |
| 29 | TeardownFlushTests.cs:113 | `writeStarted.Wait(5 s)` + Assert.True | deterministic signal, bounded (already the right shape; message made classifying) | **1 — already a signal; keep + loud message** |

### CcpClient.HeadlessTests

| # | file:line | construct | waits for | class → action |
|---|-----------|-----------|-----------|----------------|
| 30 | AvatarTubeHeadlessTests.cs:66-76 `AdvanceAsync` | 600×5 ms inner pump + `Task.Delay(10)` projection settle | engine `DelayPending` pump (real async) + posted UI projections | **2 → helper** (inner pump); the 10 ms projection settle is a **3 — marker** (negative settle, false-green-only) |
| 31 | AvatarTubeHeadlessTests.cs:83-86 | `DateTime.UtcNow`+5 s poll (50 ms) | first frame rendered (real dispatcher) | **2 → helper** |
| 32 | AvatarTubeHeadlessTests.cs:186 | `Task.Delay(100)` negative settle (frozen frame) | false-green-only | **3 — keep, marker** |
| 33 | AvatarTubeHeadlessTests.cs:206 | `Task.Delay(150)` | capability-text projection (a POSITIVE condition exists: the text) | **2 → helper poll on the text** |
| 34 | AvatarTubeHeadlessTests.cs:228 | `Completion.WaitAsync(5 s)` | terminal tripwire | **3 — keep, marker** |
| 35 | DashboardCardHeadlessTests.cs:108-112 | `DateTime.UtcNow`+5 s, `Task.Delay(600)` poll | ticker tick + ElementName mirror text (real dispatcher) | **2 → helper** |

### Latent findings recorded (no action — outside conversion boundary)

- `Inbox.PollAsync` itself (`client/src/.../Dtrh/Inbox.cs:48,68`) uses `DateTime.UtcNow`
  deadlines — PRODUCT long-poll timeout machinery, not a test wait. Out of scope (zero
  product change this task).
- The four private `ManualClock` copies (BarkPipelineTests:703, DtrhFxRouterTests:147,
  DtrhNativeEffectsTests:492, SoundArbitrationTests:551) are the SP-043 SOLUTION shape,
  not wall-clock dependencies. **Not consolidated**: consolidation would touch 4 files
  for zero behavior/assertion gain — outside the "conversions only" boundary. Recorded
  per the packet's "record why" branch.

## 4. Design

### (a) The single approved wait helper — `TestWait` (new shared source)

One file `client/tests/CcpClient.Tests/TestWait.cs`, linked into HeadlessTests via
`<Compile Include>` (single source, both assemblies; both are xunit.v3 3.2.2).

API:

```csharp
public static class TestWait
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(20);
    public static Task Until(Func<bool> condition, string what, Func<string>? state = null, TimeSpan? window = null, CancellationToken ct = default);
    public static Task Until(Task signal, string what, Func<string>? state = null, TimeSpan? window = null);      // deterministic-signal overload
    public static void UntilSync(Func<bool> condition, string what, Func<string>? state = null, TimeSpan? window = null); // Thread.Sleep poll for sync dispatcher-pump fixtures
}
```

**Loud classifier** (binding framing b; consult-hardened): the poll loop tracks polls,
elapsed, and the worst scheduler slip. On expiry the message LEADS with a greppable
verdict token (survives into TRX failure names — the SP-058 land lesson):

- `TIMING-VERDICT:ENVIRONMENT-STARVED` when the loop itself was starved (worst slip
  > 250 ms or far fewer polls than the window allows) — rerun/reduce load before
  treating as a failure.
- `TIMING-VERDICT:CONDITION-NEVER-TRUE` when the loop ran on schedule and the condition
  stayed false — treat as a real product/test failure.

**Consult correction adopted:** slip alone blames the wrong direction — a cold-start
flake has the poll loop ON SCHEDULE while the ACTOR (server/JIT/socket) is the starved
side. So every verdict also appends EVIDENCE: slip, polls, `ThreadPool.PendingWorkItemCount`,
and a caller-supplied `state()` snapshot of the actor's progress counters (e.g.
`SendAttempts/BytesReadSoFar/HitCount`) so the human gets the real differential
(request never left vs reached server, no reply). Verdict is a hypothesis; the evidence
travels with it.

**Consult check adopted:** no converted wait may sit INSIDE an elapsed-measured window.
Verified at all three lab sites: the wait precedes `started = TickCount64` (:128<:131,
:283<:285) or the unmeasured send phase (:257). A genuine product break now costs up to
20 s per wait site (was 8 s) — failure path only, accepted.

**Rejected alternatives:**
- *Raise 8000 → 30000 (or any literal)* — rejected at authoring (packet framing a);
  makes the flake rarer and the failure slower, encodes nothing.
- *Inject a clock into the lab/provider*: no product seam exists for "first bytes
  arrived" and the wait is on a REAL socket actor — determinism is not available; a
  fake clock would fake the very thing under test.
- *Per-test ad-hoc helpers*: that is exactly the scattered shape that produced T-15/T-16
  recurrences; ONE helper or the guard has nothing to bless.

### (b) The guard — `TestTimingGuardTests` (DataRootChokePointGuardTests style)

Source-scanning, deterministic, never skips (FindRepoRoot precedent). Scans
`client/tests/**/*.cs` (both projects). **Forbidden tokens** on any line NOT carrying a
`// wallclock-allow: <class-3 reason>` marker:

`Environment.TickCount`, `Thread.Sleep(`, `Task.Delay(`, `DateTime.UtcNow`,
`DateTime.Now`, `DateTimeOffset.UtcNow`, `DateTimeOffset.Now`, `Stopwatch`,
`.WaitAsync(TimeSpan`, `.Wait(TimeSpan`, `.WaitOne(TimeSpan`.

Exempt files: `TestWait.cs` (the helper itself) and `TestTimingGuardTests.cs` (holds the
tokens as string literals).

**PINNED ALLOWLIST (consult hardening — the packet's "named allowlist", made
rubber-stamp-proof):** the guard carries a checked-in list of
(relative path, exact trimmed line content) for every permitted marked site. A
forbidden token without a marker FAILS; a marked site NOT in the pinned list FAILS
(new wall-clock requires editing the guard itself — the friction IS the review gate);
a pinned entry that no longer matches any source line FAILS (stale entries can't rot).
Red-then-green proof in Step 3 (deliberate literal → guard red → removal → green;
transcript here).

### (c) Product seam: **NONE**

Every converted wait is test-side; the helper and the registry are test-side.
`client/src/**` does not change. (The class-1 DtrhLoopbackContractTests conversion uses
a DEDICATED test-side server instance with a generous long-poll timeout — fixture
configuration, not product code.)

### (d) Class-1 deterministic conversions (detail)

1. **AiOperationPipelineTests** (:237, :261 waits): `FakeProvider` gains a
   `FirstCall` `TaskCompletionSource` (set at `CompleteAsync` entry); the 200×10 ms
   local poll is deleted; waits become `await TestWait.Until(provider.FirstCall.Task,
   "the in-flight operation reaching the provider")`.
2. **DtrhInboxTests** (:66, :89): delays DELETED. Proof: `Inbox.PollAsync`'s
   registration (lock → capture `_signal.Task`) executes synchronously on the caller's
   thread before the first incomplete await (Inbox.cs:45-72); `Assert.False(
   poll.IsCompleted)` + the 5 s/2 s terminal tripwires keep both orderings green.
3. **DtrhLoopbackContractTests.Inbox_LongPollHangs_UntilEnqueue**: gets its own
   DEDICATED `LoopbackServer` (the :288 inline pattern) **sharing the fixture's
   `_inbox` and `_log`** (consult condition — a different inbox would not be woken by
   the test's enqueue) with `longPollTimeout: 5 s`; the 50 ms pre-enqueue delay is
   DELETED (either arrival order satisfies `Assert.Single`; the generous timeout removes
   the "server poll expires before enqueue" load race that the shared fixture's 200 ms
   timeout makes possible today). The shared fixture's 200 ms is LOAD-BEARING for the
   ack-purge tests (:211/:222/:231 rely on fast empty-timeout returns), so raising the
   fixture timeout was rejected. The dedicated server's two listeners join the leak
   registry; in-code comment carries the ordering proof (consult: the mechanism must
   live in the test, not only the record).
4. **TeardownFlushTests:113**: already a deterministic signal; Assert message made
   classifying ("the wedged write never started — hook/product failure, not timing").
5. **Every deletion/conversion carries the mechanism-proof comment IN THE TEST CODE**
   (consult: otherwise the next maintainer re-adds the sleep).

### (e) Registry-gap closure (SP-041 follow-through)

The T-15 self-check registry covers `AiProviderLab` only. New shared test-side
`LoopbackListenerRegistry` (static, port → owner+prefix): `AiProviderLab` registers
through it (replacing its private dict, same loud semantics); the assembly fixture
(renamed `LoopbackListenerLeakSelfCheck`, same `[assembly: AssemblyFixture]`
mechanism) fails the run LOUD naming every leaked port/owner. Registered:

- `IntakeServingTests` fixture `LoopbackServer` (the row's named gap) — PagePort +
  MediaPort, registered after `Start()`, unregistered in `Dispose` (fail-loud preserved).
- Also found by the sweep, SAME gap class, registered with it: `DtrhLoopbackContractTests`
  (fixture :41 + inline :288), `DtrhLoomTests.ServerHarness` (:153),
  `AiMemoryPromptAssemblyTests`' private `HttpListener` harness (:284-).

## 5. Consults

### Pre-approach solo consult (Step 1)

- **Route:** in-session `consult` tool, `mode: "solo"` (T-7: never bare). Per the pause
  protocol the intended main route is Opus 5 (Fable 5 fallback); the tool response does
  not echo its answering-model identity — **ACTUAL answering model honestly
  unidentifiable from tool output** (SP-037/SP-043 record precedent).
- **TRUNCATION (recorded per the SP-058 truncated-consult precedent):** the response
  was truncated mid-sentence during item (c) (the guard critique); nothing past the cut
  was stitched or guessed. Item (d) (assertion-weakening audit) was never reached —
  discharged instead by the grep-level assertion-diff proof in §6 and the
  pre-completion consult.
- **Received verdict (items a–c, adopted in full):**
  1. *(a) classifier:* slip alone is flawed — a cold-start flake has the poll loop ON
     SCHEDULE while the ACTOR is the starved side, so slip-only would mislabel it
     "condition never became true". Adopted: verdict token leads (greppable in TRX:
     `TIMING-VERDICT:CONDITION-NEVER-TRUE` / `TIMING-VERDICT:ENVIRONMENT-STARVED`),
     followed by EVIDENCE — slip, polls, `ThreadPool.PendingWorkItemCount`, and a
     caller-supplied actor-state snapshot (`SendAttempts/BytesReadSoFar/HitCount`).
     Verdict = hypothesis; evidence travels with it.
  2. *(a) window check:* no converted wait may sit inside an elapsed-measured region —
     verified (:128<:131, :283<:285, :257 unmeasured).
  3. *(b) deletions sound, two conditions, both adopted:* the ordering mechanism proof
     must live as a comment IN THE TEST CODE; the dedicated DTRH server must share the
     fixture's `_inbox`/`_log` and register its two listeners in the leak registry.
     Fixture-timeout-raise rejected on evidence: the 200 ms is load-bearing for the
     ack-purge tests (:211/:222/:231).
  4. *(c) marker-only guard = rubber stamp.* Adopted: PINNED ALLOWLIST inside the guard
     (path + exact line content); unmarked token fails, unpinned marker fails, stale pin
     fails.

## Review Level: 2 — engine-review presence log (T-2 heading format)

| Step | Call | Result |
|------|------|--------|
| 1 | `spine_review_step --step 1 --type plan` | **SKIPPED** (SP-195: nested reviewer spawn blocked in worker session; engine runs reviews after .DONE). Artifact `.reviews/1-20260812T123030.md` |

## 6. Assertion-change proof

(pending Step 3 — grep-level diff summary of every assertion touched; expected: none)

## 7. Ten-run index

(pending Step 4)

## 8. Drafted `docs/constitution.md` line (NOT applied — orchestrator applies at land)

> - **No new wall-clock waits in tests.** Every test wait is a deterministic signal or the shared bounded-window helper with its loud classifier; hard-coded deadline literals, `Thread.Sleep`, `DateTime`/`Environment.TickCount64` polls, and bare `Task.Delay` waits outside the approved helper fail the timing guard.

## 9. Intended board filings (orchestrator reconciles — ENABLER 2)

1. The P1 timing-discipline row: evidence pointer to this record (sweep table,
   conversion inventory, guard, 10-run chain).
2. `port-lessons` candidate: three occurrences of one class means the fix is encoded
   (helper + guard + standing order), not applied once; the loud classifier is the
   difference between a flake report and a verdict.
3. `port-lessons` candidate: negative-observation settles ("wait to see nothing
   happens") can only false-green, never false-red — they are class 3 by construction,
   but must carry the marker so the guard can tell them from deadlines.
