# SP-086: Make the ProcessEnvCollection convention mechanical

**Supersedes SP-077** (wave 31, escalated at the plan gate having written no product code; record: `spine-tasks/CONTEXT.md`, wave-31 section). Same work, new ID: the packet ID is execution state, the durable identity is the board row. Renamed rather than reissued so SP-0077-as-escalated stays exactly what the wave-31 record describes.

## Mission

SP-062 established the only isolation the suite has against a process-wide `CCP_DATA_ROOT`
mutation: co-location. The one in-suite mutator of that variable lives in
`ProcessEnvCollection`, and every test class that *reads* the variable joins the same
collection so xunit's intra-collection sequentiality serializes them. SP-068's scheduling
reshuffle then exposed the pre-existing hole and it was fixed in-lane by adding the
attribute to two more classes.

**The convention is text. Nothing checks it.** The next class that builds a real
`CompositionRoot` silently rejoins the racy default collection, and the symptom arrives as
an unrelated packet's scheduling change reddening a test it never touched. That is the same
"intent is text, not machinery" class as the `allowedSkips` permanent bans.

Your outcome: **one new mechanical guard file that fails, with `file:line` and the offending
class name, when a test class reads the data-root variable without carrying
`[Collection(nameof(ProcessEnvCollection))]`.** Shape it on the guards the suite already has:
repo-root walk, fails closed, `file:line` violations, never skips.

**The premise of this board row was verified at authoring, not transcribed.** Every citation
below was opened. The row holds, and it holds harder than it claims: the census found a
**live, currently-unguarded violation** in the tree today. It is named in Context, and it
creates a scope problem you must read before you write anything.

## Dependencies

SP-062 (the collection and the co-location mechanism), SP-068 (the in-lane fix that made the
two `CompositionRoot` classes join it). SP-066 (`VacuousShapeDetector` / `VacuousShapeGuardTests`)
is the shape precedent and also a **constraint on your facts**, see Step 3. Nothing product-side.

## Context to Read First

Verified by the orchestrator at authoring. Every line below was opened and confirmed.

**The mechanism as it exists**

- `client/tests/CcpClient.Tests/DataRootOverrideTests.cs:121-122` defines
  `ProcessEnvCollection` with `[CollectionDefinition(DisableParallelization = true)]`. The
  doc comment at `:116-120` is explicit that `DisableParallelization` is a
  **non-relied-upon hint** on this runner and that intra-collection sequentiality is the
  actual mechanism.
- `DataRootOverrideTests.cs:132, 148, 158, 194, 203, 211` are the **only**
  `Environment.SetEnvironmentVariable` calls of that variable anywhere under `client/tests/`.
  All six sit in `DataRootOverrideEnvTests` (`:124-125`), which carries the attribute.
  `:203` is the literal `"relative/not-absolute"` the row cites as the leak.
- `client/src/CcpClient.Desktop/Lifecycle/CompositionRoot.cs:108` is the read:
  `Environment.GetEnvironmentVariable(DataRootOverrideVariable)` inside
  `DefaultSettingsPath()`. `:59` is the variable name. `:85`,
  `public Func<string> SettingsPathFactory { get; init; } = DefaultSettingsPath;`, is why a
  construction that does not override the factory reads the process environment.
- `CompositionRoot.cs:251`, `var dataDirectory = Path.GetDirectoryName(SettingsPathFactory())!;`
  in `Build()`, is **unconditional**. It runs whatever `ParticipantsFactory` is set to. This
  is the line that makes the live violation below real.

**The four classes that carry the attribute today** (the complete set; no other file in
`client/tests/` contains the token):

- `client/tests/CcpClient.Tests/DataRootOverrideTests.cs:28` (`DataRootOverrideTests`, the pin/reader)
- `client/tests/CcpClient.Tests/DataRootOverrideTests.cs:124` (`DataRootOverrideEnvTests`, the mutator)
- `client/tests/CcpClient.Tests/CompositionRootTests.cs:10` (SP-068 in-lane fix)
- `client/tests/CcpClient.Tests/CompositionRootValidationTests.cs:13` (SP-068 in-lane fix)

