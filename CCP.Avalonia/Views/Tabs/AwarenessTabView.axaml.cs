using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/AwarenessTabView.xaml.cs.
    ///
    /// <para>The WPF code-behind is pure re-hosting - eighteen three-line thunks into
    /// <c>MainWindow</c> - so the bodies ported here are MainWindow.Awareness.cs's and
    /// MainWindow.KeywordTriggers.cs's, not the tab's. Their settings half is restored against
    /// <see cref="CoreSettings"/>: the seed (SyncAwarenessTabUI), the two cooldown sliders, the
    /// master switch and its sub-toggle, OCR, ignore-own-UI, loop protection, highlight on/off,
    /// capture visibility, ignore-own-focus, the app scope mode and the highlight colour all read
    /// and write <c>AppSettings</c> for real and save.</para>
    ///
    /// <para>What is NOT restored is the runtime: nothing here starts or stops an engine. The
    /// Patreon gate (<c>KeywordTriggerService.HasAccess</c>), the keyboard hook, ScreenOcr,
    /// KeywordHighlight's overlay windows, the recently-seen-app ring, the tutorial and the preset
    /// editor are all head-side, and each stub below names the one it wants.</para>
    /// </summary>
    public partial class AwarenessTabView : UserControl
    {
        /// <summary>
        /// True while the seed is writing the controls, so the live editors do not save the value
        /// they were just handed. Starts true because the XAML wires <c>IsCheckedChanged</c>
        /// itself and two boxes carry <c>IsChecked="True"</c>, i.e. a handler fires from inside
        /// InitializeComponent, before the seed has run. MainWindow's <c>_isLoading</c>, scoped to
        /// the one view that uses it.
        /// </summary>
        private bool _isLoading = true;

        /// <summary>The off-state dot fill, straight out of the WPF XAML (Fill="#606060").</summary>
        private static readonly IBrush OffDot = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));

        /// <summary>The off-state status label colour (UpdateAwarenessStatusIndicator, "#A0A0A0").</summary>
        private static readonly IBrush OffLabel = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));

        /// <summary>Unselected swatch outline, from SyncAwarenessHighlightSwatchUi.</summary>
        private static readonly IBrush SwatchIdle = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A));

        private Border[] Swatches => new[]
        {
            SwatchHighlightPink, SwatchHighlightCyan, SwatchHighlightLime,
            SwatchHighlightOrange, SwatchHighlightViolet, SwatchHighlightWhite,
        };

        public AwarenessTabView()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields this code-behind reads.
            InitializeComponent();

            // WPF's Slider.ValueChanged forwards to MainWindow, which writes the setting AND the
            // label. Wired here rather than in XAML so the seed below can move the sliders before
            // the handler exists to react to it.
            SliderAwarenessGlobalCooldown.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty) return;
                var value = (int)SliderAwarenessGlobalCooldown.Value;
                TxtAwarenessGlobalCooldown.Text = $"{value}s";
                if (_isLoading) return;
                CoreSettings.Current.KeywordGlobalCooldownSeconds = value;
                CoreSettings.Save();
            };
            SliderAwarenessSameWordCooldown.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty) return;
                var value = (int)SliderAwarenessSameWordCooldown.Value;
                TxtAwarenessSameWordCooldown.Text = $"{value}s";
                if (_isLoading) return;
                CoreSettings.Current.KeywordPerKeywordCooldownSeconds = value;
                CoreSettings.Save();
            };

            // The drawer ships shut on WPF (IsExpanded="False"); the Avalonia panel ships open so
            // its own --render-view proof shows an interior. Its host owns the real state, so the
            // Awareness tab closes it the way MainWindow does.
            foreach (var ex in this.GetLogicalDescendants().OfType<Expander>())
                if (ex.Name == "KeywordTriggersExpander")
                    ex.SetCurrentValue(Expander.IsExpandedProperty, false);

            SyncAwarenessTabUi();

            // Tabs are shown and hidden rather than rebuilt, so re-read on every show: the master
            // switch is [JsonIgnore] session state and the app list can be edited elsewhere.
            AttachedToVisualTree += (_, _) => SyncAwarenessTabUi();
            PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty && IsVisible) SyncAwarenessTabUi();
            };
        }

        // ------------------------------------------------------------------ seed

        /// <summary>
        /// The settings half of MainWindow.SyncAwarenessTabUI, plus RefreshAwarenessAppScopeUi and
        /// SyncAwarenessHighlightSwatchUi, which it calls. Paints, never writes back.
        /// </summary>
        private void SyncAwarenessTabUi()
        {
            try
            {
                var s = CoreSettings.Current;
                _isLoading = true;

                var masterOn = s.KeywordTriggersEnabled;
                Set(ChkAwarenessMaster, masterOn);
                Set(ChkAwarenessOcr, s.ScreenOcrEnabled);
                // The keyboard sub-toggle mirrors the master, as on WPF: it is one signal source,
                // and the master is what actually arms it.
                Set(ChkAwarenessKeyboard, masterOn);
                Set(ChkAwarenessIgnoreOwnUi, s.AwarenessIgnoreOwnUi);
                Set(ChkAwarenessLoopProtection, s.AwarenessLoopProtectionEnabled);
                Set(ChkAwarenessHighlight, s.KeywordHighlightEnabled);
                Set(ChkAwarenessHighlightVisibleInCapture, s.OcrHighlightVisibleInCapture);
                Set(ChkAwarenessIgnoreOwnFocus, s.KeywordTriggerIgnoreOwnFocus);

                SyncHighlightSwatchUi(s.KeywordHighlightColor);

                // Clamped to the slider's own range: the settings clamp is wider (1-300 / 1-600)
                // and a value past Maximum would be silently coerced back and then saved.
                SliderAwarenessGlobalCooldown.Value = Math.Clamp(s.KeywordGlobalCooldownSeconds, 1, 180);
                SliderAwarenessSameWordCooldown.Value = Math.Clamp(s.KeywordPerKeywordCooldownSeconds, 1, 180);

                // Matched on Tag rather than index so reordering the XAML items cannot silently
                // remap a saved setting onto the wrong mode.
                var wanted = s.KeywordTriggerAppScope.ToString();
                foreach (var item in CmbAwarenessAppScope.Items.OfType<ComboBoxItem>())
                    if (string.Equals(item.Tag as string, wanted, StringComparison.Ordinal))
                        CmbAwarenessAppScope.SelectedItem = item;

                TxtAwarenessAppList.Text = string.Join(", ", s.KeywordTriggerApps ?? new());
                AwarenessAppListPanel.IsVisible = s.KeywordTriggerAppScope != AwarenessAppScope.Everywhere;

                UpdateStatusIndicator(masterOn);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Awareness tab: failed to load settings");
            }
            finally
            {
                _isLoading = false;
            }

            // Assign only on a real difference: Avalonia raises IsCheckedChanged on a programmatic
            // set too, and every handler below is a live editor.
            static void Set(CheckBox box, bool value)
            {
                if ((box.IsChecked ?? false) != value) box.IsChecked = value;
            }
        }

        /// <summary>The dot and the Live/Off label beside the master switch.</summary>
        private void UpdateStatusIndicator(bool on)
        {
            var pink = this.FindResource("PinkBrush") as IBrush;
            AwarenessStatusDot.Fill = on ? pink ?? Brushes.HotPink : OffDot;
            TxtAwarenessStatus.Text = on ? "Live" : "Off";
            TxtAwarenessStatus.Foreground = on ? pink ?? Brushes.HotPink : OffLabel;

            // ponytail: WPF also breathes the dot while the engine is genuinely live
            // (SetAwarenessStatusPulse). Cosmetic, and it belongs with the engine seam.
        }

        // ------------------------------------------------------------------ live editors

        /// <summary>
        /// Master switch. Writes the setting, keeps the keyboard sub-toggle and the status
        /// indicator in step, and saves - the whole of ChkAwarenessMaster_Changed except starting
        /// anything.
        /// </summary>
        private void ChkAwarenessMaster_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                var on = ChkAwarenessMaster.IsChecked == true;

                // ponytail: WPF gates ON behind KeywordTriggerService.HasAccess() and bounces the
                // box with a Patreon message box. Both are head-side (cloud identity + a dialog);
                // no Core seam, so this head lets the toggle through.

                CoreSettings.Current.KeywordTriggersEnabled = on;

                // ponytail: this is where WPF starts/stops App.KeywordTriggers, the keyboard hook
                // and App.ScreenOcr. All three are Win32 on that head; nothing arms here.

                _isLoading = true;
                try { if ((ChkAwarenessKeyboard.IsChecked ?? false) != on) ChkAwarenessKeyboard.IsChecked = on; }
                finally { _isLoading = false; }

                UpdateStatusIndicator(on);
                CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Awareness tab: failed to write KeywordTriggersEnabled");
            }
        }

        /// <summary>
        /// Keyboard is one signal source, toggled independently - but turning it on with the master
        /// off turns the master on, which is what actually arms it. Turning it off leaves the
        /// master alone: OCR may still want the engine.
        /// </summary>
        private void ChkAwarenessKeyboard_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (ChkAwarenessKeyboard.IsChecked == true && ChkAwarenessMaster.IsChecked != true)
                ChkAwarenessMaster.IsChecked = true;   // deliberately outside the guard: routes through the master handler

            // ponytail: turning it OFF drops the keyboard hook on WPF when nothing else needs it
            // (the panic key, OCR). The hook is Win32 and head-side.
        }

        private void ChkAwarenessOcr_Changed(object? sender, RoutedEventArgs e)
        {
            // ponytail: WPF gates ON behind KeywordTriggerService.HasAccess() with a Patreon
            // message box, starts/stops App.ScreenOcr, and calls SyncKeywordRescuePanelUi() to
            // show the scan-interval / confirmation rows. The OCR engine is head-side, and the
            // rescue rows live in KeywordTriggersPanel.
            WriteFlag(v => CoreSettings.Current.ScreenOcrEnabled = v, ChkAwarenessOcr, "ScreenOcrEnabled");
        }

        private void ChkAwarenessIgnoreOwnUi_Changed(object? sender, RoutedEventArgs e)
            => WriteFlag(v => CoreSettings.Current.AwarenessIgnoreOwnUi = v,
                         ChkAwarenessIgnoreOwnUi, "AwarenessIgnoreOwnUi");

        private void ChkAwarenessLoopProtection_Changed(object? sender, RoutedEventArgs e)
            => WriteFlag(v => CoreSettings.Current.AwarenessLoopProtectionEnabled = v,
                         ChkAwarenessLoopProtection, "AwarenessLoopProtectionEnabled");

        private void ChkAwarenessIgnoreOwnFocus_Changed(object? sender, RoutedEventArgs e)
            => WriteFlag(v => CoreSettings.Current.KeywordTriggerIgnoreOwnFocus = v,
                         ChkAwarenessIgnoreOwnFocus, "KeywordTriggerIgnoreOwnFocus");

        private void ChkAwarenessHighlight_Changed(object? sender, RoutedEventArgs e)
        {
            // ponytail: WPF also calls SyncKeywordRescuePanelUi() here for the highlight mode +
            // duration rows, which live in KeywordTriggersPanel (not this layer's file).
            WriteFlag(v => CoreSettings.Current.KeywordHighlightEnabled = v,
                      ChkAwarenessHighlight, "KeywordHighlightEnabled");
            SyncHighlightSwatchUi(CoreSettings.Current.KeywordHighlightColor);
        }

        private void ChkAwarenessHighlightVisibleInCapture_Changed(object? sender, RoutedEventArgs e)
        {
            // ponytail: WPF then flips display affinity on the live overlay windows
            // (App.KeywordHighlight.RefreshCaptureVisibility) - WDA_EXCLUDEFROMCAPTURE, Win32,
            // head-side. The setting is stored either way, so a later head reads the right value.
            WriteFlag(v => CoreSettings.Current.OcrHighlightVisibleInCapture = v,
                      ChkAwarenessHighlightVisibleInCapture, "OcrHighlightVisibleInCapture");
        }

        /// <summary>One box, one flag, one save - the shape most of these toggles share.</summary>
        private void WriteFlag(Action<bool> write, CheckBox box, string name)
        {
            if (_isLoading) return;
            try
            {
                write(box.IsChecked == true);
                CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Awareness tab: failed to write {Setting}", name);
            }
        }

        // ------------------------------------------------------------------ app scope

        /// <summary>
        /// The list of apps is meaningless in Everywhere mode, and leaving it visible invites
        /// someone to fill it in and wonder why nothing changed. Tag-matched, not index-matched,
        /// exactly as MainWindow.RefreshAwarenessAppScopeUi does it.
        /// </summary>
        private void CmbAwarenessAppScope_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var tag = (CmbAwarenessAppScope.SelectedItem as ComboBoxItem)?.Tag as string;
            if (!Enum.TryParse<AwarenessAppScope>(tag, out var scope)) return;

            AwarenessAppListPanel.IsVisible = scope != AwarenessAppScope.Everywhere;

            if (_isLoading) return;
            try
            {
                CoreSettings.Current.KeywordTriggerAppScope = scope;
                CoreSettings.Save();
                Log.Information("Awareness app scope set to {Mode} ({Count} apps listed)",
                    scope, CoreSettings.Current.KeywordTriggerApps?.Count ?? 0);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Awareness tab: failed to write KeywordTriggerAppScope");
            }

            // ponytail: WPF also rebuilds the "recently focused" chips from
            // KeywordTriggerService.GetRecentForegroundApps(). That ring is fed by a foreground-
            // window poll (Win32) and lives with the service, so the chip row stays empty here.
        }

        private void TxtAwarenessAppList_LostFocus(object? sender, RoutedEventArgs e) => CommitAppList();

        private void TxtAwarenessAppList_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) CommitAppList();
        }

        private void CommitAppList()
        {
            // ponytail: BLOCKED on KeywordTriggerService.ParseAppList, which canonicalises the box
            // (split on , ; newline, strip a trailing ".exe", de-duplicate case-insensitively).
            // That is a pure static with no platform dependency and belongs in Core, but the
            // service is not this layer's file and copying the parse here would give the two heads
            // two definitions of what "chrome.exe" means. The box is seeded and readable; commit
            // lands with the service.
        }

        // ------------------------------------------------------------------ highlight colour

        /// <summary>
        /// Swatch click. Writes the colour and repaints the row, as ApplyAwarenessHighlightColor
        /// does; the live overlay repaint is head-side.
        /// </summary>
        private void AwarenessHighlightSwatch_Click(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border b && b.Tag is string hex) ApplyHighlightColour(hex);
        }

        private void TxtAwarenessHighlightHex_LostFocus(object? sender, RoutedEventArgs e)
            => ApplyHighlightColour(TxtAwarenessHighlightHex.Text);

        private void TxtAwarenessHighlightHex_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            ApplyHighlightColour(TxtAwarenessHighlightHex.Text);
            e.Handled = true;
        }

        /// <summary>
        /// Validates a hex colour and writes it to settings. Silently no-ops on malformed input so
        /// a half-typed value in the textbox does not wipe the user's colour.
        /// </summary>
        private void ApplyHighlightColour(string? hex)
        {
            if (_isLoading || string.IsNullOrWhiteSpace(hex)) return;

            var trimmed = hex.Trim();
            if (!trimmed.StartsWith('#')) trimmed = "#" + trimmed;
            if (!Color.TryParse(trimmed, out _)) return;   // Avalonia's twin of WPF's ColorConverter

            try
            {
                CoreSettings.Current.KeywordHighlightColor = trimmed;
                CoreSettings.Save();
                SyncHighlightSwatchUi(CoreSettings.Current.KeywordHighlightColor);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Awareness tab: failed to write KeywordHighlightColor");
            }

            // ponytail: WPF then repaints the live highlight overlay through App.KeywordHighlight -
            // click-through layered windows, head-side.
        }

        /// <summary>
        /// Dims every swatch then re-outlines the one matching the current colour, so the user can
        /// see which preset (if any) their colour is. Ported from SyncAwarenessHighlightSwatchUi,
        /// which also rewrites the hex box.
        /// </summary>
        private void SyncHighlightSwatchUi(string? colour)
        {
            var selected = (colour ?? "").ToUpperInvariant();
            if (!string.Equals(TxtAwarenessHighlightHex.Text, colour, StringComparison.Ordinal))
                TxtAwarenessHighlightHex.Text = colour;

            foreach (var swatch in Swatches)
            {
                var match = string.Equals(swatch.Tag?.ToString()?.ToUpperInvariant(), selected, StringComparison.Ordinal);
                swatch.BorderBrush = match ? Brushes.White : SwatchIdle;
                swatch.BorderThickness = new Thickness(match ? 2 : 1);
            }
        }

        // ------------------------------------------------------------------ still head-side

        /// <summary>
        /// WPF: MainWindow.Settings.cs:660 -&gt; StartAwarenessTutorial() -&gt;
        /// StartTutorial(TutorialType.Awareness). The seam takes the tour by name and the head
        /// parses it, so this is the whole call. This head does NOT seed
        /// CoreTutorial.StartAction today, so the button reaches the seam and no tour appears -
        /// the seam's documented no-op, not a wrong action, and one seeding line away from real.
        ///
        /// ponytail: the WPF version also hooks TutorialCompleted once, to pop the Puppy preset's
        /// editor when the tour is finished rather than skipped. That half needs
        /// ConditioningControlPanel/Views/Dialogs/AwarenessPresetDetailDialog.xaml, which has no
        /// Avalonia twin; CoreTutorial.Finished carries the completed/skipped bool it would need.
        /// </summary>
        private void BtnAwarenessTutorial_Click(object? sender, RoutedEventArgs e)
            => CoreTutorial.Start("Awareness");

        private void BtnGateUnlock_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: the premium gate. Needs PatreonService to open the pledge page, and
            // RefreshPremiumGate to raise or drop the AwarenessGate overlay in the first place.
        }

        /// <summary>
        /// WPF: MainWindow.Awareness.cs:1175. Its SECOND branch is ported verbatim - reveal, scroll
        /// to and pulse the custom-trigger drawer, which KeywordTriggersPanel.RevealTriggerEditor
        /// already does on this head and which is idempotent by design, because "nothing visibly
        /// happened" is how this link got reported as a dead click in the first place.
        ///
        /// ponytail: WPF PREFERS a first branch when a preset is installed - open that preset's
        /// inline editor. KeywordTriggerPresetService is in Core
        /// (CCP.Core/Services/KeywordTriggerPresetService.cs), so the lookup is available; what is
        /// missing is the dialog, ConditioningControlPanel/Views/Dialogs/AwarenessPresetDetailDialog.xaml,
        /// which has no Avalonia twin. Falling through to the drawer is WPF's own no-preset path,
        /// so the link is never dead and never lands somewhere wrong.
        /// </summary>
        private void LnkAwarenessAdvanced_Click(object? sender, RoutedEventArgs e)
            => KeywordPanel?.RevealTriggerEditor();
    }
}
