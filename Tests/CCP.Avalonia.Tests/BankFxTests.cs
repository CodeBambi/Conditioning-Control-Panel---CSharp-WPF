using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Progression;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>THE BANK on the header's XP counter (WPF MainWindow.BankFx.cs): the readout is held while a
/// completion's pot collects and flies, weather passes straight through, and every way out of the hold
/// leaves the display on the ledger. The flight's look is owed a desk run.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class BankFxTests
{
    private static void OnShell(Action<MainShellWindow, AppSettings, Action<double>> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.PerformanceMode = true;                    // no Forever loops under RunJobs; the gate is forced below
        s.PlayerLevel = 5;
        s.PlayerXP = 10;
        double now = 0;
        FxTrack.ManualClock = true;
        var shell = new MainShellWindow { BankClockOverride = () => now, BankGateOverride = true };
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.UpdateLevelDisplay();
            shell.SettleXpOdometerForTests();
            body(shell, s, ms => now += ms);
        }
        finally { FxTrack.ManualClock = false; shell.Close(); CoreSettings.ServiceProvider = oldProvider; }
    });

    private static string Readout(MainShellWindow shell)
    {
        shell.SettleXpOdometerForTests();
        return shell.FindControl<TextBlock>("TxtXP")!.Text!;
    }

    private static string Truth(AppSettings s) =>
        $"{(int)s.PlayerXP} / {(int)XpCurve.GetXPForLevel(s.PlayerLevel, XpCurve.EpochOf(s))} XP";

    [Fact]
    public void WeatherIsNeverHeld() => OnShell((shell, s, _) =>
    {
        s.PlayerXP += 5;
        shell.OnBankAward(5, "Flash");
        Assert.False(shell.BankHolding);
        Assert.False(shell.BankFlightLive);
        Assert.Equal(Truth(s), Readout(shell));
    });

    [Fact]
    public void ACompletionHoldsTheCounter_FliesAfterTheWindow_AndLandsOnTheLedger() => OnShell((shell, s, wait) =>
    {
        string before = Readout(shell);
        s.PlayerXP += 40;
        shell.OnBankAward(40, "Quest");
        Assert.True(shell.BankHolding);                                    // the readout waits for the tokens
        Assert.True(shell.BankPollRunning);
        Assert.Equal(before, Readout(shell));

        s.PlayerXP += 3;                                                   // weather under an open pot: still held
        shell.OnBankAward(3, "Flash");
        Assert.True(shell.BankHolding);
        Assert.Equal(before, Readout(shell));

        wait(BankAccumulator.WindowMs - 100);
        shell.BankPollTickForTests();
        Assert.True(shell.BankHolding);                                    // the pot is still collecting

        wait(200);
        shell.BankPollTickForTests();                                      // the pot ripens: the flight launches
        shell.BankLayer?.StepForTests(120);                                // every token lands (or settled at once headless)
        Assert.False(shell.BankFlightLive);
        Assert.False(shell.BankHolding);
        Assert.Equal(Truth(s), Readout(shell));                            // the display stands on the ledger
        shell.BankPopRun?.Finish();
        Assert.Equal(1, shell.BankPopScaleNow);                            // the catch ends at rest
        shell.BankPollTickForTests();
        Assert.False(shell.BankPollRunning);                               // no idle clock between flights
    });

    [Fact]
    public void ALevelUpOwnsTheMoment_TheHoldIsDroppedOnTheTruth() => OnShell((shell, s, _) =>
    {
        s.PlayerXP += 40;
        shell.OnBankAward(40, "Session");
        Assert.True(shell.BankHolding);

        s.PlayerLevel++;
        s.PlayerXP = 2;
        shell.OnBankAward(500, "Quest");
        Assert.False(shell.BankHolding);
        Assert.False(shell.BankFlightLive);
        Assert.Equal(Truth(s), Readout(shell));
    });

    [Fact]
    public void WithTheGateShut_TheBankIsSkippedOutright() => OnShell((shell, s, wait) =>
    {
        shell.BankGateOverride = false;                                    // reduced motion, or the window not in front
        s.PlayerXP += 40;
        shell.OnBankAward(40, "Quest");
        Assert.False(shell.BankHolding);
        Assert.Equal(Truth(s), Readout(shell));
        wait(5000);
        shell.BankPollTickForTests();
        Assert.False(shell.BankFlightLive);
        Assert.Null(shell.BankLayer);                                      // no token surface was ever made
    });

    [Fact]
    public void AHoldWhoseFlightNeverLands_IsHealedByThePoll() => OnShell((shell, s, wait) =>
    {
        s.PlayerXP += 40;
        shell.OnBankAward(40, "LockCard");
        Assert.True(shell.BankHolding);
        shell.BankGateOverride = false;                                    // the user walked away while the pot was open
        wait(BankAccumulator.WindowMs + 100);
        shell.BankPollTickForTests();                                      // the launch is refused: abort, on the truth
        Assert.False(shell.BankHolding);
        Assert.False(shell.BankFlightLive);
        Assert.Equal(Truth(s), Readout(shell));
    });
}