**The only enforcement that exists, and exactly how narrow it is**

- `client/tests/CcpClient.Tests/DataRootChokePointGuardTests.cs:64-70` is a reflection
  assertion, hard-coded to `typeof(DataRootOverrideTests)`. It binds **one named class**.
  It does not bind `DataRootOverrideEnvTests` (the mutator), it does not bind either
  `CompositionRoot` class, and it cannot bind a class that does not exist yet. The row's
  claim is therefore **TRUE as filed**: nothing enforces the general convention.

**The live violation the census found**

- `client/tests/CcpClient.Tests/IntegrationProofTests.cs:14` declares
  `public class IntegrationProofTests` with **no** `[Collection]` attribute.
- Its first fact constructs `new CompositionRoot { SettingsPathFactory = ... }`
  (`:21-26`), which does not read the variable. Fine.
- Its second fact constructs `new CompositionRoot { ParticipantsFactory = infra => {...} }`
  (`:67-74`) with **no** `SettingsPathFactory`. That root reaches `root.Build(trace)` through
  `Program.CreateStartupPhases` (`client/src/CcpClient.Desktop/Program.cs:279-289`), and
  `Build` hits `CompositionRoot.cs:251`, which calls the **default** factory, which is
  `DefaultSettingsPath()`, which reads `CCP_DATA_ROOT` at `CompositionRoot.cs:108`.
- So `IntegrationProofTests` is a real unprotected reader, racing the mutator in
  `DataRootOverrideEnvTests`, with the same failure shape SP-068 probe-proved
  (`"relative/not-absolute"` leaking in and `DefaultSettingsPath()` throwing
  `DataRootOverrideException` mid-phase). **A correctly written guard reds on the current tree
  naming this class.** Read the SCOPE PROBLEM section before you decide what to do about it.

**Sanitization is load-bearing, proven by four sites**

A naive token scan over `client/tests/**/*.cs` produces false positives, because these tokens
appear inside string literals and doc comments in *other guards*:

- `client/tests/CcpClient.Tests/HarnessEntryPointGuardTests.cs:74` contains the string literal
  `"new CompositionRoot"`. An unsanitized scan reports `HarnessEntryPointGuardTests` as a
  violating class, and there is no honest fix for that report.
- `client/tests/CcpClient.Tests/HarnessEntryPointGuardTests.cs:72` contains the literal
  `"CompositionRoot.ActiveDataRootOverride()"`.
- `client/tests/CcpClient.Tests/DataRootChokePointGuardTests.cs:49` contains the literal
  `"...CompositionRoot.DefaultSettingsPath() (SP-057)"`.
- `client/tests/CcpClient.Tests/VacuousShapeDetector.cs:220` contains the literal
  `"Environment.GetEnvironmentVariable"`.
- `client/tests/CcpClient.Tests/HarnessEntryPointGateTests.cs:10` names
  `CompositionRoot.ActiveDataRootOverride` inside a `<see cref>` doc comment.

Read `client/tests/CcpClient.Tests/VacuousShapeDetector.cs:341-471` (`Sanitize`) for the
shape of a literal/comment blanker that preserves offsets and newlines. **You may not edit
that file** (see File Scope), so you write your own, smaller one.

**The precedent guards, for shape and message style**

- `client/tests/CcpClient.Tests/DataRootChokePointGuardTests.cs` (repo-root walk, `file:line`
  violation list, `FindRepoRoot` that throws rather than skips, `:73-89`).
- `client/tests/CcpClient.Tests/HarnessEntryPointGuardTests.cs` (same walk over `client/src`,
  fail-closed on a missing tree at `:30`).
- `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs:78-111` (fail closed on an input the
  guard cannot parse, rather than skipping it) and `:156-157` (violation aggregation).
- `client/tests/CcpClient.Tests/VacuousShapeGuardTests.cs:47`,
  `Assert.NotEmpty(entries); // an empty ledger on this suite is a broken ledger, not a clean sweep`.
  That is the anti-vacuity idiom your guard needs, see Step 3.

