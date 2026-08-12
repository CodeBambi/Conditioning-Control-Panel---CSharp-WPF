# SP-062 record — Loud skip + real process-env isolation for the SP-057 pin

**Task:** spine-tasks/SP-062-pin-skip-env-isolation · **Review Level:** 2 · **Board row:** "SP-057 pin test can pass vacuously — make the skip LOUD and fix the fixture's process-env isolation" (P1)
**Engine-review presence (T-2):** recorded per `spine_review_step` call below.

---

## Step 0 discovery — wave-19 base was RED on arrival (land-defect repair, File-Scope amendment)

The first baseline full-suite run on the untouched wave-19 tree reported **891 passed / 1 failed** —
NOT the pin, NOT the row-49 cold-start site:

- **Failed test:** `UpstreamPayloadInventoryTests.RealRepo_InventoryCoversEveryUpstreamPayloadTree`
- **Message:** `tree 'tunnel' is 'served' but names no serving code path (evidence) — dispositions are honest, not aspirational`
- **Root cause (verified in git):** the wave-18 land commit `f3a1192b` flipped `tunnel` + `vendor`
  to `served` in `client/docs/upstream-payload-inventory.json` WITHOUT the `evidence` field that
  SP-056's guard requires for served trees (`UpstreamPayloadInventoryTests.cs:296-302`). SP-061's
  record (intended filing #3) prescribed the flip WITH evidence naming the serving code path; the
  reconciliation applied the disposition but not the field. The orchestrator's post-land
  merged-state verification did not catch it — the same detection-path gap class the row-49
  filing named.
- **Repair (commit `59fbcf1e`, documented File-Scope amendment per FR-WORK-06):** added the two
  `evidence` fields, content dictated verbatim by SP-061's landed record (glob + manifest copies;
  served by `Features/Chaos/ChaosTunnelLoopback.cs`; consumer `ChaosTunnelWindow.cs`; landed
  `e1a4df6e`). Filtered guard run after repair: **19/19 green**. `client/docs/upstream-payload-inventory.json`
  is NOT in this packet's File Scope and NOT in fileScopeMustNotChange; stopping the wave for a
  two-field omission whose correct content is dictated by the landed record would have burnt the
  slot. Named in STATUS.md Discoveries, this record, the commit body, and the intended filings (§Step 4).
- **Consequence for the 10-green measurement:** every green run below is on the REPAIRED base.
  The delivered wave-19 base was red; the repair predates all measurement. Stated per the honesty
  cell so no "10 consecutive greens" claim silently inherits a repaired base.

---

## Step 1 — reproduce, verify the API, enumerate, design, pre-approach consult

### Pinned versions (framing d — verified against binaries, not docs)

- `xunit.v3` **3.2.2** (`xunit.v3.assert` 3.2.2, `xunit.v3.extensibility.core` 3.2.2, MTP v1 runner
  assemblies 3.2.2) + `xunit.runner.visualstudio` **3.1.5** — from `CcpClient.Tests.csproj` and the
  local NuGet cache (only 3.2.2 present).
- **Runtime-skip API, binary-verified:** reflection over the PINNED
  `xunit.v3.assert/3.2.2/lib/net8.0/xunit.v3.assert.dll` (pwsh 7 probe) shows
  `Void Skip(System.String)`, `Void SkipUnless(Boolean, System.String)`,
  `Void SkipWhen(Boolean, System.String)` on `Xunit.Assert`, and `Xunit.Sdk.SkipException` as an
  exported type. Compile check: the probe harness + implementation build 0W/0E against these
  assemblies. Observed `skipped` reporting: the positive control (Step 2) — `Skipped: 1` in both
  the console summary and the TRX (`outcome="NotExecuted"` + skip message recorded below).

### The reproduction (both directions, observed counts — harness since deleted, source embedded below)

**Direction A — the vacuous pass is silent TODAY.** With `CCP_DATA_ROOT` set process-wide
(`C:\Temp\ccp-sp062-positive-control-sandbox`), the pin filtered run on the CURRENT code:
`Passed: 1, Skipped: 0, Total: 1` (`evidence/trx/sp062-dirA-prefix.trx`,
`evidence/runs/dirA-silent-pass-prefix.log`). The pin's first guard checkpoint hit the override
and `return`ed — a green that asserted nothing.

