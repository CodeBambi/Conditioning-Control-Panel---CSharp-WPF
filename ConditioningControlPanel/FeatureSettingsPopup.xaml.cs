using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Models;
using Microsoft.Win32;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Popup for editing feature settings on timeline events
    /// </summary>
    public partial class FeatureSettingsPopup : UserControl
    {
        private TimelineEvent? _event;
        private FeatureDefinition? _feature;
        private int _maxMinute = 120;
        private readonly Dictionary<string, FrameworkElement> _settingControls = new();

        public event EventHandler<TimelineEvent>? SettingsChanged;
        public event EventHandler<TimelineEvent>? DeleteRequested;
        public event EventHandler? CloseRequested;

        public FeatureSettingsPopup()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Load an event for editing
        /// </summary>
        public void LoadEvent(TimelineEvent evt, int maxMinute)
        {
            _event = evt;
            _maxMinute = maxMinute;
            _feature = FeatureDefinition.GetById(evt.FeatureId);

            if (_feature == null) return;

            // Update header
            TxtIcon.Text = _feature.Icon;
            TxtFeatureName.Text = _feature.Name;
            TxtEventType.Text = evt.EventType == TimelineEventType.Start ? "Start Event" : "Stop Event";

            // Update minute slider
            SliderMinute.Maximum = maxMinute;
            SliderMinute.Value = evt.Minute;
            TxtMinuteValue.Text = evt.Minute.ToString();

            // Generate settings controls
            GenerateSettingsControls();
        }

        private void GenerateSettingsControls()
        {
            SettingsPanel.Children.Clear();
            _settingControls.Clear();

            if (_event == null || _feature == null) return;

            // Only show settings for start events
            if (_event.EventType != TimelineEventType.Start)
            {
                var noSettingsText = new TextBlock
                {
                    Text = "Stop events have no settings.\nDelete this to change when the feature ends.",
                    Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136)),
                    FontSize = 12,
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 10, 0, 10)
                };
                SettingsPanel.Children.Add(noSettingsText);
                return;
            }

            // Add ramping controls if feature supports it
            if (_feature.SupportsRamping)
            {
                AddRampingControls();
            }

            // Add settings from feature definition
            foreach (var setting in _feature.Settings)
            {
                AddSettingControl(setting);
            }
        }

        private void AddRampingControls()
        {
            if (_event == null || _feature == null) return;

            // Find the setting that supports ramping
            var rampSetting = _feature.Settings.Find(s => s.SupportsRamp);
            if (rampSetting == null) return;

            // Header
            var header = new TextBlock
            {
                Text = $"{rampSetting.Name} Ramping",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 105, 180)),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 5, 0, 8)
            };
            SettingsPanel.Children.Add(header);

            // Start value
            var startValue = _event.StartValue ?? (int)(Convert.ToDouble(rampSetting.Default ?? rampSetting.Min));
            AddSlider($"Start {rampSetting.Name}", "ramp_start", (int)rampSetting.Min, (int)rampSetting.Max, startValue);

            // End value
            var endValue = _event.EndValue ?? startValue;
            AddSlider($"End {rampSetting.Name}", "ramp_end", (int)rampSetting.Min, (int)rampSetting.Max, endValue);

            // Separator
            var separator = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 80)),
                Margin = new Thickness(0, 10, 0, 10)
            };
            SettingsPanel.Children.Add(separator);
        }

        private void AddSettingControl(FeatureSettingDefinition setting)
        {
            if (_event == null) return;

            // Skip ramp-supporting settings if already handled above
            if (setting.SupportsRamp && _feature?.SupportsRamping == true) return;

            switch (setting.Type)
            {
                case SettingType.Slider:
                    var value = _event.GetSetting<int>(setting.Key, (int)Convert.ToDouble(setting.Default ?? setting.Min));
                    AddSlider(setting.Name, setting.Key, (int)setting.Min, (int)setting.Max, value);
                    break;

                case SettingType.Toggle:
                    var boolValue = _event.GetSetting<bool>(setting.Key, (bool)(setting.Default ?? false));
                    AddToggle(setting.Name, setting.Key, boolValue);
                    break;

                case SettingType.Dropdown:
                    var stringValue = _event.GetSetting<string>(setting.Key, setting.Default?.ToString() ?? "");
                    AddDropdown(setting.Name, setting.Key, setting.Options ?? Array.Empty<string>(), stringValue);
                    break;

                case SettingType.FilePicker:
                    var pathValue = _event.GetSetting<string>(setting.Key, setting.Default?.ToString() ?? "");
                    AddFilePicker(setting.Name, setting.Key, pathValue);
                    break;
            }
        }

        private void AddSlider(string name, string key, int min, int max, int value)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            var label = new TextBlock
            {
                Text = name,
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = key
            };
            Grid.SetColumn(slider, 1);
            grid.Children.Add(slider);

            var valueText = new TextBlock
            {
                Text = value.ToString(),
                Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(valueText, 2);
            grid.Children.Add(valueText);

            slider.ValueChanged += (s, e) =>
            {
                valueText.Text = ((int)e.NewValue).ToString();
                SaveSetting(key, (int)e.NewValue);
            };

            _settingControls[key] = slider;
            SettingsPanel.Children.Add(grid);
        }

        private void AddToggle(string name, string key, bool value)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var checkBox = new CheckBox
            {
                IsChecked = value,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = key
            };
            checkBox.Checked += (s, e) => SaveSetting(key, true);
            checkBox.Unchecked += (s, e) => SaveSetting(key, false);

            var label = new TextBlock
            {
                Text = name,
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };

            panel.Children.Add(checkBox);
            panel.Children.Add(label);

            _settingControls[key] = checkBox;
            SettingsPanel.Children.Add(panel);
        }

        private void AddDropdown(string name, string key, string[] options, string value)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = name,
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            var comboBox = new ComboBox
            {
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 66)),
                Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                Tag = key
            };
            foreach (var option in options)
            {
                comboBox.Items.Add(option);
            }
            comboBox.SelectedItem = value;
            comboBox.SelectionChanged += (s, e) =>
            {
                if (comboBox.SelectedItem != null)
                    SaveSetting(key, comboBox.SelectedItem.ToString() ?? "");
            };

            Grid.SetColumn(comboBox, 1);
            grid.Children.Add(comboBox);

            _settingControls[key] = comboBox;
            SettingsPanel.Children.Add(grid);
        }

        private void AddFilePicker(string name, string key, string value)
        {
            var stackPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            var label = new TextBlock
            {
                Text = name,
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 5)
            };
            stackPanel.Children.Add(label);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textBox = new TextBox
            {
                Text = value,
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 66)),
                Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(53, 53, 85)),
                Padding = new Thickness(8, 5, 8, 5),
                FontSize = 11,
                IsReadOnly = true,
                Tag = key
            };
            Grid.SetColumn(textBox, 0);
            grid.Children.Add(textBox);

            var browseButton = new Button
            {
                Content = "...",
                Width = 30,
                Margin = new Thickness(5, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(53, 53, 85)),
                Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            browseButton.Click += (s, e) =>
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Image/GIF Files (*.gif;*.png;*.jpg)|*.gif;*.png;*.jpg|All Files (*.*)|*.*",
                    Title = $"Select {name}"
                };
                if (dialog.ShowDialog() == true)
                {
                    textBox.Text = dialog.FileName;
                    SaveSetting(key, dialog.FileName);
                }
            };
            Grid.SetColumn(browseButton, 1);
            grid.Children.Add(browseButton);

            stackPanel.Children.Add(grid);

            _settingControls[key] = textBox;
            SettingsPanel.Children.Add(stackPanel);
        }

        private void SaveSetting(string key, object value)
        {
            if (_event == null) return;

            // Handle ramp values specially
            if (key == "ramp_start")
            {
                _event.StartValue = (int)value;
            }
            else if (key == "ramp_end")
            {
                _event.EndValue = (int)value;
            }
            else
            {
                _event.SetSetting(key, value);
            }

            SettingsChanged?.Invoke(this, _event);
        }

        private void SliderMinute_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_event == null) return;

            _event.Minute = (int)e.NewValue;
            TxtMinuteValue.Text = _event.Minute.ToString();
            SettingsChanged?.Invoke(this, _event);
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_event == null) return;
            DeleteRequested?.Invoke(this, _event);
        }

        private void BtnDone_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
