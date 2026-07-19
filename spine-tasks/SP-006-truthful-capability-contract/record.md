# SP-006 — truthful runtime capability contract: record

**Task:** task-board row 5 (P0). **Worker:** kimi-coding/k3. **Date:** 2026-07-19.

---

## First-attempt capability evidence digest (outcomes only)

Three lying patterns, from `first-attempt-systemic-lessons.md` (code + history citations there):

1. **Capability-by-OS-assumption.** `AvaloniaPlatformCapabilities.cs:24-65` reported overlay/screen-capture support from broad desktop/OS assumptions, and identified several capabilities by checking whether DI returned a fallback type. Outcome: the app claimed support for things that had never run.
2. **DI-fallback-identity.** `WebKitGtkBrowserHost.cs:9-49` was registered as the Linux embedded browser host but actually opened an external browser, returned no embedded control, and had inert script/events. Outcome: "registered" was indistinguishable from "integrated"; the external-browser fallback *claimed* embedded-host capability.
3. **Fault→no-op.** `LinuxOverlaySurface.cs:9-78` deliberately converted backend faults into logged no-ops; `57cbfc81` documented a Wayland backend claiming per-region behavior while stubbing it. Outcome: failures were invisible; logs were the only channel and nothing downstream could tell support from silence.

Disposition applied by this task: REJECT capability-by-platform, capability-by-registration, capability-by-assets-present, and fault-swallowing; ADAPT graceful fallback only for explicitly optional behavior — and a fallback reports `Unavailable`, never `Available`.

## Pre-approach consult (solo Fable 5, 2026-07-19)

Full outline submitted (state model, probe rule, honesty rule, both demonstrators, phase placement, fallback policy, test matrix, WSL2 gate). **The reply truncated mid-sentence** partway through question 4 ("display-session probe Available-from-env-vars being a lie-by-construction risk…"); received portion applied, truncation labeled per SP-005 precedent.

Verdict text (received portion, condensed):

1. **State model:** matches the packet's six states; encoding not-probed as `Unavailable(not-probed)` is conservative and truthful. Gap noted: no probe timestamp/staleness — **advice: do NOT add staleness machinery**; record in the contract that re-probing is deferred to a later row with a consumer. (Applied.)
2. **Session probe should expose a typed result** (a `SessionKind` enum: Windows / LinuxX11 / LinuxWayland / LinuxWaylandWithX11) so tests assert typed values instead of parsing detail strings. (Applied: `SessionProbe.Detect` returns the enum; the capability state's detail derives from it.)
3. **`/proc/self/mounts` is defensible kernel evidence, not OS-string inference — under three conditions:** (a) real I/O runs and gates `Available`/`Unavailable` — mount evidence may only DOWNGRADE, never upgrade; (b) the reason is framed as `durability-unverified` (an epistemic claim: fsync honoring cannot be verified in-process) not a behavioral claim that flush is broken; (c) mount lookup uses longest-mount-point-prefix matching and symlink resolution. Also flagged: **probe files must not collide with PersistenceStore's temp-adoption/stale-temp deletion** (a `settings.json.tmp`-shaped probe file in the data directory would be adopted or deleted by the store's load) — use a distinct probe-file prefix and clean pre-existing probe leftovers. (All applied; the contract encodes the downgrade-only rule and the epistemic framing.)
4. **Phase placement: prefer a dedicated named phase `CapabilityProbes` after `CoreServices`** over hiding probes in a participant's `StartAsync`: (a) the packet's "named startup phase" language is honored literally and the trace shows `CapabilityProbes: ok`; (b) capability states never fail startup — a `Faulted` capability is a truthful state, not a startup error (participant-start faults are Fatal, wrong semantics); (c) probes run after `PersistenceStore`'s load, so its temp cleanup can't race the fs probe's files. The phase body always returns `Success`. (Applied.)
5. **(Truncated — reconstruction, labeled):** the reply cut off inside the display-session truthfulness analysis — env vars are *offered-session* evidence, not an exercised backend connection. Reconstruction of the concern: a session capability reporting `Available` from env vars alone risks repeating the OS-assumption lie one level down. Disposition applied: the contract introduces a **declared evidence class per capability** (`environment-evidence` vs `exercised-backend`). `display-session` is explicitly an environment-evidence capability whose `Available` detail names the session facts and disclaims any backend claim; feature capabilities (browser host, overlay, camera…) must use exercised-backend probes. A short-lived TCP/connectivity probe of `$DISPLAY` was considered and rejected: no BCL X11 client, native interop is banned by this packet, and env evidence is what the packet specifies for this demonstrator.

