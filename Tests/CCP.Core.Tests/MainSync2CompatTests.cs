using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>release/6.11.3 behaviour carried into the Core copies (main sync #2).</summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class MainSync2CompatTests
{
    private static BouncingTextEngine.Logo Logo(double x, double velX) =>
        new() { PosX = x, PosY = 400, VelX = velX, VelY = 0, TextWidth = 100, TextHeight = 60, OverX = 20 };

    [Fact]
    public void BounceWallsSitInsideTheScreenByTheDrawnOverhang()
    {
        // WPF BouncingTextService (5609da6fd): a breathing/tilted line reaches 20 px past its box,
        // so the left wall is at 20, not 0.
        var engine = new BouncingTextEngine(new Random(1));
        engine.SetBounds(0, 0, 1000, 1000);
        var s = new AppSettings();

        var into = Logo(10, -100);
        Assert.True(engine.Step(into, 0, s).Bounced);
        Assert.Equal(20, into.PosX);
        Assert.True(into.VelX > 0);

        // A wall that moved in under a line already heading away nudges it without a bounce.
        var away = Logo(10, 100);
        Assert.False(engine.Step(away, 0, s).Bounced);
        Assert.Equal(20, away.PosX);
    }

    [Fact]
    public void IdentityChangeReachesEveryObserverOnceEvenPastAThrowingOne()
    {
        int calls = 0;
        EventHandler broken = (_, _) => throw new InvalidOperationException();
        EventHandler observer = (_, _) => calls++;
        CoreAccount.UnifiedUserId = null;
        CoreAccount.UnifiedIdentityChanged += broken;
        CoreAccount.UnifiedIdentityChanged += observer;
        try
        {
            CoreAccount.UnifiedUserId = "u_sync2";
            CoreAccount.UnifiedUserId = "u_sync2";
            Assert.Equal(1, calls);
        }
        finally
        {
            CoreAccount.UnifiedIdentityChanged -= observer;
            CoreAccount.UnifiedIdentityChanged -= broken;
            CoreAccount.UnifiedUserId = null;
        }
    }

    [Fact]
    public void AiEffectControlCountsOnlyWhileLabAccessIsLive()
    {
        var p = new CompanionPromptSettings { AllowAiToControlEffects = true };
        Assert.True(AiEffectControlGate.IsOn(p, labAccess: true));
        Assert.False(AiEffectControlGate.IsOn(p, labAccess: false));
        Assert.True(p.AllowAiToControlEffects);
    }
}
