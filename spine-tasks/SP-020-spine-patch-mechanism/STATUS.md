# STATUS: SP-020 — durable pi-spine local-patch mechanism (T-1)

**Current Step:** Step 1 — empirical patch inventory + pre-approach consult
**Last Updated:** 2026-07-21 (authored)

### Step 1: Empirical patch inventory + pre-approach consult
**Status:** ⬜ Not Started

- [ ] STATUS.md updated before starting work
- [ ] Pristine scratch install (`%TEMP%/sp020-scratch`) + full recursive diff vs live `.pi/npm/node_modules/pi-spine`; EVERY delta classified (patch/noise/version-drift; windowsHide presence + load-bearing)
- [ ] T-12 local-patch feasibility decision with code location
- [ ] Pre-approach solo Fable 5 consult (verdict + actual answering model in record.md BEFORE checkbox)

### Step 2: Manifest + apply + verify
**Status:** ⬜ Not Started

- [ ] `.spine/patches/manifest.json` (anchor-based, machine-readable, rationale links)
- [ ] `.spine/patches/apply.mjs` (idempotent, loud anchor-miss failure, all-or-nothing)
- [ ] `.spine/patches/verify.mjs` (per-patch applied/missing/drifted, exit 0 only all-applied)
- [ ] `.spine/patches/README.md` (re-apply trigger, honest automation limit)

### Step 3: Scratch-cycle evidence (OUTSIDE the repo)
**Status:** ⬜ Not Started

- [ ] `%TEMP%/sp020-scratch2`: fresh install → negative control (patches absent) → apply → verify exit 0 → scratch `spine preflight` GREEN → dotnet-allowlist proven → >16KB-tail STUB batch → transcripts in record.md
- [ ] Idempotence: apply → apply → verify

### Step 4: Board reconciliation + record + pre-completion consult
**Status:** ⬜ Not Started

- [ ] record.md complete
- [ ] Pre-completion solo Fable 5 consult (verdict in record.md)
- [ ] T-1 row → `WIP` with evidence + named limits (post-land real-reinstall gate; automation limit; T-12 decision) — never `DONE`
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
**Status:** ⬜ Not Started

- [ ] Contract testCommand green (client 0W/0E + both test projects — pollution guard)
- [ ] `git diff --check` clean
- [ ] `git status --short` = File Scope only
