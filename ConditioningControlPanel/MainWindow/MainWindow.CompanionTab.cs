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
    // Companion tab: selection cards, prompts, and avatar UI.
    public partial class MainWindow
    {
        #region Companion Tab

        /// <summary>
        /// Sync Companion tab UI controls with current state
        /// </summary>
        private void SyncCompanionTabUI()
        {
            _isLoading = true;
            try
            {
                // Sync trigger mode
                CompanionTab.ChkTriggerModeCompanion.IsChecked = App.Settings?.Current?.TriggerModeEnabled == true;
                CompanionTab.TriggerSettingsPanelCompanion.Visibility = CompanionTab.ChkTriggerModeCompanion.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

                // Sync trigger interval
                var interval = App.Settings?.Current?.TriggerIntervalSeconds ?? 60;
                CompanionTab.SliderTriggerIntervalCompanion.Value = interval;
                CompanionTab.TxtTriggerIntervalCompanion.Text = $"{interval}s";

                // Sync idle interval
                var idleInterval = App.Settings?.Current?.IdleGiggleIntervalSeconds ?? 120;
                CompanionTab.SliderIdleIntervalCompanion.Value = idleInterval;
                CompanionTab.TxtIdleIntervalCompanion.Text = $"{idleInterval}s";

                // Sync bubble persistence duration
                var bubbleDuration = App.Settings?.Current?.BubbleDurationSeconds ?? 2.0;
                CompanionTab.SliderBubbleDurationCompanion.Value = bubbleDuration;
                CompanionTab.TxtBubbleDurationCompanion.Text = $"{(int)bubbleDuration}s";

                // Sync detach status (hidden compat element — the hero's own chip shows the state)
                var isDetached = _avatarTubeWindow?.IsDetached == true;
                CompanionTab.TxtDetachStatusCompanion.Text = isDetached ? "Floating freely" : "Anchored to window";

                // Sync companion leveling UI (v5.3)
                UpdateCompanionCardsUI();

                // Sync AI Brain panel + hero pills (v5.9)
                SyncAiBrainUI();
            }
            finally
            {
                _isLoading = false;
            }

            // Outside the loading guard: the page re-reads settings, and the guard exists to stop
            // the CONTROLS' change handlers from writing back while we populate them.
            CompanionRoom?.Sync();
        }

        /// <summary>
        /// Updates the companion selection cards UI with current progress and active state.
        /// </summary>
        private void UpdateCompanionCardsUI()
        {
            if (App.Companion == null || App.Settings?.Current == null) return;

            var activeId = App.Companion.ActiveCompanion;
            var playerLevel = App.Settings.Current.PlayerLevel;

            // Update each companion card
            var cards = new[] { CompanionTab.CompanionCard0, CompanionTab.CompanionCard1, CompanionTab.CompanionCard2, CompanionTab.CompanionCard3, CompanionTab.CompanionCard4 };
            var levelTexts = new[] { CompanionTab.TxtCompanion0Level, CompanionTab.TxtCompanion1Level, CompanionTab.TxtCompanion2Level, CompanionTab.TxtCompanion3Level, CompanionTab.TxtCompanion4Level };
            var lockTexts = new[] { CompanionTab.TxtCompanion0Lock, CompanionTab.TxtCompanion1Lock, CompanionTab.TxtCompanion2Lock, CompanionTab.TxtCompanion3Lock, CompanionTab.TxtCompanion4Lock };
            var nameTexts = new[] { CompanionTab.TxtCompanion0Name, CompanionTab.TxtCompanion1Name, CompanionTab.TxtCompanion2Name, CompanionTab.TxtCompanion3Name, CompanionTab.TxtCompanion4Name };
            var colors = new[] { App.Mods?.GetAccentColorHex() ?? "#FF69B4", "#9370DB", "#50C878", "#FF6B6B", "#F5DEB3" };

            for (int i = 0; i < 5; i++)
            {
                var companionId = (Models.CompanionId)i;
                var def = Models.CompanionDefinition.GetById(companionId);
                var progress = App.Companion.GetProgress(companionId);

                // Hide companion card if the active mod doesn't support this avatar set
                if (App.Mods?.IsCompanionSupported(companionId) == false)
                {
                    cards[i].Visibility = Visibility.Collapsed;
                    continue;
                }
                cards[i].Visibility = Visibility.Visible;

                // Update companion name with mod text replacements
                bool isSlutMode = App.Settings?.Current?.SlutModeEnabled ?? false;
                var companionName = def.GetDisplayName(isSlutMode);
                nameTexts[i].Text = App.Mods?.MakeModAware(companionName) ?? companionName;

                // All companions are unlocked from level 1
                levelTexts[i].Text = progress.IsMaxLevel ? "MAX" : $"Lv.{progress.Level}";

                // Highlight active companion with colored border
                var isActive = companionId == activeId;
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors[i]);
                cards[i].BorderBrush = isActive
                    ? new System.Windows.Media.SolidColorBrush(color)
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Transparent);

                // Companion lock visuals removed — every companion is available from level 1.
                lockTexts[i].Visibility = Visibility.Collapsed;
                cards[i].Opacity = 1.0;
            }

            // The hero's identity, portrait, level and XP moved into CompanionHeroRuntimeVm with
            // the Companion Card (design §6: "kept — Z1, restyled"). It reads the same
            // CompanionProgress this method used to poke into five TextBlocks, so this is one call
            // instead of eight writes — and the portrait pipeline (RefreshHeroAvatar) went with it.
            CompanionRoom?.SyncHero();

            // Update community prompts UI
            UpdateCommunityPromptsUI();

            // Update companion prompt labels
            UpdateCompanionPromptLabels();
        }

        /// <summary>
        /// Gets the display name for the currently active prompt.
        /// </summary>
        private string GetActivePromptDisplayName()
        {
            var activePromptId = App.Settings?.Current?.ActiveCommunityPromptId;

            if (!string.IsNullOrEmpty(activePromptId))
            {
                var prompt = App.CommunityPrompts?.GetInstalledPrompt(activePromptId);
                return prompt?.Name ?? "Unknown";
            }
            else if (App.Settings?.Current?.CompanionPrompt?.UseCustomPrompt == true)
            {
                return "Custom";
            }
            return "Default";
        }

        /// <summary>
        /// Updates the community prompts section UI.
        /// </summary>
        private void UpdateCommunityPromptsUI()
        {
            var activePromptId = App.Settings?.Current?.ActiveCommunityPromptId;
            var installedIds = App.Settings?.Current?.InstalledCommunityPromptIds ?? new List<string>();

            // The active-personality readout and its Reset link are Z4's now (design §6:
            // "kept — Z4 readout line"), so the three writes that used to live here — the
            // Customize button's prompt name, TxtActivePromptName, and BtnDeactivatePrompt's
            // visibility — are one viewmodel re-read. Z4 derives all three from the same settings.
            CompanionRoom?.PersonalityVm.Sync();

            // Update installed prompts list
            CompanionTab.InstalledPromptsPanel.Children.Clear();
            if (installedIds.Count == 0)
            {
                CompanionTab.InstalledPromptsPanel.Children.Add(new TextBlock
                {
                    Text = "No prompts installed",
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 80, 80)),
                    FontSize = 10,
                    FontStyle = FontStyles.Italic,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
            else
            {
                foreach (var id in installedIds)
                {
                    var prompt = App.CommunityPrompts?.GetInstalledPrompt(id);
                    if (prompt == null) continue;

                    var isActive = id == activePromptId;
                    var row = CreatePromptRow(prompt, isActive);
                    CompanionTab.InstalledPromptsPanel.Children.Add(row);
                }
            }
        }

        private FrameworkElement CreatePromptRow(Models.CommunityPrompt prompt, bool isActive)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Name + Author
            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (isActive)
            {
                namePanel.Children.Add(new TextBlock
                {
                    Text = "● ",
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(147, 112, 219)),
                    FontSize = 10
                });
            }
            namePanel.Children.Add(new TextBlock
            {
                Text = prompt.Name,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                FontSize = 10,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal
            });
            namePanel.Children.Add(new TextBlock
            {
                Text = $" by {prompt.Author}",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(96, 96, 96)),
                FontSize = 9
            });
            Grid.SetColumn(namePanel, 0);
            grid.Children.Add(namePanel);

            // Action buttons
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };

            if (!isActive)
            {
                var activateBtn = new Button
                {
                    Content = "Use",
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(147, 112, 219)),
                    BorderThickness = new Thickness(0),
                    FontSize = 9,
                    Padding = new Thickness(6, 2, 6, 2),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = prompt.Id
                };
                activateBtn.Click += (s, e) =>
                {
                    if (s is Button btn && btn.Tag is string promptId)
                    {
                        // CCBill AI Addendum: gate community prompts that ship a SlutModePersonality
                        // when SlutMode is on. Synthesize a probe preset for the gate check.
                        var probePrompt = App.CommunityPrompts?.GetInstalledPrompt(promptId);
                        var slutModeOn = App.Settings?.Current?.SlutModeEnabled == true;
                        var probe = new Models.PersonalityPreset { PromptSettings = probePrompt?.PromptSettings };
                        if (Services.ExplicitContentGate.RequiresAcknowledgement(probe, slutModeOn))
                        {
                            var prevSettings = App.Settings?.Current?.CompanionPrompt;
                            if (!Services.ExplicitContentGate.IsAlreadyAcknowledged(prevSettings))
                            {
                                var dlg = new ExplicitContentAcknowledgementDialog { Owner = this };
                                if (dlg.ShowDialog() != true) return;
                                if (prevSettings != null)
                                {
                                    Services.ExplicitContentGate.MarkAcknowledged(prevSettings);
                                    App.Settings?.Save();
                                }
                            }
                        }

                        App.CommunityPrompts?.ActivatePrompt(promptId);
                        UpdateCommunityPromptsUI();
                    }
                };
                buttonPanel.Children.Add(activateBtn);
            }

            var removeBtn = new Button
            {
                Content = "×",
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 128, 128)),
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Padding = new Thickness(4, 0, 4, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = prompt.Id,
                ToolTip = "Remove"
            };
            removeBtn.Click += (s, e) =>
            {
                if (s is Button btn && btn.Tag is string promptId)
                {
                    App.CommunityPrompts?.RemovePrompt(promptId);
                    UpdateCommunityPromptsUI();
                }
            };
            buttonPanel.Children.Add(removeBtn);

            Grid.SetColumn(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            return grid;
        }

        /// <summary>
        /// Handles clicking on a companion card to switch companions.
        /// Also switches the avatar to match the selected companion.
        /// </summary>
        internal void CompanionCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // If the click came from the personality button, ignore it (let the button handle it)
            if (e.OriginalSource is FrameworkElement source)
            {
                // Check if clicked element or any of its parents is a personality button
                var parent = source;
                while (parent != null)
                {
                    if (parent is Button btn && btn.Name != null && btn.Name.Contains("Personality"))
                    {
                        App.Logger?.Information("Click originated from personality button, ignoring card click");
                        return; // Don't handle card click, let button handle it
                    }
                    parent = System.Windows.Media.VisualTreeHelper.GetParent(parent) as FrameworkElement;
                }
            }

            if (sender is not FrameworkElement element || element.Tag == null) return;
            if (!int.TryParse(element.Tag.ToString(), out int companionIndex)) return;

            var companionId = (Models.CompanionId)companionIndex;
            var def = Models.CompanionDefinition.GetById(companionId);

            // Switch companion
            if (App.Companion?.SwitchCompanion(companionId) == true)
            {
                UpdateCompanionCardsUI();

                // Also switch the avatar to match the companion
                _avatarTubeWindow?.SwitchToCompanionAvatar(companionId);

                App.Logger?.Information("Switched to companion: {Name}", def.Name);
            }
        }

        /// <summary>
        /// Handles clicking the personality button on a companion card.
        /// Opens a dialog to assign a prompt JSON to this companion.
        /// </summary>
        internal void BtnCompanionPersonality_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            App.Logger?.Information("Personality button clicked");
            e.Handled = true; // Prevent card click from also triggering

            if (sender is not FrameworkElement element || element.Tag == null)
            {
                App.Logger?.Warning("Personality button: sender or tag is null");
                return;
            }
            if (!int.TryParse(element.Tag.ToString(), out int companionIndex))
            {
                App.Logger?.Warning("Personality button: failed to parse index");
                return;
            }

            var companionId = (Models.CompanionId)companionIndex;
            var def = Models.CompanionDefinition.GetById(companionId);
            var isUnlocked = App.Companion?.IsCompanionUnlocked(companionId) ?? false;

            App.Logger?.Information("Personality clicked: {Companion}, unlocked: {Unlocked}", def.Name, isUnlocked);

            // Check if companion is unlocked
            if (!isUnlocked)
            {
                App.Logger?.Warning("{Companion} is locked", def.Name);
                ShowStyledDialog(Loc.Get("dialog_locked"), Loc.GetF("msg_companion_locked", def.Name), "OK", "");
                return;
            }

            // Show options: Import JSON, Choose from installed, or Clear
            var currentPromptId = App.Settings?.Current?.GetCompanionPromptId(companionIndex);
            var currentPromptName = Services.CompanionService.GetAssignedPromptName(companionId);
            var hasAssigned = !string.IsNullOrEmpty(currentPromptName);

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Select AI Personality for {def.Name}",
                Filter = "Prompt JSON files (*.json)|*.json",
                DefaultExt = ".json"
            };

            // Check for prompts folder
            var promptsFolder = System.IO.Path.Combine(App.EffectiveAssetsPath, "prompts");
            if (System.IO.Directory.Exists(promptsFolder))
            {
                dialog.InitialDirectory = promptsFolder;
            }

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Import the prompt file if needed
                    var prompt = App.CommunityPrompts?.ImportFromFile(dialog.FileName);
                    if (prompt != null)
                    {
                        // Assign to companion
                        App.Settings?.Current?.SetCompanionPromptId(companionIndex, prompt.Id);
                        App.Settings?.Save();

                        // Update UI
                        UpdateCompanionPromptLabels();

                        App.Logger?.Information("Assigned prompt '{Prompt}' to companion {Companion}",
                            prompt.Name, def.Name);

                        ShowStyledDialog(Loc.Get("title_personality_assigned"),
                            Loc.GetF("msg_personality_assigned", def.Name, prompt.Name),
                            Loc.Get("btn_ok"), "");
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to assign prompt to companion");
                    ShowStyledDialog(Loc.Get("title_error"), Loc.GetF("msg_failed_to_import_prompt", ex.Message), Loc.Get("btn_ok"), "");
                }
            }
        }

        /// <summary>
        /// Updates the prompt labels on all companion cards.
        /// </summary>
        private void UpdateCompanionPromptLabels()
        {
            var promptTexts = new[] { CompanionTab.TxtCompanion0Prompt, CompanionTab.TxtCompanion1Prompt, CompanionTab.TxtCompanion2Prompt, CompanionTab.TxtCompanion3Prompt, CompanionTab.TxtCompanion4Prompt };

            for (int i = 0; i < promptTexts.Length; i++)
            {
                var promptName = Services.CompanionService.GetAssignedPromptName((Models.CompanionId)i);
                var displayName = App.Mods?.MakeModAware(promptName ?? "") ?? promptName ?? "";
                promptTexts[i].Text = displayName;
                promptTexts[i].ToolTip = string.IsNullOrEmpty(displayName) ? null : Loc.GetF("tooltip_ai_personality", displayName);
            }
        }
        #endregion
    }
}
