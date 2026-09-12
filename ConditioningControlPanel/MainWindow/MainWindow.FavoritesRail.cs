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
    /// 2026-09-12 (owner: "use the images for the different features") a chip IS a picture: the
    /// art fills all 69x36 of it, the caption sits on a scrim at the foot, and the palette glyph
    /// is only what is left if nothing resolves. Scene art is cover-fitted and cropped; square
    /// icon art - the nav door medallions - goes on a plate instead, the icon blown up soft
    /// behind itself, because the first pass drew those at glyph size and the desk read them as
    /// icons beside captions. Which picture is <see cref="FavoritesRailArt"/>; the crop is the
    /// railChip surface in <see cref="ModArtFramingRegistry"/>, so a .ccpmod that re-skins
    /// features/flash.png re-skins the chip and is framed by its own author, not by our rect.</para>
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
            var face = BuildRailChipArt(entry.Id, grid, out bool painted);
            if (!painted)
            {
                // Nothing resolved. The glyph stands, but on the same shape the plates use - a
                // subject over the scrim, not a small mark beside a caption - so one chip the
                // art missed does not break the column's rhythm.
                grid.Children.Add(RailChipScrim());
                grid.Children.Add(new Helpers.EmojiTextBlock
                {
                    Text = entry.Glyph, FontSize = 17,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 0, 0),
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
            if (face != null) chip.Background = face;
            if (locked && TryFindResource("Tier1GoldBorderBrush") is Brush gold) chip.BorderBrush = gold;
            chip.Click += (_, _) => OpenDestination(entry);
            AttachPinMenu(chip, entry.Id, holdRail: false);
            return chip;
        }

        /// <summary>
        /// Fills a chip with the destination's own picture (owner ask 2026-09-12, reworked after
        /// that day's desk pass). Returns the brush the chip takes as its Background - every
        /// chip's face is a Background, so the template's rounded Border clips it for free -
        /// and adds the layers that go over it to <paramref name="grid"/>.
        /// <paramref name="painted"/> is the one answer the caller needs: false means nothing
        /// resolved and the palette glyph stands.
        ///
        /// <list type="bullet">
        /// <item><b>Cover</b> - scene art, cropped to the chip by the railChip surface.</item>
        /// <item><b>Plate</b> - square icon art (the nav medallions). The icon fills the chip
        /// twice: once soft and darkened as the backdrop, once at 26 DIP over it. The soft copy
        /// is not a BlurEffect but a 12px decode stretched across the chip - same look, one
        /// small bitmap instead of a render target per chip.</item>
        /// </list>
        ///
        /// <para>Mod art is respected exactly as the mosaic respects it: the lookup goes through
        /// <see cref="Services.ModResourceResolver"/> on the same resource paths, the app-shipped
        /// themed fork (<c>features/vault_bambi.png</c>) is tried first for a built-in mod, and
        /// the crop comes from <see cref="Services.ModArtFramingRegistry"/> so a .ccpmod's own
        /// picture is framed by its author or centre-cropped, never by a rect drawn for ours.</para>
        /// </summary>
        private Brush? BuildRailChipArt(string paletteId, Grid grid, out bool painted)
        {
            painted = false;
            try
            {
                var art = Services.FavoritesRailArt.For(paletteId);
                if (art == null) return null;

                var suffix = Services.FavoritesRailArt.ThemeSuffix(App.Mods?.ActiveModId);
                var candidates = Services.FavoritesRailArt.Candidates(art.ResourcePath, suffix);

                return art.Fit == Services.RailArtFit.Plate
                    ? BuildPlateFace(candidates, grid, out painted)
                    : BuildCoverFace(art.ResourcePath, candidates, grid, out painted);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BuildRailChipArt({Id}): {E}", paletteId, ex.Message);
            }
            return null;
        }

        /// <summary>
        /// Scene art filling the whole chip. Two decodes at most: the crop window decides the
        /// decode width (a chip framed on a quarter of the image needs four times the pixels)
        /// and the window is not known until the first bitmap has given up its aspect ratio.
        /// Both are cached by the resolver, so a repaint costs neither.
        /// </summary>
        private Brush? BuildCoverFace(string basePath, IReadOnlyList<string> candidates, Grid grid, out bool painted)
        {
            painted = false;
            foreach (var path in candidates)
            {
                var probe = Services.ModResourceResolver.ResolveImageDecoded(
                    path, Services.FavoritesRailArt.BaseDecodeWidth);
                if (probe == null) continue;

                bool modArt = Services.ModResourceResolver.HasActiveModOverride(path);
                var brush = new ImageBrush(probe) { Stretch = Stretch.UniformToFill };
                ApplyArtFraming(brush, basePath, Services.ModArtFramingRegistry.SurfaceRailChip, modArt);

                var width = Services.FavoritesRailArt.DecodeWidthFor(brush.Viewbox.Width);
                if (width > Services.FavoritesRailArt.BaseDecodeWidth)
                {
                    var sharper = Services.ModResourceResolver.ResolveImageDecoded(path, width);
                    if (sharper != null) brush.ImageSource = sharper;
                }
                brush.Freeze();

                grid.Children.Add(RailChipScrim());
                painted = true;
                return brush;
            }
            return null;
        }

        /// <summary>
        /// A square icon on a plate of itself: the icon decoded at 12px and stretched across the
        /// chip by the template's HighQuality scaling is the backdrop (a blur that costs one
        /// small bitmap rather than a render target), a flat wash takes the contrast back out of
        /// it, and the icon proper sits at 26 DIP over the scrim inside a rounded plate so the
        /// medallion's own square edge reads as a frame rather than a seam.
        /// </summary>
        private Brush? BuildPlateFace(IReadOnlyList<string> candidates, Grid grid, out bool painted)
        {
            painted = false;
            foreach (var path in candidates)
            {
                var icon = Services.ModResourceResolver.ResolveImageDecoded(
                    path, Services.FavoritesRailArt.PlateIconDecodeWidth);
                if (icon == null) continue;

                var soft = Services.ModResourceResolver.ResolveImageDecoded(
                    path, Services.FavoritesRailArt.PlateBackdropDecodeWidth) ?? icon;
                var backdrop = new ImageBrush(soft) { Stretch = Stretch.UniformToFill };
                backdrop.Freeze();

                // Takes the backdrop back down to a wash, so the icon over it stays the subject.
                grid.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(8),
                    IsHitTestVisible = false,
                    Background = new SolidColorBrush(Color.FromArgb(0xA6, 0x0E, 0x0A, 0x1A)),
                });
                grid.Children.Add(RailChipScrim());

                var plate = new Border
                {
                    Width = Services.FavoritesRailArt.PlateIconSize,
                    Height = Services.FavoritesRailArt.PlateIconSize,
                    CornerRadius = new CornerRadius(5),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 1, 0, 0),
                    IsHitTestVisible = false,
                    Background = new ImageBrush(icon) { Stretch = Stretch.Uniform },
                };
                RenderOptions.SetBitmapScalingMode(plate, BitmapScalingMode.HighQuality);
                grid.Children.Add(plate);

                painted = true;
                return backdrop;
            }
            return null;
        }

        /// <summary>The foot band the caption sits in. Every chip gets one, art or no art, so
        /// the column reads as one set of pictures rather than two kinds of chip.</summary>
        private Border RailChipScrim() => new()
        {
            CornerRadius = new CornerRadius(8),
            IsHitTestVisible = false,
            Background = SettingsTab?.FavoritesRail?.TryFindResource("RailChipScrim") as Brush,
        };

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
