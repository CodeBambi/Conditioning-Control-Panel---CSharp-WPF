using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Page wave x1 (P6 / H6): the Home browser card's header row. Mute flips the saved flag,
/// repaints the glyph and sends the page the script; the BambiCloud override saves; the two controls
/// with no host on this head are greyed, not dead.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class HomeBrowserCardTests
{
    private static void Run(Action<SettingsTabView> body)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            try { body(new SettingsTabView()); }
            finally { CoreSettings.ServiceProvider = null; }
        });
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void Mute_FlipsTheSetting_TheGlyph_AndTellsThePage() => Run(view =>
    {
        var s = CoreSettings.Current;
        s.BrowserVideoMuted = false;
        view.SyncBrowserCard();
        var glyph = view.FindControl<TextBlock>("TxtBrowserMute")!;
        var button = view.FindControl<Button>("BtnMuteBrowser")!;
        Assert.Equal("🔊", glyph.Text);

        int before = view.BrowserMuteApplies;
        Click(button);
        Assert.True(s.BrowserVideoMuted);
        Assert.Equal("🔇", glyph.Text);
        Assert.Equal(before + 1, view.BrowserMuteApplies);

        Click(button);
        Assert.False(s.BrowserVideoMuted);
        Assert.Equal("🔊", glyph.Text);
    });

    [Fact]
    public void MuteScript_SetsTheFlag_AndHooksOnce()
    {
        var on = WebHostMedia.MuteScript(true);
        var off = WebHostMedia.MuteScript(false);
        Assert.EndsWith("(true);", on);
        Assert.EndsWith("(false);", off);
        Assert.Contains("__ccpMuteHooked", on);
        Assert.Contains("MutationObserver", on);
        Assert.Contains(".pause()", WebHostMedia.PauseScript);
    }

    [Fact]
    public void BambiCloudOverride_Saves_AndTheRuleIsModOrForced() => Run(view =>
    {
        var s = CoreSettings.Current;
        s.ForceShowBambiCloud = false;
        view.SyncBrowserCard();
        var box = view.FindControl<CheckBox>("ChkForceShowBambiCloud")!;
        Assert.False(box.IsChecked);
        box.IsChecked = true;
        Assert.True(s.ForceShowBambiCloud);
        Assert.True(view.FindControl<RadioButton>("RbBambiCloud")!.IsVisible);

        Assert.True(SettingsTabView.BambiCloudShown(modWants: true, forced: false));
        Assert.True(SettingsTabView.BambiCloudShown(modWants: false, forced: true));
        Assert.False(SettingsTabView.BambiCloudShown(modWants: false, forced: false));
    });

    [Fact]
    public void WebcamPill_FollowsTheTracker_AndTheHostlessControlsAreGreyed() => Run(view =>
    {
        var text = view.FindControl<TextBlock>("TxtWebcamTracking")!;
        view.WebcamRunningOverride = () => true;
        view.RefreshBrowserWebcamButton();
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("btn_browser_webcam_tracking_on"), text.Text);
        view.WebcamRunningOverride = () => false;
        view.RefreshBrowserWebcamButton();
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("btn_browser_webcam_tracking_off"), text.Text);

        Assert.False(view.FindControl<CheckBox>("ToggleEnhanceIfPossible")!.IsEnabled);
        Assert.False(view.FindControl<Button>("BtnPopOutBrowser")!.IsEnabled);
    });
}
