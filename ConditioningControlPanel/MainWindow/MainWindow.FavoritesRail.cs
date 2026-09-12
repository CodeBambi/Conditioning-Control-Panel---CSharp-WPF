using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The gated features the dashboard used to surface as quick-toggle chips. The rail is gone
    /// (2026-09-11); the enum stays because Remember (MainWindow.Remember.cs) and the EMI desk
    /// targets (Services/EmiDesk/EmiTargets.cs) still name features by it.
    /// </summary>
    public enum PremiumFeature { Takeover, Awareness, Haptics, Lockdown, Blink, Remote, Voice, GradedIntake, Fyp }

    /// <summary>
    /// The dashboard's FAVORITES + RECENT rail (column 0 of the Home tab), which replaced the
    /// premium quick-toggle rail on the owner's call from the Discord "New UI Feedback" thread,
    /// 2026-09-11. The toggles the old chips carried did not vanish: every one of those features
    /// is still switched on from its own card or page. What the column does now is remember
    /// where the user goes.
    ///
    /// <para>A chip is a Ctrl+K palette row (<see cref="SettingsPaletteIndex"/>): same id, same
    /// label, same navigation. The rail keeps no registry of its own, so a door that moves in the
    /// palette moves here for free, and a row the server has withheld
    /// (<see cref="SettingsPaletteEntry.Available"/>) never renders. List rules live in
    /// <see cref="FavoritesRailRule"/>; the lists live in AppSettings (RailFavorites,
    /// RailRecent).</para>
    ///
    /// <para>The FACE is the one thing the chip does not take from the palette row. Since
    /// 2026-09-12 (owner: "use the images for the different features") a chip wears the
    /// destination's own picture - the feature illustration the mosaic and the Play wall use, or
    /// the door's nav medallion - and the palette glyph only when nothing resolves. Which picture
    /// is <see cref="FavoritesRailArt"/>; the crop is the railChip surface in
    /// <see cref="ModArtFramingRegistry"/>, so a .ccpmod that re-skins features/flash.png
    /// re-skins the chip and is framed by its own author rather than by our rect.</para>
    ///
    /// <para>Locked destinations keep their chip and wear the padlock; the click goes where a
    /// rail click would and the page raises its own upsell, the same "navigation is never
    /// tier-gated" rule the old chips followed. The lock truth is <c>IsNavEntryLocked</c>
    /// (MainWindow.NavPremiumTags.cs), so the chip, the rail's gold star and the Play wall's
    /// band can never disagree.</para>
    /// </summary>
    public partial class MainWindow
    {
        private bool _favoritesRailSubscribed;
        private bool _favoritesMenusWired;

        /// <summary>
        /// The join from a rail or Play-wall control (x:Name) to the palette row it opens. The
        /// x:Names are facts about MainWindow.xaml / PlayTabView.xaml and live nowhere else;
        /// FavoritesRailMapTests reads this table from source and checks each name still exists.
        /// Launcher rows (Goon Game, the descent, the gaze games, the Loom) have no palette row
        /// yet, so they cannot be pinned - follow-up: a launch verb in the registry.
        /// </summary>
        private static readonly (string Element, string Id)[] FavoritePinMap =
        {
            ("DoorHome", "door.home"), ("DoorStudio", "door.studio"), ("DoorCompanion", "door.companion"),
            ("DoorPlay", "door.play"), ("DoorYou", "door.you"), ("DoorLibrary", "door.library"),
            ("DoorSettings", "door.settings"),
            ("BtnSettings", "tab.settings"), ("BtnNavStudio", "tab.studio"), ("BtnPresets", "tab.presets"),
            ("BtnNavHaptics", "tab.haptics"), ("BtnCompanion", "tab.companion"),
            ("BtnNavBambiTakeover", "tab.bambitakeover"), ("BtnNavSheListening", "tab.shelistening"),
            ("BtnNavAwareness", "tab.awareness"), ("BtnLab", "tab.play"), ("BtnDeeper", "tab.deeper"),
            ("BtnPatreonExclusives", "tab.exclusives"), ("BtnNavGradedIntake", "tab.gradedintake"),
            ("BtnNavLockdown", "tab.lockdown"), ("BtnNavBlinkTrainer", "tab.blinktrainer"),
            ("BtnNavRemoteControl", "tab.remotecontrol"), ("BtnAvailableSubjects", "tab.availablesubjects"),
            ("BtnDiscordTab", "tab.discord"), ("BtnNavSpiral", "tab.spiral"), ("BtnQuests", "tab.quests"),
            ("BtnAchievements", "tab.achievements"), ("BtnEnhancements", "tab.enhancements"),
            ("BtnPrograms", "tab.programs"), ("BtnLeaderboard", "tab.leaderboard"),
            ("BtnOpenAssetsTop", "tab.assets"), ("BtnNavMods", "launch.mods"),
            ("BtnNavCatalogue", "launch.catalogue"), ("BtnNavPhrases", "launch.phrases"),
            ("BtnNavMediaLog", "launch.medialog"),
            // Just Drop (owner call 2026-09-11): a creator tool, so its row is under Studio.
            ("BtnNavJustDrop", "door.justdrop"),
            // Play wall cards (PlayTabView.xaml) - resolved through PlayTab.FindName.
            ("BtnPlayRemoteControl", "tab.remotecontrol"), ("BtnPlayBlinkTrainer", "tab.blinktrainer"),
            ("BtnPlayGradedIntake", "tab.gradedintake"), ("BtnPlayFyp", "tab.fyp"),
            ("BtnPlayLockdown", "tab.lockdown"),
            ("BtnPlayArcademy", "card.arcademy"),
        };

        /// <summary>Subscribe to patron-status changes, wire the pin menus, paint once.</summary>
        internal void InitFavoritesRail()
        {
            if (!_favoritesRailSubscribed && App.Patreon != null)
            {
                try { App.Patreon.TierChanged += (s, e) => Dispatcher.BeginInvoke(new Action(RefreshDashboardRail)); }
                catch { }
                _favoritesRailSubscribed = true;
            }
            WireFavoritePinMenus();
            RefreshDashboardRail();
        }

        /// <summary>
        /// The dashboard's repaint funnel: the gesture caption, the mosaic's price tags and the
        /// rail, on every trigger the three share (patron status, the Home door shown, the ? box
        /// rotating, the weekly intake pass moving). Callers that used to wake the premium rail
        /// for its state dots wake this instead.
        /// </summary>
        internal void RefreshDashboardRail()
        {
            if (SettingsTab == null) return;
            RefreshDashboardToggleHint();
            RefreshMosaicTierBadges();
            RefreshFavoritesRail();
        }

        /// <summary>Rebuilds both chip stacks from settings. Cheap: at most fifteen buttons,
        /// and every picture on them comes out of the resolver's decode cache after the first
        /// paint.</summary>
        internal void RefreshFavoritesRail()
        {
            var dash = SettingsTab;
            var s = App.Settings?.Current;
            if (dash?.FavoritesList == null || dash.RecentList == null || s == null) return;
            try
            {
                var byId = SettingsPaletteIndex.All
                    .Where(e => FavoritesRailRule.IsDestination(e.Id))
                    .ToDictionary(e => e.Id, e => e, StringComparer.Ordinal);
                bool Known(string id) => byId.TryGetValue(id, out var e) && e.Available;

                dash.FavoritesList.Children.Clear();
                foreach (var id in s.RailFavorites.Where(Known))
                    dash.FavoritesList.Children.Add(BuildRailChip(byId[id]));
                if (dash.FavoritesEmpty != null)
                    dash.FavoritesEmpty.Visibility = dash.FavoritesList.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                dash.RecentList.Children.Clear();
                foreach (var id in FavoritesRailRule.RecentForDisplay(s.RailRecent, s.RailFavorites, Known))
                    dash.RecentList.Children.Add(BuildRailChip(byId[id]));
                if (dash.RecentEmpty != null)
                    dash.RecentEmpty.Visibility = dash.RecentList.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshFavoritesRail: {E}", ex.Message); }
        }

        private Button BuildRailChip(SettingsPaletteEntry entry)
        {
            var dash = SettingsTab!;
            bool locked = IsNavEntryLocked(entry.TabKey);

            var grid = new Grid();
            var cover = BuildRailChipArt(entry.Id, grid, out bool painted);
            if (!painted)
            {
                // Nothing resolved: the palette glyph stands, exactly as the chip shipped.
                grid.Children.Add(new Helpers.EmojiTextBlock
                {
                    Text = entry.Glyph, FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 3, 0, 0),
                });
            }
            grid.Children.Add(new TextBlock
            {
                Text = entry.Label,
                Style = dash.FavoritesRail?.TryFindResource("RailChipCaption") as Style,
            });
            if (locked)
            {
                grid.Children.Add(new Helpers.EmojiTextBlock
                {
                    Text = "🔒", FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 3, 0),
                });
            }

            var chip = new Button
            {
                Style = dash.FavoritesRail?.TryFindResource("RailChipStyle") as Style,
                Content = grid,
                Tag = entry.Id,
                ToolTip = string.IsNullOrEmpty(entry.Context)
                    ? entry.Label + "\n" + Loc.Get("rail_chip_tip")
                    : entry.Label + "  ·  " + entry.Context + "\n" + Loc.Get("rail_chip_tip"),
            };
            // The art is the chip's Background, so the template's rounded Border clips it for
            // free. A local value outranks the style's AccentTintedBgBrush setter; a chip with
            // no picture never sets one and keeps the flat tinted face.
            if (cover != null) chip.Background = cover;
            if (locked && TryFindResource("Tier1GoldBorderBrush") is Brush gold) chip.BorderBrush = gold;
            chip.Click += (_, _) => OpenDestination(entry);
            AttachPinMenu(chip, entry.Id, holdRail: false);
            return chip;
        }

        /// <summary>
        /// Puts the destination's own picture on a chip (owner ask, 2026-09-12). Cover art comes
        /// back as the brush the chip should take as its Background; an icon adds itself to
        /// <paramref name="grid"/> and the return is null. <paramref name="painted"/> is the one
        /// answer the caller needs: false means nothing resolved and the palette glyph stands.
        ///
        /// <para>Mod art is respected exactly as the mosaic respects it: the lookup goes through
        /// <see cref="Services.ModResourceResolver"/> on the same resource paths, the app-shipped
        /// themed fork (<c>features/vault_bambi.png</c>) is tried first for a built-in mod, and
        /// the crop comes from <see cref="Services.ModArtFramingRegistry"/> so a .ccpmod's own
        /// picture is framed by its author or centre-cropped, never by a rect drawn for ours.</para>
        /// </summary>
        private ImageBrush? BuildRailChipArt(string paletteId, Grid grid, out bool painted)
        {
            painted = false;
            try
            {
                var art = Services.FavoritesRailArt.For(paletteId);
                if (art == null) return null;

                var suffix = Services.FavoritesRailArt.ThemeSuffix(App.Mods?.ActiveModId);
                var candidates = Services.FavoritesRailArt.Candidates(art.ResourcePath, suffix);

                if (art.Fit == Services.RailArtFit.Icon)
                {
                    // A 64x64 door medallion, drawn where the emoji was. Never cover-fitted:
                    // cropping a square icon to a 1.9:1 chip cuts the drawing in half.
                    foreach (var path in candidates)
                    {
                        var icon = Services.ModResourceResolver.ResolveImageDecoded(
                            path, Services.FavoritesRailArt.BaseDecodeWidth);
                        if (icon == null) continue;

                        var medallion = new Image
                        {
                            Source = icon,
                            Width = 19, Height = 19,
                            Stretch = Stretch.Uniform,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(0, 2, 0, 0),
                            IsHitTestVisible = false,
                        };
                        RenderOptions.SetBitmapScalingMode(medallion, BitmapScalingMode.HighQuality);
                        grid.Children.Add(medallion);
                        painted = true;
                        return null;
                    }
                    return null;
                }

                // Cover art. Two decodes at most: the crop window decides the decode width (a
                // chip framed on a quarter of the image needs four times the pixels), and the
                // window is not known until the first bitmap has given up its aspect ratio. Both
                // decodes are cached by the resolver, so a repaint costs neither.
                foreach (var path in candidates)
                {
                    var probe = Services.ModResourceResolver.ResolveImageDecoded(
                        path, Services.FavoritesRailArt.BaseDecodeWidth);
                    if (probe == null) continue;

                    bool modArt = Services.ModResourceResolver.HasActiveModOverride(path);
                    var brush = new ImageBrush(probe) { Stretch = Stretch.UniformToFill };
                    ApplyArtFraming(brush, art.ResourcePath, Services.ModArtFramingRegistry.SurfaceRailChip, modArt);

                    var width = Services.FavoritesRailArt.DecodeWidthFor(brush.Viewbox.Width);
                    if (width > Services.FavoritesRailArt.BaseDecodeWidth)
                    {
                        var sharper = Services.ModResourceResolver.ResolveImageDecoded(path, width);
                        if (sharper != null) brush.ImageSource = sharper;
                    }
                    brush.Freeze();

                    // The caption sits over the picture, so it needs the foot band under it.
                    grid.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(8),
                        IsHitTestVisible = false,
                        Background = SettingsTab?.FavoritesRail?.TryFindResource("RailChipScrim") as Brush,
                    });
                    painted = true;
                    return brush;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BuildRailChipArt({Id}): {E}", paletteId, ex.Message);
            }
            return null;
        }

        /// <summary>
        /// Opens a destination the way the palette does - one navigation verb, so the arrival
        /// (door accordion, bark, per-tab FX, first-visit intro) is byte-for-byte a rail click.
        /// The four Library launchers are the exception the palette cannot afford and a pinned
        /// chip must: a favorite called "Mods" that lands beside the Mods button is a chip that
        /// does not do what it says, so those press the button.
        /// </summary>
        internal void OpenDestination(SettingsPaletteEntry entry)
        {
            try
            {
                if (entry.Id.StartsWith("launch.", StringComparison.Ordinal)
                    && entry.ElementNames.Length > 0
                    && FindName(entry.ElementNames[0]) is Button launcher
                    && new ButtonAutomationPeer(launcher).GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
                {
                    invoke.Invoke();
                    return;
                }
                SettingsPaletteWindow.Navigate(this, entry);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Favorites rail: could not open {Id}", entry.Id); }
        }

        /// <summary>
        /// RECENT's one input, called from ShowTab for every key that lands on a palette row.
        /// The dashboard itself and its aliases never enter the list (FavoritesRailRule), and a
        /// withheld row is skipped so the rail cannot admit to a door the app is hiding.
        /// </summary>
        internal void NoteDestinationOpened(string tabKey)
        {
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;
                var id = FavoritesRailRule.DestinationIdForTab(tabKey, SettingsPaletteIndex.All);
                if (id == null) return;
                var entry = SettingsPaletteIndex.All.FirstOrDefault(e => e.Id == id);
                if (entry == null || !entry.Available) return;
                if (!FavoritesRailRule.NoteOpened(s.RailRecent, id)) return;
                App.Settings?.Save();
                RefreshFavoritesRail();
            }
            catch (Exception ex) { App.Logger?.Debug("NoteDestinationOpened({Tab}): {E}", tabKey, ex.Message); }
        }

        internal void TogglePinned(string id)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            bool changed = FavoritesRailRule.IsPinned(s.RailFavorites, id)
                ? FavoritesRailRule.Unpin(s.RailFavorites, id)
                : FavoritesRailRule.TryPin(s.RailFavorites, id);
            if (!changed) return;
            App.Settings?.Save();
            RefreshFavoritesRail();
        }

        /// <summary>
        /// Right-click on any rail entry, door header or Play card that has a palette row: one
        /// context entry that reads the current state ("Pin to favorites" / "Unpin"), rebuilt on
        /// every open so it never lies. Wired once from <see cref="InitFavoritesRail"/>, after
        /// both views exist; a name that does not resolve is logged and skipped.
        /// </summary>
        private void WireFavoritePinMenus()
        {
            if (_favoritesMenusWired) return;
            _favoritesMenusWired = true;
            foreach (var (element, id) in FavoritePinMap)
            {
                try
                {
                    bool onPlayWall = element.StartsWith("BtnPlay", StringComparison.Ordinal);
                    var target = onPlayWall
                        ? PlayTab?.FindName(element) as FrameworkElement
                        : FindName(element) as FrameworkElement;
                    if (target == null)
                    {
                        App.Logger?.Debug("Favorites rail: no element named {Name} to pin", element);
                        continue;
                    }
                    AttachPinMenu(target, id, holdRail: !onPlayWall);
                }
                catch (Exception ex) { App.Logger?.Debug("WireFavoritePinMenus({Name}): {E}", element, ex.Message); }
            }
        }

        /// <param name="holdRail">True for the nav rail's own rows: the rail collapses on
        /// MouseLeave, and a popup taking the pointer counts as leaving.</param>
        private void AttachPinMenu(FrameworkElement target, string id, bool holdRail)
        {
            var menu = new ContextMenu();
            target.ContextMenu = menu;
            target.ContextMenuOpening += (_, _) =>
            {
                menu.Items.Clear();
                var favs = App.Settings?.Current?.RailFavorites;
                bool pinned = FavoritesRailRule.IsPinned(favs, id);
                var item = new MenuItem
                {
                    Header = pinned ? Loc.Get("rail_unpin")
                           : FavoritesRailRule.IsFull(favs) ? Loc.Get("rail_favorites_full")
                           : Loc.Get("rail_pin"),
                    IsEnabled = pinned || !FavoritesRailRule.IsFull(favs),
                };
                item.Click += (_, _) => TogglePinned(id);
                menu.Items.Add(item);
                if (holdRail) HoldNavRailOpen();
            };
            if (holdRail) menu.Closed += (_, _) => ReleaseNavRailOpen();
        }
    }
}
