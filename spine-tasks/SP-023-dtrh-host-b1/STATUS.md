# STATUS: SP-023 — DTRH host slice b1: shell, origins, transport, boot matrix

**Current Step:** Step 1 — FIRST GATE: NativeWebDialog invokeCSharpAction proof + host design + pre-approach consult
**Last Updated:** 2026-07-21 (authored)

### Step 1: FIRST GATE + host design + pre-approach consult
**Status:** ⬜ Not Started

- [ ] STATUS.md updated before starting work
- [ ] **FIRST CHECKBOX:** `invokeCSharpAction` page→host on NativeWebDialog — WSLg proof, binary verdict + transcript (PROVEN or poll-both-ways fallback named-limit)
- [ ] WPF DTRH host archaeology (READ-ONLY, `File.cs:line`)
- [ ] Host design (per-platform shape, capability states, payload serving class + rationale, bridge.js provenance plan)
- [ ] Pre-approach solo Fable 5 consult (verdict + actual answering model in record.md BEFORE checkbox)

### Step 2: Host shell + package integration
**Status:** ⬜ Not Started

- [ ] csproj: WebView pinned per admission §1 + app.manifest supportedOS; build 0W/0E both platforms
- [ ] `Features/Dtrh/DtrhHostWindow.axaml(.cs)`: Windows embedded + Linux NativeWebDialog; typed capability states; composition root + SP-004 owned lifecycle
- [ ] Payload through SP-009 manifest (`--verify-assets` green Debug+Release); bridge.js derivative + transport diff (hashes recorded)

### Step 3: Loopback origin serving + inbox endpoint
**Status:** ⬜ Not Started

- [ ] §4 origin server: two GET-only localhost origins, overlay-first, Range, MIME allowlist, CORS preflight, traversal refusal, sensitive-logging ban
- [ ] §3.3 inbox: long-poll `GET /bridge/inbox?after=N`, monotonic seq, retain-until-ack, per-session unguessable token in route path
- [ ] Unit tests: contract, inbox seq/ack/timeout/token, transport diff shape

### Step 4: Boot matrix (WH) + WSLg gate (WX) + board reconciliation + pre-completion consult
**Status:** ⬜ Not Started

- [ ] Windows headed boot matrix in-product: engine live, pixel-checked render, both-direction transports, preBuffer replay, autoplay, focus-claim, exit 0
- [ ] WSL2 in-packet gate (`~/ccp-sp023`, never /mnt/e): contract green; NativeWebDialog render facts; FIRST-GATE transport path exercised; no timing claims; Wayland untouched
- [ ] record.md complete
- [ ] Pre-completion solo Fable 5 consult (verdict in record.md)
- [ ] Host row → `WIP` with slice-b1 evidence + named limits (never `DONE`)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
**Status:** ⬜ Not Started

- [ ] Contract testCommand green (0W/0E incl. `-t:Rebuild`; both test projects)
- [ ] `git diff --check` clean
- [ ] `git status --short` = File Scope only
