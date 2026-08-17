# SP-086 — record

Packet: `spine-tasks/SP-086-process-env-collection-guard/PROMPT.md`. Plan: `.port/plans/SP-086-process-env-collection-guard/plan-round-2.md` (APPROVED, round-2 verdict). Lane branch `lane/SP-086-process-env-collection-guard`, worktree `.claude/worktrees/sp086`, base `feat/crossplatform` @ `cf9f7143`.

Deliverable: one new file, `client/tests/CcpClient.Tests/ProcessEnvCollectionGuardTests.cs`, plus the one-statement remedy in `client/tests/CcpClient.Tests/IntegrationProofTests.cs` that the 2026-08-15 scope amendment put in `fileScopeMustChange`. No product code. No board edit. No `floor.json` edit.

---

## Step 1 — census, re-derived (not transcribed)

Re-run in this worktree with `git grep` / ripgrep over `client/tests/**/*.cs`, `obj/` and `bin/` excluded. **The orchestrator's census in the packet is correct in every particular**, including the live violation. The one correction is the *reading* of Step 1 item 2's token list, resolved at the plan gate and ratified there (§ "Decision 1" below).

### 1a. Every `new CompositionRoot` under `client/tests/` — 22 sites

| file:line | initializer assigns `SettingsPathFactory`? | enclosing class | carries `[Collection(nameof(ProcessEnvCollection))]`? | BOUND by this guard? |
|---|---|---|---|---|
| `CcpClient.Tests/CapabilityTests.cs:339` | yes (`:341`) | `CapabilityTests` | no | no |
| `CcpClient.Tests/CompanionCompositionTests.cs:23` | yes | `CompanionCompositionTests` | no | no |
| `CcpClient.Tests/CompositionRootTests.cs:16` | **no** (`new CompositionRoot().Build(...)`) | `CompositionRootTests` | **yes** (`:10`) | yes — compliant |
| `CcpClient.Tests/CompositionRootValidationTests.cs:19` | **no** | `CompositionRootValidationTests` | **yes** (`:13`) | yes — compliant |
| `CcpClient.Tests/CompositionRootValidationTests.cs:30` | **no** (`LogSinkFactory` only) | same | yes | yes — compliant |
| `CcpClient.Tests/CompositionRootValidationTests.cs:44` | **no** (`ParticipantsFactory` only) | same | yes | yes — compliant |
| `CcpClient.Tests/CompositionRootValidationTests.cs:57` | **no** | same | yes | yes — compliant |
| `CcpClient.Tests/CompositionRootValidationTests.cs:83` | **no** | same | yes | yes — compliant |
| `CcpClient.Tests/DataRootOverrideTests.cs:165` | **no** | `DataRootOverrideEnvTests` | **yes** (`:124`) | yes — compliant |
| `CcpClient.Tests/HarnessEntryPointGuardTests.cs:74` | n/a — **string literal** `"new CompositionRoot"` | `HarnessEntryPointGuardTests` | no | **no — must be sanitized away** |
| `CcpClient.Tests/IntegrationProofTests.cs:21` | yes (`:25`) | `IntegrationProofTests` | no | no |
| `CcpClient.Tests/IntegrationProofTests.cs:67` | **no** (`ParticipantsFactory` only) | `IntegrationProofTests` | **no** | **VIOLATION (live, pre-remedy)** |
| `CcpClient.Tests/QuickToggleDispatchTests.cs:26` | yes | `QuickToggleDispatchTests` | no | no |
| `CcpClient.Tests/StatusTickerSliceTests.cs:26` | yes | `StatusTickerSliceTests` | no | no |
| `CcpClient.Tests/StatusTickerSliceTests.cs:120` | yes | same | no | no |
| `CcpClient.Tests/StatusTickerSliceTests.cs:162` | yes | same | no | no |
| `CcpClient.Tests/TeardownFlushTests.cs:72` | yes | `TeardownFlushTests` | no | no |
| `CcpClient.HeadlessTests/AvatarTubeHeadlessTests.cs:31` | yes | — | n/a (other assembly) | n/a |
| `CcpClient.HeadlessTests/CompanionWindowHeadlessTests.cs:26` | yes | — | n/a | n/a |
| `CcpClient.HeadlessTests/DashboardCardHeadlessTests.cs:27` | yes | — | n/a | n/a |
| `CcpClient.HeadlessTests/FeaturePopupHeadlessTests.cs:28` | yes | — | n/a | n/a |
| `CcpClient.HeadlessTests/QuickToggleDispatchHeadlessTests.cs:27` | yes | — | n/a | n/a |

22 sites, matching the packet exactly.

### 1b. Direct data-root tokens in test source

**Real code (not inside a literal or comment):**

| file:line | token | enclosing class | attribute? |
|---|---|---|---|
| `DataRootOverrideTests.cs:79`, `:93` | `CompositionRoot.ActiveDataRootOverride` | `DataRootOverrideTests` | yes (`:28`) |
| `DataRootOverrideTests.cs:91` | `CompositionRoot.DefaultSettingsPath` | `DataRootOverrideTests` | yes (`:28`) |
| `DataRootOverrideTests.cs:137`, `:207` | `CompositionRoot.DefaultSettingsPath` | `DataRootOverrideEnvTests` | yes (`:124`) |
| `DataRootOverrideTests.cs:139` | `DtrhProfileLock.DtrhDataRoot` | `DataRootOverrideEnvTests` | yes |
| `DataRootOverrideTests.cs:140` | `DtrhProfileLock.WebView2ProfileDir` | `DataRootOverrideEnvTests` | yes |
| `DataRootOverrideTests.cs:131`, `:157`, `:202` | `Environment.GetEnvironmentVariable(CompositionRoot.DataRootOverrideVariable)` | `DataRootOverrideEnvTests` | yes |
| `DataRootOverrideTests.cs:132`, `:148`, `:158`, `:194`, `:203`, `:211` | `Environment.SetEnvironmentVariable(CompositionRoot.DataRootOverrideVariable, …)` | `DataRootOverrideEnvTests` | yes |
| `HarnessEntryPointGateTests.cs:63` | `CompositionRoot.DataRootOverrideVariable` **bare** (`Assert.Contains(CompositionRoot.DataRootOverrideVariable, message)`) | `HarnessEntryPointGateTests` | **no** |

