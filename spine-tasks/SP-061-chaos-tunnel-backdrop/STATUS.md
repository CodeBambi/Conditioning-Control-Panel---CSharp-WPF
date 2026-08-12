## STATUS: SP-061 — Chaos tunnel backdrop (opaque below-Topmost web surface)
**Current Step:** complete (all 5 steps done; .DONE pending final commit)
**Last Updated:** 2026-08-12 (worker, Step 5 complete — 892/892 + 35/35, verify-assets Debug+Release, contract green)
**Blockers:** none

### Step 1: dual-source archaeology + layering design + pre-approach consult — COMPLETE (plan review pending)
- [x] Update STATUS.md before starting work
- [x] WPF truth from `Chaos/ChaosTunnelService.cs` (lifecycle, style, z-order mechanism, focus policy) with `File.cs:line`
- [x] First-attempt ACCEPT/ADAPT/REJECT list (never imported as design)
- [x] Payload + vendor derivation (what the page actually resolves); distinguished from DTRH `engine/tunnel.js`
- [x] Design: opaque non-activating below-Topmost window, platform split, §4 serving, `CCP_DATA_ROOT` profile
- [x] Pre-approach solo consult (verdict + ACTUAL model in record.md)

### Step 2: implement the window + serving + manifest — COMPLETE (plan review engine-skipped, T-2 recorded)
- [x] Tunnel window/service under `Features/Chaos/**` with typed capability probing
- [x] Payload served through manifest + loopback discipline (deny-by-default, no filename logging)
- [x] Manifest entries added; `--verify-assets` green Debug + Release
- [x] Tests obeying the timing guard (no new deadline literals or injected budgets)
- **File-Scope amendment (documented):** `CcpClient.Desktop.csproj` — two linked Content globs (the SP-023/SP-054 wiring-only class; named in record.md Step 2 + the Step-2 commit body)

### Step 3: headed layering evidence — COMPLETE (plan review pending)
- [x] Tunnel live with the three.js page actually rendering (pixels)
- [x] Layering proof both directions with window rects
- [x] Activation/focus proof + show/hide cycles
- [x] Real profile byte-identical under `CCP_DATA_ROOT`
- [x] DISPLAY3 or a LOUD named fallback; Linux disposition honest
- [x] A-013 advisory re-check (MCP unreachable at authoring — record Unavailable if still down)

### Step 4: record + window manifest + pre-completion consult — COMPLETE (plan review pending)
- [x] `client/docs/window-behavior-manifest.md` row(s) added
- [x] record.md complete (dual-source tables, captures, limits, intended filings)
- [x] Pre-completion solo consult (verdict + ACTUAL model)
- [x] STATUS.md accurate before .DONE

### Step 5: Testing & Verification — COMPLETE
- [x] Contract testCommand passes (verify.mjs 0, 0W/0E, ≥863 unit + ≥33 headless, TRX) — 892/892 + 35/35; the one flake red named, base-reproduced (20542b99), fixed (DataRootOverrideTests two-checkpoint guard), 4 consecutive greens post-fix
- [x] `git diff --check` clean
- [x] `git status --short` shows only File Scope paths (+ documented csproj amendment)
