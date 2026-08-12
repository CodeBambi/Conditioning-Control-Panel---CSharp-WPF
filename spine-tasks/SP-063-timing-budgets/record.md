# SP-063 — Raise the injected test timeout budgets (owner decree) — record

## The decree (verbatim)

> **OWNER DECREE 2026-08-12 (chat, verbatim): "Just increase the amount of budgets by a lot! So it does not happen again."**

This supersedes the SP-059-inherited "raising a timeout budget is a banned fix" standing
order **for this row only** (the ban was a consult-derived rule; the owner is authority
order #1). Implemented as decreed, with the packet's two stated deviations: (1) the two
tests whose expected outcome IS `timeout` keep short budgets; (2) the raise is large but
FINITE (60 s), never `Timeout.InfiniteTimeSpan`.

**Dissent paragraph (framing b — the decree is implemented regardless):** a bigger budget
lengthens the fuse; it does not remove the time dependence. The deterministic alternative
(remove the deadline entirely at classification-subject sites, letting the lab's
stream-close be the only terminator) was examined in the aborted run's Step 1 and set
aside by owner decree, not refuted. See the honesty cell below for what remains true.

## Step 1 — sweep verification (re-run on the current tree @ 3023de4d)

Axes re-run: option-style `X = TimeSpan.` assignments (timeout/delay/interval/window/
cooldown/budget/watchdog/pacing names), all `Timeout =` forms, `CancelAfter(` /
`new CancellationTokenSource(TimeSpan`, `Timeout.InfiniteTimeSpan` / `TimeSpan.Zero`,
and method-argument `TimeSpan` bounds handed to product calls (`FlushAsync`,
`PanicAsync`). Both test projects swept. Current tree is `4fae614d` (the prior sweep's
base) + one task-authoring chore commit; **no test file changed between the sweeps**.

| # | Site (file:line @ 3023de4d) | Literal | Population | Treatment | Verdict vs prior sweep |
|---|---|---|---|---|---|
| 1 | `LoopbackOllamaProviderTests.cs:25-26` — shared `Provider(lab)` factory (Ok, 429×2, 500, 404, refusal, malformed, **truncated :158 (named site)**, mid-stream-cancel, probe-up) | RequestTimeout 800 ms, ProbeTimeout 800 ms | **1** (must not decide the outcome) | → `TestWait.InjectedBudget` (60 s) | **confirmed** |
| 2 | `LoopbackOllamaProviderTests.cs:47` `Timeout_Classifier_Bounded_ExternalTokenNotCancelled` | RequestTimeout 800 ms | **2** (elapsing IS the subject) | keep 800 ms, own options block, `// wallclock-allow:` + pin | **CORRECTED** — prior sweep claimed an "own options block"; the test actually calls the SHARED `Provider(lab)` factory (`:50`). Raising the factory would break its `elapsed < 800 + 2500` bound, so it gets its own block now |
| 3 | `LoopbackOllamaProviderTests.cs:178` `RemoteHost_RejectedPreSocket` | RequestTimeout 800 ms | **3** (inert — pre-socket rejection, `SendAttempts==0`) | delete assignment | **confirmed** |
| 4 | `LoopbackOllamaProviderTests.cs:210` `Probe_LoopbackDown` | ProbeTimeout 800 ms | **3** (inert — verified in product: probe timeout fire → `HostUnreachable` (`LoopbackOllamaProvider.cs:232`), connection-refused → `HostUnreachable` (`:237`); SAME typed code, and refused loopback is kernel-instant) | delete assignment (2 s product default stands) | **confirmed — with the advisor-demanded product-code check the prior sweep only asserted** |
| 5 | `LoopbackOllamaProviderTests.cs:225` `Probe_RemoteHost_ZeroSocket` | ProbeTimeout 800 ms | **3** (inert — pre-socket) | delete assignment | **confirmed** |
| 6 | `AiProviderLabIntegrationTests.cs:53-54` — shared `Harness` factory (all 11 integration tests) | RequestTimeout 800 ms, ProbeTimeout 800 ms | **1** | → `TestWait.InjectedBudget` | **confirmed** |
| 7 | `AiProviderLabIntegrationTests.cs:143` `Timeout_ThroughPipeline_TypedUnavailable_TokenNotPoisoned_LabSeesClientGone` | RequestTimeout 800 ms | **2** | keep 800 ms via record-`with` in `Harness(timeoutSubject: true)`, `// wallclock-allow:` + pin; ProbeTimeout stays at the constant (the probe there is not the subject) | **CORRECTED** — prior sweep claimed an "explicit options factory in the test"; the test actually uses the SHARED `new Harness()` (`:145`) |
| 8 | `AiMemoryPromptAssemblyTests.cs:250` wire-payload proof | RequestTimeout 10 s | **1** (subject is wire shape; WireListener replies synchronously) | → `TestWait.InjectedBudget` | **confirmed** |
| 9 | `SoundArbitrationTests.cs:27-28` DuckWatchdog 5 min, VoicePacingDelay 2 s | — | not wall-clock (injected `ManualClock`) | no change | **confirmed CLEAR** |
| 10 | `AiAwarenessCooldownTests` Extends, `AiModerationBoundaryTests:156,189` threshold windows | — | not wall-clock (injected clocks; test-driven time) | no change | **confirmed CLEAR** |
| 11 | `FlushAsync(TimeSpan…)` / `PanicAsync(TimeSpan…)` method-argument bounds | 5 s / 10 s / 200 ms / 2 s / 1 s / 100 ms | method-ARGUMENT shape — outside the option-assignment token alphabet; never-elapses tripwires or bounded-drain subjects | no change; named in the honesty cell | **CORRECTED (extended)** — prior sweep listed 9 FlushAsync + 4 PanicAsync sites; the re-run found more: `DtrhSaveSlotsTests:31`, `PersistenceTests:215`, `TeardownFlushTests:30,58,112`, `CompanionMemoryRearmTests:81`. Same disposition (CLEAR) |
| 12 | `TestWait.cs:30` `DefaultWindow = TimeSpan.FromSeconds(20)` | 20 s | the approved helper itself (guard-exempt file) | no change | **NEWLY LISTED** — prior sweep did not name it; CLEAR (not an injected product budget) |
| 13 | `TimeSpan.Zero` date offsets / simulated-clock `Advance` / `FormatDuration` args (AiAwareness*, AiMemoryStoreTests:204, AiModerationBoundaryTests:155,188, BarkPipelineTests:512,714, Dtrh*:158,503,14, Intake*) | — | test-driven simulated time / date arithmetic | no change | **confirmed CLEAR** (Intake* sites also present — same disposition) |

Headless project: **zero** option-assignment or config budget sites under
`CcpClient.HeadlessTests/` — prior sweep **confirmed** (axes produced no hits).

Nothing else the prior sweep missed that changes treatment. Net: 11 of 13 rows
confirmed; rows 2 and 7 corrected (both timeout-subject tests ride the SHARED factories
— this changes the edit shape, not the classification); row 11 extended with 6 additional
CLEAR method-argument sites; row 12 newly listed.

## Step 1 — the shared constant

**`TestWait.InjectedBudget = TimeSpan.FromSeconds(60)`** — one definition in the existing
guard-exempt timing file (`TestWait.cs`, the timing-discipline home; no new file, and no
guard-token naming trap — the field name does not end in `Timeout`). 60 s is ~75× the old
800 ms budget and far beyond any plausible cold-start JIT/`HttpListener` warmup, while
staying FINITE: a genuinely wedged lab or product hang fails loudly in a minute instead
of hanging the test host forever (this suite has no per-test timeout). The prior sweep's
recommended `Timeout.InfiniteTimeSpan` is overridden by framing (a). No different value
is justified; the 60 s default stands.

## Step 1 — expected wall-clock impact

On a green run the population-1 budgets are never reached: the lab terminates every
response deterministically (ContentLength64 + close = immediate EOF; refused loopback =
kernel-instant; pre-socket rejection = no socket), so the round trips complete in
milliseconds as they do today. The ONLY budgets that can elapse on a green run are
population 2's two short budgets (~800 ms each — exactly what they cost today). The
population-3 deletions replace an 800 ms literal with a never-reached product default on
instant paths. **Expectation: suite wall-clock unchanged within noise.** Checked against
the per-run wall-clock column in Step 3.

## Step 1 — pre-approach solo consult

- Mode: solo (T-7). Question scoped to EXECUTION only; the decree stated as settled.
- **Verdict: proceed with the plan as stated**, with three sharpenings, all adopted:
  1. (a) record-`with` in the Harness is the right shape for population 2 — keeps the
     guard-token-matchable option-assignment form; wire `Provider` from the modified
     options; leave ProbeTimeout at the constant in the timeout-subject harness.
  2. (b) the constant's home is a close call; advisor's lean: `TestWait.cs` as
     `TestWait.InjectedBudget` — adopted (no new file, no naming trap).
  3. (c) row 4's "doubly inert" claim must be VERIFIED in product code, not asserted —
     done above (`LoopbackOllamaProvider.cs:232` vs `:237`, both `HostUnreachable`).
     Advisor also prompted: check Harness host-param call sites (`:304` remote host —
     probe is pre-socket rejection, instant at any budget — safe) and the mid-stream
     cancel test under a 60 s budget (lab's Timeout-mode hold is 1 500 ms; the test
     cancels on the observed partial body via `TestWait`, far inside any budget — safe).
- **ACTUAL answering model:** not disclosed by the consult tool's return. Observable facts: the worker session runs `kimi-coding/k3` (PI_PROVIDER/PI_MODEL env); the consult response arrived as a solo advisory with no model banner. Recorded honestly as "unverifiable from inside the worker" rather than guessed (packet named Opus 5 main / Fable 5 fallback as intent, not as observable).

## T-2 review log

- Step 1 plan review: **ABSENT** — `spine_review_step(step=1, type=plan)` returned `skipped: true`, `spawnFailed: false` ("Nested reviewer spawn blocked inside pi worker session … the batch engine runs reviews after worker success (SP-195)"). Artifact: `.reviews/1-20260812T224924.md`. Not a spawn failure, so no fail-closed exit; engine review runs after `.DONE`.
- Step 2 plan review: **ABSENT** — same skip (`SP-195`), `spawnFailed: false`. Artifact: `.reviews/2-20260812T225427.md`.
- Step 3 plan review: **ABSENT** — same skip (`SP-195`), `spawnFailed: false`. Artifact: `.reviews/3-20260812T231042.md`.

## Step 2 — what was applied

**Population 1 → `TestWait.InjectedBudget` (60 s), referenced from three sites:**
`LoopbackOllamaProviderTests.cs` shared `Provider(lab)` factory (request + probe),
`AiProviderLabIntegrationTests.cs` shared `Harness` factory (request + probe),
`AiMemoryPromptAssemblyTests.cs` wire-payload proof (was 10 s).

**Population 2 → short, marked, pinned (two sites):**
- `LoopbackOllamaProviderTests.cs` `Timeout_Classifier_Bounded_ExternalTokenNotCancelled`
  now builds its OWN options block (it previously rode the shared factory — prior-sweep
  correction): `RequestTimeout = TimeSpan.FromMilliseconds(800), // wallclock-allow: the
  budget's elapsing IS the subject …`. Retry defaults to `Off` (as the shared factory had
  it); the `elapsed < 800 + 2500` boundedness assertion is unchanged.
- `AiProviderLabIntegrationTests.cs` `Harness` gains `bool timeoutSubject = false`; when
  set, `options = options with { RequestTimeout = TimeSpan.FromMilliseconds(800) }; //
  wallclock-allow: …` — the record-`with` keeps the guard-token-matchable option-
  assignment shape (advisor sharpening (a)); **ProbeTimeout stays at the constant** (the
  probe is not the subject; a cold probe timeout would flip the capability state and
  every pipeline expectation with it).

**Population 3 → deleted (three sites):** `RemoteHost_RejectedPreSocket` (request),
`Probe_LoopbackDown` (probe), `Probe_RemoteHost_ZeroSocket` (probe). Product defaults
(5 min request / 2 s probe) are never reached on these instant paths — verified in
product code (`LoopbackOllamaProvider.cs:232` vs `:237`: probe-timeout and
connection-refused both classify `HostUnreachable`).

**Guard (framing f — one token, two pins, no new fact):** `TestTimingGuardTests.
ForbiddenTokens` gains `"Timeout = TimeSpan."` (matches `RequestTimeout =`,
`ProbeTimeout =`, any future `*Timeout = TimeSpan.` initializer); `Pins` gains the two
population-2 entries above (path + exact trimmed code + count 1 each). Deliberately NOT
tokenized: method-argument bounds (`FlushAsync`/`PanicAsync`), simulated-clock config,
and the constant reference itself (honesty cell).

**Guard RED captured (SP-056 red-demo discipline):** injected
`RequestTimeout = TimeSpan.FromMilliseconds(123),` (unmarked, unpinned) into
`RemoteHost_RejectedPreSocket`; the guard failed with
`CcpClient.Tests/LoopbackOllamaProviderTests.cs:182: unmarked wall-clock construct — …`
(`evidence/guard-red-injected.log`); the injection was removed and the guard re-run green
(filter run: 1/1 passed). This also proves no stale pins: the guard's pin ledger (incl.
the three `AiProviderLab.cs` pins SP-062 touched) matches the tree exactly.

