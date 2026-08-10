using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The Ctrl+K palette: type a few letters, land on the door, tab or setting you meant.
    ///
    /// <para><b>Why a window and not an in-canvas overlay.</b> Phase 2 has several agents editing
    /// <c>MainWindow.xaml</c> concurrently, and an overlay would have to claim a region inside
    /// RootGrid. A borderless owned window needs no markup there at all, and it also wins the
    /// airspace fight against the WebView2 browser card on Home, which an in-canvas overlay would
    /// lose (the same reason <c>SessionIO.cs</c> hides <c>BrowserContainer</c> during a drag).</para>
    ///
    /// <para><b>Escape and the panic key are the same key.</b> <c>AppSettings.PanicKey</c> defaults
    /// to "Escape" and is delivered by the WH_KEYBOARD_LL hook in
    /// <c>MainWindow.OnGlobalKeyPressed</c>, i.e. regardless of which window has focus. So one Esc
    /// press aimed at this palette also reaches the panic ladder, where the SECOND press exits the
    /// app. <see cref="TryConsumeEscape"/> is the hand-off that stops that: MainWindow calls it at
    /// the top of <c>HandlePanicKeyPress</c> (only when the panic key really is Escape) and returns
    /// early without advancing <c>_panicPressCount</c> - exactly the contract the open-lock-card and
    /// video-grace-pause hand-offs above it use. The 350ms grace window covers the race where WPF
    /// delivers KeyDown to this window before the hook's queued handler runs, so the press is
    /// consumed exactly once whichever order they arrive in.</para>
    /// </summary>
    public partial class SettingsPaletteWindow : Window
    {
        /// <summary>Ctrl+K, bound on MainWindow. Exposed so the binding has a stable command.</summary>
        public static readonly RoutedUICommand OpenPaletteCommand =
            new("Open settings palette", nameof(OpenPaletteCommand), typeof(SettingsPaletteWindow));

        /// <summary>How long after an Esc-close the panic hand-off still claims the press.</summary>
        private const int EscapeGraceMs = 350;

        /// <summary>Accent pulse length. Interaction motion, not an ambient loop.</summary>
        private const int PulseMs = 2000;

        private static SettingsPaletteWindow? _instance;
        private static DateTime _escapeClosedAtUtc = DateTime.MinValue;

        /// <summary>The element currently wearing a pulse, and the effect it had before.</summary>
        private static FrameworkElement? _pulseTarget;
        private static Effect? _pulsePrevEffect;
        private static DispatcherTimer? _pulseTimer;

        private bool _closing;

        public SettingsPaletteWindow()
        {
            InitializeComponent();
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
                // escape hatch even though it only ever calls ShowTab.
                if (App.Lockdown?.IsActive == true) return;
                if (_instance != null)
                {
                    _instance.ClosePalette(fromEscape: false);
                    return;
                }
                if (owner == null) return;

                var win = new SettingsPaletteWindow { Owner = owner };
                _instance = win;
                win.Show();
                win.TxtQuery.Focus();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Settings palette failed to open");
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

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // CenterOwner puts us dead centre; a palette reads better sitting a little high, so
            // the results grow downward into empty space instead of over the owner's centre.
            try
            {
                Top = Math.Max(20, Top - 70);
            }
            catch { }

            Refresh();
            TxtQuery.Focus();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // Click-away dismiss. Deliberately NOT an Escape close: it must not arm the panic
            // hand-off, because no Escape press happened.
            ClosePalette(fromEscape: false);
        }

        // =====================================================================================
        //  search
        // =====================================================================================

        private void TxtQuery_TextChanged(object sender, TextChangedEventArgs e)
        {
            TxtPlaceholder.Visibility = string.IsNullOrEmpty(TxtQuery.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            Refresh();
        }

        private void Refresh()
        {
            try
            {
                // Rows are rebuilt from loc keys on every keystroke, so a language change between
                // two opens (or mid-typing) always shows current strings - there is no cache.
                var rows = SettingsPaletteIndex.Search(TxtQuery.Text)
                                               .Select(entry => new PaletteRow(entry))
                                               .ToList();

                ListResults.ItemsSource = rows;
                if (rows.Count > 0) ListResults.SelectedIndex = 0;

                ListResults.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                TxtEmpty.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Settings palette refresh failed: {E}", ex.Message);
            }
        }

        // =====================================================================================
        //  keyboard + mouse
        // =====================================================================================

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
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

                case Key.K when Keyboard.Modifiers == ModifierKeys.Control:
                    // Ctrl+K again while the palette has focus = close it. MainWindow's own
                    // KeyBinding cannot fire here because the palette owns focus.
                    ClosePalette(fromEscape: false);
                    e.Handled = true;
                    break;
            }
        }

        private void Move(int delta)
        {
            if (ListResults.Items.Count == 0) return;
            var next = ListResults.SelectedIndex + delta;
            if (next < 0) next = ListResults.Items.Count - 1;
            if (next >= ListResults.Items.Count) next = 0;
            ListResults.SelectedIndex = next;
            try { ListResults.ScrollIntoView(ListResults.SelectedItem); } catch { }
        }

        private void ActivateSelected()
        {
            if (ListResults.SelectedItem is not PaletteRow row) return;
            var owner = Owner as MainWindow;
            var entry = row.Entry;

            // Close first: the highlight pulse should be visible against the real page, and the
            // owner needs focus back before anything navigates.
            ClosePalette(fromEscape: false);
            if (owner == null) return;

            owner.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => Navigate(owner, entry)));
        }

        // =====================================================================================
        //  navigation + highlight
        // =====================================================================================

        private static void Navigate(MainWindow mw, SettingsPaletteEntry entry)
        {
            try
            {
                // ShowTab is the only navigation API: it opens the owning door (ExpandDoorForTab),
                // fires the nav bark, parks per-tab FX and moves the active indicator. Palette
                // navigation must be indistinguishable from a rail click.
                if (!string.IsNullOrWhiteSpace(entry.TabKey)) mw.ShowTab(entry.TabKey);

                if (!string.IsNullOrWhiteSpace(entry.SectionKey))
                {
                    var view = mw.FindName("AppSettingsTab") as Views.Tabs.AppSettingsTabView;
                    view?.FocusSection(entry.SectionKey);
                }

                if (entry.ElementNames.Length == 0) return;

                // One more hop so the section scroll (which measures) has settled before we look
                // the element up and pulse it. Normal, never Loaded - Loaded gets starved here.
                mw.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    var target = ResolveFirst(mw, entry.ElementNames);
                    if (target == null)
                    {
                        // A moved/renamed control is a soft failure: the user is already on the
                        // right page. Logged so a stale index row is findable, not silent.
                        App.Logger?.Debug("Palette entry {Id}: no element matched [{Names}]",
                                          entry.Id, string.Join(", ", entry.ElementNames));
                        return;
                    }
                    try { target.BringIntoView(); } catch { }
                    Pulse(target);
                }));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Palette navigation failed for {Id}", entry.Id);
            }
        }

        private static FrameworkElement? ResolveFirst(DependencyObject root, string[] names)
        {
            foreach (var name in names)
            {
                var found = FindElementByName(root, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Name lookup that crosses namescopes. <c>FindName</c> alone only sees the namescope of
        /// the element it is called on, and every tab UserControl (and every section control
        /// inside AppSettingsTabView) owns its own - so the walk falls back to comparing
        /// <c>Name</c> child by child. Same shape as TutorialOverlay's resolver, which is why the
        /// palette can point at exactly the elements tutorial steps already spotlight.
        /// </summary>
        private static FrameworkElement? FindElementByName(DependencyObject? parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name)) return null;

            if (parent is FrameworkElement fe)
            {
                if (fe.Name == name) return fe;
                if (fe.FindName(name) is FrameworkElement viaScope) return viaScope;
            }

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement element && element.Name == name) return element;
                var result = FindElementByName(child, name);
                if (result != null) return result;
            }
            return null;
        }

        /// <summary>
        /// A 2s accent glow around the control the user searched for, then gone.
        ///
        /// <para>Self-removing by construction: the previous effect is stashed and restored, and a
        /// second pulse restores the first target before claiming a new one, so the app can never
        /// accumulate glows. Under reduced motion the glow is static instead of animated and still
        /// expires on its timer - a highlight that fades is motion, a highlight that simply exists
        /// for two seconds is not, and the point (find the control) survives either way.</para>
        /// </summary>
        private static void Pulse(FrameworkElement target)
        {
            try
            {
                ClearPulse();

                _pulseTarget = target;
                _pulsePrevEffect = target.Effect;

                var glow = new DropShadowEffect
                {
                    Color = Color.FromRgb(0xFF, 0x69, 0xB4),
                    BlurRadius = 22,
                    ShadowDepth = 0,
                    Opacity = 0.0,
                };
                target.Effect = glow;

                if (MotionFx.AllowTransitions)
                {
                    var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(PulseMs) };
                    anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromPercent(0.10)));
                    anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.45, KeyTime.FromPercent(0.45)));
                    anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromPercent(0.70)));
                    anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.00, KeyTime.FromPercent(1.0)));
                    // BeginAnimation on the effect itself, never Storyboard.SetTargetName - target
                    // names do not resolve across the tab UserControls' namescopes (MotionFx docs).
                    glow.BeginAnimation(DropShadowEffect.OpacityProperty, anim);
                }
                else
                {
                    glow.Opacity = 0.9;
                }

                _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PulseMs + 60) };
                _pulseTimer.Tick += (_, _) => ClearPulse();
                _pulseTimer.Start();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Palette pulse failed: {E}", ex.Message);
                ClearPulse();
            }
        }

        private static void ClearPulse()
        {
            try
            {
                _pulseTimer?.Stop();
                _pulseTimer = null;

                if (_pulseTarget != null)
                {
                    if (_pulseTarget.Effect is DropShadowEffect dse)
                        dse.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                    _pulseTarget.Effect = _pulsePrevEffect;
                }
            }
            catch { }
            finally
            {
                _pulseTarget = null;
                _pulsePrevEffect = null;
            }
        }

        private void Item_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item)
            {
                ListResults.SelectedItem = item.DataContext;
                ActivateSelected();
                e.Handled = true;
            }
        }

        // =====================================================================================
        //  row view-model
        // =====================================================================================

        /// <summary>
        /// One rendered row. Strings are snapshotted here at build time (which is every keystroke),
        /// not bound to the entry, so the ItemTemplate never re-enters the localization manager
        /// during layout.
        ///
        /// <para><b>Public on purpose.</b> WPF data binding reflects with public-only binding flags:
        /// an internal row type binds to nothing and fails silently (empty rows, no exception).</para>
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
            public Visibility ContextVisibility =>
                string.IsNullOrEmpty(Context) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
