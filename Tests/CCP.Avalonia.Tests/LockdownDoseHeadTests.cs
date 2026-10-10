using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>HB13: the head's half of the Lockdown Dose keeper (WPF App.xaml.cs:2657). The keeper's
/// rules are Core (LockdownDoseKeeperTests there); here the host drives the real shell's wall
/// toggles and raises the warden's bark, and refuses cleanly when no shell is alive.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class LockdownDoseHeadTests
{
    [Fact]
    public void TheHostFlipsTheShellsWallFeaturesAndBarksTheConscription() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.BouncingTextEnabled, s.MicConsentGiven);
        var raise = CoreBark.RaiseProvider;
        var raised = new List<(string Trigger, IReadOnlyDictionary<string, object>? Values)>();
        s.MicConsentGiven = false;
        var shell = new MainShellWindow();
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            CoreBark.RaiseProvider = (trigger, values, _) => { raised.Add((trigger, values)); return true; };
            var host = MainShellWindow.LockdownDoseHostFor(() => shell);
            Assert.True(host.IsAlive());

            s.BouncingTextEnabled = false;
            host.SetWallFeature("bouncingtext", true);
            Assert.True(s.BouncingTextEnabled);
            host.SetWallFeature("bouncingtext", false);
            Assert.False(s.BouncingTextEnabled);

            host.Bark("Flash and the Spiral", 2, true);
            var bark = Assert.Single(raised);
            Assert.Equal("LockdownConscript", bark.Trigger);
            Assert.Equal("Flash and the Spiral", bark.Values!["features"]);
            Assert.Equal(2.0, bark.Values["round"]);
            Assert.Equal(1.0, bark.Values["engine"]);

            // No shell: nothing can be given back, so the keeper keeps its recovery record.
            var gone = MainShellWindow.LockdownDoseHostFor(() => null);
            Assert.False(gone.IsAlive());
            Assert.Throws<InvalidOperationException>(() => gone.SetWallFeature("flash", false));
        }
        finally
        {
            CoreBark.RaiseProvider = raise;
            CoreEngine.ApplyLive("bouncingtext", false);
            (s.BouncingTextEnabled, s.MicConsentGiven) = saved;
            CoreSettings.SaveImmediate();
            shell.RequestExit();
        }
    });
}
