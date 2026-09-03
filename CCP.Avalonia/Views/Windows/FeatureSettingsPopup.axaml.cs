using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Popup for editing feature settings on timeline events.
    ///
    /// PORTED from ConditioningControlPanel/Windows/FeatureSettingsPopup.xaml.cs. Deviations:
    ///  - <c>LoadEvent</c> took a <c>TimelineSession</c> purely to reach its two phrase lists.
    ///    That type still lives in the WPF head, not Core, so the lists are passed directly.
    ///  - <c>App.Mods?.GetAccentColorHex() ?? "#FF69B4"</c> becomes the theme's PinkBrush, which
    ///    is that exact colour (Theme/Colors.xaml: PinkColor = #FFFF69B4).
    ///  - <c>_maxMinute</c> and <c>_settingControls</c> are dropped: the WPF original writes both
    ///    and reads neither.
    ///  - <c>Checked</c>/<c>Unchecked</c> collapse into <c>IsCheckedChanged</c>.
    ///  - <c>OpenFileDialog</c> -&gt; <c>TopLevel.StorageProvider.OpenFilePickerAsync</c>, which is
    ///    async, so the browse handler awaits instead of blocking.
    ///  - <c>MessageBox.Show</c> has no Avalonia equivalent and no package may be added, so the
    ///    empty-pool notice on import is a small owned window (the ModManagerDialog pattern).
    /// </summary>
    public partial class FeatureSettingsPopup : UserControl
    {
        // WPF's inline SolidColorBrush(Color.FromRgb(...)) literals, named once.
        private static readonly IBrush MutedGrey = Brush.Parse("#888888");
        private static readonly IBrush LabelGrey = Brush.Parse("#B0B0B0");
        private static readonly IBrush ValueGrey = Brush.Parse("#E0E0E0");
        private static readonly IBrush SeparatorBrush = Brush.Parse("#3C3C50");
        private static readonly IBrush InputBorder = Brush.Parse("#353555");
        private static readonly IBrush InputBg = Brush.Parse("#252542");
        private static readonly IBrush ListBg = Brush.Parse("#1E1E32");
        private static readonly IBrush AddGreen = Brush.Parse("#4CAF50");
        private static readonly IBrush RemoveRed = Brush.Parse("#F44336");
        private static readonly IBrush NeutralGrey = Brush.Parse("#646464");

        private TimelineEvent? _event;
        private FeatureDefinition? _feature;

        // The two lists TimelineSession handed over in WPF.
        private List<string>? _subliminalPhrases;
        private List<string>? _bouncingTextPhrases;

        public event EventHandler<TimelineEvent>? SettingsChanged;
        public event EventHandler<TimelineEvent>? DeleteRequested;
        public event EventHandler? CloseRequested;

        public FeatureSettingsPopup()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            SliderMinute.ValueChanged += (_, e) => SliderMinute_ValueChanged(e.NewValue);
            BtnDelete.Click += (_, _) => BtnDelete_Click();
            BtnDone.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);

            // Render/design seat: a sample event so the headless proof draws the generated
            // settings (ramp header, sliders, toggles) rather than an empty panel. Overwritten
            // by the first LoadEvent, which clears the panel and rewrites every header field.
            LoadEvent(new TimelineEvent { FeatureId = "flash", Minute = 12 }, 60);
        }

        /// <summary>
        /// Load an event for editing. <paramref name="subliminalPhrases"/> and
        /// <paramref name="bouncingTextPhrases"/> are the session's own lists, edited in place -
        /// WPF reached them through the TimelineSession this signature used to take.
        /// </summary>
        public void LoadEvent(TimelineEvent evt, int maxMinute,
                              List<string>? subliminalPhrases = null,
                              List<string>? bouncingTextPhrases = null)
        {
            _event = evt;
            _subliminalPhrases = subliminalPhrases;
            _bouncingTextPhrases = bouncingTextPhrases;
            _feature = FeatureDefinition.GetById(evt.FeatureId);

            if (_feature == null) return;

            // Update header
            TxtIcon.Text = _feature.Icon;
            TxtFeatureName.Text = _feature.Name;
            TxtEventType.Text = evt.EventType == TimelineEventType.Start ? "Start Event" : "Stop Event";

            // Update minute slider
            SliderMinute.Maximum = maxMinute;
            SliderMinute.Value = evt.Minute;
            TxtMinuteValue.Text = evt.Minute.ToString(CultureInfo.InvariantCulture);

            // Generate settings controls
            GenerateSettingsControls();
        }

        private void GenerateSettingsControls()
        {
            SettingsPanel.Children.Clear();

            if (_event == null || _feature == null) return;

            // Only show settings for start events
            if (_event.EventType != TimelineEventType.Start)
            {
                SettingsPanel.Children.Add(new TextBlock
                {
                    Text = "Stop events have no settings.\nDelete this to change when the feature ends.",
                    Foreground = MutedGrey,
                    FontSize = 12,
                    FontStyle = FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 10, 0, 10)
                });
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

            // Add phrase management for subliminal and bouncing text
            if (_event.FeatureId == "subliminal" && _subliminalPhrases != null)
            {
                AddPhraseManagement("Subliminal Phrases", _subliminalPhrases, true);
            }
            else if (_event.FeatureId == "bouncing_text" && _bouncingTextPhrases != null)
            {
                AddPhraseManagement("Bouncing Text Phrases", _bouncingTextPhrases, false);
            }
        }

        /// <summary>
        /// Theme lookup against the Application, not <c>this</c>. A control builds its settings in
        /// the constructor, before it is attached to anything, and a detached StyledElement's
        /// resource walk stops at its null Parent - it never reaches App.axaml. Looking up
        /// "PinkSlider" through <c>this</c> silently returned false and every generated slider
        /// rendered in the Fluent blue default (found by rendering, not by reading).
        /// </summary>
        private static T? ThemeRes<T>(string key) where T : class =>
            Application.Current is { } app && app.TryFindResource(key, out var res) ? res as T : null;

        /// <summary>WPF read this from App.Mods; PinkBrush is that same #FF69B4 on this head.</summary>
        private static IBrush Accent => ThemeRes<IBrush>("PinkBrush") ?? Brush.Parse("#FF69B4");

        private void AddRampingControls()
        {
            if (_event == null || _feature == null) return;

            // Find the setting that supports ramping
            var rampSetting = _feature.Settings.Find(s => s.SupportsRamp);
            if (rampSetting == null) return;

            // Header
            SettingsPanel.Children.Add(new TextBlock
            {
                Text = $"{rampSetting.Name} Ramping",
                Foreground = Accent,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 5, 0, 8)
            });

            // Start value
            var startValue = _event.StartValue ?? (int)Convert.ToDouble(rampSetting.Default ?? rampSetting.Min);
            AddSlider($"Start {rampSetting.Name}", "ramp_start", (int)rampSetting.Min, (int)rampSetting.Max, startValue);

            // End value
            var endValue = _event.EndValue ?? startValue;
            AddSlider($"End {rampSetting.Name}", "ramp_end", (int)rampSetting.Min, (int)rampSetting.Max, endValue);

            SettingsPanel.Children.Add(Separator());
        }

        private static Border Separator() => new()
        {
            Height = 1,
            Background = SeparatorBrush,
            Margin = new Thickness(0, 10, 0, 10)
        };

        private void AddSettingControl(FeatureSettingDefinition setting)
        {
            if (_event == null) return;

            // Skip ramp-supporting settings if already handled above
            if (setting.SupportsRamp && _feature?.SupportsRamping == true) return;

            switch (setting.Type)
            {
                case SettingType.Slider:
                    var value = _event.GetSetting(setting.Key, (int)Convert.ToDouble(setting.Default ?? setting.Min));
                    AddSlider(setting.Name, setting.Key, (int)setting.Min, (int)setting.Max, value);
                    break;

                case SettingType.Toggle:
                    var boolValue = _event.GetSetting(setting.Key, (bool)(setting.Default ?? false));
                    AddToggle(setting.Name, setting.Key, boolValue);
                    break;

                case SettingType.Dropdown:
                    var stringValue = _event.GetSetting(setting.Key, setting.Default?.ToString() ?? "");
                    AddDropdown(setting.Name, setting.Key, setting.Options ?? Array.Empty<string>(), stringValue);
                    break;

                case SettingType.FilePicker:
                    var pathValue = _event.GetSetting(setting.Key, setting.Default?.ToString() ?? "");
                    AddFilePicker(setting.Name, setting.Key, pathValue);
                    break;
            }
        }

        private void AddSlider(string name, string key, int min, int max, int value)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 10),
                ColumnDefinitions = new ColumnDefinitions("80,*,40")
            };

            var label = new TextBlock
            {
                Text = name,
                Foreground = LabelGrey,
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
            // The WPF implicit Slider style; here the app theme has to be asked for by key.
            slider.Theme = ThemeRes<ControlTheme>("PinkSlider");
            Grid.SetColumn(slider, 1);
            grid.Children.Add(slider);

            var valueText = new TextBlock
            {
                Text = value.ToString(CultureInfo.InvariantCulture),
                Foreground = ValueGrey,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(valueText, 2);
            grid.Children.Add(valueText);

            slider.ValueChanged += (_, e) =>
            {
                valueText.Text = ((int)e.NewValue).ToString(CultureInfo.InvariantCulture);
                SaveSetting(key, (int)e.NewValue);
            };

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
            // WPF's implicit CheckBox style; the label is a sibling TextBlock, so the box-only
            // ItemCheckBox theme is the match. Without it the box draws in Fluent blue.
            checkBox.Theme = ThemeRes<ControlTheme>("ItemCheckBox");
            checkBox.IsCheckedChanged += (_, _) => SaveSetting(key, checkBox.IsChecked ?? false);

            panel.Children.Add(checkBox);
            panel.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = LabelGrey,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            });

            SettingsPanel.Children.Add(panel);
        }

        private void AddDropdown(string name, string key, string[] options, string value)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 10),
                ColumnDefinitions = new ColumnDefinitions("80,*")
            };

            var label = new TextBlock
            {
                Text = name,
                Foreground = LabelGrey,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            var comboBox = new ComboBox
            {
                Background = InputBg,
                Foreground = ValueGrey,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Tag = key
            };
            // WPF's implicit ComboBox style, asked for by key. Without a Template a keyed
            // ControlTheme on a templated control draws nothing (CLAUDE.md trap 4), so the
            // shared DarkComboBoxStyle - which is BasedOn the Fluent default - is what to use.
            comboBox.Theme = ThemeRes<ControlTheme>("DarkComboBoxStyle");

            foreach (var option in options)
            {
                comboBox.Items.Add(option);
            }
            comboBox.SelectedItem = value;
            comboBox.SelectionChanged += (_, _) =>
            {
                if (comboBox.SelectedItem != null)
                    SaveSetting(key, comboBox.SelectedItem.ToString() ?? "");
            };

            Grid.SetColumn(comboBox, 1);
            grid.Children.Add(comboBox);

            SettingsPanel.Children.Add(grid);
        }

        private void AddFilePicker(string name, string key, string value)
        {
            var stackPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            stackPanel.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = LabelGrey,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            var textBox = new TextBox
            {
                Text = value,
                Background = InputBg,
                Foreground = ValueGrey,
                BorderBrush = InputBorder,
                Padding = new Thickness(8, 5),
                FontSize = 11,
                IsReadOnly = true,
                Tag = key
            };
            Grid.SetColumn(textBox, 0);
            grid.Children.Add(textBox);

            var browseButton = new Button
            {
                Content = new TextBlock { Text = "..." },
                Width = 30,
                Margin = new Thickness(5, 0, 0, 0),
                Background = InputBorder,
                Foreground = ValueGrey,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            browseButton.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(this) is not { } top) return;

                var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = $"Select {name}",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Image/GIF Files")
                        {
                            Patterns = new[] { "*.gif", "*.png", "*.jpg" }
                        },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } },
                    },
                });

                var path = picked.FirstOrDefault()?.TryGetLocalPath();
                if (string.IsNullOrEmpty(path)) return;

                textBox.Text = path;
                SaveSetting(key, path);
            };
            Grid.SetColumn(browseButton, 1);
            grid.Children.Add(browseButton);

            stackPanel.Children.Add(grid);

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

        private void SliderMinute_ValueChanged(double newValue)
        {
            if (_event == null) return;

            _event.Minute = (int)newValue;
            TxtMinuteValue.Text = _event.Minute.ToString(CultureInfo.InvariantCulture);
            SettingsChanged?.Invoke(this, _event);
        }

        private void BtnDelete_Click()
        {
            if (_event == null) return;
            DeleteRequested?.Invoke(this, _event);
        }

        // ---- phrase management ------------------------------------------------

        private const string NoPhrasesPlaceholder = "(No custom phrases - using global pool)";

        private void AddPhraseManagement(string title, List<string> phrases, bool isSubliminal)
        {
            SettingsPanel.Children.Add(Separator());

            // Header with count
            var headerGrid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8),
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };

            var headerText = new TextBlock
            {
                Text = title,
                Foreground = Accent,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(headerText, 0);
            headerGrid.Children.Add(headerText);

            var countText = new TextBlock
            {
                Text = phrases.Count == 0 ? "(using global)" : $"({phrases.Count} custom)",
                Foreground = MutedGrey,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(countText, 1);
            headerGrid.Children.Add(countText);

            SettingsPanel.Children.Add(headerGrid);

            // Phrase list (scrollable, max 3 visible)
            var listBox = new ListBox
            {
                Background = ListBg,
                Foreground = ValueGrey,
                BorderBrush = InputBorder,
                MaxHeight = 80,
                Margin = new Thickness(0, 0, 0, 8)
            };

            foreach (var phrase in phrases)
            {
                listBox.Items.Add(phrase);
            }

            if (phrases.Count == 0)
            {
                listBox.Items.Add(NoPhrasesPlaceholder);
                listBox.IsEnabled = false;
            }

            SettingsPanel.Children.Add(listBox);

            // Button row
            var buttonGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };

            var addButton = PhraseButton("+ Add", AddGreen, new Thickness(0, 0, 4, 0));
            addButton.Click += (_, _) => AddPhrase(phrases, listBox, countText);
            Grid.SetColumn(addButton, 0);
            buttonGrid.Children.Add(addButton);

            var removeButton = PhraseButton("- Remove", RemoveRed, new Thickness(2, 0, 2, 0));
            removeButton.Click += (_, _) => RemovePhrase(phrases, listBox, countText);
            Grid.SetColumn(removeButton, 1);
            buttonGrid.Children.Add(removeButton);

            var clearButton = PhraseButton("Clear", NeutralGrey, new Thickness(4, 0, 0, 0));
            clearButton.Click += (_, _) => ClearPhrases(phrases, listBox, countText);
            Grid.SetColumn(clearButton, 2);
            buttonGrid.Children.Add(clearButton);

            SettingsPanel.Children.Add(buttonGrid);

            // Import from global button
            var importButton = PhraseButton("📥 Import from Global Settings", InputBorder, new Thickness(0, 8, 0, 0));
            importButton.Padding = new Thickness(8, 6);
            importButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            importButton.Click += (_, _) => ImportFromGlobal(phrases, listBox, countText, isSubliminal);
            SettingsPanel.Children.Add(importButton);
        }

        /// <summary>The four phrase buttons differ only in label, colour and margin.
        /// A TextBlock, not Content: Avalonia parses "_" in Content as an access key.</summary>
        private static Button PhraseButton(string label, IBrush background, Thickness margin) => new()
        {
            Content = new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center },
            Background = background,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4),
            Margin = margin,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        private async void AddPhrase(List<string> phrases, ListBox listBox, TextBlock countText)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return; // no owner to be modal against

            // WPF's PhraseInputDialog (a code-built Window at the bottom of the original file) is
            // not ported: Views/Dialogs/InputDialog is already the same dialog on this head -
            // title, prompt, text box, Cancel/OK - and it carries the app's loc strings.
            var dialog = new Dialogs.InputDialog("Add Phrase", "Enter a new phrase:");
            if (await dialog.ShowDialog<bool?>(owner) != true) return;

            var text = dialog.ResultText;
            if (string.IsNullOrWhiteSpace(text)) return;

            // If this is the first phrase, clear the placeholder
            if (phrases.Count == 0)
            {
                listBox.Items.Clear();
                listBox.IsEnabled = true;
            }

            phrases.Add(text);
            listBox.Items.Add(text);
            countText.Text = $"({phrases.Count} custom)";

            if (_event != null)
                SettingsChanged?.Invoke(this, _event);
        }

        private void RemovePhrase(List<string> phrases, ListBox listBox, TextBlock countText)
        {
            if (listBox.SelectedIndex < 0 || listBox.SelectedIndex >= phrases.Count) return;

            var index = listBox.SelectedIndex;
            phrases.RemoveAt(index);
            listBox.Items.RemoveAt(index);

            if (phrases.Count == 0)
            {
                listBox.Items.Add(NoPhrasesPlaceholder);
                listBox.IsEnabled = false;
                countText.Text = "(using global)";
            }
            else
            {
                countText.Text = $"({phrases.Count} custom)";
            }

            if (_event != null)
                SettingsChanged?.Invoke(this, _event);
        }

        private void ClearPhrases(List<string> phrases, ListBox listBox, TextBlock countText)
        {
            phrases.Clear();
            listBox.Items.Clear();
            listBox.Items.Add(NoPhrasesPlaceholder);
            listBox.IsEnabled = false;
            countText.Text = "(using global)";

            if (_event != null)
                SettingsChanged?.Invoke(this, _event);
        }

        private void ImportFromGlobal(List<string> phrases, ListBox listBox, TextBlock countText, bool isSubliminal)
        {
            // Get enabled phrases from global pool (Dictionary<string, bool>)
            var globalPool = isSubliminal
                ? CoreSettings.Current.SubliminalPool
                : CoreSettings.Current.BouncingTextPool;

            var enabledPhrases = globalPool?.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();

            if (enabledPhrases == null || enabledPhrases.Count == 0)
            {
                Notify("Import", "No enabled phrases found in global settings.");
                return;
            }

            // Clear existing and import
            phrases.Clear();
            phrases.AddRange(enabledPhrases);

            listBox.Items.Clear();
            listBox.IsEnabled = true;
            foreach (var phrase in phrases)
            {
                listBox.Items.Add(phrase);
            }

            countText.Text = $"({phrases.Count} custom)";

            if (_event != null)
                SettingsChanged?.Invoke(this, _event);
        }

        /// <summary>Minimal stand-in for WPF's information MessageBox, which Avalonia has no
        /// equivalent of and no package may be added for. Shown owned when this control has a
        /// host window; silently dropped when it does not, because a notice is not worth
        /// stranding an ownerless window over.</summary>
        private void Notify(string title, string message)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;

            var ok = new Button
            {
                Content = new TextBlock { Text = "OK" },
                Padding = new Thickness(14, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0),
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            var dialog = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = ListBg,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = message,
                            Foreground = ValueGrey,
                            FontSize = 13,
                            TextWrapping = TextWrapping.Wrap,
                            MaxWidth = 320
                        },
                        ok
                    }
                }
            };
            ok.Click += (_, _) => dialog.Close();
            _ = dialog.ShowDialog(owner);
        }
    }
}
