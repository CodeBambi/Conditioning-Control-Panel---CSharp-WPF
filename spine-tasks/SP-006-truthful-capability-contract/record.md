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

(to be filled as work lands)
