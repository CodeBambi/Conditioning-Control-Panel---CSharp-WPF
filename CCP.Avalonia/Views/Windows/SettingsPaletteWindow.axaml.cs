using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The Ctrl+K palette: type a few letters, land on the door, tab or setting you meant.
    ///
    /// PORTED from ConditioningControlPanel/Windows/SettingsPaletteWindow.xaml.cs. What moved:
    ///  - <c>OpenPaletteCommand</c> (a WPF <c>RoutedUICommand</c>) is gone. Avalonia has no
    ///    RoutedUICommand and the only consumer is MainWindow's KeyBinding, which has not ported;
    ///    inventing an ICommand here would be a second API to reconcile later.
    ///  - <c>Refresh</c> cannot query the index: <c>Services.SettingsPaletteIndex</c> lives in the
    ///    WPF head, so this draws sample rows filtered by the query instead. See the stub below.
    ///  - <c>ActivateSelected</c> / <c>Navigate</c> / <c>ResolveFirst</c> / <c>FindElementByName</c> /
    ///    <c>Pulse</c> / <c>ClearPulse</c> all reach into MainWindow (ShowTab, AppSettingsTabView,
    ///    the visual-tree name walk and the accent glow). One stub stands in for the lot.
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
                // ponytail: needs App.Lockdown, wired when it moves to Core. Lockdown owns the
                // screen, and a navigation palette floating above it reads as an escape hatch even
                // though it only ever calls ShowTab - so the real check must come back before this
                // is reachable from a hotkey.
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
            catch
            {
                // ponytail: needs App.Logger, wired when it moves to Core.
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

        /// <summary>
        /// ponytail: needs Services.SettingsPaletteIndex, wired when it moves to Core. The real
        /// Refresh rebuilds rows from loc keys on every keystroke, so a language change between two
        /// opens always shows current strings - there is no cache. Until the index moves, these are
        /// placeholder rows, filtered the same way so the empty state and the arrow keys are still
        /// exercised.
        /// </summary>
        private void Refresh()
        {
            var query = (_txtQuery.Text ?? "").Trim();
            var rows = SampleRows
                .Where(r => query.Length == 0 ||
                            r.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            r.Context.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            _listResults.ItemsSource = rows;
            if (rows.Count > 0) _listResults.SelectedIndex = 0;

            _listResults.IsVisible = rows.Count > 0;
            _txtEmpty.IsVisible = rows.Count == 0;
        }

        private static IReadOnlyList<PaletteRow> SampleRows { get; } = new[]
        {
            new PaletteRow("🔊", "Master volume", "Settings › Audio"),
            new PaletteRow("🎥", "Webcam device", "Settings › Devices"),
            new PaletteRow("💬", "Chat thresholds", "Settings › Chat"),
            new PaletteRow("🏆", "Achievements", ""),
            new PaletteRow("🎛️", "Haptics setup", "Settings › Devices"),
        };

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

        /// <summary>
        /// Closes first - the highlight pulse should be visible against the real page, and the
        /// owner needs focus back before anything navigates.
        ///
        /// ponytail: needs MainWindow.ShowTab, AppSettingsTabView.FocusSection and the accent
        /// pulse, wired when the shell ports. The WPF original navigated via ShowTab (which opens
        /// the owning door, fires the nav bark and moves the active indicator), then walked the
        /// visual tree by x:Name across namescopes and hung a 2s self-removing DropShadow glow on
        /// whatever it found. None of that has a target on this head yet.
        /// </summary>
        private void ActivateSelected()
        {
            if (_listResults.SelectedItem is not PaletteRow) return;
            ClosePalette(fromEscape: false);
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
    /// One rendered row. Strings are snapshotted here at build time (which is every keystroke),
    /// not bound to the entry, so the ItemTemplate never re-enters the localization manager
    /// during layout.
    ///
    /// <para>Top-level rather than nested in the window, because the ItemTemplate's
    /// <c>x:DataType</c> has to name it and compiled bindings are on. It holds plain strings
    /// instead of a <c>SettingsPaletteEntry</c>: that type lives in the WPF head's Services, which
    /// this port may not reference.</para>
    /// </summary>
    public sealed class PaletteRow
    {
        public PaletteRow(string glyph, string label, string context)
        {
            Glyph = glyph;
            Label = label;
            Context = context;
        }

        public string Glyph { get; }
        public string Label { get; }
        public string Context { get; }

        /// <summary>WPF's <c>ContextVisibility</c>; Avalonia binds IsVisible to a bool.</summary>
        public bool HasContext => !string.IsNullOrEmpty(Context);
    }
}
