# SP-081 record: T-17's residual, the blind auditor NOT run

**Outcome: partial. The prompt correction (E1) landed and is proven. Neither audit run was
performed, because `claude` cannot authenticate on this machine.** Packet Step 1's stop
condition fired. No verdict was simulated, inferred, or manufactured. The T-17 row's second
half is **NOT discharged** and the exact manual gate is named in section 7.

---

## 1. Census

| Fact | Value | Where verified |
|---|---|---|
| Base SHA | `f2db1e25d6dd1662f189147611686c86d612e1cc` | `.port/WAVE-LOCK` `BASE:`; `git rev-parse HEAD` |
| Wave / lanes | 31 / 8 | `.port/WAVE-LOCK` |
| HEAD == `origin/feat/crossplatform` | yes, both `f2db1e25` | `git rev-parse origin/feat/crossplatform` |
| Lane worktree | `C:/Code/Conditioning-Control-Panel---CSharp-WPF/.claude/worktrees/sp-081`, branch `sp-081-auditor-prompt-residual` | `git worktree list` |
| Scratch worktree (induced) | `C:/ccp-sp081/induced`, `--detach` at `f2db1e25` | created, used, **removed** |
| Scratch worktree (clean) | **never created** | no audit could consume it |
| Pin | `CcpClient.Tests` 1022, `CcpClient.HeadlessTests` 35 | `client/tests/floor/floor.json:4`, `:21-22` |
| `claude` on PATH | yes, two installs, v2.1.232 | `which -a claude`; `claude --version` |
| `claude` authenticated | **no** | `evidence/auth-probe.txt` |

**Premise corrections carried in from planning, each re-verified in this checkout.**

- **The packet's floor numbers are one land stale.** Packet `:32` and `:148` say pin 1018
  unit. The pin is **1022** (`floor.json:4`), moved by `88a058ef` and recorded at
  `floor.json:28` as `1018->1022` via sum-deltas. Step 7 below targets 1022, not 1018.
- **The prompt is read by one test, not two.** `FloorWrapperGuardTests.cs:251` is the only
  reader; it asserts exactly three things at `:255-257`.
- **`.port/` exists and holds two files**, `WAVE-LOCK` and `orchestrator-notes.md`, both
  ignored by `.gitignore:61`. There is no `logs/` and no `*-audit.log`: consistent with the
  packet's claim that no audit has ever run under this engine.
- **`git log --oneline 7615c654..HEAD` returns eight commits**, not one.

---

## 2. Step 2, the induction: Method A, executed and clean

**Method A**, per packet `:84`: `[Fact(Skip = "SP-081 induced")]` on
`CcpClient.Tests.AiTextHygieneTests.H1_ReasoningBlock_CaseInsensitive`
(`client/tests/CcpClient.Tests/AiTextHygieneTests.cs:30`), **in the scratch worktree only**.

Why that victim: it is a bare `[Fact]` with a single depth-0 `Assert.Equal` and no vacuous
shape, so it carries no ledger entry that could go stale; it is in neither `allowedSkips` nor
either permanent ban (`floor.json:26`).

Why the attribute rather than `Assert.Skip` (packet `:82`, `:166`): `VacuousShapeDetector.cs`
binds `body` at `:176` from *after* the parameter list, and `skipTokens` (`:188-190`) can only
be set by an `Assert.Skip*` call **inside** that body. `DynamicSkip` (`:231-234`) is therefore
structurally unreachable from an attribute, so the shape guard stays green and the wrapper's
failure is the skip rather than a runner-level red.

**Both pre-spend abort checks passed.**

1. **Build warnings.** `dotnet build` of the induced tree: **0 Warning(s), 0 Error(s)**. This
   was checked because the auditor's first check is the warning count itself
   (`port-audit-prompt.md:39`), so a warning that does not break the build would still have
   added a second, non-wrapper FAIL cause. xUnit1004 did not surface on this stack
   (xunit.v3 3.2.2, xunit.runner.visualstudio 3.1.5, net10.0).
