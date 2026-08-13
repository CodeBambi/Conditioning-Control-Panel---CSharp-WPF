# SP-066 record — Vacuous-shape sweep, name-anchored skip pin, and the shape guard

Board row 49 part (1) + T-17 (bounded doc-only). Infrastructure-only: closes **no product capability** (port-workflow.md item 11). Zero changes under `client/src/**`.

## Step 1 — Detector, raw inventory, ledger + pin design

### The detector (committed, executable surface)

`client/tests/CcpClient.Tests/VacuousShapeDetector.cs` — one exact lexical surface shared by the inventory and the Step 4 guard (`VacuousShapeGuardTests`), so enumeration and guard cannot drift.

Surface, per `[Fact]`/`[Theory]` body in BOTH test projects (`client/tests/CcpClient.Tests/**`, `client/tests/CcpClient.HeadlessTests/**`):

1. **Sanitize**: comments, string/char literals blanked (newlines preserved); interpolated strings blanked whole (holes included — code inside an interpolation hole is invisible; documented limitation, no test here asserts inside one).
2. **Method resolution**: `[Fact]`/`[Theory]` attribute → next method declaration → brace-matched body (expression bodies supported). Unparseable attribute **throws** (fail-closed — never silently dropped from coverage). Duplicate ledger keys (path + class + method) **throw**.
3. **Shapes** (a method carrying >= 1 is a SITE):
   - `early-return` — a bare `return;` positioned before the first assertion token
   - `assertions-all-nested` — >= 1 assertions and every assertion nested inside a conditional/loop/using block (depth > 0 relative to the method body)
   - `no-assertion` — zero assertion tokens (`Assert.Skip*` are skips, not assertions)
   - `platform-predicate` — `OperatingSystem.Is*` / `RuntimeInformation.IsOSPlatform`
   - `env-predicate` — `Environment.GetEnvironmentVariable`
   - `fs-predicate` — `File.Exists(` / `Directory.Exists(`
   - `dynamic-skip` — `Assert.Skip` / `Assert.SkipWhen` / `Assert.SkipUnless`

Raw inventory produced by running THIS class through a temporary dump fact (`ZzVacuousShapeInventoryDump`, one filtered `dotnet test` run), captured to `evidence/inventory-raw.json`, harness deleted immediately after; removal proven by `git status --short` (only `VacuousShapeDetector.cs` + task files untracked/modified — the dump file was never committed and is gone).

### Raw inventory reconciliation (framing a — orchestrator magnitudes are a starting point, not input)

Population: detector regex sees 731 raw attribute tokens (≈ 725 crude grep + doc-comment mentions + the temporary dump fact itself); framing (a) counted 724 facts over 87 files. Match.

| Shape | Framing (a) | Detector | Delta explanation |
|---|---|---|---|
| bare `return;` in fact bodies | 7 | 5 (`early-return`) | Mine counts only returns BEFORE the first assertion (the silencing position); the crude scan also caught `return;` inside fake/helper classes its brace-matcher mis-attributed to fact bodies (e.g. `DtrhNativeEffectsTests.cs:524`, `SoundArbitrationTests.cs:583` live in nested fakes) |
| platform predicates | 12 | 12 | exact |
| env predicates | 3 | 3 | exact |
| filesystem-existence predicates | 48 | 44 | same order; crude scan counted files/occurrences slightly differently (predicate tokens in non-fact helper code attributed to the enclosing fact) |
| all-assertions-nested | 53 | 45 | same order; crude depth counting included nested-class/helper bodies |
| no assertion token | 10 | 3 | mine is fail-closed (unparseable body throws, never reads as no-assertion); the crude scan's looser brace-matching reads a partially-matched body as assertion-free |
| `Assert.Skip*` sites | 1 | 1 (`dynamic-skip`) | exact — `DataRootOverrideTests.cs:68` (the SP-062 pin) |

Total sites: **94** across 37 files (`evidence/inventory-raw.json`, every site, unfiltered) — AFTER the consult class-attribution fix (pre-fix: 93, with nested fakes misattributed as declaring classes). Per-shape counts: `early-return` 5, `assertions-all-nested` 45, `no-assertion` 3, `platform-predicate` 12, `env-predicate` 3, `fs-predicate` 45, `dynamic-skip` 1. Same order of magnitude as framing (a) in every class; the deltas are the crude scan's known looseness in both directions, stated above.

### Detector error directions (framing b) — concrete, from this codebase

- **FALSE POSITIVE** (assertions in a called helper read as absent): `AiOperationContractTests.VocabularyTypes_SerializeRoundTrip` (:401) and `CommandData_RoundTrips` (:456) — every check lives in the local static helper `RoundTrip<T>` (which asserts inside); the fact bodies contain zero `Assert.` tokens. Also `UpstreamPayloadInventoryTests.RealRepo_InventoryCoversEveryUpstreamPayloadTree` (:53) — all assertions live inside `RunGuard`. These are NOT vacuous; disposition names the helper.
- **FALSE NEGATIVE** (loop over a possibly-empty collection reads as asserting): `SecretStoreTests.WindowsDpapi_RoundTrip_AndFileNeverContainsPlaintext` (:18) — its file-content assertions sit inside `foreach (var file in Directory.EnumerateFiles(root))` (:37); an empty enumeration silences every one at runtime while the detector sees assertions present. Also `AiModerationCoverageTests.Harness.Inventory_EveryWiredSurface_...` (:67) loops over the `AiModerationSurfaces.All` registry — a registry emptied by a future edit turns the coverage test vacuous. **Runtime vacuity is not detected by this guard; the framing-(c) `Assert.NotEmpty` pin before such loops is the mitigation.**