**Two facts about the tree the walk must respect**

- `client/tests/**` contains generated `.cs` under `obj/` (for example
  `client/tests/CcpClient.Tests/obj/Debug/net10.0/CcpClient.Tests.AssemblyInfo.cs`). Exclude
  `obj/` and `bin/` explicitly.
- `ProcessEnvCollection` is defined in the `CcpClient.Tests` assembly. xunit collections do
  not span assemblies, so a class in `client/tests/CcpClient.HeadlessTests` **cannot** join it.
  Verified today: that project has zero `SetEnvironmentVariable` calls and all five of its
  `new CompositionRoot` sites pass an explicit `SettingsPathFactory`
  (`AvatarTubeHeadlessTests.cs:31`, `CompanionWindowHeadlessTests.cs:26`,
  `DashboardCardHeadlessTests.cs:27`, `FeaturePopupHeadlessTests.cs:28`,
  `QuickToggleDispatchHeadlessTests.cs:27`).

## File Scope

| | |
|---|---|
| May change | `client/tests/CcpClient.Tests/ProcessEnvCollectionGuardTests.cs` (new), `client/tests/CcpClient.Tests/IntegrationProofTests.cs` (**scope amendment 2026-08-15 — see SCOPE, RESOLVED**), `spine-tasks/SP-086-process-env-collection-guard/**` |
| Must not change | everything else, and specifically `client/tests/floor/floor.json`, `client/tests/floor/vacuous-shape-ledger.json`, `client/tests/CcpClient.Tests/VacuousShapeDetector.cs`, `client/tests/CcpClient.Tests/DataRootChokePointGuardTests.cs`, and every file named in the contract below |

`client/tests/floor/vacuous-shape-ledger.json` being out of scope is a **design constraint on
your facts**, not a footnote. See Step 3.

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-086-process-env-collection-guard/floor-delta.json` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/ProcessEnvCollectionGuardTests.cs`, `client/tests/CcpClient.Tests/IntegrationProofTests.cs` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `ConditioningControlPanel/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-086-process-env-collection-guard/record.md`, `spine-tasks/SP-086-process-env-collection-guard/floor-delta.json` |

**You do not edit `client/tests/floor/floor.json`.** That file is the shared pin and concurrent
lanes collide on it. Write your count change into `floor-delta.json` in your own folder:

```json
{ "packet": "SP-086-process-env-collection-guard", "unit": 2, "headless": 0, "reason": "one line naming the facts you added" }
```

Declare your **real** counts, not the illustrative `2` above. Declaring `0`/`0` is required if
you add no tests; omitting the file is not the same as declaring zero. The land sums every
packet's delta and applies one bump.
`client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs:161-242` enforces both halves and will
red your run if the `floorDelta` row or the `fileScopeMustNotChange` disclaimer is missing.

## SCOPE, RESOLVED 2026-08-15 — BRANCH A. Read this before Step 1.

**This section used to present two branches, and its honest Branch-B outcome was BLOCKED. The orchestrator has amended the scope in writing, so Branch A is now the standing instruction and Branch B is withdrawn.**

The guard, written honestly, reds on the current tree with exactly one violation:
`client/tests/CcpClient.Tests/IntegrationProofTests.cs:14` (the class) for the construction at
`:67`. The remedy is one line in that file. **That file is now in your `fileScopeMustChange`.**

Apply the minimal remedy per Decision 2 below, land the guard green, and record in `record.md`
both the amendment and the exact one-line diff you applied.

**Why this was amended rather than left to you (wave 31 evidence):** this packet was launched
once with the scope unchanged, and its designed-correct outcome was an escalation — the wave
spent a lane to be told a one-line fact the orchestrator already knew at authoring. Pre-deciding
a decision rule is good practice here; shipping a packet whose intended outcome is BLOCKED is
not. The disjointness that motivated the original split still holds: `IntegrationProofTests.cs`
is claimed by no other packet in this wave, and `validate-wave.mjs` re-checks that before launch.

