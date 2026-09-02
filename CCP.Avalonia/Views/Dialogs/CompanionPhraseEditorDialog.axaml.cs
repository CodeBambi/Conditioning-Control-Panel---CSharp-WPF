using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Phrase Manager: every companion speech-bubble line, voice line, custom phrase and bark,
    /// grouped by category, with per-row enable / audio / remove and bulk selection.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/CompanionPhraseEditorDialog.xaml.cs. Deviations:
    ///  - <c>CompanionPhrase</c> and <c>CustomCompanionPhrase</c> are ALREADY in CCP.Core, so the
    ///    rows bind to the real model - no copied type (the TextEditorDialog/QuizCategoryEditorWindow
    ///    shape is not needed here).
    ///  - <c>CollectionViewSource</c> has no Avalonia equivalent. Grouping and filtering move into
    ///    <see cref="Regroup"/>, which rebuilds a list of <see cref="PhraseGroup"/> and rebinds. The
    ///    WPF filter predicate is <see cref="Matches"/>, unchanged in behaviour.
    ///  - <c>App.CompanionPhrases</c>, <c>App.Settings</c> and
    ///    <c>Services.CompanionPhraseService</c> are all in the WPF head, not Core, so everything
    ///    that loads or persists is a stub. Each carries a ponytail comment; the view-only halves
    ///    (selection, filtering, the enable toggle, the count line) run for real.
    ///  - <c>MessageBox.Show</c> has no Avalonia equivalent and no package may be added, so the
    ///    "no phrases selected" and confirm prompts become ponytail comments and the action
    ///    proceeds (QuizCategoryEditorWindow precedent).
    ///  - <c>OpenFileDialog</c> plus <c>CopyAudioToFolder</c> is one stub: the picker would be
    ///    <c>StorageProvider.OpenFilePickerAsync</c>, but the copy target is the service's.
    ///  - <c>HitsInteractive</c> is dropped. Avalonia's Button (so also the row CheckBoxes) and
    ///    TextBox mark PointerPressed handled, so a click on them never reaches the row handler -
    ///    which is exactly what that WPF visual-tree walk existed to emulate.
    ///  - <c>TxtCustomPhrase_LostFocus</c> only persisted, so it is gone with the persistence; the
    ///    TextBox still writes back to the bound row.
    /// </summary>
    public partial class CompanionPhraseEditorDialog : Window
    {
        // Filter sentinel that matches every bark line (Category == "Bark"), so the dropdown needs
        // one "Bark Lines" entry instead of ~80 per-trigger entries; per-rule headers come from grouping.
        private const string BarkCategory = "Bark";

        private ObservableCollection<CompanionPhrase> _phrases = new();
        private string _currentFilter = "All Categories";
        private string _searchTerm = "";
        // Set while a bulk op mutates many rows so per-row persistence defers to a single Save() at the end.
        private bool _bulkUpdating;

        private readonly ItemsControl _phraseList;
        private readonly ComboBox _cmbCategoryFilter;
        private readonly TextBox _txtSearch;
        private readonly TextBlock _txtTotalCount;

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
            { BarkCategory, "🐰 Bark Lines" },
        };

        // ponytail: needs Services.CompanionPhraseService.GetCategoryNames(), wired when it moves to
        // Core. Verbatim copy of that service's _categoryNames plus its VoiceLineCategory, which is
        // what GetCategoryNames() returns.
        private static readonly string[] _categoryNames =
        {
            "Greeting", "StartupGreeting", "Idle", "RandomFloating", "Generic",
            "Gaming", "Browsing", "Shopping", "Social", "Discord",
            "TrainingSite", "HypnoContent", "Working", "Media", "Learning",
            "WindowAwarenessIdle", "EngineStop", "FlashPre", "SubliminalAck",
            "RandomBubble", "BubbleCountMercy", "BubblePop", "GameFailed",
            "BubbleMissed", "FlashClicked", "LevelUp", "MindWipe", "BrainDrain",
            "VoiceLine"
        };

        private static string GetDisplayName(string category) =>
            _categoryDisplayNames.TryGetValue(category, out var dn) ? dn : category;

        public CompanionPhraseEditorDialog()
        {
            AvaloniaXamlLoader.Load(this);

            _phraseList = this.FindControl<ItemsControl>("PhraseList")!;
            _cmbCategoryFilter = this.FindControl<ComboBox>("CmbCategoryFilter")!;
            _txtSearch = this.FindControl<TextBox>("TxtSearch")!;
            _txtTotalCount = this.FindControl<TextBlock>("TxtTotalCount")!;

            PopulateCategoryFilter();
            RefreshPhraseList();

            // Handlers live here rather than in markup, per the porting convention. Wired after
            // PopulateCategoryFilter so its SelectedIndex = 0 does not trigger a second regroup.
            _cmbCategoryFilter.SelectionChanged += CmbCategoryFilter_SelectionChanged;
            _txtSearch.TextChanged += TxtSearch_Changed;

            this.FindControl<Button>("BtnSelectAll")!.Click += (_, _) => BtnSelectAll_Click();
            this.FindControl<Button>("BtnDeselectAll")!.Click += (_, _) => BtnDeselectAll_Click();
            this.FindControl<Button>("BtnEnableSelected")!.Click += (_, _) => SetSelectedEnabled(true);
            this.FindControl<Button>("BtnDisableSelected")!.Click += (_, _) => SetSelectedEnabled(false);
            this.FindControl<Button>("BtnAddPhrase")!.Click += (_, _) => BtnAddPhrase_Click();
            this.FindControl<Button>("BtnRemoveSelected")!.Click += (_, _) => BtnRemoveSelected_Click();
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();

            // Two handlers on the list instead of per-row handlers inside the DataTemplate: template
            // content has no name scope to FindControl through. Both events bubble, and the row
            // buttons carry x:Names so one handler can tell them apart.
            _phraseList.AddHandler(Button.ClickEvent, PhraseList_RowButtonClick);
            _phraseList.AddHandler(InputElement.PointerPressedEvent, PhraseList_PointerPressed);
        }

        private void PopulateCategoryFilter()
        {
            var items = new List<ComboBoxItem>
            {
                new() { Content = "All Categories", Tag = "All Categories" }
            };
            foreach (var cat in _categoryNames)
                items.Add(new ComboBoxItem { Content = GetDisplayName(cat), Tag = cat });
            items.Add(new ComboBoxItem { Content = GetDisplayName("Custom"), Tag = "Custom" });
            // One entry for the ~1,200 bark lines; the list still groups them per rule via GroupLabel.
            items.Add(new ComboBoxItem { Content = GetDisplayName(BarkCategory), Tag = BarkCategory });

            _cmbCategoryFilter.ItemsSource = items;
            _cmbCategoryFilter.SelectedIndex = 0;
        }

        /// <summary>
        /// Rebuilds the full phrase set (built-in + voice lines + custom + bark lines) and rebinds the
        /// grouped view. Only called when the row SET changes (add/remove); plain
        /// enable/disable/select toggles mutate the bound rows in place and skip this.
        /// </summary>
        private void RefreshPhraseList()
        {
            // Preserve selection across the rebuild (rows are fresh CompanionPhrase instances).
            var selected = new HashSet<string>(_phrases.Where(p => p.IsSelected).Select(p => p.Id));

            // ponytail: needs App.CompanionPhrases (CompanionPhraseService), wired when it moves to
            // Core. Sample rows until then - see SamplePhrases.
            var all = SamplePhrases();
            var fresh = new ObservableCollection<CompanionPhrase>();
            foreach (var p in all)
            {
                // Barks arrive with their own "Bark · {trigger}" GroupLabel; give everything else one.
                if (!p.IsBark) p.GroupLabel = GetDisplayName(p.Category);
                // `||` rather than `=`: GetAllPhrases() hands back rows with IsSelected
                // false, so this is a no-op against the real service, but it lets the
                // sample set below carry a pre-selected row into the render.
                p.IsSelected = p.IsSelected || selected.Contains(p.Id);
                p.PropertyChanged += Phrase_PropertyChanged;
                fresh.Add(p);
            }

            _phrases = fresh;
            Regroup();
            UpdateTotalCount();
        }

        // ============================================================
        // Filtering (category dropdown + search box)
        // ============================================================

        /// <summary>
        /// WPF's CollectionViewSource.Filter + GroupDescription, in one pass.
        ///
        /// ponytail: rebuilds every group on each keystroke and drops the WPF ListBox's grouped UI
        /// virtualization, so all ~1,500 rows are realised. Fine for the sample set and for any
        /// filtered view; move to a ListBox over a flat header+row list with a
        /// VirtualizingStackPanel if the unfiltered "All Categories" view ever feels slow.
        /// </summary>
        private void Regroup()
        {
            _phraseList.ItemsSource = _phrases
                .Where(Matches)
                .GroupBy(p => p.GroupLabel)
                .Select(g => new PhraseGroup(g.Key, g.ToList()))
                .ToList();
        }

        private bool Matches(CompanionPhrase p)
        {
            if (_currentFilter != "All Categories" && p.Category != _currentFilter)
                return false;

            if (!string.IsNullOrWhiteSpace(_searchTerm) &&
                (p.Text == null || p.Text.IndexOf(_searchTerm, StringComparison.OrdinalIgnoreCase) < 0))
                return false;

            return true;
        }

        private void CmbCategoryFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_cmbCategoryFilter.SelectedItem is ComboBoxItem item && item.Tag is string filter)
            {
                _currentFilter = filter;
                Regroup();
            }
        }

        private void TxtSearch_Changed(object? sender, TextChangedEventArgs e)
        {
            _searchTerm = _txtSearch.Text ?? "";
            Regroup();
        }

        // ============================================================
        // Per-row handlers (resolve the row's phrase via DataContext)
        // ============================================================

        private static CompanionPhrase? PhraseOf(object? sender) =>
            (sender as Control)?.DataContext as CompanionPhrase;

        private void PhraseList_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(_phraseList).Properties.IsLeftButtonPressed) return;
            // Group headers carry a PhraseGroup, not a phrase, so this also skips them. The row's
            // Buttons, CheckBoxes and TextBox mark the event handled before it gets here, which is
            // what WPF's HitsInteractive walk was emulating.
            if (PhraseOf(e.Source) is not CompanionPhrase p) return;
            p.IsSelected = !p.IsSelected;
        }

        /// <summary>The three per-row buttons, told apart by the x:Name they carry in the template.</summary>
        private void PhraseList_RowButtonClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (e.Source is not Button b || PhraseOf(b) is not CompanionPhrase p) return;
            switch (b.Name)
            {
                case "RemoveBtn": BtnRemovePhrase_Click(p); break;
                case "BrowseBtn": BrowseAndSetAudio(p); break;
                case "ClearAudioBtn": BtnClearAudio_Click(p); break;
            }
        }

        /// <summary>Fires when a bound row's IsEnabled flips (user toggled the On/Off box) — persists it.</summary>
        private void Phrase_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not CompanionPhrase p) return;
            if (e.PropertyName == nameof(CompanionPhrase.IsEnabled))
                PersistEnabled(p);
        }

        /// <summary>
        /// Persist a row's enabled state. Built-in phrases, voice lines and bark lines all share
        /// AppSettings.DisabledPhraseIds (the "Bark:" id prefix keeps them distinct); custom phrases
        /// store it on their own model.
        /// </summary>
        private void PersistEnabled(CompanionPhrase p)
        {
            // ponytail: needs App.Settings (SettingsService), wired when it moves to Core. The WPF
            // body moved p.Id in or out of DisabledPhraseIds, or set custom.Enabled, then saved.
            if (!_bulkUpdating)
                UpdateTotalCount();
        }

        private void UpdateTotalCount()
        {
            var active = _phrases.Count(p => p.IsEnabled);
            var total = _phrases.Count;
            _txtTotalCount.Text = $"{active}/{total} phrases active";
        }

        private void BtnRemovePhrase_Click(CompanionPhrase phrase)
        {
            // ponytail: needs App.Settings, wired when it moves to Core. Built-in and bark rows went
            // into RemovedPhraseIds; custom rows were deleted from CustomCompanionPhrases. Dropping
            // the row locally keeps the view honest until then.
            _phrases.Remove(phrase);
            Regroup();
            UpdateTotalCount();
        }

        private void BtnClearAudio_Click(CompanionPhrase phrase)
        {
            // ponytail: needs App.Settings, wired when it moves to Core (PhraseAudioOverrides for
            // built-ins, custom.AudioFileName otherwise).
            phrase.AudioFileName = null;
            Regroup();
        }

        private void BrowseAndSetAudio(CompanionPhrase phrase)
        {
            // ponytail: needs App.Settings and App.CompanionPhrases.CopyAudioToFolder, wired when
            // they move to Core. The picker itself would be StorageProvider.OpenFilePickerAsync with
            // an mp3/wav/ogg/flac filter, but there is nowhere to copy the file to yet.
        }

        // ============================================================
        // Toolbar / bulk actions
        // ============================================================

        private void BtnSelectAll_Click()
        {
            // Only the rows currently visible through the filter, so "select all" respects search/category.
            foreach (var p in VisiblePhrases()) p.IsSelected = true;
        }

        private void BtnDeselectAll_Click()
        {
            foreach (var p in _phrases) p.IsSelected = false;
        }

        private void SetSelectedEnabled(bool enabled)
        {
            var selected = _phrases.Where(p => p.IsSelected).ToList();
            if (selected.Count == 0) return;

            _bulkUpdating = true;
            foreach (var p in selected) p.IsEnabled = enabled; // PropertyChanged → PersistEnabled (Save deferred)
            _bulkUpdating = false;

            // ponytail: needs App.Settings.Save(), wired when it moves to Core.
            UpdateTotalCount();
        }

        private void BtnRemoveSelected_Click()
        {
            var selected = _phrases.Where(p => p.IsSelected).ToList();
            // ponytail: WPF showed a MessageBox here ("No phrases selected." / the remove-N confirm).
            // Avalonia has no MessageBox and no package may be added, so both are silent: nothing
            // selected is a no-op, and a selection is removed without the confirm.
            if (selected.Count == 0) return;

            foreach (var phrase in selected)
                _phrases.Remove(phrase);

            Regroup();
            UpdateTotalCount();
        }

        private IEnumerable<CompanionPhrase> VisiblePhrases() => _phrases.Where(Matches);

        private async void BtnAddPhrase_Click()
        {
            var inputWindow = new Window
            {
                Title = "Add Custom Phrase",
                Width = 450,
                Height = 240,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                CanResize = false
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
            // WPF's TryFindResource("DarkComboBox"); the hoisted theme is DarkComboBoxStyle here.
            if (this.TryFindResource("DarkComboBoxStyle", out var darkStyle) && darkStyle is ControlTheme ct)
                categoryCombo.Theme = ct;

            var categoryItems = _categoryNames
                .Select(cat => new ComboBoxItem { Content = GetDisplayName(cat), Tag = cat })
                .ToList();
            categoryCombo.ItemsSource = categoryItems;

            // Custom phrases can't be authored into the bark system, so pre-select the current filter
            // only when it's a real authorable category; otherwise default to VoiceLine.
            var preselect = (_currentFilter != "All Categories" && _currentFilter != BarkCategory)
                ? _currentFilter : "VoiceLine";
            for (int i = 0; i < categoryItems.Count; i++)
            {
                if (categoryItems[i].Tag is string tag && tag == preselect)
                {
                    categoryCombo.SelectedIndex = i;
                    break;
                }
            }
            if (categoryCombo.SelectedIndex < 0) categoryCombo.SelectedIndex = categoryItems.Count - 1;

            stack.Children.Add(categoryCombo);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var cancelBtn = new Button
            {
                Content = new TextBlock { Text = "Cancel" },
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x50)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 6, 16, 6),
                Cursor = new Cursor(StandardCursorType.Hand),
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelBtn.Click += (_, _) => inputWindow.Close(false);

            var okBtn = new Button
            {
                Content = new TextBlock { Text = "Add" },
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 6, 16, 6),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            okBtn.Click += (_, _) => inputWindow.Close(true);

            btnPanel.Children.Add(cancelBtn);
            btnPanel.Children.Add(okBtn);
            stack.Children.Add(btnPanel);
            inputWindow.Content = stack;

            // WPF focused before ShowDialog; Avalonia has no visual tree until the window opens.
            inputWindow.Opened += (_, _) => inputBox.Focus();

            if (await inputWindow.ShowDialog<bool?>(this) != true) return;

            var text = inputBox.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            var selectedCategory = (categoryCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Custom";

            // ponytail: needs App.Settings (to add a CustomCompanionPhrase and save) and the
            // MessageBox + OpenFileDialog audio prompt that followed it; wired when they move to
            // Core. Until then the new row is added to the in-memory list only.
            var added = new CompanionPhrase
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Text = text,
                Category = selectedCategory,
                GroupLabel = GetDisplayName(selectedCategory),
                IsBuiltIn = false,
                IsEnabled = true
            };
            // WPF got this from the RefreshPhraseList() that followed its save; the stub cannot
            // reload, so the new row subscribes here or its On/Off toggle never updates the count.
            added.PropertyChanged += Phrase_PropertyChanged;
            _phrases.Add(added);
            Regroup();
            UpdateTotalCount();
        }

        // ============================================================
        // Sample data
        // ============================================================

        /// <summary>
        /// Stands in for App.CompanionPhrases.GetAllPhrases(). Covers every visual state the row
        /// template can take - built-in text, an editable custom row, a disabled row, a selected
        /// row, a voice line with clearable audio, a built-in voice line whose audio cannot be
        /// cleared, and a bark (no Browse) - across three groups, so the render proves the template
        /// rather than one happy path.
        ///
        /// HasAudio is computed from File.Exists, so the audio rows point at a file that is always
        /// there: this assembly.
        /// </summary>
        private static List<CompanionPhrase> SamplePhrases()
        {
            var audioFolder = AppContext.BaseDirectory;
            var audioFile = Path.GetFileName(typeof(CompanionPhraseEditorDialog).Assembly.Location);

            return new List<CompanionPhrase>
            {
                new() { Id = "Greeting:0", Text = "Hi there, welcome back~", Category = "Greeting", IsBuiltIn = true },
                new() { Id = "Greeting:1", Text = "Someone missed me, didn't they?", Category = "Greeting", IsBuiltIn = true, IsSelected = true },
                new() { Id = "Greeting:2", Text = "A disabled greeting nobody hears", Category = "Greeting", IsBuiltIn = true, IsEnabled = false },

                // Built-in voice line: inherent audio, so AudioDisplayName is "Built-in audio" and
                // CanClearAudio is false - the ✖ next to it must NOT draw.
                new() { Id = "VoiceLine:0", Text = "good_girl.mp3", Category = "VoiceLine", IsBuiltIn = true,
                        AudioFolder = audioFolder, AudioFileName = audioFile },

                // Custom row: editable TextBox, a clearable audio override, so the ✖ DOES draw.
                new() { Id = "a1b2c3d4", Text = "My own custom phrase, with audio", Category = "Custom", IsBuiltIn = false,
                        AudioFolder = audioFolder, AudioFileName = audioFile },

                // Bark: CanBrowseAudio is false, so its audio column shows "No Audio" with no Browse.
                new() { Id = "Bark:Startup:0", Text = "Oh! You're up early.", Category = BarkCategory, IsBuiltIn = true,
                        IsBark = true, GroupLabel = "Bark · Startup" },
            };
        }
    }

    /// <summary>
    /// One group of the phrase list. Replaces WPF's PropertyGroupDescription("GroupLabel") plus the
    /// GroupStyle header, which had Name and ItemCount for free.
    /// </summary>
    public sealed class PhraseGroup
    {
        public PhraseGroup(string name, IReadOnlyList<CompanionPhrase> items)
        {
            Name = name;
            Items = items;
        }

        public string Name { get; }
        public IReadOnlyList<CompanionPhrase> Items { get; }

        /// <summary>The four Runs of the WPF header template: "Name (ItemCount)".</summary>
        public string Header => $"{Name} ({Items.Count})";
    }
}
