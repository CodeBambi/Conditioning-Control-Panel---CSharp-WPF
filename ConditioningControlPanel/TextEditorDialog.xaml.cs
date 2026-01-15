using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Dialog for editing text pools (subliminals, attention targets, etc.)
    /// </summary>
    public partial class TextEditorDialog : Window
    {
        private Dictionary<string, bool> _originalData;
        private ObservableCollection<TextItem> _items;
        private bool _hasChanges = false;

        /// <summary>
        /// The edited data after Save is clicked
        /// </summary>
        public Dictionary<string, bool>? ResultData { get; private set; }

        public TextEditorDialog(string title, Dictionary<string, bool> data)
        {
            InitializeComponent();
            
            TxtTitle.Text = $"📝 {title}";
            Title = $"{title} Manager";
            
            // Make a copy of the data
            _originalData = new Dictionary<string, bool>(data);
            
            // Convert to observable collection for binding
            _items = new ObservableCollection<TextItem>(
                data.Select(kvp => new TextItem { Text = kvp.Key, IsEnabled = kvp.Value })
                    .OrderBy(x => x.Text)
            );
            
            ItemList.ItemsSource = _items;
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            AddNewItem();
        }

        private void TxtNewItem_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddNewItem();
            }
        }

        private void AddNewItem()
        {
            var text = TxtNewItem.Text.Trim().ToUpperInvariant();
            
            if (string.IsNullOrEmpty(text))
                return;
            
            // Check for duplicates
            if (_items.Any(x => x.Text.Equals(text, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("This item already exists!", "Duplicate", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            _items.Add(new TextItem { Text = text, IsEnabled = true });
            TxtNewItem.Clear();
            TxtNewItem.Focus();
            _hasChanges = true;
        }

        private void BtnSort_Click(object sender, RoutedEventArgs e)
        {
            var sorted = _items.OrderBy(x => x.Text).ToList();
            _items.Clear();
            foreach (var item in sorted)
            {
                _items.Add(item);
            }
        }

        private void BtnToggleAll_Click(object sender, RoutedEventArgs e)
        {
            // If all are enabled, disable all. Otherwise enable all.
            bool allEnabled = _items.All(x => x.IsEnabled);
            bool newState = !allEnabled;
            
            foreach (var item in _items)
            {
                item.IsEnabled = newState;
            }
            
            // Refresh the list
            ItemList.ItemsSource = null;
            ItemList.ItemsSource = _items;
            _hasChanges = true;
        }

        private void Item_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TextItem item)
            {
                // Toggle selection
                item.IsSelected = !item.IsSelected;
                
                // Refresh
                ItemList.ItemsSource = null;
                ItemList.ItemsSource = _items;
            }
        }

        private void CheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _hasChanges = true;
        }

        private void BtnRemove_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // Prevent triggering Item_Click
            
            if (sender is FrameworkElement fe && fe.DataContext is TextItem item)
            {
                var result = MessageBox.Show($"Remove \"{item.Text}\"?", "Confirm", 
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    _items.Remove(item);
                    _hasChanges = true;
                }
            }
        }

        private void BtnRemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _items.Where(x => x.IsSelected).ToList();
            
            if (selected.Count == 0)
            {
                MessageBox.Show("No items selected.\n\nClick on items to select them for removal.", 
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var result = MessageBox.Show($"Remove {selected.Count} selected item(s)?", "Confirm", 
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                foreach (var item in selected)
                {
                    _items.Remove(item);
                }
                _hasChanges = true;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_hasChanges)
            {
                var result = MessageBox.Show("Discard changes?", "Unsaved Changes", 
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                    return;
            }
            
            ResultData = null;
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Convert back to dictionary
            ResultData = _items.ToDictionary(x => x.Text, x => x.IsEnabled);
            DialogResult = true;
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // If closing via X button and there are changes
            if (DialogResult == null && _hasChanges)
            {
                var result = MessageBox.Show("Save changes before closing?", "Unsaved Changes", 
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    ResultData = _items.ToDictionary(x => x.Text, x => x.IsEnabled);
                    DialogResult = true;
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

    /// <summary>
    /// Represents a text item in the list
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