**Still forbidden, exactly as before, and this is the part that matters:** do not add a
suppression or allow list of class names; do not grandfather `IntegrationProofTests` by an ID
rule; do not narrow the detection rule so the one violation it found stops being a violation.
Those are all the same move and all of them destroy the packet. The amendment gives you the one
line needed to fix the VIOLATION, never a licence to soften the RULE.

## Review Level: 2 (Plan, Final)

Level 2 and not 3, stated so it is a decision and not an omission: this packet adds **no
product code**, changes **no runtime concurrency mechanism**, and touches **no user-visible or
privacy-bearing path**. It adds one test file that reads other files. The concurrency it
concerns is xunit's test scheduling, and the packet changes none of it. A Level 3 gate would
be ceremony with no design decision behind it.

**One consult trigger overrides the level:** if you land in SCOPE PROBLEM Branch B, or if your
Step 1 census contradicts the orchestrator's census in any way, take the advisory gate before
writing the guard body, with your census attached.

## Steps

### Step 1: Re-derive the census. Do not transcribe the one above.

The Context section is the orchestrator's census. **Re-run it yourself** and say plainly
whether it was right. Produce, as a table in `record.md`:

1. Every `new CompositionRoot` site under `client/tests/` (both projects, `obj/` and `bin/`
   excluded), its `file:line`, whether its object initializer sets `SettingsPathFactory`, its
   enclosing class, and whether that class carries `[Collection(nameof(ProcessEnvCollection))]`.
2. Every direct naming of a data-root token in test source, outside string literals and
   comments: `CompositionRoot.DefaultSettingsPath`, `CompositionRoot.ActiveDataRootOverride`,
   `CompositionRoot.DataRootOverrideVariable`, `DtrhProfileLock.DtrhDataRoot`,
   `DtrhProfileLock.WebView2ProfileDir`, `ChaosTunnelService.DataRoot`, and
   `Environment.GetEnvironmentVariable` / `SetEnvironmentVariable` against that variable.
3. The complete set of classes carrying the attribute today.

**Decision 1, pre-authorized both ways.**

- **If your census matches the orchestrator's** (the classes named above, and
  `IntegrationProofTests` the sole unprotected reader), implement the rule exactly as
  specified in Step 2.
- **If your census finds a construction that reads the variable through a path this packet
  did not model** (for example a capability probe or a participant that reaches
  `DefaultSettingsPath()` transitively despite an overridden `SettingsPathFactory`), the
  lexical rule **stays as written**, and the newly found path is named in `record.md` as a
  known limitation of a lexical guard, with a follow-up row owed at land. **Do not widen this
  packet into transitive call-graph analysis.** That is a different mechanism, it is not
  lexically decidable, and building it here would be unreviewed machinery.

Checked at authoring so you do not have to rediscover it: `Program.CreateStartupPhases`
(`Program.cs:272-307`) does **not** read the variable; only `Program.Main` does
(`Program.cs:93`). `DtrhCapabilityProbes` and `ChaosTunnelCapabilityProbes` do **not** read it.
The `atomic-filesystem` probe uses the directory derived from `SettingsPathFactory()`
(`CompositionRoot.cs:251-255`). So an overridden factory is, today, sufficient to avoid the read.
Confirm that; do not assume it.

### Step 2: The rule, and where the mechanism has to live

**Testability constraint, stated at authoring rather than discovered in review. This project
has hit this class three times (SP-067, SP-070, and the class SP-072 designed out).**

The mechanism **must be a lexical source walk from the repo root**, in the shape of
`DataRootChokePointGuardTests` / `HarnessEntryPointGuardTests`. It may **not** be reflection
over the test assembly, for two independent reasons, either of which alone is fatal:

1. `[Collection]` is visible to reflection but "this class constructs a `CompositionRoot`" is a
   property of a **method body**, which reflection cannot see without decoding IL. A reflection
   guard can therefore only re-implement the hard-coded single-class assertion that already
   exists at `DataRootChokePointGuardTests.cs:64-70`, which is the thing this row says is not
   enough.
