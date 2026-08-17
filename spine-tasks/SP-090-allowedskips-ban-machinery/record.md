# SP-090 — record

**Lane** `lane/SP-090-allowedskips-ban-machinery`, worktree `.claude/worktrees/SP-090`, base
`feat/crossplatform` @ `7413edaf`. One product file added:
`client/tests/CcpClient.Tests/AllowedSkipsBanGuardTests.cs`. `client/tests/floor/floor.json` was
mutated twice as step-3 evidence and restored byte-identically both times; it is unchanged in the
commit.

---

## 1. Census — the three facts the packet's step 1 demands, re-derived here

### 1a. Current `allowedSkips` membership

`client/tests/floor/floor.json:5-13` holds **7** fully-qualified names under
`projects.CcpClient.Tests.allowedSkips`:

| # | name |
|---|---|
| 1 | `CcpClient.Tests.ChaosTunnelCapabilityTests.Windows_DelegatesToTheSameEngineLoadAsDtrh` |
| 2 | `CcpClient.Tests.ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps` |
| 3 | `CcpClient.Tests.SecretStoreTests.WindowsDpapi_RoundTrip_AndFileNeverContainsPlaintext` |
| 4 | `CcpClient.Tests.SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked` |
| 5 | `CcpClient.Tests.SecretStoreTests.SettingsDocument_CarriesSecretNames_NeverValues` |
| 6 | `CcpClient.Tests.AiAwarenessTests.TitleProbe_PlatformTypedState_WindowsAvailable_LinuxUnavailable` |
| 7 | `CcpClient.Tests.AiAwarenessTests.TitleObservation_GatedByConsentAndCapability_TitleNeverLogged` |

`allowedSkipsMachineClasses` (`:14-22`) holds exactly those same 7 keys. `CcpClient.HeadlessTests`
(`:24-27`) has `allowedSkips: []` and **no** `allowedSkipsMachineClasses` key at all — the map is
present only where the list is non-empty, so an absent map with an empty list is a legal state
today. **Neither banned name is present**, read directly from the array and confirmed by the live
gate: the run in this lane reports 2 skips, `ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`
and `SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`, both Linux-gated, both listed, neither
banned.

### 1b. `check-floor.mjs:237` is a message, not a check — confirmed, and it is weaker than stated

* `check-floor.mjs:230-241` is the only place `allowedSkips` is consulted at skip time, and it asks
  one question only: is this OBSERVED skip permitted by the list (`skipNameAllowed`, `:68-74`)? It
  never inspects the list for content.
* `readPin` (`:41-64`) validates SHAPE only — `projects` object, each entry
  `{total: int, allowedSkips: string[]}`. It never reads `admissionRule` at all.
* The ban sentence is inside the `fail(...)` message at `:236-238`:
  `(machine/OS property, never a quarantine; the SP-057 pin and the named privacy flake are permanently banned)`.

**Sharper than the packet states, and measured here:** `grep -n "DataRootOverrideTests\|ChaosTunnelLoopbackTests" client/tests/floor/check-floor.mjs`
returns **nothing** (exit 1). The wrapper does not contain either banned test name; it names them
only by description ("the SP-057 pin", "the named privacy flake"). So even a human reading that
failure message cannot check the list without going back to `floor.json:30`. The declaration and
the enforcement did not merely lack a link — they did not share a vocabulary. Filed as finding D.

### 1c. No test policed the list — confirmed, and there was no fourth file

`grep allowedSkips client/tests/CcpClient.Tests` before this change returned exactly three files,
every hit a comment, every one of them in a test whose own name is pinned:
`AiAwarenessTests.cs:404`, `:460`; `ChaosTunnelCapabilityTests.cs:30`, `:44`;
`SecretStoreTests.cs:21`, `:67`, `:121`. **No fourth appeared.** `FloorWrapperGuardTests.cs:47`
holds `client/tests/floor/floor.json` as a string constant, but only to require that packets
DISCLAIM the pin in `fileScopeMustNotChange` (`:224-231`); it never opens the file. Nothing in
either test project parsed `floor.json`. After this change the grep returns four files, and
`AllowedSkipsBanGuardTests.cs` is the first that reads the list in order to police it.

