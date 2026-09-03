using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/AwarenessTabView.xaml.cs.
    ///
    /// <para>The WPF code-behind is pure re-hosting: every one of its eighteen handlers is a
    /// three-line thunk that finds the owning <c>MainWindow</c> and forwards. Those MainWindow
    /// bodies read <c>App.Settings</c>, <c>App.KeywordTriggers</c>, <c>App.ScreenOcr</c>,
    /// <c>App.KeywordHighlight</c> and <c>TutorialService</c>, none of which are in Core, so
    /// they are stubs here.</para>
    ///
    /// <para>What is NOT stubbed is the view-only half of those same bodies, lifted from
    /// MainWindow.Awareness.cs and MainWindow.KeywordTriggers.cs so the tab is honest on its
    /// own: the two cooldown sliders write their own <c>{v}s</c> labels (the format string is
    /// MainWindow.KeywordTriggers.cs:65/82), the master switch drives the status dot and its
    /// Live/Off label (MainWindow.Awareness.cs:376-384), the app-scope combo shows and hides the
    /// app list (MainWindow.Awareness.cs:548-554), and the swatch row paints its selected
    /// outline and mirrors the hex box (SyncAwarenessHighlightSwatchUi, :795-816).</para>
    /// </summary>
    public partial class AwarenessTabView : UserControl
    {
        // The compiled-XAML x:Name fields are only populated by the generated
        // InitializeComponent(); this head loads its views with AvaloniaXamlLoader.Load, so every
        // control the code touches is resolved by name here, as LockdownTabView does.
        private readonly Slider _sliderGlobalCooldown;
        private readonly TextBlock _txtGlobalCooldown;
        private readonly Slider _sliderSameWordCooldown;
        private readonly TextBlock _txtSameWordCooldown;
        private readonly CheckBox _chkMaster;
        private readonly Ellipse _statusDot;
        private readonly TextBlock _txtStatus;
        private readonly ComboBox _cmbAppScope;
        private readonly StackPanel _appListPanel;
        private readonly TextBox _txtHighlightHex;
        private readonly Border[] _swatches;

        /// <summary>The off-state dot fill, straight out of the WPF XAML (Fill="#606060").</summary>
        private static readonly IBrush OffDot = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));

        /// <summary>Unselected swatch outline, from SyncAwarenessHighlightSwatchUi.</summary>
        private static readonly IBrush SwatchIdle = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A));

        public AwarenessTabView()
        {
            AvaloniaXamlLoader.Load(this);

            _sliderGlobalCooldown = this.FindControl<Slider>("SliderAwarenessGlobalCooldown")!;
            _txtGlobalCooldown = this.FindControl<TextBlock>("TxtAwarenessGlobalCooldown")!;
            _sliderSameWordCooldown = this.FindControl<Slider>("SliderAwarenessSameWordCooldown")!;
            _txtSameWordCooldown = this.FindControl<TextBlock>("TxtAwarenessSameWordCooldown")!;
            _chkMaster = this.FindControl<CheckBox>("ChkAwarenessMaster")!;
            _statusDot = this.FindControl<Ellipse>("AwarenessStatusDot")!;
            _txtStatus = this.FindControl<TextBlock>("TxtAwarenessStatus")!;
            _cmbAppScope = this.FindControl<ComboBox>("CmbAwarenessAppScope")!;
            _appListPanel = this.FindControl<StackPanel>("AwarenessAppListPanel")!;
            _txtHighlightHex = this.FindControl<TextBox>("TxtAwarenessHighlightHex")!;
            _swatches = new[]
            {
                this.FindControl<Border>("SwatchHighlightPink")!,
                this.FindControl<Border>("SwatchHighlightCyan")!,
                this.FindControl<Border>("SwatchHighlightLime")!,
                this.FindControl<Border>("SwatchHighlightOrange")!,
                this.FindControl<Border>("SwatchHighlightViolet")!,
                this.FindControl<Border>("SwatchHighlightWhite")!,
            };

            // WPF's Slider.ValueChanged forwards to MainWindow, which writes the setting AND the
            // label. Only the label half is view-local, so it is wired here instead of in XAML -
            // same split KeywordTriggersPanel already makes for its four sliders.
            _sliderGlobalCooldown.PropertyChanged += (_, e) =>
            {
                if (e.Property == RangeBase.ValueProperty)
                    _txtGlobalCooldown.Text = $"{(int)_sliderGlobalCooldown.Value}s";
            };
            _sliderSameWordCooldown.PropertyChanged += (_, e) =>
            {
                if (e.Property == RangeBase.ValueProperty)
                    _txtSameWordCooldown.Text = $"{(int)_sliderSameWordCooldown.Value}s";
            };

            // The drawer ships shut on WPF (IsExpanded="False"); the Avalonia panel ships open so
            // its own --render-view proof shows an interior. Its host owns the real state, so the
            // Awareness tab closes it the way MainWindow does.
            foreach (var ex in this.GetLogicalDescendants().OfType<Expander>())
                if (ex.Name == "KeywordTriggersExpander")
                    ex.SetCurrentValue(Expander.IsExpandedProperty, false);

            // Everywhere is the default mode, as the WPF item's IsSelected="True" says. Set here,
            // not in XAML: a SelectedIndex there fires SelectionChanged mid-populate.
            _cmbAppScope.SelectedIndex = 0;

            SyncHighlightSwatchUi(_txtHighlightHex.Text ?? "#FF69B4");
        }

        // ------------------------------------------------------------------ view-only behaviour

        /// <summary>
        /// Master switch. The settings write and the engine start/stop are MainWindow's; the dot
        /// and the Live/Off label beside it are this view's, so they stay real.
        /// </summary>
        private void ChkAwarenessMaster_Changed(object? sender, RoutedEventArgs e)
        {
            var on = _chkMaster.IsChecked == true;
            _statusDot.Fill = on ? this.FindResource("PinkBrush") as IBrush : OffDot;
            _txtStatus.Text = on ? "Live" : "Off";

            // ponytail: needs App.Settings + App.KeywordTriggers to actually arm the engine,
            // wired when they move to Core.
        }

        /// <summary>
        /// The list of apps is meaningless in Everywhere mode, and leaving it visible invites
        /// someone to fill it in and wonder why nothing changed. Tag-matched, not index-matched,
        /// exactly as MainWindow.RefreshAwarenessAppScopeUi does it.
        /// </summary>
        private void CmbAwarenessAppScope_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_appListPanel is null) return;   // a selection raised from inside XAML populate
            var tag = (_cmbAppScope.SelectedItem as ComboBoxItem)?.Tag as string;
            _appListPanel.IsVisible = !string.Equals(tag, "Everywhere", StringComparison.Ordinal);

            // ponytail: needs App.Settings (KeywordTriggerAppScope) + KeywordTriggerService's
            // recent-foreground-app ring for the chips, wired when they move to Core.
        }

        /// <summary>
        /// Swatch click. MainWindow persists the colour and repaints the live highlight overlay;
        /// the hex box and the selected outline are view state and are ported.
        /// </summary>
        private void AwarenessHighlightSwatch_Click(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border b || b.Tag is not string hex) return;
            _txtHighlightHex.Text = hex;
            SyncHighlightSwatchUi(hex);

            // ponytail: needs App.Settings (KeywordHighlightColor) + App.KeywordHighlight to
            // repaint the on-screen overlay, wired when they move to Core.
        }

        private void TxtAwarenessHighlightHex_LostFocus(object? sender, RoutedEventArgs e)
            => SyncHighlightSwatchUi(_txtHighlightHex.Text ?? "");

        private void TxtAwarenessHighlightHex_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) SyncHighlightSwatchUi(_txtHighlightHex.Text ?? "");
        }

        /// <summary>
        /// Dims every swatch then re-outlines the one matching the current colour, so the user can
        /// see which preset (if any) their colour is. Ported from SyncAwarenessHighlightSwatchUi.
        /// </summary>
        private void SyncHighlightSwatchUi(string colour)
        {
            var selected = colour.ToUpperInvariant();
            foreach (var swatch in _swatches)
            {
                var match = string.Equals(swatch.Tag?.ToString()?.ToUpperInvariant(), selected, StringComparison.Ordinal);
                swatch.BorderBrush = match ? Brushes.White : SwatchIdle;
                swatch.BorderThickness = new Thickness(match ? 2 : 1);
            }
        }

        // ------------------------------------------------------------------ stubs

        private void BtnAwarenessTutorial_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs TutorialService, wired when it moves to Core.
        }

        private void BtnGateUnlock_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs PatreonService (opens the pledge page), wired when it moves to Core.
        }

        private void ChkAwarenessOcr_Changed(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs App.Settings + App.ScreenOcr, wired when they move to Core.
        }

        private void ChkAwarenessKeyboard_Changed(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs App.Settings + KeywordTriggerService, wired when they move to Core.
        }

        private void ChkAwarenessIgnoreOwnUi_Changed(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs App.Settings, wired when it moves to Core.
        }

        private void ChkAwarenessLoopProtection_Changed(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs App.Settings, wired when it moves to Core.
        }

        private void ChkAwarenessHighlight_Changed(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs App.Settings + App.KeywordHighlight, wired when they move to Core.
        }

        private void ChkAwarenessHighlightVisibleInCapture_Changed(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs App.Settings + the Win32 WDA_EXCLUDEFROMCAPTURE affordance on the
            // highlight overlay windows, wired when they move to Core.
        }

        private void ChkAwarenessIgnoreOwnFocus_Changed(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs App.Settings, wired when it moves to Core.
        }

        private void TxtAwarenessAppList_LostFocus(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs App.Settings (KeywordTriggerApps), wired when it moves to Core.
        }

        private void TxtAwarenessAppList_KeyDown(object? sender, KeyEventArgs e)
        {
            // ponytail: needs App.Settings (KeywordTriggerApps), wired when it moves to Core.
        }

        private void LnkAwarenessAdvanced_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs App.KeywordPresets to pick the installed preset and open its editor,
            // wired when it moves to Core.
        }
    }
}
