using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Settings ▸ Performance. WPF MainWindow.UiUpdates.cs:2529 CmbMotionLevel_SelectionChanged stops
/// every running ambient loop when the user drops motion to Off and re-arms them on Full; and the
/// Back Room intensity combo (PerformanceSettingsSection.xaml:186) seeds from and writes its setting.
/// </summary>
public sealed class PerformanceSettingsMotionTests
{
    [Fact]
    public async Task MotionComboStopsAndReArmsLoadedAmbientLoops_AndBackRoomComboRoundTrips()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var s = CoreSettings.Current;
            var (motion, fx, perf) = (s.MotionLevel, s.BackRoomFxIntensity, s.PerformanceMode);
            Window? w = null;
            try
            {
                s.MotionLevel = MotionLevel.Full;
                s.PerformanceMode = false;
                s.BackRoomFxIntensity = BackRoomFxIntensity.Calm;

                var section = new PerformanceSettingsSection();
                var vat = new VatGlassCanvas { Width = 200, Height = 200 };
                var orb = new TakeoverOrb { Width = 200, Height = 200 };
                var glyph = new SpiralGlyph { Width = 40, Height = 40 };
                var badge = new TierBadge { Tier = 1, Width = 60, Height = 30 };
                w = new Window { Content = new StackPanel { Children = { section, vat, orb, glyph, badge } }, Width = 700, Height = 1400 };
                w.Show();
                w.Activate();
                orb.SetActive(true);
                Dispatcher.UIThread.RunJobs();
                Assert.True(vat.IsTicking && orb.IsRunning && glyph.IsBreathing && badge.IsAnimating, "a loop never started - the test proves nothing");

                var motionCombo = section.FindControl<ComboBox>("CmbMotionLevel")!;
                motionCombo.SelectedIndex = 2;   // the user picks "Off"
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(MotionLevel.Off, s.MotionLevel);
                Assert.False(vat.IsTicking, "vat kept ticking after motion Off");
                Assert.False(orb.IsRunning, "orb kept ticking after motion Off");
                Assert.False(glyph.IsBreathing, "spiral glyph kept breathing after motion Off");
                Assert.False(badge.IsAnimating, "tier badge kept wobbling after motion Off");

                motionCombo.SelectedIndex = 0;   // back to "Full" re-arms without a restart
                Dispatcher.UIThread.RunJobs();
                Assert.True(vat.IsTicking && orb.IsRunning && glyph.IsBreathing && badge.IsAnimating, "motion Full did not re-arm the loops");

                var fxCombo = section.FindControl<ComboBox>("CmbBackRoomFxIntensity")!;
                Assert.Equal(0, fxCombo.SelectedIndex);   // seeded from Calm
                fxCombo.SelectedIndex = 2;
                Assert.Equal(BackRoomFxIntensity.Full, s.BackRoomFxIntensity);
            }
            finally
            {
                w?.Close();
                s.MotionLevel = motion;
                s.BackRoomFxIntensity = fx;
                s.PerformanceMode = perf;
            }
            return Task.CompletedTask;
        });
    }
}