---

## 2. Step 4 — should the guard also require an `allowedSkipsMachineClasses` entry for every `allowedSkips` name?

**Declined, on the file's own text.**

* `floor.json:30` requires that "the **listing commit** names the machine class where it DOES
  execute", and `:31` repeats it: "Adding a name to allowedSkips requires the admission rule above,
  **in the same commit**, naming the machine class." Neither rule mentions
  `allowedSkipsMachineClasses`. A guard making that map mandatory would enforce a **stricter rule
  than the one written**, which is a rule change, and rule changes belong to the owner and the
  board, not a lane.
* The schema already contradicts a mandatory reading: `CcpClient.HeadlessTests` (`:24-27`) carries
  `allowedSkips: []` with no map key. Absence is legal today, and needing a special case for it is
  a tell that the invariant is not the one the file holds.
* It has zero bearing on the bans. Both banned names must be ABSENT from `allowedSkips`, so neither
  can ever have a machine class.
* It would broaden the guard's subject from "the two permanent bans" (the board row this packet
  serves) to "the admission rule's bookkeeping" (a different row).

Filed instead as out-of-scope finding A below.

---

## 3. What landed

`client/tests/CcpClient.Tests/AllowedSkipsBanGuardTests.cs`, class `AllowedSkipsBanGuardTests`,
four facts. The two banned names are `const` literals in the test; `floor.json` is opened read-only
and never written.

| # | fact | subject |
|---|---|---|
| F1 | `TheCommittedPin_ListsNeitherPermanentlyBannedTest_InAnyProjectsAllowedSkips` | live membership over the real pin, filtered by `MembershipFactSees` |
| F2 | `TheCommittedPin_AdmissionRule_StillDeclaresBothPermanentBans` | live declaration over the real pin, filtered by `DeclarationFactSees` |
| F3 | `BothBannedNames_StillResolveToARealFactInThisAssembly` | anti-rename rot, by reflection, plus a named negative control |
| F4 | `TheChecker_RefusesBothDriftDirections_AndAnUnreadablePin_AndPassesTheUnmutatedControl` | four seeded documents driven through the same entry point and the same two filter delegates |

Three typed violation classes (`ViolationKind.Structural / Membership / Declaration` on a
`Violation` record — the house idiom at `UpstreamPayloadInventoryTests.cs:71-77`) and two named
predicates. `Structural` is admitted by **both** live filters, so a pin that cannot be parsed reds
F1 **and** F2 rather than neither, and every unreadable shape is constructed at the single
`Structural(...)` site, which is what makes one seeded unparseable document sufficient to pin the
routing for the whole class.

F3 exists because the packet's delete/add trap has a third door it does not name: **rename**.
Rename `DefaultSettingsPath_EnvUnset_IsThePlatformDefault` and F1 still passes (the old name is
still absent), F2 still passes (`admissionRule` still carries the old text), and the NEW name is
then free to enter `allowedSkips`. The ban evaporates with no red anywhere.

### Why the redundancy is the mechanism, not duplication to be factored away

The completion criterion asks for this explicitly. The two names are held **four** times, by four
different owners, and each pair is a genuine cross-check rather than a copy:

1. `floor.json`'s `admissionRule` — the declaration. **Redundant with the constants**: delete a
   name there and F2 reds (proved, mutation (b) below).
2. The `const` literals in the guard — the rule. **Redundant with the declaration**: they are what
   makes the delete-then-list dodge impossible, because after the delete there is still something
   to compare against.
3. The seeded fixtures, which spell both names **verbatim and independently of the constants**.
   **Redundant with the constants**: edit a constant and the fixtures no longer agree, so F4 reds
   alongside F2 and F3 (proved, revert R9 below: 3 facts red). A guard whose fixture is generated
   from the rule it tests proves nothing.
4. The suite itself, reached by reflection in F3. **Redundant with all three**: rename the test out
   from under the other three and F3 reds.

Factor any two of these together and you restore the single point of edit this packet exists to
remove. That is the whole argument: the value is not in the copies, it is in the fact that no
single edit can move them all.

---

## 4. Step 3 — proof it bites, one mutation at a time, on the REAL pin

