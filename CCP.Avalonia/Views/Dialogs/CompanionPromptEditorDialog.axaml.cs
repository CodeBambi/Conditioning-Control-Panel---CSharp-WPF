using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Moderation;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Dialog for editing AI companion prompt settings.
    /// Allows users to customize personality, reactions, knowledge base, and output rules.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/CompanionPromptEditorDialog.xaml.cs. Deviations:
    ///  - <c>NullOrEmptyToCollapsedConverter</c> is gone: the XAML uses Avalonia's built-in
    ///    <c>StringConverters.IsNotNullOrEmpty</c> on <c>IsVisible</c>.
    ///  - Settings load and save for real against <see cref="CoreSettings"/>, knowledge links
    ///    included, and every flagged field is recorded through <see cref="CoreModerationLog"/>.
    ///    The community-prompt lookup is the one stub left (see <c>UpdateActivePromptBadge</c>).
    ///  - <c>PromptValidator</c> lives in Core and runs for real on Save.
    ///  - <c>MessageBox.Show</c> becomes this head's <see cref="MessageDialog"/>, which is awaited,
    ///    so Remove / Reset All / Cancel are async void. The X button's three-way
    ///    Save/Discard/Cancel prompt uses <see cref="UnsavedChangesDialog"/> (see OnClosing).
    ///  - <c>DialogResult = x; Close()</c> -> <c>Close(x)</c>.
    /// </summary>
    public partial class CompanionPromptEditorDialog : Window
    {
        private readonly CompanionPromptSettings _defaults;
        private bool _hasUnsavedChanges;
        /// <summary>WPF's <c>DialogResult.HasValue</c>: set by Save/Cancel (and by an answered X
        /// prompt) so their Close(x) skips the unsaved-changes prompt in OnClosing.</summary>
        private bool _closeDecided;
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

            _defaults = ActivePersonaPromptText.Resolve(ActivePersonaPrompt(), CompanionPromptSettings.GetDefaults());
            LoadCurrentSettings();
            LoadKnowledgeLinks();
            UpdateActivePromptDisplay();
            ApplyPolicyBannerState();

            // Handlers wired after the loads so the initial Text assignments do not count as edits.
            _chkUseCustom.IsCheckedChanged += (_, _) => ChkUseCustom_Changed();
            foreach (var box in new[] { _txtPersonality, _txtExplicitReaction, _txtSlutMode, _txtKnowledgeBase, _txtContextReactions, _txtOutputRules })
                // Not TextChanged: Avalonia raises it on a later dispatcher pass, so the loads above
                // would mark a clean dialog dirty and its X would ask about edits nobody made.
                box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) _hasUnsavedChanges = true; };

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

        /// <summary>The running persona's prompt text, or null when it carries none (WPF
        /// ActivePersonaPrompt, read from Core's PersonalityService instead of App.Personality).</summary>
        private static CompanionPromptSettings? ActivePersonaPrompt()
        {
            try { return PersonalityService.Shared.GetActivePreset()?.PromptSettings; }
            catch { return null; }
        }

        /// <summary>
        /// CCBill AI Addendum: show the full content-policy banner until the user
        /// clicks "Got it", then collapse to a slim non-dismissable reminder.
        /// </summary>
        private void ApplyPolicyBannerState()
        {
            var acked = CoreSettings.Current.CompanionPrompt.PromptEditorDisclaimerAcknowledged;
            this.FindControl<Border>("PolicyBannerFull")!.IsVisible = !acked;
            this.FindControl<Border>("PolicyBannerSlim")!.IsVisible = acked;
        }

        private void BtnPolicyGotIt_Click()
        {
            CoreSettings.Current.CompanionPrompt.PromptEditorDisclaimerAcknowledged = true;
            CoreSettings.Save();
            ApplyPolicyBannerState();
        }

        /// <summary>WPF used Process.Start with UseShellExecute; Avalonia's Launcher is the
        /// cross-platform equivalent and needs no shell assumptions.</summary>
        private async void BtnPolicyRead_Click()
        {
            try
            {
                await Platform.ExternalOpener.OpenAsync(this, "https://app.cclabs.app/policies/prohibited-content");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "CompanionPromptEditorDialog: failed to open policy URL");
            }
        }

        /// <summary>
        /// Loads global knowledge base links into the list.
        /// </summary>
        private void LoadKnowledgeLinks()
        {
            _knowledgeLinks.Clear();
            foreach (var link in CoreSettings.Current.GlobalKnowledgeBaseLinks)
            {
                _knowledgeLinks.Add(link);
            }
            _lstKnowledgeLinks.ItemsSource = _knowledgeLinks;
        }

        /// <summary>
        /// Saves global knowledge base links from the list.
        /// </summary>
        private void SaveKnowledgeLinks()
        {
            var links = CoreSettings.Current.GlobalKnowledgeBaseLinks;
            links.Clear();
            foreach (var link in _knowledgeLinks)
            {
                links.Add(link);
            }
        }

        /// <summary>
        /// Updates the active prompt name display in the header.
        /// </summary>
        private void UpdateActivePromptDisplay()
        {
            var txt = this.FindControl<TextBlock>("TxtActivePromptName")!;
            var settings = CoreSettings.Current;

            if (!string.IsNullOrEmpty(settings.ActiveCommunityPromptId))
            {
                // ponytail: a community prompt IS active, but its display name needs
                // ConditioningControlPanel/Services/Companion/CommunityPromptService.GetInstalledPrompt,
                // which is head-only. Painting "unknown prompt" here would be an invented answer -
                // WPF says that only when the lookup MISSES - so the badge keeps its XAML default
                // until the service crosses.
                return;
            }

            if (settings.CompanionPrompt.UseCustomPrompt)
            {
                // Custom prompt is active
                BindLoc(txt, "label_custom");
                txt.Foreground = new SolidColorBrush(Color.Parse(CoreMods.AccentColorHex));
            }
            else
            {
                // Default prompt
                BindLoc(txt, "label_default");
                txt.Foreground = new SolidColorBrush(Color.FromRgb(112, 112, 112)); // Gray
            }
        }

        /// <summary>
        /// TxtActivePromptName carries <c>{loc:Str label_default}</c> from the XAML. An assignment
        /// to .Text sits UNDER that binding and is undone on the next language change, so the
        /// branch picks a key and rebinds - the same binding {loc:Str} would have produced.
        /// </summary>
        private static void BindLoc(TextBlock target, string key)
            => target.Bind(TextBlock.TextProperty, new Binding($"[{key}]")
            {
                Source = LocalizationManager.Instance,
                Mode = BindingMode.OneWay,
            });

        private void LoadCurrentSettings()
        {
            var settings = CoreSettings.Current.CompanionPrompt;

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
            var settings = CoreSettings.Current.CompanionPrompt;
            settings.UseCustomPrompt = _chkUseCustom.IsChecked == true;
            // Provider/model/host/effect-permission settings are owned by the AI Brain
            // panel; we only persist personality-related fields here.
            // Coalesced: WPF's TextBox.Text is never null, Avalonia's is null when empty, and
            // the settings model's fields are non-nullable strings.
            // ONLY what the user changed: a box still showing the persona's text persists blank
            // (WPF Persist -> ActivePersonaPromptText.PersistedField, which also coalesces null).
            settings.Personality = ActivePersonaPromptText.PersistedField(_txtPersonality.Text, _defaults.Personality);
            settings.ExplicitReaction = ActivePersonaPromptText.PersistedField(_txtExplicitReaction.Text, _defaults.ExplicitReaction);
            settings.SlutModePersonality = ActivePersonaPromptText.PersistedField(_txtSlutMode.Text, _defaults.SlutModePersonality);
            settings.KnowledgeBase = ActivePersonaPromptText.PersistedField(_txtKnowledgeBase.Text, _defaults.KnowledgeBase);
            settings.ContextReactions = ActivePersonaPromptText.PersistedField(_txtContextReactions.Text, _defaults.ContextReactions);
            settings.OutputRules = ActivePersonaPromptText.PersistedField(_txtOutputRules.Text, _defaults.OutputRules);

            // Un-ticking "use custom prompt" also drops the community id (WPF SaveSettings).
            if (!settings.UseCustomPrompt)
                PersonalityService.ClearCustomPromptOverride(CoreSettings.Current);

            // Save global knowledge base links
            SaveKnowledgeLinks();

            CoreSettings.Save();
            _hasUnsavedChanges = false;

            Log.Information("Companion prompt settings saved. UseCustomPrompt={UseCustom}, GlobalLinks={LinkCount}",
                settings.UseCustomPrompt, _knowledgeLinks.Count);
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
            await dialog.ShowDialogSafe(this);
            if (dialog.Result != null)
            {
                _knowledgeLinks.Add(dialog.Result);
                _hasUnsavedChanges = true;
            }
        }

        private async void RemoveKnowledgeLink_Click()
        {
            if (_lstKnowledgeLinks.SelectedItem is KnowledgeBaseLink link)
            {
                _knowledgeLinks.Remove(link);
                _hasUnsavedChanges = true;
            }
            else
            {
                await MessageDialog.ShowAsync(this, "No Selection", Loc.Get("msg_please_select_a_link_to_remove"));
            }
        }

        private async void ResetAll_Click()
        {
            // These two strings are hardcoded English in the WPF original, not loc keys; ported as
            // literals rather than inventing key names no catalogue carries.
            var confirmed = await MessageDialog.ConfirmAsync(this, "Reset All Prompts",
                "Put every box back to your companion's own text?\n\nThis cannot be undone.");
            if (!confirmed) return;

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
            _closeDecided = true;
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
                    // Through the seam, so the app's ONE log (and its one per-launch session hash)
                    // takes the entry. Unseeded on this head, so nothing is written here yet.
                    CoreModerationLog.RecordEdit(fieldName, result.MatchedPatterns.Count, "companion_prompt");
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
                Log.Information("PromptValidator flagged {Count} field(s) in CompanionPromptEditorDialog",
                    flaggedNames.Count);
            }
        }

        private async void BtnCancel_Click()
        {
            if (_hasUnsavedChanges)
            {
                var discard = await MessageDialog.ConfirmAsync(this, "Unsaved Changes",
                    "You have unsaved changes. Discard them?");
                if (!discard) return;
            }

            _closeDecided = true;
            Close(false);
        }

        /// <summary>
        /// WPF OnClosing: the X with unsaved changes asks Save / Discard / Cancel (the WPF
        /// strings are hardcoded English). Avalonia's Closing is synchronous, so the close is
        /// cancelled, the prompt awaited, and the window re-closed with the decision recorded.
        /// </summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (!_closeDecided && _hasUnsavedChanges)
            {
                e.Cancel = true;
                _ = AskBeforeCloseAsync();
            }
            base.OnClosing(e);
        }

        private bool _asking;

        internal async System.Threading.Tasks.Task AskBeforeCloseAsync()
        {
            if (_asking) return;
            _asking = true;
            try
            {
                var choice = await UnsavedChangesDialog.AskAsync(this, "Unsaved Changes",
                    "You have unsaved changes. Save before closing?");
                if (choice == UnsavedChangesDialog.Choice.Cancel) return;
                if (choice == UnsavedChangesDialog.Choice.Save) SaveSettings();
                _closeDecided = true;
                Close();
            }
            finally { _asking = false; }
        }
    }
}
