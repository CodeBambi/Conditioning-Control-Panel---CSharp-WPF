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
    // Presets tab: session preset management plus help-button handlers.
    public partial class MainWindow
    {
        #region Help Buttons

        private void SetupHelpButtons()
        {
            // Set up rich tooltips for all help buttons

            // Settings tab
            SetHelpContent(SettingsTab.HelpBtnFlash, "FlashImages");
            SetHelpContent(SettingsTab.HelpBtnVisuals, "Visuals");
            SetHelpContent(SettingsTab.HelpBtnVideo, "Video");
            SetHelpContent(SettingsTab.HelpBtnMiniGame, "MiniGame");
            SetHelpContent(SettingsTab.HelpBtnSubliminals, "Subliminals");
            SetHelpContent(SettingsTab.HelpBtnSystem, "System");
            SetHelpContent(SettingsTab.HelpBtnBrowser, "Browser");
            // Phase 2: the Audio section (and its help button) live on the Settings door now.
            SetHelpContent(AppSettingsTab.HelpBtnAudio, "Audio");
            SetHelpContent(SettingsTab.HelpBtnQuickLinks, "QuickLinks");

            // Presets tab
            SetHelpContent(PresetsTab.HelpBtnPresets, "Presets");
            SetHelpContent(PresetsTab.HelpBtnSessions, "Sessions");
            SetHelpContent(PresetsTab.HelpBtnSessionDetails, "SessionDetails");

            // Progression tab
            SetHelpContent(ProgressionTab.HelpBtnUnlockables, "Unlockables");
            SetHelpContent(ProgressionTab.HelpBtnScheduler, "Scheduler");
            SetHelpContent(ProgressionTab.HelpBtnRamp, "IntensityRamp");
            SetHelpContent(ProgressionTab.HelpBtnCommunity, "Community");
            SetHelpContent(ProgressionTab.HelpBtnAppInfo, "AppInfo");

            // Quests tab
            SetHelpContent(QuestsTab.HelpBtnQuests, "Quests");
            SetHelpContent(QuestsTab.HelpBtnQuestStats, "QuestStats");
            SetHelpContent(QuestsTab.HelpBtnRoadmap, "Roadmap");
            SetHelpContent(QuestsTab.HelpBtnRoadmapStats, "RoadmapStats");

            // Assets tab
            SetHelpContent(AssetsTab.HelpBtnAssets, "Assets");
            SetHelpContent(AssetsTab.HelpBtnPacks, "ContentPacks");
            SetHelpContent(AssetsTab.HelpBtnAssetBrowser, "AssetBrowser");

            // Lab tab.
            // Note: BlinkTrainer was promoted from this cluster to Exclusives
            // in v5.9.8 — see Exclusives subsection below. The Lab stub itself
            // doesn't get a ? button (it's a navigation signpost, not a feature
            // surface).
            SetHelpContent(GradedIntakeTab.HelpBtnQuiz, "Quiz");
            // HelpBtnWebcamGames removed: the bundled Webcam Games card was split into
            // separate Gaze Minigame + Focus Gaze cards (each with its own ? button).
            SetHelpContent(LabTab.HelpBtnGazeMinigame, "GazeMinigame");
            SetHelpContent(LabTab.HelpBtnFocusGaze, "FocusGaze");
            // PHASE 5 (G3): these two ? buttons moved with their editors onto the Awareness
            // tab's custom-trigger drawer. The PatreonTab twins still exist (Phase 8 owns the
            // demolition) but are permanently Collapsed, so attaching the popover there
            // reached nobody.
            SetHelpContent(AwarenessTab.HelpBtnKeywordTriggers, "KeywordTriggers");
            SetHelpContent(AwarenessTab.HelpBtnScreenOcr, "ScreenOcr");
            SetHelpContent(RemoteControlTab.HelpBtnRemoteControl, "RemoteControl");
            // "Get back to me" is a permission, so its ? button went to the Companion door with the
            // rest of the permissions card (UX restructure, Phase 5). Same registry entry, same
            // popup — only the accessor path changed.
            SetHelpContent(CompanionTab.HelpBtnGetBackToMe, "GetBackToMe");

            // Side panels + Exclusives features (Awareness / Haptics / BlinkTrainer
            // share this cluster — they're dedicated full-tab Exclusives surfaces
            // that happen to live alongside dashboard side-panel help entries).
            SetHelpContent(AchievementsTab.HelpBtnAchievements, "Achievements");
            SetHelpContent(CompanionTab.HelpBtnCompanions, "Companions");
            SetHelpContent(CompanionTab.HelpBtnPrompts, "CommunityPrompts");
            SetHelpContent(CompanionTab.HelpBtnVideoLinks, "HypnotubeLinks");
            SetHelpContent(CompanionTab.HelpBtnCompanionSettings, "CompanionSettings");
            SetHelpContent(CompanionTab.HelpBtnQuickControls, "QuickControls");
            SetHelpContent(PatreonTab.HelpBtnPatreon, "PatreonExclusives");
            SetHelpContent(CompanionTab.HelpBtnAiChat, "AiChat");
            SetHelpContent(CompanionTab.HelpBtnAwareness, "WindowAwareness");
            SetHelpContent(HapticsTab.HelpBtnHaptics, "Haptics");
            SetHelpContent(BlinkTrainerTab.HelpBtnBlinkTrainer, "BlinkTrainer");
            SetHelpContent(HapticsTab.HelpBtnVideoHapticSync, "VideoHapticSync");
            SetHelpContent(DiscordTab.HelpBtnDiscordProfile, "DiscordProfile");
            SetHelpContent(LeaderboardTab.HelpBtnLeaderboard, "Leaderboard");
        }

        private void SetHelpContent(Button helpButton, string sectionId)
        {
            var content = Services.HelpContentService.GetContent(sectionId);
            // Retire the non-interactive WPF ToolTip in favour of the interactive
            // HelpPopover (cursor can move into it; click pins it). Clearing ToolTip
            // guarantees no ToolTip + Popup double-render on these buttons.
            helpButton.ToolTip = null;
            Controls.HelpPopover.Attach(helpButton, content);
        }

        /// <summary>
        /// Click handler for help "?" buttons that should open the tutorial video
        /// popup when their topic ships a clip. The section id comes from the
        /// button's Tag. When there's no clip the rich hover tooltip (set via
        /// <see cref="SetHelpContent"/>) remains the only behaviour.
        /// </summary>
        private void HelpVideoButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string sectionId ||
                string.IsNullOrWhiteSpace(sectionId))
                return;
            var content = Services.HelpContentService.GetContent(sectionId);
            if (content.HasClip)
            {
                HelpVideoWindow.Show(content, this);
            }
        }

        #endregion

        #region Presets

        private Models.Preset? _selectedPreset;
        private List<Models.Preset> _allPresets = new();

        private void InitializePresets()
        {
            // Load default presets + user presets
            _allPresets = Models.Preset.GetDefaultPresets();
            _allPresets.AddRange(App.Settings.Current.UserPresets);

            // Restore last-used preset selection so the Presets card panel
            // highlights it on first nav and SaveSettings can find it. The
            // dropdown header reflects the same selection via Tag (preset.Id),
            // which survives mod text-replacements that the display name does not.
            var savedName = App.Settings.Current.CurrentPresetName;
            if (!string.IsNullOrEmpty(savedName))
            {
                _selectedPreset = _allPresets.FirstOrDefault(p => p.Name == savedName);
            }

            // Populate the header dropdown
            RefreshPresetsDropdown();
        }

        private void RefreshPresetsDropdown()
        {
            _isLoading = true;
            CmbPresets.Items.Clear();

            // Add all presets - use light text for dark dropdown background
            foreach (var preset in _allPresets)
            {
                CmbPresets.Items.Add(new ComboBoxItem
                {
                    Content = App.Mods?.MakeModAware(preset.Name) ?? preset.Name,
                    Tag = preset.Id,
                    Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224)) // Light gray #E0E0E0
                });
            }

            // Add separator and "Save New" option
            CmbPresets.Items.Add(new Separator());
            CmbPresets.Items.Add(new ComboBoxItem
            {
                Content = "➕ Save as New Preset...",
                Tag = "new",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")) // Bright pink for visibility
            });

            // Select current preset. Match by preset Id (stored in Tag) rather
            // than display Content — when a mod has text replacements that
            // touch preset names (e.g. "Bimbo Basics" → "Sissy Basics"), the
            // raw saved name no longer matches the mod-transformed display
            // string and the dropdown would silently show nothing selected.
            var currentName = App.Settings.Current.CurrentPresetName;
            var matchedId = _allPresets.FirstOrDefault(p => p.Name == currentName)?.Id;
            if (matchedId != null)
            {
                for (int i = 0; i < CmbPresets.Items.Count; i++)
                {
                    if (CmbPresets.Items[i] is ComboBoxItem item && item.Tag as string == matchedId)
                    {
                        CmbPresets.SelectedIndex = i;
                        break;
                    }
                }
            }

            _isLoading = false;
        }

        private void CmbPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (CmbPresets.SelectedItem is not ComboBoxItem item) return;
            
            var tag = item.Tag?.ToString();
            
            if (tag == "new")
            {
                // Show save new preset dialog
                PromptSaveNewPreset();
                // Reset selection to current
                RefreshPresetsDropdown();
                return;
            }
            
            // Find and load the preset
            var preset = _allPresets.FirstOrDefault(p => p.Id == tag);
            if (preset != null)
            {
                var result = MessageBox.Show(
                    $"Load preset '{preset.Name}'?\n\nThis will replace your current settings.",
                    "Load Preset",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                    
                if (result == MessageBoxResult.Yes)
                {
                    LoadPreset(preset);
                }
                else
                {
                    RefreshPresetsDropdown();
                }
            }
        }

        private void RefreshPresetsList()
        {
            PresetsTab.PresetCardsPanel.Children.Clear();
            _allPresets = Models.Preset.GetDefaultPresets();
            _allPresets.AddRange(App.Settings.Current.UserPresets);
            
            foreach (var preset in _allPresets)
            {
                var card = CreatePresetCard(preset);
                PresetsTab.PresetCardsPanel.Children.Add(card);
            }
        }

        private Border CreatePresetCard(Models.Preset preset)
        {
            var isSelected = _selectedPreset?.Id == preset.Id;
            var pinkBrush = FindResource("PinkBrush") as SolidColorBrush;
            
            var card = new Border
            {
                Background = new SolidColorBrush(isSelected ? Color.FromRgb(60, 60, 100) : Color.FromRgb(42, 42, 74)),
                BorderBrush = isSelected ? pinkBrush : (Application.Current.Resources["PanelAccentBrush"] as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(64, 64, 96))),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8),
                // 1px of vertical margin is headroom for the hover lift: the row sits in a
                // ScrollViewer whose viewport is exactly the card height, and WPF would clip the
                // top and bottom of a 1.02 scale without so much as a warning.
                Margin = new Thickness(0, 1, 6, 1),
                Width = 100,
                Height = 70,
                Cursor = Cursors.Hand,
                Tag = preset.Id
            };
            PreparePresetCardFx(card);

            card.MouseLeftButtonDown += (s, e) => SelectPreset(preset);
            card.MouseEnter += (s, e) => {
                if (_selectedPreset?.Id != preset.Id)
                    card.BorderBrush = pinkBrush;
            };
            card.MouseLeave += (s, e) => {
                if (_selectedPreset?.Id != preset.Id)
                    card.BorderBrush = Application.Current.Resources["PanelAccentBrush"] as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(64, 64, 96));
            };
            
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            
            // Name
            var nameText = new TextBlock
            {
                Text = App.Mods?.MakeModAware(preset.Name) ?? preset.Name,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stack.Children.Add(nameText);
            
            // Badge
            var badge = new TextBlock
            {
                Text = preset.IsDefault ? "DEFAULT" : "CUSTOM",
                Foreground = preset.IsDefault ? pinkBrush : new SolidColorBrush(Color.FromRgb(100, 200, 100)),
                FontSize = 7,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 1, 0, 0)
            };
            stack.Children.Add(badge);
            
            // Quick stats (icons only for compact view)
            var statsPanel = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (preset.FlashEnabled) AddStatIcon(statsPanel, "⚡", 10);
            if (preset.MandatoryVideosEnabled) AddStatIcon(statsPanel, "🎬", 10);
            if (preset.SubliminalEnabled) AddStatIcon(statsPanel, "💭", 10);
            if (preset.SpiralEnabled) AddStatIcon(statsPanel, "🌀", 10);
            if (preset.LockCardEnabled) AddStatIcon(statsPanel, "🔒", 10);
            stack.Children.Add(statsPanel);
            
            card.Child = stack;
            return card;
        }
        
        private void AddStatIcon(WrapPanel panel, string icon, int size = 12)
        {
            panel.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = size,
                Margin = new Thickness(0, 0, 2, 0)
            });
        }

        private string GetPresetQuickStats(Models.Preset preset)
        {
            var features = new List<string>();
            if (preset.FlashEnabled) features.Add("Flash");
            if (preset.MandatoryVideosEnabled) features.Add("Video");
            if (preset.SubliminalEnabled) features.Add("Subliminal");
            if (preset.SpiralEnabled) features.Add("Spiral");
            if (preset.PinkFilterEnabled) features.Add("Pink");
            if (preset.LockCardEnabled) features.Add("LockCard");
            
            return features.Count > 0 ? string.Join(" • ", features) : "Minimal";
        }

        private void SelectPreset(Models.Preset preset)
        {
            _selectedPreset = preset;
            _selectedSession = null;
            
            // Update cards UI
            RefreshPresetsList();
            
            // Show preset panel, hide session panel
            PresetsTab.PresetDetailScroller.Visibility = Visibility.Visible;
            PresetsTab.PresetButtonsPanel.Visibility = Visibility.Visible;
            PresetsTab.SessionDetailScroller.Visibility = Visibility.Collapsed;
            PresetsTab.SessionButtonsPanel.Visibility = Visibility.Collapsed;
            
            // Update detail panel
            PresetsTab.TxtDetailTitle.Text = App.Mods?.MakeModAware(preset.Name) ?? preset.Name;
            PresetsTab.TxtDetailSubtitle.Text = App.Mods?.MakeModAware(preset.Description) ?? preset.Description;
            
            PresetsTab.TxtDetailFlash.Text = preset.FlashEnabled
                ? $"Enabled | {preset.FlashFrequency}/hr | ×{preset.SimultaneousImages} | Opacity: {preset.FlashOpacity}%"
                : "Disabled";
                
            PresetsTab.TxtDetailVideo.Text = preset.MandatoryVideosEnabled 
                ? $"Enabled | {preset.VideosPerHour}/hr | Strict: {(preset.StrictLockEnabled ? "Yes" : "No")}"
                : "Disabled";
                
            PresetsTab.TxtDetailSubliminal.Text = preset.SubliminalEnabled 
                ? $"Enabled | {preset.SubliminalFrequency}/min | Opacity: {preset.SubliminalOpacity}%"
                : "Disabled";
                
            PresetsTab.TxtDetailAudio.Text = $"Whispers: {(preset.SubAudioEnabled ? $"Yes ({preset.SubAudioVolume}%)" : "No")} | Master: {preset.MasterVolume}%";
            
            PresetsTab.TxtDetailOverlays.Text = $"Spiral: {(preset.SpiralEnabled ? "Yes" : "No")} | Pink: {(preset.PinkFilterEnabled ? "Yes" : "No")}";
            
            PresetsTab.TxtDetailAdvanced.Text = $"Bubbles: {(preset.BubblesEnabled ? "Yes" : "No")} | Lock Card: {(preset.LockCardEnabled ? "Yes" : "No")}";
            
            // Enable buttons
            PresetsTab.BtnLoadPreset.IsEnabled = true;
            PresetsTab.BtnSaveOverPreset.IsEnabled = !preset.IsDefault;
            PresetsTab.BtnDeletePreset.IsEnabled = !preset.IsDefault;
            // Export any preset; share only user-created ones (user decision).
            PresetsTab.BtnExportPreset.IsEnabled = true;
            PresetsTab.BtnSharePreset.IsEnabled = !preset.IsDefault;
            UpdatePresetShareStatusBadge(preset);

            // Move the tab's one ambient loop onto the card that is now selected. After
            // RefreshPresetsList, deliberately - it rebuilt every card in the row.
            RefreshCardSheen();
        }
        
        internal void SessionCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string sessionType)
            {
                // Find the session
                var session = GetSessionById(sessionType);

                if (session != null)
                {
                    SelectSession(session);

                    // Show corner GIF option if applicable
                    if (session.HasCornerGifOption)
                    {
                        PresetsTab.TxtCornerGifDesc.Text = session.CornerGifDescription;
                        PresetsTab.ChkCornerGifEnabled.IsChecked = false;
                        PresetsTab.CornerGifSettings.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        private Models.Session? _selectedSession;
        
        internal void ChkCornerGifEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (PresetsTab.ChkCornerGifEnabled.IsChecked == true)
            {
                PresetsTab.CornerGifSettings.Visibility = Visibility.Visible;
            }
            else
            {
                PresetsTab.CornerGifSettings.Visibility = Visibility.Collapsed;
            }
        }
        
        internal void BtnSelectCornerGif_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.Get("title_select_corner_gif"),
                Filter = "GIF files (*.gif)|*.gif|All files (*.*)|*.*",
                InitialDirectory = System.IO.Path.Combine(App.EffectiveAssetsPath, "images")
            };

            if (dialog.ShowDialog() == true)
            {
                _selectedCornerGifPath = dialog.FileName;
                PresetsTab.BtnSelectCornerGif.Content = $"📁 {System.IO.Path.GetFileName(dialog.FileName)}";

                // Live update during session (#474) — the engine recreates the window
                if (_sessionEngine != null && _sessionEngine.IsRunning)
                {
                    _sessionEngine.UpdateCornerGifPath(dialog.FileName);
                }
            }
        }

        // Debounce for the corner GIF size slider's live update: the engine recreates
        // the layered window per change (in-place resize is the render-deadlock trigger
        // that got the old live update disabled), so only apply once the slider rests.
        private System.Windows.Threading.DispatcherTimer? _cornerGifSizeDebounce;

        internal void SliderCornerGifSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PresetsTab.TxtCornerGifSize != null)
            {
                PresetsTab.TxtCornerGifSize.Text = $"{(int)e.NewValue}px";
            }

            // Live update during session (#474), debounced 400ms
            if (_sessionEngine != null && _sessionEngine.IsRunning)
            {
                if (_cornerGifSizeDebounce == null)
                {
                    _cornerGifSizeDebounce = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(400)
                    };
                    _cornerGifSizeDebounce.Tick += (s, args) =>
                    {
                        _cornerGifSizeDebounce?.Stop();
                        if (_sessionEngine != null && _sessionEngine.IsRunning)
                        {
                            _sessionEngine.UpdateCornerGifSize((int)PresetsTab.SliderCornerGifSize.Value);
                        }
                    };
                }
                _cornerGifSizeDebounce.Stop();
                _cornerGifSizeDebounce.Start();
            }
        }

        internal void RbCornerPos_Checked(object sender, RoutedEventArgs e)
        {
            // Live update during session (#474) — position-only move, no recreate
            if (_sessionEngine != null && _sessionEngine.IsRunning)
            {
                _sessionEngine.UpdateCornerGifPosition(GetSelectedCornerPosition());
            }
        }

        internal void SliderCornerGifOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PresetsTab.TxtCornerGifOpacity != null)
            {
                PresetsTab.TxtCornerGifOpacity.Text = $"{(int)e.NewValue}%";
            }

            // Live update during session
            if (_sessionEngine != null && _sessionEngine.IsRunning)
            {
                _sessionEngine.UpdateCornerGifOpacity((int)e.NewValue);
            }
        }

        private string _selectedCornerGifPath = "";
        
        private Models.CornerPosition GetSelectedCornerPosition()
        {
            if (PresetsTab.RbCornerTL.IsChecked == true) return Models.CornerPosition.TopLeft;
            if (PresetsTab.RbCornerTR.IsChecked == true) return Models.CornerPosition.TopRight;
            if (PresetsTab.RbCornerBR.IsChecked == true) return Models.CornerPosition.BottomRight;
            return Models.CornerPosition.BottomLeft;
        }
        
        internal void BtnRevealSpoilers_Click(object sender, RoutedEventArgs e)
        {
            if (PresetsTab.SessionSpoilerPanel.Visibility == Visibility.Visible)
            {
                // Hide spoilers
                PresetsTab.SessionSpoilerPanel.Visibility = Visibility.Collapsed;
                PresetsTab.BtnRevealSpoilers.Content = Loc.Get("btn_reveal_details");
                return;
            }
            
            // Sequential warnings
            var warning1 = ShowStyledDialog(
                "⚠ Spoiler Warning",
                "Are you sure you want to see the session details?\n\n" +
                "Part of the magic is not knowing what's coming...\n" +
                "The experience works best when you surrender to the unknown.\n\n" +
                "Do you really want to spoil the surprise?",
                "Yes, show me", "No, keep the mystery");
                
            if (!warning1) return;
            
            var warning2 = ShowStyledDialog(
                "💗 Second Warning",
                "Good girls trust the process...\n\n" +
                "You're about to see exactly what will happen.\n" +
                "Once you know, you can't un-know.\n\n" +
                "Last chance to keep the mystery alive.",
                "Continue anyway", "You're right, nevermind");
                
            if (!warning2) return;
            
            var warning3 = ShowStyledDialog(
                "🏁 Final Confirmation",
                "You're choosing to see the details.\n" +
                "That's okay - some girls like to know.\n\n" +
                "Show the spoilers?",
                "Show spoilers", "Keep it secret");
                
            if (warning3)
            {
                PresetsTab.SessionSpoilerPanel.Visibility = Visibility.Visible;
                PresetsTab.BtnRevealSpoilers.Content = Loc.Get("btn_hide_details");
            }
        }
        
        private bool ShowStyledDialog(string title, string message, string yesText, string noText)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 420,
                SizeToContent = SizeToContent.Height,
                MinHeight = 200,
                MaxHeight = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent
            };

            // Setting Owner to a closed window throws InvalidOperationException — this can
            // fire from async continuations after MainWindow closed (#500). Center on
            // screen instead when the owner is no longer usable.
            try { dialog.Owner = this; }
            catch (InvalidOperationException)
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            
            var border = new Border
            {
                Background = Application.Current.Resources["DarkerBgBrush"] as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(26, 26, 46)),
                BorderBrush = FindResource("PinkBrush") as SolidColorBrush,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20)
            };
            
            var mainStack = new StackPanel();
            
            // Title
            mainStack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = FindResource("PinkBrush") as SolidColorBrush,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 15)
            });
            
            // Message
            mainStack.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                LineHeight = 20,
                Margin = new Thickness(0, 0, 0, 20)
            });
            
            // Buttons
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            
            bool result = false;
            
            var yesBtn = new Button
            {
                Content = yesText,
                Background = FindResource("PinkBrush") as SolidColorBrush,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(20, 10, 20, 10),
                Margin = new Thickness(0, 0, string.IsNullOrEmpty(noText) ? 0 : 10, 0),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            yesBtn.Click += (s, ev) => { result = true; dialog.Close(); };
            buttonPanel.Children.Add(yesBtn);
            
            // Only add cancel button if noText is provided
            if (!string.IsNullOrEmpty(noText))
            {
                var noBtn = new Button
                {
                    Content = noText,
                    Background = new SolidColorBrush(Color.FromRgb(60, 60, 80)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(20, 10, 20, 10),
                    FontSize = 12,
                    Cursor = Cursors.Hand
                };
                noBtn.Click += (s, ev) => { result = false; dialog.Close(); };
                buttonPanel.Children.Add(noBtn);
            }
            
            mainStack.Children.Add(buttonPanel);
            
            border.Child = mainStack;
            dialog.Content = border;
            dialog.ShowDialog();

            return result;
        }

        // Three-choice dialog used when a single media file is dropped onto MainWindow.
        // Modeled on ShowStyledDialog but with three primary actions stacked vertically
        // (Play / Edit / Add to Library) and a small Cancel link below.
        private MediaDropChoice ShowMediaDropChoiceDialog(string filePath)
        {
            var dialog = new Window
            {
                Title = Loc.Get("dlg_media_drop_title"),
                Width = 460,
                SizeToContent = SizeToContent.Height,
                MinHeight = 240,
                MaxHeight = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent
            };

            var border = new Border
            {
                Background = Application.Current.Resources["DarkerBgBrush"] as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(26, 26, 46)),
                BorderBrush = FindResource("PinkBrush") as SolidColorBrush,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20)
            };

            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = Loc.Get("dlg_media_drop_title"),
                Foreground = FindResource("PinkBrush") as SolidColorBrush,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            });

            stack.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(filePath),
                Foreground = Brushes.White,
                FontSize = 12,
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            });

            var choice = MediaDropChoice.Cancel;

            Button MakeBigButton(string label)
            {
                return new Button
                {
                    Content = label,
                    Background = FindResource("PinkBrush") as SolidColorBrush,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(20, 12, 20, 12),
                    Margin = new Thickness(0, 0, 0, 8),
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Cursor = Cursors.Hand
                };
            }

            var btnPlay = MakeBigButton(Loc.Get("dlg_media_drop_play"));
            btnPlay.Click += (_, _) => { choice = MediaDropChoice.Play; dialog.Close(); };
            stack.Children.Add(btnPlay);

            var btnEdit = MakeBigButton(Loc.Get("dlg_media_drop_edit"));
            btnEdit.Click += (_, _) => { choice = MediaDropChoice.Edit; dialog.Close(); };
            stack.Children.Add(btnEdit);

            var btnLib = MakeBigButton(Loc.Get("dlg_media_drop_library"));
            btnLib.Click += (_, _) => { choice = MediaDropChoice.Library; dialog.Close(); };
            stack.Children.Add(btnLib);

            var btnCancel = new Button
            {
                Content = Loc.Get("dlg_media_drop_cancel"),
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 11,
                Opacity = 0.7,
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (_, _) => { choice = MediaDropChoice.Cancel; dialog.Close(); };
            stack.Children.Add(btnCancel);

            border.Child = stack;
            dialog.Content = border;
            dialog.ShowDialog();
            return choice;
        }

        // --- velvet-mosaic: highlight feature cards whose feature is enabled ---

        private void RefreshFeatureCardActiveStates()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            if (SettingsTab.CardFlash != null) SettingsTab.CardFlash.IsActive = s.FlashEnabled;
            if (SettingsTab.CardVideo != null) SettingsTab.CardVideo.IsActive = s.MandatoryVideosEnabled;
            if (SettingsTab.CardSubliminal != null) SettingsTab.CardSubliminal.IsActive = s.SubliminalEnabled;
            if (SettingsTab.CardSpiral != null) SettingsTab.CardSpiral.IsActive = s.SpiralEnabled;
            if (SettingsTab.CardPinkFilter != null) SettingsTab.CardPinkFilter.IsActive = s.PinkFilterEnabled;
            if (SettingsTab.CardBubblePop != null) SettingsTab.CardBubblePop.IsActive = s.BubblesEnabled;
            if (SettingsTab.CardLockCard != null) SettingsTab.CardLockCard.IsActive = s.LockCardEnabled;
            if (SettingsTab.CardBubbleCount != null) SettingsTab.CardBubbleCount.IsActive = s.BubbleCountEnabled;
            if (SettingsTab.CardBouncingText != null) SettingsTab.CardBouncingText.IsActive = s.BouncingTextEnabled;
            if (SettingsTab.CardMindWipe != null) SettingsTab.CardMindWipe.IsActive = s.MindWipeEnabled;
            if (SettingsTab.CardBrainDrain != null) SettingsTab.CardBrainDrain.IsActive = s.BrainDrainEnabled;
            // Visuals and System cards have no single "enabled" toggle; they stay neutral.
        }

        // Quick-toggle: right-clicking a card flips its feature on/off. Mirrors the
        // per-feature toggle side effects in the FeatureControl popups so the running
        // effect actually starts/stops, not just the persisted flag.
        private void OnFeatureCardToggleRequested(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not Features.FeatureCard card) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            // A running session owns the prescribed dose - see MainWindow.SessionFeatureLock.cs.
            // Derived from live engine state on every click, so there is nothing to un-stick.
            if (RefuseIfSessionFeatureLocked($"card:{card.Title}")) return;
            var running = App.IsEngineRunning;
            try
            {
                if (card == SettingsTab.CardFlash) { var on = s.FlashEnabled = !s.FlashEnabled; if (running) { if (on) App.Flash?.Start(); else App.Flash?.Stop(); } }
                else if (card == SettingsTab.CardVideo) { var on = s.MandatoryVideosEnabled = !s.MandatoryVideosEnabled; if (running) { if (on) App.Video?.Start(); else App.Video?.Stop(); } }
                else if (card == SettingsTab.CardSubliminal) { var on = s.SubliminalEnabled = !s.SubliminalEnabled; if (running) { if (on) App.Subliminal?.Start(); else App.Subliminal?.Stop(); } }
                else if (card == SettingsTab.CardSpiral) { s.SpiralEnabled = !s.SpiralEnabled; App.Overlay?.RefreshOverlays(); }
                else if (card == SettingsTab.CardPinkFilter) { s.PinkFilterEnabled = !s.PinkFilterEnabled; App.Overlay?.RefreshOverlays(); }
                else if (card == SettingsTab.CardBubblePop) { var on = s.BubblesEnabled = !s.BubblesEnabled; if (running) { if (on) App.Bubbles?.Start(); else App.Bubbles?.Stop(); } }
                else if (card == SettingsTab.CardLockCard) { var on = s.LockCardEnabled = !s.LockCardEnabled; if (running) { if (on) App.LockCard?.Start(); else App.LockCard?.Stop(); } }
                else if (card == SettingsTab.CardBubbleCount) { var on = s.BubbleCountEnabled = !s.BubbleCountEnabled; if (running) { if (on) App.BubbleCount?.Start(); else App.BubbleCount?.Stop(); } }
                else if (card == SettingsTab.CardBouncingText) { var on = s.BouncingTextEnabled = !s.BouncingTextEnabled; if (running) { if (on) App.BouncingText?.Start(); else App.BouncingText?.Stop(); } }
                else if (card == SettingsTab.CardMindWipe) { s.MindWipeEnabled = !s.MindWipeEnabled; }
                else if (card == SettingsTab.CardBrainDrain)
                {
                    // Mirrors BrainDrainFeatureControl.ChkEnable_Changed exactly, INCLUDING the
                    // legacy write-back: MainWindow.SaveSettings() still reads Brain Drain FROM
                    // the dead ProgressionTab checkbox (MainWindow.Settings.cs:504) and runs on
                    // session start, so a quick-toggle that skipped the mirror would be silently
                    // reverted the next time the user pressed Start. Dies with the ghost tab in
                    // Phase 8, together with that line and the panel's own mirror.
                    var on = s.BrainDrainEnabled = !s.BrainDrainEnabled;
                    if (running) { if (on) App.BrainDrain?.Start(); else App.BrainDrain?.Stop(); }
                    if (ProgressionTab?.ChkBrainDrainEnabled != null)
                        ProgressionTab.ChkBrainDrainEnabled.IsChecked = on;
                }
                else return; // Visuals / System cards have no single on/off toggle.
                App.Settings?.Save();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Feature card quick-toggle failed for {Card}", card.Title);
            }
            // Flipping the flag fires OnSettingsPropertyChangedForCards which updates the
            // highlight; refresh explicitly too in case INPC didn't surface the change.
            RefreshFeatureCardActiveStates();
        }

        private void OnSettingsPropertyChangedForCards(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.FlashEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.MandatoryVideosEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.SubliminalEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.SpiralEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.PinkFilterEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BubblesEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.LockCardEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BubbleCountEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BouncingTextEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.MindWipeEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BrainDrainEnabled))
            {
                Dispatcher.BeginInvoke(new Action(RefreshFeatureCardActiveStates));
            }
        }

        // --- velvet-mosaic: dashboard feature card click dispatcher ----------

        private Features.FeaturePopupWindow? _activeFeaturePopup;

        private void ShowFeaturePopup(System.Windows.Controls.UserControl content, string title,
                                      System.Windows.Media.ImageSource? icon = null, string? glyph = null)
        {
            // Close any existing popup before opening a new one
            _activeFeaturePopup?.Close();

            var popup = new Features.FeaturePopupWindow(content, title, icon, glyph)
            {
                Owner = this
            };
            popup.Closed += (_, __) =>
            {
                if (_activeFeaturePopup == popup)
                {
                    _activeFeaturePopup = null;
                    _activeFeaturePopupContent = null;
                }
                // The popup has ShowInTaskbar=False, so when it closes WPF may activate
                // whatever window happens to be behind us instead of returning focus
                // to MainWindow. Explicitly bring MainWindow forward.
                try
                {
                    if (WindowState == WindowState.Minimized)
                        WindowState = WindowState.Normal;
                    Activate();
                }
                catch { /* window may be shutting down */ }
            };
            _activeFeaturePopup = popup;
            // Tracked so RefreshSessionFeatureLock can re-derive the lock onto a popup that is
            // already open when a session starts or ends (MainWindow.SessionFeatureLock.cs).
            _activeFeaturePopupContent = content;
            popup.Show(); // Non-modal so bubbles and other interactions keep working

            // A running session owns the prescribed dose: grey the master enable toggle and the
            // dials it prescribes, and say why. Applied AFTER Show so the content has been
            // through Initialized and FindName resolves - and so the visual tree the sweep walks
            // actually exists. Both directions are handled, so nothing latches.
            ApplySessionLockToFeaturePopup(content);

            // Bark hook: identify the feature by control type (locale-independent), e.g.
            // FlashFeatureControl -> "Flash". Gated/chanced in the rules so it isn't spammy.
            try
            {
                var feat = content.GetType().Name;
                const string suffix = "FeatureControl";
                if (feat.EndsWith(suffix)) feat = feat.Substring(0, feat.Length - suffix.Length);
                App.Bark?.NotifyFeatureOpened(feat);
            }
            catch { }
        }

        /// <summary>
        /// Phase 3: a Home mosaic tile is a SHORTCUT now, not a popup host.
        ///
        /// <para>Phase 4 re-hosted every one of these <c>*FeatureControl</c>s as a Studio rack
        /// entry. Opening the popup as well would give one feature two live editors — the exact
        /// duplication this restructure exists to remove — so the tile selects the rack row and
        /// shows the Studio door instead. Nothing is lost: the panels are the same instances with
        /// the same handlers, and the session feature lock is re-derived by ShowTab's
        /// <c>studio</c> case exactly as <c>ShowFeaturePopup</c> did for the popup.</para>
        ///
        /// <para><b>Order matters.</b> Focus first, THEN <c>ShowTab</c>: ShowTab's <c>studio</c>
        /// case calls <c>StudioTab.OnTabShown()</c>, which re-announces whatever row is selected.
        /// Selecting first makes that announcement the row the user actually asked for instead of
        /// the one they left behind last time. The resulting second
        /// <c>NotifyFeatureOpened</c> for the SAME key is swallowed by BarkService's global
        /// min-gap; announcing the WRONG key would not have been.</para>
        ///
        /// <para>The bark keys are unchanged — the rack fires the same values the popup derived
        /// from the control's type name (<c>StudioTabView.xaml.cs</c>, BarkFeature column). Rack
        /// keys are API (<c>StudioTabView.xaml.cs:62</c>); never spell one wrong here, because
        /// <c>FocusRackEntry</c> treats an unknown key as a quiet no-op by contract.</para>
        ///
        /// <para><b>Haptics is the one rack key that must NOT come through here.</b> It is the
        /// only module that is also a ShowTab key, and everything it owns hangs off that key
        /// rather than off the rack row: <c>NotifyTabNavigated("haptics")</c> (three voiced rules
        /// per built-in mod, plus any third-party .ccpmod's) fires from the top of
        /// <c>ShowTab</c> with the INCOMING key, and <c>MaybeShowFeatureIntro("haptics")</c> is
        /// one of only five first-visit intro cards. Its rack row deliberately carries no
        /// <c>BarkFeature</c> (<c>StudioTabView.xaml.cs:221-226</c>) precisely because the tab key
        /// already speaks for it — so routing it as a plain module would land on the right panel
        /// while silently saying nothing and never showing the intro. <c>ShowTab("haptics")</c>
        /// selects the same row itself (<c>TabNavigation.cs:376-383</c>), so the landing is
        /// identical; only the announcement differs.</para>
        /// </summary>
        private void OpenStudioModule(string rackKey)
        {
            if (string.Equals(rackKey, "haptics", StringComparison.OrdinalIgnoreCase))
            {
                ShowTab("haptics");
                return;
            }
            try { StudioTab?.FocusRackEntry(rackKey); }
            catch (Exception ex) { App.Logger?.Debug("OpenStudioModule({Key}): {E}", rackKey, ex.Message); }
            ShowTab("studio");
        }

        internal void CardFlash_Click(object sender, RoutedEventArgs e) => OpenStudioModule("flash");

        internal void CardVisuals_Click(object sender, RoutedEventArgs e) => OpenStudioModule("visuals");

        internal void CardVideo_Click(object sender, RoutedEventArgs e) => OpenStudioModule("video");

        internal void CardSubliminal_Click(object sender, RoutedEventArgs e) => OpenStudioModule("subliminal");

        internal void CardSpiral_Click(object sender, RoutedEventArgs e) => OpenStudioModule("spiral");

        internal void CardPinkFilter_Click(object sender, RoutedEventArgs e) => OpenStudioModule("pinkfilter");

        internal void CardBubblePop_Click(object sender, RoutedEventArgs e) => OpenStudioModule("bubbles");

        internal void CardLockCard_Click(object sender, RoutedEventArgs e) => OpenStudioModule("lockcard");

        internal void CardBubbleCount_Click(object sender, RoutedEventArgs e) => OpenStudioModule("bubblecount");

        internal void CardBouncingText_Click(object sender, RoutedEventArgs e) => OpenStudioModule("bouncingtext");

        internal void CardMindWipe_Click(object sender, RoutedEventArgs e) => OpenStudioModule("mindwipe");

        /// <summary>
        /// Phase 3 rescue: Brain Drain's front door. Its panel was rebuilt in Phase 4 from the dead
        /// ProgressionTab markup; before that the feature had no reachable UI at all.
        /// </summary>
        internal void CardBrainDrain_Click(object sender, RoutedEventArgs e) => OpenStudioModule("braindrain");

        /// <summary>
        /// The System popup. Still a <see cref="Features.FeaturePopupWindow"/> because System has
        /// no Studio rack entry — it is display/video switches, not a feature. Phase 3 moved its
        /// ENTRY POINT off the mosaic (the tile is Collapsed) onto the quick-toggles row's "System"
        /// pill, which calls this same method: the popup, its mod-aware title and its
        /// <c>NotifyFeatureOpened("System")</c> bark are unchanged.
        ///
        /// <para>Popup titles go through <c>ModAwareLabel</c> so a mod that renames a feature
        /// retitles the window too — otherwise a "Flash Images" → "Drone Pulses" replacement would
        /// rename the tile and the in-popup section header but leave the title bar behind.</para>
        /// </summary>
        internal void CardSystem_Click(object sender, RoutedEventArgs e) =>
            ShowFeaturePopup(new Features.SystemFeatureControl(),
                ModAwareLabel("⚙ System", "section_system"),
                glyph: "⚙");

        /// <summary>
        /// Dashboard "Webcam &amp; Mic" tile. Until Phase 2 this opened a FeaturePopupWindow around
        /// Features.WebcamFeatureControl and, pre-show, physically reparented the Lab tab's
        /// LabWebcamEngineBar into it (DetachWebcamBarInto), putting it back on close. Both the
        /// popup host and the reparenting are retired: the webcam and microphone controls live in
        /// Settings -&gt; Devices, which is a real page, so this is now plain navigation. The tile
        /// stays because it is a shortcut people know - Phase 3 re-lays the dashboard around it.
        ///
        /// <para><b>The bark is restored here, not inside OpenDeviceSettings.</b> Retiring the
        /// popup took the <c>NotifyFeatureOpened("Webcam")</c> call with it, and that left the
        /// <c>feat_webcam</c> rule - two RECORDED clips in each of the three built-in mods -
        /// with no call site at all: a voiced line that had simply stopped existing, which is the
        /// one thing this restructure is not allowed to do. It goes on this handler because this
        /// pill is the surface it always fired from; the other four
        /// <c>OpenDeviceSettings()</c> callers (Takeover, Blink Trainer, Deeper, and the Settings
        /// mirrors) never fired it, and putting it in the shared method would make her comment on
        /// the camera from four places she never used to.</para>
        /// </summary>
        internal void VelvetBtnWebcam_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyFeatureOpened("Webcam"); }
            catch { /* a bark must never break a navigation */ }
            OpenDeviceSettings();
        }

        internal void VelvetBtnAppInfo_Click(object sender, RoutedEventArgs e)
        {
            // Phase 2: no reparenting. The account/data cards live permanently in
            // Settings · Account (AppSettingsTab), so this popup is About + support forms only
            // and can be built and shown in one step.
            var control = new Features.AppInfoFeatureControl();

            // Close any existing popup before opening a new one
            _activeFeaturePopup?.Close();

            var popup = new Features.FeaturePopupWindow(
                control,
                Localization.Loc.Get("label_app_info"),
                glyph: "ℹ")
            {
                Owner = this
            };

            // Nothing to give back on close since Phase 2 — the popup no longer borrows anything.
            popup.Closed += (_, __) =>
            {
                if (_activeFeaturePopup == popup)
                    _activeFeaturePopup = null;
                try
                {
                    if (WindowState == WindowState.Minimized)
                        WindowState = WindowState.Normal;
                    Activate();
                }
                catch { /* window may be shutting down */ }
            };

            _activeFeaturePopup = popup;
            popup.Show();
        }

        /// <summary>
        /// Quick-toggles row · "Scheduler + Intensity Ramp". Phase 4 gave both panels rack entries
        /// (<c>scheduler</c> / <c>ramp</c>), so Phase 3 re-points this pill at the rack instead of
        /// opening a second copy of them in a popup. Nothing goes quiet: both rack rows fire the
        /// popup's single <c>"SchedulerRamp"</c> FeatureOpened key
        /// (<c>StudioTabView.xaml.cs:229-230</c>), so the existing bark rules keep matching.
        /// Scheduler is the landing row because it is the first of the pair in the rack.
        /// </summary>
        internal void VelvetBtnSchedulerRamp_Click(object sender, RoutedEventArgs e) =>
            OpenStudioModule("scheduler");

        // Opens the web catalogue (browse/share community presets & sessions).
        // Surfaced by the dashboard "CCP Catalogue" pill.
        internal void BtnCatalogue_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "https://app.cclabs.app/catalogue") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open CCP Catalogue URL");
            }
        }

        internal void BtnSessionHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new SessionLogHistoryWindow { Owner = this };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open session history dialog");
            }
        }

        internal void BtnStartSession_Click(object sender, RoutedEventArgs e)
        {
            // The button doubles as Start/Stop — state dictates which path to run.
            // This also makes us resilient to any stale/duplicate Click subscriptions.
            if (_sessionEngine?.IsRunning == true)
            {
                BtnStopSession_Click(sender, e);
                return;
            }

            if (_selectedSession == null || !_selectedSession.IsAvailable) return;

            var confirmed = ShowStyledDialog(
                $"🌅 Start {_selectedSession.Name}?",
                $"Duration: {_selectedSession.DurationMinutes} minutes\n\n" +
                "Your current settings will be temporarily replaced.\n" +
                "They will be restored when the session ends." +
                "\n\nReady to begin?",
                "▶ Start Session", "Not yet");

            if (confirmed)
            {
                StartSession(_selectedSession);
            }
        }
        
        private async void StartSession(Models.Session session)
        {
            // Apply corner GIF settings if enabled
            if (session.HasCornerGifOption && PresetsTab.ChkCornerGifEnabled.IsChecked == true)
            {
                session.Settings.CornerGifEnabled = true;
                session.Settings.CornerGifPath = _selectedCornerGifPath;
                session.Settings.CornerGifPosition = GetSelectedCornerPosition();
                session.Settings.CornerGifSize = (int)PresetsTab.SliderCornerGifSize.Value;
                session.Settings.CornerGifOpacity = (int)PresetsTab.SliderCornerGifOpacity.Value;
            }
            
            // Initialize session engine if needed
            if (_sessionEngine == null)
            {
                _sessionEngine = new SessionEngine(this);
                _sessionEngine.SessionCompleted += OnSessionCompleted;
                _sessionEngine.ProgressUpdated += OnSessionProgressUpdated;
                _sessionEngine.PhaseChanged += OnSessionPhaseChanged;
                _sessionEngine.SessionStarted += OnSessionStarted;
                _sessionEngine.SessionStopped += OnSessionStopped;
            }

            // The engine is MainWindow-owned and created lazily, so the observers can't subscribe
            // at their own start. Both attaches detach any previous engine first — re-attaching is
            // safe, and doing it here means a program session started from the Sessions list is
            // still observed. (This site used to forget both.)
            App.Bark?.AttachSessionEngine(_sessionEngine);
            App.Programs?.AttachSessionEngine(_sessionEngine);

            try
            {
                // Start the engine if not already running
                if (!_isRunning)
                {
                    BtnStart_Click(this, new RoutedEventArgs());
                }
                
                // Start the session
                await _sessionEngine.StartSessionAsync(session);
                
                
                App.Logger?.Information("Started session: {Name} ({Difficulty}, +{XP} XP)", 
                    session.Name, session.Difficulty, session.BonusXP);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to start session");
                ShowStyledDialog(Loc.Get("title_error"), Loc.GetF("msg_failed_to_start_session", ex.Message), Loc.Get("btn_ok"), "");
            }
        }
        
        private void OnSessionCompleted(object? sender, SessionCompletedEventArgs e)
        {
            App.IsSessionRunning = false;
            // Release the controller-owned-run marker (MainWindow.RemoteControl.cs). Hygiene only —
            // IsSessionRemoteStarted already reads false once the engine stops.
            _remoteStartedSession = null;
            Dispatcher.Invoke(() =>
            {
                // Award XP. The completion dialog is shown from OnSessionLogReady,
                // which fires for both completion and abort.
                App.Progression?.AddXP(e.XPEarned, XPSource.Session);

                App.Logger?.Information("Session {Name} completed, awarded {XP} XP", e.Session.Name, e.XPEarned);

                // Intake punch card: a session that reached its natural end has unambiguously
                // been used, so it redeems its pending stamp regardless of the halfway threshold.
                // A no-op unless this session was drafted by a Graded Intake.
                try { App.IntakePunchCard?.NotifySessionCompleted(e.Session); }
                catch (Exception ex) { App.Logger?.Debug("Punch card completion hook: {E}", ex.Message); }

                // Sync progress to cloud after session (fire and forget)
                if (App.ProfileSync?.IsSyncEnabled == true)
                {
                    _ = App.ProfileSync.SyncProfileAsync();
                }
            });
        }

        /// <summary>
        /// Deadline until which one session-summary modal is swallowed. Set by Withdraw, which ends
        /// today's session on purpose and has already told the user so in its own confirm.
        ///
        /// A DEADLINE rather than a bool because the summary is driven by SessionLogReady, and if
        /// that event never arrived a sticky flag would silently eat the NEXT session's summary
        /// instead. Nothing is lost by suppressing it: the log is still written and still readable
        /// from the session history window.
        /// </summary>
        private DateTime _suppressSessionSummaryUntil = DateTime.MinValue;

        /// <summary>
        /// Swallow the next session summary if one arrives shortly. Called before a stop the user
        /// has already explicitly confirmed, so the app does not editorialise on top of it.
        /// </summary>
        internal void SuppressNextSessionSummary(string reason)
        {
            _suppressSessionSummaryUntil = DateTime.UtcNow.AddSeconds(20);
            App.Logger?.Information("Session summary suppressed for the next stop ({Reason})", reason);
        }

        private void OnSessionLogReady(object? sender, SessionLogReadyEventArgs e)
        {
            // Withdraw is specified to be unweighted and without commentary. A modal headlined
            // "Session Ended Early" immediately after the user confirmed "the run ends here, and
            // today's session stops with it" is commentary, and reads as a rebuke for using the
            // way out. Consumed one-shot so only that stop is quiet.
            if (DateTime.UtcNow < _suppressSessionSummaryUntil)
            {
                _suppressSessionSummaryUntil = DateTime.MinValue;
                App.Logger?.Information("Session summary skipped (deliberate stop)");
                return;
            }

            var log = e.Log;
            Dispatcher.BeginInvoke(() => ShowSessionSummaryWhenClear(log, attempt: 0));
        }

        /// <summary>
        /// Shows the session-complete dialog once any in-flight video teardown has finished.
        /// A bubble-triggered bonus video can still be tearing down when the session log
        /// arrives; its dying fullscreen surface renders as a stuck white plane that buries
        /// the modal summary (#462). No cleanup is forced here: the realistic race always has
        /// a CloseAll already in flight (a fresh ForceCleanup would just no-op on its
        /// _isCleaningUp guard, and running one synchronously risks a UI-thread LibVLC wedge),
        /// so this only waits it out. The wait is cleanup-aware because CloseAll pumps the
        /// dispatcher at Background priority — our timer ticks run INSIDE that pump, and
        /// showing the modal there would suspend the teardown under the dialog.
        /// </summary>
        private void ShowSessionSummaryWhenClear(Models.SessionLog log, int attempt)
        {
            try
            {
                if (Dispatcher.HasShutdownStarted) return;

                var cleaning = App.Video?.IsCleaningUp == true;
                var videoBusy = cleaning
                    || App.Video?.IsPlaying == true
                    || App.Video?.HasOpenWindows == true;
                // Soft cap ~3s; while a CloseAll is actively in flight keep waiting up to 10s
                // (its flag clears in a finally, so this terminates).
                if (videoBusy && (attempt < 15 || (cleaning && attempt < 50)))
                {
                    var retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                    retry.Tick += (_, _) =>
                    {
                        retry.Stop();
                        ShowSessionSummaryWhenClear(log, attempt + 1);
                    };
                    retry.Start();
                    return;
                }

                // ApplicationIdle sits BELOW the Background priority CloseAll pumps at, so the
                // modal can never open re-entrantly inside a teardown's message pump.
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    try
                    {
                        if (Dispatcher.HasShutdownStarted) return;
                        // If we capped out because a NEW video legitimately started (autonomy
                        // can trigger one right after session end), don't steal topmost/focus
                        // from it — show buried, like the pre-fix behavior.
                        var videoUp = App.Video?.IsPlaying == true;
                        var dialog = new SessionCompleteWindow(log)
                        {
                            Owner = IsLoaded ? this : null,
                            // Pulse above any straggler surface, then drop back to normal once
                            // rendered so the summary can't pin itself over other apps.
                            Topmost = !videoUp,
                        };
                        if (!videoUp)
                        {
                            dialog.ContentRendered += (_, _) =>
                            {
                                dialog.Topmost = false;
                                dialog.Activate();
                            };
                        }
                        dialog.ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "Failed to show post-session log dialog");
                    }
                }));
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to schedule post-session log dialog");
            }
        }
        
        private void OnSessionProgressUpdated(object? sender, SessionProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Program feature lock heartbeat. Re-derives once a second and repaints only on
                // a change, so the toggles cannot stay locked (or stay open) through any
                // out-of-order or dropped session event. force:false = skip redundant repaints.
                RefreshSessionFeatureLock(force: false);

                if (_sessionEngine?.CurrentSession != null)
                {
                    var remaining = e.Remaining;
                    var session = _sessionEngine.CurrentSession;

                    // Intake punch card: half a drafted session is enough to prove it was used
                    // rather than started and abandoned. Called every tick and safe to be -
                    // redeeming removes the pending draft, so a hole can only land once.
                    try { App.IntakePunchCard?.NotifySessionProgress(session, e.ProgressPercent); }
                    catch (Exception ex) { App.Logger?.Debug("Punch card progress hook: {E}", ex.Message); }

                    // Update session button with remaining time
                    PresetsTab.BtnStartSession.Content = Loc.GetF("btn_stop_session_0_1", $"{((int)remaining.TotalMinutes):D2}", $"{remaining.Seconds:D2}");

                    // Update Start button label with session name + timer
                    var mName = session.GetModeAwareName();
                    var name = mName.Length > 14
                        ? mName.Substring(0, 11) + "..."
                        : mName;
                    var pauseIndicator = _sessionEngine.IsPaused ? $" [{Loc.Get("label_paused")}]" : "";
                    TxtStartLabel.Text = Loc.GetF("label_0_1_2_3", name, $"{((int)remaining.TotalMinutes):D2}", $"{remaining.Seconds:D2}", pauseIndicator);

                    // Training Programs: drive the TODAY panel's session progress strip off this
                    // same 1-second engine tick rather than a second timer. It no-ops unless the
                    // running session is the one the Programs tab launched.
                    UpdateProgramSessionRow();
                }
            });
        }
        
        private void OnSessionPhaseChanged(object? sender, SessionPhaseChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                App.Logger?.Information("Session phase: {Phase} - {Description}", e.Phase.Name, e.Phase.Description);
            });
        }

        private void OnSessionStarted(object? sender, EventArgs e)
        {
            App.IsSessionRunning = true;
            // Safety net for the one-shot above: a new session always gets its own summary, even if
            // a suppression was armed and its stop somehow never produced a log.
            _suppressSessionSummaryUntil = DateTime.MinValue;
            Dispatcher.Invoke(() =>
            {
                PresetsTab.BtnStartSession.Content = Loc.Get("btn_stop_session_2");
                // Note: BtnStartSession_Click now dispatches to Stop when a session is running,
                // so we no longer swap Click delegates (which caused duplicate-handler bugs
                // when remote-started sessions skipped the Started event subscription).

                // Update Start button to show session info
                var session = _sessionEngine?.CurrentSession;
                if (session != null)
                {
                    // Abbreviate name if over 22 chars
                    var mName = session.GetModeAwareName();
                    var name = mName.Length > 22
                        ? mName.Substring(0, 19) + "..."
                        : mName;

                    TxtStartIcon.Text = "⏹";
                    TxtStartLabel.Text = name;

                    // Make button red during session
                    BtnStart.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(220, 53, 69)); // Bootstrap danger red

                    // Show pause button
                    BtnPauseSession.Visibility = Visibility.Visible;
                    if (TxtPauseIcon != null) TxtPauseIcon.Text = "⏸";
                }

                // Training Programs: flip the TODAY panel out of its idle "start" state, and
                // announce it. Both no-op unless this is the program's own session.
                UpdateProgramSessionRow();
                AnnounceProgramSessionStarted();
                // ... and lock the dials the session prescribes. Applies to EVERY session -
                // program, preset and remote - see MainWindow.SessionFeatureLock.cs rule 1.
                RefreshSessionFeatureLock();
            });
        }

        private void OnSessionStopped(object? sender, EventArgs e)
        {
            App.IsSessionRunning = false;
            // See OnSessionCompleted — drop the reference to the controller-started run.
            _remoteStartedSession = null;

            // Captured BEFORE the dispatcher work: SessionEngine nulls _currentSession at the end
            // of StopSession, and ProgramService clears its expected id when SessionCompleted
            // fires - both of which can happen before a queued Invoke body runs.
            var wasProgramSession = App.Programs?.IsProgramSession(_sessionEngine?.CurrentSession) == true;

            Dispatcher.Invoke(() =>
            {
                // Stop the engine when session stops
                StopEngine();

                PresetsTab.BtnStartSession.Content = Loc.Get("btn_start_session");
                // Click handler is unchanged — BtnStartSession_Click dispatches based on
                // _sessionEngine.IsRunning, so no subscription swap is needed.

                // Reset Start button to normal state
                TxtStartIcon.Text = "▶";
                TxtStartLabel.Text = Loc.Get("label_start");

                // Restore pink color
                BtnStart.ClearValue(System.Windows.Controls.Control.BackgroundProperty);

                // Hide pause button
                BtnPauseSession.Visibility = Visibility.Collapsed;

                // Training Programs: back to the idle slot, then announce (deferred - the
                // day's slot is only marked done later in this same StopSession call).
                UpdateProgramSessionRow();
                AnnounceProgramSessionEnded(wasProgramSession);
                // Hand the dials back. Deliberately NOT conditional on
                // wasProgramSession: the refresh RE-DERIVES the lock from live engine state,
                // so calling it unconditionally is what guarantees the toggles come back on
                // every exit path (completion, STOP, abort, withdraw) even if this handler
                // fires out of order or twice.
                RefreshSessionFeatureLock();
            });
        }

        private void BtnStopSession_Click(object sender, RoutedEventArgs e)
        {
            if (_sessionEngine == null || !_sessionEngine.IsRunning) return;

            if (App.Lockdown?.IsActive == true)
            {
                MessageBox.Show(Loc.Get("msg_you_are_in_lockdown_mode_nyou_cannot_end_a_se"), Loc.Get("title_lockdown"),
                    MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            var session = _sessionEngine.CurrentSession;
            var elapsed = _sessionEngine.ElapsedTime;
            var remaining = _sessionEngine.RemainingTime;

            // Apply level-based XP multiplier
            var level = App.Settings?.Current?.PlayerLevel ?? 1;
            var multiplier = App.Progression?.GetSessionXPMultiplier(level) ?? 1.0;
            var potentialXP = (int)Math.Round((session?.BonusXP ?? 0) * multiplier);

            var penaltyText = _sessionEngine.PauseCount > 0
                ? Loc.GetF("msg_plus_pause_penalty_0", _sessionEngine.XPPenalty)
                : "";

            var confirmed = ShowStyledDialog(
                Loc.Get("title_stop_session_confirm"),
                Loc.GetF("msg_stop_session_body", session?.Icon, session?.Name,
                    $"{((int)elapsed.TotalMinutes):D2}:{elapsed.Seconds:D2}",
                    $"{((int)remaining.TotalMinutes):D2}:{remaining.Seconds:D2}",
                    potentialXP, penaltyText),
                Loc.Get("btn_yes_stop_session"), Loc.Get("btn_keep_going"));

            if (confirmed)
            {
                _sessionEngine.StopSession(completed: false);
            }
        }

        private void BtnPauseSession_Click(object sender, RoutedEventArgs e)
        {
            if (_sessionEngine == null || !_sessionEngine.IsRunning) return;

            if (App.Lockdown?.IsActive == true)
            {
                MessageBox.Show(Loc.Get("msg_you_are_in_lockdown_mode_nyou_cannot_pause_du"), Loc.Get("title_lockdown"),
                    MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            if (_sessionEngine.IsPaused)
            {
                // Resume
                _sessionEngine.ResumeSession();
                if (TxtPauseIcon != null) TxtPauseIcon.Text = "⏸";
                BtnPauseSession.ToolTip = Loc.GetF("tooltip_pause_session_100_xp_penalty_per_pause_npause", _sessionEngine.PauseCount);
            }
            else
            {
                // Confirm pause (costs XP)
                var confirmed = ShowStyledDialog(
                    Loc.Get("title_pause_session_confirm"),
                    Loc.GetF("msg_pause_session_body", _sessionEngine.XPPenalty, _sessionEngine.XPPenalty + 100),
                    Loc.Get("btn_yes_pause"), Loc.Get("btn_keep_going"));

                if (confirmed)
                {
                    _sessionEngine.PauseSession();
                    if (TxtPauseIcon != null) TxtPauseIcon.Text = "▶";
                    BtnPauseSession.ToolTip = Loc.Get("tooltip_resume_session");
                }
            }
        }
        
        // Methods called by SessionEngine to control features
        public void ApplySessionSettings()
        {
            _isLoading = true;
            LoadSettings();
            _isLoading = false;
        }
        
        public void UpdateSpiralOpacity(int opacity)
        {
            App.Settings.Current.SpiralOpacity = opacity;
            Dispatcher.Invoke(() =>
            {
                if (ProgressionTab.SliderSpiralOpacity != null && !_isLoading)
                {
                    _isLoading = true;
                    ProgressionTab.SliderSpiralOpacity.Value = opacity;
                    if (ProgressionTab.TxtSpiralOpacity != null) ProgressionTab.TxtSpiralOpacity.Text = $"{opacity}%";
                    _isLoading = false;
                }
            });
        }
        
        public void EnablePinkFilter(bool enabled)
        {
            App.Settings.Current.PinkFilterEnabled = enabled;
            Dispatcher.Invoke(() =>
            {
                if (ProgressionTab.ChkPinkFilterEnabled != null && !_isLoading)
                {
                    _isLoading = true;
                    ProgressionTab.ChkPinkFilterEnabled.IsChecked = enabled;
                    _isLoading = false;
                }
            });
        }
        
        public void EnableSpiral(bool enabled)
        {
            App.Settings.Current.SpiralEnabled = enabled;
            Dispatcher.Invoke(() =>
            {
                if (ProgressionTab.ChkSpiralEnabled != null && !_isLoading)
                {
                    _isLoading = true;
                    ProgressionTab.ChkSpiralEnabled.IsChecked = enabled;
                    _isLoading = false;
                }
            });
        }
        
        public void UpdatePinkFilterOpacity(int opacity)
        {
            App.Settings.Current.PinkFilterOpacity = opacity;
            Dispatcher.Invoke(() =>
            {
                if (ProgressionTab.SliderPinkOpacity != null && !_isLoading)
                {
                    _isLoading = true;
                    ProgressionTab.SliderPinkOpacity.Value = opacity;
                    if (ProgressionTab.TxtPinkOpacity != null) ProgressionTab.TxtPinkOpacity.Text = $"{opacity}%";
                    _isLoading = false;
                }
            });
        }

        public void EnableBrainDrain(bool enabled, int intensity = 5)
        {
            App.Settings.Current.BrainDrainEnabled = enabled;
            App.Settings.Current.BrainDrainIntensity = intensity;

            if (enabled)
            {
                App.BrainDrain.Start(bypassLevelCheck: true);
            }
            else
            {
                App.BrainDrain.Stop();
            }

            Dispatcher.Invoke(() =>
            {
                if (ProgressionTab.ChkBrainDrainEnabled != null && !_isLoading)
                {
                    _isLoading = true;
                    ProgressionTab.ChkBrainDrainEnabled.IsChecked = enabled;
                    if (ProgressionTab.SliderBrainDrainIntensity != null) ProgressionTab.SliderBrainDrainIntensity.Value = intensity;
                    if (ProgressionTab.TxtBrainDrainIntensity != null) ProgressionTab.TxtBrainDrainIntensity.Text = $"{intensity}%";
                    _isLoading = false;
                }
            });
        }

        public void UpdateBrainDrainIntensity(int intensity)
        {
            App.Settings.Current.BrainDrainIntensity = intensity;
            App.BrainDrain.UpdateSettings();

            Dispatcher.Invoke(() =>
            {
                if (ProgressionTab.SliderBrainDrainIntensity != null && !_isLoading)
                {
                    _isLoading = true;
                    ProgressionTab.SliderBrainDrainIntensity.Value = intensity;
                    if (ProgressionTab.TxtBrainDrainIntensity != null) ProgressionTab.TxtBrainDrainIntensity.Text = $"{intensity}%";
                    _isLoading = false;
                }
            });
        }

        public void SetBubblesActive(bool active, int bubblesPerBurst = 5)
        {
            // Bubbles are handled by BubbleService through the settings
            // Toggle the enabled state and actually start/stop the service
            if (active)
            {
                App.Settings.Current.BubblesEnabled = true;
                App.Settings.Current.BubblesFrequency = bubblesPerBurst * 2; // Higher frequency during burst

                // Actually start the bubble service if not running (bypass level check for sessions)
                if (!App.Bubbles.IsRunning)
                {
                    App.Bubbles.Start(bypassLevelCheck: true);
                    App.Logger?.Information("Bubble burst started via SetBubblesActive");
                }
                else
                {
                    // Already spawning (e.g. left running from the dashboard) - the interval is
                    // computed inside Start(), so apply the burst frequency we just wrote
                    App.Bubbles.RefreshFrequency();
                }
            }
            else
            {
                // Stop bubbles when burst ends
                App.Bubbles.Stop();
                App.Settings.Current.BubblesEnabled = false;
                App.Logger?.Information("Bubble burst ended via SetBubblesActive");
            }
        }

        private void HandleHyperlinkClick(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to open hyperlink: {Uri} - {Error}", e.Uri.AbsoluteUri, ex.Message);
            }
        }

        private void LoadPreset(Models.Preset preset)
        {
            // The chokepoint: Preset.ApplyTo overwrites ~40 of the fields a running session
            // prescribes, so applying a preset mid-session discards the dose wholesale. Guarded
            // HERE rather than only at the click handler so every caller is covered, present and
            // future - this tab is also where BtnStartSession and the countdown live, which makes
            // it the preset surface a user is most likely to be looking at mid-session.
            if (RefuseActionIfSessionLocked($"load-preset:{preset.Name}")) return;

            preset.ApplyTo(App.Settings.Current);
            App.Settings.Save();

            _isLoading = true;
            LoadSettings();
            _isLoading = false;

            // Flags alone don't reach the live engine (#872).
            ReconcileRunningServices();

            RefreshPresetsDropdown();

            App.Logger?.Information("Loaded preset: {Name}", preset.Name);
            MessageBox.Show(Loc.GetF("msg_preset_0_loaded", preset.Name), Loc.Get("title_preset_loaded"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// #872: a preset writes the feature flags but nothing told the RUNNING services about it,
        /// so a feature the preset switched off kept firing until the engine was restarted - the
        /// bubble-count game the user watched appear after loading a preset that disables it.
        /// Stops every service whose flag the preset cleared.
        ///
        /// Deliberately one-way: it never STARTS a service the preset turned on. The feature-card
        /// quick toggles above only start a service on an explicit user click, and a preset load
        /// should not conjure new effects onto the screen out of a settings change.
        /// </summary>
        private void ReconcileRunningServices()
        {
            var s = App.Settings?.Current;
            if (s == null || !App.IsEngineRunning) return;

            try
            {
                if (!s.FlashEnabled) App.Flash?.Stop();
                if (!s.MandatoryVideosEnabled) App.Video?.Stop();
                if (!s.SubliminalEnabled) App.Subliminal?.Stop();
                // Bubbles are the one shared field here: a CHAOS run drives the SAME BubbleService,
                // and BubbleService.Stop() ends with PopAllBubbles() over the shared list — so
                // honouring the preset's ambient-bubbles flag mid-chaos would wipe the chaos run's
                // own bubbles out from under it. The ambient flag is re-applied at the next start;
                // chaos owns the field while it is running.
                if (!s.BubblesEnabled && App.Chaos?.IsRunning != true) App.Bubbles?.Stop();
                if (!s.BubbleCountEnabled) App.BubbleCount?.Stop();
                if (!s.LockCardEnabled) App.LockCard?.Stop();
                if (!s.BouncingTextEnabled) App.BouncingText?.Stop();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Preset apply: reconciling running services failed");
            }
        }

        internal void BtnLoadPreset_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPreset == null) return;
            // Checked before the confirm too, so the user is never asked to approve something
            // that is then refused. LoadPreset re-checks as the real guard.
            if (RefuseActionIfSessionLocked("load-preset-click")) return;

            var result = MessageBox.Show(
                Loc.GetF("msg_load_preset_confirm_0", _selectedPreset.Name),
                Loc.Get("title_load_preset"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                LoadPreset(_selectedPreset);
            }
        }

        internal void BtnNewPreset_Click(object sender, RoutedEventArgs e)
        {
            PromptSaveNewPreset();
        }

        private void PromptSaveNewPreset()
        {
            var dialog = new InputDialog(Loc.Get("title_new_preset"), Loc.Get("msg_enter_a_name_for_your_preset"), Loc.Get("label_my_custom_preset"));
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ResultText))
            {
                var name = dialog.ResultText.Trim();
                
                // Check if name already exists
                if (_allPresets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(Loc.Get("msg_a_preset_with_this_name_already_exists"), Loc.Get("title_name_taken"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                var preset = Models.Preset.FromSettings(App.Settings.Current, name, "Custom preset created by user");
                App.Settings.Current.UserPresets.Add(preset);
                App.Settings.Current.CurrentPresetName = name;
                App.Settings.Save();
                
                RefreshPresetsList();
                RefreshPresetsDropdown();
                SelectPreset(preset);
                
                App.Logger?.Information("Created new preset: {Name}", name);
                MessageBox.Show(Loc.GetF("msg_preset_0_saved", name), Loc.Get("title_preset_saved"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        internal void BtnSaveOverPreset_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPreset == null || _selectedPreset.IsDefault) return;
            
            var result = MessageBox.Show(
                Loc.GetF("msg_overwrite_preset_confirm_0", _selectedPreset.Name),
                Loc.Get("title_overwrite_preset"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                // Update the preset with current settings
                var updated = Models.Preset.FromSettings(App.Settings.Current, _selectedPreset.Name, _selectedPreset.Description);
                updated.Id = _selectedPreset.Id;
                updated.CreatedAt = _selectedPreset.CreatedAt;
                
                // Find and replace in user presets
                var index = App.Settings.Current.UserPresets.FindIndex(p => p.Id == _selectedPreset.Id);
                if (index >= 0)
                {
                    App.Settings.Current.UserPresets[index] = updated;
                    App.Settings.Save();
                    
                    RefreshPresetsList();
                    SelectPreset(updated);
                    
                    App.Logger?.Information("Updated preset: {Name}", updated.Name);
                    MessageBox.Show(Loc.GetF("msg_preset_0_updated", updated.Name), Loc.Get("title_preset_updated"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        internal void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPreset == null || _selectedPreset.IsDefault) return;

            var result = MessageBox.Show(
                Loc.GetF("msg_delete_preset_confirm_0", _selectedPreset.Name),
                Loc.Get("title_delete_preset"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
                
            if (result == MessageBoxResult.Yes)
            {
                App.Settings.Current.UserPresets.RemoveAll(p => p.Id == _selectedPreset.Id);
                App.Settings.Save();
                
                _selectedPreset = null;
                
                RefreshPresetsList();
                RefreshPresetsDropdown();
                
                App.Logger?.Information("Deleted preset");
            }
        }

        #endregion
    }
}