`client/tests/floor/floor.json` baseline: **SHA-256
`2CD185F90706EFEF1DD1E2E3B574AB4E9D7241FB857F500C6EAF9BCA9A3C39EA`, 13750 bytes** (CRLF, no BOM).
A byte copy was taken before the first mutation; every restore is a copy back from it, never a
re-edit. The two mutations never coexisted, and neither was committed.

**Mutation (a) — a banned name enters `allowedSkips`.** Inserted
`CcpClient.Tests.DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault` into
`projects.CcpClient.Tests.allowedSkips`. Result: **F1 red, 3 passed**, naming the offender:

```
PERMANENTLY BANNED test name(s) present in allowedSkips (or client/tests/floor/floor.json unreadable). ...
client/tests/floor/floor.json: projects.CcpClient.Tests.allowedSkips[1] lists the PERMANENTLY BANNED test
CcpClient.Tests.DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault
```

Restored; re-hash **`2CD185F9...39EA`**, 13750 bytes, `git status --porcelain client/tests/floor/floor.json`
empty.

**Mutation (b) — the declaration is quietly trimmed.** Replaced
`CcpClient.Tests.ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery` inside
`admissionRule` with a different name (the file's only occurrence of it; occurrence count asserted
== 1 before mutating). Result: **F2 red, 3 passed**, naming the ban that went missing:

```
the admissionRule in client/tests/floor/floor.json no longer names a PERMANENT BAN (or the pin is unreadable). ...
client/tests/floor/floor.json: the admissionRule no longer names the PERMANENTLY BANNED test
CcpClient.Tests.ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery
```

Restored; re-hash **`2CD185F9...39EA`**, 13750 bytes, `git status --porcelain` shows only the new
test file.

---

## 5. Revert matrix — executed for real, one mechanism source at a time

Guard-file baseline: **SHA-256 `C567FD79B178BCD4FBD5BE13C6FFFBE669B06938800E1721F1F3C68D99934740`**.
Each row: apply the revert, rebuild `CcpClient.Tests`, run
`--filter "FullyQualifiedName~AllowedSkipsBanGuardTests"`, record which of the four facts go RED,
then restore from the byte copy and re-verify the baseline hash before the next row. The hash was
re-verified after **every** restore; all nine matched.

| # | mechanism reverted | facts RED | which |
|---|---|---|---|
| R1 | membership rule deleted (ban list emptied at the comparison site) | **1** | F4 |
| R2 | `MembershipFactSees` narrowed to its own kind (structural routing dropped) | **1** | F4 |
| R3 | `admissionRule` containment rule deleted (ban list emptied at the comparison site) | **1** | F4 |
| R4 | `DeclarationFactSees` narrowed to its own kind (structural routing dropped) | **1** | F4 |
| R5 | parse failure swallowed — `Structural`'s output discarded, `[]` returned | **1** | F4 |
| R6 | membership rule made unconditionally true (every listed name reported) | **2** | F1, F4 |
| R7 | reflection resolver stubbed **TRUE** (negative-control direction) | **1** | F3 |
| R8 | reflection resolver stubbed **FALSE** (anti-rename direction) | **1** | F3 |
| R9 | one ban CONSTANT mutated by one character | **3** | F2, F3, F4 |

**Reading the matrix honestly.** R1-R5 each red exactly ONE fact, and in every case it is F4, not
the live fact whose rule was removed. That is not a weakness of the matrix, it is the finding the
matrix exists to surface and the reason F4 was designed: **on a clean tree F1 and F2 are
individually revert-survivable.** Delete the membership rule and F1 still passes, because the real
pin genuinely contains no banned name; drop the structural routing and both live facts still pass,
because the real pin genuinely parses. F4 is what converts F1 and F2 from assertions-that-happen-to-hold
into facts: it drives the identical entry point and the identical filter delegates over documents
that are NOT clean, so every one of those five reverts produces a green-to-red transition on every
run, on every platform, with no file mutation and no manual step.

