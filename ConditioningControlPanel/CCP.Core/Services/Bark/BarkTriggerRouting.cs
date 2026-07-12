using System;

namespace ConditioningControlPanel.Core.Services.Bark;

/// <summary>
/// Regression-guard helper for chaos bark routing (BARK-1 slice 3). Mirrors the WPF behavior where the
/// <c>NotifyChaos*</c> calls into <c>Raise(trigger, fill)</c> (Services/Companion/BarkService.cs:261-365):
/// when at least one rule exists for the trigger the rule engine owns the bark (its gates/cooldown apply),
/// and only when NO rule exists does the caller fall back to its random-phrase bark so the chaos companion
/// never goes silent. Exactly one path runs on a single fire (no double-bark). Kept in Core so the
/// guard logic is unit-testable without the Avalonia head.
/// </summary>
public static class BarkTriggerRouting
{
    /// <summary>
    /// Route <paramref name="trigger"/> through the rule engine when at least one rule is registered
    /// for it; otherwise invoke <paramref name="fallback"/> (the head's random-phrase bark). Exactly
    /// one path runs. A null engine always falls back (e.g. heads without the engine wired). The
    /// engine's own gates decide whether a rule-backed trigger actually speaks; a gate-suppressed fire
    /// does NOT fall back, matching WPF where <c>Raise</c> simply returns false (BarkService.cs:773-817).
    /// </summary>
    /// <param name="engine">The bark engine, or null to force the fallback path.</param>
    /// <param name="trigger">WPF trigger key (e.g. "ChaosRunStarted").</param>
    /// <param name="fill">Per-fire context stamp (WPF fill delegate, BarkService.cs:780-781).</param>
    /// <param name="fallback">Invoked when no rule exists for the trigger (or engine is null).</param>
    public static void RouteOrFallback(
        BarkEngine? engine,
        string trigger,
        Action<BarkContext>? fill,
        Action fallback)
    {
        if (fallback == null) throw new ArgumentNullException(nameof(fallback));
        if (engine != null && engine.HasTrigger(trigger))
            engine.Raise(trigger, fill);
        else
            fallback();
    }
}
