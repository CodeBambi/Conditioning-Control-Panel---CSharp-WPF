using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Dialog for editing AI companion prompt settings.
    /// Allows users to customize personality, reactions, knowledge base, and output rules.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/CompanionPromptEditorDialog.xaml.cs. Deviations:
    ///  - <c>NullOrEmptyToCollapsedConverter</c> is gone: the XAML uses Avalonia's built-in
    ///    <c>StringConverters.IsNotNullOrEmpty</c> on <c>IsVisible</c>.
    ///  - Settings come from <c>CompanionPromptSettings.GetDefaults()</c> in Core; persisting them,
    ///    the community-prompt lookup and the moderation log are stubs (ponytail comments below).
    ///  - <c>PromptValidator</c> lives in Core and runs for real on Save.
    ///  - The three MessageBox confirms have no Avalonia equivalent and no package may be added;
    ///    they are stubbed and noted, so Cancel/Reset All/close act without asking.
    ///  - <c>DialogResult = x; Close()</c> -> <c>Close(x)</c>.
    /// </summary>
    public partial class CompanionPromptEditorDialog : Window
    {
        private readonly CompanionPromptSettings _defaults;
        private bool _hasUnsavedChanges;
        private readonly ObservableCollection<KnowledgeBaseLink> _knowledgeLinks = new();

        private readonly CheckBox _chkUseCustom;
        private readonly StackPanel _contentPanel;
        private readonly ListBox _lstKnowledgeLinks;
        private readonly TextBox _txtPersonality, _txtExplicitReaction, _txtSlutMode,
                                 _txtKnowledgeBase, _txtContextReactions, _txtOutputRules;

        public CompanionPromptEditorDialog()
        {
            AvaloniaXamlLoader.Load(this);

            _chkUseCustom = this.FindControl<CheckBox>("ChkUseCustom")!;
            _contentPanel = this.FindControl<StackPanel>("ContentPanel")!;
            _lstKnowledgeLinks = this.FindControl<ListBox>("LstKnowledgeLinks")!;
            _txtPersonality = this.FindControl<TextBox>("TxtPersonality")!;
            _txtExplicitReaction = this.FindControl<TextBox>("TxtExplicitReaction")!;
            _txtSlutMode = this.FindControl<TextBox>("TxtSlutMode")!;
            _txtKnowledgeBase = this.FindControl<TextBox>("TxtKnowledgeBase")!;
            _txtContextReactions = this.FindControl<TextBox>("TxtContextReactions")!;
            _txtOutputRules = this.FindControl<TextBox>("TxtOutputRules")!;

            _defaults = CompanionPromptSettings.GetDefaults();
            LoadCurrentSettings();
            LoadKnowledgeLinks();
            UpdateActivePromptDisplay();
            ApplyPolicyBannerState();

            // Handlers wired after the loads so the initial Text assignments do not count as edits.
            _chkUseCustom.IsCheckedChanged += (_, _) => ChkUseCustom_Changed();
            foreach (var box in new[] { _txtPersonality, _txtExplicitReaction, _txtSlutMode, _txtKnowledgeBase, _txtContextReactions, _txtOutputRules })
                box.TextChanged += (_, _) => _hasUnsavedChanges = true;

            this.FindControl<Button>("BtnPolicyGotIt")!.Click += (_, _) => BtnPolicyGotIt_Click();
            this.FindControl<Button>("BtnPolicyReadFull")!.Click += (_, _) => BtnPolicyRead_Click();
            this.FindControl<Button>("BtnPolicyReadSlim")!.Click += (_, _) => BtnPolicyRead_Click();
            this.FindControl<Button>("ResetPersonality")!.Click += (_, _) => _txtPersonality.Text = _defaults.Personality;
            this.FindControl<Button>("ResetExplicitReaction")!.Click += (_, _) => _txtExplicitReaction.Text = _defaults.ExplicitReaction;
            this.FindControl<Button>("ResetSlutMode")!.Click += (_, _) => _txtSlutMode.Text = _defaults.SlutModePersonality;
            this.FindControl<Button>("ResetKnowledgeBase")!.Click += (_, _) => _txtKnowledgeBase.Text = _defaults.KnowledgeBase;
            this.FindControl<Button>("ResetContextReactions")!.Click += (_, _) => _txtContextReactions.Text = _defaults.ContextReactions;
            this.FindControl<Button>("ResetOutputRules")!.Click += (_, _) => _txtOutputRules.Text = _defaults.OutputRules;
            this.FindControl<Button>("AddKnowledgeLink")!.Click += (_, _) => AddKnowledgeLink_Click();
            this.FindControl<Button>("RemoveKnowledgeLink")!.Click += (_, _) => RemoveKnowledgeLink_Click();
            this.FindControl<Button>("BtnResetAll")!.Click += (_, _) => ResetAll_Click();
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => BtnCancel_Click();
            this.FindControl<Button>("BtnSave")!.Click += (_, _) => BtnSave_Click();
        }

        /// <summary>
        /// CCBill AI Addendum: show the full content-policy banner until the user
        /// clicks "Got it", then collapse to a slim non-dismissable reminder.
        /// </summary>
        private void ApplyPolicyBannerState()
        {
            // ponytail: needs App.Settings.Current.CompanionPrompt.PromptEditorDisclaimerAcknowledged,
            // wired when settings move to Core. The default is "not yet acknowledged".
            var acked = _policyAcked;
            this.FindControl<Border>("PolicyBannerFull")!.IsVisible = !acked;
            this.FindControl<Border>("PolicyBannerSlim")!.IsVisible = acked;
        }

        private bool _policyAcked;

        private void BtnPolicyGotIt_Click()
        {
            _policyAcked = true; // ponytail: persisted via App.Settings.Save() once settings move to Core
            ApplyPolicyBannerState();
        }

        private void BtnPolicyRead_Click()
        {
            // ponytail: needs a launcher for https://app.cclabs.app/policies/prohibited-content;
            // Process.Start(UseShellExecute) is per-platform and belongs behind a Core interface.
        }

        /// <summary>
        /// Loads global knowledge base links into the list.
        /// </summary>
        private void LoadKnowledgeLinks()
        {
            _knowledgeLinks.Clear();
            // ponytail: needs App.Settings.Current.GlobalKnowledgeBaseLinks; wired when settings
            // move to Core. One sample row so the template renders.
            _knowledgeLinks.Add(new KnowledgeBaseLink { Title = "Sample link", Url = "https://example.com", Description = "Placeholder entry" });
            _lstKnowledgeLinks.ItemsSource = _knowledgeLinks;
        }

        /// <summary>
        /// Updates the active prompt name display in the header.
        /// </summary>
        private void UpdateActivePromptDisplay()
        {
            var txt = this.FindControl<TextBlock>("TxtActivePromptName")!;
            // ponytail: needs App.Settings.Current.ActiveCommunityPromptId + App.CommunityPrompts;
            // wired when they move to Core. Until then only the "custom" and "default" branches exist.
            if (_chkUseCustom.IsChecked == true)
            {
                // Custom prompt is active
                txt.Text = Loc.Get("label_custom");
                txt.Foreground = new SolidColorBrush(Color.Parse("#FF69B4")); // App.Mods accent fallback
            }
            else
            {
                // Default prompt
                txt.Text = Loc.Get("label_default");
                txt.Foreground = new SolidColorBrush(Color.FromRgb(112, 112, 112)); // Gray
            }
        }

        private void LoadCurrentSettings()
        {
            // ponytail: needs App.Settings.Current.CompanionPrompt; wired when settings move to Core.
            var settings = new CompanionPromptSettings();

            _chkUseCustom.IsChecked = settings.UseCustomPrompt;

            // Load values, falling back to defaults if empty
            _txtPersonality.Text = string.IsNullOrWhiteSpace(settings.Personality)
                ? _defaults.Personality : settings.Personality;
            _txtExplicitReaction.Text = string.IsNullOrWhiteSpace(settings.ExplicitReaction)
                ? _defaults.ExplicitReaction : settings.ExplicitReaction;
            _txtSlutMode.Text = string.IsNullOrWhiteSpace(settings.SlutModePersonality)
                ? _defaults.SlutModePersonality : settings.SlutModePersonality;
            _txtKnowledgeBase.Text = string.IsNullOrWhiteSpace(settings.KnowledgeBase)
                ? _defaults.KnowledgeBase : settings.KnowledgeBase;
            _txtContextReactions.Text = string.IsNullOrWhiteSpace(settings.ContextReactions)
                ? _defaults.ContextReactions : settings.ContextReactions;
            _txtOutputRules.Text = string.IsNullOrWhiteSpace(settings.OutputRules)
                ? _defaults.OutputRules : settings.OutputRules;

            UpdateEnabledState();
            _hasUnsavedChanges = false;
        }

        private void SaveSettings()
        {
            // ponytail: needs App.Settings (persist, CommunityPromptService.ClearCustomPromptOverride,
            // Save) and App.Logger; wired when settings move to Core.
            _hasUnsavedChanges = false;
        }

        private void UpdateEnabledState()
        {
            // Whole personality form is dimmed when the user is on default prompts.
            var isEnabled = _chkUseCustom.IsChecked == true;
            _contentPanel.IsEnabled = isEnabled;
            _contentPanel.Opacity = isEnabled ? 1.0 : 0.5;
        }

        private void ChkUseCustom_Changed()
        {
            UpdateEnabledState();
            _hasUnsavedChanges = true;
        }

        private async void AddKnowledgeLink_Click()
        {
            var dialog = new KnowledgeLinkEditorDialog();
            await dialog.ShowDialog(this);
            if (dialog.Result != null)
            {
                _knowledgeLinks.Add(dialog.Result);
                _hasUnsavedChanges = true;
            }
        }

        private void RemoveKnowledgeLink_Click()
        {
            if (_lstKnowledgeLinks.SelectedItem is KnowledgeBaseLink link)
            {
                _knowledgeLinks.Remove(link);
                _hasUnsavedChanges = true;
            }
            // ponytail: WPF showed MessageBox(msg_please_select_a_link_to_remove) otherwise; no
            // MessageBox stand-in exists in this head yet.
        }

        private void ResetAll_Click()
        {
            // ponytail: WPF confirmed with a Yes/No MessageBox first; stubbed, see class summary.
            _txtPersonality.Text = _defaults.Personality;
            _txtExplicitReaction.Text = _defaults.ExplicitReaction;
            _txtSlutMode.Text = _defaults.SlutModePersonality;
            _txtKnowledgeBase.Text = _defaults.KnowledgeBase;
            _txtContextReactions.Text = _defaults.ContextReactions;
            _txtOutputRules.Text = _defaults.OutputRules;
        }

        private void BtnSave_Click()
        {
            // P1.3 PromptValidator: warn on jailbreak/extraction patterns but still
            // allow save. The ModerationGuard at inference time is the load-bearing layer; this is
            // an early-warning surface so the user knows their edit was flagged.
            RunPromptValidation();

            SaveSettings();
            Close(true);
        }

        /// <summary>
        /// P1.3 — runs the prompt validator over each editable field, paints
        /// flagged TextBoxes yellow and shows the top banner with a per-field summary.
        /// Always returns (save is never blocked).
        /// </summary>
        private void RunPromptValidation()
        {
            var validator = new PromptValidator();

            var fields = new (string FieldName, TextBox Box)[]
            {
                ("Personality", _txtPersonality),
                ("ExplicitReaction", _txtExplicitReaction),
                ("SlutModePersonality", _txtSlutMode),
                ("KnowledgeBase", _txtKnowledgeBase),
                ("ContextReactions", _txtContextReactions),
                ("OutputRules", _txtOutputRules),
            };

            var cleanBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
            var flaggedBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xC7, 0x6B));

            var flaggedNames = new List<string>();
            foreach (var (fieldName, box) in fields)
            {
                var result = validator.Validate(box.Text ?? string.Empty);
                if (result.Clean)
                {
                    box.BorderBrush = cleanBrush;
                    box.BorderThickness = new Thickness(1);
                    box.ClearValue(ToolTip.TipProperty);
                }
                else
                {
                    box.BorderBrush = flaggedBrush;
                    box.BorderThickness = new Thickness(2);
                    ToolTip.SetTip(box, Loc.GetF("prompt_validator_warning", result.MatchedPatterns.Count));
                    flaggedNames.Add(fieldName);
                    // ponytail: App.ModerationLog?.RecordEdit(fieldName, count, "companion_prompt"),
                    // wired when the moderation log moves to Core.
                }
            }

            var banner = this.FindControl<Border>("ValidatorBanner")!;
            if (flaggedNames.Count == 0)
            {
                banner.IsVisible = false;
            }
            else
            {
                this.FindControl<TextBlock>("TxtValidatorBanner")!.Text = Loc.GetF("prompt_validator_banner", flaggedNames.Count);
                banner.IsVisible = true;
            }
        }

        private void BtnCancel_Click()
        {
            if (_hasUnsavedChanges)
            {
                // ponytail: WPF asked "discard unsaved changes?" here; stubbed, see class summary.
            }
            Close(false);
        }

        // ponytail: WPF's OnClosing prompted Save/Discard/Cancel on the X button with unsaved
        // changes; without a MessageBox stand-in the X simply closes. _hasUnsavedChanges is kept so
        // the prompt drops in unchanged.
    }
}