2. **TRX shape** (`evidence/induced-trx-evidence.txt`, raw TRX read directly):
   - `Counters/@total="1022"`, `@passed="1019"`, all bad counters 0;
   - **1022** `<UnitTestResult>` rows, so `resultCount == Counters/@total == pin`, which is
     what keeps the wrapper from reporting *total drift* first (`check-floor.mjs:222-229`
     runs before the skip check at `:230-241`, and `fail()` throws);
   - exactly **three** `outcome="NotExecuted"` rows: the two OS-gated Linux ones plus mine.

**The wrapper's only failure was the induced skip** (`evidence/induced-wrapper-precheck.log`,
exit 1):

```
CcpClient.Tests: FLOOR VIOLATION — unexpected skip:
CcpClient.Tests.AiTextHygieneTests.H1_ReasoningBlock_CaseInsensitive is NotExecuted but NOT
in allowedSkips.
```

`CcpClient.HeadlessTests` did not appear in the failure list. The SP-057 pin
(`DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault`) is **absent** from
the `NotExecuted` list, which re-confirms after the fact that `CCP_DATA_ROOT` was never set.

**The negative control is section 6's gate run**: the same commit, same machine, without the
induction, is `FLOOR OK` at 1022/35. So the FAIL was caused by the induction and by nothing
else. This is exactly what E1 asserts, demonstrated rather than argued: the skip was invisible
to the wrapper until it was compiled.

**Step 2 is fully discharged.** The induction is a one-line edit and is reproducible from this
record in a single command.

---

## 3. Step 1/Step 3, the blocker: `claude` cannot authenticate

Full probe log: `evidence/auth-probe.txt`. Summary:

| Probe | Result |
|---|---|
| Faithful audit invocation, `Get-AuditArgs` reproduced verbatim, prompt on stdin | exit 1, `Not logged in · Please run /login` |
| Bare `claude -p --model sonnet`, none of the audit flags | exit 1, same line |
| The second install on PATH (npm global) | exit 1, same line |
| `claude --version` | `2.1.232 (Claude Code)` |

The second probe is the load-bearing one: it proves the refusal is **not** caused by
`--safe-mode`, `--no-session-persistence`, or the restricted `--tools` set, so this is not a
defect in my reproduction of the invocation. The invocation itself was faithful and is
committed verbatim at `evidence/run-audit.ps1`; its launch record, including the rendered
command line, cwd, stdin path and both redirect paths, is at
`evidence/audit-induced-skip.launch.txt`. It ran, was delivered the prompt on stdin, and the
child exited 1 in 0.1 minutes having written one line.

**Root cause.** No credentials exist that an independently spawned `claude.exe` can read:
`~/.claude/.credentials.json` is absent, neither `ANTHROPIC_API_KEY` nor
`CLAUDE_CODE_OAUTH_TOKEN` is in the environment, and Windows Credential Manager has no
Anthropic entry. The executing session is a host-attached child
(`CLAUDE_CODE_CHILD_SESSION=1`, `CLAUDE_CODE_SDK_HAS_HOST_AUTH_REFRESH=1`): the Claude Code
host holds the OAuth material and injects it into its own SDK child, on a path a separately
spawned process cannot reach.

**No login was attempted.** `/login` is interactive, and entering credentials is outside what
this executor may do.

Packet `:76` is unambiguous about the consequence: *"If it is not, or if the invocation in
Step 3 cannot be made faithfully, STOP and report. Do not simulate the auditor, and do not
read the prompt and render your own verdict: you are the session that produced the work, and
`port-loop.ps1:362` calls exactly that self-certification."* I stopped.

The `.port/` observation now reads differently, and the harder reading is the correct one. The
packet inferred from the absence of audit logs that the auditor had *not yet* been run under
this engine. The probes show it **cannot** be run under this engine as currently configured.
That is a stronger and more useful finding than either audit verdict would have been, and it
means every land since the engine changed over has been unaudited in fact, not merely
unaudited in this checkout.

### Which pre-authorized branches were NOT selected, and why not

Packet Step 4's branches are all conditioned on a transcript. There is no transcript, so
**none of them was selected**, and picking one anyway would be the manufactured evidence the
packet forbids.

- Induced run: not D-a, D-b, D-c, D-d or D-e. There is no `VERDICT:` line, and per
  `port-loop.ps1:583-593` the honest classification is **inconclusive, infrastructure**, in the
  `ExitCode -ne 0` arm: *"auditor process exited 1 without a verdict (infrastructure, not
  evidence)"*. Never a FAIL.
