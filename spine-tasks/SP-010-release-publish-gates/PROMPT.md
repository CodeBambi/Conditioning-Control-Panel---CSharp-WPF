# Task: SP-010 — establish Release and publish gates

## Mission

Execute `client/docs/task-board.md` row 9 (**"Establish Release and publish gates"**, P0, Phase 1 of `spine-tasks/CONTEXT.md` — the final Phase 1 row). Deliver `client/docs/release-publish-gates.md` (contract) plus a **single version authority** plus a **publish + artifact-evidence matrix** proving the first vertical slice runs from Debug, Release, and PUBLISHED artifacts on Windows AND WSL2 Linux. Named publish strategy (pre-authoring consult, recorded with revisit trigger): **self-contained single-file per RID (`win-x64`, `linux-x64`)** — matches the WPF product's self-contained distribution shape and removes dotnet-runtime presence as a variable. Explicit exclusions: no installer/packaging work (no Inno; no board row), no auto-update mechanism (metadata shape documented only), no `PublishTrimmed` (Avalonia reflection), no `ReadyToRun` (optional, not needed for gates).

This packet also discharges TWO inherited named gates: (a) **row 8's deferred PUBLISH third** — the same `--verify-assets` invocation against the published artifact, zero new test logic; (b) **rows 2/3 headed Linux smoke (WSLg) debt** — and SP-007's WSLg graceful-close gap if the libX11 `WM_DELETE_WINDOW` ClientMessage path works (python3 ctypes, same mechanism as `client/tools/verify/xgetimage.py`; if it cannot be made to work, rename the gap honestly in the evidence).

## Dependencies

- **Task:** SP-008 (verification harness: tiers, capture scripts, named checks), SP-009 (`--verify-assets` self-check + publish hook), SP-007 via both (the slice being gated + WSLg gap list)

## Context to Read First