**Inside literals/comments (must be blanked, or they become false positives):** `HarnessEntryPointGuardTests.cs:72` and `:74`; `DataRootChokePointGuardTests.cs:49` and `:69`; `VacuousShapeDetector.cs:220`; `HarnessEntryPointGateTests.cs:10`; `DataRootOverrideTests.cs:11`, `:23`, `:80`, `:94`, `:163`; `CompositionRootTests.cs:8`; `CompositionRootValidationTests.cs:7` and `:9`; `FloorWrapperGuardTests.cs:250` and `:256`.

`ChaosTunnelService.DataRoot`: **zero** occurrences under `client/tests/`. `Environment.SetEnvironmentVariable` under `CcpClient.HeadlessTests`: **zero**. All six in-suite mutations live in `DataRootOverrideEnvTests`.

### 1c. Classes carrying the attribute today — 4, exactly as filed

`CompositionRootTests.cs:10`, `CompositionRootValidationTests.cs:13`, `DataRootOverrideTests.cs:28`, `DataRootOverrideTests.cs:124`. Definition at `DataRootOverrideTests.cs:121-122` (`[CollectionDefinition(DisableParallelization = true)] public sealed class ProcessEnvCollection;` — bodyless, which the type-span parser has to handle or it would steal the next type's brace).

### 1d. Reachability — confirmed, not assumed

- `CompositionRoot.cs:59` — `public const string DataRootOverrideVariable = "CCP_DATA_ROOT";` is a **compile-time constant**. Naming it touches nothing.
- `CompositionRoot.cs:108` and `:127` are the **only two** environment reads of it in `client/src`.
- `CompositionRoot.cs:85` — `SettingsPathFactory` defaults to `DefaultSettingsPath`. `:251` calls it **unconditionally** inside `Build`.
- **Stronger than the packet claims:** `Validate` reads it too. `CompositionRoot.cs:216` invokes `ParticipantsFactory`, and the default `DefaultParticipants` calls `SettingsPathFactory()` at `:167`, `:181`, `:188`, `:197`. So `CompositionRootValidationTests.Validate_DefaultRoot_Passes` (`:19`) reaches the variable through `Validate` alone — which is why it is the right named construction-half control.
- `Program.CreateStartupPhases` (`Program.cs:272-307`) does **not** read the variable; only `Program.Main` does (`Program.cs:93`). Confirmed by reading both.
- `DtrhProfileLock.cs:32-33`, `:37`, `:43`; `ChaosTunnelService.cs:54-55`; `DtrhParticipant.cs:57`; `IntakeParticipant.cs:42` all route through `DefaultSettingsPath()` — genuine readers, all reachable only when `SettingsPathFactory` is *not* overridden.
- An overridden `SettingsPathFactory` is, today, sufficient to avoid the read on every path a test reaches. Confirmed; **no** construction reaches `DefaultSettingsPath()` transitively despite an overridden factory.
- `HarnessEntryPoints.RefusalMessage` (`HarnessEntryPoints.cs:119-125`) is pure interpolation over the const; `HarnessFlagsIn` (`:102-116`) is a dictionary lookup. Neither touches the environment, and `:99-101` says so in source. This is why `HarnessEntryPointGateTests` provably cannot race.

### 1e. The live violation — confirmed real

`IntegrationProofTests.cs:14` (no `[Collection]`) → construction at `:67-74` assigning **only** `ParticipantsFactory` → `Program.CreateStartupPhases` (`:78`) → `root.Build` (`Program.cs:286`) → `CompositionRoot.cs:251` → default `SettingsPathFactory` = `DefaultSettingsPath()` → the env read at `:108`. Its sibling fact at `:21-26` already redirects, so only the second fact was exposed.

---

## Decision 1 — branch 1 (implement the rule as specified), with the ratified token qualification

**Branch selected: 1.** The census matches the orchestrator's on the construction half exactly, and on the live violation exactly.

**The one contradiction, and why it does not reopen anything.** Step 2 binds "any of the direct data-root tokens listed in Step 1 item 2", and item 2 lists `CompositionRoot.DataRootOverrideVariable`. Read *unqualified*, that makes `HarnessEntryPointGateTests.cs:63` a **second** violation — a file outside `fileScopeMustChange`, in a class that provably never touches the process environment (1d above), whose only available "remedy" would be serializing a class that cannot race, contradicting its own doc comment at `:9-11`. That outcome is BLOCKED, which is exactly what the 2026-08-15 amendment exists to prevent.

Item 2 already qualifies its accessors ("`Environment.GetEnvironmentVariable` / `SetEnvironmentVariable` **against that variable**"). The same qualification is applied to the variable's *name*:

> `DataRootOverrideVariable` (or the raw `CCP_DATA_ROOT`) binds a class **when it appears inside the argument list of `Environment.Get/SetEnvironmentVariable`**; a bare reference to the constant does not.

This is the only reading under which the packet's own stated outcome (`PROMPT.md:175-177` — exactly one violation, `IntegrationProofTests.cs:14`) is true. It touches none of the three forbidden moves at `PROMPT.md:189-193`: the live violation stays a violation through the **construction** rule, no class is suppressed, allow-listed or grandfathered, and the detection rule is not narrowed around what it found. **This was taken to the advisory gate as the packet's consult trigger requires (the plan gate), where it was independently verified and ratified.** An owner ruling is still recorded as owed (§ "Owed / filed, not fixed" below), so the next author does not re-litigate it.

**Branch 2 of Decision 1 does not apply:** no construction reaches `DefaultSettingsPath()` through a path this packet did not model. No call-graph analysis was added; the lexical limit is named in the honesty section instead.

---

## Decision 2 — verdict for every violation found

Exactly one violation existed on the tree: `IntegrationProofTests` for the construction at `:67`.

**Verdict: branch 1 (remove the read).** `StartupFailure_ThroughRealRunner_TypedFailureAndTeardownLeavesNoOrphan` (`:62`) asserts outcome `Failed`, phase `CoreServices`, kind `Fatal`, host non-null and `StopCount == 1` (`:80-87`). Its subject is startup failure and guarded teardown — **not** the data root, and not the override. So the correct fix removes the read rather than serializing around it, which also keeps the class out of the serialized collection and the suite parallel. This is what eleven other real-root test sites in the suite already do through the same `SettingsPathFactory` seam (`CompositionRoot.cs:85`).

The applied diff, one statement, inside the existing initializer:

```csharp
         var root = new CompositionRoot
         {
+            SettingsPathFactory = () => Path.Combine(
+                Path.GetTempPath(), "ccp-integration-fail-" + Guid.NewGuid().ToString("N"), "settings.json"),
             ParticipantsFactory = infra =>
```

Side-effect check, re-verified in source: `Build` consumes the value only through `Path.GetDirectoryName` (`CompositionRoot.cs:251`), which is pure. The directory is touched only by the `atomic-filesystem` probe (`:254-255`), registered in the **CapabilityProbes** phase, which this fact never reaches because it fails in **CoreServices** (`Program.cs:290`) and `StartupPhaseRunner` stops there. No directory is created, no cleanup is owed, and no existing assertion in the fact changes. Confirmed empirically: the fact passes on the remedied tree, and R0 (below) shows the guard reds on the pre-remedy form.

**Decision 3 not reached** — the headless half is green today (zero mutators).

---

## SCOPE — Branch A, on the evidence

`PROMPT.md:171-193` (amended 2026-08-15) makes Branch A the standing instruction and `IntegrationProofTests.cs` part of `fileScopeMustChange` (`:142`, `:154`). The evidence for being in Branch A rather than Branch B is 1e: the honest guard reds on the pre-amendment tree with **exactly one** violation, and its remedy is one statement in a file now in scope. Recorded here as the packet requires: the amendment is `PROMPT.md:171-193`, and the exact diff applied is the block quoted above.

**Refused, explicitly.** No suppression list, no allow list, no ID-based grandfather rule, no narrowing of the detection rule so the found violation stops being a violation. The guard contains no per-class data structure at all; the only class names it holds as identifiers are the four *controls*, and every one of them makes the guard **stricter** (two must be detected, two must not be). No code path consults a name in order to skip a check. Nor was `[assembly: CollectionBehavior(DisableTestParallelization = true)]` added, the rule was not narrowed to the empty-initializer form, reflection over `Assembly.GetTypes()` was not used, `VacuousShapeDetector.cs` was not edited, `vacuous-shape-ledger.json` was not edited, `DataRootChokePointGuardTests.cs:64-70` was not weakened, `floor.json` was not edited, and `CCP_DATA_ROOT` was never set or exported anywhere — including inside the bite matrix (see the R4/R4b note).

---

## Step 2/3 — what was built

One file, `client/tests/CcpClient.Tests/ProcessEnvCollectionGuardTests.cs`, class `ProcessEnvCollectionGuardTests`, **two `[Fact]`s**, both lexical repo-root source walks. No reflection, no assembly loading, no waits, no environment access.

**Fact 1 — `EveryDataRootReadingClass_CarriesTheProcessEnvCollectionAttribute`** (over `client/tests/CcpClient.Tests/`). A class is **BOUND** when, after blanking comments and string/char literals, its body contains either a `new CompositionRoot` whose brace-matched object initializer does **not** assign `SettingsPathFactory` (the no-initializer form `new CompositionRoot()` included), or one of `CompositionRoot.DefaultSettingsPath`, `CompositionRoot.ActiveDataRootOverride`, `DtrhProfileLock.DtrhDataRoot`, `DtrhProfileLock.WebView2ProfileDir`, `ChaosTunnelService.DataRoot`, or an `Environment.Get/SetEnvironmentVariable` whose paren-matched argument list names the variable. A BOUND class must carry `[Collection(nameof(ProcessEnvCollection))]` on some declaration of that class name (partials aggregate). Accepted spellings: `nameof(X)`, `typeof(X)`, `"X"`, `[Collection<X>]` — acceptance keyed on the **name**, so `[Collection<SomethingElse>]` on a bound class is still a violation. Violations are anchored at the class **declaration** line and name every offending site's `file:line`.

**Fact 2 — `TheHeadlessAssembly_NeverMutatesTheDataRootVariable`** (over `client/tests/CcpClient.HeadlessTests/`). Collections do not span assemblies and `ProcessEnvCollection` is defined in `CcpClient.Tests`, so membership is unavailable there and the only meaningful rule is: never mutate the variable at all. The assembly-boundary reason is stated in the file's doc comment, as the Completion Criteria require.

**Fail-closed, all of it:** unresolvable repo root throws; a missing `client/tests` or project tree throws; a `[Collection(...)]` argument matching none of the four shapes is a violation, not a silent pass; an unmatched `new CompositionRoot` argument list or initializer is a violation; an unbalanced type body is a violation; a data-root read with no enclosing type is a violation; and a `.cs` file under `client/tests` outside the two known project roots is a violation whose message names both roots and says to **extend** the guard rather than delete the check.

**Anti-vacuity:** BOUND is asserted non-empty, is asserted by name to contain `CompositionRootValidationTests` (construction half) and `DataRootOverrideTests` (token half), and is asserted by name **not** to contain `HarnessEntryPointGuardTests` (sanitizer control) or `ProcessEnvCollectionGuardTests` (self-exclusion). Fact 2 pins its scanned-file list non-empty.

**Shape compliance (Step 3), verified by a green `VacuousShapeGuardTests` on the full run:** no `File.Exists(`/`Directory.Exists(` in either fact body (the tree-existence checks live in throwing helpers); at least one `Assert.` at guarding depth 0 in each fact; no bare `return;`; no `Assert.Skip*`; no `OperatingSystem.Is*`/`RuntimeInformation.IsOSPlatform`; no `Environment.GetEnvironmentVariable` token in a fact body (detection tokens are class-level `private const string`). **Zero new entries are owed in `client/tests/floor/vacuous-shape-ledger.json`** — the file was not edited and `VacuousShapeGuardTests` passes.

---

## Step 4 — the bite matrix, executed

Every revert was applied **alone**, built, run against the **full** `CcpClient.Tests` project (never a filter, so the red *count* is honest), then restored with `git checkout --` from the lane commit, which is byte-identical by construction. R3 and R6 touch the same file at different lines and were run separately with a restore between them. Red counts are the runner's own `Failed:` number.

Line numbers inside a revert's violation text are one lower than the census where the revert **deleted** the attribute line above the class; that is the revert's own edit, not a detector drift.

| # | revert | facts red | what red, and the violation text |
|---|---|---|---|
| **R0** | restore `IntegrationProofTests.cs:67` to its pre-remedy form (in scope) | **1** | `ProcessEnvCollectionGuardTests.EveryDataRootReadingClass_…` → `client/tests/CcpClient.Tests/IntegrationProofTests.cs:14: IntegrationProofTests reads the data-root environment variable [client/tests/CcpClient.Tests/IntegrationProofTests.cs:67 (constructs CompositionRoot without assigning SettingsPathFactory)] but does not carry [Collection(nameof(ProcessEnvCollection))]. …` — the packet's own `:14 … for the construction at :67` phrasing. A rule narrowed to `new CompositionRoot()` would pass R0; this revert kills that narrowing. |
| **R1** | delete the attribute at `CompositionRootValidationTests.cs:13` | **1** | same fact → `…/CompositionRootValidationTests.cs:13: CompositionRootValidationTests reads the data-root environment variable [ …:18; …:29; …:43; …:56; …:82 (all "constructs CompositionRoot without assigning SettingsPathFactory")] but does not carry [Collection(nameof(ProcessEnvCollection))]. …` — all five construction sites named. Zero tokens in that class, so this is a pure **construction-half** bite. |
| **R2** | delete the attribute at `CompositionRootTests.cs:10` | **1** | same fact → `…/CompositionRootTests.cs:10: CompositionRootTests reads the data-root environment variable [ …/CompositionRootTests.cs:15 (constructs CompositionRoot without assigning SettingsPathFactory)] …` — independent class, and the `new CompositionRoot().Build(...)` form (empty argument list, no initializer). |
| **R3** | delete the attribute at `DataRootOverrideTests.cs:124` (`DataRootOverrideEnvTests`, **the mutator**) | **1** | same fact → `…/DataRootOverrideTests.cs:124: DataRootOverrideEnvTests reads the data-root environment variable [ …:164 (constructs CompositionRoot without assigning SettingsPathFactory); …:136, :206 (names CompositionRoot.DefaultSettingsPath); …:138 (names DtrhProfileLock.DtrhDataRoot); …:139 (names DtrhProfileLock.WebView2ProfileDir); …:130, :156, :201 (passes the data-root variable to Environment.GetEnvironmentVariable); …:131, :147, :157, :193, :202, :210 (passes the data-root variable to Environment.SetEnvironmentVariable)] …`. **The justifying comparison, stated explicitly: `DataRootChokePointGuardTests.NoDataRootSpecialFolderUseOutsideTheChokePoint` stayed GREEN through R3** — it hard-codes `typeof(DataRootOverrideTests)` (`DataRootChokePointGuardTests.cs:64-70`) and cannot see the mutator class at all. 1 red, and that one red is the new guard. This is the revert that proves the new guard covers ground the old assertion does not. |
| **R6** | delete the attribute at `DataRootOverrideTests.cs:28` (`DataRootOverrideTests`, the pin class) | **2** | `ProcessEnvCollectionGuardTests.EveryDataRootReadingClass_…` → `…/DataRootOverrideTests.cs:28: DataRootOverrideTests reads the data-root environment variable [ …:90 (names CompositionRoot.DefaultSettingsPath); …:78, :92 (names CompositionRoot.ActiveDataRootOverride)] …`. **Every firing site is a TOKEN; the class contains no construction**, so this red can only arrive through the token half — the half that survives R0–R5 untouched. The second red is the **pre-existing** `DataRootChokePointGuardTests.NoDataRootSpecialFolderUseOutsideTheChokePoint`, which asserts that exact attribute on that exact class; expected, and R3 already had the same property in reverse. |
| **R4** | add one uncalled `Environment.SetEnvironmentVariable(CompositionRoot.DataRootOverrideVariable, value)` to `CcpClient.HeadlessTests/DashboardCardHeadlessTests.cs` | **1** | `ProcessEnvCollectionGuardTests.TheHeadlessAssembly_NeverMutatesTheDataRootVariable` → `client/tests/CcpClient.HeadlessTests/DashboardCardHeadlessTests.cs:27: DashboardCardHeadlessTests mutates the data-root environment variable inside CcpClient.HeadlessTests. xunit collections do not span assemblies and ProcessEnvCollection is defined in CcpClient.Tests (DataRootOverrideTests.cs:121-122), so there is NO membership available here … the rule for this project is therefore that the variable is never mutated at all. …`. **Fact 1 stayed green** (different project root) — the two facts are independent. |
| **R4b** | same, in the **raw-literal** spelling `Environment.SetEnvironmentVariable("CCP_DATA_ROOT", value)` | **1** | same fact, same message. This discharges carried condition 2 with executed evidence rather than a claim: the raw spelling is matched against **pre-sanitized** text scoped to an environment-accessor argument list, so it is *not* a dead branch. `FloorWrapperGuardTests.cs:256` (`Assert.Contains("CCP_DATA_ROOT", prompt)`) is not inside such an argument list and stayed clean, as predicted. |
| **R5** | disable the local sanitizer — one line in my own file, `var sanitized = Sanitize(raw);` → `var sanitized = raw;` | **1** | `ProcessEnvCollectionGuardTests.EveryDataRootReadingClass_…` → `HarnessEntryPointGuardTests must NOT be detected: its "new CompositionRoot" is a string literal (HarnessEntryPointGuardTests.cs:74) and the local sanitizer must blank it — reporting that class is a false positive with no honest remedy`. The sanitizer is load-bearing, not decorative. (The named negative-control assertion fires before the trailing violations assertion, which is the designed mapping "assertion 4 → R5"; with the sanitizer off the violations list would additionally have named `DataRootChokePointGuardTests` for `:49`/`:69` and this guard's own literals.) |

**Note on R4/R4b and the `CCP_DATA_ROOT` prohibition.** The packet forbids setting or exporting the variable *anywhere*, "including just for the bite matrix" — a set would make the SP-057 pin skip and the exact-count floor go blind. Both probes were therefore written as **uncalled** private methods: the token exists in source for the lexical guard to find, and the mutation never executes. Confirmed by the runs: in every revert the only skips were the two Linux-gated allowedSkips names, never `DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault`.

### Positive control on the restored tree, per control, naming what it fires through

Fact 1 passes on the restored tree, which means BOUND is non-empty and contains `CompositionRootValidationTests` and `DataRootOverrideTests` by name, and excludes `HarnessEntryPointGuardTests` and `ProcessEnvCollectionGuardTests` by name. The firing sites are the ones the reverts printed:

| class | in BOUND? | fires through |
|---|---|---|
| `CompositionRootTests` | yes | **construction** — `CompositionRootTests.cs:16` |
| `CompositionRootValidationTests` | yes | **construction** ×5 — `:19`, `:30`, `:44`, `:57`, `:83` (zero tokens) |
| `DataRootOverrideTests` | yes | **token only** — `ActiveDataRootOverride` `:79`/`:93`, `DefaultSettingsPath` `:91` (zero constructions) |
| `DataRootOverrideEnvTests` | yes | **both** — construction `:165`, tokens `:137`/`:207`/`:139`/`:140`, env-accessor argument lists `:131`/`:157`/`:202` and `:132`/`:148`/`:158`/`:194`/`:203`/`:211` |
| `HarnessEntryPointGuardTests` | **no** | asserted by name; its `new CompositionRoot` is a literal (`:74`) |
| `ProcessEnvCollectionGuardTests` | **no** | asserted by name; all its detection tokens are string literals |
| `DataRootChokePointGuardTests` | **no** | not asserted by name, but proven: it carries **no** attribute, and the fact's trailing assertion is `violations.Count == 0`, so had it been BOUND it would be a violation |
| `HarnessEntryPointGateTests` | **no** | same argument — no attribute, zero violations. This is the qualified-token reading working as designed (`:63` is a bare constant reference) |
| `IntegrationProofTests` (post-remedy) | **no** | same argument; and R0 shows it *is* BOUND without the remedy |

### Clean-tree proof before the final gate

```
$ git -C .claude/worktrees/sp086 status --porcelain
(no output)

$ git -C .claude/worktrees/sp086 diff --stat HEAD
(no output)
```

Every out-of-scope file touched by R1–R4b was transient evidence only: never committed, restored byte-identically from the lane commit. The lane commit contains exactly the two in-scope source files plus this packet folder.

---

## Step 6 — verification

Two separate commands, build immediately before the gate, both through the slot semaphore (`--slots 3`), which passes the child's exit code through unchanged:

```
node client/tools/gate/with-slot.mjs --slots 3 -- dotnet build client/CcpClient.sln -c Debug --nologo
node client/tools/gate/with-slot.mjs --slots 3 -- node client/tests/floor/check-floor.mjs
```

- **Build: 0 Warning(s), 0 Error(s).**
- **Floor, read from `client/tests/floor/floor.json` (never from the packet):** pin `CcpClient.Tests` = **1028**, `CcpClient.HeadlessTests` = **35**.
- **Observed: `CcpClient.Tests` = 1030** (1028 passed + 2 skipped, 0 failed), **`CcpClient.HeadlessTests` = 35** (35 passed, 0 skipped, 0 failed — read from the run's TRX `<Counters …/>`).
- **`observed == pin + declared delta`: 1028 + 2 = 1030 ✓, 35 + 0 = 35 ✓.**
- The wrapper reports `FLOOR VIOLATION — total drift: 1030 result(s) (pin total 1028)`. **That is the designed state**: a lane never edits the shared pin; the land sums the deltas and applies one bump. The delta is declared in `spine-tasks/SP-086-process-env-collection-guard/floor-delta.json` as `{unit: 2, headless: 0}`.
- The only skips are the two Linux-gated `allowedSkips` names (`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`, `SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`). The SP-057 pin executed. `VacuousShapeGuardTests`, `TestTimingGuardTests`, `FloorWrapperGuardTests` and `DataRootChokePointGuardTests` are all green.

---

## Carried conditions — discharge

The seven non-blocking suggestions carried verbatim in the approved plan's `## Carried conditions`, plus the two non-blocking notes on the round-2 verdict. Each is discharged or explained.

1. **Qualified-token reading ratified; implement §2 as written, do not re-open.** Implemented exactly as written; not re-litigated. The owner ruling is recorded as owed below. **Discharged.**
2. **The raw-literal form escapes both facts; consider matching it against pre-sanitized text scoped to an accessor argument list.** **Adopted, and proven.** The guard finds accessor call sites in *sanitized* text (so a mention in a comment or literal is never a call), then reads the argument range out of *raw* text at the same offsets — which is why offset preservation is load-bearing in the sanitizer. `R4b` executes the exact spelling and reds. `FloorWrapperGuardTests.cs:256` is not inside such an argument list and stays clean, as the reviewer predicted. The `CCP_DATA_ROOT` alternative is therefore a live branch, not a dead one. **Discharged.**
3. **Promote the self-exclusion to a depth-0 assertion.** Done: `Assert.False(bound.Contains(SelfControl), …)` sits at depth 0 alongside the other three named controls. **Discharged.**
4. **Attribute a violation to the outermost enclosing type; `record` declarations are not matched by `\bclass\s+(\w+)`.** Both done. `Outermost(spans, offset)` returns the first (outermost) type span containing the site, so a token in a nested private helper (`IntegrationProofTests.FailingParticipant`, `DataRootOverrideTests.TempDir`, `CompositionRootValidationTests.RecordingParticipant`) is reported against the outer test class — the only one that can carry `[Collection]`. The type-declaration regex is `\b(?:class|struct|record)\s+(?:(?:class|struct)\s+)?(\w+)`, so `record`, `struct`, `record class` and `record struct` are all matched and none is silently omitted from a message. Nothing in the tree exercises either path today. **Discharged.**
5. **`[Collection<ProcessEnvCollection>]` acceptance must be keyed on the type argument.** Done: the generic head is angle-matched and the argument goes through the same name extraction as the other three spellings, then compared to `ProcessEnvCollection`. `[Collection<SomethingElse>]` on a BOUND class yields "does not carry" and therefore a violation, not an accepted-spelling pass. No user in the tree, so this path is unexercised at runtime. **Discharged in code, named as unexercised in honesty.**
6. **`PROMPT.md:293` says "one line"; the remedy is one statement wrapped across two physical lines — either format it on one line or note the wrap.** **Noted here, explicitly.** The remedy is **one statement**, wrapped across two physical lines to stay inside the file's line-length style (a single physical line would run ~145 characters). It is substantively minimal: one initializer assignment, nothing else in the file changed. The reviewer's side-effect analysis was re-confirmed at source (see Decision 2). **Discharged.**
7. **The "unrecognised third project" assertion should say so explicitly, naming both recognised roots, so the next author extends the guard rather than deleting the check.** Done: the message names `CcpClient.Tests` with its rule and `CcpClient.HeadlessTests` with its rule, states that a new project is invisible to **both** halves, says the check fails closed on purpose, and ends "EXTEND the guard … Never delete this check." **Discharged.**

Round-2 verdict, non-blocking note 1 — **"§5 summarises the mapping as 'assertion 3 to R6'; strictly, under R6 assertion 3 still passes and the trailing violations assertion is what reds."** Correct, and the executed matrix bears it out: under R6 the class stays BOUND (assertion 3 passes) and the red arrives at the trailing `violations.Count == 0`, naming only token sites. Assertion 3's own proving revert is the guard-side token-list deletion (delete `DataRootEntryPoints`, and `Assert.True(bound.Contains(TokenControl))` is what reds, because `DataRootOverrideTests` contains no construction and its only evidence is tokens). **This lane did not execute that revert.** It is not in the matrix above, and by this packet's own thesis an unexecuted claim is not evidence — so it is recorded here as **asserted by this lane, executed elsewhere**: the final completion review ran it as its **Bite D** and reports that assertion reddening. That review is the executed source; this record is not. The mapping sentence in the plan was imprecise; the mechanism is not. **Recorded.**

Round-2 verdict, non-blocking note 2 — **"R6 will also red the pre-existing `DataRootChokePointGuardTests.cs:64-70`; its recorded red count should expect more than one."** Confirmed by execution: R6 reds **2** facts, and both are named in the matrix above. The SP-057 pin did **not** skip in that run. **Recorded.**

---

## Owed / filed, not fixed

1. **`HarnessEntryPointGateTests.cs:63` — owner ruling on the qualified-token reading.** Out of File Scope, and not a race: `CompositionRoot.cs:59` is a compile-time `const string` and `HarnessEntryPoints.RefusalMessage`/`HarnessFlagsIn` never touch the environment. The reading was verified and ratified at the plan gate; an owner confirmation is still owed so the next author does not re-open it. No code change made.
2. **`DataRootChokePointGuardTests.cs:63` — stale prose.** Its comment says "the suite count stays pinned (892)"; the pin is 1028. Out of scope, not touched.
3. **`VacuousShapeDetector.cs:78` — walks `client/tests/**/*.cs` without excluding `obj/`/`bin/`.** Harmless today, but a future generated file carrying a `[Fact]`-shaped token would make it throw at `:89-92` or emit a site keyed to an `obj/` path. Out of scope, not touched. (The new guard excludes both directories explicitly.)

## Documentation Requirements — proposed, not applied

No `client/docs/**` document was edited (SP-059 precedent, followed by SP-071/SP-072: policy-touching text is applied by the orchestrator at land). One sentence is owed, and the likely home is the SP-062/SP-068 note in `client/docs/port-workflow.md`. Proposed wording:

> Membership of `ProcessEnvCollection` is mechanically checked: `ProcessEnvCollectionGuardTests` fails with `file:line` and the class name when a `CcpClient.Tests` class reads the data-root variable without carrying `[Collection(nameof(ProcessEnvCollection))]`, and when any `CcpClient.HeadlessTests` class mutates that variable at all (collections do not span assemblies, so membership is not available there). The convention is no longer text.

---

## Honesty — what this does NOT prove

- **The guard is lexical.** A class that reaches `DefaultSettingsPath()` **transitively**, through a call it does not name, is invisible to it. Today no test does — verified in 1d, not assumed — but the guard would not notice the day one does. Building transitive call-graph analysis was explicitly out of this packet (`PROMPT.md:233-235`) and would be unreviewed machinery.
- **It binds only the tokens it enumerates.** A **new door** into the variable — a new static accessor in `client/src` that reads it — is invisible until someone adds its name to `DataRootEntryPoints`. Nothing mechanically couples that list to `client/src`.
- **Aliasing evades the qualified token rule.** `var v = CompositionRoot.DataRootOverrideVariable; Environment.SetEnvironmentVariable(v, x);` is not detected, because the argument list names neither the constant nor the raw value. This is the accepted cost of the qualified reading; the unqualified alternative has no honest remedy for its own false positive at `HarnessEntryPointGateTests.cs:63`. This particular evasion is a **token-half** evasion and does not reach the construction rule — but that is **not** a claim that the construction half is sound. Until the 2026-08-17 revise this bullet ended by asserting that the construction rule "is unaffected by it either way", which read as coverage; the construction half — the actual SP-068 failure shape — has **two blind spots of its own**, plus a third, smaller one, and they are the next three bullets.
- **The construction detector binds exactly ONE SPELLING.** `RootConstruction()` is `\bnew\s+CompositionRoot\b` (`ProcessEnvCollectionGuardTests.cs:140`). A **target-typed** construction — `CompositionRoot root = new() { ParticipantsFactory = … };`, `=> new() { … }`, `return new();` — is a DIRECT construction that reaches the same environment read and is **invisible to this guard**. That is not a contrived form; it is house style in this project. Re-measured at revise time over `client/tests` (`rg -o 'new\(\)'`, `obj/`/`bin/` excluded): **91 occurrences across 25 files**, **24** of them in `CcpClient.Tests/BarkPipelineTests.cs`, including expression-bodied factories at `IntakeDraftTests.cs:27` and `IntakeProfilerTests.cs:29`. Also re-measured: there are **zero** target-typed `CompositionRoot` constructions anywhere under `client/` today, so the guard is not currently wrong about any site — §1a's 22 sites are the complete census of the *explicit* spelling, but §1a never stated that it was blind to the other one. The consequence is that this packet's own stated trigger, "the next class that builds a real `CompositionRoot`", has a **foreseeable spelling this guard does not bind**. Full coverage needs type inference, correctly out of this packet (`PROMPT.md:233-235`); naming the limit is not, and it is now also named in the guard's own doc comment, where the next author will actually look.
- **The redirect test reads the whole initializer as text.** `redirected = text[i..initializerEnd].Contains(RedirectSeam, …)` (`ProcessEnvCollectionGuardTests.cs:344`) is true when `SettingsPathFactory` occurs **anywhere** inside the brace-matched initializer. So a nested `new CompositionRoot { SettingsPathFactory = … }` written inside an outer initializer silences the **OUTER** construction, and so does an incidental mention of the identifier inside a lambda in that initializer. Not exploitable on today's tree — every "yes" row in §1a is a top-level assignment in the construction's own initializer, and R0 shows the live violation still fires — but it is a **false negative in the construction half specifically**, which is the half no other assertion in the suite backs up.
- **Attribute membership and BOUND are keyed on the SIMPLE class name.** `carries[span.Name]` (`ProcessEnvCollectionGuardTests.cs:189`) aggregates over every declaration of a name across the project. That is deliberate — partials are real in this suite (`HarnessEntryPointGuardTests.cs:18`, `FloorWrapperGuardTests.cs:32`, both re-confirmed at revise time) and the attribute may sit on any one of them — and the cost is that a **nested** type sharing a name with a bound top-level class would lend it that class's attribute. Contrived, no user in the tree, unexercised; it is a property of the mechanism and belongs on this list rather than only in a review transcript.
- **Membership is proven by an ATTRIBUTE, not by an executed demonstration** that the classes actually serialize. The executed proof is SP-062's probe, not this guard. `[CollectionDefinition(DisableParallelization = true)]` is neither relied on nor asserted anywhere in this file; the mechanism cited is intra-collection sequentiality, exactly as `DataRootOverrideTests.cs:116-120` states.
- **The guard does not prove the race is gone**, only that the convention that mitigates it is now enforced. Nothing here demonstrates that `IntegrationProofTests` was actually flaking; it demonstrates that it *could* (the read path is real, 1e) and that it no longer reads the variable.
- **Unexercised code paths.** `[Collection<X>]` generic-form acceptance, `typeof(X)`/`"X"` argument spellings, the outermost-attribution path for a nested type, the `record`/`struct` declaration forms, the "no enclosing type" violation, the fail-closed **unrecognised-third-project** assertion (`Assert.True(strays.Count == 0, …)`, `ProcessEnvCollectionGuardTests.cs:239` — it has **no bite in the matrix**; it is green because zero `.cs` files live under `client/tests` outside the two project roots today, re-confirmed at revise time, which is a property of the tree and not a demonstration of the check), and every fail-closed unparseable-input branch have **no user in the tree today**. They are written and compiled, not executed by a bite. The reverts prove the paths that the tree exercises.
- **The sanitizer is a smaller local copy**, not the shared one. It is proven load-bearing by R5 on this corpus; it is not proven equivalent to `VacuousShapeDetector.Sanitize`, and interpolated strings with quotes inside holes are handled by brace-depth tracking that no test in the corpus stresses.
- **One machine, one runner.** Everything above was executed on Windows 11 with .NET 10 and xunit.v3 3.2.2 + xunit.runner.visualstudio 3.1.5. Nothing here is Linux evidence, and none of it needs to be — the guard reads source text and asserts nothing about a platform.

---

## Final-review REVISE, applied 2026-08-17 (from `b246054d`)

The final completion review returned REVISE on the **honesty section only**: no mechanism change, no rule change, no scope change. The over-claim it named was real and is fixed. Every claimed defect was re-verified at source before it was accepted; none was refused.

**What changed, and nothing else changed.** Two files:

1. `spine-tasks/SP-086-process-env-collection-guard/record.md` — the § Honesty aliasing bullet no longer ends by asserting that the construction rule "is unaffected by it either way" (that sentence was false as written and read as coverage of a half that has real blind spots); three new bullets own the construction half's spelling limit, the initializer-text contamination false negative, and the simple-name aggregation; the unrecognised-third-project assertion joins the "Unexercised code paths" list; and § Carried conditions round-2 note 1 no longer states the guard-side token-list revert flat, but marks it **asserted by this lane, executed by the final review as its Bite D**.
2. `client/tests/CcpClient.Tests/ProcessEnvCollectionGuardTests.cs` — the first two limits (plus the third, smaller one) are **mirrored into the file's own doc comment**, as a new `<para>` after `HONESTY`, because that is where the next author looks. This is a comment-only edit: the sanitizer blanks comments before any detection runs, so the guard's behaviour is byte-identical and no bite in the matrix above is invalidated. It adds no `new CompositionRoot` and no `SettingsPathFactory` occurrence, so the R5 (sanitizer-off) ordering is unchanged too.

**Re-measured at revise time rather than transcribed from the review.** `new()` under `client/tests`, `obj/`/`bin/` excluded: **91 occurrences across 25 files**, **24** in `BarkPipelineTests.cs` (the review said "~25"; 24 is the measured number and is what both the record and the doc comment now say). Target-typed `CompositionRoot` constructions anywhere under `client/`: **zero**. Expression-bodied target-typed factories: `IntakeDraftTests.cs:27`, `IntakeProfilerTests.cs:29`. Partial declarations backing the simple-name aggregation: `HarnessEntryPointGuardTests.cs:18`, `FloorWrapperGuardTests.cs:32`. Tracked `.cs` files under `client/tests` outside the two project roots: **zero**. Guard line citations were re-read after the doc-comment insertion and are the post-edit numbers (`:140`, `:189`, `:239`, `:344`).

**Declined, with reasons.**

- **Hoisting `Guid.NewGuid()` out of the `SettingsPathFactory` lambda at `IntegrationProofTests.cs:69-70`** (review: "NOTED, NOT REQUIRED", conditioned on "if the file is being touched anyway"). Declined. The file is not being touched for any other reason, and the non-idempotency is confirmed unreachable: `Build` calls the factory exactly once (`CompositionRoot.cs:251`) and the fact fails in `CoreServices` before `CapabilityProbes`, so no second call site exists. Making it idempotent needs a **second statement** (a captured local), which would falsify the reviewed and approved "one statement, nothing else in that file changed" claim in § Decision 2 and carried condition 6 and force three approved passages to be rewritten. That cost exceeds a latent, unreachable style mismatch. Left as filed.
- **Tightening `RootConstruction()` with an additive `\bCompositionRoot\s+\w+\s*=\s*new\s*\(`** (review: "Optional and explicitly NOT required"). Declined, for the reason the review itself gives: `=> new()` and `return new()` still need type inference, so a partial catch does not retire the honesty bullet, and it would cost a new bite row plus a re-gate for zero change on the current tree. The written-down residual is the whole remedy.
- **`HarnessEntryPointGateTests.cs:63` qualified-token reading** — carried, not re-litigated; it stays filed as owed owner ruling § "Owed / filed, not fixed" item 1.

**Re-gate after the doc-comment edit** (build first, then the wrapper, two separate commands, both through `client/tools/gate/with-slot.mjs --slots 3`):

- Build: **0 Warning(s), 0 Error(s)**.
- Pin on disk (`client/tests/floor/floor.json`, read never written): `CcpClient.Tests` **1028**, `CcpClient.HeadlessTests` **35**.
- Declared delta (`floor-delta.json`, **unchanged at 2/0** — this revise adds and removes no fact): unit **+2**, headless **+0**.
- Observed TRX `<Counters>`: `CcpClient.Tests` **total=1030** (1028 passed, 2 skipped, 0 failed), `CcpClient.HeadlessTests` **total=35** (35 passed, 0 skipped, 0 failed).
- `observed == pin + delta`: 1028 + 2 = **1030** ✓, 35 + 0 = **35** ✓. The wrapper exits non-zero reporting `FLOOR VIOLATION — total drift: 1030 result(s) (pin total 1028)`; that is the designed state for a lane, which never edits the shared pin.
- The only skips remain the two Linux-gated `allowedSkips` names. The SP-057 pin executed; `CCP_DATA_ROOT` was never set or exported.
