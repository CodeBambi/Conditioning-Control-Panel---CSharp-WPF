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
using ConditioningControlPanel.Services.Awareness;
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
    /// KeywordHighlight's overlay windows and the recently-seen-app ring are all head-side, and each stub below names the one it wants.</para>
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

        /// <summary>The four switches that only mean something with a screen read behind them
        /// (OCR, skip the app's own windows, the word highlight, highlight in captures). This head
        /// has no OCR engine, so nothing reads them: each is greyed and says so. Typed keywords DO
        /// work (Platform/KeywordTriggerHead reads the master on every key).</summary>
        internal CheckBox[] ScreenReadOnlyToggles => new[]
        {
            ChkAwarenessOcr, ChkAwarenessIgnoreOwnUi, ChkAwarenessHighlight, ChkAwarenessHighlightVisibleInCapture,
        };

        private void MarkScreenReadRows()
        {
            // Windows reads the screen (Platform/ScreenOcrService over Windows.Media.Ocr): the rows are
            // live. Linux has no reader in the tree, so they stay greyed with the reason.
            if (Platform.ScreenOcrService.ReasonUnavailable is null) return;
            foreach (var box in ScreenReadOnlyToggles)
            {
                box.IsEnabled = false;
                if (box.Parent is not Grid row) continue;
                var note = new TextBlock
                {
                    Text = ConditioningControlPanel.Localization.Loc.Get("exclusives_not_on_this_build"),
                    FontSize = 10.5, FontStyle = global::Avalonia.Media.FontStyle.Italic,
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new global::Avalonia.Thickness(8, 0, 10, 0),
                    Tag = "ScreenReadNote",
                };
                note[!TextBlock.ForegroundProperty] = note.GetResourceObservable("TextMutedBrush").ToBinding();
                Grid.SetColumn(note, 0);
                row.Children.Add(note);
            }
        }

        public AwarenessTabView()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields this code-behind reads.
            InitializeComponent();
            MarkScreenReadRows();

            // WPF's Slider.ValueChanged forwards to MainWindow, which writes the setting AND the
            // label. Wired here rather than in XAML so the seed below can move the sliders before
            // the handler exists to react to it.
            SliderAwarenessGlobalCooldown.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty) return;
                // The slider is a stop index (ccp-bugs #640); the setting keeps plain seconds.
                var value = AwarenessCooldownScale.SecondsAt(SliderAwarenessGlobalCooldown.Value);
                TxtAwarenessGlobalCooldown.Text = AwarenessCooldownScale.Format(value);
                if (_isLoading) return;
                CoreSettings.Current.KeywordGlobalCooldownSeconds = value;
                CoreSettings.Save();
            };
            SliderAwarenessSameWordCooldown.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty) return;
                // The slider is a stop index (ccp-bugs #640); the setting keeps plain seconds.
                var value = AwarenessCooldownScale.SecondsAt(SliderAwarenessSameWordCooldown.Value);
                TxtAwarenessSameWordCooldown.Text = AwarenessCooldownScale.Format(value);
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

            HookLiveFeed();
            SyncAwarenessTabUi();

            // Tabs are shown and hidden rather than rebuilt, so re-read on every show: the master
            // switch is [JsonIgnore] session state and the app list can be edited elsewhere.
            AttachedToVisualTree += (_, _) => SyncAwarenessTabUi();
            // Owner, 2026-10-10: a panic press switches keyword triggers off. While this tab is in the tree
            // its switches follow at once (off the tree, the attach above repaints them).
            Action onPanicOff = OnKeywordTriggersSwitchedOffByPanic;
            AttachedToVisualTree += (_, _) => Views.Windows.PanicSurfaces.KeywordTriggersSwitchedOff += onPanicOff;
            DetachedFromVisualTree += (_, _) => Views.Windows.PanicSurfaces.KeywordTriggersSwitchedOff -= onPanicOff;
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
        internal void SyncAwarenessTabUi()
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

                // WPF MainWindow.Awareness.cs: the slider sits at the nearest stop, the label shows the
                // stored seconds as they are (an off-ladder value from an old file is not rewritten).
                SliderAwarenessGlobalCooldown.Value = AwarenessCooldownScale.IndexFor(s.KeywordGlobalCooldownSeconds);
                TxtAwarenessGlobalCooldown.Text = AwarenessCooldownScale.Format(s.KeywordGlobalCooldownSeconds);
                SliderAwarenessSameWordCooldown.Value = AwarenessCooldownScale.IndexFor(s.KeywordPerKeywordCooldownSeconds);
                TxtAwarenessSameWordCooldown.Text = AwarenessCooldownScale.Format(s.KeywordPerKeywordCooldownSeconds);

                // Matched on Tag rather than index so reordering the XAML items cannot silently
                // remap a saved setting onto the wrong mode.
                var wanted = s.KeywordTriggerAppScope.ToString();
                foreach (var item in CmbAwarenessAppScope.Items.OfType<ComboBoxItem>())
                    if (string.Equals(item.Tag as string, wanted, StringComparison.Ordinal))
                        CmbAwarenessAppScope.SelectedItem = item;

                TxtAwarenessAppList.Text = string.Join(", ", s.KeywordTriggerApps ?? new());
                AwarenessAppListPanel.IsVisible = s.KeywordTriggerAppScope != AwarenessAppScope.Everywhere;

                UpdateStatusIndicator(masterOn);
                RefreshAwarenessPresetCards();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Awareness tab: failed to load settings");
            }
            finally
            {
                _isLoading = false;
            }

            // WPF SyncAwarenessTabUI's tail: the pulse feed and the seen-app chips are rebuilt on
            // every open (both grow while the user is on other tabs).
            RefreshAwarenessPulseFeed();
            RefreshAwarenessSeenAppChips();

            // Assign only on a real difference: Avalonia raises IsCheckedChanged on a programmatic
            // set too, and every handler below is a live editor.
            static void Set(CheckBox box, bool value)
            {
                if ((box.IsChecked ?? false) != value) box.IsChecked = value;
            }
        }

        /// <summary>A panic press switched keyword triggers off (PanicSurfaces.SwitchOffKeywordTriggers):
        /// repaint the switches from the settings, never write.</summary>
        private void OnKeywordTriggersSwitchedOffByPanic()
        {
            SyncAwarenessTabUi();
            KeywordPanel?.SyncFromSettings();
        }

        /// <summary>"Switched off by a panic press." Shown while a switch the panic turned off is still
        /// off: the master (this run) or the saved screen read. Turning that switch back on clears it.</summary>
        internal void RefreshPanicNotice()
        {
            var s = CoreSettings.Current;
            var master = Views.Windows.PanicSurfaces.KeywordMasterOffByPanic && !s.KeywordTriggersEnabled;
            var screen = s.KeywordTriggersOffByPanic && !s.ScreenOcrEnabled;
            TxtAwarenessPanicNotice.IsVisible = master || screen;
        }

        /// <summary>The dot and the Live/Off label beside the master switch.</summary>
        private void UpdateStatusIndicator(bool on)
        {
            RefreshPanicNotice();
            var pink = this.FindResource("PinkBrush") as IBrush;
            AwarenessStatusDot.Fill = on ? pink ?? Brushes.HotPink : OffDot;
            TxtAwarenessStatus.Text = on ? "Live" : "Off";
            TxtAwarenessStatus.Foreground = on ? pink ?? Brushes.HotPink : OffLabel;

            PulseStatusDot(on);   // WPF SetAwarenessStatusPulse: the dot breathes while live
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

                // WPF MainWindow.Awareness.cs:395: ON needs premium or the awareness free day
                // (KeywordTriggerService.HasAccess); the box bounces back. TierGate's toast stands
                // in for WPF's message box.
                if (on && !ConditioningControlPanel.Services.TierGate.DemandPremium(ConditioningControlPanel.Localization.Loc.Get("tab_awareness"), "awareness"))
                {
                    _isLoading = true;
                    try { ChkAwarenessMaster.IsChecked = false; }
                    finally { _isLoading = false; }
                    return;
                }

                CoreSettings.Current.KeywordTriggersEnabled = on;
                if (on) Views.Windows.PanicSurfaces.KeywordMasterOffByPanic = false;   // the user turned it back on

                // WPF :416-427: the master starts and stops the sources. Typed keys ride the panic
                // key's hook on Windows (it reads this flag on every key); the screen reader's timer
                // and the X11 key listener exist only while it is on.
                Platform.KeywordTriggerHead.SyncSources();

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

            // No hook to drop: typed keywords ride the panic key's hook, which stays up. This box mirrors the master.
        }

        private void ChkAwarenessOcr_Changed(object? sender, RoutedEventArgs e)
        {
            // WPF MainWindow.Awareness.cs:448: ON needs KeywordTriggerService.HasAccess; the box
            // bounces back and WPF's own message box says why.
            if (!_isLoading && ChkAwarenessOcr.IsChecked == true
                && !ConditioningControlPanel.Services.TierGate.RequiresPremium(ConditioningControlPanel.Localization.Loc.Get("tab_awareness"), "awareness").Allowed)
            {
                _isLoading = true;
                try { ChkAwarenessOcr.IsChecked = false; }
                finally { _isLoading = false; }
                _ = Controls.Companion.KeywordTriggersPanel.Inform(TopLevel.GetTopLevel(this) as Window,
                    ConditioningControlPanel.Localization.Loc.Get("title_patreon_feature"),
                    ConditioningControlPanel.Localization.Loc.Get("msg_screen_ocr_patreon_only"));
                return;
            }
            // The user turned the screen read back on after a panic switched it off: the notice has done its job.
            if (!_isLoading && ChkAwarenessOcr.IsChecked == true) CoreSettings.Current.KeywordTriggersOffByPanic = false;
            WriteFlag(v => CoreSettings.Current.ScreenOcrEnabled = v, ChkAwarenessOcr, "ScreenOcrEnabled");
            if (!_isLoading) RefreshPanicNotice();
            // WPF :462-468: start when on (and the master is on), stop when off.
            if (!_isLoading) Platform.ScreenOcrService.Sync();
            if (!_isLoading) KeywordPanel?.SyncFromSettings();   // WPF :475 SyncKeywordRescuePanelUi
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
            WriteFlag(v => CoreSettings.Current.KeywordHighlightEnabled = v,
                      ChkAwarenessHighlight, "KeywordHighlightEnabled");
            if (!_isLoading) KeywordPanel?.SyncFromSettings();   // WPF SyncKeywordRescuePanelUi
            SyncHighlightSwatchUi(CoreSettings.Current.KeywordHighlightColor);
        }

        private void ChkAwarenessHighlightVisibleInCapture_Changed(object? sender, RoutedEventArgs e)
        {
            // WPF then flips display affinity on its live overlay windows (RefreshCaptureVisibility).
            // The port's highlight windows live for one fire (Overlays/KeywordHighlightOverlay), so the
            // next fire reads the new value: nothing to refresh.
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

            RefreshAwarenessSeenAppChips();   // WPF RefreshAwarenessAppScopeUi's tail
        }

        private void TxtAwarenessAppList_LostFocus(object? sender, RoutedEventArgs e) => CommitAppList();

        private void TxtAwarenessAppList_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) CommitAppList();
        }

        private void CommitAppList()
        {
            // WPF MainWindow.Awareness.cs:683: Core's ParseAppList canonicalises the box (split on
            // , ; newline, strip ".exe", de-duplicate case-insensitively); unchanged = no write.
            var settings = CoreSettings.Current;
            if (_isLoading || settings == null) return;
            var parsed = ConditioningControlPanel.Services.KeywordTriggers.KeywordTriggerEngine.ParseAppList(TxtAwarenessAppList.Text);
            var existing = settings.KeywordTriggerApps ?? new System.Collections.Generic.List<string>();
            if (System.Linq.Enumerable.SequenceEqual(parsed, existing, StringComparer.OrdinalIgnoreCase)) return;
            settings.KeywordTriggerApps = parsed;
            CoreSettings.Save();
            Log.Information("Awareness app scope list set to {Count} app(s) ({Mode})", parsed.Count, settings.KeywordTriggerAppScope);
            RefreshAwarenessSeenAppChips();   // a typed app drops out of the offered chips
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

            // The next highlight reads the colour when it is drawn (Overlays/KeywordHighlightOverlay).
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
        /// WPF: MainWindow.Settings.cs:660 -&gt; StartAwarenessTutorial(). A tour finished (not
        /// skipped) pops the Puppy preset's editor (MainWindow.Settings.cs:689). Tours/TutorialHead.Seed
        /// seeds CoreTutorial.StartAction, so the tour runs here too.
        /// </summary>
        /// <summary>The ? panel's Awareness row: the same tour with the same one-shot.</summary>
        internal void StartTutorialFromHelp() => BtnAwarenessTutorial_Click(this, new RoutedEventArgs());

        private void BtnAwarenessTutorial_Click(object? sender, RoutedEventArgs e)
        {
            CoreTutorial.Start("Awareness");
            if (!CoreTutorial.IsActive || CoreTutorial.CurrentTourName != "Awareness") return;

            EventHandler<bool>? onFinished = null;
            onFinished = async (_, completed) =>
            {
                CoreTutorial.Finished -= onFinished;
                // WPF Settings.cs:681-682: only the Awareness tour, and only when finished, not skipped.
                if (!completed || CoreTutorial.CurrentTourName != "Awareness") return;
                try
                {
                    if (Presets.GetPreset("builtin.puppy") is { } puppy) await OpenPresetDetail(puppy);
                }
                catch (Exception ex) { Log.Debug("Awareness tutorial editor-open failed: {Error}", ex.Message); }
            };
            CoreTutorial.Finished += onFinished;
        }

        private void BtnGateUnlock_Click(object? sender, RoutedEventArgs e)
            => (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.BtnGateUnlock_Click(sender, e);

        /// <summary>
        /// WPF: MainWindow.Awareness.cs:1175. Prefer the installed preset's editor; with none
        /// installed, reveal, scroll to and pulse the custom-trigger drawer (idempotent by design).
        /// </summary>
        private async void LnkAwarenessAdvanced_Click(object? sender, RoutedEventArgs e)
        {
            if (GetMostRecentlyInstalledPreset() is { } installed) await OpenPresetDetail(installed);
            else KeywordPanel?.RevealTriggerEditor();
        }
    }
}