## Step 3 — run matrix (10 consecutive greens)

Full output + TRX under `evidence/runs/` and `evidence/trx/` (TRX force-added — `*.trx`
is gitignored repo-wide). Run 1 is a NEW detached worktree (`C:/Code/ccp-sp063-cold`,
first-ever build, removed after); runs 2–10 warm in the lane.

| Run | Worktree | Cold/Warm | Wall (s, unit+headless) | Unit | Unit skipped | Headless | Headless skipped | Named test |
|---|---|---|---|---|---|---|---|---|
| 01 | ccp-sp063-cold | **cold (first-ever build)** | 77 | 892 | 0 | 35 | 0 | green |
| 02 | lane-1 | warm | 84 | 892 | 0 | 35 | 0 | green |
| 03 | lane-1 | warm | 72 | 892 | 0 | 35 | 0 | green |
| 04 | lane-1 | warm | 71 | 892 | 0 | 35 | 0 | green |
| 05 | lane-1 | warm | 71 | 892 | 0 | 35 | 0 | green |
| 06 | lane-1 | warm | 72 | 892 | 0 | 35 | 0 | green |
| 07 | lane-1 | warm | 71 | 892 | 0 | 35 | 0 | green |
| 08 | lane-1 | warm | 71 | 892 | 0 | 35 | 0 | green |
| 09 | lane-1 | warm | 71 | 892 | 0 | 35 | 0 | green |
| 10 | lane-1 | warm | 72 | 892 | 0 | 35 | 0 | green |

