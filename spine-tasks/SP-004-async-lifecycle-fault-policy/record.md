# SP-004 record — async lifecycle and fault policy

**Task:** task-board row 3 (P0, Phase 1). **Date:** 2026-07-18. **Worker:** kimi-coding/k3 via pi-spine batch.

---

## Consult verdicts

### Pre-approach solo consult (Fable 5) — 2026-07-18

First call truncated after answering (a)/(b) (same truncation failure mode as this packet's pre-authoring consult, recorded in PROMPT.md Amendments). A narrowed follow-up recovered (c)–(e) in full. Combined verdict text (persisted before checkbox, per packet write-then-check rule):

> **PLAN — outline is sound and matches the packet.**
>
> **(a) Per-owner generation — yes, and it's not just simpler, it's what the packet mandates.** Step 2 literally says "a monotonic cancellation generation per owner." Per-operation generations would also break the stale-completion semantics you want: "restart of participant invalidates all its prior in-flight work" only falls out naturally when the generation lives on the owner. Confirm per-owner, bumped on owner stop/restart.
>
> **(b) Real deadlock hazard — eliminate it by construction, not by bounded wait alone.** SP-003's `desktop.Exit += (_, _) => _host.ShutdownAsync().GetAwaiter().GetResult();` and both `Main` paths mean **ShutdownAsync is always blocked-on from the UI thread.** If ShutdownAsync awaits op completions while an in-flight op is awaiting a dispatch call back onto that same blocked UI thread, you deadlock. [truncated here]
>
> **(c) Explicit throw — not typed failure.** Three reasons: (1) An unbound boundary is a composition/sequencing bug, the same class as Build-before-Validate, which SP-003 already handles by `InvalidOperationException`. Typed outcomes are for runtime operational faults; converting a wiring bug into `Degraded` means startup "succeeds" with a silently broken UI path — exactly the failure the packet's amendment (null `SynchronizationContext` = silently broken boundary) is trying to prevent. (2) `Post` returns void. A "typed failure" from a bare `Post` has no outcome channel — it degenerates to silent drop or log-only, the swallowed-failure pattern the bans forbid. (3) Typed-outcome uniformity is kept anyway: if the throw happens inside an operation body, the operation's single trap boundary classifies it (default Fatal) and routes it to the registry. Ops still terminate with typed outcomes; only naked misuse throws. Consequence: Heartbeat starts in phase 3 and its first tick can race phase-4 binding — the demonstrator's tick loop must skip UI projection until bound (documented, tested behavior: unbound `Post` throws; the demonstrator skips-until-bound without faulting).
>
> **(d) No violations, with three precision points:** (1) Don't literally extend `InitFailureKind` with Completed/Cancelled — that would pollute SP-003's startup taxonomy. Make a new `OperationOutcome` result type whose failure kinds reuse the `Recoverable`/`Degraded`/`Fatal` classification vocabulary (what "activating the reserved members, not a parallel enum" means — same vocabulary, not the same enum forced to carry startup and operation cases). (2) The zero-unobserved-at-teardown assertion cannot throw inside `ShutdownAsync` — SP-003's "teardown never throws" invariant stands. `ShutdownAsync` logs/records the violation; the test asserts via registry state. (3) Stale-post safety: the posted delegate must do its generation/current check on the UI thread inside the delegate, because during shutdown the UI thread is blocked in `GetAwaiter().GetResult()` and queued posts may run late or never. A posted delegate must be harmless if it executes stale or not at all. Document that in the per-stream delivery context.
>
> **(e) Yes — and go one step further: delete `InvokeAsync` from the interface entirely.** The demonstrator needs only `Post`. With no awaitable dispatch call, the deadlock class is eliminated by construction. Contract states: UI projection is post-and-forget with stale-check; operations never await the UI thread; cancellation (not the UI thread) is what unblocks in-flight ops; `ShutdownAsync` orders cancel-generation → await owned completions → assert-observed → stop participants. A bounded await timeout may remain as a backstop but must not be the mechanism that makes shutdown terminate. Re-admit `InvokeAsync` only when a real consumer needs a result from the UI thread — and that admission must re-answer the shutdown-deadlock question.
>
> Proceed: persist this verdict in record.md, then write the contract with (c) throw + skip-until-bound, the `OperationOutcome` type split, Post-only boundary, and the shutdown ordering above.

**Disposition:** all points accepted: per-owner generation; Post-only `IUiDispatch` (no `InvokeAsync`); unbound `Post` throws `InvalidOperationException` while the demonstrator skips-until-bound via `IsBound` (both tested); new `OperationOutcome` type reusing the SP-003 failure-kind vocabulary without extending `InitFailureKind`; teardown violation is logged/recorded, asserted by tests via registry state; posted delegates do their generation check on the UI thread; shutdown ordering = cancel generation → await owned completions → record unobserved → stop participants (bounded await as backstop only).

### Pre-completion solo consult (Fable 5) — 2026-07-18

Verdict text (persisted before checkbox, per write-then-check; truncated only inside the mechanical close-out list):

> **PLAN — the work meets the completion criteria as far as this transcript shows.**
>
> **(a) Yes — honest terminal outcome on the completion task is the right semantic. Keep it.** The completion task is the ownership/observation channel; the generation check governs *state application*, not *observation*. Reporting `Cancelled` for a stale op that actually ran to completion would fabricate an outcome that didn't happen. Two truths stay separate: "what did this operation do" (completion task, always honest) and "may this result touch current state" (generation check at the point of application, contract §3.3). That separation is also what makes zero-unobserved accounting work: a stale op is still *observed*, just not *applied*. The one hazard — a caller awaiting a stale task and acting on `Completed` without a generation check — is the rule the contract already states, and the only current consumer (heartbeat) checks inside the posted delegate. [Sentence added to contract §3.3.]
>
> **(b) Acceptable — not a second ownership path.** The 3-arg ctor's throwaway registry is still *the* registry for that host: drain runs against it, and owner-less participants register nothing. A second ownership path would mean a second way to start/cancel/observe operations — that doesn't exist. The latent hazard is constructing a participant with an owner from registry A and a host holding registry B (hit and fixed in `IntegrationProofTests`). [Doc-comment added to the 3-arg ctor per the consult's hardening suggestion.]
>
> **(c) No blockers.** Trivia: redundant `.OfType<Task>().Cast<Task>()` in `CancelAndDrainAsync` [fixed]; the Track-before-Completion-assignment window is documented and unreachable in practice. Criteria audit: contract doc with all required sections incl. row-5 boundary sentence ✓; generation invalidation / mid-flight cancel / deterministic fault routing (not `UnobservedTaskException`) / zero-unobserved all tested ✓; pre-binding rule tested both ways ✓; headed smoke *observed* the background callback (tick 16→24 via UIA) with exit 0 ✓; SP-003's 23 tests intact, no second teardown path ✓; no new packages, no DI, no Headless admission ✓; consult truncation handled per packet precedent ✓. The no-amendment disposition on SP-003's "reserved" note is correct — the reservation covers *phase* failure kinds, which remain unexercised.
>
> **Close-out:** persist verdict before checkbox; board row → `WIP` citing record.md; STATUS to reality; re-run full testCommand + `git diff --check` + `git status --short` (File Scope only); commit; `.DONE`. [truncated tail]

**Disposition:** no blockers; both hardening suggestions applied (3-arg-ctor doc-comment, `OfType`/`Cast` dedup) plus the §3.3 clarifying sentence; close-out executed in the stated order.

---

## WPF async/dispatcher evidence digest (outcomes only, no mechanics transplant)

Source: `ConditioningControlPanel/App.xaml.cs` (3345 lines — grep + focused reads per wpf-parity), `ConditioningControlPanel/CLAUDE.md` threading notes, `ConditioningControlPanel/CCP.Core/Services/Deeper/IActionDispatcher.cs`. Read-only behavioral evidence.

**Crash/fault handling tiers (VERIFIED):**
- Three global hooks exist and have distinct tiers (`App.xaml.cs:1184-1255`): `DispatcherUnhandledException` (log + tiered response), `AppDomain.UnhandledException` (log), `TaskScheduler.UnobservedTaskException` (log + `SetObserved`).
- Dispatcher exceptions are **classified before response**: known-recoverable native quota failures (GDI/desktop-heap exhaustion, `Win32Exception` 1816/1450) are deliberately dropped with a warning — "the failed window-show just drops a frame" (`1189-1202`); render-thread failure/OOM is unrecoverable and triggers **immediate process exit without a dialog** because a nested message pump cascade-crashed (2026-05-25 crash storm, 10,251 reports) (`1204-1225`); everything else logs and shows one guarded dialog (`1227-1243`).
- Greenfield translation: **classification before response** is the retained outcome — faults carry a kind and the kind decides the consequence. The blanket `Handled = true` continuation for arbitrary exceptions is NOT retained (first-attempt lesson: catch-and-continue without typed outcome).

**Dispatcher/threading outcomes (VERIFIED):**
- UI-thread work must use the Dispatcher; some timers must be `DispatcherTimer`, not `Task.Delay` loops (CLAUDE.md known-issues, threading notes in `IActionDispatcher.cs`).
- Cross-thread mutation of bound state was a recurring defect class in both WPF and the first Avalonia attempt (`first-attempt-systemic-lessons.md` — haptics status-brush cross-thread crash `40a4b7c1`, off-thread bound-property mutation `5958038f`).
- `IActionDispatcher.cs:40-43` shows the retained shape: a dispatch API whose cancellation token "fires when the engine that owns the dispatcher is stopped; used so long-running multi-step dispatches abort" — owner-scoped cancellation of in-flight work.
- Greenfield translation: one deliberate dispatch boundary; every event/stream documents its delivery context; owner-scoped cancellation tokens; no implicit callback-thread assumptions.

**Unobserved-work outcomes (VERIFIED):**
- WPF contains fire-and-forget `Task.Run` with internal try/catch (e.g. update cleanup `_ = Task.Run(...)` at `App.xaml.cs:1257-1265`) — acceptable only for genuinely optional work where the catch is total; the first attempt's widespread detached required work produced leaks, races, and exit segfaults (`first-attempt-systemic-lessons.md` lifecycle lesson, commits `db4ec5d0`, `9aab5206`, `f4a556a2`).
- Greenfield translation: required work is never detached — every required operation's completion is owned and observed through the registry.

---

## Contract summary

Deliverable: `client/docs/async-lifecycle-fault-contract.md`. Every long-running operation is registered in the runner's registry with exactly one owner (per-participant `AsyncOperationOwner`), a monotonic per-owner cancellation generation (incremented on each (re)start), an owned completion task, and a typed terminal outcome (`OperationOutcome`: Completed / Cancelled / Failed — failure kinds reuse the SP-003 `InitFailureKind` vocabulary directly, activating the reserved Recoverable/Degraded as per-operation owner-supplied classifications; capability probes are row 5). Stale completions (generation older than current) are discarded at the point of application — tested by injecting a late result. One deliberate UI dispatch boundary: Post-only `IUiDispatch`, late-bound in phase 4 via `ApplicationHost.BindUiDispatch` (SynchronizationContext capture banned); pre-binding `Post` throws `InvalidOperationException` (wiring bug, not a runtime fault) while long-running owners skip UI projection until bound; posted delegates re-check liveness on the UI thread. Teardown remains SP-003's single guarded entry point, extended: cancel generations → await owned completions (bounded wait as backstop only) → record unobserved in registry state (never throws) → reverse-order participant stop. Tested bans: no async void except genuine event handlers, no unobserved required work (zero-unobserved asserted at teardown), no blanket catch-as-success (deterministic registry routing, not `UnobservedTaskException`).

**SP-003 contract amendment check (Documentation Requirements):** evaluated — no amendment needed. SP-003's "reserved" note covers Recoverable/Degraded as *startup-phase* failure kinds, which remain unexercised and accurately reserved; this task activates the same vocabulary as *operation* outcome classifications in the new contract's separate `OperationOutcome` type. Amending SP-003's note would misstate its scope.

---

## Dispatch-binding decision

Late-bound in phase 4 via `ApplicationHost.BindUiDispatch(IUiDispatch)`, never `SynchronizationContext.Current` capture (null pre-Avalonia — silently broken boundary fake-injected tests cannot catch; packet amendment per truncated pre-authoring Fable consult). Post-only interface per pre-approach consult (e): no awaitable dispatch call exists, so the shutdown-deadlock class is eliminated by construction.

---

## Engine-review presence/absence note

`spine_review_step` was called after each step (1, 2, 3) and returned **skipped=true, reviewLevel=0, spawnFailed=false** every time — fourth consecutive batch (SP-001…SP-004) with zero engine reviews; the review pipeline remains empirically dead (tooling row T-2). Worker-side quality gates substituted per the packet: two mandatory solo Fable consults (pre-approach, pre-completion) with verdict text persisted here, plus the contract testCommand and headed smoke. Orchestrator verifies the journal at land.

---

## Test output

`dotnet build client/CcpClient.sln -c Debug --nologo` — succeeded, **0 warnings, 0 errors**.
`dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo` — **Passed: 34, Failed: 0, Skipped: 0** (Windows, .NET 10; 23 SP-003 + 11 new).

New-test coverage of the contract's tested rules: `RunAsync_WithoutBegin_ThrowsSequencingError` (no op outside a live generation); `StaleGenerationCompletion_IsDiscarded_CannotOverwriteNewerState` (out-of-order completion injected; `DiscardedStaleCompletions` = 1; newer state untouched); `CancellationMidFlight_YieldsTypedCancelled_NoUnhandledException`; `ResourceFailure_ClassifiedByOwner_RoutedAsTypedOutcome_NotSwallowed` (Recoverable), `DegradedOutcome_ClassifiedByOwner_RoutedAsTypedOutcome` (Degraded), `FaultingOperation_DefaultClassifier_MapsToFatal` (deterministic registry routing — no `UnobservedTaskException`); `Teardown_CancelsInFlightThroughSingleEntryPoint_AndReportsZeroUnobserved` (in-flight tick op completes typed `Cancelled` through `ShutdownAsync`; zero unobserved; SP-003 stop invariants intact); `Teardown_OrphanedOperation_IsRecordedInRegistryState_NeverThrows` (bounded wait expires; violation logged + counted, teardown never throws); `Post_BeforeBinding_ThrowsInvalidOperation` / `Bind_ThenPost_Dispatches_DoubleBind_Throws` (pre-binding rule); `Heartbeat_SkipsProjectionUntilBound_ThenFlowsThroughBoundary` (skip-until-bound, then real flow). All 23 SP-003 tests still pass — the single guarded teardown entry point is undisturbed.

## Headed smoke (Windows, 2026-07-18)

Ran `spine-tasks/SP-004-async-lifecycle-fault-policy/headed-smoke.ps1` against the Debug build (`CcpClient.Desktop.exe`, net10.0). Observed via UIA accessibility tree (not believed): window "CCP Client" rendered the SP-003 phase trace (`Bootstrap: ok / CompositionRoot: ok / CoreServices: ok / Heartbeat: running`) **and** the heartbeat tick text advancing across two samples (`Heartbeat: tick 16` → `Heartbeat: tick 24`) — a background callback demonstrably reached the window through the real dispatch boundary (`Dispatcher.UIThread.Post` via the late-bound `UiDispatchBoundary`). Graceful close (`CloseMainWindow`) → process exited within 10s with **exit code 0** — in-flight cancellation flowed through SP-003's single guarded teardown entry point. stderr carried no panic/unobserved logs.

---

## Step 3 notes

- `IUiDispatch` is Post-only per the pre-approach consult (e): `AvaloniaUiDispatch` wraps `Dispatcher.UIThread.Post`; `UiDispatchBoundary` holds the binding, throws `InvalidOperationException` on pre-binding `Post` and on double-bind, exposes `IsBound` for the skip-until-bound rule.
- Pre-binding rule tests: `Post_BeforeBinding_ThrowsInvalidOperation`, `Bind_ThenPost_Dispatches_DoubleBind_Throws`.
- Demonstrator skip-until-bound test: `Heartbeat_SkipsProjectionUntilBound_ThenFlowsThroughBoundary` — ticks accumulate pre-binding with zero posts attempted and no fault; after a fake boundary binds, the reporter receives tick text.
- Simulated native/resource failure tests: `ResourceFailure_ClassifiedByOwner_RoutedAsTypedOutcome_NotSwallowed` (Recoverable), `DegradedOutcome_ClassifiedByOwner_RoutedAsTypedOutcome` (Degraded), `FaultingOperation_DefaultClassifier_MapsToFatal` — the SP-003 reserved taxonomy members are activated as operation-outcome classifications.

## Surprises

- **Consult truncation recurred.** The first pre-approach call truncated mid-reply after (a)/(b) — the same failure mode as this packet's pre-authoring consult. A narrowed follow-up recovered (c)–(e) in full. The (e) answer changed the design: `IUiDispatch` is Post-only (no `InvokeAsync`), eliminating the shutdown-deadlock class by construction rather than by rule.
- **Deadlock hazard was real, not theoretical:** SP-003 invokes `ShutdownAsync` synchronously from the UI thread (lifetime `Exit` handler); any awaitable UI dispatch would have made teardown capable of deadlocking. The Post-only interface plus cancel-before-drain ordering removes it.
- **xUnit2014 is build-breaking:** `Assert.Throws` over a lambda returning `Task` is an analyzer *error*, even when the throw is synchronous; a void local function is the workaround.
- **`ParticipantInfrastructure` evolution:** handing participants their owner + boundary at composition (via the factory parameter) kept the no-locator rule while leaving SP-003's 3-arg `ApplicationHost` ctor intact for existing tests — registry/boundary are optional infrastructure there, unused by owner-less recording participants.
- **PowerShell UIA smoke:** method-call argument lists cannot span lines in PowerShell; script otherwise worked first try (tick 16 → tick 24 observed).
