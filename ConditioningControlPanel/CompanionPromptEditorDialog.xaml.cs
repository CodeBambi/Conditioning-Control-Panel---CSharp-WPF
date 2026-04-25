using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.AIService;

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
            ChkUseLocal.IsChecked = settings.UseLocalAi;
            CmbAiModel.Text = string.IsNullOrWhiteSpace(settings.AiModel)
                ? _defaults.AiModel : settings.AiModel;
            TxtOllamaHost.Text = string.IsNullOrWhiteSpace(settings.AiOllamaHost)
                ? _defaults.AiOllamaHost : settings.AiOllamaHost;
            // Populate the model dropdown asynchronously from Ollama's /api/tags.
            _ = RefreshAiModelsAsync();

            ChkAllowAiEffects.IsChecked = settings.AllowAiToControlEffects;
            ChkAllowAiFlash.IsChecked = settings.AllowAiFlash;
            ChkAllowAiVideo.IsChecked = settings.AllowAiVideo;
            ChkAllowAiAudio.IsChecked = settings.AllowAiAudio;
            ChkAllowAiBubbles.IsChecked = settings.AllowAiBubbles;
            ChkAllowAiSubliminal.IsChecked = settings.AllowAiSubliminal;
            ChkAllowAiOverlay.IsChecked = settings.AllowAiOverlay;
            ChkAllowAiLockCard.IsChecked = settings.AllowAiLockCard;
            ChkAllowAiBounce.IsChecked = settings.AllowAiBounce;
            ChkAllowAiHaptic.IsChecked = settings.AllowAiHaptic;
            ChkAllowAiGetBackToMe.IsChecked = settings.AllowAiGetBackToMe;
            SldMaxAiHapticIntensity.Value = settings.MaxAiHapticIntensity;
            LblHapticIntensityValue.Text = $"{(int)(settings.MaxAiHapticIntensity * 100)}%";

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

            UpdateEnabledState();
            _hasUnsavedChanges = false;
        }

        private void SaveSettings()
        {
            if (App.Settings?.Current == null) return;

            var settings = App.Settings.Current.CompanionPrompt;
            settings.UseCustomPrompt = ChkUseCustom.IsChecked == true;
            settings.UseLocalAi = ChkUseLocal.IsChecked == true;
            settings.AiModel = string.IsNullOrWhiteSpace(CmbAiModel.Text) ? _defaults.AiModel : CmbAiModel.Text.Trim();
            settings.AiOllamaHost = string.IsNullOrWhiteSpace(TxtOllamaHost.Text) ? _defaults.AiOllamaHost : TxtOllamaHost.Text.Trim();
            settings.AllowAiToControlEffects = ChkAllowAiEffects.IsChecked == true;
            settings.AllowAiFlash = ChkAllowAiFlash.IsChecked == true;
            settings.AllowAiVideo = ChkAllowAiVideo.IsChecked == true;
            settings.AllowAiAudio = ChkAllowAiAudio.IsChecked == true;
            settings.AllowAiBubbles = ChkAllowAiBubbles.IsChecked == true;
            settings.AllowAiSubliminal = ChkAllowAiSubliminal.IsChecked == true;
            settings.AllowAiOverlay = ChkAllowAiOverlay.IsChecked == true;
            settings.AllowAiLockCard = ChkAllowAiLockCard.IsChecked == true;
            settings.AllowAiBounce = ChkAllowAiBounce.IsChecked == true;
            settings.AllowAiHaptic = ChkAllowAiHaptic.IsChecked == true;
            settings.AllowAiGetBackToMe = ChkAllowAiGetBackToMe.IsChecked == true;
            settings.MaxAiHapticIntensity = SldMaxAiHapticIntensity.Value;
            settings.Personality = TxtPersonality.Text;
            settings.ExplicitReaction = TxtExplicitReaction.Text;
            settings.SlutModePersonality = TxtSlutMode.Text;
            settings.KnowledgeBase = TxtKnowledgeBase.Text;
            settings.ContextReactions = TxtContextReactions.Text;
            settings.OutputRules = TxtOutputRules.Text;

            // Save global knowledge base links
            SaveKnowledgeLinks();

            App.Settings.Save();
            _hasUnsavedChanges = false;

            App.Logger?.Information("Companion prompt settings saved. UseCustomPrompt={UseCustom}, GlobalLinks={LinkCount}",
                settings.UseCustomPrompt, _knowledgeLinks.Count);
        }

        private void UpdateEnabledState()
        {
            var isEnabled = ChkUseCustom.IsChecked == true;
            ContentPanel.IsEnabled = isEnabled;
            ContentPanel.Opacity = isEnabled ? 1.0 : 0.5;

            if (LocalPanel != null)
            {
                LocalPanel.Visibility = ChkUseLocal.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ChkUseCustom_Changed(object sender, RoutedEventArgs e)
        {
            UpdateEnabledState();
            _hasUnsavedChanges = true;
        }

        private void ChkUseLocal_Changed(object sender, RoutedEventArgs e)
        {
            UpdateEnabledState();
            _hasUnsavedChanges = true;
        }

        private void ResetAiModel_Click(object sender, RoutedEventArgs e)
        {
            CmbAiModel.Text = _defaults.AiModel;
        }

        private async void RefreshAiModels_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAiModelsAsync();
        }

        private async Task RefreshAiModelsAsync()
        {
            // Use the host as currently typed (not the saved one), so the user can
            // switch to a remote Ollama and refresh the list before saving.
            var host = string.IsNullOrWhiteSpace(TxtOllamaHost?.Text) ? _defaults.AiOllamaHost : TxtOllamaHost.Text.Trim();
            var current = CmbAiModel.Text;

            var models = await LocalAiService.ListInstalledModelsAsync(host);

            CmbAiModel.Items.Clear();
            foreach (var name in models) CmbAiModel.Items.Add(name);

            // Preserve whatever the user had typed/selected so we don't blow away their setting.
            CmbAiModel.Text = current;
        }

        private void OnComboTextChanged(object? sender, EventArgs e)
        {
            _hasUnsavedChanges = true;
        }

        private void ResetOllamaHost_Click(object sender, RoutedEventArgs e)
        {
            TxtOllamaHost.Text = _defaults.AiOllamaHost;
        }

        // Tracks whether the current Checked event was caused by user interaction (so we
        // can show the warning only on the first opt-in, not when restoring saved state).
        private bool _aiEffectsLoadedOnce;

        private void ChkAllowAiEffects_Changed(object sender, RoutedEventArgs e)
        {
            var on = ChkAllowAiEffects.IsChecked == true;

            if (on && _aiEffectsLoadedOnce)
            {
                var result = MessageBox.Show(
                    Loc.Get("dialog_ai_effects_first_time_warning"),
                    Loc.Get("section_ai_effect_permissions"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    ChkAllowAiEffects.IsChecked = false;
                    return;
                }
            }

            if (AiEffectsGrid != null)
            {
                AiEffectsGrid.IsEnabled = on;
                AiEffectsGrid.Opacity = on ? 1.0 : 0.5;
            }
            if (HapticIntensityPanel != null)
            {
                HapticIntensityPanel.IsEnabled = on;
                HapticIntensityPanel.Opacity = on ? 1.0 : 0.5;
            }

            _aiEffectsLoadedOnce = true;
            _hasUnsavedChanges = true;
        }

        private void SldMaxAiHapticIntensity_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblHapticIntensityValue != null)
            {
                LblHapticIntensityValue.Text = $"{(int)(e.NewValue * 100)}%";
            }
            _hasUnsavedChanges = true;
        }

        private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _hasUnsavedChanges = true;
        }

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
            SaveSettings();
            DialogResult = true;
            Close();
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
