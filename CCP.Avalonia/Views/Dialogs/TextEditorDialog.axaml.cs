using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Dialog for editing text pools (subliminals, attention targets, etc.)
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/TextEditorDialog.xaml.cs. Deviations:
    ///  - WPF's <c>DialogResult</c> property becomes <c>Close(bool)</c> plus a <c>_dialogResult</c>
    ///    field, because Avalonia carries the result through <c>ShowDialog&lt;bool?&gt;</c>.
    ///  - <c>MessageBox.Show</c> has no Avalonia equivalent and no package may be added, so
    ///    <see cref="Ask"/> below is a minimal stand-in.
    ///  - <c>OnClosing</c> cannot prompt synchronously: it cancels the close, awaits the prompt and
    ///    closes again. <c>_dialogResult</c> is what stops it prompting twice.
    ///  - The <c>ItemsSource = null; ItemsSource = _items;</c> refresh hacks are gone. TextItem
    ///    raises PropertyChanged and the row highlight is a bound style class, so the list updates
    ///    itself.
    ///  - <c>_originalData</c> is dropped: the WPF original assigns it and never reads it.
    /// </summary>
    public partial class TextEditorDialog : Window
    {
        private readonly ObservableCollection<TextItem> _items;
        private readonly TextBox _txtNewItem;
        private readonly ItemsControl _itemList;
        private bool _hasChanges = false;
        private bool? _dialogResult;

        /// <summary>
        /// The edited data after Save is clicked
        /// </summary>
        public Dictionary<string, bool>? ResultData { get; private set; }

        /// <summary>Render/design constructor: sample data so --render-view can draw the dialog.</summary>
        public TextEditorDialog() : this("Sample", new Dictionary<string, bool> { ["first item"] = true, ["second item"] = false }) { }

        public TextEditorDialog(string title, Dictionary<string, bool> data)
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new TextEditorViewModel();

            _txtNewItem = this.FindControl<TextBox>("TxtNewItem")!;
            _itemList = this.FindControl<ItemsControl>("ItemList")!;

            this.FindControl<TextBlock>("TxtTitle")!.Text = $"📝 {title}";
            Title = Loc.GetF("title_manager", title);

            // Convert to observable collection for binding
            _items = new ObservableCollection<TextItem>(
                data.Select(kvp => new TextItem { Text = kvp.Key, IsEnabled = kvp.Value })
                    .OrderBy(x => x.Text)
            );

            foreach (var item in _items)
                item.PropertyChanged += TextItem_Changed;

            _itemList.ItemsSource = _items;

            // Handlers live here rather than in markup, per the porting convention.
            _txtNewItem.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddNewItem(); };
            this.FindControl<Button>("BtnAdd")!.Click += (_, _) => AddNewItem();
            this.FindControl<Button>("BtnSort")!.Click += (_, _) => BtnSort_Click();
            this.FindControl<Button>("BtnToggleAll")!.Click += (_, _) => BtnToggleAll_Click();
            this.FindControl<Button>("BtnRemoveSelected")!.Click += (_, _) => BtnRemoveSelected_Click();
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => BtnCancel_Click();
            this.FindControl<Button>("BtnSave")!.Click += (_, _) => BtnSave_Click();

            // One handler on the list instead of one inside the DataTemplate: template content has
            // no name scope to FindControl through. PointerPressed bubbles, and the CheckBox marks
            // it handled exactly as WPF's ButtonBase did, so ticking a box still does not select
            // the row.
            _itemList.AddHandler(InputElement.PointerPressedEvent, ItemList_PointerPressed);
        }

        private async void AddNewItem()
        {
            var text = (_txtNewItem.Text ?? "").Trim().ToUpperInvariant();

            if (string.IsNullOrEmpty(text))
                return;

            // Check for duplicates
            if (_items.Any(x => x.Text.Equals(text, StringComparison.OrdinalIgnoreCase)))
            {
                await Ask(Loc.Get("title_duplicate"), Loc.Get("msg_this_item_already_exists"),
                    (Loc.Get("btn_ok"), true));
                return;
            }

            var added = new TextItem { Text = text, IsEnabled = true };
            added.PropertyChanged += TextItem_Changed;
            _items.Add(added);
            _txtNewItem.Clear();
            _txtNewItem.Focus();
            _hasChanges = true;
        }

        private void BtnSort_Click()
        {
            var sorted = _items.OrderBy(x => x.Text).ToList();
            _items.Clear();
            foreach (var item in sorted)
            {
                _items.Add(item);
            }
        }

        private void BtnToggleAll_Click()
        {
            // If all are enabled, disable all. Otherwise enable all.
            bool allEnabled = _items.All(x => x.IsEnabled);
            bool newState = !allEnabled;

            foreach (var item in _items)
            {
                item.IsEnabled = newState;
            }

            _hasChanges = true;
        }

        private async void ItemList_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(_itemList).Properties.IsLeftButtonPressed)
                return;

            if ((e.Source as Control)?.DataContext is not TextItem item)
                return;

            if (e.Source is TextBlock { Name: "BtnRemove" })
            {
                e.Handled = true; // Prevent triggering the row click

                var confirm = await Ask(Loc.Get("title_confirm"), Loc.GetF("msg_confirm_remove_item", item.Text),
                    ("Yes", true), ("No", false));

                if (confirm == true)
                {
                    _items.Remove(item);
                    _hasChanges = true;
                }
                return;
            }

            // Toggle selection
            item.IsSelected = !item.IsSelected;
        }

        /// <summary>
        /// WPF's Checked/Unchecked handlers on the row CheckBox. Watching the item rather than the
        /// widget is what keeps Sort clean: Sort re-materialises every container, so any handler
        /// hung off ToggleButton.IsCheckedChanged would depend on whether the initial binding lands
        /// before or after the CheckBox joins the visual tree. IsEnabled only changes when someone
        /// actually edits, which is the thing WPF meant to track.
        /// </summary>
        private void TextItem_Changed(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TextItem.IsEnabled))
                _hasChanges = true;
        }

        private async void BtnRemoveSelected_Click()
        {
            var selected = _items.Where(x => x.IsSelected).ToList();

            if (selected.Count == 0)
            {
                await Ask(Loc.Get("title_no_selection"),
                    Loc.Get("msg_no_items_selected_n_nclick_on_items_to_select"),
                    (Loc.Get("btn_ok"), true));
                return;
            }

            var result = await Ask(Loc.Get("title_confirm"), Loc.GetF("msg_confirm_remove_selected", selected.Count),
                ("Yes", true), ("No", false));

            if (result == true)
            {
                foreach (var item in selected)
                {
                    _items.Remove(item);
                }
                _hasChanges = true;
            }
        }

        private async void BtnCancel_Click()
        {
            if (_hasChanges)
            {
                var result = await Ask(Loc.Get("title_unsaved_changes"), Loc.Get("msg_discard_changes"),
                    ("Yes", true), ("No", false));

                if (result != true)
                    return;
            }

            ResultData = null;
            _dialogResult = false;
            Close(false);
        }

        private void BtnSave_Click()
        {
            // Convert back to dictionary
            ResultData = _items.ToDictionary(x => x.Text, x => x.IsEnabled);
            _dialogResult = true;
            Close(true);
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            // If closing via X button and there are changes
            if (_dialogResult == null && _hasChanges)
            {
                // The prompt is async, so the close has to be cancelled and re-issued from it.
                e.Cancel = true;
                PromptSaveOnClose();
            }

            base.OnClosing(e);
        }

        private async void PromptSaveOnClose()
        {
            var result = await Ask(Loc.Get("title_unsaved_changes"), Loc.Get("msg_save_changes_before_closing"),
                ("Yes", true), ("No", false), (Loc.Get("btn_cancel"), null));

            if (result == true)
            {
                ResultData = _items.ToDictionary(x => x.Text, x => x.IsEnabled);
                _dialogResult = true;
                Close(true);
            }
            else if (result == false)
            {
                _dialogResult = false;
                Close(false);
            }
            // Cancel, or dismissed with the X: stay open, as MessageBoxResult.Cancel did.
        }

        /// <summary>
        /// Minimal stand-in for WPF's MessageBox, which Avalonia has no equivalent of. Each button
        /// carries the value Close() hands back; dismissing the window yields null, matching the
        /// Cancel button. Buttons hold a TextBlock, not Content, for the access-key reason above.
        /// The app has no btn_yes/btn_no loc keys - WPF got those strings from the OS - so Yes and
        /// No are English here. btn_ok and btn_cancel do exist and are used.
        /// </summary>
        private Task<bool?> Ask(string title, string message, params (string Label, bool? Value)[] buttons)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var dialog = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Background,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = message,
                            Foreground = Brushes.White,
                            FontSize = 13,
                            TextWrapping = TextWrapping.Wrap,
                            MaxWidth = 360
                        },
                        row
                    }
                }
            };

            foreach (var (label, value) in buttons)
            {
                var button = new Button
                {
                    Content = new TextBlock { Text = label },
                    Padding = new Thickness(14, 6),
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                button.Click += (_, _) => dialog.Close(value);
                row.Children.Add(button);
            }

            return dialog.ShowDialog<bool?>(this);
        }
    }

    /// <summary>Strings from CCP.Core's Loc. See the porting notes in the repo-root CLAUDE.md
    /// for why {loc:Str} becomes a binding.</summary>
    public sealed class TextEditorViewModel
    {
        public string LocDialogTextManager => Loc.Get("dialog_text_manager");
        public string LocBtnSortAZ => Loc.Get("btn_sort_a_z");
        public string LocBtnToggleAll => Loc.Get("btn_toggle_all");
        public string LocBtnAdd => Loc.Get("btn_add_2");
        public string LocTooltipRemoveItem => Loc.Get("tooltip_remove_item");
        public string LocBtnRemoveSelected => Loc.Get("btn_remove_selected_2");
        public string LocBtnCancel => Loc.Get("btn_cancel");
        public string LocBtnSave => Loc.Get("btn_save");
    }

    /// <summary>
    /// Represents a text item in the list.
    /// Copied from the WPF code-behind: the type lives there, not in CCP.Core, and neither the
    /// WPF head nor Core may be touched by this port.
    /// </summary>
    public class TextItem : INotifyPropertyChanged
    {
        private string _text = "";
        private bool _isEnabled = true;
        private bool _isSelected = false;

        public string Text
        {
            get => _text;
            set { _text = value; OnPropertyChanged(nameof(Text)); }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
