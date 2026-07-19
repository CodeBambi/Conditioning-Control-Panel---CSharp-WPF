# Startup, shutdown, and integration contract

**Date:** 2026-07-18 · **Task:** SP-003 (task-board row 2) · **Status:** ratified by implementation + tests in this slice; evidence in `spine-tasks/SP-003-startup-shutdown-contract/record.md`

This contract instantiates `architecture-proposal.md` §3/§6 (row-2 column) and the A-014 foundation rules for how the greenfield client starts, validates, and stops. It governs every later row that starts or stops code. It implements no product features: it proves the lifecycle shape with the placeholder window and one demonstrator background participant.

---

## 1. Startup phases

Startup is an ordered sequence of named phases executed by a single phase runner. Phases 1–3 run in `Program.Main` **before** `BuildAvaloniaApp()` starts — plain C#, no Avalonia runtime required, fully unit-testable. Phase 4 is the Avalonia lifetime itself.

| # | Phase | Runs where | Contents | Failure kind |
|---|-------|-----------|----------|--------------|
| 1 | `Bootstrap` | `Main`, pre-Avalonia | Install panic hooks (`AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`) and the minimal logger seam (§9); parse command line. | Fatal |
| 2 | `CompositionRoot` | `Main`, pre-Avalonia | Construct the composition root by explicit manual construction; run composition-root self-validation (§4). | Fatal |
| 3 | `CoreServices` | `Main`, pre-Avalonia | Explicitly start each registered background participant (§5) in registration order. | Fatal |
| 4 | `UserInterface` | Avalonia lifetime | `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`; `App.OnFrameworkInitializationCompleted` assigns the main window, which displays the phase-outcome trace (§10). | Fatal (panic rules, §6) |

In this bootstrap slice every phase is required, so any phase failure is **Fatal** and aborts startup. Feature rows may register optional phases whose failure kinds are Recoverable or Degraded (§2).

## 2. Typed initialization-failure taxonomy

Expected failures are **typed results, never exceptions-as-control-flow**. The phase runner returns a `StartupOutcome`:

- `Success` — phase completed; next phase runs.
- `Cancelled` — the startup cancellation token fired before or during the phase; later phases do not run (§3).
- `Failed(InitFailure)` — carries `Phase`, `Kind`, and `Reason`. `Kind` is one of:
  - `Fatal` — startup aborts; teardown of completed phases runs (§6). *Only kind exercised in this slice.*
  - `Recoverable` — phase's feature is skipped with a named reason; startup continues. **Reserved; first consumer is row 3 (async lifecycle/fault policy) or row 5 (capability contract).**
  - `Degraded` — phase completes with named reduced semantics recorded in the phase trace. **Reserved; same first consumers.**

An unexpected exception escaping a phase is trapped once at the phase-runner boundary and converted to `Failed(Fatal)` with the exception as the reason. Nothing else catches it; there is no swallow-and-continue.

## 3. Cancellation semantics

1. The `CancellationTokenSource` is owned by the host in `Program.Main`; tests inject their own token. No phase creates its own.
2. The token is passed to every phase; each phase must observe it before doing work and may pass it into the work it starts.
3. Cancellation requested before phase N begins means phases N..last **never run**.
4. Cancellation during phase N yields `Cancelled`; completed phases N−1..1 are torn down in reverse order per §6.
5. Cancellation is not an error: exit code 0, no failure entry in the trace.

## 4. Composition-root validation rules

1. The composition root is a plain object graph built by explicit constructor calls in one place (`CompositionRoot.Build`). **No DI container** (§7), **no static accessor** — nothing static hands out services.
2. `CompositionRoot.Validate()` runs as phase 2 and checks that every required registration is present and constructable. A missing registration fails fast with `Failed(Fatal, phase: CompositionRoot, reason: <what is missing>)` — before the window exists, never at first use.
3. Validation is an explicit checklist, not reflection over a container: each required participant is named in code, so a deleted registration is a compile error or a validation failure, not a null at runtime.
4. Constructors are cheap: **no background work is started from any constructor**. Construction (phase 2) and start (phase 3) are separate, testable steps. This is a tested rule (the demonstrator proves start happens only via the phase runner).

