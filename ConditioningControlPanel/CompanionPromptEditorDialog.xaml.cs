using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Converts null or empty strings to Collapsed visibility.
    /// </summary>
    public class NullOrEmptyToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Dialog for editing AI companion prompt settings.
    /// Allows users to customize personality, reactions, knowledge base, and output rules.
    /// </summary>
    public partial class CompanionPromptEditorDialog : Window
    {
        private readonly CompanionPromptSettings _defaults;
        private bool _hasUnsavedChanges;
        private readonly ObservableCollection<KnowledgeBaseLink> _knowledgeLinks = new();

        public CompanionPromptEditorDialog()
        {
            InitializeComponent();

            _defaults = CompanionPromptSettings.GetDefaults();
            LoadCurrentSettings();
            LoadKnowledgeLinks();
            UpdateActivePromptDisplay();
            ApplyPolicyBannerState();
        }

        /// <summary>
        /// CCBill AI Addendum: show the full content-policy banner until the user
        /// clicks "Got it", then collapse to a slim non-dismissable reminder. State is
        /// persisted in CompanionPromptSettings.PromptEditorDisclaimerAcknowledged.
        /// </summary>
        private void ApplyPolicyBannerState()
        {
            var acked = App.Settings?.Current?.CompanionPrompt?.PromptEditorDisclaimerAcknowledged == true;
            if (PolicyBannerFull != null)
                PolicyBannerFull.Visibility = acked ? Visibility.Collapsed : Visibility.Visible;
            if (PolicyBannerSlim != null)
                PolicyBannerSlim.Visibility = acked ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnPolicyGotIt_Click(object sender, RoutedEventArgs e)
        {
            var settings = App.Settings?.Current?.CompanionPrompt;
            if (settings != null)
            {
                settings.PromptEditorDisclaimerAcknowledged = true;
                App.Settings?.Save();
            }
            ApplyPolicyBannerState();
        }

        private void BtnPolicyRead_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "https://app.cclabs.app/policies/prohibited-content") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "CompanionPromptEditorDialog: failed to open policy URL");
            }
        }

        /// <summary>
        /// Loads global knowledge base links into the list.
        /// </summary>
        private void LoadKnowledgeLinks()
        {
            _knowledgeLinks.Clear();
            var links = App.Settings?.Current?.GlobalKnowledgeBaseLinks;
            if (links != null)
            {
                foreach (var link in links)
                {
                    _knowledgeLinks.Add(link);
                }
            }
            LstKnowledgeLinks.ItemsSource = _knowledgeLinks;
        }

        /// <summary>
        /// Saves global knowledge base links from the list.
        /// </summary>
        private void SaveKnowledgeLinks()
        {
            if (App.Settings?.Current == null) return;

            App.Settings.Current.GlobalKnowledgeBaseLinks.Clear();
            foreach (var link in _knowledgeLinks)
            {
                App.Settings.Current.GlobalKnowledgeBaseLinks.Add(link);
            }
        }

        /// <summary>
        /// Updates the active prompt name display in the header.
        /// </summary>
        private void UpdateActivePromptDisplay()
        {
            var activePromptId = App.Settings?.Current?.ActiveCommunityPromptId;

            if (!string.IsNullOrEmpty(activePromptId))
            {
                // Community prompt is active
                var prompt = App.CommunityPrompts?.GetInstalledPrompt(activePromptId);
                if (prompt != null)
                {
                    TxtActivePromptName.Text = prompt.Name;
                    TxtActivePromptName.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(147, 112, 219)); // Purple
                }
                else
                {
                    TxtActivePromptName.Text = Loc.Get("label_unknown_prompt");
                    TxtActivePromptName.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(255, 107, 107)); // Red
                }
            }
            else if (App.Settings?.Current?.CompanionPrompt?.UseCustomPrompt == true)
            {
                // Custom prompt is active
                TxtActivePromptName.Text = Loc.Get("label_custom");
                TxtActivePromptName.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4"));
            }
            else
            {
                // Default prompt
                TxtActivePromptName.Text = Loc.Get("label_default");
                TxtActivePromptName.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(112, 112, 112)); // Gray
            }
        }



        private void LoadCurrentSettings()
        {
            var settings = App.Settings?.Current?.CompanionPrompt ?? new CompanionPromptSettings();

            ChkUseCustom.IsChecked = settings.UseCustomPrompt;
            // Provider toggle, model name, host URL, and effect permissions all live
            // in the AI Brain panel on the Companion tab — this dialog is for prompts,
            // memory, and awareness tuning.

            // Load values, falling back to defaults if empty
            TxtPersonality.Text = string.IsNullOrWhiteSpace(settings.Personality)
                ? _defaults.Personality : settings.Personality;
            TxtExplicitReaction.Text = string.IsNullOrWhiteSpace(settings.ExplicitReaction)
                ? _defaults.ExplicitReaction : settings.ExplicitReaction;
            TxtSlutMode.Text = string.IsNullOrWhiteSpace(settings.SlutModePersonality)
                ? _defaults.SlutModePersonality : settings.SlutModePersonality;
            TxtKnowledgeBase.Text = string.IsNullOrWhiteSpace(settings.KnowledgeBase)
                ? _defaults.KnowledgeBase : settings.KnowledgeBase;
            TxtContextReactions.Text = string.IsNullOrWhiteSpace(settings.ContextReactions)
                ? _defaults.ContextReactions : settings.ContextReactions;
            TxtOutputRules.Text = string.IsNullOrWhiteSpace(settings.OutputRules)
                ? _defaults.OutputRules : settings.OutputRules;

            // Memory & Awareness
            ChkDialogChatMemoryEnabled.IsChecked = settings.ChatMemoryEnabled;
            SliderDialogChatMemorySendPairs.Value = Math.Clamp(settings.ChatMemorySendPairs, 1, 50);
            TxtDialogChatMemorySendPairs.Text = settings.ChatMemorySendPairs.ToString();

            var appSettings = App.Settings?.Current;
            ChkDialogAwarenessMode.IsChecked = appSettings?.AwarenessModeEnabled == true && appSettings?.AwarenessConsentGiven == true;

            ChkDialogActionHistoryEnabled.IsChecked = settings.AiActionHistoryEnabled;
            var hours = Math.Clamp(settings.AiActionHistoryHours, 1, 8);
            SliderDialogActionHistoryHours.Value = hours;
            TxtDialogActionHistoryHours.Text = $"{hours} hour{(hours == 1 ? "" : "s")}";
            var maxEvents = Math.Clamp(settings.AiActionHistoryMaxEvents, 50, 500);
            SliderDialogActionHistoryMaxEvents.Value = maxEvents;
            TxtDialogActionHistoryMaxEvents.Text = maxEvents.ToString();
            UpdateActionHistoryWarning();

            ChkDialogSubjectProfileEnabled.IsChecked = settings.AiSubjectProfileEnabled;

            var cooldown = Math.Clamp(appSettings?.AwarenessReactionCooldownSeconds ?? 90, 10, 300);
            SliderDialogAwarenessCooldown.Value = cooldown;
            TxtDialogAwarenessCooldown.Text = $"{cooldown}s";

            var bufferMs = Math.Clamp(appSettings?.KeywordBufferTimeoutMs ?? 3000, 1000, 10000);
            SliderDialogKeywordBufferTimeout.Value = bufferMs;
            TxtDialogKeywordBufferTimeout.Text = $"{bufferMs / 1000.0:F1}s";

            var multiplier = Math.Clamp(appSettings?.KeywordSessionMultiplier ?? 1.5, 1.0, 3.0);
            SliderDialogKeywordSessionMultiplier.Value = multiplier;
            TxtDialogKeywordSessionMultiplier.Text = $"{multiplier:F1}x";

            var highlightMs = Math.Clamp(appSettings?.KeywordHighlightDurationMs ?? 1500, 300, 5000);
            SliderDialogKeywordHighlightDuration.Value = highlightMs / 1000.0;
            TxtDialogKeywordHighlightDuration.Text = $"{highlightMs / 1000.0:F1}s";

            var scanSec = Math.Clamp(appSettings?.ScreenOcrIntervalMs ?? 3000, 2000, 10000) / 1000;
            SliderDialogScreenOcrInterval.Value = scanSec;
            TxtDialogScreenOcrInterval.Text = $"{scanSec}s";

            UpdateEnabledState();
            _hasUnsavedChanges = false;
        }

        private void SaveSettings()
        {
            if (App.Settings?.Current == null) return;

            var settings = App.Settings.Current.CompanionPrompt;
            settings.UseCustomPrompt = ChkUseCustom.IsChecked == true;
            // Provider/model/host/effect-permission settings are owned by the AI Brain
            // panel; we only persist personality-related fields here.
            settings.Personality = TxtPersonality.Text;
            settings.ExplicitReaction = TxtExplicitReaction.Text;
            settings.SlutModePersonality = TxtSlutMode.Text;
            settings.KnowledgeBase = TxtKnowledgeBase.Text;
            settings.ContextReactions = TxtContextReactions.Text;
            settings.OutputRules = TxtOutputRules.Text;

            // Memory & Awareness
            settings.ChatMemoryEnabled = ChkDialogChatMemoryEnabled.IsChecked == true;
            settings.OpenAiCompatibleChatMemoryEnabled = settings.ChatMemoryEnabled;
            settings.ChatMemorySendPairs = (int)SliderDialogChatMemorySendPairs.Value;
            settings.AiActionHistoryEnabled = ChkDialogActionHistoryEnabled.IsChecked == true;
            settings.AiActionHistoryHours = (int)SliderDialogActionHistoryHours.Value;
            settings.AiActionHistoryMaxEvents = (int)SliderDialogActionHistoryMaxEvents.Value;
            settings.AiSubjectProfileEnabled = ChkDialogSubjectProfileEnabled.IsChecked == true;

            if (App.Settings?.Current != null)
            {
                var appSettings = App.Settings.Current;

                bool awarenessOn = ChkDialogAwarenessMode.IsChecked == true;
                appSettings.AwarenessModeEnabled = awarenessOn;
                appSettings.AwarenessConsentGiven = awarenessOn;

                var cooldown = (int)SliderDialogAwarenessCooldown.Value;
                appSettings.AwarenessReactionCooldownSeconds = cooldown;
                // Unify keyword-trigger cooldowns so there is only one awareness cooldown to manage.
                appSettings.KeywordGlobalCooldownSeconds = cooldown;
                appSettings.KeywordPerKeywordCooldownSeconds = cooldown;

                appSettings.KeywordBufferTimeoutMs = (int)SliderDialogKeywordBufferTimeout.Value;
                appSettings.KeywordSessionMultiplier = SliderDialogKeywordSessionMultiplier.Value;
                appSettings.KeywordHighlightDurationMs = (int)(SliderDialogKeywordHighlightDuration.Value * 1000);
                appSettings.ScreenOcrIntervalMs = (int)(SliderDialogScreenOcrInterval.Value * 1000);
            }

            // Save global knowledge base links
            SaveKnowledgeLinks();

            App.Settings.Save();
            _hasUnsavedChanges = false;

            App.Logger?.Information("Companion prompt settings saved. UseCustomPrompt={UseCustom}, GlobalLinks={LinkCount}",
                settings.UseCustomPrompt, _knowledgeLinks.Count);
        }

                private void UpdateEnabledState()
        {
            // Personality form is dimmed when the user is on default prompts, but
            // memory/awareness settings are independent and stay editable.
            var isEnabled = ChkUseCustom.IsChecked == true;
            ContentPanel.IsEnabled = isEnabled;
            ContentPanel.Opacity = isEnabled ? 1.0 : 0.5;
            if (ExpanderMemoryAwareness != null)
            {
                ExpanderMemoryAwareness.IsEnabled = true;
                ExpanderMemoryAwareness.Opacity = 1.0;
            }
        }

        private void ChkUseCustom_Changed(object sender, RoutedEventArgs e)
        {
            UpdateEnabledState();
            _hasUnsavedChanges = true;
        }

        private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _hasUnsavedChanges = true;
        }

        #region Memory & Awareness handlers

        private void ChkDialogChatMemoryEnabled_Changed(object sender, RoutedEventArgs e)
        {
            _hasUnsavedChanges = true;
        }

        private void ChkDialogAwarenessMode_Changed(object sender, RoutedEventArgs e)
        {
            _hasUnsavedChanges = true;
            var appSettings = App.Settings?.Current;
            if (appSettings == null) return;
            bool isEnabled = ChkDialogAwarenessMode.IsChecked == true;
            appSettings.AwarenessModeEnabled = isEnabled;
            appSettings.AwarenessConsentGiven = isEnabled;
            App.Settings?.Save();
            if (isEnabled) App.WindowAwareness?.Start();
            else App.WindowAwareness?.Stop();
        }

        private void SliderDialogChatMemorySendPairs_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtDialogChatMemorySendPairs == null) return;
            var value = Math.Max(1, (int)SliderDialogChatMemorySendPairs.Value);
            TxtDialogChatMemorySendPairs.Text = value.ToString();
            _hasUnsavedChanges = true;
        }

        private void ChkDialogActionHistoryEnabled_Changed(object sender, RoutedEventArgs e)
        {
            _hasUnsavedChanges = true;
        }

        private void SliderDialogActionHistoryHours_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtDialogActionHistoryHours == null) return;
            var hours = Math.Max(1, Math.Min(8, (int)SliderDialogActionHistoryHours.Value));
            TxtDialogActionHistoryHours.Text = $"{hours} hour{(hours == 1 ? "" : "s")}";
            UpdateActionHistoryWarning();
            _hasUnsavedChanges = true;
        }

        private void SliderDialogActionHistoryMaxEvents_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtDialogActionHistoryMaxEvents == null) return;
            var value = Math.Max(50, Math.Min(500, (int)SliderDialogActionHistoryMaxEvents.Value));
            TxtDialogActionHistoryMaxEvents.Text = value.ToString();
            _hasUnsavedChanges = true;
        }

        private void UpdateActionHistoryWarning()
        {
            if (TxtDialogActionHistoryWarning == null || SliderDialogActionHistoryHours == null) return;
            var hours = (int)SliderDialogActionHistoryHours.Value;
            var (msg, color) = hours switch
            {
                <= 2 => ("Short window: keeps prompts small and fast.", TryFindResource("TextDimBrush") as Brush),
                <= 5 => ("Moderate window: balanced recall and prompt size.", new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x00))),
                _ => ("Long window: richer recall but larger prompts and slower responses.", new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)))
            };
            TxtDialogActionHistoryWarning.Text = msg;
            TxtDialogActionHistoryWarning.Foreground = color ?? TxtDialogActionHistoryWarning.Foreground;
        }

        private void ChkDialogSubjectProfileEnabled_Changed(object sender, RoutedEventArgs e)
        {
            _hasUnsavedChanges = true;
        }

        private void SliderDialogAwarenessCooldown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtDialogAwarenessCooldown == null) return;
            var value = Math.Max(10, Math.Min(300, (int)SliderDialogAwarenessCooldown.Value));
            TxtDialogAwarenessCooldown.Text = $"{value}s";
            _hasUnsavedChanges = true;
        }

        private void SliderDialogKeywordBufferTimeout_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtDialogKeywordBufferTimeout == null) return;
            var value = Math.Max(1000, Math.Min(10000, (int)SliderDialogKeywordBufferTimeout.Value));
            TxtDialogKeywordBufferTimeout.Text = $"{value / 1000.0:F1}s";
            _hasUnsavedChanges = true;
        }

        private void SliderDialogKeywordSessionMultiplier_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtDialogKeywordSessionMultiplier == null) return;
            var value = Math.Max(1.0, Math.Min(3.0, SliderDialogKeywordSessionMultiplier.Value));
            TxtDialogKeywordSessionMultiplier.Text = $"{value:F1}x";
            _hasUnsavedChanges = true;
        }

        private void SliderDialogKeywordHighlightDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtDialogKeywordHighlightDuration == null) return;
            var value = Math.Max(0.3, Math.Min(5.0, SliderDialogKeywordHighlightDuration.Value));
            TxtDialogKeywordHighlightDuration.Text = $"{value:F1}s";
            _hasUnsavedChanges = true;
        }

        private void SliderDialogScreenOcrInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtDialogScreenOcrInterval == null) return;
            var value = Math.Max(2, Math.Min(10, (int)SliderDialogScreenOcrInterval.Value));
            TxtDialogScreenOcrInterval.Text = $"{value}s";
            _hasUnsavedChanges = true;
        }

        private void BtnResetAiMemory_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Clear all AI memory?\n\nThis will erase persisted chat history, recent actions, live actions, and the subject profile. This cannot be undone.",
                "Reset AI Memory",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                if (App.Ai is Services.AIService.AiServiceStrategy strategy)
                    strategy.ClearChatHistory();

                App.AiLiveActions.Clear();
                App.ActionHistory?.Clear();
                App.SubjectProfile?.Clear();

                MessageBox.Show("AI memory cleared.", "Reset AI Memory", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BtnResetAiMemory_Click failed");
            }
        }

        #endregion

        private void ResetPersonality_Click(object sender, RoutedEventArgs e)
        {
            TxtPersonality.Text = _defaults.Personality;
        }

        private void ResetExplicitReaction_Click(object sender, RoutedEventArgs e)
        {
            TxtExplicitReaction.Text = _defaults.ExplicitReaction;
        }

        private void ResetSlutMode_Click(object sender, RoutedEventArgs e)
        {
            TxtSlutMode.Text = _defaults.SlutModePersonality;
        }

        private void ResetKnowledgeBase_Click(object sender, RoutedEventArgs e)
        {
            TxtKnowledgeBase.Text = _defaults.KnowledgeBase;
        }

        private void ResetContextReactions_Click(object sender, RoutedEventArgs e)
        {
            TxtContextReactions.Text = _defaults.ContextReactions;
        }

        private void ResetOutputRules_Click(object sender, RoutedEventArgs e)
        {
            TxtOutputRules.Text = _defaults.OutputRules;
        }

        private void AddKnowledgeLink_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new KnowledgeLinkEditorDialog { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                _knowledgeLinks.Add(dialog.Result);
                _hasUnsavedChanges = true;
            }
        }

        private void RemoveKnowledgeLink_Click(object sender, RoutedEventArgs e)
        {
            if (LstKnowledgeLinks.SelectedItem is KnowledgeBaseLink link)
            {
                _knowledgeLinks.Remove(link);
                _hasUnsavedChanges = true;
            }
            else
            {
                MessageBox.Show(Loc.Get("msg_please_select_a_link_to_remove"), "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ResetAll_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Reset all prompts to their default values?\n\nThis cannot be undone.",
                "Reset All Prompts",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                TxtPersonality.Text = _defaults.Personality;
                TxtExplicitReaction.Text = _defaults.ExplicitReaction;
                TxtSlutMode.Text = _defaults.SlutModePersonality;
                TxtKnowledgeBase.Text = _defaults.KnowledgeBase;
                TxtContextReactions.Text = _defaults.ContextReactions;
                TxtOutputRules.Text = _defaults.OutputRules;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // P1.3 PromptValidator: warn on jailbreak/extraction patterns but still
            // allow save. Hits are logged to moderation.log (source=edit). The
            // ModerationGuard at inference time is the load-bearing layer; this is
            // an early-warning surface so the user knows their edit was flagged.
            RunPromptValidation();

            SaveSettings();
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// P1.3 — runs the prompt validator over each editable field, paints
        /// flagged TextBoxes yellow, shows the top banner with a per-field summary,
        /// and records one moderation.log entry per flagged field. Always returns
        /// (save is never blocked).
        /// </summary>
        private void RunPromptValidation()
        {
            var validator = App.PromptValidator;
            if (validator == null) return;

            var fields = new (string FieldName, TextBox Box)[]
            {
                ("Personality", TxtPersonality),
                ("ExplicitReaction", TxtExplicitReaction),
                ("SlutModePersonality", TxtSlutMode),
                ("KnowledgeBase", TxtKnowledgeBase),
                ("ContextReactions", TxtContextReactions),
                ("OutputRules", TxtOutputRules),
            };

            // Default brush to restore on clean fields (matches the existing PromptTextBox
            // style's BorderBrush; pulled from the in-style #20FFFFFF subtle outline).
            var cleanBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
            var flaggedBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xC7, 0x6B));

            var flaggedNames = new List<string>();
            foreach (var (fieldName, box) in fields)
            {
                if (box == null) continue;
                var result = validator.Validate(box.Text ?? string.Empty);
                if (result.Clean)
                {
                    box.BorderBrush = cleanBrush;
                    box.BorderThickness = new Thickness(1);
                    box.ClearValue(TextBox.ToolTipProperty);
                }
                else
                {
                    box.BorderBrush = flaggedBrush;
                    box.BorderThickness = new Thickness(2);
                    var tip = string.Format(
                        Loc.Get("prompt_validator_warning"),
                        result.MatchedPatterns.Count);
                    box.ToolTip = tip;
                    flaggedNames.Add(fieldName);
                    App.ModerationLog?.RecordEdit(fieldName, result.MatchedPatterns.Count, "companion_prompt");
                }
            }

            if (flaggedNames.Count == 0)
            {
                ValidatorBanner.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtValidatorBanner.Text = string.Format(
                    Loc.Get("prompt_validator_banner"),
                    flaggedNames.Count);
                ValidatorBanner.Visibility = Visibility.Visible;
                App.Logger?.Information(
                    "PromptValidator flagged {Count} field(s) in CompanionPromptEditorDialog",
                    flaggedNames.Count);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Discard them?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            DialogResult = false;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // If closing via X button and has unsaved changes, prompt
            if (!DialogResult.HasValue && _hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Save before closing?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    SaveSettings();
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
            }

            base.OnClosing(e);
        }
    }
}
