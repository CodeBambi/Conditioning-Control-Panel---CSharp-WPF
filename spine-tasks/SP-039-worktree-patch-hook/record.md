# SP-039 — T-14: lane-local patch application at worktree creation (worktree-setup hook)

## Incident log (honesty framing c)

- **2026-08-04 (this packet, 4th occurrence in one day):** this packet's OWN lane (batch `20260804T144454` lane-2) started unpatched as predicted — first `apply.mjs` run applied all **7 project-root patches** (engine root already applied, 5 skips) → `verify.mjs` exit 0 both roots. Recorded as the (hopefully) last manual occurrence. Same failure shape as SP-035 record.md:186, SP-036 record.md:153, SP-037 record.md:124-126: fresh lane → pi installs pristine `pi-spine@2.10.0` into the lane's gitignored `.pi/npm` → contract `verify.mjs` reds → mid-task `apply.mjs`.

## Step 1 — Engine archaeology (READ-ONLY, global install `C:\Users\Micha\.pi\agent\npm\node_modules\pi-spine` @ 2.10.0)

### Hook contract (`src/config/worktree-setup-hook.mjs` + `src/batch/worktree.mjs`)

- **Resolution** (`resolveWorktreeSetupHook`, worktree-setup-hook.mjs:270-283): reads `config.worktreeSetupHook`; must be a non-empty relative path, no `..`, **must live under `scripts/`** relative to the project root (`validateWorktreeSetupHookPath`:184-190 — hard requirement, `CONFIG_SETUP_HOOK_UNSAFE` otherwise); must exist; symlink escapes rejected. Missing/unresolvable → returns `null` → hook **silently skipped**.
- **Invocation point** (`src/batch/engine.mjs:215-258`): inside the per-lane provisioning loop — `provisionLaneWorktree` (plain `git worktree add -b`, worktree.mjs:234-251) → journal `lane.setup_hook.started` → `runWorktreeSetupHook` → journal `lane.setup_hook.completed` → journal `lane.provisioned`. **Fires once per lane at worktree creation, before any worker runs.** Second call site: `src/batch/lane-dirty-check-git.mjs:229` (re-run to repair hook-managed drift).
- **Spawn** (worktree.mjs:357-383): `spawnSync(hookPath, { cwd: worktreePath, env: process.env + SPINE_PROJECT_ROOT/SPINE_WORKTREE/SPINE_BATCH_ID/SPINE_LANE_NUMBER, timeout: WORKTREE_SETUP_HOOK_TIMEOUT_MS = 120_000 (worktree.mjs:16), stdio: pipe })`. **No shell, no arguments.**
- **Exit semantics** (worktree.mjs:385-418): spawn error → throw; **last stdout line must parse as JSON with `ok: true`** — no output, invalid JSON, `ok !== true`, or exit code ≠ 0 all → `throw`. Caller (engine.mjs:245-252) journals `lane.setup_hook.failed` and **re-throws → the batch dies at provisioning**. So a failing hook blocks every future batch: fail-safe = the hook itself must always exit 0 with `{"ok":true}` and absorb its own errors.

### Windows spawn constraint (empirical, this laptop, node v24.5.0)

`spawnSync` of the configured path with no shell means the hook must be a real executable. Scratch test (`spawnSync` on `.sh` / `.cmd` / `.mjs` files): **`.sh` → EFTYPE, `.cmd` → EINVAL (CVE-2024-27980), `.mjs` → EFTYPE**. The engine's own Flutter template is a `.sh` (POSIX-only). On this Windows engine host the configured hook path must therefore be a genuine `.exe`.

### Lane `.pi/npm` install timing (the open timing question — decided from source)

