# SP-063 — Timing discipline part 2: injected timeout BUDGETS, not waits (FOURTH occurrence)

Wave 20, single lane (consult decision — this packet edits a suite-wide pinned allowlist
keyed on path + exact string + count; a parallel lane would stale the pins at merge).
Floor entering the task: **892 unit / 35 headless, 0 skipped** (SP-062, landed `7518c6a4`).
An unexpected SKIP counts as a red for this packet.

## Step 1 — reproduction, fourth-class definition, sweep, design

### Reproduction attempt (honest counts)

| Attempt | Worktree | Conditions | Result |
|---|---|---|---|
| cold1 | `C:/Code/ccp-sp063-cold1` (detached @`4fae614d`, removed after) | fresh checkout, **first-ever build**, full unit suite, TRX attached | green, 892/0 (`evidence/runs/cold1-repro.log`, `evidence/trx/sp063-cold1-unit.trx`) |
| cold2 | `C:/Code/ccp-sp063-cold2` (detached @`4fae614d`, removed after) | fresh checkout, **first-ever build**, full unit suite, TRX attached | green, 892/0 (`evidence/runs/cold2-repro.log`, `evidence/trx/sp063-cold2-unit.trx`) |

**Not reproduced: 0 firings / 2 genuine cold first-ever builds.** Consistent with SP-062's
measurement of the same site (0 firings / 23 runs incl. 3 cold builds) and with the board
row's observed hit rate (1 red / 6 merged-state runs, only on fresh-checkout + first-ever
build). Per the packet: non-reproduction treated as likely; the design proceeds from the
MECHANISM, verified by reading the provider's classification order, never from a lucky red.

### The mechanism (read from the code, not assumed)

`LoopbackOllamaProviderTests.Provider(lab)` builds every provider with
`RequestTimeout = TimeSpan.FromMilliseconds(800)` (`LoopbackOllamaProviderTests.cs:25`).
In `LoopbackOllamaProvider.CompleteAsync` (`client/src/CcpClient.Desktop/Ai/LoopbackOllamaProvider.cs`)
the classification order is:

1. Pre-socket rejection (remote hosts) — no timer involved.
2. Per attempt: linked CTS `CancelAfter(RequestTimeout)` → `SendAsync` → `ReadBodyAsync` → `Classify(body)`.
3. `catch (OperationCanceledException) when (external.IsCancellationRequested)` → rethrow (external cancel).
4. `catch (OperationCanceledException)` → **`AiReply.Unavailable(timeout)`** — the linked CTS fired.
5. `Classify` only runs on a fully-read body; truncated prefix → `JsonException` → `malformed-output`.

The lab's `Truncated` mode (`AiProviderLab.cs`) writes the truncated body with
`ContentLength64` set and closes the response — **EOF is deterministic and immediate**. So
the ONLY thing that can turn `malformed-output` into `timeout` is the linked CTS firing
before the round trip completes. On a first-ever cold build, JIT + `HttpListener` warmup
exceeds 800 ms, step 4 wins the race, and
`Truncated_PrefixCut_NeverSurfaced_TypedUnavailable` fails expecting `malformed-output`.
The test's subject is classification; its outcome was decided by wall time. The same race
exists for every other classification/round-trip test built from the same factory
(malformed, refusal, 429, 500, 404, ok, mid-stream-cancel — where the 800 ms timeout can
also fire before the cancel on a cold machine) and in `AiProviderLabIntegrationTests`'
`Harness` (800 ms request AND probe budgets — a probe timeout on a cold build flips the
capability state to Unavailable and every pipeline-level expectation with it).

### The fourth class (admission rule — the sweep's and the guard's vocabulary)

**Class 4 = an injected deadline BUDGET: a wall-clock deadline literal handed INTO product
code under test via options or config (an object-initializer / options-record assignment),
which the product enforces with its own timer.** It differs from SP-059's three classes
(waits in test bodies) in that the wall clock runs INSIDE the product code; the test body
contains no wait construct at all, so no wait-token ever matches it. The admission question
for any site is: **"If this deadline fired on the pass path, would the test's expected
outcome change?"**

- **4a (legitimate — pin):** the budget's ELAPSING IS the subject under test (timeout
  classification; a boundedness proof). Same discipline as SP-059 class 3: inline
  `// wallclock-allow:` marker naming WHY, plus a pin (path + exact trimmed code + count).
- **4b (defect):** the budget silently decides the outcome of a test whose subject is
  something else (classification, round-trip, payload shape). On a cold machine the
  deadline fires first and the test measures the budget, not the behavior. Fix direction:
  **remove the time dependence** — never raise the number (banned fix).

