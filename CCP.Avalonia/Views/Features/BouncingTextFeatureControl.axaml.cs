using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Bouncing Text panel, ported from the WPF head, against <see cref="CoreSettings"/>.
    ///
    /// <para>WPF's <c>ISettingsRebindable</c> + <c>SettingsHook</c> pair is reproduced inline: a
    /// cloud restore SWAPS the AppSettings instance, so the PropertyChanged subscription is
    /// tracked per instance and re-pointed on <c>SettingsService.CurrentReplaced</c>.</para>
    ///
    /// <para>The phrase editor is the ported <see cref="TextEditorDialog"/>, so it is real here;
    /// Avalonia's ShowDialog is async and needs an owner Window, which is the only shape change
    /// from the WPF handler. The live-apply calls into BouncingTextService are named at each
    /// handler - that service draws Win32 layered windows and has no port.</para>
    /// </summary>
    public partial class BouncingTextFeatureControl : UserControl
    {
        /// <summary>WPF's <c>FontPickerHelper.Populate</c> fallback family. Kept verbatim so a
        /// settings file written on either head selects the same row.</summary>
        private const string FontFallback = "Segoe UI";

        private bool _isLoading = true;
        private bool _fontsPopulated;
        private AppSettings? _hooked;

        public BouncingTextFeatureControl()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            ChkEnable.IsCheckedChanged += ChkEnable_Changed;
            SliderSpeed.ValueChanged += SliderSpeed_Changed;
            SliderSize.ValueChanged += SliderSize_Changed;
            SliderOpacity.ValueChanged += SliderOpacity_Changed;
            CmbColorMode.SelectionChanged += CmbColorMode_Changed;
            CmbFont.SelectionChanged += CmbFont_Changed;
            BtnChooseColor.Click += (_, _) =>
            {
                // ponytail: needs a colour picker. WPF opens System.Windows.Forms.ColorDialog;
                // Avalonia's equivalent is the Avalonia.Controls.ColorPicker package, which is
                // NOT referenced by CCP.Avalonia.csproj (a csproj edit is the coordinator's call).
            };
            BtnResetColor.Click += BtnResetColor_Click;
            ChkFxBreathing.IsCheckedChanged += FxToggle_Changed;
            ChkFxWobble.IsCheckedChanged += FxToggle_Changed;
            ChkFxSpin.IsCheckedChanged += FxToggle_Changed;
            ChkFxVelocityTilt.IsCheckedChanged += FxToggle_Changed;
            ChkFxSquash.IsCheckedChanged += FxToggle_Changed;
            ChkFxCornerBurst.IsCheckedChanged += FxToggle_Changed;
            ChkOutline.IsCheckedChanged += ChkOutline_Changed;
            ChkSecondText.IsCheckedChanged += ChkSecondText_Changed;
            ChkAlwaysOnTop.IsCheckedChanged += ChkAlwaysOnTop_Changed;
            BtnEditPhrases.Click += BtnEditPhrases_Click;

            LoadFromSettings();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += RebindToCurrentSettings;
            RebindToCurrentSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= RebindToCurrentSettings;
            Unhook();
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>WPF's <c>ISettingsRebindable.RebindToCurrentSettings</c>: detach from whichever
        /// instance we were on, attach to the live one, repaint from it.</summary>
        private void RebindToCurrentSettings()
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

        private void LoadFromSettings()
        {
            var s = CoreSettings.Current;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.BouncingTextEnabled;
                SliderSpeed.Value = s.BouncingTextSpeed;
                TxtSpeed.Text = s.BouncingTextSpeed.ToString();
                SliderSize.Value = s.BouncingTextSize;
                TxtSize.Text = $"{s.BouncingTextSize}%";
                SliderOpacity.Value = Math.Max(10, s.BouncingTextOpacity);
                TxtOpacity.Text = $"{s.BouncingTextOpacity}%";
                CmbColorMode.SelectedIndex = s.BouncingTextColorMode;
                ChkFxBreathing.IsChecked = s.BouncingTextFxBreathing;
                ChkFxWobble.IsChecked = s.BouncingTextFxWobble;
                ChkFxSpin.IsChecked = s.BouncingTextFxSpin;
                ChkFxVelocityTilt.IsChecked = s.BouncingTextFxVelocityTilt;
                ChkFxSquash.IsChecked = s.BouncingTextFxSquashStretch;
                ChkFxCornerBurst.IsChecked = s.BouncingTextFxCornerBurst;
                ChkOutline.IsChecked = s.BouncingTextOutline;
                ChkSecondText.IsChecked = s.BouncingTextSecondText;
                ChkAlwaysOnTop.IsChecked = s.BouncingTextAlwaysOnTop;
                PopulateFonts(s.BouncingTextFont);
                UpdateSwatch();
                UpdateFixedColorPanel();
            }
            finally { _isLoading = false; }
        }

        /// <summary>Reflects external writes (Ramp, presets, the session engine) back into the
        /// panel. Marshalled: those writers are not all on the UI thread.</summary>
        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.BouncingTextEnabled) ||
                e.PropertyName == nameof(AppSettings.BouncingTextSpeed) ||
                e.PropertyName == nameof(AppSettings.BouncingTextSize) ||
                e.PropertyName == nameof(AppSettings.BouncingTextOpacity) ||
                e.PropertyName == nameof(AppSettings.BouncingTextColorMode) ||
                e.PropertyName == nameof(AppSettings.BouncingTextFixedColor) ||
                e.PropertyName == nameof(AppSettings.BouncingTextFont) ||
                e.PropertyName == nameof(AppSettings.BouncingTextFxBreathing) ||
                e.PropertyName == nameof(AppSettings.BouncingTextFxWobble) ||
                e.PropertyName == nameof(AppSettings.BouncingTextFxSpin) ||
                e.PropertyName == nameof(AppSettings.BouncingTextFxVelocityTilt) ||
                e.PropertyName == nameof(AppSettings.BouncingTextFxSquashStretch) ||
                e.PropertyName == nameof(AppSettings.BouncingTextFxCornerBurst) ||
                e.PropertyName == nameof(AppSettings.BouncingTextOutline) ||
                e.PropertyName == nameof(AppSettings.BouncingTextSecondText) ||
                e.PropertyName == nameof(AppSettings.BouncingTextAlwaysOnTop))
            {
                Dispatcher.UIThread.Post(LoadFromSettings);
            }
        }

        // =====================================================================================
        //  handlers
        // =====================================================================================

        private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var want = ChkEnable.IsChecked ?? false;
            if (s.BouncingTextEnabled == want) return;   // an echo of the seed must not save
            s.BouncingTextEnabled = want;
            CoreSettings.Save();
            // ponytail: WPF then live-applies through App.BouncingText.Start()/Stop() while
            // App.IsEngineRunning (ConditioningControlPanel/Services/Subliminal/BouncingTextService.cs),
            // still in the WPF head - it draws Win32 layered windows.
        }

        private void SliderSpeed_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtSpeed.Text = v.ToString();
            CoreSettings.Current.BouncingTextSpeed = v;
            SafeRefresh();
            CoreSettings.Save();
        }

        private void SliderSize_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtSize.Text = $"{v}%";
            CoreSettings.Current.BouncingTextSize = v;
            SafeRefresh();
            CoreSettings.Save();
        }

        private void SliderOpacity_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtOpacity.Text = $"{v}%";
            CoreSettings.Current.BouncingTextOpacity = v;
            SafeRefresh();
            CoreSettings.Save();
        }

        private void CmbColorMode_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var mode = CmbColorMode.SelectedIndex;
            var s = CoreSettings.Current;
            if (mode < 0 || s.BouncingTextColorMode == mode) return;
            s.BouncingTextColorMode = mode;
            CoreSettings.Save();
            UpdateFixedColorPanel();
            SafeRefresh();
        }

        /// <summary>The service re-measures and pushes the family into the live windows on Refresh,
        /// so the pick applies mid-run without a restart (unlike the outline toggle, which swaps
        /// element types).</summary>
        private void CmbFont_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var name = (CmbFont.SelectedItem as ComboBoxItem)?.Tag as string;
            var s = CoreSettings.Current;
            if (string.IsNullOrWhiteSpace(name) || s.BouncingTextFont == name) return;
            s.BouncingTextFont = name!;
            CoreSettings.Save();
            SafeRefresh();
        }

        private void BtnResetColor_Click(object? sender, RoutedEventArgs e)
        {
            CoreSettings.Current.BouncingTextFixedColor = ""; // empty = hot pink default
            CoreSettings.Save();
            UpdateSwatch();
            SafeRefresh();
        }

        /// <summary>The transform effects are read per-frame by the service, so writing the setting
        /// is all a live apply needs. One handler for all six, as on WPF.</summary>
        private void FxToggle_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            // Compare before writing: every AppSettings setter raises PropertyChanged whether or
            // not the value moved, and this handler fires for all six boxes on any one of them.
            bool changed =
                Set(ChkFxBreathing, s.BouncingTextFxBreathing, v => s.BouncingTextFxBreathing = v) |
                Set(ChkFxWobble, s.BouncingTextFxWobble, v => s.BouncingTextFxWobble = v) |
                Set(ChkFxSpin, s.BouncingTextFxSpin, v => s.BouncingTextFxSpin = v) |
                Set(ChkFxVelocityTilt, s.BouncingTextFxVelocityTilt, v => s.BouncingTextFxVelocityTilt = v) |
                Set(ChkFxSquash, s.BouncingTextFxSquashStretch, v => s.BouncingTextFxSquashStretch = v) |
                Set(ChkFxCornerBurst, s.BouncingTextFxCornerBurst, v => s.BouncingTextFxCornerBurst = v);
            if (changed) CoreSettings.Save();

            static bool Set(CheckBox box, bool stored, Action<bool> write)
            {
                var want = box.IsChecked ?? false;
                if (stored == want) return false;
                write(want);
                return true;
            }
        }

        /// <summary>Outlined style swaps the rendered element type, so a running instance restarts.</summary>
        private void ChkOutline_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var want = ChkOutline.IsChecked ?? false;
            if (s.BouncingTextOutline == want) return;
            s.BouncingTextOutline = want;
            CoreSettings.Save();
            SafeRestart();
        }

        /// <summary>The second logo needs its own window visuals, so a running instance restarts.</summary>
        private void ChkSecondText_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var want = ChkSecondText.IsChecked ?? false;
            if (s.BouncingTextSecondText == want) return;
            s.BouncingTextSecondText = want;
            CoreSettings.Save();
            SafeRestart();
        }

        private void ChkAlwaysOnTop_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var want = ChkAlwaysOnTop.IsChecked ?? false;
            if (s.BouncingTextAlwaysOnTop == want) return;
            s.BouncingTextAlwaysOnTop = want;
            CoreSettings.Save();
            SafeRefresh(); // applies mid-video (pause/resume the loop accordingly)
        }

        /// <summary>
        /// The phrase pool editor. Avalonia's ShowDialog is async and needs a non-null owner, so
        /// this awaits where WPF blocked; the write-back afterwards is the WPF handler unchanged.
        /// The dialog title is the WPF literal, not a loc key - the original hard-codes it.
        /// </summary>
        private async void BtnEditPhrases_Click(object? sender, RoutedEventArgs e)
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is null) return;   // detached, or a headless render: nothing to own the modal

            var s = CoreSettings.Current;
            var editor = new TextEditorDialog("Bouncing Text Phrases", s.BouncingTextPool);
            try
            {
                if (await editor.ShowDialog<bool?>(owner) == true && editor.ResultData != null)
                {
                    s.BouncingTextPool = editor.ResultData;
                    CoreSettings.Save();
                    Log.Information("Bouncing text phrases updated: {Count} items", editor.ResultData.Count);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Bouncing text phrase editor failed");
            }
        }

        // ponytail: both need App.BouncingText
        // (ConditioningControlPanel/Services/Subliminal/BouncingTextService.cs), still in the WPF
        // head. Kept as named no-ops so the call ORDER around them stays the WPF one.
        private static void SafeRefresh() { }

        private static void SafeRestart() { }

        // =====================================================================================
        //  view state
        // =====================================================================================

        private void UpdateFixedColorPanel()
            => PanelFixedColor.IsVisible = CmbColorMode.SelectedIndex == 1;

        private void UpdateSwatch()
        {
            var (r, g, b) = EffectiveColor();
            ColorSwatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        /// <summary>The colour Fixed mode renders: the user's pick if set, else hot pink.
        /// Mirrors BouncingTextService.GetFixedColor.</summary>
        private static (byte R, byte G, byte B) EffectiveColor()
        {
            if (TryParseHex(CoreSettings.Current.BouncingTextFixedColor, out var rgb)) return rgb;
            return (255, 105, 180);
        }

        private static bool TryParseHex(string? hex, out (byte R, byte G, byte B) rgb)
        {
            rgb = (255, 105, 180);
            if (string.IsNullOrWhiteSpace(hex)) return false;
            hex = hex.Trim().TrimStart('#');
            if (hex.Length != 6) return false;
            try
            {
                rgb = (Convert.ToByte(hex.Substring(0, 2), 16),
                       Convert.ToByte(hex.Substring(2, 2), 16),
                       Convert.ToByte(hex.Substring(4, 2), 16));
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// WPF calls <c>Helpers.FontPickerHelper.Populate(CmbFont, s.BouncingTextFont, "Segoe UI")</c>
        /// - a WinForms-era enumeration of installed families plus the bundled Fredoka pack:// face.
        /// <see cref="FontManager"/> is cross-platform, so the installed half is real here; the
        /// Fredoka sentinel is not, because that face ships as a WPF pack resource.
        ///
        /// <para>Built once, as WPF's cheap path is: <see cref="LoadFromSettings"/> re-runs on every
        /// property in the chain (a slider drag included), and rebuilding several hundred items each
        /// time would stutter. After that only the selection moves.</para>
        ///
        /// <para>Each row carries the stored value in <c>Tag</c> and its own face, so the list reads
        /// as a real preview. A headless run may enumerate nothing, hence the fallback row.</para>
        /// </summary>
        private void PopulateFonts(string? current)
        {
            var wanted = string.IsNullOrWhiteSpace(current) ? FontFallback : current!.Trim();

            if (!_fontsPopulated)
            {
                string[] names;
                try
                {
                    names = FontManager.Current.SystemFonts
                        .Select(f => f.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "BouncingText: font enumeration failed, using the fallback row");
                    names = Array.Empty<string>();
                }
                if (names.Length == 0) names = new[] { FontFallback };

                foreach (var n in names)
                    CmbFont.Items.Add(new ComboBoxItem { Content = n, Tag = n, FontFamily = new FontFamily(n), FontSize = 14 });
                _fontsPopulated = true;
            }

            // Selection, WPF's order: the stored pick, else the fallback family, else the first row.
            ComboBoxItem? match = null, fallback = null;
            foreach (var obj in CmbFont.Items)
            {
                if (obj is not ComboBoxItem item || item.Tag is not string name) continue;
                if (match == null && string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)) match = item;
                if (fallback == null && string.Equals(name, FontFallback, StringComparison.OrdinalIgnoreCase)) fallback = item;
            }
            CmbFont.SelectedItem = match ?? fallback;
            if (CmbFont.SelectedItem == null && CmbFont.Items.Count > 0) CmbFont.SelectedIndex = 0;
        }
    }
}