- At hook time the lane has **no `.pi/npm`**: the worktree is a fresh `git worktree add`; `.pi/npm` is gitignored runtime state; the engine's provisioning path contains no npm install (grep of `src/batch/*.mjs`, `bin/*.mjs` — nothing installs pi deps in lanes).
- The lane's install appears when the **worker's pi session first starts in the lane**: `.pi/settings.json` (tracked) declares `"packages": ["npm:pi-spine@2.10.0", "npm:@booplex/bpx-consult@0.10.1"]`; pi's package manager (`dist/core/package-manager.js:1008-1015`) resolves each npm source at session start: `needsInstall = !existsSync(installedPath) || !installedNpmMatchesConfiguredVersion(...)`. Only when `needsInstall` does pi run `npm install <spec> --prefix .pi/npm --legacy-peer-deps` (package-manager.js:1466-1476).
- `installedNpmMatchesConfiguredVersion` (package-manager.js:1167-1174) reads `package.json` → `version` from the installed path and checks `satisfies(installedVersion, range)`. Our patches never touch `package.json` version.
- **Consequence:** if a satisfying pi-spine@2.10.0 tree is ALREADY present in the lane's `.pi/npm` when pi first starts, `needsInstall` is false, pi never calls npm, and whatever patch state the tree carries is what the worker gets. A pre-staged **patched** tree therefore survives untouched. (SP-020's npm-skip finding is the same gating one level down; pi's version check fires before npm is even invoked.)

## Mechanism decision

**Chosen seam: the engine's `worktreeSetupHook` — but the hook PRE-STAGES the lane's `.pi/npm` by copying the main checkout's (`SPINE_PROJECT_ROOT`) already-patched `.pi/npm` tree into the lane, instead of running `apply.mjs`.**

- The hook is the right seam: it fires per-lane at creation, before any worker/pi run (engine.mjs evidence above), and `SPINE_PROJECT_ROOT` gives it the patched source tree. This is exactly the pattern the engine's own Flutter template uses (copy/symlink gitignored assets from `SPINE_PROJECT_ROOT` into the lane).
- Pre-staging works because of pi's version-satisfies gate (above): pi sees pi-spine@2.10.0 present → no install → patches ride in with the copy. Main checkout's `.pi/npm` is patched by the standing pre-launch `verify.mjs` rule (T-5 row), so the copy source is trustworthy; the hook also self-verifies (presence check, cheap) and reports.
- **Fail-safe exit semantics (honesty framing b):** the hook ALWAYS exits 0 with last line `{"ok":true,...}` (fields: `prestaged`, `reason`, `durationMs`). Every internal failure — missing source tree, copy error, missing env — degrades to `{"ok":true,"prestaged":false,"reason":...}` + stderr detail. Rationale: a failed pre-stage reproduces today's recoverable disease (worker remediates mid-task), while a thrown hook blocks ALL provisioning (worse). Drift detection is the named post-land gate's job, not the hook's.
- **Idempotent:** if the lane already has a satisfying `pi-spine` install (hook re-run via the dirty-check repair path, lane-dirty-check-git.mjs:229), the hook skips the copy and reports `prestaged:"already-present"`.
- **Shape:** `scripts/spine-worktree-setup.exe` — a tiny committed C# shim (source `scripts/spine-worktree-setup.cs`, built with the .NET Framework `csc.exe` present on every Windows box) that execs `node .spine/patches/worktree-setup-hook.mjs` with cwd/env passthrough, streams stdout/stderr, propagates exit code. Logic lives in `.spine/patches/worktree-setup-hook.mjs` (repo's spine tooling is all node; iterate without recompiling). Config wiring: `.spine/spine-config.json` `worktreeSetupHook: "scripts/spine-worktree-setup.exe"`.
- **Platform honesty:** the `.exe` is Windows-only; the spine batch engine host is the owner's Windows laptop (the product's Windows+Linux targets are the CLIENT, not the engine). A future Linux engine host would configure a `.sh` twin running the same `.mjs` — recorded, not built (YAGNI).

### Rejected alternatives