- `client/docs/task-board.md` row 9 + gate history (incl. rows 2/3/8 named-gate annotations)
- `client/docs/architecture.md` — A-014 (Release rule: Debug/Release/published artifacts are separate gates; YAGNI)
- `client/docs/architecture-proposal.md` — §6 (publish strategy = row 9's open decision — this packet decides it, recorded with revisit trigger)
- `client/docs/asset-manifest.md` — the row-9 publish hook (same `--verify-assets` invocation against the published artifact)
- `client/docs/verification-harness.md` — tier discipline; `client/tools/verify/` scripts (capture.ps1, capture-wslg.sh, xgetimage.py)
- `client/docs/startup-shutdown-contract.md` — phase trace + clean-exit evidence shape; `client/docs/persistence-migration-contract.md` — quarantine/Degraded path for the corrupt-settings run
- `spine-tasks/SP-009-asset-manifest/record.md` — self-check transcripts, budget discipline
- Required skills: load `port-feature`, `avalonia-research` before Step 1

## File Scope

- `client/Directory.Build.props` (NEW — the one version authority; no repo-root props exists to chain, verified 2026-07-19)
- `client/src/CcpClient.Desktop/**` (runtime version surface reading the InformationalVersion ATTRIBUTE; publish profile wiring if any)
- `client/tools/**` (publish + artifact-matrix scripts) — **`client/tools/` matches a bare `tools/` rule in the repo-root `.gitignore`: run `git check-ignore` on every new path and audit the changed-file list before .DONE; force-add with care (`-f` also sweeps bin/obj — re-audit the index)**
- `client/tests/CcpClient.Tests/**` (version-derivation + publish-script tests)
- `client/docs/release-publish-gates.md` (contract deliverable)
- `client/docs/task-board.md` (row-9 evidence edit + rows 2/3/8 gate-discharge annotations)
- `spine-tasks/SP-010-release-publish-gates/**` (STATUS.md, record.md, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/release-publish-gates.md`, `client/Directory.Build.props` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `.spine/**` |
| artifactsMustExist | `client/docs/release-publish-gates.md`, `spine-tasks/SP-010-release-publish-gates/record.md` |

**Review Level 2 (plan + code).** Call `spine_review_step` after each step. Engine reviews are empirically dead (zero in SP-001…SP-009 — T-2 open); if skipped, record it and rely on the mandatory Fable consults. Do not stall.

## Steps

### Step 1: Pre-approach consult, current-docs research, contract draft

- [ ] **Pre-approach solo consult** (Fable 5, solo; council is NOT used this run per owner direction — T-7 route unproven) with the planned publish strategy, version-authority design, and evidence matrix; verdict text in record.md BEFORE checkbox. Keep questions few/pointed
- [ ] Update STATUS.md before starting work
- [ ] Research from CURRENT official docs (avalonia-research; record URLs + freshness — verify, never memory): (a) .NET 10 single-file publish semantics — whether/when native libraries (SkiaSharp/HarfBuzz) are bundled vs left beside the binary, extraction location and first-run behavior, `IncludeNativeLibrariesForSelfExtract` current default; (b) `AppContext.BaseDirectory` / `Assembly.Location` behavior under single-file (data-path trap: `Assembly.Location` is EMPTY — path-based version/file reads break); (c) residual Linux system dependencies of a self-contained Avalonia app (libX11, fontconfig, ICU — `ldd` enumeration method); (d) any Avalonia 12.1 publish guidance
- [ ] Write `client/docs/release-publish-gates.md`: named publish strategy (self-contained single-file per RID) WITH revisit trigger; the one-authority version rule (all surfaces DERIVE from `client/Directory.Build.props` `<Version>`; canonical display = InformationalVersion; binding identity = AssemblyVersion; derivation-tested, NOT blind string equality); the artifact evidence matrix (Debug/Release/publish × Windows/WSL2: startup+shutdown exit 0, `--verify-assets`, fresh-profile run, corrupt-settings quarantine run, data-path identity, logs-absence, native-deps floor); the documented update/package metadata shape (authority feeds it; mechanism excluded); honest absences (no logging subsystem exists yet — VERIFY absence, never invent one; localization entries absent per A-014)

### Step 2: Version authority + derivation tests

- [ ] `client/Directory.Build.props`: single `<Version>` (choose the honest greenfield number per the architecture proposal — record the choice); confirm SDK flow to AssemblyVersion/FileVersion/InformationalVersion
- [ ] Runtime version surface in `CcpClient.Desktop`: reads `AssemblyInformationalVersionAttribute` from the entry assembly (NEVER `FileVersionInfo` from a path, NEVER `Assembly.Location`); surface it where the app can report it (minimal — e.g. a `--version` diagnostic line on the existing self-check path pattern, or the log/startup trace; do NOT build UI for it)
- [ ] Publish script (`client/tools/`): derives artifact naming from the authority via `dotnet msbuild -getProperty:Version` — NEVER a hardcoded version string
- [ ] Tests (`CcpClient.Tests`): version-derivation tests — prefix/derivation agreement across the assembly attributes AND the publish artifact name (blind full-string equality is WRONG: InformationalVersion may carry suffixes)

### Step 3: Windows artifact evidence matrix

- [ ] Publish `win-x64` self-contained single-file (Release); record artifact size + the publish command in the contract doc
- [ ] Run the matrix on Windows against Debug exe, Release exe, AND the published artifact: process starts and shuts down cleanly (exit 0, SP-003 teardown discipline); `--verify-assets` exit 0 on all three (**published run = row-8 gate discharge**); fresh-profile no-config run (no configuration-only crash; defaults per SP-005); corrupt-settings run (SP-005 quarantine → typed Degraded, never silent); data path resolves identically across the three modes (record the single-file extraction/base-dir facts); verify NO stray log files (logging honestly absent)
- [ ] Native dependencies on Windows: record what ships beside/inside the binary (research-verified expectation vs observed reality)

### Step 4: WSL2 gate — Linux matrix + WSLg headed smoke

- [ ] **WSL2 gate (in-packet, SP-005/007/008/009 pattern):** native-dir copy (`~/ccp-sp010`, NEVER /mnt/e), full contract testCommand green
- [ ] Publish `linux-x64` self-contained single-file; run the same matrix on Linux: startup/shutdown, `--verify-assets` on Debug+Release+published (case-exactness meaningful on ext4), fresh-profile, corrupt-settings, data-path identity (XDG expectations recorded), logs-absence
- [ ] **Residual native-deps floor:** `ldd` enumeration on the published Linux artifact; record the system-library floor (libX11/fontconfig/ICU reality) — feeds the owner's "which Linux distributions" decision WITHOUT settling it
- [ ] **WSLg headed smoke (rows 2/3 debt):** the published Linux artifact renders its window for real (XGetImage capture, SP-007 pattern) AND shuts down cleanly; attempt graceful close via libX11 `WM_DELETE_WINDOW` ClientMessage (python3 ctypes, xgetimage.py mechanism) — success discharges SP-007's named gap; failure renames it honestly
- [ ] **Measured budgets** (verify the cold precondition — port-lessons 2026-07-19): publish time + matrix run time, both platforms, cold vs incremental; record actuals in the contract doc

### Step 5: Evidence, board reconciliation, pre-completion consult

- [ ] Write `spine-tasks/SP-010-release-publish-gates/record.md`: publish-strategy decision + revisit trigger, consult verdicts (provenance noted), research citations, matrix transcripts (both platforms, all modes), native-deps floor, budgets, surprises; **record engine-review presence/absence** (T-2)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the diff + contract; verdict text in record.md
- [ ] Update `client/docs/task-board.md`: row 9 **"Establish Release and publish gates"** → `WIP` with evidence citing record.md (never `DONE` — owner ratification); annotate row 8's publish-third discharge and rows 2/3 WSLg-smoke discharge on their rows (annotate, never rewrite acceptance)
- [ ] STATUS.md accurate before .DONE

### Step 6: Testing & Verification

- [ ] Contract testCommand passes (solution + BOTH test projects)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths (`git check-ignore` audit for `client/tools/` paths done)

## Completion Criteria

- `release-publish-gates.md` records the named publish strategy + revisit trigger, one-authority version rule, full evidence matrix definition, native-deps floor, update/package metadata shape, and honest absences (logging, localization)
- One `<Version>` in `client/Directory.Build.props`; runtime surface reads the InformationalVersion attribute; publish script derives naming from the authority; derivation tests green
- Matrix green on Windows AND WSL2 for Debug/Release/published: clean startup/shutdown, `--verify-assets` exit 0 (row-8 publish third DISCHARGED with transcript), fresh-profile and corrupt-settings runs per SP-005 contract, data-path identity, logs-absence verified
- WSLg headed smoke evidence (rendered window + clean exit) discharging rows 2/3; graceful-close attempt outcome recorded (discharged or honestly renamed)
- Both solo Fable consults persisted; STATUS.md accurate; board rows `WIP` (not `DONE`); no tracked changes outside File Scope; no new packages; no installer/auto-update work

## Do NOT

- Modify `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`, `.pi/`
- Touch `ConditioningControlPanel/**` (read-only evidence — incl. NO Inno/`installer.iss` reuse; the WPF installer is not this row)
- Build an installer, updater, or any packaging/distribution mechanism beyond the named publish strategy + scripts
- Use `PublishTrimmed` or `ReadyToRun`; admit any package; add native interop beyond the python3 ctypes X11 patterns already proven in `client/tools/verify/`
- Invent a logging subsystem or localization entries to satisfy the acceptance words (verify/document their ABSENCE honestly — A-014)
- Hardcode a version string anywhere outside `client/Directory.Build.props`; read versions via file paths or `Assembly.Location`
- Build or publish from `/mnt/e` in WSL (native ext4 dir only)
- Set any board row to `DONE`; fake STATUS.md/review notes; use `consult` council mode (solo Fable 5 only this packet)
- Weaken SP-003…SP-009 invariants; broaden webcam/secret/path/logging/network boundaries

## Git Commit Convention

- `feat(SP-010): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/release-publish-gates.md` (deliverable), `client/docs/task-board.md` (row 9 + rows 2/3/8 annotations), `spine-tasks/SP-010-release-publish-gates/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only), `client/docs/asset-manifest.md` (only if the publish hook text needs the now-real invocation recorded — one line, justify in record.md)

## Amendments

- 2026-07-19 (authoring): **pre-authoring consult RAN — solo Fable 5 (council not used per owner direction 2026-07-19; T-7 route unproven).** Verdicts applied: (a) self-contained single-file per RID approved as the named strategy with recorded revisit trigger; sharpest risks = native-library extraction semantics (MUST be verified from current .NET 10 docs, not memory) and residual Linux system deps (`ldd` floor); exclude PublishTrimmed/ReadyToRun explicitly; (b) version authority = `client/Directory.Build.props` with DERIVATION tests (never blind equality), runtime reads the InformationalVersion ATTRIBUTE (`Assembly.Location` is empty under single-file), publish script derives naming via `dotnet msbuild -getProperty:Version`, no repo-root props exists to chain (verified); (c) boundary: discharge row-8 publish third + rows 2/3 WSLg smoke; exclude installer/auto-update; "logs" = verify ABSENCE (no logging subsystem exists — inventing one is new scope); data path must be proven identical across Debug/Release/publish (single-file base-dir trap); no-config crash tested BOTH directions (fresh profile + SP-005 corrupt-settings quarantine); WSLg graceful close via libX11 `WM_DELETE_WINDOW` ClientMessage (xgetimage.py mechanism).
- 2026-07-19 (authoring): engine reviews assumed absent (T-2); Review Level 2 retained. Launch: same-shape packet (5th), straight to real batch after validate/analyze/plan/preflight per owner cycle; T-6 playbook on standby (no stub batch shares a real batch ever). `.gitignore tools/` trap called out in File Scope (SP-008 lesson).
