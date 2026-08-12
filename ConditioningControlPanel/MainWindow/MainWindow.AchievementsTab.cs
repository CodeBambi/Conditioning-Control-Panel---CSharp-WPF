using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Achievements tab: reward cards, counts, filters, and season recap.
    //
    // The redesign in one paragraph: everything an achievement has to say is on the card FACE.
    // Hero art dominates, the info line says how to earn it (locked) or the flavor (earned), and
    // a full-width reward band at the bottom shows what the achievement pays out - a flat cut-out
    // of the wardrobe item you have not earned yet, or the item in full colour with a glow once
    // you have. No hover drawer: hover only tilts the badge and warms the border. Everything
    // about the locked state is deliberately shape-without-colour: you can see there IS a bow,
    // you cannot see whose it is.
    public partial class MainWindow
    {
        #region Achievements Tab

        // ---- tuning ---------------------------------------------------------------------

        private const double AchvBadgePx = 150;     // hero art on the card face
        private const double AchvRewardIconPx = 40; // reward art in the bottom band

        private static readonly SolidColorBrush AchvMutedBrush = FrozenBrush(0x9A, 0x93, 0xB8);
        private static readonly SolidColorBrush AchvDimBrush = FrozenBrush(0x80, 0x79, 0xA3);
        private static readonly SolidColorBrush AchvTickBrush = FrozenBrush(0x5E, 0xC8, 0xF2);
        private static readonly SolidColorBrush AchvRuleBrush =
            FrozenBrush(0x33, 0xFF, 0xFF, 0xFF);

        // ---- state ----------------------------------------------------------------------

        /// <summary>Everything the card needs to redraw itself without walking the visual tree.</summary>
        private sealed class AchievementCardParts
        {
            public Models.Achievement Achievement = null!;
            public ToggleButton Card = null!;
            public Image Badge = null!;
            public TextBlock NameText = null!;
            /// <summary>Requirement while locked, flavor once earned.</summary>
            public TextBlock InfoText = null!;
            /// <summary>The bottom band's content host; rebuilt whole on unlock-state changes.</summary>
            public Grid RewardBand = null!;
            public Services.WardrobeItem? Reward;
        }

        private readonly Dictionary<string, ToggleButton> _achievementCards = new(StringComparer.Ordinal);
        private readonly Dictionary<ToggleButton, AchievementCardParts> _achievementCardParts = new();
        /// <summary>achievement id → the wardrobe item it pays out (first gate in registry order wins).</summary>
        private readonly Dictionary<string, Services.WardrobeItem> _achievementRewards = new(StringComparer.Ordinal);
        private readonly List<ToggleButton> _achievementFilterChips = new();
        private string _achievementFilter = AchvFilterAll;

        private const string AchvFilterAll = "all";
        private const string AchvFilterUnlocked = "unlocked";
        private const string AchvFilterLocked = "locked";
        private const string AchvFilterRewards = "rewards";

        // =================================================================================

        private void BtnAchievements_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("achievements");
        }

        private void BtnCompanion_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("companion");
        }

        private void BtnLeaderboard_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("leaderboard");
            // Surface the Season Recap re-view button only when a persisted snapshot exists.
            try
            {
                if (LeaderboardTab.BtnViewSeasonRecap != null)
                    LeaderboardTab.BtnViewSeasonRecap.Visibility = Services.SeasonRecapService.HasAnySnapshot()
                        ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SeasonRecap: failed to update re-view button visibility");
            }
        }

        /// <summary>Re-view the most recent season's recap card from its persisted snapshot.</summary>
        internal void BtnViewSeasonRecap_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("season_recap"); } catch { }
            try
            {
                var snapshot = Services.SeasonRecapService.LoadLatest();
                if (snapshot == null)
                {
                    App.Notifications?.Show(Loc.Get("recap_toast_none"), Services.NotificationType.Info);
                    return;
                }
                var vm = new ViewModels.SeasonRecapViewModel(snapshot);
                var win = new Controls.SeasonRecapWindow(vm) { Owner = this };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SeasonRecap: failed to open re-view window");
            }
        }

        // ============================== counters ==============================

        private void UpdateAchievementCount()
        {
            if (App.Achievements == null) return;

            // Free and patron counts are kept strictly separate — never summed.
            if (AchievementsTab.TxtAchievementCount != null)
            {
                var unlocked = App.Achievements.GetUnlockedCount(exclusive: false);
                var total = App.Achievements.GetTotalCount(exclusive: false);
                AchievementsTab.TxtAchievementCount.Text = Loc.GetF("label_0_1_achievements_unlocked", unlocked, total);
            }

            if (AchievementsTab.TxtPatronAchievementCount != null)
            {
                var pUnlocked = App.Achievements.GetUnlockedCount(exclusive: true);
                var pTotal = App.Achievements.GetTotalCount(exclusive: true);
                AchievementsTab.TxtPatronAchievementCount.Text = Loc.GetF("label_0_1_achievements_unlocked", pUnlocked, pTotal);
            }

            UpdateRewardCount();

            // Free users see the patron collection as a labeled, locked section.
            if (AchievementsTab.PatronAchievementsOverlay != null)
            {
                AchievementsTab.PatronAchievementsOverlay.Visibility = App.Patreon?.HasPremiumAccess == true
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        /// <summary>
        /// The third, independent counter: how many registry-gated wardrobe items the user has
        /// actually earned. Counted off the REGISTRY (item → achievement), not off the achievement
        /// list, so an achievement that gates two items counts twice and one that gates none is not
        /// in the denominator at all. Never folded into the free/patron achievement counts.
        /// </summary>
        private void UpdateRewardCount()
        {
            var label = AchievementsTab?.TxtRewardCount;
            if (label == null) return;
            try
            {
                var gates = Services.WardrobeCatalog.AchievementGates();
                if (gates == null || gates.Count == 0)
                {
                    // Nothing in the registry is gated - the line would read "0 / 0" forever.
                    label.Visibility = Visibility.Collapsed;
                    return;
                }

                int earned = gates.Count(g => IsAchievementUnlocked(g.Value));
                label.Visibility = Visibility.Visible;
                label.Text = LocFmtOr("achv_reward_count", "{0} / {1} wardrobe items earned",
                                      earned, gates.Count);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("UpdateRewardCount: {E}", ex.Message);
                label.Visibility = Visibility.Collapsed;
            }
        }

        // ============================== grid build ==============================

        private void PopulateAchievementGrid()
        {
            if (AchievementsTab?.AchievementGrid == null) return;

            AchievementsTab.AchievementGrid.Children.Clear();
            AchievementsTab.PatronAchievementGrid?.Children.Clear();
            _achievementImages.Clear();
            _achievementCards.Clear();
            _achievementCardParts.Clear();
            _achievementRewards.Clear();
            // The old cards are gone; drop the FX bookkeeping with them.
            _achievementTilesUnlocked.Clear();
            _achievementTiltTargets.Clear();

            BuildAchievementRewardMap();

            var cardStyle = TryFindResource("AchievementCard") as Style;

            foreach (var kvp in Models.Achievement.All)
            {
                var achievement = kvp.Value;
                // Skip parked achievements (no reachable unlock path in this build).
                if (achievement.IsHidden) continue;

                _achievementRewards.TryGetValue(achievement.Id, out var reward);
                var card = BuildAchievementCard(achievement, reward, cardStyle);

                if (achievement.IsExclusive)
                    AchievementsTab.PatronAchievementGrid?.Children.Add(card);
                else
                    AchievementsTab.AchievementGrid.Children.Add(card);
            }

            BuildAchievementFilters();
            ApplyAchievementFilter();
            UpdateAchievementCount();
            App.Logger?.Information("Achievement grid populated with {Count} cards ({Rewards} carry a wardrobe reward)",
                                    _achievementCards.Count, _achievementRewards.Count);
        }

        /// <summary>
        /// achievement id → wardrobe item, reversed out of the registry's item → achievement gates.
        /// Walked in REGISTRY order (not the gate dictionary's) so "first wins" is deterministic:
        /// two items gated on one achievement always resolve to the same one across launches.
        /// </summary>
        private void BuildAchievementRewardMap()
        {
            try
            {
                var gates = Services.WardrobeCatalog.AchievementGates();
                if (gates == null || gates.Count == 0) return;

                foreach (var item in Services.WardrobeCatalog.Items)
                {
                    var gate = item.RequiredAchievementId;
                    if (string.IsNullOrEmpty(gate)) continue;
                    if (!_achievementRewards.ContainsKey(gate!)) _achievementRewards[gate!] = item;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BuildAchievementRewardMap: {E}", ex.Message);
            }
        }

        private ToggleButton BuildAchievementCard(Models.Achievement achievement,
                                                  Services.WardrobeItem? reward,
                                                  Style? cardStyle)
        {
            var unlocked = IsAchievementUnlocked(achievement.Id);

            var card = new ToggleButton
            {
                Style = cardStyle,
                Tag = achievement.Id,
                IsChecked = false,
            };

            // -- hero art. Its own host so the holo-foil tilt has something to rotate that is
            //    NOT the card (rotating the card would drag the reward band along).
            var badgeHost = new Grid
            {
                Width = AchvBadgePx,
                Height = AchvBadgePx,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var image = new Image
            {
                Width = AchvBadgePx,
                Height = AchvBadgePx,
                Stretch = Stretch.Uniform,
                Source = LoadAchievementImage(achievement.ImageName),
            };
            if (!unlocked) image.Effect = new BlurEffect { Radius = 15 };
            badgeHost.Children.Add(image);

            var nameText = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                // Pinned line height so the height cap cuts on a line boundary, never mid-glyph.
                LineHeight = 17,
                MaxHeight = 34,
                Margin = new Thickness(10, 6, 10, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Text = unlocked ? AchName(achievement) : LocOr("achv_card_locked_name", "???"),
            };

            // Requirement while locked, flavor once earned - the line that used to hide in the
            // drawer, now spent on the dead zone between name and band.
            var infoText = new TextBlock
            {
                FontSize = 11,
                Foreground = AchvDimBrush,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                // Pinned line height: exactly three lines, cut on a boundary, never mid-glyph.
                LineHeight = 14,
                MaxHeight = 42,
                Margin = new Thickness(12, 4, 12, 4),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var band = new Grid();

            // Rows: hero art / name / info (absorbs all slack) / reward band on the bottom edge.
            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(AchvBadgePx + 14) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(badgeHost, 0);
            Grid.SetRow(nameText, 1);
            Grid.SetRow(infoText, 2);

            var bandChrome = new Border
            {
                Height = 56,
                Background = FrozenBrush(0xF2, 0x25, 0x25, 0x42),
                BorderBrush = AchvRuleBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                CornerRadius = new CornerRadius(0, 0, 14, 14),
                Child = band,
            };
            Grid.SetRow(bandChrome, 3);

            content.Children.Add(badgeHost);
            content.Children.Add(nameText);
            content.Children.Add(infoText);
            content.Children.Add(bandChrome);
            card.Content = content;

            var parts = new AchievementCardParts
            {
                Achievement = achievement,
                Card = card,
                Badge = image,
                NameText = nameText,
                InfoText = infoText,
                RewardBand = band,
                Reward = reward,
            };

            _achievementImages[achievement.Id] = image;
            _achievementCards[achievement.Id] = card;
            _achievementCardParts[card] = parts;

            ApplyAchievementInfoText(parts, unlocked);
            BuildRewardBand(parts);
            ApplyAchievementCardTooltip(parts, unlocked);

            // The card carries the transforms the entrance stagger and the unlock reveal need;
            // the badge host carries the hover tilt; the badge Image carries the hover pop.
            // Three separate transform groups on purpose - each animator owns exactly one.
            EnsureCardTransforms(card);
            PrepareAchievementTileFx(card, unlocked, badgeHost, image);

            return card;
        }

        // ============================== card content ==============================

        /// <summary>Locked: how to earn it. Earned: the flavor line (or the equip nudge when the
        /// achievement pays out an item).</summary>
        private void ApplyAchievementInfoText(AchievementCardParts parts, bool unlocked)
        {
            var infoText = parts.InfoText;
            if (!unlocked)
            {
                infoText.Text = AchReq(parts.Achievement);
                infoText.FontStyle = FontStyles.Normal;
                infoText.Foreground = AchvDimBrush;
            }
            else
            {
                infoText.Text = AchFlavor(parts.Achievement);
                infoText.FontStyle = FontStyles.Italic;
                infoText.Foreground = AchvMutedBrush;
            }
        }

        /// <summary>
        /// The always-visible bottom band: [reward art] "Reward" + item name ...... [lock / tick].
        /// Locked rewards are a flat silhouette and a "???" name - the shape, not the goods.
        /// </summary>
        private void BuildRewardBand(AchievementCardParts parts)
        {
            var band = parts.RewardBand;
            band.Children.Clear();
            band.ColumnDefinitions.Clear();

            var achievement = parts.Achievement;
            var reward = parts.Reward;
            var unlocked = IsAchievementUnlocked(achievement.Id);

            var row = new Grid { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 1. the goods (or the shape of them). Earned items glow - the band is the payoff.
            var icon = BuildRewardIcon(parts, AchvRewardIconPx, unlocked,
                                       glow: unlocked ? null : TryFindResource("PinkBrush") as Brush)
                       ?? CategoryGlyphBox(achievement, AchvRewardIconPx, 22, 0.3);
            if (unlocked && reward != null && icon is Image art)
            {
                art.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0xFF, 0x69, 0xB4),
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.55,
                };
            }
            icon.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(icon, 0);
            row.Children.Add(icon);

            // 2. "Reward" over the item's name. Registry names are plain English proper nouns and
            //    are NEVER localized (same rule the wardrobe picker follows); a locked item's name
            //    stays "???".
            var textStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 6, 0),
            };
            textStack.Children.Add(new TextBlock
            {
                Text = LocOr("achv_reward_header", "Reward"),
                FontSize = 9.5,
                Foreground = AchvDimBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            string rewardName;
            Brush rewardNameBrush;
            if (reward == null)
            {
                rewardName = LocOr("achv_reward_none", "No wardrobe reward");
                rewardNameBrush = AchvDimBrush;
            }
            else if (unlocked)
            {
                rewardName = reward.Name;
                rewardNameBrush = Brushes.White;
            }
            else
            {
                rewardName = LocOr("achv_card_locked_name", "???");
                rewardNameBrush = AchvMutedBrush;
            }
            textStack.Children.Add(new TextBlock
            {
                Text = rewardName,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = rewardNameBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(textStack, 1);
            row.Children.Add(textStack);

            // 3. the state
            FrameworkElement state = unlocked
                ? new TextBlock
                {
                    Text = "✓",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = AchvTickBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                }
                : new EmojiTextBlock
                {
                    Text = "🔒",
                    FontSize = 13,
                    Foreground = AchvMutedBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            Grid.SetColumn(state, 2);
            row.Children.Add(state);

            band.Children.Add(row);
        }

        /// <summary>
        /// The reward's art at <paramref name="size"/>: full colour once unlocked, a flat cut-out
        /// while locked. Null when the achievement pays out nothing, or when the item's PNG never
        /// shipped - callers substitute a category glyph rather than render an empty box.
        /// </summary>
        private FrameworkElement? BuildRewardIcon(AchievementCardParts parts, double size,
                                                  bool unlocked, Brush? glow)
        {
            var reward = parts.Reward;
            if (reward == null) return null;

            try
            {
                if (unlocked)
                {
                    var art = Services.WardrobeCatalog.GetImage(reward.Id);
                    if (art == null) return null;
                    return new Image
                    {
                        Width = size,
                        Height = size,
                        Stretch = Stretch.Uniform,
                        Source = art,
                        IsHitTestVisible = false,
                    };
                }
                return Silhouette.Build(reward.Id, size, Silhouette.DefaultFill, glow);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BuildRewardIcon: {E}", ex.Message);
                return null;
            }
        }

        /// <summary>A square box holding the achievement's category glyph, used wherever there is
        /// no reward art to show. Geometric glyphs only - no emoji font dependency.</summary>
        private static FrameworkElement CategoryGlyphBox(Models.Achievement achievement, double box,
                                                         double fontSize, double opacity)
        {
            var host = new Grid { Width = box, Height = box, IsHitTestVisible = false };
            host.Children.Add(new TextBlock
            {
                Text = CategoryGlyph(achievement.Category),
                FontSize = fontSize,
                Foreground = Brushes.White,
                Opacity = opacity,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            return host;
        }

        private static string CategoryGlyph(AchievementCategory category) => category switch
        {
            AchievementCategory.Progression => "▲",
            AchievementCategory.TimeSessions => "◆",
            AchievementCategory.Minigames => "●",
            AchievementCategory.Hardcore => "■",
            AchievementCategory.Deeper => "▼",
            AchievementCategory.Creator => "★",
            _ => "◆",
        };

        // ============================== filters ==============================

        /// <summary>
        /// All / Unlocked / Locked / Rewards, radio-style. Built once and reused across populates
        /// so the user's chosen filter survives a grid rebuild.
        /// </summary>
        private void BuildAchievementFilters()
        {
            var host = AchievementsTab?.AchievementFilters;
            if (host == null || host.Children.Count > 0) return;

            var chipStyle = TryFindResource("AchievementFilterChip") as Style;

            void Add(string key, string locKey, string fallback)
            {
                var chip = new ToggleButton
                {
                    Style = chipStyle,
                    Tag = key,
                    Content = LocOr(locKey, fallback),
                    IsChecked = string.Equals(key, _achievementFilter, StringComparison.Ordinal),
                };
                chip.Checked += AchievementFilterChip_Changed;
                chip.Unchecked += AchievementFilterChip_Changed;
                _achievementFilterChips.Add(chip);
                host.Children.Add(chip);
            }

            Add(AchvFilterAll, "achv_filter_all", "All");
            Add(AchvFilterUnlocked, "achv_filter_unlocked", "Unlocked");
            Add(AchvFilterLocked, "achv_filter_locked", "Locked");
            Add(AchvFilterRewards, "achv_filter_rewards", "Rewards");
        }

        private void AchievementFilterChip_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not ToggleButton chip || chip.Tag is not string key) return;

                if (chip.IsChecked != true)
                {
                    // Clicking the active chip must not leave the grid with no filter at all.
                    if (string.Equals(key, _achievementFilter, StringComparison.Ordinal))
                        chip.IsChecked = true;
                    return;
                }

                _achievementFilter = key;
                foreach (var other in _achievementFilterChips)
                    if (!ReferenceEquals(other, chip)) other.IsChecked = false;

                ApplyAchievementFilter();
            }
            catch (Exception ex) { App.Logger?.Debug("AchievementFilterChip_Changed: {E}", ex.Message); }
        }

        /// <summary>Hides cards in BOTH grids - the patron section filters with the free one.</summary>
        private void ApplyAchievementFilter()
        {
            try
            {
                foreach (var (card, parts) in _achievementCardParts)
                {
                    var unlocked = IsAchievementUnlocked(parts.Achievement.Id);
                    var show = _achievementFilter switch
                    {
                        AchvFilterUnlocked => unlocked,
                        AchvFilterLocked => !unlocked,
                        AchvFilterRewards => parts.Reward != null,
                        _ => true,
                    };
                    card.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyAchievementFilter: {E}", ex.Message); }
        }

        // ============================== refresh ==============================

        private BitmapImage? LoadAchievementImage(string imageName)
        {
            try
            {
                var image = Services.ModResourceResolver.ResolveImage($"achievements/{imageName}");
                return image as BitmapImage ?? new BitmapImage(new Uri($"pack://application:,,,/Resources/achievements/{imageName}", UriKind.Absolute));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to load achievement image {Name}: {Error}", imageName, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Brings one card back in line with the live unlock state: badge blur, name, tooltip,
        /// hover-tilt eligibility, the reward strip, the body (only if it was ever built) and the
        /// reward counter.
        /// </summary>
        private void RefreshAchievementTile(string achievementId)
        {
            if (!_achievementCards.TryGetValue(achievementId, out var card)) return;
            if (!_achievementCardParts.TryGetValue(card, out var parts)) return;

            var isUnlocked = IsAchievementUnlocked(achievementId);

            try
            {
                parts.Badge.Effect = isUnlocked ? null : new BlurEffect { Radius = 15 };
                parts.NameText.Text = isUnlocked
                    ? AchName(parts.Achievement)
                    : LocOr("achv_card_locked_name", "???");

                // A card that just unlocked starts offering the hover tilt (and one that was
                // somehow re-locked stops).
                SetAchievementTileUnlocked(card, isUnlocked);
                ApplyAchievementInfoText(parts, isUnlocked);
                BuildRewardBand(parts);
                ApplyAchievementCardTooltip(parts, isUnlocked);
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshAchievementTile: {E}", ex.Message); }

            UpdateAchievementCount();

            // An unlock can move the card out from under an active "Locked" filter.
            if (!string.Equals(_achievementFilter, AchvFilterAll, StringComparison.Ordinal))
                ApplyAchievementFilter();
        }

        private void RefreshAllAchievementTiles()
        {
            // Refresh all achievement cards to reflect current unlock state
            foreach (var achievementId in _achievementCards.Keys.ToList())
            {
                RefreshAchievementTile(achievementId);
            }
            App.Logger?.Debug("All achievement cards refreshed");
        }

        private void OnAchievementUnlockedInMainWindow(object? sender, Models.Achievement achievement)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshAchievementTile(achievement.Id);
                // Event FX (PR-5): tile reveal + burst, or a burst on the Achievements nav button
                // when the grid is not the tab on screen. See MainWindow.EventFx.cs.
                CelebrateAchievementUnlock(achievement.Id);
                App.Logger?.Information("Achievement tile refreshed: {Name}", achievement.Name);
            });
        }

        // ============================== text ==============================

        /// <summary>
        /// The tooltip. Previously this concatenated English literals ("???\n\nRequirement: ...")
        /// no matter the UI language - a live localization bug. It is now a single localized
        /// template per state, which also lets a translator move the reward line.
        /// </summary>
        private void ApplyAchievementCardTooltip(AchievementCardParts parts, bool unlocked)
        {
            var achievement = parts.Achievement;
            var rewardName = parts.Reward?.Name;

            string tip;
            if (unlocked)
            {
                tip = LocFmtOr("achv_tooltip_unlocked", "{0}\n\n\"{1}\"\n\nReward: {2}",
                               AchName(achievement),
                               AchFlavor(achievement),
                               rewardName ?? LocOr("achv_reward_none", "No wardrobe reward"));
            }
            else if (parts.Reward != null)
            {
                tip = LocFmtOr("achv_tooltip_locked",
                               "???\n\nHow to earn it: {0}\n\nReward: a locked wardrobe item.",
                               AchReq(achievement));
            }
            else
            {
                tip = LocFmtOr("achv_tooltip_locked_no_reward", "???\n\nHow to earn it: {0}",
                               AchReq(achievement));
            }
            parts.Card.ToolTip = tip;

            // Screen readers get the same two facts as the card face.
            AutomationProperties.SetName(parts.Card, unlocked
                ? LocFmtOr("achv_automation_unlocked", "{0}, unlocked. Reward: {1}.",
                           AchName(achievement),
                           rewardName ?? LocOr("achv_reward_none", "No item"))
                : LocFmtOr("achv_automation_locked", "Locked achievement. {0}. Reward locked.",
                           AchReq(achievement)));
        }

        private static bool IsAchievementUnlocked(string? achievementId)
        {
            if (string.IsNullOrEmpty(achievementId)) return false;
            try { return App.Achievements?.Progress.IsUnlocked(achievementId!) ?? false; }
            catch { return false; }
        }

        private static string AchName(Models.Achievement a)
            => ModAware(LocOr($"achievement_{a.Id}_name", a.Name));

        private static string AchReq(Models.Achievement a)
            => ModAware(LocOr($"achievement_{a.Id}_req", a.Requirement));

        private static string AchFlavor(Models.Achievement a)
            => ModAware(LocOr($"achievement_{a.Id}_flavor", a.FlavorText));

        private static string ModAware(string text)
        {
            try { return App.Mods?.MakeModAware(text) ?? text; }
            catch { return text; }
        }

        /// <summary>
        /// Same idea as <see cref="LocOr"/> (which lives in MainWindow.Marquee.cs and is reused
        /// here), for the templated strings: Loc.Get hands back the KEY when a string is missing,
        /// and "achv_tooltip_locked" on a tooltip is worse than shipped English.
        /// </summary>
        private static string LocFmtOr(string key, string fallbackTemplate, params object[] args)
        {
            var template = LocOr(key, fallbackTemplate);
            try { return string.Format(template, args); }
            catch (FormatException) { return template; }
        }

        private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
            => FrozenBrush(0xFF, r, g, b);

        private static SolidColorBrush FrozenBrush(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
        #endregion
    }
}
