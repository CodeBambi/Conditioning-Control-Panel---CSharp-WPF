# STATUS: SP-024 — DTRH host slice b2: save slots, picker/quick start, protocol v1

**Current Step:** Step 1 — slots/picker/protocol archaeology + design + pre-approach consult
**Last Updated:** 2026-07-21 (authored)

### Step 1: Archaeology + design + pre-approach consult
**Status:** ⬜ Not Started

- [ ] STATUS.md updated before starting work
- [ ] WPF archaeology (READ-ONLY, `File.cs:line`): three-slot model, save picker, quick start, protocol usage
- [ ] Payload `protocol.js` archaeology (READ-ONLY): full v1 vocabulary + per-direction mapping
- [ ] Design (slot store on SP-005, protocol dispatcher + tolerance decision, picker/quick-start surface)
- [ ] Pre-approach solo Fable 5 consult (verdict + actual answering model in record.md BEFORE checkbox)

### Step 2: Three local save slots (SP-005 machinery)
**Status:** ⬜ Not Started

- [ ] `Features/Dtrh/DtrhSaveSlots.cs`: three slots — create/select/persist/delete; schema-versioned, quarantine, journal, unknown-member preserve
- [ ] Unit tests: lifecycle across reloads, corruption → quarantine + flagged defaults, ordering, empty-slot

### Step 3: Protocol v1 full vocabulary
**Status:** ⬜ Not Started

- [ ] `Features/Dtrh/DtrhProtocol.cs`: full v1 message set, per-direction dispatch, typed outcomes, unknown-message tolerance
- [ ] Unit tests: every message round-trips (shapes match payload sources); tolerance proven (typed, logged, no crash, no silent drop)

### Step 4: Picker + quick start + headed/WX evidence + board reconciliation + pre-completion consult
**Status:** ⬜ Not Started

- [ ] Save picker + quick start in the host window (SP-004 owned operations)
- [ ] **Windows headed evidence ON DISPLAY3 (GetWindowRect-verified):** picker flows, quick start end-to-end, restart persistence proof; K3 where pixels matter
- [ ] WSL2 in-packet gate (`~/ccp-sp024`, never /mnt/e): contract green; WX render facts; protocol round-trips over Linux transport; no timing claims; Wayland untouched
- [ ] record.md complete
- [ ] Pre-completion solo Fable 5 consult (verdict in record.md)
- [ ] Host row → `WIP` with slice-b2 evidence + named limits (never `DONE`)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
**Status:** ⬜ Not Started

- [ ] Contract testCommand green (0W/0E incl. `-t:Rebuild`; both test projects)
- [ ] `git diff --check` clean
- [ ] `git status --short` = File Scope only
