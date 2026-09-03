using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Subliminal settings panel, ported from the WPF head. Every editor reads and writes
    /// <see cref="CoreSettings.Current"/> and persists through <see cref="CoreSettings.Save"/>,
    /// and the settings hook is inlined for the reason spelled out in
    /// <see cref="BubbleCountFeatureControl"/>.
    ///
    /// <para>The font combo is real rather than two placeholders:
    /// <c>FontManager.Current.SystemFonts</c> is the cross-platform half of the WPF
    /// <c>Helpers.FontPickerHelper</c>, and each row previews in its own face exactly as
    /// <c>Populate</c> did. What does not carry over is that helper's <c>Fredoka (bundled)</c>
    /// sentinel: the face ships as a WPF <c>pack://</c> Resource and is not packed on this head,
    /// so offering it would name a font nothing can resolve.</para>
    ///
    /// <para>Still head-side: SubliminalService (the flash loop) and the mod-aware feature art.</para>
    /// </summary>
    public partial class SubliminalFeatureControl : UserControl
    {
        /// <summary>What the picker falls back to when the stored family is gone - the WPF
        /// <c>Populate(CmbFont, s.SubliminalFont, "Arial")</c> fallback, unchanged.</summary>
        private const string FontFallback = "Arial";

        private bool _isLoading = true;
        private AppSettings? _hooked;

        public SubliminalFeatureControl()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            PopulateFonts();

            ChkEnable.IsCheckedChanged += ChkEnable_Changed;
            SliderPerMin.ValueChanged += SliderPerMin_Changed;
            SliderFrames.ValueChanged += SliderFrames_Changed;
            SliderOpacity.ValueChanged += SliderOpacity_Changed;
            ChkWhispers.IsCheckedChanged += ChkWhispers_Changed;
            SliderWhisperVol.ValueChanged += SliderWhisperVol_Changed;
            ChkSolidMode.IsCheckedChanged += ChkSolidMode_Changed;
            CmbFont.SelectionChanged += CmbFont_Changed;
            BtnManageMessages.Click += BtnManageMessages_Click;
            BtnAdvanced.Click += BtnAdvanced_Click;

            Loaded += (_, _) => RebindToCurrentSettings();
            Unloaded += (_, _) => Unhook();

            // ponytail: WPF also repaints the hero and side plates on ModChanged through
            // Services/ModResourceResolver.cs (WPF head) from Resources/features/subliminal.png.
            // The Avalonia .axaml drops both plates deliberately, so nothing here to repaint yet.

            RebindToCurrentSettings();
        }

        /// <summary>Re-points the settings hook at the live instance and repaints from it.</summary>
        public void RebindToCurrentSettings()
        {
            Unhook();
            _hooked = CoreSettings.Current;
            _hooked.PropertyChanged += OnSettingsPropertyChanged;
            LoadFromSettings();
        }

        private void Unhook()
        {
            if (_hooked != null) _hooked.PropertyChanged -= OnSettingsPropertyChanged;
            _hooked = null;
        }

        /// <summary>
        /// Built once, in the constructor. The settings hook re-runs
        /// <see cref="LoadFromSettings"/> on every property in this feature's chain - a slider drag
        /// included - and rebuilding several hundred rows each time would stutter, which is the
        /// same reason WPF's Populate keeps a "already filled" marker. Only the selection moves.
        /// </summary>
        private void PopulateFonts()
        {
            string[] names;
            try { names = FontManager.Current.SystemFonts.Select(f => f.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray(); }
            catch (Exception ex)
            {
                Log.Warning(ex, "Subliminal font enumeration failed, using the fallback list");
                names = Array.Empty<string>();
            }
            if (names.Length == 0) names = new[] { FontFallback };

            foreach (var name in names)
            {
                // Tag carries the value that gets stored; Content is what the row shows. Each row
                // renders in its own face, so the list reads as a real preview, as on WPF.
                CmbFont.Items.Add(new ComboBoxItem
                {
                    Content = name,
                    Tag = name,
                    FontFamily = new FontFamily($"{name}, {FontFallback}"),
                    FontSize = 14,
                });
            }
        }

        /// <summary>
        /// Moves the selection to the stored family, else to the fallback, else row 0 - WPF's
        /// <c>match ?? fallbackItem</c> chain.
        ///
        /// <para>WPF's <c>ApplySelectedFamily</c> (repainting the CLOSED box in the picked face) is
        /// deliberately not ported: setting the ComboBox's own FontFamily shifts which face
        /// Avalonia's fallback chain picks for the emoji on the sibling buttons, and a render
        /// proved it turns the colour glyphs monochrome. The per-row preview below is the part
        /// that carries the information.</para>
        /// </summary>
        private void SelectFont(string? wanted)
        {
            if (string.IsNullOrWhiteSpace(wanted)) wanted = FontFallback;
            ComboBoxItem? fallbackItem = null;
            foreach (var obj in CmbFont.Items)
            {
                if (obj is not ComboBoxItem item || item.Tag is not string name) continue;
                if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    CmbFont.SelectedItem = item;
                    return;
                }
                if (fallbackItem == null && string.Equals(name, FontFallback, StringComparison.OrdinalIgnoreCase))
                    fallbackItem = item;
            }
            if (fallbackItem != null) CmbFont.SelectedItem = fallbackItem;
            else if (CmbFont.SelectedItem == null && CmbFont.ItemCount > 0) CmbFont.SelectedIndex = 0;
        }

        private void LoadFromSettings()
        {
            var s = CoreSettings.Current;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.SubliminalEnabled;
                SliderPerMin.Value = s.SubliminalFrequency;
                TxtPerMin.Text = s.SubliminalFrequency.ToString();
                SliderFrames.Value = s.SubliminalDuration;
                TxtFrames.Text = s.SubliminalDuration.ToString();
                SliderOpacity.Value = s.SubliminalOpacity;
                TxtOpacity.Text = $"{s.SubliminalOpacity}%";
                ChkWhispers.IsChecked = s.SubAudioEnabled;
                SliderWhisperVol.Value = s.SubAudioVolume;
                TxtWhisperVol.Text = $"{s.SubAudioVolume}%";
                ChkSolidMode.IsChecked = s.SubliminalSolidMode;
                SelectFont(s.SubliminalFont);
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.SubliminalEnabled) ||
                e.PropertyName == nameof(AppSettings.SubliminalFrequency) ||
                e.PropertyName == nameof(AppSettings.SubliminalDuration) ||
                e.PropertyName == nameof(AppSettings.SubliminalOpacity) ||
                e.PropertyName == nameof(AppSettings.SubAudioEnabled) ||
                e.PropertyName == nameof(AppSettings.SubAudioVolume) ||
                e.PropertyName == nameof(AppSettings.SubliminalSolidMode) ||
                e.PropertyName == nameof(AppSettings.SubliminalFont))
            {
                Dispatcher.UIThread.Post(LoadFromSettings);
            }
        }

        /// <summary>
        /// WPF hands the whole toggle to <c>App.Subliminal.SetEnabled</c>, which is the single
        /// authority. Its settings half - compare, write, save, log - is restored here verbatim;
        /// only the idempotent Start/Stop it also does still needs the service.
        /// </summary>
        private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var on = ChkEnable.IsChecked ?? false;
            if (s.SubliminalEnabled == on) return;
            s.SubliminalEnabled = on;
            // ponytail: SubliminalService.SetEnabled also does the live Start()/Stop() gated on
            // App.IsEngineRunning - ConditioningControlPanel/Services/Subliminal/SubliminalService.cs,
            // still in the WPF head. Route this whole handler back through it when it moves.
            CoreSettings.Save();
            Log.Information("Subliminals toggled: {Enabled}", on);
        }

        private void SliderPerMin_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)e.NewValue;
            TxtPerMin.Text = v.ToString();
            if (s.SubliminalFrequency == v) return;
            s.SubliminalFrequency = v;
            CoreSettings.Save();
        }

        private void SliderFrames_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)e.NewValue;
            TxtFrames.Text = v.ToString();
            if (s.SubliminalDuration == v) return;
            s.SubliminalDuration = v;
            CoreSettings.Save();
        }

        private void SliderOpacity_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)e.NewValue;
            TxtOpacity.Text = $"{v}%";
            if (s.SubliminalOpacity == v) return;
            s.SubliminalOpacity = v;
            CoreSettings.Save();
        }

        private void ChkWhispers_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var on = ChkWhispers.IsChecked ?? false;
            if (s.SubAudioEnabled == on) return;
            s.SubAudioEnabled = on;
            CoreSettings.Save();
        }

        private void SliderWhisperVol_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)e.NewValue;
            TxtWhisperVol.Text = $"{v}%";
            if (s.SubAudioVolume == v) return;
            s.SubAudioVolume = v;
            CoreSettings.Save();
        }

        private void ChkSolidMode_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var on = ChkSolidMode.IsChecked ?? false;
            if (s.SubliminalSolidMode == on) return;
            s.SubliminalSolidMode = on;
            CoreSettings.Save();
            // No service bounce needed: each show reads the setting, so the next subliminal
            // uses the new renderer. An in-flight card finishes out on whichever spawned it.
        }

        // No service bounce: the text blocks are built per flash, so the next subliminal picks
        // the new face up on its own.
        private void CmbFont_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var name = (CmbFont.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrWhiteSpace(name) || s.SubliminalFont == name) return;
            s.SubliminalFont = name;
            CoreSettings.Save();
        }

        /// <summary>
        /// The pool editor, plus the bookkeeping that keeps a mod's top-up honest: phrases the
        /// user added by hand are remembered so a cross-mod prune never deletes them, and a
        /// DELETED DEFAULT is recorded so ModService's top-up does not put it straight back on the
        /// next launch (#892).
        /// </summary>
        private async void BtnManageMessages_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var s = CoreSettings.Current;

            var oldKeys = new HashSet<string>(s.SubliminalPool.Keys);
            // OrdinalIgnoreCase: the removed-set and ModService's top-up compare
            // case-insensitively, so detection must too or modded defaults slip through.
            // ponytail: WPF asks App.Mods.GetDefaultSubliminalPool() first
            // (ConditioningControlPanel/Services/ModService.cs:1113, no Core seam yet) and only
            // falls back to BuiltInMods. With no mod layer on this head the fallback IS the
            // answer, which is the branch WPF takes for a null App.Mods too.
            var defaults = new HashSet<string>(
                (BuiltInMods.BambiSleep.SubliminalPool ?? new Dictionary<string, bool>()).Keys,
                StringComparer.OrdinalIgnoreCase);

            var dialog = new Dialogs.TextEditorDialog("Subliminal Messages", s.SubliminalPool);
            if (await dialog.ShowDialog<bool?>(owner) != true || dialog.ResultData == null) return;

            // Remember hand-added phrases (and forget removed ones) so the cross-mod prune
            // never silently deletes a custom phrase that collides with another mod's default.
            var newKeys = new HashSet<string>(dialog.ResultData.Keys);
            foreach (var key in newKeys)
                if (!oldKeys.Contains(key)) s.UserAddedSubliminals.Add(key);
            foreach (var key in oldKeys)
                if (!newKeys.Contains(key)) s.UserAddedSubliminals.Remove(key);

            // Record deleted DEFAULTS, or ModService's top-up puts them straight back on the
            // next launch — the phrase the user deliberately deleted returns forever (#892).
            foreach (var key in oldKeys)
                if (!newKeys.Contains(key) && defaults.Contains(key))
                    s.RemovedDefaultSubliminals.Add(key);
            // A default they added back is no longer "removed".
            foreach (var key in newKeys)
                s.RemovedDefaultSubliminals.Remove(key);

            s.SubliminalPool = dialog.ResultData;
            CoreSettings.Save();
            Log.Information("Subliminal pool updated: {Count} items", dialog.ResultData.Count);
        }

        private async void BtnAdvanced_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            await new Dialogs.ColorEditorDialog().ShowDialog(owner);
        }
    }
}