- Clean run: not P1, P2 or P3. Not attempted; the clean scratch worktree was never created,
  because creating a ~4.6 GB tree no audit could consume would have cost disk and lane time
  for nothing.

**E2 therefore makes no edit, and this is the "no edit" branch reached for the wrong reason.**
Packet `:123` authorizes widening the verdict template only *"if your induced-skip run shows
the auditor could not express the wrapper's reason in that shape"*. No run, no such showing, no
edit. But note the asymmetry honestly: the packet's no-edit branch presumes a run that
*demonstrated* the current wording works. Mine did not. The template question at `:51` is
therefore **still open**, not settled, and it is filed in section 7 rather than closed.

**E3 makes no edit** for the same reason: no run output exists to quote alongside a change.

---

## 4. Step 5: the prompt correction (E1)

E1 is required unconditionally by packet `:119` and its justification does not depend on the
audit runs: the wave-30 observation is dated and independently recorded
(`client/docs/port-workflow.md`, Verification floor), and section 2 above is a fresh executed
demonstration of the same mechanism.

Inserted as a new paragraph after `:28`, so the wrapper paragraph stays adjacent to the fenced
block it refers to. **8 insertions, 0 deletions** (`git diff --numstat`), leaving `:15-20`,
`:22-28`, `:30-42` and `:44-46` byte-identical:

```diff
@@ -27,6 +27,14 @@ loop or lane logs.
    and an override makes the SP-057 pin skip, blinding the exact-count floor it exists to
    enforce.

+   Run these two commands ADJACENTLY, in the same checkout, with nothing in between: not a
+   branch switch, not a reset, not a copy from another tree. The wrapper invokes the runner
+   with `--no-build`, so it measures whatever DLLs are sitting in `bin/`, and `git reset
+   --hard`, `git checkout` and switching branches all leave gitignored build output
+   untouched. Observed at the wave-30 close (2026-08-14): the gate reported 1022 against a
+   source tree containing 1018, a clean pass on tests that were no longer in the checkout. A
+   count is evidence about the source only if the build that produced it is.
+
 3. Check every one of these:
```

"The wrapper invokes the runner" is a **mandatory** paraphrase, not a stylistic one:
`FloorWrapperGuardTests.cs:55-56` matches `\bdotnet\s+test\b` case-insensitively against the
whole file, and `client/docs/port-workflow.md:203` carries the literal forbidden token, so the
source sentence could not be quoted. R1 below proves the guard bites on exactly this.

Post-edit invariants, all re-checked: three numbered steps; six bullets under step 3; E1 sits
in step 2's prose and never as a bullet; no `\bdotnet\s+test\b` match anywhere in the file.

**The prompt gained no new checks** (packet `:127`, `:164`), no softening of "a non-zero exit
is an audit FAIL", no tolerance, no retry, no exception.

---

## 5. Revert matrix, executed

Each mechanism source reverted **one at a time**, the guard class run in full each time, and
the tree restored **byte-identically** between reverts (verified by `sha256sum -c`, not by
eye). Command each time, through the slot semaphore:

```
dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo --no-build \
  --filter "FullyQualifiedName~FloorWrapperGuardTests"
```

| # | Revert | Facts red | Which fact, and its message |
|---|---|---|---|
| -- | **Baseline** (E1 applied, delta declared) | **0** of 3 | none |
| R1 | E1 rephrased to name the runner invocation verbatim (`The wrapper runs \`dotnet test\``) | **1** of 3 | `AuditorPrompt_InvokesTheFloorWrapper_NeverBareDotnetTest`: *"port-audit-prompt.md contains a bare `dotnet test` invocation"* (`FloorWrapperGuardTests.cs:257-260`, a line I did not write) |
| R2 | **E1 deleted entirely** | **0** of 3 | none, and this is the honest result, see below |
| R3 | `floor-delta.json` deleted from the packet folder | **0** of 3 | none; the guard reads the PROMPT.md *row*, never the artifact |
| R4 | `\| floorDelta \|` row removed from the packet's `PROMPT.md` | **1** of 3 | `PacketsAtOrAboveSp073_DeclareAFloorDeltaAndNeverOwnTheSharedPin`: *"has no parseable `\| floorDelta \| ... \|` row"* (`:206-212`) |

