using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>hunt3 IC7: the moments EMI did not hear. crashRecovered fires once at startup when the
/// last run died with the engine on; emergencyExitOpened is a hold armed and let go together (this
/// head has no games window to hold it for). The game open / close moments are built from a stem.</summary>
[Collection(RunsAloneCollection.Name)]   // swaps the EmiDeskBus sinks and the engine's sentinel switch
public sealed class EmiMissingMomentsTests
{
    private static void WithBus(Action<List<string>> body)
    {
        var (sink, release, consume, armed) = (EmiDeskBus.Sink, EmiDeskBus.ReleaseSink, CrashRecoveryHead.Consume, CoreEngine.CrashSentinel);
        var heard = new List<string>();
        EmiDeskBus.Sink = (id, _) => heard.Add("fire:" + id);
        EmiDeskBus.ReleaseSink = id => heard.Add("release:" + id);
        try { body(heard); }
        finally
        {
            (EmiDeskBus.Sink, EmiDeskBus.ReleaseSink, CrashRecoveryHead.Consume) = (sink, release, consume);
            CoreEngine.CrashSentinel = armed;
        }
    }

    [Fact]
    public void ACrashedLastRun_IsHeardOnce_AndTheEngineArmsTheSentinelFromThenOn() => WithBus(heard =>
    {
        CoreEngine.CrashSentinel = false;
        CrashRecoveryHead.Consume = () => true;
        Assert.True(CrashRecoveryHead.Start());
        Assert.Equal(new[] { "fire:crashRecovered" }, heard);
        Assert.True(CoreEngine.CrashSentinel);

        heard.Clear();
        CrashRecoveryHead.Consume = () => false;
        Assert.False(CrashRecoveryHead.Start());
        Assert.Empty(heard);

        CrashRecoveryHead.Consume = () => throw new InvalidOperationException("no disk");
        Assert.False(CrashRecoveryHead.Start());                 // never throws into startup
        Assert.Empty(heard);
    });

    [Fact]
    public void TheSentinelFileRoundTrips_AndACleanExitClearsIt() => WithBus(_ =>
    {
        EngineCrashSentinel.Clear();
        Assert.False(EngineCrashSentinel.ConsumeAndReport(null));
        EngineCrashSentinel.Mark("started test");
        Assert.True(EngineCrashSentinel.ConsumeAndReport(null));  // reported
        Assert.False(EngineCrashSentinel.ConsumeAndReport(null)); // once

        CoreEngine.CrashSentinel = true;
        EngineCrashSentinel.Mark("started test");
        CrashRecoveryHead.CleanExit();
        Assert.False(EngineCrashSentinel.ConsumeAndReport(null));
    });

    [Fact]
    public void TheEmergencyExitHoldIsArmedAndLetGoTogether() => WithBus(heard =>
    {
        LockdownTabView.FireEmergencyExitMoment();
        Assert.Equal(new[] { "fire:emergencyExitOpened", "release:emergencyExitOpened" }, heard);
    });

    [Theory]
    [InlineData("arcademy", "arcademy")]
    [InlineData("dtrh", "dtrh")]
    [InlineData("fyp", "fyp")]
    public void TheGameHostsFireUnderTheWpfNames(string gameId, string stem) =>
        Assert.Equal(stem, GameWindow.EmiMomentStem(gameId));     // + "Opened" / "Closed" (GameWindow.Host.cs:41, :46)
}
