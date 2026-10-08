using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// The Trainer Card customization kit (Profile redesign Phase 2). Edits a CLONE of the
    /// viewer's loadout and exposes it as <see cref="Result"/> on OK, so Cancel really cancels
    /// and nothing here can half-write settings.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/ProfileCustomizeDialog.xaml.cs. The tile
    /// builders, selection rules, pin cap and reset are the original's; <see cref="ProfileCosmetics"/>
    /// is already in Core, and so are WardrobeCatalog and the banner/avatar pools (CosmeticsPool;
    /// the art decodes in Helpers/ModArt). Differences: unlocked achievements arrive as (id, name)
    /// pairs (the name is the pin tooltip, where WPF asks MainWindow.ResolveAchievementTitle), and
    /// WPF's DialogResult becomes Close(bool).
    /// </summary>
    public partial class ProfileCustomizeDialog : Window
    {
        private readonly ProfileCosmetics _draft;
        private readonly List<(string Id, string Name)> _unlocked;

        private readonly Dictionary<string, Border> _bannerTiles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Border> _avatarTiles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Border> _accentTiles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Border> _titleRows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Border> _pinTiles = new(StringComparer.Ordinal);

        /// <summary>Sentinel key for the "nothing equipped" tile in each selectable group.</summary>
        private const string NoneKey = "__none__";

        private static readonly IBrush IdleBorder = Brush.Parse("#33FFFFFF");
        private static readonly IBrush SelectedBorder = Brush.Parse("#FF69B4");
        private static readonly IBrush SelectedGold = Brush.Parse("#FFD700");
        private static readonly IBrush TileBg = Brush.Parse("#26FFFFFF");
        private static readonly IBrush SelectedBg = Brush.Parse("#33FF69B4");
        private static readonly IBrush Muted = Brush.Parse("#8079A3");
        private static readonly IBrush Alert = Brush.Parse("#FF5C7A");
        private static readonly IBrush SelectedCyan = Brush.Parse("#5EC8F2");
        private static readonly IBrush SelectedCyanBg = Brush.Parse("#335EC8F2");
        private static readonly IBrush SilhouetteFill = Brush.Parse("#E60D0A1A");

        private readonly Dictionary<string, Border> _wardrobeTiles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Border> _modTabs = new(StringComparer.OrdinalIgnoreCase);
        private string? _selectedMod;

        private readonly WrapPanel _bannerHost, _avatarHost, _accentHost, _pinHost;
        private readonly StackPanel _titleHost;
        private readonly TextBlock _txtNoTitlesYet, _txtNoPinsYet, _txtPinCount, _txtWardrobeSlots, _txtWardrobeEmpty;

        /// <summary>The edited loadout. Only meaningful when ShowDialog() returned true.</summary>
        public ProfileCosmetics Result => _draft;

        /// <summary>Render/design constructor: sample data so --render-view can draw the dialog.</summary>
        public ProfileCustomizeDialog() : this(
            new ProfileCosmetics
            {
                BannerId = "bambi_neon_den",
                Accent = "#B478FF",
                TitleId = "plastic_initiation",
                AvatarDeco = "bambi_silk_bow",
                Charms = new List<string> { "bambi_plush_bunny" },
                PinnedAchievements = new List<string> { "plastic_initiation", "dumb_bimbo" }
            },
            new[] { ("plastic_initiation", "Plastic Initiation"), ("dumb_bimbo", "Dumb Bimbo"), ("fully_synthetic", "Fully Synthetic") })
        { }

        private readonly IImageBrushSource? _editorAvatar;
        private readonly double _cardWidth, _cardHeight;

        public ProfileCustomizeDialog(ProfileCosmetics current, IEnumerable<(string Id, string Name)>? unlocked,
                                      IImageBrushSource? editorAvatar = null, double cardWidth = 0, double cardHeight = 0)
        {
            _editorAvatar = editorAvatar;
            _cardWidth = cardWidth;
            _cardHeight = cardHeight;
            AvaloniaXamlLoader.Load(this);

            T C<T>(string name) where T : Control => this.FindControl<T>(name)!;
            _bannerHost = C<WrapPanel>("BannerHost");
            _avatarHost = C<WrapPanel>("AvatarHost");
            _accentHost = C<WrapPanel>("AccentHost");
            _pinHost = C<WrapPanel>("PinHost");
            _titleHost = C<StackPanel>("TitleHost");
            _txtNoTitlesYet = C<TextBlock>("TxtNoTitlesYet");
            _txtNoPinsYet = C<TextBlock>("TxtNoPinsYet");
            _txtPinCount = C<TextBlock>("TxtPinCount");
            _txtWardrobeSlots = C<TextBlock>("TxtWardrobeSlots");
            _txtWardrobeEmpty = C<TextBlock>("TxtWardrobeEmpty");

            C<Button>("BtnArrange").Click += (_, _) => BtnArrange_Click();
            C<Button>("BtnReset").Click += (_, _) => BtnReset_Click();
            C<Button>("BtnCancel").Click += (_, _) => Close(false);
            C<Button>("BtnSave").Click += (_, _) => Close(true);

            _draft = (current ?? new ProfileCosmetics()).Clone();
            _unlocked = (unlocked ?? Enumerable.Empty<(string, string)>()).ToList();

            BuildBanners();
            BuildAvatars();
            BuildAccents();
            BuildTitles();
            BuildPins();
            BuildWardrobe();
        }

        /// <summary>A tile answers a click like WPF's MouseLeftButtonUp, and Enter/Space once tabbed to (P17).</summary>
        private static void Clickable(Border tile, Action act)
        {
            tile.Focusable = true;
            tile.PointerReleased += (_, _) => act();
            tile.KeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Enter or Key.Space)) return;
                act();
                e.Handled = true;
            };
        }

        // ============================== banner ==============================

        private void BuildBanners()
        {
            _bannerHost.Children.Add(BuildBannerTile(NoneKey, Loc.Get("profile_customize_none"), null));

            foreach (var banner in CosmeticsPool.Banners)
            {
                // A banner whose art will not load is not offered at all - better than a tile that
                // looks broken and equips to nothing. Thumbnails, not the card-sized decodes.
                var art = ModArt.Banner(banner.Id, 256);
                if (art == null) continue;
                _bannerHost.Children.Add(BuildBannerTile(banner.Id, banner.Name, art));
            }

            SelectBanner(_draft.BannerId ?? NoneKey);
        }

        private Border BuildBannerTile(string key, string label, IBrush? art)
        {
            var content = new Grid { Width = 128, Height = 52 };

            if (art != null)
            {
                content.Children.Add(new Border { Background = art, IsHitTestVisible = false });
                content.Children.Add(new Border { Background = Brush.Parse("#99000000"), IsHitTestVisible = false });
            }

            content.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
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
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = content
            };
            ToolTip.SetTip(tile, label);
            Clickable(tile, () => SelectBanner(key));

            _bannerTiles[key] = tile;
            return tile;
        }

        private void SelectBanner(string key)
        {
            _draft.BannerId = key == NoneKey ? null : key;
            foreach (var (id, tile) in _bannerTiles)
                tile.BorderBrush = id == key ? SelectedBorder : IdleBorder;
        }

        // ============================== preset avatar ==============================

        private void BuildAvatars()
        {
            _avatarHost.Children.Add(BuildAvatarTile(NoneKey, Loc.Get("profile_customize_none"), null));

            foreach (var preset in CosmeticsPool.AvatarPresets)
            {
                // Same rule as banners: a preset whose art will not load is not offered at all.
                var image = ModArt.AvatarPreset(preset.Id);
                if (image == null) continue;
                _avatarHost.Children.Add(BuildAvatarTile(preset.Id, preset.Name, new ImageBrush(image) { Stretch = Stretch.UniformToFill }));
            }

            SelectAvatar(_draft.AvatarId ?? NoneKey);
        }

        private Border BuildAvatarTile(string key, string label, IBrush? art)
        {
            var circle = new Border
            {
                Width = 56,
                Height = 56,
                CornerRadius = new CornerRadius(28),
                Background = art ?? TileBg,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            var tile = new Border
            {
                Width = 64,
                Height = 64,
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(32),
                Background = Brushes.Transparent,
                BorderBrush = IdleBorder,
                BorderThickness = new Thickness(2),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = circle
            };
            ToolTip.SetTip(tile, label);
            Clickable(tile, () => SelectAvatar(key));

            _avatarTiles[key] = tile;
            return tile;
        }

        private void SelectAvatar(string key)
        {
            _draft.AvatarId = key == NoneKey ? null : key;
            foreach (var (id, tile) in _avatarTiles)
                tile.BorderBrush = id == key ? SelectedBorder : IdleBorder;
        }

        // ============================== accent ==============================

        private void BuildAccents()
        {
            _accentHost.Children.Add(BuildAccentTile(NoneKey, null));
            foreach (var hex in ProfileCosmetics.AccentSwatches)
                _accentHost.Children.Add(BuildAccentTile(hex, hex));

            SelectAccent(_draft.Accent ?? NoneKey);
        }

        private Border BuildAccentTile(string key, string? hex)
        {
            var swatch = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = hex != null ? Brush.Parse(hex) : Brush.Parse("#26FFFFFF"),
                IsHitTestVisible = false
            };

            if (hex == null)
            {
                swatch.Child = new TextBlock
                {
                    Text = "✕",
                    Foreground = Muted,
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
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = swatch
            };
            ToolTip.SetTip(tile, hex ?? Loc.Get("profile_customize_none"));
            Clickable(tile, () => SelectAccent(key));

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
            _titleHost.Children.Add(BuildTitleRow(NoneKey, Loc.Get("profile_customize_no_title")));

            // The caller supplies each name already resolved (Core Achievement.TitleName + CoreMods.MakeModAware,
            // what WPF MainWindow.ResolveAchievementTitle does).
            foreach (var (id, name) in _unlocked)
                _titleHost.Children.Add(BuildTitleRow(id, name));

            _txtNoTitlesYet.IsVisible = _unlocked.Count == 0;

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
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock
                {
                    Text = label,
                    Foreground = Brushes.White,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            Clickable(row, () => SelectTitle(key));

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
                var tile = BuildPinTile(achievement.Id, achievement.Name);
                if (tile != null) _pinHost.Children.Add(tile);
            }

            _txtNoPinsYet.IsVisible = _pinHost.Children.Count == 0;

            // Drop pins whose tile is not on offer here, so the counter matches what is visible.
            _draft.PinnedAchievements = _draft.PinnedAchievements
                .Where(id => _pinTiles.ContainsKey(id))
                .Take(ProfileCosmetics.MaxPinnedAchievements)
                .ToList();

            RefreshPinVisuals();
        }

        private Border? BuildPinTile(string achievementId, string name)
        {
            // WPF LoadAchievementArt: mod override first, the shipped achievements/ PNG second; a pin
            // whose art will not load is not offered (WPF BuildPinTile returns null).
            var art = Achievement.All.TryGetValue(achievementId, out var achievement)
                ? ModArt.TryLoad($"achievements/{achievement.ImageName}", 116)
                : null;
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
                IsVisible = false,
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
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = content
            };
            ToolTip.SetTip(tile, name);
            Clickable(tile, () => TogglePin(achievementId));

            _pinTiles[achievementId] = tile;
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
                    _txtPinCount.Text = Loc.GetF("profile_customize_pins_full", ProfileCosmetics.MaxPinnedAchievements);
                    _txtPinCount.Foreground = Alert;
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
                    if (star != null) star.IsVisible = on;
                }
            }

            _txtPinCount.Text = Loc.GetF("profile_customize_pins_count",
                _draft.PinnedAchievements.Count, ProfileCosmetics.MaxPinnedAchievements);
            _txtPinCount.Foreground = Muted;
        }

        // ============================== wardrobe (Phase 3) ==============================

        /// <summary>
        /// Mod tabs + the two slot groups from Core WardrobeCatalog (WPF BuildWardrobe). Items whose
        /// PNG did not ship are not offered.
        /// </summary>
        private void BuildWardrobe()
        {
            try
            {
                var mods = WardrobeCatalog.Mods
                    .Where(m => WardrobeCatalog.ItemsFor(m, true).Any(i => WardrobeCatalog.HasArtFile(i.Id))
                             || WardrobeCatalog.ItemsFor(m, false).Any(i => WardrobeCatalog.HasArtFile(i.Id)))
                    .ToList();

                if (mods.Count == 0)
                {
                    this.FindControl<WrapPanel>("WardrobeModTabs")!.IsVisible = false;
                    this.FindControl<StackPanel>("WardrobeGroups")!.IsVisible = false;
                    _txtWardrobeEmpty.IsVisible = true;
                    RefreshWardrobeSlots();
                    return;
                }

                foreach (var mod in mods)
                    this.FindControl<WrapPanel>("WardrobeModTabs")!.Children.Add(BuildModTab(mod));

                // Open on the mod the app is actually running, when it has a tab.
                var active = CoreMods.ActiveModId;
                SelectMod(mods.FirstOrDefault(m => string.Equals(m, active, StringComparison.OrdinalIgnoreCase)) ?? mods[0]);
            }
            catch (Exception ex)
            {
                Log.Debug("ProfileCustomizeDialog: wardrobe build failed: {E}", ex.Message);
                this.FindControl<Border>("WardrobeSection")!.IsVisible = false;
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
                Cursor = new Cursor(StandardCursorType.Hand),
                // Registry mod ids are plain English buckets - displayed, not localized.
                Child = new TextBlock
                {
                    Text = mod.Length > 0 ? char.ToUpperInvariant(mod[0]) + mod.Substring(1) : mod,
                    Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeight.SemiBold
                }
            };
            Clickable(tab, () => SelectMod(mod));
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

            var decoHost = this.FindControl<WrapPanel>("WardrobeDecoHost")!;
            var charmHost = this.FindControl<WrapPanel>("WardrobeCharmHost")!;
            _wardrobeTiles.Clear();
            decoHost.Children.Clear();
            charmHost.Children.Clear();

            FillWardrobeHost(decoHost, WardrobeCatalog.ItemsFor(mod, true));
            FillWardrobeHost(charmHost, WardrobeCatalog.ItemsFor(mod, false));

            this.FindControl<TextBlock>("TxtWardrobeDecoHeader")!.IsVisible = decoHost.Children.Count > 0;
            this.FindControl<TextBlock>("TxtWardrobeCharmHeader")!.IsVisible = charmHost.Children.Count > 0;
            _txtWardrobeEmpty.IsVisible = decoHost.Children.Count == 0 && charmHost.Children.Count == 0;

            RefreshWardrobeVisuals();
        }

        private void FillWardrobeHost(WrapPanel host, IReadOnlyList<WardrobeItem> items)
        {
            foreach (var item in items)
                if (Helpers.ModArt.Wardrobe(item.Id) is { } art)   // art never shipped - not on offer
                    host.Children.Add(BuildWardrobeTile(item, art));
        }

        private Border BuildWardrobeTile(WardrobeItem item, IImage art)
        {
            var locked = !WardrobeCatalog.IsUnlockedForCurrentUser(item);

            var content = new Grid();
            if (locked)
            {
                // Silhouette: the art's alpha as an opacity mask over a near-black fill.
                content.Children.Add(new global::Avalonia.Controls.Shapes.Rectangle
                {
                    Fill = SilhouetteFill,
                    OpacityMask = new ImageBrush((IImageBrushSource)art) { Stretch = Stretch.Uniform },
                    Margin = new Thickness(4),
                    IsHitTestVisible = false
                });
                content.Children.Add(new TextBlock
                {
                    Text = "🔒", FontSize = 11, Opacity = 0.85, IsHitTestVisible = false,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 3, 2)
                });
            }
            else
            {
                content.Children.Add(new Image { Source = art, Stretch = Stretch.Uniform, Margin = new Thickness(4), IsHitTestVisible = false });
            }
            content.Children.Add(new TextBlock
            {
                Text = "✓", Foreground = SelectedCyan, FontSize = 12, FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 4, 0), IsVisible = false, Tag = "check", IsHitTestVisible = false
            });

            var tile = new Border
            {
                Width = 72, Height = 72,
                Margin = new Thickness(0, 0, 7, 7),
                CornerRadius = new CornerRadius(8),
                Background = TileBg,
                BorderBrush = IdleBorder,
                BorderThickness = new Thickness(2),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = content
            };
            // Item names are plain English proper nouns in the registry - not localized.
            // A locked tile teases the gate instead of naming the item.
            ToolTip.SetTip(tile, locked ? Loc.GetF("profile_customize_wardrobe_locked_tip", GateName(item)) : item.Name);
            Clickable(tile, () => ToggleWardrobeItem(item));

            _wardrobeTiles[item.Id] = tile;
            return tile;
        }

        /// <summary>Localized name of the achievement gating an item, for lock copy.</summary>
        private static string GateName(WardrobeItem item)
        {
            var gate = item.RequiredAchievementId;
            return gate != null && Achievement.All.TryGetValue(gate, out var ach) ? ach.LocalizedName : gate ?? string.Empty;
        }

        /// <summary>Equip, or unequip when it is already worn (WPF ToggleWardrobeItem).</summary>
        internal void ToggleWardrobeItem(WardrobeItem item)
        {
            if (!WardrobeCatalog.IsUnlockedForCurrentUser(item))
            {
                _txtWardrobeSlots.Text = Loc.Get("profile_customize_wardrobe_locked_click");
                _txtWardrobeSlots.Foreground = Alert;
                return;
            }

            if (item.IsCharm)
            {
                if (_draft.Charms.Contains(item.Id))
                    _draft.Charms.Remove(item.Id);
                else if (_draft.Charms.Count >= ProfileCosmetics.MaxCharms)
                {
                    _txtWardrobeSlots.Text = Loc.GetF("profile_customize_wardrobe_charms_full", ProfileCosmetics.MaxCharms);
                    _txtWardrobeSlots.Foreground = Alert;
                    return;
                }
                else
                    _draft.Charms.Add(item.Id);
            }
            else
            {
                _draft.AvatarDeco = string.Equals(_draft.AvatarDeco, item.Id, StringComparison.Ordinal) ? null : item.Id;
            }

            RefreshWardrobeVisuals();
        }

        private void RefreshWardrobeVisuals()
        {
            foreach (var (id, tile) in _wardrobeTiles)
            {
                var on = string.Equals(_draft.AvatarDeco, id, StringComparison.Ordinal) || _draft.Charms.Contains(id);
                tile.BorderBrush = on ? SelectedCyan : IdleBorder;
                tile.Background = on ? SelectedCyanBg : TileBg;
                if (tile.Child is Grid grid
                    && grid.Children.OfType<TextBlock>().FirstOrDefault(t => (t.Tag as string) == "check") is { } check)
                    check.IsVisible = on;
            }

            RefreshWardrobeSlots();
        }

        /// <summary>
        /// "Decoration 1/1 · Charms 2/2". Counts the LOADOUT, not the visible grid: an item from a
        /// mod tab you are not looking at is still equipped.
        /// </summary>
        private void RefreshWardrobeSlots()
        {
            _txtWardrobeSlots.Text = Loc.GetF("profile_customize_wardrobe_slots",
                string.IsNullOrWhiteSpace(_draft.AvatarDeco) ? 0 : 1,
                _draft.Charms.Count,
                ProfileCosmetics.MaxCharms);
            _txtWardrobeSlots.Foreground = Muted;
        }

        /// <summary>
        /// Opens the Wardrobe editor on the SAME draft instance, exactly as WPF does — it edits the
        /// transform fields in place and snapshots them on open, so its own Cancel is the undo and
        /// this method has nothing to write back.
        ///
        /// Hands it the hero card's avatar and measured size, as WPF does (zero = the editor's
        /// documented fallback stage).
        /// </summary>
        private async void BtnArrange_Click()
        {
            try
            {
                await new WardrobeEditorDialog(_draft, _editorAvatar, _cardWidth, _cardHeight).ShowDialogSafe(this);
            }
            catch (Exception ex)
            {
                Log.Debug("ProfileCustomizeDialog: wardrobe editor failed: {E}", ex.Message);
            }
        }

        // ============================== footer ==============================

        private void BtnReset_Click()
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
    }
}