## 5. Background-participant ownership rule

1. Every background participant (anything with a start/stop lifecycle that outlives a single call) implements the participant contract: `StartAsync(CancellationToken)` / `StopAsync()` / a `Running` state.
2. Each participant has **exactly one owner**: the application host assembled by the composition root. The owner is the only caller of `StartAsync` (phase 3) and `StopAsync` (teardown, §6). Participants never start themselves, and no other object holds a second start/stop path.
3. `StartAsync` and `StopAsync` are **idempotent**: repeated calls are no-ops. Stop of a never-started participant is a no-op.
4. The demonstrator participant in this slice (a heartbeat no-op) exists only to prove rules 1–3 end-to-end. It is not a product feature.
5. Dispatcher discipline, cancellation generations, and out-of-order completion policy for participant *work* are row 3's contract; this contract owns only start/stop ownership and teardown ordering.

## 6. Teardown matrix

Teardown is a **single idempotent entry point** (`ApplicationHost.ShutdownAsync`), guarded (`Interlocked`) so concurrent or repeated invocation runs the body exactly once. It stops participants in reverse start order, then releases host resources. This matters because the three trigger paths reach teardown differently:

| Trigger | How teardown is reached | Guarantees |
|---------|------------------------|------------|
| **Window close** | `ShutdownMode = OnMainWindowClose` → `IClassicDesktopStyleApplicationLifetime.Exit` event → guarded teardown. | Reverse-order stop; process exits 0. |
| **Startup failure** (phase `Failed(Fatal)` or `Cancelled` in phases 1–3) | Phase runner's failure branch → guarded teardown of completed phases only; `StartWithClassicDesktopLifetime` is never called; the window never exists. | Participants started so far are stopped exactly once; exit code 0 for cancel, non-zero for failure; reason is logged via the §9 seam. |
| **Panic** (unhandled exception) | **The `Exit` event does NOT fire on this path** — an unhandled UI-thread exception unwinds `Main`. Teardown is reached from a `try/catch` around the lifetime call in `Main`, and the Bootstrap-phase hooks (`AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`) log last-ditch diagnostics. | Log via the §9 seam → best-effort guarded teardown → non-zero exit. No dialog, no nested message pump, no swallow-and-continue (WPF crash-storm lesson, record.md). |

Rules:

1. Teardown runs the body **exactly once** per process; every later invocation (any path, repeated close, double fault) is a no-op.
2. Participants stop in **reverse start order**.
3. Teardown never throws: an individual participant's stop failure is logged and teardown continues to the remaining participants.
4. The startup-failure path and the panic path both leave **no orphaned background participant**: anything phase 3 started is provably stopped.
5. Graceful-shutdown flush of persisted state (settings) is **ACTIVATED** (SP-005, 2026-07-19): the persistence store's bounded flush occupies this reserved slot at the head of the teardown sequence, before participant stop — semantics, panic-path policy, and the dirty-at-shutdown test are defined in [`persistence-migration-contract.md`](persistence-migration-contract.md) §11; evidence in `spine-tasks/SP-005-persistence-migration-contract/record.md`.

## 7. Container-admission decision: manual construction, no DI container

**Decision: explicit manual construction. No DI container is admitted.**

Reasons:

1. **A-014 YAGNI constraint.** This slice has two registrations (host, demonstrator participant). A container's value — lifetime management across many optional registrations — has no consumer. A-014 forbids a framework without a concrete consumer.
2. **First-attempt lesson: "startup order and hidden globals became architecture."** The first attempt's container grew broad fallbacks and hundreds of `App.Services.GetService` call sites; wiring bugs surfaced as runtime nulls, and "registered" was mistaken for "integrated" (`first-attempt-systemic-lessons.md`). Explicit construction makes every dependency a constructor parameter visible at compile time, and §4's checklist makes a missing registration a fast typed failure.
3. **Validation is simpler without a container.** A container validates by resolution at first use (too late) or by container-specific verify hooks (another API to learn and mis-configure). An explicit checklist over named registrations is ordinary code, unit-tested like everything else.
4. **The rejected alternative is not banned forever.** Revisit trigger: participant count and optional-dependency fan-out reach the point where manual wiring errors recur in review or a row's contract requires scoped/transient lifetimes. The admitting row must record the decision and reasons here.

