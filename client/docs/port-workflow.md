# Greenfield client port workflow

This workflow combines the repository's behavioral documentation, the project skills and agents under `.claude/`, and Claude Code's own execution primitives (worktree-isolated subagents, fresh-context review). Those orchestrate and review work; they do not replace product decisions, WPF archaeology, current Avalonia v12 research, headed acceptance, or the client task board.

Use `client/docs/port-session-prompt.md` as the direct input for every new orchestrator session. It contains stable protocol only and forces live reconciliation before product work. Never copy live status into the starting prompt.

## Engine change, 2026-08-14

pi-spine and `@booplex/bpx-consult` are **retired**. Execution moved to Claude Code.

The decisive reason was mechanical, not preference: every lane worker and reviewer pi-spine spawns is the literal string `pi` (`bin/spine-worker-runner.mjs:431`, `src/batch/review-spawn.mjs:226`), with no config key, environment variable, or plugin seam to substitute another CLI. `lanes.workerBackend` offers only `subprocess` and `agentSession`, both Pi. Removing `pi` from PATH does not fail closed either: `src/batch/worker-host.mjs:172` silently degrades batches to stub mode. A Claude Code orchestrator driving pi-spine would therefore still be driving Pi workers.

What is retained, deliberately:

- **The packet path.** Packets stay at `spine-tasks/SP-NNN-slug/PROMPT.md`. `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs:42-100` asserts the directory exists, enumerates `PROMPT.md` at exactly one level, requires the directory to parse as `SP-<n>-`, and requires a `| testCommand | ... |` row for every packet at or above SP-065, failing closed on each. A new packet root would silently drop new packets out of the only mechanical guard binding packet contracts.
- **The review levels** 0-3 and their verdict vocabulary.
- **Every land lesson** below. They were paid for and none of them was about the engine.

What is gone, and must be recorded as a named limit rather than glossed:

> **Cross-vendor advisory diversity is gone.** Every advisor seat, every reviewer, and the blind auditor are now Anthropic models. Three seats (simplifier, security, synthesizer) previously ran on a different vendor and no longer do. The council can no longer surface a disagreement rooted in a different training lineage. What independence remains is context blindness (reviewers and the auditor never see the producing session) and mechanical checks that hold no opinion: `node client/tests/floor/check-floor.mjs`, a 0W/0E build, the scoped diff, and tree identity at land. Weight the mechanical checks accordingly, and treat seat agreement as weaker evidence than the old profile allowed.

`.pi/`, `.spine/`, and the 74 existing packets remain on disk as read-only history and as a fallback if the Claude Code path has to be abandoned mid-flight.

## Sources of authority

In descending order for port execution:

1. Owner decisions recorded in `client/docs/architecture.md` and `client/docs/capability-inventory.md`.
2. `client/docs/task-board.md`, the only live queue for the greenfield client.
3. Repository instructions in `CLAUDE.md` and `docs/constitution.md`.
4. Relevant project skills under `.claude/skills/`, especially `port-plan`, `port-feature`, `wpf-parity`, `avalonia-research`, `dashboard-design`, `overlay-clickthrough`, and `unified-compositor-engine`.
5. The task packet (`spine-tasks/SP-*/PROMPT.md`).
6. Advice returned by `port-advisor` and `port-advisor-critic`.

A generated spec or advisor verdict cannot override a higher source. If it conflicts, stop the task, inspect the evidence, correct the source documentation or reject the advice with a recorded reason, then resume.

## Execution model

**Lane.** One packet, one `port-slice-executor` subagent, one git worktree, one branch. Lanes run concurrently; eight is the target. A lane never edits the board during a parallel wave, never lands itself, and never certifies its own completion.

**Wave.** A set of packets with **disjoint File Scopes**, authored and committed together, then launched in one message so they run concurrently. Overlapping scopes are an authoring defect, not a merge problem to solve later.

**Phase session.** Reconcile, then either author-and-run one wave or land finished lanes. Never both. The session that ran a wave exits without landing it.

**Worktree base.** `.claude/settings.json` sets `worktree.baseRef: "head"`. Left unset, Claude Code branches lane worktrees from the repository default branch (`main`), which carries neither `client/` nor `spine-tasks/`. That failure is silent: the lane builds, and it builds the wrong tree. Verify this setting at reconciliation.

