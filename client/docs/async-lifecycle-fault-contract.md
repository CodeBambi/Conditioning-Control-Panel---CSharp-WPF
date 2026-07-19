# Async lifecycle and fault policy

**Date:** 2026-07-18 · **Task:** SP-004 (task-board row 3) · **Status:** ratified by implementation + tests in this slice; evidence in `spine-tasks/SP-004-async-lifecycle-fault-policy/record.md`

This contract instantiates `architecture-proposal.md` §6 (row-3 column: dispatcher discipline, cancellation generations, out-of-order completion policy) and the first-attempt async lessons (`first-attempt-systemic-lessons.md`: "Lifecycle completion must be owned and awaitable", "UI-thread ownership must be explicit", "Error swallowing hid product failure"). It extends — and does not disturb — the landed SP-003 lifecycle (`startup-shutdown-contract.md`): the runner's registry remains the sole owner set, and the single guarded teardown entry point remains the only teardown path. It implements no product features: it proves the async/fault shape through the SP-003 demonstrator participant.

---

## 1. Operation ownership rule

1. Every long-running operation (any work that outlives the call that started it) is registered with the runner's registry as an **owned operation**. An owned operation has exactly four things:
   - **Exactly one owner** — the registry entry of the background participant that started it. SP-003's one-owner rule (`startup-shutdown-contract.md` §5) is unchanged; this contract extends it from start/stop to the participant's in-flight work. No operation is started outside an owner, and no second object holds a cancel/observe path to it.
   - **A cancellation generation** — a monotonic integer owned by the participant owner (§3).
   - **An owned completion task** — the registry holds the operation's completion `Task`, so any caller (and teardown) can deterministically await the typed outcome. Fire-and-forget of required work does not exist.
   - **A typed terminal outcome** — exactly one of §2, delivered through the completion task.
2. The registry traps an operation's escaping exception **once**, at the operation boundary, and converts it to the operation's typed outcome via the owner-supplied classifier (§4). There is no second catch and no swallow-and-continue.
3. `async void` is forbidden except for genuine event handlers (UI/lifetime events that have no caller to observe a task — e.g., the lifetime `Exit` handler shape SP-003 already uses). An event handler that starts required work must register that work as an owned operation.
4. Optional best-effort work (genuinely droppable, e.g. cleanup that may fail silently) may run unregistered, but its body must carry a total catch and it must never be load-bearing for a product outcome. Required work is always registered.

## 2. Typed operation outcomes

`OperationOutcome` is the terminal result of an owned operation:

- `Completed` — the operation finished its work.
- `Cancelled` — the owner's generation was cancelled (teardown or owner stop); the operation observed the token and terminated. Not an error.
- `Failed(kind, reason, exception?)` — the operation faulted; `kind` reuses the SP-003 failure classification vocabulary:
  - `Recoverable` — the operation failed; the owning feature continues without it, with a named reason. **Activated by this contract** (reserved in SP-003 §2 with "first consumer is row 3").
  - `Degraded` — the operation completed with named reduced semantics. **Activated by this contract** (same reservation).
  - `Fatal` — the operation failed in a way the owner cannot absorb; the owning feature must treat it as a startup/lifecycle-class failure.

The classification vocabulary is shared with `InitFailureKind`; the outcome type is separate (it also carries `Completed`/`Cancelled`, which are not failure kinds). The SP-003 startup taxonomy itself is untouched.

**Row-3/row-5 boundary:** Recoverable/Degraded here are **operation-outcome classifications supplied per-operation by the owner**; capability-availability states and runtime probes (what a platform/backend can do at all) are row 5's contract — this contract builds no probe machinery.

## 3. Cancellation generations and stale completions

1. Each owner carries a monotonic **generation**: an integer that starts at 0 and increments every time the owner is (re)started after a stop. Starting an owner creates a fresh `CancellationTokenSource` linked to the host token; stopping or tearing down an owner cancels the current generation's source.
2. Every operation the owner starts captures the generation current at its start. A completion (or any state mutation, including a UI projection) arriving for generation *N* when the owner's current generation is *M > N* is **stale and discarded** — it cannot overwrite newer-generation state.
3. Out-of-order completion is therefore a non-event: a late result from a previous generation is checked against the current generation at the point of application and dropped. The owned completion task always reports the operation's honest terminal outcome; staleness gates **application**, not **observation** — a stale op is still observed, just not applied. Tests inject out-of-order completion, cancellation, and background-thread callbacks (first-attempt lesson: tests inject, not assume).
4. In-flight operations observe the generation token and terminate with the typed `Cancelled` outcome. Cancellation — never the UI thread — is what unblocks in-flight work (§6 rule 3).

## 4. Fault policy

1. The owner supplies a **classifier** — `Func<Exception, OperationFailureKind>` — when registering an operation. The registry applies it at the single trap boundary to produce the typed `Failed` outcome. The default classifier maps every exception to `Fatal`.
2. Expected, named failure modes are classified by the owner: a resource-style failure (device lost, quota exhausted, backend fault) the feature can survive is `Recoverable` or `Degraded`; anything else stays `Fatal`. WPF retained outcome: classification precedes response (quota exhaustion dropped a frame; render failure exited; record.md WPF digest). The WPF blanket `Handled = true` continuation is **not** retained.
3. A `Failed` outcome is observed state in the registry (per-owner last outcome), inspectable by tests and by later feature rows that surface bounded user-visible state. Logs remain diagnostic, never the only failure channel.
4. The panic backstop is unchanged: `TaskScheduler.UnobservedTaskException` stays installed from SP-003 phase 1 as a last-ditch logger. Because every required operation's completion is owned and awaited, that hook must never fire for registered work; it is untested here (GC-timing nondeterminism) and its silence is asserted indirectly by the zero-unobserved rule (§7 rule 4).

