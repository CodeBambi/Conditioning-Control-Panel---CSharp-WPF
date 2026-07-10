# CCP Avalonia Port — Docs Index

**Branch:** `feat/crossplatform` @ `5e3ed650` · **App version:** 6.2.11 · **Index authored:** 2026-07-10 (docs rework)
**Live gates at index time (re-run 2026-07-10):** `CCP.Desktop.slnf` build 0 errors (384 warnings) · WPF sln build 0 errors · Core tests **542/542** (Release, 0 failed) · working tree clean. Re-run gates live before claiming them; do not cite a stale capture.
**Trust-nothing verification pass (2026-07-10):** 90 material status claims from the canonical docs were audited against code/git/live output — **68 VERIFIED · 16 WEAKENED (downgraded in place) · 2 FALSIFIED (corrected: `IBrowserHost` is an implemented 11-member seam, not missing; CCP.Core is 302 `.cs` / 33 seam interfaces / 91 models) · 4 PLATFORM-LIMITED**. Corrections applied in place; improvement rows filed on the task board.

This is THE map of the port doc set. A doc is listed here or it does not exist. Future port sessions read
this first, then `skia-rebuild-goal.md`, then claim exactly ONE row on the task board. Everything else in
`docs/` that is not listed below was deleted in the 2026-07-10 rework — the full **Deletion record** at the
bottom says where each piece of knowledge now lives (git history + successor doc), so no future agent goes
looking for a ghost.

The umbrella driver is [`skia-rebuild-goal.md`](skia-rebuild-goal.md) — "functionality is the contract,
implementation is not." Every WPF feature must work end-to-end in the Avalonia v12 heads on Windows AND
Linux; all real-time visuals render as `IAvaloniaLayer`s inside one `CompositorEngine` (one topmost window
per monitor, z-ordered layers, one 60Hz tick, PER-REGION click-through per the 2026-07-09 team review).
The WPF head is behavior reference ONLY and is never modified.

---

## 1. Read order

### Core set (read in this order for every port session)

| # | Doc | One-line purpose |
|---|---|---|
| 1 | `docs/docs-index.md` (this file) | The map: read order, workflow/tier model, deletion record. |
| 2 | [`skia-rebuild-goal.md`](skia-rebuild-goal.md) | THE umbrella driver: spirit, doctrine, workflow model, definition-of-done. |
| 3 | [`avalonia-migration-task-board.md`](avalonia-migration-task-board.md) | The ONLY live work tracker. Claim exactly ONE row here; one task per commit. |
| 4 | [`avalonia-ui-parity-matrix.md`](avalonia-ui-parity-matrix.md) | Parity evidence store + re-verify queue + Linux sweep status. |
| 5 | [`unified-compositor-engine-plan.md`](unified-compositor-engine-plan.md) | UCE state, 22-layer registry, per-region input-mask spec, FPS protocol. |
| 6 | [`crossplatform-rebuild-plan.md`](crossplatform-rebuild-plan.md) | v12 gotchas (section 21), platform seams, Linux mechanism catalogue. |
| 7 | [`port-session-prompt.md`](port-session-prompt.md) | LIVE driver prompt + launch pre-flight for autonomous port sessions. Stable protocol only; volatile facts stay on the board; the running session maintains it. |

### On-demand per-area detail (read only when a claimed row points at it)

| # | Doc | One-line purpose |
|---|---|---|
| 7 | [`linux-vm-testing.md`](linux-vm-testing.md) | Linux VM verification runbook (WS4 sweeps). |
| 8 | [`benchmark-2026-07-05-analysis.md`](benchmark-2026-07-05-analysis.md) | Perf baseline evidence + the MinFps=0 / environmental-invalidation caveat (pairs with `benchmark-optimized.json`). |
| 9 | [`uce-coverage-audit.md`](uce-coverage-audit.md) | Layer coverage ground truth + interactive-window justification. |
| 10 | [`uce-eyes-verification-runsheet.md`](uce-eyes-verification-runsheet.md) | Human visual-verification runsheet. |
| 11 | [`webcam-calibration-port-plan.md`](webcam-calibration-port-plan.md) | Calibration backlog (home of the folded overhaul remainder). |
| 12 | [`voice-port-status.md`](voice-port-status.md) | Voice area status + open remainder. |
| 13 | [`TUTORIAL_SYSTEM_CONTEXT.md`](TUTORIAL_SYSTEM_CONTEXT.md) | Recon for the not-yet-ported tutorial system. |
| 14 | [`../Services/Chaos/CHAOS_DESIGN.md`](../Services/Chaos/CHAOS_DESIGN.md) | Chaos design reference (now the only chaos design doc). |

