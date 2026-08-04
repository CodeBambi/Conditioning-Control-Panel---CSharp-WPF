# SP-037 record — Reconcile asset manifest with v6.6.3 DTRH payload delta

## Step 1: empirical delta sweep + re-derivation plan

### Failing tests, captured verbatim (2026-08-04, this worktree, Debug)

`dotnet test tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --filter FullyQualifiedName~AssetManifestTests`
→ Failed: 2, Passed: 21, Total: 23.

1. `CopiedDirection_RealManifest_AllCopiedEntriesPresentCaseExact_SweepClean` — `Assert.Empty() Failure: Collection was not empty`. Named failures:
   - `dtrh.payload/assets/bubbles/effects/spirals/sp8.gif` — `copied-missing-or-case-drift`
   - `payload/dtrh/CLAUDE.md` — `unmanifested-copied-asset`
   - `payload/dtrh/loom.html` — `unmanifested-copied-asset`
   - `payload/dtrh/loomBoot.js` — `unmanifested-copied-asset`
   - `payload/dtrh/LOOM_PRIMER.md` — `unmanifested-copied-asset`
   - (collection display truncated by xUnit; full set derived by the file-listing sweep below)
2. `SelfCheck_RealAssembly_ExitZero_WithPerAssetLines` — `Assert.Equal() Failure. Expected: 0, Actual: 1` (AssetManifestTests.cs:185).

### Empirical two-direction sweep (file listing vs manifest — NOT the board row's list)

Method: `os.walk` of `ConditioningControlPanel/Resources/web/dtrh/` (→ `payload/dtrh/<relpath>`) + `client/src/CcpClient.Desktop/Features/Dtrh/overlay/` (→ `payload-overlay/<relpath>`), set-diffed against all `"source": "copied"` manifest paths, both directions.

- On disk, expected copied paths: **1544**
- Manifest copied entries: **1538**

**On disk, NOT manifested (7 adds):**

| Path | Added to legacy tree by commit |
|---|---|
| `payload/dtrh/CLAUDE.md` | `51707be8` docs(dtrh): commit DTRH primer doc |
| `payload/dtrh/LOOM_PRIMER.md` | `b8683451` docs(primers): Loom feature primer |
| `payload/dtrh/loom.html` | `f0c093f4` feat(dtrh): Loom studio standalone host + gifenc export |
| `payload/dtrh/loomBoot.js` | `f0c093f4` (same) |
| `payload/dtrh/shared/audioSrc.js` | `05f7714a` feat(packs): Wave 1 - ContentLocator plumbing… |
| `payload/dtrh/shared/loomField.js` | `f0c093f4` (same) |
| `payload/dtrh/vendor/gifenc/gifenc.esm.js` | `f0c093f4` (same) |

**Manifested, NOT on disk (1 remove):**
- `payload/dtrh/assets/bubbles/effects/spirals/sp8.gif` (spiral-pool deletion per main-sync inventory)

Note: the board row's hypothesis listed only 4 adds; the empirical sweep found 3 more
(`shared/audioSrc.js`, `shared/loomField.js`, `vendor/gifenc/gifenc.esm.js`). The empirical
sweep rules, per honesty framing (a).

**Derived new copied-count assertion: 1538 − 1 + 7 = 1544** (matches the on-disk expected count — cross-checked, not guessed).

### Re-derivation plan (per SP-009 schema, matching existing dtrh-entry convention)

All 7 new entries: `source: copied`, `required: true`, `heads: ["desktop"]`,
`overridePolicy: "none"`, `trust: "full"`, `provenance.license: "project-internal"` —
identical to every existing `dtrh.payload/*` entry (verified: exactly one distinct
provenance/heads/trust/override tuple across all 1536 payload entries).

`provenance.origin` per entry names the v6.6.3 main-sync promotion and the adding commit:
- Base phrasing: `WPF legacy DTRH payload tree ConditioningControlPanel/Resources/web/dtrh, added in main commit <sha> (<subject>) and flowed to the client via the v6.6.3 main-sync merge 56f156fc (SP-037 catalogue reconciliation), copied unmodified at build via linked Content glob (SP-023)`
- Loom-studio files (loom.html, loomBoot.js, shared/loomField.js, vendor/gifenc/gifenc.esm.js): commit `f0c093f4`, promoted into the main app by `d64860d4` — origin names both.
- ID convention: `dtrh.payload/<relpath>` (e.g. `dtrh.payload/vendor/gifenc/gifenc.esm.js`).

