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
    // UI refresh updates, slider event handlers, and general button-event handlers.
    public partial class MainWindow
    {
        #region UI Updates

        private void UpdateUI()
        {
            // Update all value labels
            SettingsTab.TxtPerMin.Text = ((int)SettingsTab.SliderPerMin.Value).ToString();
            SettingsTab.TxtImages.Text = ((int)SettingsTab.SliderImages.Value).ToString();
            SettingsTab.TxtMaxOnScreen.Text = ((int)SettingsTab.SliderMaxOnScreen.Value).ToString();
            SettingsTab.TxtSize.Text = $"{(int)SettingsTab.SliderSize.Value}%";
            SettingsTab.TxtOpacity.Text = $"{(int)SettingsTab.SliderOpacity.Value}%";
            SettingsTab.TxtFade.Text = $"{(int)SettingsTab.SliderFade.Value}%";
            SettingsTab.TxtPerHour.Text = ((int)SettingsTab.SliderPerHour.Value).ToString();
            SettingsTab.TxtTargets.Text = ((int)SettingsTab.SliderTargets.Value).ToString();
            SettingsTab.TxtDuration.Text = ((int)SettingsTab.SliderDuration.Value).ToString();
            SettingsTab.TxtTargetSize.Text = ((int)SettingsTab.SliderTargetSize.Value).ToString();
            SettingsTab.TxtSubPerMin.Text = ((int)SettingsTab.SliderSubPerMin.Value).ToString();
            SettingsTab.TxtFrames.Text = ((int)SettingsTab.SliderFrames.Value).ToString();
            SettingsTab.TxtSubOpacity.Text = $"{(int)SettingsTab.SliderSubOpacity.Value}%";
            SettingsTab.TxtWhisperVol.Text = $"{(int)SettingsTab.SliderWhisperVol.Value}%";
            SettingsTab.TxtMaster.Text = $"{(int)SettingsTab.SliderMaster.Value}%";
            SettingsTab.TxtDuck.Text = $"{(int)SettingsTab.SliderDuck.Value}%";
            ProgressionTab.TxtSpiralOpacity.Text = $"{(int)ProgressionTab.SliderSpiralOpacity.Value}%";
            ProgressionTab.TxtPinkOpacity.Text = $"{(int)ProgressionTab.SliderPinkOpacity.Value}%";
            ProgressionTab.TxtBubbleFreq.Text = ((int)ProgressionTab.SliderBubbleFreq.Value).ToString();
            ProgressionTab.TxtRampDuration.Text = $"{(int)ProgressionTab.SliderRampDuration.Value} min";
            ProgressionTab.TxtMultiplier.Text = $"{ProgressionTab.SliderMultiplier.Value:F1}x";
        }

        private void UpdateLevelDisplay()
        {
            var s = App.Settings.Current;
            var level = s.PlayerLevel;
            var xp = s.PlayerXP;
            var xpNeeded = App.Progression.GetXPForLevel(level);

            TxtLevel.Text = $"Lvl {level}";
            TxtLevelLabel.Text = $"LVL {level}";

            // XP readout + bar fill (chrome FX): the number odometers from its previous value and
            // the bar tweens to its new width instead of both snapping. Both fall back to an
            // instant set under reduced motion. See AnimateXpDisplay in MainWindow.ChromeFx.cs -
            // it reads the same container ActualWidth this used to (XPBar.Parent is the wrapping
            // Grid, NOT the rounded outer Border; casting it to Border made the expression always
            // null and froze the bar at its 100px XAML default).
            AnimateXpDisplay(xp, xpNeeded, level);

            // Update title based on level
            var rankTitle = level switch
            {
                < 20 => "BASIC BIMBO",
                < 50 => "DUMB AIRHEAD",
                < 100 => "SYNTHETIC BLOWDOLL",
                _ => "PERFECT FUCKPUPPET"
            };
            TxtPlayerTitle.Text = App.Mods?.MakeModAware(rankTitle) ?? rankTitle;

            // Update unlockables visibility based on level
            UpdateUnlockablesVisibility(level);

            // Update XP bar login state
            UpdateXPBarLoginState();

            // Update stat pills visibility and values
            UpdateStatPills();
        }

        /// <summary>
        /// Applies mod text replacements to all hardcoded feature/section labels in the XAML.
        /// Called on startup and when the active mod changes.
        /// </summary>
        private void ApplyModFeatureNames()
        {
            // If a mod is active, use mod-aware text; otherwise use localized text
            string ML(string englishText, string locKey) => ModAwareLabel(englishText, locKey);

            // Dashboard cards draw their own icon, so their captions must not carry the
            // leading emoji the shared section keys use — strip it off.
            string MLCard(string englishText, string locKey) => StripLeadingGlyph(ML(englishText, locKey));

            // Velvet mosaic dashboard cards — the XAML Title="" literals are plain English
            // design-time fallbacks, so this is the only place they get localized OR
            // mod-renamed. Keys are the same ones the matching popup titles use.
            if (SettingsTab.CardFlash != null) SettingsTab.CardFlash.Title = MLCard("Flash Images", "section_flash_images");
            if (SettingsTab.CardVisuals != null) SettingsTab.CardVisuals.Title = MLCard("Visuals", "section_visuals");
            if (SettingsTab.CardVideo != null) SettingsTab.CardVideo.Title = MLCard("Mandatory Video", "section_mandatory_video");
            if (SettingsTab.CardSubliminal != null) SettingsTab.CardSubliminal.Title = MLCard("Subliminals", "section_subliminals_2");
            if (SettingsTab.CardSpiral != null) SettingsTab.CardSpiral.Title = MLCard("Spiral Overlay", "label_spiral_overlay");
            if (SettingsTab.CardLockCard != null) SettingsTab.CardLockCard.Title = MLCard("Lock Card", "label_lock_card");
            if (SettingsTab.CardPinkFilter != null) SettingsTab.CardPinkFilter.Title = MLCard("Pink Filter", "label_pink_filter");
            if (SettingsTab.CardMindWipe != null) SettingsTab.CardMindWipe.Title = MLCard("Mind Wipe", "label_mind_wipe");
            if (SettingsTab.CardBubblePop != null) SettingsTab.CardBubblePop.Title = MLCard("Bubble Pop", "label_bubble_pop");
            if (SettingsTab.CardBouncingText != null) SettingsTab.CardBouncingText.Title = MLCard("Bouncing Text", "label_bouncing_text");
            if (SettingsTab.CardSystem != null) SettingsTab.CardSystem.Title = MLCard("System", "section_system");
            if (SettingsTab.CardBubbleCount != null) SettingsTab.CardBubbleCount.Title = MLCard("Bubble Count", "label_bubble_count");

            // Main section headers
            if (SettingsTab.TxtFeatureFlash != null) SettingsTab.TxtFeatureFlash.Text = ML("⚡ Flash Images", "section_flash_images");
            if (SettingsTab.TxtFeatureVideo != null) SettingsTab.TxtFeatureVideo.Text = ML("🎬 Mandatory Video", "section_mandatory_video");
            if (SettingsTab.TxtFeatureSubliminal != null) SettingsTab.TxtFeatureSubliminal.Text = ML("💭 Subliminals", "section_subliminals");
            if (SettingsTab.TxtFeatureWhispers != null) SettingsTab.TxtFeatureWhispers.Text = ML("📊 Audio Whispers", "label_audio_whispers");

            // Enhancement locked/unlocked pairs
            if (ProgressionTab.TxtFeatureSpiralLocked != null) ProgressionTab.TxtFeatureSpiralLocked.Text = ML("🌀 Spiral Overlay", "label_spiral_overlay");
            if (ProgressionTab.TxtFeatureSpiral != null) ProgressionTab.TxtFeatureSpiral.Text = ML("🌀 Spiral Overlay", "label_spiral_overlay");
            if (ProgressionTab.TxtFeaturePinkFilterLocked != null) ProgressionTab.TxtFeaturePinkFilterLocked.Text = ML("💗 Pink Filter", "label_pink_filter");
            if (ProgressionTab.TxtFeaturePinkFilter != null) ProgressionTab.TxtFeaturePinkFilter.Text = ML("💗 Pink Filter", "label_pink_filter");
            if (ProgressionTab.TxtFeatureBubblePopLocked != null) ProgressionTab.TxtFeatureBubblePopLocked.Text = ML("🫧 Bubble Pop", "label_bubble_pop");
            if (ProgressionTab.TxtFeatureBubblePop != null) ProgressionTab.TxtFeatureBubblePop.Text = ML("🫧 Bubble Pop", "label_bubble_pop");
            if (ProgressionTab.TxtFeatureLockCardLocked != null) ProgressionTab.TxtFeatureLockCardLocked.Text = ML("📐 Lock Card", "label_lock_card");
            if (ProgressionTab.TxtFeatureLockCard != null) ProgressionTab.TxtFeatureLockCard.Text = ML("📐 Lock Card", "label_lock_card");
            if (ProgressionTab.TxtFeatureBubbleCountLocked != null) ProgressionTab.TxtFeatureBubbleCountLocked.Text = ML("🫧 Bubble Count", "label_bubble_count");
            if (ProgressionTab.TxtFeatureBubbleCount != null) ProgressionTab.TxtFeatureBubbleCount.Text = ML("🫧 Bubble Count", "label_bubble_count");
            if (ProgressionTab.TxtFeatureBouncingLocked != null) ProgressionTab.TxtFeatureBouncingLocked.Text = ML("📺 Bouncing Text", "label_bouncing_text");
            if (ProgressionTab.TxtFeatureBouncing != null) ProgressionTab.TxtFeatureBouncing.Text = ML("📺 Bouncing Text", "label_bouncing_text");
            if (ProgressionTab.TxtFeatureBrainDrain != null) ProgressionTab.TxtFeatureBrainDrain.Text = ML("💧 Brain Drain", "label_brain_drain");
            if (ProgressionTab.TxtFeatureMindWipeLocked != null) ProgressionTab.TxtFeatureMindWipeLocked.Text = ML("🧠 Mind Wipe", "label_mind_wipe");
            if (ProgressionTab.TxtFeatureMindWipe != null) ProgressionTab.TxtFeatureMindWipe.Text = ML("🧠 Mind Wipe", "label_mind_wipe");
            if (PresetsTab.TxtFeatureCornerGif != null) PresetsTab.TxtFeatureCornerGif.Text = ML("🖼 Corner GIF", "label_corner_gif");

            // Preset/session detail labels
            if (PresetsTab.TxtDetailFlashLabel != null) PresetsTab.TxtDetailFlashLabel.Text = ML("⚡ Flash Images", "section_flash_images");
            if (PresetsTab.TxtDetailVideoLabel != null) PresetsTab.TxtDetailVideoLabel.Text = ML("🎬 Mandatory Videos", "label_mandatory_videos");
            if (PresetsTab.TxtDetailSubLabel != null) PresetsTab.TxtDetailSubLabel.Text = ML("💭 Subliminals", "section_subliminals");
            if (PresetsTab.TxtSessionFlashLabel != null) PresetsTab.TxtSessionFlashLabel.Text = ML("⚡ Flash Images", "section_flash_images");
            if (PresetsTab.TxtSessionSubLabel != null) PresetsTab.TxtSessionSubLabel.Text = ML("💭 Subliminals", "section_subliminals");

            // Autonomy toggle labels
            if (BambiTakeoverTab.TxtAutoFlash != null) BambiTakeoverTab.TxtAutoFlash.Text = ML("Flashes", "tab_flashes");
            if (BambiTakeoverTab.TxtAutoVideo != null) BambiTakeoverTab.TxtAutoVideo.Text = ML("Videos", "tab_videos");
            if (BambiTakeoverTab.TxtAutoSubliminal != null) BambiTakeoverTab.TxtAutoSubliminal.Text = ML("Subliminals", "tab_subliminals");
            if (BambiTakeoverTab.TxtAutoBubbles != null) BambiTakeoverTab.TxtAutoBubbles.Text = ML("Bubbles", "label_bubbles");
            if (BambiTakeoverTab.TxtAutoPinkFilter != null) BambiTakeoverTab.TxtAutoPinkFilter.Text = ML("Pink Filter", "label_pink_filter");
            if (BambiTakeoverTab.TxtAutoLockCards != null) BambiTakeoverTab.TxtAutoLockCards.Text = ML("Lock Cards", "label_lock_card");
            if (BambiTakeoverTab.TxtAutoBouncing != null) BambiTakeoverTab.TxtAutoBouncing.Text = ML("Bouncing", "label_bouncing_text");
            if (BambiTakeoverTab.TxtAutoMindwipe != null) BambiTakeoverTab.TxtAutoMindwipe.Text = ML("Mindwipe", "label_mind_wipe");

            // Enhancement tab tooltip
            if (BtnEnhancements != null)
                BtnEnhancements.ToolTip = App.Mods?.GetTabTooltip() ?? Loc.Get("tooltip_enhancement_tree");

            // Stat pill tooltips
            if (PillConditioningTime != null)
                PillConditioningTime.ToolTip = App.Mods?.GetStatPillTooltip("pink_hours")
                    ?? ML("Total conditioning time (Pink Hours skill)", "tooltip_total_conditioning_time_pink_hours_skill");
            if (PillOnlineUsers != null)
                PillOnlineUsers.ToolTip = App.Mods?.GetStatPillTooltip("hive_mind")
                    ?? ML("Bimbos online now (Hive Mind skill)", "tooltip_bimbos_online_now_hive_mind_skill");
            if (PillRankPercentile != null)
                PillRankPercentile.ToolTip = App.Mods?.GetStatPillTooltip("popular_girl")
                    ?? ML("Your rank percentile (Popular Girl skill)", "tooltip_your_rank_percentile_popular_girl_skill");

            // Mod-aware Bambi Takeover header + side-nav button label
            // (Drone mod → "Drone Takeover", SissyHypno → "Sissy Takeover", etc.)
            var takeoverLabel = App.Mods?.GetTakeoverLabel() ?? Loc.Get("tab_takeover");
            if (BambiTakeoverTab.TxtBambiTakeoverHeader != null) BambiTakeoverTab.TxtBambiTakeoverHeader.Text = takeoverLabel;
            if (TxtSubBambiTakeover != null) TxtSubBambiTakeover.Text = takeoverLabel;

            // Refresh bonus chips with updated names
            RefreshXPBarBonuses();

            // Also refresh rank title
            UpdateLevelDisplay();

            // Show/hide the Bimbo Journal sub-tab based on the active mod.
            ApplyBimboJournalModVisibility();
        }

        /// <summary>
        /// Resolves a hardcoded English UI label: if the active mod's text replacements
        /// change it, the mod wording wins; otherwise fall back to the localized string.
        /// Shared by ApplyModFeatureNames and the dashboard feature popups so a mod that
        /// renames e.g. "Flash Images" retitles the tile, the popup, and the section header.
        /// </summary>
        internal static string ModAwareLabel(string englishText, string locKey) =>
            App.Mods?.MakeModAware(englishText) is string modText && modText != englishText
                ? modText : Loc.Get(locKey);

        /// <summary>
        /// Drops a leading emoji/symbol prefix (e.g. "⚡ Flash Images" → "Flash Images").
        /// The shared section loc keys all carry one, but the dashboard cards render an
        /// icon of their own and would double up.
        /// </summary>
        private static string StripLeadingGlyph(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var i = 0;
            while (i < text.Length && !char.IsLetterOrDigit(text, i))
                i += char.IsSurrogatePair(text, i) ? 2 : 1;
            // All-symbol string (or nothing to strip) — leave it untouched.
            return i > 0 && i < text.Length ? text.Substring(i) : text;
        }

        /// <summary>
        /// The Bimbo Journal is built around bimbofication photo tracks, so it only
        /// fits the CCP Default, Bambi Sleep, and Sissy Hypno mods. For any other mod
        /// (Dronification, Locked, community mods) we hide its sub-tab entry point —
        /// and, if it happens to be open, fall back to the Daily/Weekly panel.
        /// Re-run whenever the active mod changes (via ApplyModFeatureNames).
        /// </summary>
        private void ApplyBimboJournalModVisibility()
        {
            if (QuestsTab.BtnQuestSubRoadmap == null) return;

            var modId = App.Mods?.ActiveModId;
            bool supported = modId == Models.BuiltInMods.CCPDefaultId
                          || modId == Models.BuiltInMods.BambiSleepId
                          || modId == Models.BuiltInMods.SissyHypnoId;

            QuestsTab.BtnQuestSubRoadmap.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;

            // If the journal is hidden out from under the user, snap back to Daily/Weekly.
            if (!supported && QuestsTab.RoadmapPanel?.Visibility == Visibility.Visible)
            {
                QuestsTab.RoadmapPanel.Visibility = Visibility.Collapsed;
                if (QuestsTab.DailyWeeklyPanel != null) QuestsTab.DailyWeeklyPanel.Visibility = Visibility.Visible;
                if (QuestsTab.BtnQuestSubDaily != null) QuestsTab.BtnQuestSubDaily.Style = (Style)FindResource("TabButtonActive");
            }
        }

        /// <summary>
        /// Updates the XP bar visibility based on login status.
        /// Shows a login prompt overlay when user is not logged in.
        /// </summary>
        private void UpdateXPBarLoginState()
        {
            var isLoggedIn = App.IsLoggedIn;

            if (XPBarLoginOverlay != null && XPBarContent != null)
            {
                if (isLoggedIn)
                {
                    // User is logged in - show normal XP bar
                    XPBarLoginOverlay.Visibility = Visibility.Collapsed;
                    XPBarContent.Opacity = 1.0;
                }
                else
                {
                    // User is not logged in - show overlay and gray out XP bar
                    XPBarLoginOverlay.Visibility = Visibility.Visible;
                    XPBarContent.Opacity = 0.3;
                }
            }
        }

        /// <summary>
        /// Updates the stat pill visibility and values based on unlocked skills.
        /// Pills only show when their respective skills are unlocked.
        /// </summary>
        private void UpdateStatPills()
        {
            // Privacy backstop: keep the mic pill honest on the timer even if some state change slipped
            // past the event-driven UpdateMicPill() calls. Cheap (one visibility set) and self-healing.
            UpdateMicPill();

            if (App.SkillTree == null) return;

            // Pink Hours: Total Conditioning Time (5 points - tier 1)
            if (PillConditioningTime != null)
            {
                bool hasPinkHours = App.SkillTree.HasSkill("pink_hours");
                PillConditioningTime.Visibility = hasPinkHours ? Visibility.Visible : Visibility.Collapsed;

                if (hasPinkHours && TxtPillConditioningTime != null)
                {
                    double totalMinutes;

                    if (_isRunning && _conditioningTimeTimer != null)
                    {
                        // Use baseline + session elapsed to avoid double-counting
                        // (storedMinutes gets incremented every 60s by the tracker, so adding
                        // sessionElapsed on top would count those minutes twice)
                        var sessionElapsed = DateTime.Now - _conditioningStartTime;
                        totalMinutes = _conditioningBaselineMinutes + sessionElapsed.TotalMinutes;
                    }
                    else
                    {
                        totalMinutes = App.Settings?.Current?.TotalConditioningMinutes ?? 0;
                    }

                    // Format as hours, minutes, and seconds
                    var totalSeconds = totalMinutes * 60;
                    var hours = (int)(totalSeconds / 3600);
                    var minutes = (int)((totalSeconds % 3600) / 60);
                    var seconds = (int)(totalSeconds % 60);
                    TxtPillConditioningTime.Text = $"{hours}h {minutes}m {seconds}s";
                }
            }

            // Hive Mind: Online Users Count (60 points total - tier 3)
            if (PillOnlineUsers != null)
            {
                bool hasHiveMind = App.SkillTree.HasSkill("hive_mind");
                PillOnlineUsers.Visibility = hasHiveMind ? Visibility.Visible : Visibility.Collapsed;

                if (hasHiveMind && TxtPillOnlineUsers != null)
                {
                    // Get online user count from leaderboard service
                    var onlineCount = App.Leaderboard?.OnlineUsers ?? 0;
                    TxtPillOnlineUsers.Text = onlineCount.ToString();
                }
            }

            // Popular Girl: Rank Percentile (130 points total - tier 4)
            if (PillRankPercentile != null)
            {
                bool hasPopularGirl = App.SkillTree.HasSkill("popular_girl");
                PillRankPercentile.Visibility = hasPopularGirl ? Visibility.Visible : Visibility.Collapsed;

                if (hasPopularGirl && TxtPillRankPercentile != null)
                {
                    // Get rank percentile from leaderboard
                    var percentile = App.Leaderboard?.GetPlayerPercentile() ?? 0;

                    if (percentile > 0)
                    {
                        TxtPillRankPercentile.Text = $"Top {percentile}%";
                    }
                    else if (App.Leaderboard?.Entries?.Count > 0)
                    {
                        // Leaderboard loaded but player not found - might be unranked or need to sync
                        TxtPillRankPercentile.Text = Loc.Get("label_unranked");
                    }
                    else
                    {
                        // Leaderboard not loaded yet
                        TxtPillRankPercentile.Text = Loc.Get("label_loading_2");
                    }
                }
            }

            // Good Girl Streak: Fire icon with current streak count (tier 2)
            if (StreakFirePill != null)
            {
                bool hasGoodGirlStreak = App.SkillTree?.HasSkill("good_girl_streak") == true;
                StreakFirePill.Visibility = hasGoodGirlStreak ? Visibility.Visible : Visibility.Collapsed;

                if (hasGoodGirlStreak)
                {
                    if (TxtStreakFireCount != null)
                    {
                        var streak = App.Achievements?.Progress?.ConsecutiveDays ?? 0;
                        TxtStreakFireCount.Text = streak.ToString();
                    }

                    // Show shield icon if shields are available
                    if (TxtStreakShieldIcon != null)
                    {
                        var shieldsRemaining = App.Settings?.Current?.StreakShieldsRemaining ?? 0;
                        TxtStreakShieldIcon.Visibility = shieldsRemaining > 0 ? Visibility.Visible : Visibility.Collapsed;
                        TxtStreakShieldIcon.ToolTip = shieldsRemaining > 0
                            ? "Streak shield available — protects your streak if you miss a day"
                            : "Streak shield used — resets weekly";
                    }
                }
            }

            RefreshXPBarBonuses();
        }

        private void RefreshXPBarBonuses()
        {
            if (XPBarBonusList == null) return;

            var breakdown = App.SkillTree?.GetMultiplierBreakdown() ?? new List<(string, double)>();
            XPBarBonusList.Children.Clear();

            foreach (var (source, value) in breakdown)
            {
                if (source == "Base") continue;

                var displaySource = App.Mods?.MakeModAware(source) ?? source;
                var chip = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(42, 42, 74)), // #2A2A4A - matches stat pills
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(6, 3, 6, 3),
                    Margin = new Thickness(0, 0, 8, 0),
                    ToolTip = GetBonusChipTooltip(source)
                };

                chip.Child = new TextBlock
                {
                    Text = $"+{value:P0} {displaySource}",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentLightColorHex() ?? "#FFB6C1")),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center
                };

                XPBarBonusList.Children.Add(chip);
            }
        }

        private static string? GetBonusChipTooltip(string source)
        {
            string M(string text) => App.Mods?.MakeModAware(text) ?? text;

            // Check for explicit mod override first
            string? modTip = null;
            if (source.StartsWith("Streak Power"))
                modTip = App.Mods?.GetBoostTooltip("streak_power");
            else
            {
                var skillId = source switch
                {
                    "Sparkle Boost" => "sparkle_boost_1",
                    "Extra Sparkly" => "sparkle_boost_2",
                    "Maximum Sparkle" => "sparkle_boost_3",
                    "Night Shift" => "night_shift",
                    "Early Bird Bimbo" => "early_bird_bimbo",
                    "PINK RUSH ACTIVE!" => "pink_rush",
                    _ => null
                };
                if (skillId != null)
                    modTip = App.Mods?.GetBoostTooltip(skillId);
            }
            if (modTip != null) return modTip;

            // Fall back to defaults with MakeModAware
            if (source.StartsWith("Streak Power")) return M("Skill tree bonus: +0.5% XP per day of consecutive use (max 15%)");
            return source switch
            {
                "Sparkle Boost" => M("Skill tree bonus: +10% XP from Sparkle Boost"),
                "Extra Sparkly" => M("Skill tree bonus: +15% XP from Extra Sparkly (stacks with Sparkle Boost)"),
                "Maximum Sparkle" => M("Skill tree bonus: +20% XP from Maximum Sparkle (stacks with other Sparkle skills)"),
                "Night Shift" => M("Skill tree bonus: +50% XP for conditioning between 11 PM and 5 AM"),
                "Early Bird Bimbo" => M("Skill tree bonus: +50% XP for conditioning between 5 AM and 8 AM"),
                "PINK RUSH ACTIVE!" => M("Skill tree bonus: 3x XP multiplier! Random 60-second windows of boosted XP"),
                _ => null
            };
        }

        /// <summary>
        /// Start a timer to periodically update stat pill values (conditioning time, online users, rank).
        /// Updates every 30 seconds.
        /// </summary>
        private void StartStatPillUpdateTimer()
        {
            if (_statPillUpdateTimer != null) return; // Already started

            _statPillUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };

            _statPillUpdateTimer.Tick += (s, e) =>
            {
                try
                {
                    UpdateStatPills();
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Error updating stat pills");
                }
            };

            _statPillUpdateTimer.Start();
            App.Logger?.Debug("Stat pill update timer started (30s interval)");
        }

        /// <summary>
        /// Start tracking conditioning time (updates live while engine is running).
        /// Updates display every second, saves to storage every minute, syncs to server every 15 minutes.
        /// </summary>
        private void StartConditioningTimeTracker()
        {
            if (_conditioningTimeTimer != null) return; // Already started

            _conditioningStartTime = DateTime.Now;
            _conditioningBaselineMinutes = App.Settings?.Current?.TotalConditioningMinutes ?? 0;
            _conditioningTimeSecondCounter = 0;

            // Update display every second for LIVE tracking
            _conditioningTimeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            _conditioningTimeTimer.Tick += (s, e) =>
            {
                try
                {
                    _conditioningTimeSecondCounter++;

                    // Update stat pill display every second (live update)
                    UpdateStatPills();

                    // Debug log every 10 seconds to verify timer is working
                    if (_conditioningTimeSecondCounter % 10 == 0)
                    {
                        var elapsed = DateTime.Now - _conditioningStartTime;
                        App.Logger?.Debug("Conditioning time tracker tick: {Seconds}s elapsed, stored: {Minutes}m",
                            (int)elapsed.TotalSeconds, App.Settings?.Current?.TotalConditioningMinutes ?? 0);
                    }

                    // Save to local storage every minute (avoid excessive disk writes)
                    if (_conditioningTimeSecondCounter >= 60)
                    {
                        var elapsed = DateTime.Now - _conditioningStartTime;
                        App.SkillTree?.AddConditioningTime(1.0); // Add 1 minute
                        _conditioningTimeSecondCounter = 0;
                        App.Logger?.Debug("Conditioning time saved to storage: {Time}", App.SkillTree?.GetFormattedConditioningTime());
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Error tracking conditioning time");
                }
            };

            _conditioningTimeTimer.Start();

            // Start server sync timer (every 15 minutes)
            _conditioningTimeSyncTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(15)
            };
            _conditioningTimeSyncTimer.Tick += async (s, e) =>
            {
                await SyncConditioningTimeToServerAsync();
            };
            _conditioningTimeSyncTimer.Start();

            App.Logger?.Debug("Conditioning time tracker started (live updates every second, server sync every 15 minutes)");
        }

        /// <summary>
        /// Stop tracking conditioning time and sync to server.
        /// </summary>
        private void StopConditioningTimeTracker()
        {
            if (_conditioningTimeTimer == null) return;

            _conditioningTimeTimer.Stop();
            _conditioningTimeTimer = null;

            // Stop server sync timer
            if (_conditioningTimeSyncTimer != null)
            {
                _conditioningTimeSyncTimer.Stop();
                _conditioningTimeSyncTimer = null;
            }

            // Add any remaining partial minutes not yet saved by the 60-second tracker
            try
            {
                var elapsed = DateTime.Now - _conditioningStartTime;
                var expectedTotal = _conditioningBaselineMinutes + elapsed.TotalMinutes;
                var currentStored = App.Settings?.Current?.TotalConditioningMinutes ?? 0;
                var remainingMinutes = expectedTotal - currentStored;

                if (remainingMinutes > 0)
                {
                    App.SkillTree?.AddConditioningTime(remainingMinutes);
                    App.Logger?.Debug("Added remaining {Minutes:F2} minutes on stop", remainingMinutes);
                }

                // Final update to stat pills
                UpdateStatPills();

                // Sync to server on stop
                _ = SyncConditioningTimeToServerAsync();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error finalizing conditioning time");
            }

            App.Logger?.Debug("Conditioning time tracker stopped");
        }

        /// <summary>
        /// Sync conditioning time to server.
        /// Called every 15 minutes during session and on session end.
        /// </summary>
        private async Task SyncConditioningTimeToServerAsync()
        {
            try
            {
                // Only sync if user is authenticated (Patreon or Discord)
                if (App.ProfileSync == null)
                {
                    App.Logger?.Debug("Skipping conditioning time sync - ProfileSync not available");
                    return;
                }

                // Only sync if user is authenticated
                if (App.Patreon?.IsAuthenticated != true && App.Discord?.IsAuthenticated != true)
                {
                    App.Logger?.Debug("Skipping conditioning time sync - user not authenticated");
                    return;
                }

                App.Logger?.Information("Syncing conditioning time to server ({Minutes:F1} minutes)",
                    App.Settings?.Current?.TotalConditioningMinutes ?? 0);

                // ProfileSyncService will automatically include conditioning time in the sync
                await App.ProfileSync.SyncProfileAsync();

                App.Logger?.Information("Conditioning time synced successfully");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to sync conditioning time to server");
            }
        }

        private void UpdateUnlockablesVisibility(int level)
        {
            try
            {
                // Feature level gating has been removed — every feature is available from level 1.
                // The legacy Locked/Unlocked panels below live inside the collapsed SettingsTab.LegacyDashboardHost,
                // but we still flip them to the unlocked state so nothing appears locked if anything
                // ever ends up rendering them.
                if (ProgressionTab.SpiralLocked != null) ProgressionTab.SpiralLocked.Visibility = Visibility.Collapsed;
                if (ProgressionTab.SpiralUnlocked != null) ProgressionTab.SpiralUnlocked.Visibility = Visibility.Visible;
                if (ProgressionTab.PinkFilterLocked != null) ProgressionTab.PinkFilterLocked.Visibility = Visibility.Collapsed;
                if (ProgressionTab.PinkFilterUnlocked != null) ProgressionTab.PinkFilterUnlocked.Visibility = Visibility.Visible;
                if (ProgressionTab.SpiralFeatureImage != null) SetFeatureImageBlur(ProgressionTab.SpiralFeatureImage, false);
                if (ProgressionTab.PinkFilterFeatureImage != null) SetFeatureImageBlur(ProgressionTab.PinkFilterFeatureImage, false);

                if (ProgressionTab.BubblesLocked != null) ProgressionTab.BubblesLocked.Visibility = Visibility.Collapsed;
                if (ProgressionTab.BubblesUnlocked != null) ProgressionTab.BubblesUnlocked.Visibility = Visibility.Visible;
                if (ProgressionTab.BubblePopFeatureImage != null) SetFeatureImageBlur(ProgressionTab.BubblePopFeatureImage, false);

                if (ProgressionTab.LockCardLocked != null) ProgressionTab.LockCardLocked.Visibility = Visibility.Collapsed;
                if (ProgressionTab.LockCardUnlocked != null) ProgressionTab.LockCardUnlocked.Visibility = Visibility.Visible;
                if (ProgressionTab.LockCardFeatureImage != null) SetFeatureImageBlur(ProgressionTab.LockCardFeatureImage, false);

                if (ProgressionTab.Level50Locked != null) ProgressionTab.Level50Locked.Visibility = Visibility.Collapsed;
                if (ProgressionTab.Level50Unlocked != null) ProgressionTab.Level50Unlocked.Visibility = Visibility.Visible;
                if (ProgressionTab.BubbleCountFeatureImage != null) SetFeatureImageBlur(ProgressionTab.BubbleCountFeatureImage, false);

                if (ProgressionTab.Level60Locked != null) ProgressionTab.Level60Locked.Visibility = Visibility.Collapsed;
                if (ProgressionTab.Level60Unlocked != null) ProgressionTab.Level60Unlocked.Visibility = Visibility.Visible;
                if (ProgressionTab.BouncingTextFeatureImage != null) SetFeatureImageBlur(ProgressionTab.BouncingTextFeatureImage, false);

                if (ProgressionTab.MindWipeLocked != null) ProgressionTab.MindWipeLocked.Visibility = Visibility.Collapsed;
                if (ProgressionTab.MindWipeUnlocked != null) ProgressionTab.MindWipeUnlocked.Visibility = Visibility.Visible;
                if (ProgressionTab.MindWipeFeatureImage != null) SetFeatureImageBlur(ProgressionTab.MindWipeFeatureImage, false);

                if (ProgressionTab.BrainDrainLocked != null) ProgressionTab.BrainDrainLocked.Visibility = Visibility.Collapsed;
                if (ProgressionTab.BrainDrainUnlocked != null) ProgressionTab.BrainDrainUnlocked.Visibility = Visibility.Visible;
                if (ProgressionTab.BrainDrainFeatureImage != null) SetFeatureImageBlur(ProgressionTab.BrainDrainFeatureImage, false);

                // velvet-mosaic dashboard cards are never locked anymore.
                if (SettingsTab.CardSpiral != null) SettingsTab.CardSpiral.IsLocked = false;
                if (SettingsTab.CardPinkFilter != null) SettingsTab.CardPinkFilter.IsLocked = false;
                if (SettingsTab.CardBubblePop != null) SettingsTab.CardBubblePop.IsLocked = false;
                if (SettingsTab.CardLockCard != null) SettingsTab.CardLockCard.IsLocked = false;
                if (SettingsTab.CardBubbleCount != null) SettingsTab.CardBubbleCount.IsLocked = false;
                if (SettingsTab.CardBouncingText != null) SettingsTab.CardBouncingText.IsLocked = false;
                if (SettingsTab.CardMindWipe != null) SettingsTab.CardMindWipe.IsLocked = false;

                // Lab Tab: Requires Patreon T2 / whitelist. Cover every entitlement source —
                // this gate is destructive below (force-clears AllowAiToControlEffects and
                // SAVES), so a stale-negative here wipes a legit user's setting: live tier,
                // server-linked tier (Discord login), whitelist, and SubscribeStar T2.
                var labUnlocked = App.Patreon?.CurrentTier >= PatreonTier.Level2
                    || (App.Settings?.Current?.PatreonTier ?? 0) >= 2
                    || App.Patreon?.IsWhitelisted == true
                    || App.SubscribeStar?.CurrentTier >= PatreonTier.Level2;
                if (LabTab.LabSmokescreen != null) LabTab.LabSmokescreen.Visibility = labUnlocked ? Visibility.Collapsed : Visibility.Visible;

                // AI effect control lives in the Lab — force-disable for non-T2 users so settings can't outlive the entitlement.
                if (!labUnlocked)
                {
                    var cp = App.Settings?.Current?.CompanionPrompt;
                    if (cp != null && cp.AllowAiToControlEffects)
                    {
                        cp.AllowAiToControlEffects = false;
                        App.Settings?.Save();
                    }
                    if (LabTab.ChkCapEffects != null && LabTab.ChkCapEffects.IsChecked == true)
                        LabTab.ChkCapEffects.IsChecked = false;
                    if (LabTab.EffectPermsPanel != null)
                        LabTab.EffectPermsPanel.Visibility = Visibility.Collapsed;
                }

                // Bambi Takeover: Requires Patreon (any tier)
                var autonomyUnlocked = App.Patreon?.HasPremiumAccess == true;
                if (BambiTakeoverTab.AutonomyLocked != null) BambiTakeoverTab.AutonomyLocked.Visibility = autonomyUnlocked ? Visibility.Collapsed : Visibility.Visible;
                if (BambiTakeoverTab.AutonomyUnlocked != null) BambiTakeoverTab.AutonomyUnlocked.Visibility = autonomyUnlocked ? Visibility.Visible : Visibility.Collapsed;

                // Update lock message
                if (BambiTakeoverTab.TxtAutonomyLockStatus != null && BambiTakeoverTab.TxtAutonomyLockMessage != null)
                {
                    BambiTakeoverTab.TxtAutonomyLockStatus.Text = Loc.Get("label_patreon_only");
                    BambiTakeoverTab.TxtAutonomyLockMessage.Text = Loc.Get("label_support_on_patreon_to_unlock");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error("UpdateUnlockablesVisibility: Error updating unlockables visibility: {Error}", ex.Message);
            }
        }
        
        /// <summary>
        /// Applies or removes blur effect on feature images based on lock state
        /// </summary>
        private void SetFeatureImageBlur(Rectangle? featureImageRect, bool blur)
        {
            try
            {
                if (featureImageRect == null)
                {
                    App.Logger?.Warning("SetFeatureImageBlur: featureImageRect is null.");
                    return;
                }

                if (blur)
                {
                    featureImageRect.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 15 };
                    App.Logger?.Debug("SetFeatureImageBlur: Applied blur to {ElementName}", featureImageRect.Name);
                }
                else
                {
                    featureImageRect.Effect = null;
                    App.Logger?.Debug("SetFeatureImageBlur: Removed blur from {ElementName}", featureImageRect.Name);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error("SetFeatureImageBlur: Error setting blur effect for {ElementName}: {Error}", featureImageRect?.Name, ex.Message);
            }
        }

        #endregion

        #region Slider Events

        internal void SliderPerMin_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtPerMin == null) return;
            SettingsTab.TxtPerMin.Text = ((int)e.NewValue).ToString();
            UpdateAudioLinkState();
            ApplySettingsLive();
        }

        internal void SliderImages_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtImages == null) return;
            SettingsTab.TxtImages.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        internal void SliderMaxOnScreen_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtMaxOnScreen == null) return;
            SettingsTab.TxtMaxOnScreen.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        internal void SliderSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtSize == null) return;
            SettingsTab.TxtSize.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        internal void SliderOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtOpacity == null) return;
            SettingsTab.TxtOpacity.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        internal void SliderFade_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtFade == null) return;
            SettingsTab.TxtFade.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        internal void SliderFlashDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtFlashDuration == null) return;
            SettingsTab.TxtFlashDuration.Text = $"{(int)e.NewValue}s";
            App.Settings.Current.FlashDuration = (int)e.NewValue;
        }

        internal void ChkFlashAudio_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = SettingsTab.ChkFlashAudio.IsChecked ?? true;
            App.Settings.Current.FlashAudioEnabled = isEnabled;
            
            // Enable/disable duration slider based on audio link
            SettingsTab.SliderFlashDuration.IsEnabled = !isEnabled;
            SettingsTab.SliderFlashDuration.Opacity = isEnabled ? 0.5 : 1.0;
            
            // Show/hide warning
            SettingsTab.TxtAudioWarning.Visibility = isEnabled ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateAudioLinkState()
        {
            if (_isLoading) return;
            
            var flashFreq = (int)SettingsTab.SliderPerMin.Value;
            
            // If flashes > 60, force audio OFF and disable checkbox
            if (flashFreq > 60)
            {
                SettingsTab.ChkFlashAudio.IsChecked = false;
                SettingsTab.ChkFlashAudio.IsEnabled = false;
                App.Settings.Current.FlashAudioEnabled = false;
                SettingsTab.SliderFlashDuration.IsEnabled = true;
                SettingsTab.SliderFlashDuration.Opacity = 1.0;
                SettingsTab.TxtAudioWarning.Visibility = Visibility.Visible;
                SettingsTab.TxtAudioWarning.Text = Loc.Get("label_audio_off_60_h");
            }
            else
            {
                SettingsTab.ChkFlashAudio.IsEnabled = true;
                SettingsTab.TxtAudioWarning.Text = Loc.Get("label_max_60_h");
                SettingsTab.TxtAudioWarning.Visibility = (SettingsTab.ChkFlashAudio.IsChecked ?? true) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        internal void SliderPerHour_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtPerHour == null) return;
            SettingsTab.TxtPerHour.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        internal void SliderTargets_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtTargets == null) return;
            SettingsTab.TxtTargets.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        internal void ChkRandomizeTargets_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ApplySettingsLive();
        }

        internal void SliderDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtDuration == null) return;
            SettingsTab.TxtDuration.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        internal void SliderTargetSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtTargetSize == null) return;
            SettingsTab.TxtTargetSize.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        internal void SliderSubPerMin_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtSubPerMin == null) return;
            SettingsTab.TxtSubPerMin.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        internal void SliderFrames_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtFrames == null) return;
            SettingsTab.TxtFrames.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        internal void SliderSubOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtSubOpacity == null) return;
            SettingsTab.TxtSubOpacity.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        internal void SliderWhisperVol_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtWhisperVol == null) return;
            SettingsTab.TxtWhisperVol.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        internal void SliderMaster_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtMaster == null) return;
            SettingsTab.TxtMaster.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();

            // Update volume on all currently playing audio
            var volume = (int)e.NewValue;
            App.Video?.UpdateMasterVolume(volume);
            App.BrainDrain?.UpdateMasterVolume(volume);
        }

        internal void SliderVideoVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtVideoVolume == null) return;
            SettingsTab.TxtVideoVolume.Text = $"{(int)e.NewValue}%";
            App.Settings.Current.VideoVolume = (int)e.NewValue;
            App.Video?.UpdateVideoVolume((int)e.NewValue);
        }

        internal void SliderDuck_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || SettingsTab.TxtDuck == null) return;
            SettingsTab.TxtDuck.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        internal void ChkAudioDuck_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            // If ducking was just disabled, immediately restore audio for any ducked sessions
            if (SettingsTab.ChkAudioDuck.IsChecked == false)
            {
                App.Audio?.ForceUnduck();
            }

            ApplySettingsLive();
        }

        internal void ChkExcludeBambiCloudDucking_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ApplySettingsLive();
        }

        // Guards against a second click while the diagnostic is still running — the probe
        // reassigns the shared test player, so two concurrent runs would race it.
        private bool _testingAudio;

        internal async void BtnTestAudio_Click(object sender, RoutedEventArgs e)
        {
            if (_testingAudio) return;
            _testingAudio = true;
            try
            {
                var audio = App.Audio;
                // Off the UI thread: the probe opens WaveOut devices, which blocks for many
                // seconds (or forever) when the audio endpoint is wedged (#686).
                var result = audio == null
                    ? "Audio service not initialized"
                    : await Task.Run(() => audio.TestAudioPlayback());

                App.Logger?.Information("[AudioDiag] Test requested:\n{Result}", result);
                System.Windows.MessageBox.Show(result, "Audio Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[AudioDiag] Test failed");
                System.Windows.MessageBox.Show($"Audio diagnostics failed: {ex.Message}", "Audio Diagnostics",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _testingAudio = false;
            }
        }

        // Set during PopulateAudioOutputDevices to suppress the SelectionChanged save while
        // we're rebuilding the list (otherwise restoring the persisted selection writes itself
        // back, which is harmless but logs a redundant info line).
        private bool _populatingAudioOutputs;

        private void PopulateAudioOutputDevices()
        {
            if (SettingsTab.CmbAudioOutputDevice == null || App.Audio == null) return;
            try
            {
                _populatingAudioOutputs = true;
                var devices = App.Audio.EnumerateOutputDevices();
                SettingsTab.CmbAudioOutputDevice.ItemsSource = devices;
                SettingsTab.CmbAudioOutputDevice.DisplayMemberPath = nameof(Services.AudioService.AudioOutputDevice.Name);

                // Restore persisted selection: prefer ID match, fall back to name (handles ID
                // changes after driver reinstall / device reorder).
                var savedId = App.Settings?.Current?.AudioOutputDeviceId ?? "";
                var savedName = App.Settings?.Current?.AudioOutputDeviceName ?? "";
                Services.AudioService.AudioOutputDevice? pick = null;
                foreach (var d in devices)
                {
                    if (!string.IsNullOrEmpty(savedId) && d.Id == savedId) { pick = d; break; }
                }
                if (pick == null && !string.IsNullOrEmpty(savedName))
                {
                    foreach (var d in devices)
                    {
                        if (string.Equals(d.Name, savedName, StringComparison.OrdinalIgnoreCase)) { pick = d; break; }
                    }
                }
                SettingsTab.CmbAudioOutputDevice.SelectedItem = pick ?? devices[0]; // index 0 = "System default"
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("PopulateAudioOutputDevices failed: {Error}", ex.Message);
            }
            finally
            {
                _populatingAudioOutputs = false;
            }
        }

        internal void CmbAudioOutputDevice_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isLoading || _populatingAudioOutputs) return;
            if (SettingsTab.CmbAudioOutputDevice?.SelectedItem is not Services.AudioService.AudioOutputDevice dev) return;
            if (App.Settings?.Current == null) return;

            App.Settings.Current.AudioOutputDeviceId = dev.Id ?? "";
            App.Settings.Current.AudioOutputDeviceName = dev.Name ?? "";
            App.Settings.Save();

            // Invalidate cached device-number resolution + drain pooled WaveOuts (their
            // DeviceNumber is locked once Init() ran, so they need to be re-created).
            App.Audio?.InvalidateOutputDeviceCache();
            try { Services.BubbleService.DrainAudioDevicePool(); } catch { }
            try { QuizWindow.DrainAudioDevicePool(); } catch { }

            App.Logger?.Information("Audio output device set to '{Name}' (id={Id})", dev.Name, string.IsNullOrEmpty(dev.Id) ? "(default)" : dev.Id);
        }

        internal void BtnAudioOutputRefresh_Click(object sender, RoutedEventArgs e)
        {
            PopulateAudioOutputDevices();
        }

        internal void SliderSpiralOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || ProgressionTab.TxtSpiralOpacity == null) return;
            ProgressionTab.TxtSpiralOpacity.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        internal void SliderPinkOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || ProgressionTab.TxtPinkOpacity == null) return;
            ProgressionTab.TxtPinkOpacity.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        internal void SliderBubbleFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || ProgressionTab.TxtBubbleFreq == null) return;
            ProgressionTab.TxtBubbleFreq.Text = ((int)e.NewValue).ToString();
            App.Settings.Current.BubblesFrequency = (int)e.NewValue;

            if (_isRunning)
            {
                App.Bubbles.RefreshFrequency();
            }

            App.Settings.Save();
        }

        internal void SliderBubbleVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || ProgressionTab.TxtBubbleVolume == null) return;
            ProgressionTab.TxtBubbleVolume.Text = $"{(int)e.NewValue}%";
            App.Settings.Current.BubblesVolume = (int)e.NewValue;
            App.Settings.Save();
        }

        internal void ChkSpiralEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ProgressionTab.ChkSpiralEnabled.IsChecked ?? false;
            App.Settings.Current.SpiralEnabled = isEnabled;
            
            // Immediately update overlay if engine is running
            if (_isRunning)
            {
                App.Overlay.RefreshOverlays();
                App.Logger?.Information("Spiral overlay toggled: {Enabled}", isEnabled);
            }
            
            App.Settings.Save();
        }

        internal void ChkPinkFilterEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ProgressionTab.ChkPinkFilterEnabled.IsChecked ?? false;
            App.Settings.Current.PinkFilterEnabled = isEnabled;
            
            // Immediately update overlay if engine is running
            if (_isRunning)
            {
                App.Overlay.RefreshOverlays();
                App.Logger?.Information("Pink filter toggled: {Enabled}", isEnabled);
            }
            
            App.Settings.Save();
        }

        internal void ChkBubblesEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ProgressionTab.ChkBubblesEnabled.IsChecked ?? false;
            App.Settings.Current.BubblesEnabled = isEnabled;

            // Immediately update bubbles if engine is running
            if (_isRunning)
            {
                if (isEnabled)
                {
                    App.Bubbles.Start();
                }
                else
                {
                    App.Bubbles.Stop();
                }
                App.Logger?.Information("Bubbles toggled: {Enabled}", isEnabled);
            }
            
            App.Settings.Save();
        }

        internal void ChkLockCardEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ProgressionTab.ChkLockCardEnabled.IsChecked ?? false;
            App.Settings.Current.LockCardEnabled = isEnabled;
            
            // Immediately update lock card service if engine is running
            if (_isRunning)
            {
                if (isEnabled)
                {
                    App.LockCard.Start();
                }
                else
                {
                    App.LockCard.Stop();
                }
                App.Logger?.Information("Lock Card toggled: {Enabled}", isEnabled);
            }
            
            App.Settings.Save();
        }

        internal void ChkFlashEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = SettingsTab.ChkFlashEnabled.IsChecked ?? true;
            App.Settings.Current.FlashEnabled = isEnabled;

            // Immediately start/stop flash service if engine is running
            if (_isRunning)
            {
                if (isEnabled)
                {
                    App.Flash.Start();
                }
                else
                {
                    App.Flash.Stop();
                }
                App.Logger?.Information("Flash images toggled: {Enabled}", isEnabled);
            }

            App.Settings.Save();
        }

        internal void ChkClickable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isClickable = SettingsTab.ChkClickable.IsChecked ?? true;
            App.Settings.Current.FlashClickable = isClickable;
            App.Logger?.Information("Flash clickable toggled: {Enabled}", isClickable);
            App.Settings.Save();
        }

        internal void ChkCorruption_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = SettingsTab.ChkCorruption.IsChecked ?? false;
            App.Settings.Current.CorruptionMode = isEnabled;
            App.Logger?.Information("Hydra mode toggled: {Enabled}", isEnabled);
            App.Settings.Save();
        }

        internal void ChkFlashGlow_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = SettingsTab.ChkFlashGlow.IsChecked ?? true;
            App.Settings.Current.FlashGlowEnabled = isEnabled;
            App.Logger?.Information("Flash glow toggled: {Enabled}", isEnabled);
            App.Settings.Save();
        }

        /// <summary>
        /// Toggles linked vs independent timing for hydra spawns~ 🔗✨
        /// Linked = hydra children share the parent's remaining timer.
        /// Independent = each hydra spawn gets a fresh full-duration lifetime.
        /// </summary>
        internal void ChkHydraLinked_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isLinked = SettingsTab.ChkHydraLinked.IsChecked ?? true;
            App.Settings.Current.HydraLinkedTiming = isLinked;
            App.Logger?.Information("Hydra linked timing toggled: {Linked}", isLinked);
            App.Settings.Save();
        }

        internal void ChkVideoEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = SettingsTab.ChkVideoEnabled.IsChecked ?? false;
            App.Settings.Current.MandatoryVideosEnabled = isEnabled;

            // Immediately start/stop video service if engine is running
            if (_isRunning)
            {
                if (isEnabled)
                {
                    App.Video.Start();
                }
                else
                {
                    App.Video.Stop();
                }
                App.Logger?.Information("Mandatory videos toggled: {Enabled}", isEnabled);
            }

            App.Settings.Save();
        }

        internal void ChkSubliminalEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            // Single authority: persists the flag and live-applies start/stop (idempotently),
            // so this checkbox and the feature popup can't churn the service between them.
            App.Subliminal?.SetEnabled(SettingsTab.ChkSubliminalEnabled.IsChecked ?? false);
        }

        internal void ChkAudioWhispers_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = SettingsTab.ChkAudioWhispers.IsChecked ?? false;
            App.Settings.Current.SubAudioEnabled = isEnabled;
            App.Logger?.Information("Audio whispers toggled: {Enabled}", isEnabled);
            App.Settings.Save();
        }

        internal void ChkMiniGameEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = SettingsTab.ChkMiniGameEnabled.IsChecked ?? false;
            App.Settings.Current.AttentionChecksEnabled = isEnabled;
            App.Logger?.Information("Attention checks toggled: {Enabled}", isEnabled);
            App.Settings.Save();
        }

        internal void SliderLockCardFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || ProgressionTab.TxtLockCardFreq == null) return;
            ProgressionTab.TxtLockCardFreq.Text = ((int)e.NewValue).ToString();
            App.Settings.Current.LockCardFrequency = (int)e.NewValue;
            App.Settings.Save();
        }

        internal void SliderLockCardRepeats_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || ProgressionTab.TxtLockCardRepeats == null) return;
            ProgressionTab.TxtLockCardRepeats.Text = $"{(int)e.NewValue}x";
            App.Settings.Current.LockCardRepeats = (int)e.NewValue;
            App.Settings.Save();
        }






        internal void SliderRampDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || ProgressionTab.TxtRampDuration == null) return;
            ProgressionTab.TxtRampDuration.Text = $"{(int)e.NewValue} min";
            ApplySettingsLive();
        }

        internal void SliderMultiplier_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || ProgressionTab.TxtMultiplier == null) return;
            ProgressionTab.TxtMultiplier.Text = $"{e.NewValue:F1}x";
            ApplySettingsLive();
        }

        #endregion

        #region Button Events
        
        internal void ImgLogo_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Track for Neon Obsession achievement (20 rapid clicks on the avatar/logo)
            App.Achievements?.TrackAvatarClick();

            // Bark hook: rolling 60s click count drives the click-escalation eggs.
            try { App.Bark?.NotifyAvatarClicked(); } catch { }

            // Log click count for debugging
            var clickCount = App.Achievements?.Progress.AvatarClickCount ?? 0;
            App.Logger?.Debug("Logo/Avatar clicked! Count: {Count}/20", clickCount);

            // Easter egg tracking (100 clicks in 60 seconds)
            if (!_easterEggTriggered)
            {
                var now = DateTime.Now;
                if (_easterEggFirstClick == DateTime.MinValue || (now - _easterEggFirstClick).TotalSeconds > 60)
                {
                    // Reset if more than 60 seconds passed
                    _easterEggFirstClick = now;
                    _easterEggClickCount = 1;
                }
                else
                {
                    _easterEggClickCount++;
                    if (_easterEggClickCount >= 100)
                    {
                        _easterEggTriggered = true;
                        ShowEasterEgg();
                    }
                }
            }

            // Visual feedback - quick pulse effect
            if (SettingsTab.ImgLogo != null)
            {
                var pulse = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 1.05,
                    Duration = TimeSpan.FromMilliseconds(80),
                    AutoReverse = true
                };

                var scaleTransform = SettingsTab.ImgLogo.RenderTransform as System.Windows.Media.ScaleTransform;
                if (scaleTransform == null)
                {
                    scaleTransform = new System.Windows.Media.ScaleTransform(1, 1);
                    SettingsTab.ImgLogo.RenderTransformOrigin = new Point(0.5, 0.5);
                    SettingsTab.ImgLogo.RenderTransform = scaleTransform;
                }

                scaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pulse);
                scaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pulse);
            }
        }

        #region Weekly Intake Pass tile (Dashboard centre)

        // ------------------------------------------------------------------------------------
        // The Dashboard's centre logo tile doubles as the weekly Graded Intake pass card.
        //
        // Shape of the thing:
        //   * PATRONS NEVER SEE THE CARD. The pass is the free tier's consolation prize; showing
        //     it to someone who already has unlimited runs advertises a limit they do not have.
        //     Premium is the one state that keeps the plain wordmark, and it is checked first.
        //   * ONLY "pass waiting" (Available) shows the card at all. Signed-out and spent both
        //     keep the wordmark - see the long note in RefreshIntakePassTile for why that is a
        //     correctness rule and not a style choice. ApplyIntakePassFaceState still carries
        //     NeedsLogin and Spent branches; they are unreachable today and kept only so the
        //     product decision can be reversed by relaxing one condition rather than rewriting
        //     the copy. Do not "clean them up" without changing that condition first.
        //   * The card art is PER-NICHE and the niche comes from the active mod, so the tile
        //     re-resolves it on ModChanged as well as on the state changes.
        //   * The "pass waiting" state turns, and it turns FOREVER: hold the wordmark 8s,
        //     turn 2.5 times, hold the card 8s, turn back, repeat for as long as the Dashboard is
        //     on screen. The tile is a two-sided plate rather than a one-shot reveal, so a pass
        //     cannot be missed by having looked away at the wrong moment. A signed-out or spent
        //     card never turns - a flourish for a card that cannot be spent is a lie told with
        //     animation.
        //
        // The spin is the standard WPF fake - ScaleX 1 -> 0, swap the visible face at the zero
        // crossing, 0 -> 1 - chained through DoubleAnimation.Completed. Deliberately NOT a
        // Storyboard: Storyboard.SetTargetName silently no-ops across the SettingsTabView
        // namescope (see MainWindow.Animations.cs and the note in KeywordHighlightService), so
        // every animation here is started straight on the transform via BeginAnimation. The CTA
        // breath at the bottom of the card follows the same rule.
        // ------------------------------------------------------------------------------------

        /// <summary>How long each face is held before the tile turns to the other one. Long
        /// enough that each side is a thing you looked at rather than a thing that flickered
        /// past, and long enough that the CTA at the bottom gets a couple of full breaths in
        /// while the card is up.</summary>
        private const int IntakePassFaceHoldMs = 8000;

        /// <summary>Per-half-turn durations, front to back. Four quick turns and one long one:
        /// the plate spins at roughly constant speed, then drops all its momentum into a single
        /// slow half-turn that settles onto the card. Each entry is split across the two ScaleX
        /// ramps of its half-turn.
        ///
        /// The COUNT MUST STAY ODD. Each half-turn swaps the visible face at its zero crossing
        /// starting from the logo, so an even count would land back on the wordmark.</summary>
        private static readonly int[] IntakePassHalfTurnMs = { 105, 115, 130, 150, 560 };

        /// <summary>Skew (degrees) applied at the thin point of each half-turn. Purely cosmetic
        /// fake perspective - it always animates back to 0 and is torn down with the spin.</summary>
        private const double IntakePassSkewDeg = 6.0;

        /// <summary>Monotonic spin token. Every chained Completed callback re-checks it, so a tab
        /// switch (or a state change) mid-spin can bump the token and be certain the stale tail of
        /// the old chain will not repaint a superseded face or leave the tile half-scaled.</summary>
        private int _intakePassSpinGen;

        /// <summary>True while the alternation is live (holding a face or mid-turn). Keeps a
        /// repeat <see cref="RefreshIntakePassTile"/> from stacking a second loop on the first -
        /// two loops on one transform would fight over ScaleX and the tile would judder.</summary>
        private bool _intakePassLoopRunning;

        /// <summary>Which face is up right now. The turn toggles from whatever is showing rather
        /// than assuming it started on the wordmark, so the same code drives both directions.</summary>
        private bool _intakePassShowingCard;

        /// <summary>One-shot latches for the subscriptions below (this method is called on
        /// every Dashboard entry, so it has to be safe to hammer).</summary>
        private bool _intakePassLoadedHooked;
        private bool _intakePassStateHooked;
        private bool _intakePassVisibilityHooked;
        private bool _intakePassModHooked;
        private bool _intakePassHelpAttached;

        /// <summary>Half a breath of the CTA - it auto-reverses, so a full in-and-out is twice
        /// this. Paced off a slow human breath rather than a UI blink: at the old 1150ms the swell
        /// was travelling roughly a third of a pixel per rendered frame, which is small enough
        /// that glyph rasterisation quantises it into a few visible steps instead of a glide.
        /// The scale amplitude stays small because the swell is meant to be felt, not read.</summary>
        private static readonly TimeSpan IntakePassCtaBreath = TimeSpan.FromMilliseconds(1900);

        /// <summary>Should the CTA be breathing right now? Set by
        /// <see cref="ApplyIntakePassFaceState"/> (true only for an actually-spendable pass) and
        /// consumed by <see cref="SetIntakePassFace"/>, which is the single choke point for "is
        /// the card on screen" - so the pulse can never outlive the face it belongs to.</summary>
        private bool _intakePassCtaShouldPulse;

        /// <summary>True while the CTA animations are attached, so teardown is idempotent.</summary>
        private bool _intakePassCtaPulsing;

        /// <summary>
        /// Decides what the centre tile shows - logo or weekly pass card - and whether the
        /// reveal spin plays. Safe to call repeatedly and from any point in the lifecycle;
        /// call it on every entry to the Dashboard tab.
        /// </summary>
        internal void RefreshIntakePassTile()
        {
            try
            {
                if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted)
                    return;

                var tab = SettingsTab;
                if (tab?.IntakePassFace == null || tab.LogoFaceLogo == null
                    || tab.LogoFlipScale == null || tab.LogoFlipSkew == null)
                    return;

                // The ceremony can be requested before the tab is realised (startup lands on the
                // Dashboard before its visual tree exists). Park the request on Loaded rather than
                // animating into a null render target.
                if (!tab.IsLoaded)
                {
                    if (!_intakePassLoadedHooked)
                    {
                        _intakePassLoadedHooked = true;
                        tab.Loaded += (_, _) => RefreshIntakePassTile();
                    }
                    return;
                }

                var pass = App.IntakePass;

                // Repaint when the door changes underneath us: a login lands the card without a
                // tab bounce, and completing a run takes it away again. Marshalled, because
                // PassStateChanged can be raised from a background validation.
                if (!_intakePassStateHooked && pass != null)
                {
                    _intakePassStateHooked = true;
                    pass.PassStateChanged += (_, _) =>
                    {
                        var d = Application.Current?.Dispatcher;
                        if (d == null || d.HasShutdownStarted) return;
                        d.BeginInvoke(new Action(RefreshIntakePassTile));
                    };
                }

                // Repaint when the ACTIVE MOD changes. The card art is per-niche and the niche is
                // derived from the mod (IntakeNiche.Current), so a mod switch invalidates whatever
                // is currently painted - and nothing else re-runs this: the mod picker is in the
                // top bar and the Manage Mods dialog is modal, so a user already sitting on the
                // Dashboard gets no tab change, no Loaded, no visibility change, and no pass-state
                // change. That was the whole bug (switch to Drone, keep the old niche's ticket).
                //
                // ModChanged is the app's authoritative mod signal - the same one the avatar
                // (AvatarTubeWindow), the theme/feature images (MainWindow.xaml.cs) and the window
                // chrome (App.xaml.cs) already ride - so this cannot drift away from them the way a
                // second hook off ApplyActiveModChange would (that method is only on MainWindow's
                // own two paths and misses any other caller of ModService.ActivateMod).
                //
                // Marshalled with the shutdown guard because ActivateMod has no thread affinity.
                // ModService clears the resource cache BEFORE raising this, so the re-resolve below
                // is guaranteed to see the new mod's art.
                //
                // This does NOT disturb the flip loop: a re-entrant refresh repaints the face via
                // ApplyIntakePassFaceState and then bails at the _intakePassLoopRunning check
                // without touching the transform or the spin generation, so the turn in progress
                // keeps its rhythm. The swap is deliberately IMMEDIATE rather than deferred to the
                // next zero crossing - a mod switch already hard-cuts the logo, the theme, the
                // feature images and the skill tree in the same frame, so one more instant repaint
                // is what the user expects; whereas art that stayed wrong for up to 8s would be
                // indistinguishable from the bug this fixes. When the wordmark face is up the swap
                // is invisible anyway (the card is Collapsed).
                if (!_intakePassModHooked && App.Mods != null)
                {
                    _intakePassModHooked = true;
                    App.Mods.ModChanged += (_, _) =>
                    {
                        var d = Application.Current?.Dispatcher;
                        if (d == null || d.HasShutdownStarted) return;
                        d.BeginInvoke(new Action(RefreshIntakePassTile));
                    };
                }

                // Leaving the Dashboard has to kill the CTA breath. It repeats forever, and a
                // forever animation left running on a collapsed tile burns a composition slot for
                // the rest of the session with nothing on screen to show for it. Coming back is
                // just another refresh (which is idempotent), and this also catches the window
                // being minimised, where IsVisible goes false without a tab change.
                if (!_intakePassVisibilityHooked)
                {
                    _intakePassVisibilityHooked = true;
                    tab.IsVisibleChanged += (_, args) =>
                    {
                        if (args.NewValue is bool visible && !visible)
                        {
                            StopIntakePassCtaPulse();

                            // Kill a reveal nobody is watching. Animations keep running on a
                            // collapsed element, so without this the tile would turn - and spend
                            // the week's ceremony - off screen. Cancelling during the opening
                            // dwell leaves the week unlatched (see StartIntakePassSpin), so the
                            // reveal is owed again on the next visit rather than lost.
                            CancelIntakePassSpin();
                            return;
                        }
                        RefreshIntakePassTile();
                    };
                }

                EnsureIntakePassHelpPopover();

                // ONLY AN AVAILABLE PASS GETS THE CARD. Three different reasons converge here:
                //
                //  - Premium: patrons have the feature outright, so a "weekly pass" tile would
                //    invent a limit they do not have.
                //  - Spent: the owner's design is that the door shuts once the pass is used. The
                //    countdown still lives on the Graded Intake tab's gate copy, which is where a
                //    user goes when they actually want another run.
                //  - NeedsLogin: signed-out is the DEFAULT state of a fresh install.
                //
                // The last two are not cosmetic. The card sits inside LogoBrandFrame and marks
                // MouseLeftButtonDown handled, so while it is up the rapid-click logo easter egg
                // (and its achievement) cannot fire. Showing the card in Spent/NeedsLogin would
                // have taken that away permanently from every signed-out user and from everyone
                // else for most of the week - a regression in an existing feature, paid to display
                // a countdown that is already shown elsewhere.
                var state = pass?.State ?? IntakePassState.Premium;
                if (pass == null || state != IntakePassState.Available)
                {
                    CancelIntakePassSpin();
                    SetIntakePassFace(showCard: false);   // also stops the CTA breath
                    return;
                }

                ApplyIntakePassFaceState(state);

                // Only alternate while the tile is actually on screen. A background refresh (a
                // login callback landing while the user is three tabs away) must not leave a
                // forever-loop turning an invisible control.
                var onScreen = tab.Visibility == Visibility.Visible && tab.IsVisible;

                if (_intakePassLoopRunning) return;   // already alternating; don't restart it

                if (!onScreen)
                {
                    // Off screen: park on the card so returning to the tab never catches it
                    // mid-wordmark, and start the loop from the top on the way back in.
                    SetIntakePassFace(showCard: true);
                    return;
                }

                StartIntakePassFlipLoop();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("RefreshIntakePassTile: {E}", ex.Message);
            }
        }

        /// <summary>Swaps which of the two stacked faces is visible, and gates the CTA breath on
        /// the result. The flip transform is not touched here, so this is safe to call at a zero
        /// crossing.</summary>
        private void SetIntakePassFace(bool showCard)
        {
            var tab = SettingsTab;
            if (tab?.IntakePassFace == null || tab.LogoFaceLogo == null) return;

            tab.IntakePassFace.Visibility = showCard ? Visibility.Visible : Visibility.Collapsed;
            tab.LogoFaceLogo.Visibility = showCard ? Visibility.Collapsed : Visibility.Visible;
            _intakePassShowingCard = showCard;   // the turn toggles off this, so it must stay honest

            // Every route that hides the card comes through here, so this is the one place that
            // has to remember to stop a forever-repeating animation.
            if (showCard && _intakePassCtaShouldPulse) StartIntakePassCtaPulse();
            else StopIntakePassCtaPulse();
        }

        /// <summary>
        /// Paints the card for one of the three non-premium states: art, copy, tooltip, and
        /// whether the CTA is allowed to breathe. Premium never reaches here - the caller has
        /// already put the wordmark back.
        /// </summary>
        private void ApplyIntakePassFaceState(IntakePassState state)
        {
            var tab = SettingsTab;
            if (tab?.IntakePassFace == null) return;

            // Art first, because whether it resolved decides the whole layout. IntakeNiche is the
            // single source of truth for the mod -> niche mapping (the nudge popup reads the same
            // one), and it is documented to return null when the resource is missing - a mod can
            // ship a broken override. Null means: hide every art layer and let the vector block
            // carry the card, exactly as it did before there was art.
            ImageSource? art = null;
            try { art = Services.Quiz.IntakeNiche.PassCardImage(); }
            catch (Exception ex) { App.Logger?.Debug("Intake pass art: {E}", ex.Message); }

            var hasArt = art != null;

            if (tab.IntakePassArtBrush != null) tab.IntakePassArtBrush.ImageSource = art;
            if (tab.IntakePassArt != null)
            {
                tab.IntakePassArt.Visibility = hasArt ? Visibility.Visible : Visibility.Collapsed;
                // A spent week reads as a used ticket: the art stays so the tile keeps its
                // identity, drained of its glow so it plainly is not an invitation.
                tab.IntakePassArt.Opacity = state == IntakePassState.Spent ? 0.45 : 1.0;
            }
            if (tab.IntakePassScrim != null)
                tab.IntakePassScrim.Visibility = hasArt ? Visibility.Visible : Visibility.Collapsed;

            // The paragraph block only has room when there is no art beneath it; over the ticket
            // it would sit straight on the artwork's busy middle.
            if (tab.IntakePassVectorBody != null)
                tab.IntakePassVectorBody.Visibility = hasArt ? Visibility.Collapsed : Visibility.Visible;

            string headline, body, cta, tip;
            switch (state)
            {
                // Signed out. The pass is per-account, so there is nothing to claim yet - the copy
                // must invite a login WITHOUT implying a run is already sitting there waiting.
                case IntakePassState.NeedsLogin:
                    headline = Loc.Get("intake_pass_card_headline");
                    body = Loc.Get("intake_pass_card_body_needs_login");
                    cta = Loc.Get("intake_pass_card_cta_needs_login");
                    tip = Loc.Get("tooltip_intake_pass_card_needs_login");
                    break;

                // Already ran it. Say so, and say when it comes back - "come back later" with no
                // date is the kind of copy that gets filed as a bug.
                case IntakePassState.Spent:
                {
                    var days = IntakePassService.DaysUntilNextPass;
                    headline = Loc.Get("intake_pass_card_headline_spent");
                    body = Loc.GetF("intake_pass_card_body_spent", days);
                    cta = days <= 1
                        ? Loc.Get("intake_pass_card_cta_spent_tomorrow")
                        : Loc.GetF("intake_pass_card_cta_spent", days);
                    tip = Loc.GetF("tooltip_intake_pass_card_spent", days);
                    break;
                }

                default:
                    headline = Loc.Get("intake_pass_card_headline");
                    body = Loc.Get("intake_pass_card_body");
                    cta = Loc.Get("intake_pass_card_cta");
                    tip = Loc.Get("tooltip_intake_pass_card");
                    break;
            }

            if (tab.IntakePassHeadline != null) tab.IntakePassHeadline.Text = headline;
            if (tab.IntakePassBody != null) tab.IntakePassBody.Text = body;
            if (tab.IntakePassCta != null)
            {
                tab.IntakePassCta.Text = cta;
                // A spent CTA is a status line, not a button - drop it to the muted register so it
                // does not read as something to press.
                tab.IntakePassCta.Foreground = state == IntakePassState.Spent
                    ? (TryFindResource("TextMutedBrush") as Brush ?? Brushes.Gainsboro)
                    : Brushes.White;
            }
            tab.IntakePassFace.ToolTip = tip;

            // Only a spendable pass breathes. SetIntakePassFace applies this the moment the face
            // is next shown or hidden, and is called immediately after us on every path.
            _intakePassCtaShouldPulse = state == IntakePassState.Available;
        }

        /// <summary>
        /// Starts the CTA's continuous breath - a small synchronised scale + opacity swell that
        /// makes the bottom line read as "press me" without the cost of a second animated tile.
        /// Idempotent; started only via <see cref="SetIntakePassFace"/> so it cannot outlive the
        /// card. Storyboards are not an option here (SetTargetName no-ops across the
        /// SettingsTabView namescope), so both animations go straight onto their targets.
        /// </summary>
        private void StartIntakePassCtaPulse()
        {
            var tab = SettingsTab;
            if (tab?.IntakePassCta == null || tab.IntakePassCtaScale == null) return;
            if (_intakePassCtaPulsing) return;   // already breathing; restarting only resets phase

            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

            var swell = new DoubleAnimation(1.0, 1.05, IntakePassCtaBreath)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease
            };
            var glow = new DoubleAnimation(0.65, 1.0, IntakePassCtaBreath)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease
            };

            // ONE clock driving both axes, not two BeginAnimation calls off the same timeline.
            // BeginAnimation mints a fresh clock per property, so ScaleX and ScaleY started their
            // forever-loops on different ticks and drifted apart - which does not read as "the
            // text is a bit wide", it reads as the glyphs shearing, i.e. exactly the dropped-frame
            // stutter this had. A shared clock cannot drift from itself.
            var swellClock = swell.CreateClock();
            tab.IntakePassCtaScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, swellClock);
            tab.IntakePassCtaScale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, swellClock);

            // Opacity gets its own clock. It is a composition-level property on a different object,
            // so it has nothing to stay pixel-locked to and drift here is invisible.
            tab.IntakePassCta.BeginAnimation(UIElement.OpacityProperty, glow);

            _intakePassCtaPulsing = true;
        }

        /// <summary>Tears the breath off the CTA and restores its resting values. A held animation
        /// pins the render target, so the base values are only re-set after
        /// BeginAnimation(prop, null) - same rule as the flip's teardown.</summary>
        private void StopIntakePassCtaPulse()
        {
            _intakePassCtaPulsing = false;

            var tab = SettingsTab;
            if (tab?.IntakePassCta == null || tab.IntakePassCtaScale == null) return;

            // ApplyAnimationClock(_, null) to match how the scale was attached - the clock-based
            // and BeginAnimation-based paths are not documented to be interchangeable, and a
            // half-detached forever-animation would keep the base values below from sticking.
            tab.IntakePassCtaScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, null);
            tab.IntakePassCtaScale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, null);
            tab.IntakePassCta.BeginAnimation(UIElement.OpacityProperty, null);

            tab.IntakePassCtaScale.ScaleX = 1;
            tab.IntakePassCtaScale.ScaleY = 1;
            tab.IntakePassCta.Opacity = 1;
        }

        /// <summary>
        /// Wires the card's "(?)" to the same interactive help popover the feature tiles use
        /// (<see cref="Controls.HelpPopover"/>) rather than inventing a second explainer surface -
        /// hover to peek, click to pin, click away to dismiss, one open at a time app-wide.
        ///
        /// The content is built here instead of being added to
        /// <see cref="Services.HelpContentService"/> because the pass card is not a dashboard
        /// feature section: it has no FeatureCard, no HelpSectionId and no tutorial clip, and the
        /// service's dictionary is keyed by exactly those.
        ///
        /// One-shot. HelpPopover caches its rendered card on first open, so a language switch mid
        /// session will not repaint an explainer that has already been opened - it is correct
        /// again on the next launch. Re-attaching on every refresh would close a pinned card out
        /// from under the user, which is the worse trade.
        /// </summary>
        private void EnsureIntakePassHelpPopover()
        {
            if (_intakePassHelpAttached) return;

            var btn = SettingsTab?.BtnIntakePassHelp;
            if (btn == null) return;

            _intakePassHelpAttached = true;
            try
            {
                btn.ToolTip = null;   // popover and ToolTip must never double-render
                Controls.HelpPopover.Attach(btn, BuildIntakePassHelpContent());
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Intake pass help popover: {E}", ex.Message);
            }
        }

        /// <summary>What the Weekly Intake Pass actually is, in the shape
        /// <see cref="HelpTooltipBuilder"/> renders: header, "What It Does", tips, "How It
        /// Works".</summary>
        private static HelpContent BuildIntakePassHelpContent() => new()
        {
            SectionId = "IntakePass",
            // The header icon is drawn by a plain TextBlock, which cannot render COLR/CPAL colour
            // emoji - a BMP dingbat is the only glyph that survives.
            Icon = "★",
            Title = Loc.Get("help_intake_pass_title"),
            WhatItDoes = Loc.Get("help_intake_pass_what"),
            Tips = new List<string>
            {
                Loc.Get("help_intake_pass_tip_free"),
                Loc.Get("help_intake_pass_tip_patron"),
                Loc.Get("help_intake_pass_tip_punch"),
            },
            HowItWorks = Loc.Get("help_intake_pass_how"),
        };

        /// <summary>Kills any in-flight spin and snaps the tile's transform back to rest. Called
        /// before every state change so the tile can never be left frozen mid-turn (a held
        /// animation also pins the render target, which is why the base values are re-set only
        /// after BeginAnimation(prop, null)).</summary>
        private void CancelIntakePassSpin()
        {
            var tab = SettingsTab;
            if (tab?.LogoFlipScale == null || tab.LogoFlipSkew == null) return;

            _intakePassSpinGen++;      // orphans every pending Completed callback
            _intakePassLoopRunning = false;

            tab.LogoFlipScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            tab.LogoFlipSkew.BeginAnimation(SkewTransform.AngleYProperty, null);
            tab.LogoFlipScale.ScaleX = 1;
            tab.LogoFlipSkew.AngleY = 0;
        }

        /// <summary>Starts the perpetual alternation, opening on the wordmark. Idempotent via
        /// <see cref="_intakePassLoopRunning"/> - the caller checks it before getting here.</summary>
        private void StartIntakePassFlipLoop()
        {
            CancelIntakePassSpin();               // clean slate + fresh generation token
            var gen = _intakePassSpinGen;
            _intakePassLoopRunning = true;

            var tab = SettingsTab;
            if (tab?.LogoFlipScale == null) { _intakePassLoopRunning = false; return; }

            // Open on the wordmark. The first turn is then the one that introduces the card, which
            // is the same beat the tile used to spend its once-a-week reveal on.
            SetIntakePassFace(showCard: false);

            App.Logger?.Debug("Intake pass tile: starting flip loop");
            HoldIntakePassFace(gen);
        }

        /// <summary>
        /// Holds whichever face is currently up, then turns to the other one. Together with
        /// <see cref="FinishIntakePassSpin"/> - which calls straight back into this - it forms the
        /// loop: hold, turn, hold, turn.
        /// </summary>
        /// <remarks>
        /// The hold is a 1 -> 1 no-op animation rather than a DispatcherTimer, so it is torn off by
        /// exactly the same BeginAnimation(prop, null) that <see cref="CancelIntakePassSpin"/>
        /// already runs on that property. A timer would need a second teardown path and could
        /// outlive the loop it belongs to - and this loop never ends on its own, so a leaked timer
        /// would keep firing at a dead tile for the rest of the session.
        ///
        /// This recurses through animation callbacks, not the stack, so the depth stays flat.
        /// </remarks>
        private void HoldIntakePassFace(int gen)
        {
            if (gen != _intakePassSpinGen) return;
            if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted) return;

            var tab = SettingsTab;
            if (tab?.LogoFlipScale == null) { _intakePassLoopRunning = false; return; }

            var hold = new DoubleAnimation(1.0, 1.0, TimeSpan.FromMilliseconds(IntakePassFaceHoldMs));
            hold.Completed += (_, _) =>
            {
                if (gen != _intakePassSpinGen) return;
                if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted) return;
                RunIntakePassSpinPhase(gen, 0);
            };
            tab.LogoFlipScale.BeginAnimation(ScaleTransform.ScaleXProperty, hold);
        }

        /// <summary>
        /// One ScaleX ramp of the spin, chained to the next from its Completed handler.
        /// Even phases close the plate (1 -> 0), odd phases open it (0 -> 1); the visible face is
        /// swapped at the zero crossing between them, which is the whole trick.
        /// </summary>
        private void RunIntakePassSpinPhase(int gen, int phase)
        {
            // Superseded, shutting down, or the tab got torn out from under us.
            if (gen != _intakePassSpinGen) return;
            if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted) return;

            var tab = SettingsTab;
            if (tab?.LogoFlipScale == null || tab.LogoFlipSkew == null) { _intakePassLoopRunning = false; return; }

            var halfTurn = phase / 2;
            var closing = (phase % 2) == 0;

            if (halfTurn >= IntakePassHalfTurnMs.Length)
            {
                FinishIntakePassSpin(gen);
                return;
            }

            var ms = Math.Max(1, IntakePassHalfTurnMs[halfTurn] / 2);
            var duration = TimeSpan.FromMilliseconds(ms);

            var scaleAnim = new DoubleAnimation(closing ? 1.0 : 0.0, closing ? 0.0 : 1.0, duration);
            var skewAnim = closing
                ? new DoubleAnimation(0.0, IntakePassSkewDeg, duration)
                : new DoubleAnimation(-IntakePassSkewDeg, 0.0, duration);

            // Ease ONLY the final, slow half-turn. The easing is not "slow down" - it approximates
            // the cos() an actual rotation would trace, and at 105ms a quick turn is over before
            // the eye can resolve that shape, so easing those just reads as each ramp stalling
            // into its own end. The deceleration comes from the widening durations; this is only
            // here to keep the one long turn from looking like a linear stretch.
            if (halfTurn >= IntakePassHalfTurnMs.Length - 1)
            {
                var ease = new CubicEase { EasingMode = closing ? EasingMode.EaseIn : EasingMode.EaseOut };
                scaleAnim.EasingFunction = ease;
                skewAnim.EasingFunction = ease;
            }

            scaleAnim.Completed += (_, _) =>
            {
                if (gen != _intakePassSpinGen) return;
                if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted) return;

                // At the zero crossing the plate is edge-on and one pixel wide - the only frame
                // where a face swap is invisible. Toggling from whatever is currently up (rather
                // than deriving it from halfTurn) is what lets the same chain drive both
                // directions; the odd half-turn count is what guarantees it lands on the opposite
                // face from the one it started on.
                if (closing) SetIntakePassFace(showCard: !_intakePassShowingCard);

                RunIntakePassSpinPhase(gen, phase + 1);
            };

            tab.LogoFlipScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            tab.LogoFlipSkew.BeginAnimation(SkewTransform.AngleYProperty, skewAnim);
        }

        /// <summary>Settles one turn: tears the animations off the transform (a held animation
        /// keeps the render target pinned) and snaps the base values, then hands straight back to
        /// <see cref="HoldIntakePassFace"/> to sit on the face it just landed on. The face itself
        /// is NOT set here - the last zero crossing already chose it, and overriding that would
        /// pin the loop to one side.</summary>
        private void FinishIntakePassSpin(int gen)
        {
            if (gen != _intakePassSpinGen) return;

            var tab = SettingsTab;
            if (tab?.LogoFlipScale != null && tab.LogoFlipSkew != null)
            {
                tab.LogoFlipScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                tab.LogoFlipSkew.BeginAnimation(SkewTransform.AngleYProperty, null);
                tab.LogoFlipScale.ScaleX = 1;
                tab.LogoFlipSkew.AngleY = 0;
            }

            HoldIntakePassFace(gen);
        }

        /// <summary>
        /// Click on the revealed pass card. Forwards straight to the Exclusives launch path, which
        /// already owns every gate (pass state, login, AI availability, first-run windowing) - the
        /// card must not grow a second opinion about who gets in.
        /// </summary>
        internal void IntakePassFace_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // The card sits inside LogoBrandFrame, whose own handler is the rapid-click easter
            // egg / achievement counter. Stop the bubble so launching an intake doesn't also
            // register as a logo poke.
            e.Handled = true;

            // Clicks on the "(?)" belong to the help popover. ButtonBase already marks
            // MouseLeftButtonDown handled so this should be unreachable, but the same guard exists
            // in FeatureCard.OnClick for the same reason: the cost of being wrong here is
            // launching an intake because someone asked what one is.
            if (e.OriginalSource is DependencyObject src && SettingsTab?.BtnIntakePassHelp != null
                && IsVisualDescendant(src, SettingsTab.BtnIntakePassHelp))
                return;

            try
            {
                BtnStartIntake_Click(this, new RoutedEventArgs());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Intake pass tile: launch from Dashboard card failed");
            }
        }

        #endregion





        private async void ShowEasterEgg()
        {
            int readerCount = -1;
            try
            {
                if (App.ProfileSync != null)
                    readerCount = await App.ProfileSync.RecordEasterEggReadAsync();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to fetch easter egg reader count");
            }

            if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted)
                return;

            // Once ever: add the companion's recorded voice on top of the written note (additive —
            // the note dialog still shows as before). The recording is bundled later; PlayNoteClip
            // no-ops if the file is missing, and we only latch the flag once it actually plays, so it
            // still fires the first time the clip exists. Start it before the modal note so the voice
            // plays while the note is read.
            if (App.Settings?.Current?.NewYearNoteReactionSeen != true)
            {
                var notePath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "note_newyear.wav");
                if (App.AvatarWindow?.PlayNoteClip(notePath) == true)
                    App.Settings.Current.NewYearNoteReactionSeen = true;
            }

            var easterEggWindow = new EasterEggWindow(readerCount);
            easterEggWindow.Owner = this;
            easterEggWindow.ShowDialog();
        }

        internal void BtnTestVideo_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("test_video"); } catch { }
            try
            {
                // Check if video is already playing - offer force reset if stuck
                if (App.Video.IsPlaying)
                {
                    var result = MessageBox.Show(
                        "A video appears to be playing.\n\nIf you don't see a video, it may be stuck. Click Yes to force reset and try again.",
                        "Video Playing",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        App.Logger?.Warning("User requested force reset of stuck video state");
                        App.Video.ForceCleanup();
                        // Also tear down any web-video takeover: ForceReset alone frees the
                        // queue slot but leaves the browser video playing, and the test video
                        // would then stack on top of it.
                        App.Autonomy?.ForceEndWebVideoTakeover();
                        App.InteractionQueue?.ForceReset();
                        // Continue to trigger video below
                    }
                    else
                    {
                        return;
                    }
                }

                // Check if another interaction is blocking - offer force reset if stuck
                if (App.InteractionQueue != null && !App.InteractionQueue.CanStart)
                {
                    var result = MessageBox.Show(
                        $"Another interaction is in progress ({App.InteractionQueue.CurrentInteraction}).\n\nIf this seems stuck, click Yes to force reset and try again.",
                        "Please Wait",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        App.Logger?.Warning("User requested force reset of stuck interaction queue");
                        App.Video.ForceCleanup();
                        App.Autonomy?.ForceEndWebVideoTakeover();
                        App.InteractionQueue.ForceReset();
                        // Continue to trigger video below
                    }
                    else
                    {
                        return;
                    }
                }

                App.Video.TriggerVideo();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error in BtnTestVideo_Click");
                MessageBox.Show($"Error triggering video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TriggerStartupVideo()
        {
            var startupPath = App.Settings.Current.StartupVideoPath;

            // If a specific video is configured, play that one
            if (!string.IsNullOrEmpty(startupPath) && System.IO.File.Exists(startupPath))
            {
                App.Logger?.Information("Playing startup video: {Path}", startupPath);
                App.Video.PlaySpecificVideo(startupPath, App.Settings.Current.StrictLockEnabled);
            }
            else
            {
                // Play a random video
                App.Logger?.Information("Playing random startup video");
                App.Video.TriggerVideo(silentIfEmpty: true);
            }
        }

        internal void BtnSelectStartupVideo_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.Get("title_select_startup_video"),
                Filter = "Video Files|*.mp4;*.mov;*.avi;*.wmv;*.mkv;*.webm|All Files|*.*",
                InitialDirectory = System.IO.Path.Combine(App.EffectiveAssetsPath, "videos")
            };

            if (dialog.ShowDialog() == true)
            {
                App.Settings.Current.StartupVideoPath = dialog.FileName;
                SettingsTab.TxtStartupVideo.Text = System.IO.Path.GetFileName(dialog.FileName);
                App.Settings.Save();
                App.Logger?.Information("Startup video set to: {Path}", dialog.FileName);
            }
        }

        internal void BtnClearStartupVideo_Click(object sender, RoutedEventArgs e)
        {
            App.Settings.Current.StartupVideoPath = null;
            SettingsTab.TxtStartupVideo.Text = Loc.Get("label_random");
            App.Settings.Save();
            App.Logger?.Information("Startup video cleared - will use random");
        }

        internal void BtnManageAttention_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TextEditorDialog("Attention Targets", App.Settings.Current.AttentionPool);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && dialog.ResultData != null)
            {
                App.Settings.Current.AttentionPool = dialog.ResultData;
                App.Settings.Save();
                App.Logger?.Information("Attention pool updated: {Count} items", dialog.ResultData.Count);
            }
        }

        internal void BtnAttentionStyle_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AttentionTargetEditorDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        internal void BtnSubliminalSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ColorEditorDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        internal void BtnManageMessages_Click(object sender, RoutedEventArgs e)
        {
            var oldKeys = new HashSet<string>(App.Settings.Current.SubliminalPool.Keys);
            var defaults = App.Mods?.GetDefaultSubliminalPool() ?? Models.BuiltInMods.BambiSleep.SubliminalPool ?? new Dictionary<string, bool>();

            var dialog = new TextEditorDialog("Subliminal Messages", App.Settings.Current.SubliminalPool);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && dialog.ResultData != null)
            {
                // Track default triggers the user explicitly removed
                var newKeys = new HashSet<string>(dialog.ResultData.Keys);
                foreach (var key in oldKeys)
                {
                    if (!newKeys.Contains(key) && defaults.ContainsKey(key))
                        App.Settings.Current.RemovedDefaultSubliminals.Add(key);
                }

                // If user re-adds a previously removed default, un-track it
                foreach (var key in newKeys)
                {
                    App.Settings.Current.RemovedDefaultSubliminals.Remove(key);
                }

                // Remember phrases the user added by hand so the cross-mod prune never deletes
                // them (a custom phrase can legitimately collide with another mod's default).
                foreach (var key in newKeys)
                {
                    if (!oldKeys.Contains(key))
                        App.Settings.Current.UserAddedSubliminals.Add(key);
                }
                // Forget any user-added phrase they just removed.
                foreach (var key in oldKeys)
                {
                    if (!newKeys.Contains(key))
                        App.Settings.Current.UserAddedSubliminals.Remove(key);
                }

                App.Settings.Current.SubliminalPool = dialog.ResultData;
                App.Settings.Save();
                App.Logger?.Information("Subliminal pool updated: {Count} items", dialog.ResultData.Count);
            }
        }

        internal void BtnManageLockCardPhrases_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TextEditorDialog("Lock Card Phrases", App.Settings.Current.LockCardPhrases);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && dialog.ResultData != null)
            {
                App.Settings.Current.LockCardPhrases = dialog.ResultData;
                App.Settings.Save();
                App.Logger?.Information("Lock card phrases updated: {Count} items", dialog.ResultData.Count);
            }
        }

        internal void BtnTestLockCard_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("test_lockcard"); } catch { }
            var phrases = App.Settings.Current.LockCardPhrases;
            var enabledPhrases = phrases.Where(p => p.Value).Select(p => p.Key).ToList();
            
            if (enabledPhrases.Count == 0)
            {
                MessageBox.Show(Loc.Get("msg_no_phrases_enabled_add_some_phrases_first"), "No Phrases", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Show the actual lock card
            App.LockCard.TestLockCard();
        }

        internal void BtnLockCardSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new LockCardColorDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        internal void ChkLockCardStrict_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ProgressionTab.ChkLockCardStrict.IsChecked ?? false;

            // Show warning when enabling strict mode
            if (isEnabled)
            {
                var confirmed = WarningDialog.ShowDoubleWarning(this,
                    "Strict Lock Card",
                    "• You will NOT be able to escape lock cards with ESC\n" +
                    "• You MUST type the phrase the required number of times\n" +
                    "• This can be very restrictive!");

                if (!confirmed)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _isLoading = true;
                        ProgressionTab.ChkLockCardStrict.IsChecked = false;
                        _isLoading = false;
                    }));
                    return;
                }
            }

            App.Settings.Current.LockCardStrict = isEnabled;
            App.Settings?.Save();
        }

        internal void BtnSelectSpiral_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "GIF Files (*.gif)|*.gif|All Image Files|*.gif;*.png;*.jpg;*.jpeg",
                Title = Loc.Get("title_select_spiral_gif")
            };
            
            // Start in last used directory if available
            var currentPath = App.Settings.Current.SpiralPath;
            if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
            }

            if (dialog.ShowDialog() == true)
            {
                App.Settings.Current.SpiralPath = dialog.FileName;
                App.Settings.Save();
                
                // Refresh overlays if running
                if (_isRunning)
                {
                    App.Overlay.RefreshOverlays();
                }
                
                MessageBox.Show($"Selected: {Path.GetFileName(dialog.FileName)}", "Spiral Selected");
            }
        }

        private void BtnPrevImage_Click(object sender, RoutedEventArgs e)
        {
            // Image carousel navigation
        }

        private void BtnNextImage_Click(object sender, RoutedEventArgs e)
        {
            // Image carousel navigation
        }


        internal void BtnPickAssetsFolder_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("pick_assets"); } catch { }
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a folder for your custom assets (images and videos).\nTwo subfolders 'images' and 'videos' will be created.",
                ShowNewFolderButton = true,
                UseDescriptionForTitle = true
            };

            // Start from current custom path if set, otherwise default
            var currentPath = App.Settings?.Current?.CustomAssetsPath;
            var oldEffectivePath = App.EffectiveAssetsPath;
            if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
            {
                dialog.SelectedPath = currentPath;
            }
            else
            {
                dialog.SelectedPath = App.UserAssetsPath;
            }

            // Own the dialog to the active popup if one is open — otherwise the dialog
            // renders behind the popup. If no popup, fall back to MainWindow.
            var ownerWindow = (_activeFeaturePopup != null && _activeFeaturePopup.IsVisible)
                ? (Window)_activeFeaturePopup
                : this;
            var owner = new Win32WindowWrapper(new System.Windows.Interop.WindowInteropHelper(ownerWindow).Handle);
            if (dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK)
            {
                var selectedPath = dialog.SelectedPath;
                var newPacksFolder = Path.Combine(selectedPath, ".packs");
                var shouldMovePacks = false;
                var packFoldersToMove = new List<(string SourceFolder, string PackName)>();
                long totalBytes = 0;

                // Check multiple locations for existing packs (retrocompatibility)
                // 1. Current effective path (where user currently has assets)
                // 2. Default path (in case packs were stranded there from before)
                var locationsToCheck = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    Path.Combine(oldEffectivePath, ".packs"),
                    Path.Combine(App.UserAssetsPath, ".packs")
                };

                // Don't check the new location (we're moving TO there)
                locationsToCheck.Remove(newPacksFolder);

                App.Logger?.Information("Asset folder change: checking {Count} locations for packs: {Locations}",
                    locationsToCheck.Count, string.Join(", ", locationsToCheck));

                foreach (var packsFolder in locationsToCheck)
                {
                    if (!Directory.Exists(packsFolder)) continue;

                    foreach (var dir in Directory.GetDirectories(packsFolder))
                    {
                        var manifestPath = Path.Combine(dir, ".manifest.enc");
                        if (!File.Exists(manifestPath)) continue;

                        // Try to read pack name from manifest
                        string packName = Path.GetFileName(dir); // Default to GUID if we can't read name
                        try
                        {
                            var json = Services.PackEncryptionService.LoadEncryptedManifest(manifestPath);
                            var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);
                            if (manifest?.PackName != null)
                            {
                                packName = (string)manifest.PackName;
                            }
                        }
                        catch { }

                        packFoldersToMove.Add((dir, packName));

                        // Calculate folder size
                        try
                        {
                            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                            {
                                totalBytes += new FileInfo(file).Length;
                            }
                        }
                        catch { }
                    }
                }

                App.Logger?.Information("Found {Count} packs to potentially move, total size: {Size} bytes",
                    packFoldersToMove.Count, totalBytes);

                if (packFoldersToMove.Count > 0)
                {
                    var sizeText = FormatFileSize(totalBytes);
                    var packNames = string.Join("\n• ", packFoldersToMove.Select(p => p.PackName));

                    var moveResult = MessageBox.Show(
                        Loc.GetF("msg_move_packs_confirm", packFoldersToMove.Count, sizeText, packNames,
                            totalBytes > 500_000_000 ? Loc.Get("msg_may_take_a_moment") : ""),
                        Loc.Get("title_move_downloaded_packs"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    shouldMovePacks = moveResult == MessageBoxResult.Yes;
                }

                // Create subfolders
                Directory.CreateDirectory(Path.Combine(selectedPath, "images"));
                Directory.CreateDirectory(Path.Combine(selectedPath, "videos"));

                // Move packs if requested
                if (shouldMovePacks && packFoldersToMove.Count > 0)
                {
                    try
                    {
                        // Create new packs folder if needed
                        if (!Directory.Exists(newPacksFolder))
                        {
                            var di = Directory.CreateDirectory(newPacksFolder);
                            di.Attributes |= FileAttributes.Hidden;
                        }

                        var movedCount = 0;
                        var registeredCount = 0;
                        foreach (var (sourceFolder, packName) in packFoldersToMove)
                        {
                            var guid = Path.GetFileName(sourceFolder);
                            var destDir = Path.Combine(newPacksFolder, guid);
                            if (!Directory.Exists(destDir))
                            {
                                // Use copy+delete instead of Directory.Move to support
                                // moving packs across different drive volumes
                                CopyDirectoryRecursive(sourceFolder, destDir);
                                Directory.Delete(sourceFolder, recursive: true);
                                movedCount++;
                                App.Logger?.Information("Moved pack '{PackName}' from {Source} to {Dest}", packName, sourceFolder, destDir);
                            }
                            else
                            {
                                App.Logger?.Warning("Pack folder already exists at destination, skipping: {Dest}", destDir);
                            }

                            // Register pack in settings (fix for packs not being detected after move)
                            var manifestPath = Path.Combine(destDir, ".manifest.enc");
                            if (File.Exists(manifestPath))
                            {
                                try
                                {
                                    var json = Services.PackEncryptionService.LoadEncryptedManifest(manifestPath);
                                    var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);
                                    var packId = (string?)manifest?.PackId;

                                    if (!string.IsNullOrEmpty(packId))
                                    {
                                        // Ensure settings collections exist
                                        App.Settings.Current.InstalledPackIds ??= new List<string>();
                                        App.Settings.Current.PackGuidMap ??= new Dictionary<string, string>();
                                        App.Settings.Current.ActivePackIds ??= new List<string>();

                                        // Add to InstalledPackIds if not present
                                        if (!App.Settings.Current.InstalledPackIds.Contains(packId))
                                        {
                                            App.Settings.Current.InstalledPackIds.Add(packId);
                                        }

                                        // Update PackGuidMap (overwrite if different GUID was stored)
                                        App.Settings.Current.PackGuidMap[packId] = guid;

                                        // Auto-activate pack so it shows immediately
                                        if (!App.Settings.Current.ActivePackIds.Contains(packId))
                                        {
                                            App.Settings.Current.ActivePackIds.Add(packId);
                                        }

                                        registeredCount++;
                                        App.Logger?.Information("Registered pack in settings: {PackId} -> {Guid}", packId, guid);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    App.Logger?.Warning(ex, "Failed to register pack from manifest: {Path}", manifestPath);
                                }
                            }
                        }

                        App.Logger?.Information("Moved {MovedCount}/{Total} packs, registered {RegCount} in settings",
                            movedCount, packFoldersToMove.Count, registeredCount);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "Failed to move packs to new location");
                        MessageBox.Show(
                            Loc.GetF("msg_could_not_move_packs_0", ex.Message),
                            Loc.Get("label_warning"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }

                // Save to settings
                App.Settings.Current.CustomAssetsPath = selectedPath;
                App.Settings.Save();

                // Refresh all services to use new path
                App.Flash?.RefreshImagesPath();
                App.Video?.RefreshVideosPath();
                App.BubbleCount?.RefreshVideosPath();
                App.ContentPacks?.RefreshPacksPath();

                // Refresh the asset tree to show new location
                RefreshAssetTree();

                MessageBox.Show(
                    Loc.GetF("msg_custom_assets_folder_set_0", selectedPath) +
                    (shouldMovePacks ? "\n\n" + Loc.Get("msg_packs_have_been_moved") : ""),
                    Loc.Get("title_assets_folder_set"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                App.Logger?.Information("Custom assets path set to: {Path}", selectedPath);
            }
        }

        /// <summary>
        /// Recursively copies a directory. Works across drive volumes unlike Directory.Move.
        /// </summary>
        private static void CopyDirectoryRecursive(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectoryRecursive(dir, Path.Combine(destinationDir, Path.GetFileName(dir)));
            }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0):F1} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} bytes";
        }

        internal void BtnRefreshAssets_Click(object sender, RoutedEventArgs e)
        {
            // Rescan every asset consumer so newly added/removed files are picked up without a restart
            // (#336 — BUG-BWJ7EGRTUP: the Assets help tip referenced a Refresh button that didn't exist).
            App.Flash?.RefreshImagesPath();
            App.Video?.RefreshVideosPath();
            App.BubbleCount?.RefreshVideosPath();
            RefreshAssetTree();
            MessageBox.Show(Loc.Get("msg_assets_refreshed"), Loc.Get("title_success"));
        }

        private void BtnViewLog_Click(object sender, RoutedEventArgs e)
        {
            var logPath = Path.Combine(App.UserDataPath, "logs");
            if (Directory.Exists(logPath))
            {
                Process.Start("explorer.exe", logPath);
            }
            else
            {
                MessageBox.Show(Loc.Get("msg_no_logs_found"), "Info");
            }
        }

        internal void BtnPanicKey_Click(object sender, RoutedEventArgs e)
        {
            // Don't show a blocking MessageBox: the global keyboard hook fires through
            // it, so the next keypress would set the panic key AND immediately trigger
            // a panic. Instead, just enter capture mode — both this window's button and
            // the SystemFeatureControl popup button show "Press any key..." until the
            // hook captures the next key.
            _isCapturingPanicKey = true;
            UpdatePanicKeyButton();
        }

        internal void ChkStrictLock_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = SettingsTab.ChkStrictLock.IsChecked ?? false;

            // Show warning when enabling strict mode
            if (isEnabled)
            {
                var confirmed = WarningDialog.ShowDoubleWarning(this,
                    "Strict Lock",
                    "• You will NOT be able to skip or close videos\n" +
                    "• Videos MUST be watched to completion\n" +
                    "• The only way out is the panic key (if enabled)\n" +
                    "• This can be very intense and restrictive");

                if (!confirmed)
                {
                    // Defer revert so it runs after the dialog's event stack fully unwinds,
                    // preventing WPF toggle animation from getting stuck in the ON position.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _isLoading = true;
                        SettingsTab.ChkStrictLock.IsChecked = false;
                        _isLoading = false;
                    }));
                    return;
                }
            }

            App.Settings.Current.StrictLockEnabled = isEnabled;
            App.Settings?.Save();
        }

        internal void ChkNoPanic_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isNoPanic = SettingsTab.ChkNoPanic.IsChecked ?? false;

            // Show warning when enabling no-panic mode
            if (isNoPanic)
            {
                var confirmed = WarningDialog.ShowDoubleWarning(this,
                    "Disable Panic Key",
                    "• You will have NO emergency escape option\n" +
                    "• The ONLY way to exit will be the Exit button\n" +
                    "• Combined with Strict Lock, this is VERY restrictive\n" +
                    "• Make sure you know what you're doing!");

                if (!confirmed)
                {
                    // Defer revert so it runs after the dialog's event stack fully unwinds,
                    // preventing WPF toggle animation from getting stuck in the ON position.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _isLoading = true;
                        SettingsTab.ChkNoPanic.IsChecked = false;
                        _isLoading = false;
                    }));
                    return;
                }

                // Stop keyboard hook when panic key is disabled (privacy improvement)
                // But keep it running if keyword triggers need it
                if (App.Settings.Current.KeywordTriggersEnabled != true)
                    _keyboardHook?.Stop();
                App.Settings.Current.PanicKeyEnabled = false;
                App.Settings?.Save();
                App.Logger?.Information("Keyboard hook stopped - panic key disabled");
            }
            else
            {
                // Start keyboard hook when panic key is re-enabled
                _keyboardHook?.Start();
                App.Settings.Current.PanicKeyEnabled = true;
                App.Settings?.Save();
                App.Logger?.Information("Keyboard hook started - panic key enabled");
            }
        }

        internal void ChkPerformanceMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.PerformanceMode = SettingsTab.ChkPerformanceMode.IsChecked ?? false;
            App.Logger?.Information("Performance mode set to {Enabled}", App.Settings.Current.PerformanceMode);
        }

        internal void ChkAutoPerformance_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.AutoPerformanceMode = SettingsTab.ChkAutoPerformance.IsChecked ?? true;
            App.Logger?.Information("Auto performance mode set to {Enabled}", App.Settings.Current.AutoPerformanceMode);
        }

        internal void CmbMotionLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var index = SettingsTab.CmbMotionLevel?.SelectedIndex ?? 0;
            var level = index switch
            {
                1 => Models.MotionLevel.Reduced,
                2 => Models.MotionLevel.Off,
                _ => Models.MotionLevel.Full,
            };
            App.Settings.Current.MotionLevel = level;
            App.Logger?.Information("Motion level set to {Level}", level);
            // Loops read the gate when they start, so a switch to Reduced/Off needs the running ones
            // stopped now; a switch back to Full re-arms them on the next tab visit.
            if (!Services.MotionFx.AllowAmbientLoops)
            {
                StopSeasonTitleShimmer();
                StopSkillTreeAnimations();
                StopProgramBannerFx();
                SwitchTabFx(string.Empty);
            }
            StartMarqueeAnimation();
            // The OG border re-evaluates both ways too (PR-5 gave it a gate at last).
            ApplyOgBorderLoop();
            // Chrome loops (nav glow breath, START glow + sheen, XP gloss) re-evaluate both ways:
            // down to Reduced/Off stops them now, back up to Full re-arms them without a restart.
            // RefreshChromeFx also drives the dashboard loops (see ApplyChromeFxLoops).
            RefreshChromeFx();
        }

        internal void ChkVideoHwDecode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.VideoForceHardwareDecoding = SettingsTab.ChkVideoHwDecode.IsChecked ?? false;
            App.Logger?.Information("Force video hardware decoding set to {Enabled}", App.Settings.Current.VideoForceHardwareDecoding);
        }

        internal void ChkUnifiedOverlay_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.UnifiedOverlayHost = SettingsTab.ChkUnifiedOverlay.IsChecked ?? true;
            App.Logger?.Information("Unified overlay renderer set to {Enabled}", App.Settings.Current.UnifiedOverlayHost);
        }

        internal void ChkOfflineMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = SettingsTab.ChkOfflineMode.IsChecked ?? false;

            if (isEnabled)
            {
                // Enabling offline mode - prompt for username if not set
                if (string.IsNullOrWhiteSpace(App.Settings.Current.OfflineUsername))
                {
                    var dialog = new OfflineUsernameDialog();
                    dialog.Owner = this;

                    if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Username))
                    {
                        App.Settings.Current.OfflineUsername = dialog.Username;
                    }
                    else
                    {
                        // User cancelled - revert checkbox
                        SettingsTab.ChkOfflineMode.IsChecked = false;
                        return;
                    }
                }

                // Set offline mode
                App.Settings.Current.OfflineMode = true;

                // Disconnect all network services
                DisconnectNetworkServices();

                App.Logger?.Information("Offline mode enabled with username '{Username}'",
                    App.Settings.Current.OfflineUsername);
            }
            else
            {
                // Disabling offline mode
                App.Settings.Current.OfflineMode = false;
                App.Logger?.Information("Offline mode disabled");
            }

            // Update UI to reflect offline mode state
            UpdateOfflineModeUI(isEnabled);

            App.Settings.Save();
        }

        /// <summary>
        /// Updates UI elements based on offline mode state.
        /// Disables/enables login buttons, browser, and updates banner.
        /// </summary>
        private void UpdateOfflineModeUI(bool isOffline)
        {
            try
            {
                // === LOGIN BUTTONS (disable all of them) ===

                // Patreon login button (in Patreon Exclusives tab)
                if (PatreonTab.BtnPatreonLogin != null)
                {
                    PatreonTab.BtnPatreonLogin.IsEnabled = !isOffline;
                    PatreonTab.BtnPatreonLogin.Opacity = isOffline ? 0.5 : 1.0;
                    if (isOffline)
                        PatreonTab.BtnPatreonLogin.ToolTip = Loc.Get("tooltip_disabled_in_offline_mode");
                    else
                        PatreonTab.BtnPatreonLogin.ToolTip = null;
                }

                // Discord login button (in Patreon Exclusives tab)
                if (PatreonTab.BtnDiscordLogin != null)
                {
                    PatreonTab.BtnDiscordLogin.IsEnabled = !isOffline;
                    PatreonTab.BtnDiscordLogin.Opacity = isOffline ? 0.5 : 1.0;
                    if (isOffline)
                        PatreonTab.BtnDiscordLogin.ToolTip = Loc.Get("tooltip_disabled_in_offline_mode");
                    else
                        PatreonTab.BtnDiscordLogin.ToolTip = null;
                }

                // Unified login button (in main area)
                if (SettingsTab.BtnUnifiedLogin != null)
                {
                    SettingsTab.BtnUnifiedLogin.IsEnabled = !isOffline;
                    SettingsTab.BtnUnifiedLogin.Opacity = isOffline ? 0.5 : 1.0;
                    if (isOffline)
                        SettingsTab.BtnUnifiedLogin.ToolTip = Loc.Get("tooltip_disabled_in_offline_mode");
                }

                // Discord tab login button (in Profile/Discord tab)
                if (DiscordTab.BtnDiscordTabLogin != null)
                {
                    DiscordTab.BtnDiscordTabLogin.IsEnabled = !isOffline;
                    DiscordTab.BtnDiscordTabLogin.Opacity = isOffline ? 0.5 : 1.0;
                    if (isOffline)
                        DiscordTab.BtnDiscordTabLogin.ToolTip = Loc.Get("tooltip_disabled_in_offline_mode");
                }

                // === BROWSER SECTION ===

                // Disable browser controls
                if (SettingsTab.RbBambiCloud != null)
                {
                    SettingsTab.RbBambiCloud.IsEnabled = !isOffline;
                    SettingsTab.RbBambiCloud.Opacity = isOffline ? 0.5 : 1.0;
                }
                if (SettingsTab.RbHypnoTube != null)
                {
                    SettingsTab.RbHypnoTube.IsEnabled = !isOffline;
                    SettingsTab.RbHypnoTube.Opacity = isOffline ? 0.5 : 1.0;
                }
                if (SettingsTab.BtnPopOutBrowser != null)
                {
                    SettingsTab.BtnPopOutBrowser.IsEnabled = !isOffline;
                    SettingsTab.BtnPopOutBrowser.Opacity = isOffline ? 0.5 : 1.0;
                }
                if (SettingsTab.TxtBrowserStatus != null)
                {
                    SettingsTab.TxtBrowserStatus.Text = isOffline ? "● Offline" : "● Ready";
                    SettingsTab.TxtBrowserStatus.Foreground = isOffline
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 128, 128))
                        : (System.Windows.Media.Brush)FindResource("PinkBrush");
                }

                // Navigate browser to blank page and show offline message
                if (isOffline)
                {
                    // Navigate to blank page to stop any loading content
                    if (_browser?.WebView?.CoreWebView2 != null)
                    {
                        try
                        {
                            _browser.WebView.CoreWebView2.Navigate("about:blank");
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Debug("Could not navigate browser to blank: {Error}", ex.Message);
                        }
                    }

                    // Show offline message over browser
                    if (SettingsTab.BrowserLoadingText != null)
                    {
                        SettingsTab.BrowserLoadingText.Visibility = Visibility.Visible;
                        SettingsTab.BrowserLoadingText.Text = Loc.Get("label_browser_disabled_in_offline_mode");
                    }
                    if (SettingsTab.BrowserContainer != null)
                    {
                        SettingsTab.BrowserContainer.Opacity = 0.3;
                    }
                }
                else
                {
                    // Hide offline message and restore browser
                    if (SettingsTab.BrowserLoadingText != null)
                    {
                        SettingsTab.BrowserLoadingText.Visibility = Visibility.Collapsed;
                    }
                    if (SettingsTab.BrowserContainer != null)
                    {
                        SettingsTab.BrowserContainer.Opacity = 1.0;
                    }

                    // Reload the browser with the currently selected site
                    if (_browser?.WebView?.CoreWebView2 != null)
                    {
                        try
                        {
                            var isBambiCloud = SettingsTab.RbBambiCloud?.IsChecked == true;
                            var url = isBambiCloud
                                ? "https://bambicloud.com/"
                                : "https://hypnotube.com/";
                            _browser.Navigate(url);
                            App.Logger?.Information("Browser reloaded after exiting offline mode: {Url}", url);
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Debug("Could not reload browser: {Error}", ex.Message);
                        }
                    }
                }

                // Update welcome banner
                UpdateBannerWelcomeMessage();

                App.Logger?.Debug("Offline mode UI updated: {State}", isOffline ? "disabled" : "enabled");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error updating offline mode UI");
            }
        }

        /// <summary>
        /// Disconnects all network services when entering offline mode.
        /// This ensures no external connections are maintained.
        /// </summary>
        private void DisconnectNetworkServices()
        {
            try
            {
                // Stop profile sync heartbeat (server pings)
                App.ProfileSync?.StopHeartbeat();

                // Disconnect Discord Rich Presence (IPC connection)
                if (App.DiscordRpc?.IsEnabled == true)
                {
                    App.DiscordRpc.IsEnabled = false;
                }

                App.Logger?.Debug("Network services disconnected for offline mode");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error disconnecting network services");
            }
        }

        internal void ChkDualMon_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = SettingsTab.ChkDualMon.IsChecked ?? true;
            App.Settings.Current.DualMonitorEnabled = isEnabled;

            // Refresh all services if engine is running
            if (_isRunning)
            {
                // Refresh overlays (pink filter, spiral, brain drain) - restart to add/remove monitor windows
                App.Overlay.RefreshForDualMonitorChange();

                // Bouncing text needs restart
                App.BouncingText.Stop();
                if (App.Settings.Current.BouncingTextEnabled)
                {
                    App.BouncingText.Start();
                }

                App.Logger?.Information("Dual monitor toggled: {Enabled} - services refreshed", isEnabled);
            }

            App.Settings.Save();
        }

        internal void ChkWinStart_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = SettingsTab.ChkWinStart.IsChecked ?? false;
            var isHidden = SettingsTab.ChkStartHidden.IsChecked ?? false;

            if (isEnabled && isHidden)
            {
                // Show warning when both startup and hidden are enabled
                var result = MessageBox.Show(this,
                    Loc.Get("msg_startup_hidden_warning"),
                    Loc.Get("title_startup_warning"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    SettingsTab.ChkWinStart.IsChecked = false;
                    return;
                }
            }

            // Apply the startup setting
            if (!StartupManager.SetStartupState(isEnabled))
            {
                MessageBox.Show(this,
                    Loc.Get("msg_failed_to_update_startup"),
                    Loc.Get("title_startup_error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                SettingsTab.ChkWinStart.IsChecked = StartupManager.IsRegistered();
                App.Settings.Current.RunOnStartup = SettingsTab.ChkWinStart.IsChecked ?? false;
                App.Settings.Save();
                return;
            }

            // Persist to settings so any subsequent LoadSettings() (e.g. from saving a
            // preset) doesn't reset the checkbox to a stale value (#150).
            App.Settings.Current.RunOnStartup = isEnabled;
            App.Settings.Save();
        }

        internal void ChkStartHidden_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isStartup = SettingsTab.ChkWinStart.IsChecked ?? false;
            var isHidden = SettingsTab.ChkStartHidden.IsChecked ?? false;

            if (isStartup && isHidden)
            {
                // Show warning when enabling hidden while startup is already enabled
                var result = MessageBox.Show(this,
                    Loc.Get("msg_startup_hidden_warning"),
                    Loc.Get("title_startup_warning"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    SettingsTab.ChkStartHidden.IsChecked = false;
                }
            }
        }

        #endregion
    }
}