### Evergreen references (outside the port read-path; kept, not part of the claim loop)

openspec bundle (`../openspec/`), [`locked-content-spec.md`](locked-content-spec.md),
[`ollama-integration.md`](ollama-integration.md), [`prestige-server-spec.md`](prestige-server-spec.md),
[`../Services/Chaos/CHAOS_NARRATIVE_PLAN.md`](../Services/Chaos/CHAOS_NARRATIVE_PLAN.md),
root `AI_AUDIT.md` (WPF-era paths — a task-board row tracks refreshing it), and the protected root docs
(README, GUIDE, CONTRIBUTING, CODE_OF_CONDUCT, both CLAUDE.md).

---

## 2. Workflow execution model

Future port sessions are driven by the pi-dynamic-workflows `workflow` tool, not by grinding one context.
Fan work out instead: `agent()` / `parallel()` / `pipeline()` / `phase()`, with journaled resume and
git-worktree isolation. Use `verify()` for adversarial fact-checking of findings and `judgePanel()` for
candidate selection on JUDGMENT-tier outputs. Work is routed to one of three model tiers; project
agentTypes and the mandatory skills below are how the tier model stays disciplined.

### Model tiers (verbatim routing — copy into every kickoff)

| Tier | Model id | Allowed work |
|---|---|---|
| **small** (MECHANICAL) | `kimi-for-coding` | Literal, list-driven execution of **pre-sliced** turnkey edits with WPF file:line citations, deletions, sweeps, tracker updates. Fast but literal. MUST stop with a `BLOCKED:` note instead of improvising when a precondition fails or a step is ambiguous. |
| **medium** (STANDARD) | `zai/glm-5.2` | Bounded implementation, research digestion, reference reconciliation, routine reviews, inventories. |
| **big** (JUDGMENT) | `anthropic/claude-fable-5` | Architecture, slicing, adversarial review, and anything touching state, economy, security, input hooks, or compositor internals. |

### Project agentTypes (defined in `.pi/agents/`, mirrored in `.kimi-code/agents/`)

| AgentType | Role |
|---|---|
| `wpf-archaeologist` | Read-only WPF behavior-contract extraction with File.cs:line cites — use it so nobody opens the 100KB+ WPF files raw. |
| `port-slice-executor` | Implements ONE pre-planned slice under the iron rules (gates, no TODOs, no forbidden zones, WPF cites, test floor). |
| `port-parity-auditor` | Adversarial working-tree diff audit vs WPF ground truth before commit — mandatory for state/economy/lifecycle diffs. |

### Mandatory skills (NOT optional — Avalonia v12 is 2026-new; training data is stale or actively wrong)

All live in `.pi/skills/` (authoritative) with `.kimi-code/skills/` mirrors. Fire conditions:

| Skill | When it fires |
|---|---|
| `avalonia-research` | Before ANY Avalonia API use, new dependency, or unexplained exception (web reality over stale v11 training). |
| `port-plan` | At session start / task claiming; slicing, Core-vs-head seam design. |
| `wpf-parity` | When you need a WPF behavior contract or are changing any ported behavior. |
| `port-feature` | The implementation workflow + the v12 conversion cheatsheet; whenever you edit `.axaml` or a CCP.Avalonia service. |
| `mechanical-port-work` | Small-tier execution discipline for pre-sliced turnkey rows. |
| `unified-compositor-engine` | All layer/video/z-order/overlay work under `CCP.Avalonia/Compositor/`. |
| `overlay-clickthrough` | All input / `WS_EX_*` ex-styles / global-hook / hit-test / focus / topmost work; Linux/macOS click-through planning. |
| `dashboard-design` | All user-facing surfaces (5-theme reskin is part of done). |
| `port-audit` | Workstream close-out health/drift audit. |

### Gates before every commit (all must pass)

