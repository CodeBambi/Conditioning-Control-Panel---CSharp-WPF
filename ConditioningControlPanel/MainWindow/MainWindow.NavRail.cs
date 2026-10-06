using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls.Depth;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel
{
    /// <summary>
    /// THE SECTION RAIL (nav rework, 2026-10-06). Replaces the icon strip + hover flyout +
    /// click-to-open doors with an always-labelled rail of seven sections and a Settings gear,
    /// inside the 96px canvas column (the column already reserved 96px for the old 56px strip +
    /// its 40px gutter, so the page did not shrink). No flyout, no hold latch, no watchdog, no
    /// airspace juggling: the rail never overlays the page any more, so every one of those
    /// existed for a problem that is gone.
    ///
    /// <para>What stayed, because it is the identity the owner kept: the medallion art (mods
    /// re-art it through <see cref="ApplyDoorArt"/>, nav/door_*.png), the hue halo, ChromeFx's
    /// hover nudge (the icon Viewbox is a direct child of each row's Grid), the possession
    /// reroute seam and the mod-aware labels.</para>
    ///
    /// <para>This file paints; <see cref="NavRailRules"/> decides (pure, tested).</para>
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>One section row's parts, found once by type inside the row's Grid.</summary>
        private sealed class NavSectionRow
        {
            internal string Section = "";
            internal Button Button = null!;
            internal Ellipse? Glow;
            internal Border? Tile;
            /// <summary>The ring drawn OVER the art (Tag "navring", polish wave 9). Null on a row
            /// authored without it: the ring then paints on <see cref="Tile"/> as before.</summary>
            internal Border? Ring;
            /// <summary>The hue wash over the art (Tag "navtint", polish wave 9).</summary>
            internal Border? Tint;
            internal Border? Badge;
            internal TextBlock? BadgeText;
            internal TextBlock? Label;
            internal SolidColorBrush? Hue;
            /// <summary>The coin's own parts (polish wave 10): contact disc, dish, socket shade, lip.</summary>
            internal NavCoinParts? Coin;
            /// <summary>The row's content Grid (coin + label): the one element that travels and leans.</summary>
            internal FrameworkElement? Face;
            internal TranslateTransform Lift = new();
            internal RotateTransform Tilt = new();
            internal bool Active;
            internal bool Painted;
            internal bool Hovered;
            internal bool Pressed;
        }

        /// <summary>
        /// A rail medallion as a COIN (nav polish wave 10, depth; the law is DepthRules). Built once
        /// per row by <see cref="BuildNavCoin"/> and inserted around the authored tile:
        /// <list type="bullet">
        /// <item>Disc: the contact shadow on the sheet, an Ellipse UNDER the tile, tinted by the
        ///   row's hue (DepthRules.ShadowColor), pushed down by DepthRules.ShadowFor.</item>
        /// <item>Dish: DepthCoinDish over the tile, under the art.</item>
        /// <item>Socket: DepthPressedShade over the art, shown while the coin is lit or pressed.</item>
        /// <item>Rim: the 1 px lip just inside the hue ring: DepthCoinRim raised, DepthPressedBevel
        ///   seated. The wave 9 ring stays above it and keeps its job.</item>
        /// </list>
        /// </summary>
        internal sealed class NavCoinParts
        {
            internal Ellipse Disc = null!;
            internal TranslateTransform DiscShift = new();
            internal Border Dish = null!;
            internal Border Socket = null!;
            internal Border Rim = null!;
        }

        private readonly List<NavSectionRow> _navSectionRows = new();
        private bool _navRailReady;

        // Polish wave 9: the lit halo reads louder (0.35 -> 0.55); idle stays 0, chrome never idles lit.
        private const double NavGlowActive = 0.55;
        private const string NavRingTag = "navring";
        private const string NavTintTag = "navtint";
        private const int NavGlowFadeMs = 160;
        private const string NavSectionLabelTag = "navsectionlabel";
        private const string NavBadgeTag = "navbadge";

        /// <summary>Every rail button, top to bottom, gear last. Null-free.</summary>
        private IEnumerable<Button> NavSectionButtons => new[]
        {
            DoorHome, DoorStudio, DoorCompanion, DoorPlay, DoorSocial, DoorYou, DoorLibrary, DoorSettings,
        }.Where(b => b != null);

        private void InitializeNavRail()
        {
            try
            {
                if (_navRailReady || NavSidebar == null) return;

                CacheNavSectionRows();
                HookNavDoorRerouteSeam();
                ApplyDoorArt();
                ApplyModToNavRailLabels();
                RegisterSectionShortcuts();

                NavBadges.Changed += OnNavBadgeChanged;
                Closed += (_, __) => NavBadges.Changed -= OnNavBadgeChanged;
                foreach (var row in _navSectionRows) PaintNavBadge(row, NavBadges.Get(row.Section));

                if (NavRailShadow != null) NavRailShadow.Width = DepthRules.RailShadowPx;

                _navRailReady = true;
                RefreshSectionRail(_activeTabKey);
            }
            catch (Exception ex)
            {
                // A rail that fails here stays as authored: labelled rows that navigate, no
                // badges, no lit row. Degraded, never empty.
                App.Logger?.Warning(ex, "InitializeNavRail failed; rail stays as authored");
            }
        }

        private void CacheNavSectionRows()
        {
            _navSectionRows.Clear();
            foreach (var btn in NavSectionButtons)
            {
                if (btn.Tag is not string tag) continue;
                var row = new NavSectionRow
                {
                    Section = NavRailRules.SectionForDoorTag(tag),
                    Button = btn,
                };
                // Polish wave 2: the hue comes from the ONE table (NavStripRules.Accent), never
                // from per-button XAML. The template's fill and bar read Background/BorderBrush.
                var hue = NavStripRules.Accent(row.Section);
                row.Hue = NavFrozen(hue);
                btn.Background = NavFrozen(NavRailRules.WithAlpha(hue, NavRailRules.FillAlpha));
                btn.BorderBrush = row.Hue;
                if (btn.Content is Panel grid)
                {
                    foreach (var child in grid.Children.OfType<FrameworkElement>())
                    {
                        switch (child)
                        {
                            case Ellipse e: row.Glow = e; break;
                            case Border b when (b.Tag as string) == NavBadgeTag:
                                row.Badge = b; row.BadgeText = b.Child as TextBlock; break;
                            case Border b when (b.Tag as string) == NavRingTag: row.Ring = b; break;
                            case Border b when (b.Tag as string) == NavTintTag: row.Tint = b; break;
                            case Border b: row.Tile ??= b; break;
                            case TextBlock t when (t.Tag as string) == NavSectionLabelTag: row.Label = t; break;
                        }
                    }
                }
                if (row.Glow != null) row.Glow.Fill = BuildNavDoorGlow(row.Hue);
                if (row.Label != null)
                {
                    // A quiet, static halo in the mod's glow colour (no breathing loop: chrome
                    // never idles in motion). The label itself stays full-contrast TextLight.
                    row.Label.Effect = new DropShadowEffect
                    {
                        Color = FxTheme.GlowColor, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.45,
                    };
                }
                if (row.Tile != null)
                    row.Tile.Background = NavFrozen(NavRailRules.WithAlpha(hue, NavRailRules.TileTintAlpha));
                PaintNavTint(row);
                if (btn.Content is Panel face && row.Tile != null && row.Hue != null)
                {
                    row.Face = face;
                    row.Coin = BuildNavCoin(face, row.Tile, row.Hue.Color);
                    face.RenderTransformOrigin = new Point(0.5, 0.4);
                    face.RenderTransform = new TransformGroup { Children = { row.Tilt, row.Lift } };
                }
                HookNavPress(row);
                var captured = row;
                btn.MouseEnter += (_, __) =>
                {
                    captured.Hovered = true;
                    PaintNavRing(captured, hover: true);
                    PaintNavCoin(captured);
                };
                btn.MouseLeave += (_, __) =>
                {
                    captured.Hovered = false;
                    captured.Pressed = false;
                    PaintNavRing(captured, hover: false);
                    SettleNavTilt(captured);
                    PaintNavCoin(captured);
                };
                btn.MouseMove += (_, e) => TiltNavCoin(captured, e);
                PaintNavRing(row, hover: false);
                PaintNavCoin(row);
                btn.ToolTipOpening += (_, __) => btn.ToolTip = BuildNavRowToolTip(captured);
                _navSectionRows.Add(row);
            }
        }

        /// <summary>Press (polish wave 10): the coin travels DepthRules.PressTravelPx down in PressMs
        /// and drops its shadow; release springs it to wherever the state now puts it (lit = the
        /// socket, else hover lift) over ReleaseMs, passing the target by ReleaseOvershootPx. The
        /// release listens with handledEventsToo, AFTER the Button raised Click, so a press that
        /// navigates springs straight into its socket.</summary>
        private static void HookNavPress(NavSectionRow row)
        {
            var btn = row.Button;
            btn.PreviewMouseLeftButtonDown += (_, __) =>
            {
                row.Pressed = true;
                SettleNavTilt(row);
                PaintNavCoin(row);
            };
            btn.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler((_, __) =>
            {
                if (!row.Pressed) return;
                row.Pressed = false;
                row.Hovered = btn.IsMouseOver;
                PaintNavCoin(row, spring: true);
            }), handledEventsToo: true);
        }

        // ============================== the coin (polish wave 10) ==============================

        /// <summary>Builds the coin's parts around an authored row: the disc goes under the tile,
        /// the dish right over it (under the art), the socket shade and the lip over the art and
        /// the hue wash but under the ring. Brushes come from Resources/Theme/Depth.xaml by
        /// resource reference; only the disc is painted here, from the row's hue.</summary>
        internal static NavCoinParts BuildNavCoin(Panel face, Border tile, Color hue)
        {
            var coin = new NavCoinParts();
            int row = Grid.GetRow(tile);

            coin.Disc = new Ellipse
            {
                Width = NavRailRules.CoinDiscWidth,
                Height = NavRailRules.CoinDiscHeight,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                // Centred on the tile's foot; the shift pushes it down by the shadow length.
                Margin = new Thickness(0, 0, 0, -NavRailRules.CoinDiscHeight / 2),
                Fill = BuildNavCoinDisc(hue),
                RenderTransform = coin.DiscShift,
                IsHitTestVisible = false,
            };
            coin.Dish = new Border
            {
                Width = tile.Width,
                Height = tile.Height,
                CornerRadius = tile.CornerRadius,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            coin.Dish.SetResourceReference(Border.BackgroundProperty, "DepthCoinDish");
            coin.Socket = new Border
            {
                Width = NavRailRules.CoinRimSize,
                Height = NavRailRules.CoinRimSize,
                CornerRadius = new CornerRadius(NavRailRules.CoinRimRadius),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0,
                IsHitTestVisible = false,
            };
            coin.Socket.SetResourceReference(Border.BackgroundProperty, "DepthPressedShade");
            coin.Rim = new Border
            {
                Width = NavRailRules.CoinRimSize,
                Height = NavRailRules.CoinRimSize,
                CornerRadius = new CornerRadius(NavRailRules.CoinRimRadius),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            coin.Rim.SetResourceReference(Border.BorderBrushProperty, "DepthCoinRim");
            foreach (var part in new FrameworkElement[] { coin.Disc, coin.Dish, coin.Socket, coin.Rim })
                Grid.SetRow(part, row);

            var kids = face.Children;
            kids.Insert(kids.IndexOf(tile), coin.Disc);
            kids.Insert(kids.IndexOf(tile) + 1, coin.Dish);
            // Over the art and its hue wash, under the ring (the art itself when a row has no wash).
            int over = -1;
            for (int i = 0; i < kids.Count; i++)
                if (kids[i] is Border b && (b.Tag as string) == NavTintTag) over = i;
            if (over < 0)
                for (int i = 0; i < kids.Count; i++)
                    if (kids[i] is Viewbox) over = i;
            int at = over < 0 ? kids.IndexOf(coin.Dish) + 1 : over + 1;
            kids.Insert(at, coin.Socket);
            kids.Insert(at + 1, coin.Rim);
            return coin;
        }

        /// <summary>The contact disc: the DepthDropDisc shape in the row's own shadow colour.</summary>
        internal static Brush BuildNavCoinDisc(Color hue)
        {
            var b = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5,
            };
            foreach (var (c, o) in NavRailRules.CoinDiscStops(hue)) b.GradientStops.Add(new GradientStop(c, o));
            b.Freeze();
            return b;
        }

        /// <summary>Paints a coin for its row's state, eased by DepthRules (Motion Off = instant).</summary>
        private static void PaintNavCoin(NavSectionRow row, bool spring = false)
        {
            if (row.Coin == null) return;
            try
            {
                int full = row.Pressed ? DepthRules.PressMs : spring ? DepthRules.ReleaseMs : DepthRules.HoverMs;
                ApplyNavCoinState(row.Coin, row.Lift, row.Pressed, row.Active, row.Hovered,
                    DepthRules.Ms(full, MotionFx.Level), spring);
            }
            catch (Exception ex) { App.Logger?.Debug("PaintNavCoin: {E}", ex.Message); }
        }

        /// <summary>
        /// The coin law in one place. Light swaps at once (a lit coin wears the pressed lip and the
        /// socket shade, an idle one the raised lip); motion eases: the face travels to
        /// <see cref="NavRailRules.CoinTravel"/>, the disc to <see cref="NavRailRules.CoinShadow"/>
        /// and fades out when the coin sits down. <paramref name="ms"/> 0 = set, no animation.
        /// </summary>
        internal static void ApplyNavCoinState(NavCoinParts coin, TranslateTransform lift,
            bool pressed, bool active, bool hovered, int ms, bool spring)
        {
            bool seated = pressed || active;
            coin.Rim.SetResourceReference(Border.BorderBrushProperty, seated ? "DepthPressedBevel" : "DepthCoinRim");
            coin.Socket.Opacity = seated ? 1 : 0;

            double travel = NavRailRules.CoinTravel(pressed, active, hovered);
            double shadow = NavRailRules.CoinShadow(pressed, active, hovered);
            double discTo = shadow > 0 ? 1 : 0;

            if (ms <= 0)
            {
                lift.BeginAnimation(TranslateTransform.YProperty, null);
                coin.DiscShift.BeginAnimation(TranslateTransform.YProperty, null);
                coin.Disc.BeginAnimation(UIElement.OpacityProperty, null);
                lift.Y = travel;
                if (shadow > 0) coin.DiscShift.Y = shadow;
                coin.Disc.Opacity = discTo;
                return;
            }

            var span = TimeSpan.FromMilliseconds(ms);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            double from = lift.Y;
            if (spring && Math.Abs(from - travel) > 0.01)
            {
                var k = new DoubleAnimationUsingKeyFrames { Duration = span };
                k.KeyFrames.Add(new EasingDoubleKeyFrame(NavRailRules.CoinOvershoot(from, travel),
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * 0.6)), ease));
                k.KeyFrames.Add(new EasingDoubleKeyFrame(travel, KeyTime.FromTimeSpan(span),
                    new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
                lift.BeginAnimation(TranslateTransform.YProperty, k);
            }
            else
            {
                lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(travel, span) { EasingFunction = ease });
            }
            if (shadow > 0)
                coin.DiscShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(shadow, span) { EasingFunction = ease });
            coin.Disc.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(discTo, span));
        }

        /// <summary>Hover lean toward the pointer (the launcher tile recipe), idle coins only and
        /// only where DepthRules.TiltAllowed says so. A lit coin sits in its socket and stays level.</summary>
        private static void TiltNavCoin(NavSectionRow row, MouseEventArgs e)
        {
            try
            {
                if (row.Face == null || row.Tile == null) return;
                if (row.Pressed || !NavRailRules.CoinTilts(row.Active, MotionFx.Level, PerformanceProfile.CurrentTier))
                {
                    SettleNavTilt(row);
                    return;
                }
                double w = row.Tile.ActualWidth, h = row.Tile.ActualHeight;
                if (w <= 0 || h <= 0) return;
                var at = e.GetPosition(row.Tile);
                double angle = NavRailRules.CoinTilt(at.X / w * 2 - 1, at.Y / h * 2 - 1);
                int ms = DepthRules.Ms(NavRailRules.CoinTiltMs, MotionFx.Level);
                if (ms <= 0) { row.Tilt.BeginAnimation(RotateTransform.AngleProperty, null); row.Tilt.Angle = angle; return; }
                row.Tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, TimeSpan.FromMilliseconds(ms)));
            }
            catch (Exception ex) { App.Logger?.Debug("TiltNavCoin: {E}", ex.Message); }
        }

        private static void SettleNavTilt(NavSectionRow row)
        {
            int ms = DepthRules.Ms(DepthRules.HoverMs, MotionFx.Level);
            if (ms <= 0 || (row.Tilt.Angle == 0 && !row.Tilt.HasAnimatedProperties))
            {
                row.Tilt.BeginAnimation(RotateTransform.AngleProperty, null);
                row.Tilt.Angle = 0;
                return;
            }
            row.Tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(ms))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        }

        /// <summary>
        /// The rail's shadow on the page (NavRailShadow, DepthRules.RailShadowPx wide), tinted by
        /// the section hue. The integrator calls this from PaintSectionWash; until then the
        /// Rectangle wears the neutral DepthRailShadow theme brush.
        /// </summary>
        internal void PaintDepthRail(Color hue)
        {
            try
            {
                if (NavRailShadow == null) return;
                var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                foreach (var (c, o) in NavRailRules.RailShadowStops(hue)) g.GradientStops.Add(new GradientStop(c, o));
                g.Freeze();
                NavRailShadow.Fill = g;
            }
            catch (Exception ex) { App.Logger?.Debug("PaintDepthRail: {E}", ex.Message); }
        }

        /// <summary>The section name, the open count when a badge is up, and a greyed "Ctrl+2".
        /// Built on open so a language or mod switch never leaves a stale tooltip.</summary>
        private static object BuildNavRowToolTip(NavSectionRow row)
        {
            var panel = new StackPanel();
            var name = row.Label?.Text ?? row.Section;
            if (!string.IsNullOrEmpty(name))
                name = name.Substring(0, 1) + (name.Length > 1 ? name.Substring(1).ToLowerInvariant() : "");
            panel.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold });
            int count = NavBadges.Get(row.Section);
            if (count > 0)
                panel.Children.Add(new TextBlock { Text = Loc.GetF("nav_badge_open", count) });
            int n = NavRailRules.ShortcutNumber(row.Section);
            if (n > 0)
                panel.Children.Add(new TextBlock { Text = "Ctrl+" + n, Opacity = 0.6, FontSize = 11 });
            return panel;
        }

        // ============================== the reroute seam ==============================

        /// <summary>
        /// A single consultation point on the door-press path, for anything that wants to send a
        /// click somewhere other than where it was aimed. Given the pressed row's Tag it returns
        /// the section or tab key to open INSTEAD, or null to let the press through. Possession's
        /// "misroute" haunt is the only writer, and it clears itself the moment it fires.
        /// </summary>
        internal static Func<string, string?>? PossessionReroute;

        private void HookNavDoorRerouteSeam()
        {
            try
            {
                foreach (var btn in NavSectionButtons)
                    btn.PreviewMouseLeftButtonDown += NavDoor_PossessionReroute;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "HookNavDoorRerouteSeam failed; doors route normally"); }
        }

        private void NavDoor_PossessionReroute(object sender, MouseButtonEventArgs e)
        {
            var hook = PossessionReroute;
            if (hook == null) return;
            if (sender is not FrameworkElement fe || fe.Tag is not string door) return;

            string? to;
            try { to = hook(door); }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "NavDoor_PossessionReroute: hook threw; the press stands");
                return;
            }
            if (string.IsNullOrWhiteSpace(to)) return;

            e.Handled = true;
            var section = NavRailRules.SectionForDoorTag(to!);
            if (NavSections.Find(section) != null) { OpenNavSection(section); return; }
            ShowTab(to!);
        }

        /// <summary>The rail's search pill. Same call as the Ctrl+K InputBinding.</summary>
        private void BtnNavSearch_Click(object sender, RoutedEventArgs e)
        {
            try { SettingsPaletteWindow.Toggle(this); }
            catch (Exception ex) { App.Logger?.Warning(ex, "BtnNavSearch_Click: palette toggle failed"); }
        }

        // ============================== navigation ==============================

        /// <summary>A row press: the section's last tab, else its default tab.</summary>
        private void NavDoor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tag) return;
            OpenNavSection(NavRailRules.SectionForDoorTag(tag));
        }

        /// <summary>Opens a section the way its rail row does; logs and stays on an unknown key.</summary>
        internal void OpenNavSection(string section)
        {
            var tab = NavRailRules.TargetTab(section, App.Settings?.Current?.NavLastTabBySection);
            if (tab == null)
            {
                App.Logger?.Warning("OpenNavSection: no section {Section}", section);
                return;
            }
            ShowTab(tab);
        }

        /// <summary>Ctrl+1..7 jump to the rail's sections. Window InputBindings: they only fire
        /// while this window has keyboard focus, so a game window (its own HWND) never sees them,
        /// and the panic key is a global hook that no binding here can swallow.</summary>
        private void RegisterSectionShortcuts()
        {
            var keys = new[] { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7 };
            var rails = NavRailRules.RailSections;
            for (int i = 0; i < rails.Count && i < keys.Length; i++)
            {
                var section = rails[i].Key;
                var cmd = new RoutedCommand("NavSection_" + section, typeof(MainWindow));
                CommandBindings.Add(new CommandBinding(cmd, (_, __) => OpenNavSection(section)));
                InputBindings.Add(new KeyBinding(cmd, keys[i], ModifierKeys.Control));
            }
        }

        /// <summary>
        /// Lights the row that owns <paramref name="tabKey"/>: filled tile, 4px bar in the
        /// section hue, full-opacity tile, hue ring, halo. Called from ShowTab (through
        /// ExpandDoorForTab) on every navigation, so the lit row follows deep links, the palette
        /// and the strip. A key no section owns leaves the current row lit.
        /// </summary>
        internal void RefreshSectionRail(string? tabKey)
        {
            if (!_navRailReady) return;
            var section = NavSections.SectionForTab(CanonicalTabKey(tabKey ?? ""));
            if (section == null) return;
            foreach (var row in _navSectionRows)
            {
                bool on = row.Section == section;
                if (row.Painted && on == row.Active) continue;
                row.Active = on;
                row.Painted = true;
                PaintNavRowActive(row);
            }
        }

        private static void PaintNavRowActive(NavSectionRow row)
        {
            try
            {
                var btn = row.Button;
                btn.ApplyTemplate();
                if (btn.Template?.FindName("NavActiveBar", btn) is FrameworkElement bar)
                {
                    bar.BeginAnimation(UIElement.OpacityProperty, null);
                    bar.Opacity = 1;
                    bar.Visibility = row.Active ? Visibility.Visible : Visibility.Collapsed;
                }
                if (btn.Template?.FindName("NavActiveFill", btn) is FrameworkElement fill)
                    fill.Opacity = row.Active ? 1 : 0;
                PaintNavSpur(row);
                PaintNavTint(row);

                // A pale medallion carries a shade (x:Name "Shade" + door name) so it idles at its
                // neighbours' weight; lit, it shows its art in full.
                if (btn.FindName("Shade" + btn.Name) is UIElement shade)
                    shade.Opacity = row.Active ? 0 : 1;

                PaintNavRing(row, hover: btn.IsMouseOver);
                if (row.Label != null)
                {
                    if (row.Active && row.Hue != null) row.Label.Foreground = row.Hue;
                    else row.Label.ClearValue(TextBlock.ForegroundProperty);
                }
                if (row.Glow != null)
                {
                    double to = row.Active ? NavGlowActive : 0;
                    int ms = NavRailRules.Ms(NavGlowFadeMs, MotionFx.Level);
                    if (ms <= 0)
                    {
                        row.Glow.BeginAnimation(UIElement.OpacityProperty, null);
                        row.Glow.Opacity = to;
                    }
                    else
                    {
                        row.Glow.BeginAnimation(UIElement.OpacityProperty,
                            new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)));
                    }
                }
                // The lit coin sits down in its socket (polish wave 10); a press in flight wins.
                if (row.Active) SettleNavTilt(row);
                PaintNavCoin(row);
            }
            catch (Exception ex) { App.Logger?.Debug("PaintNavRowActive: {E}", ex.Message); }
        }

        /// <summary>The medallion ring (polish wave 9): idle = the hue at 80% (hover 95%), 3 px;
        /// active = the hue lifted 25% toward white, solid, 3.5 px. Painted on the "navring" Border
        /// that sits over the art, so a thicker ring eats no picture; a row without one falls back
        /// to the tile's own border. Values live in NavRailRules so a test can pin them.</summary>
        private static void PaintNavRing(NavSectionRow row, bool hover)
        {
            var target = row.Ring ?? row.Tile;
            if (target == null || row.Hue == null) return;
            if (row.Tile != null) row.Tile.Opacity = 1.0;
            target.BorderBrush = NavFrozen(NavRailRules.RingColor(row.Hue.Color, row.Active, hover));
            target.BorderThickness = new Thickness(NavRailRules.RingThickness(row.Active));
        }

        /// <summary>The hue wash over the medallion art: a touch louder on the lit row.</summary>
        private static void PaintNavTint(NavSectionRow row)
        {
            if (row.Tint == null || row.Hue == null) return;
            row.Tint.Background = NavFrozen(NavRailRules.WithAlpha(row.Hue.Color, NavRailRules.ArtTintAlpha(row.Active)));
        }

        /// <summary>
        /// The spur (template part NavEdgeSpur): a short hue tab from the window edge into the lit
        /// medallion's ring, so the rail selection and the window edge read as one line. Built from
        /// the row's hue (the edge at 90%, the ring end at 60%), faded in over the halo's timing
        /// (160 ms, Reduced 80, Off instant). Hidden on every other row.
        /// </summary>
        private static void PaintNavSpur(NavSectionRow row)
        {
            var btn = row.Button;
            if (btn.Template?.FindName("NavEdgeSpur", btn) is not Border spur) return;
            spur.BeginAnimation(UIElement.OpacityProperty, null);
            if (!row.Active || row.Hue == null)
            {
                spur.Opacity = 0;
                spur.Visibility = Visibility.Collapsed;
                return;
            }
            var c = row.Hue.Color;
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            g.GradientStops.Add(new GradientStop(NavRailRules.WithAlpha(c, NavRailRules.SpurEdgeAlpha), 0));
            g.GradientStops.Add(new GradientStop(NavRailRules.WithAlpha(c, NavRailRules.SpurRingAlpha), 1));
            g.Freeze();
            spur.Background = g;
            spur.Visibility = Visibility.Visible;
            int ms = NavRailRules.Ms(NavGlowFadeMs, MotionFx.Level);
            if (ms <= 0) { spur.Opacity = 1; return; }
            spur.Opacity = 0;
            spur.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)));
        }

        private static SolidColorBrush NavFrozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>The medallion's halo: the row's hue fading to transparent. Frozen.</summary>
        private static Brush? BuildNavDoorGlow(SolidColorBrush? hue)
        {
            if (hue == null) return null;
            var c = hue.Color;
            var b = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
            };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, c.R, c.G, c.B), 0.0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, c.R, c.G, c.B), 0.60));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 1.0));
            b.Freeze();
            return b;
        }

        // ============================== badges ==============================

        private void OnNavBadgeChanged(string section, int count)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(new Action(() => OnNavBadgeChanged(section, count)));
                    return;
                }
                foreach (var row in _navSectionRows)
                    if (row.Section == section) PaintNavBadge(row, count);
            }
            catch (Exception ex) { App.Logger?.Debug("OnNavBadgeChanged: {E}", ex.Message); }
        }

        private static void PaintNavBadge(NavSectionRow row, int count)
        {
            if (row.Badge == null) return;
            var text = NavRailRules.BadgeText(count);
            if (row.BadgeText != null) row.BadgeText.Text = text ?? "";
            row.Badge.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        }

        // ============================== art and labels ==============================

        /// <summary>Decode cap for a medallion: 64px native art in a 40px icon, 2x headroom.</summary>
        private const int NavDoorArtDecodeWidth = 128;

        /// <summary>
        /// Points the medallions at the active mod's art. The paths are the mod compatibility
        /// surface (nav/door_*.png) and are never renamed. A null resolve leaves the authored
        /// Source alone: an empty rail is the worst failure this file can produce. Social has no
        /// embedded door art yet (it wears the Lobby's picture as a placeholder), so a mod may
        /// ship nav/door_social.png and nothing changes when it does not.
        /// </summary>
        private void ApplyDoorArt()
        {
            try
            {
                var doors = new (Image? Img, string Path)[]
                {
                    (ImgDoorHome,      "nav/door_home.png"),
                    (ImgDoorStudio,    "nav/door_studio.png"),
                    (ImgDoorCompanion, "nav/door_companion.png"),
                    (ImgDoorPlay,      "nav/door_play.png"),
                    (ImgDoorSocial,    "nav/door_social.png"),
                    (ImgDoorYou,       "nav/door_you.png"),
                    (ImgDoorLibrary,   "nav/door_library.png"),
                    (ImgDoorSettings,  "nav/door_settings.png"),
                };

                foreach (var (img, path) in doors)
                {
                    if (img == null) continue;
                    var art = ModResourceResolver.ResolveImageDecoded(path, NavDoorArtDecodeWidth);
                    if (art != null) img.Source = art;
                    // Social's shade only tones down the embedded placeholder; a mod's own art
                    // is drawn the way its author made it.
                    if (ReferenceEquals(img, ImgDoorSocial) && ShadeDoorSocial != null)
                        ShadeDoorSocial.Visibility = art != null ? Visibility.Collapsed : Visibility.Visible;
                }

                var glow = FxTheme.GlowColor;
                foreach (var row in _navSectionRows)
                    if (row.Label?.Effect is DropShadowEffect fx) fx.Color = glow;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ApplyDoorArt failed; nav rail keeps its embedded medallions");
            }
        }

        /// <summary>
        /// Puts the active mod's wording on the rail's labels: every {loc:Str key} binding in the
        /// rail is swapped for the same binding through ModAwareLocText, so a language switch still
        /// updates it live and a mod switch lands. Section names additionally go through
        /// <see cref="NavCapsConverter"/> (small ExtraBold caps). Called from ApplyModFeatureNames.
        /// </summary>
        private void ApplyModToNavRailLabels()
        {
            if (NavSidebar == null) return;
            ApplyModToNavRailLabels(NavSidebar);
        }

        private static void ApplyModToNavRailLabels(DependencyObject node)
        {
            if (node is TextBlock tb)
            {
                var be = BindingOperations.GetBindingExpression(tb, TextBlock.TextProperty);
                var b = be?.ParentBinding;
                if (b != null && ReferenceEquals(b.Source, Localization.LocalizationManager.Instance)
                    && b.Path?.Path is string path && path.Length > 2 && path[0] == '[' && path[^1] == ']')
                {
                    if (b.Converter is Localization.ModAwareLocText.Converter or NavCapsConverter)
                    {
                        be!.UpdateTarget();
                    }
                    else if (b.Converter == null)
                    {
                        var key = path.Substring(1, path.Length - 2);
                        IValueConverter conv = new Localization.ModAwareLocText.Converter(key);
                        if ((tb.Tag as string) == NavSectionLabelTag) conv = new NavCapsConverter(conv);
                        BindingOperations.SetBinding(tb, TextBlock.TextProperty, new Binding(path)
                        {
                            Source = Localization.LocalizationManager.Instance,
                            Mode = BindingMode.OneWay,
                            Converter = conv,
                        });
                    }
                }
            }

            foreach (var child in LogicalTreeHelper.GetChildren(node))
                if (child is DependencyObject d) ApplyModToNavRailLabels(d);
        }

        // ============================== kept entry points ==============================
        // The flyout is gone, so nothing holds the rail open and nothing re-syncs it to the
        // pointer. These stay as no-ops because other files (FriendsRailChip, the favourites
        // context menu, WindowChrome, the DoorShooter dev tool) still call them; remove them
        // with those calls.

        /// <summary>No-op: the rail no longer opens on hover.</summary>
        internal void SyncNavRailToPointer() { }

        /// <summary>No-op: there is no flyout to hold open.</summary>
        internal void HoldNavRailOpen(object owner) { }

        /// <summary>No-op: there is no flyout to hold open.</summary>
        internal void HoldNavRailOpen() { }

        /// <summary>No-op: there is no flyout to release.</summary>
        internal void ReleaseNavRailOpen(object owner) { }

        /// <summary>No-op: there is no flyout to release.</summary>
        internal void ReleaseNavRailOpen() { }

        /// <summary>No-op: a rail popup no longer has a flyout to keep open.</summary>
        internal void RegisterNavRailPopup(ContextMenu menu) { }
        /// <summary>
        /// Is the roster feature behind a tab key locked for this account right now? The roster's
        /// own probe (ExclusiveFeature.GateState) plus the daily rotation; a key with no roster row
        /// is not sold, so it answers false. Fails to false: a locked row that shows open costs a
        /// TierGate card the user was going to see anyway, an open row that shows locked lies about
        /// what somebody paid for. Read by the favourites chips and (next) the tab strip's badges.
        /// </summary>
        internal static bool IsNavEntryLocked(string exclusiveKey)
        {
            try
            {
                var feature = ExclusiveFeature.All.FirstOrDefault(
                    f => string.Equals(f.Key, exclusiveKey, StringComparison.Ordinal));
                if (feature == null) return false;
                if (feature.GateState() != ExclusiveGateState.Locked) return false;
                if (feature.DailyFreeKey != null &&
                    App.DailyFree?.IsFreeToday(feature.DailyFreeKey) == true) return false;
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("IsNavEntryLocked({Key}): {E}", exclusiveKey, ex.Message);
                return false;
            }
        }
    }
}
