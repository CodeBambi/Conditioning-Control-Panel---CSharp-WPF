using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// WPF 41288a884 + acd51fcf9 (HoverBubbleBarTests): the five Home pills and the account strip's
/// controls become one line of hover bubbles, driven through the real shell from startup.
/// </summary>
public sealed class HoverBubbleBarTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public void AccountStripIsOneLineOfBubblesThatOpenOneAtATime() => AvaloniaTestDispatcher.Run(LineBody);

    private static void LineBody()
    {
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var motionBefore = s.MotionLevel;
            MainShellWindow? w = null;
            try
            {
                s.MotionLevel = MotionLevel.Off;   // instant states: widths are assertable
                w = new MainShellWindow();
                w.Show();
                Dispatcher.UIThread.RunJobs();
                var dash = w.Named<SettingsTabView>("SettingsTab")!;
                var bar = dash.FindControl<HoverBubbleBar>("HomeBubbleBar")!;
                Button B(string n) => dash.FindControl<Button>(n)!;

                // WPF order: Link phone, Logout | Discord pill (not a bubble), RP | five quick bubbles.
                Assert.Equal(new[] { "BtnLinkPhone", "BtnQuickLogout", "ChkQuickDiscordRichPresence", "VelvetBtnWebcam",
                                     "VelvetBtnSystem", "VelvetBtnSchedulerRamp", "VelvetBtnCatalogue", "VelvetBtnAppInfo" },
                             bar.Bubbles.Select(b => b.Name));
                Assert.Same(bar, B("BtnDiscord").Parent);
                Assert.DoesNotContain(B("BtnDiscord"), bar.Bubbles);
                Assert.Null(dash.FindControl<Grid>("VelvetHelperButtonRow"));   // the row went back to the mosaic

                // Labels come from Loc with the leading emoji stripped; two keys join with " + ".
                string Strip(string k) => HoverBubbleBar.StripLeadingGlyph(Loc.Get(k));
                Assert.Equal(Strip("section_scheduler") + " + " + Strip("section_intensity_ramp"), bar.LabelOf(B("VelvetBtnSchedulerRamp")));
                Assert.Equal(Strip("btn_home_catalogue"), AutomationProperties.GetName(B("VelvetBtnCatalogue")));
                Assert.Equal(Color.Parse("#FF69B4"), bar.RestFillOf(B("BtnQuickLogout")));
                Assert.Equal(HoverBubbleBar.RestFill, bar.RestFillOf(B("VelvetBtnSystem")));

                // At the default window the whole line fits: Login keeps its width, the "?" is inside the strip.
                var strip = dash.FindControl<Border>("AccountStrip")!;
                Assert.True(B("BtnUnifiedLogin").Bounds.Width > 40, $"login {B("BtnUnifiedLogin").Bounds.Width}");
                var help = B("HelpBtnQuickLinks").TranslatePoint(new Point(B("HelpBtnQuickLinks").Bounds.Width, 0), strip)!.Value;
                Assert.True(help.X <= strip.Bounds.Width, $"help {help.X} > {strip.Bounds.Width}");

                // Signed out: Link phone and Logout follow the logged-in face.
                Assert.Equal(dash.FindControl<Border>("LoggedInStatusPanel")!.IsVisible, B("BtnQuickLogout").IsVisible);

                // Hover opens one label; the next closes the first. The button keeps its 34 px slot.
                var sys = B("VelvetBtnSystem");
                var cat = B("VelvetBtnCatalogue");
                double slot = sys.Bounds.Width;
                Assert.InRange(slot, HoverBubbleBar.BubbleSize, HoverBubbleBar.BubbleSize + 1.5);
                // Real pointer input (P04): the PointerEntered wiring opens the label.
                Point Centre(Button b) => b.TranslatePoint(new Point(b.Bounds.Width - 6, b.Bounds.Height / 2), w)!.Value;
                w.CaptureRenderedFrame();   // headless hit testing reads the last rendered frame
                w.MouseMove(Centre(sys));
                Dispatcher.UIThread.RunJobs();
                Assert.True(bar.IsExpanded(sys));
                Assert.True(bar.LabelWidthOf(sys) > 0);
                Assert.Equal(slot, sys.Bounds.Width, 1);
                Assert.False(sys.ClipToBounds);   // the open plate overhangs the slot to the left
                w.CaptureRenderedFrame();
                w.MouseMove(Centre(cat));
                Dispatcher.UIThread.RunJobs();
                Assert.True(bar.IsExpanded(cat));
                Assert.False(bar.IsExpanded(sys));
                Assert.Equal(0, bar.LabelWidthOf(sys));
                w.CaptureRenderedFrame();
                w.MouseMove(new Point(2, 2));   // leaving collapses it
                Dispatcher.UIThread.RunJobs();
                Assert.False(bar.IsExpanded(cat));

                // The Rich Presence switch is lit only while checked.
                var rp = dash.FindControl<CheckBox>("ChkQuickDiscordRichPresence")!;
                rp.IsChecked = false;
                Assert.False(bar.IsLit(rp));
                rp.IsChecked = true;
                Assert.True(bar.IsLit(rp));
                rp.IsChecked = false;
            }
            finally
            {
                w?.Close();
                s.MotionLevel = motionBefore;
            }
        }
    }

    [Fact]
    public void IdlePulseRunsOnlyWhileHomeIsShownAndAmbientLoopsAreOn() => AvaloniaTestDispatcher.Run(PulseBody);

    private static void PulseBody()
    {
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var motionBefore = s.MotionLevel;
            bool perfBefore = s.PerformanceMode;
            MainShellWindow? w = null;
            try
            {
                s.MotionLevel = MotionLevel.Full;
                s.PerformanceMode = false;
                w = new MainShellWindow();
                w.Show();
                w.ShowTab("settings");
                Dispatcher.UIThread.RunJobs();
                var dash = w.Named<SettingsTabView>("SettingsTab")!;
                var bar = dash.FindControl<HoverBubbleBar>("HomeBubbleBar")!;

                Assert.True(bar.PulseRunning);
                var first = bar.PulseNext();
                Assert.NotNull(first);
                Assert.NotSame(first, bar.PulseNext());   // round robin

                w.ShowTab("haptics");
                Dispatcher.UIThread.RunJobs();
                Assert.False(bar.PulseRunning);
                Assert.Null(bar.PulseNext());
                w.ShowTab("settings");
                Dispatcher.UIThread.RunJobs();
                Assert.True(bar.PulseRunning);

                w.WindowState = WindowState.Minimized;
                Dispatcher.UIThread.RunJobs();
                Assert.False(bar.PulseRunning);
                w.WindowState = WindowState.Normal;
                Dispatcher.UIThread.RunJobs();
                Assert.True(bar.PulseRunning);

                // An open label holds the cue; Motion Off stops it.
                bar.Expand(bar.Bubbles.First(b => b.IsVisible), animate: false);
                Assert.Null(bar.PulseNext());
                s.MotionLevel = MotionLevel.Off;
                Assert.Null(bar.PulseNext());

                w.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.False(bar.PulseRunning);   // P65: no timer outlives the window
                w = null;
            }
            finally
            {
                w?.Close();
                s.MotionLevel = motionBefore;
                s.PerformanceMode = perfBefore;
            }
        }
    }
}
