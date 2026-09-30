// PORTED (slice 1) from WPF LauncherWindow.xaml.cs/.Tiles.cs and LauncherHost.cs; rules are Core's
// LauncherCards/LauncherRules. ponytail: games, boot surface, account row, FX = later slices (launcher-plan.md).
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Launcher;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class LauncherWindow : Window
    {
        private const double TileRadius = 16;

        /// <summary>Cards whose destination exists on this head: padlock probe + how to open it.</summary>
        internal static readonly IReadOnlyDictionary<string, (Func<bool> Locked, Action<MainShellWindow> Open)> Destinations =
            new Dictionary<string, (Func<bool>, Action<MainShellWindow>)>(StringComparer.OrdinalIgnoreCase)
            {
                // Graded Intake (WPF IntakePassService.CanStartIntake); locked still opens the tab's gate.
                ["intake"] = (() => !(CoreEntitlement.HasLab || CoreEntitlement.IsIntakePassAvailable),
                              panel => panel.ShowTab("gradedintake")),
            };

        internal static IEnumerable<LauncherCard> VisibleCards => LauncherCards.All.Where(c => Destinations.ContainsKey(c.Id));

        private static LauncherWindow? _window;

        /// <summary>The panel that sent us here, else the app's main window.</summary>
        private MainShellWindow? _panel;
        private MainShellWindow? Panel => _panel ?? MainShellWindow.Current;

        /// <summary>The launcher, if it has been built this run.</summary>
        internal static LauncherWindow? Instance => _window;

        public LauncherWindow()
        {
            InitializeComponent();
            GamesColumn.SizeChanged += (_, _) => SeatGrid();
            Closed += (_, _) => { if (ReferenceEquals(_window, this)) _window = null; };
            BuildTiles();
        }

        /// <summary>WPF LauncherHost.Show: bring the launcher up, creating it on first use.</summary>
        internal static LauncherWindow Open()
        {
            var w = _window ??= new LauncherWindow();
            w.BuildTiles();
            if (!w.IsVisible) w.Show();
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            return w;
        }

        /// <summary>WPF LauncherHost.BackToLauncher: the panel's door; refused under Lockdown.</summary>
        internal static bool BackToLauncher(MainShellWindow panel)
        {
            if (MainShellWindow.LockdownActive)
            {
                try { Services.LockdownService.Current?.NotifyEscapeAttempt(Services.Possession.EscapeKinds.Close); } catch { }
                return false;
            }
            panel.Hide();
            panel.HideAvatarTube();
            var w = Open();
            // The launcher goes with its panel: hidden, it would keep a trayless process alive UI-less.
            if (!ReferenceEquals(w._panel, panel)) { w._panel = panel; panel.Closed += (_, _) => w.Close(); }
            return true;
        }

        /// <summary>WPF LauncherHost.OpenPanel: the launcher hides, the panel comes up, then <paramref name="then"/>.</summary>
        internal void OpenPanel(Action<MainShellWindow>? then = null)
        {
            var panel = Panel;
            if (panel == null) { Log.Warning("[Launcher] OpenPanel with no main window"); return; }
            Hide();
            panel.ShowFromTray();
            try { then?.Invoke(panel); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] step after OpenPanel failed"); }
        }

        /// <summary>WPF LauncherHost.RequestClose, decided by Core's LauncherRules.Close.</summary>
        internal void RequestClose()
        {
            var panel = Panel;
            var outcome = LauncherRules.Close(MainShellWindow.LockdownActive,
                CoreEngine.IsRunning || CoreSession.IsSessionRunning,
                panel is { IsVisible: true }, panel != null);
            switch (outcome)
            {
                case LauncherCloseOutcome.Veto:
                    try { Services.LockdownService.Current?.NotifyEscapeAttempt(Services.Possession.EscapeKinds.Close); } catch { }
                    break;
                case LauncherCloseOutcome.Hide:
                    // WPF hides behind the panel's tray icon. With no tray host (a bare Linux
                    // desktop) a hidden launcher over a hidden panel has no way back: minimize.
                    if (panel is { IsVisible: false } && !panel.TrayHostPresent()) WindowState = WindowState.Minimized;
                    else Hide();
                    break;
                case LauncherCloseOutcome.Exit:
                    panel!.RequestExit();
                    break;
            }
        }

        /// <summary>Closing is never the user's own decision (WPF OnClosing): the host decides.
        /// An application shutdown and a Close() from code go through.</summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (!e.IsProgrammatic && e.CloseReason == WindowCloseReason.WindowClosing)
            {
                e.Cancel = true;
                Dispatcher.UIThread.Post(RequestClose);
            }
            base.OnClosing(e);
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        }

        private void BtnMinimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void BtnClose_Click(object? sender, RoutedEventArgs e) => RequestClose();

        private void PanelCta_Click(object? sender, RoutedEventArgs e) => OpenPanel();

        /// <summary>WPF LauncherHost.LaunchGame for the destinations this head has.</summary>
        internal void Play(LauncherCard card)
        {
            if (!Destinations.TryGetValue(card.Id, out var dest)) return;
            bool needsAccount = card.RequiresAccount && !CoreAccount.IsLoggedIn;
            // No leash gate on this head yet, and every destination here is a panel tab: a locked
            // one still opens it (its own gate paints the refusal), as WPF's does.
            if (LauncherRules.Game(needsAccount, Locked(dest), leashBlocks: false) == LauncherGameStep.SignIn)
            {
                OpenSignIn();
                return;
            }
            OpenPanel(dest.Open);
        }

        private static bool Locked((Func<bool> Locked, Action<MainShellWindow> Open) dest)
        {
            try { return dest.Locked(); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] lock probe threw"); return true; }
        }

        /// <summary>WPF LauncherWindow.OpenSignIn: the panel's login dialog, owned by the launcher.</summary>
        private async void OpenSignIn()
        {
            var panel = Panel;
            if (panel == null) { Log.Warning("[Launcher] sign-in with no main window"); return; }
            try { await panel.OpenUnifiedLoginDialog(this); }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] sign-in dialog failed"); }
            BuildTiles();
        }

        internal void BuildTiles()
        {
            GamesGrid.Children.Clear();
            foreach (var card in VisibleCards) GamesGrid.Children.Add(CreateTile(card));
            SeatGrid();
        }

        /// <summary>WPF GamesColumn_SizeChanged: columns and card height from LauncherGridLayout.</summary>
        private void SeatGrid()
        {
            int count = GamesGrid.Children.Count;
            double width = GamesColumn.Bounds.Width;
            int columns = LauncherGridLayout.Columns(width, count);
            GamesGrid.Columns = columns;
            double height = LauncherGridLayout.TileHeight(LauncherGridLayout.TileWidth(width, columns));
            foreach (var tile in GamesGrid.Children) tile.Height = height;
        }

        private Border CreateTile(LauncherCard card)
        {
            var dest = Destinations[card.Id];
            bool needsAccount = card.RequiresAccount && !CoreAccount.IsLoggedIn;
            bool locked = !needsAccount && Locked(dest);
            var hue = Color.FromRgb(card.R, card.G, card.B);

            var tile = new Border
            {
                CornerRadius = new CornerRadius(TileRadius),
                Background = Res("SurfaceBgBrush"),
                BorderBrush = locked ? Res("Tier2DiamondBorderBrush") : RimBrush(hue),
                BorderThickness = new Thickness(LauncherGridLayout.TileBorder),
                Margin = new Thickness(LauncherGridLayout.TileMargin),
                ClipToBounds = true,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = card.Id,
            };

            var body = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };

            // --- the art plate: the mod's art, else the hue plate with the glyph ---
            // ponytail: WPF's glyph plate also draws a radial hue and a road (no art-less card shows yet).
            var plate = new Panel { MinHeight = LauncherGridLayout.MinArtHeight, Background = new SolidColorBrush(hue, 0.35) };
            if (ModArt.TryLoad(card.ArtPath, 640) is { } art)
                plate.Children.Add(new Image { Source = art, Stretch = Stretch.UniformToFill });
            else
                plate.Children.Add(new TextBlock
                {
                    Text = card.Glyph, FontSize = 64, Foreground = Brushes.White, Opacity = 0.95,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                });
            var surface = ((ISolidColorBrush)Res("SurfaceBgBrush")).Color;
            plate.Children.Add(new Border
            {
                IsHitTestVisible = false,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Colors.Transparent, 0),
                        new GradientStop(Color.FromArgb(0x30, 0, 0, 0), 0.55),
                        new GradientStop(Color.FromArgb(0xB0, surface.R, surface.G, surface.B), 0.88),
                        new GradientStop(surface, 1),
                    },
                },
            });
            if (needsAccount) plate.Children.Add(Pill("launcher_pill_sign_in", null, Res("AccentGradientBrush"), Brushes.White, 10));
            else if (locked) plate.Children.Add(Pill("launcher_prime_pill", "🔒", Res("Tier2DiamondBorderBrush"),
                new SolidColorBrush(Color.FromRgb(0x2A, 0x1C, 0x08)), 10));
            if (card.IsNew) plate.Children.Add(Pill("exclusives_badge_new", null, Res("AccentGradientBrush"), Brushes.White,
                needsAccount || locked ? 40 : 10));
            body.Children.Add(plate);

            // --- title, blurb, play: fixed heights so every title sits on the same line ---
            var text = new StackPanel
            {
                Margin = new Thickness(16, LauncherGridLayout.TextPadTop, 16, LauncherGridLayout.TextPadBottom),
                Height = LauncherGridLayout.TextHeight - LauncherGridLayout.TextPadTop - LauncherGridLayout.TextPadBottom,
            };
            Grid.SetRow(text, 1);
            text.Children.Add(new TextBlock
            {
                Text = Loc.Get(card.TitleKey), FontFamily = new FontFamily("Fredoka, Segoe UI"), FontSize = 19,
                FontWeight = FontWeight.SemiBold, Foreground = Res("TextLightBrush"),
                Height = LauncherGridLayout.TitleHeight, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            text.Children.Add(new TextBlock
            {
                Text = Loc.Get(card.BlurbKey), FontSize = 14, Margin = new Thickness(0, LauncherGridLayout.BlurbGap, 0, 0),
                Foreground = Res("TextSecondaryBrush"), TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, Height = LauncherGridLayout.BlurbHeight,
            });
            var play = PlayButton(hue, locked, needsAccount);
            play.Click += (_, _) => Play(card);
            text.Children.Add(play);
            body.Children.Add(text);

            // A signed-out card is the ask as a whole, not only its button (WPF Tiles.cs:218).
            if (needsAccount) tile.PointerReleased += (_, _) => OpenSignIn();

            tile.Child = body;
            return tile;
        }

        /// <summary>WPF BuildPlayButton: outline at rest, hue fill on hover; Sign in / Locked / Play.</summary>
        private Button PlayButton(Color hue, bool locked, bool needsAccount)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            if (!needsAccount)
                label.Children.Add(new TextBlock
                {
                    Text = locked ? "🔒" : "▶", FontSize = locked ? 13 : 11, Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            label.Children.Add(new TextBlock
            {
                Text = Loc.Get(needsAccount ? "launcher_sign_in" : locked ? "launcher_locked" : "launcher_play"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            IBrush fill = needsAccount ? Res("AccentGradientBrush")
                : locked ? Res("Tier2DiamondBorderBrush")
                : new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Lighten(hue, 0.15), 0), new GradientStop(Darken(hue, 0.75), 1) },
                };
            return new Button
            {
                Content = label,
                Theme = (ControlTheme)this.FindResource("LauncherPlay")!,
                Height = LauncherGridLayout.PlayHeight, Margin = new Thickness(0, LauncherGridLayout.PlayGap, 0, 0),
                Background = fill,
                BorderBrush = new SolidColorBrush(Lighten(hue, 0.25)),
                Foreground = new SolidColorBrush(Lighten(hue, 0.45)),
            };
        }

        private Border Pill(string key, string? icon, IBrush background, IBrush ink, double top)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (icon != null) row.Children.Add(new TextBlock { Text = icon, FontSize = 10, Margin = new Thickness(0, 0, 5, 0), Foreground = ink });
            row.Children.Add(new TextBlock { Text = Loc.Get(key), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = ink });
            return new Border
            {
                Background = background, CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 10, 3),
                Margin = new Thickness(12, top, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = row,
            };
        }

        private IBrush Res(string key) => (IBrush)this.FindResource(key)!;

        /// <summary>WPF RimBrush: the hue at the corners, the glass border between.</summary>
        private IBrush RimBrush(Color hue)
        {
            var glass = ((ISolidColorBrush)Res("GlassBorderBrush")).Color;
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x99, hue.R, hue.G, hue.B), 0),
                    new GradientStop(glass, 0.6),
                    new GradientStop(Color.FromArgb(0x55, hue.R, hue.G, hue.B), 1),
                },
            };
        }

        private static Color Lighten(Color c, double t) => Color.FromRgb(
            (byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));

        private static Color Darken(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
    }
}
