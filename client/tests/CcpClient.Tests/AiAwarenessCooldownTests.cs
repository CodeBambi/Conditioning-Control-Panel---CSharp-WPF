using CcpClient.Desktop.Ai;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Cooldown machinery tests (SP-042 slice c5; contract §4 rule 2; admission §5 rule 3).
/// Proves: the 4 typed classes, extend-not-shrink per (kind, key), live-check boundary
/// (admitted at exact equality — WPF `UtcNow &lt; expiresAt` KeywordTriggerService.cs:94),
/// expiry + prune-on-access, class/key independence, and the recorded WPF baseline VALUES
/// as facts (never decisions — the 10-vs-90 owner question stands verbatim, §9.2 #4).
/// Clocks are injected (the c3 AiModerationEscalation precedent) — fully deterministic.
/// </summary>
public class AiAwarenessCooldownTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    private sealed class Clock
    {
        public DateTimeOffset Now = T0;
        public DateTimeOffset Read() => Now;
    }

    // ---- admission / suppression per class ----

    [Fact]
    public void EmptyRegistry_AdmitsAllFourClasses()
    {
        var clock = new Clock();
        var registry = new AiCooldownRegistry(clock.Read);

        foreach (var kind in Enum.GetValues<AiCooldownKind>())
        {
            Assert.IsType<AiCooldownVerdict.Admitted>(registry.Check(kind, "k"));
        }
    }

    [Fact]
    public void Extend_ThenCheck_SuppressesWithTypedKindAndExpiry_UntilClockPasses()
    {
        var clock = new Clock();
        var registry = new AiCooldownRegistry(clock.Read);

        var expiry = registry.Extend(AiCooldownKind.Global, "g", TimeSpan.FromSeconds(10));
        Assert.Equal(T0.AddSeconds(10), expiry);

        var suppressed = Assert.IsType<AiCooldownVerdict.Suppressed>(registry.Check(AiCooldownKind.Global, "g"));
        Assert.Equal(AiCooldownKind.Global, suppressed.Kind);
        Assert.Equal(expiry, suppressed.Until);

        clock.Now = T0.AddSeconds(9.999);
        Assert.IsType<AiCooldownVerdict.Suppressed>(registry.Check(AiCooldownKind.Global, "g"));

        // Live iff now < expiry (WPF boundary): admitted at EXACT equality.
        clock.Now = T0.AddSeconds(10);
        Assert.IsType<AiCooldownVerdict.Admitted>(registry.Check(AiCooldownKind.Global, "g"));
    }

    [Fact]
    public void ClassesAndKeys_AreIndependent()
    {
        var clock = new Clock();
        var registry = new AiCooldownRegistry(clock.Read);

        registry.Extend(AiCooldownKind.PerKeyword, "alpha", TimeSpan.FromSeconds(15));

        Assert.IsType<AiCooldownVerdict.Suppressed>(registry.Check(AiCooldownKind.PerKeyword, "alpha"));
        Assert.IsType<AiCooldownVerdict.Admitted>(registry.Check(AiCooldownKind.PerKeyword, "beta"));
        Assert.IsType<AiCooldownVerdict.Admitted>(registry.Check(AiCooldownKind.LoopProtection, "alpha"));
        Assert.IsType<AiCooldownVerdict.Admitted>(registry.Check(AiCooldownKind.PerTrigger, "alpha"));
        Assert.IsType<AiCooldownVerdict.Admitted>(registry.Check(AiCooldownKind.Global, "alpha"));
    }

    // ---- extend-not-shrink (WPF mechanism, KeywordTriggerService.cs:178-181) ----

    [Fact]
    public void ExtendNotShrink_ShorterRequestAgainstLiveLonger_KeepsExistingExpiry()
    {
        var clock = new Clock();
        var registry = new AiCooldownRegistry(clock.Read);

        var longer = registry.Extend(AiCooldownKind.LoopProtection, "k", TimeSpan.FromSeconds(60));
        clock.Now = T0.AddSeconds(10);
        var result = registry.Extend(AiCooldownKind.LoopProtection, "k", TimeSpan.FromSeconds(5));

        Assert.Equal(longer, result); // the live, longer cooldown is never shortened
        var suppressed = Assert.IsType<AiCooldownVerdict.Suppressed>(registry.Check(AiCooldownKind.LoopProtection, "k"));
        Assert.Equal(longer, suppressed.Until);
    }

    [Fact]
    public void ExtendNotShrink_LongerRequest_ExtendsTheCooldown()
    {
        var clock = new Clock();
        var registry = new AiCooldownRegistry(clock.Read);

        registry.Extend(AiCooldownKind.PerKeyword, "k", TimeSpan.FromSeconds(5));
        clock.Now = T0.AddSeconds(1);
        var extended = registry.Extend(AiCooldownKind.PerKeyword, "k", TimeSpan.FromSeconds(30));

        Assert.Equal(T0.AddSeconds(31), extended);
        var suppressed = Assert.IsType<AiCooldownVerdict.Suppressed>(registry.Check(AiCooldownKind.PerKeyword, "k"));
        Assert.Equal(extended, suppressed.Until);
    }

    [Fact]
    public void Extend_AgainstExpiredEntry_ReplacesIt()
    {
        var clock = new Clock();
        var registry = new AiCooldownRegistry(clock.Read);

        registry.Extend(AiCooldownKind.PerTrigger, "t", TimeSpan.FromSeconds(5));
        clock.Now = T0.AddSeconds(10); // expired
        var fresh = registry.Extend(AiCooldownKind.PerTrigger, "t", TimeSpan.FromSeconds(3));

        Assert.Equal(T0.AddSeconds(13), fresh);
    }

    // ---- observable suppression + argument discipline ----

    [Fact]
    public void Suppression_IsTypedAndObservable_NeverSilent()
    {
        var clock = new Clock();
        var registry = new AiCooldownRegistry(clock.Read);
        registry.Extend(AiCooldownKind.Global, "g", TimeSpan.FromSeconds(10));

        AiCooldownVerdict verdict = registry.Check(AiCooldownKind.Global, "g");

        // The typed shape is the observability contract: kind + expiry, never a bare bool.
        var suppressed = Assert.IsType<AiCooldownVerdict.Suppressed>(verdict);
        Assert.Equal(AiCooldownKind.Global, suppressed.Kind);
        Assert.True(suppressed.Until > T0);
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        var registry = new AiCooldownRegistry();
        Assert.ThrowsAny<ArgumentException>(() => registry.Check(AiCooldownKind.Global, ""));
        Assert.ThrowsAny<ArgumentException>(() => registry.Extend(AiCooldownKind.Global, "", TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.Extend(AiCooldownKind.Global, "g", TimeSpan.FromSeconds(-1)));
    }

    // ---- recorded WPF baseline values (facts, never decisions; §9.2 #4 owner-pending) ----

    [Fact]
    public void WpfBaselinePlaceholder_RecordsTheBaselineFacts()
    {
        // Reaction 10s (AppSettings.cs:3000-3008, clamp 10-600 — the `?? 90` fallback at
        // WindowAwarenessService.cs:374-388 is dead code against this non-nullable default;
        // OWNER QUESTION 10-or-90 carried verbatim, never answered here); global 10s
        // (:4294); per-keyword 15s (:4314); loop protection 5s (:4438,4450).
        var values = AiCooldownValues.WpfBaselinePlaceholder;
        Assert.Equal(TimeSpan.FromSeconds(10), values.Reaction);
        Assert.Equal(TimeSpan.FromSeconds(10), values.Global);
        Assert.Equal(TimeSpan.FromSeconds(15), values.PerKeyword);
        Assert.Equal(TimeSpan.FromSeconds(5), values.LoopProtection);
    }
}
