using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Controls.Header;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Rows 97/99: the header Premium spark (WPF Controls/Header/PremiumSpark.xaml.cs). From
/// shell startup: it sits in the header, wears the canonical tier, its clock runs only while it is
/// visible and the motion level allows (P01), stops on detach (P65), and a click opens Premium.</summary>
public sealed class PremiumSparkTests
{
    private static void Platform()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task TheHeaderSparkWearsTheTierParksAndOpensPremium()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Platform();
            var s = CoreSettings.Current;
            var old = (s.MotionLevel, s.PerformanceMode);
            var (premium, lab) = (CoreAccount.HasPremiumAccessProvider, CoreAccount.HasLabAccessProvider);
            MainShellWindow? shell = null;
            try
            {
                (s.MotionLevel, s.PerformanceMode) = (MotionLevel.Full, false);
                CoreAccount.HasPremiumAccessProvider = () => false;
                CoreAccount.HasLabAccessProvider = () => false;
                shell = new MainShellWindow { Width = 1400, Height = 900 };
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                var spark = shell.Named<PremiumSpark>("HeaderPremiumSpark")!;
                Assert.Equal(SparkTier.Free, spark.Tier);
                Assert.False(spark.LoopsRunning);            // Free never moves
                Assert.Equal(PremiumSparkRules.FreeOpacity, spark.Opacity);

                // Prime through the shell's tier choke point (WPF UpdatePatreonUI -> RefreshPremiumSpark).
                CoreAccount.HasPremiumAccessProvider = () => true;
                CoreAccount.HasLabAccessProvider = () => true;
                shell.RefreshProfileBubble();
                Assert.Equal(SparkTier.Prime, spark.Tier);
                Assert.True(spark.LoopsRunning);
                Assert.True(spark.Field.Alive.Count > 0, "prewarm left a bare card");
                // A same-state refresh (sign-in, rollover) keeps the running field (WPF Apply :144).
                spark.Advance(0.5);
                var before = spark.Field.Alive[0];
                shell.RefreshProfileBubble();
                Assert.Equal(before, spark.Field.Alive[0]);

                // P01: hidden -> parked; shown -> running again.
                spark.IsVisible = false;
                Assert.False(spark.LoopsRunning);
                spark.IsVisible = true;
                Assert.True(spark.LoopsRunning);

                // Motion Off stills it to the static lit card.
                s.MotionLevel = MotionLevel.Off;
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(SparkMotion.Off, spark.Motion);
                Assert.False(spark.LoopsRunning);
                s.MotionLevel = MotionLevel.Full;
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
                Dispatcher.UIThread.RunJobs();
                Assert.True(spark.LoopsRunning);

                // Click opens Premium (WPF HeaderPremiumSpark_Click).
                shell.ShowTab("settings");
                Dispatcher.UIThread.RunJobs();
                spark.OnClick();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("premium", shell.CurrentTab);

                // P65: detaching (no visibility change) stops its clock.
                Assert.True(spark.LoopsRunning);
                ((Panel)spark.Parent!).Children.Remove(spark);
                Dispatcher.UIThread.RunJobs();
                Assert.False(spark.LoopsRunning);
            }
            finally
            {
                shell?.Close();
                (s.MotionLevel, s.PerformanceMode) = old;
                (CoreAccount.HasPremiumAccessProvider, CoreAccount.HasLabAccessProvider) = (premium, lab);
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>Render() swallows exceptions (a broken frame must not take the header down), so prove
    /// it actually paints: a Basic card is gold at the star's heart and the label strip is inked.</summary>
    [Fact]
    public async Task TheBasicCardPaintsGold()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Platform();
            var spark = new PremiumSpark { Pinned = true };
            var w = new Window { Content = spark, Width = 120, Height = 80 };
            try
            {
                w.Show();
                spark.Apply(SparkTier.Basic, SparkMotion.Off, false);
                Dispatcher.UIThread.RunJobs();
                using var rt = new RenderTargetBitmap(new PixelSize(54, 42));
                rt.Render(spark);
                var px = new byte[54 * 42 * 4];
                var h = System.Runtime.InteropServices.GCHandle.Alloc(px, System.Runtime.InteropServices.GCHandleType.Pinned);
                try { rt.CopyPixels(new PixelRect(0, 0, 54, 42), h.AddrOfPinnedObject(), px.Length, 54 * 4); }
                finally { h.Free(); }
                int inked = 0;
                (byte b, byte g, byte r, byte a) Px(int x, int y) { var i = (y * 54 + x) * 4; return (px[i], px[i + 1], px[i + 2], px[i + 3]); }
                var heart = Px(24, 20);   // below-left of the heart: the face, under the lamp's wash
                Assert.True(heart.a > 200 && heart.r > 180 && heart.g > 120 && heart.b < heart.r - 40, $"heart {heart}");
                for (int x = 0; x < 54; x++) for (int y = 34; y < 42; y++) if (Px(x, y).a > 0) inked++;
                Assert.True(inked > 40, $"label strip inked {inked}");
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }
}
