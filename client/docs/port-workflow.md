# Greenfield client port workflow

This workflow combines the repository's behavioral documentation, Pi skills/workflows/agents, the project-pinned `pi-spine` execution engine, and the project-pinned `bpx-consult` advisor council. The extensions orchestrate and review work; they do not replace product decisions, WPF archaeology, current Avalonia v12 research, headed acceptance, or the client task board.

Use `client/docs/port-session-prompt.md` as the direct input for every new orchestrator session. It contains stable protocol only and forces live reconciliation before product work. After interruption, the same reconciliation resumes the active spine batch (`spine batch resume` / `spine batch retry`). Never copy live status into the starting prompt.

## Sources of authority

In descending order for port execution:

1. Owner decisions recorded in `client/docs/architecture.md` and `client/docs/capability-inventory.md`.
2. `client/docs/task-board.md`, the only live queue for the greenfield client.
3. Repository instructions in `AGENTS.md`, `CLAUDE.md`, and `.pi/CLAUDE.md`.
4. Relevant Pi skills, especially `port-plan`, `port-feature`, `wpf-parity`, `avalonia-research`, `dashboard-design`, `overlay-clickthrough`, and `unified-compositor-engine`.
5. The spine task packet (`spine-tasks/SP-*/PROMPT.md`).
6. Advice returned by `consult`, including a council verdict.

A generated spec or advisor verdict cannot override a higher source. If it conflicts, stop the task, inspect the evidence, correct the source documentation/spec or reject the advice with a recorded reason, then resume.

## Extension admission

Execution engine:

- Package: `pi-spine`. **Owner decision 2026-07-18: pi-spine replaces `@mjasnikovs/pi-task` as the port's execution engine**, and the pi-task pin was removed from `.pi/settings.json`. Evidence: the 2026-07-17/18 `/task-auto` runs failed at LLM decomposition (`decompose produced 0 titles`, then 11 titles with 7 unmapped requirements) while `client/docs/task-board.md` already is the decomposition; spine executes deterministic packets authored from board rows instead. Recorded as an owner decision because the council route normally required for tooling admission is itself unproven (see the consult-probe board row).
- Admitted version: `2.8.0`, pinned project-locally in `.pi/settings.json` as `npm:pi-spine@2.8.0`.
- Requirements verified: Node `>= 22` (machine has `25.0.0`), Git with worktree support, pi CLI for real workers. `spine` is not on bash PATH by default: `export PATH="$PATH:/c/Users/Micha/.pi/agent/npm/node_modules/.bin"`.
- License: MIT. Development-only; not linked into or distributed with CCP client binaries.
- Security boundary: executes with user permissions, spawns pi worker subprocesses in git worktree lanes, and commits at step boundaries. Review each version's diff before changing the pin.
- Project profile: `.spine/spine-config.json` — worker `kimi-coding/k3` (high), reviewer `anthropic/claude-fable-5` (medium), plan reviewer `uva/gpt-5.6-sol` (low), `lanes.maxParallel` 3, base branch `feat/crossplatform`, contract mode required. Worker standing orders: `docs/constitution.md`. Packets + execution phases: `spine-tasks/`.
- Source references: [repository](https://github.com/beettlle/pi-spine), [Pi package page](https://pi.dev/packages/pi-spine), bundled skills `create-spine-tasks` and `spine-orchestrate-waves`.

The installed npm tree under `.pi/npm/` is generated and ignored. On a trusted checkout, Pi restores the exact package declared by `.pi/settings.json`. Generated spine runtime state (`.spine/runtime/`, `.spine/batch-state.json`, `.worktrees/`) is likewise ignored because it is resumable local execution state, not the authoritative project queue. Leftover `.pi-tasks/` state from the retired pi-task engine is inert history.

Advisor package:

- Package: `@booplex/bpx-consult`.
- Admitted version: `0.10.1`, pinned project-locally in `.pi/settings.json` as `npm:@booplex/bpx-consult@0.10.1`.
- Requirements verified for this machine: Pi `>= 0.80`, Node `>= 22.19`; admission used Pi `0.80.10` and Node `25.0.0`.
- License: MIT. It remains a development-only extension and is not distributed with CCP binaries.
- Registered interface: `consult({ mode?, persona?, question? })`, with `solo`, `council`, `debate`, and `gut-check` modes.
- Project configuration: `.pi/bpx-consult.json`. The roster intentionally covers architecture, critique, simplification, testing, security, and performance using distinct authenticated model routes.
- Source references: [Pi package page](https://pi.dev/packages/@booplex/bpx-consult?name=consult), [repository](https://github.com/gabelul/bpx-mono/tree/main/packages/bpx-consult), and the installed package source under `.pi/npm/node_modules/@booplex/bpx-consult/`.

Avalonia MCP:

- Server: [`decriptor/AvaloniaUI.MCP`](https://github.com/decriptor/AvaloniaUI.MCP), added to the user's Pi MCP configuration.
- Local configuration: `~/.pi/agent/mcp.json` starts `E:\Code\AvaloniaUI.MCP\src\AvaloniaUI.MCP\bin\Release\net9.0\AvaloniaUI.MCP.dll` with `dotnet`. The inspected clone is commit `974ec59bff1c2f70e2c00e4820e5723168ac17df`, but is dirty, so the built DLL cannot yet be assumed byte-equivalent to that commit.
- Status: optional and not yet admitted as an implementation authority. Upstream has no release, one June 2025 commit, Avalonia `11.3.1`, .NET 9, and preview MCP dependencies. Pi had no cached `avalonia` tool inventory at inspection time, so successful connection/tool discovery remains unproven.
- Allowed role: advisory review of small redacted AXAML, selectors, bindings, layout, accessibility, diagnostics, and heuristic performance after official v12 research.
- Prohibited role: direct production generation, dependency/API approval, WPF conversion authority, completion gate, visual verdict, or substitute for compilation, profiling, K3 images, and headed Windows/Linux tests.
- Security gate: upstream startup config enables a fixed Sentry endpoint with tracing/profiling. Inspect the installed copy and disable/verify outbound telemetry before sending any repository source. Never send secrets, user data, camera data, private URLs, paths, or sensitive logs.
- Failure policy: if unavailable, skip it and continue with official sources. Record only concise accepted/rejected findings, not full MCP transcripts.

`bpx-consult` forwards a fitted representation of the current conversation and tool evidence to configured advisor models or CLI backends. Treat that transcript as external model input: never include secrets, private tokens, signed URLs, user media, camera frames/data, or unredacted sensitive logs in the session or consultation question. Inspect the consult result's fit ledger when advice depends on older evidence; a high dropped/clipped count lowers confidence and may require a fresh focused consult with the relevant facts summarized explicitly.

## Safe configuration before the first run

Verify the spine setup from the workspace rather than trusting this file:

- `spine doctor` and `spine preflight` pass from the repo root.
- `.spine/spine-config.json` matches the admitted profile above (models, `maxParallel` 3, base branch `feat/crossplatform`).
- `testing.*` commands are `git diff --check` placeholders until row 1 creates the client solution; each packet's `## Contract` `testCommand` is the real gate. Update `testing.*` to the real dotnet commands in the same task that creates the solution.
- Run one stub batch (`SPINE_WORKER_STUB=1 spine batch start <id>`) for every new packet shape before real workers (`SPINE_WORKER_STUB=0`).
- The tree is clean on the port branch before `spine batch start`; integrate merges only the task slice through an isolated worktree.
- Parallel waves only with disjoint File Scopes; shared chokepoint files (the task board, architecture docs) stay out of parallel packets' scopes — the orchestrator reconciles the board at land time for parallel waves.
- Review Level 2+ (code review) only after cross-model review is proven in this environment; Level 1 (plan review) is the pilot floor. If `kimi-coding/k3` workers fail to spawn (the kimi routes previously failed to engage inside bpx-consult), diagnose the provider route before blaming the packet.

The project consult profile uses:

- **default mode: council** so an unspecified consult gathers independent persona reviews and synthesizes them;
- **solo model:** `anthropic/claude-fable-5` at high thinking when a focused single-advisor review is explicitly requested;
- **gut-check model:** `zai/glm-5.2` at low thinking for quick bounded reviews;
- **council roster:** architect = `uva/gpt-5.6-sol` high, critic = `anthropic/claude-fable-5` high, simplifier = `kimi-coding/k3` medium, tester = `uva/gpt-5.6-luna` high, security = `kimi-coding/k3` high, performance = `anthropic/claude-fable-5` high;
- **council synthesizer:** `kimi-coding/k3` at high thinking;
- **whenStuck: 3** to trigger a solo review after three consecutive errors or identical tool calls;
- **onDone: off** because completion review is explicit and task-shaped, avoiding an uncontrolled extra advisor call after every turn;
- **feedback: steer** so manual/tool advice returns to the executor for action;
- **max model-initiated consults: 3 per turn** to bound cost and loops.

Run `/consult status` after restart and probe every configured council seat before relying on it. A missing or unauthorized advisor is a failed gate, not silent consensus from the remaining models. Distinct routes reduce shared-provider rate-limit failure, but council confidence is only a heuristic and never evidence by itself.

Before any run:

- use a dedicated port branch or worktree;
- require `git status --short` to be clean, or intentionally commit/stash unrelated work;
- confirm the intended package version with `pi list`;
- restart Pi after changing project trust/package configuration so the project-local extension loads;
- do not place secrets, tokens, private URLs, user media, or camera data in task prompts or generated specs.

## When to use each command

- At session start, reconcile git, `client/docs/task-board.md`, spine state (`spine status --diagnose`, `spine plan pending`, `spine-tasks/SP-*/STATUS.md`), active loops/monitors, and current tooling before issuing any batch command.
- If a batch is interrupted or diagnosed stalled, use `spine batch retry <taskId>` then `spine batch resume` (detached); never start a second engine on the same batch.
- Use `spine batch start <SP-id>` for one narrow, independently verifiable packet; `spine batch start pending --wave N` for an owner-approved phase wave.
- Use `spine tasks validate pending` → `spine tasks analyze pending` → `spine plan pending` → `spine preflight` before every launch; keep status mirrored in `client/docs/task-board.md`.
- Use `spine batch abort` (or retry with a corrected packet) when a worker conflicts with architecture, broadens scope, guesses an API, touches WPF unnecessarily, or lacks a valid headed acceptance path.
- Land finished batches promptly: evidence checklist → `spine gate approve` → `spine integrate` → `spine batch complete`.

## Dynamic workflow model routing

The `pi-dynamic-workflows` tier map is configured globally for this workstation in `~/.pi/workflows/model-tiers.json`:

- `small` / low effort: `zai/glm-5.2:low`;
- `medium`: `kimi-coding/k3:medium`;
- `big` / high effort: `anthropic/claude-fable-5:high`;
- named high fallback: `big-fallback` = `uva/gpt-5.6-sol:high`.

All four identifiers were verified against Pi's live model registry. The workflow extension resolves one configured model per tier and does not currently expose an automatic fallback chain. Therefore Fable 5 remains the primary `big` route; if it fails authentication, availability, rate limit, context, or repeated execution, retry the affected agent/phase explicitly with `tier: "big-fallback"` or exact model `uva/gpt-5.6-sol:high`. Do not silently lower a high-judgment task to medium or small.

Workflow scripts should use `small`, `medium`, and `big` rather than embedding providers except when a task needs the named fallback or another explicitly justified model. Record fallback use in the workflow result because it changes the reviewing model and may affect reproducibility.

## Consultation gates

Use consultation extensively at decision boundaries, not as a substitute for every read, edit, or test.

### Mode policy

- **gut-check:** cheap pre-edit smell test for a narrow, nearly decided change, or a post-diff check for obvious scope drift.
- **solo:** default review for one implementation strategy, an unfamiliar API interpretation after primary-source research, one failure diagnosis, or one completed narrow task.
- **council:** required for architecture decisions, new dependencies, Core/platform seams, privacy/security changes, UCE/window/input strategy, cross-platform degradation, milestone decomposition, and release/gate decisions.
- **debate:** use when two concrete approaches remain viable and materially conflict, such as framework API versus native interop or shared composition versus a separate interactive window.

### Mandatory checkpoints

1. **Before phase decomposition:** council (solo with recorded caveat until the probe row passes) reviews the proposed phase scope, packet slicing, order, blockers, acceptance, and exclusions.
2. **Before substantive work on a non-mechanical task:** solo or council reviews the selected approach after repository orientation and current official research. Do not consult before gathering enough evidence for a precise question.
3. **Before admitting a dependency or platform mechanism:** council includes security, performance, tester, and simplifier perspectives. Supply primary package/API sources.
4. **When stuck:** automatic solo at threshold 3 may steer the executor. If two approaches failed or the failure is native/intermittent, manually escalate to council or debate with exact errors, latest diff, and reproduction.
5. **Before declaring a task complete:** solo reviews the durable diff, verification output, headed evidence, tracker update, and unresolved risks. Use council for P0 architecture, security/privacy, UCE, windowing, browser, or release work.
6. **Before approving the integrate gate after a high-risk task:** reconcile advice with empirical verification and the client task board.

### Question contract

Every substantive consult question should state:

- the exact decision or defect, not merely "how is it going?";
- governing owner decisions and contract links;
- relevant files/symbols and current official sources;
- alternatives considered;
- latest diff, failing output, measurements, or headed evidence;
- Windows and Linux consequences;
- security, privacy, and performance constraints;
- the requested judgment: proceed, stop, choose A/B, identify missing tests, or propose a smaller slice.

Ask advisors to separate verified facts, inferences, unknowns, and product decisions. Require concrete evidence and falsifiable verification. Reject suggestions that invent APIs, ignore WPF behavior evidence, silently weaken Linux support, or broaden safety/privacy scope.

### Advice handling

- Record the chosen recommendation and important dissent in the task spec or task-board evidence for architecture/P0 decisions. Do not paste full advisor transcripts into tracked docs.
- Check the evidence-aware fit ledger. If decisive context was clipped or dropped, rerun a focused consult; do not trust the confidence number.
- A council split indicates uncertainty. Low agreement or member failure triggers more research or an owner decision, not majority-rule implementation.
- Review advice against code, official docs, tests, measurements, and owner decisions. Empirical contradiction wins and belongs in any follow-up consult.
- The executor remains responsible for implementation and verification. `consult` cannot mark a row `DONE`.

## Pilot before full batches

Do not start the whole port with an unconstrained batch. First run one low-risk bounded packet (`SP-001`, the Avalonia 12 template spike) through the complete spine pipeline: validate → stub batch → real batch → review → contract verify → gate → integrate.

The pilot passes only when:

- its packet cites the relevant client contract and evidence (WPF evidence where parity is relevant);
- current Avalonia v12 APIs are verified rather than guessed;
- it changes only its declared slice under `client/` and permitted documentation;
- the declared build/tests run successfully;
- headed behavioral evidence is still requested where automation cannot prove the outcome;
- the diff contains no unrelated files and does not import first-attempt implementation by default;
- task-board status and evidence are updated;
- a focused pre-approach consult and pre-completion consult were run;
- advice was reconciled against primary evidence and any dissent or unknowns were recorded;
- the consult fit ledger did not silently omit decisive context.

After the pilot, admit multi-packet waves only if one-task/one-commit boundaries were clean and no generated state or unrelated working-tree content landed.

## Phase scope input

Batches execute an owner-approved phase scope recorded in `spine-tasks/CONTEXT.md`, not the entire evolving documentation set. The scope states: exact task-board rows and explicit exclusions; dependency order and blockers; owner-held gates; and per-packet requirements (WPF evidence, first-attempt lessons, required skills, consult gates, Windows/Linux acceptance, chokepoints, verification tiers, tracker updates). Packet outcomes derive only from board rows in the approved scope — never from headings in the wider doc set (the 2026-07-18 pi-task failure mode). The former `milestone-1.md` was deleted 2026-07-18; its scope lives in `spine-tasks/CONTEXT.md` Phase 1 and its owner decisions moved to the task board.

## Required task shape

Every spine packet (`PROMPT.md`) must include:

1. **Outcome:** one user-observable or architecture-enabling result.
2. **Behavior contract:** direct references to `capability-inventory.md` and WPF evidence.
	For non-trivial features, include focused git-history archaeology for the relevant WPF and first-attempt paths; later fixes/reverts/re-openings are evidence leads, not authority over final code.
3. **Scope:** allowed files/areas and explicit exclusions.
4. **Platform contract:** Windows and Linux result or a documented blocker requiring owner decision.
5. **Implementation constraints:** current v12 research, security/privacy rules, UCE/window/input rules, and no literal WPF translation requirement.
6. **Verification:** automated checks plus headed acceptance. A compile-only check cannot verify interaction, rendering, audio, focus, window behavior, or animation.
7. **Documentation:** task-board evidence and architecture/lesson updates when facts change.
8. **Commit:** one conventional commit for the slice.
9. **Consultation:** pre-approach and pre-completion mode, focused question, evidence payload, and how advice or dissent is recorded.
10. **Avalonia MCP review:** for AXAML/UX work, the optional redacted snippet and desired review category, or an explicit reason it adds no value; record accepted/rejected findings and always run the real compiler.
11. **Integration proof:** the composition-root-to-user-outcome path that proves the work is wired; infrastructure-only tasks must say explicitly that they do not close a product capability.

For every WPF-shaped UI task, the spec must cite the current official migration index and cheat sheet plus the deeper topic page for the chosen property/style/binding/event/window/animation/control pattern. The older expert guide contributes planning methodology only and cannot override greenfield architecture or approve dependencies.

## Verification floor

The `VERIFY` block is task-specific and tiered:

1. **Fast iteration gate:** build the affected client project, run affected unit/headless tests, `git diff --check`, and scoped status. Do not launch the whole app by default.
2. **Task close gate:** run only the affected user path on Windows and claimed Linux backends. If pixels changed, capture the exact states through `app-visual-verification` and have `kimi-coding/k3` inspect them. Run required interaction/audio/focus/animation/failure evidence separately.
3. **Milestone/release gate:** broader five-theme, language, window, monitor, composition, and platform matrices only when a task-board milestone explicitly requires them.

Do not inherit the first attempt's long all-tabs smoke test or generic layer sweep. They consumed substantial time and missed visual defects. Reuse only narrowly useful capture/test primitives after proving they fit the greenfield client.

If a headed check cannot be automated, the task remains `WIP` or `BLOCKED` with the exact manual gate. Do not mark it `DONE` because the extension's command exited successfully.

## Tracker reconciliation

`pi-spine` persists execution state in `spine-tasks/SP-*/STATUS.md`, `.spine/batch-state.json`, and `.spine/runtime/` evidence. These are local execution/crash-recovery state. They do not replace `client/docs/task-board.md`.

After each subtask:

1. inspect the code diff and verification output;
2. update the matching client task-board row immediately;
3. record concrete evidence or blocker;
4. update architecture/lessons when research changes a decision;
5. confirm the task commit contains only that slice;
6. record required consult verdict, dissent, fit-ledger caveat, and empirical reconciliation for P0/high-risk work;
7. only then approve the gate and allow the next packet/wave.

Before resuming after a crash or long pause, compare spine state with git history and the client task board. The repository sources win when they disagree.

## Stop conditions

Cancel or pause the pipeline when it:

- modifies the legacy WPF head without the task explicitly requiring reference-side work;
- copies first-attempt implementation wholesale instead of preserving behavior;
- introduces a package without current version/license/platform research;
- guesses an Avalonia v12 API;
- broadens webcam, path, logging, or secret-handling privacy boundaries;
- weakens tint-opacity, input, focus, or click-through safety;
- uses browser fallback where architecture forbids it;
- skips Windows/Linux evidence while claiming cross-platform completion;
- accepts a failed verify gate to keep the auto loop moving;
- modifies files outside the task slice or commits unrelated dirty-tree content;
- creates parallel work on shared chokepoints;
- treats advisor confidence or consensus as proof without primary evidence;
- sends sensitive data to advisor models;
- repeatedly consults without narrowing the question or gathering new evidence;
- follows advice that conflicts with owner decisions or verified behavior without escalation.

## Updating the extension

Do not use unpinned task or consult package entries for port execution. To update either extension:

1. read the release diff and package manifest;
2. review changes touching command execution, prompt/system modification, file writes, git, verification, enforcement, worker network access, remote server, and persistence;
3. test the new version with a stub batch (`SPINE_WORKER_STUB=1`) on a disposable branch/worktree;
4. verify Pi peer-version compatibility and package install audit;
5. update the exact version in `.pi/settings.json` only after the pilot passes;
6. record the update and evidence in `client/docs/task-board.md` gate history.