Insertion points (manifest entries are ordinally sorted by id):
- `CLAUDE.md`, `LOOM_PRIMER.md`: before first `dtrh.payload/assets/...` entry (uppercase sorts first), `CLAUDE.md` before `LOOM_PRIMER.md`.
- `loom.html`, `loomBoot.js`: between `dtrh.payload/index.html` and `dtrh.payload/m2test.js` (loom.html first — `.` < `B` ordinal).
- `shared/audioSrc.js`: between `shared/audioMute.js` and `shared/capability.js`.
- `shared/loomField.js`: between `shared/fog.js` and `shared/loomSpiral.js`.
- `vendor/gifenc/gifenc.esm.js`: between `styles.css` and `vendor/omggif/omggif.module.js`.
- Remove the 15-line `dtrh.payload/assets/bubbles/effects/spirals/sp8.gif` block (line ~16624).

`AssetManifestTests.cs`: only the copied-count assertion `Assert.Equal(1538, …)` → `1544`
plus its comment line (1536 DTRH payload + 2 product overlay → 1542 DTRH payload + 2 product overlay). Nothing else in the test file changes.

### Session fact — current legacy tree state (honesty framing (d))

- Worktree HEAD: `7457c97964344840e1d750d60a931c6bea925291`
- Current `ConditioningControlPanel/Resources/web/dtrh` git tree: `649db67890f1cfc522b16c25c20f2e6d568ca7be`
- Legacy tree working state: clean (`git status --short -- ConditioningControlPanel/` empty)
- The SP-011 trust anchor (tree `40be29df…`) PREDATES the v6.6.3 merge. Re-anchoring payload trust is NOT this packet; new-entry provenance names the adding commits instead of citing the stale anchor as covering the new bytes.

### WSL2 named-limit probe (verbatim, honesty framing (f))

```
$ wsl -l -q
exit=0
```