- `CCP.Desktop.slnf` build 0 errors; legacy WPF `.sln` build 0 errors.
- Core tests all pass and the count NEVER decreases (floor **542** as of 2026-07-10 — read the live count).
- `--smoke-test` → 44 tabs + 0 unhandled + findings ⊆ the recorded benign drift set (task-board smoke-drift row; logged-out baseline = Findings 5, count-equality is NOT the signal while the smoke env is authed — owner-waved 2026-07-10); `--verify-layers` / `--verify-video` when touching the compositor/video.
- `--benchmark` before/after on hot paths — not worse than `benchmark-optimized.json` (re-baseline caveat: task-board row #2).

**Acceptance gate (the spirit):** a ported feature is accepted only when at least as fast and smooth as the
WPF head — preferably measurably improved; big changes are encouraged when they win on merit, with what/why
recorded in the board.

---

## 3. Deletion record (44 removals)

Every path the 2026-07-10 docs rework removed. "Where it lives now" = git history (always) + the successor
doc that absorbed its still-relevant knowledge. **40 direct deletes** + **4 merge sources** (deleted only
after their owner folded the open remainder into the named successor). Contentious rows were still cited by
live code — those `.cs` comment citations are scrubbed via the task-board **R-scrub** row, not in this
.md-only rework.

### Direct deletes — TODO debris (11; SWEEPER skimmed each before deletion)

| Deleted path | Why | Knowledge now lives in |
|---|---|---|
| `ConditioningControlPanel/.todo.md` | TODO debris | task-board Triage Inbox (skim) + git history |
| `ConditioningControlPanel/AVALONIA_LIBVLC_DISCOVERY_TODO.md` | Discovery debris; LibVLC wiring shipped (WS1 A–E) | task-board Triage Inbox + git history |
| `ConditioningControlPanel/CCP.Avalonia/AvatarTube/TODO.md` | Local TODO debris (AvatarTube stays a window per doctrine) | task-board Triage Inbox + git history |
| `ConditioningControlPanel/CCP.Avalonia/Dialogs/PORT_TODO.md` | Port-TODO debris; dialogs lot passed (WS0 lot 9) | task-board Triage Inbox + git history |
| `ConditioningControlPanel/CCP.Avalonia/MOBILE_TODO.md` | Mobile debris; Android head out of port scope (builds stay green) | task-board Triage Inbox + git history |
| `ConditioningControlPanel/CCP.Avalonia/TODO-avalonia-port-batch.md` | Completed batch | git history |
| `ConditioningControlPanel/CCP.Avalonia/TODO-port-batch1.md` | Completed batch | git history |
| `ConditioningControlPanel/CCP.Avalonia/TODO_gif_svg_migration.md` | Completed gif/svg migration | git history |
| `ConditioningControlPanel/CCP.Avalonia/TODO_tray_dialog.md` | Completed tray-dialog work | git history |
| `ConditioningControlPanel/CCP.Avalonia/Views/Deeper/TODO.md` | Local debris | task-board Triage Inbox + git history |
| `ConditioningControlPanel/_pending/SecretStore/TODO.md` | Debris; `ISecretStore` seam exists | `CCP.Core/Platform/ISecretStore` + git history |

### Direct deletes — completed plans, specs, catalogues, audits (29)

| Deleted path | Why | Knowledge now lives in |
|---|---|---|
| `ConditioningControlPanel/docs/ai-command-service-port-plan.md` | COMPLETE 2026-07-05 (`70cf980`/`9fa0985`/`424ea52`); 3 P3 gaps already tracked | skia-rebuild-goal shipped ledger + task-board P3 rows + git history |
| `ConditioningControlPanel/docs/attention-check-layer-migration-spec.md` | DONE (`57f6f048`); cited by `AttentionCheckLayer.cs` | WPF source + `AttentionCheckLayer.cs`; code-comment scrub = task-board R-scrub + git history |
| `ConditioningControlPanel/docs/behavior-design-audit.md` | Point-in-time audit; refcount 0 | WPF code + `avalonia-ui-parity-matrix.md` + git history |
| `ConditioningControlPanel/docs/bubbleservice-avalonia-port-plan.md` | Completed (bubbles are a UCE layer, `BubbleLayer` Z=45) | `unified-compositor-engine-plan.md` layer registry + `CHAOS_DESIGN.md` + git history |
| `ConditioningControlPanel/docs/chaos-run-engine-contracts/avalonia-current-state.md` | Point-in-time snapshot; chaos port complete (S1–S9) | `CHAOS_DESIGN.md` + git history |
| `ConditioningControlPanel/docs/chaos-run-engine-contracts/draft-payloads-lifecycle.md` | Planning contract; port complete | `CHAOS_DESIGN.md` + git history |
| `ConditioningControlPanel/docs/chaos-run-engine-contracts/economy-scoring.md` | CONTENTIOUS: economy ported+tested (`87515732`; `ChaosEconomyTests`/`ChaosScoringTests`) | WPF source (permanent behavior reference) + pinned tests + `CHAOS_DESIGN.md`; `.cs` cites (`ChaosEconomy.cs`/`ChaosScoring.cs`) = task-board R-scrub + git history |
| `ConditioningControlPanel/docs/chaos-run-engine-contracts/estim-arc-visual-slice.md` | Shipped (`ChaosEStimArcLayer` Z=125, `05520f52`) | `unified-compositor-engine-plan.md` layer registry + git history |
| `ConditioningControlPanel/docs/chaos-run-engine-contracts/spawn-system.md` | CONTENTIOUS: spawn ported+tested (`2d7bc384`, `ChaosBubbleHintsTests`) | WPF source + pinned tests + `CHAOS_DESIGN.md`; `.cs` cites (`ChaosSpawnDirector.cs`/`ChaosBubbleHints.cs`) = task-board R-scrub + git history |
| `ConditioningControlPanel/docs/chaos-run-engine-port-plan.md` | S1–S9 ALL DONE + user-verified (`2d7bc384`…`1f4c19fc`/`e61633c0`); `mechanical-port-work` skill cited it | task-board shipped ledger; skill re-pointed to live rows (SWEEPER); `AvaloniaHeadStubs.cs` cite = task-board R-scrub + git history |
| `ConditioningControlPanel/docs/chaos/planning/00-master-plan.md` | Superseded by `CHAOS_DESIGN.md` | `CHAOS_DESIGN.md` + git history |
| `ConditioningControlPanel/docs/chaos/planning/A-loop.md` | Superseded by `CHAOS_DESIGN.md` | `CHAOS_DESIGN.md` + git history |
| `ConditioningControlPanel/docs/chaos/planning/B-meta.md` | Superseded by `CHAOS_DESIGN.md` | `CHAOS_DESIGN.md` + git history |
| `ConditioningControlPanel/docs/chaos/planning/C-voice.md` | Superseded by `CHAOS_DESIGN.md` | `CHAOS_DESIGN.md` + git history |
| `ConditioningControlPanel/docs/chaos/planning/D-art.md` | Superseded by `CHAOS_DESIGN.md` | `CHAOS_DESIGN.md` + git history |
| `ConditioningControlPanel/docs/chaos/planning/E-flavour.md` | Superseded by `CHAOS_DESIGN.md` | `CHAOS_DESIGN.md` + git history |
| `ConditioningControlPanel/docs/chaos/planning/F-systems.md` | Superseded by `CHAOS_DESIGN.md` | `CHAOS_DESIGN.md` + git history |
| `ConditioningControlPanel/docs/drone-mod-recon.md` | Completed recon; content lives in the `.ccpmod` | the `.ccpmod` package + git history |
| `ConditioningControlPanel/docs/gamification-audit.md` | Superseded | `openspec/specs/05-gamification.md` + `avalonia-ui-parity-matrix.md` + git history |
| `ConditioningControlPanel/docs/kept-mode-changemap.md` | Point-in-time audit; refcount 0 | git history |
| `ConditioningControlPanel/docs/model-handoff-queue.md` | Q1–Q5 (chaos S5–S9) ALL DONE + user-verified; `mechanical-port-work` skill cited it | task-board shipped ledger; skill re-pointed (SWEEPER) + git history |
| `ConditioningControlPanel/docs/plans/secondary-tab-richness-sprint.md` | Completed sprint | `avalonia-ui-parity-matrix.md` + git history |
| `ConditioningControlPanel/docs/profilesync-port-plan.md` | DONE 2026-07-04, all 7 slices (`4f051ab0`/`80e1442`); cited 3x by the umbrella driver + `ProfileSyncService.cs:27` | skia-rebuild-goal shipped ledger + `avalonia-ui-parity-matrix.md` row 1; code-comment scrub = task-board R-scrub + git history |
| `ConditioningControlPanel/docs/tab-view-parity-plans.md` | Completed; superseded | `avalonia-ui-parity-matrix.md` + git history |
| `ConditioningControlPanel/docs/v6.2.10-port-catalogue.md` | Superseded release catalogue; its only open remnant (#493 pair) became two task-board rows | task-board (#493 pair rows) + git history |
| `ConditioningControlPanel/docs/v6.2.9-port-catalogue.md` | Superseded release catalogue; no open remnants | git history |
| `MODERATION_PR_AUDIT.md` | One-time PR #24 review; refcount 0 | git history |
| `PROGRESSION_AUDIT.md` | Point-in-time audit; superseded | `CHAOS_DESIGN.md` + shipped chaos port + git history |
| `docs/avalonia-ponytail-audit-queue.md` | All items completed + verified against the four gates | task-board + git history |

### Merge sources — deleted only AFTER the named successor absorbed the open remainder (4)

| Deleted path | Folded into | By owner |
|---|---|---|
| `ConditioningControlPanel/docs/avalonia-calibration-overhaul-port.md` | `webcam-calibration-port-plan.md` (calibration window UX + 3 services + live-webcam verify) | SWEEPER |
| `ConditioningControlPanel/docs/parity-reverify-triage.md` | `avalonia-ui-parity-matrix.md` ("Re-verify queue" section) | PARITY |
| `ConditioningControlPanel/docs/unified-compositor-engine-goal.md` | `unified-compositor-engine-plan.md` (surviving acceptance criteria + layer doctrine) | UCE |
| `ConditioningControlPanel/docs/v6.2.11-port-catalogue.md` | `avalonia-migration-task-board.md` (DTRH web roguelite epic appendix — dollhouse rewrite superseded "The Fall" 2026-07-10 — + verify-set rows) | BOARD |

**Arithmetic:** 118 tracked `.md` before the rework → 40 direct deletes + 4 post-merge deletes = **44 removed**;
1 created (`docs-index.md`); **75 surviving tracked `.md`**. 6 were rewritten in place
(`ConditioningControlPanel/CLAUDE.md`, the umbrella goal, the task board, the parity matrix,
the UCE plan, the cross-platform plan).

---

## 4. Known gaps (each has a task-board row)

- **`.kimi-code/subagents.json` diverges from `.pi/subagents.json`** — it is not a `.md` file, so this
  `.md`-only rework could not touch it. A task-board row tracks re-syncing it in a code-capable session.
  `.pi/` is the authoritative tree; `.kimi-code/` is a mirror.
- **Stale `.cs` comment citations of deleted docs** — the contentious deletes (`economy-scoring.md`,
  `spawn-system.md`, `chaos-run-engine-port-plan.md`, `attention-check-layer-migration-spec.md`,
  `profilesync-port-plan.md`) were still referenced in code comments. Those are scrubbed via the task-board
  **R-scrub** MECHANICAL row (comments only, zero behavior) — not in this `.md`-only workflow.
- **`AI_AUDIT.md` carries WPF-era paths** — still canonical (refcount 6) but its paths mislead porting
  agents. A low-priority task-board row tracks a path refresh when it is next edited.

---

## 5. Maintenance rule

- **A doc is added to this index or it does not exist.** New port docs land in section 1; new evergreen
  docs land in the evergreen list.
- **Deletions append to the record in section 3.** Never remove a row — append a new one with the successor
  so the history stays queryable.
- **Evidence is re-read live, never invented.** Commit hashes, dates, and test counts are re-read from
  `git` and the live gate runs (or copied from the task-board SHIPPED ledger / parity-matrix rows) before
  they are claimed — there is no separate `progress`/`gates` scratch file in the read path.
- **Doc trust (owner ruling 2026-07-10 — supersedes the same-day "docs are hypotheses" ruling).** The
  full doc-vs-code reconciliation pass completed 2026-07-10 (90 claims audited: 68 verified / 16 weakened
  / 2 falsified / 4 platform-limited; corrections landed same day). Doc statuses are now trustworthy — do
  NOT re-audit them wholesale; that is double work. Spot-verify a claim only when it is load-bearing for a
  change touching state, economy, security, input hooks, or compositor internals, or when live evidence
  contradicts it; a stale doc is fixed in the same commit. Verified-existing features are still fair game
  for improvement: big changes are allowed when they win on merit (behavior is the only contract). Any
  future full verification pass records its date + claim total + verdict counts here and in
  `skia-rebuild-goal.md`.
- **Transient workflow scratch is not part of this index.** Recon/scratch directories produced by a docs
  rework (e.g. a local `.rework/`) are local-only, not committed, not listed here, and not pointed at by any
  governed doc — the workflow that creates them purges them on close. Only the docs in sections 1–3 belong
  to this index's governance.
- **Only `.md` files belong to this index's governance.** Code-comment citations, `.json`, and other
  artifacts are tracked as task-board rows, not here.