## 8. Lifetime shape and the `TerminateProcess`-analogue disposition

- **Lifetime:** `StartWithClassicDesktopLifetime(args)` with `ShutdownMode = OnMainWindowClose` — a decided value, not an implementation detail. The manual `Start(AppMain, args)` + `app.Run(cts.Token)` path is **rejected for now**: no requirement in this slice needs main-loop control that the lifetime events cannot express, and phases 1–3 already run before the loop exists. Revisit trigger: row 4 (persistence) demonstrates a flush-before-disposal ordering that the `Exit` event cannot satisfy.
- **`TerminateProcess` analogue: not adopted.** WPF's `TerminateProcess(GetCurrentProcess(), 0)` worked around a DirectWriteForwarder CRT-teardown crash specific to WPF's text stack on a half-shut-down runtime (`App.xaml.cs:3332-3342`). No equivalent hazard is evidenced for Avalonia/.NET 10 with zero native dependencies; the client exits by returning from `Main` with a normal process exit code. Revisit trigger: the first native dependency admission (LibVLC, OpenCV, ONNX rows) — that row must test Release-mode exit for native teardown faults, per the first-attempt Release-native-crash lesson.
- **Crash sentinels / hang watchdog / splash triage (proposal §6 row-2 column):** deferred. The panic path needs only the §9 logger seam in this slice. A crash sentinel (clean-exit marker consumed at next startup) has no consumer until row 4's persistence and row 9's Release gates exist; a splash has no consumer until startup does visible work. Each lands with its consuming row.

## 9. Logger seam (panic-path minimum)

The smallest seam the panic path needs: one `Log(string message)`-shaped sink writing to stderr/debug output, installed in phase 1 and reachable from the panic hooks. It carries no levels, formatting, rolling files, or framework. **Logging-framework admission (Serilog or other) is explicitly deferred** — the first row with a real logging consumer (durable diagnostics, crash-log rotation) must record the admission decision and reasons. Nothing in this contract logs secrets or user content.

## 10. Integration proof (anti-unwired rule)

Per A-014 and the "unwired but verified is not shippable" lesson, this slice proves registered code is reachable from a user path:

1. **User-visible trace:** the placeholder window displays the phase-outcome trace produced by phases 1–3 (each phase, its outcome, and the demonstrator participant's running state). A running app shows that the composition root's products reached the screen.
2. **Composition-root walk test:** a unit test walks the **real** composition root (no mocks, no substitute builders) and asserts that every dependency `MainWindow` requires resolves from it — the same objects phase 4 hands to the window. Constructing the window itself is not unit-testable without `Avalonia.Headless.XUnit` (not admitted; row 7), so the window-construction half of the proof is the visible trace in (1).

## 11. Single-instance carve-out

**No single-instance mechanism is designed or implemented.** Owner question §5.3 (is single-instance a product requirement?) is unanswered, and WPF's mutex + ack handshake is Windows-only. This contract reserves exactly one named seam point: a pre-`UserInterface` hook in the phase ordering (between phases 3 and 4) where a future admitted mechanism can veto startup with a typed `Failed`/`Cancelled` outcome and receive the file-open arguments parsed in phase 1. The retained WPF *outcome* — a wedged or failed primary must not make every relaunch a silent no-op — is recorded for whichever row takes the requirement; the mechanism is not.

---

## Conformance checklist (tested in this slice)

- Phase order enforced; cancellation between phases stops later phases; failing phase yields typed `Failed(Fatal)` with no unhandled exception; composition-root validation catches a deliberately missing registration.
- No static service locator; no background work started from constructors (demonstrator asserts start only via phase 3).
- Each teardown path (window-close shape, startup-failure, panic) stops the demonstrator exactly once; repeated shutdown is a no-op; panic path logs and tears down without hanging.
- Real composition root resolves the window's dependencies (integration proof §10.2).
