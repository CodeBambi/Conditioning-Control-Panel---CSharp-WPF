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

Population: the committed (final) detector sees 729 raw attribute tokens (≈ 725 crude grep + doc-comment mentions + the temporary dump fact itself); framing (a) counted 724 facts over 87 files. Match.

**Detector lineage honesty (pre-completion consult correction):** the detector was refined TWICE during this packet, and the numbers below are from the FINAL committed surface, re-derived from the committed `evidence/inventory-raw.json` — earlier prose (94 sites, nested 45) described intermediate surfaces and has been rewritten so no artifact disagrees with another. Refinement 1 (pre-approach consult): enclosing-class attribution by brace range, not last textual match (sibling nested fakes no longer claim facts). Refinement 2 (Step 3, ridden on that commit and stated in its message): GUARDING-brace depth — try/finally/catch/using/lock braces are transparent (an assertion inside `try {}` with cleanup in `finally` cannot be silenced); only if/else/foreach/for/while/switch/lambda braces count. That refinement moved `assertions-all-nested` 45 → 22 — the 23 delta were try/using-wrapped assertions misclassified by the cruder depth rule (e.g. `DtrhLoopbackContractTests.Inbox_SeqAckAndJsonShape` asserts inside `using (var doc = ...)`; never silenced), and it is the refinement, not a miscount.

| Shape | Framing (a) | Final detector | Delta explanation |
|---|---|---|---|
| bare `return;` in fact bodies | 7 | 5 (`early-return`) | Mine counts only returns BEFORE the first assertion (the silencing position); the crude scan also caught `return;` inside fake/helper classes its brace-matcher mis-attributed to fact bodies (e.g. `DtrhNativeEffectsTests.cs:524`, `SoundArbitrationTests.cs:583` live in nested fakes) |
| platform predicates | 12 | 12 | exact |
| env predicates | 3 | 3 | exact |
| filesystem-existence predicates | 48 | 45 | same order; crude scan counted predicate tokens in non-fact helper code attributed to the enclosing fact |
| all-assertions-nested | 53 | 22 | the guarding-brace refinement (above) removed try/using-false-positives; the crude scan's depth counting also included nested-class/helper bodies |
| no assertion token | 10 | 3 | mine is fail-closed (unparseable body throws, never reads as no-assertion); the crude scan's looser brace-matching reads a partially-matched body as assertion-free |
| `Assert.Skip*` sites | 1 | 1 (`dynamic-skip`) | exact — `DataRootOverrideTests.cs:68` (the SP-062 pin) |

Total sites: **79** in `evidence/inventory-raw.json` (every site, unfiltered) = **78 real** + the temporary dump harness itself (excluded from the ledger, deleted after capture). Same order of magnitude as framing (a) in every class; the deltas are the crude scan's known looseness in both directions plus the two stated refinements.

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

- **Verdict (CORRECTION, three material issues):** (1) class attribution was buggy — last-textual-match assigns facts to sibling nested fakes (`Clock`, `FakeMountTable`, `CollectingLog`, `InventoryFormatException`); resolve the enclosing type by brace range and re-dump. **Applied** — `EnclosingClasses` walks brace-matched spans (plus a bodyless-`class X;` guard for CollectionDefinition declarations); inventory re-dumped. (2) framing-(c) `Assert.NotEmpty` mitigation erases the `assertions-all-nested` shape, so mitigated sites VANISH from detection — presence expectation must be per-entry (`expectDetected`), mitigated sites recorded as expected-absent like `deleted`; counted `for` loops take a reason, not a `NotEmpty`. **Applied** to the ledger/guard design above. (3) check real TRX `testName` before fixing match semantics — theory rows serialize with arguments; match exact on the pre-`(` portion, say so in the file; and demonstrate wrapper verdicts on synthetic fixtures, not a real probe. **Applied** to the wrapper design above.
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

## Step 3 — disposition of every site

