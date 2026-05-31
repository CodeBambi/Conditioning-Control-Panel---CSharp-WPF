using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Modal view of an Awareness Engine preset pack with full inline action editing.
    /// States:
    ///   * Not installed — read-only preview of the preset's bundled triggers,
    ///     Install + Clone-to-custom buttons.
    ///   * Installed — each trigger shows an editable action list. Users can
    ///     add/remove any action type, tune per-action parameters (audio file,
    ///     visual effect, haptic intensity, XP amount, avatar prompt, etc.),
    ///     and every edit persists to <see cref="AppSettings.KeywordTriggers"/>.
    ///
    /// Rows are built imperatively in <see cref="BuildTriggerBorder"/> rather
    /// than via XAML DataTemplates — eight action types × several controls each
    /// would need a matching DataTemplate hierarchy, and building them in code
    /// keeps the logic (event handlers, validation, enable-state) in one place.
    /// </summary>
    public partial class AwarenessPresetDetailDialog : Window
    {
        private readonly KeywordTriggerPreset _preset;

        /// <summary>
        /// True until the first successful save of a brand-new user-created preset.
        /// While set, persist-hooks add the preset to <c>settings.KeywordTriggerPresets</c>
        /// on first write so the card shows up in the Awareness grid.
        /// </summary>
        private bool _isCustomPresetUnsaved;

        /// <summary>True if install/uninstall state changed — caller should refresh.</summary>
        public bool Changed { get; private set; }

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
            InitializeComponent();
            _preset = preset;
            _isCustomPresetUnsaved = isNewCustomPreset;

            if (preset.IsBuiltIn)
            {
                // Built-in presets: static header. Name/icon/description are fixed and
                // refreshed from the JSON on version bumps, so they aren't editable.
                TxtIcon.Text = preset.Icon;
                TxtName.Text = preset.Name;
                TxtAuthor.Text = preset.Author;
                TxtDescription.Text = string.IsNullOrEmpty(preset.LongDescription)
                    ? preset.Description
                    : preset.LongDescription;
            }
            else
            {
                // Custom presets: editable name/icon/description. Wire LostFocus so
                // every edit both mutates the preset AND persists (which also creates
                // the preset in settings the first time around).
                TxtIcon.Visibility = Visibility.Collapsed;
                NameReadPanel.Visibility = Visibility.Collapsed;
                TxtDescription.Visibility = Visibility.Collapsed;

                TxtIconEdit.Visibility = Visibility.Visible;
                NameEditPanel.Visibility = Visibility.Visible;
                TxtDescriptionEdit.Visibility = Visibility.Visible;

                TxtIconEdit.Text = preset.Icon;
                TxtNameEdit.Text = preset.Name;
                TxtDescriptionEdit.Text = string.IsNullOrEmpty(preset.LongDescription)
                    ? preset.Description
                    : preset.LongDescription;

                TxtIconEdit.LostFocus += (_, _) =>
                {
                    preset.Icon = (TxtIconEdit.Text ?? "").Trim();
                    PersistAndMaybeCreate();
                };
                TxtNameEdit.LostFocus += (_, _) =>
                {
                    var name = (TxtNameEdit.Text ?? "").Trim();
                    preset.Name = string.IsNullOrEmpty(name) ? "Untitled preset" : name;
                    TxtNameEdit.Text = preset.Name;
                    PersistAndMaybeCreate();
                };
                TxtDescriptionEdit.LostFocus += (_, _) =>
                {
                    var text = (TxtDescriptionEdit.Text ?? "").Trim();
                    preset.Description = text;
                    preset.LongDescription = text;
                    PersistAndMaybeCreate();
                };
            }

            if (preset.RequiresAi)
                BrdAiBadge.Visibility = Visibility.Visible;

            ApplyPolicyBannerState();
            RebuildRows();
            UpdateInstallButton();
        }

        /// <summary>
        /// CCBill AI Addendum: show full content-policy banner until acked, then slim version.
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
                App.Logger?.Warning(ex, "AwarenessPresetDetailDialog: failed to open policy URL");
            }
        }

        /// <summary>
        /// Persists settings AND, if this dialog is editing a brand-new user-created
        /// preset, registers it in <c>settings.KeywordTriggerPresets</c> on first write.
        /// </summary>
        private void PersistAndMaybeCreate()
        {
            if (_isCustomPresetUnsaved)
            {
                var list = App.Settings?.Current?.KeywordTriggerPresets;
                if (list != null && !list.Any(p => p.Id == _preset.Id))
                    list.Add(_preset);
                _isCustomPresetUnsaved = false;
                Changed = true;
            }
            App.Settings?.Save();
        }

        /// <summary>
        /// True when the trigger list (and "+ Add trigger" button, per-trigger delete,
        /// keyword edit) should be usable. Installed presets are always editable;
        /// user-authored presets are editable even when uninstalled so they can be
        /// authored before being turned on.
        /// </summary>
        private bool IsEditable()
        {
            if (App.KeywordPresets?.IsInstalled(_preset.Id) == true) return true;
            if (!_preset.IsBuiltIn) return true;
            return false;
        }

        /// <summary>
        /// For user-created presets that are currently installed, mirror the live
        /// cloned triggers in <c>settings.KeywordTriggers</c> back into
        /// <c>preset.Triggers</c>. Keeps the preset source-of-truth in sync so a later
        /// Uninstall → Install cycle preserves user edits. No-op for built-ins (their
        /// source is owned by the JSON in <c>Resources/AwarenessPresets/</c>).
        /// </summary>
        private void MirrorLiveClonesToCustomSource()
        {
            if (_preset.IsBuiltIn) return;
            var installed = App.KeywordPresets?.IsInstalled(_preset.Id) == true;
            if (!installed) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            var prefix = "preset:" + _preset.Id + ":";
            var live = settings.KeywordTriggers
                .Where(t => t?.Id?.StartsWith(prefix, StringComparison.Ordinal) == true)
                .ToList();

            var synced = new List<KeywordTrigger>();
            foreach (var clone in live)
            {
                var copy = clone.Clone();
                copy.Id = clone.Id!.Substring(prefix.Length);
                copy.LastTriggeredAt = DateTime.MinValue;
                synced.Add(copy);
            }
            _preset.Triggers = synced;
        }

        // ============================================================
        // Top-level list build
        // ============================================================

        private void RebuildRows()
        {
            TriggerStack.Children.Clear();

            var installed = App.KeywordPresets?.IsInstalled(_preset.Id) == true;
            var editable = IsEditable();
            var settings = App.Settings?.Current;
            List<KeywordTrigger> triggers;

            if (installed && settings != null)
            {
                // Edit the live cloned triggers from settings so every mutation
                // flows back via App.Settings.Save().
                var prefix = "preset:" + _preset.Id + ":";
                triggers = settings.KeywordTriggers
                    .Where(t => t?.Id?.StartsWith(prefix, StringComparison.Ordinal) == true)
                    .ToList();
            }
            else
            {
                // Uninstalled: edit preset.Triggers directly (custom presets) or
                // show read-only preview (built-ins).
                triggers = _preset.Triggers?.Where(t => t != null).ToList() ?? new List<KeywordTrigger>();
            }

            foreach (var trigger in triggers)
                TriggerStack.Children.Add(BuildTriggerBorder(trigger, editable));

            // Inline "+ Add trigger" button at the bottom of the editable list.
            if (editable)
                TriggerStack.Children.Add(BuildAddTriggerRow());
            else if (triggers.Count == 0)
                TriggerStack.Children.Add(BuildEmptyStateNotice());

            // Clone-to-custom only helps for built-in previews (dumps defaults
            // into the Exclusives editor for free tweaking). Custom presets have
            // a Delete button instead.
            BtnClone.Visibility = (_preset.IsBuiltIn && !installed) ? Visibility.Visible : Visibility.Collapsed;
            BtnDeletePreset.Visibility = !_preset.IsBuiltIn ? Visibility.Visible : Visibility.Collapsed;
            TxtFooterNote.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateInstallButton()
        {
            if (App.KeywordPresets?.IsInstalled(_preset.Id) == true)
            {
                BtnInstall.Content = "Uninstall";
                BtnInstall.Background = new SolidColorBrush(Color.FromRgb(0x5A, 0x30, 0x30));
            }
            else
            {
                BtnInstall.Content = "Install";
                BtnInstall.Background = (Brush)FindResource("PinkBrush");
            }
        }

        private FrameworkElement BuildAddTriggerRow()
        {
            var btn = new Button
            {
                Content = "＋ Add trigger",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 10, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x44)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
                FontSize = 11,
                ToolTip = "Append a fresh keyword trigger. Edit the keyword field to name it, then Add actions to define what fires.",
            };
            btn.Click += (_, _) => AddNewTrigger();
            return btn;
        }

        private static FrameworkElement BuildEmptyStateNotice()
        {
            return new TextBlock
            {
                Text = "No triggers defined for this preset.",
                Foreground = (Brush)new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0xA0)),
                FontStyle = FontStyles.Italic,
                FontSize = 11,
                Margin = new Thickness(4, 8, 0, 4),
            };
        }

        /// <summary>
        /// Append a new blank <see cref="KeywordTrigger"/> to the correct list
        /// (live clones for installed presets, source list for uninstalled custom
        /// presets) and refresh the dialog so its row appears.
        /// </summary>
        private void AddNewTrigger()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

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

            var installed = App.KeywordPresets?.IsInstalled(_preset.Id) == true;
            if (installed)
            {
                // Live clone list uses the "preset:<presetId>:<sourceId>" id convention.
                var prefix = "preset:" + _preset.Id + ":";
                var sourceId = Guid.NewGuid().ToString("N")[..8];
                newTrigger.Id = prefix + sourceId;
                settings.KeywordTriggers.Add(newTrigger);

                // For custom presets, also add a matching source entry so
                // uninstall→reinstall preserves this addition.
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
                // Uninstalled custom preset: edit the source list directly.
                _preset.Triggers ??= new List<KeywordTrigger>();
                _preset.Triggers.Add(newTrigger);
            }

            PersistAndMaybeCreate();
            Changed = true;
            RebuildRows();
        }

        /// <summary>
        /// Remove a trigger row entirely. For installed presets this removes the
        /// live clone from settings; for custom presets we also strip the matching
        /// source entry so the trigger doesn't come back on Uninstall→Install.
        /// </summary>
        private void DeleteTrigger(KeywordTrigger trigger)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var installed = App.KeywordPresets?.IsInstalled(_preset.Id) == true;
            if (installed)
            {
                settings.KeywordTriggers.RemoveAll(t => t.Id == trigger.Id);

                if (!_preset.IsBuiltIn)
                {
                    // Source id == live clone id with prefix stripped.
                    var prefix = "preset:" + _preset.Id + ":";
                    if (trigger.Id?.StartsWith(prefix, StringComparison.Ordinal) == true)
                    {
                        var sourceId = trigger.Id.Substring(prefix.Length);
                        _preset.Triggers?.RemoveAll(t => t.Id == sourceId);
                    }
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
                ToolTip = "Toggle whether this keyword fires",
            };
            enableBox.Checked += (_, _) => { trigger.Enabled = true; if (editable) PersistAndMaybeCreate(); };
            enableBox.Unchecked += (_, _) => { trigger.Enabled = false; if (editable) PersistAndMaybeCreate(); };
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
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x32)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6, 3, 6, 3),
                    MinWidth = 140,
                    ToolTip = "Word or phrase that fires this trigger. Edit freely.",
                };
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
                    Cursor = Cursors.Hand,
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
                    Cursor = Cursors.Hand,
                    FontSize = 14,
                    ToolTip = "Delete this trigger from the preset",
                };
                deleteTriggerBtn.Click += (_, _) =>
                {
                    var label = string.IsNullOrWhiteSpace(trigger.Keyword) ? "(unnamed trigger)" : $"\"{trigger.Keyword}\"";
                    var confirm = MessageBox.Show(
                        $"Delete trigger {label}?\n\nThis removes the keyword and all its actions.",
                        "Delete trigger",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (confirm == MessageBoxResult.Yes)
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
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(keywordText, 1);
                headerGrid.Children.Add(keywordText);

                var chipText = new TextBlock
                {
                    Text = BuildActionChips(trigger),
                    Foreground = (Brush)FindResource("TextMutedBrush"),
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

        private FrameworkElement? BuildActionRow(KeywordTrigger trigger, KeywordAction action, bool editable, Border parentBorder)
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
        private Border ActionRowFrame(string icon, UIElement body, KeywordTrigger trigger, KeywordAction action, bool editable, Border parentBorder)
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

            if (body is FrameworkElement fe)
            {
                Grid.SetColumn(fe, 1);
                grid.Children.Add(fe);
            }

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
                    Cursor = Cursors.Hand,
                    FontSize = 13,
                    ToolTip = "Remove this action from the trigger",
                };
                removeBtn.Click += (_, _) =>
                {
                    trigger.Actions?.Remove(action);
                    App.Settings?.Save();
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
            browseBtn.Click += (_, _) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Audio Files|*.mp3;*.wav;*.ogg|All Files|*.*",
                    Title = "Select trigger sound"
                };
                if (dlg.ShowDialog() == true)
                {
                    pa.FilePath = dlg.FileName;
                    fileText.Text = DescribeAudioFile(pa.FilePath);
                    App.Settings?.Save();
                }
            };
            Grid.SetColumn(browseBtn, 1);
            body.Children.Add(browseBtn);

            var testBtn = MakeChipButton("▶ Test", editable);
            testBtn.Click += (_, _) =>
            {
                if (!string.IsNullOrEmpty(pa.FilePath))
                    App.KeywordTriggers?.PreviewAudioClip(pa.FilePath, pa.Volume);
            };
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
                App.Settings?.Save();
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

            var combo = new ComboBox
            {
                Style = (Style)FindResource("DialogDarkComboBox"),
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
                    App.Settings?.Save();
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
                App.Settings?.Save();
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
                FontStyle = FontStyles.Italic,
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
                ToolTip = "AI prompt template. Use {keyword} as a placeholder. Leave empty for the preset's default.",
            };
            promptBox.LostFocus += (_, _) =>
            {
                ac.PromptTemplate = string.IsNullOrWhiteSpace(promptBox.Text) ? null : promptBox.Text;
                App.Settings?.Save();
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
                Style = (Style)FindResource("DialogDarkComboBox"),
                IsEnabled = editable,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 140,
                ToolTip = "Canned phrase pool used when AI is unavailable. Categories come from the active mod + any installed preset packs.",
            };

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
                    App.Settings?.Save();
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
                ToolTip = "When checked, this comment only fires if AI is available. Uncheck to always use canned phrases.",
            };
            aiCheck.Checked += (_, _) => { ac.RequireAiAvailable = true; App.Settings?.Save(); };
            aiCheck.Unchecked += (_, _) => { ac.RequireAiAvailable = false; App.Settings?.Save(); };
            Grid.SetColumn(aiCheck, 2);
            flagsRow.Children.Add(aiCheck);

            body.Children.Add(flagsRow);

            return ActionRowFrame("💬", body, trigger, ac, editable, parentBorder);
        }

        /// <summary>
        /// Collects every phrase-pool category name the fallback dropdown can offer.
        /// Two sources:
        ///   1. Built-in mod categories (<see cref="Services.CompanionPhraseService.GetCategoryNames"/>)
        ///   2. Distinct <c>Category</c> values from <c>settings.CustomCompanionPhrases</c>,
        ///      which includes pools installed by preset packs (PuppyPraise, ChastityShame,
        ///      BimboGiggle, TranceMurmur, TestLab, etc.).
        /// Result is sorted alphabetically and de-duped case-insensitively.
        /// </summary>
        private static IEnumerable<string> GetAvailableFallbackCategories()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();

            try
            {
                foreach (var cat in Services.CompanionPhraseService.GetCategoryNames())
                {
                    if (!string.IsNullOrEmpty(cat) && seen.Add(cat))
                        result.Add(cat);
                }
            }
            catch { }

            try
            {
                var custom = App.Settings?.Current?.CustomCompanionPhrases;
                if (custom != null)
                {
                    foreach (var p in custom)
                    {
                        if (!string.IsNullOrEmpty(p?.Category) && seen.Add(p.Category))
                            result.Add(p.Category);
                    }
                }
            }
            catch { }

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
                    App.Settings?.Save();
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
            menu.IsOpen = true;
        }

        private void AddMenuItem(ContextMenu menu, string header, Action onClick, string? disabledReason = null)
        {
            var item = new MenuItem { Header = header };
            if (disabledReason != null)
            {
                item.IsEnabled = false;
                item.InputGestureText = disabledReason;
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
            App.Settings?.Save();
            RebuildTriggerBorder(trigger, parentBorder, editable: true);
        }

        // ============================================================
        // Install / Clone / Close handlers (unchanged)
        // ============================================================

        private void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            if (App.KeywordPresets == null) { Close(); return; }

            // New custom preset must be registered before it can be installed, otherwise
            // KeywordTriggerPresetService.GetPreset returns null. Persist now so the
            // preset lives in settings.KeywordTriggerPresets.
            if (_isCustomPresetUnsaved)
                PersistAndMaybeCreate();

            if (App.KeywordPresets.IsInstalled(_preset.Id))
            {
                // For custom presets: capture any in-place edits to the live clones
                // back into the source list before uninstall wipes the clones. That
                // way a later re-install keeps the user's tuning.
                if (!_preset.IsBuiltIn)
                    MirrorLiveClonesToCustomSource();
                App.KeywordPresets.UninstallPreset(_preset.Id);
            }
            else
            {
                App.KeywordPresets.InstallPreset(_preset.Id);
            }

            Changed = true;
            RebuildRows();
            UpdateInstallButton();
        }

        private void BtnClone_Click(object sender, RoutedEventArgs e)
        {
            if (App.KeywordPresets == null) { Close(); return; }
            var added = App.KeywordPresets.CloneToCustom(_preset.Id);
            Changed = true;
            MessageBox.Show($"Cloned {added} trigger(s) into your custom list.", "Cloned",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Delete a user-created preset entirely. Only offered for non-built-in
        /// presets — built-ins would just reappear on next app launch via
        /// <see cref="Services.SettingsService.MergeBuiltInAwarenessPresets"/>.
        /// Uninstalls first (to clean up live clones + injected phrases) and then
        /// removes the preset entry itself.
        /// </summary>
        private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (_preset.IsBuiltIn)
            {
                // Guard rail — the button shouldn't even be visible for built-ins.
                return;
            }

            var label = string.IsNullOrWhiteSpace(_preset.Name) ? "this preset" : $"\"{_preset.Name}\"";
            var confirm = MessageBox.Show(
                $"Delete {label}?\n\nThis removes the preset and all its triggers permanently.",
                "Delete preset",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            // Uninstall first so cloned triggers / canned phrases are cleaned up.
            if (App.KeywordPresets?.IsInstalled(_preset.Id) == true)
                App.KeywordPresets.UninstallPreset(_preset.Id);

            var list = App.Settings?.Current?.KeywordTriggerPresets;
            if (list != null)
                list.RemoveAll(p => p.Id == _preset.Id);
            App.Settings?.Save();

            Changed = true;
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
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
            var validator = App.PromptValidator;
            if (validator == null || promptBox == null) return;

            var text = promptBox.Text ?? string.Empty;
            var result = validator.Validate(text);
            if (result.Clean)
            {
                promptBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A));
                promptBox.BorderThickness = new Thickness(1);
                promptBox.ClearValue(TextBox.ToolTipProperty);
                return;
            }

            promptBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xC7, 0x6B));
            promptBox.BorderThickness = new Thickness(2);
            promptBox.ToolTip = string.Format(
                Localization.Loc.Get("prompt_validator_warning"),
                result.MatchedPatterns.Count);

            App.ModerationLog?.RecordEdit(
                "avatarPromptTemplate",
                result.MatchedPatterns.Count,
                "awareness_preset");
            App.Logger?.Information(
                "PromptValidator flagged AwarenessPresetDetailDialog avatar prompt ({Count} matches)",
                result.MatchedPatterns.Count);
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
                Cursor = Cursors.Hand,
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
    }
}
