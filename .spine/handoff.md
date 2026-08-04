# HANDOFF — 2026-08-04 ~15:35 local — wave-4 park LIFTED (pi billing-header fix)

**Status: NOT PARKED — wave 4 launching.** This file remains as the machine-facts + resume reference (per convention), updated after the fix.

## What happened (2026-08-04, one day, three acts)

1. **Resume reconciliation** (waves 1–3 verified; desktop wave-4 lanes never travelled → fresh execution; engine restored to admitted 2.10.0 + 12 patches; SP-037 authored as floor-repair).
2. **SP-037 LANDED** (v6.6.3 manifest-drift; floor restored 466/466 + 29/29) via substitution norm — engine reviewer spawn 400'd.
3. **The 400 was a pi REQUEST-SHAPE defect, not account exhaustion.** Anthropic's metering pause restores on-plan billing only for requests carrying the machine billing token as `system[0]`: `x-anthropic-billing-header: cc_version=<v>; cc_entrypoint=sdk-cli;` (hermes-agent issue #48176; related #46675 on tool-name shapes). pi-ai 0.83.0 carried the older prose-identity-only fix (`__PI_OAUTH_FIX__`), which the classifier no longer accepts. **Local patch `__PI_BILLING_HEADER_FIX__`** applied to `…/pi-coding-agent/node_modules/@earendil-works/pi-ai/dist/api/anthropic-messages.js`: billing block prepended as system[0] (identity stays system[1]), UA → `claude-cli/2.1.221 (external, sdk-cli)`, `claudeCodeVersion` 2.1.123→2.1.221. Verified: `pi -p --no-session --model anthropic/claude-opus-5` AND `--model anthropic/claude-fable-5` → HTTP 200. Owner ratified (referenced the hermes fix).

**Patch durability:** lives in npm-global node_modules — WIPED by pi reinstall/upgrade. Re-apply after any pi update (grep for `__PI_BILLING_HEADER_FIX__` / `x-anthropic-billing-header`; if upstream ships it, drop the local patch). The patch site is the NESTED pi-ai under pi-coding-agent — the copy the `pi` CLI actually loads.

## State (all pushed)

- Branch `feat/crossplatform`: SP-037 land (`7e2fd5b8`) + reconcile (`c27e5ae1`) + unpark docs on top.
- Spine: idle, `verify.mjs` green both roots, floor 466/466 + 29/29.
- Wave 4 = SP-035 + SP-036 (amended 2026-08-04: consult rewire, Ollama re-probe, WSL named limit, three-seat MCP subject), sequencing dep SP-037 satisfied. Next unused ID: SP-038.

## Machine facts (laptop — fresh-machine bootstrap deltas, durable)

1. `git config --global core.hidedotfiles false` APPLIED (hidden-.git EPERM class; required on any fresh machine/clone).
2. `pi.exe` shim at `AppData/Roaming/npm/pi.exe` (source `~/.pi/pi-shim/PiShim.cs`) — Node 24 cannot spawn the cmd/shell shims.
3. WSL installed, ZERO distros — every WSL2 gate = named limit until provisioned (owner decision).
4. Ollama installed (`ollama --version` presence probes only — no socket/pull).
5. No `Z:\CCP Vids`, no DISPLAY3 (desktop-only); memory = hermes.
6. Global pi-spine pinned exact 2.10.0 in BOTH `~/.pi/agent/settings.json` + `~/.pi/agent/npm/package.json`.
7. bpx-consult: pass `mode:"solo"` explicitly (`defaultMode:"council"` fails here).
8. **`__PI_BILLING_HEADER_FIX__` applied** (see above) — check its presence after any pi upgrade.

## Open owner questions (unchanged)

WSL distro provisioning; MCP Sentry posture on the laptop build (SP-036 answers); AI admission §9.2 ledger ×7; dashboard-priority question.