**Chokepoints.** `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/docs/port-digest.md`, `client/docs/port-lessons.md`, and `spine-tasks/CONTEXT.md` are orchestrator-owned. At eight lanes, `floor.json` in particular collides every wave, because every lane that adds tests must bump `total`. Resolving that at merge time by setting `total` to the observed count would recreate exactly the vacuous-green class the pin exists to prevent.

**The floor-delta mechanism (built 2026-08-14, and mechanically enforced).** A lane never touches `floor.json`. It declares its count change in its own packet folder:

```json
{ "packet": "SP-NNN-slug", "unit": 5, "headless": 0, "reason": "one line naming the facts added" }
```

at `spine-tasks/SP-NNN-slug/floor-delta.json`. `unit` targets `CcpClient.Tests`, `headless` targets `CcpClient.HeadlessTests`; both are integers and may be negative. A packet that adds no tests declares `0`/`0` — omitting the file is not the same as declaring zero, and the two are distinguished deliberately, because only one of them is a decision.

The land sums them and applies ONE bump:
```bash
node client/tests/floor/sum-deltas.mjs --check --packets SP-073-slug,SP-074-slug
```
```bash
node client/tests/floor/sum-deltas.mjs --apply --packets SP-073-slug,SP-074-slug
```
`--apply` splices only the two totals and `lastMovedBy`; every other byte of `floor.json` survives, because `allowedSkips`, `skipSemantics`, `admissionRule` and `bumpRule` are prose that packets cite verbatim and a reserialize would reflow them. Run WITHOUT `--packets` only to inspect: it sums every delta file on disk, including already-landed ones, and says so loudly.

**This is enforced, not merely documented.** `FloorWrapperGuardTests.PacketsAtOrAboveSp073_DeclareAFloorDeltaAndNeverOwnTheSharedPin` fails the suite when a packet at or above SP-073 lacks a `| floorDelta |` row naming its own folder, or fails to list `client/tests/floor/floor.json` in `fileScopeMustNotChange`. Grandfathering is by explicit ID rule, never a suppression list — the same shape as the SP-065 rule above it.

**A summed pin that disagrees with the observed count is a hard halt, not a pin adjustment.** `sum-deltas.mjs` sums DECLARATIONS and never observes a test count; `check-floor.mjs` observes and never sums. Keeping those two apart is the point: if they disagree, a lane declared something it did not do.

**Validate the wave before any lane launches.**
```bash
node client/tools/wave/validate-wave.mjs SP-073-slug SP-074-slug
```
Checks packet parse, the `testCommand` row and its floor-wrapper routing, the `floorDelta` row naming its own folder, the two `fileScopeMustNotChange` disclaimers, **glob-aware File Scope disjointness across the wave**, duplicate arguments, and task-ID reuse against the on-disk high-water mark. It reports every violation rather than stopping at the first, and it over-reports rather than under-reports on ambiguous globs by design: an over-report costs a re-read, an under-report costs two lanes.

**Machine limits, measured 2026-08-14.** Eight lanes is a model-concurrency target, not a build-concurrency target. This machine has 8 physical / 16 logical cores, 31.3 GB RAM, 514 GB free; a built `client/` tree is 4.61 GB and a full lane worktree is ~10.3 GB, so eight lanes is ~82 GB — **disk is not the constraint, CPU and RAM are.**

Gate every build and test run through the slot semaphore so model concurrency and build concurrency stay different numbers:
```bash
node client/tools/gate/with-slot.mjs --slots 3 -- node client/tests/floor/check-warnings.mjs
node client/tools/gate/with-slot.mjs --slots 3 -- node client/tests/floor/check-floor.mjs
```
**The first command IS the build** (SP-114 — see "The warning gate" under Verification floor, below): a plain `dotnet build` here is the incremental build whose output a human reads by eye, which reports `0 Warning(s)` over a tree that still holds live warnings. `client/tools/wave/port-wave.workflow.mjs` issues exactly this pair into every lane and into the code-review prompt, and `WarningGateGuardTests` fails if it stops doing so. Never run the two concurrently in the same worktree.
It passes the child's exit code through unchanged — a wrapper that swallowed a red gate would be the worst thing in this repo — holds one lock file per slot in the OS temp dir, and reaps locks whose owning pid is provably gone on this host, with a hard age ceiling as the backstop for a recycled pid. Default slots come from `CCP_GATE_SLOTS`, else 3. Set `MSBUILDDISABLENODEREUSE=1` so parallel worktrees do not accumulate msbuild nodes holding file locks.

