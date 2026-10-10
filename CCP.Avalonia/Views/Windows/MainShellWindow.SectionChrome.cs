// PORTED from ConditioningControlPanel/MainWindow/MainWindow.SectionChrome.cs (nav rework
// 2026-10-06): the section page chrome ShowTab syncs on every navigation - the pill strip, the
// breadcrumb and the last-tab memory - plus the pill routing (OnSectionPillChosen,
// OpenLibraryLauncher).
//
// The section wash and ink (PaintSectionWash, PaintSectionInk: 25a4d456b, 4f22d0478) are here
// (sync6-nav-polish-b).
//
// The section edge (PaintSectionEdge, WPF MainWindow.SectionEdge.cs, 8f4bb7814) is here too: the
// frame line, the 28 px glow band and the lift in the hue.
//
// ponytail: not yet here (lane sync6-nav-polish-c): the edge lift's lap round the frame at Full
// motion (it sits at the top centre, WPF's Reduced), the fog and ember strips the band thins for
// (EdgeParticles, AmbientFxCanvas.EdgeFog), the depth painters the wash feeds
// (PaintDepthRail/Hud/Home/Quests), the window-title crumb (UpdateNavTitle), the "Moved" redirects and their note,
// GlowNavTarget and the landing glow.

using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private bool _sectionStripWired;

        internal SectionTabStrip? PageStrip => Named<SectionTabStrip>("SectionStrip");

        /// <summary>The tab a section returns to (its remembered tab, else its default). WPF :97.</summary>
        internal static string NavLastTabFor(string section) =>
            NavStripTable.LastTabFor(CoreSettings.Current.NavLastTabBySection, section)
            ?? NavSections.DefaultTab(section) ?? "settings";

        /// <summary>Sync the strip, breadcrumb and last-tab memory to the tab on screen. WPF :102.</summary>
        private void SyncSectionChrome(string tab)
        {
            try
            {
                WireSectionStrip();
                var section = NavSections.SectionForTab(tab);
                PageStrip?.Show(section, tab, section == NavSections.Settings ? CurrentSettingsSectionLabel() : null);
                PaintSectionWash(section);

                // Last tab per section. Old keys are not remembered (their new home is).
                var memo = tab == "lab" ? "play" : tab;
                var s = CoreSettings.Current;
                if (section != null && !NavSections.Redirects.ContainsKey(memo))
                {
                    var json = NavStripTable.WithLastTab(s.NavLastTabBySection, section, memo);
                    if (!string.Equals(json, s.NavLastTabBySection, StringComparison.Ordinal))
                    {
                        s.NavLastTabBySection = json;
                        CoreSettings.Save();
                    }
                }
            }
            catch (Exception ex) { Serilog.Log.Debug("SyncSectionChrome({Tab}) failed: {E}", tab, ex.Message); }
        }

        /// <summary>The page wash's top-left alpha (about 14%) and its hue line's (about 35%). WPF :138.</summary>
        internal const byte SectionWashAlpha = 0x24, SectionWashLineAlpha = 0x59;

        /// <summary>The wash's colour change, 250 ms (Reduced halves it, Off is instant). WPF :141.</summary>
        internal const int SectionWashMs = 250;

        private string? _washSection;

        /// <summary>WPF PaintSectionWash (:147): tints the page ground in the section hue. A tab no
        /// section owns keeps the current wash.</summary>
        private void PaintSectionWash(string? section)
        {
            try
            {
                if (section == null || section == _washSection) return;
                _washSection = section;
                var hue = NavStripRules.Accent(section);
                PaintSectionInk(section);
                var level = CoreSettings.Current.MotionLevel;
                int ms = level switch { Models.MotionLevel.Off => 0, Models.MotionLevel.Reduced => SectionWashMs / 2, _ => SectionWashMs };
                PaintFill(Named<Border>("SectionWashFill"), NavStripRules.WithAlpha(hue, SectionWashAlpha / 255.0), ms);
                PaintFill(Named<Border>("SectionWashLine"), NavStripRules.WithAlpha(hue, SectionWashLineAlpha / 255.0), ms);
                PaintSectionEdge(hue, level, ms);
            }
            catch (Exception ex) { Serilog.Log.Debug("PaintSectionWash failed: {E}", ex.Message); }
        }

        /// <summary>WPF PaintSectionEdge (SectionEdge.cs :105) with no fog running (the full band):
        /// the 3 px line at 0xE6, each band's edge alpha balanced for brightness
        /// (SectionEdgeRules.GlowAlpha), the lift the hue toward white, hidden at Motion Off.</summary>
        private void PaintSectionEdge(global::Avalonia.Media.Color hue, Models.MotionLevel level, int ms)
        {
            if (Named<Border>("GlassWindowEdge") is { } line)
            {
                Fade(line, Border.BorderBrushProperty, ms);
                line.BorderBrush = new global::Avalonia.Media.SolidColorBrush(NavStripRules.WithAlpha(hue, SectionEdgeLineAlpha / 255.0));
            }
            if (Named<Panel>("SectionEdgeGlow") is { } glow)
                foreach (var band in glow.Children.OfType<Border>())
                    PaintFill(band, NavStripRules.WithAlpha(hue, NavStripRules.EdgeGlowAlpha(hue) / 255.0), ms);
            if (Named<Border>("SectionEdgeLiftTop") is { } lift)
            {
                lift.IsVisible = level != Models.MotionLevel.Off;
                PaintFill(lift, NavStripRules.Mix(hue, global::Avalonia.Media.Colors.White, SectionEdgeLiftWhite), ms);
            }
        }

        /// <summary>A solid background, crossfaded over <paramref name="ms"/> (0 = at once, WPF's
        /// QuadraticEase out).</summary>
        private static void PaintFill(Border? b, global::Avalonia.Media.Color to, int ms)
        {
            if (b == null) return;
            Fade(b, Border.BackgroundProperty, ms);
            b.Background = new global::Avalonia.Media.SolidColorBrush(to);
        }

        private static void Fade(Border b, global::Avalonia.AvaloniaProperty property, int ms) =>
            b.Transitions = ms <= 0 ? null : new global::Avalonia.Animation.Transitions
            {
                new global::Avalonia.Animation.BrushTransition
                {
                    Property = property, Duration = TimeSpan.FromMilliseconds(ms),
                    Easing = new global::Avalonia.Animation.Easings.QuadraticEaseOut(),
                },
            };

        /// <summary>WPF SectionEdgeRules: the line's alpha (about 90%) and the lift's pull to white.</summary>
        internal const byte SectionEdgeLineAlpha = 0xE6;
        internal const double SectionEdgeLiftWhite = 0.35;

        /// <summary>WPF PaintSectionInk (:192): the four section Color resources and their brushes
        /// (the SectionInkBrush family consumers bind with DynamicResource), swapped at once.</summary>
        internal static void PaintSectionInk(string? section)
        {
            var res = global::Avalonia.Application.Current?.Resources;
            if (res == null) return;
            Set("SectionInk", NavStripRules.Ink(section));
            Set("SectionTint", NavStripRules.Tint(section));
            Set("SectionRule", NavStripRules.Rule(section));
            Set("SectionOutline", NavStripRules.Outline(section));

            void Set(string key, global::Avalonia.Media.Color c)
            {
                res[key] = c;
                res[key + "Brush"] = new global::Avalonia.Media.SolidColorBrush(c);
            }
        }

        private void WireSectionStrip()
        {
            if (_sectionStripWired || PageStrip is not { } strip) return;
            _sectionStripWired = true;
            // WPF NavStripRules.PillLocked: a paying account sees no tier sign on pages it owns.
            strip.PillLocked = tier => tier > 0 && (tier == 1 ? !CoreAccount.HasPremiumAccess : !CoreAccount.HasLabAccess);
            strip.CanOpen = CanOpenNavTab;
            strip.SettingsPageLabel = CurrentSettingsSectionLabel;
            strip.TabRequested += OnSectionPillChosen;
            // WPF :221 (ea2d4cfca): right-click on a pill pins or unpins it to the Home Favourites
            // column, as the rail rows did. Only pills with a Ctrl+K row are pinnable.
            strip.PillCreated += (tab, pill) =>
            {
                if (PinIdForPill(tab) is { } id) AttachPinMenu(pill, id);
            };
            strip.SectionRequested += section =>
                ShowTab(section == NavSections.Settings ? "appsettings" : NavLastTabFor(section));
            // Settings keeps its own left pill column; the breadcrumb follows it (WPF :233).
            AppSettingsPage?.AddHandler(ToggleButton.IsCheckedChangedEvent, (_, e) =>
            {
                if (e.Source is RadioButton { IsChecked: true } rb && rb.Name?.StartsWith("SectionPill", StringComparison.Ordinal) == true
                    && CurrentTab == "appsettings")
                    strip.Show(NavSections.Settings, "appsettings", CurrentSettingsSectionLabel());
            }, RoutingStrategies.Bubble);
        }

        /// <summary>Pills this head can open. A page another lane has not ported yet (Friends,
        /// Leash, the Companion pages, Folders' scroll...) is not drawn rather than drawn dead.</summary>
        private static bool CanOpenNavTab(NavTab tab) => tab.Kind == NavTabKind.Launcher
            ? tab.Key is "mods" or "catalogue" or "phrases" or "medialog"
            : TabPanels.ContainsKey(tab.Key);

        /// <summary>WPF OnSectionPillChosen (:246): launchers through their old handlers, pages and
        /// zones through ShowTab.</summary>
        private void OnSectionPillChosen(NavTab tab)
        {
            if (tab.Kind == NavTabKind.Launcher) { OpenLibraryLauncher(tab.Key); return; }
            var before = CurrentTab;
            ShowTab(tab.Key);
            // WPF :263: the Games pill scrolls to the top from anywhere (the active pill while
            // scrolled down included); from another zone ShowTab already did.
            if (tab.Key == "play" && before is not ("playsessions" or "playeyes"))
                Named<Views.Tabs.PlayTabView>("PlayTab")?.ScrollToZone("games");
        }

        /// <summary>WPF OpenLibraryLauncher (:274): the handlers the Library's rail rows call.</summary>
        internal bool OpenLibraryLauncher(string key)
        {
            var e = new RoutedEventArgs();
            switch (key)
            {
                case "mods": BtnManageMods_Click(this, e); return true;
                case "catalogue": BtnCatalogue_Click(this, e); return true;
                case "phrases": BtnManagePhrases_Click(this, e); return true;
                case "medialog": BtnNavMediaLog_Click(this, e); return true;
                default: return false;
            }
        }

        /// <summary>WPF PinIdForPill (:290): the Favourites destination a strip pill pins as, or null
        /// when it has no Ctrl+K row.</summary>
        internal static string? PinIdForPill(NavTab tab)
        {
            var id = tab.Kind == NavTabKind.Launcher ? "launch." + tab.Key
                   : tab.Key == "justdrop" ? "door.justdrop"
                   : "tab." + tab.Key;
            return FavoritesRailRule.IsDestination(id) && SettingsPaletteIndex.ById(id) != null ? id : null;
        }

        /// <summary>
        /// Ctrl+K from every page (WPF EnsurePaletteShortcut, d858d6108). WPF reads the raw
        /// keystroke before routing so no focused control can swallow it; the window's Tunnel pass
        /// is that point here, and handledEventsToo keeps it whatever a child marks. Ctrl alone
        /// (Ctrl+Alt+K is the camera). A held chord toggles once, as WPF's lParam bit-30 check
        /// does (SettingsPaletteWindow.FirstChordPress).
        /// </summary>
        private void InitializePaletteShortcut()
        {
            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key != global::Avalonia.Input.Key.K || e.KeyModifiers != global::Avalonia.Input.KeyModifiers.Control) return;
                e.Handled = true;
                if (SettingsPaletteWindow.FirstChordPress()) SettingsPaletteWindow.Toggle(this);
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(KeyUpEvent, (_, e) =>
            {
                if (e.Key == global::Avalonia.Input.Key.K) SettingsPaletteWindow.ChordReleased();
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        /// <summary>The label of the Settings section whose pill is checked (null if none). WPF :340.</summary>
        private string? CurrentSettingsSectionLabel()
        {
            var key = AppSettingsPage?.CheckedSectionKey;
            if (key is null) return null;
            foreach (var t in NavSections.Find(NavSections.Settings)?.Tabs ?? Array.Empty<NavTab>())
                if (t.Key == key) return Loc.Get(t.LabelKey);
            return null;
        }
    }
}
