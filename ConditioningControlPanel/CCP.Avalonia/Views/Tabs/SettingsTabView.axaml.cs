using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Avalonia.Features;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Services.Theme;
using ConditioningControlPanel.Avalonia.Windows;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.LockCard;
using ConditioningControlPanel.Core.Services.MindWipe;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Core.Services.Video;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class SettingsTabView : UserControl
{
    private FeaturePopupWindow? _activePopup;
    private DispatcherTimer? _marqueeTimer;
    private double _marqueeSegmentWidth;
    private double _marqueeOffset;

    private DateTime _easterEggFirstClick = DateTime.MinValue;
    private int _easterEggClickCount;
    private bool _easterEggTriggered;

    public SettingsTabView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        AddHandler(FeatureCard.ToggleRequestedEvent, OnFeatureCardToggleRequested);
        AddHandler(FeatureCard.TestRequestedEvent, OnFeatureCardTestRequested);
        AddHandler(FeatureCard.HelpRequestedEvent, OnFeatureCardHelpRequested);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        LoadLogo();
        InitializeMarquee();
        AttachEmbeddedBrowser();
        WireBrowserFullscreenEvents();
        InitPremiumRail();

        var themeService = App.Services?.GetService<AvaloniaThemeService>();
        if (themeService != null)
            themeService.ThemeChanged += OnThemeChanged;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _marqueeTimer?.Stop();
        _marqueeTimer = null;

        ShutdownPremiumRail();

        UnwireBrowserFullscreenEvents();
        ExitBrowserFullscreen();

        var themeService = App.Services?.GetService<AvaloniaThemeService>();
        if (themeService != null)
            themeService.ThemeChanged -= OnThemeChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnwireBrowserFullscreenEvents();
        AttachEmbeddedBrowser();
        WireBrowserFullscreenEvents();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        RefreshThemedImages();
    }

    private void RefreshThemedImages()
    {
        LoadLogo();

        foreach (var card in this.GetVisualDescendants().OfType<FeatureCard>())
        {
            var uri = card.IconUri;
            if (!string.IsNullOrWhiteSpace(uri))
                card.Icon = AvaloniaBitmapHelper.Load(uri);
        }
    }

    private void InitializeMarquee()
    {
        if (MarqueeText1 == null || MarqueeContent == null) return;

        // Wait for the first layout pass so we know the width of one text segment.
        MarqueeText1.LayoutUpdated += OnMarqueeLayoutUpdated;
    }

    private void OnMarqueeLayoutUpdated(object? sender, EventArgs e)
    {
        if (MarqueeText1 == null) return;
        MarqueeText1.LayoutUpdated -= OnMarqueeLayoutUpdated;

        _marqueeSegmentWidth = MarqueeText1.Bounds.Width;
        if (_marqueeSegmentWidth <= 0 || MarqueeCanvas == null) return;

        Canvas.SetLeft(MarqueeContent, 0);
        _marqueeOffset = 0;

        _marqueeTimer?.Stop();
        _marqueeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnMarqueeTick);
        _marqueeTimer.Start();
    }

    private void OnMarqueeTick(object? sender, EventArgs e)
    {
        if (MarqueeContent == null) return;

        _marqueeOffset += 1.5;
        if (_marqueeOffset >= _marqueeSegmentWidth)
            _marqueeOffset = 0;

        Canvas.SetLeft(MarqueeContent, -_marqueeOffset);
    }

    private void LoadLogo()
    {
        try
        {
            var settings = App.Services?.GetService<ISettingsService>()?.Current;
            var useNeutral = settings?.ActiveModId == BuiltInMods.CCPDefaultId || settings?.IsSissyMode == true;
            var logoFile = useNeutral ? "logo2.png" : "logo.png";
            var bitmap = AvaloniaBitmapHelper.LoadResource(logoFile);
            if (bitmap != null)
                ImgLogo.Source = bitmap;
        }
        catch (Exception ex)
        {
            App.Services?.GetRequiredService<ILogger<SettingsTabView>>().LogWarning(ex, "SettingsTabView: failed to load logo");
        }
    }

    #region Dashboard feature cards

    private void CardFlash_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeaturePopup(new FlashFeatureControl(), LocalizationManager.Instance.Get("section_flash_images"), sender);
    }

    private void CardVisuals_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeaturePopup(new VisualsFeatureControl(), LocalizationManager.Instance.Get("section_visuals"), sender, glyph: "👁");
    }

    private void CardVideo_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeaturePopup(new VideoFeatureControl(), LocalizationManager.Instance.Get("section_mandatory_video"), sender);
    }

    private void CardSubliminal_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeaturePopup(new SubliminalFeatureControl(), LocalizationManager.Instance.Get("section_subliminals_2"), sender);
    }

    private void CardSpiral_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeaturePopup(new SpiralFeatureControl(), LocalizationManager.Instance.Get("label_spiral_overlay"), sender);
    }

    private void CardLockCard_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeaturePopup(new LockCardFeatureControl(), LocalizationManager.Instance.Get("label_lock_card"), sender);
    }

    private void CardPinkFilter_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeaturePopup(new PinkFilterFeatureControl(), LocalizationManager.Instance.Get("label_pink_filter"), sender);
    }

    private void CardMindWipe_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeaturePopup(new MindWipeFeatureControl(), LocalizationManager.Instance.Get("label_mind_wipe"), sender);
    }

    private void CardBubblePop_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeaturePopup(new BubblePopFeatureControl(), LocalizationManager.Instance.Get("label_bubble_pop"), sender);
    }

    private void CardBouncingText_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeaturePopup(new BouncingTextFeatureControl(), LocalizationManager.Instance.Get("label_bouncing_text"), sender);
    }

    private void CardSystem_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeaturePopup(new SystemFeatureControl(), LocalizationManager.Instance.Get("section_system"), sender, glyph: "⚙");
    }

    private void CardBubbleCount_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeaturePopup(new BubbleCountFeatureControl(), LocalizationManager.Instance.Get("label_bubble_count"), sender);
    }

    private void ShowFeaturePopup(Control content, string title, object? cardSource, string? glyph = null)
    {
        // Close any existing popup before opening a new one, mirroring the WPF behavior.
        _activePopup?.Close();

        var card = cardSource as FeatureCard;
        var owner = TopLevel.GetTopLevel(this) as Window;
        var popup = new FeaturePopupWindow(content, title, card?.Icon, glyph, owner)
        {
            ShowInTaskbar = false
        };

        popup.Closed += (_, __) =>
        {
            if (_activePopup == popup)
                _activePopup = null;

            try
            {
                if (TopLevel.GetTopLevel(this) is Window w && w.WindowState == WindowState.Minimized)
                    w.WindowState = WindowState.Normal;
                (TopLevel.GetTopLevel(this) as Window)?.Activate();
            }
            catch
            {
                // window may be shutting down
            }
        };

        _activePopup = popup;
        popup.Show();
    }

    #endregion

    private void ImgLogo_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            return;

        App.Services?.GetService<IAchievementService>()?.TrackAvatarClick();
        try { App.Services?.GetService<IBarkService>()?.NotifyAvatarClicked(); } catch { }

        var logger = App.Services?.GetRequiredService<ILogger<SettingsTabView>>();
        try
        {
            var achievements = App.Services?.GetService<IAchievementService>();
            var clickCount = achievements?.Progress.AvatarClickCount ?? 0;
            logger?.LogDebug("Logo clicked! Count: {Count}/20", clickCount);
        }
        catch { }

        if (!_easterEggTriggered)
        {
            var now = DateTime.Now;
            if (_easterEggFirstClick == DateTime.MinValue || (now - _easterEggFirstClick).TotalSeconds > 60)
            {
                _easterEggFirstClick = now;
                _easterEggClickCount = 1;
            }
            else
            {
                _easterEggClickCount++;
                if (_easterEggClickCount >= 100)
                {
                    _easterEggTriggered = true;
                    _ = ShowEasterEggAsync();
                }
            }
        }

        if (ImgLogo != null)
        {
            var scaleTransform = ImgLogo.RenderTransform as ScaleTransform;
            if (scaleTransform == null)
            {
                scaleTransform = new ScaleTransform(1, 1);
                ImgLogo.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
                ImgLogo.RenderTransform = scaleTransform;
            }

            // Manual 160 ms pulse: Avalonia's TransformAnimator expects a Visual target,
            // so we animate the transform values directly instead of using Animation.RunAsync.
            var start = DateTime.UtcNow;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                var elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
                var t = Math.Min(1.0, elapsed / 160.0);
                var s = 1.0 + 0.05 * Math.Sin(t * Math.PI);
                scaleTransform.ScaleX = s;
                scaleTransform.ScaleY = s;
                if (t >= 1.0)
                {
                    scaleTransform.ScaleX = 1.0;
                    scaleTransform.ScaleY = 1.0;
                    timer.Stop();
                }
            };
            timer.Start();
        }
    }

    private async Task ShowEasterEggAsync()
    {
        // ProfileSync slice 7: record the read server-side and show the live reader count
        // (WPF SettingsTab easter egg parity). -1 = unavailable (offline / not logged in).
        int readerCount = -1;
        try
        {
            var profileSync = App.Services?.GetService<Core.Services.Settings.IProfileSyncService>();
            if (profileSync != null)
                readerCount = await profileSync.RecordEasterEggReadAsync();
        }
        catch
        {
            // Best-effort: the easter egg must open even when the counter call fails.
        }

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                var window = new EasterEggWindow(readerCount);
                if (owner != null)
                    await window.ShowDialog(owner);
                else
                    window.Show();
            }
            catch (Exception ex)
            {
                App.Services?.GetRequiredService<ILogger<SettingsTabView>>().LogWarning(ex, "Failed to show easter egg window");
            }
        });
    }

    private void OnFeatureCardToggleRequested(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not FeatureCard card) return;

        var settingsService = App.Services?.GetService<ISettingsService>();
        var settings = settingsService?.Current;
        if (settings == null) return;

        var session = App.Services?.GetService<ISessionService>();
        var running = session?.State == SessionState.Running;

        try
        {
            switch (card.Title)
            {
                case var t when t == LocalizationManager.Instance.Get("feature_title_flash_images"):
                    settings.FlashEnabled = !settings.FlashEnabled;
                    if (running)
                    {
                        if (settings.FlashEnabled) App.Services?.GetService<IFlashService>()?.Start();
                        else App.Services?.GetService<IFlashService>()?.Stop();
                    }
                    break;
                case var t when t == LocalizationManager.Instance.Get("feature_title_mandatory_video"):
                    settings.MandatoryVideosEnabled = !settings.MandatoryVideosEnabled;
                    if (running)
                    {
                        if (settings.MandatoryVideosEnabled) App.Services?.GetService<IVideoService>()?.Start();
                        else App.Services?.GetService<IVideoService>()?.Stop();
                    }
                    break;
                case var t when t == LocalizationManager.Instance.Get("feature_title_subliminals"):
                    // Single authority: persists the flag and live-applies start/stop (idempotently).
                    App.Services?.GetService<ISubliminalService>()?.SetEnabled(!settings.SubliminalEnabled);
                    break;
                case var t when t == LocalizationManager.Instance.Get("feature_title_spiral_overlay"):
                    settings.SpiralEnabled = !settings.SpiralEnabled;
                    App.Services?.GetService<IOverlayService>()?.RefreshOverlays();
                    break;
                case var t when t == LocalizationManager.Instance.Get("feature_title_lock_card"):
                    settings.LockCardEnabled = !settings.LockCardEnabled;
                    if (running)
                    {
                        if (settings.LockCardEnabled) App.Services?.GetService<ILockCardService>()?.Start();
                        else App.Services?.GetService<ILockCardService>()?.Stop();
                    }
                    break;
                case var t when t == LocalizationManager.Instance.Get("feature_title_pink_filter"):
                    settings.PinkFilterEnabled = !settings.PinkFilterEnabled;
                    App.Services?.GetService<IOverlayService>()?.RefreshOverlays();
                    break;
                case var t when t == LocalizationManager.Instance.Get("feature_title_mind_wipe"):
                    settings.MindWipeEnabled = !settings.MindWipeEnabled;
                    if (running)
                    {
                        if (settings.MindWipeEnabled) App.Services?.GetService<IMindWipeService>()?.Start(settings.MindWipeFrequency, settings.MindWipeVolume / 100.0);
                        else App.Services?.GetService<IMindWipeService>()?.Stop();
                    }
                    break;
                case var t when t == LocalizationManager.Instance.Get("feature_title_bubble_pop"):
                    settings.BubblesEnabled = !settings.BubblesEnabled;
                    if (running)
                    {
                        if (settings.BubblesEnabled) App.Services?.GetService<IBubbleService>()?.Start();
                        else App.Services?.GetService<IBubbleService>()?.Stop();
                    }
                    break;
                case var t when t == LocalizationManager.Instance.Get("feature_title_bouncing_text"):
                    settings.BouncingTextEnabled = !settings.BouncingTextEnabled;
                    if (running)
                    {
                        if (settings.BouncingTextEnabled) App.Services?.GetService<IBouncingTextService>()?.Start();
                        else App.Services?.GetService<IBouncingTextService>()?.Stop();
                    }
                    break;
                case var t when t == LocalizationManager.Instance.Get("feature_title_bubble_count"):
                    settings.BubbleCountEnabled = !settings.BubbleCountEnabled;
                    if (running)
                    {
                        if (settings.BubbleCountEnabled) App.Services?.GetService<IBubbleCountService>()?.Start();
                        else App.Services?.GetService<IBubbleCountService>()?.Stop();
                    }
                    break;
                default:
                    return; // Visuals / System cards have no single on/off toggle.
            }

            settingsService.Save();
        }
        catch (Exception ex)
        {
            App.Services?.GetRequiredService<ILogger<SettingsTabView>>().LogWarning(ex, "Feature card quick-toggle failed for {Card}", card.Title);
        }
    }

    private void OnFeatureCardHelpRequested(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not FeatureCard card) return;
        if (string.IsNullOrWhiteSpace(card.HelpSectionId)) return;

        if (DataContext is ViewModels.Tabs.SettingsTabViewModel vm)
            vm.ShowHelpSectionCommand.Execute(card.HelpSectionId);

        e.Handled = true;
    }

    private async void OnFeatureCardTestRequested(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not FeatureCard card) return;
        if (card.Title != LocalizationManager.Instance.Get("feature_title_mandatory_video")) return;

        try
        {
            var video = App.Services?.GetService<IVideoService>();
            var interactionQueue = App.Services?.GetService<IInteractionQueueService>();
            var dialogService = App.Services?.GetRequiredService<IDialogService>();
            var logger = App.Services?.GetRequiredService<ILogger<SettingsTabView>>();

            if (video?.IsPlaying == true)
            {
                var proceed = await dialogService.ShowConfirmationAsync(
                    LocalizationManager.Instance.Get("title_confirm"),
                    LocalizationManager.Instance.Get("msg_video_test_already_playing"));
                if (!proceed) return;

                logger.LogWarning("Dashboard test video: force reset requested");
                video.ForceCleanup();
                interactionQueue?.ForceReset();
            }

            if (interactionQueue is { IsBusy: true })
            {
                var proceed = await dialogService.ShowConfirmationAsync(
                    LocalizationManager.Instance.Get("title_confirm"),
                    LocalizationManager.Instance.Get("msg_video_test_queue_busy"));
                if (!proceed) return;

                video?.ForceCleanup();
                interactionQueue.ForceReset();
            }

            video?.TriggerVideo();
        }
        catch (Exception ex)
        {
            App.Services?.GetRequiredService<ILogger<SettingsTabView>>().LogError(ex, "Dashboard test video failed");
        }
    }

    private void BrowserLoadingText_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ViewModels.Tabs.SettingsTabViewModel vm)
            return;

        // The embedded control is attached automatically on Windows. On platforms where
        // embedding is unavailable, the placeholder remains and clicking it triggers navigation
        // through the platform fallback (system browser or popup window).
        vm.InitializeBrowserCommand.Execute(null);
    }

    #region Embedded browser reparenting

    private Control? _browserControl;
    private Window? _browserFullscreenWindow;
    private bool _browserFullscreenEventsWired;

    /// <summary>
    /// Requests an embedded browser control from the view-model and places it inside the
    /// dashboard's <see cref="BrowserContainer"/>. No-op if the platform host does not
    /// support visual embedding (e.g. Linux/macOS fallback) or the control is already attached.
    /// </summary>
    private void AttachEmbeddedBrowser()
    {
        if (_browserControl != null)
            return;

        if (DataContext is not ViewModels.Tabs.SettingsTabViewModel vm)
            return;

        if (vm.BrowserHost?.CreateBrowserControl() is not Control control)
            return;

        _browserControl = control;
        BrowserContainer.Child = control;
    }

    private void WireBrowserFullscreenEvents()
    {
        if (_browserFullscreenEventsWired)
            return;
        if (DataContext is not ViewModels.Tabs.SettingsTabViewModel vm)
            return;
        if (vm.BrowserHost == null)
            return;

        vm.BrowserHost.FullscreenChanged += OnBrowserFullscreenChanged;
        _browserFullscreenEventsWired = true;
    }

    private void UnwireBrowserFullscreenEvents()
    {
        if (!_browserFullscreenEventsWired)
            return;
        if (DataContext is not ViewModels.Tabs.SettingsTabViewModel vm)
            return;
        if (vm.BrowserHost == null)
            return;

        vm.BrowserHost.FullscreenChanged -= OnBrowserFullscreenChanged;
        _browserFullscreenEventsWired = false;
    }

    private void OnBrowserFullscreenChanged(object? sender, bool fullscreen)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (fullscreen)
                EnterBrowserFullscreen();
            else
                ExitBrowserFullscreen();
        });
    }

    /// <summary>
    /// Reparents the embedded browser control into a fullscreen Avalonia window when the
    /// hosted page requests HTML5 fullscreen (e.g. a video fullscreen button).
    /// </summary>
    private void EnterBrowserFullscreen()
    {
        if (_browserControl == null || _browserFullscreenWindow != null)
            return;

        var window = new Window
        {
            WindowDecorations = WindowDecorations.None,
            WindowState = WindowState.FullScreen,
            Background = new SolidColorBrush(Colors.Black),
            Topmost = true,
            ShowInTaskbar = false,
            CanResize = false,
            Content = new Panel { Background = new SolidColorBrush(Colors.Black) }
        };

        var container = (Panel)window.Content;
        BrowserContainer.Child = null;
        container.Children.Add(_browserControl);

        window.Closed += OnBrowserFullscreenWindowClosed;
        _browserFullscreenWindow = window;
        window.Show();
    }

    /// <summary>
    /// Restores the embedded browser control from the fullscreen window back into the
    /// dashboard's <see cref="BrowserContainer"/>.
    /// </summary>
    private void ExitBrowserFullscreen()
    {
        var window = _browserFullscreenWindow;
        if (window == null)
            return;

        _browserFullscreenWindow = null;
        window.Closed -= OnBrowserFullscreenWindowClosed;

        if (window.Content is Panel container && _browserControl != null)
        {
            container.Children.Remove(_browserControl);
            BrowserContainer.Child = _browserControl;
        }

        try { window.Close(); }
        catch { /* window may already be closing */ }
    }

    /// <summary>
    /// Handles the fullscreen window being closed directly (e.g. Alt+F4) by returning the
    /// browser control to the dashboard and asking the host to leave fullscreen.
    /// </summary>
    private void OnBrowserFullscreenWindowClosed(object? sender, EventArgs e)
    {
        if (_browserFullscreenWindow == null)
            return; // Already handled by ExitBrowserFullscreen.

        _browserFullscreenWindow = null;

        if (sender is Window window && window.Content is Panel container && _browserControl != null)
        {
            container.Children.Remove(_browserControl);
            BrowserContainer.Child = _browserControl;
        }

        // If the browser still believes it is fullscreen, request exit via script.
        if (DataContext is ViewModels.Tabs.SettingsTabViewModel vm && vm.BrowserHost is { IsFullscreen: true })
        {
            _ = vm.BrowserHost.ExecuteScriptAsync("document.exitFullscreen?.().catch(()=>{});");
        }
    }

    #endregion

    private void VelvetBtnWebcam_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeaturePopup(new WebcamFeatureControl(), LocalizationManager.Instance.Get("blink_trainer_section_webcam"), sender, glyph: "📷");
    }

    private void BrowserWebcamTracking_Click(object? sender, RoutedEventArgs e)
    {
        // Browser toolbar webcam entry point mirrors the dashboard helper button:
        // open the webcam feature popup for consent/calibration/tracking.
        ShowFeaturePopup(new WebcamFeatureControl(), LocalizationManager.Instance.Get("blink_trainer_section_webcam"), sender, glyph: "📷");
    }

    private void VelvetBtnAppInfo_Click(object? sender, RoutedEventArgs e)
    {
        var vm = App.Services?.GetService<ViewModels.Tabs.AppInfoTabViewModel>();
        var view = new Views.Tabs.AppInfoTabView { DataContext = vm };
        ShowFeaturePopup(view, LocalizationManager.Instance.Get("label_app_info"), sender, glyph: "ℹ️");
    }

    private void VelvetBtnSchedulerRamp_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeaturePopup(new SchedulerRampFeatureControl(), LocalizationManager.Instance.Get("btn_scheduler_intensity_ramp"), sender, glyph: "📅");
    }

    private void VelvetBtnCatalogue_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://ccppanel.com/catalogue",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            var logger = App.Services?.GetRequiredService<ILogger<SettingsTabView>>();
            logger?.LogWarning(ex, "Failed to open CCP catalogue link");
        }
    }
}