**Named limit:** the semaphore is verified on Windows only. There is no WSL distro on this machine, so its Linux behaviour is an API audit, not a test. Two Linux caveats are recorded in the file: a shared `/tmp` with the sticky bit can refuse another user's lock unlink (it logs and keeps waiting rather than crashing), and `O_EXCL` is unreliable on old NFS.

**Lane worktrees.** Claude Code creates them at `.claude/worktrees/agent-<id>` — inside the repo, ignored by the `.claude/*` rule, **auto-removed when unchanged**. `core.longpaths` is now enabled and must stay enabled: the deepest tracked file is a 168-character WPF audio asset, which put the worst-case lane path within ~3 characters of the 260-character limit before the fix.

**A lane that goes idle having written nothing loses its worktree, and the loss is silent (wave 30, 2026-08-14).** The stop-at-plan checkpoint told the lane to change nothing so the plan review could run first. It complied, went idle, and its unchanged worktree was garbage-collected. Resumed by message, it had no worktree, so its implementation committed straight onto `feat/crossplatform` in the shared tree. Nothing warned: it built, its facts passed, its scope was clean, and only `git worktree list` and the branch the commits landed on revealed it.

Two consequences, both now encoded rather than remembered:

1. **Every checkpoint produces a file in the packet folder.** `port-slice-executor` carries this rule. A lane paused for review writes `plan.md` before stopping, which both keeps the worktree alive and produces the artifact the reviewer should be reading anyway.
2. **Verify isolation at the point of harm, not at the end.** Before accepting any lane's report, check `git worktree list` and `git branch --contains <lane head>`. A lane that reports commits "on `feat/crossplatform`" has already lost isolation. The recovery is non-destructive and takes one minute: `git branch lane/<packet> <lane tip>`, then `git reset --hard <base>` on the port branch — every commit stays reachable from the new branch, and the wave gets the merge-back it was supposed to test.

The deeper lesson is about the checkpoint pattern itself, not about worktrees: **an instruction that forbids all writes is an instruction to discard the workspace.** If a checkpoint needs the tree untouched, it needs an artifact somewhere else.

## Advisory gates

Use advice at decision boundaries, not as a substitute for every read, edit, or test.

### Seat policy

- **`port-advisor`** (Opus): the default. One bounded decision, with the evidence attached.
- **`port-advisor-critic`** (Fable): the single costly adversarial seat. Its brief is to find the false premise, not to improve the proposal. Spend it on architecture, new dependencies, platform seams, privacy and security changes, and phase decomposition.
- **A council** is the orchestrator fanning both seats out in one round and synthesizing the result itself. There is no council tool.
- **Cheap bounded reads** that used to go to a fast third-party model now go to a Haiku-tier subagent, and are never a gate.

### Mandatory checkpoints

1. **Before phase decomposition:** `port-advisor` reviews the proposed phase scope, packet slicing, order, blockers, acceptance, and exclusions. Add the critic seat.
2. **Before substantive work on a non-mechanical task:** `port-advisor` reviews the selected approach after repository orientation and current official research. Do not ask before gathering enough evidence for a precise question.
3. **Before admitting a dependency or platform mechanism:** both seats, with security and performance framing. Supply primary package and API sources.
4. **When stuck:** if two approaches have failed, or the failure is native or intermittent, escalate to both seats with exact errors, the latest diff, and a reproduction.
5. **Before declaring a task complete:** `port-advisor` reviews the durable diff, verification output, headed evidence, tracker update, and unresolved risks. Add the critic for P0 architecture, security and privacy, UCE, windowing, browser, or release work.
6. **Before landing high-risk work:** reconcile advice with empirical verification and the client task board.

### Question contract

Every substantive question should state: the exact decision or defect, not "how is it going"; governing owner decisions and contract links; relevant files and symbols and current official sources; alternatives considered; the latest diff, failing output, measurements, or headed evidence; Windows and Linux consequences; security, privacy, and performance constraints; and the requested judgment (proceed, stop, choose A or B, identify missing tests, propose a smaller slice).

Ask advisors to separate verified facts, inferences, unknowns, and product decisions, and to list the **checkable claims** you should verify yourself. Reject suggestions that invent APIs, ignore WPF behavior evidence, silently weaken Linux support, or broaden safety and privacy scope.

### Advice handling