R6 is the complementary direction — a rule that fires unconditionally reds the seeded CONTROL and
the live pin together, which is what stops "always report a violation" from being a way to pass F4.
R7/R8 pin both directions of the reflection resolver, and R7 in particular is the reason F3 carries
a named negative control: without it a resolver stubbed true would leave F3 green forever. R9 is
the cross-owner check described in §3: mutating a constant reds three facts precisely because the
seeded fixtures spell the names independently.

**Process note, recorded because it is exactly the stale-`bin/` class the lane rules warn about.**
The harness restored the guard file with `fs.copyFileSync`, which on Windows preserves the source
file's last-write time. The restored file therefore looked OLDER than the last compile, MSBuild
skipped the recompile, and the first post-matrix gate run reported 3 failures that reflected the
R9 revert rather than the committed source. The source hash was already correct; the fix was to
refresh the mtime and rebuild. Content hash before and after that touch:
`C567FD79...4740` both times. This did not affect the matrix rows themselves — each row is written
with `fs.writeFileSync`, which does set the mtime to now, so each row's build did pick up its own
revert (visibly so: the rows produce three different red sets).

---

## 6. Gate

Build immediately before the gate, both through the slot semaphore, in this worktree:

```
node client/tools/gate/with-slot.mjs --slots 3 -- dotnet build client/CcpClient.sln -c Debug --nologo
  Build succeeded. 0 Warning(s) 0 Error(s)

node client/tools/gate/with-slot.mjs --slots 3 -- node client/tests/floor/check-floor.mjs
  FLOOR CHECK FAILED (SP-065):
    CcpClient.Tests: FLOOR VIOLATION - total drift: 1052 result(s) (pin total 1048).
  Passed! - Failed: 0, Passed: 1050, Skipped: 2, Total: 1052
```

**This is the designed state for a bound lane.** The lane never edits the shared pin; it declares
`floor-delta.json` and the land sums it.

| project | pin | declared delta | expected | observed | agrees |
|---|---|---|---|---|---|
| `CcpClient.Tests` | 1048 | +4 | 1052 | **1052** | yes |
| `CcpClient.HeadlessTests` | 35 | +0 | 35 | **35** | yes |

Zero failures, zero unexpected skips. The 2 skips are the two Linux-gated names already in
`allowedSkips`; neither is banned. The headless number was confirmed by running that project
directly (`Failed: 0, Passed: 35, Total: 35`), because the wrapper suppresses per-project summaries
once any project fails.

Also run green, before the gate, since a shape or convention slip is the most likely way this
packet breaks its own File Scope:

* `VacuousShapeGuardTests` — green, so the new file raises **zero** detector shapes and needs no
  entry in `client/tests/floor/vacuous-shape-ledger.json` (which is in `fileScopeMustNotChange`).
* `TestTimingGuardTests`, `ProcessEnvCollectionGuardTests`, `FloorWrapperGuardTests`,
  `DataRootChokePointGuardTests`, `HarnessEntryPointGuardTests` — 8/8 green.

---

## 7. Carried conditions from the plan review — dispositions

**S1 — no non-vacuity pin on the live document. ADOPTED, with one deliberate limit.**
`PinViolations` now reports a `PinCensus(ProjectCount, ScannedNameCount, AdmissionRuleLength)`
through an `out` parameter (the plan's signature otherwise unchanged; `out` is house idiom, e.g.
`ProcessEnvCollectionGuardTests.cs:183`). F1 asserts `ProjectCount > 0` and F2 asserts
`AdmissionRuleLength > 0`, both at depth 0, matching `VacuousShapeGuardTests.cs:47` and
`ProcessEnvCollectionGuardTests.cs:247`/`:298`. I deliberately did **not** pin
`ScannedNameCount > 0`: an honestly-emptied `allowedSkips` is a legal state and must not red, so
that number is reported in the failure text instead of asserted. Both facts additionally assert the
resolved pin path ends with `client/tests/floor/floor.json`, which is the part of the
reader-substitution direction that a count cannot close. **Honest limit: this does not close that
direction fully.** A `CommittedPin()` replaced with a hand-built clean document at the right path
would still satisfy every one of these assertions. See §8.