Restores verified: after R1 and R2 the prompt hashed back to its baseline
`1c57fd35a3c8…`; after R2 `git diff --numstat` on the prompt was **empty**, proving the
deletion had been exact; after R3 the delta file hashed back to `f9bcffa4…`; after R4
`PROMPT.md` hashed back to its pre-revert value. Final `git status --short` lists only File
Scope paths.

**R2 is the finding, not a formality.** Deleting the entire clause this packet was required to
add leaves the suite green. E1 is unpinned prose held by nothing but review attention.

**R3 is a second, unplanned finding.** `artifactsMustExist` names `floor-delta.json`, and the
delta guard is described as the multi-lane chokepoint, but deleting the file is invisible to
it. R4 shows what is actually enforced: the *declaration row* in the packet, not the artifact.
A packet that declares the row and then never writes the file passes the guard, and the land's
`sum-deltas` would silently have one fewer contributor than the wave has packets. Filed in
section 7.

---

## 6. Step 7: verification

Build immediately before the gate, in this tree, both through the slot semaphore, as two
separate commands. Full gate output at `evidence/lane-gate.log`.

```
node client/tools/gate/with-slot.mjs --slots 3 -- dotnet build client/CcpClient.sln -c Debug --nologo
  -> Build succeeded. 0 Warning(s) 0 Error(s)

node client/tools/gate/with-slot.mjs --slots 3 -- node client/tests/floor/check-floor.mjs
  -> FLOOR OK: CcpClient.Tests: 1022/1022 total, 2 skipped
     [CcpClient.Tests.SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked,
      CcpClient.Tests.ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps]
     CcpClient.HeadlessTests: 35/35 total, 0 skipped
  -> exit 0
```

Three numbers, kept separate so the substitution for the packet's stale 1018 is auditable:

| | unit | headless |
|---|---|---|
| Pin (`floor.json:4`, `:22`) | 1022 | 35 |
| Declared delta (`floor-delta.json`) | 0 | 0 |
| **Observed** | **1022** | **35** |

Observed == pin + declared delta, exactly. No pin mismatch, which is the correct state for a
0/0 delta; `client/tests/floor/floor.json` was not touched.

The two observed skips are both OS-gated Linux entries, which is the expected Windows shape
per `floor.json:13-17`. The SP-057 pin is not among them.

---

## 7. Honesty: what is NOT proven

1. **The whole point of this packet is not proven.** No audit ran. The blind auditor has still
   never executed the current prompt end to end, against either tree. The T-17 row's second
   half is **NOT discharged** and nothing here should be read as narrowing it. The residual is
   unchanged in substance and now has a named blocker in front of it.

2. **The exact manual gate.** An operator with an authenticated `claude` runs
   `evidence/run-audit.ps1` twice, unchanged, against two detached scratch worktrees at the
   audited SHA. Both redirect paths must stay **outside** the tree under audit or the harness
   dirties it and the auditor fails its own `git status --short` check on the harness's bytes;
   the script asserts this rather than trusting it. The induced tree needs one edit, given in
   section 2. Nothing else about this packet needs redoing: the induction, its TRX shape, and
   the negative control are all executed and recorded here.

3. **E1 is unpinned prose, demonstrated (R2).** The guard fact asserts only the wrapper
   substring, `CCP_DATA_ROOT`, and the absence of the bare invocation. Nothing stops a later
   edit from deleting my clause with a green suite. Extending that fact means editing
   `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs`, which is outside this packet's
   scope and belongs to another lane this wave. **Filed, not done.**

4. **The `floor-delta.json` artifact is unenforced (R3).** The chokepoint guard binds the
   declaration row, never the file. Deleting the file is green. **Filed, not done**; the fix
   is a new assertion in the same out-of-scope guard file.

