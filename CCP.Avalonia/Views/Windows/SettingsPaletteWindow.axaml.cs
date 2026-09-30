using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Animation;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The Ctrl+K palette: type a few letters, land on the door, tab or setting you meant.
    ///
    /// PORTED from ConditioningControlPanel/Windows/SettingsPaletteWindow.xaml.cs. What moved:
    ///  - <c>OpenPaletteCommand</c> (a WPF <c>RoutedUICommand</c>) is gone. Avalonia has no
    ///    RoutedUICommand, and its only consumer was MainWindow's KeyBinding; the shell HAS ported
    ///    (MainShellWindow), so the remaining work is a KeyGesture on it calling
    ///    <see cref="Toggle"/> - which is a shell file, not this one. Inventing an ICommand here
    ///    would be a second API to reconcile later.
    ///  - <c>Refresh</c> queries Core's <c>SettingsPaletteIndex.Search</c>, as WPF does.
    ///  - <c>Navigate</c> is the same ShowTab + FocusSection + first-named-element 2s glow.
    ///  - <c>Top = Math.Max(20, Top - 70)</c> becomes a <c>Position</c> nudge: Avalonia has no
    ///    Top/Left, only a device-pixel <c>PixelPoint</c>.
    ///  - <c>PreviewKeyDown</c> becomes a tunnelling KeyDown handler, which is what Preview meant.
    ///  - The click-away close only arms once the window has actually been activated, so a headless
    ///    render (which never activates it) does not close it out from under the capture.
    ///
    /// <para><b>Escape and the panic key are the same key.</b> <c>AppSettings.PanicKey</c> defaults
    /// to "Escape" and is delivered by the global key hook regardless of which window has focus, so
    /// one Esc press aimed at this palette also reaches the panic ladder, where the SECOND press
    /// exits the app. <see cref="TryConsumeEscape"/> is the hand-off that stops that: the shell
    /// calls it at the top of its panic handler and returns early without advancing the press
    /// count. The 350ms grace window covers the race where the window sees KeyDown before the
    /// hook's queued handler runs, so the press is consumed exactly once either way.</para>
    /// </summary>
    public partial class SettingsPaletteWindow : Window
    {
        /// <summary>How long after an Esc-close the panic hand-off still claims the press.</summary>
        private const int EscapeGraceMs = 350;

        private static SettingsPaletteWindow? _instance;
        private static DateTime _escapeClosedAtUtc = DateTime.MinValue;

        private readonly TextBox _txtQuery;
        private readonly TextBlock _txtPlaceholder;
        private readonly TextBlock _txtEmpty;
        private readonly ListBox _listResults;

        private bool _closing;
        private bool _wasActivated;

        public SettingsPaletteWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _txtQuery = this.FindControl<TextBox>("TxtQuery")!;
            _txtPlaceholder = this.FindControl<TextBlock>("TxtPlaceholder")!;
            _txtEmpty = this.FindControl<TextBlock>("TxtEmpty")!;
            _listResults = this.FindControl<ListBox>("ListResults")!;

            // Handlers live here rather than in markup, per the porting convention.
            _txtQuery.TextChanged += (_, _) => TxtQuery_TextChanged();
            AddHandler(KeyDownEvent, Window_PreviewKeyDown, RoutingStrategies.Tunnel);
            _listResults.AddHandler(PointerReleasedEvent, Item_Click, RoutingStrategies.Tunnel);

            Loaded += (_, _) => Window_Loaded();
            // The click-away dismiss must not fire before the window has ever had focus: a headless
            // render shows the window without activating it, and an unguarded Deactivated closed it
            // mid-capture.
            Activated += (_, _) => _wasActivated = true;
            Deactivated += (_, _) => Window_Deactivated();
            Closed += (_, _) => { if (ReferenceEquals(_instance, this)) _instance = null; };
        }

        // =====================================================================================
        //  open / close
        // =====================================================================================

        /// <summary>True while the palette is on screen.</summary>
        internal static bool IsOpen => _instance != null;

        /// <summary>
        /// Ctrl+K: open the palette, or close it if it is already up. Never throws - it is wired
        /// to a hotkey, and a palette that can crash the app is worse than no palette.
        /// </summary>
        internal static void Toggle(Window? owner)
        {
            try
            {
                // Lockdown owns the screen; a navigation palette floating above it reads as an
                // escape hatch even though it only ever calls ShowTab (WPF SettingsPaletteWindow.xaml.cs:80).
                if (MainShellWindow.LockdownActive) return;
                if (_instance != null)
                {
                    _instance.ClosePalette(fromEscape: false);
                    return;
                }
                if (owner == null) return;

                var win = new SettingsPaletteWindow();
                _instance = win;
                win.Show(owner);
                win._txtQuery.Focus();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Settings palette failed to open");
                _instance = null;
            }
        }

        /// <summary>Closes the palette if it is open. Safe to call at any time.</summary>
        internal static void CloseIfOpen() => _instance?.ClosePalette(fromEscape: false);

        /// <summary>
        /// The panic-ladder hand-off. Returns true when this Escape press belongs to the palette -
        /// either because it is open right now (in which case it is closed here) or because the
        /// palette closed itself on the very same press moments ago. See the class remarks for why
        /// the caller must NOT advance the panic press count when this returns true.
        /// </summary>
        internal static bool TryConsumeEscape()
        {
            try
            {
                if (_instance != null)
                {
                    _instance.ClosePalette(fromEscape: true);
                    // Already stamped by ClosePalette; clear it so the *next* press is a real panic.
                    _escapeClosedAtUtc = DateTime.MinValue;
                    return true;
                }

                if ((DateTime.UtcNow - _escapeClosedAtUtc).TotalMilliseconds <= EscapeGraceMs)
                {
                    _escapeClosedAtUtc = DateTime.MinValue;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private void ClosePalette(bool fromEscape)
        {
            if (_closing) return;
            _closing = true;
            if (fromEscape) _escapeClosedAtUtc = DateTime.UtcNow;
            try { Close(); } catch { }
        }

        // =====================================================================================
        //  lifecycle
        // =====================================================================================

        private void Window_Loaded()
        {
            // CenterOwner puts us dead centre; a palette reads better sitting a little high, so
            // the results grow downward into empty space instead of over the owner's centre.
            try
            {
                Position = new PixelPoint(Position.X, Math.Max(20, Position.Y - 70));
            }
            catch { }

            Refresh();
            _txtQuery.Focus();
        }

        private void Window_Deactivated()
        {
            // Click-away dismiss. Deliberately NOT an Escape close: it must not arm the panic
            // hand-off, because no Escape press happened.
            if (_wasActivated) ClosePalette(fromEscape: false);
        }

        // =====================================================================================
        //  search
        // =====================================================================================

        private void TxtQuery_TextChanged()
        {
            _txtPlaceholder.IsVisible = string.IsNullOrEmpty(_txtQuery.Text);
            Refresh();
        }

        /// <summary>Rows are rebuilt from loc keys on every keystroke, so a language change always
        /// shows current strings - there is no cache.</summary>
        private void Refresh()
        {
            try
            {
                var rows = SettingsPaletteIndex.Search(_txtQuery.Text)
                                               .Select(entry => new PaletteRow(entry))
                                               .ToList();

                _listResults.ItemsSource = rows;
                if (rows.Count > 0) _listResults.SelectedIndex = 0;

                _listResults.IsVisible = rows.Count > 0;
                _txtEmpty.IsVisible = rows.Count == 0;
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug("Settings palette refresh failed: {E}", ex.Message);
            }
        }

        // =====================================================================================
        //  keyboard + mouse
        // =====================================================================================

        private void Window_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    ClosePalette(fromEscape: true);
                    e.Handled = true;
                    break;

                case Key.Enter:
                    ActivateSelected();
                    e.Handled = true;
                    break;

                case Key.Down:
                    Move(1);
                    e.Handled = true;
                    break;

                case Key.Up:
                    Move(-1);
                    e.Handled = true;
                    break;

                case Key.K when e.KeyModifiers == KeyModifiers.Control:
                    // Ctrl+K again while the palette has focus = close it. The shell's own gesture
                    // cannot fire here because the palette owns focus.
                    ClosePalette(fromEscape: false);
                    e.Handled = true;
                    break;
            }
        }

        private void Move(int delta)
        {
            if (_listResults.ItemCount == 0) return;
            var next = _listResults.SelectedIndex + delta;
            if (next < 0) next = _listResults.ItemCount - 1;
            if (next >= _listResults.ItemCount) next = 0;
            _listResults.SelectedIndex = next;
            try { _listResults.ScrollIntoView(next); } catch { }
        }

        /// <summary>Closes first - the highlight should be visible against the real page, and the
        /// owner needs focus back before anything navigates.</summary>
        private void ActivateSelected()
        {
            if (_listResults.SelectedItem is not PaletteRow row) return;
            var owner = Owner as MainShellWindow;
            ClosePalette(fromEscape: false);
            if (owner == null) return;
            Dispatcher.UIThread.Post(() => Navigate(owner, row.Entry));
        }

        // =====================================================================================
        //  navigation + highlight
        // =====================================================================================

        private const int PulseMs = 2000;
        private static Control? _pulseTarget;
        private static IEffect? _pulsePrevEffect;
        private static DispatcherTimer? _pulseTimer;

        /// <summary>ShowTab is the only navigation API, so a palette hit is indistinguishable
        /// from a rail click.</summary>
        internal static void Navigate(MainShellWindow shell, SettingsPaletteEntry entry)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(entry.TabKey)) shell.ShowTab(entry.TabKey);
                if (!string.IsNullOrWhiteSpace(entry.SectionKey)) shell.AppSettingsPage?.FocusSection(entry.SectionKey);
                if (entry.ElementNames.Length == 0) return;

                // One more hop so the section scroll has settled before the lookup.
                Dispatcher.UIThread.Post(() =>
                {
                    // Name lookup across namescopes: every tab owns its own, so walk the tree.
                    var target = entry.ElementNames
                        .Select(n => shell.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == n))
                        .FirstOrDefault(c => c != null);
                    if (target == null)
                    {
                        Serilog.Log.Debug("Palette entry {Id}: no element matched [{Names}]",
                                          entry.Id, string.Join(", ", entry.ElementNames));
                        return;
                    }
                    target.BringIntoView();
                    Pulse(target);
                });
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Palette navigation failed for {Id}", entry.Id);
            }
        }

        /// <summary>A 2s accent glow around the found control, then gone. Self-removing: the
        /// previous effect is restored, and a second pulse clears the first.</summary>
        private static void Pulse(Control target)
        {
            ClearPulse();
            _pulseTarget = target;
            _pulsePrevEffect = target.Effect;
            var glow = new DropShadowEffect
            {
                Color = Color.FromRgb(0xFF, 0x69, 0xB4),
                BlurRadius = 22,
                OffsetX = 0,
                OffsetY = 0,
                Opacity = 0.9,
            };
            target.Effect = glow;

            // Reduced motion keeps the static glow for the same two seconds, as WPF does.
            if (AmbientFxCanvas.Env.AllowTransitions)
            {
                glow.Opacity = 0.0;
                var anim = new Animation { Duration = TimeSpan.FromMilliseconds(PulseMs), FillMode = FillMode.Forward };
                foreach (var (cue, value) in new[] { (0.10, 0.95), (0.45, 0.45), (0.70, 0.95), (1.0, 0.0) })
                    anim.Children.Add(new KeyFrame { Cue = new Cue(cue), Setters = { new Setter(DropShadowEffect.OpacityProperty, value) } });
                _ = anim.RunAsync(glow);
            }
            _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PulseMs + 60) };
            _pulseTimer.Tick += (_, _) => ClearPulse();
            _pulseTimer.Start();
        }

        private static void ClearPulse()
        {
            _pulseTimer?.Stop();
            _pulseTimer = null;
            if (_pulseTarget != null) _pulseTarget.Effect = _pulsePrevEffect;
            _pulseTarget = null;
            _pulsePrevEffect = null;
        }

        private void Item_Click(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            if ((e.Source as Control)?.DataContext is not PaletteRow row) return;

            _listResults.SelectedItem = row;
            ActivateSelected();
            e.Handled = true;
        }
    }

    /// <summary>
    /// One rendered row. Strings are snapshotted at build time (every keystroke), not bound to
    /// the entry, so the ItemTemplate never re-enters the localization manager during layout.
    /// Top-level because the ItemTemplate's <c>x:DataType</c> has to name it.
    /// </summary>
    public sealed class PaletteRow
    {
        public PaletteRow(SettingsPaletteEntry entry)
        {
            Entry = entry;
            Glyph = entry.Glyph;
            Label = entry.Label;
            Context = entry.Context;
        }

        public SettingsPaletteEntry Entry { get; }
        public string Glyph { get; }
        public string Label { get; }
        public string Context { get; }

        /// <summary>WPF's <c>ContextVisibility</c>; Avalonia binds IsVisible to a bool.</summary>
        public bool HasContext => !string.IsNullOrEmpty(Context);
    }
}
