using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Awareness;

namespace ConditioningControlPanel
{
    /// <summary>Which awareness list the picker is editing. The two have opposite meanings.</summary>
    public enum AwarenessListKind
    {
        /// <summary>Apps she must never see at all.</summary>
        Deny,

        /// <summary>Apps whose page title may travel off this PC.</summary>
        TitleAllow
    }

    /// <summary>
    /// The picker for both awareness lists. See the XAML header for why this replaced
    /// <see cref="TextEditorDialog"/>.
    ///
    /// <para>Two rules this dialog exists to keep. First, a tick has to state its consequence in the
    /// list's own direction: ticking in the deny list HIDES an app, ticking in the allow list SENDS
    /// its titles, and one control that means both is how someone leaks a title while believing they
    /// muted one. Second, the candidate rows are the point - a privacy list you can only fill by
    /// typing process names from memory is one nobody fills.</para>
    /// </summary>
    public partial class AwarenessAppPickerDialog : Window
    {
        private readonly AwarenessListKind _kind;
        private readonly ObservableCollection<AwarenessPickRow> _rows = new();
        private bool _suppressPrompt;

        /// <summary>The chosen entries, or null when the dialog was cancelled.</summary>
        public List<string>? Result { get; private set; }

        /// <param name="kind">Which list is being edited. Drives every string on the dialog.</param>
        /// <param name="listed">Entries currently in the list, raw (group tokens included).</param>
        /// <param name="candidates">Apps to offer as rows, already sanitised, newest-interesting first.</param>
        public AwarenessAppPickerDialog(AwarenessListKind kind,
                                        IEnumerable<string> listed,
                                        IEnumerable<string> candidates)
        {
            InitializeComponent();
            _kind = kind;

            bool deny = kind == AwarenessListKind.Deny;
            Title = Loc.Get(deny ? "companion_awareness_deny_editor_title"
                                 : "companion_awareness_allow_editor_title");
            TxtTitle.Text = Title;
            TxtSubtitle.Text = Loc.Get(deny ? "companion_awareness_picker_deny_sub"
                                            : "companion_awareness_picker_allow_sub");
            TxtEmptyHint.Text = Loc.Get(deny ? "companion_awareness_picker_deny_empty"
                                             : "companion_awareness_picker_allow_empty");
            BtnSave.Content = Loc.Get("companion_awareness_picker_save");

            BuildRows(listed, candidates);
            ItemList.ItemsSource = _rows;

            // The empty-state card explains the blank list instead of leaving an Add box to guess at.
            EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Listed entries first (they are the answer to "what is on right now"), then the offered
        /// candidates. A candidate already in the list is not repeated.
        /// </summary>
        private void BuildRows(IEnumerable<string> listed, IEnumerable<string> candidates)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in listed ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw) || !seen.Add(raw)) continue;
                _rows.Add(MakeRow(raw, isListed: true));
            }

            foreach (var raw in candidates ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw) || !seen.Add(raw)) continue;
                _rows.Add(MakeRow(raw, isListed: false));
            }
        }

        /// <summary>
        /// A group token renders under its friendly label ("password managers") and carries the
        /// recommended mark; an ordinary app renders as itself. The raw value is what gets saved either
        /// way - a row that saved its LABEL would write "password managers" into the list and quietly
        /// stop matching anything.
        /// </summary>
        private AwarenessPickRow MakeRow(string raw, bool isListed)
        {
            bool group = AwarenessPrivacyRules.IsGroupToken(raw);
            var labelKey = AwarenessPrivacyRules.ChipLabelKey(raw);

            return new AwarenessPickRow
            {
                Raw = raw,
                Label = labelKey.Length > 0 ? Loc.Get(labelKey) : raw,
                Detail = group ? Loc.Get("companion_awareness_picker_group_detail") : string.Empty,
                IsRecommended = group && _kind == AwarenessListKind.Deny,
                IsListed = isListed
            };
        }

        // =====================================================================================
        //  ticking
        // =====================================================================================

        private void Row_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox { Tag: AwarenessPickRow row }) row.IsListed = true;
        }

        /// <summary>
        /// Unticking a shipped guard asks first. This is the exact click that emptied a live user's
        /// deny list during play-testing: the row looked like a phrase in a pool, so removing it did
        /// not read as disarming the password-manager, banking and email-title protections - and every
        /// editor path also marks the seed as done, so nothing restores them afterwards.
        /// </summary>
        private void Row_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox { Tag: AwarenessPickRow row } box) return;

            if (_suppressPrompt || !row.IsRecommended)
            {
                row.IsListed = false;
                return;
            }

            var answer = MessageBox.Show(
                Loc.GetF("companion_awareness_picker_drop_guard", row.Label),
                Loc.Get("companion_awareness_picker_drop_guard_title"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            if (answer == MessageBoxResult.Yes)
            {
                row.IsListed = false;
                return;
            }

            // Put the tick back without re-entering this handler.
            _suppressPrompt = true;
            try { box.IsChecked = true; row.IsListed = true; }
            finally { _suppressPrompt = false; }
        }

        // =====================================================================================
        //  typing an app in
        // =====================================================================================

        private void TxtNewItem_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddTypedItem();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e) => AddTypedItem();

        /// <summary>
        /// Adds a hand-typed app, sanitised by the SAME helper the settings setters use - so what the
        /// row shows is what will actually be stored. The old dialog uppercased on add and the setter
        /// lowercased on save, so the list you approved was never the list you got.
        /// </summary>
        private void AddTypedItem()
        {
            var clean = AwarenessText.SanitizeRuleEntry(TxtNewItem.Text);
            if (clean == null)
            {
                MessageBox.Show(Loc.Get("companion_awareness_picker_bad_entry"),
                    Loc.Get("title_confirm"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var existing = _rows.FirstOrDefault(r =>
                string.Equals(r.Raw, clean, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.IsListed = true;   // already offered: tick it rather than duplicate it
            }
            else
            {
                _rows.Insert(0, new AwarenessPickRow
                {
                    Raw = clean,
                    Label = clean,
                    Detail = string.Empty,
                    IsRecommended = false,
                    IsListed = true
                });
            }

            TxtNewItem.Clear();
            TxtNewItem.Focus();
            EmptyHint.Visibility = Visibility.Collapsed;
        }

        // =====================================================================================
        //  close
        // =====================================================================================

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            Result = _rows.Where(r => r.IsListed).Select(r => r.Raw).ToList();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Result = null;
            DialogResult = false;
            Close();
        }
    }

    /// <summary>One offered app. <see cref="Raw"/> is stored; <see cref="Label"/> is only shown.</summary>
    public sealed class AwarenessPickRow : INotifyPropertyChanged
    {
        private bool _isListed;

        public string Raw { get; set; } = "";
        public string Label { get; set; } = "";
        public string Detail { get; set; } = "";
        public bool IsRecommended { get; set; }

        public bool HasDetail => !string.IsNullOrEmpty(Detail);

        public bool IsListed
        {
            get => _isListed;
            set
            {
                if (_isListed == value) return;
                _isListed = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsListed)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
