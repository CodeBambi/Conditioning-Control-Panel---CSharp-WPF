using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Moderation;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Modal view of an Awareness Engine preset pack with full inline action editing.
    /// States:
    ///   * Not installed — read-only preview of the preset's bundled triggers,
    ///     Install + Clone-to-custom buttons.
    ///   * Installed — each trigger shows an editable action list. Users can
    ///     add/remove any action type, tune per-action parameters (audio file,
    ///     visual effect, haptic intensity, XP amount, avatar prompt, etc.),
    ///     and every edit persists to <c>AppSettings.KeywordTriggers</c>.
    ///
    /// Rows are built imperatively in <see cref="BuildTriggerBorder"/> rather
    /// than via XAML DataTemplates — eight action types × several controls each
    /// would need a matching DataTemplate hierarchy, and building them in code
    /// keeps the logic (event handlers, validation, enable-state) in one place.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/AwarenessPresetDetailDialog.xaml.cs.
    /// Deviations, all forced:
    ///  - <c>App.Settings</c> is <see cref="CoreSettings"/> and <c>App.KeywordPresets</c> is a
    ///    local <see cref="KeywordTriggerPresetService"/>, both in Core, so install / uninstall /
    ///    clone / delete, the live-clone list in <c>settings.KeywordTriggers</c> and every
    ///    per-action save run for real.
    ///  - <c>MessageBox.Show</c> becomes <see cref="MessageDialog"/>, this head's replacement. The
    ///    two destructive confirmations are kept: they guard against silent data loss.
    ///  - <c>Microsoft.Win32.OpenFileDialog</c> -> <c>StorageProvider.OpenFilePickerAsync</c>, the
    ///    native Avalonia picker. Not a stub: it needs no service and no Win32.
    ///  - <c>Checked</c>/<c>Unchecked</c> -> one <c>IsCheckedChanged</c> handler.
    ///  - <c>ToolTip="…"</c> -> <c>ToolTip.SetTip(…)</c>; <c>FontStyles</c>/<c>FontWeights</c> ->
    ///    <c>FontStyle</c>/<c>FontWeight</c>; <c>Cursors.Hand</c> -> <c>new Cursor(StandardCursorType.Hand)</c>.
    ///  - <c>MenuItem.InputGestureText</c> does not exist in Avalonia (only a KeyGesture), so the
    ///    "(already added)" note is appended to the item header instead.
    ///  - The <c>DialogDarkComboBox</c> style is one FontSize setter, applied directly here.
    /// </summary>
    public partial class AwarenessPresetDetailDialog : Window
    {
        private readonly KeywordTriggerPreset _preset;

        private readonly TextBlock _txtIcon;
        private readonly StackPanel _nameReadPanel;
        private readonly TextBlock _txtName;
        private readonly TextBlock _txtAuthor;
        private readonly TextBox _txtIconEdit;
        private readonly StackPanel _nameEditPanel;
        private readonly TextBox _txtNameEdit;
        private readonly TextBlock _txtDescription;
        private readonly TextBox _txtDescriptionEdit;
        private readonly Border _brdAiBadge;
        private readonly StackPanel _triggerStack;
        private readonly TextBlock _txtFooterNote;
        private readonly Button _btnClone;
        private readonly Button _btnDeletePreset;
        private readonly Button _btnInstall;

        /// <summary>
        /// True until the first successful save of a brand-new user-created preset.
        /// While set, persist-hooks add the preset to <c>settings.KeywordTriggerPresets</c>
        /// on first write so the card shows up in the Awareness grid.
        /// </summary>
        private bool _isCustomPresetUnsaved;

        /// <summary>True if install/uninstall state changed — caller should refresh.</summary>
        public bool Changed { get; private set; }

        /// <summary>Render/design constructor: a sample custom preset that exercises every
        /// action editor, so --render-view draws the whole dialog. Internal, so no production
        /// caller can ship the sample.</summary>
        internal AwarenessPresetDetailDialog() : this(SamplePreset(), isNewCustomPreset: false) { }

        /// <summary>
        /// Open an existing preset (built-in or previously-saved custom) for preview/edit.
        /// </summary>
        public AwarenessPresetDetailDialog(KeywordTriggerPreset preset)
            : this(preset, isNewCustomPreset: false) { }

        /// <summary>
        /// Open a preset for preview/edit. When <paramref name="isNewCustomPreset"/> is
        /// true, the preset is treated as unsaved — editing its metadata or adding a
        /// trigger is what persists it to settings.
        /// </summary>
        public AwarenessPresetDetailDialog(KeywordTriggerPreset preset, bool isNewCustomPreset)
        {
            AvaloniaXamlLoader.Load(this);

            _txtIcon = this.FindControl<TextBlock>("TxtIcon")!;
            _nameReadPanel = this.FindControl<StackPanel>("NameReadPanel")!;
            _txtName = this.FindControl<TextBlock>("TxtName")!;
            _txtAuthor = this.FindControl<TextBlock>("TxtAuthor")!;
            _txtIconEdit = this.FindControl<TextBox>("TxtIconEdit")!;
            _nameEditPanel = this.FindControl<StackPanel>("NameEditPanel")!;
            _txtNameEdit = this.FindControl<TextBox>("TxtNameEdit")!;
            _txtDescription = this.FindControl<TextBlock>("TxtDescription")!;
            _txtDescriptionEdit = this.FindControl<TextBox>("TxtDescriptionEdit")!;
            _brdAiBadge = this.FindControl<Border>("BrdAiBadge")!;
            _triggerStack = this.FindControl<StackPanel>("TriggerStack")!;
            _txtFooterNote = this.FindControl<TextBlock>("TxtFooterNote")!;
            _btnClone = this.FindControl<Button>("BtnClone")!;
            _btnDeletePreset = this.FindControl<Button>("BtnDeletePreset")!;
            _btnInstall = this.FindControl<Button>("BtnInstall")!;

            // Handlers live here rather than in markup, per the porting convention.
            this.FindControl<Button>("BtnPolicyGotIt")!.Click += (_, _) => BtnPolicyGotIt_Click();
            this.FindControl<Button>("BtnPolicyReadFull")!.Click += (_, _) => BtnPolicyRead_Click();
            this.FindControl<Button>("BtnPolicyReadSlim")!.Click += (_, _) => BtnPolicyRead_Click();
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();
            _btnInstall.Click += (_, _) => BtnInstall_Click();
            _btnClone.Click += async (_, _) => await BtnClone_Click();
            _btnDeletePreset.Click += async (_, _) => await BtnDeletePreset_Click();

            _preset = preset;
            _isCustomPresetUnsaved = isNewCustomPreset;

            if (preset.IsBuiltIn)
            {
                // Built-in presets: static header. Name/icon/description are fixed and
                // refreshed from the JSON on version bumps, so they aren't editable.
                _txtIcon.Text = preset.Icon;
                _txtName.Text = preset.Name;
                _txtAuthor.Text = preset.Author;
                _txtDescription.Text = string.IsNullOrEmpty(preset.LongDescription)
                    ? preset.Description
                    : preset.LongDescription;
            }
            else
            {
                // Custom presets: editable name/icon/description. Wire LostFocus so
                // every edit both mutates the preset AND persists (which also creates
                // the preset in settings the first time around).
                _txtIcon.IsVisible = false;
                _nameReadPanel.IsVisible = false;
                _txtDescription.IsVisible = false;

                _txtIconEdit.IsVisible = true;
                _nameEditPanel.IsVisible = true;
                _txtDescriptionEdit.IsVisible = true;

                _txtIconEdit.Text = preset.Icon;
                _txtNameEdit.Text = preset.Name;
                _txtDescriptionEdit.Text = string.IsNullOrEmpty(preset.LongDescription)
                    ? preset.Description
                    : preset.LongDescription;

                _txtIconEdit.LostFocus += (_, _) =>
                {
                    preset.Icon = (_txtIconEdit.Text ?? "").Trim();
                    PersistAndMaybeCreate();
                };
                _txtNameEdit.LostFocus += (_, _) =>
                {
                    var name = (_txtNameEdit.Text ?? "").Trim();
                    preset.Name = string.IsNullOrEmpty(name) ? "Untitled preset" : name;
                    _txtNameEdit.Text = preset.Name;
                    PersistAndMaybeCreate();
                };
                _txtDescriptionEdit.LostFocus += (_, _) =>
                {
                    var text = (_txtDescriptionEdit.Text ?? "").Trim();
                    preset.Description = text;
                    preset.LongDescription = text;
                    PersistAndMaybeCreate();
                };
            }

            if (preset.RequiresAi)
                _brdAiBadge.IsVisible = true;

            ApplyPolicyBannerState();
            RebuildRows();
            UpdateInstallButton();
        }

        // ============================================================
        // Services — App.KeywordPresets and App.Settings, both now in Core
        // ============================================================

        /// <summary>
        /// WPF's <c>App.KeywordPresets</c>. The service is stateless — every read and write goes
        /// through <see cref="CoreSettings.Current"/> — so a local instance installs, uninstalls
        /// and clones exactly as the head-owned one does.
        ///
        /// ponytail: its <c>PresetsChanged</c> event therefore fires only on this instance. Nothing
        /// on this head subscribes yet (the Awareness tab refreshes off <see cref="Changed"/>);
        /// give the head one shared instance if a listener ever needs it.
        /// </summary>
        private static readonly KeywordTriggerPresetService Presets = new();

        private string LiveClonePrefix => "preset:" + _preset.Id + ":";

        private bool IsInstalled() => Presets.IsInstalled(_preset.Id);

        private void Persist() => CoreSettings.Save();

        /// <summary>
        /// CCBill AI Addendum: show full content-policy banner until acked, then slim version.
        /// </summary>
        private void ApplyPolicyBannerState()
        {
            var acked = CoreSettings.Current.CompanionPrompt?.PromptEditorDisclaimerAcknowledged == true;
            this.FindControl<Border>("PolicyBannerFull")!.IsVisible = !acked;
            this.FindControl<Border>("PolicyBannerSlim")!.IsVisible = acked;
        }

        private void BtnPolicyGotIt_Click()
        {
            var prompt = CoreSettings.Current.CompanionPrompt;
            if (prompt != null)
            {
                prompt.PromptEditorDisclaimerAcknowledged = true;
                CoreSettings.Save();
            }
            ApplyPolicyBannerState();
        }

        private async void BtnPolicyRead_Click()
        {
            // WPF used Process.Start(UseShellExecute); TopLevel.Launcher is Avalonia's own, so no
            // seam is needed. Same precedent as CompanionPromptEditorDialog.
            try { await Launcher.LaunchUriAsync(new Uri("https://app.cclabs.app/policies/prohibited-content")); }
            catch (Exception ex) { Log.Warning(ex, "AwarenessPresetDetailDialog: failed to open policy URL"); }
        }

        /// <summary>
        /// Persists settings AND, if this dialog is editing a brand-new user-created
        /// preset, registers it in <c>settings.KeywordTriggerPresets</c> on first write.
        /// </summary>
        private void PersistAndMaybeCreate()
        {
            if (_isCustomPresetUnsaved)
            {
                var list = CoreSettings.Current.KeywordTriggerPresets;
                if (list != null && !list.Any(p => p.Id == _preset.Id))
                    list.Add(_preset);
                _isCustomPresetUnsaved = false;
                Changed = true;
            }
            Persist();
        }

        /// <summary>
        /// For user-created presets that are currently installed, mirror the live cloned triggers
        /// in <c>settings.KeywordTriggers</c> back into <c>preset.Triggers</c>. Keeps the preset
        /// source-of-truth in sync so a later Uninstall -> Install cycle preserves user edits.
        /// No-op for built-ins (their source is owned by the bundled preset JSON).
        /// </summary>
        private void MirrorLiveClonesToCustomSource()
        {
            if (_preset.IsBuiltIn) return;
            if (!IsInstalled()) return;

            var prefix = LiveClonePrefix;
            var synced = new List<KeywordTrigger>();
            foreach (var clone in CoreSettings.Current.KeywordTriggers
                         .Where(t => t?.Id?.StartsWith(prefix, StringComparison.Ordinal) == true)
                         .ToList())
            {
                var copy = clone.Clone();
                copy.Id = clone.Id!.Substring(prefix.Length);
                copy.LastTriggeredAt = DateTime.MinValue;
                synced.Add(copy);
            }
            _preset.Triggers = synced;
        }

        /// <summary>
        /// True when the trigger list (and "+ Add trigger" button, per-trigger delete,
        /// keyword edit) should be usable. Installed presets are always editable;
        /// user-authored presets are editable even when uninstalled so they can be
        /// authored before being turned on.
        /// </summary>
        private bool IsEditable()
        {
            if (IsInstalled()) return true;
            if (!_preset.IsBuiltIn) return true;
            return false;
        }

        // ============================================================
        // Top-level list build
        // ============================================================

        private void RebuildRows()
        {
            _triggerStack.Children.Clear();

            var editable = IsEditable();

            // Installed: edit the LIVE CLONES out of settings.KeywordTriggers (id prefix
            // "preset:<id>:") so every mutation flows back through CoreSettings.Save(). Uninstalled:
            // edit preset.Triggers directly (custom presets) or preview them read-only (built-ins).
            var prefix = LiveClonePrefix;
            var triggers = IsInstalled()
                ? CoreSettings.Current.KeywordTriggers
                    .Where(t => t?.Id?.StartsWith(prefix, StringComparison.Ordinal) == true)
                    .ToList()
                : _preset.Triggers?.Where(t => t != null).ToList() ?? new List<KeywordTrigger>();

            foreach (var trigger in triggers)
                _triggerStack.Children.Add(BuildTriggerBorder(trigger, editable));

            // Inline "+ Add trigger" button at the bottom of the editable list.
            if (editable)
                _triggerStack.Children.Add(BuildAddTriggerRow());
            else if (triggers.Count == 0)
                _triggerStack.Children.Add(BuildEmptyStateNotice());

            // Copy-to-custom spins a built-in off into an editable custom preset.
            // Offered for built-ins regardless of activation state. Custom presets
            // are already editable in place and get a Delete button instead.
            _btnClone.IsVisible = _preset.IsBuiltIn;
            _btnDeletePreset.IsVisible = !_preset.IsBuiltIn;
            _txtFooterNote.IsVisible = editable;
        }

        private void UpdateInstallButton()
        {
            if (IsInstalled())
            {
                _btnInstall.Content = "Deactivate";
                _btnInstall.Background = new SolidColorBrush(Color.FromRgb(0x5A, 0x30, 0x30));
            }
            else
            {
                _btnInstall.Content = "Activate";
                _btnInstall.Background = this.TryFindResource("PinkBrush", out var pink) && pink is IBrush brush
                    ? brush
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
            }
        }

        private Control BuildAddTriggerRow()
        {
            var btn = new Button
            {
                Content = "＋ Add trigger",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 10, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x44)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment = HorizontalAlignment.Left,
                FontSize = 11,
            };
            ToolTip.SetTip(btn, "Append a fresh keyword trigger. Edit the keyword field to name it, then Add actions to define what fires.");
            btn.Click += (_, _) => AddNewTrigger();
            return btn;
        }

        private static Control BuildEmptyStateNotice()
        {
            return new TextBlock
            {
                Text = "No triggers defined for this preset.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0xA0)),
                FontStyle = FontStyle.Italic,
                FontSize = 11,
                Margin = new Thickness(4, 8, 0, 4),
            };
        }

        /// <summary>
        /// Append a new blank <see cref="KeywordTrigger"/> to the preset's trigger list
        /// and refresh the dialog so its row appears.
        /// </summary>
        private void AddNewTrigger()
        {
            var newTrigger = new KeywordTrigger
            {
                Keyword = "",
                MatchType = KeywordMatchType.PlainText,
                Enabled = true,
                CooldownSeconds = 30,
                AudioVolume = 80,
                VisualEffect = KeywordVisualEffect.SubliminalFlash,
                HapticEnabled = false,
                HapticIntensity = 0.3,
                DuckAudio = true,
                XPAward = 0,
            };
            // Blank trigger starts with an empty action list so users can build up
            // exactly what they want via the per-trigger "+ Add action" menu.
            newTrigger.Actions = new List<KeywordAction>();

            if (IsInstalled())
            {
                // Live clone list uses the "preset:<presetId>:<sourceId>" id convention, so the
                // trigger fires immediately.
                var sourceId = Guid.NewGuid().ToString("N")[..8];
                newTrigger.Id = LiveClonePrefix + sourceId;
                CoreSettings.Current.KeywordTriggers.Add(newTrigger);

                // For custom presets, also add a matching source entry so
                // uninstall -> reinstall preserves this addition.
                if (!_preset.IsBuiltIn)
                {
                    var sourceCopy = newTrigger.Clone();
                    sourceCopy.Id = sourceId;
                    _preset.Triggers ??= new List<KeywordTrigger>();
                    _preset.Triggers.Add(sourceCopy);
                }
            }
            else
            {
                _preset.Triggers ??= new List<KeywordTrigger>();
                _preset.Triggers.Add(newTrigger);
            }

            PersistAndMaybeCreate();
            Changed = true;
            RebuildRows();
        }

        /// <summary>
        /// Remove a trigger row entirely.
        /// </summary>
        private void DeleteTrigger(KeywordTrigger trigger)
        {
            if (IsInstalled())
            {
                // Strip the live clone so the keyword stops firing at once.
                CoreSettings.Current.KeywordTriggers.RemoveAll(t => t.Id == trigger.Id);

                if (!_preset.IsBuiltIn)
                {
                    // Source id == live clone id with the prefix stripped.
                    var prefix = LiveClonePrefix;
                    if (trigger.Id?.StartsWith(prefix, StringComparison.Ordinal) == true)
                        _preset.Triggers?.RemoveAll(t => t.Id == trigger.Id.Substring(prefix.Length));
                }
            }
            else
            {
                _preset.Triggers?.RemoveAll(t => t.Id == trigger.Id);
            }

            PersistAndMaybeCreate();
            Changed = true;
            RebuildRows();
        }

        // ============================================================
        // Per-trigger border (header + action list)
        // ============================================================

        private Border BuildTriggerBorder(KeywordTrigger trigger, bool editable)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x3A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x48)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 4, 0, 4),
            };

            var stack = new StackPanel();
            border.Child = stack;

            // ---- Header row: enable checkbox + keyword (editable) + add-action + delete-trigger ----
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // enable
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // keyword
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // add action / chips
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // delete trigger

            var enableBox = new CheckBox
            {
                IsChecked = trigger.Enabled,
                IsEnabled = editable,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            ToolTip.SetTip(enableBox, "Toggle whether this keyword fires");
            // WPF's Checked + Unchecked pair; Avalonia has the one event.
            enableBox.IsCheckedChanged += (_, _) =>
            {
                trigger.Enabled = enableBox.IsChecked == true;
                if (editable) PersistAndMaybeCreate();
            };
            Grid.SetColumn(enableBox, 0);
            headerGrid.Children.Add(enableBox);

            if (editable)
            {
                // Editable keyword TextBox — LostFocus commits the change and also
                // mirrors to the custom preset's source list so uninstall/reinstall
                // preserves the user's word choice.
                var keywordBox = new TextBox
                {
                    Text = trigger.Keyword,
                    Foreground = Brushes.White,
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x32)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6, 3, 6, 3),
                    MinWidth = 140,
                };
                ToolTip.SetTip(keywordBox, "Word or phrase that fires this trigger. Edit freely.");
                keywordBox.LostFocus += (_, _) =>
                {
                    var newKeyword = (keywordBox.Text ?? "").Trim();
                    if (newKeyword == trigger.Keyword) return;
                    trigger.Keyword = newKeyword;
                    PersistAndMaybeCreate();
                };
                Grid.SetColumn(keywordBox, 1);
                headerGrid.Children.Add(keywordBox);

                var addBtn = new Button
                {
                    Content = "＋ Add action",
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(6, 0, 0, 0),
                    Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x44)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    FontSize = 11,
                };
                addBtn.Click += (_, _) => ShowAddActionMenu(addBtn, trigger, border);
                Grid.SetColumn(addBtn, 2);
                headerGrid.Children.Add(addBtn);

                var deleteTriggerBtn = new Button
                {
                    Content = "×",
                    Width = 26,
                    Height = 26,
                    Margin = new Thickness(6, 0, 0, 0),
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Color.FromRgb(0x40, 0x20, 0x20)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    FontSize = 14,
                };
                ToolTip.SetTip(deleteTriggerBtn, "Delete this trigger from the preset");
                deleteTriggerBtn.Click += async (_, _) =>
                {
                    var label = string.IsNullOrWhiteSpace(trigger.Keyword) ? "(unnamed trigger)" : $"\"{trigger.Keyword}\"";
                    var confirm = await MessageDialog.ConfirmAsync(this,
                        "Delete trigger",
                        $"Delete trigger {label}?\n\nThis removes the keyword and all its actions.");
                    if (confirm)
                        DeleteTrigger(trigger);
                };
                Grid.SetColumn(deleteTriggerBtn, 3);
                headerGrid.Children.Add(deleteTriggerBtn);
            }
            else
            {
                // Read-only preview (built-in, not installed).
                var keywordText = new TextBlock
                {
                    Text = trigger.Keyword,
                    Foreground = Brushes.White,
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(keywordText, 1);
                headerGrid.Children.Add(keywordText);

                var chipText = new TextBlock
                {
                    Text = BuildActionChips(trigger),
                    Foreground = this.TryFindResource("TextMutedBrush", out var muted) && muted is IBrush mutedBrush
                        ? mutedBrush
                        : new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0xA0)),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(chipText, 2);
                headerGrid.Children.Add(chipText);
            }

            stack.Children.Add(headerGrid);

            // ---- Action list ----
            if (trigger.Actions != null)
            {
                foreach (var action in trigger.Actions.ToList())
                {
                    var row = BuildActionRow(trigger, action, editable, border);
                    if (row != null) stack.Children.Add(row);
                }
            }

            return border;
        }

        /// <summary>
        /// Rebuilds just the action list of a single trigger's border after an
        /// edit/remove/add. Keeps scroll position and avoids a full dialog repaint.
        /// </summary>
        private void RebuildTriggerBorder(KeywordTrigger trigger, Border triggerBorder, bool editable)
        {
            if (triggerBorder.Child is not StackPanel stack) return;
            // Keep the header (index 0), drop the rest.
            while (stack.Children.Count > 1) stack.Children.RemoveAt(1);
            if (trigger.Actions != null)
            {
                foreach (var action in trigger.Actions.ToList())
                {
                    var row = BuildActionRow(trigger, action, editable, triggerBorder);
                    if (row != null) stack.Children.Add(row);
                }
            }
        }

        // ============================================================
        // Action row dispatch — picks the right inline editor per action type
        // ============================================================

        private Control? BuildActionRow(KeywordTrigger trigger, KeywordAction action, bool editable, Border parentBorder)
        {
            return action switch
            {
                PlayAudioAction pa      => BuildPlayAudioRow(trigger, pa, editable, parentBorder),
                VisualEffectAction ve   => BuildVisualEffectRow(trigger, ve, editable, parentBorder),
                HighlightAction hi      => BuildSimpleRow("👁", "Highlight matched words on screen", trigger, hi, editable, parentBorder),
                HapticAction h          => BuildHapticRow(trigger, h, editable, parentBorder),
                AvatarCommentAction ac  => BuildAvatarCommentRow(trigger, ac, editable, parentBorder),
                ExtendSessionAction es  => BuildExtendSessionRow(trigger, es, editable, parentBorder),
                ChasterAddTimeAction ct => BuildChasterAddTimeRow(trigger, ct, editable, parentBorder),
                // AddXpAction intentionally not rendered — XP awards are an
                // internal progression mechanic, not a user-configurable trigger
                // effect. Existing clones still dispatch AddXp via the service,
                // but the editor hides it and new presets don't ship it.
                _ => null,
            };
        }

        /// <summary>
        /// Standard wrapper for an action row: indent + outer grid with space for
        /// an icon on the left, the supplied editor in the middle, and a remove
        /// button on the right when editable.
        /// </summary>
        private Border ActionRowFrame(string icon, Control body, KeywordTrigger trigger, KeywordAction action, bool editable, Border parentBorder)
        {
            var rowBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x2E)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x48)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 5, 6, 5),
                Margin = new Thickness(24, 6, 0, 0),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // icon
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // body
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // remove

            var iconText = new TextBlock
            {
                Text = icon,
                FontSize = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(iconText, 0);
            grid.Children.Add(iconText);

            Grid.SetColumn(body, 1);
            grid.Children.Add(body);

            if (editable)
            {
                var removeBtn = new Button
                {
                    Content = "×",
                    Width = 22,
                    Height = 22,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Color.FromRgb(0x40, 0x20, 0x20)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    FontSize = 13,
                };
                ToolTip.SetTip(removeBtn, "Remove this action from the trigger");
                removeBtn.Click += (_, _) =>
                {
                    trigger.Actions?.Remove(action);
                    Persist();
                    RebuildTriggerBorder(trigger, parentBorder, editable);
                };
                Grid.SetColumn(removeBtn, 2);
                grid.Children.Add(removeBtn);
            }

            rowBorder.Child = grid;
            return rowBorder;
        }

        // ---- PlayAudio ----
        private Border BuildPlayAudioRow(KeywordTrigger trigger, PlayAudioAction pa, bool editable, Border parentBorder)
        {
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var fileText = new TextBlock
            {
                Text = DescribeAudioFile(pa.FilePath),
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(fileText, 0);
            body.Children.Add(fileText);

            var browseBtn = MakeChipButton("Browse", editable);
            browseBtn.Click += async (_, _) =>
            {
                // WPF's Microsoft.Win32.OpenFileDialog; the Avalonia picker is the native equivalent.
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select trigger sound",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Audio Files") { Patterns = new[] { "*.mp3", "*.wav", "*.ogg" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } },
                    },
                });
                var picked = files.FirstOrDefault();
                if (picked != null)
                {
                    pa.FilePath = picked.TryGetLocalPath() ?? picked.Path.ToString();
                    fileText.Text = DescribeAudioFile(pa.FilePath);
                    Persist();
                }
            };
            Grid.SetColumn(browseBtn, 1);
            body.Children.Add(browseBtn);

            var testBtn = MakeChipButton("▶ Test", editable);
            testBtn.Click += (_, _) => PreviewAudioClip(pa.FilePath, pa.Volume);
            Grid.SetColumn(testBtn, 2);
            body.Children.Add(testBtn);

            var volLabel = new TextBlock
            {
                Text = "Vol",
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0xA0)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 4, 0),
            };
            Grid.SetColumn(volLabel, 3);
            body.Children.Add(volLabel);

            var volSlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = pa.Volume,
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = editable,
                Margin = new Thickness(0, 0, 4, 0),
            };
            var volValueText = new TextBlock
            {
                Text = $"{pa.Volume}%",
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 30,
            };
            volSlider.ValueChanged += (_, _) =>
            {
                pa.Volume = (int)Math.Round(volSlider.Value);
                volValueText.Text = $"{pa.Volume}%";
                Persist();
            };
            Grid.SetColumn(volSlider, 4);
            body.Children.Add(volSlider);
            Grid.SetColumn(volValueText, 5);
            body.Children.Add(volValueText);

            return ActionRowFrame("🔊", body, trigger, pa, editable, parentBorder);
        }

        // ---- VisualEffect (dropdown of the variants) ----
        private Border BuildVisualEffectRow(KeywordTrigger trigger, VisualEffectAction ve, bool editable, Border parentBorder)
        {
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = "Effect:",
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            Grid.SetColumn(label, 0);
            body.Children.Add(label);

            // The WPF DialogDarkComboBox style was one FontSize setter over the implicit chrome.
            var combo = new ComboBox
            {
                FontSize = 11,
                MinWidth = 160,
                IsEnabled = editable,
                VerticalAlignment = VerticalAlignment.Center,
            };
            // Only offer user-fireable effects (skip None / HighlightOnly — the
            // dedicated HighlightAction covers that).
            var effectValues = new[]
            {
                KeywordVisualEffect.SubliminalFlash,
                KeywordVisualEffect.ExactSubliminal,
                KeywordVisualEffect.ImageFlash,
                KeywordVisualEffect.OverlayPulse,
                KeywordVisualEffect.MindWipe,
                KeywordVisualEffect.Bubbles,
            };
            foreach (var v in effectValues)
            {
                combo.Items.Add(new ComboBoxItem { Content = DescribeVisualEffect(v), Tag = v });
            }
            combo.SelectedIndex = Math.Max(0, Array.IndexOf(effectValues, ve.Effect));
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem item && item.Tag is KeywordVisualEffect eff)
                {
                    ve.Effect = eff;
                    Persist();
                }
            };
            Grid.SetColumn(combo, 1);
            body.Children.Add(combo);

            return ActionRowFrame(IconForVisualEffect(ve.Effect), body, trigger, ve, editable, parentBorder);
        }

        // ---- Haptic (intensity slider 0..1) ----
        private Border BuildHapticRow(KeywordTrigger trigger, HapticAction h, bool editable, Border parentBorder)
        {
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = "Intensity:",
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            Grid.SetColumn(label, 0);
            body.Children.Add(label);

            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 1,
                TickFrequency = 0.05,
                IsSnapToTickEnabled = true,
                Value = h.Intensity,
                IsEnabled = editable,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var valueText = new TextBlock
            {
                Text = $"{h.Intensity:F2}",
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 30,
                Margin = new Thickness(6, 0, 0, 0),
            };
            slider.ValueChanged += (_, _) =>
            {
                h.Intensity = slider.Value;
                valueText.Text = $"{h.Intensity:F2}";
                Persist();
            };
            Grid.SetColumn(slider, 1);
            body.Children.Add(slider);
            Grid.SetColumn(valueText, 2);
            body.Children.Add(valueText);

            return ActionRowFrame("💥", body, trigger, h, editable, parentBorder);
        }

        // ---- AvatarComment (example + prompt + fallback pool dropdown + AI checkbox) ----
        private Border BuildAvatarCommentRow(KeywordTrigger trigger, AvatarCommentAction ac, bool editable, Border parentBorder)
        {
            var body = new StackPanel { Orientation = Orientation.Vertical };

            // Row 0: italic example hint above the prompt textbox so users can see
            // what kind of string to write. Uses {keyword} placeholder syntax.
            var exampleText = new TextBlock
            {
                Text = "e.g. \"She just encountered the word '{keyword}'. Remind her she's locked, in character, one sentence.\"",
                Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x9A)),
                FontSize = 10,
                FontStyle = FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 3),
            };
            body.Children.Add(exampleText);

            // Row 1: prompt textbox (full width)
            var promptRow = new Grid();
            promptRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            promptRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var promptLabel = new TextBlock
            {
                Text = "Prompt:",
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            Grid.SetColumn(promptLabel, 0);
            promptRow.Children.Add(promptLabel);

            var promptBox = new TextBox
            {
                Text = ac.PromptTemplate ?? "",
                FontSize = 11,
                IsEnabled = editable,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x32)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A)),
                Padding = new Thickness(4, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(promptBox, "AI prompt template. Use {keyword} as a placeholder. Leave empty for the preset's default.");
            promptBox.LostFocus += (_, _) =>
            {
                ac.PromptTemplate = string.IsNullOrWhiteSpace(promptBox.Text) ? null : promptBox.Text;
                Persist();
                // P1.3 PromptValidator — soft validation on save. Always allow; on hit,
                // paint the textbox border + show inline warning + log to moderation.log.
                RunAwarenessPromptValidation(promptBox);
            };
            Grid.SetColumn(promptBox, 1);
            promptRow.Children.Add(promptBox);
            body.Children.Add(promptRow);

            // Row 2: fallback pool dropdown + "requires AI" checkbox
            var flagsRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            flagsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            flagsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            flagsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var fallbackLabel = new TextBlock
            {
                Text = "Fallback pool:",
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            Grid.SetColumn(fallbackLabel, 0);
            flagsRow.Children.Add(fallbackLabel);

            var fallbackCombo = new ComboBox
            {
                FontSize = 11,
                IsEnabled = editable,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 140,
            };
            ToolTip.SetTip(fallbackCombo, "Canned phrase pool used when AI is unavailable. Categories come from the active mod + any installed preset packs.");

            // "(none)" sentinel so the user can clear the fallback — skips canned
            // lines entirely when AI isn't available.
            fallbackCombo.Items.Add(new ComboBoxItem { Content = "(none)", Tag = null });

            var currentCat = ac.FallbackPhraseCategory ?? "";
            int selectedIndex = 0;
            int idx = 1;
            foreach (var cat in GetAvailableFallbackCategories())
            {
                fallbackCombo.Items.Add(new ComboBoxItem { Content = cat, Tag = cat });
                if (cat.Equals(currentCat, StringComparison.OrdinalIgnoreCase))
                    selectedIndex = idx;
                idx++;
            }

            // If the saved category isn't in the known list (e.g. preset uninstalled
            // but value remains) still show it so the user can see what's stored.
            if (selectedIndex == 0 && !string.IsNullOrEmpty(currentCat))
            {
                fallbackCombo.Items.Add(new ComboBoxItem
                {
                    Content = currentCat + "  (not available)",
                    Tag = currentCat,
                });
                selectedIndex = fallbackCombo.Items.Count - 1;
            }

            fallbackCombo.SelectedIndex = selectedIndex;
            fallbackCombo.SelectionChanged += (_, _) =>
            {
                if (fallbackCombo.SelectedItem is ComboBoxItem item)
                {
                    ac.FallbackPhraseCategory = item.Tag as string;
                    Persist();
                }
            };
            Grid.SetColumn(fallbackCombo, 1);
            flagsRow.Children.Add(fallbackCombo);

            var aiCheck = new CheckBox
            {
                Content = "Require AI",
                Foreground = Brushes.White,
                FontSize = 11,
                IsChecked = ac.RequireAiAvailable,
                IsEnabled = editable,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
            };
            ToolTip.SetTip(aiCheck, "When checked, this comment only fires if AI is available. Uncheck to always use canned phrases.");
            aiCheck.IsCheckedChanged += (_, _) =>
            {
                ac.RequireAiAvailable = aiCheck.IsChecked == true;
                Persist();
            };
            Grid.SetColumn(aiCheck, 2);
            flagsRow.Children.Add(aiCheck);

            body.Children.Add(flagsRow);

            return ActionRowFrame("💬", body, trigger, ac, editable, parentBorder);
        }

        /// <summary>
        /// Collects every phrase-pool category name the fallback dropdown can offer.
        /// On WPF, two sources: the built-in mod categories from
        /// <c>Services.CompanionPhraseService.GetCategoryNames()</c> and the distinct
        /// <c>Category</c> values in <c>settings.CustomCompanionPhrases</c> (which includes pools
        /// installed by preset packs). Both are sorted alphabetically and de-duped
        /// case-insensitively.
        /// </summary>
        private static IEnumerable<string> GetAvailableFallbackCategories()
        {
            // ponytail: the built-in half is CompanionPhraseEditorDialog's copy of
            // CompanionPhraseService.GetCategoryNames(); both collapse into that call when the
            // service moves to Core.
            var result = new List<string>(CompanionPhraseEditorDialog.CategoryNames);
            foreach (var c in CoreSettings.Current.CustomCompanionPhrases
                         .Select(c => c.Category)
                         .Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                if (!result.Contains(c, StringComparer.OrdinalIgnoreCase)) result.Add(c);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        // ---- ExtendSession (minutes) ----
        private Border BuildExtendSessionRow(KeywordTrigger trigger, ExtendSessionAction es, bool editable, Border parentBorder)
        {
            return BuildMinutesRow("⏱", "Extend session by:", es.Minutes, v => es.Minutes = v, trigger, es, editable, parentBorder);
        }

        // ---- ChasterAddTime (minutes) ----
        private Border BuildChasterAddTimeRow(KeywordTrigger trigger, ChasterAddTimeAction ct, bool editable, Border parentBorder)
        {
            return BuildMinutesRow("🔒", "Add lock time:", ct.Minutes, v => ct.Minutes = v, trigger, ct, editable, parentBorder);
        }

        private Border BuildMinutesRow(string icon, string label, int current, Action<int> setter, KeywordTrigger trigger, KeywordAction action, bool editable, Border parentBorder)
        {
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            Grid.SetColumn(lbl, 0);
            body.Children.Add(lbl);

            var tb = new TextBox
            {
                Text = current.ToString(),
                FontSize = 11,
                IsEnabled = editable,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x32)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A)),
                Padding = new Thickness(4, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            tb.LostFocus += (_, _) =>
            {
                if (int.TryParse(tb.Text, out var v))
                {
                    var clamped = Math.Clamp(v, 0, 1440);
                    setter(clamped);
                    tb.Text = clamped.ToString();
                    Persist();
                }
                else
                {
                    tb.Text = current.ToString();
                }
            };
            Grid.SetColumn(tb, 1);
            body.Children.Add(tb);

            var suffix = new TextBlock
            {
                Text = " min",
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0xA0)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(suffix, 2);
            body.Children.Add(suffix);

            return ActionRowFrame(icon, body, trigger, action, editable, parentBorder);
        }

        // ---- Simple (no params, just present) row ----
        private Border BuildSimpleRow(string icon, string description, KeywordTrigger trigger, KeywordAction action, bool editable, Border parentBorder)
        {
            var body = new TextBlock
            {
                Text = description,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return ActionRowFrame(icon, body, trigger, action, editable, parentBorder);
        }

        // ============================================================
        // Add-action menu (＋ button)
        // ============================================================

        private void ShowAddActionMenu(Button anchor, KeywordTrigger trigger, Border parentBorder)
        {
            var menu = new ContextMenu();
            var existing = new HashSet<string>();
            foreach (var a in trigger.Actions ?? new List<KeywordAction>())
            {
                if (a is VisualEffectAction ve) existing.Add("VisualEffect:" + ve.Effect);
                else existing.Add(a.GetType().Name);
            }

            AddMenuItem(menu, "🔊 Play Audio",              () => AddAction(trigger, new PlayAudioAction { Volume = 70 }, parentBorder),
                existing.Contains(nameof(PlayAudioAction)) ? "(already added)" : null);
            AddMenuItem(menu, "👁 Highlight matched words", () => AddAction(trigger, new HighlightAction(), parentBorder),
                existing.Contains(nameof(HighlightAction)) ? "(already added)" : null);
            AddMenuItem(menu, "💥 Haptic",                  () => AddAction(trigger, new HapticAction { Intensity = 0.3 }, parentBorder),
                existing.Contains(nameof(HapticAction)) ? "(already added)" : null);
            AddMenuItem(menu, "💬 Avatar Comment",          () => AddAction(trigger, new AvatarCommentAction(), parentBorder),
                existing.Contains(nameof(AvatarCommentAction)) ? "(already added)" : null);

            menu.Items.Add(new Separator());

            AddMenuItem(menu, "✨ Subliminal Flash",   () => AddAction(trigger, new VisualEffectAction { Effect = KeywordVisualEffect.SubliminalFlash }, parentBorder),
                existing.Contains("VisualEffect:" + KeywordVisualEffect.SubliminalFlash) ? "(already added)" : null);
            AddMenuItem(menu, "🔤 Exact Subliminal",   () => AddAction(trigger, new VisualEffectAction { Effect = KeywordVisualEffect.ExactSubliminal }, parentBorder),
                existing.Contains("VisualEffect:" + KeywordVisualEffect.ExactSubliminal) ? "(already added)" : null);
            AddMenuItem(menu, "⚡ Image Flash",         () => AddAction(trigger, new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash }, parentBorder),
                existing.Contains("VisualEffect:" + KeywordVisualEffect.ImageFlash) ? "(already added)" : null);
            AddMenuItem(menu, "🌫 Overlay Pulse",       () => AddAction(trigger, new VisualEffectAction { Effect = KeywordVisualEffect.OverlayPulse }, parentBorder),
                existing.Contains("VisualEffect:" + KeywordVisualEffect.OverlayPulse) ? "(already added)" : null);
            AddMenuItem(menu, "🧠 Mind Wipe",           () => AddAction(trigger, new VisualEffectAction { Effect = KeywordVisualEffect.MindWipe }, parentBorder),
                existing.Contains("VisualEffect:" + KeywordVisualEffect.MindWipe) ? "(already added)" : null);
            AddMenuItem(menu, "🫧 Bubbles",             () => AddAction(trigger, new VisualEffectAction { Effect = KeywordVisualEffect.Bubbles }, parentBorder),
                existing.Contains("VisualEffect:" + KeywordVisualEffect.Bubbles) ? "(already added)" : null);

            menu.PlacementTarget = anchor;
            menu.Open(anchor);
        }

        private void AddMenuItem(ContextMenu menu, string header, Action onClick, string? disabledReason = null)
        {
            // Avalonia's MenuItem.InputGesture is a KeyGesture, not free text, so the WPF
            // InputGestureText note is appended to the header instead.
            var item = new MenuItem { Header = disabledReason == null ? header : $"{header}   {disabledReason}" };
            if (disabledReason != null)
            {
                item.IsEnabled = false;
            }
            else
            {
                item.Click += (_, _) => onClick();
            }
            menu.Items.Add(item);
        }

        private void AddAction(KeywordTrigger trigger, KeywordAction newAction, Border parentBorder)
        {
            trigger.Actions ??= new List<KeywordAction>();
            trigger.Actions.Add(newAction);
            Persist();
            RebuildTriggerBorder(trigger, parentBorder, editable: true);
        }

        // ============================================================
        // Install / Clone / Close handlers
        // ============================================================

        private void BtnInstall_Click()
        {
            // New custom preset must be registered before it can be installed, otherwise
            // KeywordTriggerPresetService.GetPreset returns null. Persist now so the
            // preset lives in settings.KeywordTriggerPresets.
            if (_isCustomPresetUnsaved)
                PersistAndMaybeCreate();

            if (IsInstalled())
            {
                // For custom presets: capture any in-place edits to the live clones back into the
                // source list before uninstall wipes the clones, so a later re-install keeps the
                // user's tuning.
                if (!_preset.IsBuiltIn)
                    MirrorLiveClonesToCustomSource();
                Presets.UninstallPreset(_preset.Id);
            }
            else
            {
                Presets.InstallPreset(_preset.Id);
            }

            Changed = true;
            RebuildRows();
            UpdateInstallButton();
        }

        private async Task BtnClone_Click()
        {
            var copy = Presets.CloneToCustom(_preset.Id);
            Changed = true;
            if (copy == null)
            {
                await MessageDialog.ShowAsync(this, "Copy failed", "Couldn't copy this preset.");
                return;
            }

            // Close this preview and open the fresh editable copy so the user lands straight in the
            // new preset (which is also now a card in the grid). Owner is captured BEFORE Close():
            // Avalonia clears it on close, and ShowDialog needs a live owner.
            var owner = Owner as Window;
            Close();
            var dlg = new AwarenessPresetDetailDialog(copy);
            if (owner != null) await dlg.ShowDialog(owner);
            else dlg.Show();
        }

        /// <summary>
        /// Delete a user-created preset entirely. Only offered for non-built-in
        /// presets — built-ins would just reappear on next app launch via
        /// <c>Services.SettingsService.MergeBuiltInAwarenessPresets</c>.
        /// Uninstalls first (to clean up live clones + injected phrases) and then
        /// removes the preset entry itself.
        /// </summary>
        private async Task BtnDeletePreset_Click()
        {
            if (_preset.IsBuiltIn)
            {
                // Guard rail — the button shouldn't even be visible for built-ins.
                return;
            }

            var label = string.IsNullOrWhiteSpace(_preset.Name) ? "this preset" : $"\"{_preset.Name}\"";
            var confirm = await MessageDialog.ConfirmAsync(this,
                "Delete preset",
                $"Delete {label}?\n\nThis removes the preset and all its triggers permanently.");
            if (!confirm) return;

            // Uninstall first so cloned triggers / canned phrases are cleaned up.
            if (IsInstalled())
                Presets.UninstallPreset(_preset.Id);

            CoreSettings.Current.KeywordTriggerPresets?.RemoveAll(p => p.Id == _preset.Id);
            Persist();

            Changed = true;
            Close();
        }

        // ============================================================
        // Helpers
        // ============================================================

        /// <summary>
        /// P1.3 — runs the prompt validator over a single avatar-comment prompt
        /// textbox on LostFocus (which is when this dialog persists the field).
        /// On hit: paint border yellow, set tooltip, log to moderation.log.
        /// Save is never blocked.
        /// </summary>
        private void RunAwarenessPromptValidation(TextBox promptBox)
        {
            if (promptBox == null) return;

            var result = new PromptValidator().Validate(promptBox.Text ?? string.Empty);
            if (result.Clean)
            {
                promptBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A));
                promptBox.BorderThickness = new Thickness(1);
                promptBox.ClearValue(ToolTip.TipProperty);
                return;
            }

            promptBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xC7, 0x6B));
            promptBox.BorderThickness = new Thickness(2);
            ToolTip.SetTip(promptBox, Loc.GetF("prompt_validator_warning", result.MatchedPatterns.Count));

            // Through the seam, so the app's ONE moderation log takes the entry. Unseeded on this
            // head, so nothing is written here yet.
            CoreModerationLog.RecordEdit("avatarPromptTemplate", result.MatchedPatterns.Count, "awareness_preset");
            Log.Information(
                "PromptValidator flagged AwarenessPresetDetailDialog avatar prompt ({Count} matches)",
                result.MatchedPatterns.Count);
        }

        /// <summary>
        /// WPF's <c>App.KeywordTriggers.PreviewAudioClip</c>: audition a clip a user is about to
        /// assign to a trigger, resolving relative preset paths the way dispatch does.
        ///
        /// ponytail: the resolve is KeywordTriggerService.ResolveAudioPath verbatim; both collapse
        /// into that call when the keyword-trigger service moves to Core. WPF's NAudio path also
        /// applied a pow(1.5) master-volume curve, which lives inside the audio service the seam
        /// calls, so playback here is the seam's own curve, not that one.
        /// </summary>
        private static void PreviewAudioClip(string? path, int volumePercent)
        {
            if (string.IsNullOrEmpty(path)) return;

            var resolved = path;
            if (!Path.IsPathRooted(path))
            {
                // Preset-bundled audio under Resources/ first (install dir, then content pack),
                // then the legacy sub_audio folder.
                var res = ContentLocator.Resolve(Path.Combine("Resources", path));
                var sub = ContentLocator.Resolve(Path.Combine("Resources", "sub_audio", path));
                resolved = File.Exists(res) ? res : File.Exists(sub) ? sub : path;
            }

            if (!File.Exists(resolved)) return;
            CoreAudio.PlayOneShot(resolved, volumePercent / 100f, "keyword-preview");
        }

        private static Button MakeChipButton(string label, bool editable)
        {
            return new Button
            {
                Content = label,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 4, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x44)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 11,
                IsEnabled = editable,
            };
        }

        private static string DescribeAudioFile(string? path)
        {
            if (string.IsNullOrEmpty(path)) return "(no file)";
            try { return Path.GetFileName(path) ?? path; }
            catch { return path; }
        }

        private static string DescribeVisualEffect(KeywordVisualEffect e) => e switch
        {
            KeywordVisualEffect.SubliminalFlash => "Subliminal Flash (random pool word)",
            KeywordVisualEffect.ExactSubliminal => "Exact Subliminal (flash the keyword itself)",
            KeywordVisualEffect.ImageFlash      => "Image Flash (burst image)",
            KeywordVisualEffect.OverlayPulse    => "Overlay Pulse",
            KeywordVisualEffect.MindWipe        => "Mind Wipe",
            KeywordVisualEffect.Bubbles         => "Bubbles (spawn once)",
            _ => e.ToString(),
        };

        private static string IconForVisualEffect(KeywordVisualEffect e) => e switch
        {
            KeywordVisualEffect.SubliminalFlash => "✨",
            KeywordVisualEffect.ExactSubliminal => "🔤",
            KeywordVisualEffect.ImageFlash      => "⚡",
            KeywordVisualEffect.OverlayPulse    => "🌫",
            KeywordVisualEffect.MindWipe        => "🧠",
            KeywordVisualEffect.Bubbles         => "🫧",
            _ => "✨",
        };

        internal static string BuildActionChips(KeywordTrigger trigger)
        {
            if (trigger.Actions == null || trigger.Actions.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var a in trigger.Actions)
            {
                if (a == null) continue;
                switch (a)
                {
                    case PlayAudioAction:     sb.Append("🔊 "); break;
                    case VisualEffectAction v: sb.Append(IconForVisualEffect(v.Effect)).Append(' '); break;
                    case HighlightAction:     sb.Append("👁 "); break;
                    case HapticAction:        sb.Append("💥 "); break;
                    case AvatarCommentAction: sb.Append("💬 "); break;
                    case ExtendSessionAction: sb.Append("⏱ "); break;
                    case ChasterAddTimeAction: sb.Append("🔒 "); break;
                    // AddXpAction intentionally NOT shown — progression XP is not
                    // a user-facing trigger effect.
                }
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Sample data for the render constructor: a custom (editable) preset carrying one trigger
        /// per action editor, so the PNG shows every inline row this dialog can draw.
        /// </summary>
        private static KeywordTriggerPreset SamplePreset() => new()
        {
            Id = "sample.preview",
            Name = "Sample preset",
            Icon = "🎯",
            Author = "Preview",
            Description = "Placeholder preset used only by --render-view / --render-all.",
            LongDescription = "Placeholder preset used only by --render-view / --render-all. It carries one trigger per inline action editor so the render proves each one draws.",
            IsBuiltIn = false,
            RequiresAi = true,
            Triggers = new List<KeywordTrigger>
            {
                new()
                {
                    Id = "sample1",
                    Keyword = "good girl",
                    Enabled = true,
                    Actions = new List<KeywordAction>
                    {
                        new PlayAudioAction { FilePath = "/home/sample/chime.mp3", Volume = 70 },
                        new VisualEffectAction { Effect = KeywordVisualEffect.SubliminalFlash },
                        new HighlightAction(),
                    },
                },
                new()
                {
                    Id = "sample2",
                    Keyword = "locked",
                    Enabled = false,
                    Actions = new List<KeywordAction>
                    {
                        new HapticAction { Intensity = 0.35 },
                        new AvatarCommentAction { PromptTemplate = "React to '{keyword}' in one sentence.", FallbackPhraseCategory = "ChastityShame" },
                        new ExtendSessionAction { Minutes = 10 },
                        new ChasterAddTimeAction { Minutes = 30 },
                    },
                },
            },
        };
    }
}
