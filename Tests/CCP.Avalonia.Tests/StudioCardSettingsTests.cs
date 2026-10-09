using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Studio;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

internal static class StudioCardApp
{
    internal static void Ensure()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}

/// <summary>Brain Drain card: the visual rows, the volume fold and the folder lines round-trip
/// (WPF BrainDrainFeatureControl LoadFromSettings and its *_Changed handlers).</summary>
public sealed class BrainDrainCardSettingsTests
{
    [Fact]
    public async Task Visual_rows_and_volume_write_the_setting_and_a_fresh_card_shows_it()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            StudioCardApp.Ensure();
            var s = CoreSettings.Current;
            var saved = (s.BrainDrainBlurStrength, s.BrainDrainMeltEnabled, s.BrainDrainKeepPicturesClear,
                s.AllowOverlayCapture, s.BrainDrainVolume);
            try
            {
                (s.BrainDrainBlurStrength, s.BrainDrainMeltEnabled, s.BrainDrainKeepPicturesClear,
                    s.AllowOverlayCapture, s.BrainDrainVolume) = (50, false, false, false, 100);
                var card = new BrainDrainFeatureControl();
                Assert.False(card.FindControl<MoreFold>("FoldMore")!.IsOpen);   // default volume stays folded
                Assert.False(string.IsNullOrWhiteSpace(card.FindControl<TextBlock>("TxtAudioFolderPath")!.Text));

                card.FindControl<Slider>("SliderBlurStrength")!.Value = 73;
                card.FindControl<CheckBox>("ChkMelt")!.IsChecked = true;
                card.FindControl<CheckBox>("ChkKeepClear")!.IsChecked = true;
                card.FindControl<CheckBox>("ChkAllowCapture")!.IsChecked = true;
                card.FindControl<Slider>("SliderVolume")!.Value = 40;

                Assert.Equal(73, s.BrainDrainBlurStrength);
                Assert.True(s.BrainDrainMeltEnabled);
                Assert.True(s.BrainDrainKeepPicturesClear);
                Assert.True(s.AllowOverlayCapture);
                Assert.Equal(40, s.BrainDrainVolume);
                Assert.Equal("40%", card.FindControl<TextBlock>("TxtVolume")!.Text);

                var fresh = new BrainDrainFeatureControl();
                Assert.Equal(73, fresh.FindControl<Slider>("SliderBlurStrength")!.Value);
                Assert.Equal("73%", fresh.FindControl<TextBlock>("TxtBlurStrength")!.Text);
                Assert.True(fresh.FindControl<CheckBox>("ChkMelt")!.IsChecked);
                Assert.True(fresh.FindControl<CheckBox>("ChkKeepClear")!.IsChecked);
                Assert.True(fresh.FindControl<CheckBox>("ChkAllowCapture")!.IsChecked);
                Assert.Equal(40, fresh.FindControl<Slider>("SliderVolume")!.Value);
                Assert.True(fresh.FindControl<MoreFold>("FoldMore")!.IsOpen);   // a changed setting is never hidden
            }
            finally
            {
                (s.BrainDrainBlurStrength, s.BrainDrainMeltEnabled, s.BrainDrainKeepPicturesClear,
                    s.AllowOverlayCapture, s.BrainDrainVolume) = saved;
            }
            return Task.CompletedTask;
        });
    }
}

/// <summary>Visuals card: the GIF speed row (WPF VisualsFeatureControl SliderGifSpeed, #1194).</summary>
public sealed class VisualsCardSettingsTests
{
    [Fact]
    public async Task Gif_speed_writes_the_setting_and_a_fresh_card_shows_it()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            StudioCardApp.Ensure();
            var s = CoreSettings.Current;
            var saved = s.FlashGifSpeedMultiplier;
            try
            {
                s.FlashGifSpeedMultiplier = 1;
                var card = new VisualsFeatureControl();
                var slider = card.FindControl<Slider>("SliderGifSpeed")!;
                Assert.Equal(0.25, slider.Minimum);
                Assert.Equal(4, slider.Maximum);
                Assert.Equal("1.0x", card.FindControl<TextBlock>("TxtGifSpeed")!.Text);

                slider.Value = 2.5;
                Assert.Equal(2.5, s.FlashGifSpeedMultiplier);
                Assert.Equal("2.5x", card.FindControl<TextBlock>("TxtGifSpeed")!.Text);

                var fresh = new VisualsFeatureControl();
                Assert.Equal(2.5, fresh.FindControl<Slider>("SliderGifSpeed")!.Value);
                Assert.Equal("2.5x", fresh.FindControl<TextBlock>("TxtGifSpeed")!.Text);
            }
            finally { s.FlashGifSpeedMultiplier = saved; }
            return Task.CompletedTask;
        });
    }
}

