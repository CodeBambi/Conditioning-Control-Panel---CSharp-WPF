# Bite 2 — single-flight guard reverted alone

**Reverted lines:** `SoundArbitration.cs` `ReadyLocked` — removed the `if (!_reprobeInFlight)`
guard so EVERY refused play with an expired cooldown schedules a probe (the unconditional
`_reprobeInFlight = true; Schedule(...); log` body left intact; nothing else touched).

**Command:** `dotnet test --filter "FullyQualifiedName~SoundArbitrationTests"` (rebuild included).

**Result: RED — 1 failed / 29 passed / 30 total.** Red set (exactly the single-flight pin):

- `Recovery_SingleFlight_ConcurrentAttempts_OneInitCall` [FAIL]
  (32 concurrent cross-channel attempts → 33 backend enumerations instead of 2: without the
  guard every refused attempt schedules its own probe and ManualClock fires all of them.)

**Stayed GREEN (confirmed):** all eight other recovery facts
(`Recovery_EndpointReturns_...`, `Recovery_FailureCounting_...`,
`Recovery_CooldownEnforcedBeforeAttempt_...`, `Recovery_RepeatedFailure_...`,
`Recovery_Panic_...`, `Recovery_Teardown_...`, `Recovery_HealthySession_...`,
`Recovery_ProbeThrows_...`) and all 21 landed arbitration facts.

**Note for the record:** the pin originally kept the endpoint PRESENT during the probe burst
and passed even with the guard reverted — the probe callback's not-suppressed no-op absorbed
the duplicate schedules (SP-067-class falsely-verified pin, caught and fixed by keeping the
endpoint down during the burst so duplicate schedules produce duplicate backend init calls).