## 5. UI dispatch boundary

1. There is exactly **one deliberate UI dispatch boundary**: a small injected interface, `IUiDispatch`, with one method — `Post(Action)`. Production implementation wraps `Dispatcher.UIThread`; tests inject a fake. There is deliberately **no awaitable dispatch method**: with no way to synchronously wait on the UI thread, the shutdown-deadlock class (teardown blocked on the UI thread awaiting an operation that awaits the UI thread) is eliminated by construction. Re-admission of an awaitable dispatch requires a real consumer and must re-answer the shutdown-deadlock question.
2. **Late binding.** `IUiDispatch` is bound in phase 4 (`App.OnFrameworkInitializationCompleted` → `ApplicationHost.BindUiDispatch`). Phases 1–3 run before Avalonia exists, so there is no `SynchronizationContext` to capture at composition time; capturing `SynchronizationContext.Current` is **banned** (null capture = silently broken boundary that fake-injected tests cannot catch).
3. **Pre-binding rule.** Calling `Post` before the boundary is bound **throws `InvalidOperationException`** — an unbound boundary is a composition/sequencing bug (same class as SP-003's Build-before-Validate), not a runtime operational fault, so it must not degrade silently. If the throw happens inside an operation body, §4's trap classifies it (default `Fatal`) and the operation terminates typed. Long-running owners that start in phase 3 and may produce output before phase 4 binds (the demonstrator) must instead check `IsBound` and **skip UI projection until bound** — a documented, tested behavior, not a race.
4. **Delivery context, documented per stream.** This slice has exactly one stream crossing the boundary:

   | Stream | Producer context | Delivery context | Stale handling |
   |---|---|---|---|
   | Heartbeat tick text → placeholder window | Heartbeat operation body, thread-pool (its generation token's loop) | UI thread, via `IUiDispatch.Post` | Generation check runs **inside the posted delegate on the UI thread**; a stale or never-run post is harmless |

   Every future event/stream that crosses the boundary must add a row here naming its producer context, delivery context, and stale handling before it ships (first-attempt lesson: document delivery context per event/stream; reject implicit callback-thread assumptions).
5. Posted delegates must be harmless if they run late or never: during teardown the UI thread is blocked inside `ShutdownAsync` (SP-003 invokes it synchronously from the lifetime `Exit` handler), so queued posts may execute stale or not at all. The generation check inside the delegate is what makes this safe.

## 6. Shutdown ordering for async work

Teardown remains SP-003's single guarded entry point (`ApplicationHost.ShutdownAsync`). This contract extends its body; there is **no second teardown path** for async operations:

1. **Cancel generations** — every owner's current generation CTS is cancelled. In-flight operations observe their tokens.
2. **Await owned completions** — the registry awaits every registered operation's completion task (bounded wait as a backstop only; cancellation, not the timeout, is the mechanism that terminates well-behaved operations). Each completes with its typed outcome — `Cancelled` for well-behaved in-flight work.
3. **Record unobserved** — operations that have not completed after the bounded wait are recorded via the `ILogSink` seam and remain counted in registry state. SP-003's "teardown never throws" invariant stands: the zero-unobserved guarantee is asserted by **tests reading registry state**, not by teardown throwing.
4. **Stop participants** — the existing SP-003 reverse-order participant stop runs, unchanged.

`ShutdownAsync` never awaits the UI thread, and operations never await the UI thread (§5 rule 1), so steps 1–4 cannot deadlock.

## 7. Tested bans (contract rules)

1. **No `async void`** except genuine event handlers (§1 rule 3).
2. **No unobserved required work** — every required operation's completion is owned and observed through the registry; at teardown the registry reports zero unobserved operations (§6 rule 3, asserted by test).
3. **No blanket catch-as-success** — a faulting required operation surfaces a typed outcome, synchronously awaitable through the registry. Tests assert deterministic registry routing; they do **not** test via `TaskScheduler.UnobservedTaskException` (GC-timing flaky; that hook stays as the untested SP-003 backstop).
4. **No `SynchronizationContext.Current` capture** (§5 rule 2).
5. **No second teardown path** — in-flight cancellation and completion-drain flow through SP-003's guarded `ShutdownAsync` only.

---

## Conformance checklist (tested in this slice)

- Out-of-order completion from a stale generation is discarded: a late result cannot overwrite a newer generation's state.
- Cancellation mid-flight yields typed `Cancelled` with no unhandled exception.
- A faulting operation surfaces its typed outcome synchronously-awaitable through the registry (owner classifier maps a resource-style exception to `Recoverable`/`Degraded`; default classifier maps to `Fatal`).
- Registry reports zero unobserved operations at teardown; a deliberately orphaned operation is recorded (logged + registry state), never thrown from teardown.
- `Post` before binding throws `InvalidOperationException`; the demonstrator skips UI projection until bound without faulting.
- The demonstrator's tick reaches the placeholder window through the real boundary (headed Windows smoke: background callback observed reaching the window via UIA, graceful close, exit code 0).
- All SP-003 tests still pass; the single guarded teardown entry point is undisturbed.
