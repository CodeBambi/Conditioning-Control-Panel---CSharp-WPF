using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Bubbles card, U9 + U10: the Bubbles v2 box follows the grants, the Motion picker and the
/// Brain Drain bubble switch write their settings, "Stare to pop" writes its setting and shows the
/// hint while the camera cannot feed a dwell, and a drain bubble pop asks for the ten second haze.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class BubblesV2CardTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public async Task The_v2_box_follows_the_grants_and_the_picker_writes_the_style()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var (style, drain) = (s.BubbleMotionStyle, s.BubbleBrainDrainEnabled);
            var oldGrant = PrizeOwnership.IsGranted;
            try
            {
                PrizeOwnership.IsGranted = _ => false;
                s.BubbleMotionStyle = BubbleMotionStyle.Rain;   // a synced profile carrying an unowned style
                var card = new BubblePopFeatureControl();
                var box = card.FindControl<Border>("V2Box")!;
                var combo = card.FindControl<ComboBox>("CmbMotion")!;
                var chkDrain = card.FindControl<CheckBox>("ChkBrainDrainBubble")!;
                Assert.False(box.IsVisible);
                Assert.False(chkDrain.IsVisible);
                Assert.Single(combo.Items);                                    // Float up only
                Assert.Equal(BubbleMotionStyle.Rain, s.BubbleMotionStyle);     // shown as Float up, the setting is left alone

                PrizeOwnership.IsGranted = id => id == AmbientBubbleMotion.RainGrant;
                card = new BubblePopFeatureControl();
                box = card.FindControl<Border>("V2Box")!;
                combo = card.FindControl<ComboBox>("CmbMotion")!;
                chkDrain = card.FindControl<CheckBox>("ChkBrainDrainBubble")!;
                Assert.True(box.IsVisible);
                Assert.True(chkDrain.IsVisible);
                var tags = combo.Items.OfType<ComboBoxItem>().Select(i => (BubbleMotionStyle)i.Tag!).ToArray();
                Assert.Equal(new[] { BubbleMotionStyle.FloatUp, BubbleMotionStyle.Rain, BubbleMotionStyle.Mix }, tags);
                Assert.Equal(BubbleMotionStyle.Rain, (BubbleMotionStyle)((ComboBoxItem)combo.SelectedItem!).Tag!);

                combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().First(i => (BubbleMotionStyle)i.Tag! == BubbleMotionStyle.Mix);
                Assert.Equal(BubbleMotionStyle.Mix, s.BubbleMotionStyle);

                chkDrain.IsChecked = !s.BubbleBrainDrainEnabled;
                Assert.Equal(chkDrain.IsChecked, s.BubbleBrainDrainEnabled);
            }
            finally
            {
                PrizeOwnership.IsGranted = oldGrant;
                s.BubbleMotionStyle = style; s.BubbleBrainDrainEnabled = drain;
                CoreSettings.SaveImmediate();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Stare_to_pop_writes_the_setting_and_hints_while_the_camera_is_not_ready()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var was = s.BubbleGazePopEnabled;
            var oldReady = BubblePopFeatureControl.GazeReady;
            try
            {
                BubblePopFeatureControl.GazeReady = () => false;
                s.BubbleGazePopEnabled = false;
                var card = new BubblePopFeatureControl();
                var chk = card.FindControl<CheckBox>("ChkBubbleGazePop")!;
                var hint = card.FindControl<TextBlock>("TxtBubbleGazeHint")!;
                Assert.False(chk.IsChecked);
                Assert.True(hint.IsVisible);
                Assert.Empty(BubbleOverlay.GazeTargets());

                chk.IsChecked = true;
                Assert.True(s.BubbleGazePopEnabled);
                Assert.True(chk.IsEnabled);            // the row stays live: a stored preference, not a camera button

                BubblePopFeatureControl.GazeReady = () => true;
                chk.IsChecked = false;
                Assert.False(s.BubbleGazePopEnabled);
                Assert.False(hint.IsVisible);
            }
            finally
            {
                BubblePopFeatureControl.GazeReady = oldReady;
                s.BubbleGazePopEnabled = was;
                CoreSettings.SaveImmediate();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_gaze_pop_is_the_same_pop_as_a_click_and_needs_the_switch()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var (was, dayKey, paid) = (s.BubbleGazePopEnabled, s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday);
            try
            {
                s.AmbientBubbleXpDayKey = "";
                var b = new AmbientBubble { Screen = 0, X = 10, Y = 10, Size = 100, Clickable = true };
                s.BubbleGazePopEnabled = false;
                BubbleOverlay.GazePop(b);
                Assert.False(b.Popping);
                s.BubbleGazePopEnabled = true;
                BubbleOverlay.GazePop(b);
                Assert.True(b.Popping);
                Assert.Equal(5, AmbientBubbleXp.PaidToday(s));
            }
            finally
            {
                s.BubbleGazePopEnabled = was; s.AmbientBubbleXpDayKey = dayKey; s.AmbientBubbleXpPaidToday = paid;
                CoreSettings.SaveImmediate();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_drain_bubble_pop_asks_for_ten_seconds_of_haze_on_the_users_dial()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var (strength, level) = (s.BrainDrainBlurStrength, s.MotionLevel);
            var oldShow = BubbleOverlay.ShowTimedDrain;
            (int Strength, bool Melt, int Ms)? asked = null;
            BubbleOverlay.ShowTimedDrain = (_, st, melt, ms) => { asked = (st, melt, ms); return true; };
            try
            {
                s.BrainDrainBlurStrength = 60;
                s.MotionLevel = MotionLevel.Full;
                Assert.True(BubbleOverlay.FireDrain());
                Assert.Equal((60, true, 10000), asked);

                asked = null;
                s.MotionLevel = MotionLevel.Reduced;       // no warp below Full: the plain blur
                Assert.True(BubbleOverlay.FireDrain());
                Assert.Equal((60, false, 10000), asked);

                asked = null;
                s.BrainDrainBlurStrength = 0;              // the dial at 0: the pop only pays
                Assert.False(BubbleOverlay.FireDrain());
                Assert.Null(asked);
            }
            finally
            {
                BubbleOverlay.ShowTimedDrain = oldShow;
                s.BrainDrainBlurStrength = strength; s.MotionLevel = level;
                CoreSettings.SaveImmediate();
            }
            return Task.CompletedTask;
        });
    }
}
