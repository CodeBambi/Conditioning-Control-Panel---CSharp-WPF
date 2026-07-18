# SP-003 record — startup, shutdown, and integration contract

**Task:** task-board row 2 (P0, Phase 1). **Date:** 2026-07-18. **Worker:** kimi-coding/k3 via pi-spine batch.

---

## Consult verdicts

### Pre-approach solo consult (Fable 5) — 2026-07-18

Verdict text (persisted before checkbox, per packet write-then-check rule):

> **PLAN — outline is sound; four concrete adjustments before you write the contract.**
>
> The phase list, taxonomy, ownership rule, container decision, and single-instance carve-out all match the packet and A-014. The gaps are in how the design meets Avalonia's actual lifetime and your test constraints:
>
> **1. Panic path: the Exit event will NOT fire — design teardown around that.** Avalonia has no `DispatcherUnhandledException` equivalent; an unhandled UI-thread exception propagates out of `StartWithClassicDesktopLifetime` and unwinds `Main`, and `IControlledApplicationLifetime.Exit` never fires on that path. So your teardown must be one idempotent entry point (guarded, e.g. `Interlocked`) invoked from **three** call sites: the lifetime `Exit` handler (window-close), the phase runner's failure branch (startup-failure), and a `try/catch` around the lifetime call in `Main` plus `AppDomain.UnhandledException`/`TaskScheduler.UnobservedTaskException` hooks (panic). State this explicitly in the teardown matrix — "Exit event → teardown" alone is wrong for the panic row, and your exactly-once tests depend on the single guarded entry point.
>
> **2. Structure phases so they're testable without Avalonia running.** `Avalonia.Headless.XUnit` is **not admitted** (deferred to row 7 per proposal §2), so unit tests cannot construct a real `Window` or spin the lifetime. Concretely:
> - Run phases 1–3 (Bootstrap, CompositionRoot, CoreServices) in `Main` **before** `BuildAvaloniaApp()` starts — plain C#, fully unit-testable with the real phase runner and real composition root.
> - Phase 4 (UserInterface) is the Avalonia start + window assignment in `OnFrameworkInitializationCompleted`, consuming the phase-outcome state built earlier.
> - For the integration proof, split "window's dependencies resolve from the real composition root" (testable — resolve the dependency set the window constructor needs, assert non-null/validated, no mock) from "window instantiates" (not unit-testable without headless; the user-visible phase trace in the running app covers that half). If you write a test that `new MainWindow(...)` directly, it will throw without an initialized `Application` — don't build Step 3 on that assumption.
>
> **3. Record the `TerminateProcess`-analogue disposition in the contract — it's assigned to row 2.** Proposal §6 explicitly routes "shutdown ordering incl. `TerminateProcess` analogue" to this task. Your outline digests the WPF evidence but doesn't decide. Write the disposition: WPF's `TerminateProcess` existed for a DirectWriteForwarder CRT-teardown crash specific to WPF's text stack; no equivalent hazard is evidenced for Avalonia/.NET 10 with zero native deps — standard return from `Main` with a process exit code; revisit trigger = first native dependency admission (LibVLC/ONNX rows). Same for `ShutdownMode`: state `OnMainWindowClose` as the decided value, not an implementation detail.
>
> **4. Minor contract-doc completeness points:**
> - Cancellation semantics: name who owns the CTS (the host in `Main`; tests inject a token) and state that cancellation after phase N tears down phases N..1 in reverse — you said this, make it a numbered rule so a test can cite it.
> - The taxonomy defines Recoverable/Degraded but this slice only exercises Fatal — say so in the contract ("reserved; first consumer is row 3/row 5") so a reviewer doesn't flag dead enum members as speculation.
> - Logger seam: define it as the smallest interface the panic path needs (e.g., one `Log(string)`-shaped seam writing to stderr/debug), and state explicitly that framework admission (Serilog) is deferred — the packet's Do-NOT list requires this wording.
>
> With those four folded in, proceed: write record.md consult verdict first (write-then-check per the packet), then the WPF digest, then the contract doc.

**Disposition:** all four adjustments accepted and folded into the contract (§teardown matrix single guarded entry point; phases 1–3 run pre-Avalonia in `Main`; `TerminateProcess`-analogue disposition recorded; numbered cancellation rule + reserved-taxonomy wording + minimal logger seam).

### Pre-completion solo consult (Fable 5) — 2026-07-18

Verdict text (persisted before checkbox, per write-then-check):

