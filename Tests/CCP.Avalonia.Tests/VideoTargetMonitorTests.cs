using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>ccp-bugs #1154: mandatory videos got their own monitor pick (WPF VideoService.VideoScreens).</summary>
public sealed class VideoTargetMonitorTests
{
    [Theory]
    [InlineData(MonitorTarget.FollowGlobal, 1, 3, 1)]   // Default follows "Show content on"
    [InlineData(2, 1, 3, 2)]                            // the card's own monitor wins
    [InlineData(MonitorTarget.All, 1, 3, MonitorTarget.All)]
    [InlineData(5, 1, 3, 1)]                            // unplugged index follows global, setting kept
    public void The_video_pick_overrides_the_global_one(int video, int global, int screens, int expected) =>
        Assert.Equal(expected, MandatoryVideoOverlay.VideoTarget(video, global, screens));

    [Fact]
    public async Task The_card_offers_Default_and_All_and_saves_the_pick()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = s.VideoTargetMonitor;
            s.VideoTargetMonitor = MonitorTarget.FollowGlobal;
            var (mercy, after) = (s.MercySystemEnabled, s.MercyAfterFails);
            (s.MercySystemEnabled, s.MercyAfterFails) = (true, 3);
            var card = new VideoFeatureControl();
            var host = new Window { Content = card };
            host.Show();
            try
            {
                // c0aa628b8: every row at its default -> the More options fold stays closed.
                var fold = card.FindControl<MoreFold>("FoldMore")!;
                var cmb = card.FindControl<ComboBox>("CmbMonitor")!;
                Assert.False(fold.IsOpen);
                Assert.False(cmb.IsEffectivelyVisible);
                fold.IsOpen = true;
                Assert.True(cmb.IsEffectivelyVisible);
                Assert.Equal(MonitorTarget.FollowGlobal, ((ComboBoxItem)cmb.SelectedItem!).Tag);
                cmb.SelectedItem = cmb.Items.OfType<ComboBoxItem>().Single(i => (int)i.Tag! == MonitorTarget.All);
                Assert.Equal(MonitorTarget.All, s.VideoTargetMonitor);

                // #1145 Mercy: the picker saves the threshold and hides while Mercy is off.
                var after5 = card.FindControl<ComboBox>("CmbMercyAfter")!;
                after5.SelectedItem = after5.Items.OfType<ComboBoxItem>().Single(i => (int)i.Tag! == 5);
                Assert.Equal(5, s.MercyAfterFails);
                card.FindControl<CheckBox>("ChkMercy")!.IsChecked = false;
                Assert.False(s.MercySystemEnabled);
                Assert.False(after5.IsVisible);
            }
            finally
            {
                host.Close();
                s.VideoTargetMonitor = saved;
                (s.MercySystemEnabled, s.MercyAfterFails) = (mercy, after);
            }
            // A changed row opens the fold on load (never hide a changed setting).
            s.VideoTargetMonitor = 0;
            try { Assert.True(new VideoFeatureControl().FindControl<MoreFold>("FoldMore")!.IsOpen); }
            finally { s.VideoTargetMonitor = saved; }
            return Task.CompletedTask;
        });
    }
}
