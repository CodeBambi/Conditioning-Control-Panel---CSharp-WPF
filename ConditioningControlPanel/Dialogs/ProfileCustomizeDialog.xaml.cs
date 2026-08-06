using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The Trainer Card customization kit (Profile redesign Phase 2). Edits a CLONE of the
    /// viewer's loadout and exposes it as <see cref="Result"/> on OK, so Cancel really cancels
    /// and nothing here can half-write settings.
    ///
    /// The dialog only ever offers what the viewer has actually earned: titles and pins come from
    /// the unlocked-achievement set handed in by the caller. Banners and accents are free for
    /// everyone this build — distribution gating is explicitly out of scope (DESIGN.md).
    /// </summary>
    public partial class ProfileCustomizeDialog : Window
    {
        private readonly ProfileCosmetics _draft;
        private readonly List<Achievement> _unlocked;

        /// <summary>The viewer's avatar picture, for the Wardrobe editor's stage. Optional.</summary>
        private readonly ImageSource? _editorAvatar;

        private readonly Dictionary<string, Border> _bannerTiles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Border> _accentTiles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Border> _titleRows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Border> _pinTiles = new(StringComparer.Ordinal);

        /// <summary>Wardrobe tiles for the mod tab currently on screen (rebuilt per tab).</summary>
        private readonly Dictionary<string, Border> _wardrobeTiles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Border> _modTabs = new(StringComparer.OrdinalIgnoreCase);
        private string? _selectedMod;

        /// <summary>Sentinel key for the "nothing equipped" tile in each selectable group.</summary>
        private const string NoneKey = "__none__";

        private static readonly Brush IdleBorder = Frozen("#33FFFFFF");
        private static readonly Brush SelectedBorder = Frozen("#FF69B4");
        private static readonly Brush SelectedGold = Frozen("#FFD700");
        private static readonly Brush TileBg = Frozen("#26FFFFFF");
        private static readonly Brush SelectedBg = Frozen("#33FF69B4");
        private static readonly Brush SelectedCyan = Frozen("#5EC8F2");
        private static readonly Brush SelectedCyanBg = Frozen("#335EC8F2");

        /// <summary>The edited loadout. Only meaningful when ShowDialog() returned true.</summary>
        public ProfileCosmetics Result => _draft;

        public ProfileCustomizeDialog(ProfileCosmetics current, IEnumerable<string>? unlockedAchievementIds,
                                      ImageSource? editorAvatar = null)
        {
            InitializeComponent();

            _draft = (current ?? new ProfileCosmetics()).Clone();
            _editorAvatar = editorAvatar;

            var unlockedSet = new HashSet<string>(
                unlockedAchievementIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

            // Declaration order, hidden entries included (you earned it, you may wear it).
            _unlocked = Achievement.All.Values
                .Where(a => unlockedSet.Contains(a.Id))
                .ToList();

            BuildBanners();
            BuildAccents();
            BuildTitles();
            BuildPins();
            BuildWardrobe();
        }

        // ============================== banner ==============================

        private void BuildBanners()
        {
            BannerHost.Children.Add(BuildBannerTile(
                NoneKey, Loc.Get("profile_customize_none"), null));

            foreach (var banner in CosmeticsCatalog.Banners)
            {
                // A banner whose art will not load is not offered at all — better than a tile that
                // looks broken and equips to nothing. Thumbnails, not the card-sized decodes: this
                // loop runs for all 19 banners the moment the dialog is constructed.
                var image = CosmeticsCatalog.GetBannerThumbnail(banner.Id);
                if (image == null) continue;
                BannerHost.Children.Add(BuildBannerTile(banner.Id, banner.Name, image));
            }

            SelectBanner(_draft.BannerId ?? NoneKey);
        }

        private Border BuildBannerTile(string key, string label, ImageSource? art)
        {
            var content = new Grid { Width = 128, Height = 52 };

            if (art != null)
            {
                content.Children.Add(new Image
                {
                    Source = art,
                    Stretch = Stretch.UniformToFill,
                    IsHitTestVisible = false
                });
                content.Children.Add(new Border
                {
                    Background = Frozen("#99000000"),
                    IsHitTestVisible = false
                });
            }

            content.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(6, 0, 6, 0)
            });

            var tile = new Border
            {
                Width = 128,
                Height = 52,
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(8),
                Background = TileBg,
                BorderBrush = IdleBorder,
                BorderThickness = new Thickness(2),
                ClipToBounds = true,
                Cursor = Cursors.Hand,
                ToolTip = label,
                Child = content
            };
            tile.MouseLeftButtonUp += (_, _) => SelectBanner(key);

            _bannerTiles[key] = tile;
            return tile;
        }

        private void SelectBanner(string key)
        {
            _draft.BannerId = key == NoneKey ? null : key;
            foreach (var (id, tile) in _bannerTiles)
            {
                var on = id == key;
                tile.BorderBrush = on ? SelectedBorder : IdleBorder;
            }
        }

        // ============================== accent ==============================

        private void BuildAccents()
        {
            AccentHost.Children.Add(BuildAccentTile(NoneKey, null));
            foreach (var hex in CosmeticsCatalog.AccentSwatches)
                AccentHost.Children.Add(BuildAccentTile(hex, hex));

            SelectAccent(_draft.Accent ?? NoneKey);
        }

        private Border BuildAccentTile(string key, string? hex)
        {
            var swatch = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = hex != null ? Frozen(hex) : Frozen("#26FFFFFF"),
                IsHitTestVisible = false
            };

            if (hex == null)
            {
                swatch.Child = new TextBlock
                {
                    Text = "✕",
                    Foreground = Frozen("#8079A3"),
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            var tile = new Border
            {
                Width = 44,
                Height = 44,
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(22),
                BorderBrush = IdleBorder,
                BorderThickness = new Thickness(2),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = hex ?? Loc.Get("profile_customize_none"),
                Child = swatch
            };
            tile.MouseLeftButtonUp += (_, _) => SelectAccent(key);

            _accentTiles[key] = tile;
            return tile;
        }

        private void SelectAccent(string key)
        {
            _draft.Accent = key == NoneKey ? null : key;
            foreach (var (id, tile) in _accentTiles)
                tile.BorderBrush = id == key ? SelectedBorder : IdleBorder;
        }

        // ============================== title ==============================

        private void BuildTitles()
        {
            TitleHost.Children.Add(BuildTitleRow(NoneKey, Loc.Get("profile_customize_no_title")));

            foreach (var achievement in _unlocked)
            {
                var name = MainWindow.ResolveAchievementTitle(achievement.Id) ?? achievement.Name;
                TitleHost.Children.Add(BuildTitleRow(achievement.Id, name));
            }

            TxtNoTitlesYet.Visibility = _unlocked.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // An id we can no longer offer (mod swap, achievement retired) silently falls back to
            // "no title" rather than leaving the group with nothing selected.
            var wanted = _draft.TitleId != null && _titleRows.ContainsKey(_draft.TitleId)
                ? _draft.TitleId
                : NoneKey;
            SelectTitle(wanted);
        }

        private Border BuildTitleRow(string key, string label)
        {
            var row = new Border
            {
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(10, 6, 10, 6),
                CornerRadius = new CornerRadius(6),
                Background = TileBg,
                BorderBrush = IdleBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = label,
                    Foreground = Brushes.White,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            row.MouseLeftButtonUp += (_, _) => SelectTitle(key);

            _titleRows[key] = row;
            return row;
        }

        private void SelectTitle(string key)
        {
            _draft.TitleId = key == NoneKey ? null : key;
            foreach (var (id, row) in _titleRows)
            {
                var on = id == key;
                row.BorderBrush = on ? SelectedGold : IdleBorder;
                row.Background = on ? SelectedBg : TileBg;
            }
        }

        // ============================== pins ==============================

        private void BuildPins()
        {
            foreach (var achievement in _unlocked)
            {
                var tile = BuildPinTile(achievement);
                if (tile == null) continue;
                PinHost.Children.Add(tile);
            }

            TxtNoPinsYet.Visibility = PinHost.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Drop pins whose tile is not on offer here, so the counter matches what is visible.
            _draft.PinnedAchievements = _draft.PinnedAchievements
                .Where(id => _pinTiles.ContainsKey(id))
                .Take(ProfileCosmetics.MaxPinnedAchievements)
                .ToList();

            RefreshPinVisuals();
        }

        private Border? BuildPinTile(Achievement achievement)
        {
            var art = LoadAchievementArt(achievement.ImageName);
            if (art == null) return null;

            var content = new Grid();
            content.Children.Add(new Image
            {
                Source = art,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(5),
                IsHitTestVisible = false
            });
            content.Children.Add(new TextBlock
            {
                Text = "★",
                Foreground = SelectedGold,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 4, 0),
                Visibility = Visibility.Collapsed,
                Tag = "star",
                IsHitTestVisible = false
            });

            var tile = new Border
            {
                Width = 58,
                Height = 58,
                Margin = new Thickness(0, 0, 7, 7),
                CornerRadius = new CornerRadius(8),
                Background = TileBg,
                BorderBrush = IdleBorder,
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                ToolTip = MainWindow.ResolveAchievementTitle(achievement.Id) ?? achievement.Name,
                Child = content
            };
            tile.MouseLeftButtonUp += (_, _) => TogglePin(achievement.Id);

            _pinTiles[achievement.Id] = tile;
            return tile;
        }

        private void TogglePin(string achievementId)
        {
            if (_draft.PinnedAchievements.Contains(achievementId))
            {
                _draft.PinnedAchievements.Remove(achievementId);
            }
            else
            {
                // Silently ignoring the click at the cap reads as a broken tile; say so instead.
                if (_draft.PinnedAchievements.Count >= ProfileCosmetics.MaxPinnedAchievements)
                {
                    TxtPinCount.Text = Loc.GetF("profile_customize_pins_full", ProfileCosmetics.MaxPinnedAchievements);
                    TxtPinCount.Foreground = Frozen("#FF5C7A");
                    return;
                }
                _draft.PinnedAchievements.Add(achievementId);
            }

            RefreshPinVisuals();
        }

        private void RefreshPinVisuals()
        {
            foreach (var (id, tile) in _pinTiles)
            {
                var on = _draft.PinnedAchievements.Contains(id);
                tile.BorderBrush = on ? SelectedGold : IdleBorder;
                tile.Background = on ? SelectedBg : TileBg;

                if (tile.Child is Grid grid)
                {
                    var star = grid.Children.OfType<TextBlock>()
                        .FirstOrDefault(t => (t.Tag as string) == "star");
                    if (star != null) star.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            TxtPinCount.Text = Loc.GetF("profile_customize_pins_count",
                _draft.PinnedAchievements.Count, ProfileCosmetics.MaxPinnedAchievements);
            TxtPinCount.Foreground = Frozen("#8079A3");
        }

        // ============================== wardrobe (Phase 3) ==============================

        /// <summary>
        /// Mod tabs + the two slot groups, all read from Resources/cosmetics/registry.json. Items
        /// whose PNG did not ship are not offered: a tile that equips to an invisible decoration is
        /// worse than a shorter grid.
        /// </summary>
        private void BuildWardrobe()
        {
            try
            {
                // Only mods that have at least one paintable item get a tab. HasArtFile, not
                // HasArt: this is a 60-item existence question, and decoding every canvas to
                // answer it would cost tens of MB before the dialog is even on screen.
                var mods = WardrobeCatalog.Mods
                    .Where(m => WardrobeCatalog.ItemsFor(m, true).Any(i => WardrobeCatalog.HasArtFile(i.Id))
                             || WardrobeCatalog.ItemsFor(m, false).Any(i => WardrobeCatalog.HasArtFile(i.Id)))
                    .ToList();

                if (mods.Count == 0)
                {
                    // No registry, or no art installed yet - say so once and keep the slot counter,
                    // which still reports whatever the loadout carries.
                    WardrobeModTabs.Visibility = Visibility.Collapsed;
                    WardrobeGroups.Visibility = Visibility.Collapsed;
                    TxtWardrobeEmpty.Visibility = Visibility.Visible;
                    RefreshWardrobeSlots();
                    return;
                }

                foreach (var mod in mods)
                    WardrobeModTabs.Children.Add(BuildModTab(mod));

                // Open on the mod the app is actually running, when it has a tab.
                var active = App.Mods?.ActiveModId;
                var wanted = mods.FirstOrDefault(m => string.Equals(m, active, StringComparison.OrdinalIgnoreCase))
                             ?? mods[0];
                SelectMod(wanted);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ProfileCustomizeDialog: wardrobe build failed: {E}", ex.Message);
                WardrobeSection.Visibility = Visibility.Collapsed;
            }
        }

        private Border BuildModTab(string mod)
        {
            var tab = new Border
            {
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(12, 5, 12, 5),
                CornerRadius = new CornerRadius(13),
                Background = TileBg,
                BorderBrush = IdleBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    // Registry mod ids are plain English buckets - displayed, not localized.
                    Text = TitleCase(mod),
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold
                }
            };
            tab.MouseLeftButtonUp += (_, _) => SelectMod(mod);

            _modTabs[mod] = tab;
            return tab;
        }

        private void SelectMod(string mod)
        {
            if (string.Equals(_selectedMod, mod, StringComparison.OrdinalIgnoreCase)) return;
            _selectedMod = mod;

            foreach (var (id, tab) in _modTabs)
            {
                var on = string.Equals(id, mod, StringComparison.OrdinalIgnoreCase);
                tab.BorderBrush = on ? SelectedCyan : IdleBorder;
                tab.Background = on ? SelectedCyanBg : TileBg;
            }

            _wardrobeTiles.Clear();
            WardrobeDecoHost.Children.Clear();
            WardrobeCharmHost.Children.Clear();

            FillWardrobeHost(WardrobeDecoHost, WardrobeCatalog.ItemsFor(mod, true));
            FillWardrobeHost(WardrobeCharmHost, WardrobeCatalog.ItemsFor(mod, false));

            TxtWardrobeDecoHeader.Visibility = WardrobeDecoHost.Children.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
            TxtWardrobeCharmHeader.Visibility = WardrobeCharmHost.Children.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
            TxtWardrobeEmpty.Visibility =
                WardrobeDecoHost.Children.Count == 0 && WardrobeCharmHost.Children.Count == 0
                    ? Visibility.Visible : Visibility.Collapsed;

            RefreshWardrobeVisuals();
        }

        private void FillWardrobeHost(WrapPanel host, IReadOnlyList<WardrobeItem> items)
        {
            foreach (var item in items)
            {
                var art = WardrobeCatalog.GetImage(item.Id);
                if (art == null) continue;           // art never shipped - not on offer
                host.Children.Add(BuildWardrobeTile(item, art));
            }
        }

        private Border BuildWardrobeTile(WardrobeItem item, ImageSource art)
        {
            var content = new Grid();
            content.Children.Add(new Image
            {
                Source = art,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(4),
                IsHitTestVisible = false
            });
            content.Children.Add(new TextBlock
            {
                Text = "✓",
                Foreground = SelectedCyan,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 4, 0),
                Visibility = Visibility.Collapsed,
                Tag = "check",
                IsHitTestVisible = false
            });

            var tile = new Border
            {
                Width = 72,
                Height = 72,
                Margin = new Thickness(0, 0, 7, 7),
                CornerRadius = new CornerRadius(8),
                Background = TileBg,
                BorderBrush = IdleBorder,
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                // Item names are plain English proper nouns in the registry - not localized.
                ToolTip = item.Name,
                Child = content
            };
            tile.MouseLeftButtonUp += (_, _) => ToggleWardrobeItem(item);

            _wardrobeTiles[item.Id] = tile;
            return tile;
        }

        /// <summary>
        /// Equip, or unequip when it is already worn - clicking the worn item is the only "take it
        /// off" gesture, so it is spelled out in the section's hint line.
        /// </summary>
        private void ToggleWardrobeItem(WardrobeItem item)
        {
            if (item.IsCharm)
            {
                if (_draft.Charms.Contains(item.Id))
                {
                    _draft.Charms.Remove(item.Id);
                }
                else
                {
                    if (_draft.Charms.Count >= ProfileCosmetics.MaxCharms)
                    {
                        // Same treatment as the pin cap: a dead click reads as a broken tile.
                        TxtWardrobeSlots.Text = Loc.GetF("profile_customize_wardrobe_charms_full",
                            ProfileCosmetics.MaxCharms);
                        TxtWardrobeSlots.Foreground = Frozen("#FF5C7A");
                        return;
                    }
                    _draft.Charms.Add(item.Id);
                }
            }
            else
            {
                _draft.AvatarDeco = string.Equals(_draft.AvatarDeco, item.Id, StringComparison.Ordinal)
                    ? null
                    : item.Id;
            }

            RefreshWardrobeVisuals();
        }

        private void RefreshWardrobeVisuals()
        {
            foreach (var (id, tile) in _wardrobeTiles)
            {
                var on = string.Equals(_draft.AvatarDeco, id, StringComparison.Ordinal)
                         || _draft.Charms.Contains(id);

                tile.BorderBrush = on ? SelectedCyan : IdleBorder;
                tile.Background = on ? SelectedCyanBg : TileBg;

                if (tile.Child is Grid grid)
                {
                    var check = grid.Children.OfType<TextBlock>()
                        .FirstOrDefault(t => (t.Tag as string) == "check");
                    if (check != null) check.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            RefreshWardrobeSlots();
        }

        /// <summary>
        /// "Decoration 1/1 · Charms 2/2". Counts the LOADOUT, not the visible grid: an item from a
        /// mod tab you are not looking at is still equipped.
        /// </summary>
        private void RefreshWardrobeSlots()
        {
            TxtWardrobeSlots.Text = Loc.GetF("profile_customize_wardrobe_slots",
                string.IsNullOrWhiteSpace(_draft.AvatarDeco) ? 0 : 1,
                _draft.Charms.Count,
                ProfileCosmetics.MaxCharms);
            TxtWardrobeSlots.Foreground = Frozen("#8079A3");
        }

        private static string TitleCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            return char.ToUpperInvariant(value[0]) + (value.Length > 1 ? value.Substring(1) : string.Empty);
        }

        /// <summary>
        /// Opens the Wardrobe editor on this dialog's draft. The editor mutates only the
        /// transform fields (and reverts them itself on its own Cancel), so nothing to merge
        /// here - this dialog's Save/Cancel still decides whether anything is committed.
        /// </summary>
        private void BtnArrange_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var editor = new WardrobeEditorDialog(_draft, _editorAvatar) { Owner = this };
                editor.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ProfileCustomizeDialog: wardrobe editor failed: {E}", ex.Message);
            }
        }

        // ============================== footer ==============================

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            SelectBanner(NoneKey);
            SelectAccent(NoneKey);
            SelectTitle(NoneKey);
            _draft.PinnedAchievements.Clear();
            RefreshPinVisuals();
            _draft.AvatarDeco = null;
            _draft.Charms.Clear();
            RefreshWardrobeVisuals();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        // ============================== helpers ==============================

        /// <summary>
        /// Achievement art, mod-aware, mirroring MainWindow's loader. Returns null rather than
        /// throwing so a missing PNG just means "that achievement is not offered as a pin".
        /// </summary>
        private static ImageSource? LoadAchievementArt(string imageName)
        {
            try
            {
                var resolved = ModResourceResolver.ResolveImage($"achievements/{imageName}");
                if (resolved != null) return resolved;
                return new BitmapImage(
                    new Uri($"pack://application:,,,/Resources/achievements/{imageName}", UriKind.Absolute));
            }
            catch
            {
                return null;
            }
        }

        private static Brush Frozen(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
    }
}
