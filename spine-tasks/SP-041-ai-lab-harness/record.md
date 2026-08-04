# SP-041 — T-15: c2 AI lab harness hardening (HttpListener lifecycle + leaked test hosts)

**Lane:** lane-2, batch 20260804T163642 | **Review Level:** 2 | **Scope:** harness-only (`client/tests/**`); zero product change; task-board/port-lessons NOT touched (enabler 2)

## 1. Archaeology + before-state (Step 1)

### Root cause (PROVEN, not hypothesized)

Two stacked harness-side classes, matching the wave-5 T-3 forensics exactly:

1. **Constructor ODE race (the failing-test mechanism).** `AiProviderLab`'s ctor binds a RANDOM
   port in 49152–65535 (the OS dynamic client range — collisions are systematic per the SP-023
   lesson). On collision `Start()` throws `HttpListenerException` — and per the SP-023 port-lesson
   (2026-07-21), **a failed `Start()` DISPOSES the instance**. The old retry loop caught the
   `HttpListenerException` and then called `Prefixes.Clear()` on the SAME (now disposed) instance,
   which throws `ObjectDisposedException` — and the catch clause only handles
   `HttpListenerException`, so the ODE **escapes the constructor into the test body**. This is the
   exact wave-5 exception: `System.ObjectDisposedException: Cannot access a disposed object.
   Object name: 'System.Net.HttpListener'` in
   `AiProviderLabIntegrationTests.Refusal_ThroughPipeline_TypedCarrier_ExactlyOneHit`.
2. **Zombie test hosts (the 1→2→3 progression).** Leaked `dotnet.exe` test-host processes from
   earlier runs held loopback ports; each additional zombie raises the collision probability for
   the NEXT run's random binds, hence progressive red on identical content. Zombie kill →
   immediate 516/516 green (wave-5 forensics; port-lessons 2026-08-04 entry).

**Deterministic before-state repro** (throwaway, never committed; transcript in
`evidence/before-state-repro.txt`, source `evidence/before-state-repro.cs.txt`): a "zombie"
HttpListener holds the lab's chosen port; replaying the lab's OLD constructor shape verbatim
(colliding bind → catch `HttpListenerException` → `Prefixes.Clear()`) produces:

```
attempt 0: HttpListenerException (collision) — listener.IsListening=False
RESULT: ObjectDisposedException at Prefixes.Clear(): Cannot access a disposed object.
Object name: 'System.Net.HttpListener'.
```

The full suite would not flake on demand on a clean box (zero `dotnet.exe` running at task
start — verified via `tasklist`), so the wave-5 forensics + this deterministic mechanism repro
stand as the before-state, per the packet's own allowance.

### Secondary teardown-class holes found while reading the lifecycle

- `ServeLoop` catches `OperationCanceledException` and `HttpListenerException` but NOT
  `ObjectDisposedException` — `GetContextAsync` on a torn-down listener faults `_loop`
  (unobserved-fault class; `_loop.Wait` in Dispose already swallows the AggregateException, but
  the classification was absent).
- `Handle` accesses `ctx.Request` / `ctx.Response` BEFORE its try block — an ODE there escapes
  into an unobserved `Task.Run` fault instead of the harness-teardown catch.

## 2. Design (consult-shaped)

1. **Fresh `HttpListener` instance per bind attempt** (the SP-023 rule, now actually honored);
   failed candidate `Close()`d; `Prefixes.Clear()` deleted (nothing to clear on a fresh
   instance). After 25 failed attempts: loud `InvalidOperationException` naming the last prefix
   and the zombie class ("check for leaked dotnet test hosts holding loopback ports").
2. **Teardown-class classification:** `ServeLoop` adds `catch (ObjectDisposedException) { break; }`;
   `Handle` moves `ctx.Request`/`ctx.Response` inside the try (`res` nullable, `res?.Abort()` in
   the catch) with an explicit `catch (ObjectDisposedException)` arm documented as
   harness-teardown, NEVER a product failure. No record written for a request the lab never saw —
   observed semantics identical in green runs.
