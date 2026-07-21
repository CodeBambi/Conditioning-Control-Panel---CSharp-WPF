# Task: SP-021 — stub tail-spawn probe (T-1 verification apparatus)

## Mission

STUB-ONLY spawn probe for the T-1 post-land reinstall gate: prove the `worker-tail-at-file` patch launches a worker whose prompt tail exceeds 16KB (the Windows 32,767-char CreateProcess failure mode). **This packet is never run with a real worker** — its terminal state is the stub `.DONE`; it exists so `spine batch start` has a pending task for the spawn proof. Recorded on the T-1 row.

## Dependencies

- **None**

## File Scope

- `spine-tasks/SP-021-stub-tail-probe/**`

## Contract

| Field | Value |
|-------|-------|
| testCommand | `true` |
| fileScopeMustChange | `spine-tasks/SP-021-stub-tail-probe/probe-note.md` |

## Review Level: 0

## Steps

### Step 1: Probe

- [ ] `probe-note.md` records this packet's stub-only purpose

### Step 2: Testing & Verification

- [ ] No-op (stub-only packet)

## Completion Criteria

- Stub `.DONE` written by a worker that spawned successfully with a >16KB tail

## Do NOT

- Run with a real worker; touch any product path
