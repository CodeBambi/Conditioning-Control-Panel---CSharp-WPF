# STATUS: SP-023 — DTRH host slice b1: shell, origins, transport, boot matrix

**Current Step:** COMPLETE (all 5 steps done; .DONE created)
**Last Updated:** 2026-07-21 (Step 5 verified; contract green both platforms)

### Step 1: FIRST GATE + host design + pre-approach consult
**Status:** ✅ Complete

- [x] STATUS.md updated before starting work
- [x] **FIRST CHECKBOX:** `invokeCSharpAction` page→host on NativeWebDialog — **PROVEN** on WSLg 2026-07-21 (spike `--dialog --page probe`, `page->host: {"type":"probe-p2h-ica"...}` arrived via dialog `WebMessageReceived`; transcript `evidence/first-gate-dialog-ica.log`). Linux page→host = invokeCSharpAction per §3.2 — no fallback needed
- [x] WPF DTRH host archaeology (READ-ONLY, `File.cs:line`) — ChaosWebViewHost.cs / DtrhHostService.cs / bridge.js / boot.js citations in record.md
- [x] Host design (per-platform shape, capability states, payload serving class + rationale, bridge.js provenance plan) — record.md Step 1
- [x] Pre-approach solo Fable 5 consult (verdict + actual answering model in record.md BEFORE checkbox) — 4 corrections folded in (query-strip logging; probe = Step-2 research; injectable long-poll timeout; reload semantics)

### Step 2: Host shell + package integration
**Status:** ✅ Complete

- [x] csproj: WebView pinned per admission §1 + app.manifest supportedOS; build 0W/0E both platforms
- [x] `Features/Dtrh/DtrhHostWindow.axaml(.cs)`: Windows embedded + Linux NativeWebDialog; typed capability states; composition root + SP-004 owned lifecycle
- [x] Payload through SP-009 manifest (`--verify-assets` green Debug+Release, Windows+WSL2); bridge.js derivative + transport diff (hashes recorded)

### Step 3: Loopback origin serving + inbox endpoint
**Status:** ✅ Complete

- [x] §4 origin server: two GET-only localhost origins, overlay-first, Range, MIME allowlist, CORS preflight, traversal refusal, sensitive-logging ban
- [x] §3.3 inbox: long-poll `GET /bridge/<token>/inbox?after=N`, monotonic seq, retain-until-ack, per-session unguessable token in route path
- [x] Unit tests: contract, inbox seq/ack/timeout/token, transport diff shape (245/245 both platforms)

### Step 4: Boot matrix (WH) + WSLg gate (WX) + board reconciliation + pre-completion consult
**Status:** ✅ Complete

**Discoveries (worker, 2026-07-21):** File Scope expanded by 4 wiring files — see PROMPT.md Amendments (2026-07-21 worker entry). `fileScopeMustNotChange` untouched.

- [x] Windows headed boot matrix in-product: engine live, pixel-checked render, both-direction transports, preBuffer replay, autoplay, focus-claim (`hasFocus()=true`), ESC-hold exit → **EXIT=0** (`evidence/wh/`)
- [x] WSL2 in-packet gate (`~/ccp-sp023`, never /mnt/e): contract green (245/245 + 22/22, 0W/0E); NativeWebDialog render facts (xwd `evidence/wx/wx-render.png`); FIRST-GATE transport path exercised (invokeCSharpAction round-trip + inbox DELIVERED + retained replay); **engine live on Linux**; exit 0 via timed close; no input automation; no timing claims; Wayland untouched
- [x] record.md complete (FIRST-GATE verdict + transcript, archaeology, design, consults, engine-review log, boot matrix transcripts, surprise ledger)
- [x] Pre-completion consult (solo ×2 content-filter blocked, recorded; gut-check succeeded: NO fix-first — verdict in record.md)
- [x] Host row → `WIP` with slice-b1 evidence + named limits (never `DONE`)
- [x] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
**Status:** ✅ Complete

- [x] Contract testCommand green (Windows: sln 0W/0E on `-t:Rebuild`, 245/245 + 22/22; WSL2: 0W/0E, 245/245 + 22/22 — both on the final tree)
- [x] `git diff --check` clean
- [x] `git status --short` = File Scope only (untracked `.pi/loops/*.json` is engine-owned, never committed)