**S2 — wrong line cites for the `floor.json` writer. VERIFIED and used.** The write is
`fs.writeFileSync(PIN_PATH, next);` at `client/tests/floor/sum-deltas.mjs:465`, and `applyPin`
spans `:314-345`; both re-read in this lane. The claim they support is correct and unchanged: that
is the only writer of the pin in the repository, it runs at land, never during a test run, and
`check-floor.mjs:266` reads the pin in the parent node process before spawning `dotnet test` at
`:287`, so there is no read-during-write window and the guard needs no lock.

**S3 — `TheoryAttribute` derives from `FactAttribute`, asserted without a citation. DROPPED, as
suggested.** Nothing depends on it: both banned tests are `[Fact]`
(`DataRootOverrideTests.cs:68-69`, `ChaosTunnelLoopbackTests.cs:135-136`). `ResolvesToAFact` checks
for `FactAttribute` and makes no claim about `TheoryAttribute` either way. CLAUDE.md forbids
guessing APIs and the `FactAttribute`-only check is the more conservative behaviour.

**S4 — the helper-vs-body placement of the never-skip checkpoint has two live, opposing house
precedents. NAMED, as required.** This guard follows SP-086
(`ProcessEnvCollectionGuardTests.cs:553-555`): path resolution and the existence refusal live in
`CommittedPin()`, not in a `[Fact]` body. The opposing precedent is recorded verbatim in
`vacuous-shape-ledger.json` for `FloorWrapperGuardTests.PacketsAtOrAboveSp073` — that guard keeps
its `Directory.Exists` checkpoint in the body **on purpose**, because the detector is lexical and a
checkpoint moved into a helper stops being visible to the ledger. **The consequence here is
accepted and stated: this guard's never-skip checkpoint is invisible to the vacuous-shape ledger by
construction.** What replaces that visibility is that the refusal throws out of `CommittedPin()`,
which both live facts call as their first statement, so a missing pin or an unresolvable repo root
errors both facts rather than skipping either — and there is no `Assert.Skip*` token anywhere in
the file for the ledger to have caught in the first place. The tension is real; SP-086 is the later
precedent and following it keeps the fact bodies shape-free, which is what lets this packet land
without touching a file in its own `fileScopeMustNotChange`.

**S5 — the one false-positive path of case-insensitive membership comparison. RECORDED.**
Comparison is on the trimmed pre-`(` portion (the wrapper's own semantics,
`check-floor.mjs:68-74`) and `OrdinalIgnoreCase`. It has exactly one false-positive path: two real
tests whose fully-qualified names differ only in case. That is not reachable on today's tree and
would be a naming defect in its own right, but the claim is **not** "no false-positive path" — it
is "one unreachable false-positive path, accepted in exchange for closing two spelling-shaped
dodges (a casing variant and a theory-argument suffix), neither of which could ever match a real
TRX name and so neither of which could ever be honest".

**S6 — the sharper framing of `check-floor.mjs:237`. FILED as finding D**, with the measurement
that produced it (§1b): the wrapper contains neither banned test name, and grep for either returns
nothing.

---

## 8. Honesty — what this packet does NOT prove

* **F1 and F2 are individually revert-survivable on a clean tree.** Reverts R1-R5 each left the
  live fact whose rule they removed GREEN. The live facts are load-bearing only in combination with
  F4, which is the fact that fails. Anyone reading F1 alone and concluding "the ban is enforced"
  is reading one third of the mechanism.
* **The reader-substitution direction is only partly closed.** F1/F2 pin that the resolved path
  ends with `client/tests/floor/floor.json` and that the scan saw at least one project entry and a
  non-empty `admissionRule`. A `CommittedPin()` rewritten to synthesise a clean document at that
  path would still pass all of it. Nothing in this packet detects that; it would have to be caught
  in review.
* **F3 cannot see a coordinated rename.** Rename the test, update the `admissionRule`, update the
  constant and update the seeded fixtures in one commit and every fact stays green. That is the
  correct outcome for an honest rename and an undetectable one for a dishonest one. The guard's
  failure message says so explicitly.
* **The bans are enforced only where this suite runs.** Nothing here binds `check-floor.mjs`, which
  still consults `allowedSkips` without knowing what a ban is. If the wrapper ever ran against a
  pin this suite did not, the ban would be text again for that run.