### Ledger design

Committed at `client/tests/floor/vacuous-shape-ledger.json`:

```json
{
  "schema": 1,
  "purpose": "...shape guard... lexical... not runtime-vacuity proof...",
  "keying": "path::ClassName.MethodName — the line field is INFORMATIONAL ONLY; moving a test's line number never un-covers it",
  "entries": [
    { "key", "path", "line", "method", "shapes": [...], "verdict", "reason" }
  ]
}
```

Verdicts: `not-vacuous` (reason it cannot be silenced, incl. helper-hoisted naming the helper), `platform-skip-converted` (now `Assert.Skip*`, so it REPORTS), `fixed` (real assertion added; reason states what breaks it), `deleted` (reason states which behavior is consequently unverified), `residual` (named, with filing intent — never fake-cleared).

**Keying**: `path::OuterClass[.NestedClass].MethodName` — the enclosing-type chain is resolved by BRACE RANGE (the class whose `{...}` span contains the attribute), never by last textual match (a sibling nested fake that already closed would otherwise claim the fact — the pre-approach consult caught exactly this on the first dump). Line numbers are recorded but never matched. Each entry carries `expectDetected` (bool): the guard compares the DETECTED set against the ledger both directions and on the shape set:
- site detected but not in ledger → RED with `file:line` (the non-recurrence bite);
- `expectDetected: true` entry not detected, or shapes differ → RED (stale entries cannot rot; a shape change forces a ledger edit, which is the review friction);
- `expectDetected: false` entry detected → RED. Used by `deleted` (a deleted test that reappears unclassified is RED — resurrection guard) AND by `not-vacuous` sites mitigated per framing (c): adding `Assert.NotEmpty` before the loop puts an assertion at body depth 0, the `assertions-all-nested` shape disappears, and the site legitimately VANISHES from detection (pre-approach consult correction #2 — a blanket "must still be detected" rule would fail on every mitigated entry).

Counted loops (`for (var y = 0; y < Const; ...)`) take a reason, not a `NotEmpty` (pre-approach consult correction #2).

### floor.json schema change (framing e) + admission rule (framing f)

Per project: `{ "total": N, "allowedSkips": ["<fully-qualified test name>", ...] }`. The wrapper enforces: zero bad outcomes (unchanged), `passed + skipped == total` (result-list anchored, never `Counters` arithmetic — SP-065's finding), and every `NotExecuted` result's `testName` present in `allowedSkips`, failing with the **offending test name**. Semantics: **"may skip"**, not "must skip" — a listed test that runs and passes on a machine where its precondition holds is green. Strictly stronger than the count pin: a deleted test still reddens `total`; machine-portable (no platform-conditional pin).

Admission rule text (lives in `floor.json` itself): a test may be listed ONLY when its precondition is a property of the machine or OS that **cannot be satisfied by configuration during a contract run**, and the ledger names the machine class where it DOES execute. `allowedSkips` is NOT a quarantine list. Two named permanent bans: (1) the SP-057 pin `DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault` — its skip means a process-wide `CCP_DATA_ROOT` leak, the vacuous 896/1 green SP-062 closed; (2) the named flake `ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery` — it guards a privacy boundary (route classes only, never a filename or query); reproduce and fix at the source, never quarantine.

**testName matching** (pre-approach consult correction #3): xunit theory rows serialize with arguments (`Class.Method(x: 1)`), so matching is exact on the **pre-`(` portion** of `testName` (or the full string if it carries no parens); the rule is written into `floor.json` next to the admission rule. Both new wrapper verdicts and `total` drift will be demonstrated against **synthetic TRX fixtures** through the exported `verifyProjectResults` (SP-065 `demo-fail-closed.mjs` pattern) — never a real probe fact, which would perturb `total`.

### Pre-approach consult (solo)

- **Verdict (CORRECTION, three material issues):** (1) class attribution was buggy — last-textual-match assigns facts to sibling nested fakes (`Clock`, `FakeMountTable`, `CollectingLog`, `InventoryFormatException`); resolve the enclosing type by brace range and re-dump. **Applied** — `EnclosingClasses` walks brace-matched spans; inventory re-dumped (94 sites, correct owners). (2) framing-(c) `Assert.NotEmpty` mitigation erases the `assertions-all-nested` shape, so mitigated sites VANISH from detection — presence expectation must be per-entry (`expectDetected`), mitigated sites recorded as expected-absent like `deleted`; counted `for` loops take a reason, not a `NotEmpty`. **Applied** to the ledger/guard design above. (3) check real TRX `testName` before fixing match semantics — theory rows serialize with arguments; match exact on the pre-`(` portion, say so in the file; and demonstrate wrapper verdicts on synthetic fixtures, not a real probe. **Applied** to the wrapper design above.
- **Actual answering model:** not disclosed by the consult tool's return (SP-063/SP-065 precedent: unverifiable from inside the worker; the worker session runs `kimi-coding/k3` per PI_PROVIDER/PI_MODEL, the packet's routing intent was Opus 5 main / Fable 5 fallback, and no model banner surfaced). The reply arrived COMPLETE — no reasoning-only or mid-sentence truncation (the wave-17/21/22 class) — and the three corrections above are reproduced from the verdict text itself, nothing stitched from reasoning.

## Step 2 — name-anchored skip pin in the wrapper (landed BEFORE any conversion)

`floor.json` per project is now `{ "total": N, "allowedSkips": [...] }`, carrying the admission rule, the may-skip semantics, the testName pre-`(` match rule, and the unchanged `bumpRule` in-file. `check-floor.mjs` enforces: zero bad outcomes (unchanged), result-list count == `total` (drift in EITHER direction RED), every `NotExecuted` `testName` present in `allowedSkips` (RED **naming the test**). Anchored on the TRX result list; `Counters` arithmetic is only used for the pre-existing consistency cross-checks. Counts unchanged by the schema change (898/35/0), so no bump rode the commit.

### Fail-closed re-demonstration table (synthetic fixtures through the REAL exported `verifyProjectResults`/`discoverTestProjects`; harness `evidence/demo-pin-semantics.mjs`, full output `evidence/pin-semantics-table.txt`, exit 0, "ALL PIN-SEMANTICS CASES HELD")

| Mode | Induced condition | Observed |
|---|---|---|
| missing results dir | dir never created | RED "results directory missing" |
| no .trx | empty dir | RED "no .trx" |
| two .trx | a.trx + b.trx | RED "expected exactly 1" |
| garbage not XML | plain text file | RED "XML declaration" |
| truncated mid-write | body cut before `</ResultSummary>` | RED "truncated" |
| stale mtime + creation | both stamps 1h old | RED "stale results" |
| stale creation only (fresh mtime) | creation 1h old, mtime now | RED "stale results" |
| zero results | 0 UnitTestResult | RED "0 total results" |
| failed category | 1 Failed | RED "failed" |
| outcome not Completed | ResultSummary Failed | RED "did not finish cleanly" |
| result list vs Counters total | total=900 vs 898 entries | RED "inconsistent results" |
| result list Passed vs Counters passed | passed=896 vs 898 Passed | RED "inconsistent results" |
| exotic outcome | 1 Warning | RED "unexpected outcome" |
| Counters not self-closing | `></Counters>` | RED "not a self-closing tag" |
| result lacking testName (new parse check) | name attr dropped | RED "unparseable results" |
| **NEW (i)** non-allowlisted skip | 1 NotExecuted, pin `allowedSkips: []` | RED "unexpected skip: FakeSuite.FakeTests.ConditionalFact …" — the test is NAMED |
| **NEW (iii)** total drift +1 | 899 results vs pin 898 | RED "total drift: 899" |
| **NEW (iv)** total drift -1 | 897 results vs pin 898 | RED "total drift: 897" |
| positive control | 898 passed, 0 skipped | GREEN |
| **NEW (ii)** allowlisted skip | same fixture, name listed | GREEN (may-skip semantics) |
| **NEW (ii-b)** allowlisted theory-row skip | testName carries `(x: 1)` arguments, base name listed | GREEN via pre-`(` match |
| discovery: unpinned project in sln | synthetic sln + 3rd tests/ project | RED "NO floor pin" |
| discovery: pinned project absent | stale pin entry | RED "stale pin" |
| discovery: no test projects | sln without tests/ | RED "refuses to go blind" |

### End-to-end induction (framing j — scoped child-process environment, never a process-wide export)

`CCP_DATA_ROOT=<temp>` set on ONE bash invocation of the wrapper (`evidence/red-induced-skip.txt`, exit 1): the SP-057 pin skipped (`Skipped CcpClient.Tests.DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault`, suite 897/1) and the floor went **RED naming exactly that test**. Injection removal proven: the variable was set per-command (bash env prefix, child process only); parent shell `CCP_DATA_ROOT` is `<unset>` before AND after (echoed in the capture). The TRX path is named in the capture (`ccp-floor-1afzwm`).

The allowlisted-GREEN verdict is demonstrated **synthetically only** — deliberately: the only live skip on this machine is the SP-057 pin, which framing (f) permanently BANS from `allowedSkips`, so a real allowlisted-GREEN run cannot exist on this machine without violating the ban. The synthetic fixture exercises the same exported code path with the name listed → GREEN, and the theory-row variant proves the pre-`(` match rule.

Clean real run after the schema change: `evidence/green-schema-change.txt`, exit 0 — `FLOOR OK: CcpClient.Tests: 898/898 total, 0 skipped; CcpClient.HeadlessTests: 35/35 total, 0 skipped`.

Schema change + wrapper change committed as ONE commit (one semantic unit); no count moved, so no bump rode it.
