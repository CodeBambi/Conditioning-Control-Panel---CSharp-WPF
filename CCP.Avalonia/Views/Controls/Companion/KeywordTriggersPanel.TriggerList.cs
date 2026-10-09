using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// The custom trigger LIST: MainWindow.KeywordTriggers.cs:160-575 (add, import, the per-row
    /// editors, RefreshKeywordTriggerList and CreateKeywordTriggerRow), against the same
    /// <see cref="AppSettings.KeywordTriggers"/> and the same defaults.
    /// </summary>
    public partial class KeywordTriggersPanel
    {
        /// <summary>WPF MessageBox.Show(text, title, OK, Information). Test seam.</summary>
        internal static Func<Window?, string, string, Task> Inform = (owner, title, text) =>
            owner == null ? Task.CompletedTask : Dialogs.MessageDialog.ShowAsync(owner, title, text);

        /// <summary>WPF OpenFileDialog "Audio Files|*.mp3;*.wav;*.ogg|All Files|*.*". Test seam.</summary>
        internal static Func<TopLevel?, Task<string?>> PickAudioFile = async top =>
        {
            if (top?.StorageProvider is not { } sp) return null;
            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.Get("title_select_trigger_audio"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Audio Files") { Patterns = new[] { "*.mp3", "*.wav", "*.ogg" } },
                    FilePickerFileTypes.All,
                },
            });
            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        };

        private static readonly string[] EffectKeys =
        {
            "kwt_effect_none", "kwt_effect_highlight_only", "kwt_effect_subliminal",
            "kwt_effect_exact_subliminal", "kwt_effect_image_flash", "kwt_effect_overlay_pulse",
            "kwt_effect_mind_wipe", "kwt_effect_bubbles",
        };

        private static IBrush Rgb(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

        /// <summary>True while rows are built, so a seeded IsChecked/Value/SelectedIndex is not an edit.</summary>
        private bool _rowsLoading;

        private StackPanel ListPanel => this.FindControl<StackPanel>("KeywordTriggerListPanel")!;

        /// <summary>The defaults WPF's add and import both use (KeywordTriggers.cs:162, KeywordTriggerService.cs:456).</summary>
        internal static KeywordTrigger NewTrigger(string keyword, string? audio) => new()
        {
            Keyword = keyword,
            MatchType = KeywordMatchType.PlainText,
            Enabled = true,
            CooldownSeconds = 30,
            AudioFilePath = audio,
            AudioVolume = 80,
            VisualEffect = KeywordVisualEffect.SubliminalFlash,
            HapticEnabled = true,
            HapticIntensity = 0.5,
            DuckAudio = true,
            XPAward = 10,
        };

        /// <summary>KeywordTriggerService.FindLinkedAudio: the active mod's clips, then sub_audio / neutral voice.</summary>
        internal static string? FindLinkedAudio(string keyword) =>
            string.IsNullOrWhiteSpace(keyword) ? null
            : SubliminalWhisper.FindLinkedAudio(keyword, SubliminalWhisper.ModAudioDir(App.Mods?.ActiveMod?.InstalledPath),
                SubliminalWhisperShow.SubAudioDir, App.Mods?.ActiveModId);

        internal void AddTrigger()
        {
            var t = NewTrigger("", null);
            t.RebuildActionsFromFlatFields();
            CoreSettings.Current.KeywordTriggers.Add(t);
            CoreSettings.Save();
            RefreshTriggerList();
        }

        /// <summary>KeywordTriggerService.ImportFromCustomTriggers + BtnImportFromCustomTriggers_Click.</summary>
        internal async Task ImportFromCustomTriggersAsync()
        {
            var s = CoreSettings.Current;
            var existing = new System.Collections.Generic.HashSet<string>(s.KeywordTriggers.Select(t => t.Keyword.ToUpperInvariant()));
            var imported = (s.CustomTriggers ?? new())
                .Where(c => !string.IsNullOrWhiteSpace(c) && !existing.Contains(c.ToUpperInvariant()))
                .Select(c => NewTrigger(c, FindLinkedAudio(c)))
                .ToList();
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (imported.Count == 0)
            {
                await Inform(owner, Loc.Get("title_import_complete"), Loc.Get("msg_no_new_triggers_to_import_all_existing_trigge"));
                return;
            }
            s.KeywordTriggers.AddRange(imported);
            CoreSettings.Save();
            RefreshTriggerList();
            await Inform(owner, Loc.Get("title_import_complete"), Loc.GetF("msg_imported_0_trigger_s_from_your_trigger_mode_l", imported.Count));
        }

        /// <summary>RefreshKeywordTriggerList: preset clones ("preset:" ids) stay with their preset dialogs.</summary>
        internal void RefreshTriggerList()
        {
            var panel = ListPanel;
            panel.Children.Clear();
            _rowsLoading = true;
            try
            {
                foreach (var t in CoreSettings.Current.KeywordTriggers.ToList())
                {
                    if (t?.Id?.StartsWith("preset:", StringComparison.Ordinal) == true) continue;
                    if (t != null) panel.Children.Add(CreateRow(t));
                }
            }
            finally { _rowsLoading = false; }
        }

        /// <summary>One edit to one trigger, then save - the shape of every WPF per-row handler.</summary>
        private void Edit(KeywordTrigger t, Action<KeywordTrigger> change, bool rebuild = true)
        {
            if (_rowsLoading || _isLoading) return;
            change(t);
            if (rebuild) t.RebuildActionsFromFlatFields();
            CoreSettings.Save();
        }

        private Border CreateRow(KeywordTrigger t)
        {
            var main = new StackPanel();

            // Row 1: enable + keyword + delete
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            var enable = new CheckBox { IsChecked = t.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            enable.IsCheckedChanged += (_, _) => Edit(t, x => x.Enabled = enable.IsChecked == true, rebuild: false);
            var keyword = new TextBox
            {
                Text = t.Keyword, Background = Rgb(0x25, 0x25, 0x42), Foreground = Brushes.White,
                BorderBrush = Rgb(0x50, 0x50, 0x70), BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 4, 6, 4), FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            };
            keyword.LostFocus += (_, _) => Edit(t, x =>
            {
                x.Keyword = keyword.Text ?? "";
                if (string.IsNullOrEmpty(x.AudioFilePath)) x.AudioFilePath = FindLinkedAudio(x.Keyword);
            });
            var delete = new Button
            {
                Content = "\u2716", Background = Brushes.Transparent, Foreground = Rgb(0xFF, 0x69, 0xB4),
                BorderThickness = new Thickness(0), FontSize = 14, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            delete.Click += (_, _) =>
            {
                if (CoreSettings.Current.KeywordTriggers.Remove(t)) { CoreSettings.Save(); RefreshTriggerList(); }
            };
            Grid.SetColumn(keyword, 1);
            Grid.SetColumn(delete, 2);
            top.Children.Add(enable);
            top.Children.Add(keyword);
            top.Children.Add(delete);
            main.Children.Add(top);

            // Row 2: audio file + browse + visual effect
            var settingsRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 6, 0, 0) };
            var hasAudio = !string.IsNullOrEmpty(t.AudioFilePath);
            var audio = new TextBlock
            {
                Text = hasAudio ? Path.GetFileName(t.AudioFilePath) : Loc.Get("kwt_no_audio"),
                Foreground = hasAudio ? Rgb(0xFF, 0x69, 0xB4) : Rgb(0x80, 0x80, 0x80),
                FontSize = 10, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var browse = new Button
            {
                Content = new TextBlock { Text = Loc.Get("btn_browse") }, Background = Rgb(0x35, 0x35, 0x50),
                Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 10,
                Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 0, 0),
            };
            browse.Click += async (_, _) =>
            {
                var path = await PickAudioFile(TopLevel.GetTopLevel(this));
                if (string.IsNullOrEmpty(path)) return;
                t.AudioFilePath = path;
                t.RebuildActionsFromFlatFields();
                CoreSettings.Save();
                RefreshTriggerList();
            };
            var effect = new ComboBox { Margin = new Thickness(6, 0, 0, 0), MinWidth = 100 };
            if (this.TryFindResource("DarkComboBoxStyle", out var theme) && theme is global::Avalonia.Styling.ControlTheme ct) effect.Theme = ct;
            foreach (var key in EffectKeys) effect.Items.Add(Loc.Get(key));
            effect.SelectedIndex = (int)t.VisualEffect < EffectKeys.Length ? (int)t.VisualEffect : -1;
            effect.SelectionChanged += (_, _) =>
            {
                if (effect.SelectedIndex >= 0) Edit(t, x => x.VisualEffect = (KeywordVisualEffect)effect.SelectedIndex);
            };
            Grid.SetColumn(browse, 1);
            Grid.SetColumn(effect, 2);
            settingsRow.Children.Add(audio);
            settingsRow.Children.Add(browse);
            settingsRow.Children.Add(effect);
            main.Children.Add(settingsRow);

            // Row 3: cooldown + volume + haptic + duck
            var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            options.Children.Add(Caption(Loc.Get("kwt_cd"), 0x80, new Thickness(0, 0, 4, 0)));
            var cooldown = new Slider { Minimum = 1, Maximum = 300, Value = t.CooldownSeconds, Width = 60, VerticalAlignment = VerticalAlignment.Center };
            var cooldownText = Caption($"{t.CooldownSeconds}s", 0xA0, new Thickness(4, 0, 10, 0));
            cooldown.ValueChanged += (_, e) =>
            {
                cooldownText.Text = $"{(int)e.NewValue}s";   // WPF froze this label at build time
                Edit(t, x => x.CooldownSeconds = (int)e.NewValue, rebuild: false);
            };
            options.Children.Add(cooldown);
            options.Children.Add(cooldownText);
            options.Children.Add(Caption(Loc.Get("kwt_vol"), 0x80, new Thickness(0, 0, 4, 0)));
            var volume = new Slider { Minimum = 0, Maximum = 100, Value = t.AudioVolume, Width = 50, VerticalAlignment = VerticalAlignment.Center };
            volume.ValueChanged += (_, e) => Edit(t, x => x.AudioVolume = (int)e.NewValue);
            options.Children.Add(volume);
            var haptic = Toggle(Loc.Get("label_allow_haptic"), t.HapticEnabled);
            haptic.IsCheckedChanged += (_, _) => Edit(t, x => x.HapticEnabled = haptic.IsChecked == true);
            var duck = Toggle(Loc.Get("label_duck"), t.DuckAudio);
            duck.IsCheckedChanged += (_, _) => Edit(t, x => x.DuckAudio = duck.IsChecked == true);
            options.Children.Add(haptic);
            options.Children.Add(duck);
            main.Children.Add(options);

            return new Border
            {
                Background = Rgb(0x1E, 0x1E, 0x3A), CornerRadius = new CornerRadius(6), Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 6), BorderBrush = Rgb(0x35, 0x35, 0x50), BorderThickness = new Thickness(1),
                Child = main, Tag = t.Id,
            };

            static TextBlock Caption(string text, byte grey, Thickness margin) => new()
            {
                Text = text, Foreground = Rgb(grey, grey, grey), FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center, Margin = margin,
            };
            static CheckBox Toggle(string text, bool on) => new()
            {
                Content = new TextBlock { Text = text }, IsChecked = on, Foreground = Rgb(0xA0, 0xA0, 0xA0),
                FontSize = 10, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            };
        }

        private void OnLanguageChanged(object? sender, EventArgs e) => RefreshTriggerList();

        /// <summary>KeywordTriggerService.HasAccess: premium, or the awareness free day.</summary>
        internal static bool HasAccess() => CoreEntitlement.HasPremium || CoreEntitlement.IsFreeToday("awareness");
    }
}
