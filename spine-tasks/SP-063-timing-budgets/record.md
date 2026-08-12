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

(engine-review presence/absence recorded per call — filled as the batch proceeds)

<!-- Step 2/3/4 content appended below -->