**Direction B — the leak really crosses collections on this runner.** Probe 1 (cross-collection
handshake): an observer fact in the DEFAULT collection latches `ActiveDataRootOverride() != null`
while a mutator fact in `ProcessEnvCollection` (DisableParallelization=true) sets and HOLDS
`CCP_DATA_ROOT`, both rendezvousing on static latches inside bounded `TestWait` windows.
Observed: **GREEN, 2/2 passed in 65 ms** (`evidence/trx/sp062-probe1-cross.trx`) — the handshake
completing at all proves the two collections ran concurrently, i.e.
**`DisableParallelization = true` does NOT serialize a collection against others under
xUnit.v3 3.2.2 + the VS/MTP runner path this suite uses.** (Corroborates the SP-061 stochastic
base repro, 1-in-14 on `20542b99`; this probe makes it deterministic.)

**The fix's premise, measured (not trusted from docs — the consult's hole-closure).** The
isolation fix relies on tests in ONE collection never overlapping — including across two
CLASSES in that collection (the pin's class vs the env-mutator class). Two probes:
- Probe 2 (two facts, ONE class, ProcessEnvCollection): **RED 2/2**, both full-window 20 s
  `TestWait` timeouts, `TIMING-VERDICT:CONDITION-NEVER-TRUE` ×2 (poll loops on schedule — not
  starvation). 40 s wall. (`evidence/trx/sp062-probe2-intra.trx`)
- Probe 1b (two facts, TWO classes, BOTH ProcessEnvCollection — the exact shape of the fix):
  **RED 2/2**, same signature. (`evidence/trx/sp062-probe1b-samecoll.trx`)

  A serialized pair can never complete the handshake; the bounded timeout RED is therefore the
  positive proof of sequentiality, and `CONDITION-NEVER-TRUE` (not `ENVIRONMENT-STARVED`)
  classifies it as a real property, not a slow machine.

Probe harness source: preserved in git history is NOT applicable (never committed by design —
the probes are deliberately-RED instruments); the full source is embedded in
`evidence/probes/Sp062LeakReproProbes.cs.snapshot` with this paragraph as its index. All waits
are `TestWait.Until` (timing-guard clean: zero deadline literals, zero `Task.Delay` outside the
helper).

### Enumeration of process-wide-state / DisableParallelization correctness dependencies (framing c)

Sweep of BOTH test projects (`client/tests/CcpClient.Tests`, `client/tests/CcpClient.HeadlessTests`),
grep-verified 2026-08-12 on this tree:

| Surface | Hits | Disposition |
|---|---|---|
| `DisableParallelization` | 1 — `ProcessEnvCollection` (DataRootOverrideTests.cs) | **REAL dependency** — the pin relied on it for serialization; falsified by probe 1. Fixed by this task. |
| `SetEnvironmentVariable` (mutation) | 1 class — `DataRootOverrideEnvTests` (`CCP_DATA_ROOT` ×3 facts, try/finally restore) | **REAL** — the leak source; fixed by co-location. |
| `Environment.GetEnvironmentVariable` in product code reachable from tests | `CompositionRoot` (CCP_DATA_ROOT ×2, per-call, NO cache — SP-057 consult A2), `SessionProbe` (WAYLAND_DISPLAY/DISPLAY, read-only), `SecretStores` (PATH, read-only), `Program` (CCP_MCP, harness-only) | Cleared — no test mutates these; read-only consumers. |
| Current directory (`SetCurrentDirectory`) | 0 | Cleared — none. |
| `AppContext.SetSwitch` | 0 (`AppContext.BaseDirectory` READS in 4 guard/fixture tests) | Cleared — reads are not process-wide mutation. |
| Culture mutation (`CurrentCulture =` / `CurrentUICulture =`) | 0 | Cleared — none. |
| Static mutable singletons | `LoopbackListenerRegistry.Live` (ConcurrentDictionary) | Cleared — concurrent-safe by construction; only consumed by the assembly-teardown leak assertion, which runs AFTER all collections (no mid-run race); never gates a single test's correctness on serialization. |
| Headless project (all surfaces above) | 0 hits on every row | Cleared — no process-wide mutation or serialization dependency. |
| Tests reading `ActiveDataRootOverride` outside DataRootOverrideTests.cs | 0 | Cleared — the positive control (process-wide override) perturbs ONLY the pin (skips) and composition-running tests (write into the sandbox root; every assertion is location-agnostic). |

**Surface honesty:** the sweep covered the five framing-(c) categories across both test projects
by token grep + per-hit adjudication. It did NOT attempt to enumerate OS-global state beyond
those categories (named pipes, mutexes, temp-dir collisions — temp consumers use
`Guid.NewGuid()` names, swept) and makes no claim there.

### Design (post-consult)

1. **Loud skip at both checkpoints** — `Assert.SkipWhen(ActiveDataRootOverride() is not null, reason)`
   (pinned-binary-verified API). Checkpoint 1 reason names `CCP_DATA_ROOT` + leak class
   "runner-set override (external process environment)"; checkpoint 2 names `CCP_DATA_ROOT` +
   "cross-collection process-env leak". Reasons name the VARIABLE and the CLASS only — never the
   override VALUE (a path may be user-sensitive; the consult's privacy note). The assertion when
   the pin binds is byte-identical to today (strictness unchanged, framing h).
2. **Isolation = co-location, not trust.** `DataRootOverrideTests` joins
   `[Collection(nameof(ProcessEnvCollection))]`: intra-collection sequentiality — probe-1b-proven
   cross-class on THIS runner — means the pin and the ONLY in-suite `CCP_DATA_ROOT` mutator can
   never overlap; mutators restore in `finally`, so the pin always binds in a normal run
   (892/0). The skip then fires ONLY for an externally-set override (the positive control) —
   live tripwire, dead-code-free.
3. **Keep `DisableParallelization = true`** on the collection as a non-relied-upon hint (harmless
   where honored, reduces scheduling pressure where honored; the fix never cites it).
4. **Rejected alternatives:**
   - *Assembly-level `[assembly: CollectionBehavior(DisableTestParallelization = true)]`* — serializes
     all 892 tests to protect ONE contested resource. Measured parallel suite duration 36 s
     (baseline TRX); serial would be the sum of all test durations (loopback/integration facts
     alone hold multi-second bounded windows). Disproportionate; rejected.
   - *Trust `DisableParallelization` (status quo)* — probe-1-falsified on this runner.
   - *Product-side seam (AsyncLocal/thread-local override, injected env accessor)* — product code
     is out of scope by default; the leak is a TEST-scheduling defect, not a product defect;
     adding product surface to fix test scheduling inverts the dependency. Rejected.
   - *Cross-collection shared lock / global fixture* — cannot compel tests OUTSIDE the lock's
     collection to take it; strictly more machinery than co-location for a weaker guarantee.
   - *Delete the guard, assert unconditionally* — reintroduces the false RED (framing a, banned).
   - *Reflection tripwire asserting the `[Collection]` attribute stays on the pin's class* (the
     consult's suggestion) — REJECTED as redundant: if a future refactor removes the attribute,
     the race returns and the LOUD SKIP fires (891/1), which the exact-count floor discipline
     already catches. The skip IS the tripwire for that regression class; a second one is
     machinery without new signal. (Also: a new fact would move the pinned 892 count.)
5. **Serialization cost:** the 8 pin-class facts + 3 env facts now serialize AMONG THEMSELVES
   (~11 facts, all sub-second except the real-composition boot fact which already ran
   effectively alone). Suite-level cost: unmeasurable (the suite is collection-parallel over
   ~80 collections; one collection gained 8 fast facts).

### Pre-approach consult (Step 1)

**Mode:** solo ×1. **Actual answering model:** NOT surfaced by the tool response (recorded
honestly, the standing provenance discipline).

**Verdict:** design sound; ONE evidence hole + three precision notes, ALL folded in above:
1. **HOLE (fixed):** probe 2 measured intra-CLASS sequentiality; the fix relies on CROSS-CLASS
   same-collection sequentiality → probe 1b added (two classes, one collection), RED ×2
   full-window timeouts — premise now measured in the fix's exact shape.
2. **Positive-control safety:** verify no other test depends on the override being unset —
   swept (enumeration table, zero hits).
3. **Land-defect repair was the right call** vs stopping; record it as an intended board filing
   (the reconciliation class that flips dispositions must carry the guard-required fields; the
   post-land verification gap is the same class that caught row 49).
4. **Skip reason must name the variable + leak class, never the override VALUE** (paths can be
   user-sensitive).

---

## Engine-review log (T-2)

| Step | Type | Result | Artifact |
|------|------|--------|----------|
| 1 | plan | **SKIPPED BY DESIGN** (nested reviewer spawn blocked in worker session; `skipped=true, spawnFailed=false` — the engine runs reviews after `.DONE`, SP-195) | `.reviews/1-20260812T205835.md` |
| 2 | plan | **SKIPPED BY DESIGN** (same SP-195 engine-owned shape) | `.reviews/2-20260812T210637.md` |
| 3 | plan | **SKIPPED BY DESIGN** (same SP-195 engine-owned shape) | `.reviews/3-20260812T212352.md` |
| 4 | plan | **SKIPPED BY DESIGN** (same SP-195 engine-owned shape) | `.reviews/4-20260812T214956.md` |
| 5 | plan | **SKIPPED BY DESIGN** (same SP-195 engine-owned shape) | `.reviews/5-20260812T215001.md` |

---

## Step 2 — implement: loud skip + co-location isolation (+ one discovered-race fix)

### The pin (`DataRootOverrideTests.cs` — fileScopeMustChange)

- Both silent `return`s replaced with `Assert.SkipWhen(ActiveDataRootOverride() is not null, reason)`.
  Checkpoint 1 reason: "runner-set override in the external process environment"; checkpoint 2:
  "cross-collection process-env leak, the SP-057 flake class". Both name `CCP_DATA_ROOT`; NEITHER
  carries the override value (privacy: a user path, the consult's note). The `Assert.Equal` when
  the pin binds is **byte-identical** to the pre-task code (strictness unchanged, framing h).
- **Co-location:** `DataRootOverrideTests` now carries `[Collection(nameof(ProcessEnvCollection))]`
  with a header comment recording WHY (probe-proven, never doc-trusted). The class's pure facts
  ride along harmlessly. `DisableParallelization = true` stays on the collection definition as a
  non-relied-upon hint; its summary now says so.
- Probe harness deleted from the tree (source preserved at `evidence/probes/Sp062LeakReproProbes.cs.snapshot`).

### Positive control (framing b) — the tripwire is live code

Command (Windows, git-bash):

```
export CCP_DATA_ROOT="C:\\Temp\\ccp-sp062-positive-control-sandbox"
dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo \
  --logger "trx;LogFileName=sp062-positive-control.trx" --results-directory <evidence>/trx
```

Observed output (`evidence/runs/positive-control.log`, `evidence/trx/sp062-positive-control.trx`):

```
Skipped CcpClient.Tests.DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault [1 ms]
Passed!  - Failed: 0, Passed: 891, Skipped: 1, Total: 892, Duration: 35 s
```

TRX: `outcome="NotExecuted"` ×1 with
`<Message>CCP_DATA_ROOT override is active at the guard checkpoint (leak class: runner-set override in the external process environment) — the pin only binds with the override unset at BOTH checkpoints</Message>`.
The suite exits 0 on a skip — by design the **exact-count floor** (892/0 expected) is what turns a
vacuous run loud, exactly the zero-new-machinery mechanism the board row asked for. Contrast with
the pre-fix Direction-A run: same induction, `Passed: 1, Skipped: 0` (silent).

### Discovered red root-caused and fixed (run-01 interruption, named per framing f's inverse)

The FIRST post-implementation full-suite run went **891 passed / 1 failed / 0 skipped** — the pin
held (0 skips: the co-location works), but `AiProviderLabIntegrationTests.Refusal_ThroughPipeline_TypedCarrier_ExactlyOneHit`
failed `Assert.Equal(1, h.Lab.HitsFor(AiLabMode.Refusal))` with Actual 0
(`evidence/trx/sp062-run01-unit.trx`). NOT the row-49 site; NOT attributable to it (framing f's
"no other red may hide behind this name").

**Root cause (proven by code read, `AiProviderLab.cs`):** every simple-mode branch did
`await Write(res, ...)` — which `Close()`s the response, making the reply observable to the client —
**before** `_records.Enqueue(...)`. `Handle` runs on a fire-and-forget thread-pool task
(`ServeLoop`'s `_ = Task.Run(() => Handle(ctx))`), so a fast client can consume the reply, unwind
the pipeline, and read `HitsFor` in the gap before the server task's enqueue executes. The
"server-side hit count, not client-side hope" discipline (SP-041) was violated by microseconds.
Same class in 9 sites: version endpoint, catch-all 404, Ok, Rate429, Error500, NotFound404,
Refusal, Malformed, Truncated.

**Fix (assertion-neutral harness ordering, in-scope `client/tests/**`):** the record is enqueued
BEFORE the response becomes observable for every outcome-INDEPENDENT mode — the hit exists the
moment the request is classified. The outcome-DEPENDENT modes (Timeout, HangStream, SlowOk) keep
their post-outcome records — their payload ("client-gone" vs "completed") only exists after the
outcome, and their consumers already synchronize via `WaitForRecordAsync` (deterministic signal).
Zero assertions changed, zero waits/literals touched, `TestTimingGuardTests` pins untouched
(verified in every green run). Filtered lab check post-fix: 12/12 green.

**Causality vs this task:** the race predates SP-062 (the ordering is SP-041-era); my change did
not touch the lab. The 10-green series below is post-fix and saw zero recurrence.

### Timing discipline (framing g)

No new deadline literals, no `Task.Delay`/`Thread.Sleep` outside `TestWait`, no new injected
timeout budgets handed to product code. The probe harness used only `TestWait.Until` (bounded
windows with the loud classifier). `TestTimingGuardTests` green with its allowlist UNCHANGED in
every run.

---

## Step 3 — ten consecutive greens, one genuinely cold

**Base honesty:** all runs on the repaired base (Step-0 land-defect repair `59fbcf1e`) + the
Step-2 tree (commit `b89b25f0`). The delivered wave-19 base was red (see Step 0); the repair
predates ALL measurement. Pre-series interruption: one 891/1 red (the lab race above),
root-caused and fixed BEFORE the series; the 10 below are consecutive post-fix.

| run | worktree | cold/warm | wall | unit | headless | TRX |
|---|---|---|---|---|---|---|
| green01 | in-place lane-1 | warm | 79s | **892/0** | 35/0 | sp062-green01-{unit,headless}.trx |
| green02 | in-place | warm | 72s | 892/0 | 35/0 | sp062-green02-* |
| green03 | in-place | warm | 71s | 892/0 | 35/0 | sp062-green03-* |
| green04 | in-place | warm | 72s | 892/0 | 35/0 | sp062-green04-* |
| green05 | **FRESH** `C:/Code/ccp-sp062-cold` (detached @b89b25f0, removed after) | **COLD — first-ever build**, 0W/0E (`runs/green05-cold-build.log`) | 106s | 892/0 | 35/0 | sp062-green05-cold-* |
| green06 | in-place | warm | 72s | 892/0 | 35/0 | sp062-green06-* |
| green07 | in-place | warm | 72s | 892/0 | 35/0 | sp062-green07-* |
| green08 | in-place | warm | 73s | 892/0 | 35/0 | sp062-green08-* |
| green09 | in-place | warm | 72s | 892/0 | 35/0 | sp062-green09-* |
| green10 | in-place | warm | 73s | 892/0 | 35/0 | sp062-green10-* |

Every run: `dotnet test` unit + headless, `-c Debug --nologo`, `--logger "trx"`, stdout/stderr
redirected to `evidence/runs/greenNN-*.log` (never tailed — failure names were read from the
TRX, SP-058 discipline; the one red's name came from `sp062-run01-unit.trx`).

**Row-49 site** (`LoopbackOllamaProviderTests.Truncated_PrefixCut_NeverSurfaced_TypedUnavailable`):
**ZERO firings in all 10 runs including the cold first-ever build.** Nothing to attribute; the
site is byte-untouched. Data point the row inherits: 0/1 cold, 0/10 overall this measurement.

**Skip count across the series: 0 in every run** — the normal-run skip path was unreachable,
exactly as designed (the tripwire fired ONLY in the deliberate positive control).

### Final-tree series (green11–green20, commit `89b7fdd5` — THE acceptance series)

The Step-4 consult's tripwire assertion changed test content, so the series re-ran on the
final tree. All runs: same command shape as above; row-49 site ZERO firings again.

| run | worktree | cold/warm | wall | unit | headless | TRX |
|---|---|---|---|---|---|---|
| green11 | in-place lane-1 | warm | 81s | 892/0 | 35/0 | sp062-green11-{unit,headless}.trx |
| green12 | in-place | warm | 72s | 892/0 | 35/0 | sp062-green12-* |
| green13 | in-place | warm | 72s | 892/0 | 35/0 | sp062-green13-* |
| green14 | in-place | warm | 72s | 892/0 | 35/0 | sp062-green14-* |
| green15 | **FRESH** `C:/Code/ccp-sp062-cold2` (detached @89b7fdd5, removed after) | **COLD — first-ever build**, 0W/0E (`runs/green15-cold-build.log`); NuGet cache warm (machine-global — coldness = JIT + first compile + first test-host, the row-49 mechanism) | 108s | 892/0 | 35/0 | sp062-green15-cold-* |
| green16 | in-place | warm | 73s | 892/0 | 35/0 | sp062-green16-* |
| green17 | in-place | warm | 72s | 892/0 | 35/0 | sp062-green17-* |
| green18 | in-place | warm | 71s | 892/0 | 35/0 | sp062-green18-* |
| green19 | in-place | warm | 72s | 892/0 | 35/0 | sp062-green19-* |
| green20 | in-place | warm | 72s | 892/0 | 35/0 | sp062-green20-* |

Row-49 cumulative data point: **0 firings / 2 cold first-ever builds, 0/20 total runs** across
both series. Site byte-untouched.

---

## Step 5 — verification (final tree `89b7fdd5`)

- `node .spine/patches/verify.mjs` — **exit 0** (`runs/contract-verify.log`: "OK — all patches applied on all roots").
- `dotnet build client/CcpClient.sln -c Debug --nologo` — **0W/0E** (`runs/contract-build.log`).
- `dotnet test client/tests/CcpClient.Tests` — **892 passed / 0 skipped / 0 failed**, TRX `evidence/trx/sp062-contract-unit.trx`.
- `dotnet test client/tests/CcpClient.HeadlessTests` — **35 passed / 0 skipped / 0 failed**, TRX `evidence/trx/sp062-contract-headless.trx`.
- `git diff --check` — clean.
- `git status --short` — File Scope paths only: `client/tests/CcpClient.Tests/**` (3 files: DataRootOverrideTests.cs, AiProviderLab.cs, DataRootChokePointGuardTests.cs), `spine-tasks/SP-062-pin-skip-env-isolation/**`, plus the declared File-Scope amendment `client/docs/upstream-payload-inventory.json` (Step-0 land-defect repair, commit `59fbcf1e`, named in STATUS/record/commit-body; NOT in fileScopeMustNotChange).

## Completion criteria ledger

| Criterion | Status |
|---|---|
| Pin can no longer pass vacuously; vacuous path reports a skip naming the variable; floor discipline catches it | ✅ `Assert.SkipWhen` ×2; positive control 891/1 with reason (TRX NotExecuted); contrast run pre-fix 1/0 silent |
| Process-env leak fixed by a mechanism PROVEN with a repro harness on this runner | ✅ co-location; probes 1/1b/2 (cross-collection GREEN 65ms = DisableParallelization dead; same-collection cross-class RED ×2 timeouts = sequentiality real) |
| Committed enumeration of DisableParallelization / process-wide-state dependencies with dispositions | ✅ Step-1 table (5 categories × 2 projects; 2 real — this fix; rest cleared with reasons) |
| Positive control 891 passed / 1 skipped — live code | ✅ `runs/positive-control.log` + `sp062-positive-control.trx`; checkpoint-2 path demonstrated via replica (`sp062-cp2-replica.trx`) |
| 10 consecutive greens at 892/0 + 35 incl. ≥1 fresh-checkout first-ever build, TRX committed | ✅ green11–green20 on the final tree; green15 cold first-ever build; all TRX force-committed under evidence/ |
| Zero assertions weakened; zero new deadline literals/budgets; row-49 site untouched | ✅ bound assertion byte-identical; guard allowlist unchanged (green in all 21 runs); LoopbackOllamaProviderTests.cs untouched (0 firings recorded) |


---

## Step 4 — record + pre-completion consult

### Honesty cell — what this task does NOT prove

- **Runner-generality:** the runner semantics were measured on THIS machine, xUnit.v3 3.2.2 +
  `xunit.runner.visualstudio` 3.1.5 via `dotnet test` (VSTest adapter over MTP v1). A different
  runner entry point (e.g. direct `dotnet run` on the test assembly / standalone MTP mode), a
  different xUnit version, or different parallelism settings (`-maxcpucount`, RunConfiguration)
  were NOT measured. The co-location fix relies on intra-collection sequentiality — xUnit's
  oldest guarantee, probe-proven here — but this record claims it only for the measured surface.
- **Sweep exhaustiveness:** the enumeration covered the five framing-(c) categories (env vars,
  current directory, static mutable singletons, AppContext switches, culture) across BOTH test
  projects by token grep + per-hit adjudication. It does not claim coverage of OS-global state
  beyond those categories (named mutexes/pipes, fixed ports — loopback servers bind ephemeral
  ports; temp consumers use Guid-named roots, swept) nor of the HEADLESS project's UI-thread
  dispatcher coupling beyond the token level.
- **The lab-race fix's completeness:** the 9 outcome-independent sites were reordered; a
  record-after-observable race in any FUTURE mode branch is possible if added in the old shape
  (the file's comment now names the invariant). No guard test was added for that (would move
  the pinned 892 count; the lesson is filed instead).
- **10 greens is a sample, not a proof:** it bounds the post-fix flake rate of the measured
  suite on this machine; it says nothing about hit rates below ~1/10 except for the named
  row-49 site's 0/1 cold.
- **Linux:** no WSL distribution on this machine (standing named gate). Everything here is
  Windows evidence; nothing Linux is claimed.

### Intended board filings (orchestrator reconciles at land — ENABLER 2; no row state set)

1. **This row ("SP-057 pin test can pass vacuously — make the skip LOUD and fix the fixture's
   process-env isolation"):** all four acceptance clauses met — (1) loud `Assert.SkipWhen` skip,
   vacuous run reports 891/1 and the floor discipline catches it; (2) isolation fixed by
   co-location, PROVEN by the probe-1b repro harness on this runner (never the docs' claim);
   (3) sweep committed (enumeration table, Step 1) with dispositions; (4) 10 consecutive greens
   incl. a fresh-checkout first-ever build. Named limits: the honesty cell above.
2. **LAND-DEFECT lesson (wave-18 reconciliation):** the SP-061 land flipped
   `upstream-payload-inventory.json` `tunnel`/`vendor` to `served` WITHOUT the guard-required
   `evidence` field — wave-19's base was RED on arrival (891/1,
   `UpstreamPayloadInventoryTests.RealRepo_InventoryCoversEveryUpstreamPayloadTree`). Repaired
   in-lane (`59fbcf1e`, content dictated by SP-061's own record). Lesson for
   `port-lessons.md`/reconciliation practice: a disposition flip must carry every field the
   guard keys on the disposition for, and the orchestrator's post-land merged-state verification
   should re-run the FULL suite (not only diff-touched guards) after reconciliation edits —
   same detection-path class as the row-49 filing.
3. **AI lab record-ordering race (fixed in passing, in-scope):** `AiProviderLab` recorded hits
   AFTER closing the response; a fast client could read `HitsFor` before its own record existed
   (observed 891/1, `Refusal_ThroughPipeline_TypedCarrier_ExactlyOneHit`, Expected 1 Actual 0).
   Root-caused + reordered (record precedes the observable for all outcome-independent modes).
   This is a FIFTH occurrence-adjacent data point for the timing/harness lesson series
   (record-vs-observable ordering, not a wait): candidate `port-lessons.md` entry — "a
   server-side count asserted by a client must be durable BEFORE the response that unblocks
   the client; ordering, not waiting."
4. **Row 38 (harness refuse-unsealed) / row 49 (timeout budgets) sequencing note:** rows 38/49
   were named this row's natural successors at authoring; this landing makes the suite's signal
   trustworthy for their 10-green acceptances (the pin can no longer pass vacuously; the floor
   count now means what it says). Row-49 hit-rate data inherited: 0/1 cold, 0/10 here.
5. **Port-lessons candidates:** (a) `DisableParallelization` on a CollectionDefinition does NOT
   serialize cross-collection traffic under xUnit.v3 3.2.2 + this runner (probe-proven; the
   SP-057 consult A1 premise is dead — intra-collection co-location is the serialization
   primitive that holds). (b) A deliberately-RED handshake probe (bounded-timeout deadlock) is
   a POSITIVE proof of runner sequentiality — measure the guarantee you rely on, in the shape
   you rely on it (cross-class, not just intra-class). (c) xUnit.v3 3.2.2 dynamic skip:
   `Assert.Skip/SkipWhen/SkipUnless` exist in the pinned assert assembly and report as
   TRX `NotExecuted` + console `Skipped: N` — verify by reflection + observed run, never docs.

### Pre-completion consult (Step 4)

**Mode:** solo ×1. **Actual answering model:** NOT surfaced by the tool response (recorded
honestly, the standing provenance discipline).

**Verdict — three sub-questions answered + four gaps named, ALL discharged below:**

1. **(a) The AiProviderLab fix belonged in this packet** — in File Scope, it red-ed the
   measurement, assertion-neutral, root-caused. **With one required check (DONE):** moving the
   record BEFORE the write creates a phantom-record possibility when a client abandons a
   simple-mode response mid-write (old shape dropped that record). Swept every `HitsFor`/
   `HitCount` assertion (`AiProviderLabIntegrationTests.cs:80,89,180,197,212,227,241,339`,
   `LoopbackOllamaProviderTests.cs:43,78,94,109,123,139,154,169,253`): every count assertion
   follows FULL reply consumption; no test abandons a simple-mode response mid-write (the
   abandon-window tests use the outcome-dependent Timeout/HangStream/SlowOk modes, unchanged).
   The phantom-record window has no observer — the reorder is behavior-neutral for the suite.
2. **(b) The cold cell was adequate** — detached fresh worktree, no bin/obj, first-ever build
   0W/0E at the same commit as the warm runs (identical content is REQUIRED, not a flaw).
   **Caveat recorded:** the NuGet cache was warm (machine-global); the coldness is
   JIT + first-ever-compile + first test-host start — exactly the row-49 mechanism
   (JIT + HttpListener warmup), so the cell measures what that row needs. Also recorded: the
   cold worktree ran build+test only (verify.mjs runs in-lane at the contract gate).
3. **(c) Tripwire/positive-control gaps closed:**
   - **Checkpoint-2 skip had never been OBSERVED firing** (the positive control exercises
     checkpoint 1 — the override is set before the test starts). CLOSED by demonstration: a
     scratch replica of the pin's exact two-checkpoint sequence with a handshake injected in
     the window (`evidence/probes/Sp062Checkpoint2Replica.cs.snapshot`, since deleted) + a
     mutator in a separate collection setting the override on demand. Observed:
     `Passed: 1, Skipped: 1` — the replica reported SKIPPED with the checkpoint-2 reason
     verbatim (`evidence/trx/sp062-cp2-replica.trx`: NotExecuted + "CCP_DATA_ROOT became
     non-null between the guard and the read (leak class: cross-collection process-env
     leak...)"). The REAL pin's checkpoint-2 firing remains unobserved by construction (the
     pin cannot be instrumented without changing what it measures) — stated, not implied.
   - **Skip does not fail the run** (`dotnet test` exits 0 on skips): the enforcement is the
     exact-count floor discipline (892/0 expected), not the exit code. Recorded so a future
     CI gate wired to exit codes alone knows it would be blind to a vacuous run. Corollary:
     a full-suite run under a sandboxed harness profile (CCP_DATA_ROOT set, the SP-057
     headed-evidence discipline) now reports 891/1 by DESIGN — loud and expected, not a
     defect.
   - **Collection-attribute tripwire upgraded from probabilistic to deterministic:** the
     consult's earlier suggestion, rejected in Step 1 as redundant with the skip, was
     re-adopted in its no-count-change form: ONE reflection assertion inside the EXISTING
     `DataRootChokePointGuardTests.NoDataRootSpecialFolderUseOutsideTheChokePoint` fact
     asserts `DataRootOverrideTests` carries `[Collection(nameof(ProcessEnvCollection))]`
     (`CollectionAttribute.Name` binary-verified on the pinned xunit.v3.core 3.2.2 assembly).
     A future refactor dropping the attribute now fails deterministically instead of waiting
     for the race to fire the skip. Suite count stays 892 (assertion inside an existing fact).
4. **Process items:** the Step-4/5 engine-review calls are logged below; the inventory-JSON
   amendment is declared in STATUS/commit/record (it is NOT in fileScopeMustNotChange — no
   hard-contract violation); the full contract testCommand + `git diff --check` +
   scope-limited `git status` are Step 5 on the final tree.

**Consequence of the test-content change (the tripwire assertion):** the Step-3 series
(green01–green10) measured commit `b89b25f0`. The final tree differs by that one assertion, so
the 10-green series was RE-RUN on the final tree (green11–green20 below, with green15 a SECOND
fresh-checkout first-ever build at the final commit). The first series stands as recorded
evidence for `b89b25f0`; the acceptance series is the final-tree one.

