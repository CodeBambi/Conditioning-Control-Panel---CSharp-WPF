# STATUS: SP-020 — durable pi-spine local-patch mechanism (T-1)

**Current Step:** Step 2 — manifest + apply + verify
**Last Updated:** 2026-07-21 (Step 1 in progress)

### Step 1: Empirical patch inventory + pre-approach consult
**Status:** 🔵 In Progress

- [x] STATUS.md updated before starting work
- [x] Pristine scratch install (`%TEMP%/sp020-scratch` @2.8.0 == live version; latest 2.10.0 drift-checked) + full recursive diff vs live `.pi/npm/node_modules/pi-spine`; EVERY delta classified in record.md (2 fsync PRESENT, dotnet-allowlist LOST, @file-tail LOST, SKILL.md T-11 amendment PRESENT-undocumented, journal.mjs upstream-OK, windowsHide ABSENT/not load-bearing)
- [x] T-12 local-patch feasibility decision: STAYS UPSTREAM (locations: git-helpers.mjs:22 `--no-index`, merge.mjs ~205-222 `git rm --cached` fallback, diagnosis-merge-failure.mjs repair command)
- [x] Pre-approach solo Fable 5 consult (verdict + actual answering model in record.md BEFORE checkbox)

### Step 2: Manifest + apply + verify
**Status:** 🔵 In Progress

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