No rejection of the overall outline. Skipped: staleness machinery (no consumer), in-process fsync verification (impossible without power-cut), X-connection probing (banned native interop).

## Steps 2–5 evidence

### Implementation summary
- `Capabilities/CapabilityState.cs` — the six typed states + structured `CapabilityReason(code, detail)` with stable codes (`not-probed`, `unknown-capability`, `no-display-server`, `unsupported-platform`, `io-failure`, `durability-unverified`, `probe-fault`).
- `Capabilities/CapabilityRegistry.cs` — registry (registration alone = `Unavailable(not-probed)`; unknown name = `Unavailable(unknown-capability)`) + `CapabilityProbeRunner`: each probe is an SP-004 owned operation on one `CapabilityProbes` owner; outcome→state mapping is the only row-3/row-5 bridge (Completed → probe's state; Failed → `Faulted` with exception class; Cancelled → stays not-probed; null-returning probe → `Faulted`).
- `Capabilities/SessionProbe.cs` — demonstrator 1 (environment-evidence): typed `SessionKind` (Windows/LinuxX11/LinuxWayland/LinuxWaylandWithX11) from OS platform + `WAYLAND_DISPLAY`/`DISPLAY`; the both-set detail names XWayland and explicitly disclaims any Avalonia-backend claim.
- `Capabilities/AtomicFileSystemProbe.cs` — demonstrator 2 (exercised-backend): real I/O in the actual data dir (create, temp write, `Flush(flushToDisk: true)`, rename-over-existing, quarantine-style move, content verify, cleanup incl. prior leftovers) under a dedicated `ccp-capability-probe-` prefix that can never collide with the store's `settings.json.tmp` handling; `/proc/self/mounts` longest-prefix match (symlink-resolved) downgrades to `Degraded(durability-unverified)` on 9p/drvfs/cifs/smbfs/nfs — downgrade-only, never upgrade.
- Wiring: `CompositionRoot.Build` registers both demonstrators and creates the runner on the host registry; `Program.CreateStartupPhases` adds the named `CapabilityProbes` phase after `CoreServices` (always Success; cancellation → Cancelled); `ApplicationHost` exposes `Capabilities`/`ProbeRunner`; `MainWindow` renders each capability's typed state (integration proof §9).

### Demonstrator choice rationale (why they resist gaming)
- Session probe: the honest answer differs per environment (headless Linux = Unavailable; WSLg = Available-with-XWayland-facts) — a stub would be caught by the headless case in one run.
- FS probe: performs verifiable I/O (effects on disk, content check) and has a REAL degraded environment on this machine (/mnt/e DrvFs) — faking Available would contradict the observable mount table.
- Both avoid backends that don't exist yet (overlay/browser), which would invite stub-probing — the exact first-attempt sin.

### Test output — Windows (contract testCommand)
`dotnet build client/CcpClient.sln -c Debug --nologo` → **0 Warning(s), 0 Error(s)** (.NET SDK 10, net10.0).
`dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo` → **Passed: 74, Failed: 0** (51 SP-003/004/005 intact + 23 capability: registry honesty, all-states reachability, throw→Faulted, null→Faulted, cancel→not-probed, session matrix incl. WSLg/headless/unsupported, fs real-IO/9p-degrade/ext4-no-downgrade/io-failure, real-composition-root walk).

### Test output — WSL2 Linux (in-packet gate)
Environment: WSL2 Ubuntu, dotnet SDK 10.0.110; `client/` copied to native `~/ccp-sp006` (SP-002/SP-005 pattern — /mnt/e build avoided).
Build: **0 Warning(s), 0 Error(s)**. Tests: **Passed: 74, Failed: 0** — identical suite green on Linux.

### ACTUAL observed demonstrator states under WSL2/WSLg (honesty proof)
Observed by running the real probes in the WSL2 copy (temporary observation test, WSL copy only — not committed; raw output below):
- env: `WAYLAND_DISPLAY=wayland-0`, `DISPLAY=:0` (WSLg).
- `display-session`: **Available** — "linux wayland session with X11 offered via XWayland (both WAYLAND_DISPLAY and DISPLAY set); session facts only — not a claim about the selected Avalonia backend (X11 by default, proposal §5.1)".
- `atomic-filesystem` in ext4 home: **Available** — "temp write, rename-over-existing, and quarantine move verified by real I/O in '/home/mich/ccp-sp006-probe-home' on filesystem 'ext4'".
- `atomic-filesystem` in `/mnt/e` (DrvFs): **Degraded** — surviving "temp write, rename-over-existing, and quarantine move verified by real I/O"; reason `durability-unverified`: "flush-to-disk honoring cannot be verified in-process: '/mnt/e/ccp-sp006-probe-drvfs' is served by passthrough/network filesystem '9p'". **This degraded report IS the honesty proof** — the probe ran, verified real I/O, and refused to claim durability it could not verify.
- WSL2-observed states match what the unit tests assert (comparison done here, per packet: record, not assertion): the WSLg session shape matches `SessionProbe_WslgShape_...`; the DrvFs degradation matches `FileSystemProbe_PassthroughMount_DowngradesToTruthfulDegraded`.

### Headed Windows smoke (2026-07-19)
Ran `spine-tasks/SP-006-truthful-capability-contract/headed-smoke.ps1` against the Debug exe. Observed via UIA (not believed): window "CCP Client" rendered `Bootstrap/CompositionRoot/CoreServices/CapabilityProbes: ok`, `Persistence: running`, `Heartbeat: running`, `capability display-session: Available — windows desktop session`, `capability atomic-filesystem: Available — temp write, rename-over-existing, and quarantine move verified by real I/O in 'C:\Users\Micha\AppData\Roaming\CcpClient'`, and the heartbeat tick advancing. Graceful close → **exit code 0**. (First script run FAILed only on a ps1-file-encoding artifact: PS 5.1 misread the UTF-8 em-dash in the check string — the rendered states were already visible in that run's UIA dump; check strings made ASCII, rerun passed.)

### Engine reviews
`spine_review_step` called after Steps 1, 2, 3 (plan): all **skipped=true, reviewLevel=0, spawnFailed=false** — sixth consecutive batch (SP-001…SP-006) with zero engine reviews; T-2 remains open. Fable solo consults are the active quality gate per the packet.

### Surprises
1. PowerShell 5.1 reads BOM-less UTF-8 scripts as ANSI — the smoke script's em-dash check string became mojibake while the app rendered fine. ASCII check strings now; observation unaffected.
2. None in the capability core: the SP-004 owned-operation trap made throw→Faulted a one-line mapping; the failed-edit hazard (edit tool matched a wrong `}` pair in MainWindow.axaml.cs) was caught by the immediate build, fixed before commit.

### Known accepted gaps
- `UnauthorizedAccessException` → `PermissionRequired` branch is covered by code path + state-reachability theory, not a live-permission test (cross-platform permission simulation is flaky: root ignores chmod, Windows dir ACLs are heavyweight). The `IOException` → `Unavailable(io-failure)` branch IS live-tested via a file-as-directory path on both platforms.
- Re-probing/staleness intentionally deferred (contract §3 rule 5) — no consumer.

## Pre-completion consult (solo Fable 5, 2026-07-19)

(verdict to be appended)
