using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.KeywordTriggers;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// platform#1: WPF <c>MainWindow.KeywordTriggers.cs</c>'s custom-trigger list - Add, Import from
    /// Trigger Mode, and one row per custom trigger (preset clones are managed by their preset cards
    /// and never listed here) with the same nine editors, numbers and English labels as WPF's
    /// <c>CreateKeywordTriggerRow</c>.
    /// </summary>
    public partial class KeywordTriggersPanel
    {
        private static readonly string[] VisualEffectNames =
            { "None", "Highlight Only", "Subliminal", "Exact Subliminal", "Image Flash", "Overlay Pulse", "Mind Wipe", "Bubbles" };

        private static readonly IBrush Dim = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
        private static readonly IBrush Light = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));

        private StackPanel? _listPanel;

        private void WireTriggerList()
        {
            _listPanel = this.FindControl<StackPanel>("KeywordTriggerListPanel");
            this.FindControl<Button>("BtnAddKeywordTrigger")!.Click += (_, _) => AddTrigger();
            this.FindControl<Button>("BtnImportFromCustomTriggers")!.Click += async (_, _) => await ImportAsync();
        }

        /// <summary>WPF BtnAddKeywordTrigger_Click.</summary>
        internal void AddTrigger()
        {
            CoreSettings.Current.KeywordTriggers.Add(KeywordTriggerEngine.NewCustomTrigger());
            CoreSettings.Save();
            RefreshKeywordTriggerList();
        }

        /// <summary>WPF BtnImportFromCustomTriggers_Click.</summary>
        internal async System.Threading.Tasks.Task ImportAsync()
        {
            var s = CoreSettings.Current;
            var imported = KeywordTriggerEngine.ImportFromCustomTriggers(s, KeywordTriggerHead.FindLinkedAudio);
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (imported.Count == 0)
            {
                if (owner != null)
                    await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_import_complete"),
                        Loc.Get("msg_no_new_triggers_to_import_all_existing_trigge"));
                return;
            }
            s.KeywordTriggers.AddRange(imported);
            CoreSettings.Save();
            RefreshKeywordTriggerList();
            if (owner != null)
                await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_import_complete"),
                    Loc.GetF("msg_imported_0_trigger_s_from_your_trigger_mode_l", imported.Count));
        }

        /// <summary>WPF RefreshKeywordTriggerList: custom triggers only.</summary>
        internal void RefreshKeywordTriggerList()
        {
            if (_listPanel == null) return;
            _listPanel.Children.Clear();
            foreach (var t in CoreSettings.Current.KeywordTriggers.ToList())
            {
                if (t?.Id?.StartsWith("preset:", StringComparison.Ordinal) == true || t == null) continue;
                _listPanel.Children.Add(CreateRow(t));
            }
        }

        private static void Edit(KeywordTrigger t, Action<KeywordTrigger> change, bool rebuild = true)
        {
            change(t);
            if (rebuild) t.RebuildActionsFromFlatFields();
            CoreSettings.Save();
        }

        /// <summary>WPF CreateKeywordTriggerRow.</summary>
        private Border CreateRow(KeywordTrigger trigger)
        {
            var root = new StackPanel { Spacing = 4 };

            // Top: enabled, keyword, delete.
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            var enabled = new CheckBox { IsChecked = trigger.Enabled, VerticalAlignment = VerticalAlignment.Center };
            enabled.IsCheckedChanged += (_, _) => Edit(trigger, t => t.Enabled = enabled.IsChecked == true, rebuild: false);
            var keyword = new TextBox
            {
                Text = trigger.Keyword, Margin = new Thickness(6, 0), FontSize = 12, Watermark = "keyword",
            };
            keyword.LostFocus += (_, _) =>
            {
                if (keyword.Text == trigger.Keyword) return;
                Edit(trigger, t =>
                {
                    t.Keyword = keyword.Text ?? "";
                    if (string.IsNullOrEmpty(t.AudioFilePath)) t.AudioFilePath = KeywordTriggerHead.FindLinkedAudio(t.Keyword);
                });
            };
            var delete = new Button { Content = "✖", Padding = new Thickness(6, 2), Background = Brushes.Transparent, Foreground = Dim };
            delete.Click += (_, _) =>
            {
                CoreSettings.Current.KeywordTriggers.Remove(trigger);
                CoreSettings.Save();
                RefreshKeywordTriggerList();
            };
            Grid.SetColumn(keyword, 1);
            Grid.SetColumn(delete, 2);
            top.Children.Add(enabled);
            top.Children.Add(keyword);
            top.Children.Add(delete);
            root.Children.Add(top);

            // Settings: audio name, Browse, visual effect.
            var settings = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
            var audioName = new TextBlock
            {
                Text = string.IsNullOrEmpty(trigger.AudioFilePath) ? "No audio" : Path.GetFileName(trigger.AudioFilePath),
                Foreground = Light, FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var browse = new Button { Content = "Browse", FontSize = 10, Padding = new Thickness(8, 2), Margin = new Thickness(6, 0) };
            browse.Click += async (_, _) => await BrowseAudioAsync(trigger);
            var visual = new ComboBox { MinWidth = 100, FontSize = 10, ItemsSource = VisualEffectNames };
            var effectIndex = (int)trigger.VisualEffect;
            visual.SelectedIndex = effectIndex < VisualEffectNames.Length ? effectIndex : 0;
            visual.SelectionChanged += (_, _) =>
            {
                if (visual.SelectedIndex >= 0 && visual.SelectedIndex != (int)trigger.VisualEffect)
                    Edit(trigger, t => t.VisualEffect = (KeywordVisualEffect)visual.SelectedIndex);
            };
            Grid.SetColumn(browse, 1);
            Grid.SetColumn(visual, 2);
            settings.Children.Add(audioName);
            settings.Children.Add(browse);
            settings.Children.Add(visual);
            root.Children.Add(settings);

            // Options: cooldown, volume, haptic, duck.
            var options = new StackPanel { Orientation = Orientation.Horizontal };
            options.Children.Add(Label("CD:"));
            var cdValue = Value($"{trigger.CooldownSeconds}s");
            var cd = new Slider { Minimum = 1, Maximum = 300, Value = trigger.CooldownSeconds, Width = 60 };
            cd.ValueChanged += (_, e) =>
            {
                cdValue.Text = $"{(int)e.NewValue}s";
                Edit(trigger, t => t.CooldownSeconds = (int)e.NewValue, rebuild: false);
            };
            options.Children.Add(cd);
            options.Children.Add(cdValue);
            options.Children.Add(Label("Vol:"));
            var vol = new Slider { Minimum = 0, Maximum = 100, Value = trigger.AudioVolume, Width = 50 };
            vol.ValueChanged += (_, e) => Edit(trigger, t => t.AudioVolume = (int)e.NewValue);
            options.Children.Add(vol);
            var haptic = new CheckBox { Content = "Haptic", IsChecked = trigger.HapticEnabled, FontSize = 10, Margin = new Thickness(10, 0, 0, 0) };
            haptic.IsCheckedChanged += (_, _) => Edit(trigger, t => t.HapticEnabled = haptic.IsChecked == true);
            options.Children.Add(haptic);
            var duck = new CheckBox { Content = "Duck", IsChecked = trigger.DuckAudio, FontSize = 10, Margin = new Thickness(6, 0, 0, 0) };
            duck.IsCheckedChanged += (_, _) => Edit(trigger, t => t.DuckAudio = duck.IsChecked == true);
            options.Children.Add(duck);
            root.Children.Add(options);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x3A)),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6), Margin = new Thickness(0, 0, 0, 6),
                Child = root,
                Tag = trigger.Id,
            };

            static TextBlock Label(string text) => new()
            {
                Text = text, Foreground = Dim, FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
            };
            static TextBlock Value(string text) => new()
            {
                Text = text, Foreground = Light, FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 10, 0),
            };
        }

        /// <summary>WPF BtnKeywordTriggerBrowseAudio_Click (Audio Files|*.mp3;*.wav;*.ogg).</summary>
        private async System.Threading.Tasks.Task BrowseAudioAsync(KeywordTrigger trigger)
        {
            try
            {
                if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Loc.Get("title_select_trigger_audio"),
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Audio Files") { Patterns = new[] { "*.mp3", "*.wav", "*.ogg" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } },
                    },
                });
                if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
                Edit(trigger, t => t.AudioFilePath = path);
                RefreshKeywordTriggerList();
            }
            catch (Exception ex) { Log.Debug(ex, "Keyword trigger audio picker failed"); }
        }
    }
}
