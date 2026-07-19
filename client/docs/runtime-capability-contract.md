# Runtime capability contract

**Date:** 2026-07-19 · **Task:** SP-006 (task-board row 5) · **Status:** ratified by implementation + tests in this slice; evidence in `spine-tasks/SP-006-truthful-capability-contract/record.md`

This contract instantiates `architecture-proposal.md` §6 (row-5 column: typed capability states and runtime probes) and the first-attempt capability lesson (`first-attempt-systemic-lessons.md`: "Capability must be an observed result, not an OS or registration guess" — dispositions: REJECT capability-by-platform, capability-by-registration, capability-by-assets-present; ADAPT graceful fallback only for explicitly optional behavior). It activates the row-3/row-5 boundary sentence in `async-lifecycle-fault-contract.md` §2: row 3 owns OPERATION outcomes; **this contract owns capability-availability states**. It implements no feature capabilities: it proves the honesty shape with two demonstrator capabilities (§8) chosen because they resist gaming.

---

## 1. Typed capability states

Every capability query returns exactly one typed `CapabilityState`:

- `Available(detail)` — the capability's probe ran in the **current environment** and its declared evidence (§3) confirmed support. `detail` carries what was confirmed (e.g., the session kind).
- `Unavailable(reason)` — the capability is not supported here, or has not been proven (§4 rule 3). Not an error.
- `Degraded(survivingSemantics, reason)` — the probe ran and **part** of the capability holds. `survivingSemantics` names exactly which semantics survive; `reason` names what does not and why. Degradation that cannot name its surviving semantics is not degradation — it is `Unavailable`.
- `PermissionRequired(reason)` — the probe established that support exists but is gated on a user/OS grant the app does not currently hold.
- `DependencyMissing(dependency, reason)` — the probe established that a named external dependency (binary, service, device) is absent.
- `Faulted(reason)` — the probe itself faulted. `reason` carries the exception class. A faulted probe is a truthful state, never an unhandled exception and never `Available`.

`reason` is a structured `CapabilityReason(code, detail)` — `code` is a stable machine-readable token (`not-probed`, `unknown-capability`, `no-display-server`, `io-failure`, `durability-unverified`, `probe-fault`, …); `detail` is human diagnostic text. Codes are additive: new codes land with their consumer row.

## 2. The probe rule