* **`allowedSkipsMachineClasses` is still unguarded**, by decision (§2), and so is the admission
  rule's "the listing commit names the machine class" requirement. This packet says nothing about
  whether the 7 currently-listed names are honest — only that neither banned name is among them.
* **The step-3 mutations prove the two directions on THIS machine, on Windows, with this runner.**
  They are a re-demonstration through the real entry point; the platform-independent proof is F4,
  which is pure string and JSON handling with no OS dependency.
* **Nothing here is executed proof of xunit isolation.** The guard deliberately stays out of
  `ProcessEnvCollection` (`DataRootOverrideTests.cs:121-122`): it reads no environment variable,
  constructs no `CompositionRoot`, and names none of the five data-root entry points, so joining
  would only lengthen a serialized critical section. F3's reflection is metadata-only —
  `Type.GetMethods` runs no type initializer — so it cannot perturb `ChaosTunnelLoopbackTests`'s
  port registration or `DataRootOverrideTests`'s scheduling. That is an argument from the API's
  contract, not an executed probe.
* **No new wall-clock wait.** The file contains none of `TestTimingGuardTests.cs:20-42`'s forbidden
  tokens; it is synchronous, does no waiting, and reads no clock, so `TestWait` is not referenced.

---

## 9. Out of File Scope — filed, not fixed

**A. `allowedSkipsMachineClasses` has no rule and no guard, and the two written forms have
drifted.** `floor.json:30`/`:31` require the machine class be named by the **listing commit**;
`:14-22` records it in a **map** no rule mentions; `:24-27` shows the map may be absent entirely.
Two forms of one requirement, only one durable, neither enforced. Board row: decide which form is
canonical, write it into `admissionRule`, and only then make it mechanical. (This is §2's decision
restated as the work it actually is.)

**B. `check-floor.mjs` exports two functions for a harness that does not exist.** `:80`
`export function discoverTestProjects` and `:111` `export function verifyProjectResults`, both
introduced by the comments at `:79` ("Exported for the fail-closed demonstration harness") and
`:109-110`. The only call sites are inside the same file (`:269`, `:291`), so the `export` keyword
serves nothing: `client/tests/floor/` contains only `check-floor.mjs`, `floor.json`,
`sum-deltas.mjs` and `vacuous-shape-ledger.json`, and a grep across `client/` finds no consumer
outside the module itself. The wrapper's own fail-closed checks (`:111-243`) are therefore proven
by nothing. Precedent for the fix: `client/tools/citations/self-test.mjs` (SP-088),
`client/tools/verify/self-test.ps1`.

**C. `FindRepoRoot()` is now copied privately into ELEVEN files — the plan's census of eight was
short by two, corrected here.** `grep "private static string FindRepoRoot" client/tests` returns:
`AiAwarenessTests.cs:686`, `ChaosTunnelLoopbackTests.cs:404`, `DataRootChokePointGuardTests.cs`,
`FloorWrapperGuardTests.cs:263`, `HarnessEntryPointGuardTests.cs`, `ProcessEnvCollectionGuardTests.cs`,
`TestTimingGuardTests.cs`, `UpstreamPayloadInventoryTests.cs:98`, `VacuousShapeDetector.cs:484`,
`VacuousShapeGuardTests.cs:103`, and now `AllowedSkipsBanGuardTests.cs` — same
`["client", "CcpClient.sln"]` anchor and the same walk, only the refusal message differing. (The
plan enumerated eight guard/detector files; `AiAwarenessTests` and `ChaosTunnelLoopbackTests` carry
the same private helper and were missed.) All ten existing copies are `private static`, so there is
no public surface to call; extracting a shared helper means editing ten files or adding a second
new file, and this packet's File Scope grants exactly ONE new file. An eleventh copy matching ten
precedents is the honest in-scope choice. Board row: extract one shared `TestRepo.Root()` in a
packet that owns all of them.

**D. `check-floor.mjs`'s ban message does not name the banned tests** (§1b). Now that F1/F2 exist,
the wrapper's prose could cite the guard by name so a reader hitting that message has a mechanical
place to look. Bounded doc-level change to a file this packet may not touch.
