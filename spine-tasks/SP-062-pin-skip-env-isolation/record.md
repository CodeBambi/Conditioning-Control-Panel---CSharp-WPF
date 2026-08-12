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
