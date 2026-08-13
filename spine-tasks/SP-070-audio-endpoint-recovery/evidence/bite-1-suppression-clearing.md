# Bite 1 — suppression-clearing reverted alone

**Reverted line:** `SoundArbitration.cs` `Initialize` success path — removed
`_audioDisabledForSession = false;` (streak/window resets left intact; nothing else touched).

**Command:** `dotnet test --filter "FullyQualifiedName~SoundArbitrationTests"` (rebuild included).

**Result: RED — 5 failed / 25 passed / 30 total.** Red set (exactly the recovery pins):

- `Recovery_EndpointReturns_AfterCooldownNextPlay_PlaysAgain` [FAIL]
- `Recovery_FailureCounting_EscalatesAtThreshold_SuccessResets` [FAIL]
- `Recovery_SingleFlight_ConcurrentAttempts_OneInitCall` [FAIL]
- `Recovery_Panic_NothingResurrected_ExplicitStopUntouched` [FAIL]
- `Recovery_ProbeThrows_DegradesTyped_FlagClears_NoEscape` [FAIL]

**Stayed GREEN (confirmed):** `Recovery_CooldownEnforcedBeforeAttempt_InitCountDoesNotMove`,
`Recovery_RepeatedFailure_ExactlyOneAttemptPerCooldownWindow`,
`Recovery_Teardown_NoProbeAfterDispose_Ever`,
`Recovery_HealthySession_NoExtraDeviceCalls_NoNewLogLines`, and all 21 landed arbitration
facts (voice/whisper/SFX/queue/duck/panic/device/off-sync-context suites untouched).
