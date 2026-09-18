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
    private const double TileArtHeight = 150;

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

        _statusTimer.Tick += (_, _) => RefreshStatus();
        _shortcutTextTimer.Tick += (_, _) =>
        {
            _shortcutTextTimer.Stop();
            ShortcutLink.Content = Loc.Get("launcher_add_shortcut");
        };

        IsVisibleChanged += OnIsVisibleChanged;
        Loaded += (_, _) => OnLoadedOnce();
        Closed += OnClosedCleanup;

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
            BuildTiles();
            RefreshAccount();
            RefreshStatus();
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
        _statusTimer.Stop();
        _shortcutTextTimer.Stop();
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

    // ------------------------------------------------------------------ the panel card

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
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] RefreshStatus failed"); }
    }

    private void RefreshAccount()
    {
        try
        {
            var name = App.Settings?.Current?.UserDisplayName;
            bool signedIn = App.IsLoggedIn && !string.IsNullOrWhiteSpace(name);
            AccountName.Text = signedIn ? name!.Trim() : "-";
            AvatarInitial.Text = signedIn ? name!.Trim()[..1].ToUpperInvariant() : "-";

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

    private Border CreateTile(LauncherEntry entry)
    {
        bool locked = entry.Locked;

        var tile = new Border
        {
            CornerRadius = new CornerRadius(TileRadius),
            Background = (Brush)FindResource("SurfaceBgBrush"),
            BorderBrush = (Brush)FindResource(locked ? "Tier2DiamondBorderBrush" : "PanelAccentBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(9),
            Tag = entry,
            Cursor = Cursors.Hand,
            // Scale for HoverLift, translate for StaggerIn: both helpers look inside a group.
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(1, 1), new TranslateTransform() },
            },
        };

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TileArtHeight) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // --- the art plate, rounded at the top only (the clip runs past the bottom edge) ---
        var plate = new Grid { ClipToBounds = true };
        plate.SizeChanged += (_, _) =>
            plate.Clip = new RectangleGeometry(new Rect(0, 0, plate.ActualWidth, plate.ActualHeight + TileRadius),
                                               TileRadius, TileRadius);
        ImageSource? art = null;
        if (entry.ArtPath != null)
        {
            try { art = ModResourceResolver.ResolveImageDecoded(entry.ArtPath, 640); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] art {Path} failed", entry.ArtPath); }
        }
        if (art != null)
        {
            var image = new Image { Source = art, Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            plate.Children.Add(image);
            FxDecorateArt(image);
        }
        else
        {
            var hue = entry.Hue;
            plate.Background = new RadialGradientBrush(hue, Darken(hue, 0.45))
            {
                GradientOrigin = new Point(0.5, 0.35), Center = new Point(0.5, 0.35),
                RadiusX = 0.8, RadiusY = 0.9,
            };
            plate.Children.Add(new TextBlock
            {
                Text = entry.Glyph, FontSize = 48, FontFamily = new FontFamily("/Fonts/#Fredoka, Segoe UI"),
                Foreground = Brushes.White, Opacity = 0.9,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
        }
        // A soft fade into the card so the plate never ends on a hard line.
        plate.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(Colors.Transparent, Color.FromArgb(0x66, 0, 0, 0), 90),
        });
        if (locked)
        {
            plate.Children.Add(new TextBlock
            {
                Text = "🔒", FontSize = 16, Margin = new Thickness(12, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            });
        }
        var shortcutBtn = new Button
        {
            Content = "🔗", Style = (Style)FindResource("LauncherIconButton"),
            ToolTip = Loc.Get("launcher_add_shortcut"), Opacity = 0,
            Margin = new Thickness(0, 8, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Background = (Brush)FindResource("SurfaceBgBrush"),
        };
        shortcutBtn.Click += (_, _) =>
        {
            LauncherSfx.Click();
            ShowShortcutResult(LauncherShortcuts.TryCreateDesktopShortcut(entry.Id));
        };
        plate.Children.Add(shortcutBtn);
        body.Children.Add(plate);

        // --- title, blurb, play ---
        var text = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };
        Grid.SetRow(text, 1);
        text.Children.Add(new TextBlock
        {
            Text = entry.Title, FontSize = 19, FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("/Fonts/#Fredoka, Segoe UI"),
            Foreground = (Brush)FindResource("TextLightBrush"), TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = entry.Blurb, FontSize = 14, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextSecondaryBrush"), MinHeight = 38,
        });
        var play = new Button
        {
            Content = Loc.Get(locked ? "launcher_locked" : "launcher_play"),
            Style = (Style)FindResource("OutlineButton"), Height = 44, Margin = new Thickness(0, 12, 0, 0),
            FontSize = 14,
        };
        play.PreviewMouseLeftButtonDown += Press_Down;
        play.PreviewMouseLeftButtonUp += Press_Up;
        play.Click += (_, _) =>
        {
            if (locked) LauncherSfx.Denied(); else LauncherSfx.Click();
            FxOnPlay(tile, entry);
            LauncherHost.LaunchGame(entry.Id);
        };
        text.Children.Add(play);
        body.Children.Add(text);
        tile.Child = body;

        tile.MouseEnter += (_, _) =>
        {
            MotionFx.HoverLift(tile, true);
            shortcutBtn.Opacity = 1;
            LauncherSfx.Hover();
            FxOnTileHover(tile, entry, true);
        };
        tile.MouseLeave += (_, _) =>
        {
            MotionFx.HoverLift(tile, false);
            shortcutBtn.Opacity = 0;
            FxOnTileHover(tile, entry, false);
        };
        FxDecorateTile(tile, entry);
        return tile;
    }

    private static Color Darken(Color c, double keep) =>
        Color.FromRgb((byte)(c.R * keep), (byte)(c.G * keep), (byte)(c.B * keep));

    // ------------------------------------------------------------------ bottom row

    private void PanelLink_Click(object sender, RoutedEventArgs e) =>
        OpenPanelTab((sender as FrameworkElement)?.Tag as string ?? "settings");

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
    partial void FxOnPanelLaunch();
    partial void FxOnPlay(Border tile, LauncherEntry entry);
    partial void FxOnTileHover(Border tile, LauncherEntry entry, bool on);
    partial void FxDecorateTile(Border tile, LauncherEntry entry);
    partial void FxDecorateArt(Image art);
}
