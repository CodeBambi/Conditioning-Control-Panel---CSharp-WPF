using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ConditioningControlPanel.Models;

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
                    TxtActivePromptName.Text = "Unknown Prompt";
                    TxtActivePromptName.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(255, 107, 107)); // Red
                }
            }
            else if (App.Settings?.Current?.CompanionPrompt?.UseCustomPrompt == true)
            {
                // Custom prompt is active
                TxtActivePromptName.Text = "Custom";
                TxtActivePromptName.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(255, 105, 180)); // Pink
            }
            else
            {
                // Default prompt
                TxtActivePromptName.Text = "Default";
                TxtActivePromptName.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(112, 112, 112)); // Gray
            }
        }

        private void LoadCurrentSettings()
        {
            var settings = App.Settings?.Current?.CompanionPrompt ?? new CompanionPromptSettings();

            ChkUseCustom.IsChecked = settings.UseCustomPrompt;

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
                MessageBox.Show("Please select a link to remove.", "No Selection",
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
