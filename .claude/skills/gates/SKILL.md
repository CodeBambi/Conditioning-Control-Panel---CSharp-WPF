---
name: gates
description: Run the correct build and test gate for whichever of the two code trees the current changes touch (legacy WPF, or the greenfield client). Use before committing, or when asked to verify or validate a change.
disable-model-invocation: true
argument-hint: "[--fast]"
allowed-tools: Bash, Read, Grep
---

## Changed files

Tracked edits:

!`git diff --name-only HEAD`

New untracked files:

!`git ls-files --others --exclude-standard`

## Instructions

Route by the paths above. If a change spans more than one tree, run every matching gate. If nothing is listed, say so and stop rather than running a full build.

### Tree 1: legacy WPF (`ConditioningControlPanel/**` outside `CCP.*`, `Tests/**`)

```bash
dotnet build ConditioningControlPanel.sln
```
```bash
dotnet test Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
```

Narrow to one class while iterating with `--filter "FullyQualifiedName~TheTestClass"`.

### Tree 2: greenfield client (`client/**`)

> **The CCP.\* Avalonia attempt was DELETED on 2026-08-22.** It used to be Tree 2 here, gated by
> `./ConditioningControlPanel/tools/run-gates.sh`. **That script and the whole tree are gone** - do not look
> for them, and do not treat their absence as a broken checkout. Nothing in the repo now builds
> `ConditioningControlPanel.sln` automatically; that gap is an open P0 on the task board.


Tier 1, always:

```bash
dotnet build client/CcpClient.sln -c Debug --nologo
```
```bash
dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo
```
```bash
dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo
```

Then the mechanical floor, which is the real pre-land gate:

```bash
node client/tests/floor/check-floor.mjs
```

It runs both projects with TRX loggers and compares against the name-anchored pin in `client/tests/floor/floor.json`. Green requires exit 0 from `dotnet test`, zero bad outcomes, `passed + skipped == total`, and every skipped test present in `allowedSkips`.

Rules when the floor fails:

- A count mismatch means the pin moved. Bump `total` only in the same commit as the test change that moved it, and state the reason in the commit message. Never widen the pin, disable a test, or special-case to make the step pass.
- A skip that is not in `allowedSkips` names the offender. `allowedSkips` is not a quarantine list; a test qualifies only when its precondition is a property of the machine or OS that configuration cannot satisfy.
- Never export `CCP_DATA_ROOT` process-wide. It makes the data-root isolation pin skip and the floor goes blind.

If the change touched documents that guards read (`client/docs/**`, inventory JSON), re-run at minimum `UpstreamPayloadInventoryTests`, `AiOperationContractTests`, and `VersionDerivationTests` after the doc edit, not before.

## Reporting

State per gate: the command, pass or fail, and the actual numbers (tests passed, tabs visited, floor totals). If a gate was skipped, say which and why. A compile-only result never counts as verification of interaction, rendering, audio, focus, window behavior, or animation; if the change touches those, name the headed check that is still outstanding instead of calling the change verified.
