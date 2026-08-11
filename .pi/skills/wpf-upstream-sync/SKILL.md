---
name: "wpf-upstream-sync"
description: "Merge the still-shipping WPF product branch (main) into the port branch (feat/crossplatform) and convert the delta into port obligations, so nothing from a new WPF version is silently left behind. Use whenever the user says sync main, pull main, merge main into crossplatform, \"the WPF app was updated\", or before planning a wave that touches a surface upstream may have changed."
version: 1
created: "2026-08-11"
updated: "2026-08-11"
---
## When to Use
Use when upstream `main` (the live WPF product) has moved and the port branch needs it: explicit asks ("switch to main, pull, merge into crossplatform"), a new WPF release, or before scheduling port work on a surface upstream may have rewritten. NOT for ordinary port work — the WPF tree is read-only reference and is never edited by a port task. Prefer running it BETWEEN waves; if a spine batch is mid-flight, the checkout-free path below is mandatory and in-flight packets are never retargeted.

## Procedure
1. SAFETY FIRST — never `git checkout main` while a spine batch runs. `main` has no `.spine/` and no `client/`, so checking it out DELETES the running engine's journal directory and the whole port from the working tree. Check `spine status --diagnose`; if anything is running (or to be safe always), update the local ref without a checkout: `git fetch origin --prune` then `git fetch origin main:main` (fast-forwards local `main`), and stay on `feat/crossplatform`.
2. Measure the delta before merging: `git merge-base origin/main feat/crossplatform`, `git rev-list --count feat/crossplatform..origin/main`, `git diff --name-only <base> origin/main | awk -F/ '{print $1"/"$2}' | sort | uniq -c | sort -rn`, and `git diff --name-status <base> origin/main | awk '$1=="A"{print $2}'` grouped by directory (new dirs = new product surfaces). Read the in-tree version marker on both sides: `git show <ref>:ConditioningControlPanel/ConditioningControlPanel.csproj | grep '<Version>'`.
3. Read the release notes for the feature-level story: `git show origin/main:notes-v*.txt` (the newest ones). Treat them as a map, never as evidence — every board row must cite code (`File.cs:line`, payload paths, file counts), because notes are marketing copy and lag or overstate.
4. Commit or stash any local work, then `git merge main --no-edit` from `feat/crossplatform`.
5. Resolve conflicts with the standing rule: the `ConditioningControlPanel/**` tree is a READ-ONLY archaeology reference and must track `main` exactly, so content conflicts there resolve to main (`git checkout --theirs <path> && git add <path>`). `.gitignore` (and other shared config) keeps BOTH sides. A delete/modify conflict usually means abandoned first-attempt residue (`CCP.Core/`, `CCP.Avalonia.*`) holds a stale fork of a WPF model that upstream just edited — restore main's file (`git checkout main -- <path>`) and leave the residue copy alone; it is not port work to clean up.
6. Before committing the merge, prove the port was not touched: `git diff --cached --name-only HEAD | grep -E '^(client|spine-tasks|\.spine)/'` must be EMPTY. Then commit.
7. Verify port health on the merged tree: `dotnet build client/CcpClient.sln -c Debug --nologo` (0W/0E) and the unit suite (must stay at or above the current floor). Green here does NOT mean no drift — see the next step.
8. Hunt drift the tests cannot see. Diff the WPF services the port has ALREADY ported and the payload trees the port SERVES: `git diff --stat <base> main -- 'ConditioningControlPanel/Services/Chaos/*' 'ConditioningControlPanel/Services/Quiz/*' 'ConditioningControlPanel/Chaos/*' 'ConditioningControlPanel/Resources/web/<tree>/**'`, then read the added members (`git diff ... | grep -E '^\+' | grep -E 'public |case "'`). Upstream bug fixes to code the port already copied are PARITY DEFECTS IN LANDED PORT CODE (defect-class rows), not new features.
9. Convert the delta into rows on `client/docs/task-board.md`: one row per NEW independent product surface (with evidence: service dir + payload file counts), one defect-class row per behavior change to already-ported code (P0 when it is silent user-observable disobedience), one follow-up row per in-flight packet whose baseline the merge just moved, and ONE backlog row itemizing everything smaller — with the detail living in the ledger, not the row.
10. Append a dated section to `client/docs/upstream-sync.md`: baseline SHA/version pair, commit count, merge SHA and size, every conflict and how it was resolved, port health, and the four buckets (new surfaces / parity drift on landed code / smaller deltas / gaps this sync exposed in the port's own guards). Update `client/memories/port-status.md` with the new baseline in one compact block.
11. Never retarget a mid-flight spine packet to the new baseline. Its archaeology, contract and tests are internally consistent against the old one; file the delta as a follow-up row naming the new baseline instead.
12. Commit and push everything (merge + ledger + rows + memories), then report the delta to the owner in buckets, leading with anything that is a defect in already-landed port code.

## Pitfalls
- `git checkout main` mid-batch is destructive: `main` carries neither `.spine/` nor `client/`, so the running engine's journal and the entire port vanish from the working tree. Use `git fetch origin main:main` instead — same result, no checkout.
- A green client test suite after the merge proves nothing about new upstream trees: the asset-manifest parity test only covers what the client already ships, so a brand-new 184-file payload tree can appear with zero test signal. Always diff the payload directories by hand.
- Release notes describe the version they were written for; the in-tree `<Version>` can be ahead (notes stopped at v6.7.2 while the csproj said 6.7.4). Cite the csproj, not the notes.
- Upstream bug fixes are the dangerous category, not upstream features: a fix to code the port already copied means the port now carries the bug that upstream retired, and nobody's tests will say so.
- The first-attempt residue (`CCP.Core/`, `CCP.Avalonia.*`) forks WPF models, so every upstream edit to `Models/*.cs` returns as a delete/modify conflict. Restoring main's file is correct; 'fixing' the residue is scope creep.
- Do not let the WPF tree drift toward the port's convenience. The moment it stops matching `main`, every archaeology citation (`File.cs:line`) becomes ambiguous and the port's evidence base rots.

## Verification
1. `git diff --cached --name-only HEAD` at merge time lists nothing under `client/`, `spine-tasks/`, or `.spine/`.
2. `git diff --name-only --diff-filter=U` is empty (no unresolved conflicts) and no `<<<<<<<` markers remain: `grep -rn '^<<<<<<<' ConditioningControlPanel/ .gitignore`.
3. `dotnet build client/CcpClient.sln -c Debug --nologo` reports 0 Warning(s) 0 Error(s) and the unit suite is at or above the recorded floor.
4. `git log -1 --format='%h %s'` shows the merge commit, and `git rev-list --count feat/crossplatform..origin/main` is 0.
5. `client/docs/upstream-sync.md` has a new dated section whose baseline pair matches the merge, and every claim in it resolves to a real path (spot-check two).
6. Every new board row cites code evidence (path + counts or `File.cs:line`), and any change to already-ported code is filed as a defect-class row rather than a feature row.