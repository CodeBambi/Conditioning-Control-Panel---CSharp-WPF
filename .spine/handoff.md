# HANDOFF — 2026-07-22 ~16:30 UTC — consult-route saturation park (owner-directed)

**Trigger:** Fable 5 / reviewer endpoint hit its account rate limit (429s all afternoon: SP-036's worker exhausted the 8/8 turn cap; the engine's code-review spawn for SP-035 failed `code_review_spawn_failed` with `429 rate_limit_error`). Owner directed: pause all work + prepare save spot. Batch `20260722T152755` was paused at 16:26 UTC. Loops deleted (steering loop #28 removed). No monitors active.

## How to resume

1. `export PATH="$PATH:/c/Users/Micha/.pi/agent/npm/node_modules/.bin" && export SPINE_WORKER_PI_TIMEOUT_MS=14400000`
2. Wait for the rate limit to clear (owner's call when — consult capacity is account-level).
3. `spine batch resume --force` (batch `20260722T152755`, currently `paused`).
4. Watch the journal for 429s on consult/review spawns. If they persist → re-park (`spine batch pause`) and wait longer, or the owner switches the reviewer/consult model config.
5. When both lanes complete: land per the wave-4 steering checklist (auto-gate expected; verify targetRevision + scope grep; T-3: `node .spine/patches/verify.mjs` + client build 0W/0E + tests — floor **≥485/29** (SP-035's WSL gate showed 485/485 + 29/29). Then `spine batch complete` (watch the lifecycle cleanup-throw bug: if worktree rm EBUSYs, verify archive then delete root `.spine/batch-state.json` manually). Reconcile per the wave-4 loop prompt §5.

## In-flight state (both lanes' work is committed on disk — nothing lost)

### Lane-1 — SP-035 (AI companion c2: loopback Ollama provider)
- **Committed:** `a178c5c4` Step 2 (provider + lab matrix, 485/485 unit), `708a6ed5` Step 3 (LAB both platforms — WSL2 485/485 + 29/29 0W/0E, live panic, secrets audit zero hits). Step 4 progress recorded at 16:19 (evidence consolidation begun).
- **Recovery history:** original worker healthy until the duplicate-kill collateral-failed the lane (engine tracked the wedged duplicate); retry worker spawned 16:24, its review spawn 429'd.
- **Remaining:** Step 4 (record.md + pre-completion consult), Step 5 (verification), finalization. A fresh worker will read STATUS/record and continue.

### Lane-2 — SP-036 (Avalonia MCP audit + bounded admission)
- **Committed:** Steps 1–3 evidence (`8071714e`): installation verified (ls-remote upstream equivalence), **config audit: Sentry LIVE, no disable path** (⚠ owner question — see below), runtime egress observed, 53-tool inventory, probe matrix **0 FP / 3 FN**, redaction posture. Invocation-1 exit state `8fde2dc9` (429 ×8 → turn cap; resume instructions in STATUS).
- **Replacement worker** spawned 16:24 (rules selected), parked before progress.
- **Remaining:** both consults (pre-approach + pre-completion — blocked from the start), record completion, .DONE.

## Pending owner questions (surfaced, not decided)

1. **MCP Sentry:** telemetry LIVE with no disable path — the 2026-07-21 Sentry-mitigation decision may not be implementable as decided. Admit-with-live-Sentry vs reject the server? (SP-036's admission record will present it.)
2. **AI admission §9.2 ledger (7):** moderation policy values, endpoint allow-list, memory consent/retention, awareness cooldown values, admissible command set, retry values, cloud timeline.
3. **Dashboard priority:** owner asked about frontend/dashboard progress 16:4x — offered to prioritize a dashboard-surface wave after the AI chain if wanted. No decision recorded.

## Landed today (all reconciled on `feat/crossplatform`)

- **DTRH host COMPLETE (SP-023…SP-027):** shell/transports, slots/picker/protocol v1, SFX/freeze/tint/video, progression/payout/Loom/media, watchdog/exit/failure-injection. Consolidated named limits in the board row.
- **Wave 1 (SP-029+SP-030):** quips arbitration core (`Audio/SoundArbitration.cs`) + AI companion admission (`client/docs/ai-companion-admission.md`, c1…c7 cut).
- **Wave 2 (SP-031+SP-032):** T-5 two-root fix (engine = GLOBAL 2.8.0 install; repo `.pi/npm` = tools-only) + quips q2 (bark pipeline, DTRH bark wired).
- **Wave 3 (SP-033+SP-034):** AI c1 foundation (F1 duplicate-key fix, offline zero-network, DPAPI/Linux-Unavailable secrets, panic pipeline) + stall-detector probe (`Tools/spine-worker-probe.ps1`; T-13 DONE). **T-5 NAMED GATE DISCHARGED on SP-033's lane.**
- Test floor at park: **466/466 + 29/29** (SP-035 brings 485+ at merge).
- 22 consecutive engine reviews APPROVE/PASS on review-enabled lanes.

## Key lessons in `client/docs/port-lessons.md` (all committed through 5769913a)

Two-root patch model + `verify.mjs` mandatory pre-launch; wave recovery rules (pause→retry→resume→duplicate-kill); k3 silent-wedge detection (probe tool); Review-Level heading grep-≥2 authoring rule; lifecycle-complete cleanup-throw ordering bug (state-clear must tolerate worktree rm EBUSY); fixture/evidence honesty rules.

## Next wave after resume + land

SP-037 (AI companion c3 — moderation boundary per admission §8) + lane partner TBD (evaluate tooling rows: lifecycle-ordering patch, T-3 stale-evidence, T-10 zombie-kill — vs product rows).

---

## ADDENDUM (2026-07-22 evening) — Avalonia MCP for the UX port (orchestrator research, feeds SP-036 + future UX packets)

Full 53-tool inventory pulled from the gateway. **Port-relevant subset (advisory-only per the run's rules):**

| Tool class | UX-port use (advisory) |
|---|---|
| `avalonia_ConvertWpfXamlToAvalonia` | FIRST-DRAFT conversion of simple WPF popups/cards — then dashboard-design grammar + wpf-parity outcomes govern corrections; NEVER merged without compilation + pixel verification |
| `avalonia_ValidateXaml` | Fast pre-compile syntax check on hand-written AXAML (the one tool with a proven-good record in this run) |
| `avalonia_get_getmigrationguide/steps/controlmappings/namespaceandbindingchanges/xamlpatterns/mvvmpatterns/controlsreference` | Migration reference — CROSS-CHECK against the avalonia-research skill's official-docs research; discrepancies → official docs win, recorded |
| `avalonia_GenerateSelectors/Theme/ColorScheme/DesignSystem` | Advisory drafts for the five-theme dark-neon grammar expansion — compared against the existing grammar, never pasted |
| `avalonia_GenerateCustomControl/ControlTemplate/AttachedProperty/LayoutPanel/ResponsiveDesign` | Advisory scaffolding for feature cards/popups |
| `avalonia_GenerateAnimation/Storyboard/CustomAnimation/PageTransition` | Advisory for AvatarTube-adjacent + card transitions |
| `avalonia_DiagnoseCommonIssues` | Debugging reference |
| `avalonia_GenerateUITests/UnitTests/PerformanceTests` | Test scaffolding drafts (reviewed like any generated code) |
| `avalonia_GetServerInfo/PerformHealthCheck/GetServerMetrics/Echo/TestLogging` | Server ops — SP-036's audit instruments |

**Not-admitted / rejected:** `AnalyzePerformance` (self-contradictory ×2 in this run — SP-013/SP-014 records). **Out of scope for a desktop port:** microservices/EF Core/HTTP/DDD/auth/business+domain services/plugin architecture/API models/CreateAvaloniaProject (and localization generators until the localization row).

**Usage discipline (standing, from A-013 norm):** every MCP call is recorded in the using packet's record.md with accept/reject + reasons; MCP output never substitutes for official docs, compilation, K3 screenshots, or headed gates. **The Sentry-live finding gates any expansion of use (owner question pending).**

---

## RESUME-LOOP REFERENCE — the full wave-4 steering prompt (recreate with LoopCreate cron */15, maxFires 40, after a successful resume)

STEERING FIRE — WAVE 4 batch 20260722T152755 (lane-1 = SP-035 AI companion c2 [loopback Ollama provider on c1's seam: cancellable request/stream client, timeout CLASSIFIER (never the mechanism), bounded-retry placeholder per §9.2 #6, refusal no-retry typed, malformed/truncated → typed Unavailable never partial, remote-host pre-socket rejection sendAttempts==0, LAB failure matrix both platforms (127.0.0.1 only — Ollama absence = named limit), panic re-verified LIVE, offline zero-network preserved]; lane-2 = SP-036 Avalonia MCP audit [installation/version/hash inventory, config + Sentry posture verified empirically, outbound connections (telemetry endpoints answered), tool inventory classified, seeded valid/invalid probe matrix + redaction, bounded admission record per the 2026-07-21 decree — advisory boundary: NO MCP output bypasses docs/compilation/screenshots/headed gates]. ENABLER 2 ACTIVE: workers must NOT touch task-board.md/port-lessons.md. T-5 v2 patch proven (verify.mjs mandatory pre-launch — was green). SP-034's probe tool AVAILABLE at Tools/spine-worker-probe.ps1 for wedge checks. Workers respawned after the 429 park). You are the single steering loop. Always: export PATH="$PATH:/c/Users/Micha/.pi/agent/npm/node_modules/.bin" first.

1. `spine status --diagnose`; tail `.spine/runtime/20260722T152755/journal/events.jsonl` — BOTH lanes. Heartbeats advancing = alive. Wedge check on any lane's commit gap >45 min: run `powershell -NoProfile -File Tools/spine-worker-probe.ps1 -BatchId 20260722T152755` (wedged = TRUE 0.00s CPU + 30-min write drought → T-10; crawling = stand down).

2. If phase=failed/needs_retry: ZOMBIE CHECK FIRST (T-10) — `powershell.exe -NoProfile -Command 'Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -match "spine-20260722T152755" } | Select ProcessId,Name'`; `taskkill //F //T //PID <pid>` zombie trees BEFORE retrying. Salvage-commit dirty lane WIP UNVERIFIED per-lane, EXCLUDING `.pi/loops`,`.pi/tasks`,session metadata. **WAVE RECOVERY RULE:** `spine batch pause` → `spine batch retry <BARE-id>` (per failed lane) → `spine batch resume --force` (export SPINE_WORKER_PI_TIMEOUT_MS=14400000) → **IMMEDIATELY enumerate pi/node processes and T-10-kill the engine-untracked DUPLICATE original worker** (newest spawned pair = engine-tracked). **429-WATCH: if consults or engine review spawns fail with 429 rate_limit_error, the route is saturated again — `spine batch pause` and park (owner decides when to retry; do not churn retries into a capped endpoint).**

3. Drift checks per lane: SP-035 — contacting a real Ollama or ANY external host (lab = 127.0.0.1 ONLY), implementing cloud, deciding retry VALUES (placeholder shape only), string-sniffing refusals, surfacing truncated prefixes, partial-applying malformed output, logging prompts/completions/lab secrets, rewriting c1's foundation, editing hot docs = HARD stop that lane. SP-036 — exfiltrating anything (synthetic seeds only, configs read-only, secrets presence+shape), re-deciding the owner's Sentry admission (record only), using MCP output AS verification (advisory only), product/test changes (audit only), editing hot docs = HARD stop that lane. Cancel/retry the offending lane with corrected packet.

4. **Learning harvest (every fire):** NEVER commit to base while the batch is active (T-9) — append candidates to `.spine/harvest-holding.md` (create if absent).

5. **LAND (phase=needs_integrate OR paused OR failed-after-reviews-complete):** evidence checklist FIRST per lane — journal contract.verified ok:true ×2; engine verdicts ×2 (SP-036: watch the review chain EXISTS — SP-034 defect class: absent chain = substitution case, not a T-5 proof; also SP-036's consults were 429-blocked in invocation-1 — verify both consults completed in the resumed run with verdict text + actual answering model in record.md; if the resumed worker completed WITHOUT them, that's the substitution case again — land consult explicitly covers it); lane step commits (T-6); merge preview: disjoint scopes only (SP-035: Ai/**, tests, packet dir; SP-036: packet dir only) — NO hot docs (grep-verify), NO debris; UTF-8 validity. SP-035: LAB matrix transcripts (all failure shapes), stale-discard exactly 1, panic-live typed Cancelled + bounded drain, zero external traffic proven, secrets audit zero hits; SP-036: per-item findings + admitted subset vs rejected + advisory boundary rule + decree verbatim. **T-5 state: v2 proven — clean finalizations expected; if EITHER lane T-5s → journal-first clean (CAPTURE contract-failure logs BEFORE deleting) → pause → retry per lane → resume --force → duplicate-worker sweep.** Auto-gate expected: verify targetRevision = orch tip + evidence freshness + scope grep → solo consult `anthropic/claude-fable-5` bound → `spine gate approve` → `spine integrate`. **T-3 binding: re-run BOTH testCommands on exact merge content — verify.mjs exit 0 (multi-root) + client 0W/0E, floor ≥485/29 (SP-035's WSL gate showed 485/485+29/29); any RED/below-floor → halt.** Then `spine batch complete` — **WATCH the lifecycle-ordering bug: if complete errors on worktree rm (EBUSY — VS Code/Defender on this box), verify archive exists, then delete root `.spine/batch-state.json` manually (content-preserved), record in harvest.** Reconcile (ORCHESTRATOR-WRITTEN): AI-companion row → WIP with c2 evidence + c3 named next (moderation boundary, serial cut); board MCP-audit row → WIP with the bounded admission recorded (owner ratifies or vetoes — never DONE by us; **Sentry-live finding = owner question surfaced**); CONTEXT.md SP-035/SP-036 → Done; gate-history entry; port-lessons from harvest + lane records; mem_save; **WSL gate-tree deletions (`ls /home/mich`)**; `git show --stat` verify. Then delete this loop, report + next wave: SP-037 (AI companion c3 — moderation boundary per admission §8: every-surface + every-command-field wiring, typed refusal surfacing, escalation counter mechanism with placeholder thresholds, coverage honesty — read the admission §8 c3 BEFORE authoring); lane partner = check the board (lifecycle-ordering engine patch? T-3 stale-evidence? T-10 zombie-kill? — evaluate load-bearing vs waiting; the MCP-UX usage map in `.spine/handoff.md` addendum feeds future UX packets).

6. **Pause protocol:** repeated failure (3+ same root cause), unresolvable ambiguity, safety/privacy questions, or Fable route failure → park: `spine batch pause`, write/refresh `.spine/handoff.md`, mem_save checkpoint, delete all loops/monitors, stop.

7. If phase=running and healthy: do nothing beyond the harvest check. Silence is correct steering.

**MCP deep research (2026-07-22 evening):** full report at `.spine/mcp-avalonia-deepresearch.md` — local source build at `E:\Code\AvaloniaUI.MCP` (exact upstream HEAD match, MIT, dormant single-commit project); Sentry UNCONDITIONAL in Program.cs:22 with hardcoded US DSN (no env/config disable; flows = Error+ events, Info+ breadcrumbs, 10% traces, session pings; tool names + error summaries, no full XAML); implementation is static/deterministic (ValidateXaml = real lint; generators = templates; get* refs = frozen 2025-06 JSON, docs win); **patch-and-rebuild without Sentry is the actionable answer (2-line edit, owner-controlled artifact).**
