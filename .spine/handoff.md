# HANDOFF — 2026-08-04 ~13:10 UTC — anthropic fresh-subprocess route down (pause protocol, both-routes-failed branch)

**Trigger:** the engine's reviewer spawn (`pi -p --no-session --model anthropic/*`) fails deterministically with `400 invalid_request_error: "Third-party apps now draw from your extra usage, not your plan limits. Add more at claude.ai/settings/usage and keep going."` (batch `20260804T120113` journal `review.failed` ×2, 12:27:17Z + 12:36:18Z, classification `code_review_spawn_failed`). Manual probes outside the engine: `pi -p --model anthropic/claude-opus-5` AND `--model anthropic/claude-fable-5` BOTH 400 identically. **In-session bpx-consult (orchestrator + worker in-packet) is UNAFFECTED** — workers spawn on kimi k3 fine; only anthropic fresh subprocesses are down.

**Owner action to resume:** restore anthropic spawn capacity (add extra usage at claude.ai/settings/usage) or wait for the account window to recover. Then paste `client/port.txt` — reconciliation resumes at wave 4.
**Owner options if throughput matters before recovery:** (a) SP-036-only substitution run (audit packet, zero product drift, self-verifying contract — the nearer-safe option, still requires your explicit acceptance of an absent engine review chain); (b) reviewer repin to kimi k3 (a GATE DOWNGRADE — owner-only decision, recorded as such in the rewire doc). Neither was taken unilaterally.

## State at park (all durable, all pushed)

- **Branch:** `feat/crossplatform` @ SP-037 land (`7e2fd5b8`) + reconcile commit on top. Pushed.
- **SP-037 LANDED** (substitution norm): v6.6.3 manifest-drift repaired; floor restored **466/466 + 29/29**; board row → WIP (owner ratifies); gate-history entry written; evidence in `spine-tasks/SP-037-asset-manifest-v663-resync/record.md` + archived `.spine/runtime/20260804T120113/`.
- **Wave 4 PENDING, amended, unlaunched:** SP-035 (AI companion c2) + SP-036 (avalonia MCP audit) — both packets amended 2026-08-04 (commit `cda4d1d6`): consult rewire Opus-5-main/Fable-fallback; SP-035 Ollama-presence re-probe (laptop HAS Ollama — `ollama --version` probe only, no socket/pull) + WSL-gate named limit; SP-036 audit subject = the 2026-08-04 three-seat registration + deep-research report as verify-input. Sequencing deps on SP-037 (now satisfied). `spine plan pending`: wave 0 = SP-035+SP-036, 2 lanes.
- **Spine:** idle, no batch state, husks cleaned, `verify.mjs` green both roots (engine re-pinned to admitted 2.10.0 + 12 patches after the global root floated to 2.12.2).
- **Test floor:** 466/466 + 29/29 (restored by SP-037).

## Machine facts (laptop — fresh-machine bootstrap deltas, durable)

1. **`git config --global core.hidedotfiles false` APPLIED** (2026-08-04) — without it, git-for-Windows hides worktree `.git` pointer files and the engine's Node rewrite EPERMs (SP-020 class; killed 2 batch starts). Required on any fresh machine/clone.
2. **`pi.exe` shim installed** at `AppData/Roaming/npm/pi.exe` (source `~/.pi/pi-shim/PiShim.cs`, stock csc) — Node 24 cannot spawn the extension-less/`pi.cmd` shims (ENOENT/EINVAL); blocks spine doctor + worker-runner. Required on any fresh Windows machine.
3. WSL installed, ZERO distros — every WSL2 gate = named limit until a distro is provisioned (owner decision).
4. Ollama installed (`ollama --version` presence probes only — no socket/pull).
5. No `Z:\CCP Vids`, no DISPLAY3 (desktop-only); memory provider = hermes.
6. Global pi-spine pinned exact 2.10.0 in BOTH `~/.pi/agent/settings.json` and `~/.pi/agent/npm/package.json` (the float source was the unpinned settings entry + caret range).
7. bpx-consult: pass `mode:"solo"` explicitly (project config `defaultMode:"council"` fails here — kimi-api unregistered).

## Resume checklist

1. `export PATH="$PATH:$HOME/.pi/agent/npm/node_modules/.bin"` (+ `SPINE_WORKER_PI_TIMEOUT_MS=14400000` at launch).
2. Verify the anthropic spawn route recovered: `pi -p --no-session --model anthropic/claude-opus-5 "Reply OK"` — a 400 means still parked.
3. `node .spine/patches/verify.mjs` (mandatory pre-launch) + `spine preflight`.
4. `spine batch start SP-035 SP-036` (detached); steer per `client/docs/port-session-prompt.md`; land per the wave playbook (auto-gate expected when reviews can run; T-3 floor = SP-035's contract will show ≥466+new).
5. Named limits to carry into wave-4 lands: WSL2 zero-distros (Linux gates open); SP-035's real-Ollama = session fact only.

## Open owner questions surfaced (not decided)

1. Anthropic extra-usage capacity (the park cause).
2. WSL distro provisioning on the laptop (Linux gates).
3. MCP Sentry posture: laptop's `avalonia-ui` build — Sentry-carrying vs patched (SP-036's audit answers; deep research found Sentry unconditional upstream; patch-and-rebuild was the noted mitigation).
4. AI admission §9.2 ledger (7 questions, unchanged since 2026-07-22).
5. Dashboard-priority question (owner asked 2026-07-22; no decision recorded).
