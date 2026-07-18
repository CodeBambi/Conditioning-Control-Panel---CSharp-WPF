# SP-001 Record — Avalonia template pilot

Date: 2026-07-18. Worker: spine worker session (manual stub-assisted execution).

## Resolved versions (real feeds, not assumptions)

| Item | Version | Source |
|------|---------|--------|
| .NET SDK | 10.0.302 | `dotnet --version` on this machine |
| Avalonia.Templates (nuget) | 12.1.0 | `api.nuget.org/v3-flatcontainer/avalonia.templates/index.json` |
| Avalonia package pinned by generated csproj | 12.1.0 | `.spine-scratch/CcpPilotApp/CcpPilotApp.csproj` |
| Avalonia.Desktop / Themes.Fluent / Fonts.Inter | 12.1.0 | same csproj |
| AvaloniaUI.DiagnosticsSupport | 2.2.3 | same csproj |
| Template target framework | net10.0 | generated csproj |

Template and Avalonia package versions reconcile: templates 12.1.0 pin Avalonia 12.1.0.

## Build evidence

Command (contract testCommand, run from lane worktree root):

```
dotnet build .spine-scratch/CcpPilotApp/CcpPilotApp.csproj -c Debug --nologo
```

Final output lines:

```
  Restored ...\.spine-scratch\CcpPilotApp\CcpPilotApp.csproj (in 175 ms).
  CcpPilotApp -> ...\.spine-scratch\CcpPilotApp\bin\Debug\net10.0\CcpPilotApp.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

`.spine-scratch/` is gitignored (throwaway, never committed) — confirmed via `git status` clean except task-folder files.

## Consult verdicts

- Pre-approach solo consult: not recorded in worker session (worker executed the spike directly; engine reviews run post-worker per SP-278).
- Pre-completion solo consult: same — deferred to engine-owned plan/code review steps.

## Surprises (worth knowing for future spine batches on this repo)

1. **Windows hidden `.git` gitfile breaks pi-spine lane worktree setup.** `git worktree add` creates the lane `.git` gitfile with the `H` attribute (git-for-windows `core.hideDotFiles` default). pi-spine's `normalizeLaneWorktreeGitPaths` rewrites it with Node `writeFileSync`, which EPERMs on hidden files. Fix applied repo-wide: `git config core.hideDotFiles false`.
2. **pi-spine fsync bug on Windows** (upstream): `src/batch/abort.mjs` and `src/batch/lifecycle-archive.mjs` open the archive file with flag `"r"` then call `fs.fsyncSync` — EPERM on Windows read-only handles. Local patch changed both to `"r+"`. Needs upstream report/patch; local node_modules edits do not survive `pi-spine` reinstall.
3. `SPINE_WORKER_STUB=1` writes only `.DONE`; a packet whose contract has `fileScopeMustChange` (this one) fails contract verify under stub and requires real worker output in the lane worktree + `spine batch retry`.

## Board reconciliation

- `client/docs/task-board.md` pilot row: evidence recorded here; board row update to `WIP`/evidence link deferred to the port owner — per packet, the owner judges pass/fail against `client/docs/port-workflow.md` §Pilot. This packet's contract deliverable is this `record.md`; the board file was removed from `fileScopeMustChange` (pre-landed on base during engine switch).
