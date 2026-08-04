# Incidents & Gotchas

## 2026-08-04 — git divergence / force-push (RESOLVED)

The greenfield port was accidentally **restarted from scratch on the laptop**
(2026-08-03/04: fresh pi-spine init, new SP-001…SP-010 series, wave-4/5 harvest)
and pushed, while the desktop held the real continued work (373 unpushed commits,
SP-001…SP-036 old series, July 18–22). Owner ruled the laptop work a mistake.

Resolution: desktop force-pushed (`--force-with-lease`) `feat/crossplatform` over
origin; remote tip is now `93e7d612` (real work). The discarded from-scratch work
is preserved **on the desktop** as branch `backup/feat-crossplatform-remote-20260804`
(points at `ef5967b4`) — delete once confirmed worthless.

**Laptop must NOT pull** (would merge the mistake back). Recovery:
`git fetch origin && git checkout feat/crossplatform && git reset --hard origin/feat/crossplatform`

## Git traps in this repo

- **root `.gitignore` `tools/` rule silently excludes `client/tools/` files**
  (SP-008 lesson). Build outputs got force-added once because of this. Check
  `git check-ignore` before assuming a file is tracked.
- **80 MB+ `libSkiaSharp.pdb` build outputs are tracked** under
  `client/tools/verify/CcpVerify/bin/Debug/...` — GitHub warns on every push.
  Needs a `.gitignore` cleanup + history awareness (not yet done as of export).
- **Encoding**: a worker wrote em-dashes as raw CP1252 0x97 into port-lessons.md,
  making it mixed-encoding/invalid UTF-8 (fixed SP-018 by re-encoding to UTF-8).
  Write docs as UTF-8; spot-check smart quotes/dashes from non-Windows tooling.

## Spine engine lessons

- **engine-output-vs-reality (3 incidents)**: pi-runtime loop metadata got swept
  into engine auto-commits (T-10-adjacent debris; not product content). Standing
  order: review engine auto-commits before landing; drop the debris with a chore
  commit, don't let it pollute task content.
- **Lifecycle-complete cleanup-throw ordering bug** (wave-3 harvest): state-clear
  must tolerate worktree `rm` failure.
- **T-5 two-root truth**: the SP-028 anchor-patch premise was falsified with 3
  independent proofs (SP-031); the .reviews dirty-gate was patched in the base
  install (owner-approved) which is what actually unblocked waves.
- **Worker silent wedge**: SP-027 run-1 wedged at 0 CPU on a dead API call —
  heartbeat/stall detection exists because of this (T-13 stall-detector now DONE).
- **Gate-record path correction + modal-drive rule** (SP-024 harvest) — recorded in
  `client/docs/port-lessons.md`.
- **SoundFlow deadlock lesson + rect-persistence binding** (SP-025) — in port-lessons.

## Privacy hard constraints (never relax without a consent/version bump)

- Webcam frames + per-frame derived data: never to disk, never over network. Only
  calibration coefficients persist. Broadening usage must bump `ConsentVersion`.
- `.ccpenh.json` validation rejects NaN/Infinity, UNC paths, absolute asset paths,
  control characters in subliminal text, out-of-range numerics.
- File-open CLI args (`--play`, `--edit`) reject UNC and extended-length paths;
  local rooted files with allowed media extensions only.

## 2026-08-04 — laptop resume session (second export)

- **Anthropic fresh-subprocess route DOWN (wave-4 park):** engine reviewer spawn + manual `pi -p` probes 400 `"Third-party apps now draw from your extra usage, not your plan limits."` — opus-5 AND fable-5 identically; in-session bpx-consult UNAFFECTED (worker in-packet consults completed 12:10–12:25Z, engine spawn failed 12:27Z). Engine misclassifies as "review timed out — increase SPINE_REVIEW_TIMEOUT_MS" (do NOT chase that knob). SP-037 landed via SP-034 substitution norm (self-verifying deliverable); wave 4 parked — constitution bans gate downgrade; `spine batch complete` refuses on failed-task state post-manual-land → close via post-integrate `abort` + husk cleanup. Owner action: claude.ai/settings/usage.
- **Hidden-.git EPERM repeat (SP-020 class, bootstrap gap):** laptop clone lacked `core.hidedotfiles=false` (git config does NOT travel with clone) → 2 batch starts died at `normalizeLaneWorktreeGitPaths` EPERM ~36s pre-worker. Fixed GLOBAL on the laptop. Diagnostic trap: plain `git worktree add` SUCCEEDS (misleading) — the failing write is the ENGINE's Node rewrite; probes must replicate the engine's exact write. A batch failing DURING worktree provisioning cannot `resume --force` ("Lane 1 worktree not found") → abort + husk-verify-zero-unique-commits + fresh start.
- **pi spawn broken on fresh Windows machines:** Node 24 `spawnSync('pi')`=ENOENT, `('pi.cmd')`=EINVAL → blocks spine doctor + worker-runner. Fix: `~/.pi/pi-shim/PiShim.cs` (15-line C# forwarder, stock .NET Framework csc) compiled to `AppData/Roaming/npm/pi.exe`. Desktop's prefix resolves pi — uncharacterized difference.
- **Engine float on the global root:** global pi-spine floated 2.10.0→2.12.2 (unpinned `~/.pi/agent/settings.json` entry + caret npm range); pre-launch verify.mjs caught it. Pin BOTH files exact; the pin lesson applies to the global root too.
- **Per-machine premise re-probe:** laptop HAS Ollama (SP-019's "absent" was desktop-scoped; probe = `ollama --version` only, no socket/pull); laptop WSL has ZERO distros (every WSL2 gate = named limit until owner provisions).
- **bpx-consult `defaultMode:"council"` trap:** bare `consult()` fails where kimi-api is unregistered — pass `mode:"solo"` explicitly.
- **Cross-machine handoff limit:** the desktop's parked wave-4 lane commits never travelled — handoff prose assumed resumable state. Cross-machine parks must push lane branches (or land) first; reconciliation verifies commit EXISTENCE, never trust handoff text.
