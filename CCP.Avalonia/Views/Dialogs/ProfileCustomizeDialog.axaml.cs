using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// The Trainer Card customization kit (Profile redesign Phase 2). Edits a CLONE of the
    /// viewer's loadout and exposes it as <see cref="Result"/> on OK, so Cancel really cancels
    /// and nothing here can half-write settings.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/ProfileCustomizeDialog.xaml.cs. The tile
    /// builders, selection rules, pin cap and reset are the original's; <see cref="ProfileCosmetics"/>
    /// is already in Core. What is not: Achievement, CosmeticsCatalog, WardrobeCatalog,
    /// ModResourceResolver and the WardrobeEditorDialog all live in the WPF head, so
    ///  - unlocked achievements arrive as (id, name) pairs instead of being resolved from Achievement.All,
    ///  - banners are the catalog's three generated gradients (no art file needed), avatar presets
    ///    are none (art needed), pins draw a trophy glyph where the achievement PNG would be,
    ///  - the wardrobe takes the original's "no registry" branch.
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

        // ponytail: CosmeticsCatalog.Banners lives in the WPF head. These are its three generated
        // gradients (id, name, stops), which need no art file; the scene banners come with it.
        private static readonly (string Id, string Name, string A, string B, string C)[] PlaceholderBanners =
        {
            ("gradient_velvet", "Velvet", "#2A1E4D", "#3B2159", "#1E1E3F"),
            ("gradient_bloom",  "Bloom",  "#5A1B3D", "#8A2B63", "#2A1230"),
            ("gradient_drone",  "Drone",  "#0E2A38", "#164A5E", "#0A1622"),
        };

        private readonly WrapPanel _bannerHost, _avatarHost, _accentHost, _pinHost;
        private readonly StackPanel _titleHost;
        private readonly TextBlock _txtNoTitlesYet, _txtNoPinsYet, _txtPinCount, _txtWardrobeSlots, _txtWardrobeEmpty;

        /// <summary>The edited loadout. Only meaningful when ShowDialog() returned true.</summary>
        public ProfileCosmetics Result => _draft;

        /// <summary>Render/design constructor: sample data so --render-view can draw the dialog.</summary>
        public ProfileCustomizeDialog() : this(
            new ProfileCosmetics
            {
                BannerId = "gradient_bloom",
                Accent = "#B478FF",
                TitleId = "first_session",
                PinnedAchievements = new List<string> { "first_session", "night_owl" }
            },
            new[] { ("first_session", "First Session"), ("night_owl", "Night Owl"), ("marathon", "Marathon") })
        { }

        public ProfileCustomizeDialog(ProfileCosmetics current, IEnumerable<(string Id, string Name)>? unlocked)
        {
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

        // ============================== banner ==============================

        private void BuildBanners()
        {
            _bannerHost.Children.Add(BuildBannerTile(NoneKey, Loc.Get("profile_customize_none"), null));

            foreach (var (id, name, a, b, c) in PlaceholderBanners)
            {
                var art = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.Parse(a), 0),
                        new GradientStop(Color.Parse(b), 0.5),
                        new GradientStop(Color.Parse(c), 1),
                    }
                };
                _bannerHost.Children.Add(BuildBannerTile(id, name, art));
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
            tile.PointerReleased += (_, _) => SelectBanner(key);

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

            // ponytail: needs CosmeticsCatalog.AvatarPresets + GetAvatarImage (WPF head, pack:// art),
            // wired when the catalog moves to Core. Same rule as WPF: no art, no tile.

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
            tile.PointerReleased += (_, _) => SelectAvatar(key);

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
            tile.PointerReleased += (_, _) => SelectAccent(key);

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

            // ponytail: WPF resolves the worn name via MainWindow.ResolveAchievementTitle (mod-aware);
            // here the caller supplies the name with the id.
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
            row.PointerReleased += (_, _) => SelectTitle(key);

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
                _pinHost.Children.Add(BuildPinTile(achievement.Id, achievement.Name));

            _txtNoPinsYet.IsVisible = _pinHost.Children.Count == 0;

            // Drop pins whose tile is not on offer here, so the counter matches what is visible.
            _draft.PinnedAchievements = _draft.PinnedAchievements
                .Where(id => _pinTiles.ContainsKey(id))
                .Take(ProfileCosmetics.MaxPinnedAchievements)
                .ToList();

            RefreshPinVisuals();
        }

        private Border BuildPinTile(string achievementId, string name)
        {
            var content = new Grid();
            // ponytail: needs ModResourceResolver + Resources/achievements/<png> (WPF head); a
            // trophy glyph stands in for the art until the achievement assets move to Core.
            content.Children.Add(new TextBlock
            {
                Text = "🏆",
                FontSize = 26,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
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
            tile.PointerReleased += (_, _) => TogglePin(achievementId);

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

        private void BuildWardrobe()
        {
            // ponytail: needs WardrobeCatalog (registry.json + per-item PNGs, WPF head), wired when it
            // moves to Core. This is the original's "no registry, or no art installed" branch verbatim.
            this.FindControl<WrapPanel>("WardrobeModTabs")!.IsVisible = false;
            this.FindControl<StackPanel>("WardrobeGroups")!.IsVisible = false;
            _txtWardrobeEmpty.IsVisible = true;
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

        private void BtnArrange_Click()
        {
            // ponytail: needs WardrobeEditorDialog (WPF head, bucket E drag/rotate canvas), ported separately
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
            RefreshWardrobeSlots();
        }
    }
}
