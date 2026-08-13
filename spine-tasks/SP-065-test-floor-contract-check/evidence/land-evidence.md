# SP-065 — LAND evidence (orchestrator, 2026-08-13)

Integrate `09b4b639`; reconcile + push `c799d2cf`. Batch `20260813T032810` completed and archived.

## Honest note on what is and is not here

The land's FIRST verification ran in a scratch worktree (`land-verify-w22`, merge commit `e0ba3ab1`)
and produced build + 3 run logs under `/c/Code/ccp-land-w22/_landlog/`. **Those files were deleted
when I removed the scratch worktree at cleanup, before they were copied anywhere durable.** That was
a mistake — the land rule says the runs are redirected to files and attached, and prose in CONTEXT.md
is not a substitute for the artifact.

What ties the deleted logs to the tree that shipped is the identity check, which IS durable in git:
the scratch merge's tree SHA `ec157d0e45c8d0f0cb45a791839848d4806d58e5` is **byte-identical** to the
integrated tip's tree, and `git diff e0ba3ab1 HEAD` was EMPTY. So the merged tree I verified and the
tree that was integrated are provably the same content.

The files in this folder are the **replacement** evidence: the same contract, re-run on the pushed
tip `c799d2cf` (whose tree differs from the integrated tip only by this land's documentation commit).
They are not a reconstruction of the deleted logs and are not presented as one.

## Files

| File | What it is |
|---|---|
| `land-checkB-verify.txt` | `node .spine/patches/verify.mjs` on the pushed tip — OK, all 9 patches applied on both roots (includes the new `skill-floor-wrapper-testcommand`). |
| `land-checkB-build.txt` | `dotnet build client/CcpClient.sln -c Debug --nologo` on the pushed tip — **0 Warning(s), 0 Error(s)**. |
| `land-tip-run-1.txt` | Full contract floor run 1 on the pushed tip. Exit code was captured out-of-band as `floor=0` (this file has no `EXIT=` line for that reason); runs 2 and 3 carry theirs inline. |
| `land-tip-run-2.txt` | Full contract floor run 2 — `EXIT=0`. |
| `land-tip-run-3.txt` | Full contract floor run 3 — `EXIT=0`. |

All three runs: `FLOOR OK: CcpClient.Tests: 898/898 passed, 0/0 skipped; CcpClient.HeadlessTests: 35/35 passed, 0/0 skipped`.

## Sharp edge found by this land's own final check

Check B initially FAILED with `FLOOR VIOLATION — passed 897 (pin 898)` on the tree about to be pushed.
That was **not** a drift: `check-floor.mjs` runs `dotnet test --no-build` by design (the contract chains
an explicit `dotnet build` first), and the main checkout's dll predated the merged `FloorWrapperGuardTests.cs`.
The wrapper failed CLOSED, which is correct, but it named the wrong cause. Recorded as a named limit on
board row 49 and in `client/docs/port-lessons.md`.

A second trap fired in the same step: `node check-floor.mjs | tail -4; echo $?` printed `0` for a run that
had actually exited 1, because `$?` reports the exit code of `tail`. Use `${PIPESTATUS[0]}`.

## Land obligations (recorded pre-launch) — disposition

1. **Verify the merged tree THROUGH the wrapper** — done (3 greens in the scratch worktree; logs since lost, see above), and the decisive `git diff` EMPTY / identical-tree-SHA check passed. Re-established on the pushed tip by the runs in this folder.
2. **`.spine/patches/manifest.json` so future packets inherit the wrapper** — done: patch `skill-floor-wrapper-testcommand`, project-tree-only like the other skill patches. First attempt verified as `drifted` because the replacement contained the anchor verbatim; anchor re-based across the insertion point; `apply.mjs` + `verify.mjs` OK on both roots, and a second `apply.mjs` confirmed the idempotent skip.
3. **File, do not fix, the blind auditor's bare `dotnet test`** — filed as board row T-17. Not fixed inline: it would have been unverified scope on an already-verified tree, which is the wave-18 failure shape.