### Suite-wide sweep (enumeration, no prediction)

Axes: option-style `Identifier = TimeSpan.` assignments (line-start and mid-line),
`Timeout =`/`CancelAfter`/`CancellationTokenSource(TimeSpan` (already wait-tokens),
`TimeSpan.Zero`/`Timeout.InfiniteTimeSpan`/`new TimeSpan(` shapes, method-argument
TimeSpan bounds handed to product code, options/config records carrying TimeSpan values.
Both test projects swept; every hit below.

| # | Site (file:line @ `4fae614d`) | Literal | Population | Disposition |
|---|---|---|---|---|
| 1 | `LoopbackOllamaProviderTests.cs:25-26` — shared `Provider(lab)` factory feeding Ok, 429×2, 500, 404, refusal, malformed, **truncated (named site)**, mid-stream-cancel, probe-up | RequestTimeout 800 ms, ProbeTimeout 800 ms | **4b DEFECT** — the deadline decides the outcome of tests whose subject is classification/round-trip/cancellation | **Remove the deadline** (`Timeout.InfiniteTimeSpan`): the lab closes every stream deterministically — EOF/stream-close becomes the ONLY terminator |
| 2 | `LoopbackOllamaProviderTests.cs` `Timeout_Classifier_Bounded_ExternalTokenNotCancelled` | RequestTimeout 800 ms | **4a** — the budget's elapsing IS the subject (timeout classification + boundedness + token-not-poisoned) | KEEP — own options block, marker + pin |
| 3 | `LoopbackOllamaProviderTests.cs:178` `RemoteHost_RejectedPreSocket` | RequestTimeout 800 ms | Inert — rejection precedes the timer entirely (SendAttempts==0 asserts the real guard) | REMOVE the assignment (no budget needed; product default never reached) |
| 4 | `LoopbackOllamaProviderTests.cs:210` `Probe_LoopbackDown` | ProbeTimeout 800 ms | Doubly inert — loopback connection-refused is kernel-instant, and even a fired budget yields the SAME typed code (`HostUnreachable`) | REMOVE the assignment (default 2 s product bound stands) |
| 5 | `LoopbackOllamaProviderTests.cs:225` `Probe_RemoteHost_ZeroSocket` | ProbeTimeout 800 ms | Inert — pre-socket rejection | REMOVE the assignment |
| 6 | `AiProviderLabIntegrationTests.cs:53-54` — shared `Harness` factory feeding all 11 integration tests | RequestTimeout 800 ms, ProbeTimeout 800 ms | **4b DEFECT** — same race as #1 plus a PROBE-timeout race: on cold, the probe budget flips the capability state and every pipeline expectation with it | **Remove both deadlines** (infinite) |
| 7 | `AiProviderLabIntegrationTests.cs` `Timeout_ThroughPipeline_TypedUnavailable_TokenNotPoisoned_LabSeesClientGone` | RequestTimeout 800 ms | **4a** — elapsing IS the subject (through the real pipeline) | KEEP — explicit options factory in the test, marker + pin |
| 8 | `AiMemoryPromptAssemblyTests.cs:250` wire-payload proof | RequestTimeout 10 s | **4b** — never-elapses in practice, but if it fired the payload assertions would fail: the subject is wire shape, not time | REMOVE the deadline (infinite — `WireListener` replies synchronously, write+close, deterministic) |
| 9 | `SoundArbitrationTests.cs:27-28` | DuckWatchdog 5 min, VoicePacingDelay 2 s | NOT wall-clock — `SoundArbitration` runs on the injected `ManualClock`; time advances only when the test advances it | **CLEAR**, no change |
| 10 | `AiAwarenessCooldownTests.cs` (10 `Extend(...)` TimeSpans), `AiModerationBoundaryTests.cs:156,189` (`AiEscalationThresholds` Window/Cooldown) | various | NOT wall-clock — injected clocks (`AiAwarenessService` registry clock, `AiModerationEscalation(clock:)`); time is test-driven data | **CLEAR**, no change |
| 11 | `FlushAsync(TimeSpan.FromSeconds(5))` ×9 (`AiMemoryPipelineTests` ×6, `AiMemoryStoreTests` ×2, `AiMemoryPromptAssemblyTests` teardown) | 5 s method-arg bound | Never-elapses tripwire: `PersistenceStore.FlushAsync` treats elapse as "shutdown continues"; the temp-dir write completes in ms. Method-ARGUMENT shape — outside the option-assignment token alphabet | **CLEAR** with the surface named in the honesty cell |
| 12 | `PanicAsync(...)` ×3 (`AiOperationPipelineTests.cs:249` 10 s, `:274` 200 ms; `AiAwarenessTests.cs:362` 5 s) + `AiProviderLabIntegrationTests` `PanicAsync(2 s)` | method-arg drain bounds | Bounded-drain SUBJECT (the elapsed-time assertions are already SP-059 class 3). Method-argument shape | **CLEAR**, same honesty-cell note |
| 13 | `AiAwarenessTests.cs` cooldown Extends (`:161,:183,:212`), `FormatDuration` args, `TimeSpan.Zero` date offsets (`AiAwarenessCooldownTests:16`, `AiAwarenessTests:70`, `AiMemoryStoreTests:204`, `AiModerationBoundaryTests:155,188`, `BarkPipelineTests:512,714`, `DtrhFxRouterTests:158`, `DtrhNativeEffectsTests:503`, `DtrhWatchdogTests:14`) | various | Test-driven simulated time / date arithmetic — never a machine wall clock | **CLEAR**, no change |