3. **Teardown = cancel → `Close()` (aborts in-flight) → bounded loop wait.** Drain-or-abandon =
   abandon-by-abort: in-flight handlers' next write faults into their own catches. No in-flight
   task registry (YAGNI — records are per-test state; post-dispose enqueues are harmless).
4. **Leaked-listener self-check = static live-instance registry + xUnit v3 assembly fixture**
   (NOT a throw from the lab's own `Dispose` — see consult catch #1 below):
   - `AiProviderLab` registers `(port → prefix)` in a static `ConcurrentDictionary` on successful
     bind, removes it in `Dispose`.
   - `[assembly: AssemblyFixture(typeof(AiLabLeakSelfCheck))]` — the fixture's `Dispose` runs at
     assembly teardown (after ALL collections, so no parallel-lab race) and throws naming every
     leaked port/prefix if the registry is non-empty. Adds ZERO test cases — the 516/29 contract
     floor is untouched.
5. **Consumer files:** verified — every lab is already disposed (`using var lab` in all 14
   provider tests; `Harness.Dispose() → Lab.Dispose()` in all 12 integration tests). Zero
   consumer changes needed; justification recorded (§4).

## 3. Consults (solo route per the 2026-08-04 rewire)

### Pre-approach (Step 1) — verdict + dispositions

**Actual answering model:** not identifiable from tool output (self-report is non-evidence per
the SP-039/T-7 precedent); route pin = Opus 5 main. Substance applied:

1. **DO NOT throw the self-check from the lab's `Dispose`** (adopted, load-bearing): a `using`
   Dispose runs during exception unwinding — if the test body already threw, a Dispose throw
   MASKS the real failure, converting a genuine product red into a harness red (honesty framing
   (a) violation). The port-release-probe-in-Dispose idea was dropped entirely (it also detects
   the rare class — Close failing to release — not the dominant one).
2. **Primary self-check = static registry + assembly fixture** (adopted): assembly-fixture
   Dispose runs after all collections (non-racy under xUnit parallelism) and adds zero test
   cases — the 516 floor is load-bearing, any new `[Fact]` would break it.
3. **`Handle` try-block restructuring needs nullable `res` + `res?.Abort()`** (adopted —
   compile-trap catch).
4. **Final-attempt failure should throw a loud diagnostic naming the zombie class** (adopted).
5. **Fresh instances make `Prefixes.Clear()` dead code — delete it** (adopted).
6. **Optional: bind a `TcpListener` on port 0 to ask the OS for a free port instead of random
   selection.** NOT adopted: TOCTOU window remains, 25 fresh-instance retries already make
   collisions survivable, random-in-range keeps the zombie-collision path exercised (the
   self-check's raison d'être). ponytail: skipped; add if flakes recur with the fresh-instance
   fix in place.
7. **Assembly-fixture-Dispose failure semantics must be verified empirically** (xUnit-version
   dependent) — Step 2/3 does this with the throwaway leak injection before relying on it.

## 4. Per-change justifications (honesty framing (a))

- **Zero assertion changes.** Every change is harness-side lifecycle/classification. The lab's
  OBSERVED semantics (hit counts, `completed`/`client-gone`/`released-after-disconnect` records,
  Retry-After gaps, SlowOk arrival, per-mode bodies) are untouched.
- **Consumer files: no change** — disposal already guaranteed by `using`/`Harness.Dispose`;
  there is no host-exit gap to close in the consumers. (File Scope lists them as permission,
  not obligation.)

## 5. Engine review presence (T-2)

| Step | Call | Result |
|------|------|--------|
| 1 | `spine_review_step --step 1 --type plan` | (recorded at call time) |

## 6. Stability proof (Step 3) — transcripts in `evidence/`

(to fill)

## 7. Completion-criteria disposition

(to fill)
