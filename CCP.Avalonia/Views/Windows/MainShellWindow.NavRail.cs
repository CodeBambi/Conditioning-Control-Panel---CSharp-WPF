// PORTED from ConditioningControlPanel/MainWindow/MainWindow.NavRail.cs (the section rail, nav
// rework 2026-10-06: cd426fe36, 8b67e45d2, ac59c3344). An always-labelled rail of seven sections and
// a Settings gear inside the 96px column; no flyout, no hold latch, no door accordion. A row opens its
// section's last tab; the lit row follows every navigation (RefreshSectionRail, from ShowTab).
//
// ponytail: not yet here (sync6-nav-rail-c): the coin depth (BuildNavCoin, press travel, tilt,
// specular), the edge spur, the hue-bevel ring gradient (a flat hue ring stands in), NavBadges
// counts, the rich row tooltip, the possession reroute seam, mod door art (ApplyDoorArt) and the
// rail shadow (PaintDepthRail).

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>One section row's parts, found once by type/Tag inside the row's Grid.</summary>
        internal sealed class NavSectionRow
        {
            internal string Section = "";
            internal Button Button = null!;
            internal Ellipse? Glow;
            internal Border? Tile, Ring, Shade;
            internal TextBlock? Label;
            internal Color Hue;
            internal bool Active, Painted;
        }

        // WPF NavRailRules: row tint 0x33, tile tint 0x40, ring idle/hover/active alpha, lit halo.
        private const byte NavFillAlpha = 0x33, NavTileTintAlpha = 0x40;
        private const byte NavRingIdleAlpha = 0xCC, NavRingHoverAlpha = 0xF2;
        private const double NavGlowActive = 0.55;

        private readonly List<NavSectionRow> _navSectionRows = new();
        private bool _navRailReady;

        internal IReadOnlyList<NavSectionRow> NavSectionRowsForTests => _navSectionRows;

        /// <summary>Every rail button, top to bottom, gear last (WPF NavSectionButtons).</summary>
        private static readonly string[] NavSectionButtonNames =
            { "DoorHome", "DoorStudio", "DoorCompanion", "DoorPlay", "DoorSocial", "DoorYou", "DoorLibrary", "DoorSettings" };

        /// <summary>WPF InitializeNavRail (:110). Called from the constructor after XAML load; a rail
        /// that fails here stays as authored: rows that navigate, no hue, no lit row.</summary>
        internal void InitializeNavRail()
        {
            try { RefreshNavPremiumTags(); }   // repaints the favorites chips (MainShellWindow.NavPremiumTags.cs)
            catch (Exception ex) { Log.Debug("RefreshNavPremiumTags: {E}", ex.Message); }
            try
            {
                if (_navRailReady) return;
                CacheNavSectionRows();
                RegisterSectionShortcuts();
                _navRailReady = true;
                RefreshSectionRail(CurrentTab);
            }
            catch (Exception ex) { Log.Warning(ex, "InitializeNavRail failed; rail stays as authored"); }
        }

        private static readonly IValueConverter NavCaps =
            new FuncValueConverter<object?, string?>(v => (v as string)?.ToUpper(CultureInfo.CurrentUICulture));

        /// <summary>WPF CacheNavSectionRows (:139): hue from the one table (NavStripTable.AccentRgb),
        /// the label bound upper-cased to the section's own key (WPF NavCapsConverter).</summary>
        private void CacheNavSectionRows()
        {
            _navSectionRows.Clear();
            foreach (var name in NavSectionButtonNames)
            {
                if (Named<Button>(name) is not { Tag: string tag } btn) continue;
                var section = tag == "appsettings" ? NavSections.Settings : tag;
                var rgb = NavStripTable.AccentRgb(section);
                var row = new NavSectionRow
                {
                    Section = section,
                    Button = btn,
                    Hue = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb),
                };
                btn.Background = new SolidColorBrush(WithAlpha(row.Hue, NavFillAlpha));
                btn.BorderBrush = new SolidColorBrush(row.Hue);
                if (btn.Content is Panel grid)
                {
                    foreach (var child in grid.Children)
                    {
                        switch (child)
                        {
                            case Ellipse e: row.Glow = e; break;
                            case Border b when (b.Tag as string) == "navring": row.Ring = b; break;
                            case Border b when (b.Tag as string) == "navart": break;
                            case Border b: row.Tile ??= b; break;
                            case TextBlock t: row.Label = t; break;
                        }
                    }
                }
                row.Shade = Named<Border>("Shade" + name);
                if (row.Glow != null)
                    row.Glow.Fill = new RadialGradientBrush
                    {
                        GradientStops = { new GradientStop(WithAlpha(row.Hue, 0x99), 0), new GradientStop(WithAlpha(row.Hue, 0), 1) },
                    };
                if (row.Tile != null) row.Tile.Background = new SolidColorBrush(WithAlpha(row.Hue, NavTileTintAlpha));
                if (row.Label != null && NavSections.Find(section) is { } s)
                    row.Label.Bind(TextBlock.TextProperty, new Binding($"[{s.LabelKey}]")
                    {
                        Source = LocalizationManager.Instance, Mode = BindingMode.OneWay, Converter = NavCaps,
                    });
                var captured = row;
                btn.PointerEntered += (_, _) => PaintNavRing(captured, hover: true);
                btn.PointerExited += (_, _) => PaintNavRing(captured, hover: false);
                PaintNavRowActive(row);
                _navSectionRows.Add(row);
            }
        }

        private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        /// <summary>A row press: the section's last tab, else its default (WPF NavDoor_Click :568).</summary>
        private void NavDoor_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag }) return;
            OpenNavSection(tag == "appsettings" ? NavSections.Settings : tag);
        }

        /// <summary>WPF OpenNavSection (:575): opens a section the way its rail row does; logs and
        /// stays on an unknown key.</summary>
        internal void OpenNavSection(string section)
        {
            if (NavSections.Find(section) is null)
            {
                Log.Warning("OpenNavSection: no section {Section}", section);
                return;
            }
            ShowTab(section == NavSections.Settings ? "appsettings" : NavLastTabFor(section));
        }

        /// <summary>WPF RegisterSectionShortcuts (:589): Ctrl+1..7 jump to the rail's sections.
        /// Window KeyBindings, so they fire only while this window has focus.</summary>
        private void RegisterSectionShortcuts()
        {
            int n = 0;
            foreach (var s in NavSections.Order)
            {
                if (s.Key == NavSections.Settings || ++n > 7) continue;
                var section = s.Key;
                KeyBindings.Add(new KeyBinding
                {
                    Gesture = new KeyGesture(Key.D0 + n, KeyModifiers.Control),
                    Command = new NavSectionCommand(() => OpenNavSection(section)),
                });
            }
        }

        private sealed class NavSectionCommand(Action run) : System.Windows.Input.ICommand
        {
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => run();
        }

        /// <summary>
        /// WPF RefreshSectionRail (:608): lights the row that owns <paramref name="tabKey"/> - filled
        /// row, 4px bar and label in the section hue, full ring, halo. Called from ShowTab on every
        /// navigation, so the lit row follows deep links, the palette and the strip. A key no section
        /// owns leaves the current row lit. Repaints only rows whose state changed (P07).
        /// </summary>
        internal void RefreshSectionRail(string? tabKey)
        {
            if (!_navRailReady) return;
            var section = NavSections.SectionForTab(tabKey == "lab" ? "play" : tabKey);
            if (section == null) return;
            foreach (var row in _navSectionRows)
            {
                bool on = row.Section == section;
                if (row.Painted && on == row.Active) continue;
                row.Active = on;
                PaintNavRowActive(row);
            }
        }

        private static void PaintNavRowActive(NavSectionRow row)
        {
            row.Painted = true;
            row.Button.Classes.Set("active", row.Active);
            if (row.Shade != null) row.Shade.Opacity = row.Active ? 0 : 1;
            if (row.Label != null)
            {
                if (row.Active) row.Label.Foreground = new SolidColorBrush(row.Hue);
                else row.Label.ClearValue(TextBlock.ForegroundProperty);
            }
            if (row.Glow != null) row.Glow.Opacity = row.Active ? NavGlowActive : 0;
            PaintNavRing(row, hover: row.Button.IsPointerOver);
        }

        /// <summary>WPF PaintNavRingParts (:687), flat: the hue at 0xCC idle, 0xF2 hover, solid and
        /// 3.5px lit.</summary>
        private static void PaintNavRing(NavSectionRow row, bool hover)
        {
            if (row.Ring is not { } ring) return;
            byte a = row.Active ? (byte)0xFF : hover ? NavRingHoverAlpha : NavRingIdleAlpha;
            ring.BorderBrush = new SolidColorBrush(WithAlpha(row.Hue, a));
            ring.BorderThickness = new Thickness(row.Active ? 3.5 : 3);
        }

        /// <summary>The rail's search pill, one line as in WPF (:559). Toggle refuses during Lockdown.</summary>
        private void BtnNavSearch_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => SettingsPaletteWindow.Toggle(this);
    }
}
