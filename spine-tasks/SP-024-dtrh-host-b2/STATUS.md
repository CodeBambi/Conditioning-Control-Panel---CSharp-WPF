# STATUS: SP-024 — DTRH host slice b2: save slots, picker/quick start, protocol v1

**Current Step:** done — all steps complete
**Last Updated:** 2026-07-21 (Step 1 in progress)

### Step 1: Archaeology + design + pre-approach consult
**Status:** ✅ Complete

- [x] STATUS.md updated before starting work
- [x] WPF archaeology (READ-ONLY, `File.cs:line`): three-slot model, save picker, quick start, protocol usage
- [x] Payload `protocol.js` archaeology (READ-ONLY): full v1 vocabulary + per-direction mapping (bridge.js/boot.js — no protocol.js file exists; the protocol sources are bridge.js + send/handler sites)
- [x] Design (slot store on SP-005, protocol dispatcher + tolerance decision, picker/quick-start surface)
- [x] Pre-approach solo Fable 5 consult (verdict + actual answering model in record.md BEFORE checkbox)

### Step 2: Three local save slots (SP-005 machinery)
**Status:** ✅ Complete

- [x] `Features/Dtrh/DtrhSaveSlots.cs`: three slots — create/select/persist/delete; schema-versioned, quarantine, journal, unknown-member preserve
- [x] Unit tests: lifecycle across reloads, corruption → quarantine + flagged defaults, ordering, empty-slot

### Step 3: Protocol v1 full vocabulary
**Status:** ✅ Complete

- [x] `Features/Dtrh/DtrhProtocol.cs`: full v1 message set, per-direction dispatch, typed outcomes, unknown-message tolerance
- [x] Unit tests: every message round-trips (shapes match payload sources); tolerance proven (typed, logged, no crash, no silent drop)

### Step 4: Picker + quick start + headed/WX evidence + board reconciliation + pre-completion consult
**Status:** ✅ Complete

- [x] Save picker + quick start in the host window (SP-004 owned operations)
- [x] **Windows headed evidence ON DISPLAY3 (GetWindowRect-verified):** picker flows, quick start end-to-end, restart persistence proof; K3 where pixels matter
- [x] WSL2 in-packet gate (`~/ccp-sp024`, never /mnt/e): contract green; WX render facts; protocol round-trips over Linux transport; no timing claims; Wayland untouched
- [x] record.md complete
- [x] Pre-completion solo Fable 5 consult (verdict in record.md; 2 fix-first items fixed: select-by-click proof, degraded-slot lock visibility)
- [x] Host row → `WIP` with slice-b2 evidence + named limits (never `DONE`)
- [x] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
**Status:** ✅ Complete

- [x] Contract testCommand green (0W/0E incl. `-t:Rebuild`; both test projects)
- [x] `git diff --check` clean
- [x] `git status --short` = File Scope only