- Record the chosen recommendation and important dissent in the packet evidence for architecture and P0 decisions. Do not paste full advisor transcripts into tracked docs.
- A split between seats indicates uncertainty. Low agreement triggers more research or an owner decision, not majority-rule implementation.
- Agreement between seats is weak evidence, because both are Anthropic models with correlated blind spots. One claim you check yourself outranks any verdict.
- Review advice against code, official docs, tests, measurements, and owner decisions. Empirical contradiction wins and belongs in any follow-up question.
- The executor remains responsible for implementation and verification. An advisor cannot mark a row `DONE`.
- Keep questions narrow and cap reply length. Long replies have been truncated in transit before; a capped precise answer is worth more than a long one that arrives cut off.

## Review levels

Packets declare `## Review Level: N`. The level drives which reviewers run.

| Level | Plan | Code | Final | Blind land audit |
|---|---|---|---|---|
| 0 | no | no | no | yes |
| 1 | yes | no | no | yes |
| 2 | yes | yes | no | yes |
| 3 | yes | yes | yes | yes |

Plan review (`port-plan-reviewer`) runs before the first product edit. Code review (`port-code-reviewer`) runs on the committed diff after the lane reports complete. Final review (`port-final-reviewer`) judges the whole packet against its own contract and returns PASS, REVISE, or REPLAN. Each reviewer is a fresh context and never edits the lane; the verdict file is the only channel.

A missing verdict, or a verdict that does not parse, is treated as REVISE. Fail closed.

## Pilot before full waves

Do not start with an eight-lane wave. Run one low-risk bounded packet through the complete pipeline first: author, launch, plan review, implement, code review, mechanical gate, merge, blind audit, board reconciliation.

The pilot passes only when: its packet cites the relevant client contract and evidence; current Avalonia v12 APIs are verified rather than guessed; it changes only its declared slice; the declared build and tests run successfully; headed behavioral evidence is still requested where automation cannot prove the outcome; the diff contains no unrelated files and does not import first-attempt implementation; task-board status and evidence are updated; the pre-approach and pre-completion advisory gates were run; and advice was reconciled against primary evidence with dissent or unknowns recorded.

## Phase scope input

Waves execute an owner-approved phase scope recorded in `spine-tasks/CONTEXT.md`, not the entire evolving documentation set. The scope states exact task-board rows and explicit exclusions; dependency order and blockers; owner-held gates; and per-packet requirements (WPF evidence, first-attempt lessons, required skills, advisory gates, Windows and Linux acceptance, chokepoints, verification tiers, tracker updates). Packet outcomes derive only from board rows in the approved scope, never from headings in the wider doc set.

## Required task shape

Every packet (`PROMPT.md`) must include:

1. **Outcome:** one user-observable or architecture-enabling result.
2. **Behavior contract:** direct references to `capability-inventory.md` and WPF evidence. For non-trivial features, include focused git-history archaeology for the relevant WPF and first-attempt paths; later fixes, reverts, and re-openings are evidence leads, not authority over final code.
3. **Scope:** allowed files and areas, and explicit exclusions. In a parallel wave, `client/docs/task-board.md` is excluded.
4. **Platform contract:** Windows and Linux result, or a documented blocker requiring an owner decision.
5. **Implementation constraints:** current v12 research, security and privacy rules, UCE, window and input rules, and no literal WPF translation requirement.
6. **Verification:** automated checks plus headed acceptance. A compile-only check cannot verify interaction, rendering, audio, focus, window behavior, or animation.
7. **Documentation:** task-board evidence, and architecture or lesson updates when facts change.
8. **Commit:** one slice. Commit as often as checkpoints require, and merge once. A slice is not one commit; it is one coherent change with no unrelated files.
9. **Advisory gates:** which checkpoints, which seat, the focused question, the evidence payload, and how advice or dissent is recorded.
10. **MCP review:** for AXAML and UX work, the redacted snippet and desired review category, or an explicit reason it adds no value. Record accepted and rejected findings and always run the real compiler.
11. **Integration proof:** the composition-root-to-user-outcome path that proves the work is wired. Infrastructure-only tasks must say explicitly that they do not close a product capability.

For every WPF-shaped UI task, cite the current official migration index and cheat sheet plus the deeper topic page for the chosen property, style, binding, event, window, animation, or control pattern.

## Unattended loop

