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
