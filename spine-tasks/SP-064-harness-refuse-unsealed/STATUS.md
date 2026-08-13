## STATUS: SP-064 — Harness entry points must REFUSE to run unsealed
**Current Step:** 4 (in progress)
**Last Updated:** 2026-08-13 (worker, lane-1)
**Blockers:** none

### Step 1: enumerate every entry point, classify it, design the gate — COMPLETE
- [x] Update STATUS.md before starting work
- [x] Disposition table built from the tree (flag, file:line, class 1-4, reason) — the Mission's list is a starting point, not the answer
- [x] Per class-1 entry: what it writes unsealed. Per class-2 entry: why a human running it unsealed is legitimate
- [x] Gate design: insertion point, message text, exit code, registry shape, `--verify-assets --dtrh-m2test` consequence named
- [x] Guard design: literal surface scanned, failure mode, why it cannot skip
- [x] Pre-approach solo consult (T-7: `mode: "solo"`) — verdict + ACTUAL answering model

### Step 2: implement the gate, the registry, and the guard — COMPLETE
- [x] One registry consumed by the gate; no second copy of the classification
- [x] Gate after the SP-057 override block, before composition-root construction; stderr names `CCP_DATA_ROOT`; non-zero exit; no host, no window
- [x] Refusal pin table-driven over the registry; allow direction pinned for classes 2/3/4
- [x] Guard test mirroring `DataRootChokePointGuardTests`; RED captured with an unclassified literal, then removed
- [x] Any process-env-mutating test joins `ProcessEnvCollection` (SP-062) — N/A by design: no test in this packet mutates process env (consult guidance: no in-process Program.Main call; wiring pinned by the guard's source-shape assertion)

### Step 3: real-process evidence — the pin is not the proof — COMPLETE
- [x] (a) class-1 flag, `CCP_DATA_ROOT` unset → non-zero exit, stderr names the variable, no window
- [x] (b) real profile path-hashed manifest before/after (a), both directions, with SP-057's positive controls
- [x] (c) same flag sealed → not refused, override line observed, bounded by auto-close
- [x] (d) plain launch unsealed → not refused, window rect-verified, exit 0, profile delta reported honestly
- [x] 3 consecutive full-suite greens, ≥1 fresh-checkout first-ever build, TRX attached, per-run table incl. skipped column

### Step 4: record + pre-completion consult — IN PROGRESS
- [ ] record.md complete (table, gate, registry, guard RED, four process runs, 3-run suite table with the new exact floor, intended filings)
- [ ] Honesty cell: the demo+auto-close residual hole, non-data-root writes, guard surface bounds, non-`Main` harness paths, Linux unproven
- [ ] Pre-completion solo consult (verdict + ACTUAL model)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification — NOT STARTED
- [ ] Contract testCommand passes (verify.mjs 0, 0W/0E, new exact unit count / 35 headless, 0 skipped, TRX)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

### Discoveries
- The Mission's class-4 list named `--no-video-title-show` a startup modifier; the tree shows it is a LibVLC constructor arg (LibVlcDtrhVideo.cs:144), never a program arg. It + 4 sibling non-startup literals get the registry's `NotAStartupFlag` bucket so the guard binds them.
- Consult tool returned reasoning only (no final verdict, no answering-model attribution) — same shape as the authoring consult; recorded in record.md, guidance extracted and followed.
- `--avatar-corrupt-demo` classified class 2 (in-memory-only corruption; consult-confirmed); named in the honesty cell.
- New exact floor: **897 unit / 35 headless, 0 skipped** (892 + 5 new facts: 4 gate + 1 guard). First interim runs: unit 897/0/0, headless 35/0/0 (step2 logs + TRX in evidence/).
- Guard RED captured: `evidence/guard-red.txt` — unclassified `--sp064-red-probe` failed the guard with file:line; probe file then deleted. First guard run also caught the registry's own doc comments quoting `"--..."` — reworded to `--flag` (the guard binds its own registry file, as designed).