Raw inventory: 79 sites incl. the temporary dump harness itself (`ZzVacuousShapeInventoryDump` — excluded from the ledger; it existed only to run the detector and was deleted after each capture). **78 real sites, every one verdicted.** Ledger: `client/tests/floor/vacuous-shape-ledger.json` (80 entries after Step 4 added the two guard facts' own entries; keyed by `path::OuterClass[.Nested].Method`, line informational only).

Verdict totals: **not-vacuous 67, platform-skip-converted 5, fixed 6, deleted 0, residual 0** (plus the 2 guard-self entries = not-vacuous). Zero tests deleted — no behavior left unverified by deletion. Zero residuals — no site needed work beyond this packet.

### platform-skip-converted (5) — the early-return silencers now REPORT

| Site | Conversion | Skip fires on | Executes on (machine class) |
|---|---|---|---|
| `ChaosTunnelCapabilityTests.Windows_DelegatesToTheSameEngineLoadAsDtrh` | `Assert.SkipUnless(IsWindows)` | Linux contract runs | Windows |
| `ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps` | `Assert.SkipWhen(IsWindows)` | **this machine** | Linux (WSL gate / Linux CI) |
| `SecretStoreTests.WindowsDpapi_RoundTrip_AndFileNeverContainsPlaintext` | `Assert.SkipUnless(IsWindows)` (+ framing-c NotEmpty, see fixed) | Linux contract runs | Windows |
| `SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked` | `Assert.SkipUnless(IsLinux)` | **this machine** | Linux (WSL2 evidence box / Linux CI) |
| `SecretStoreTests.SettingsDocument_CarriesSecretNames_NeverValues` | `Assert.SkipUnless(IsWindows)` | Linux contract runs | Windows |

All five names are in `floor.json` `allowedSkips` under the admission rule (OS property, cannot be satisfied by configuration during a contract run), each with its machine class named in `allowedSkipsMachineClasses` in the same file. The SP-057 pin and the named privacy flake are NOT listed and never will be. The Step 2 schema change landed BEFORE these conversions (framing d) — the first conversion commit went green through the new pin, not through a widened count.

### fixed (6) — runtime-vacuous-capable loops given a failing-when-broken pin (framing c)

| Site | Pin added | What breaks it |
|---|---|---|
| `DtrhBridgeDiffTests.Derivative_RetainsEveryOriginalLine_...` | `Assert.NotEmpty(removed)` + `Assert.NotEmpty(kept)` | upstream diff drifts so nothing is removed/kept — the "except the named transport lines" half was over a runtime LINQ diff of real files |
| `SecretStoreTests.WindowsDpapi_RoundTrip_...` | `Assert.NotEmpty(Directory.EnumerateFiles(root))` before the plaintext scan | the store writes no file — filesystem enumeration over a possibly-empty root |
| `AiModerationCoverageTests.Inventory_EveryWiredSurface_...` | `Assert.NotEmpty(AiModerationSurfaces.All)` | a future edit emptying the surface registry |
| `AvatarPackTests.Definitions_AreNonUniform_...` | `Assert.NotEmpty(SyntheticAvatarPacks.All)` | an emptied pack registry |
| `HarnessEntryPointGateTests.HarnessEntries_AreSingledOut_...` | `Assert.NotEmpty(HarnessEntryPoints.All)` | an emptied flag registry (the count pin covers the Harness subset; this covers All) |
| `AiAwarenessCooldownTests.EmptyRegistry_AdmitsAllFourClasses` | `Assert.NotEmpty(Enum.GetValues<AiCooldownKind>())` | an emptied cooldown-kind enum |
| `CapabilityTests.StartupCancelled_LeavesRemainingProbesHonestlyNotProbed` | `Assert.NotEmpty(registry.Names)` | (belt — source is in-test registrations, but the loop is over the runtime registry) |

(6 substantive + the literal-source belts below — ledger verdicts assign `fixed` where runtime vacuity was reachable, `not-vacuous` + belt where the source is an in-test literal.)

### not-vacuous belts (framing c applied to in-test literal sources)

`AiAwarenessTests.Packaging_BlockingPolicyOnAnyField` (`Assert.NotEmpty(cases)`), `AiModerationBoundaryTests.Verdicts_SerializationRoundTrip_WithSurface` (`Assert.NotEmpty(samples)`), `AiModerationCoverageTests.Boundary_PureLocalEvaluation_NoNetworkSurface` (hoisted `boundaryTypes` + `Assert.NotEmpty`), `AiOperationContractTests.VocabularyTypes_SerializeRoundTrip` (`Assert.NotEmpty(VerdictSamples())`) and `CommandData_RoundTrips` (`Assert.NotEmpty(samples)`) — loop sources are in-test literals whose emptying is a visible edit to the test itself; the pin makes it compile-visible anyway. The two AiOperationContractTests facts' real assertions live in the helper `RoundTrip<T>` (`Assert.Equal` at AiOperationContractTests.cs:491) — the detector's no-assertion reading is the framing-(b) false positive, dispositioned by naming the helper.

### not-vacuous reason classes (no edit)

- **fs-predicate sites (44 incl. guards):** every `File.Exists`/`Directory.Exists` use is (a) inside `Assert.True/False` at body depth 0 — the predicate IS the assertion, absence fails, never silences (mechanical per-use proof: every fs use in every fs-flagged site was classified asserted / finally-cleanup / anchor / recorded); (b) `finally`-block cleanup (`if (Directory.Exists(root)) Directory.Delete(...)`) — no assertion depends on it; (c) the `FindRepoRoot` anchor probe + `Assert.True(Directory.Exists(...))` never-skip checkpoint in the guard tests; (d) `TeardownFlushTests.Flush_ExceedsBoundedWait` — captured into the fake's `FileExistedAtStop` field, asserted through it after Stop.
- **env-predicate (3, `DataRootOverrideEnvTests`):** the env var IS the seam under test — the test sets and restores `CCP_DATA_ROOT` itself (ProcessEnvCollection-isolated); no machine state can silence it.
- **platform-arm sites without early return (7):** `TitleProbe_PlatformTypedState`, `TitleObservation_GatedByConsentAndCapability`, `TryRecover_NoMatchingChildren`, `Reveal_ExistingSpiral_LaunchesOsSeam`, `ReadOsClientAreaAnimation_OnThisBox`, `ProcessFailedAttach_TypedUnavailable`, `ForCurrentPlatform_ReturnsPlatformBackend` — exhaustive if/else arms over the two supported OSes, every arm asserts; the predicate is the branch mechanism.
- **dynamic-skip (1):** the SP-062 pin — the `Assert.SkipWhen` IS the loud tripwire by design; permanently banned from `allowedSkips` (framing f).
- **`UpstreamPayloadInventoryTests` (9):** `RealRepo_...` assertions live in `RunGuard` (helper-hoisted false positive); the 8 `Fixture_...` facts assert inside the `act:` lambda that `WithFixtureRepo` invokes exactly once — not a loop/conditional (the detector's lambda=guarding rule is a deliberate conservative over-flag, dispositioned by reason).
- **`DtrhWatchdogTests.Tick_LiveSessionWithRegularBeats`:** counted `for` loop, constant bound 600 (pre-approach consult carve-out: counted loops take a reason, not a NotEmpty).

### Zero-weakening proof

`git diff` of every touched test file reviewed line-by-line: the only REMOVED lines are the five early-return guards (converted to `Assert.Skip*` — strictly stronger: the test now REPORTS its skip and the floor pins it by name), one inline `foreach` header hoisted to a named variable (with a NotEmpty added), and detector-internal lines. No assertion was weakened, no tolerance widened, no test quarantined, no `[Fact]` removed. Detector refinement (guarding-brace classification: try/finally/catch/using/lock transparent; only if/else/foreach/for/while/switch/lambda guard) rode the Step 3 commit and is stated in its message.


## Step 4 — the shape guard, and the T-17 auditor edit

- **Guard:** `VacuousShapeGuardTests.EverySilencingShapeSite_IsDispositionedInTheLedger` — repo-root walk, never skips (missing ledger = failure, empty ledger = failure), file:line violations; compares the detector surface against the ledger in BOTH directions plus shape-set equality, with `expectDetected` per entry (consult correction #2). Duplicate ledger keys violate. Its own honesty (shape guard; cannot see helper-hoisted assertions or empty-collection loops) is stated in its XML doc.
- **Captured RED:** probe fact `ZzVacuousShapeProbe.Probe_SilencedByEarlyReturn` (`if (!OperatingSystem.IsWindows()) return;` before its only assertion) → guard FAILED naming `CcpClient.Tests/ZzVacuousShapeProbe.cs:9` with shapes `[early-return, platform-predicate]` (`evidence/guard-red-probe.txt`, "Failed!"). Probe then deleted; guard green on rebuild; `git status --short` proves the probe file was never committed and is gone.
- The guard ALSO bit its own new fact on first run (its `FindRepoRoot` anchor reads as fs-predicate) — caught, ledgered with the anchor reason, green. The class guards itself.
- **T-17 (bounded, framing i):** `client/tools/port-audit-prompt.md` step 2 now invokes `node client/tests/floor/check-floor.mjs` after the build; a non-zero exit is an audit FAIL naming the wrapper's reason; the `CCP_DATA_ROOT` never-set note (port-workflow.md:204) is in the prompt; the skip check now reads "any skipped tests are exactly the names pinned in allowedSkips". NO other file under `client/tools/` touched, NO new file created there.
- **Mechanical pin:** `FloorWrapperGuardTests.AuditorPrompt_InvokesTheFloorWrapper_NeverBareDotnetTest` asserts the prompt invokes the wrapper, carries the `CCP_DATA_ROOT` warning, and contains NO bare `dotnet test` (same DotnetTest regex the packet guard uses).
- **`git ls-files client/tools/port-audit-prompt.md` proof** (force-added under the `.gitignore:168` bare `tools/` rule — the edit is in the tracked tree):

      $ git ls-files client/tools/port-audit-prompt.md
      client/tools/port-audit-prompt.md

- Floor bumped 898 -> 900 unit in this step's commit (+2 facts: the shape guard + the auditor pin), reason in the message; headless unchanged 35; allowedSkips unchanged (5 names).

## Step 5 — record: runs, honesty, consults, filings

### Run table (final code state, HEAD `f5f5d03b`; NEW exact counts: **900 unit / 35 headless / 2 named skips, build 0W/0E**)

| Run | Worktree | Cold/warm | Scope | Result |
|---|---|---|---|---|
| 0 | lane-1 | warm | contract (verify 0, build 0W/0E, floor) | **RED** — see the named red below (`evidence/run1-*.txt`) |
| 1 | `C:/Code/sp066-cold`, fresh `git worktree add --detach` from HEAD, **first-ever build** (0W/0E) | **COLD** | build + floor (verify.mjs N/A — `.pi/npm` is T-14-staged only in the lane; SP-065 precedent) | GREEN 900/35, skips exactly the 2 pinned names (`evidence/run2-cold-*.txt`) |
| 2 | lane-1 | warm | floor | GREEN 900/35, same 2 names (`evidence/run3-floor.txt`) |
| 3 | lane-1 | warm | floor | GREEN 900/35, same 2 names (`evidence/run4-floor.txt`) |
| 4 | lane-1 | warm | full contract: `verify.mjs` 0 + build 0W/0E + floor | GREEN (`evidence/run5-contract.txt`) — the terminal contract pass |

Four consecutive greens after the recorded red; >= 1 fresh-checkout first-ever build (run 1). Cold worktree removed afterwards (`git worktree list` shows only the expected entries). Skipped names every green run: `CcpClient.Tests.SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked` and `CcpClient.Tests.ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps` — exactly the pinned set, both allowed-skipped on Windows, both executing on the Linux machine class.

### The named red (framing k/f discipline — identified BY NAME, never retried away, never listed)

Run 0 (2026-08-13, lane-1, full contract): `CcpClient.Tests.AsyncLifecycleTests.Heartbeat_SkipsProjectionUntilBound_ThenFlowsThroughBoundary` FAILED — `Assert.IsType` expected `OperationOutcome.Cancelled`, got `Completed` at AsyncLifecycleTests.cs:203 (the StopAsync completion race in the SP-003 heartbeat lifecycle). TRX preserved at `C:\Users\Micha\AppData\Local\Temp\ccp-floor-JKnqKn\CcpClient.Tests\results.trx` (path also in `evidence/run1-floor.txt`). This is the **second recorded occurrence** of this exact race — SP-055's record (record.md:162) logged the identical failure 1/814 on a tree without any of this packet's changes; it passes 3/3 isolated here. The diff it ran under touches no lifecycle code (NotEmpty pins, OS-skip conversions in unrelated classes, the detector/guard, floor machinery). It was NOT listed in `allowedSkips` (that would be the quarantine abuse framing (f) bans) and NOT retried away silently — it is recorded here and named as an intended board filing below. The named privacy flake `ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery` did NOT fire in any run (0/5 runs).

### Honesty cell

1. **The detector is LEXICAL** (framing b): assertions in called helpers read as absent (false positive — dispositioned by naming the helper: `RoundTrip<T>`, `RunGuard`); a loop over a possibly-empty collection reads as asserting (false negative). **Runtime vacuity is not detected.** The mitigation is the framing-(c) NotEmpty pins, applied at disposition — 6 fixed, 5 literal-source belts.
2. **The guard binds only the shapes it enumerates.** A new way to silence an assertion is invisible until someone adds it to the detector surface. One such shape was OBSERVED during the sweep and deliberately not enumerated: `Assert.All(collection, ...)` / `collection.ForEach(...)` over a possibly-empty collection with expression-lambda assertions reads as a depth-0 assertion (21 `Assert.All` uses exist); the ledger's all-nested sweep covered statement-bodied loops only. Filing intent named below.
3. **`allowedSkips` records intent; nothing mechanically verifies that a listed test SHOULD be allowed to skip.** The admission rule is text in the file; the two named bans are text in the file; enforcement is review + this record, not code.
4. **The ledger's per-site reasons are human judgment that no test checks.** The guard binds key presence and shape sets, not the prose.
5. **T-17's auditor proof is NOT delivered** (framing i): the prompt edit and its mechanical pin are landed; the induced-skip audit RUN (a blind-auditor process executing the edited prompt against an induced skip) is not — it is beyond this packet's budget and is named as a residual below.
6. **Linux unproven**: zero WSL distros on this machine (standing named gate). The two Linux-gated skips were observed allowed-skipped on Windows; their Linux-side execution is unproven here and was not faked.

### Consults (both solo; verdicts reproduced in substance, nothing stitched from reasoning)

- **Pre-approach (Step 1):** CORRECTION — (1) class attribution by last textual match assigned facts to sibling nested fakes; fixed to brace-range enclosing chains, inventory re-derived; (2) framing-(c) NotEmpty erases the all-nested shape — per-entry `expectDetected` added, mitigated sites recorded expected-absent; counted for-loops take a reason; (3) TRX `testName` carries theory arguments — pre-`(` exact match, written into floor.json; demonstrate wrapper verdicts on synthetic fixtures, not a real probe. **Actual answering model: not surfaced by the consult transport** (SP-063/SP-065 precedent — recorded as unverifiable from inside the worker; the verdict arrived COMPLETE, no truncation).
- **Pre-completion (Step 5):** three calls — (a) connection error, no content; (b) **truncated verdict** ("**CORRECTION — do" then cut — the wave-17/21/22 truncation class, now also here); (c) narrow re-ask capped at 120 words surfaced cleanly: **CORRECTION — record.md Step 1 was stale/self-contradicting** (94 sites / nested 45 prose against a committed 79-site / nested-22 `inventory-raw.json`; "the enumeration with the exact surface that produced it" failed as written). **Applied**: the Step 1 reconciliation above was re-derived from the final committed inventory with the two mid-packet detector refinements stated plainly (class attribution by brace range; guarding-brace depth 45→22). **Actual answering model: not surfaced by the consult transport** (same as pre-approach; recorded as unverifiable from inside the worker, never guessed).

### Engine-review presence per step (Review Level 2; `spine_review_step --type plan` called after steps 1-4)

| Step | Call | Outcome |
|---|---|---|
| 1 | `.reviews/1-20260813T055252.md` | engine-skipped (SP-195: nested reviewer spawn blocked in worker; engine reviews after .DONE), spawnFailed=false |
| 2 | `.reviews/2-20260813T060326.md` | engine-skipped, spawnFailed=false |
| 3 | `.reviews/3-20260813T062550.md` | engine-skipped, spawnFailed=false |
| 4 | `.reviews/4-20260813T064815.md` | engine-skipped, spawnFailed=false |

### Intended board filings (ENABLER 2 — no board row state set; orchestrator reconciles at land)

1. **Row 49 part (1) DONE** with this record as evidence: 78-site sweep ledgered, detector + guard committed, name-anchored skip pin live (5 names, admission rule, two permanent bans), T-17 edit + pin landed.
2. **New filing:** `AsyncLifecycleTests.Heartbeat_SkipsProjectionUntilBound_ThenFlowsThroughBoundary` Cancelled-vs-Completed race — SECOND occurrence (first: SP-055 record.md:162). Not a quarantine candidate; reproduce and root-cause at the source (the StopAsync completion path), same discipline as the named privacy flake.
3. **New filing:** `Assert.All`/expression-lambda assertion loops over possibly-empty collections are an unenumerated silencing shape (21 `Assert.All` uses) — extend the detector surface or pin NotEmpty at those sites in a follow-up.
4. **T-17 residual:** the auditor-prompt edit + pin are landed; the induced-skip blind-auditor RUN (executing the edited prompt end-to-end against an induced skip) is unproven — schedule with the next loop audit cycle.

### What this sweep does NOT close

Runtime vacuity (honesty 1), unenumerated future shapes (honesty 2), admission-rule enforcement (honesty 3), reason-checking (honesty 4), the T-17 auditor run (honesty 5), Linux execution of the two Linux-gated facts (honesty 6). This packet is infrastructure-only: it closes NO product capability (port-workflow.md item 11); `client/src/**` is untouched (zero product code).

## Step 6 — verification evidence

- Contract testCommand green through the wrapper: run 4 (`evidence/run5-contract.txt`) — `verify.mjs` exit 0, build 0W/0E, floor `FLOOR OK: CcpClient.Tests: 900/900 total, 2 skipped [the 2 pinned names]; CcpClient.HeadlessTests: 35/35 total, 0 skipped`.
- 3 consecutive full-suite greens: runs 1-3 above (+ run 4), run 1 a fresh-checkout first-ever COLD build.
- `git diff --check`: clean.
- `git status --short`: only File Scope paths (client/tests/**, client/tools/port-audit-prompt.md, spine-tasks/SP-066-vacuous-shape-sweep/**).
- `git status --porcelain --ignored=matching -uall`: no NEW ignored artifact from any run — only the pre-existing `.pi/npm/` (T-14-staged) and `bin/`/`obj/` build output the contract's 