5. **The pin-versus-summed-deltas arithmetic gap** (packet `:135`, `:164`). The auditor never
   checks that a new pin equals the old pin plus the declared deltas. A land that instead set
   `total` to the observed count would pass the auditor while committing exactly the
   vacuous-green the pin exists to catch. R3 and R4 together make this worse than the packet
   states: the land can also lose a contributor entirely, because a declared-but-missing delta
   file is green. That is a new check, a different row. **Named for the board; not added here.**

6. **The verdict template question at `:51` is still open**, not settled. See section 3's E2
   note: the no-edit branch was reached because no run exists, not because a run showed the
   current wording sufficient.

7. **The auditor's own check 6 has no obtainable input on this tip.** The newest wave section
   (`spine-tasks/CONTEXT.md`, Wave 31) is `NOT LAUNCHED` and carries no pre-wave SHA. The only
   place the base SHA lives is `.port/WAVE-LOCK`, which `port-audit-prompt.md:10-11` forbids
   the auditor to read. Pointing the auditor at it would breach the blindness `:1-11`
   establishes, and rewording a currently-unsatisfiable check reads as making the auditor
   easier to pass. **Left alone deliberately.** This was the predicted P3 branch for the clean
   run and it remains a prediction, not an observation.

8. **`88a058ef` is a `feat(SP-073)` commit containing the `1018 -> 1022` pin edit.** Benign in
   origin, but indistinguishable from a violation of "a lane never touches the shared pin",
   which today is enforced by prose plus a row-shape guard and by no diff check at all.

9. **Isolation caveat, stated because the check was asked for by name.** This executor's
   working directory was pinned by the harness to the repository root, which is checked out on
   `feat/crossplatform`; `EnterWorktree` refused to move it ("cannot create a worktree from a
   subagent with a cwd override"), and bash cwd resets between calls. So the literal
   `git rev-parse --abbrev-ref HEAD` in the session's cwd reports `feat/crossplatform`. All
   work was done in the lane worktree via `git -C` and absolute paths, and the substantive
   proof is in section 8: the commit is on `sp-081-auditor-prompt-residual` and
   `feat/crossplatform` is still exactly `f2db1e25`.

## 8. Wording owed to out-of-scope documents (orchestrator applies at land, SP-059 precedent)

`client/docs/port-workflow.md` and `client/docs/port-lessons.md` are outside this packet's
scope and were **not edited**. Two things this run bears on:

- **`port-workflow.md`, Unattended loop.** The loop's blind-audit step is documented as if it
  will run. On this machine it cannot: a spawned `claude` has no credentials. Suggested
  wording: *"The blind audit requires a `claude` that can authenticate as an independently
  spawned process. A host-attached session does not give its child that; verify with a bare
  `claude -p` before relying on the audit gate, because an unauthenticated auditor exits 1
  with no VERDICT line and the loop halts as `inconclusive (infrastructure)`, not as FAIL."*
- **`port-lessons.md`.** Suggested lesson: *"An absent artifact proves less than it looks. No
  audit log meant 'not yet run'; probing showed 'cannot run'. Probe the mechanism before
  inferring history from its output."*

## 9. Disk and concurrency notes for the land

Scratch worktrees were created and removed **one at a time**: a built client tree is ~4.6 GB
(`with-slot.mjs:8`), so two scratch trees plus the lane is a real number on this machine. Only
one was ever created, and it is removed.

The audits, had they run, would have been **ungated and sequential**, matching the loop, which
wraps the auditor in no semaphore (`port-loop.ps1:572-574` is a bare
`Invoke-ClaudeWithStdinPrompt`; `grep with-slot client/tools/port-loop.ps1` returns nothing).
Wrapping a model process in the `ccp-gate` **build** semaphore would convert model concurrency
into build concurrency, the exact conflation `with-slot.mjs:10-11` exists to prevent, and would
put siblings an hour of queueing from exit 75 (`DEFAULT_TIMEOUT_SEC = 3600` at `:58`, enforced
at `:397-405`). Stated precisely, without the absolute: an ungated audit never **consumes** a
slot, so its only route to a sibling's exit 75 is hold-time inflation through CPU and RAM
contention, bounded by gate duration times queue depth and far short of the hour. Under the
rejected design the starvation would have been direct.

Every build and test run this lane *did* perform, including all four revert-matrix runs, went
through `--slots 3 ccp-gate`, and this lane never ran two heavy processes at once.