1. A capability reports `Available` **only** after its probe actually ran against the selected backend in the current environment and confirmed the capability's declared evidence.
2. The following can **never** yield `Available`, alone or in combination:
   - OS/platform checks (`OperatingSystem.IsWindows()` and friends);
   - DI registration or composition-root wiring, including the *identity* of a registered fallback;
   - assets present on disk or embedded;
   - a stub, a no-op, or an external-program fallback (the first attempt's external-browser-as-embedded-host pattern);
   - a swallowed exception (the first attempt's fault→logged-no-op pattern).
3. Each capability declares its **evidence class** (pre-approach consult, record.md):
   - `exercised-backend` — the probe performed the capability's real operation (I/O, device open, surface creation) and observed the result. **Required for feature capabilities** (browser host, overlay, camera, audio, …) when those rows land.
   - `environment-evidence` — the probe read environment facts (session variables, kernel mount tables) and reports those facts. Permitted only for capabilities whose subject IS the environment fact (e.g., §8 demonstrator 1), and the state's detail must name the facts and disclaim any stronger claim.
4. A probe's result may be **downgraded** by additional environment evidence (e.g., §8 demonstrator 2's mount-table downgrade) but environment evidence can never **upgrade** a probe result toward `Available` (pre-approach consult condition, record.md).

## 3. Probe execution

1. Probes execute as SP-004 **owned operations** (`async-lifecycle-fault-contract.md` §1): one owner (`CapabilityProbes`), a cancellation generation, an owned completion per probe, and a typed `OperationOutcome`. No probe runs detached; no probe starts from a constructor.
2. Probes run in the dedicated named startup phase **`CapabilityProbes`**, sequenced after `CoreServices` and before `UserInterface` (SP-003 §1: feature rows may register phases). Sequencing after `CoreServices` guarantees the persistence store's load-time temp handling cannot race probe files in the same directory. The phase body awaits every probe's completion, so all states are populated before the window exists — and the phase itself always returns `Success`: **capability states never fail startup**; a `Faulted` state is truthful information, not a startup error.
3. Outcome→state mapping (the only bridge across the row-3/row-5 boundary):
   - probe body returned a state → that state is applied;
   - `OperationOutcome.Failed` → `Faulted` with the exception class as the reason (the registry's single trap boundary classifies; there is no second catch);
   - `OperationOutcome.Cancelled` (startup cancelled or teardown raced the probe) → the capability stays `Unavailable(not-probed)`.
4. Query API never throws-and-claims:
   - a registered capability whose probe has not run → `Unavailable(not-probed)` — **registration alone never yields `Available`**;
   - an unregistered capability name → `Unavailable(unknown-capability)` — never a fabricated state, never a throw at query time.
5. Re-probing (hot-plug, permission grant, dependency appearing later) is **deferred**: states are probed once at startup in this slice; a later row with a re-probe consumer owns staleness/timestamp semantics (pre-approach consult, record.md).

## 4. The honesty rule

1. A probe that cannot run meaningfully in the current environment **must** report `Unavailable` or `Degraded` with the environmental reason. A truthful degraded report is a passing result.
2. **Faking availability is a contract violation** — in production code, in tests, and in CI. A test that requires fake availability to pass is a defective test and must be fixed, not accommodated.
3. A no-op or fallback may keep the application alive, but the capability it backs reports `Unavailable` with its reason. Silence is not support.

## 5. Degradation semantics

1. `Degraded` names its surviving semantics explicitly (§1). Consumers may rely on exactly those semantics and nothing more.
2. The reason distinguishes *verified-absent* (the probe observed the missing semantics fail) from *unverifiable* (the semantics cannot be exercised in-process — e.g., durability of flush-to-disk on a passthrough filesystem, reported as `durability-unverified`). Unverifiable claims are stated as unverifiable, never as observed failure or observed success.
3. Environment-evidence downgrades (§2 rule 4) are recorded with the environment fact that caused them (e.g., the filesystem type from the kernel mount table).

## 6. Row-3/row-5 boundary activation

`async-lifecycle-fault-contract.md` §2 reserved capability-availability states and runtime probes to this contract. The boundary is now active:

- **Row 3 (SP-004) owns operation outcomes**: `Completed` / `Cancelled` / `Failed(kind, reason)` per owned operation, where `Recoverable`/`Degraded` are per-operation failure classifications supplied by an owner.
- **Row 5 (this contract) owns capability states**: `Available` / `Unavailable` / `Degraded(survivingSemantics)` / `PermissionRequired` / `DependencyMissing` / `Faulted` per capability, derived only from probes.
- The two vocabularies meet at exactly one place: §3 rule 3's outcome→state mapping. A capability state's `Degraded` is not an operation outcome's `Degraded`; neither type references the other.

## 7. Fallback policy

Graceful fallback is admitted only for **explicitly optional** behavior (a feature the product can lose without breaking a promise). A fallback is registered with its own honest capability state: while the preferred backend is unproven, the preferred capability reports its truthful state and the fallback reports its own. There is no silent substitution, and no fallback ever reports the preferred capability's semantics as its own (the external-browser lesson). Feature rows with a fallback requirement name both capabilities and their states in their own contracts.

## 8. Demonstrator capabilities

Chosen because they resist gaming (SP-006 packet): both probe for real, both have environments where the honest answer is *not* `Available`, and neither requires a backend that does not exist yet (which would invite stub-probing — the exact first-attempt sin).

### 8.1 `display-session` (environment-evidence)

Reports the display session the process was launched in, from environment evidence: `RuntimeInformation` for the OS platform; `WAYLAND_DISPLAY` and `DISPLAY` for the Linux session offer.

| Evidence | Reported state |
|---|---|
| Windows | `Available` — session kind `Windows` |
| Linux, only `WAYLAND_DISPLAY` set | `Available` — session kind `LinuxWayland` |
| Linux, only `DISPLAY` set | `Available` — session kind `LinuxX11` |
| Linux, both set (WSLg, most Wayland desktops) | `Available` — session kind `LinuxWaylandWithX11` (Wayland session; X11 also offered, i.e. XWayland present) |
| Linux, neither set | `Unavailable(no-display-server)` — a truthful headless report |

**Session facts, not backend claims.** Avalonia 12.1 selects X11 by default under `UsePlatformDetect()` (proposal §5.1); the native Wayland backend is experimental, opt-in, and an open owner question. Under WSLg, `WAYLAND_DISPLAY` is set while the client runs as an X11 client through XWayland. This capability therefore reports what the session **offers**; it never claims which backend Avalonia selected or what a Wayland backend could do. Any future backend-behavior capability (input, overlay, per-region semantics) is a separate `exercised-backend` capability owned by its feature row.

### 8.2 `atomic-filesystem` (exercised-backend)

Exercises the persistence store's filesystem guarantees **for real** in the actual settings data directory: create the directory, write a uniquely-prefixed probe temp file, `Flush(flushToDisk: true)`, rename over an existing file (`File.Move(overwrite: true)`), perform a quarantine-style move, and clean up (including pre-existing probe leftovers from an interrupted earlier run). Probe files use a dedicated `ccp-capability-probe-` prefix so they can never be mistaken for the store's `settings.json.tmp` by its crash-recovery path (pre-approach consult, record.md).

- Every operation verified → `Available`, **unless** environment evidence downgrades (§2 rule 4): on Linux the probe resolves the data directory's filesystem type from `/proc/self/mounts` (kernel-reported evidence, longest mount-point prefix match) and a passthrough/network filesystem (`9p`, `drvfs`, `cifs`, `smbfs`, `nfs`, `nfs4`) yields `Degraded(survivingSemantics: "temp write, rename-over-existing, and quarantine move verified by real I/O", reason: durability-unverified)` — flush-to-disk honoring cannot be verified in-process on a filesystem that may not forward fsync. Native Linux filesystems (ext4, xfs, btrfs, …) and Windows yield no downgrade.
- `UnauthorizedAccessException` from any step → `PermissionRequired(io-failure)`.
- `IOException` from any step → `Unavailable(io-failure)` with the failing step named.
- Anything else escaping the probe → `Faulted(probe-fault)` via §3 rule 3. Never unhandled, never `Available`.

## 9. Integration proof

1. The `CapabilityProbes` phase appears in the startup trace (`CapabilityProbes: ok`) and the placeholder window lists every registered capability with its current typed state — the composition root's probe results are visibly reachable from a user path (SP-003 §10 pattern).
2. A composition-root walk test runs the **real** composition root through the real phase runner (no test-double substitution on that path) and asserts every registered capability left the `not-probed` state via its real probe.
3. WSL2 is part of this contract's subject matter: the recorded evidence includes the **actual observed demonstrator states** under WSL2/WSLg (record.md). A `Degraded` report there is the honesty proof, not a failure.

---

## Conformance checklist (tested in this slice)

- All six states reachable in unit tests via probe doubles and the real demonstrator probes.
- Registration alone never yields `Available` (`not-probed`); unregistered query → `Unavailable(unknown-capability)`; probe throw → `Faulted` with the exception class, never unhandled, never `Available`.
- Session probe reports the truthful session kind for the Windows / X11 / Wayland / both-set / neither matrix via injected environment; the WSLg both-set case reports `LinuxWaylandWithX11` with the no-backend-claim detail.
- Filesystem probe performs real I/O (verified by its effects) and degrades truthfully under an injected passthrough mount table; permission and I/O failures map to `PermissionRequired` / `Unavailable`.
- Probes execute as owned operations in the `CapabilityProbes` phase; the window surfaces states through the real composition root; WSL2 observed states recorded.