Headless project: **zero** option-assignment or config budget sites (sweep axes produced
no hits under `CcpClient.HeadlessTests/`).

### Design

**(i) Guard extension.** `TestTimingGuardTests.ForbiddenTokens` gains ONE token:
`"Timeout = TimeSpan."` — the option-assignment budget shape (catches `RequestTimeout =`,
`ProbeTimeout =`, and any future `*Timeout = TimeSpan.` initializer, including
`.Timeout =` on client objects). Same marker (`// wallclock-allow:` + reason), same Pins
discipline (repo-relative path + exact trimmed code + expected count; unmarked → fail,
marked-but-unpinned → fail, count mismatch → fail, stale pin → fail). Two new pins:
`RequestTimeout = TimeSpan.FromMilliseconds(800),` count 1 in
`CcpClient.Tests/LoopbackOllamaProviderTests.cs` and count 1 in
`CcpClient.Tests/AiProviderLabIntegrationTests.cs`. Deliberately NOT tokenized:
`Timeout = Timeout.InfiniteTimeSpan` (the sanctioned deadline-REMOVAL shape — any later
edit that turns it into a real deadline trips the token), simulated-clock config values,
and method-argument bounds (honesty cell). **Bite proof:** an unpinned injected budget
will be introduced, the guard's RED captured (SP-056 red-demo discipline), then removed.

**(ii) Deterministic fix at the named site (and its siblings).** Classification/round-trip/
cancellation-subject tests stop injecting a wall-clock deadline: the provider is built with
`RequestTimeout = Timeout.InfiniteTimeSpan` / `ProbeTimeout = Timeout.InfiniteTimeSpan`.
The lab terminates every response deterministically (ContentLength64 + close = EOF; refused
loopback connect = instant `HttpRequestException`; pre-socket rejection = no socket), so
the outcome is decided by the lab's behavior alone — deterministic EOF/stream-close
classification. The two timeout-classification tests keep 800 ms as 4a pinned budgets
(elapsing is their subject). No product-code change: framing (c) examined and NOT invoked —
on the cold path the deadline fires before ANY bytes arrive, so no classification-seam
change (distinguishing stream-closed-mid-document from deadline-elapsed) can help; the
race is in the test's options, not the product's classification. User-path timeout
semantics untouched.

**Rejected alternatives:**

- *Raise 800 ms* — the banned fix (a bigger number is the same bug with a longer fuse).
- *Product default (5 min) instead of infinite* — still a wall-clock deadline that can
  decide an outcome (a 5-minute fuse); also hides a product hang for 5 minutes. Infinite
  makes "no time dependence" explicit; a genuine product hang wedges the test host LOUD
  instead of failing 5 minutes late with a misleading `timeout` classification.
- *Test-fixture warmup (pre-JIT the provider/HttpListener before assertions)* — hides the
  race behind hidden ordering; the deadline still decides outcomes whenever warmup is
  insufficient. Does not remove the time dependence.
- *Retry the assertion / poll for the expected classification* — converts a wrong
  classification into a wait loop; the outcome still depends on the 800 ms race.
- *Product-side classification change (framing c)* — cannot address the mechanism (the
  deadline fires pre-body); would touch the user path for zero gain.
- *Mark skipped on cold* — banned (a skip is a red under this packet) and hides the defect.

**(iii) Stale pins.** SP-062 touched `AiProviderLab.cs` (record-ordering); its three pins
were verified green in all 20 SP-062 runs and re-verified here — no stale pins expected;
any that appear will be updated WITH the reason.

**(iv) Floor.** No facts added or removed: the floor stays **892 unit / 35 headless,
0 skipped**, proven in every Step-3 run.

### Pre-approach solo consult

(verdict + actual answering model recorded below after the call)