2. `CcpClient.HeadlessTests` is a separate test assembly that `CcpClient.Tests` does not
   reference. Its types are not loadable from your guard. Only a source walk sees both trees.

**The rule.** A class under `client/tests/CcpClient.Tests/` is **BOUND** when, after blanking
comments and string/char literals, its body contains either:

- a `new CompositionRoot` construction whose object initializer does **not** assign
  `SettingsPathFactory` (including the no-initializer form `new CompositionRoot()`), or
- any of the direct data-root tokens listed in Step 1 item 2.

A BOUND class must carry `[Collection(nameof(ProcessEnvCollection))]`. Every class that does
not is a violation reported as `path:line: ClassName ...` with a message that names the
mechanism, the variable, and why membership is the fix.

**The assembly-boundary half.** Classes under `client/tests/CcpClient.HeadlessTests/` cannot
join a collection defined in the other assembly, so the membership rule does not apply there.
Bind that project with the only rule that is meaningful for it instead: **no mutation of the
data-root variable at all**, because there is no collection to serialize it against. Verified
green today (zero mutators). **Decision 3, pre-authorized:** if that fact ever reds, the answer
is **not** to define a second `ProcessEnvCollection` in the headless assembly inside this
packet. It fails closed and the remedy is a filed row.

**Decision 2, pre-authorized both ways, for how a violating class is remedied when its file is
in scope.**

- **If the offending fact's subject is not the data root** (it merely needs somewhere to
  persist), remove the read: give the construction an explicit `SettingsPathFactory` pointing
  at a per-test temp path. This eliminates the race rather than serializing around it, and it
  keeps the class out of the serialized collection so the suite stays parallel. This is what
  every other real-root test in the suite already does.
- **If the offending fact's subject IS the default path or the override itself**, it must join
  `ProcessEnvCollection`. Serializing is the only correct answer there.

`IntegrationProofTests.cs:67` falls in the first branch: its subject is startup failure and
teardown, and its sibling fact at `:21` already sets `SettingsPathFactory`. If Branch A of the
SCOPE PROBLEM is live, that is the remedy, one line, nothing else in that file.

**Fail-closed requirements, all of them non-optional:**

- Repo root unresolvable, or `client/tests` missing: throw. Never skip.
- A `[Collection(...)]` attribute whose argument does not parse: violation, not a silent pass.
- A `new CompositionRoot` whose initializer cannot be brace-matched: violation, not a skip.
  (`FloorWrapperGuardTests.cs:92-101` is the precedent for "unparseable input is a violation".)
- The detected BOUND set being empty is a **broken detector, not a clean tree**: assert it is
  non-empty, and assert by name that at least one known-bound class
  (`CompositionRootValidationTests` is the unambiguous one; it is the class SP-068 fixed) is in
  it. Without this the guard passes forever the moment a regex breaks.

### Step 3: Write the facts so the ledger never needs editing

`client/tests/floor/vacuous-shape-ledger.json` is **out of File Scope**, and
`VacuousShapeGuardTests.EverySilencingShapeSite_IsDispositionedInTheLedger` reds on any
detected site with no ledger entry. Every existing guard fact in this suite trips the detector
(they all call `Directory.Exists(`) and every one of them is ledgered. **Yours must trip
nothing.** Concretely, in each `[Fact]` body:

- **No** `File.Exists(` or `Directory.Exists(` (`fs-predicate`). Put the tree-existence check
  in a private helper that throws `InvalidOperationException`; the detector only analyses
  `[Fact]`/`[Theory]` bodies, and `FindRepoRoot` already works this way at
  `DataRootChokePointGuardTests.cs:73-89`.
- **At least one `Assert.` at guarding depth 0** in every fact body, so neither `no-assertion`
  nor `assertions-all-nested` fires. The trailing
  `Assert.True(violations.Count == 0, ...)` gives you this for free; do not bury it in an `if`.
- **No bare `return;`** anywhere before the first assertion (`early-return`). `continue;` inside
  the walk is fine.
