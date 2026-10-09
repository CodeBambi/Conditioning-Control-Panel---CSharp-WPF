using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>platform#9: the 6-blink stop (WPF MainWindow.LabTab.cs:156 + BlinkStopGate). Six blinks inside
/// 3.5 s stop everything a panic stops except the camera, and never where the panic key could not (hard rule 6).</summary>
public sealed class BlinkStopTests
{
    [Fact]
    public void GateIsNeverMorePermissiveThanThePanicKey()
    {
        Assert.Equal(BlinkStopGate.Block.None, BlinkStopGate.Check(false, false, true, false));
        Assert.Equal(BlinkStopGate.Block.BlinkTrainer, BlinkStopGate.Check(true, false, true, false));
        Assert.Equal(BlinkStopGate.Block.Lockdown, BlinkStopGate.Check(false, true, true, false));
        Assert.Equal(BlinkStopGate.Block.NoEscape, BlinkStopGate.Check(false, false, false, false));
        Assert.Equal(BlinkStopGate.Block.StrictLock, BlinkStopGate.Check(false, false, true, true));
    }

    [Fact]
    public void SixFastBlinksStopEverythingButTheCamera() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.BlinkRecalibrateShortcutEnabled, s.PanicKeyEnabled, s.StrictLockEnabled);
        var prev = PanicSurfaces.All;
        var stopped = new List<string>();
        PanicSurfaces.All = new[]
        {
            new PanicSurfaces.Surface("engine", _ => stopped.Add("engine")),
            new PanicSurfaces.Surface("tube", _ => stopped.Add("tube")),
            new PanicSurfaces.Surface("games", _ => stopped.Add("games")),
            new PanicSurfaces.Surface("camera", _ => stopped.Add("camera")),
        };
        var shell = new MainShellWindow();
        int offers = 0;
        shell.BlinkRecalOfferForTests = () => { offers++; return Task.CompletedTask; };
        try
        {
            (s.BlinkRecalibrateShortcutEnabled, s.PanicKeyEnabled, s.StrictLockEnabled) = (true, true, false);
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            // Natural blinking (one every 1 s) never builds a run of 6 inside 3.5 s.
            for (int i = 0; i < 10; i++) Assert.False(shell.OnRapidBlink(t0.AddSeconds(i)));
            Assert.Empty(stopped);

            var t1 = t0.AddMinutes(1);
            for (int i = 0; i < 5; i++) Assert.False(shell.OnRapidBlink(t1.AddMilliseconds(i * 550)));
            Assert.True(shell.OnRapidBlink(t1.AddMilliseconds(5 * 550)));
            Assert.Equal(new[] { "engine", "tube" }, stopped);   // camera stays up for recalibration
            Assert.Equal(1, offers);

            // Panic key off ("no escape") and Strict Lock both turn the gesture into plain blinking.
            stopped.Clear();
            s.PanicKeyEnabled = false;
            var t2 = t0.AddMinutes(2);
            for (int i = 0; i < 8; i++) Assert.False(shell.OnRapidBlink(t2.AddMilliseconds(i * 500)));
            s.PanicKeyEnabled = true; s.StrictLockEnabled = true;
            var t3 = t0.AddMinutes(3);
            for (int i = 0; i < 8; i++) Assert.False(shell.OnRapidBlink(t3.AddMilliseconds(i * 500)));
            s.StrictLockEnabled = false; s.BlinkRecalibrateShortcutEnabled = false;
            var t4 = t0.AddMinutes(4);
            for (int i = 0; i < 8; i++) Assert.False(shell.OnRapidBlink(t4.AddMilliseconds(i * 500)));
            Assert.Empty(stopped);
        }
        finally
        {
            PanicSurfaces.All = prev;
            shell.Close();
            (s.BlinkRecalibrateShortcutEnabled, s.PanicKeyEnabled, s.StrictLockEnabled) = saved;
        }
    });

    /// <summary>platform#17: the header mic pill follows WPF UpdateMicPill (consent + wake word = lit).</summary>
    [Fact]
    public void MicPillLightsWhileTheWakeWordHoldsTheMic() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.MicConsentGiven, s.SpeechWakeWordEnabled);
        var shell = new MainShellWindow();
        try
        {
            var pill = shell.FindControl<global::Avalonia.Controls.Border>("MicActivePill")!;
            (s.MicConsentGiven, s.SpeechWakeWordEnabled) = (true, true);
            shell.UpdateMicPill();
            Assert.True(pill.IsVisible);
            s.SpeechWakeWordEnabled = false;
            shell.UpdateMicPill();
            Assert.False(pill.IsVisible);
            shell.UpdateWebcamPill();
            Assert.False(shell.FindControl<global::Avalonia.Controls.Border>("WebcamActivePill")!.IsVisible);
        }
        finally
        {
            shell.Close();
            (s.MicConsentGiven, s.SpeechWakeWordEnabled) = saved;
        }
    });
}
