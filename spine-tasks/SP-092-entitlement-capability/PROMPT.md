# SP-092 — A typed entitlement capability that reads the shipping app's existing login

## Mission

WPF gates Down The Rabbit Hole at Tier 2 and **fails closed when the Patreon service is null**. The port has no entitlement service at all — verified: every `Patreon` match in `client/src` is a WPF citation inside a comment, and the only entitlement type in the tree documents itself as a typed seam.

So today there is no honest way to ship a DTRH door: faithful parity opens it for nobody, ungated hands out paid content, and an always-allowed stub is exactly the fake-available shape the truthful-capability contract bans.

**The owner supplied a fourth answer by doing it: they installed the shipping WPF app and logged in.** Your outcome: **a typed entitlement capability that reads that existing login on Windows, reports honestly on Linux, and never lies in either direction.**

## Dependencies

Board row: "DTRH is Tier-2 gated in WPF and the port has no entitlement service", P1, OPEN. **You do not edit the board.** SP-006's truthful runtime capability contract is the shape you must obey. No dependency on SP-091 or SP-093 — you register nothing and wire nothing (see Wiring).

## The mechanism, verified by the orchestrator at authoring

Read these in the WPF tree (read-only evidence) and confirm each yourself before you plan:

- `ConditioningControlPanel/Services/Auth/SecureAuthTokenStore.cs:14` — entropy is the compile-time literal `ConditioningControlPanel_AuthToken_v1`. It is a constant in shipped source, **not a secret**.
- the same file `:17` — the path is `auth_token.dat` under `%LOCALAPPDATA%/ConditioningControlPanel/`.
- the same file `:39,65` — `ProtectedData.Protect` / `Unprotect` with **`DataProtectionScope.CurrentUser`**. This is the load-bearing fact: any process running as the same Windows user can decrypt it. That is what makes reading the existing login possible without a second OAuth flow.
- `ConditioningControlPanel/Services/TierGate.cs:88-94` — the tier itself is **not in the token**. It resolves through `App.Patreon?.HasLabAccess`, or the server naming a key as today's free feature. The token is a bearer; the entitlement is a server answer.

External connections are owner-approved (2026-08-14, verbatim: *"we want all the external connections to work for sure"*), so the tier lookup is permitted.

## THE TRAP THAT DECIDES THE DESIGN, named at authoring

**Two failures look identical from the outside and must never be conflated: "you are not a patron" and "I could not tell."**

If a decrypt fails — the file is absent, the user is a different Windows account, the app was never installed, or upstream rotated the entropy to `_v2` — the honest answer is **Unavailable, with the reason**. Returning `NotEntitled` there is a silent downgrade that presents exactly like a legitimate refusal, and the user would see their paid features vanish with no explanation and no way to tell a bug from a policy. **That conflation is the defect this packet exists to prevent**, and a design that cannot distinguish them is disqualified regardless of how well it works on the happy path.

The second trap: **this is a bridge, not a destination.** It presumes the shipping app is installed and logged in. A port-only user has no token. Say so in `record.md` as a named limit; do not let it read as a finished entitlement system.

## Privacy rules, non-negotiable

- The token is **read, used, and dropped**. Never logged, never persisted, never copied, never written into the port's own secret store, never included in a diagnostic string or an exception message.
- No test may contain a real token, and no test may read the developer's actual `auth_token.dat`. Drive your tests through an injected seam over your own fixture data.
- If you log anything about this path, log the **outcome class**, never the value. The port already has a route-classes-only logging precedent; match it.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Entitlement/**` (new), `client/tests/CcpClient.Tests/Entitlement*`, and `spine-tasks/SP-092-entitlement-capability/**` |
| Must not change | everything else, and specifically `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/src/CcpClient.Desktop/Navigation/**`, `client/src/CcpClient.Desktop/Views/**`, `client/src/CcpClient.Desktop/Tray/**`, `client/src/CcpClient.Desktop/App.axaml.cs`, `client/src/CcpClient.Desktop/Features/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Wiring: deliberately none

You produce a capability and its tests. **You register nothing in the composition root and you gate nothing**, because SP-091 owns those files in this wave and a lane may not reach into a sibling's scope. Per A-014 this makes your work **infrastructure only** — state that plainly in `record.md`, and do not claim the DTRH gate is closed. It is closed when a later packet consumes this.

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-092-entitlement-capability/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Entitlement` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/src/CcpClient.Desktop/Navigation/**`, `client/src/CcpClient.Desktop/Views/**`, `client/src/CcpClient.Desktop/Tray/**`, `client/src/CcpClient.Desktop/App.axaml.cs`, `client/src/CcpClient.Desktop/Features/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-092-entitlement-capability/record.md`, `spine-tasks/SP-092-entitlement-capability/floor-delta.json` |

**READ THE PIN FROM `client/tests/floor/floor.json`**, never from this packet. Your gate reports `observed == pin + your declared delta`.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. Confirm the four mechanism facts yourself from the WPF source and report what you measure. If any line has moved, re-grep the token and say so.
2. Design the typed outcome. At minimum it must distinguish **Entitled**, **NotEntitled**, and **Unavailable(reason)** — and the reasons must separate at least: no shipping app data directory, no token file, decrypt failed, and platform has no DPAPI.
3. Implement the Windows read behind an injected seam so the tests never touch a real token or the developer's real file.
4. Implement the Linux answer as an honest typed `Unavailable`, matching how `ISecretStore` already reports there. **A Linux stub that returns NotEntitled is the banned shape.**
5. **Prove the conflation cannot happen.** A test per Unavailable reason, each asserting the outcome is Unavailable and NOT NotEntitled. This is the packet's central fact; if only one test matters, it is this one.
6. **Prove it bites:** make the decrypt-failure path return NotEntitled in a scratch edit and confirm your test reds. Restore byte-identically and verify. Do not commit the mutation.
7. Answer in `record.md`: what happens when the token is valid but the tier lookup cannot reach the server? Name which outcome that is and defend it.

## Completion Criteria

- Three-state typed outcome with reasons, honest on both platforms.
- A test per Unavailable reason proving it is never reported as NotEntitled.
- No real token in any test; no token value in any log, message, or exception.
- `record.md` names the bridge limit (a port-only user has no token) and the infrastructure-only status.
- Build 0 warnings / 0 errors.

## Do NOT

- Return `NotEntitled` for anything you could not determine.
- Write the token into the port's settings, its secret store, or any file.
- Register or wire this capability. That is a later packet's job and another lane's files.
- Invent a tier when the server cannot be reached.
- Introduce a wall-clock wait. Use the shared `TestWait` helper.
- Claim the DTRH gate is delivered. It is not until something consumes this.

## Git Commit Convention

Conventional commit, `feat(SP-092): ...`. Create `.DONE` as your last action and do NOT commit it.

## Documentation Requirements

`record.md` only. The orchestrator writes the board and the digest at land.