/// <summary>Flash card: stay until popped, the exit picker and its Shatter rule, the v2 switches, and
/// the rebuild when a prize lands after the card loaded (WPF FlashFeatureControl).</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class FlashCardSwitchTests
{
    [Fact]
    public async Task Stay_exit_and_v2_switches_write_the_setting_and_a_fresh_card_shows_it()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            StudioCardApp.Ensure();
            var s = CoreSettings.Current;
            var saved = (s.FlashStayUntilPopped, s.FlashExitStyle, s.FlashRoundedCorners, s.FlashDraggable,
                s.FlashShatterEnabled, s.FlashClickable);
            var seam = PrizeOwnership.IsGranted;
            try
            {
                (s.FlashStayUntilPopped, s.FlashExitStyle, s.FlashRoundedCorners, s.FlashDraggable,
                    s.FlashShatterEnabled, s.FlashClickable) = (false, FlashExitStyle.Mix, false, false, false, true);

                PrizeOwnership.IsGranted = _ => false;
                var unowned = new FlashFeatureControl();
                Assert.False(unowned.FindControl<Grid>("RowRoundedCorners")!.IsVisible);
                Assert.False(unowned.FindControl<Grid>("RowDraggable")!.IsVisible);
                Assert.False(unowned.FindControl<Grid>("RowShatter")!.IsVisible);

                PrizeOwnership.IsGranted = id => id == PrizeOwnership.FlashPendulum;
                var card = new FlashFeatureControl();
                Assert.True(card.FindControl<Border>("BoxFlashV2")!.IsVisible);
                Assert.True(card.FindControl<Grid>("RowRoundedCorners")!.IsVisible);
                Assert.True(card.FindControl<Grid>("RowDraggable")!.IsVisible);
                Assert.True(card.FindControl<Grid>("RowShatter")!.IsVisible);

                var exit = card.FindControl<ComboBox>("CmbExit")!;
                Assert.Equal(7, exit.Items.Count);
                Assert.True(exit.IsEnabled);

                card.FindControl<CheckBox>("ChkStayUntilPopped")!.IsChecked = true;
                exit.SelectedItem = exit.Items.OfType<ComboBoxItem>().First(i => (FlashExitStyle)i.Tag! == FlashExitStyle.Melt);
                card.FindControl<CheckBox>("ChkFlashRoundedCorners")!.IsChecked = true;
                card.FindControl<CheckBox>("ChkFlashDraggable")!.IsChecked = true;
                Assert.True(s.FlashStayUntilPopped);
                Assert.Equal(FlashExitStyle.Melt, s.FlashExitStyle);
                Assert.True(s.FlashRoundedCorners);
                Assert.True(s.FlashDraggable);

                // #1386: an owned Shatter decides the click, so the exit picker greys out and says why.
                card.FindControl<CheckBox>("ChkFlashShatter")!.IsChecked = true;
                Assert.True(s.FlashShatterEnabled);
                Assert.False(exit.IsEnabled);
                Assert.True(card.FindControl<TextBlock>("TxtExitShatterNote")!.IsVisible);

                var fresh = new FlashFeatureControl();
                Assert.True(fresh.FindControl<CheckBox>("ChkStayUntilPopped")!.IsChecked);
                Assert.Equal(FlashExitStyle.Melt, (FlashExitStyle)((ComboBoxItem)fresh.FindControl<ComboBox>("CmbExit")!.SelectedItem!).Tag!);
                Assert.True(fresh.FindControl<CheckBox>("ChkFlashRoundedCorners")!.IsChecked);
                Assert.True(fresh.FindControl<CheckBox>("ChkFlashDraggable")!.IsChecked);
                Assert.True(fresh.FindControl<CheckBox>("ChkFlashShatter")!.IsChecked);
                Assert.False(fresh.FindControl<ComboBox>("CmbExit")!.IsEnabled);

                // Shatter on but not owned: the picker stays live.
                PrizeOwnership.IsGranted = _ => false;
                Assert.True(new FlashFeatureControl().FindControl<ComboBox>("CmbExit")!.IsEnabled);
            }
            finally
            {
                (s.FlashStayUntilPopped, s.FlashExitStyle, s.FlashRoundedCorners, s.FlashDraggable,
                    s.FlashShatterEnabled, s.FlashClickable) = saved;
                PrizeOwnership.IsGranted = seam;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_prize_granted_after_load_brings_the_motion_row_in()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            StudioCardApp.Ensure();
            var s = CoreSettings.Current;
            var style = s.FlashMotionStyle;
            var (seam, account) = (PrizeOwnership.IsGranted, PrizeOwnership.CurrentAccount);
            Window? host = null;
            try
            {
                PrizeOwnership.Clear();
                PrizeOwnership.CurrentAccount = () => "acct-test";
                var card = new FlashFeatureControl();
                host = new Window { Content = card };
                host.Show();
                Assert.False(card.FindControl<Grid>("RowMotion")!.IsVisible);

                PrizeOwnership.ApplySnapshot("acct-test", 1, new[] { PrizeOwnership.FlashDriftBounce });
                Dispatcher.UIThread.RunJobs();
                Assert.True(card.FindControl<Grid>("RowMotion")!.IsVisible);
                Assert.True(card.FindControl<Border>("BoxFlashV2")!.IsVisible);
                Assert.Equal(style, s.FlashMotionStyle);   // the rebuild selects, it never writes

                // Detached: the card no longer listens.
                host.Close();
                host = null;
                Dispatcher.UIThread.RunJobs();
                PrizeOwnership.Clear();
                Dispatcher.UIThread.RunJobs();
                Assert.True(card.FindControl<Grid>("RowMotion")!.IsVisible);
            }
            finally
            {
                host?.Close();
                PrizeOwnership.Clear();
                (PrizeOwnership.IsGranted, PrizeOwnership.CurrentAccount) = (seam, account);
                s.FlashMotionStyle = style;
            }
            return Task.CompletedTask;
        });
    }
}
