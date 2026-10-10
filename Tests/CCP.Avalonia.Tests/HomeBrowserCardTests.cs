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
        Assert.True(view.FindControl<Button>("BtnPopOutBrowser")!.IsEnabled);
    });

    [Fact]
    public void PopOut_OpensItsOwnWindow_FocusRelabels_AndClosingBringsThePageBack() => Run(view =>
    {
        var L = (Func<string, string>)ConditioningControlPanel.Localization.Loc.Get;
        CoreSettings.Current.OfflineMode = false;
        var button = view.FindControl<Button>("BtnPopOutBrowser")!;
        var label = Assert.IsType<TextBlock>(button.Content);
        var prompt = view.FindControl<TextBlock>("BrowserLoadingText")!;
        var card = view.FindControl<WebHost>("BrowserWebHost")!;
        view.SyncBrowserCard();
        Assert.Equal(L("btn_pop_out"), label.Text);
        Assert.Same(card, view.LiveBrowserHost);

        card.Navigate(new Uri("https://hypnotube.com/video/x"));
        Click(button);
        var window = view.BrowserPopoutWindow;
        Assert.NotNull(window);
        Assert.True(view.BrowserPoppedOut);
        Assert.Equal(1024, window!.Width);
        Assert.Equal(768, window.Height);
        Assert.Equal(L("title_browser_window"), window.Title);
        var popped = Assert.IsType<WebHost>(window.Content);
        Assert.Same(popped, view.LiveBrowserHost);                       // navigation, pause and mute follow it
        Assert.Equal(new Uri("https://hypnotube.com/video/x"), popped.Source);   // the page the card was on
        Assert.Equal(L("btn_focus"), label.Text);
        Assert.True(prompt.IsVisible);
        Assert.Equal(L("label_browser_popped_out_nclick_to_focus_window"), prompt.Text);
        Assert.False(card.IsVisible);

        Click(button);                                                    // a second click only focuses
        Assert.Same(window, view.BrowserPopoutWindow);

        popped.Navigate(new Uri("https://hypnotube.com/video/y"));
        int navs = card.NavigationRequests;
        window.Close();
        Assert.False(view.BrowserPoppedOut);
        Assert.Same(card, view.LiveBrowserHost);
        Assert.Equal(navs + 1, card.NavigationRequests);                  // the pop-out's page comes back
        Assert.Equal(new Uri("https://hypnotube.com/video/y"), card.Source);
        Assert.Equal(L("btn_pop_out"), label.Text);
        Assert.True(card.IsVisible);
    });

    [Fact]
    public void OfflineMode_RefusesPopOutAndTheLoadPrompt() => Run(view =>
    {
        CoreSettings.Current.OfflineMode = true;
        try
        {
            var card = view.FindControl<WebHost>("BrowserWebHost")!;
            Click(view.FindControl<Button>("BtnPopOutBrowser")!);
            Assert.False(view.BrowserPoppedOut);
            int navs = card.NavigationRequests;
            view.OnBrowserPromptClick();
            Assert.Equal(navs, card.NavigationRequests);
        }
        finally { CoreSettings.Current.OfflineMode = false; }
    });

    [Fact]
    public void LoadPrompt_StandsInUntilTheFirstPage_AndAClickLoadsTheSelectedSite() => Run(view =>
    {
        // The rule: popped out always; else only with an engine and nothing loaded (no engine = the host's own panel).
        Assert.True(SettingsTabView.BrowserPromptShown(poppedOut: false, hasEngine: true, loaded: false));
        Assert.False(SettingsTabView.BrowserPromptShown(poppedOut: false, hasEngine: true, loaded: true));
        Assert.False(SettingsTabView.BrowserPromptShown(poppedOut: false, hasEngine: false, loaded: false));
        Assert.True(SettingsTabView.BrowserPromptShown(poppedOut: true, hasEngine: false, loaded: true));

        CoreSettings.Current.OfflineMode = false;
        var card = view.FindControl<WebHost>("BrowserWebHost")!;
        view.FindControl<RadioButton>("RbHypnoTube")!.IsChecked = true;
        int navs = card.NavigationRequests;
        view.OnBrowserPromptClick();
        Assert.Equal(navs + 1, card.NavigationRequests);
        Assert.Equal(new Uri("https://hypnotube.com/"), card.Source);
        Assert.False(view.FindControl<TextBlock>("BrowserLoadingText")!.IsVisible);
        Assert.True(card.IsVisible);
    });
}
