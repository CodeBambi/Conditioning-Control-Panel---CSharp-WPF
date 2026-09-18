using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using Serilog;

namespace ConditioningControlPanel.Launcher;

/// <summary>
/// The CC Labs launcher: the panel card on the left, the game tiles on the right, links along the
/// bottom. Every transition (open the panel, start a game, close) goes through
/// <see cref="LauncherHost"/>; this window only draws and asks. The tiles are built in code from
/// <see cref="LauncherCatalogue.Games"/>, so a new game is a catalogue row and nothing here.
///
/// <para>The decoration (particles, comets, sheen, the mascot) lives in the Fx partial and is
/// gated there. Everything in this file must read and work with all of it switched off.</para>
/// </summary>
public partial class LauncherWindow : Window
{
    private const double TileRadius = 16;

    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _shortcutTextTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly List<Border> _tiles = new();
    private readonly Dictionary<string, Border> _tileById = new(StringComparer.OrdinalIgnoreCase);
    private MainWindow? _engineSource;
    private bool _firstShow = true;

    public LauncherWindow()
    {
        InitializeComponent();

        try { DataContext = App.Settings?.Current; } catch { }

        _statusTimer.Tick += (_, _) => { RefreshStatus(); RefreshStats(); };
        _shortcutTextTimer.Tick += (_, _) =>
        {
            _shortcutTextTimer.Stop();
            ShortcutLink.Content = Loc.Get("launcher_add_shortcut");
        };

        IsVisibleChanged += OnIsVisibleChanged;
        Loaded += (_, _) => OnLoadedOnce();
        Closed += OnClosedCleanup;
        LauncherHost.FadeOut = FadeOutThen;
        LauncherHost.RequestSignIn = OpenSignIn;

        var lockdown = App.Lockdown;
        if (lockdown != null)
        {
            lockdown.LockdownActivated += OnLockdownChanged;
            lockdown.LockdownDeactivated += OnLockdownChanged;
        }
    }

    // ------------------------------------------------------------------ lifecycle

