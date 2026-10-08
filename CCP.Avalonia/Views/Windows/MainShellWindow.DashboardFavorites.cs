// PORTED from WPF 7.1.5 MainWindow/MainWindow.FavoritesRail.cs (parity lane E3).
//
// The Home FAVORITES + RECENT drawer's chips (owner, Discord "New UI Feedback", 2026-09-11: the
// premium quick-toggle rail is gone; the column remembers where the user goes). A chip IS a Ctrl+K
// palette row (Core SettingsPaletteIndex): same id, same label, same navigation, so a door that
// moves in the palette moves here for free and a withheld row never renders. List rules are Core
// FavoritesRailRule; the lists are AppSettings.RailFavorites / RailRecent. The FACE is the
// destination's own picture (Core FavoritesRailArt: cover-cropped scene art through the railChip
// frame of ModArtFramingRegistry, or a nav medallion on a plate), the palette glyph only when no
// picture resolves. Locked destinations keep their chip and wear the padlock and the gold rim.
//
// Not on this head yet (seam requests in the lane hand-back): ShowTab does not call
// NoteDestinationOpened (RECENT fills only from chips and the board), the palette rows and rail
// doors carry no pin menu (pin from a chip's own menu is unpin only), mod art overrides
// (ModResourceResolver) are not consulted: the shipped art and its themed forks are.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Home;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private const string ArtRoot = "avares://CCP.Avalonia/Resources/";
        private static readonly Dictionary<string, Bitmap?> RailArtCache = new(StringComparer.Ordinal);

        private FavoritesDrawer? HomeFavorites => HomeTab?.FindControl<FavoritesDrawer>("FavoritesDrawer");

        /// <summary>Paint the drawer once its page is up (InitHomeDashboard).</summary>
        private void InitFavoritesRail()
        {
            HomeFavorites?.ApplySetting();
            RefreshFavoritesRail();
        }

        /// <summary>Rebuilds both chip stacks from settings. At most fifteen buttons; every picture
        /// comes out of the decode cache after the first paint.</summary>
        internal void RefreshFavoritesRail()
        {
            var drawer = HomeFavorites;
            if (drawer == null) return;
            try
            {
                var s = CoreSettings.Current;
                var byId = SettingsPaletteIndex.All
                    .Where(e => FavoritesRailRule.IsDestination(e.Id))
                    .GroupBy(e => e.Id, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                bool Known(string id) => byId.TryGetValue(id, out var e) && e.Available;

                drawer.FavoritesList.Children.Clear();
                foreach (var id in s.RailFavorites.Where(Known))
                    drawer.FavoritesList.Children.Add(BuildRailChip(byId[id]));
                drawer.FavoritesEmpty.IsVisible = drawer.FavoritesList.Children.Count == 0;

                drawer.RecentList.Children.Clear();
                foreach (var id in FavoritesRailRule.RecentForDisplay(s.RailRecent, s.RailFavorites, Known))
                    drawer.RecentList.Children.Add(BuildRailChip(byId[id]));
                drawer.RecentEmpty.IsVisible = drawer.RecentList.Children.Count == 0;
            }
            catch (Exception ex) { Log.Debug("RefreshFavoritesRail: {E}", ex.Message); }
        }

        private Button BuildRailChip(SettingsPaletteEntry entry)
        {
            bool locked = IsNavEntryLocked(entry.TabKey);
            var layers = new List<Control>();
            var face = BuildRailChipArt(entry.Id, layers);
            if (face == null)
            {
                // Nothing resolved: the glyph stands, on the same scrim the pictures use, so one
                // chip the art missed does not break the column's rhythm.
                layers.Add(FavoritesDrawer.Scrim());
                layers.Add(new TextBlock
                {
                    Text = entry.Glyph, FontSize = 17,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 0, 0),
                });
            }
            string tip = string.IsNullOrEmpty(entry.Context)
                ? entry.Label + "\n" + Loc.Get("rail_chip_tip")
                : entry.Label + "  ·  " + entry.Context + "\n" + Loc.Get("rail_chip_tip");
            var chip = FavoritesDrawer.BuildChip(entry.Id, entry.Label, face, layers, locked, tip);
            chip.Click += (_, _) => OpenDestination(entry);
            AttachPinMenu(chip, entry.Id);
            return chip;
        }

        /// <summary>The destination's own picture, or null (the glyph stands). Cover art is cropped
        /// by its railChip frame; a medallion sits on a plate over a soft copy of itself.</summary>
        private IBrush? BuildRailChipArt(string paletteId, List<Control> layers)
        {
            try
            {
                var art = FavoritesRailArt.For(paletteId);
                if (art == null) return null;
                var suffix = FavoritesRailArt.ThemeSuffix(CoreSettings.Current.ActiveModId);
                foreach (var path in FavoritesRailArt.Candidates(art.ResourcePath, suffix))
                {
                    var bmp = LoadRailArt(path);
                    if (bmp == null) continue;
                    if (art.Fit == RailArtFit.Plate)
                    {
                        layers.Add(new Border { CornerRadius = new CornerRadius(8), IsHitTestVisible = false, Background = new SolidColorBrush(Color.FromArgb(0xA6, 0x0E, 0x0A, 0x1A)) });
                        layers.Add(FavoritesDrawer.Scrim());
                        layers.Add(new Border
                        {
                            Width = FavoritesRailArt.PlateIconSize, Height = FavoritesRailArt.PlateIconSize,
                            CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0),
                            IsHitTestVisible = false,
                            Background = new ImageBrush(bmp) { Stretch = Stretch.Uniform },
                        });
                        // The soft backdrop: the icon stretched across the chip, then washed down.
                        return new ImageBrush(bmp) { Stretch = Stretch.UniformToFill, Opacity = 0.9 };
                    }
                    var vb = ModArtFramingRegistry.ResolveViewbox(art.ResourcePath, ModArtFramingRegistry.SurfaceRailChip,
                        isModSupplied: false, sourceAspect: bmp.PixelSize.Width / (double)Math.Max(1, bmp.PixelSize.Height), framing: null);
                    layers.Add(FavoritesDrawer.Scrim());
                    return new ImageBrush(bmp)
                    {
                        Stretch = Stretch.UniformToFill,
                        SourceRect = new RelativeRect(vb.X, vb.Y, vb.Width, vb.Height, RelativeUnit.Relative),
                    };
                }
            }
            catch (Exception ex) { Log.Debug("BuildRailChipArt({Id}): {E}", paletteId, ex.Message); }
            return null;
        }

        private static Bitmap? LoadRailArt(string path)
        {
            lock (RailArtCache)
            {
                if (RailArtCache.TryGetValue(path, out var cached)) return cached;
                Bitmap? bmp = null;
                try
                {
                    var uri = new Uri(ArtRoot + path);
                    if (AssetLoader.Exists(uri))
                    {
                        using var s = AssetLoader.Open(uri);
                        bmp = Bitmap.DecodeToWidth(s, FavoritesRailArt.MaxDecodeWidth / 2);
                    }
                }
                catch (Exception ex) { Log.Debug("Rail art {Path}: {E}", path, ex.Message); }
                RailArtCache[path] = bmp;
                return bmp;
            }
        }

        /// <summary>WPF OpenDestination: a launch.* row presses the button it names; everything else
        /// navigates the way the palette does.</summary>
        internal void OpenDestination(SettingsPaletteEntry entry)
        {
            try
            {
                if (entry.Id.StartsWith("launch.", StringComparison.Ordinal) && entry.ElementNames.Length > 0
                    && this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == entry.ElementNames[0]) is { } launcher)
                {
                    launcher.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    return;
                }
                SettingsPaletteWindow.Navigate(this, entry);
                NoteDestinationOpened(entry.TabKey);
            }
            catch (Exception ex) { Log.Warning(ex, "Favorites rail: could not open {Id}", entry.Id); }
        }

        /// <summary>WPF NoteDestinationOpened (called from ShowTab there): the destination moves to
        /// the front of RECENT. Called from ShowTab.</summary>
        internal void NoteDestinationOpened(string tabKey)
        {
            try
            {
                var s = CoreSettings.Current;
                var id = FavoritesRailRule.DestinationIdForTab(tabKey, SettingsPaletteIndex.All);
                if (id == null) return;
                var entry = SettingsPaletteIndex.All.FirstOrDefault(e => e.Id == id);
                if (entry == null || !entry.Available) return;
                if (!FavoritesRailRule.NoteOpened(s.RailRecent, id)) return;
                // Navigation never writes settings.json on this head (PremiumGatesTests lapse rule): RECENT
                // rides the next save, like the last-tab memory.
                RefreshFavoritesRail();
            }
            catch (Exception ex) { Log.Debug("NoteDestinationOpened({Tab}): {E}", tabKey, ex.Message); }
        }

        internal static bool IsPinned(string id) => FavoritesRailRule.IsPinned(CoreSettings.Current.RailFavorites, id);

        /// <summary>Pin or unpin a destination. A NEW pin slides a closed drawer out for 2.5 s with
        /// the glow on its chip (a pin the player cannot see landing reads as a pin that did nothing).</summary>
        internal void TogglePinned(string id)
        {
            var s = CoreSettings.Current;
            bool wasPinned = FavoritesRailRule.IsPinned(s.RailFavorites, id);
            bool changed = wasPinned ? FavoritesRailRule.Unpin(s.RailFavorites, id) : FavoritesRailRule.TryPin(s.RailFavorites, id);
            if (!changed) return;
            CoreSettings.Save();
            RefreshFavoritesRail();
            if (!wasPinned)
            {
                try { HomeFavorites?.Peek(id); }
                catch (Exception ex) { Log.Debug("Favorites peek({Id}): {E}", id, ex.Message); }
            }
        }

        /// <summary>Right-click a chip: pin / unpin (or "full"), rebuilt on every open.</summary>
        private void AttachPinMenu(Control target, string id)
        {
            var menu = new ContextMenu();
            target.ContextMenu = menu;
            menu.Opening += (_, _) =>
            {
                var favs = CoreSettings.Current.RailFavorites;
                bool pinned = FavoritesRailRule.IsPinned(favs, id);
                var item = new MenuItem
                {
                    Header = pinned ? Loc.Get("rail_unpin")
                           : FavoritesRailRule.IsFull(favs) ? Loc.Get("rail_favorites_full")
                           : Loc.Get("rail_pin"),
                    IsEnabled = pinned || !FavoritesRailRule.IsFull(favs),
                };
                item.Click += (_, _) => TogglePinned(id);
                menu.Items.Clear();
                menu.Items.Add(item);
            };
        }
    }
}
