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
            internal Border? Badge;
            internal TextBlock? BadgeText;
            internal TextBlock? Label;
            internal SolidColorBrush? Hue;
            internal ScaleTransform Press = new(1, 1);
            internal bool Active;
            internal bool Painted;
        }

        private readonly List<NavSectionRow> _navSectionRows = new();
        private bool _navRailReady;

        private const double NavTileIdleOpacity = 0.85;
        private const double NavGlowActive = 0.50;
        private const int NavGlowFadeMs = 160;
        private const int NavPressMs = 80;
        private const double NavPressScale = 0.97;
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
                    Hue = btn.BorderBrush as SolidColorBrush,
                };
                if (btn.Content is Panel grid)
                {
                    foreach (var child in grid.Children.OfType<FrameworkElement>())
                    {
                        switch (child)
                        {
                            case Ellipse e: row.Glow = e; break;
                            case Border b when (b.Tag as string) == NavBadgeTag:
                                row.Badge = b; row.BadgeText = b.Child as TextBlock; break;
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
                HookNavPress(row);
                var captured = row;
                btn.ToolTipOpening += (_, __) => btn.ToolTip = BuildNavRowToolTip(captured);
                _navSectionRows.Add(row);
            }
        }

        /// <summary>Pressed = scale 0.97 for 80ms (Reduced 40ms, Off instant). The transform sits
        /// on the Button, never on the icon, so ChromeFx's hover nudge on the Viewbox keeps its
        /// own RenderTransform.</summary>
        private static void HookNavPress(NavSectionRow row)
        {
            var btn = row.Button;
            btn.RenderTransform = row.Press;
            void To(double v)
            {
                int ms = NavRailRules.Ms(NavPressMs, MotionFx.Level);
                if (ms <= 0)
                {
                    row.Press.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    row.Press.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    row.Press.ScaleX = v;
                    row.Press.ScaleY = v;
                    return;
                }
                var a = new DoubleAnimation(v, TimeSpan.FromMilliseconds(ms))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                row.Press.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                row.Press.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            btn.PreviewMouseLeftButtonDown += (_, __) => To(NavPressScale);
            btn.PreviewMouseLeftButtonUp += (_, __) => To(1);
            btn.MouseLeave += (_, __) => To(1);
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
        /// Lights the row that owns <paramref name="tabKey"/>: filled tile, 3px bar in the
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

                // A pale medallion carries a shade (x:Name "Shade" + door name) so it idles at its
                // neighbours' weight; lit, it shows its art in full.
                if (btn.FindName("Shade" + btn.Name) is UIElement shade)
                    shade.Opacity = row.Active ? 0 : 1;

                if (row.Tile != null)
                {
                    row.Tile.Opacity = row.Active ? 1.0 : NavTileIdleOpacity;
                    if (row.Active && row.Hue != null) row.Tile.BorderBrush = row.Hue;
                    else row.Tile.ClearValue(Border.BorderBrushProperty);
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
            }
            catch (Exception ex) { App.Logger?.Debug("PaintNavRowActive: {E}", ex.Message); }
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
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0xC0, c.R, c.G, c.B), 0.0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, c.R, c.G, c.B), 0.42));
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