- **No** `Assert.Skip`, `Assert.SkipWhen`, `Assert.SkipUnless` (`dynamic-skip`). This guard
  never skips anyway.
- **No** `OperatingSystem.Is*` or `RuntimeInformation.IsOSPlatform` (`platform-predicate`).
- **No** `Environment.GetEnvironmentVariable` token in a fact body (`env-predicate`). Keep your
  detection tokens as class-level `private const string` fields. They are string literals, so
  your own sanitizer blanks them and your guard does not report itself, which is the other
  reason to keep them out of fact bodies.

Confirm this by running the suite: if `VacuousShapeGuardTests` reds, your facts have shapes and
the fix is to reshape the fact, never to edit the ledger.

### Step 4: Prove every fact bites, one source at a time

A guard that reports nothing on a green tree is indistinguishable from a guard whose detector is
broken. The bite matrix is the **only** thing that proves the detector reaches the mechanism.
Run each revert **independently**, one at a time, restoring the tree **byte-identically**
between reverts, and record the red count and the exact violation text per revert:

- **R1** remove the attribute at `CompositionRootValidationTests.cs:13`. Fact 1 must red naming
  that class with `file:line`.
- **R2** remove the attribute at `CompositionRootTests.cs:10`. Same.
- **R3** remove the attribute at `DataRootOverrideTests.cs:124` (`DataRootOverrideEnvTests`, the
  **mutator**). Same. **R3 is the revert that proves the new guard covers ground the old one does
  not:** `DataRootChokePointGuardTests.cs:64-70` hard-codes `DataRootOverrideTests` and would
  stay green through R3. State that comparison explicitly in `record.md`.
- **R4** add one `Environment.SetEnvironmentVariable(CompositionRoot.DataRootOverrideVariable, ...)`
  line to any file under `client/tests/CcpClient.HeadlessTests/`. Fact 2 must red naming it.
- **R5** disable your own sanitizer (one line, in your own file, in scope). The guard must then
  report the string-literal false positive at `HarnessEntryPointGuardTests.cs:74`. This proves
  the sanitizer is load-bearing rather than decorative.
- **Positive control, tree restored:** the BOUND set is non-empty, contains the three real
  `CompositionRoot`-reading classes, and does **not** contain `HarnessEntryPointGuardTests`.

R1 through R4 touch files outside your File Scope. That is allowed **only** as transient
evidence: never committed, restored byte-identically, and proven restored with
`git status --porcelain` showing nothing but your in-scope files before the final gate run.
Paste that output into `record.md`.

### Step 5: Record

`record.md` carries: the Step 1 census table; which branch of Decision 1 your evidence selected
and why; the Decision 2 verdict for every violation found; which SCOPE PROBLEM branch you are in
and the evidence for it; the R1 to R5 bite matrix with red counts and violation text; the clean
`git status` proof; and an honesty section naming what is **not** proven. Name at least these
limits, because they are real: the guard is **lexical**, so a class that reaches
`DefaultSettingsPath()` transitively through a call it does not name is invisible to it; the
guard binds the tokens it enumerates and a new door into the variable is invisible until someone
adds it; and membership in the collection is proven by an attribute, not by an executed
demonstration that the two classes actually serialize.

`floor-delta.json` with your real counts.

### Step 6: Verification

Run these as **separate** commands. The worktree isolation guard refuses compound shell
commands (`cd X && ...`), so chain nothing.

```
dotnet build client/CcpClient.sln -c Debug --nologo
```
```
node client/tests/floor/check-floor.mjs
```

**Build immediately before the gate, every time.** The wrapper runs `dotnet test --no-build`
(`client/tests/floor/check-floor.mjs:251-253`); a stale `bin/` once reported 1022 against a tree
containing 1018.

Your floor run will report a total that does **not** match the pin, because the pin is bumped at
land from the summed deltas and not by you. That is expected and is not a failure of your work.
READ THE PIN FROM THE FILE, never from this packet: it has already gone stale twice (it said 1018; wave 30 made it 1022 and wave 31 made it 1028). Open `client/tests/floor/floor.json` and use what is there.
Confirm that `observed == pin + your declared delta`, and state both numbers in your report.

