using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Awareness;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>Which awareness list the picker is editing. The two have opposite meanings.</summary>
    /// <remarks>
    /// Copied from the WPF code-behind: the enum lives beside the WPF Window, and neither the WPF
    /// head nor Core may be touched by this port. Same precedent as <c>TextItem</c> in
    /// <see cref="TextEditorDialog"/>.
    /// </remarks>
    public enum AwarenessListKind
    {
        /// <summary>Apps she must never see at all.</summary>
        Deny,

        /// <summary>Apps whose page title may travel off this PC.</summary>
        TitleAllow
    }

    /// <summary>
    /// The picker for both awareness lists. See the AXAML header for why this replaced
    /// <see cref="TextEditorDialog"/>.
    ///
    /// <para>Two rules this dialog exists to keep. First, a tick has to state its consequence in the
    /// list's own direction: ticking in the deny list HIDES an app, ticking in the allow list SENDS
    /// its titles, and one control that means both is how someone leaks a title while believing they
    /// muted one. Second, the candidate rows are the point - a privacy list you can only fill by
    /// typing process names from memory is one nobody fills.</para>
    ///
    /// <para>PORTED from ConditioningControlPanel/Dialogs/AwarenessAppPickerDialog.xaml.cs.
    /// Deviations:</para>
    /// <list type="bullet">
    ///   <item>WPF's <c>DialogResult</c> becomes <c>Close(bool)</c>; the caller uses
    ///   <c>ShowDialog&lt;bool?&gt;</c>. <see cref="Result"/> is unchanged.</item>
    ///   <item><c>MessageBox.Show</c> becomes <see cref="MessageDialog"/>, this head's message box.
    ///   It is async, so <c>Row_Unchecked</c> became one async handler over both tick directions.
    ///   The guard prompt reads OK/Cancel rather than WPF's OS-supplied Yes/No, and those two
    ///   labels are localised where the old hand-rolled stand-in's English "Yes"/"No" were not.
    ///   The guard prompt passes <c>defaultToCancel</c>, which is the WPF call's
    ///   <c>MessageBoxResult.No</c>: Enter, Cancel and the window X all KEEP the guard, and only
    ///   an explicit OK drops one.</item>
    ///   <item><c>AwarenessPrivacyRules.IsGroupToken</c>/<c>ChipLabelKey</c> are called directly:
    ///   the class is in Core now (CCP.Core/Services/Awareness/AwarenessPrivacyRules.cs), so the
    ///   verbatim mirror this port used to carry is gone. A group token's meaning is therefore
    ///   decided in exactly one place again.</item>
    /// </list>
    /// </summary>
    public partial class AwarenessAppPickerDialog : Window
    {
        private readonly AwarenessListKind _kind;
        private readonly ObservableCollection<AwarenessPickRow> _rows = new();
        private readonly TextBox _txtNewItem;
        private readonly Border _emptyHint;
        private bool _suppressPrompt;

        /// <summary>The chosen entries, or null when the dialog was cancelled.</summary>
        public List<string>? Result { get; private set; }

        /// <summary>Render/design constructor: sample rows so --render-view can draw the dialog.</summary>
        internal AwarenessAppPickerDialog()
            : this(AwarenessListKind.Deny, new[] { "firefox" }, new[] { "firefox", "steam", "discord" }) { }

        /// <param name="kind">Which list is being edited. Drives every string on the dialog.</param>
        /// <param name="listed">Entries currently in the list, raw (group tokens included).</param>
        /// <param name="candidates">Apps to offer as rows, already sanitised, newest-interesting first.</param>
        public AwarenessAppPickerDialog(AwarenessListKind kind,
                                        IEnumerable<string> listed,
                                        IEnumerable<string> candidates)
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new AwarenessAppPickerViewModel();
            _kind = kind;

            _txtNewItem = this.FindControl<TextBox>("TxtNewItem")!;
            _emptyHint = this.FindControl<Border>("EmptyHint")!;
            var itemList = this.FindControl<ItemsControl>("ItemList")!;
            var btnSave = this.FindControl<Button>("BtnSave")!;

            bool deny = kind == AwarenessListKind.Deny;
            Title = Loc.Get(deny ? "companion_awareness_deny_editor_title"
                                 : "companion_awareness_allow_editor_title");
            this.FindControl<TextBlock>("TxtTitle")!.Text = Title;
            this.FindControl<TextBlock>("TxtSubtitle")!.Text =
                Loc.Get(deny ? "companion_awareness_picker_deny_sub"
                             : "companion_awareness_picker_allow_sub");
            this.FindControl<TextBlock>("TxtEmptyHint")!.Text =
                Loc.Get(deny ? "companion_awareness_picker_deny_empty"
                             : "companion_awareness_picker_allow_empty");
            btnSave.Content = new TextBlock { Text = Loc.Get("companion_awareness_picker_save") };

            BuildRows(listed, candidates);
            itemList.ItemsSource = _rows;

            // The empty-state card explains the blank list instead of leaving an Add box to guess at.
            _emptyHint.IsVisible = _rows.Count == 0;

            // Handlers live here rather than in markup, per the porting convention.
            _txtNewItem.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddTypedItem(); };
            this.FindControl<Button>("BtnAdd")!.Click += (_, _) => AddTypedItem();
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => BtnCancel_Click();
            btnSave.Click += (_, _) => BtnSave_Click();

            // One handler on the list instead of two inside the DataTemplate: template content has
            // no name scope to FindControl through. IsCheckedChanged bubbles, so this stands in for
            // WPF's Checked and Unchecked pair.
            itemList.AddHandler(ToggleButton.IsCheckedChangedEvent, Row_IsCheckedChanged);
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

        /// <summary>
        /// WPF's <c>Row_Checked</c> and <c>Row_Unchecked</c>, folded into the one event Avalonia
        /// raises for both directions.
        ///
        /// <para>Unticking a shipped guard asks first. This is the exact click that emptied a live
        /// user's deny list during play-testing: the row looked like a phrase in a pool, so removing
        /// it did not read as disarming the password-manager, banking and email-title protections -
        /// and every editor path also marks the seed as done, so nothing restores them afterwards.</para>
        /// </summary>
        private async void Row_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (e.Source is not CheckBox { Tag: AwarenessPickRow row } box) return;

            if (box.IsChecked == true)
            {
                row.IsListed = true;
                return;
            }

            if (_suppressPrompt || !row.IsRecommended)
            {
                row.IsListed = false;
                return;
            }

            // defaultToCancel is the WPF call's MessageBoxResult.No: Enter must KEEP the guard.
            var answer = await MessageDialog.ConfirmAsync(this,
                Loc.Get("companion_awareness_picker_drop_guard_title"),
                Loc.GetF("companion_awareness_picker_drop_guard", row.Label),
                defaultToCancel: true);

            if (answer)
            {
                row.IsListed = false;
                return;
            }

            // Anything but an explicit OK keeps the guard, matching MessageBoxResult.No as the
            // WPF default - Cancel and the window X both answer false, so dismissing the prompt
            // can never disarm one.
            // Put the tick back without re-entering this handler.
            _suppressPrompt = true;
            try { box.IsChecked = true; row.IsListed = true; }
            finally { _suppressPrompt = false; }
        }

        // =====================================================================================
        //  typing an app in
        // =====================================================================================

        /// <summary>
        /// Adds a hand-typed app, sanitised by the SAME helper the settings setters use - so what the
        /// row shows is what will actually be stored. The old dialog uppercased on add and the setter
        /// lowercased on save, so the list you approved was never the list you got.
        /// </summary>
        private async void AddTypedItem()
        {
            var clean = AwarenessText.SanitizeRuleEntry(_txtNewItem.Text);
            if (clean == null)
            {
                await MessageDialog.ShowAsync(this, Loc.Get("title_confirm"),
                    Loc.Get("companion_awareness_picker_bad_entry"));
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

            _txtNewItem.Clear();
            _txtNewItem.Focus();
            _emptyHint.IsVisible = false;
        }

        // =====================================================================================
        //  close
        // =====================================================================================

        private void BtnSave_Click()
        {
            Result = _rows.Where(r => r.IsListed).Select(r => r.Raw).ToList();
            Close(true);
        }

        private void BtnCancel_Click()
        {
            Result = null;
            Close(false);
        }

    }


    /// <summary>Strings from CCP.Core's Loc. See the porting notes in the repo-root CLAUDE.md
    /// for why {loc:Str} becomes a binding. The four strings the code-behind sets directly are not
    /// here, because they depend on which list is being edited.</summary>
    public sealed class AwarenessAppPickerViewModel
    {
        public string LocRecommended => Loc.Get("companion_awareness_picker_recommended");
        public string LocAddTip => Loc.Get("companion_awareness_picker_add_tip");
        public string LocBtnAdd => Loc.Get("btn_add_2");
        public string LocAddHint => Loc.Get("companion_awareness_picker_add_hint");
        public string LocBtnCancel => Loc.Get("btn_cancel");
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