`client/tools/port-loop.ps1` ran the port without a human in the chair. **It is currently disabled.** It is still the Pi version, and because `pi` is installed on this machine it would not fail safe: it would start and hand the Claude Code phase prompt to a Pi session. It now refuses on an engine/prompt mismatch and exits 2. It also watched `.spine/STOP` while the new prompt writes `.port/STOP`, so a pause raised by the prompt would not have halted it.

Until it is rewritten as the Claude Code lane scheduler, run supervised: an interactive session in the repository, given `client/port.txt`.

The design principles it must preserve when rewritten:

- **The shell owns the waiting; the model owns the judgment.** Each phase reconciles, does exactly one phase, and exits, so context is fresh every time and no session sits resident for hours.
- **Stops on:** a stop file, an iteration cap, a wall-clock cap, or two consecutive non-zero exits.
- **Owner digest:** every landing phase appends three lines to `client/docs/port-digest.md` (landed, does not prove, owner question). Unattended running must not bury named limits in records nobody opens.
- **Blind audit after every HEAD-moving phase, fail closed.** A fresh process with no skills, no memory, and no knowledge of the session that did the work re-derives the floor from the pushed tree and compares it against the claims in `port-digest.md` and `CONTEXT.md`. A count mismatch in either direction, any unexpected skip, a dirty or unpushed tree, or a missing verdict all halt the loop. Every failure this project has actually shipped (stale gate evidence, a red base, a vacuously green pin) was self-certified by the context that produced it; this is the cheapest structural answer. Under Claude Code, use `--safe-mode` for the auditor rather than `--bare`, because bare mode does not read subscription credentials.
- **The auditor's prompt is a file, passed on stdin.** A multi-line argument does not survive a shell shim: the first Pi implementation inlined the prompt, the auditor received nothing, asked what to audit, and the fail-closed default halted the run on a false FAIL. Claude Code has no `@file` prompt syntax, so stdin piping is the required form.
- **Auditor model must differ from the phase model,** so at least weight diversity survives now that vendor diversity does not.
- **Never export `CCP_DATA_ROOT` loop-wide.** It makes the SP-057 pin skip, so the suite reports a vacuous green and the exact-count floor discipline goes blind. Isolation comes from worktree lanes; `CCP_DATA_ROOT` is set per headed-evidence run by the packet that needs it.
- **One orchestrator at a time.** While the loop runs, an interactive session must not author, launch, or land anything.

## Verification floor

The `VERIFY` block is task-specific and tiered:

1. **Fast iteration gate:** build the affected client project, run affected unit and headless tests, `git diff --check`, and scoped status. Do not launch the whole app by default.
2. **Task close gate:** run only the affected user path on Windows and claimed Linux backends. If pixels changed, capture the exact states through `app-visual-verification` and read the captures. Run required interaction, audio, focus, animation, and failure evidence separately.
3. **Milestone gate:** broader theme, language, window, monitor, composition, and platform matrices only when a task-board milestone explicitly requires them.

The mechanical gate is `node client/tests/floor/check-floor.mjs`. A bare `dotnet test` is not it, and `FloorWrapperGuardTests` enforces that for packets and for the auditor prompt.

**Always build immediately before the gate, in the same tree.** The wrapper runs `dotnet test --no-build`, so it tests whatever DLLs are sitting in `bin/` — which need not correspond to the source in the working tree. `git reset --hard`, `git checkout`, and switching branches all leave gitignored build output untouched. Observed at the wave-30 close (2026-08-14): after the port branch was reset back over a lane's commits, the gate reported **1022 against a source tree containing 1018** — a clean pass on tests that no longer existed in the checkout. It fails the other way just as easily: a stale DLL can red a tree that is actually green, or green a tree that is actually red. The count is only evidence about the source if the build that produced it is.

### The warning gate (adopted 2026-08-20, SP-114)

**"0 warnings / 0 errors" is a claim, and it is now made by a gate rather than by a lane reading its own build output.** Run, in this order:

```
node client/tests/floor/check-warnings.mjs
node client/tests/floor/check-floor.mjs
```

The first builds and reads the build; the second still runs `dotnet test --no-build` and still carries its stale-build guard. In that order the warning gate IS the "always build immediately before the gate" step above, so it satisfies the floor's freshness precondition instead of competing with it. **Never merge the two, and never teach `check-floor.mjs` to build** — `client/docs/port-lessons.md:204` is the reason it does not, and `WarningGateGuardTests` pins it.

Two measured facts drove this, and both were false-green mechanisms rather than opinions:

- **A filtered stream cannot report what the filter cannot match.** SP-113 read its builds through `grep -E "error|warning CS|Build succ"`, which is structurally incapable of matching `warning xUnit2013`. It reported clean four times; the two real warnings surfaced only when a reviewer forced a full rebuild. **Never report a warning count obtained through a filter you have not verified against a known-matching case.**
- **An incremental build reports the warnings of the compilations it actually ran, which can be none.** Measured at SP-114 on the base tree, with a live `CS0219` in a source file: `dotnet build client/CcpClient.sln -c Debug --nologo` printed `1 Warning(s)`, and the very next run of the same command over the unchanged tree printed `0 Warning(s)`, because MSBuild skipped `CoreCompile`. The gate therefore forces `--no-incremental`; without that flag it would be vacuous.

The gate covers `client/CcpClient.sln` in `Debug` only, and it cannot see a warning that was **suppressed** before MSBuild printed it — including by a two-line `.globalconfig`, which the SDK auto-includes with no csproj reference at all. That hole is covered by a second, lexical instrument, `WarningSuppressionCensusTests`, which pins the enumerated suppression shapes under `client/` by file and code. The full boundary, and the list of shapes it does and does not enumerate, is in `client/docs/verification-harness.md`.

**When the diff touches a `*.csproj`, `*.props`, `*.targets` or a lock file, run `node client/tests/floor/check-warnings.mjs --cold`.** `--no-incremental` forces compilation, not restore, so restore-time `NU*` warnings are otherwise not re-evaluated; the gate says so on every run where restore no-ops.

Never silence a warning to make this gate pass. No `#pragma`, no `NoWarn`, no raised `WarningLevel`. If the tree has warnings, the honest outcome is a red gate and a report.

Do not inherit the first attempt's long all-tabs smoke test or generic layer sweep. They consumed substantial time and missed visual defects.

If a headed check cannot be automated, the task remains `WIP` or `BLOCKED` with the exact manual gate named. Do not mark it `DONE` because a command exited successfully.

## Tracker reconciliation

Lane branches, worktrees, and packet records are local execution and crash-recovery state. They do not replace `client/docs/task-board.md`.

After each task:

1. Inspect the code diff and verification output.
2. Update the matching client task-board row immediately.
3. Record concrete evidence or the blocker.
4. Update architecture and lessons when research changes a decision.
5. Confirm the task commit contains only that slice.
6. Record the required advisory verdict, dissent, and empirical reconciliation for P0 and high-risk work.
7. Only then merge and allow the next wave.
8. **The land's last action is a verification of the tree actually being pushed.** Reconciliation edits belong BEFORE the merged-state run; when an edit lands after it, including a docs or JSON edit, re-run at minimum the guards that CONSUME repository documents (`UpstreamPayloadInventoryTests`, `AiOperationContractTests`, `VersionDerivationTests`, and any successor) before pushing. The wave-18 land shipped a RED base by flipping an inventory disposition to `served` without the `evidence` field that guard keys on, after its verification runs had already passed.
9. **Audit a snapshot nobody can write to.** The blind auditor must be given a worktree detached at the exact SHA under audit, created for it and abandoned after — never a shared worktree a live session may still commit into. At the wave-30 land the orchestrator committed a docs-only citation fix into the very worktree being audited, and the auditor watched HEAD move from `cbfd7278` to `2748fa16` mid-run. It caught this itself and classified it correctly (docs-only, floor and source untouched, tree stable and re-verified at the new SHA), so the verdict survived — but the premise it was handed, that the tree was a fixed candidate, was false while it worked. An auditor that has to reason about whether the ground moved under it is doing the orchestrator's job. This is the hazard the wave-21 digest already named; it is now a rule.
10. **Trust your own merged-state run over any reported evidence.** Gate artifacts have been stale, and at one land were a base-tree run reporting failures the lane had already fixed. The decisive check is `git diff` EMPTY between the scratch tree you verified and the integrated tip.

Before resuming after a crash or long pause, compare lane state with git history and the client task board. The repository sources win when they disagree.

## Stop conditions

Cancel or pause when work:

- modifies the legacy WPF head without the task explicitly requiring reference-side work;
- copies first-attempt implementation wholesale instead of preserving behavior;
- introduces a package without current version, license, and platform research;
- guesses an Avalonia v12 API;
- broadens webcam, path, logging, or secret-handling privacy boundaries;
- weakens tint-opacity, input, focus, or click-through safety;
- uses browser fallback where architecture forbids it;
- skips Windows or Linux evidence while claiming cross-platform completion;
- accepts a failed verify gate to keep the loop moving;
- modifies files outside the task slice or commits unrelated dirty-tree content;
- creates parallel work on shared chokepoints;
- treats advisor agreement as proof without primary evidence;
- sends sensitive data to advisor models or MCPs;
- repeatedly asks without narrowing the question or gathering new evidence;
- follows advice that conflicts with owner decisions or verified behavior without escalation.

## Changing the execution setup

The agents and skills under `.claude/` are tracked files and land like code. To change one:

1. Read the current definition and the rule it encodes.
2. Change the smallest thing that fixes the observed problem, and say which observation drove it.
3. Verify the agent still launches and the skill still loads. A subagent whose `tools` list resolves to nothing fails to launch, and an unknown frontmatter key is silently inert.
4. Record the change and its evidence in `client/docs/task-board.md` gate history.

Never widen a mechanical guard to make a step pass. If a guard is wrong, fix the guard deliberately in its own commit with the reason stated, never as a side effect of unblocking a lane.

## The equivalence rule (adopted 2026-08-20, after four false claims in three waves)

**An equivalence claim in a mutation sweep is INADMISSIBLE until every consumer of the mutated
symbol has been enumerated by `grep`, and the claim discharged against each one by name.**

"Equivalent mutant" is the only sweep disposition that asserts a UNIVERSAL - that no input
distinguishes the mutant from the original - and it is written by the same reasoning that
produced the code. Four have been disproved, each in the same shape: **a true proposition about
ONE consumer of the mutated symbol, generalised to "no input distinguishes".**

| claim | what the proof forgot |
|---|---|
| SP-112 M-s | the pixel grid inside the box (nearest centre is within `sqrt(0.5)`) |
| SP-113 M-ch | the **null** receiver - the guard was also a null guard |
| SP-113 M-au | the **reader** - `DiscBox` also drove `ReadInk`'s scan window and stride |
| SP-113 M-aq | that the painter is not the only **writer** into the DC |

SP-113 M-ch's false proof was asserted in **shipped product source**, where the next reader would
have acted on it. Two of the four were killable by a single assertion once the consumer was named.

**If enumeration is not done, the survivor is UNCOVERED, not equivalent.** Uncovered is an honest
gap; a false equivalence is a false belief, and it propagates.

## The tolerance rule (adopted 2026-08-20, SP-115)

**A tolerance sized to an observed error is exactly the size of the defect it will next hide.**

SP-115's compositing oracle predicted one unit low in one channel. The lane refused to tune the
model to the observation - the right instinct - and set a `+/-1` tolerance instead. But `+/-1`
per channel is precisely what a one-unit-per-channel regression looks like, so the allowance
would have passed the defect the check exists to catch, at every input nobody had verified.

The residue turned out to be **the oracle's own arithmetic**: three roundings where the formula
has one. Correcting it reproduced the measurement at 8 of 8 points and the tolerance was
**deleted, not justified**.

**Before accepting any tolerance, ask what defect is the same size as the allowance.** If the
answer is one the check was written to catch, the tolerance is a hole. Prefer finding the model
wrong over widening the window: a disagreement between a model and a measurement is evidence
about the model at least as often as about the world.

## The line-ending trap (recorded 2026-08-20, SP-116)

**A single lone CR makes git's clean filter decline to normalise the WHOLE file.**

SP-116 wrote four `
` sequences into a governance document. Each produced a lone CR, so
`core.autocrlf=true` stopped normalising that file entirely and a **13-line edit entered the blob
as a 533-line rewrite** - invisibly, inside a commit about something else, making `git log -p`
and `git blame` useless on the document that defines this port's evidence classes.

Proven empirically rather than argued, by hashing through the repo's own clean filter:

    git hash-object --path=<in-repo path>  "a
b
"    -> blob holds  a 
 b 
      (normalised)
    git hash-object --path=<in-repo path>  "a
b
"  -> blob holds  a   
 b  
  (verbatim)

**Before landing any doc change, compare `git diff --numstat` against `git diff --numstat
--ignore-all-space`.** If they disagree, the commit is carrying whitespace churn that hides its
own content. And **match base's convention** - SP-116 was told to restore a BOM, checked, found
base had none, and correctly refused: the BOM was its own earlier slip, and restoring it would
have re-introduced the very divergence the fix existed to remove.