## Completion Criteria

- The census is complete, re-derived rather than transcribed, and its Decision 1 branch is
  stated with evidence.
- The guard exists as the single new file, is a repo-root lexical walk, never skips, fails
  closed on unparseable input, and reports `file:line` plus the offending class name.
- Both halves are bound: the membership rule for `CcpClient.Tests`, the no-mutator rule for
  `CcpClient.HeadlessTests`, with the assembly-boundary reason stated in the file's doc comment.
- The BOUND set is asserted non-empty with a named positive control.
- Every fact bites under its own independent revert; R3 and R5 are present and recorded.
- No new entry is owed in `client/tests/floor/vacuous-shape-ledger.json` (your facts carry zero
  detector shapes) and `VacuousShapeGuardTests` is green.
- `record.md` and `floor-delta.json` exist and are accurate.
- Build 0W/0E.
- Either the suite is green (Branch A), or the packet reports BLOCKED with the RED captured and
  the one-line amendment named (Branch B). Nothing in between.

## Do NOT

- **Add `[assembly: CollectionBehavior(DisableTestParallelization = true)]` or
  `CollectionPerAssembly` to your new file.** It is technically inside your File Scope, it would
  make the race impossible, and it is the wrong answer: it serializes the entire suite at a large
  runtime cost, it replaces a checkable convention with a global switch, and it makes the guard
  you were asked to write pointless.
- **Narrow the rule to the empty-initializer form `new CompositionRoot()`.** The one live
  violation is `new CompositionRoot { ParticipantsFactory = ... }`, so that narrowing exists only
  to make the guard stop reporting the thing it found.
- **Add a suppression list, an allow list, or an ID-based grandfather rule for a class.** The row
  exists because intent as text does not bind; a suppression list is the text again.
- **Use reflection over `Assembly.GetTypes()` as the mechanism.** It cannot see method bodies and
  cannot see the headless assembly.
- **Edit `client/tests/CcpClient.Tests/VacuousShapeDetector.cs` to expose its sanitizer.** Out of
  scope, and it couples two independent guards. Write a local one.
- **Edit `client/tests/floor/vacuous-shape-ledger.json`.** Out of scope. Reshape the fact instead.
- **Delete or weaken the assertion at `DataRootChokePointGuardTests.cs:64-70` as "now
  redundant".** Out of scope, it is a different fact with a different message, and removing it
  would move another guard's count.
- **Export or set `CCP_DATA_ROOT` anywhere**, including "just for the bite matrix". It makes the
  SP-057 pin skip and the exact-count floor goes blind (`client/docs/port-workflow.md:190`;
  `floor.json` names it a permanent `allowedSkips` ban).
- **Edit `client/tests/floor/floor.json`.** Declare `floor-delta.json` instead.
- **Add a wall-clock wait.** `client/tests/CcpClient.Tests/TestWait.cs` is the only approved
  helper; `Thread.Sleep`, bare `Task.Delay`, and `DateTime` / `Environment.TickCount64` polls red
  `TestTimingGuardTests`. A file walk needs none of them.
- **Close, edit, or claim any board row**, including this one. Rows are landed by the
  orchestrator.
- **Leave a TODO, a placeholder, a commented-out rule, or a partially wired detector.**

## Git Commit Convention

Conventional commits, `test(SP-086): ...`. One coherent slice, no unrelated files. Leave the tree
buildable at every commit. Commit your own work on your branch; do not merge, do not land, and do
not touch the shared pin.

## Documentation Requirements

If your work changes a fact stated in a `client/docs/**` document, say so in `record.md` and quote
the wording you believe is owed. **Do not edit any document yourself**; policy-touching text is
applied by the orchestrator at land (SP-059 precedent, followed by SP-071 and SP-072). The likely
candidate is a sentence recording that collection membership is now mechanically checked rather
than conventional; propose the wording, do not apply it.
