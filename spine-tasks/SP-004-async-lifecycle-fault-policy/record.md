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

`client/docs/async-lifecycle-fault-contract.md` — see doc. (Step 1 deliverable; summary finalized in Step 4.)

---

## Dispatch-binding decision

Late-bound in phase 4 via `ApplicationHost.BindUiDispatch(IUiDispatch)`, never `SynchronizationContext.Current` capture (null pre-Avalonia — silently broken boundary fake-injected tests cannot catch; packet amendment per truncated pre-authoring Fable consult). Post-only interface per pre-approach consult (e): no awaitable dispatch call exists, so the shutdown-deadlock class is eliminated by construction.

---

## Engine-review presence/absence note

(Filled in Step 4 — `spine_review_step` results recorded here as called.)

---

## Test output

(Filled in Step 5.)

## Headed smoke

(Filled in Step 3.)

## Surprises

(Filled in Step 4.)
