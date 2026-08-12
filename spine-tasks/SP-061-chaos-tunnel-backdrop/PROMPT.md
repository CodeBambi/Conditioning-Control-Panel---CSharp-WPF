# Task: SP-061 — Chaos tunnel backdrop (opaque below-Topmost web surface)

## Mission

Port the endless rabbit-hole backdrop that renders **under the whole Chaos game**: upstream's `ChaosTunnelService` hosts a WebView2 on a local three.js page in an **opaque** window that sits **deterministically BELOW every Topmost bubble/FX/video/HUD window**. SP-056's tree inventory surfaced it — the b1–b5 DTRH slice cut never enumerated it, and the DTRH host row now carries a ratification qualifier naming this omission, so this row unblocks that one.

**The load-bearing part is the layering contract, not the page.** The page is upstream payload (`Resources/web/tunnel/` 9 files + the shared `Resources/web/vendor/three` consumed only by tunnel's import map). What must be ported and proven is: an opaque own-lifecycle window that is always below every Topmost surface, never steals focus or activation, and never becomes the top window when the game's own windows appear.

**Binding framings:**
(a) **TWO files named `ChaosTunnelService.cs` exist. Only one is evidence.** `ConditioningControlPanel/Chaos/ChaosTunnelService.cs` is the shipping WPF product = **behavioral truth**. `ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/Services/Chaos/ChaosTunnelService.cs` is the FIRST Avalonia attempt = **lessons/failure evidence only**. Read the second only to record ACCEPT/ADAPT/REJECT lessons; never import its classes, window topology, timers, or service shape. Say in `record.md` which facts came from which.
(b) **`tunnel` the surface ≠ `tunnel.js` the DTRH module.** The manifest already carries `dtrh.payload/engine/tunnel.js` (a DTRH-internal module landed in b1–b5). This task is the standalone `Resources/web/tunnel/` tree. Confusing them would "prove" work already done.
(c) **Z-order is proven by pixels, not by a property.** Setting a window non-topmost is not evidence. The proof is a real Topmost surface (an existing DTRH/companion window) visibly occluding the tunnel in a capture, plus the reverse case (tunnel visible where nothing covers it), with the window rects recorded.
(d) **Opaque is the design, not a limitation.** Upstream is opaque because a WebView2 child HWND forces it. Do not invent transparency/click-through here — that is the FYP/overlay class and a different contract (wave-16 decomposition consult: deliberately not bundled).
(e) **Timing discipline is now standing law (SP-059, `docs/constitution.md`).** Every new test obeys `TestTimingGuardTests`: no new hard-coded deadline literals, no `Task.Delay` waits outside `TestWait`. **Also inherited: injected timeout BUDGETS** (`RequestTimeout = TimeSpan.From…` style options handed to product code) are the fourth-occurrence row's subject — do not add new ones; if a bounded budget is unavoidable, name it in `record.md` so the budgets row can sweep it.
(f) **Profile isolation (SP-057) is mandatory for headed runs:** any WebView2 `UserDataFolder` or persisted state rides `CCP_DATA_ROOT`; prove the real profile is byte-identical after the headed run.
(g) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, or `client/docs/upstream-sync.md`** — name intended filings in `record.md`; the orchestrator reconciles at land.

## Dependencies

- none (wave-18 runs alone per the wave-16/17 decomposition consults — an M product + headed surface deserves the slot)

## Context to Read First

- `client/docs/task-board.md` — the row "Chaos tunnel backdrop surface (`tunnel` + `vendor` payload trees, v6.7.x)" (READ-ONLY; its acceptance is this task's acceptance) and the DTRH host row's ratification qualifier that this row discharges
- `ConditioningControlPanel/Chaos/ChaosTunnelService.cs` — the WPF behavioral truth: lifecycle (who creates/destroys it and when), window style/state, z-order mechanism, focus/activation handling, what it does on game start/stop
- `ConditioningControlPanel/Resources/web/tunnel/**` (9 files: `index.html`, `main.js`, `tunnel.js`, `camera.js`, `fx.js`, `zones.js`, `powerups.js`, `quality.js`, `audio.js`) and `ConditioningControlPanel/Resources/web/vendor/three/**` — verify the import-map claim yourself (which vendor files the page actually resolves)
- `spine-tasks/SP-023-dtrh-host-b1/record.md` — the host-shell precedent: probed typed capability (`dtrh-webview-embedded` Windows / `dtrh-web-dialog` Linux), the §4 loopback origin discipline (GET-only, overlay-first, MIME deny-by-default, traversal refusal, sensitive-logging ban), and payload serving through SP-009's manifest
- `spine-tasks/SP-027-dtrh-host-b5/record.md` — watchdog/graceful-exit/stale-profile-lock classes that any second WebView2 host inherits or explicitly disclaims
- `client/docs/window-behavior-manifest.md` — the per-window behavior rows (W-xx) this window must be described in the language of
- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhHostWindow.axaml.cs` + `DtrhVideoWindow.axaml` — the landed host + a real Topmost surface to layer against
- `client/docs/asset-manifest.md` + `client/tools/verify/**` — how new payload trees enter the manifest and are `--verify-assets` checked
- `spine-tasks/SP-059-timing-discipline/record.md` §4 — `TestWait` and the guard's allowlist discipline your tests must satisfy

## File Scope

- `client/src/CcpClient.Desktop/Features/Chaos/**` (new — the tunnel window/service)
- `client/src/CcpClient.Desktop/Lifecycle/**`, `Program.cs`, `App.axaml.cs` — **wiring only** (registration + any harness flag), documented per-file in `record.md`
- `client/assets/**` + the asset manifest source of truth (the tunnel + vendor payload entries)
- `client/tests/CcpClient.Tests/**`, `client/tests/CcpClient.HeadlessTests/**`
- `client/docs/window-behavior-manifest.md` — add this window's row(s) only
- `spine-tasks/SP-061-chaos-tunnel-backdrop/**`
- **NOT in scope:** `ConditioningControlPanel/**` (read-only both trees), `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `client/spikes/**`, `client/CcpClient.sln` (unless a NEW project is genuinely required — then say why in `record.md` before touching it)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/window-behavior-manifest.md` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `client/spikes/**` |
| artifactsMustExist | `spine-tasks/SP-061-chaos-tunnel-backdrop/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Dual-source archaeology + layering design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] **WPF truth** (`Chaos/ChaosTunnelService.cs`, `File.cs:line` per fact): creation/destruction trigger, window style/state/size/monitor choice, the exact z-order mechanism, focus/activation policy, what happens when the game's Topmost windows appear and disappear, and any settings that gate it
- [ ] **First-attempt lessons** (the `CCP.Avalonia.Desktop.Windows` copy): ACCEPT/ADAPT/REJECT list only — explicitly state what you are NOT taking
- [ ] **Payload facts**: enumerate the tunnel tree and verify which `vendor/three` files the page actually resolves (import map / network graph), so the manifest addition is derived, not guessed. State explicitly how this differs from the DTRH-internal `engine/tunnel.js` already in the manifest
- [ ] Design: the window (opaque, own lifecycle, non-activating, always-below-Topmost), the platform split (Windows embedded WebView2 per SP-023's probed capability; Linux = `NativeWebDialog` with the z-order honesty question answered, not assumed), serving through the §4 loopback origin, and the `CCP_DATA_ROOT`-rooted profile
- [ ] **Pre-approach solo consult** (`mode: "solo"` — bare `consult` hits the council-roster trap, T-7; Opus 5 main, Fable 5 fallback); verdict + **ACTUAL answering model** in record.md

### Step 2: Implement the window + serving + manifest

- [ ] Implement the tunnel window/service under `Features/Chaos/**` with typed capability probing (never a silent fallback); it must be creatable, showable, and destroyable without disturbing existing windows
- [ ] Serve the tunnel + vendor payload through the existing manifest + loopback discipline (GET-only, MIME deny-by-default, traversal refusal, no filename logging beyond presence/shape)
- [ ] Manifest: add the derived payload entries; `--verify-assets` green in Debug **and** Release
- [ ] Tests (obeying the timing guard): z-order/activation policy at the unit level where the API allows, capability typing, serving route behavior incl. a negative control, manifest two-direction validation. **No new deadline literals; no new injected timeout budgets** (or name them in record.md)

### Step 3: Headed layering evidence (the row's real acceptance)

- [ ] Windows headed run under `CCP_DATA_ROOT` (SP-057): tunnel window live with the three.js page **actually rendering** (pixel evidence, not "engine live")
- [ ] **The layering proof, both directions:** (i) a real Topmost surface (DTRH video/host or companion window) shown over the tunnel → capture proves the Topmost surface occludes it; (ii) with nothing above it, the tunnel is visible. Record `GetWindowRect`-style rects for every window involved in each capture
- [ ] **Activation/focus proof:** showing the tunnel does not steal focus or activation from the window that had it, and the tunnel never rises above a Topmost surface after show/hide cycles (at least one cycle each way)
- [ ] Real profile **byte-identical** before/after the headed run (pre/post manifest + diff)
- [ ] Display convention: DISPLAY3 `(-2576,1091) 2560×1440` when present; **if absent, fall back LOUDLY and name it** (SP-057/SP-058 precedent — this laptop has repeatedly lacked it). Never fake a rect
- [ ] Linux disposition: run it if the environment allows; if WSL has zero distros (the standing machine limit), record the exact gate rather than a guess. **The Linux z-order answer is a named limit unless proven** — b3 already recorded Linux toplevel z-order as best-effort
- [ ] **A-013 advisory step (conditional):** at authoring the avalonia-live MCP was **not connectable on this machine** (`fetch failed`). Re-check once; if reachable, send small redacted AXAML snippets after official v12 research and record accepted/rejected findings; if not, record `Unavailable` — it never blocks the task and is never v12 authority

### Step 4: Record + window manifest + pre-completion consult

- [ ] Add the window's row(s) to `client/docs/window-behavior-manifest.md` in the existing W-xx language (behavior per field, not implementation)
- [ ] Write `record.md`: dual-source archaeology tables (WPF truth vs first-attempt lessons, clearly separated), payload/vendor derivation, design + rejected alternatives, the layering captures with rects, profile byte-identity, capability typing per platform, named limits, consults + ACTUAL models, engine-review presence, intended board filings (incl. whether the DTRH row's ratification qualifier is now dischargeable — state it, do not set it)
- [ ] **Pre-completion solo consult** (same route discipline); verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0, build 0W/0E, **≥863 unit / ≥33 headless**, TRX attached). A red of 862 must be named before it is discussed — the known cold-start site is `LoopbackOllamaProviderTests.Truncated_PrefixCut_NeverSurfaced_TypedUnavailable`, and no other red may hide behind it
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- An opaque, own-lifecycle tunnel window rendering the real three.js payload, served through the existing manifest + loopback discipline, with typed per-platform capability (never a silent fallback)
- **Pixel-proven layering both directions** plus an activation/focus proof and at least one show/hide cycle per direction, with window rects recorded
- Real user profile byte-identical after the headed run
- `--verify-assets` green Debug and Release; manifest entries derived from the page's actual resolution, not guessed
- Window manifest row(s) added; Linux disposition and any absent-display gate named honestly
- Contract green at or above the 863/33 floor; both consults persisted with actual answering models

## Do NOT

- Copy the first Avalonia attempt's tunnel service, window topology, or timers; conflate `Resources/web/tunnel/` with the DTRH `engine/tunnel.js`
- Claim layering from a property, a handler, or "engine live" — pixels and rects or it did not happen
- Add transparency, click-through, or overlay input behavior (different contract, different row); steal focus/activation; make the tunnel Topmost "temporarily"
- Add new hard-coded deadline literals or injected timeout budgets in tests (standing order, `docs/constitution.md`); weaken any assertion or the guard's allowlist
- Write outside `CCP_DATA_ROOT` in a headed run; fake a DISPLAY3 rect; claim Wayland; claim Linux z-order without proof
- Edit the three hot docs, `docs/constitution.md`, `.spine/**`, `.pi/**`, `ConditioningControlPanel/**`; set any board row state
- Use `consult` council mode (T-7: solo only)

## Git Commit Convention

- `feat(SP-061): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `client/docs/window-behavior-manifest.md`, `spine-tasks/SP-061-chaos-tunnel-backdrop/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`

## Amendments

- 2026-08-12 (authoring, orchestrator): wave 18, **single lane by consult decision** — the wave-16 consult sized it standalone (deliberately not bundled with FYP: opaque-below-Topmost and transparent-click-through-above are opposite ends of the layering contract), and the wave-17 consult deferred it one wave so it would be written **under** SP-059's timing guard rather than beside it. Sequencing paid off in reverse too: SP-059's landed guard now binds this packet's tests. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-12 (authoring, orchestrator): machine posture at authoring — avalonia-live MCP **not connectable** (`fetch failed`); DISPLAY3 absent on this laptop in the last two waves; WSL zero distros. All three are named limits with loud-fallback handling, never blockers, never faked.
