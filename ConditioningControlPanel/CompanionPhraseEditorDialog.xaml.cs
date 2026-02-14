using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Models;
using Microsoft.Win32;

namespace ConditioningControlPanel
{
    public partial class CompanionPhraseEditorDialog : Window
    {
        private readonly List<CompanionPhrase> _allPhrases = new();
        private readonly HashSet<string> _selectedIds = new();
        private string _currentFilter = "All Categories";

        private static readonly Dictionary<string, string> _categoryDisplayNames = new()
        {
            { "Greeting", "Greeting" },
            { "StartupGreeting", "Startup Greeting" },
            { "Idle", "Idle" },
            { "RandomFloating", "Random Floating" },
            { "Generic", "Generic" },
            { "Gaming", "Gaming" },
            { "Browsing", "Browsing" },
            { "Shopping", "Shopping" },
            { "Social", "Social Media" },
            { "Discord", "Discord" },
            { "TrainingSite", "Training Site" },
            { "HypnoContent", "Hypno Content" },
            { "Working", "Working" },
            { "Media", "Media" },
            { "Learning", "Learning" },
            { "WindowAwarenessIdle", "Idle Detection" },
            { "EngineStop", "Engine Stop" },
            { "FlashPre", "Flash (Pre)" },
            { "SubliminalAck", "Subliminal Reaction" },
            { "RandomBubble", "Random Bubble" },
            { "BubbleCountMercy", "Bubble Count Mercy" },
            { "BubblePop", "Bubble Pop" },
            { "GameFailed", "Game Failed" },
            { "BubbleMissed", "Bubble Missed" },
            { "FlashClicked", "Flash Clicked" },
            { "LevelUp", "Level Up" },
            { "MindWipe", "Mind Wipe" },
            { "BrainDrain", "Brain Drain" },
            { "VoiceLine", "Voice Line" },
            { "Custom", "Custom (General)" },
        };

        private static string GetDisplayName(string category) =>
            _categoryDisplayNames.TryGetValue(category, out var dn) ? dn : category;

        public CompanionPhraseEditorDialog()
        {
            InitializeComponent();
            PopulateCategoryFilter();
            RefreshPhraseList();
        }

        private ContentMode CurrentMode =>
            App.Settings?.Current?.ContentMode ?? ContentMode.BambiSleep;

        private void PopulateCategoryFilter()
        {
            CmbCategoryFilter.Items.Add(new ComboBoxItem { Content = "All Categories", Tag = "All Categories" });
            foreach (var cat in Services.CompanionPhraseService.GetCategoryNames())
                CmbCategoryFilter.Items.Add(new ComboBoxItem { Content = GetDisplayName(cat), Tag = cat });
            CmbCategoryFilter.Items.Add(new ComboBoxItem { Content = GetDisplayName("Custom"), Tag = "Custom" });
            CmbCategoryFilter.SelectedIndex = 0;
        }

