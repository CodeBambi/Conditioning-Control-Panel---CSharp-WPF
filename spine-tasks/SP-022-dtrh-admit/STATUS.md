# STATUS: SP-022 — admit DTRH browser and origin design

**Current Step:** Complete — .DONE
**Last Updated:** 2026-07-21 (worker, all steps complete)

### Step 1: Pin re-verification + transport design + pre-approach consult
**Status:** ✅ Complete

- [x] STATUS.md updated before starting work
- [x] Package pin re-confirmed (live feed + spike restore/build re-run Windows AND WSL2 `~/ccp-sp022`); Linux native deps restated + apt-source check
- [x] Transport design: minimal transport-only diff spec + Linux host→page shape DECIDED (poll-endpoint vs navigation vs named-limit)
- [x] Pre-approach solo Fable 5 consult (verdict + actual answering model in record.md BEFORE checkbox)

### Step 2: Admission record + host slice cut
**Status:** ✅ Complete

- [x] `client/docs/dtrh-admission.md`: package pin, Linux natives, transport diff spec + per-direction matrix, loopback security contract (approved-by-decree), no-classic-fallback, Wayland named limit, payload hashes referenced
- [x] Host slice cut b1…b5 with per-slice acceptance mapping + evidence classes

### Step 3: Board reconciliation + record + pre-completion consult
**Status:** ✅ Complete

- [x] record.md complete
- [x] Pre-completion solo Fable 5 consult (verdict in record.md; 2 corrections applied: MIME sweep pinned, b3 compositing divergence named)
- [x] Admit row → `WIP` with evidence + named limits (never `DONE`); host row annotated "first slice = SP-023"
- [x] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
**Status:** ✅ Complete

- [x] Contract testCommand green (client 0W/0E + both test projects — pollution guard; Windows 213/213 + 22/22, WSL2 213/213 + 22/22)
- [x] `git diff --check` clean
- [x] `git status --short` = File Scope only
