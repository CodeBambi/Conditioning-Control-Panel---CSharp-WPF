# Bite 3 — cooldown gate reverted alone

**Reverted line:** `SoundArbitration.cs` `ReadyLocked` — the pre-attempt gate
`if (_suppressedUntilUtc is not { } until || _clock.UtcNow >= until)` replaced by `if (true)`
(kick schedules on EVERY refused play while suppressed, cooldown never consulted; the
single-flight guard left intact; nothing else touched).

**Command:** `dotnet test --filter "FullyQualifiedName~SoundArbitrationTests"` (rebuild included).

**Result: RED — 3 failed / 27 passed / 30 total.** Red set (the cooldown pins, plus the
user-story pin that traverses the gate by construction):

- `Recovery_CooldownEnforcedBeforeAttempt_InitCountDoesNotMove` [FAIL] — its own pin
- `Recovery_RepeatedFailure_ExactlyOneAttemptPerCooldownWindow` [FAIL] — the no-busy-loop pin
- `Recovery_EndpointReturns_AfterCooldownNextPlay_PlaysAgain` [FAIL] — the user-story pin
  carries two gate-dependent assertions by construction (the pre-cooldown refusal's honest
  "re-probe after cooldown" reason, and "nothing fires without a play attempt after expiry").
  With the gate removed the FIRST refused play kicks a probe, so both move. Recorded as
  traversal collateral, not a second mechanism: the gate is exercised by every recovery fact
  that starts from a failed startup init, so perfect isolation of the user-story pin from the
  gate revert is not achievable; the two pure cooldown pins above isolate the mechanism.

**Stayed GREEN (confirmed):** `Recovery_FailureCounting_...`,
`Recovery_SingleFlight_ConcurrentAttempts_OneInitCall`, `Recovery_Panic_...`,
`Recovery_Teardown_...`, `Recovery_HealthySession_...`, `Recovery_ProbeThrows_...`, and all
21 landed arbitration facts.

**Re-confirmed at the final tree** (after the pre-completion-consult `_initLock` hardening,
commit 233d2c61): the same revert was re-applied on the final code and produced the identical
RED set; all other pins green; then restored and re-verified green.