        private void RefreshPhraseList()
        {
            PhraseListPanel.Children.Clear();
            _allPhrases.Clear();

            var allPhrases = App.CompanionPhrases?.GetAllPhrases(CurrentMode) ?? new List<CompanionPhrase>();
            // Restore selection state from tracked IDs
            foreach (var p in allPhrases)
                p.IsSelected = _selectedIds.Contains(p.Id);
            _allPhrases.AddRange(allPhrases);

            // Group by category
            var grouped = _currentFilter == "All Categories"
                ? allPhrases.GroupBy(p => p.Category)
                : allPhrases.Where(p => p.Category == _currentFilter).GroupBy(p => p.Category);

            foreach (var group in grouped)
            {
                var enabledCount = group.Count(p => p.IsEnabled);
                var totalCount = group.Count();

                // Category header
                var headerBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x3C)),
                    CornerRadius = new CornerRadius(6, 6, 0, 0),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 8, 0, 0)
                };

                var headerGrid = new Grid();
                var headerText = new TextBlock
                {
                    Text = $"{GetDisplayName(group.Key).ToUpperInvariant()} ({enabledCount}/{totalCount} active)",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerGrid.Children.Add(headerText);
                headerBorder.Child = headerGrid;
                PhraseListPanel.Children.Add(headerBorder);

                // Phrase rows in this category
                foreach (var phrase in group)
                {
                    var row = CreatePhraseRow(phrase);
                    PhraseListPanel.Children.Add(row);
                }
            }

            UpdateTotalCount();
        }

        private Border CreatePhraseRow(CompanionPhrase phrase)
        {
            var isSelected = _selectedIds.Contains(phrase.Id);
            var border = new Border
            {
                Background = new SolidColorBrush(isSelected
                    ? Color.FromRgb(0x2A, 0x1E, 0x3A) : Color.FromRgb(0x1E, 0x1E, 0x3A)),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 1),
                BorderBrush = new SolidColorBrush(isSelected
                    ? Color.FromRgb(0xFF, 0x69, 0xB4) : Color.FromRgb(0x2A, 0x2A, 0x45)),
                BorderThickness = new Thickness(isSelected ? 1 : 0, 0, 0, 1),
                Tag = phrase.Id,
                Cursor = Cursors.Hand
            };
            border.MouseLeftButtonDown += RowBorder_Click;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // Select checkbox
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Text
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); // Audio
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // Enable toggle
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // Remove

            // Selection checkbox (Column 0)
            var selectChk = new CheckBox
            {
                IsChecked = phrase.IsSelected,
                Tag = phrase.Id,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            selectChk.Checked += (s, e) => SetPhraseSelected(phrase.Id, true);
            selectChk.Unchecked += (s, e) => SetPhraseSelected(phrase.Id, false);
            Grid.SetColumn(selectChk, 0);
            grid.Children.Add(selectChk);

            // Phrase text (Column 1)
            if (phrase.IsBuiltIn)
            {
                var textBlock = new TextBlock
                {
                    Text = phrase.Text,
                    Foreground = new SolidColorBrush(phrase.IsEnabled ? Colors.White : Color.FromRgb(0x60, 0x60, 0x60)),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = phrase.Text
                };
                Grid.SetColumn(textBlock, 1);
                grid.Children.Add(textBlock);
            }
            else
            {
                var textBox = new TextBox
                {
                    Text = phrase.Text,
                    Tag = phrase.Id,
                    Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x42)),
                    Foreground = new SolidColorBrush(phrase.IsEnabled ? Colors.White : Color.FromRgb(0x60, 0x60, 0x60)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x60)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4, 2, 4, 2),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                textBox.LostFocus += TxtCustomPhrase_LostFocus;
                Grid.SetColumn(textBox, 1);
                grid.Children.Add(textBox);
            }

            // Audio status (Column 2)
            var audioPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };

            bool isVoiceLine = phrase.Category == Services.CompanionPhraseService.VoiceLineCategory;

            if (phrase.HasAudio)
            {
                audioPanel.Children.Add(new TextBlock
                {
                    Text = "\u266B ",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x50, 0xC8, 0x78)),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                });
                audioPanel.Children.Add(new TextBlock
                {
                    Text = (isVoiceLine && phrase.IsBuiltIn) ? "Built-in audio" : phrase.AudioFileName,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x50, 0xC8, 0x78)),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 90,
                    ToolTip = phrase.AudioFileName
                });
                // Voice line audio is inherent to the file - no clear button (only for built-in)
                if (!(isVoiceLine && phrase.IsBuiltIn))
                {
                    var clearAudioBtn = new Button
                    {
                        Content = "\u2716",
                        Tag = phrase.Id,
                        Background = Brushes.Transparent,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
                        BorderThickness = new Thickness(0),
                        FontSize = 10,
                        Padding = new Thickness(4, 0, 0, 0),
                        Cursor = Cursors.Hand,
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = "Remove audio"
                    };
                    clearAudioBtn.Click += BtnClearAudio_Click;
                    audioPanel.Children.Add(clearAudioBtn);
                }
            }
            else
            {
                audioPanel.Children.Add(new TextBlock
                {
                    Text = "No Audio",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                });
                var browseBtn = new Button
                {
                    Content = "Browse",
                    Tag = phrase.Id,
                    Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x50)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderThickness = new Thickness(0),
                    FontSize = 10,
                    Padding = new Thickness(6, 2, 6, 2),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center
                };
                browseBtn.Click += BtnBrowseAudio_Click;
                audioPanel.Children.Add(browseBtn);
            }
            Grid.SetColumn(audioPanel, 2);
            grid.Children.Add(audioPanel);

            // Enable/Disable toggle (Column 3)
            var enableChk = new CheckBox
            {
                IsChecked = phrase.IsEnabled,
                Tag = phrase.Id,
                Content = phrase.IsEnabled ? "On" : "Off",
                Foreground = new SolidColorBrush(phrase.IsEnabled
                    ? Color.FromRgb(0x50, 0xC8, 0x78) : Color.FromRgb(0x80, 0x80, 0x80)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            enableChk.Checked += ChkPhraseEnabled_Changed;
            enableChk.Unchecked += ChkPhraseEnabled_Changed;
            Grid.SetColumn(enableChk, 3);
            grid.Children.Add(enableChk);

            // Remove button (Column 4)
            var removeBtn = new Button
            {
                Content = "\u2716",
                Tag = phrase.Id,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Padding = new Thickness(6, 0, 2, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = phrase.IsBuiltIn ? "Hide phrase" : "Delete phrase"
            };
            removeBtn.Click += BtnRemovePhrase_Click;
            Grid.SetColumn(removeBtn, 4);
            grid.Children.Add(removeBtn);

            border.Child = grid;
            return border;
        }

        private void RowBorder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not Border border || border.Tag is not string id) return;

            // Don't toggle if clicking on a button, textbox, or combobox
            if (e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase
                || e.OriginalSource is TextBox
                || e.OriginalSource is ComboBox)
                return;

            var isSelected = _selectedIds.Contains(id);
            SetPhraseSelected(id, !isSelected);
            RefreshPhraseList();
        }

        private void SetPhraseSelected(string id, bool selected)
        {
            if (selected)
                _selectedIds.Add(id);
            else
                _selectedIds.Remove(id);

            var phrase = _allPhrases.FirstOrDefault(p => p.Id == id);
            if (phrase != null) phrase.IsSelected = selected;
        }

        private void UpdateTotalCount()
        {
            var active = _allPhrases.Count(p => p.IsEnabled);
            var total = _allPhrases.Count;
            TxtTotalCount.Text = $"{active}/{total} phrases active";
        }

        // ============================================================
        // Event Handlers
        // ============================================================

        private void ChkPhraseEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox chk || chk.Tag is not string id) return;
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var enabled = chk.IsChecked == true;
            var phrase = _allPhrases.FirstOrDefault(p => p.Id == id);

            if (phrase?.IsBuiltIn == true)
            {
                if (enabled)
                    settings.DisabledPhraseIds.Remove(id);
                else
                    settings.DisabledPhraseIds.Add(id);
            }
            else
            {
                var custom = settings.CustomCompanionPhrases.FirstOrDefault(c => c.Id == id);
                if (custom != null) custom.Enabled = enabled;
            }

            App.Settings?.Save();
            RefreshPhraseList();
        }

        private void BtnRemovePhrase_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string id) return;
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var phrase = _allPhrases.FirstOrDefault(p => p.Id == id);
            if (phrase == null) return;

            if (phrase.IsBuiltIn)
            {
                settings.RemovedPhraseIds.Add(id);
            }
            else
            {
                settings.CustomCompanionPhrases.RemoveAll(c => c.Id == id);
            }

            App.Settings?.Save();
            RefreshPhraseList();
        }

        private void BtnBrowseAudio_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string id) return;
            BrowseAndSetAudio(id);
        }

        private void BtnClearAudio_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string id) return;
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var phrase = _allPhrases.FirstOrDefault(p => p.Id == id);
            if (phrase == null) return;

            if (phrase.IsBuiltIn)
            {
                settings.PhraseAudioOverrides.Remove(id);
            }
            else
            {
                var custom = settings.CustomCompanionPhrases.FirstOrDefault(c => c.Id == id);
                if (custom != null) custom.AudioFileName = null;
            }

            App.Settings?.Save();
            RefreshPhraseList();
        }

        private void BrowseAndSetAudio(string phraseId)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var phrase = _allPhrases.FirstOrDefault(p => p.Id == phraseId);
            if (phrase == null) return;

            var dialog = new OpenFileDialog
            {
                Filter = "Audio Files|*.mp3;*.wav;*.ogg;*.flac|All Files|*.*",
                Title = "Select audio file for phrase"
            };

            if (dialog.ShowDialog() != true) return;

            var fileName = App.CompanionPhrases?.CopyAudioToFolder(dialog.FileName, phrase.Text);
            if (fileName == null) return;

            if (phrase.IsBuiltIn)
            {
                settings.PhraseAudioOverrides[phraseId] = fileName;
            }
            else
            {
                var custom = settings.CustomCompanionPhrases.FirstOrDefault(c => c.Id == phraseId);
                if (custom != null) custom.AudioFileName = fileName;
            }

            App.Settings?.Save();
            RefreshPhraseList();
        }

        private void TxtCustomPhrase_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox txt || txt.Tag is not string id) return;
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var custom = settings.CustomCompanionPhrases.FirstOrDefault(c => c.Id == id);
            if (custom != null && custom.Text != txt.Text)
            {
                custom.Text = txt.Text;
                App.Settings?.Save();
            }
        }

        private void BtnAddPhrase_Click(object sender, RoutedEventArgs e)
        {
            // Show a simple input dialog
            var inputWindow = new Window
            {
                Title = "Add Custom Phrase",
                Width = 450,
                Height = 240,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                ResizeMode = ResizeMode.NoResize
            };

            var stack = new StackPanel { Margin = new Thickness(15) };
            stack.Children.Add(new TextBlock
            {
                Text = "Enter phrase text:",
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var inputBox = new TextBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x42)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x70)),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 13
            };
            stack.Children.Add(inputBox);

            // Category selector
            stack.Children.Add(new TextBlock
            {
                Text = "Category:",
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 10, 0, 6)
            });

            var categoryCombo = new ComboBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x42)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x70)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 5, 8, 5),
                FontSize = 13
            };
            // Apply DarkComboBox style from parent window resources
            if (TryFindResource("DarkComboBox") is Style darkStyle)
                categoryCombo.Style = darkStyle;

            // Populate with all categories
            foreach (var cat in Services.CompanionPhraseService.GetCategoryNames())
                categoryCombo.Items.Add(new ComboBoxItem { Content = GetDisplayName(cat), Tag = cat });

            // Pre-select the current filter category, or "VoiceLine" by default
            var preselect = _currentFilter != "All Categories" ? _currentFilter : "VoiceLine";
            for (int i = 0; i < categoryCombo.Items.Count; i++)
            {
                if (categoryCombo.Items[i] is ComboBoxItem ci && ci.Tag is string tag && tag == preselect)
                {
                    categoryCombo.SelectedIndex = i;
                    break;
                }
            }
            if (categoryCombo.SelectedIndex < 0) categoryCombo.SelectedIndex = categoryCombo.Items.Count - 1;

            stack.Children.Add(categoryCombo);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x50)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 6, 16, 6),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelBtn.Click += (s, ev) => inputWindow.DialogResult = false;

            var okBtn = new Button
            {
                Content = "Add",
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 6, 16, 6),
                Cursor = Cursors.Hand
            };
            okBtn.Click += (s, ev) => inputWindow.DialogResult = true;

            btnPanel.Children.Add(cancelBtn);
            btnPanel.Children.Add(okBtn);
            stack.Children.Add(btnPanel);
            inputWindow.Content = stack;

            inputBox.Focus();

            if (inputWindow.ShowDialog() != true) return;

            var text = inputBox.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            var selectedCategory = (categoryCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Custom";

            var newPhrase = new CustomCompanionPhrase
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Text = text,
                Category = selectedCategory,
                Enabled = true
            };

            // Ask if they want to add audio
            var result = MessageBox.Show(this,
                "Would you like to connect an audio file to this phrase?",
                "Audio File",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Audio Files|*.mp3;*.wav;*.ogg;*.flac|All Files|*.*",
                    Title = "Select audio file for phrase"
                };

                if (dialog.ShowDialog() == true)
                {
                    var fileName = App.CompanionPhrases?.CopyAudioToFolder(dialog.FileName, text);
                    if (fileName != null) newPhrase.AudioFileName = fileName;
                }
            }

            settings.CustomCompanionPhrases.Add(newPhrase);
            App.Settings?.Save();
            RefreshPhraseList();
        }

        private void BtnRemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _allPhrases.Where(p => p.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "No phrases selected.", "Remove Selected", MessageBoxButton.OK);
                return;
            }

            var result = MessageBox.Show(this,
                $"Remove {selected.Count} selected phrase(s)?\n\nBuilt-in phrases will be hidden (can be restored by clearing settings).\nCustom phrases will be permanently deleted.",
                "Remove Selected",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            foreach (var phrase in selected)
            {
                if (phrase.IsBuiltIn)
                {
                    settings.RemovedPhraseIds.Add(phrase.Id);
                }
                else
                {
                    settings.CustomCompanionPhrases.RemoveAll(c => c.Id == phrase.Id);
                }
            }

            App.Settings?.Save();
            RefreshPhraseList();
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var phrase in _allPhrases)
                _selectedIds.Add(phrase.Id);
            RefreshPhraseList();
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            _selectedIds.Clear();
            RefreshPhraseList();
        }

        private void BtnEnableSelected_Click(object sender, RoutedEventArgs e)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var selected = _allPhrases.Where(p => p.IsSelected).ToList();
            foreach (var phrase in selected)
            {
                if (phrase.IsBuiltIn)
                    settings.DisabledPhraseIds.Remove(phrase.Id);
                else
                {
                    var custom = settings.CustomCompanionPhrases.FirstOrDefault(c => c.Id == phrase.Id);
                    if (custom != null) custom.Enabled = true;
                }
            }

            App.Settings?.Save();
            RefreshPhraseList();
        }

        private void BtnDisableSelected_Click(object sender, RoutedEventArgs e)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var selected = _allPhrases.Where(p => p.IsSelected).ToList();
            foreach (var phrase in selected)
            {
                if (phrase.IsBuiltIn)
                    settings.DisabledPhraseIds.Add(phrase.Id);
                else
                {
                    var custom = settings.CustomCompanionPhrases.FirstOrDefault(c => c.Id == phrase.Id);
                    if (custom != null) custom.Enabled = false;
                }
            }

            App.Settings?.Save();
            RefreshPhraseList();
        }

        private void CmbCategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCategoryFilter.SelectedItem is ComboBoxItem item && item.Tag is string filter)
            {
                _currentFilter = filter;
                RefreshPhraseList();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