1. **Hook runs `apply.mjs` at creation (the T-14 row's literal sketch):** REJECTED — at hook time the lane has no `.pi/npm` at all (gitignored, engine installs nothing); `apply.mjs` would find no target and no-op, and the disease would recur when pi installs pristine pi-spine minutes later. Timing evidence above.
2. **Hook runs `npm install` in the lane then `apply.mjs`:** REJECTED — slower (network, registry), more failure surface inside a 120s provisioning timeout, and redundant: copying the main checkout's tree achieves the identical end state with zero network.
3. **New engine patch in the T-1 manifest (e.g. patch `spine-worker-runner.mjs` pre-prompt phase):** REJECTED — engine-source patches are the most fragile artifact we own (every one is a reinstall/upgrade liability; T-5's applied≠loaded lesson), the runner pre-prompt phase STILL precedes pi's install (same no-op timing), and the board row names the config hook, which needs no engine modification.
4. **Directory junction lane `.pi/npm` → main `.pi/npm`:** REJECTED — lanes would share mutable state with the main checkout; a lane-side `npm install`/update would write into the main tree, and two lanes could race. Copy is isolated and cheap (`.pi/npm` ≈ 3.4 MB).
5. **All-logic-in-C# single exe:** REJECTED — couples every logic iteration to a recompile and splits the repo's node-based spine tooling idiom; shim + `.mjs` keeps the stable boundary compiled once and the logic reviewable. (Close call; recorded.)

### File Scope expansion (SP-023 norm — justified, consult-flagged)

The engine contract HARD-requires the hook under `scripts/` (worktree-setup-hook.mjs:184-190). `scripts/` is absent from the packet's File Scope (the packet anticipated the hook living in `.spine/patches/**` — impossible per engine validation). Minimal expansion: `scripts/spine-worktree-setup.exe` + `scripts/spine-worktree-setup.cs` (two files, nothing else under `scripts/`). `fileScopeMustNotChange` untouched. Flagged in the pre-approach consult.

## Consults

### Pre-approach (Step 1)

**Route:** solo consult. **ACTUAL answering model: GPT-5 (reasoning transcript returned).** Verdict: **no flaw in the timing reasoning; pre-stage-by-copy endorsed**, with five refinements — all ADOPTED:

1. **Auto-update clobber gap closed by pinning:** pi's update check skips pinned sources (`package-manager.js:940` — `if (parsed.type === "local" || parsed.pinned) return undefined;`); `.pi/settings.json` pins `npm:pi-spine@2.10.0` exactly → a satisfying pre-staged tree is never auto-updated. Verified in source.
2. **Observability:** the engine journals only `durationMs` from the hook — stdout/stderr are DISCARDED (engine.mjs:236-244). The hook therefore writes its own log to `SPINE_WORKTREE/.pi/npm/worktree-setup-hook.log` (`.pi/npm/` is gitignored — .gitignore:50; the rest of `.pi/` is TRACKED, so a log anywhere else in `.pi/` would dirty every lane → T-5-class). The final JSON line carries `prestaged`, `reason`, `sourceVersion`, `verifyExit`, `durationMs`.
3. **Post-copy self-check:** hook runs `node .spine/patches/verify.mjs --root <lane>/.pi/npm/node_modules/pi-spine` in the lane and records the exit code (never propagates it). `--root` keeps it single-root + read-only; per-lane proof in the log.
4. **`.exe` committability checked:** `git check-ignore scripts/spine-worktree-setup.exe` → NOT ignored (exit 1) — no `.gitignore` change needed. `csc.exe` confirmed at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.
5. **Scratch isolation:** provision the scratch worktree with `projectRoot` = THIS lane-2 worktree (a worktree-of-worktree under lane-2's gitignored `.worktrees/`, .gitignore:184) — zero main-checkout pollution, identical engine code path (`provisionLaneWorktree` + `runWorktreeSetupHook` imported from the global engine install), full cleanup after. SPINE_PROJECT_ROOT for the scratch = lane-2, whose `.pi/npm` is patched (this packet's recorded remediation).

## Step 2 — Implementation + scratch verification

### Delivered

- `.spine/patches/worktree-setup-hook.mjs` — the hook logic (in File Scope). Pre-stages `$SPINE_PROJECT_ROOT/.pi/npm` → lane, dest-first idempotent skip, post-copy `verify.mjs --root` self-check (recorded, never propagated), always writes `\<lane\>/.pi/npm/worktree-setup-hook.log` (gitignored), ALWAYS exits 0 with last stdout line `{"ok":true,...}`.
- `scripts/spine-worktree-setup.cs` + compiled `scripts/spine-worktree-setup.exe` (4.6 KB, csc.exe 4.8.9221.0) — Windows spawn shim (SP-023-norm scope expansion, consult-flagged). Missing script / node-launch failure → emits `{"ok":true,"prestaged":false,...}` itself, exit 0.
- `.spine/spine-config.json`: `worktreeSetupHook: "scripts/spine-worktree-setup.exe"` (was `""`).
- Build note: from git-bash, csc.exe must be invoked via `cmd /c` with backslash paths — bare `/out:` slash-args get path-mangled (CS2001/CS1504). Rebuild command in the .cs header.

### Scratch verification — THROUGH THE ENGINE'S OWN PROVISIONING PATH

Driver: `spine-tasks/SP-039-worktree-patch-hook/scratch-verify.mjs` — imports `provisionLaneWorktree` / `runWorktreeSetupHook` / `removeLaneWorktree` from the GLOBAL engine install (`C:\Users\Micha\.pi\agent\npm\node_modules\pi-spine\src\batch\worktree.mjs`) and calls them exactly as `engine.mjs`'s provisioning loop does.

- **v1 attempt (worktree-of-worktree, projectRoot = lane-2 per consult preference): FAILED at `git worktree add` — MAX_PATH**: the nested base path pushes the deep WPF asset filenames (`ConditioningControlPanel/Resources/sounds/companion_audio/mods/builtin-sissyhypno/flashes_audio/*.mp3`) past 260 chars. Honest limitation: the hook mechanism is unaffected, but scratch isolation moved to the main checkout (production-identical base depth — production lanes check out fine). v1 debris (dir + `task/spine-lane-1-scratch-sp039` branch) cleaned before v2.
- **v2 (projectRoot = main checkout, orch = this lane's branch): GREEN.** Hook-enabled lane transcript:
  - `runWorktreeSetupHook` → `{ ok: true, durationMs: 788 }` (the engine's exact contract consumption — JSON last line parsed, exit 0, no throw).
  - Fresh lane's `.pi/npm/node_modules/pi-spine/package.json` present BEFORE any worker/pi run.
  - In-lane `node .spine/patches/verify.mjs --root <lane>/.pi/npm/node_modules/pi-spine` → **exit 0, "verify.mjs: OK — all patches applied on all roots."**
  - Lane log: `{"at":"2026-08-04T15:19:15.174Z","batchId":"scratch-sp039","laneNumber":"1",...,"ok":true,"prestaged":true,"reason":"copied .pi/npm from SPINE_PROJECT_ROOT (pi-spine 2.10.0)","sourceVersion":"2.10.0","verifyExit":0,"durationMs":603}`.
- **Negative control (same driver, `config: {}`):** `runWorktreeSetupHook` → `{ ok: true, skipped: true }`; lane's pi-spine **absent** — the old unpatched state, falsifiable contrast.
- **Fail-safe paths (direct invocation):** shim with hook script absent → `{"ok":true,"prestaged":false,"reason":"hook script absent in lane: ..."}` exit 0; mjs with SPINE_PROJECT_ROOT nonexistent → `{"ok":true,"prestaged":false,"reason":"source pi-spine missing ..."}` exit 0 + lane log written; idempotent re-run with dest present and source gone → `{"ok":true,"prestaged":true,"reason":"already-present (pi-spine 2.10.0 in lane) — idempotent skip"}` exit 0.
- **Cleanup verified:** both scratch worktrees removed, both `task/spine-lane-N-scratch-sp039` branches deleted, `git worktree prune`, temp exe copy + `scripts/` dir removed from the main checkout, `git status` on the main checkout EMPTY, `.worktrees/` contains only the live batch dir.

### Post-land production note

After this packet lands, the main checkout's `scripts/spine-worktree-setup.exe` + `.spine/patches/worktree-setup-hook.mjs` + the `worktreeSetupHook` config value exist on every fresh clone and every new lane's orch base — the engine resolves the hook from the MAIN checkout (patched per the standing pre-launch verify rule) and the `.mjs` rides in each lane's branch. No fresh-machine step beyond the standing `verify.mjs`/`apply.mjs` rule for the main checkout itself.

## Engine-review presence (T-2 heading format load-bearing)

- **Step 1 plan review (`spine_review_step` type=plan):** SKIPPED by the runtime — "Nested reviewer spawn blocked inside pi worker session ... the batch engine runs reviews after worker success (SP-195)"; `skipped: true`, `spawnFailed: false`, artifact `.reviews/1-20260804T150503.md`. Not a spawn failure → proceeded per the engine-owned review path.

_Pending: Step 2+ calls._