> **PLAN — the work is sound as far as I can verify it, but note the limits of that verification and the remaining close-out steps before `.DONE`.**
>
> **Verification caveat first:** the transcript shows test names, a green build (0 warnings), 23/23 passing, and record.md/contract excerpts — but the actual implementation diffs (Program.cs, ApplicationHost.cs, App.axaml.cs, MainWindow) were never printed in this conversation. My verdict on claims (1)–(4) rests on the test inventory and your own record, not direct code inspection. Three cheap self-checks close that gap:
>
> 1. **Locator/constructor-work ban:** `grep -rn "public static" client/src/CcpClient.Desktop/` — anything resembling `App.Services` or a static registry is a blocker. Should return only `Main`/`BuildAvaloniaApp`.
> 2. **Panic hooks actually wired:** `grep -n "UnhandledException\|UnobservedTaskException\|catch" client/src/CcpClient.Desktop/Program.cs` — the three-call-site design is load-bearing; confirm `Main` really has the try/catch + both hooks, not just the tests simulating them.
> 3. **No package creep:** `git diff bd10ae88 HEAD -- client/src/CcpClient.Desktop/CcpClient.Desktop.csproj` — must show no `PackageReference` beyond the SP-002 baseline.
>
> **One evidence gap worth naming, not fixing with code:** the integration proof has two halves — the composition-root test (done) and the *user-visible* phase trace in the window. No test can assert the latter without headless Avalonia (correctly not admitted). A 30-second headed smoke — launch the exe, confirm the trace renders, close, confirm clean exit code — turns that claim from "believed" to "observed." If you skip it, record.md must name it as the unproven manual gate per the constitution's honesty rule. Prefer running it.
>
> **Then the mechanical close-out, in order:** persist this verdict before checking the consult checkbox (write-then-check); board row 2 → `WIP` citing record.md; STATUS.md Steps 4/5 to reality; re-run full contract testCommand + `git diff --check` + `git status --short` (File Scope only, no stray bin/obj); commit Step 4/5; create `.DONE`.
>
> Nothing visible contradicts the packet's completion criteria. The container decision (§7) is well-reasoned and tied to A-014; the single-instance carve-out and `TerminateProcess` disposition match the amendments. Only a failed grep (static locator, missing panic hooks, or a new package) blocks `.DONE`.

**Disposition:** all three self-checks run (results in Test output section below); headed smoke run on Windows (trace observed, clean exit 0); close-out steps executed in the stated order.

---

## Contract summary

Deliverable: `client/docs/startup-shutdown-contract.md`. Four named phases (Bootstrap → CompositionRoot → CoreServices → UserInterface) run in order with a host-owned `CancellationToken`; cancellation or failure after phase N tears down N..1 in reverse (numbered rule). Typed failure taxonomy: Recoverable/Degraded (reserved — first consumers rows 3/5) and Fatal. Composition root is manual construction over a named-registration checklist, validated fail-fast before the window appears. Every background participant has exactly one owner — the runner's registry — and constructors never start work (tested rule). One guarded idempotent teardown entry point serves window-close, startup-failure, and panic. Single-instance: seam point reserved, no mechanism (owner question §5.3). `TerminateProcess` analogue: rejected — standard `Main` return with exit code; revisit at first native-dependency admission. `ShutdownMode = OnMainWindowClose`.

---

## WPF startup/shutdown evidence digest (outcomes only, no mechanics transplant)

Source: `ConditioningControlPanel/App.xaml.cs` via `client/docs/row-1-research-inputs.md` §3.1 (archaeology performed 2026-07-18, line citations therein). Read-only behavioral evidence per `wpf-parity`.

**Startup outcomes (VERIFIED):**
- Initialization is **strictly ordered** and order is load-bearing: secret-store seams are wired BEFORE settings load (`App.xaml.cs:1284-1291` — a no-op stub ordering bug "silently broke token/API-key persistence"); one-shot migration runs and is saved immediately after settings construction (`1296-1307`); background asset migration must follow settings (`1309-1314`).
- **User-visible startup progress** exists (splash with progress 0.3→0.95 on its own thread) and the main window is created only after service wiring completes (`1676-1678`).
- **Crash surfacing is tiered:** dispatcher exceptions are logged with full detail to a crash log; known-recoverable native quota failures are dropped deliberately; render-thread failure/OOM triggers immediate process exit WITHOUT a dialog because a nested message pump would cascade-crash (2026-05-25 crash storm, 10,251 reports) (`1185-1243`).
- **Crash sentinels** distinguish a clean shutdown from a crash across runs (`1166-1167`, cleared at `3184-3185`).

