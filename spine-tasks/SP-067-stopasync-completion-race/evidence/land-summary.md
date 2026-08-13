# SP-067 — orchestrator land verification (independent; not the gate's evidence)

Verified tree: `75a09d61` in scratch worktree `C:/Code/ccp-land-w24`, which is byte-identical to
the integrated tip of `feat/crossplatform` (`spine integrate` was a clean fast-forward; `git diff`
between the verified tree and the tip is EMPTY).

| Check | File | Result |
|---|---|---|
| Cold build, fresh worktree | `land-build-scratch.txt` | **0 Warning(s), 0 Error(s)** |
| Floor run 1-5 (5 consecutive) | `land-floor-1..5.txt` | **5/5 GREEN** — 903/903 unit, 35/35 headless, exactly 2 named Linux-gated skips |
| Probe on FIXED tree (500 iters) | `land-probe-fixed.txt` | **500/500 `Cancelled`**, 0 `Completed` (493 zero-tick) |
| Probe BITE (fix reverted) | `land-probe-red-bite.txt` | **498/500 `Completed`** — probe is not vacuous |
| Pin bite @ `Participants.cs:108` | `land-pins-bite-heartbeat.txt` | fails **only** `AsyncLifecycleTests...ZeroTick` (1 failed / 2 passed) |
| Pin bite @ `StatusTickerParticipant.cs:152` + `AvatarAnimationEngine.cs:438` | `land-pins-bite-ticker-avatar.txt` | fails **only** those two pins (2 failed / 1 passed) |
| Engine patches, MAIN checkout | `land-patches-verify-main.txt` | exit 0 — 5 engine + 4 project patches, pi-spine 2.10.0 |

`CCP_DATA_ROOT` was unset for every run (port-workflow.md:204). The floor wrapper writes TRX to
`os.tmpdir()`, so TRX paths appear in the run files but the files themselves are outside the tree.

## Bite matrix — why it was run one site at a time

Each induced defect fails **exactly** its own pin and no other. Reverting only the Heartbeat fix
leaves the StatusTicker and Avatar pins green, so a single shared revert would have "verified" two
pins that were never exercised. All three of the floor's new facts are non-vacuous.

## What this does NOT prove

The probe drives the **zero-tick** window (stop before the first tick), which the originally failing
test does not use — that test stops *after* ticks. So neither the 5 greens nor the 500 iterations
re-bound its historical ~1-in-15 hit rate by frequency. Closure is **mechanistic**: the only path by
which the assertion could observe `Completed` was the post-loop return, and it can no longer yield
`Completed` while the token is cancelled. The loop body has no `break`, so the ternary's `Completed`
arm is unreachable today — the fix is a documented invariant, not a knife-edge race repair. Linux
is unproven (zero WSL distros on this machine). The worker's narrative said the RED was "497/500";
its own `red-unmodified.txt` says **495/5** — the artifact is cited, not the prose.