    private void OnLoadedOnce()
    {
        try
        {
            PanelEyebrow.Text = PanelEyebrow.Text.ToUpperInvariant();
            GamesEyebrow.Text = GamesEyebrow.Text.ToUpperInvariant();
        }
        catch { }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible) OnShown();
        else OnHidden();
    }

    private void OnShown()
    {
        try
        {
            FadeIn();
            BuildTiles();
            RefreshAccount();
            RefreshStatus();
            RefreshStats();
            RefreshLockdownVeil();
            HookEngine();
            _statusTimer.Start();

            if (MotionFx.AllowTransitions)
                foreach (var t in _tiles) t.Opacity = 0;
            MotionFx.StaggerIn(_tiles);

            LauncherSfx.Open();
            FxOnShown(_firstShow);
            _firstShow = false;
        }
        catch (Exception ex) { Log.Warning(ex, "[Launcher] OnShown failed"); }
    }

    private void OnHidden()
    {
        _statusTimer.Stop();
        RestoreRootOpacity();
        try { FxOnHidden(); } catch (Exception ex) { Log.Debug(ex, "[Launcher] FxOnHidden threw"); }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing is never the window's own decision. The host hides when something is still
        // running behind the launcher and exits the process when nothing is; an Application
        // shutdown ignores the cancel, which is exactly the one case the window must not veto.
        // Posted rather than called: the exit path closes every window, including this one,
        // and it must not find the launcher still inside its own Closing.
        e.Cancel = true;
        base.OnClosing(e);
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, LauncherHost.RequestClose);
    }

    private void OnClosedCleanup(object? sender, EventArgs e)
    {
        LauncherHost.FadeOut = null;
        _fadeGuard?.Stop();
        _statusTimer.Stop();
        _shortcutTextTimer.Stop();
        LauncherHost.RequestSignIn = null;
        UnhookEngine();
        var lockdown = App.Lockdown;
        if (lockdown != null)
        {
            lockdown.LockdownActivated -= OnLockdownChanged;
            lockdown.LockdownDeactivated -= OnLockdownChanged;
        }
        try { FxOnClosed(); } catch (Exception ex) { Log.Debug(ex, "[Launcher] FxOnClosed threw"); }
    }

    // ------------------------------------------------------------------ title bar

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }
            if (WindowState == WindowState.Maximized)
            {
                var point = PointToScreen(e.GetPosition(this));
                WindowState = WindowState.Normal;
                Left = point.X - Width / 2;
                Top = point.Y - 15;
            }
            DragMove();
        }
        catch (InvalidOperationException) { /* DragMove after the button went up; nothing to do */ }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        LauncherSfx.Click();
        WindowState = WindowState.Minimized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        LauncherSfx.Click();
        LauncherHost.RequestClose();
    }

    private void BtnGear_Click(object sender, RoutedEventArgs e) => OpenPanelTab("appsettings");

    private void AccountChip_Click(object sender, RoutedEventArgs e) => OpenPanelTab("appsettings");

    private void SignIn_Click(object sender, RoutedEventArgs e)
    {
        LauncherSfx.Click();
        OpenSignIn();
    }

    // ------------------------------------------------------------------ sign in

    private bool _signingIn;

    /// <summary>
    /// The one sign-in flow, the panel's own (<see cref="MainWindow.OpenUnifiedLoginDialog"/>),
    /// with the dialog centred over the launcher and the panel left hidden. Whatever the dialog
    /// returns, the chip, the stats and the tiles are read again so the locks and the name follow
    /// the account. Reached from the title bar pill, a signed-out tile and
    /// <see cref="LauncherHost.RequestSignIn"/>.
    /// </summary>
    internal void OpenSignIn()
    {
        if (_signingIn) return;
        var mw = App.MainWindowRef;
        if (mw == null) { Log.Warning("[Launcher] sign-in with no main window"); return; }
        _signingIn = true;
        try { mw.OpenUnifiedLoginDialog(this); }
        catch (Exception ex) { Log.Warning(ex, "[Launcher] sign-in dialog failed"); }
        finally { _signingIn = false; }

        RefreshAccount();
        RefreshStats();
        BuildTiles();
        if (MotionFx.AllowTransitions)
            foreach (var t in _tiles) t.Opacity = 0;
        MotionFx.StaggerIn(_tiles);
    }

    // ------------------------------------------------------------------ the panel card

    /// <summary>
    /// The hero plate keeps the card's top radius and runs its clip past the bottom edge so the
    /// fade, not a corner, is what ends it. Same recipe as the tile plates.
    /// </summary>
    private void PanelArtPlate_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        try
        {
            const double radius = 20;
            PanelArtPlate.Clip = new RectangleGeometry(
                new Rect(0, 0, PanelArtPlate.ActualWidth, PanelArtPlate.ActualHeight + radius), radius, radius);
            if (PanelArtFade.Background == null)
            {
                var surface = (FindResource("SurfaceBgBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(0x17, 0x12, 0x2A);
                var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, surface.R, surface.G, surface.B), 0.0));
                fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x40, surface.R, surface.G, surface.B), 0.55));
                fade.GradientStops.Add(new GradientStop(surface, 1.0));
                PanelArtFade.Background = fade;
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] panel art plate clip failed"); }
    }

    private void HookEngine()
    {
        var mw = App.MainWindowRef;
        if (mw == null || ReferenceEquals(mw, _engineSource)) return;
        UnhookEngine();
        _engineSource = mw;
        mw.EngineStopped += OnEngineStopped;
    }

    private void UnhookEngine()
    {
        if (_engineSource != null) _engineSource.EngineStopped -= OnEngineStopped;
        _engineSource = null;
    }

    private void OnEngineStopped(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) RefreshStatus();
        else Dispatcher.BeginInvoke(RefreshStatus);
    }

    private void RefreshStatus()
    {
        try
        {
            bool running = App.IsEngineRunning;
            if (running)
            {
                var since = App.MainWindowRef?.EngineStartedUtc;
                var stamp = (since ?? DateTime.UtcNow).ToLocalTime().ToString("HH:mm");
                StatusText.Text = Loc.GetF("launcher_panel_running", stamp);
                StopLink.Visibility = Visibility.Visible;
                PanelCta.Content = Loc.Get("launcher_panel_open");
            }
            else
            {
                StatusText.Text = Loc.Get("launcher_panel_idle");
                StopLink.Visibility = Visibility.Collapsed;
                PanelCta.Content = Loc.Get("launcher_panel_launch");
            }
            FxOnEngineState(running);
            FxOnStatusTick();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] RefreshStatus failed"); }
    }

    private void RefreshAccount()
    {
        try
        {
            var name = App.Settings?.Current?.UserDisplayName;
            bool loggedIn = !LauncherCatalogue.NeedsAccount;
            bool signedIn = loggedIn && !string.IsNullOrWhiteSpace(name);
            AccountName.Text = signedIn ? name!.Trim() : "-";
            AvatarInitial.Text = signedIn ? name!.Trim()[..1].ToUpperInvariant() : "-";
            AccountChipButton.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
            SignInPill.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;

            var tier = App.Patreon?.CurrentTier ?? PatreonTier.None;
            string? badge = tier switch
            {
                PatreonTier.Level1 => "features/tier_badge_t1.png",
                PatreonTier.Level2 => "features/tier_badge_t2.png",
                _ => null,
            };
            TierBadge.Source = badge == null ? null : ModResourceResolver.ResolveImageDecoded(badge, 64);
            TierBadge.Visibility = TierBadge.Source == null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] RefreshAccount failed"); }
    }

    private void StopLink_Click(object sender, RoutedEventArgs e)
    {
        LauncherSfx.Click();
        try { App.MainWindowRef?.StopEngine(); }
        catch (Exception ex) { Log.Warning(ex, "[Launcher] StopEngine failed"); }
        RefreshStatus();
    }

    private void PanelCta_Click(object sender, RoutedEventArgs e)
    {
        LauncherSfx.Click();
        FxOnPanelLaunch();
        LauncherHost.OpenPanel();
    }

    private void ShortcutLink_Click(object sender, RoutedEventArgs e)
    {
        LauncherSfx.Click();
        ShowShortcutResult(LauncherShortcuts.TryCreateDesktopShortcut(null));
    }

    private void ShowShortcutResult(bool ok)
    {
        ShortcutLink.Content = Loc.Get(ok ? "launcher_shortcut_added" : "launcher_shortcut_failed");
        _shortcutTextTimer.Stop();
        _shortcutTextTimer.Start();
    }

    private void Press_Down(object sender, MouseButtonEventArgs e) => MotionFx.PressSquish(sender as FrameworkElement, true);
    private void Press_Up(object sender, MouseButtonEventArgs e) => MotionFx.PressSquish(sender as FrameworkElement, false);

    // ------------------------------------------------------------------ the tiles

    private void BuildTiles()
    {
        GamesGrid.Children.Clear();
        _tiles.Clear();
        _tileById.Clear();
        foreach (var entry in LauncherCatalogue.Games.Where(g => g.Available))
        {
            try
            {
                var tile = CreateTile(entry);
                _tiles.Add(tile);
                _tileById[entry.Id] = tile;
                GamesGrid.Children.Add(tile);
            }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] tile {Id} failed to build", entry.Id); }
        }
    }

    // ------------------------------------------------------------------ the stats strip

    private double _shownLevel, _shownSparkles, _shownXpWidth = -1;

    /// <summary>
    /// Three live numbers on the panel card: level, Sparkle Points and time under, plus a slim
    /// XP bar. Read from the same settings the panel's own chips read (PlayerLevel, PlayerXP,
    /// SkillPoints, TotalConditioningMinutes) and the progression curve for the level's cap.
    /// The numbers odometer from the last value shown, so the first show counts up from zero
    /// and a tick while the engine runs nudges rather than snaps.
    /// </summary>
    private void RefreshStats()
    {
        try
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            int level = Math.Max(1, s.PlayerLevel);
            double xp = Math.Max(0, s.PlayerXP);
            double need = 0;
            try { need = App.Progression?.GetXPForLevel(level) ?? 0; } catch { }
            int sparkles = Math.Max(0, s.SkillPoints);
            double minutes = Math.Max(0, s.TotalConditioningMinutes);

            MotionFx.Odometer(StatLevel, _shownLevel, level, "{0:0}", _shownLevel == 0 ? 0.9 : 0.5);
            MotionFx.Odometer(StatSparkles, _shownSparkles, sparkles, "{0:N0}", _shownSparkles == 0 ? 1.1 : 0.5);
            _shownLevel = level;
            _shownSparkles = sparkles;

            int hours = (int)(minutes / 60);
            int mins = (int)(minutes % 60);
            StatTime.Text = hours > 0 ? $"{hours}h {mins:00}m" : $"{mins}m";

            double ratio = need > 0 ? Math.Clamp(xp / need, 0, 1) : 0;
            double track = XpTrack.ActualWidth;
            if (track > 0)
            {
                double width = track * ratio;
                if (Math.Abs(width - _shownXpWidth) >= 0.5)
                {
                    MotionFx.BarFill(XpFill, _shownXpWidth < 0 ? 0 : _shownXpWidth, width, null, _shownXpWidth < 0 ? 1.0 : 0.5);
                    _shownXpWidth = width;
                }
            }
            XpCaption.Text = Loc.GetF("launcher_stat_xp", ((int)xp).ToString("N0"), ((int)need).ToString("N0"));
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] RefreshStats failed"); }
    }

    // ------------------------------------------------------------------ bottom row

    private void OpenPanelTab(string tab)
    {
        LauncherSfx.Click();
        LauncherHost.OpenPanel();
        try { App.MainWindowRef?.ShowTab(tab); }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] ShowTab {Tab} failed", tab); }
    }

    private void WhatsNew_Click(object sender, RoutedEventArgs e)
    {
        LauncherSfx.Click();
        // MainWindow only shows the dialog from its startup ladder; the dialog itself is public,
        // so the launcher opens the same notes over itself instead of waking the panel for them.
        try
        {
            var dlg = new WhatsNewDialog("What's New in v" + UpdateService.AppVersion,
                                         UpdateService.CurrentPatchNotes) { Owner = this };
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Launcher] What's New dialog failed; falling back to the settings tab");
            OpenPanelTab("settings");
        }
    }

    private void SkipToPanel_Click(object sender, RoutedEventArgs e)
    {
        LauncherSfx.Click();
        try { App.Settings?.Save(); }
        catch (Exception ex) { Log.Warning(ex, "[Launcher] saving LauncherSkipToPanel failed"); }
    }

    // ------------------------------------------------------------------ lockdown

    private void OnLockdownChanged()
    {
        if (Dispatcher.CheckAccess()) RefreshLockdownVeil();
        else Dispatcher.BeginInvoke(RefreshLockdownVeil);
    }

    private void RefreshLockdownVeil()
    {
        try
        {
            LockdownVeil.Visibility = App.Lockdown?.IsActive == true ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] veil refresh failed"); }
    }

    // ------------------------------------------------------------------ Fx seams
    // Implemented in LauncherWindow.Fx.cs. Partial methods with no body compile to nothing, so
    // the layout stands on its own with the juice file absent.

    partial void FxOnShown(bool firstShow);
    partial void FxOnHidden();
    partial void FxOnClosed();
    partial void FxOnEngineState(bool running);
    partial void FxOnStatusTick();
    partial void FxOnPanelLaunch();
    partial void FxOnPlay(Border tile, LauncherEntry entry);
    partial void FxOnTileHover(Border tile, LauncherEntry entry, bool on);
    partial void FxDecorateTile(Border tile, LauncherEntry entry);
    partial void FxDecorateArt(Image art);
}