**Shutdown outcomes (VERIFIED):**
- Teardown is **ordered**: unhook system events → clear crash sentinels (clean exit is not a crash) → close interactive hosts → **`Settings.SaveImmediate()` FIRST, before cloud sync and service disposal** (`3208`) → trigger sources disposed first "so no new effects get queued" (`3223-3226`) → ~60 services disposed **in reverse initialization order** with named ordering constraints (`3228-3287`) → secrets cleared → log flushed → OS primitives released → `base.OnExit`.
- WPF deliberately calls **`TerminateProcess` instead of `Environment.Exit`** because DirectWriteForwarder CRT-teardown throws during `AppDomain.ProcessExit` on a half-shut-down runtime ("crash on close" WER dumps) (`3332-3342`). This hazard is WPF-text-stack-specific; no equivalent is evidenced for Avalonia/.NET 10 (disposition in contract §8).
- **Startup-failure path:** the single-instance protocol shows the "wedged primary holds the mutex forever" failure class; the ack handshake exists because signal-then-exit made every relaunch a silent no-op (`App.xaml.cs:45-52, 1029-1044`). Mechanism is Windows-only and carved out of this contract (owner question §5.3); the *outcome* retained is "a failed or wedged startup must not silently strand the user."

**Greenfield translation (outcomes kept, mechanics dropped):**
- Ordered phases with explicit dependencies; validation before the window appears.
- Settings-flush-before-disposal ordering belongs to row 4; this contract reserves the ordering guarantee in the teardown sequence.
- Tiered crash handling: typed failures for expected cases; panic path logs then performs controlled teardown; no swallow-and-continue, no nested-pump dialogs on the panic path.
- Reverse-order, idempotent teardown with exactly one owner per participant.

---

## Engine-review presence/absence note

`spine_review_step` was called after each step (1, 2, 3) and returned **skipped=true, reviewLevel=0, spawnFailed=false** every time — the batch engine ran no reviewer for this batch either. That makes three consecutive batches (SP-001, SP-002, SP-003) with zero engine review events; the review pipeline remains empirically unproven. Worker-side quality gates substituted: two mandatory solo Fable consults (pre-approach, pre-completion) with verdict text persisted in this record, plus the contract testCommand. Orchestrator verifies the journal at land.

---

## Container decision + reasons

**Manual construction, no DI container** (full text: `client/docs/startup-shutdown-contract.md` §7). Two registrations have no consumer for container lifetime management (A-014); the first attempt's container produced hidden-globals-as-architecture and runtime-null wiring bugs; an explicit named-registration checklist is ordinary unit-testable code. Revisit trigger: recurring manual-wiring errors or a row requiring scoped/transient lifetimes. No package beyond the SP-002 baseline was admitted; the panic path uses a minimal `ILogSink` seam with a stderr default — Serilog/framework admission explicitly deferred per the packet Do-NOT list.

## Test output

`dotnet build client/CcpClient.sln -c Debug --nologo` — succeeded, **0 warnings, 0 errors**.
`dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo` — **Passed: 23, Failed: 0, Skipped: 0** (Windows, .NET 10).

Coverage of the contract's tested rules: phase-order enforcement; inter-phase cancellation stops later phases; typed failure (no unhandled exception) from a failing phase; missing-registration validation fail-fast; reverse-order exactly-once teardown; repeated/concurrent shutdown no-op; startup-failure path stops only started participants; panic path logs and tears down without hanging; participant stop-throw is logged and teardown continues; stop-of-never-started is a no-op; real composition root resolves every MainWindow dependency through the real phase runner.

## Headed smoke (Windows, 2026-07-18)

Ran `CcpClient.Desktop.exe` (Debug build) headed. Window "CCP Client" rendered the user-visible phase trace (observed via UIA accessibility tree, not just believed): `Bootstrap: ok / CompositionRoot: ok / CoreServices: ok / Heartbeat: running`. Graceful window close (`CloseMainWindow`) → process exited within 10s with **exit code 0**. Both halves of the integration proof are therefore observed: real-composition-root resolution (test) and user-visible trace (headed smoke). stderr was empty on the happy path, as designed — `ILogSink` fires on failures/panic only.

Pre-completion consult self-checks (Fable-requested): (1) `public static` scan — only stateless factories, singleton result instances, `Program.Main`/`BuildAvaloniaApp`/`CreateStartupPhases`; no `App.Services`-style mutable registry. (2) `Program.cs` has the `try/catch` around the lifetime call plus both `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` hooks. (3) csproj diff vs SP-002 baseline adds only a `NoWarn` for AVLN3001 (documented: composition-root construction, never runtime XAML loader) — zero new `PackageReference`s.

## Surprises

- The runner must own **start** as well as stop: letting `Program.cs` start a participant directly broke the startup-failure teardown guarantee (started but not yet handed to the runner = orphan). The runner's registry is the single owner set; contract §5 states this explicitly.
- Panic path confirmed per the pre-approach consult: Avalonia's `Exit` event does not fire for an unhandled UI-thread exception, so one guarded idempotent teardown entry point is invoked from three call sites (lifetime `Exit`, phase-runner failure branch, `Main` catch + unhandled-exception hooks). The exactly-once tests depend on that single entry point.