Empty output, exit 0: WSL installed with ZERO distros on this machine (owner's laptop).
Provisioning a distro is an owner decision. Windows evidence only; no Linux evidence faked.

### Pre-approach solo consult (2026-08-04)

- Mode: solo (packet bans council; route per the 2026-08-04 rewire: Opus 5 main / Fable 5 fallback).
- Actual answering model: **not identifiable from tool output** — the consult response carries no model identity field. Recorded honestly as unknown; verdict text below is what the tool returned.
- **VERDICT: plan is sound — proceed. Do NOT exclude any of the 7.** The sweep enumerates the OUTPUT directory and the csproj glob has no `Exclude`; excluding would require a csproj edit outside File Scope, and the packet bans invented exclusion rules. Four gaps named; all closed below:
  1. **Source-vs-output check (highest risk):** enumerated `bin/Debug/net10.0/payload/dtrh` + `payload-overlay` and set-diffed against the source-derived 1544 — **EXACT match, zero drift both directions**. No stale-bin hazard; the 1544 count is source-derived and output-confirmed.
  2. **Other consumers of 1538/1536:** grep found only `AssetManifestTests.cs:129,133` (in scope, will move to 1544). The `1536` figures in `client/docs/dtrh-admission.md` and `task-board.md` cite the SP-011 trust-anchored tree `40be29df` (historical anchor fact, still accurate about that tree — not the live count). task-board.md is enabler-2 (orchestrator); dtrh-admission.md is not in File Scope — recorded here, not edited.
  3. **sp8.gif consumers:** grep across `client/src`, `client/tests`, and the payload tree — **zero references** outside the manifest entry itself. Clean removal.
  4. **Empirical arithmetic:** measured split = **1542 payload/dtrh + 2 payload-overlay = 1544** (printed from the sweep, not inferred by subtraction). The test comment will read "1542 DTRH payload + 2 product overlay".
- Durable-lesson / future-row candidate (advisor, recorded — NOT acted on here): main commit `51707be8`'s subject is "exclude CLAUDE.md files from publish output" — upstream considers `CLAUDE.md` non-shippable. If a future packet adds a glob `Exclude`, the manifest count moves again.

## Engine-review presence

- Step 1 plan review: `spine_review_step(step=1, type=plan)` → `skipped: true`, `spawnFailed: false`, verdict null — "Nested reviewer spawn blocked inside pi worker session… the batch engine runs reviews after worker success (SP-195)". Artifact: `.reviews/1-20260804T121421.md`. Review Level 2 honored by the engine post-.DONE.
- Step 2 plan review: `spine_review_step(step=2, type=plan)` → `skipped: true`, `spawnFailed: false`, verdict null (same SP-195 message). Artifact: `.reviews/2-20260804T121819.md`.
- Step 3 plan review: `spine_review_step(step=3, type=plan)` → `skipped: true`, `spawnFailed: false`, verdict null (same SP-195 message). Artifact: `.reviews/3-20260804T122510.md`.

## Step 2: applied re-derivation — evidence

- `assets.manifest.json`: 7 entries added at ordinal-sorted insertion points (post-edit check: `dtrh.payload/` subset exactly ordinally sorted, zero duplicate ids, JSON parses; 1551 total entries, 1544 copied), sp8.gif 15-line block removed. `git diff --stat`: manifest +120/−15 lines net, test file 4 lines, nothing else.
- `AssetManifestTests.cs`: only the copied-count comment (`1544 copied entries (1542 DTRH payload + 2 product overlay)`) and `Assert.Equal(1544, copied.Length)` changed.
- `dotnet test --filter FullyQualifiedName~AssetManifestTests` → **Passed: 23, Failed: 0, Total: 23** (both named tests green).

## Step 3: self-check binaries + full-suite floor — evidence

- `dotnet build client/CcpClient.sln -c Release` → 0 Warning(s), 0 Error(s).
- `--verify-assets` against real binaries:
  - Debug: `asset OK copied: 1544 entries present, case-exact, sweep clean` / `verify-assets: PASS (1551 manifest entries, all required embedded assets open)` — **exit 0**
  - Release: identical PASS line — **exit 0**
- Full contract testCommand (Windows):
  - `node .spine/patches/verify.mjs` → **OK — all patches applied on all roots** (see incident note below)
  - `dotnet build client/CcpClient.sln -c Debug --nologo` → **0 Warning(s), 0 Error(s)**
  - `dotnet test CcpClient.Tests` → **Passed: 466, Failed: 0, Total: 466**
  - `dotnet test CcpClient.HeadlessTests` → **Passed: 29, Failed: 0, Total: 29**
  - Floor restored EXACTLY: **466/466 + 29/29**. Zero drift beyond the two repaired tests.

### Incident: verify.mjs red on first Step 3 run (environment, not product)

First contract run failed at `verify.mjs`: the worktree-local project pi-spine (`lane-1/.pi/npm/node_modules/pi-spine` 2.10.0) had all 7 project patches missing ("reinstall removed it — run apply.mjs"); engine root was fine. Remediated with `node .spine/patches/apply.mjs` (7 applied, 5 engine skipped as already applied) → verify.mjs OK both roots. `.pi/` is git-ignored runtime state; `git status` clean afterward. No File Scope impact. Recorded so the orchestrator knows the lane's local pi-spine needed re-patching mid-task.

### Pre-completion solo consult (2026-08-04)

- Mode: solo. Actual answering model: **not identifiable from tool output** (no model identity field in the response — same honest recording as Step 1).
- **VERDICT: the substance is done and correct — do NOT touch the manifest, the test assertion, or the counts again.** 1544 = 1542 + 2 confirmed three independent ways (source/output set-diff zero-drift, Debug `--verify-assets` line, fresh Release binary identical line); the 7 entries reuse the single provenance/heads/trust/override tuple all pre-existing `dtrh.payload/*` entries share; naming the adding commits instead of the stale `40be29df` anchor is exactly right under honesty framing (d).
- Advisor items closed before .DONE:
  1. **Leftover stash risk:** `git stash list` → empty (verbatim: `stash-empty` marker printed after). Closed.
  2. **apply.mjs write targets vs fileScopeMustNotChange:** `git status --short` after apply.mjs shows only `M spine-tasks/SP-037-asset-manifest-v663-resync/record.md` — nothing under `.spine/` or `.pi/` tracked (writes were inside git-ignored node_modules runtime roots). Contract safe. Closed.
  3. Step 4 check outputs pasted below; STATUS marked accurate before .DONE; Step 3 plan-review artifact recorded from the ACTUAL call (the pre-written line was stripped before commit, never re-invented).
  4. No full-testCommand re-run after markdown-only record/STATUS edits (advisor-endorsed; the 466/466 + 29/29 evidence stands).
  5. `51707be8` future-row candidate carried in Durable-lesson candidates (named, not acted on).

## Step 4: Testing & Verification — outputs

- Contract testCommand: passed in Step 3 (verify.mjs OK both roots + build 0W/0E + `Passed: 466, Failed: 0, Total: 466` + `Passed: 29, Failed: 0, Total: 29`) — counts EXACTLY 466/466 + 29/29, zero drift. Only markdown changed since; not re-run (pre-completion consult item 4).
- `git diff --check` → clean (no output; `diff-check-clean` marker).
- `git status --short` before final commit → only `M spine-tasks/SP-037-asset-manifest-v663-resync/record.md` (File Scope). Cumulative product diff across the task: `assets.manifest.json` (+120/−15 region) and `AssetManifestTests.cs` (4 lines) only — no other client/ paths touched.
- `git stash list` → empty.

## Durable-lesson candidates

1. A main-sync merge that changes the read-only payload tree silently breaks the manifest floor because the csproj linked glob auto-copies new files while the manifest is the only catalogue — the sweep test catches it, but the board row's hypothesized file list was incomplete (4 vs the empirical 7). Always derive the delta by file-listing sweep, never trust the inventory row.
2. `PreserveNewest` copies but never deletes — stale `bin/` payload files can masquerade as unmanifested assets; verify output tree == source tree before trusting a derived count.
