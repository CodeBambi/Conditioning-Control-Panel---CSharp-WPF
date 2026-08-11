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
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Enhancements / Skill Tree tab: node layout, unlock logic, and skill-tree rendering.
    public partial class MainWindow
    {
        #region Enhancements (Skill Tree)

        // Node size constants for skill tree (sized for image backgrounds)
        private const double NodeWidth = 156;  // 10% smaller than 173
        private const double NodeHeight = 139;  // Includes name label row
        private const double TierSpacing = 350; // Much larger vertical spacing between tiers

        // Secret-skill rail cards (the strip under the tree). The height is a budget, not taste:
        // the rail shares a ~620dip tab with the fixed 460dip tree canvas, and that canvas has no
        // vertical scroll to spill into - anything taller here silently crops the bottom row of
        // skill nodes. Landscape cards keep three of them on one line at any window width.
        private const double SecretCardWidth = 180;
        private const double SecretCardHeight = 56;

        /// <summary>
        /// Refreshes the entire Enhancements tab UI
        /// </summary>
        private void RefreshEnhancementsUI()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            // The tab's FX (ambient dust canvas + the owned-node breath) wire themselves up on
            // first refresh - see MainWindow.EnhancementsFx.cs. Idempotent.
            EnsureEnhancementsFx();

            // Update skill points display
            EnhancementsTab.TxtSkillPoints.Text = settings.SkillPoints.ToString();

            // Update XP multiplier display
            var multiplier = App.SkillTree?.GetTotalXpMultiplier() ?? 1.0;
            EnhancementsTab.TxtXpMultiplier.Text = $"{multiplier:F2}x";

            // Update conditioning time display
            EnhancementsTab.TxtConditioningTime.Text = App.SkillTree?.GetFormattedConditioningTime() ?? "0h 0m";

            // Update Pink Rush indicator
            EnhancementsTab.TxtPinkRushIndicator.Visibility = settings.PinkRushActive ? Visibility.Visible : Visibility.Collapsed;

            // Draw the skill tree on canvas
            DrawSkillTree();

            // Fill the secret-skill rail under it (the tree itself never draws secrets)
            PopulateSecretSkills();

            // Update active bonuses panel
            RefreshActiveBonuses();
        }

        /// <summary>
        /// Draws the entire skill tree with nodes and connecting lines
        /// </summary>
        private void DrawSkillTree()
        {
            EnhancementsTab.SkillTreeCanvas.Children.Clear();
            // The nodes about to be rebuilt own the glows the shared breath drives.
            ResetOwnedNodeGlows();

            // Set animated background on the outer border
            EnhancementsTab.SkillTreeOuterBorder.Background = CreateAnimatedSkillTreeBrush(isHeader: false);

            // The tree's floating sparkles are now the AmbientFxCanvas DustField declared behind
            // the scroller in EnhancementsTabView.xaml: budgeted by the performance tier, tinted
            // from the mod palette, and parked with the tab. The 55 hand-rolled ellipses it
            // replaces each held their own Forever clock across the full 3760dip canvas - most of
            // them animating off-screen - which is exactly the "one focal loop" rule the FX plan
            // exists to enforce.
            _skillTreeAnimationsActive = true;

            // Add header section at the start of the canvas
            CreateSkillTreeHeader();

            // 3 LINEAR HORIZONTAL PATHS
            var nodePositions = new Dictionary<string, (double X, double Y)>();

            var startX = 570.0;  // Start after the header section (20 + 500 + 50 margin)
            var startY = 0.0;    // Align with header top
            var colSpacing = 270.0; // Horizontal spacing between nodes
            var rowSpacing = 160.0; // Vertical spacing between the 3 paths

            // COLUMN 0: Root node (centered, branches to 3 paths)
            var rootY = startY + rowSpacing; // Center vertically
            nodePositions["pink_hours"] = (startX, rootY);

            // PATH 1 (TOP ROW): ditzy_data branch
            var path1Y = startY;
            nodePositions["ditzy_data"] = (startX + colSpacing, path1Y);
            nodePositions["hive_mind"] = (startX + colSpacing * 2, path1Y);
            nodePositions["trophy_case"] = (startX + colSpacing * 3, path1Y);
            nodePositions["popular_girl"] = (startX + colSpacing * 4, path1Y);
            nodePositions["quest_refresh"] = (startX + colSpacing * 5, path1Y);
            nodePositions["better_quests"] = (startX + colSpacing * 6, path1Y);

            // PATH 2 (MIDDLE ROW): sparkle_boost_1 branch
            var path2Y = startY + rowSpacing;
            nodePositions["sparkle_boost_1"] = (startX + colSpacing, path2Y);
            nodePositions["sparkle_boost_2"] = (startX + colSpacing * 2, path2Y);
            nodePositions["lucky_bimbo"] = (startX + colSpacing * 3, path2Y);
            nodePositions["sparkle_boost_3"] = (startX + colSpacing * 4, path2Y);
            nodePositions["lucky_bubbles"] = (startX + colSpacing * 5, path2Y);
            nodePositions["pink_rush"] = (startX + colSpacing * 6, path2Y);

            // PATH 3 (BOTTOM ROW): good_girl_streak branch
            var path3Y = startY + rowSpacing * 2;
            nodePositions["good_girl_streak"] = (startX + colSpacing, path3Y);
            nodePositions["milestone_rewards"] = (startX + colSpacing * 2, path3Y);
            nodePositions["oopsie_insurance"] = (startX + colSpacing * 3, path3Y);
            nodePositions["streak_power"] = (startX + colSpacing * 4, path3Y);
            nodePositions["reroll_addict"] = (startX + colSpacing * 5, path3Y);
            nodePositions["perfect_bimbo_week"] = (startX + colSpacing * 6, path3Y);

            // TIER 6 (ANALYTICS ROW): single row after all three paths end, vertically
            // centered — the three paths visually converge into the Ditzy Data PRO chain
            nodePositions["ditzy_data_pro"] = (startX + colSpacing * 7, path2Y);
            nodePositions["season_rewind"] = (startX + colSpacing * 8, path2Y);
            nodePositions["bestie_records"] = (startX + colSpacing * 9, path2Y);
            nodePositions["brain_drain_report"] = (startX + colSpacing * 10, path2Y);
            nodePositions["certified_data_bimbo"] = (startX + colSpacing * 11, path2Y);

            // Draw connection lines first (so they're behind nodes)
            DrawConnectionLines(nodePositions);

            // Draw skill nodes. Secret skills are excluded on purpose: they have no position in
            // nodePositions and no prerequisite chain to hang a connection line off, and a card
            // sitting in the tree would announce them. They render in the rail underneath instead
            // (PopulateSecretSkills → EnhancementsTab.SecretSkills).
            foreach (var skill in Models.SkillDefinition.All.Where(s => !s.IsSecret))
            {
                if (nodePositions.TryGetValue(skill.Id, out var pos))
                {
                    var node = CreateSkillNode(skill);
                    Canvas.SetLeft(node, pos.X);
                    Canvas.SetTop(node, pos.Y);
                    EnhancementsTab.SkillTreeCanvas.Children.Add(node);
                }
            }

            // One clock for every owned node's glow, started only if the gate allows it.
            ApplyOwnedNodeBreath();
        }

        /// <summary>
        /// Creates the header panel at the start of the skill tree
        /// </summary>
        private void CreateSkillTreeHeader()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Main header border
            var headerBorder = new Border
            {
                Width = 500,
                Background = CreateAnimatedSkillTreeBrush(isHeader: true),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(15, 8, 15, 15) // Left, Top, Right, Bottom
            };
            Canvas.SetLeft(headerBorder, 5);
            Canvas.SetTop(headerBorder, 0);

            var mainStack = new StackPanel();

            // Title section
            var titleStack = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
            titleStack.Children.Add(new TextBlock
            {
                Text = "✨ " + (App.Mods?.GetEnhancementTreeTitle() ?? Loc.Get("label_enhancement_tree_title")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")),
                FontSize = 22,
                FontWeight = FontWeights.Bold
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = App.Mods?.GetEnhancementTreeSubtitle() ?? Loc.Get("label_enhancement_tree_subtitle"),
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 4, 0, 0)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = App.Mods?.GetEnhancementTreeWarning() ?? Loc.Get("label_enhancement_tree_warning"),
                Foreground = new SolidColorBrush(Color.FromRgb(136, 170, 204)),
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 2, 0, 0)
            });
            // Capstone badge (certified_data_bimbo owned)
            if (App.SkillTree?.HasSkill("certified_data_bimbo") == true)
            {
                var certifiedChip = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(70, 55, 25)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(255, 200, 80)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                certifiedChip.Child = new TextBlock
                {
                    Text = "🎓 " + Loc.Get("skill_certified_data_bimbo_name"),
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 80)),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold
                };
                titleStack.Children.Add(certifiedChip);
            }
            mainStack.Children.Add(titleStack);

            // Sparkle Points display
            var pointsBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(42, 42, 74)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(15, 10, 15, 10),
                Margin = new Thickness(0, 0, 0, 15)
            };
            var pointsStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            pointsStack.Children.Add(new TextBlock
            {
                Text = "💎",
                FontSize = 24,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });
            var pointsInfoStack = new StackPanel();
            pointsInfoStack.Children.Add(new TextBlock
            {
                Text = App.Mods?.GetPointsLabel() ?? Loc.Get("label_sparkle_points"),
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 10
            });
            pointsInfoStack.Children.Add(new TextBlock
            {
                Text = settings.SkillPoints.ToString(),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")),
                FontSize = 24,
                FontWeight = FontWeights.Bold
            });
            pointsStack.Children.Add(pointsInfoStack);
            pointsBorder.Child = pointsStack;
            mainStack.Children.Add(pointsBorder);

            // Prestige row — lifetime sparkle points SPENT (monotonic, survives seasons).
            // Re-buying seasonal nodes raises it: number always goes up.
            var lifetimeSpent = App.Achievements?.Progress?.LifetimeSkillPointsSpent ?? 0;
            var prestigeRank = 1 + (int)(lifetimeSpent / 100);
            var prestigeBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(52, 44, 28)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(15, 6, 15, 6),
                Margin = new Thickness(0, 0, 0, 15)
            };
            var prestigeStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            prestigeStack.Children.Add(new TextBlock
            {
                Text = "✦",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 80)),
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });
            var prestigeInfoStack = new StackPanel();
            prestigeInfoStack.Children.Add(new TextBlock
            {
                Text = Loc.Get("label_prestige"),
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 10
            });
            var prestigeValueStack = new StackPanel { Orientation = Orientation.Horizontal };
            prestigeValueStack.Children.Add(new TextBlock
            {
                Text = lifetimeSpent.ToString("N0"),
                Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 80)),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            prestigeValueStack.Children.Add(new TextBlock
            {
                Text = "  ✦ " + Loc.GetF("label_prestige_rank", prestigeRank),
                Foreground = new SolidColorBrush(Color.FromRgb(210, 180, 120)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            });
            prestigeInfoStack.Children.Add(prestigeValueStack);
            prestigeStack.Children.Add(prestigeInfoStack);
            prestigeBorder.Child = prestigeStack;
            prestigeBorder.ToolTip = Loc.Get("tooltip_prestige");
            mainStack.Children.Add(prestigeBorder);
            // Event FX (PR-5): remembered so a prestige rank-up can burst on the number that
            // changed. Re-pointed on every refresh, so it never holds a detached border.
            _prestigeRowBorder = prestigeBorder;

            // Ditzy Data Stats Toggle Button (only show if ditzy_data skill is unlocked)
            var hasDitzyData = App.SkillTree?.HasSkill("ditzy_data") == true;
            var ditzyButton = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(60, 40, 80)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = Cursors.Hand,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")),
                BorderThickness = new Thickness(1)
            };
            var ditzyButtonStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var ditzyArrow = new TextBlock
            {
                Text = " ▼",
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            ditzyButtonStack.Children.Add(new TextBlock
            {
                Text = "📊 ",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            ditzyButtonStack.Children.Add(new TextBlock
            {
                Text = App.Mods?.GetStatsTitle() ?? Loc.Get("label_ditzy_data_stats"),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentLightColorHex() ?? "#FFB6C1")),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            ditzyButtonStack.Children.Add(ditzyArrow);
            ditzyButton.Child = ditzyButtonStack;

            // Detailed Stats Box (initially hidden)
            var detailedStatsBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(22, 22, 42)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 15),
                Visibility = Visibility.Collapsed // Start hidden
            };
            var detailedStatsStack = new StackPanel();

            // Toggle click handler
            ditzyButton.MouseLeftButtonDown += (s, e) =>
            {
                var isCollapsed = detailedStatsBorder.Visibility == Visibility.Collapsed;
                detailedStatsBorder.Visibility = isCollapsed ? Visibility.Visible : Visibility.Collapsed;
                ditzyArrow.Text = isCollapsed ? " ▲" : " ▼";
            };
            if (hasDitzyData)
                mainStack.Children.Add(ditzyButton);

            // Stats title
            detailedStatsStack.Children.Add(new TextBlock
            {
                Text = "📊 " + (App.Mods?.GetStatsTitle() ?? "Ditzy Data Stats"),
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var achievements = App.Achievements?.Progress;
            if (achievements != null)
            {
                // Create a grid for stats layout (3 columns)
                var statsGrid = new Grid();
                statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                int row = 0;
                void AddStatRow(string label, string value, int column)
                {
                    var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
                    stack.Children.Add(new TextBlock
                    {
                        Text = label,
                        Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                        FontSize = 9
                    });
                    stack.Children.Add(new TextBlock
                    {
                        Text = value,
                        Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold
                    });
                    Grid.SetColumn(stack, column);
                    Grid.SetRow(stack, row);
                    statsGrid.Children.Add(stack);
                }

                // Row 1: Session stats
                statsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddStatRow(Loc.Get("label_sessions_started"), achievements.TotalSessionsStarted.ToString("N0"), 0);
                AddStatRow(Loc.Get("label_sessions_completed"), achievements.CompletedSessions.Count.ToString("N0"), 1);
                AddStatRow(Loc.Get("label_sessions_abandoned"), achievements.TotalSessionsAbandoned.ToString("N0"), 2);
                row++;

                // Row 2: XP & Skill Points
                statsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddStatRow(Loc.Get("label_total_xp_earned_stat"), achievements.TotalXPEarned.ToString("N0"), 0);
                AddStatRow(Loc.Get("label_skill_points_earned"), achievements.TotalSkillPointsEarned.ToString("N0"), 1);
                AddStatRow(Loc.Get("label_longest_session"), $"{achievements.LongestSessionMinutes:F1} {Loc.Get("label_min_abbrev")}", 2);
                row++;

                // Row 3: Attention checks
                statsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddStatRow(Loc.Get("label_attention_passes"), achievements.TotalAttentionChecksPassed.ToString("N0"), 0);
                AddStatRow(Loc.Get("label_video_att_passed"), achievements.VideoAttentionChecksPassed.ToString("N0"), 1);
                AddStatRow(Loc.Get("label_video_att_failed"), achievements.VideoAttentionChecksFailed.ToString("N0"), 2);
                row++;

                // Row 4: Bubble count
                statsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddStatRow(Loc.Get("label_bubble_count_games"), achievements.TotalBubbleCountGames.ToString("N0"), 0);
                AddStatRow(Loc.Get("label_bc_correct"), achievements.TotalBubbleCountCorrect.ToString("N0"), 1);
                AddStatRow(Loc.Get("label_bc_best_streak"), achievements.BubbleCountBestStreak.ToString("N0"), 2);
                row++;

                // Row 5: Content consumption
                statsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddStatRow(Loc.Get("label_total_flashes_stat"), achievements.TotalFlashImages.ToString("N0"), 0);
                AddStatRow(Loc.Get("label_bubbles_popped_stat"), achievements.TotalBubblesPopped.ToString("N0"), 1);
                AddStatRow(Loc.Get("label_lock_cards_done"), achievements.TotalLockCardsCompleted.ToString("N0"), 2);
                row++;

                // Row 6: Time stats
                statsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var videoMin = achievements.TotalVideoMinutes;
                var videoTimeStr = videoMin >= 60 ? $"{videoMin / 60:F1} {Loc.Get("label_hrs")}" : $"{videoMin:F1} {Loc.Get("label_min_abbrev")}";
                AddStatRow(Loc.Get("label_video_time"), videoTimeStr, 0);
                var pinkMin = achievements.TotalPinkFilterMinutes;
                var pinkTimeStr = pinkMin >= 60 ? $"{pinkMin / 60:F1} {Loc.Get("label_hrs")}" : $"{pinkMin:F1} {Loc.Get("label_min_abbrev")}";
                AddStatRow(Loc.Get("label_pink_filter_time"), pinkTimeStr, 1);
                var spiralMin = achievements.TotalSpiralMinutes;
                var spiralTimeStr = spiralMin >= 60 ? $"{spiralMin / 60:F1} {Loc.Get("label_hrs")}" : $"{spiralMin:F1} {Loc.Get("label_min_abbrev")}";
                AddStatRow(Loc.Get("label_spiral_time"), spiralTimeStr, 2);
                row++;

                // Row 7: Misc stats
                statsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddStatRow(Loc.Get("label_consecutive_days"), achievements.ConsecutiveDays.ToString("N0"), 0);

                detailedStatsStack.Children.Add(statsGrid);
            }

            detailedStatsBorder.Child = detailedStatsStack;
            if (hasDitzyData)
                mainStack.Children.Add(detailedStatsBorder);

            // Ditzy Data PRO analytics — each Tier 6 node unlocks one expander panel.
            // Content is built lazily on first expand (Season Rewind reads snapshots from disk).
            AddProSection(mainStack, "ditzy_data_pro", "📈", "label_pro_lifetime_title", BuildProLifetimePanel);
            AddProSection(mainStack, "season_rewind", "⏪", "label_pro_rewind_title", BuildSeasonRewindPanel);
            AddProSection(mainStack, "bestie_records", "🏅", "label_pro_bestie_title", BuildBestieRecordsPanel);
            AddProSection(mainStack, "brain_drain_report", "🧠", "label_pro_braindrain_title", BuildBrainDrainPanel);

            // Stats section
            var statsBorder = new Border
            {
                Background = Application.Current.Resources["SurfaceBgBrush"] as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(30, 30, 58)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12)
            };
            var statsStack = new StackPanel();

            // XP Mult
            var multiplier = App.SkillTree?.GetTotalXpMultiplier() ?? 1.0;
            var xpStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            xpStack.Children.Add(new TextBlock
            {
                Text = Loc.Get("label_xp_mult"),
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            xpStack.Children.Add(new TextBlock
            {
                Text = $"{multiplier:F2}x",
                Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 136)),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (settings.PinkRushActive)
            {
                xpStack.Children.Add(new TextBlock
                {
                    Text = " " + Loc.Get("label_xp_rush"),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentDarkColorHex() ?? "#FF1493")),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            statsStack.Children.Add(xpStack);

            // Time
            var conditioningTime = App.SkillTree?.GetFormattedConditioningTime() ?? "0h 0m";
            var timeStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            timeStack.Children.Add(new TextBlock
            {
                Text = "⏱️ ",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            timeStack.Children.Add(new TextBlock
            {
                Text = conditioningTime,
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            statsStack.Children.Add(timeStack);

            statsBorder.Child = statsStack;
            mainStack.Children.Add(statsBorder);

            // Active Bonuses Section
            var breakdown = App.SkillTree?.GetMultiplierBreakdown() ?? new List<(string, double)>();
            if (breakdown.Count > 1) // Only show if there are bonuses beyond base
            {
                var bonusesTitle = new TextBlock
                {
                    Text = "Active Bonuses:",
                    Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                    FontSize = 11,
                    Margin = new Thickness(0, 15, 0, 8)
                };
                mainStack.Children.Add(bonusesTitle);

                var bonusesWrap = new WrapPanel { Orientation = Orientation.Horizontal };
                foreach (var (source, value) in breakdown)
                {
                    if (source == "Base") continue; // Don't show base multiplier

                    var chip = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(60, 40, 80)),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(10, 5, 10, 5),
                        Margin = new Thickness(0, 0, 8, 8)
                    };

                    chip.Child = new TextBlock
                    {
                        Text = $"{source}: +{value:P0}",
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentLightColorHex() ?? "#FFB6C1")),
                        FontSize = 11
                    };

                    bonusesWrap.Children.Add(chip);
                }
                mainStack.Children.Add(bonusesWrap);
            }

            // The header column can outgrow the fixed-height canvas (stats + analytics
            // expanders), and the tree only scrolls horizontally — so the header scrolls
            // its own content vertically. The tab's PreviewMouseWheel handler yields to
            // this viewer when the cursor is over it.
            headerBorder.Child = new ScrollViewer
            {
                Content = mainStack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 430
            };
            EnhancementsTab.SkillTreeCanvas.Children.Add(headerBorder);
        }

        /// <summary>
        /// Creates an animated gradient brush for the skill tree background or header
        /// </summary>
        private LinearGradientBrush CreateAnimatedSkillTreeBrush(bool isHeader)
        {
            var brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 0);
            brush.EndPoint = new Point(1, 1);

            // Ambient loop: at the Performance tier or under reduced motion the tree keeps exactly
            // this gradient, it just never gets a clock.
            bool animate = Services.MotionFx.AllowAmbientLoops;
            void Drift(GradientStop stop, DependencyProperty prop, AnimationTimeline anim)
            {
                if (!animate) return;
                Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
                stop.BeginAnimation(prop, anim);
            }

            if (isHeader)
            {
                // Header: dark purple → vivid purple-pink → dark purple
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(35, 20, 60), 0.0));    // deeper purple edge
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(80, 30, 100), 0.5));   // vivid purple-pink center
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(35, 20, 60), 1.0));    // deeper purple edge

                // Animate middle stop offset: drift 0.2 ↔ 0.8
                var offsetAnim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.2,
                    To = 0.8,
                    Duration = TimeSpan.FromSeconds(5),
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                Drift(brush.GradientStops[1], GradientStop.OffsetProperty, offsetAnim);

                // Animate middle stop color: shift between purple tones
                var colorAnim = new System.Windows.Media.Animation.ColorAnimation
                {
                    From = Color.FromRgb(80, 30, 100),   // vivid purple
                    To = Color.FromRgb(120, 40, 90),      // bright magenta-purple
                    Duration = TimeSpan.FromSeconds(4),
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                Drift(brush.GradientStops[1], GradientStop.ColorProperty, colorAnim);
            }
            else
            {
                // Canvas background: deep purple → vivid purple → rich blue-purple → deep purple
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(25, 15, 50), 0.0));    // deep purple
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(60, 25, 80), 0.3));    // vivid purple
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(30, 35, 75), 0.7));    // rich blue-purple
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(25, 15, 50), 1.0));    // deep purple

                // Animate stop[1] offset: drift 0.15 ↔ 0.5
                var offset1Anim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.15,
                    To = 0.5,
                    Duration = TimeSpan.FromSeconds(6),
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                Drift(brush.GradientStops[1], GradientStop.OffsetProperty, offset1Anim);

                // Animate stop[2] offset: drift 0.5 ↔ 0.85
                var offset2Anim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.5,
                    To = 0.85,
                    Duration = TimeSpan.FromSeconds(8),
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                Drift(brush.GradientStops[2], GradientStop.OffsetProperty, offset2Anim);

                // Animate stop[1] color: shift between purple and blue tones
                var colorAnim = new System.Windows.Media.Animation.ColorAnimation
                {
                    From = Color.FromRgb(60, 25, 80),    // vivid purple
                    To = Color.FromRgb(35, 40, 90),       // bright blue
                    Duration = TimeSpan.FromSeconds(7),
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                Drift(brush.GradientStops[1], GradientStop.ColorProperty, colorAnim);
            }

            return brush;
        }

        /// <summary>
        /// Draws connecting lines between parent and child nodes
        /// </summary>
        private void DrawConnectionLines(Dictionary<string, (double X, double Y)> positions)
        {
            var connections = new List<(string Parent, string Child)>
            {
                // Root branches into 3 paths
                ("pink_hours", "ditzy_data"),
                ("pink_hours", "sparkle_boost_1"),
                ("pink_hours", "good_girl_streak"),

                // PATH 1 (TOP): Linear progression + Tier 6 analytics chain
                ("ditzy_data", "hive_mind"),
                ("hive_mind", "trophy_case"),
                ("trophy_case", "popular_girl"),
                ("popular_girl", "quest_refresh"),
                ("quest_refresh", "better_quests"),
                ("better_quests", "ditzy_data_pro"),
                ("ditzy_data_pro", "season_rewind"),
                ("season_rewind", "bestie_records"),
                ("bestie_records", "brain_drain_report"),
                ("brain_drain_report", "certified_data_bimbo"),

                // PATH 2 (MIDDLE): Linear progression
                ("sparkle_boost_1", "sparkle_boost_2"),
                ("sparkle_boost_2", "lucky_bimbo"),
                ("lucky_bimbo", "sparkle_boost_3"),
                ("sparkle_boost_3", "lucky_bubbles"),
                ("lucky_bubbles", "pink_rush"),

                // PATH 3 (BOTTOM): Linear progression
                ("good_girl_streak", "milestone_rewards"),
                ("milestone_rewards", "oopsie_insurance"),
                ("oopsie_insurance", "streak_power"),
                ("streak_power", "reroll_addict"),
                ("reroll_addict", "perfect_bimbo_week"),
            };

            foreach (var (parent, child) in connections)
            {
                if (positions.TryGetValue(parent, out var parentPos) &&
                    positions.TryGetValue(child, out var childPos))
                {
                    var isParentUnlocked = App.SkillTree?.HasSkill(parent) == true;
                    var isChildUnlocked = App.SkillTree?.HasSkill(child) == true;

                    // Line color based on unlock state
                    var lineColor = isChildUnlocked ? Color.FromRgb(100, 255, 150) :
                                   isParentUnlocked ? (Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4") :
                                   Color.FromRgb(60, 60, 80);

                    // HORIZONTAL LAYOUT: Connect right edge of parent to left edge of child
                    var line = new System.Windows.Shapes.Line
                    {
                        X1 = parentPos.X + NodeWidth,           // Right edge of parent
                        Y1 = parentPos.Y + NodeHeight / 2,      // Vertical center of parent
                        X2 = childPos.X,                        // Left edge of child
                        Y2 = childPos.Y + NodeHeight / 2,       // Vertical center of child
                        Stroke = new SolidColorBrush(lineColor),
                        StrokeThickness = isChildUnlocked ? 3 : 2,
                        Opacity = isParentUnlocked || isChildUnlocked ? 1.0 : 0.3
                    };

                    // Add glow effect for unlocked paths
                    if (isChildUnlocked)
                    {
                        line.Effect = new DropShadowEffect
                        {
                            Color = Colors.LimeGreen,
                            BlurRadius = 8,
                            ShadowDepth = 0,
                            Opacity = 0.6
                        };
                    }

                    EnhancementsTab.SkillTreeCanvas.Children.Add(line);
                }
            }
        }

        /// <summary>
        /// Creates a skill node for the tree canvas with image background support
        /// </summary>
        private Border CreateSkillNode(Models.SkillDefinition skill)
        {
            var isUnlocked = App.SkillTree?.HasSkill(skill.Id) == true;
            var canPurchase = App.SkillTree?.CanPurchaseSkill(skill.Id) == true;
            var hasPrereq = string.IsNullOrEmpty(skill.PrerequisiteId) ||
                           App.SkillTree?.HasSkill(skill.PrerequisiteId) == true;
            var settings = App.Settings?.Current;
            var isLocked = !isUnlocked && !canPurchase;

            // Border color based on state
            Color borderColor;
            if (isUnlocked)
                borderColor = Color.FromRgb(100, 255, 150);
            else if (canPurchase)
                borderColor = (Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4");
            else
                borderColor = Color.FromRgb(60, 50, 70);

            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                Width = NodeWidth,
                Height = NodeHeight,
                Cursor = canPurchase ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                Tag = skill.Id,
                ClipToBounds = true,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1.0, 1.0)
            };

            // Add glow effect for unlocked or purchasable nodes
            if (isUnlocked)
            {
                // Owned nodes breathe this glow. The effect is registered with the tab's shared
                // clock (MainWindow.EnhancementsFx.cs) rather than given an animation of its own,
                // so a fully-bought tree still runs exactly one timeline.
                var ownedGlow = new DropShadowEffect
                {
                    Color = Colors.LimeGreen,
                    BlurRadius = 18,
                    ShadowDepth = 0,
                    Opacity = 0.6
                };
                border.Effect = ownedGlow;
                RegisterOwnedNodeGlow(ownedGlow);
            }
            else if (canPurchase)
            {
                border.Effect = new DropShadowEffect
                {
                    Color = Colors.HotPink,
                    BlurRadius = 15,
                    ShadowDepth = 0,
                    Opacity = 0.7
                };
            }

            // Hover: scale up with pop effect, plus the z-order lift. Both live in
            // MainWindow.EnhancementsFx.cs so the motion gate is applied in one place.
            border.MouseEnter += (s, e) => ApplySkillNodeHover(border, true);
            border.MouseLeave += (s, e) => ApplySkillNodeHover(border, false);

            // Click handler
            if (canPurchase)
            {
                border.MouseLeftButtonUp += SkillCard_Click;
            }

            // Tooltip
            var tooltipStack = new StackPanel { MaxWidth = 280 };
            tooltipStack.Children.Add(new TextBlock
            {
                Text = App.Mods?.MakeModAware(skill.FlavorText) ?? skill.LocalizedFlavorText,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentLightColorHex() ?? "#FFB6C1")),
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
            tooltipStack.Children.Add(new TextBlock
            {
                Text = App.Mods?.MakeModAware(skill.Description) ?? skill.LocalizedDescription,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrEmpty(skill.PrerequisiteId) && !hasPrereq)
            {
                var prereqSkill = Models.SkillDefinition.All.FirstOrDefault(s => s.Id == skill.PrerequisiteId);
                tooltipStack.Children.Add(new TextBlock
                {
                    Text = Loc.GetF("label_skill_requires", prereqSkill?.LocalizedName ?? skill.PrerequisiteId),
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                    Margin = new Thickness(0, 6, 0, 0)
                });
            }

            border.ToolTip = new ToolTip
            {
                Content = tooltipStack,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 50)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")),
                Padding = new Thickness(10)
            };

            // Main content grid: image, name label, gap, button
            var contentGrid = new Grid();
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(86) }); // Row 0: Image area
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) }); // Row 1: Skill name
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });  // Row 2: Gap
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) }); // Row 3: Button area

            // Row 0: Image (blurred if locked)
            bool imageLoaded = false;

            // Try to load skill image (will support individual files like skills/hive_mind.png)
            try
            {
                var skillImageSource = Services.ModResourceResolver.ResolveImage($"skills/{skill.Id}.png");
                if (skillImageSource == null)
                    throw new FileNotFoundException($"skills/{skill.Id}.png"); // no art yet — use the gradient fallback below
                var skillImage = new System.Windows.Controls.Image
                {
                    Source = skillImageSource,
                    Stretch = Stretch.UniformToFill
                };

                // Blur effect if locked
                if (isLocked)
                {
                    skillImage.Effect = new System.Windows.Media.Effects.BlurEffect
                    {
                        Radius = 8
                    };
                }

                Grid.SetRow(skillImage, 0);
                contentGrid.Children.Add(skillImage);
                imageLoaded = true;
            }
            catch
            {
                // Fallback to gradient placeholder
                var imagePlaceholder = new Border
                {
                    Background = CreateSkillPlaceholderGradient(skill.Tier),
                    CornerRadius = new CornerRadius(8, 8, 0, 0)
                };

                // Blur gradient if locked
                if (isLocked)
                {
                    imagePlaceholder.Effect = new System.Windows.Media.Effects.BlurEffect
                    {
                        Radius = 8
                    };
                }

                Grid.SetRow(imagePlaceholder, 0);
                contentGrid.Children.Add(imagePlaceholder);
            }

            // Row 1: Skill name label
            var nameLabel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 28, 45)),
                Child = new TextBlock
                {
                    Text = App.Mods?.MakeModAware(skill.Name) ?? skill.LocalizedName,
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 210)),
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            Grid.SetRow(nameLabel, 1);
            contentGrid.Children.Add(nameLabel);

            // Row 3: Cost/Status Button. Owned permanent nodes get a gold "FOREVER" badge —
            // they survive the season reset; owned mechanical nodes keep the green OWNED look
            // (and reset monthly).
            var buttonBg = isUnlocked && skill.IsPermanent ? Color.FromRgb(255, 200, 80) :
                          isUnlocked ? Color.FromRgb(100, 255, 150) :
                          canPurchase ? (Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4") :
                          Color.FromRgb(40, 35, 50);

            var buttonText = isUnlocked && skill.IsPermanent ? $"💎{skill.Cost} {Loc.Get("label_skill_permanent")}" :
                            isUnlocked ? $"💎{skill.Cost} {Loc.Get("label_skill_owned")}" :
                            canPurchase ? $"💎 {skill.Cost}" :
                            $"🔒 {skill.Cost}";

            var buttonTextColor = isUnlocked ? Color.FromRgb(20, 20, 30) :
                                 canPurchase ? Colors.White :
                                 Color.FromRgb(120, 120, 130);

            var statusButton = new Border
            {
                Background = new SolidColorBrush(buttonBg),
                CornerRadius = new CornerRadius(0, 0, 8, 8),
                Child = new TextBlock
                {
                    Text = buttonText,
                    Foreground = new SolidColorBrush(buttonTextColor),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            Grid.SetRow(statusButton, 3);  // Row 3 (after gap)
            contentGrid.Children.Add(statusButton);

            border.Child = contentGrid;
            return border;
        }

        /// <summary>
        /// Creates a placeholder gradient for skill nodes based on tier
        /// </summary>
        private LinearGradientBrush CreateSkillPlaceholderGradient(int tier)
        {
            // Different color schemes per tier for visual distinction
            var (startColor, endColor) = tier switch
            {
                1 => (Color.FromRgb(80, 50, 100), Color.FromRgb(50, 30, 70)),   // Purple - Foundation
                2 => (Color.FromRgb(100, 50, 80), Color.FromRgb(60, 30, 50)),   // Pink - Core
                3 => (Color.FromRgb(80, 60, 100), Color.FromRgb(45, 35, 65)),   // Deep Purple - Specialization
                4 => (Color.FromRgb(100, 40, 90), Color.FromRgb(55, 25, 50)),   // Hot Pink - Mastery
                6 => (Color.FromRgb(110, 85, 40), Color.FromRgb(60, 45, 25)),   // Gold - Ditzy Data PRO
                _ => (Color.FromRgb(60, 40, 80), Color.FromRgb(35, 25, 50))     // Default
            };

            return new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(startColor, 0),
                    new GradientStop(endColor, 1)
                }
            };
        }

        #region Ditzy Data PRO analytics panels

        /// <summary>
        /// Adds one PRO expander (toggle button + collapsed panel) to the tree header,
        /// gated on owning the given Tier 6 node. Mirrors the Ditzy Data expander pattern.
        /// Panel content is built lazily on first expand (Season Rewind reads disk snapshots).
        /// </summary>
        private void AddProSection(StackPanel parent, string skillId, string emoji, string titleLocKey, Func<UIElement> contentFactory)
        {
            if (App.SkillTree?.HasSkill(skillId) != true) return;

            var gold = Color.FromRgb(255, 200, 80);

            var button = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(60, 50, 30)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = Cursors.Hand,
                BorderBrush = new SolidColorBrush(gold),
                BorderThickness = new Thickness(1)
            };
            var buttonStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var arrow = new TextBlock
            {
                Text = " ▼",
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            buttonStack.Children.Add(new TextBlock { Text = emoji + " ", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            buttonStack.Children.Add(new TextBlock
            {
                Text = Loc.Get(titleLocKey),
                Foreground = new SolidColorBrush(gold),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            buttonStack.Children.Add(arrow);
            button.Child = buttonStack;

            // No inner scroller/height cap: the panel expands fully and the header's own
            // vertical ScrollViewer (see CreateSkillTreeHeader) handles the overflow —
            // one scrollbar for the whole column.
            var panel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(22, 22, 42)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 15),
                Visibility = Visibility.Collapsed
            };

            var built = false;
            button.MouseLeftButtonDown += (s, e) =>
            {
                if (!built)
                {
                    built = true;
                    try
                    {
                        panel.Child = contentFactory();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "PRO panel build failed: {Skill}", skillId);
                        panel.Child = new TextBlock { Text = "—", Foreground = Brushes.Gray };
                    }
                }
                var isCollapsed = panel.Visibility == Visibility.Collapsed;
                panel.Visibility = isCollapsed ? Visibility.Visible : Visibility.Collapsed;
                arrow.Text = isCollapsed ? " ▲" : " ▼";
            };

            parent.Children.Add(button);
            parent.Children.Add(panel);
        }

        /// <summary>Minutes → "H.h h" / "M m" using the existing time loc units.</summary>
        private static string FormatProMinutes(double minutes) =>
            minutes >= 60 ? $"{minutes / 60:F1} {Loc.Get("label_hrs")}" : $"{minutes:F0} {Loc.Get("label_min_abbrev")}";

        private static TextBlock ProLabel(string text) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
            FontSize = 9
        };

        private static TextBlock ProValue(string text) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            FontSize = 10,
            FontWeight = FontWeights.Bold
        };

        // ---- chart primitives (single-series, app accent colors, ink-colored text) ----

        private static readonly Color ChartInkDim = Color.FromRgb(140, 140, 140);
        private static readonly Color ChartInk = Color.FromRgb(230, 230, 235);
        private static readonly Color ChartBaseline = Color.FromRgb(58, 58, 85);
        private static readonly Color ChartPink = Color.FromRgb(255, 105, 180);
        private static readonly Color ChartGold = Color.FromRgb(255, 200, 80);
        private static readonly Color ChartSurface = Color.FromRgb(22, 22, 42);   // panel bg — used as mark ring
        private static readonly Color ChartCellIdle = Color.FromRgb(38, 38, 64);

        /// <summary>
        /// All seasons with data, oldest → newest (snapshots from disk + the live bucket),
        /// capped to the most recent <paramref name="cap"/>.
        /// </summary>
        private List<(string Key, double Minutes, int Level, List<string> ActiveDays)> GetSeasonSeriesAscending(int cap = 12)
        {
            var result = new List<(string, double, int, List<string>)>();
            var settings = App.Settings?.Current;
            if (settings == null) return new();

            foreach (var key in Services.SeasonRecapService.ListSeasonKeys().Take(24))
            {
                var snap = Services.SeasonRecapService.Load(key);
                if (snap == null) continue;
                result.Add((snap.SeasonKey, snap.SeasonMinutes, snap.PeakLevel, snap.ActiveDays ?? new List<string>()));
            }

            var liveKey = settings.SeasonStatsSeason ?? Services.SeasonRecapService.CurrentSeasonKey;
            if (!result.Any(r => r.Item1 == liveKey))
                result.Add((liveKey, settings.SeasonConditioningMinutes,
                    Math.Max(settings.SeasonPeakLevel, settings.PlayerLevel),
                    new List<string>(settings.SeasonActiveDays)));

            return result
                .OrderBy(r => r.Item1, StringComparer.Ordinal)
                .TakeLast(cap)
                .Select(r => ((string)r.Item1, (double)r.Item2, (int)r.Item3, (List<string>)r.Item4))
                .ToList();
        }

        private static string SeasonMonthLabel(string seasonKey)
        {
            if (Models.SeasonNumbering.TryParse(seasonKey, out var y, out var m))
                return new DateTime(y, m, 1).ToString("MMM", System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant();
            return seasonKey;
        }

        /// <summary>
        /// Minutes-per-season bar chart: thin pink bars, rounded data ends, 2px gaps,
        /// month labels in muted ink, values only on the max and newest bars, tooltip per bar.
        /// </summary>
        private UIElement BuildSeasonBarChart(List<(string Key, double Minutes, int Level, List<string> ActiveDays)> series)
        {
            const double plotH = 78, barW = 20, gap = 10, labelH = 14, valueH = 13;
            var maxVal = Math.Max(1.0, series.Max(s => s.Minutes));
            var maxIdx = series.FindIndex(s => s.Minutes == series.Max(x => x.Minutes));

            var canvas = new Canvas
            {
                Height = valueH + plotH + labelH,
                Width = series.Count * (barW + gap) - gap,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            // recessive baseline
            canvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = 0, X2 = canvas.Width, Y1 = valueH + plotH, Y2 = valueH + plotH,
                Stroke = new SolidColorBrush(ChartBaseline), StrokeThickness = 1
            });

            for (int i = 0; i < series.Count; i++)
            {
                var s = series[i];
                var h = Math.Max(2, plotH * s.Minutes / maxVal);
                var x = i * (barW + gap);

                var bar = new Border
                {
                    Width = barW,
                    Height = h,
                    Background = new SolidColorBrush(ChartPink),
                    CornerRadius = new CornerRadius(3, 3, 0, 0),
                    ToolTip = $"{s.Key} · {FormatProMinutes(s.Minutes)}"
                };
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, valueH + plotH - h);
                canvas.Children.Add(bar);

                // selective direct labels: max bar + newest bar only, in ink (not series color)
                if (i == maxIdx || i == series.Count - 1)
                {
                    var value = new TextBlock
                    {
                        Text = FormatProMinutes(s.Minutes),
                        Foreground = new SolidColorBrush(ChartInk),
                        FontSize = 8.5,
                        FontWeight = FontWeights.SemiBold
                    };
                    value.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(value, x + barW / 2 - value.DesiredSize.Width / 2);
                    Canvas.SetTop(value, valueH + plotH - h - 12);
                    canvas.Children.Add(value);
                }

                var month = new TextBlock
                {
                    Text = SeasonMonthLabel(s.Key),
                    Foreground = new SolidColorBrush(ChartInkDim),
                    FontSize = 8.5
                };
                month.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(month, x + barW / 2 - month.DesiredSize.Width / 2);
                Canvas.SetTop(month, valueH + plotH + 2);
                canvas.Children.Add(month);
            }

            return canvas;
        }

        /// <summary>
        /// Peak-level-per-season sparkline: 2px gold line, 8px dots with a 2px surface ring,
        /// only the newest point carries a value label, tooltip per dot.
        /// </summary>
        private UIElement BuildLevelSparkline(List<(string Key, double Minutes, int Level, List<string> ActiveDays)> series)
        {
            const double plotH = 52, stepW = 30, padTop = 14, labelH = 14, dotR = 4;
            var pts = series.Where(s => s.Level > 0).ToList();
            if (pts.Count == 0)
                return new TextBlock { Text = "—", Foreground = new SolidColorBrush(ChartInkDim), FontSize = 9 };

            var maxLevel = Math.Max(1, pts.Max(p => p.Level));
            var canvas = new Canvas
            {
                Height = padTop + plotH + labelH,
                Width = Math.Max(stepW, (pts.Count - 1) * stepW) + dotR * 2 + 2,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            double X(int i) => dotR + 1 + i * stepW;
            double Y(int level) => padTop + plotH - plotH * level / maxLevel;

            if (pts.Count > 1)
            {
                var line = new System.Windows.Shapes.Polyline
                {
                    Stroke = new SolidColorBrush(ChartGold),
                    StrokeThickness = 2,
                    StrokeLineJoin = PenLineJoin.Round
                };
                for (int i = 0; i < pts.Count; i++)
                    line.Points.Add(new Point(X(i), Y(pts[i].Level)));
                canvas.Children.Add(line);
            }

            for (int i = 0; i < pts.Count; i++)
            {
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = dotR * 2, Height = dotR * 2,
                    Fill = new SolidColorBrush(ChartGold),
                    Stroke = new SolidColorBrush(ChartSurface),
                    StrokeThickness = 2,
                    ToolTip = $"{pts[i].Key} · {Loc.Get("label_rewind_level")} {pts[i].Level}"
                };
                Canvas.SetLeft(dot, X(i) - dotR);
                Canvas.SetTop(dot, Y(pts[i].Level) - dotR);
                canvas.Children.Add(dot);

                var month = new TextBlock
                {
                    Text = SeasonMonthLabel(pts[i].Key),
                    Foreground = new SolidColorBrush(ChartInkDim),
                    FontSize = 8.5
                };
                month.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(month, X(i) - month.DesiredSize.Width / 2);
                Canvas.SetTop(month, padTop + plotH + 2);
                canvas.Children.Add(month);

                if (i == pts.Count - 1)
                {
                    var value = new TextBlock
                    {
                        Text = pts[i].Level.ToString(),
                        Foreground = new SolidColorBrush(ChartInk),
                        FontSize = 8.5,
                        FontWeight = FontWeights.SemiBold
                    };
                    value.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(value, X(i) - value.DesiredSize.Width / 2);
                    Canvas.SetTop(value, Y(pts[i].Level) - dotR - 13);
                    canvas.Children.Add(value);
                }
            }

            return canvas;
        }

        /// <summary>
        /// GitHub-style activity heatmap: one mini-grid per month (weeks as columns,
        /// Mon–Sun as rows), pink = active day, dim = inactive, tooltip per cell.
        /// Months come from the live bucket + snapshot active_days (schema 2+).
        /// </summary>
        private UIElement BuildActivityHeatmap()
        {
            const double cell = 11, cellGap = 2;
            // Months without per-day data (schema-1 snapshots only stored the count) are
            // skipped — an all-dim grid would read as "zero activity" when it wasn't.
            var months = GetSeasonSeriesAscending(6)
                .Where(s => s.ActiveDays.Count > 0 && Models.SeasonNumbering.TryParse(s.Key, out _, out _))
                .ToList();
            if (months.Count == 0)
                return new TextBlock { Text = "—", Foreground = new SolidColorBrush(ChartInkDim), FontSize = 9 };

            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var m in months)
            {
                Models.SeasonNumbering.TryParse(m.Key, out var year, out var month);
                var daysInMonth = DateTime.DaysInMonth(year, month);
                var active = new HashSet<string>(m.ActiveDays);
                // Monday-first row index; weeks advance by column (GitHub layout)
                var firstDow = ((int)new DateTime(year, month, 1).DayOfWeek + 6) % 7;
                var weeks = (int)Math.Ceiling((firstDow + daysInMonth) / 7.0);

                var monthCanvas = new Canvas
                {
                    Width = weeks * (cell + cellGap) - cellGap,
                    Height = 7 * (cell + cellGap) - cellGap
                };
                for (int day = 1; day <= daysInMonth; day++)
                {
                    var slot = firstDow + day - 1;
                    var col = slot / 7;
                    var row = slot % 7;
                    var dateStr = $"{year:D4}-{month:D2}-{day:D2}";
                    var isActive = active.Contains(dateStr);
                    var box = new Border
                    {
                        Width = cell, Height = cell,
                        CornerRadius = new CornerRadius(2),
                        Background = new SolidColorBrush(isActive ? ChartPink : ChartCellIdle),
                        ToolTip = dateStr
                    };
                    Canvas.SetLeft(box, col * (cell + cellGap));
                    Canvas.SetTop(box, row * (cell + cellGap));
                    monthCanvas.Children.Add(box);
                }

                var monthStack = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
                monthStack.Children.Add(monthCanvas);
                monthStack.Children.Add(new TextBlock
                {
                    Text = SeasonMonthLabel(m.Key),
                    Foreground = new SolidColorBrush(ChartInkDim),
                    FontSize = 8.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0)
                });
                strip.Children.Add(monthStack);
            }

            var scroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = strip
            };
            return scroller;
        }

        /// <summary>Muted section caption used above each PRO chart.</summary>
        private static TextBlock ProChartTitle(string locKey) => new()
        {
            Text = Loc.Get(locKey),
            Foreground = new SolidColorBrush(ChartInkDim),
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };

        /// <summary>PRO Lifetime Dashboard — every all-time counter the app tracks, 3-column grid.</summary>
        private UIElement BuildProLifetimePanel()
        {
            var progress = App.Achievements?.Progress;
            var settings = App.Settings?.Current;
            var stack = new StackPanel();
            if (progress == null || settings == null) return stack;

            // Activity heatmap calendar (months from live bucket + snapshot active_days)
            stack.Children.Add(ProChartTitle("label_heatmap_title"));
            stack.Children.Add(BuildActivityHeatmap());
            stack.Children.Add(new Border { Height = 10 });

            var grid = new Grid();
            for (int c = 0; c < 3; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int row = -1, col = 0;
            void Add(string labelKey, string value)
            {
                if (col == 0)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    row++;
                }
                var cell = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
                cell.Children.Add(ProLabel(Loc.Get(labelKey)));
                cell.Children.Add(ProValue(value));
                Grid.SetColumn(cell, col);
                Grid.SetRow(cell, row);
                grid.Children.Add(cell);
                col = (col + 1) % 3;
            }

            Add("label_pro_total_time", FormatProMinutes(settings.TotalConditioningMinutes));
            Add("label_total_xp_earned_stat", progress.TotalXPEarned.ToString("N0"));
            Add("label_skill_points_earned", progress.TotalSkillPointsEarned.ToString("N0"));

            Add("label_pro_points_spent", progress.LifetimeSkillPointsSpent.ToString("N0"));
            Add("label_pro_highest_level", settings.HighestLevelEver.ToString("N0"));
            Add("label_pro_highest_streak", settings.HighestStreak.ToString("N0"));

            Add("label_pro_chaos_rounds", progress.EnhancementsPlayed.ToString("N0"));
            Add("label_pro_deeper_time", FormatProMinutes(progress.DeeperMinutes));
            Add("label_pro_quizzes", progress.QuizzesPassed.ToString("N0"));

            Add("label_pro_keyword_triggers", progress.KeywordTriggersFired.ToString("N0"));
            Add("label_pro_blinks", progress.BlinkTrainerBlinks.ToString("N0"));
            Add("label_pro_gaze_pops", progress.GazePops.ToString("N0"));

            Add("label_pro_fastest_lock", progress.FastestLockCardSeconds < double.MaxValue
                ? $"{progress.FastestLockCardSeconds:F1}s" : "—");
            Add("label_pro_night_uses", settings.NightTimeUsageCount.ToString("N0"));
            Add("label_pro_morning_uses", settings.EarlyMorningUsageCount.ToString("N0"));

            stack.Children.Add(grid);
            return stack;
        }

        /// <summary>
        /// Season Rewind — one row per season (live bucket first, then disk snapshots, newest
        /// first). The time column carries a ▲/▼ delta vs the season before it. The Spent
        /// column appears once certified_data_bimbo is owned (its capstone perk).
        /// </summary>
        private UIElement BuildSeasonRewindPanel()
        {
            var settings = App.Settings?.Current;
            var stack = new StackPanel();
            if (settings == null) return stack;

            // Trend charts above the table (one measure per chart — never dual-axis)
            var chartSeries = GetSeasonSeriesAscending(10);
            if (chartSeries.Count > 0)
            {
                stack.Children.Add(ProChartTitle("label_chart_time_per_season"));
                stack.Children.Add(BuildSeasonBarChart(chartSeries));
                stack.Children.Add(new Border { Height = 8 });
                if (chartSeries.Any(s => s.Level > 0))
                {
                    stack.Children.Add(ProChartTitle("label_chart_level_per_season"));
                    stack.Children.Add(BuildLevelSparkline(chartSeries));
                    stack.Children.Add(new Border { Height = 8 });
                }
            }

            var showSpent = App.SkillTree?.HasSkill("certified_data_bimbo") == true;

            // (key, minutes, sessions, days, streak, pct, level, spent, isLive, hasSchema2)
            var rows = new List<(string Key, double Minutes, int Sessions, int Days, int Streak, int Pct, int Level, int Spent, bool IsLive, bool HasV2)>();

            var liveKey = settings.SeasonStatsSeason ?? Services.SeasonRecapService.CurrentSeasonKey;
            rows.Add((liveKey,
                settings.SeasonConditioningMinutes,
                settings.SeasonSessionsStarted,
                settings.SeasonActiveDays.Count,
                settings.SeasonPeakStreak,
                Services.SeasonRecapService.PercentileFor(settings.SeasonPeakRank, settings.SeasonPeakRankTotal),
                Math.Max(settings.SeasonPeakLevel, settings.PlayerLevel),
                settings.SeasonPointsSpent,
                true, true));

            foreach (var key in Services.SeasonRecapService.ListSeasonKeys().Take(8))
            {
                if (key == liveKey) continue; // don't double-list a desynced bucket
                var snap = Services.SeasonRecapService.Load(key);
                if (snap == null) continue;
                rows.Add((snap.SeasonKey, snap.SeasonMinutes, snap.SessionCount, snap.DaysActive,
                    snap.LongestStreak, snap.Percentile, snap.PeakLevel, snap.PointsSpentSeason,
                    false, snap.Schema >= 2));
            }

            var colCount = showSpent ? 8 : 7;
            var grid = new Grid();
            for (int c = 0; c < colCount; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            void AddCell(UIElement el, int r, int c)
            {
                Grid.SetRow(el, r);
                Grid.SetColumn(el, c);
                grid.Children.Add(el);
            }

            // Header row
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var headers = new List<string> {
                "label_rewind_season", "label_rewind_time", "label_rewind_sessions", "label_rewind_days",
                "label_rewind_streak", "label_rewind_top", "label_rewind_level" };
            if (showSpent) headers.Add("label_rewind_spent");
            for (int c = 0; c < headers.Count; c++)
                AddCell(ProLabel(Loc.Get(headers[c])), 0, c);

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var gridRow = i + 1;

                var seasonText = ProValue(r.Key + (r.IsLive ? " •" : ""));
                if (r.IsLive) seasonText.Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 80));
                AddCell(seasonText, gridRow, 0);

                // Time with delta vs the season before it (next row down = older)
                var timeStack = new StackPanel { Orientation = Orientation.Horizontal };
                timeStack.Children.Add(ProValue(FormatProMinutes(r.Minutes)));
                if (i + 1 < rows.Count)
                {
                    var older = rows[i + 1];
                    if (r.Minutes > older.Minutes)
                        timeStack.Children.Add(new TextBlock { Text = " ▲", Foreground = new SolidColorBrush(Color.FromRgb(100, 255, 150)), FontSize = 9 });
                    else if (r.Minutes < older.Minutes)
                        timeStack.Children.Add(new TextBlock { Text = " ▼", Foreground = new SolidColorBrush(Color.FromRgb(255, 110, 110)), FontSize = 9 });
                }
                AddCell(timeStack, gridRow, 1);

                AddCell(ProValue(r.Sessions.ToString("N0")), gridRow, 2);
                AddCell(ProValue(r.Days.ToString("N0")), gridRow, 3);
                AddCell(ProValue(r.Streak.ToString("N0")), gridRow, 4);
                AddCell(ProValue(r.Pct > 0 ? $"{r.Pct}%" : "—"), gridRow, 5);
                AddCell(ProValue(r.HasV2 && r.Level > 0 ? r.Level.ToString("N0") : "—"), gridRow, 6);
                if (showSpent)
                    AddCell(ProValue(r.HasV2 ? r.Spent.ToString("N0") : "—"), gridRow, 7);
            }

            stack.Children.Add(grid);

            if (rows.Count <= 1)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = Loc.Get("label_rewind_empty"),
                    Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                    FontSize = 9,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 6, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            return stack;
        }

        /// <summary>Bestie Records — all-time personal bests, with the season they happened where known.</summary>
        private UIElement BuildBestieRecordsPanel()
        {
            var progress = App.Achievements?.Progress;
            var settings = App.Settings?.Current;
            var stack = new StackPanel();
            if (progress == null || settings == null) return stack;

            void AddRecord(string labelKey, string value, string? when = null)
            {
                var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var label = ProLabel(Loc.Get(labelKey));
                label.VerticalAlignment = VerticalAlignment.Center;
                rowGrid.Children.Add(label);
                var valueStack = new StackPanel { Orientation = Orientation.Horizontal };
                valueStack.Children.Add(ProValue(value));
                if (!string.IsNullOrEmpty(when))
                    valueStack.Children.Add(new TextBlock
                    {
                        Text = $"  {when}",
                        Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                        FontSize = 9,
                        VerticalAlignment = VerticalAlignment.Bottom
                    });
                Grid.SetColumn(valueStack, 1);
                rowGrid.Children.Add(valueStack);
                stack.Children.Add(rowGrid);
            }

            // Straight all-time records
            AddRecord("label_longest_session", $"{progress.LongestSessionMinutes:F1} {Loc.Get("label_min_abbrev")}");
            AddRecord("label_bc_best_streak", progress.BubbleCountBestStreak.ToString("N0"));
            AddRecord("label_pro_fastest_lock", progress.FastestLockCardSeconds < double.MaxValue
                ? $"{progress.FastestLockCardSeconds:F1}s" : "—");
            AddRecord("label_pro_highest_streak", settings.HighestStreak.ToString("N0"));
            AddRecord("label_pro_highest_level", settings.HighestLevelEver.ToString("N0"));

            // Season-scoped records mined from snapshots + the live bucket
            var seasons = new List<(string Key, double Minutes, int Rank, int Level)>
            {
                (settings.SeasonStatsSeason ?? Services.SeasonRecapService.CurrentSeasonKey,
                 settings.SeasonConditioningMinutes, settings.SeasonPeakRank,
                 Math.Max(settings.SeasonPeakLevel, settings.PlayerLevel))
            };
            foreach (var key in Services.SeasonRecapService.ListSeasonKeys().Take(24))
            {
                var snap = Services.SeasonRecapService.Load(key);
                if (snap != null) seasons.Add((snap.SeasonKey, snap.SeasonMinutes, snap.PeakRank, snap.PeakLevel));
            }

            var bestTime = seasons.OrderByDescending(s => s.Minutes).First();
            if (bestTime.Minutes > 0)
                AddRecord("label_pro_best_season_time", FormatProMinutes(bestTime.Minutes), bestTime.Key);

            var ranked = seasons.Where(s => s.Rank > 0).OrderBy(s => s.Rank).ToList();
            if (ranked.Count > 0)
                AddRecord("label_pro_best_rank", $"#{ranked[0].Rank}", ranked[0].Key);

            var levelled = seasons.Where(s => s.Level > 0).OrderByDescending(s => s.Level).ToList();
            if (levelled.Count > 0)
                AddRecord("label_pro_best_season_level", levelled[0].Level.ToString("N0"), levelled[0].Key);

            return stack;
        }

        /// <summary>
        /// Brain Drain Report — per-feature engagement, this season and lifetime
        /// (lifetime = live bucket + every snapshot on disk), with proportional bars.
        /// </summary>
        private UIElement BuildBrainDrainPanel()
        {
            var settings = App.Settings?.Current;
            var stack = new StackPanel();
            if (settings == null) return stack;

            var lifetime = new Dictionary<string, int>(settings.SeasonFeatureUse);
            foreach (var key in Services.SeasonRecapService.ListSeasonKeys().Take(24))
            {
                var snap = Services.SeasonRecapService.Load(key);
                if (snap == null) continue;
                foreach (var kv in snap.FeatureUse)
                {
                    lifetime.TryGetValue(kv.Key, out var n);
                    lifetime[kv.Key] = n + kv.Value;
                }
            }

            var maxLifetime = Math.Max(1, lifetime.Count > 0 ? lifetime.Values.Max() : 1);
            var accentLight = (Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentLightColorHex() ?? "#FFB6C1");

            // Column headers
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            var seasonHeader = ProLabel(Loc.Get("label_braindrain_season_lifetime"));
            Grid.SetColumn(seasonHeader, 1);
            headerGrid.Children.Add(ProLabel(Loc.Get("recap_badges_title")));
            headerGrid.Children.Add(seasonHeader);
            stack.Children.Add(headerGrid);

            foreach (var def in Models.SeasonFeatureKeys.Catalog
                         .OrderByDescending(d => lifetime.TryGetValue(d.Key, out var n) ? n : 0))
            {
                lifetime.TryGetValue(def.Key, out var life);
                settings.SeasonFeatureUse.TryGetValue(def.Key, out var season);

                var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

                var left = new StackPanel();
                left.Children.Add(ProLabel(Loc.Get(def.LabelLocKey)));
                left.Children.Add(new Border
                {
                    Height = 5,
                    Width = 8 + 160.0 * life / maxLifetime,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(accentLight),
                    Opacity = life > 0 ? 0.9 : 0.25,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                rowGrid.Children.Add(left);

                var counts = ProValue($"{season:N0} / {life:N0}");
                counts.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(counts, 1);
                rowGrid.Children.Add(counts);

                stack.Children.Add(rowGrid);
            }

            return stack;
        }

        #endregion

        /// <summary>
        /// Fills the secret-skill rail under the tree: one card per IsSecret skill, hidden
        /// ("???" + its requirement hint) until <see cref="Services.SkillTreeService.IsSecretSkillAvailable"/>
        /// says the condition has been met. The counters behind those conditions are ticked
        /// elsewhere already (TrackTimeOfDayUsage on every session start, HighestLevelEver on
        /// level-up), so the rail reveals itself over time with no other prompting.
        /// </summary>
        private void PopulateSecretSkills()
        {
            var panel = EnhancementsTab?.SecretSkills;
            if (panel == null) return;

            panel.Children.Clear();

            foreach (var skill in Models.SkillDefinition.All.Where(s => s.IsSecret))
            {
                var isAvailable = App.SkillTree?.IsSecretSkillAvailable(skill.Id) == true;
                var isUnlocked = App.SkillTree?.HasSkill(skill.Id) == true;

                // Show hidden card if not available, actual card if available
                panel.Children.Add(isAvailable || isUnlocked
                    ? CreateSecretSkillCard(skill)
                    : CreateHiddenSecretCard(skill));
            }
        }

        /// <summary>
        /// Creates a hidden secret skill card showing only the requirement hint. The skill's name,
        /// icon, cost and effect all stay withheld until <see cref="Services.SkillTreeService.IsSecretSkillAvailable"/>
        /// turns true - the hint is the whole card.
        /// </summary>
        private Border CreateHiddenSecretCard(Models.SkillDefinition skill)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 20, 40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 60, 100)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Width = SecretCardWidth,
                Height = SecretCardHeight,
                Margin = new Thickness(0, 3, 10, 3),
                Padding = new Thickness(8, 6, 8, 6),
                Opacity = 0.6
            };

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // EmojiTextBlock, not TextBlock: at 18pt the padlock is this card's only art, and a
            // plain TextBlock renders it as the monochrome Segoe silhouette.
            body.Children.Add(new Helpers.EmojiTextBlock
            {
                Text = "🔒",
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1);

            text.Children.Add(new TextBlock
            {
                Text = Loc.Get("label_secret_skill_hidden"),
                Foreground = new SolidColorBrush(Color.FromRgb(153, 50, 204)),
                FontSize = 11,
                FontWeight = FontWeights.Bold
            });

            text.Children.Add(new TextBlock
            {
                Text = skill.LocalizedSecretRequirementDesc,
                Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                FontSize = 8,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0)
            });

            body.Children.Add(text);
            border.Child = body;
            return border;
        }

        /// <summary>
        /// Creates a secret skill card (revealed but maybe not purchased)
        /// </summary>
        private Border CreateSecretSkillCard(Models.SkillDefinition skill)
        {
            var settings = App.Settings?.Current;
            var isUnlocked = App.SkillTree?.HasSkill(skill.Id) == true;
            var canPurchase = App.SkillTree?.CanPurchaseSkill(skill.Id) == true;

            Color bgColor, borderColor;
            if (isUnlocked)
            {
                bgColor = Color.FromRgb(40, 30, 50);
                borderColor = Color.FromRgb(180, 100, 255);
            }
            else if (canPurchase)
            {
                bgColor = Color.FromRgb(50, 30, 60);
                borderColor = Color.FromRgb(153, 50, 204);
            }
            else
            {
                bgColor = Color.FromRgb(35, 25, 45);
                borderColor = Color.FromRgb(100, 70, 130);
            }

            var border = new Border
            {
                Background = new SolidColorBrush(bgColor),
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(isUnlocked ? 2 : 1),
                CornerRadius = new CornerRadius(8),
                Width = SecretCardWidth,
                Height = SecretCardHeight,
                Margin = new Thickness(0, 3, 10, 3),
                Padding = new Thickness(8, 6, 8, 6),
                Cursor = canPurchase ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                Tag = skill.Id
            };

            if (isUnlocked)
            {
                border.Effect = new DropShadowEffect
                {
                    Color = Colors.Purple,
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.5
                };
            }
            else if (canPurchase)
            {
                border.Effect = new DropShadowEffect
                {
                    Color = Colors.MediumPurple,
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.4
                };
            }

            if (canPurchase)
            {
                border.MouseLeftButtonUp += SkillCard_Click;
            }

            // Tooltip
            var tooltipStack = new StackPanel { MaxWidth = 280 };
            tooltipStack.Children.Add(new TextBlock
            {
                Text = App.Mods?.MakeModAware(skill.FlavorText) ?? skill.LocalizedFlavorText,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 150, 255)),
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
            tooltipStack.Children.Add(new TextBlock
            {
                Text = App.Mods?.MakeModAware(skill.Description) ?? skill.LocalizedDescription,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });

            border.ToolTip = new ToolTip
            {
                Content = tooltipStack,
                Background = new SolidColorBrush(Color.FromRgb(40, 25, 55)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(153, 50, 204)),
                Padding = new Thickness(10)
            };

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Same per-skill art path the tree nodes use, then the emoji. The emoji really is only
            // a fallback here: all three secrets carry a PAIR of emoji in Icon ("🌙😴"), and Twemoji
            // ships no file for a pair, so EmojiImage returns null for every one of them.
            var iconSource = Services.ModResourceResolver.ResolveImage($"skills/{skill.Id}.png")
                             ?? Helpers.EmojiImage.Get(skill.Icon);
            if (iconSource != null)
            {
                // 44x30 is the 3:2 the skills/ art is drawn at, so Uniform shows all of it with
                // no letterbox and nothing to clip.
                body.Children.Add(new Image
                {
                    Source = iconSource,
                    Width = 44,
                    Height = 30,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                });
            }

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(stack, 1);

            stack.Children.Add(new TextBlock
            {
                Text = App.Mods?.MakeModAware(skill.Name) ?? skill.LocalizedName,
                Foreground = new SolidColorBrush(isUnlocked ? Color.FromRgb(180, 130, 255) : Color.FromRgb(153, 50, 204)),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });

            if (isUnlocked)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"💎{skill.Cost} {Loc.Get("label_skill_owned")}",
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 130, 255)),
                    FontSize = 9,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
            else
            {
                var costColor = (settings?.SkillPoints >= skill.Cost)
                    ? Color.FromRgb(255, 215, 0)
                    : Color.FromRgb(120, 120, 120);

                stack.Children.Add(new TextBlock
                {
                    Text = $"💎 {skill.Cost}",
                    Foreground = new SolidColorBrush(costColor),
                    FontSize = 10,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            body.Children.Add(stack);
            border.Child = body;
            return border;
        }

        /// <summary>
        /// Handles clicking on a purchasable skill card
        /// </summary>
        private async void SkillCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string skillId)
            {
                var skill = Models.SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
                if (skill == null) return;

                // Show confirmation dialog
                var skillName = App.Mods?.MakeModAware(skill.Name) ?? skill.LocalizedName;
                var pointsLabel = (App.Mods?.GetPointsLabel() ?? Loc.Get("label_sparkle_points")).ToLower();
                var flavorText = App.Mods?.MakeModAware(skill.FlavorText) ?? skill.LocalizedFlavorText;
                var descText = App.Mods?.MakeModAware(skill.Description) ?? skill.LocalizedDescription;
                var confirmMessage = Loc.GetF("msg_purchase_skill", skillName, skill.Cost, pointsLabel, flavorText, descText);
                // Permanent nodes survive the season; mechanical nodes reset monthly — say so up front
                confirmMessage += "\n\n" + Loc.Get(skill.IsPermanent ? "msg_skill_permanent_note" : "msg_skill_seasonal_note");
                var result = MessageBox.Show(
                    confirmMessage,
                    Loc.Get("dialog_purchase_enhancement"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Disable the card during purchase to prevent double-clicks
                    border.IsEnabled = false;
                    // Event FX (PR-5): prestige rank is 1 + lifetime points spent / 100, so the
                    // rank-up moment IS a purchase that crosses a hundred. Sample it before.
                    var prestigeBefore = PrestigeRankNow();
                    try
                    {
                        var (success, error) = await (App.SkillTree?.PurchaseSkillAsync(skillId)
                            ?? Task.FromResult((false, (string?)"Skill tree unavailable")));

                        if (success)
                        {
                            // Celebration audio intentionally omitted: SkillTree
                            // raises SkillUnlocked, which BarkService voices via the
                            // skill_unlock rule. Playing a random flash-pool clip here
                            // too produced two overlapping voicelines. (#366)

                            // Update Trophy Case columns if trophy_case was purchased
                            if (skillId == "trophy_case")
                            {
                                UpdateTrophyCaseColumns();
                            }

                            App.Logger?.Information("Skill purchased via UI: {SkillId}", skillId);

                            // Burst on the node the user just bought - and, if the spend crossed a
                            // prestige rank, the full-chrome prestige moment on top. Must run
                            // BEFORE the finally block: RefreshEnhancementsUI rebuilds the tree
                            // and detaches this border, and a detached anchor cannot be mapped.
                            CelebrateEnhancementPurchase(border, prestigeBefore);
                        }
                        else if (!string.IsNullOrEmpty(error))
                        {
                            MessageBox.Show(error, Loc.Get("dialog_purchase_failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    finally
                    {
                        border.IsEnabled = true;
                        RefreshEnhancementsUI();
                    }
                }
            }
        }

        /// <summary>
        /// Refreshes the active bonuses panel showing current skill effects
        /// </summary>
        private void RefreshActiveBonuses()
        {
            var breakdown = App.SkillTree?.GetMultiplierBreakdown() ?? new List<(string, double)>();

            if (breakdown.Count <= 1) // Only base
            {
                EnhancementsTab.ActiveBonusesPanel.Visibility = Visibility.Collapsed;
                return;
            }

            EnhancementsTab.ActiveBonusesPanel.Visibility = Visibility.Visible;
            EnhancementsTab.ActiveBonusesList.Children.Clear();

            foreach (var (source, value) in breakdown)
            {
                if (source == "Base") continue; // Don't show base multiplier

                var chip = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(60, 40, 80)),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 8, 8)
                };

                chip.Child = new TextBlock
                {
                    Text = $"{source}: +{value:P0}",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentLightColorHex() ?? "#FFB6C1")),
                    FontSize = 11
                };

                EnhancementsTab.ActiveBonusesList.Children.Add(chip);
            }
        }

        /// <summary>
        /// Called when skill tree service fires Pink Rush events
        /// </summary>
        private void OnPinkRushStarted(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                EnhancementsTab.TxtPinkRushIndicator.Visibility = Visibility.Visible;

                // Full-screen pink flash effect
                try
                {
                    var flashWindow = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0xFF, 0x14, 0x93)),
                        Topmost = true,
                        ShowInTaskbar = false,
                        ShowActivated = false,
                        Left = SystemParameters.VirtualScreenLeft,
                        Top = SystemParameters.VirtualScreenTop,
                        Width = SystemParameters.VirtualScreenWidth,
                        Height = SystemParameters.VirtualScreenHeight,
                        IsHitTestVisible = false,
                        Focusable = false,
                        Opacity = 0.6
                    };
                    flashWindow.Show();

                    var fadeOut = new DoubleAnimation(0.6, 0, TimeSpan.FromMilliseconds(500));
                    fadeOut.Completed += (s, args) =>
                    {
                        try { flashWindow.Close(); } catch { }
                    };
                    flashWindow.BeginAnimation(Window.OpacityProperty, fadeOut);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Pink Rush flash effect failed: {Error}", ex.Message);
                }

                // Show toast notification popup
                try
                {
                    _pinkRushPopup?.Close();
                }
                catch { }

                _pinkRushPopup = new PinkRushPopup();
                _pinkRushPopup.Show();
                App.Logger?.Information("Pink Rush activated! Showing popup.");
            });
        }

        private void OnPinkRushEnded(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                EnhancementsTab.TxtPinkRushIndicator.Visibility = Visibility.Collapsed;

                try
                {
                    _pinkRushPopup?.Close();
                }
                catch { }
                _pinkRushPopup = null;
            });
        }

        private void OnLuckyProc(object? sender, LuckyProcEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    // Close previous lucky popup if still showing
                    try { _luckyProcPopup?.Close(); } catch { }

                    var isGold = e.ProcType.Contains("Flash");
                    var glowColor = isGold
                        ? System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00)
                        : System.Windows.Media.Color.FromRgb(0xFF, 0x69, 0xB4);

                    var border = new Border
                    {
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE0, 0x15, 0x15, 0x30)),
                        CornerRadius = new CornerRadius(12),
                        BorderBrush = new SolidColorBrush(glowColor),
                        BorderThickness = new Thickness(2),
                        Padding = new Thickness(20, 12, 20, 12),
                        Effect = new DropShadowEffect
                        {
                            Color = glowColor,
                            BlurRadius = 30,
                            ShadowDepth = 0,
                            Opacity = 0.8
                        }
                    };

                    var stack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
                    stack.Children.Add(new TextBlock
                    {
                        Text = "LUCKY!",
                        Foreground = new SolidColorBrush(glowColor),
                        FontWeight = FontWeights.Bold,
                        FontSize = 22,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                    });
                    stack.Children.Add(new TextBlock
                    {
                        Text = $"{e.Multiplier}x XP!",
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xB6, 0xC1)),
                        FontSize = 14,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        Margin = new Thickness(0, 4, 0, 0)
                    });

                    border.Child = stack;

                    var popup = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Background = System.Windows.Media.Brushes.Transparent,
                        Topmost = true,
                        ShowInTaskbar = false,
                        ShowActivated = false,
                        SizeToContent = SizeToContent.WidthAndHeight,
                        Content = border
                    };

                    // Position at top-center of primary screen
                    popup.Loaded += (s, args) =>
                    {
                        try
                        {
                            var workArea = SystemParameters.WorkArea;
                            popup.Left = workArea.Left + (workArea.Width - popup.ActualWidth) / 2;
                            popup.Top = workArea.Top + 40;
                        }
                        catch { }
                    };

                    _luckyProcPopup = popup;

                    // Fade in
                    popup.Opacity = 0;
                    popup.Show();

                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                    popup.BeginAnimation(Window.OpacityProperty, fadeIn);

                    // Auto-close after 3 seconds with fade-out
                    var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    closeTimer.Tick += (s, args) =>
                    {
                        closeTimer.Stop();
                        try
                        {
                            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                            fadeOut.Completed += (s2, args2) =>
                            {
                                try { popup.Close(); } catch { }
                                if (_luckyProcPopup == popup) _luckyProcPopup = null;
                            };
                            popup.BeginAnimation(Window.OpacityProperty, fadeOut);
                        }
                        catch
                        {
                            try { popup.Close(); } catch { }
                        }
                    };
                    closeTimer.Start();
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Lucky proc popup failed: {Error}", ex.Message);
                }
            });
        }

        #endregion
    }
}
