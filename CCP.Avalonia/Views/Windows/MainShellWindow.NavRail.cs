// PORTED from WPF 7.1.5 ConditioningControlPanel/MainWindow/MainWindow.NavRail.cs (955 lines):
// THE SECTION RAIL (nav rework, 2026-10-06). Replaces the icon strip + hover flyout + door
// accordion of the older port with an always-labelled rail of seven sections and a Settings
// gear inside the 96px canvas column. No flyout, no hold latch: the rail never overlays the page.
//
// This file paints; Core decides (ConditioningControlPanel.Nav: NavSections, NavRailRules,
// NavStripRules, NavBadges; ConditioningControlPanel.Depth: DepthRules, DepthPalette).
//
// Ported: row cache by Tag, the hue per row from NavStripRules.Accent, lit fill + 4 px bar
// (class "navActive"), the vivid bevelled ring drawn over the art (RingStops), the art wash
// (ArtTintAlpha), the halo, the edge spur, the coin (contact disc, dish, socket, lip, outer
// hairline, inner shadow, specular crescent; face travel with the press spring), the badge pill
// (section hue, dims once seen), the row tooltip (name, open count, Ctrl+N), Ctrl+1..7, the
// rail's shadow on the page, upper-case labels.
//
// ponytail (not on this head / not this wave): mod-aware door art (ApplyDoorArt) and mod-aware
// labels, the possession reroute seam, the hover lean (TiltNavCoin), NavGlow ("moved here"
// ring), the section edge + embers.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.NavRail;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Nav;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>One section row's parts, found once by type / Tag inside the row's Grid.</summary>
        private sealed class NavSectionRow
        {
            internal string Section = "";
            internal Button Button = null!;
            internal Ellipse? Glow;
            internal Border? Tile;
            internal Border? Ring;
            internal Border? Tint;
            internal Border? Badge;
            internal TextBlock? BadgeText;
            internal TextBlock? Label;
            internal Border? Spur;
            internal uint Hue;
            internal NavCoinParts? Coin;
            internal Panel? Face;
            internal readonly TranslateTransform Lift = new();
            internal bool Active;
            internal bool Painted;
            internal bool Hovered;
            internal bool Pressed;
        }

        /// <summary>A rail medallion as a COIN (depth law: DepthRules). Built once per row.</summary>
        private sealed class NavCoinParts
        {
            internal Ellipse Disc = null!;
            internal readonly TranslateTransform DiscShift = new();
            internal Border Dish = null!;
            internal Border Socket = null!;
            internal Border Rim = null!;
            internal Border Outer = null!;
            internal Border Inner = null!;
            internal Path Specular = null!;
        }

        private readonly List<NavSectionRow> _navSectionRows = new();
        private bool _navRailReady;

        private const double NavGlowActive = 0.55;
        private const int NavGlowFadeMs = 160;
        private const string NavRingTag = "navring";
        private const string NavTintTag = "navtint";
        private const string NavBadgeTag = "navbadge";
        private const string NavSectionLabelTag = "navsectionlabel";

        /// <summary>Every rail button, top to bottom, gear last. Null-free.</summary>
        private IEnumerable<Button> NavSectionButtons =>
            new[] { "DoorHome", "DoorStudio", "DoorCompanion", "DoorPlay", "DoorSocial", "DoorYou", "DoorLibrary", "DoorSettings" }
                .Select(n => Named<Button>(n)).Where(b => b != null)!;

        /// <summary>
        /// The rail's one-time setup. Called from the constructor right after XAML load (the rows
        /// are namescope lookups). A rail that fails here stays as authored: labelled rows that
        /// navigate, no badges, no lit row. Degraded, never empty.
        /// </summary>
        internal void InitializeNavRail()
        {
            if (_navRailReady) { RefreshNavPremiumTags(); return; }
            try
            {
                if (Named<Border>("NavSidebar") == null) return;
                CacheNavSectionRows();
                RegisterSectionShortcuts();

                NavBadges.Changed += OnNavBadgeChanged;
                Closed += (_, _) => NavBadges.Changed -= OnNavBadgeChanged;
                foreach (var row in _navSectionRows) PaintNavBadge(row, NavBadges.Get(row.Section));

                if (Named<Rectangle>("NavRailShadow") is { } shadow) shadow.Width = DepthRules.RailShadowPx;
                if (Named<Rectangle>("HudBandWellTop") is { } well)
                {
                    well.Height = DepthRules.WellPx;
                    well.Fill = NavPaint.Depth("DepthWellTop");
                }

                try { LocalizationManager.Instance.LanguageChanged += OnNavRailLanguageChanged; } catch { }
                Closed += (_, _) => { try { LocalizationManager.Instance.LanguageChanged -= OnNavRailLanguageChanged; } catch { } };

                _navRailReady = true;
                RefreshSectionRail(CurrentTab);
                SyncSectionChrome(CurrentTab);
            }
            catch (Exception ex) { Log.Warning(ex, "InitializeNavRail failed; rail stays as authored"); }
        }

        private void CacheNavSectionRows()
        {
            _navSectionRows.Clear();
            foreach (var btn in NavSectionButtons)
            {
                if (btn.Tag is not string tag) continue;
                var row = new NavSectionRow { Section = NavRailRules.SectionForDoorTag(tag), Button = btn };
                // The hue comes from the ONE table (NavStripRules.Accent), never from per-button XAML.
                // The template's fill and bar read Background / BorderBrush.
                var hue = NavStripRules.Accent(row.Section);
                row.Hue = hue;
                btn.Background = NavPaint.Solid(NavRailRules.WithAlpha(hue, NavRailRules.FillAlpha));
                btn.BorderBrush = NavPaint.Solid(hue);
                if (btn.Content is Panel grid)
                {
                    foreach (var child in grid.Children)
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
                    row.Face = grid;
                }
                if (row.Glow != null)
                {
                    row.Glow.Fill = NavPaint.Radial(new[]
                    {
                        (NavRailRules.WithAlpha(hue, 0xFF), 0.0),
                        (NavRailRules.WithAlpha(hue, 0x80), 0.60),
                        (NavRailRules.WithAlpha(hue, 0x00), 1.0),
                    });
                }
                if (row.Tile != null) row.Tile.Background = NavPaint.Solid(NavRailRules.WithAlpha(hue, NavRailRules.TileTintAlpha));
                PaintNavRowLabel(row);
                PaintNavTint(row);
                if (row.Face != null && row.Tile != null)
                {
                    row.Coin = BuildNavCoin(row.Face, row.Tile, hue);
                    row.Face.RenderTransformOrigin = new RelativePoint(0.5, 0.4, RelativeUnit.Relative);
                    row.Face.RenderTransform = row.Lift;
                }

                var captured = row;
                btn.TemplateApplied += (_, e) =>
                {
                    captured.Spur = e.NameScope.Find<Border>("NavEdgeSpur");
                    PaintNavSpur(captured, animate: false);
                };
                btn.PointerEntered += (_, _) =>
                {
                    captured.Hovered = true;
                    ToolTip.SetTip(btn, BuildNavRowToolTip(captured));
                    PaintNavRing(captured);
                    PaintNavCoin(captured);
                };
                btn.PointerExited += (_, _) =>
                {
                    captured.Hovered = false;
                    captured.Pressed = false;
                    PaintNavRing(captured);
                    PaintNavCoin(captured);
                };
                // Press: the coin travels down and drops its shadow; release springs it to wherever
                // the state now puts it. Both listen with handledEventsToo (Button handles them).
                btn.AddHandler(PointerPressedEvent, (_, e) =>
                {
                    if (!e.GetCurrentPoint(btn).Properties.IsLeftButtonPressed) return;
                    captured.Pressed = true;
                    PaintNavCoin(captured);
                }, RoutingStrategies.Tunnel, handledEventsToo: true);
                btn.AddHandler(PointerReleasedEvent, (_, _) =>
                {
                    if (!captured.Pressed) return;
                    captured.Pressed = false;
                    captured.Hovered = btn.IsPointerOver;
                    PaintNavCoin(captured, spring: true);
                }, RoutingStrategies.Bubble, handledEventsToo: true);
                ToolTip.SetTip(btn, BuildNavRowToolTip(row));
                PaintNavRing(row);
                PaintNavCoin(row);
                _navSectionRows.Add(row);
            }
        }

        // ============================== the coin ==============================

        /// <summary>Builds the coin's parts around an authored row, in WPF's order: the outer
        /// hairline and the contact disc under the tile, the dish right over it (under the art),
        /// the socket shade, the lip and the inner shadow over the art and its wash, the specular
        /// crescent over the ring. Brushes come from Core DepthPalette (Depth.xaml as data).</summary>
        private static NavCoinParts BuildNavCoin(Panel face, Border tile, uint hue)
        {
            var coin = new NavCoinParts();
            T Centred<T>(T c) where T : Control
            {
                c.HorizontalAlignment = HorizontalAlignment.Center;
                c.VerticalAlignment = VerticalAlignment.Center;
                c.IsHitTestVisible = false;
                Grid.SetRow(c, Grid.GetRow(tile));
                return c;
            }

            coin.Disc = Centred(new Ellipse
            {
                Width = NavRailRules.CoinDiscWidth,
                Height = NavRailRules.CoinDiscHeight,
                Fill = NavPaint.Radial(NavRailRules.CoinDiscStops(hue)),
                RenderTransform = coin.DiscShift,
            });
            coin.Disc.VerticalAlignment = VerticalAlignment.Bottom;
            // Centred on the tile's foot; the shift pushes it down by the shadow length.
            coin.Disc.Margin = new Thickness(0, 0, 0, -NavRailRules.CoinDiscHeight / 2);
            coin.DiscShift.X = NavRailRules.CoinDiscRightPx;

            coin.Dish = Centred(new Border
            {
                Width = tile.Width,
                Height = tile.Height,
                CornerRadius = tile.CornerRadius,
                Background = NavPaint.Depth("DepthCoinDish"),
            });
            coin.Socket = Centred(new Border
            {
                Width = NavRailRules.CoinRimSize,
                Height = NavRailRules.CoinRimSize,
                CornerRadius = new CornerRadius(NavRailRules.CoinRimRadius),
                Background = NavPaint.Depth("DepthCoinSocket"),
                Opacity = 0,
            });
            coin.Rim = Centred(new Border
            {
                Width = NavRailRules.CoinRimSize,
                Height = NavRailRules.CoinRimSize,
                CornerRadius = new CornerRadius(NavRailRules.CoinRimRadius),
                BorderThickness = new Thickness(1),
                BorderBrush = NavPaint.Depth("DepthCoinRim"),
            });
            coin.Outer = Centred(new Border
            {
                Width = NavRailRules.CoinOuterSize,
                Height = NavRailRules.CoinOuterSize,
                CornerRadius = new CornerRadius(NavRailRules.CoinOuterRadius),
                BorderThickness = new Thickness(1),
                BorderBrush = NavPaint.Depth("DepthCoinOuterRim"),
            });
            coin.Inner = Centred(new Border
            {
                Width = NavRailRules.CoinInnerSize,
                Height = NavRailRules.CoinInnerSize,
                CornerRadius = new CornerRadius(NavRailRules.CoinInnerRadius),
                BorderThickness = new Thickness(NavRailRules.CoinInnerThickness),
                BorderBrush = NavPaint.Depth("DepthCoinInnerShadow"),
            });
            coin.Specular = Centred(new Path
            {
                Data = BuildNavCoinCrescent(),
                Width = NavRailRules.CoinRimSize,
                Height = NavRailRules.CoinRimSize,
                Stretch = Stretch.None,
                Fill = NavPaint.Depth("DepthCoinSpecular"),
            });

            var kids = face.Children;
            kids.Insert(kids.IndexOf(tile), coin.Disc);
            kids.Insert(kids.IndexOf(tile) + 1, coin.Dish);
            int over = -1;
            for (int i = 0; i < kids.Count; i++)
                if (kids[i] is Border b && (b.Tag as string) == NavTintTag) over = i;
            if (over < 0)
                for (int i = 0; i < kids.Count; i++)
                    if (kids[i] is Viewbox) over = i;
            int at = over < 0 ? kids.IndexOf(coin.Dish) + 1 : over + 1;
            kids.Insert(at, coin.Socket);
            kids.Insert(at + 1, coin.Rim);
            kids.Insert(at + 2, coin.Inner);
            kids.Insert(kids.IndexOf(coin.Disc) + 1, coin.Outer);
            int ringAt = -1;
            for (int i = 0; i < kids.Count; i++)
                if (kids[i] is Border b && (b.Tag as string) == NavRingTag) ringAt = i;
            kids.Insert(ringAt < 0 ? kids.IndexOf(coin.Inner) + 1 : ringAt + 1, coin.Specular);
            return coin;
        }

        /// <summary>The specular crescent: the sliver between the coin face and the face nudged
        /// down-right, kept only near the top-left corner.</summary>
        private static Geometry BuildNavCoinCrescent()
        {
            double s = NavRailRules.CoinRimSize, r = NavRailRules.CoinRimRadius;
            var face = new RectangleGeometry(new Rect(0, 0, s, s)) { RadiusX = r, RadiusY = r };
            var nudged = new RectangleGeometry(new Rect(NavRailRules.SpecularOffsetX, NavRailRules.SpecularOffsetY, s, s)) { RadiusX = r, RadiusY = r };
            var sliver = new CombinedGeometry(GeometryCombineMode.Exclude, face, nudged);
            var corner = new EllipseGeometry { Center = new Point(r * 0.8, r * 0.8), RadiusX = s * 0.48, RadiusY = s * 0.48 };
            return new CombinedGeometry(GeometryCombineMode.Intersect, sliver, corner);
        }

        /// <summary>
        /// The coin law in one place. Light swaps at once (a lit coin wears the pressed lip and the
        /// socket shade, an idle one the raised lip); motion eases: the face travels to
        /// NavRailRules.CoinTravel, the disc to CoinShadow and fades out when the coin sits down.
        /// A release springs past its target by DepthRules.ReleaseOvershootPx, then settles.
        /// </summary>
        private static void PaintNavCoin(NavSectionRow row, bool spring = false)
        {
            var coin = row.Coin;
            if (coin == null) return;
            try
            {
                bool seated = row.Pressed || row.Active;
                coin.Rim.BorderBrush = NavPaint.Depth(seated ? "DepthPressedBevel" : "DepthCoinRim");
                coin.Socket.Opacity = seated ? 1 : 0;

                double travel = NavRailRules.CoinTravel(row.Pressed, row.Active, row.Hovered);
                double shadow = NavRailRules.CoinShadow(row.Pressed, row.Active, row.Hovered);
                int full = row.Pressed ? DepthRules.PressMs : spring ? DepthRules.ReleaseMs : DepthRules.HoverMs;
                int ms = DepthRules.Ms(full, NavPaint.Level);

                if (ms <= 0)
                {
                    row.Lift.Transitions = null;
                    coin.DiscShift.Transitions = null;
                    coin.Disc.Transitions = null;
                    row.Lift.Y = travel;
                    if (shadow > 0) coin.DiscShift.Y = shadow;
                    coin.Disc.Opacity = shadow > 0 ? 1 : 0;
                    return;
                }
                var span = TimeSpan.FromMilliseconds(ms);
                double from = row.Lift.Y;
                Easing ease = spring && Math.Abs(from - travel) > 0.01
                    ? new NavSpringEasing(Math.Abs(NavRailRules.CoinOvershoot(from, travel) - travel) / Math.Abs(travel - from))
                    : new QuadraticEaseOut();
                row.Lift.Transitions = new Transitions { new DoubleTransition { Property = TranslateTransform.YProperty, Duration = span, Easing = ease } };
                coin.DiscShift.Transitions = new Transitions { new DoubleTransition { Property = TranslateTransform.YProperty, Duration = span, Easing = new QuadraticEaseOut() } };
                coin.Disc.Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = span } };
                row.Lift.Y = travel;
                if (shadow > 0) coin.DiscShift.Y = shadow;
                coin.Disc.Opacity = shadow > 0 ? 1 : 0;
            }
            catch (Exception ex) { Log.Debug("PaintNavCoin: {E}", ex.Message); }
        }

        /// <summary>WPF's release keyframes as one easing: ease out PAST the target (by
        /// <paramref name="overshoot"/> of the travel) at 60%, then ease in-out back onto it.</summary>
        private sealed class NavSpringEasing : Easing
        {
            private readonly double _over;
            public NavSpringEasing(double overshoot) => _over = Math.Max(0, overshoot);
            public override double Ease(double p)
            {
                if (p <= 0) return 0;
                if (p >= 1) return 1;
                if (p < 0.6)
                {
                    double t = p / 0.6;
                    return (1 + _over) * (1 - (1 - t) * (1 - t));
                }
                double u = (p - 0.6) / 0.4;
                double io = u < 0.5 ? 2 * u * u : 1 - Math.Pow(-2 * u + 2, 2) / 2;
                return (1 + _over) - _over * io;
            }
        }

        /// <summary>
        /// The rail's shadow on the page (NavRailShadow, DepthRules.RailShadowPx wide), tinted by
        /// the section hue. Called from PaintSectionWash.
        /// </summary>
        internal void PaintDepthRail(uint hue)
        {
            try
            {
                if (Named<Rectangle>("NavRailShadow") is { } shadow)
                    shadow.Fill = NavPaint.Horizontal(NavRailRules.RailShadowStops(hue));
            }
            catch (Exception ex) { Log.Debug("PaintDepthRail: {E}", ex.Message); }
        }

        /// <summary>The section name, the open count when a badge is up, and a greyed "Ctrl+2".
        /// Built on hover so a language switch never leaves a stale tooltip.</summary>
        private static object BuildNavRowToolTip(NavSectionRow row)
        {
            var panel = new StackPanel();
            var key = NavSections.Find(row.Section)?.LabelKey;
            var name = key == null ? row.Section : SafeNavLoc(key, row.Section);
            panel.Children.Add(new TextBlock { Text = name, FontWeight = FontWeight.SemiBold });
            int count = NavBadges.Get(row.Section);
            if (count > 0)
            {
                var line = SafeNavLoc("nav_badge_open", string.Empty);
                if (!string.IsNullOrEmpty(line))
                {
                    try { panel.Children.Add(new TextBlock { Text = string.Format(CultureInfo.CurrentCulture, line, count) }); }
                    catch (FormatException) { }
                }
            }
            int n = NavRailRules.ShortcutNumber(row.Section);
            if (n > 0) panel.Children.Add(new TextBlock { Text = "Ctrl+" + n, Opacity = 0.6, FontSize = 11 });
            return panel;
        }

        private static string SafeNavLoc(string key, string fallback)
        {
            try
            {
                var s = Loc.Get(key);
                return string.IsNullOrEmpty(s) || s == key ? fallback : s;
            }
            catch { return fallback; }
        }

        /// <summary>The rail's search pill. Same call as the Ctrl+K shortcut.</summary>
        private void BtnNavSearch_Click(object? sender, RoutedEventArgs e)
        {
            try { SettingsPaletteWindow.Toggle(this); }
            catch (Exception ex) { Log.Warning(ex, "BtnNavSearch_Click: palette toggle failed"); }
        }

        // ============================== navigation ==============================

        /// <summary>A row press: the section's last tab, else its default tab.</summary>
        private void NavDoor_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tag) return;
            OpenNavSection(NavRailRules.SectionForDoorTag(tag));
        }

        /// <summary>Opens a section the way its rail row does; logs and stays on an unknown key.</summary>
        internal void OpenNavSection(string section)
        {
            var tab = NavRailRules.TargetTab(section, NavLastTabJson);
            if (tab == null)
            {
                Log.Warning("OpenNavSection: no section {Section}", section);
                return;
            }
            ShowTab(tab);
        }

        private bool _sectionShortcutsRegistered;

        /// <summary>Ctrl+1..7 jump to the rail's sections. A window-level BUBBLE handler: it only
        /// fires while this window has keyboard focus, a child that handles the chord itself wins,
        /// and the panic key is a global hook no handler here can swallow.</summary>
        private void RegisterSectionShortcuts()
        {
            if (_sectionShortcutsRegistered) return;
            _sectionShortcutsRegistered = true;
            AddHandler(KeyDownEvent, OnSectionShortcutKeyDown, RoutingStrategies.Bubble);
        }

        private void OnSectionShortcutKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Handled || e.KeyModifiers != KeyModifiers.Control) return;
            int n = e.Key switch
            {
                Key.D1 => 1, Key.D2 => 2, Key.D3 => 3, Key.D4 => 4, Key.D5 => 5, Key.D6 => 6, Key.D7 => 7,
                _ => 0,
            };
            if (!TryOpenSectionShortcut(n)) return;
            e.Handled = true;
        }

        /// <summary>The Ctrl+N action itself (NavCheck drives it). False for a digit with no row.</summary>
        internal bool TryOpenSectionShortcut(int n)
        {
            var rails = NavRailRules.RailSections;
            if (n < 1 || n > rails.Count) return false;
            // A lockdown or a tutorial card holds the page on purpose (same rule as Back).
            if (LockdownActive || CoreTutorial.IsActive) return false;
            OpenNavSection(rails[n - 1].Key);
            return true;
        }

        /// <summary>
        /// Lights the row that owns <paramref name="tabKey"/>: filled tile, 4px bar in the section
        /// hue, hue ring, halo, spur, the coin seated. Called from ShowTab on every navigation, so
        /// the lit row follows deep links, the palette and the strip. A key no section owns leaves
        /// the current row lit.
        /// </summary>
        internal void RefreshSectionRail(string? tabKey)
        {
            if (!_navRailReady) return;
            var section = NavSections.SectionForTab(CanonicalTabKey(tabKey ?? string.Empty));
            if (section == null) return;
            _litSection = section;
            foreach (var row in _navSectionRows)
            {
                bool on = row.Section == section;
                if (row.Painted && on == row.Active) continue;
                row.Active = on;
                row.Painted = true;
                PaintNavRowActive(row);
            }
            // 7.1.5: being on the section is seeing its count; the badge dims (NavBadges.IsFresh).
            try { NavBadges.MarkSeen(section); } catch { }
        }

        private string? _litSection;

        /// <summary>The lit section's rail Tag ("appsettings" for the gear), or null before the
        /// first navigation. The old door accordion's question, answered by the lit row.</summary>
        internal string? ExpandedDoor => _litSection == null ? null : NavRailRules.DoorTagForSection(_litSection);

        /// <summary>The section whose row is lit (NavCheck).</summary>
        internal string? LitNavSection => _litSection;

        private static void PaintNavRowActive(NavSectionRow row)
        {
            try
            {
                row.Button.Classes.Set("navActive", row.Active);
                PaintNavSpur(row, animate: true);
                PaintNavTint(row);
                PaintNavRing(row);
                PaintNavRowLabel(row);
                if (row.Glow != null)
                {
                    double to = row.Active ? NavGlowActive : 0;
                    int ms = NavRailRules.Ms(NavGlowFadeMs, NavPaint.Level);
                    row.Glow.Transitions = ms <= 0 ? null : new Transitions
                    {
                        new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(ms) },
                    };
                    row.Glow.Opacity = to;
                }
                PaintNavCoin(row);
            }
            catch (Exception ex) { Log.Debug("PaintNavRowActive: {E}", ex.Message); }
        }

        /// <summary>The section name in small caps (WPF NavCapsConverter: upper-cased in the UI
        /// culture), the hue on the lit row, TextLight elsewhere.</summary>
        private static void PaintNavRowLabel(NavSectionRow row)
        {
            if (row.Label == null) return;
            var key = NavSections.Find(row.Section)?.LabelKey;
            if (key != null)
            {
                var text = SafeNavLoc(key, row.Label.Text ?? row.Section);
                row.Label.Text = text.ToUpper(CultureInfo.CurrentUICulture);
            }
            if (row.Active) row.Label.Foreground = NavPaint.Solid(row.Hue);
            else row.Label.ClearValue(TextBlock.ForegroundProperty);
        }

        private void OnNavRailLanguageChanged(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var row in _navSectionRows) PaintNavRowLabel(row);
                Named<SectionTabStrip>("SectionStrip")?.RefreshLabels();
                SyncSectionChrome(CurrentTab);
            });
        }

        /// <summary>The medallion ring: the section hue made vivid (NavRailRules.Vivid), a diagonal
        /// bevel lit from the top-left at 80% (hover 95%), 3 px; lit = the bevel flipped (a socket
        /// lip), solid, 3.5 px. Painted on the "navring" Border over the art.</summary>
        private static void PaintNavRing(NavSectionRow row)
        {
            var target = row.Ring ?? row.Tile;
            if (target == null) return;
            if (row.Tile != null) row.Tile.Opacity = 1.0;
            target.BorderBrush = NavPaint.Diagonal(NavRailRules.RingStops(row.Hue, row.Active, row.Hovered));
            target.BorderThickness = new Thickness(NavRailRules.RingThickness(row.Active));
        }

        /// <summary>The hue wash over the medallion art: a touch louder on the lit row.</summary>
        private static void PaintNavTint(NavSectionRow row)
        {
            if (row.Tint == null) return;
            row.Tint.Background = NavPaint.Solid(NavRailRules.WithAlpha(row.Hue, NavRailRules.ArtTintAlpha(row.Active)));
        }

        /// <summary>
        /// The spur (template part NavEdgeSpur): a short hue tab from the window edge into the lit
        /// medallion's ring (the edge at 90%, the ring end at 60%), faded in over the halo's timing
        /// (160 ms, Reduced 80, Off instant). Hidden on every other row.
        /// </summary>
        private static void PaintNavSpur(NavSectionRow row, bool animate)
        {
            var spur = row.Spur;
            if (spur == null) return;
            if (!row.Active)
            {
                spur.Transitions = null;
                spur.Opacity = 0;
                spur.IsVisible = false;
                return;
            }
            spur.Background = NavPaint.Horizontal(new[]
            {
                (NavRailRules.WithAlpha(row.Hue, NavRailRules.SpurEdgeAlpha), 0.0),
                (NavRailRules.WithAlpha(row.Hue, NavRailRules.SpurRingAlpha), 1.0),
            });
            spur.IsVisible = true;
            int ms = animate ? NavRailRules.Ms(NavGlowFadeMs, NavPaint.Level) : 0;
            spur.Transitions = ms <= 0 ? null : new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(ms) },
            };
            spur.Opacity = 1;
        }

        // ============================== badges ==============================

        private void OnNavBadgeChanged(string section, int count)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => OnNavBadgeChanged(section, count));
                return;
            }
            try
            {
                foreach (var row in _navSectionRows)
                {
                    if (row.Section != section) continue;
                    // A count that rises while the player is looking at its section is seen.
                    if (row.Active && IsVisible && WindowState != WindowState.Minimized) NavBadges.MarkSeen(section);
                    PaintNavBadge(row, NavBadges.Get(section));
                }
            }
            catch (Exception ex) { Log.Debug("OnNavBadgeChanged: {E}", ex.Message); }
        }

        /// <summary>
        /// 7.1.5 (tier-2: a red "7" read as seven unread messages): the pill wears the SECTION hue
        /// (never the mod accent) with contrast-picked ink, and once the player has seen the count
        /// it dims and drops its shadow. It brightens again only when the count rises past what
        /// they saw (NavBadges.IsFresh).
        /// </summary>
        private static void PaintNavBadge(NavSectionRow row, int count)
        {
            if (row.Badge == null) return;
            var text = NavRailRules.BadgeText(count);
            if (row.BadgeText != null) row.BadgeText.Text = text ?? string.Empty;
            row.Badge.IsVisible = text != null;
            if (text == null) return;
            row.Badge.Background = NavPaint.Solid(row.Hue);
            if (row.BadgeText != null) row.BadgeText.Foreground = NavPaint.Solid(NavStripRules.ActiveTextOn(row.Hue));
            bool fresh = NavBadges.IsFresh(count, NavBadges.Seen(row.Section));
            row.Badge.Opacity = NavRailRules.BadgeOpacity(fresh);
            if (fresh) row.Badge.ClearValue(Border.BoxShadowProperty);
            else row.Badge.BoxShadow = default;
        }

        /// <summary>NavCheck seam: a row's badge text and opacity ("" / 0 when hidden).</summary>
        internal (string Text, double Opacity) NavBadgeFor(string section)
        {
            var row = _navSectionRows.FirstOrDefault(r => r.Section == section);
            if (row?.Badge == null || !row.Badge.IsVisible) return (string.Empty, 0);
            return (row.BadgeText?.Text ?? string.Empty, row.Badge.Opacity);
        }

        /// <summary>NavCheck seam: the rail's section order as painted.</summary>
        internal IReadOnlyList<string> NavRailSectionOrder => _navSectionRows.Select(r => r.Section).ToArray();

        // ============================== locks ==============================

        /// <summary>
        /// The tier signs on the section strip's pills (7.1.5 retired the rail's gold stars with
        /// the door rows). Kept under the old name because App.axaml.cs repaints it on every tier,
        /// daily-free and intake-pass change.
        /// </summary>
        internal void RefreshNavPremiumTags()
        {
            try { Named<SectionTabStrip>("SectionStrip")?.RefreshLocks(); }
            catch (Exception ex) { Log.Debug("RefreshNavPremiumTags: {E}", ex.Message); }
        }

        /// <summary>WPF 7.1.5 keeps this on the rail (MainWindow.NavRail.cs): is a sold feature's
        /// door shut to this account, over Core's roster and the entitlement seam. Fails to NOT
        /// LOCKED on anything unexpected, as WPF does.</summary>
        private static bool IsNavEntryLocked(string exclusiveKey)
        {
            try
            {
                var feature = Models.ExclusiveFeature.All.FirstOrDefault(f => f.Key == exclusiveKey);
                if (feature == null) return false;
                var state = feature.GateState();
                return state == Models.ExclusiveGateState.Locked && !feature.IsFreeToday(state);
            }
            catch { return false; }
        }

        /// <summary>The two canonical gates for the strip's tier signs; null while no account
        /// provider is wired (render, nav-check), which reads as locked.</summary>
        private static (bool? Premium, bool? Lab) NavAccess() =>
            (CoreAccount.HasPremiumAccessProvider == null ? null : CoreAccount.HasPremiumAccess,
             CoreAccount.HasLabAccessProvider == null ? null : CoreAccount.HasLabAccess);

        // ============================== kept entry points ==============================
        // The flyout is gone, so nothing holds the rail open. Kept because other files and the
        // click-through driver still name them; WPF 7.1.5 keeps the same no-ops.

        /// <summary>No-op: the rail no longer opens on hover.</summary>
        internal void HoldNavRailOpen(object owner) { }

        /// <summary>No-op: the rail no longer opens on hover.</summary>
        internal void ReleaseNavRailOpen(object owner) { }

        /// <summary>Always true: the rail is always labelled now.</summary>
        internal bool NavRailExpanded => true;

        /// <summary>True once the rail's setup ran.</summary>
        internal bool NavRailHooked => _navRailReady;
    }
}