TRX-verified post hoc (not the script's shorthand): `Truncated_PrefixCut_NeverSurfaced_
TypedUnavailable` has `outcome="Passed"` in all 10 unit TRX; `outcome="Failed"` count is
0 in all 20 TRX.

**Wall-clock vs the Step-1 expectation:** unchanged within noise. Expectation was: only
population-2's ~800 ms budgets can elapse on a green run; population-1 budgets are never
reached. Observed: warm runs sit in a 71–84 s band with no run near any 60 s boundary —
had any raised budget fired, that run's wall-clock would have jumped by ~60 s per firing.
The cold first-ever-build run lands in the same band (77 s), i.e. cold-start warmup is
seconds, and now has ~75× headroom instead of a sub-second fuse.

## Honesty cell — what a bigger number does NOT fix

- **The time dependence remains; the fuse is longer.** The classification/round-trip/
  cancellation tests still inject a wall-clock deadline into product code. The cold-start
  flake class returns if any future machine or test is ~75× slower than today (a >60 s
  first round trip), or if someone lowers the constant. This change removes the OBSERVED
  failure mode, not the mechanism.
- **Population-2 tests still depend on wall time BY DESIGN** — their subject is the
  budget's elapsing (bounded timeout classification, token-not-poisoned). They are marked
  and pinned; a pathologically slow machine can still push their `elapsed < 800 + 2500`
  bounds.
- **The guard catches the option-assignment SHAPE only** (`XyzTimeout = TimeSpan.`
  literal). A budget expressed as a named constant, a computed `TimeSpan`, a method
  argument (`FlushAsync`/`PanicAsync` bounds — 18 sites, CLEAR in the sweep), a
  differently-spaced assignment (`Timeout=TimeSpan.`), or a non-`TimeSpan.` factory call
  evades the token. That is the declared smallness of the guard (framing f), not an
  oversight to patch silently.
- **`TestWait.cs` is guard-exempt wholesale** — the constant lives there deliberately
  (one definition, no naming trap), which also means a future literal budget edited INTO
  that file would not trip the guard. Accepted trade-off, named here.
- **The deterministic alternative was set aside, not refuted.** Removing the deadline at
  classification-subject sites entirely (lab stream-close as the only terminator — the
  aborted run's design) would have removed the time dependence rather than lengthening
  its fuse. The owner decree chose the budget raise; this record implements it and says
  plainly what it does not do.

## Intended board filings (ENABLER 2 — worker sets no row state; orchestrator reconciles at land)

- `client/docs/task-board.md`: close the fourth-occurrence budgets row with this packet's
  evidence (10 consecutive greens at 892/35/0 incl. one fresh-checkout first-ever build;
  guard token + captured RED; decree quoted; honesty-cell pointer).
- `client/docs/port-lessons.md`: one entry — owner decree 2026-08-12 supersedes the
  "raising a budget is the banned fix" consult-derived rule FOR THAT ROW; the constant is
  finite (60 s) by packet framing so a wedged lab still fails loudly; the guard now
  catches the option-assignment budget shape with pins.
- `client/docs/upstream-sync.md`: no filing — no WPF-upstream interaction in this packet.

## Step 4 — pre-completion solo consult

- Mode: solo (T-7).
- **Verdict: clear to complete.** Four closing checks named, all run:
  1. Contract `testCommand` (incl. `verify.mjs`, not yet run this session) — run as Step 5 below.
  2. Cold worktree registration — `git worktree list` confirms `ccp-sp063-cold` removed.
  3. Scope diff — confirmed in Step 5 (`git status --short`: File Scope paths only; zero `client/src/**` changes).
  4. Stale `800` literals in the two edited files — grep found only the two pinned population-2 budgets, their two `elapsed < 800 + 2500` boundedness assertions (unchanged by design), and ONE now-stale COMMENT at `LoopbackOllamaProviderTests.cs:244` ("800ms request timeout would also fire" — the factory is now 60 s); comment corrected to name no number.
- **Advisor-noted latent trade-off (adopted into the record):** the `uncooperative: true`
  transport test (`SlowOk_LateCompletion_…`, `AiProviderLabIntegrationTests.cs:251`) is
  bounded on the pass path by the lab's SlowOk 1.5 s-late body and the stale-discard seam
  (plus `PanicAsync(2 s)` drain), NOT by the provider budget — verified by reading the
  test and by ten wall-clock runs with no 60 s signature. On a FAILURE path (body never
  arrives) that test now sits up to 60 s instead of 800 ms before failing loudly — the
  bounded-fuse trade the decree accepts.
- **ACTUAL answering model:** not disclosed by the consult tool's return (same as the
  pre-approach call — worker session model is `kimi-coding/k3`; the consult's answering
  model is unverifiable from inside the worker and is recorded as such, not guessed).

<!-- Step 5 contract results appended below -->
